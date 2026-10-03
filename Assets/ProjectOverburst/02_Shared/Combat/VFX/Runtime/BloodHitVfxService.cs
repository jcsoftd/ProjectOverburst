using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;

// Target-local feedback. Global hit stop/camera grouping stays in CombatHitFeedbackService.
[DefaultExecutionOrder(-800)]
public sealed class BloodHitVfxService : MonoBehaviour
{
    public const int Capacity = 96, PerFrameLimit = 16;
    private const int QueueCapacity = 64;
    private static BloodHitVfxService instance;
    private static readonly int MainColor = Shader.PropertyToID("BloodColorMain"),
        SecondaryColor = Shader.PropertyToID("BloodColorSecondary"),
        SpecularColor = Shader.PropertyToID("BloodSpecularColor"),
        Specular = Shader.PropertyToID("SpecularValue"),
        LoopCount = Shader.PropertyToID("LoopCount"), HitSize = Shader.PropertyToID("HitSize");
    private struct Pending
    {
        public BloodHitProfile Profile;
        public Vector3 Position, Direction;
        public CombatImpactShape Shape;
        public float Size, WeightScale;
        public int Priority, Source, Sequence, Phase, Target;
        public bool AllowSuppressed, VarySweep;
    }
    private struct Slot { public VisualEffect Effect; public float Until; public int Priority, StartFrame; public bool PendingPlay; }
    private readonly Pending[] queue = new Pending[QueueCapacity];
    private readonly Slot[] slots = new Slot[Capacity];
    private readonly int[] recentTargets = new int[Capacity], recentVariations = new int[Capacity];
    private int recentCursor;
    public int LastSweepVariation { get; private set; } = -1;
    public int SweepVariationPlayedCount { get; private set; }
    private BloodHitCatalog catalog;
    private BloodGroundDecalService groundDecals;
    private int queued;
    public int PlayedCount { get; private set; }
    public int DroppedCount { get; private set; }
    public int ActiveCount { get; private set; }
    public int PeakActiveCount { get; private set; }
    public int RequestedCount { get; private set; }
    public int OffscreenCount { get; private set; }
    public int DuplicateCount { get; private set; }
    public int DroppedQueueCount { get; private set; }
    public int DroppedFrameCount { get; private set; }
    public int DroppedPoolCount { get; private set; }
    public int PreemptedCount { get; private set; }
    public int SweepPlayedCount { get; private set; }
    public int ThrustPlayedCount { get; private set; }
    public int DownwardPlayedCount { get; private set; }
    public int PeakQueuedCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.BloodSpray) && !Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.GroundDecals)) return;
#endif
        if (instance != null) return;
        var data = Resources.Load<BloodHitCatalog>(BloodHitCatalog.ResourcePath);
        if (data == null || data.slash == null || data.stab == null || data.burst == null) return;
        var root = new GameObject(nameof(BloodHitVfxService));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<BloodHitVfxService>();
        instance.catalog = data;
        instance.groundDecals = root.AddComponent<BloodGroundDecalService>();
        instance.groundDecals.Configure(data);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.BloodSpray)) return;
