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
        public bool AllowSuppressed, VarySweep, Accent;
    }
    private struct Slot { public VisualEffect Effect; public float Until; public int Priority, StartFrame; public bool PendingPlay; public float BaseHitSize; public BloodHitProfile Profile; }
    private readonly Pending[] queue = new Pending[QueueCapacity];
    private readonly Slot[] slots = new Slot[Capacity];
    private readonly int[] recentTargets = new int[Capacity], recentVariations = new int[Capacity];
    private int recentCursor;
    private int tuningRevision = -1;
    public int LastSweepVariation { get; private set; } = -1;
    public int SweepVariationPlayedCount { get; private set; }
    private BloodHitCatalog catalog;
    private BloodGroundDecalService groundDecals;
    private BloodEffectsPackPool packPool;
    private int legacyActiveCount;
    private int retiredPackPlayedCount;
    private uint retiredPackPlayedVariants;
    private static BloodEffectStyle currentStyle;
    private static bool uniformRed;
    private readonly System.Collections.Generic.Dictionary<BloodHitProfile, BloodHitProfile> redProfiles = new System.Collections.Generic.Dictionary<BloodHitProfile, BloodHitProfile>();
    private readonly System.Collections.Generic.HashSet<BloodHitProfile> redProfileInstances = new System.Collections.Generic.HashSet<BloodHitProfile>();
    public static BloodEffectStyle CurrentStyle => currentStyle;
    public static bool PackEnabled => currentStyle == BloodEffectStyle.EffectsPack;
    public static bool UsePackVignette => currentStyle != BloodEffectStyle.Legacy;
    public static bool UniformRed => uniformRed;
    public static void SetUniformRed(bool enabled)
    {
        if (uniformRed == enabled) return;
        instance?.Clear();
        if (instance != null && instance.groundDecals != null) instance.groundDecals.ClearForComparison();
        uniformRed = enabled;
    }
    public static BloodHitProfile ResolveColorProfile(BloodHitProfile source)
    {
        if (!uniformRed || source == null || instance == null) return source;
        if (instance.redProfileInstances.Contains(source)) return source;
        if (!instance.redProfiles.TryGetValue(source, out var profile) || profile == null)
        {
            profile = Instantiate(source);
            profile.name = source.name + " Red Comparison";
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.mainColor = new Color(.50f, .137f, .153f);
            profile.secondaryColor = new Color(.22f, .059f, .071f);
            profile.specularColor = new Color(.69f, .39f, .39f);
            instance.redProfiles[source] = profile;
            instance.redProfileInstances.Add(profile);
        }
        return profile;
    }
    public int PackPlayedCount => retiredPackPlayedCount + (packPool != null ? packPool.PlayedCount : 0);
    public int LastPackVariation => packPool != null ? packPool.LastVariant : -1;
    public uint PackPlayedVariants => retiredPackPlayedVariants | (packPool != null ? packPool.PlayedVariants : 0);
    public static bool SetPackEnabled(bool enabled) => SetStyle(enabled ? BloodEffectStyle.EffectsPack : BloodEffectStyle.Legacy);
    public static bool SetStyle(BloodEffectStyle style)
    {
        if ((int)style < 0 || (int)style > 2) return false;
        if (instance == null) Bootstrap();
        if (instance == null) return false;
        if (currentStyle == style && (style == BloodEffectStyle.Legacy ? instance.slots[0].Effect != null : instance.packPool != null && instance.packPool.Ready)) return true;
        BloodEffectsPackPool preparedPool = null;
        if (style != BloodEffectStyle.Legacy)
        {
            var data = Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.ResourceFor(style));
            preparedPool = new BloodEffectsPackPool(instance.transform, data);
            if (!preparedPool.Ready) { preparedPool.Dispose(); return false; }
        }
        instance.Clear();
        if (instance.groundDecals != null) instance.groundDecals.ClearForPackChange();
        instance.DisposePackPool();
        if (style == BloodEffectStyle.Legacy) instance.CreateLegacyPool();
        else { instance.DisposeLegacyPool(); instance.packPool = preparedPool; }
        currentStyle = style;
        instance.tuningRevision = -1;
        return true;
    }
    private void CreateLegacyPool()
    {
        if (slots[0].Effect != null) return;
        for (int i = 0; i < Capacity; i++)
        {
            var child = new GameObject("Blood " + i);
            child.SetActive(false);
            child.transform.SetParent(transform, false);
            var vfx = child.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = catalog.ResolvePooledGraph(i);
            vfx.initialEventName = "BloodIdle";
            slots[i].Effect = vfx;
        }
    }
    private void DisposeLegacyPool()
    {
        for (int i = 0; i < Capacity; i++)
        {
            if (slots[i].Effect != null)
            {
                slots[i].Effect.gameObject.SetActive(false);
                Destroy(slots[i].Effect.gameObject);
            }
            slots[i] = default;
        }
        legacyActiveCount = 0;
    }
    private void DisposePackPool()
    {
        if (packPool == null) return;
        retiredPackPlayedCount += packPool.PlayedCount;
        retiredPackPlayedVariants |= packPool.PlayedVariants;
        packPool.Dispose();
        packPool = null;
    }
    private int queued;
    public int PlayedCount { get; private set; }
    public int DroppedCount { get; private set; }
    public int ActiveCount => legacyActiveCount + (packPool != null ? packPool.ActiveCount : 0);
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
    private static void ResetStatics() { instance = null; currentStyle = BloodEffectStyle.Legacy; uniformRed = false; }

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
        root.AddComponent<LowHealthBloodTrailService>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.BloodSpray)) return;
