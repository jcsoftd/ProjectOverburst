using UnityEngine;

[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyActor))]
public sealed class EnemyEliteFootstepEmitter : MonoBehaviour
{
    private static readonly RaycastHit[] GroundHits = new RaycastHit[32];
    [SerializeField] private EnemyActor actor;
    [SerializeField] private EnemyFootfallProfile profile;
    [SerializeField, Min(0f)] private float minimumMoveSpeed = 0.3f;

    private Vector3 previousPosition;
    private float previousNormalizedTime;
    private uint observedLeaseVersion;
    private bool hasTracking;

    public EnemyFootfallProfile Profile => profile;
    public int ContactCount { get; private set; }

    public void Configure(EnemyActor owner, EnemyFootfallProfile footfallProfile)
    {
        actor = owner;
        profile = footfallProfile;
        ResetTracking();
    }

    private void Awake()
    {
        if (actor == null) actor = GetComponent<EnemyActor>();
    }

    private void OnEnable() => ResetTracking();
    private void OnDisable() => ResetTracking();

    private void LateUpdate()
    {
        if (actor == null || profile == null || !profile.IsValid || !actor.IsLeased
            || actor.Definition == null || actor.Definition.EnemyId != profile.EnemyId
            || actor.Health == null || actor.Health.IsDead || actor.Animator == null
            || actor.Movement == null || actor.Movement.IsStatusMovementLocked)
        {
            ResetTracking();
            return;
        }

        if (observedLeaseVersion != actor.LeaseVersion)
        {
            observedLeaseVersion = actor.LeaseVersion;
            ResetTracking();
        }

        Vector3 position = transform.position;
        Animator animator = actor.Animator;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        EnemyLocomotionMode mode = actor.Movement.LocomotionMode;
        bool locomoting = !animator.IsInTransition(0) && state.IsName("Locomotion")
            && (mode == EnemyLocomotionMode.Walk || mode == EnemyLocomotionMode.Run)
            && actor.Movement.HasDestination && !actor.Movement.IsActionLocked;
        float normalizedTime = state.normalizedTime;
        if (!locomoting || normalizedTime < 0f)
        {
            previousPosition = position;
            hasTracking = false;
            return;
        }

        if (!hasTracking || normalizedTime < previousNormalizedTime)
        {
            previousPosition = position;
            previousNormalizedTime = normalizedTime;
            hasTracking = true;
            return;
        }

        Vector3 delta = position - previousPosition;
        previousPosition = position;
        float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
        float speed = horizontalDistance / Mathf.Max(Time.deltaTime, 0.0001f);
        if (speed < minimumMoveSpeed || horizontalDistance > 0.75f || Mathf.Abs(delta.y) > 0.15f)
        {
            previousNormalizedTime = normalizedTime;
            return;
        }

        bool running = mode == EnemyLocomotionMode.Run;
        int crossedIndex = -1;
        for (int i = 0; i < profile.GetContactCount(running); i++)
        {
            float phase = profile.GetContact(running, i).Phase;
            if (Mathf.FloorToInt(normalizedTime - phase) > Mathf.FloorToInt(previousNormalizedTime - phase))
            {
                crossedIndex = i;
                break;
            }
        }
        previousNormalizedTime = normalizedTime;
        if (crossedIndex >= 0) EmitContact(position, delta, running, crossedIndex);
    }

    private void EmitContact(Vector3 position, Vector3 travel, bool running, int contactIndex)
    {
        QuarterViewCamera camera = QuarterViewCamera.ActiveInstance;
        if (camera == null || camera.CurrentTarget == null) return;
        Vector3 difference = camera.CurrentTarget.position - position;
        difference.y = 0f;
        float distance = difference.magnitude;
        Vector3 foot = transform.TransformPoint(profile.GetContact(running, contactIndex).LocalPosition);
        EnemyGroundStepTier tier = profile.GroundStepTier;
        bool audible = tier == EnemyGroundStepTier.Elite
            && distance < EnemyGroundStepTuning.AudibleDistance(tier);
        bool dustEligible = EnemyFootDustVfx.IsEligible(foot, profile.VisualWeight, distance);
        if (!audible && !dustEligible) return;
        int mask = LayerMask.GetMask("Default", "Environment", "Ground");
        int count = Physics.RaycastNonAlloc(foot + Vector3.up * 0.55f, Vector3.down,
            GroundHits, 1.6f, mask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        RaycastHit chosen = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = GroundHits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<EnemyActor>() != null
                || hit.collider.GetComponentInParent<EnemyController>() != null
                || hit.distance >= nearest) continue;
            nearest = hit.distance;
            chosen = hit;
        }
        if (float.IsPositiveInfinity(nearest) || Mathf.Abs(chosen.point.y - foot.y) > 0.45f)
        {
            EnemyFootDustVfx.RecordGroundMiss();
            return;
        }

        Vector3 point = chosen.point;
        ContactCount++;
        travel.y = 0f;
        if (dustEligible)
            EnemyFootDustVfx.TryEmit(point, chosen.normal, travel,
                profile.VisualWeight, distance, chosen.collider);
        if (!audible) return;
        EnemyEliteFootstepFeel.Play(point, distance, tier);
        float amplitude = EnemyGroundStepTuning.CameraAmplitude(tier, distance);
        if (amplitude > 0f) camera.QueueGroundStep(amplitude, .14f);
    }

    private void ResetTracking()
    {
        hasTracking = false;
        previousPosition = transform.position;
        previousNormalizedTime = 0f;
    }
}