#endif
        for (int i = 0; i < Capacity; i++)
        {
            var child = new GameObject("Blood " + i);
            child.SetActive(false);
            child.transform.SetParent(root.transform, false);
            var vfx = child.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = data.ResolvePooledGraph(i);
            vfx.initialEventName = "BloodIdle";
            instance.slots[i].Effect = vfx;
        }
    }

    public static void Request(in CombatHitFeedbackRequest hit, Vector3 point, float size)
    {
        if (hit.Target == null || !hit.Target.TryGetComponent<BloodHitTarget>(out var target)
            || target.Profile == null || target.Profile.suppressBlood) return;
        if (instance == null) Bootstrap();
        if (instance == null) return;
        instance.RequestedCount++;
        if (IsOffscreen(point))
        {
            instance.OffscreenCount++;
            return;
        }
        var request = new Pending
        {
            Profile = target.Profile, Position = point, Direction = hit.ImpactDirection,
            Shape = hit.ImpactShape, Size = Mathf.Clamp(size, .55f, 1.5f),
            WeightScale = ResolveWeightScale(hit.Target),
            Priority = (hit.IsLethal ? 2 : 0) + (hit.IsCritical ? 1 : 0),
            Source = hit.Source != null ? hit.Source.GetInstanceID() : 0,
            Sequence = hit.AttackSequenceId, Phase = hit.PhaseIndex, Target = hit.Target.GetInstanceID(),
            VarySweep = hit.ImpactShape == CombatImpactShape.Sweep
        };
        instance.Enqueue(request);
    }

    public const string PlayerProfilePath = "Combat/Blood/Player";
    public const int FullDeathBurstsPerFrame = 6;
    private static BloodHitProfile playerProfile;
    private static bool playerProfileLoaded;
    private static int deathFrame = -1, deathsThisFrame;

    // Blood that is not a player melee hit (projectile goo, player wounds, lightning hops, kills).
    // It shares the queue, pool, per-frame budget, priorities and ground marks with melee blood.
    public static bool RequestAt(BloodHitProfile profile, Vector3 point, Vector3 direction,
        CombatImpactShape shape, float size, int priority, float weightScale = 1f,
        int targetId = 0, bool allowSuppressed = false)
    {
        if (profile == null || (profile.suppressBlood && !allowSuppressed)) return false;
        if (instance == null) Bootstrap();
        if (instance == null) return false;
        instance.RequestedCount++;
        if (IsOffscreen(point))
        {
            instance.OffscreenCount++;
            return false;
        }
        instance.Enqueue(new Pending
        {
            Profile = profile, Position = point, Direction = direction, Shape = shape,
            Size = Mathf.Clamp(size, .55f, 1.5f), WeightScale = Mathf.Max(.5f, weightScale),
            Priority = Mathf.Clamp(priority, 0, 3), Target = targetId, AllowSuppressed = allowSuppressed
        });
        return true;
    }

    public static void RequestTargetHit(CombatHealth health, Vector3 point, Vector3 direction,
        CombatImpactShape shape, float size, int priority)
    {
        if (health == null || !health.TryGetComponent<BloodHitTarget>(out var target)) return;
        RequestAt(target.Profile, point, direction, shape, size, priority,
            ResolveWeightScale(health), health.GetInstanceID());
    }

    // Player wounds use one red profile. Like enemy blood, the spray starts on the side facing the
    // attacker and travels along the attack; strong attacks burst, projectiles jet.
    public static void RequestPlayerHit(CombatHealth health, in DamageInfo info)
    {
        if (health == null || info.isDamageOverTime || info.damage <= 0f) return;
        if (!playerProfileLoaded)
        {
            playerProfile = Resources.Load<BloodHitProfile>(PlayerProfilePath);
            playerProfileLoaded = true;
        }
        if (playerProfile == null) return;
        Vector3 direction = Vector3.ProjectOnPlane(info.direction, Vector3.up);
        if (direction.sqrMagnitude < .0001f && info.source != null)
            direction = Vector3.ProjectOnPlane(health.transform.position - info.source.transform.position, Vector3.up);
        if (direction.sqrMagnitude < .0001f) direction = -health.transform.forward;
        direction.Normalize();
        Vector3 center = ResolveBodyCenter(health, out float radius);
        EnemyAbilityDefinition ability = info.enemyAbility;
        bool strong = ability != null && ability.IsTelegraphedStrongAttack;
        CombatImpactShape shape = ability != null && ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile
            ? CombatImpactShape.Thrust : strong ? CombatImpactShape.Downward : CombatImpactShape.Sweep;
        Vector3 wound = center - direction * Mathf.Min(radius, .3f);
        if (!RequestAt(playerProfile, wound, direction, shape, strong ? 1.4f : 1.2f,
            health.CurrentHp <= 0f ? 2 : 1, 1f, health.GetInstanceID())) return;
        // A splatter lands behind the player on top of the spray's own mark.
        if (instance.groundDecals != null)
            instance.groundDecals.Request(playerProfile, center + direction * .55f, direction,
                CombatImpactShape.Downward, strong ? 1.35f : 1.1f, 1, false, .22f);
    }

    // A kill throws a gush: a burst out of the body, two fanned side jets, one rising jet and
    // splatters flung around the corpse. Past the per-frame budget a kill keeps only the burst.
    public static void RequestDeath(CombatHealth health, in DamageInfo info)
    {
        if (health == null || !health.TryGetComponent<BloodHitTarget>(out var target)
            || target.Profile == null || target.Profile.suppressBlood) return;
        if (instance == null) Bootstrap();
        if (instance == null) return;
        BloodHitProfile profile = target.Profile;
        Vector3 center = ResolveBodyCenter(health, out float radius);
        Vector3 forward = Vector3.ProjectOnPlane(info.direction, Vector3.up);
        if (forward.sqrMagnitude < .0001f && info.source != null)
            forward = Vector3.ProjectOnPlane(health.transform.position - info.source.transform.position, Vector3.up);
        if (forward.sqrMagnitude < .0001f) forward = health.transform.forward;
        forward.Normalize();
        float weight = ResolveWeightScale(health);
        float size = Mathf.Clamp(.85f + radius * .5f, .95f, 1.5f);
        int id = health.GetInstanceID();
        if (Time.frameCount != deathFrame)
        {
            deathFrame = Time.frameCount;
            deathsThisFrame = 0;
        }
        bool full = deathsThisFrame++ < FullDeathBurstsPerFrame;
        RequestAt(profile, center, forward, CombatImpactShape.Downward, size, 3, weight, id);
        if (!full) return;
        float spread = Random.Range(40f, 65f);
        RequestAt(profile, center, Quaternion.AngleAxis(spread, Vector3.up) * forward,
            CombatImpactShape.Sweep, size * .85f, 3, weight, id);
        RequestAt(profile, center, Quaternion.AngleAxis(-spread - Random.Range(0f, 30f), Vector3.up) * forward,
            CombatImpactShape.Sweep, size * .85f, 3, weight, id);
        RequestAt(profile, center + Vector3.up * .15f, (forward + Vector3.up * .8f).normalized,
            CombatImpactShape.Thrust, size * .8f, 3, weight, id);
        if (instance.groundDecals == null || IsOffscreen(center)) return;
        // Flung drops land later the farther they fly.
        for (int i = 0; i < 5; i++)
        {
            Vector3 fling = Quaternion.AngleAxis(Random.Range(-110f, 110f), Vector3.up) * forward;
            float distance = radius + Random.Range(.35f, 1.9f);
            instance.groundDecals.Request(profile, center + fling * distance, fling, CombatImpactShape.Downward,
                Random.Range(.8f, 1.3f) * weight, 1, false, .14f + distance * .12f);
        }
    }

    private static Vector3 ResolveBodyCenter(CombatHealth health, out float radius)
    {
        var target = health.GetComponent<CombatTarget>();
        if (target == null) target = health.GetComponentInParent<CombatTarget>();
        if (target != null)
        {
            CombatTargetVolume volume = target.CurrentHurtVolume;
            radius = volume.Radius;
            return volume.Center;
        }
        radius = .45f;
        return health.transform.position + Vector3.up;
    }

    private static bool IsOffscreen(Vector3 point)
    {
        var camera = Camera.main;
        if (camera == null) return false;
        Vector3 viewport = camera.WorldToViewportPoint(point);
        return viewport.z <= 0 || viewport.x < -.1f || viewport.x > 1.1f
            || viewport.y < -.1f || viewport.y > 1.1f;
    }

    private static float ResolveWeightScale(CombatHealth health)
    {
        var reaction = health.GetComponentInParent<EnemyMovementReaction>();
        if (reaction == null || reaction.HitWeightProfile == null) return 1f;
        return reaction.HitWeightProfile.Weight == EnemyHitWeight.Heavy ? 1.28f
            : reaction.HitWeightProfile.Weight == EnemyHitWeight.Standard ? 1.14f : 1f;
    }

    private void Enqueue(Pending value)
    {
        int weakest = 0;
        for (int i = 0; i < queued; i++)
        {
            if (value.Sequence > 0 && value.Source != 0 && queue[i].Source == value.Source
                && queue[i].Sequence == value.Sequence && queue[i].Phase == value.Phase
                && queue[i].Target == value.Target)
            {
                DuplicateCount++;
                return;
            }
            if (queue[i].Priority < queue[weakest].Priority) weakest = i;
        }
        if (queued < QueueCapacity)
        {
            queue[queued++] = value;
            PeakQueuedCount = Mathf.Max(PeakQueuedCount, queued);
        }
        else
        {
            DroppedCount++;
            DroppedQueueCount++;
            if (value.Priority > queue[weakest].Priority) queue[weakest] = value;
        }
    }

    private void LateUpdate()
    {
        for (int i = 0; i < Capacity; i++)
        {
            if (slots[i].Until > 0 && Time.time >= slots[i].Until) Release(i);
            else if (slots[i].PendingPlay && Time.frameCount > slots[i].StartFrame)
            {
                // Activation/Reinit has now initialized the native instance. Emit exactly once.
                slots[i].Effect.Play();
                slots[i].PendingPlay = false;
            }
        }
        int emitted = 0;
        for (int priority = 3; priority >= 0; priority--)
            for (int i = 0; i < queued; i++)
                if (queue[i].Priority == priority)
                {
                    if (emitted >= PerFrameLimit)
                    {
                        DroppedCount++;
                        DroppedFrameCount++;
                    }
                    else if (!Play(queue[i]))
                    {
                        DroppedCount++;
                        DroppedPoolCount++;
                    }
                    else emitted++;
                }
        System.Array.Clear(queue, 0, queued);
        queued = 0;
    }

    private bool Play(Pending request)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.BloodSpray))
        {
            float diagnosticSize = request.Size * request.WeightScale;
            if (request.WeightScale > 1.2f) diagnosticSize = Mathf.Max(diagnosticSize, 1.25f);
            else if (request.WeightScale > 1f) diagnosticSize = Mathf.Max(diagnosticSize, .8f);
            diagnosticSize = Mathf.Clamp(diagnosticSize, .55f, 1.95f);
            if (groundDecals)
                groundDecals.Request(request.Profile, request.Position, request.Direction,
                    request.Shape, diagnosticSize, request.Priority, request.AllowSuppressed);
            return true;
        }