#endif
        SetUniformRed(OverburstGameSettings.BloodUniformRed);
        if (!SetStyle(OverburstGameSettings.BloodStyle))
        {
            SetStyle(BloodEffectStyle.Legacy);
            OverburstGameSettings.BloodStyle = BloodEffectStyle.Legacy;
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
            Accent = hit.IsCritical || hit.IsStrong || hit.IsLethal, VarySweep = hit.ImpactShape == CombatImpactShape.Sweep
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
        int targetId = 0, bool allowSuppressed = false, bool critical = false, bool strong = false, bool lethal = false)
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
            Priority = Mathf.Clamp(priority, 0, 3), Target = targetId, AllowSuppressed = allowSuppressed, Accent = critical || strong || lethal
        });
        return true;
    }

    public static bool RequestBleed(CombatHealth health, BloodHitProfile profile, Vector3 feet, Vector3 travel)
    {
        if (profile == null || profile.suppressBlood || instance == null || instance.groundDecals == null) return false;
        Vector3 spray = feet + Vector3.up * .6f;
        Vector3 groundOrigin = feet + Vector3.up * .5f;
        if (health != null && health.TryGetComponent(out CombatTarget target)
            && target.TryGetComponent(out CombatTargetVfxPlacement placement) && placement.HasBodyContacts)
            groundOrigin = spray = placement.BodyContactCenter;
        if (IsOffscreen(groundOrigin)) return false;
        profile = ResolveColorProfile(profile);
        // Small falling drops have no attack priority; they cannot evict a combat splash.
        if (currentStyle != BloodEffectStyle.Legacy && instance.packPool != null)
            instance.packPool.Play(profile, spray, Vector3.down, CombatImpactShape.Downward,
                .12f, 0, CosmeticSeed(0, Time.frameCount, 0, health.GetInstanceID()), health.GetInstanceID(), true);
        instance.groundDecals.Request(profile, groundOrigin, travel, CombatImpactShape.Thrust, .38f, 0, false, .08f, true);
        return true;
    }

    public static void RequestTargetHit(CombatHealth health, Vector3 point, Vector3 direction,
        CombatImpactShape shape, float size, int priority)
    {
        if (health == null || !health.TryGetComponent<BloodHitTarget>(out var target)) return;
        if (health.TryGetComponent(out CombatTarget combatTarget)
            && combatTarget.TryGetComponent(out CombatTargetVfxPlacement placement) && placement.HasBodyContacts)
            point = CombatTargetVfxPlacement.ResolveContact(combatTarget, point, direction, out _);
        RequestAt(target.Profile, point, direction, shape, size, priority,
            ResolveWeightScale(health), health.GetInstanceID(), lethal: health.IsDead);
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
            health.CurrentHp <= 0f ? 2 : 1, 1f, health.GetInstanceID(), critical: info.isCritical, strong: strong, lethal: health.CurrentHp <= 0f)) return;
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
        RequestAt(profile, center, forward, CombatImpactShape.Downward, size, 3, weight, id, lethal: true);
        if (!full) return;
        float spread = Random.Range(40f, 65f);
        RequestAt(profile, center, Quaternion.AngleAxis(spread, Vector3.up) * forward,
            CombatImpactShape.Sweep, size * .85f, 3, weight, id);
        RequestAt(profile, center, Quaternion.AngleAxis(-spread - Random.Range(0f, 30f), Vector3.up) * forward,
            CombatImpactShape.Sweep, size * .85f, 3, weight, id);
        Vector3 risingCenter = center + Vector3.up * .15f;
        if (health.TryGetComponent(out CombatTarget combatTarget)
            && combatTarget.TryGetComponent(out CombatTargetVfxPlacement placement) && placement.HasBodyContacts)
            risingCenter = center;
        RequestAt(profile, risingCenter, (forward + Vector3.up * .8f).normalized,
            CombatImpactShape.Thrust, size * .8f, 3, weight, id);
        if (instance.groundDecals == null || IsOffscreen(center)) return;
        // Flung drops land later the farther they fly.
        for (int i = 0; i < 5; i++)
        {
            Vector3 fling = Quaternion.AngleAxis(Random.Range(-110f, 110f), Vector3.up) * forward;
            float distance = radius + Random.Range(.35f, 1.9f);
            instance.groundDecals.Request(profile, center + fling * distance, fling, CombatImpactShape.Downward,
                Random.Range(.8f, 1.3f) * (currentStyle == BloodEffectStyle.Volumetric ? Mathf.Lerp(1f, weight, .5f) : weight), 1, false, .14f + distance * .12f);
        }
    }

    private static Vector3 ResolveBodyCenter(CombatHealth health, out float radius)
    {
        var target = health.GetComponent<CombatTarget>();
        if (target == null) target = health.GetComponentInParent<CombatTarget>();
        if (target != null)
        {
            return CombatTargetVfxPlacement.ResolveBodyCenter(target, out radius);
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
        if (tuningRevision != BloodComparisonTuning.Revision)
        {
            tuningRevision = BloodComparisonTuning.Revision;
            if (currentStyle != BloodEffectStyle.Legacy) packPool?.RefreshTuning();
            else for (int i = 0; i < Capacity; i++) if (slots[i].Until > 0f) ApplyTuning(i);
        }
        if (currentStyle != BloodEffectStyle.Legacy) packPool?.Tick(Time.time);
        else for (int i = 0; i < Capacity; i++)
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
        request.Profile = ResolveColorProfile(request.Profile);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.BloodSpray))
        {
            float diagnosticSize = request.Size * request.WeightScale;
            if (request.WeightScale > 1.2f) diagnosticSize = Mathf.Max(diagnosticSize, 1.25f);
            else if (request.WeightScale > 1f) diagnosticSize = Mathf.Max(diagnosticSize, .8f);
            diagnosticSize = Mathf.Clamp(diagnosticSize, .55f, 1.95f);
            if (currentStyle == BloodEffectStyle.Volumetric) diagnosticSize = Mathf.Lerp(Mathf.Clamp(request.Size, .55f, 1.95f), diagnosticSize, .5f);
            if (groundDecals)
                groundDecals.Request(request.Profile, request.Position, request.Direction,
                    request.Shape, diagnosticSize, request.Priority, request.AllowSuppressed);
            return true;
        }
