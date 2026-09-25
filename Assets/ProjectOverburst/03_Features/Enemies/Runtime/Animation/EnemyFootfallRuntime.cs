using System.Collections.Generic;
using UnityEngine;

/// <summary>Samples authored contact phases for active pooled enemies; visual output is shared.</summary>
[DefaultExecutionOrder(550)]
public sealed class EnemyFootfallRuntime : MonoBehaviour
{
    private sealed class Tracker
    {
        public EnemyActor actor;
        public EnemyFootfallProfile profile;
        public EnemyHitWeight weight;
        public uint leaseVersion;
        public Vector3 previousPosition;
        public float previousTime;
        public float lastEmissionTime;
        public float fallbackHalfWidth;
        public EnemyLocomotionMode previousMode;
        public bool tracking;
    }

    private sealed class LegacyTracker
    {
        public EnemyController controller;
        public EnemyMovement movement;
        public EnemyHitWeight weight;
        public Vector3 previousPosition;
        public float accumulatedDistance;
        public float lastEmissionTime;
        public float halfWidth;
        public bool rightFoot;
    }

    private static readonly RaycastHit[] GroundHits = new RaycastHit[32];
    private static EnemyFootfallRuntime instance;
    private readonly List<Tracker> trackers = new List<Tracker>(64);
    private readonly List<LegacyTracker> legacyTrackers = new List<LegacyTracker>(16);
    private readonly Dictionary<string, EnemyFootfallProfile> profiles =
        new Dictionary<string, EnemyFootfallProfile>(32);
    private readonly int[] nearbyTierCounts = new int[3];

    public int ActiveTrackers => trackers.Count;
    public int ActiveLegacyTrackers => legacyTrackers.Count;
    public int ContactCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Prewarm()
    {
        if (instance != null) return;
        instance = FindFirstObjectByType<EnemyFootfallRuntime>(FindObjectsInactive.Include);
        if (instance != null) return;
        var root = new GameObject("EnemyFootfallRuntime", typeof(EnemyFootfallRuntime));
        DontDestroyOnLoad(root);
    }

    public static void Register(EnemyActor actor)
    {
        if (!Application.isPlaying || actor == null
            || actor.GetComponent<EnemyEliteFootstepEmitter>() != null) return;
        if (instance == null) Prewarm();
        instance.RegisterInternal(actor);
    }

    public static void Unregister(EnemyActor actor)
    {
        if (instance == null || actor == null) return;
        for (int i = instance.trackers.Count - 1; i >= 0; i--)
        {
            if (instance.trackers[i].actor == actor)
                instance.trackers.RemoveAt(i);
        }
    }

    public static void RegisterLegacy(EnemyController controller)
    {
        if (!Application.isPlaying || controller == null
            || controller.GetComponent<EnemyActor>() != null) return;
        if (instance == null) Prewarm();
        for (int i = 0; i < instance.legacyTrackers.Count; i++)
            if (instance.legacyTrackers[i].controller == controller) return;
        EnemyMovement movement = controller.GetComponent<EnemyMovement>();
        if (movement == null) return;
        EnemyMovementProfile profile = movement != null ? movement.Profile : null;
        EnemyHitWeightProfile hit = profile != null ? profile.HitWeightProfile : null;
        CapsuleCollider capsule = controller.GetComponentInChildren<CapsuleCollider>(true);
        instance.legacyTrackers.Add(new LegacyTracker
        {
            controller = controller,
            movement = movement,
            weight = hit != null ? hit.Weight : EnemyHitWeight.Standard,
            previousPosition = controller.transform.position,
            lastEmissionTime = -100f,
            halfWidth = capsule != null
                ? Mathf.Clamp(capsule.bounds.extents.x * .45f, .12f, .6f) : .2f
        });
    }

