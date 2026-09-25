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
    }
    private struct Slot { public VisualEffect Effect; public float Until; public int Priority, StartFrame; public bool PendingPlay; }
    private readonly Pending[] queue = new Pending[QueueCapacity];
    private readonly Slot[] slots = new Slot[Capacity];
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
        if (instance != null) return;
        var data = Resources.Load<BloodHitCatalog>(BloodHitCatalog.ResourcePath);
        if (data == null || data.slash == null || data.stab == null || data.burst == null) return;
        var root = new GameObject(nameof(BloodHitVfxService));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<BloodHitVfxService>();
        instance.catalog = data;
        instance.groundDecals = root.AddComponent<BloodGroundDecalService>();
        instance.groundDecals.Configure(data);
        for (int i = 0; i < Capacity; i++)
        {
            var child = new GameObject("Blood " + i);
            child.SetActive(false);
            child.transform.SetParent(root.transform, false);
            var vfx = child.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = data.Resolve((CombatImpactShape)(i % 3));
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
        var camera = Camera.main;
        if (camera != null)
        {
            Vector3 viewport = camera.WorldToViewportPoint(point);
            if (viewport.z <= 0 || viewport.x < -.1f || viewport.x > 1.1f
                || viewport.y < -.1f || viewport.y > 1.1f)
            {
                instance.OffscreenCount++;
                return;
            }
        }
        var request = new Pending
        {
            Profile = target.Profile, Position = point, Direction = hit.ImpactDirection,
            Shape = hit.ImpactShape, Size = Mathf.Clamp(size, .55f, 1.5f),
            WeightScale = ResolveWeightScale(hit.Target),
            Priority = (hit.IsLethal ? 2 : 0) + (hit.IsCritical ? 1 : 0),
            Source = hit.Source != null ? hit.Source.GetInstanceID() : 0,
            Sequence = hit.AttackSequenceId, Phase = hit.PhaseIndex, Target = hit.Target.GetInstanceID()
        };
        instance.Enqueue(request);
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
        var graph = catalog.Resolve(request.Shape);
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
        if (request.Shape != CombatImpactShape.Downward) rotation *= Quaternion.Euler(0, 90, 0);
        vfx.transform.SetPositionAndRotation(request.Position, rotation);
        vfx.transform.localScale = Vector3.one;
        float visualSize = request.Size * request.WeightScale;
        if (request.WeightScale > 1.2f) visualSize = Mathf.Max(visualSize, 1.25f);
        else if (request.WeightScale > 1f) visualSize = Mathf.Max(visualSize, .8f);
        visualSize = Mathf.Clamp(visualSize, .55f, 1.95f);
        vfx.SetFloat(HitSize, request.Profile.size * visualSize
            * (request.Priority >= 2 ? 1.2f : request.Priority == 1 ? 1.1f : 1f));
        vfx.SetVector4(MainColor, request.Profile.mainColor.linear);
        vfx.SetVector4(SecondaryColor, request.Profile.secondaryColor.linear);
        vfx.SetVector4(SpecularColor, request.Profile.specularColor.linear);
        vfx.SetFloat(Specular, request.Profile.specular);
        vfx.SetInt(LoopCount, 1);
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
        if (groundDecals)
            groundDecals.Request(request.Profile, request.Position, request.Direction,
                request.Shape, visualSize, request.Priority);
        return true;
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
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Until > 0 && slots[i].Effect != null) Release(i);
        ActiveCount = 0;
    }
}
