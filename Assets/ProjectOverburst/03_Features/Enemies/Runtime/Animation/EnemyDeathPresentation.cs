using System.Collections.Generic;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEngine;

/// <summary>Feel owns visual displacement; EnemyController remains the only corpse lifetime owner.</summary>
[DefaultExecutionOrder(650)]
[DisallowMultipleComponent]
public sealed class EnemyDeathPresentation : MonoBehaviour
{
    [SerializeField] private CombatImpactSurface surface;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private MMF_Player feedback;
    [SerializeField, Range(0f, 1f)] private float landingNormalizedTime = .5f;
    [SerializeField, Min(0f)] private float normalDisplacement = .2f;
    [SerializeField, Min(0f)] private float strongDisplacement = .36f;
    [SerializeField, Min(0f)] private float strongLift = .06f;
    [SerializeField, Min(.05f)] private float displacementDuration = .32f;
    private EnemyActor actor;
    private EnemyMovementReaction reaction;
    private Vector3 restPosition;
    private float carriedLift;
    private bool dying, landed;
    private MMF_Position positionFeedback;
    public CombatImpactSurface Surface => surface;
    public bool LandingPlayed => landed;
    public Vector3 VisualOffset => visualRoot != null ? visualRoot.localPosition - restPosition : Vector3.zero;

    public void Configure(CombatImpactSurface material, Transform root, MMF_Player player,
        float normal, float strong, float lift, float duration, float landing)
    {
        surface = material; visualRoot = root; feedback = player;
        normalDisplacement = normal; strongDisplacement = strong; strongLift = lift;
        displacementDuration = duration; landingNormalizedTime = landing;
    }

    private void Awake()
    {
        actor = GetComponent<EnemyActor>();
        reaction = GetComponent<EnemyMovementReaction>();
        if (visualRoot != null) restPosition = visualRoot.localPosition;
        if (feedback != null)
            foreach (var item in feedback.FeedbacksList)
                if (item is MMF_Position move) { positionFeedback = move; break; }
    }
    private void OnEnable()
    {
        ResetVisual();
        if (actor == null || actor.Health == null) return;
        actor.Health.OnDamaged += CaptureLastHit;
        actor.Health.OnDead += OnDead;
        actor.Health.OnReset += OnReset;
    }
    private void OnDisable()
    {
        if (actor != null && actor.Health != null)
        {
            actor.Health.OnDamaged -= CaptureLastHit;
            actor.Health.OnDead -= OnDead;
            actor.Health.OnReset -= OnReset;
        }
        ResetVisual();
    }
    private void OnReset(CombatHealth health) => ResetVisual();
    private void ResetVisual()
    {
        if (feedback != null) { feedback.StopFeedbacks(); feedback.ResetFeedbacks(); }
        if (visualRoot != null) visualRoot.localPosition = restPosition;
        dying = landed = false; carriedLift = 0;
    }
    private void CaptureLastHit(CombatHealth health, DamageInfo info)
    {
        // Capture before death listeners stop movement and release its visual lift.
        if (health.CurrentHp > 0 || dying || reaction == null) return;
        carriedLift = reaction.VisualLift;
        restPosition = reaction.VisualRestPosition;
    }
    private void OnDead(CombatHealth health, DamageInfo info)
    {
        if (dying || visualRoot == null || feedback == null || positionFeedback == null) return;
        dying = true;
        bool direct = info.triggersOnHitEffects && !info.isDamageOverTime;
        bool strong = direct && (info.isCritical || info.knockback >= 4f);
        Vector3 direction = info.direction;
        direction.y = 0;
        if (direction.sqrMagnitude < .001f && info.source != null)
            direction = Vector3.ProjectOnPlane(transform.position - info.source.transform.position, Vector3.up);
        Vector3 displacement = direct ? direction.normalized * (strong ? strongDisplacement : normalDisplacement) : Vector3.zero;
        displacement = ClampToGround(displacement);
        Vector3 local = visualRoot.parent.InverseTransformVector(displacement);
        float localLift = strong ? strongLift / Mathf.Max(.01f, visualRoot.parent.lossyScale.y) : 0;
        visualRoot.localPosition = restPosition;
        positionFeedback.InitialPosition = Vector3.zero; // RelativePosition captures the restored local base.
        positionFeedback.AnimatePositionDuration = displacementDuration;
        // Most of the displacement happens at contact; the last part settles
        // into the authored death clip instead of slowly starting to slide.
        positionFeedback.AnimatePositionTweenX = new MMTweenType(FastOut(local.x));
        positionFeedback.AnimatePositionTweenZ = new MMTweenType(FastOut(local.z));
        positionFeedback.AnimatePositionTweenY = new MMTweenType(new AnimationCurve(
            new Keyframe(0, carriedLift, 0, 0), new Keyframe(.35f, Mathf.Max(carriedLift, localLift), 0, 0), new Keyframe(1, 0, 0, 0)));
        feedback.Initialization(true);
        feedback.PlayFeedbacks(transform.position, 1f);
    }

    private static AnimationCurve FastOut(float distance) => new AnimationCurve(
        new Keyframe(0f, 0f, 2.8f * distance, 2.8f * distance),
        new Keyframe(1f, distance, 0f, 0f));

    private Vector3 ClampToGround(Vector3 displacement)
    {
        if (displacement.sqrMagnitude < .00001f) return Vector3.zero;
        int mask = LayerMask.GetMask("Default", "Environment", "Ground");
        if (Physics.SphereCast(transform.position + Vector3.up * .35f, .12f, displacement.normalized,
            out var obstacle, displacement.magnitude, mask, QueryTriggerInteraction.Ignore))
            displacement = displacement.normalized * Mathf.Max(0, obstacle.distance - .03f);
        Vector3 destination = transform.position + displacement;
        if (!Physics.Raycast(destination + Vector3.up * .3f, Vector3.down, out var ground, .6f, mask, QueryTriggerInteraction.Ignore)
            || Mathf.Abs(ground.point.y - transform.position.y) > .12f) return Vector3.zero;
        return displacement;
    }

    private void LateUpdate()
    {
        if (!dying || landed || actor == null || actor.Animator == null) return;
        var state = actor.Animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName("Death") || state.normalizedTime < landingNormalizedTime) return;
        landed = true;
        if (reaction == null || reaction.HitWeightProfile == null || reaction.HitWeightProfile.Weight != EnemyHitWeight.Heavy) return;
        Vector3 point = visualRoot.position;
        if (!Physics.Raycast(point + Vector3.up * .5f, Vector3.down, out var ground, 1.1f,
            LayerMask.GetMask("Default", "Environment", "Ground"), QueryTriggerInteraction.Ignore)) return;
        if (!CombatImpactFeel.Play(CombatImpactSurface.Ground, CombatImpactShape.Downward, ground.point + Vector3.up * .025f, Vector3.forward)) return;
        var camera = QuarterViewCamera.ActiveInstance;
        if (camera == null || camera.CurrentTarget == null) return;
        float distance = Vector3.Distance(camera.CurrentTarget.position, point);
        float amplitude = .025f * Mathf.Clamp01(1 - distance / 9f);
        if (amplitude <= .001f) return;
        camera.RequestCombatImpact(CombatCameraRequestKind.AttackHit, Vector3.forward, Vector3.zero, false,
            .12f, amplitude, 0, .8f, .06f, .05f, .5f, .03f, .1f);
    }
}