    public static void UnregisterLegacy(EnemyController controller)
    {
        if (instance == null || controller == null) return;
        for (int i = instance.legacyTrackers.Count - 1; i >= 0; i--)
            if (instance.legacyTrackers[i].controller == controller)
                instance.legacyTrackers.RemoveAt(i);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        EnemyFootfallProfile[] found = Resources.LoadAll<EnemyFootfallProfile>("Enemies/Themes/Footfalls");
        for (int i = 0; i < found.Length; i++)
        {
            EnemyFootfallProfile profile = found[i];
            if (profile != null && profile.IsValid) profiles[profile.EnemyId] = profile;
        }
    }

    private void RegisterInternal(EnemyActor actor)
    {
        // An elite without authored ground contacts may be airborne (for example Reaper).
        // Never invent fallback feet for it.
        string id = actor.Definition != null ? actor.Definition.EnemyId : string.Empty;
        if (!profiles.ContainsKey(id) && actor.Definition != null
            && actor.Definition.Grade != null
            && actor.Definition.Grade.GradeType == EnemyGradeType.Elite) return;
        for (int i = 0; i < trackers.Count; i++)
        {
            if (trackers[i].actor == actor)
            {
                Configure(trackers[i], actor);
                return;
            }
        }
        var tracker = new Tracker { actor = actor };
        Configure(tracker, actor);
        trackers.Add(tracker);
    }

    private void Configure(Tracker tracker, EnemyActor actor)
    {
        string id = actor.Definition != null ? actor.Definition.EnemyId : string.Empty;
        profiles.TryGetValue(id, out tracker.profile);
        EnemyMovementProfile movement = actor.Movement != null ? actor.Movement.Profile : null;
        EnemyHitWeightProfile hit = movement != null ? movement.HitWeightProfile : null;
        tracker.weight = tracker.profile != null ? tracker.profile.VisualWeight
            : hit != null ? hit.Weight : EnemyHitWeight.Standard;
        tracker.leaseVersion = actor.LeaseVersion;
        tracker.previousPosition = actor.transform.position;
        tracker.tracking = false;
        tracker.lastEmissionTime = -100f;
        CapsuleCollider capsule = actor.CollisionRoot != null
            ? actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>(true) : null;
        tracker.fallbackHalfWidth = capsule != null
            ? Mathf.Clamp(capsule.bounds.extents.x * .45f, .12f, .6f) : .2f;
    }

    private void LateUpdate()
    {
        QuarterViewCamera camera = QuarterViewCamera.ActiveInstance;
        Transform target = camera != null ? camera.CurrentTarget : null;
        if (target == null) return;
        System.Array.Clear(nearbyTierCounts, 0, nearbyTierCounts.Length);
        for (int i = 0; i < trackers.Count; i++)
        {
            Tracker tracker = trackers[i];
            if (tracker.actor == null || !tracker.actor.isActiveAndEnabled
                || !tracker.actor.IsLeased || tracker.actor.Movement == null
                || !tracker.actor.Movement.HasDestination) continue;
            EnemyLocomotionMode mode = tracker.actor.Movement.LocomotionMode;
            if (mode != EnemyLocomotionMode.Walk && mode != EnemyLocomotionMode.Run)
                continue;
            if (IsNearby(tracker.actor.transform.position, target.position, tracker.weight))
                nearbyTierCounts[Mathf.Clamp((int)tracker.weight, 0, 2)]++;
        }
        for (int i = 0; i < legacyTrackers.Count; i++)
        {
            LegacyTracker tracker = legacyTrackers[i];
            if (tracker.controller == null || !tracker.controller.isActiveAndEnabled
                || tracker.movement == null || !tracker.movement.HasDestination) continue;
            if (IsNearby(tracker.controller.transform.position, target.position, tracker.weight))
                nearbyTierCounts[Mathf.Clamp((int)tracker.weight, 0, 2)]++;
        }
        for (int i = trackers.Count - 1; i >= 0; i--)
        {
            Tracker tracker = trackers[i];
            EnemyActor actor = tracker.actor;
            if (actor == null || !actor.isActiveAndEnabled || !actor.IsLeased)
            {
                trackers.RemoveAt(i);
                continue;
            }
            Tick(tracker, target);
        }
        for (int i = legacyTrackers.Count - 1; i >= 0; i--)
        {
            LegacyTracker legacy = legacyTrackers[i];
            if (legacy.controller == null || !legacy.controller.isActiveAndEnabled)
            {
                legacyTrackers.RemoveAt(i);
                continue;
            }
            TickLegacy(legacy, target);
        }
    }