#endif
        if (currentStyle != BloodEffectStyle.Legacy)
        {
            float size = Mathf.Clamp(request.Size * request.WeightScale, .55f, 1.95f);
            if (request.WeightScale > 1.2f) size = Mathf.Max(size, 1.25f);
            else if (request.WeightScale > 1f) size = Mathf.Max(size, .8f);
            if (currentStyle == BloodEffectStyle.Volumetric) size = Mathf.Lerp(Mathf.Clamp(request.Size, .55f, 1.95f), size, .5f);
            uint packSeed = CosmeticSeed(request.Source, request.Sequence, request.Phase, request.Target);
            if (!packPool.Play(request.Profile, request.Position, request.Direction, request.Shape, size,
                request.Priority, packSeed, request.Target, accented: request.Accent)) return false;
            PlayedCount++;
            PeakActiveCount = Mathf.Max(PeakActiveCount, ActiveCount);
            if (request.Shape == CombatImpactShape.Thrust) ThrustPlayedCount++;
            else if (request.Shape == CombatImpactShape.Downward) DownwardPlayedCount++;
            else SweepPlayedCount++;
            if (groundDecals != null) groundDecals.Request(request.Profile, request.Position, request.Direction,
                request.Shape, size, request.Priority, request.AllowSuppressed, .16f, false,
                    currentStyle == BloodEffectStyle.Volumetric ? packPool.LastGroundPrefab : null, packPool.LastBaseScale, packPool.LastRotation);
            return true;
        }
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
        slots[chosen].BaseHitSize = request.Profile.size * visualSize
            * (request.Priority >= 2 ? 1.2f : request.Priority == 1 ? 1.1f : 1f)
            * (variant != null ? variant.sizeMultiplier : 1f);
        slots[chosen].Profile = request.Profile; ApplyTuning(chosen);
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
        legacyActiveCount++;
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

    private void ApplyTuning(int index)
    {
        var slot = slots[index]; var profile = slot.Profile;
        slot.Effect.SetFloat(HitSize, slot.BaseHitSize * BloodComparisonTuning.Scale);
        slot.Effect.SetVector4(MainColor, BloodComparisonTuning.SprayColor(profile.mainColor));
        slot.Effect.SetVector4(SecondaryColor, BloodComparisonTuning.SprayColor(profile.secondaryColor));
        slot.Effect.SetVector4(SpecularColor, BloodComparisonTuning.SprayColor(profile.specularColor));
    }
    private void Release(int index)
    {
        var vfx = slots[index].Effect;
        vfx.Stop();
        vfx.Reinit();
        vfx.gameObject.SetActive(false);
        slots[index].Until = 0;
        slots[index].PendingPlay = false; slots[index].Profile = null;
        legacyActiveCount--;
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
    private void OnDestroy()
    {
        DisposePackPool();
        foreach (var profile in redProfiles.Values) if (profile != null) Destroy(profile);
        redProfiles.Clear();
        redProfileInstances.Clear();
        if (instance == this) instance = null;
    }
    private void SceneChanged(Scene previous, Scene current) => Clear();
    private void SceneUnloaded(Scene scene) => Clear();
    private void Clear()
    {
        packPool?.Clear();
        System.Array.Clear(queue, 0, queue.Length);
        queued = 0;
        System.Array.Clear(recentTargets, 0, Capacity);
        recentCursor = 0;
        LastSweepVariation = -1;
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Until > 0 && slots[i].Effect != null) Release(i);
        legacyActiveCount = 0;
    }
}