#endif
        var graph = catalog.Resolve(request.Shape);
        uint seed = CosmeticSeed(request.Source, request.Sequence, request.Phase, request.Target);
        int variantIndex = -1;
        BloodHitCatalog.SweepVariation variant = null;
        if (request.VarySweep)
            catalog.TryResolveSweep(seed, PreviousVariation(request.Target), out variantIndex, out variant);
        if (variant != null) graph = variant.graph;
        int chosen = -1;
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Until == 0)
            {
                chosen = i;
                if (slots[i].Effect.visualEffectAsset == graph) break;
            }
        if (chosen < 0 && request.Priority > 0)
            for (int i = 0; i < Capacity; i++)
                if (slots[i].Priority < request.Priority
                    && (chosen < 0 || slots[i].Until < slots[chosen].Until)) chosen = i;
        if (chosen < 0) return false;
        if (slots[chosen].Until > 0)
        {
            PreemptedCount++;
            Release(chosen);
        }
        var vfx = slots[chosen].Effect;
        vfx.visualEffectAsset = graph;
        var direction = request.Direction;
        if (direction.sqrMagnitude < .0001f) direction = Vector3.forward;
        direction.Normalize();
        Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
        // Supplier slash/stab jets use local -X; map that axis onto the strike direction.
        var rotation = Quaternion.LookRotation(direction, up);
        if (variant != null) rotation *= Quaternion.Euler(variant.localEuler);
        else if (request.Shape != CombatImpactShape.Downward) rotation *= Quaternion.Euler(0, 90, 0);
        vfx.transform.SetPositionAndRotation(request.Position, rotation);
        vfx.transform.localScale = Vector3.one;
        float visualSize = request.Size * request.WeightScale;
        if (request.WeightScale > 1.2f) visualSize = Mathf.Max(visualSize, 1.25f);
        else if (request.WeightScale > 1f) visualSize = Mathf.Max(visualSize, .8f);
        visualSize = Mathf.Clamp(visualSize, .55f, 1.95f);
        vfx.SetFloat(HitSize, request.Profile.size * visualSize
            * (request.Priority >= 2 ? 1.2f : request.Priority == 1 ? 1.1f : 1f)
            * (variant != null ? variant.sizeMultiplier : 1f));
        vfx.SetVector4(MainColor, request.Profile.mainColor.linear);
        vfx.SetVector4(SecondaryColor, request.Profile.secondaryColor.linear);
        vfx.SetVector4(SpecularColor, request.Profile.specularColor.linear);
        vfx.SetFloat(Specular, request.Profile.specular);
        vfx.SetInt(LoopCount, 1);
        vfx.resetSeedOnPlay = variant == null;
        if (variant != null) vfx.startSeed = seed;
        vfx.gameObject.SetActive(true);
        vfx.Reinit();
        slots[chosen].Until = Time.time + catalog.lifetime;
        slots[chosen].Priority = request.Priority;
        slots[chosen].StartFrame = Time.frameCount;
        slots[chosen].PendingPlay = true;
        ActiveCount++;
        PeakActiveCount = Mathf.Max(PeakActiveCount, ActiveCount);
        PlayedCount++;
        if (request.Shape == CombatImpactShape.Thrust) ThrustPlayedCount++;
        else if (request.Shape == CombatImpactShape.Downward) DownwardPlayedCount++;
        else SweepPlayedCount++;
        if (variant != null)
        {
            RememberVariation(request.Target, variantIndex);
            LastSweepVariation = variantIndex;
            SweepVariationPlayedCount++;
        }
        if (groundDecals)
            groundDecals.Request(request.Profile, request.Position, request.Direction,
                request.Shape, visualSize, request.Priority, request.AllowSuppressed);
        return true;
    }

    public static uint CosmeticSeed(int source, int sequence, int phase, int target)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)source) * 16777619u;
            hash = (hash ^ (uint)sequence) * 16777619u;
            hash = (hash ^ (uint)phase) * 16777619u;
            hash = (hash ^ (uint)target) * 16777619u;
            hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
            return hash == 0 ? 1u : hash;
        }
    }
    private int PreviousVariation(int target)
    {
        if (target != 0)
            for (int i = 0; i < Capacity; i++) if (recentTargets[i] == target) return recentVariations[i];
        return -1;
    }
    private void RememberVariation(int target, int variation)
    {
        if (target == 0) return;
        int index = -1;
        for (int i = 0; i < Capacity; i++) if (recentTargets[i] == target) { index = i; break; }
        if (index < 0) { index = recentCursor; recentCursor = (recentCursor + 1) % Capacity; }
        recentTargets[index] = target; recentVariations[index] = variation;
    }

    private void Release(int index)
    {
        var vfx = slots[index].Effect;
        vfx.Stop();
        vfx.Reinit();
        vfx.gameObject.SetActive(false);
        slots[index].Until = 0;
        slots[index].PendingPlay = false;
        ActiveCount--;
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += SceneChanged;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= SceneChanged;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        Clear();
    }
    private void OnDestroy() { if (instance == this) instance = null; }
    private void SceneChanged(Scene previous, Scene current) => Clear();
    private void SceneUnloaded(Scene scene) => Clear();
    private void Clear()
    {
        System.Array.Clear(queue, 0, queue.Length);
        queued = 0;
        System.Array.Clear(recentTargets, 0, Capacity);
        recentCursor = 0;
        LastSweepVariation = -1;
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Until > 0 && slots[i].Effect != null) Release(i);
        ActiveCount = 0;
    }
}