    private void Tick(Tracker tracker, Transform target)
    {
        EnemyActor actor = tracker.actor;
        if (actor.LeaseVersion != tracker.leaseVersion) Configure(tracker, actor);
        Vector3 position = actor.transform.position;
        Vector3 toPlayer = target.position - position;
        toPlayer.y = 0f;
        float visibleDistance = EnemyFootDustVfx.MaxVisibleDistance(tracker.weight);
        if (toPlayer.sqrMagnitude > visibleDistance * visibleDistance
            || actor.Health == null || actor.Health.IsDead
            || actor.Animator == null || actor.Movement == null
            || actor.Movement.IsStatusMovementLocked)
        {
            tracker.tracking = false;
            tracker.previousPosition = position;
            return;
        }

        Animator animator = actor.Animator;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        EnemyLocomotionMode mode = actor.Movement.LocomotionMode;
        bool running = mode == EnemyLocomotionMode.Run;
        bool locomoting = !animator.IsInTransition(0) && state.IsName("Locomotion")
            && (mode == EnemyLocomotionMode.Walk || running)
            && actor.Movement.HasDestination && !actor.Movement.IsActionLocked;
        float time = state.normalizedTime;
        if (!locomoting || time < 0f || mode != tracker.previousMode)
        {
            tracker.tracking = false;
            tracker.previousPosition = position;
            tracker.previousMode = mode;
            return;
        }
        if (!tracker.tracking || time < tracker.previousTime || time - tracker.previousTime > .5f)
        {
            tracker.tracking = true;
            tracker.previousPosition = position;
            tracker.previousTime = time;
            return;
        }

        Vector3 delta = position - tracker.previousPosition;
        tracker.previousPosition = position;
        float horizontal = new Vector2(delta.x, delta.z).magnitude;
        if (horizontal / Mathf.Max(Time.deltaTime, .0001f) < .3f
            || horizontal > .75f || Mathf.Abs(delta.y) > .15f)
        {
            tracker.previousTime = time;
            return;
        }

        int count = tracker.profile != null ? tracker.profile.GetContactCount(running) : 2;
        for (int i = 0; i < count; i++)
        {
            EnemyFootfallContact contact = tracker.profile != null
                ? tracker.profile.GetContact(running, i)
                : new EnemyFootfallContact(i * .5f,
                    new Vector3((i == 0 ? -1f : 1f) * tracker.fallbackHalfWidth, 0f, .15f));
            float phase = contact.Phase;
            if (Mathf.FloorToInt(time - phase) <= Mathf.FloorToInt(tracker.previousTime - phase))
                continue;
            int tier = Mathf.Clamp((int)tracker.weight, 0, 2);
            float interval = EnemyFootDustVfx.RecommendedContactInterval(
                tracker.weight, nearbyTierCounts[tier]);
            if (Time.unscaledTime - tracker.lastEmissionTime < interval) break;
            tracker.lastEmissionTime = Time.unscaledTime;
            TryContact(tracker, contact.LocalPosition, delta, toPlayer.magnitude);
            break;
        }
        tracker.previousTime = time;
    }

    private void TryContact(Tracker tracker, Vector3 localFoot, Vector3 travel, float distance)
    {
        EnemyActor actor = tracker.actor;
        Vector3 foot = actor.transform.TransformPoint(localFoot);
        bool dustEligible = EnemyFootDustVfx.IsEligible(foot, tracker.weight, distance);
        bool audible = tracker.profile != null
            && tracker.profile.GroundStepTier == EnemyGroundStepTier.Medium
            && distance < EnemyGroundStepTuning.AudibleDistance(EnemyGroundStepTier.Medium);
        if (!dustEligible && !audible) return;
        if (!TryResolveGround(actor.transform, foot, out RaycastHit chosen))
        {
            EnemyFootDustVfx.RecordGroundMiss();
            return;
        }
        ContactCount++;
        travel.y = 0f;
        if (dustEligible)
            EnemyFootDustVfx.TryEmit(chosen.point, chosen.normal, travel,
                tracker.weight, distance, chosen.collider);
        if (!audible) return;
        EnemyEliteFootstepFeel.Play(chosen.point, distance, EnemyGroundStepTier.Medium);
        float amplitude = EnemyGroundStepTuning.CameraAmplitude(EnemyGroundStepTier.Medium, distance);
        if (amplitude > 0f) QuarterViewCamera.ActiveInstance?.QueueGroundStep(amplitude, .1f);
    }

    private void TickLegacy(LegacyTracker tracker, Transform target)
    {
        EnemyController controller = tracker.controller;
        Vector3 position = controller.transform.position;
        Vector3 delta = position - tracker.previousPosition;
        tracker.previousPosition = position;
        Vector3 toPlayer = target.position - position;
        toPlayer.y = 0f;
        float visibleDistance = EnemyFootDustVfx.MaxVisibleDistance(tracker.weight);
        if (controller.IsDead || toPlayer.sqrMagnitude > visibleDistance * visibleDistance
            || (tracker.movement != null && (tracker.movement.IsStatusMovementLocked
                || tracker.movement.IsActionLocked || !tracker.movement.HasDestination)))
        {
            tracker.accumulatedDistance = 0f;
            return;
        }
        delta.y = 0f;
        float traveled = delta.magnitude;
        if (traveled / Mathf.Max(Time.deltaTime, .0001f) < .3f
            || traveled > .75f)
        {
            if (traveled > .75f) tracker.accumulatedDistance = 0f;
            return;
        }
        tracker.accumulatedDistance += traveled;
        float stride = tracker.weight == EnemyHitWeight.Light ? .65f
            : tracker.weight == EnemyHitWeight.Heavy ? 1.2f : .9f;
        if (tracker.accumulatedDistance < stride) return;
        tracker.accumulatedDistance -= stride;
        tracker.rightFoot = !tracker.rightFoot;
        int tier = Mathf.Clamp((int)tracker.weight, 0, 2);
        float interval = EnemyFootDustVfx.RecommendedContactInterval(
            tracker.weight, nearbyTierCounts[tier]);
        if (Time.unscaledTime - tracker.lastEmissionTime < interval) return;
        tracker.lastEmissionTime = Time.unscaledTime;
        Vector3 local = new Vector3((tracker.rightFoot ? 1f : -1f) * tracker.halfWidth, 0f, .15f);
        Vector3 foot = controller.transform.TransformPoint(local);
        if (!EnemyFootDustVfx.IsEligible(foot, tracker.weight, toPlayer.magnitude)) return;
        if (!TryResolveGround(controller.transform, foot, out RaycastHit chosen))
        {
            EnemyFootDustVfx.RecordGroundMiss();
            return;
        }
        ContactCount++;
        EnemyFootDustVfx.TryEmit(chosen.point, chosen.normal, delta,
            tracker.weight, toPlayer.magnitude, chosen.collider);
    }

    private static bool TryResolveGround(Transform owner, Vector3 foot, out RaycastHit chosen)
    {
        int mask = LayerMask.GetMask("Default", "Environment", "Ground");
        int hitCount = Physics.RaycastNonAlloc(foot + Vector3.up * .55f,
            Vector3.down, GroundHits, 1.6f, mask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        chosen = default;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = GroundHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(owner)
                || hit.collider.GetComponentInParent<EnemyActor>() != null
                || hit.collider.GetComponentInParent<EnemyController>() != null
                || hit.distance >= nearest) continue;
            nearest = hit.distance;
            chosen = hit;
        }
        return !float.IsPositiveInfinity(nearest) && Mathf.Abs(chosen.point.y - foot.y) <= .45f;
    }

    private static bool IsNearby(Vector3 actor, Vector3 target, EnemyHitWeight weight)
    {
        float x = actor.x - target.x;
        float z = actor.z - target.z;
        float distance = EnemyFootDustVfx.MaxVisibleDistance(weight);
        return x * x + z * z <= distance * distance;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
