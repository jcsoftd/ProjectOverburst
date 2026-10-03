using System.Collections.Generic;
using UnityEngine;

// KillerDoll 전진 공격 클립은 이동량이 포즈에 구워져 있고(Bake Into Pose XZ) 적 Animator는 루트 모션을 끈다.
// 그래서 몸만 앞으로 나가고 콜라이더·피해 판정 원점은 제자리에 남았다가 클립 끝에서 몸이 되돌아왔다.
// 지정 클립이 재생되는 동안 골반의 수평 이동을 모델에서 빼서 몸을 액터 위에 고정하고,
// 같은 양만큼 액터를 EnemyMovement 공격 이동으로 옮긴다. 벽·군집·플레이어에 막히면 몸도 같이 멈춘다.
[DefaultExecutionOrder(810)] // EnemyVisualRootGuard(800)가 모델 루트를 되돌린 뒤 최종 포즈를 읽는다.
public sealed class EnemyAttackRootMotion : MonoBehaviour
{
    private const float MaxStepPerFixedUpdate = .3f;
    private const float ReleaseSeconds = .12f;

    [Tooltip("이동이 포즈에 구워진 공격 클립. 이 클립이 재생되는 동안만 몸과 판정을 같이 옮긴다.")]
    [SerializeField] private AnimationClip[] travelClips = new AnimationClip[0];
    [Tooltip("몸을 되돌리는 오프셋을 넣을 트랜스폼. Animator의 부모(Authored model scale)")]
    [SerializeField] private Transform offsetRoot;
    [SerializeField] private Animator animator;
    [SerializeField] private EnemyMovement movement;

    private readonly HashSet<AnimationClip> travelSet = new HashSet<AnimationClip>();
    private readonly List<AnimatorClipInfo> clipBuffer = new List<AnimatorClipInfo>(4);
    private Transform pelvis;
    private EnemyMeleeAttackController melee;
    private Vector3 restLocalPosition;
    private bool restCaptured;
    private bool engaged;
    private Vector3 basePose;   // 공격 시작 시 골반 수평 위치(액터 기준)
    private Vector3 lastTravel; // 직전 프레임까지 반영한 포즈 이동(액터 기준)
    private Vector3 counter;    // 모델에서 뺀 이동(액터 기준)
    private Vector3 owed;       // 아직 액터에 적용하지 않은 이동(월드)

    public bool IsEngaged => engaged;
    public Vector3 CounterOffset => counter;
    public float AppliedTravel { get; private set; } // 이번 공격에서 액터에 요청한 수평 이동 합(m)
    public int TravelClipCount => travelClips != null ? travelClips.Length : 0;

    public void Configure(Transform modelOffsetRoot, Animator modelAnimator, EnemyMovement actorMovement, AnimationClip[] clips)
    {
        offsetRoot = modelOffsetRoot; animator = modelAnimator; movement = actorMovement;
        travelClips = clips ?? new AnimationClip[0];
        restCaptured = false; pelvis = null; RebuildSet();
    }

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (movement == null) movement = GetComponent<EnemyMovement>();
        if (offsetRoot == null && animator != null) offsetRoot = animator.transform.parent;
        melee = GetComponent<EnemyMeleeAttackController>();
        RebuildSet();
    }

    private void OnDisable() => ResetState();

    private void RebuildSet()
    {
        travelSet.Clear();
        if (travelClips == null) return;
        foreach (var clip in travelClips) if (clip != null) travelSet.Add(clip);
    }

    private bool Resolve()
    {
        if (animator == null || offsetRoot == null || movement == null || travelSet.Count == 0) return false;
        if (!restCaptured) { restLocalPosition = offsetRoot.localPosition; restCaptured = true; }
        if (pelvis == null && animator.isHuman) pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);
        return pelvis != null && animator.isActiveAndEnabled;
    }

    private bool HasTravelClip(bool next, out float weight)
    {
        weight = 0f;
        if (next) animator.GetNextAnimatorClipInfo(0, clipBuffer); else animator.GetCurrentAnimatorClipInfo(0, clipBuffer);
        for (int i = 0; i < clipBuffer.Count; i++)
            if (clipBuffer[i].clip != null && travelSet.Contains(clipBuffer[i].clip)) weight += clipBuffer[i].weight;
        return weight > .001f;
    }

    private void LateUpdate()
    {
        if (IsV3WeakExecution())
        {
            if (engaged || owed != Vector3.zero || counter != Vector3.zero) ResetState(true);
            return;
        }
        // Animator가 잠시 꺼져도 포즈는 멈춘 자리에 남으므로 오프셋을 유지한다. 초기화는 비활성화(풀 반납) 때만 한다.
        if (!Resolve()) return;

        bool inTransition = animator.IsInTransition(0);
        bool current = HasTravelClip(false, out float currentWeight);
        bool next = inTransition && HasTravelClip(true, out _);
        bool engagedNow = current || next;
        // 전환 중에는 포즈가 섞이며 이동량이 되돌아가므로 액터는 옮기지 않고 몸만 고정한다.
        bool drive = current && !inTransition && currentWeight > .5f;

        Vector3 pose = PoseLocal();
        if (engagedNow && !engaged)
        {
            engaged = true; basePose = pose; lastTravel = Vector3.zero; owed = Vector3.zero; AppliedTravel = 0f;
        }

        float dt = Mathf.Max(Time.deltaTime, 0f);
        if (engaged)
        {
            Vector3 travel = pose - basePose;
            if (drive)
            {
                Vector3 step = transform.TransformVector(travel - lastTravel); step.y = 0f;
                owed += step;
            }
            lastTravel = travel;
            counter = -travel;
            if (!engagedNow) { engaged = false; owed = Vector3.zero; }
        }
        else if (counter != Vector3.zero)
        {
            // 공격이 끝난 뒤 남은 작은 오차만 짧게 풀어 준다.
            counter = Vector3.MoveTowards(counter, Vector3.zero, Mathf.Max(counter.magnitude, .05f) * dt / ReleaseSeconds);
        }
        ApplyCounter();
    }

    private void FixedUpdate()
    {
        if (IsV3WeakExecution()) return;
        if (owed.sqrMagnitude < .000001f || movement == null) return;
        Vector3 step = Vector3.ClampMagnitude(owed, MaxStepPerFixedUpdate);
        if (movement.RequestAttackDisplacement(step)) { owed -= step; AppliedTravel += step.magnitude; }
        else owed = Vector3.zero; // 경직·넉백·잠금 해제 중에는 이동을 버린다. 몸은 계속 액터 위에 고정된다.
    }

    // 골반의 액터 기준 수평 위치. 지난 프레임에 넣은 되돌림 오프셋은 뺀다.
    private Vector3 PoseLocal()
    {
        Vector3 local = transform.InverseTransformPoint(pelvis.position) - CounterActorLocal();
        local.y = 0f;
        return local;
    }

    private Vector3 CounterActorLocal()
    {
        Transform parent = offsetRoot.parent;
        Vector3 offsetLocal = offsetRoot.localPosition - restLocalPosition;
        Vector3 world = parent != null ? parent.TransformVector(offsetLocal) : offsetLocal;
        return transform.InverseTransformVector(world);
    }

    private void ApplyCounter()
    {
        Transform parent = offsetRoot.parent;
        Vector3 world = transform.TransformVector(counter);
        offsetRoot.localPosition = restLocalPosition + (parent != null ? parent.InverseTransformVector(world) : world);
    }

    private bool IsV3WeakExecution()
    {
        if (melee == null) melee = GetComponent<EnemyMeleeAttackController>();
        return melee != null && melee.ActiveWeakExecution != null;
    }

    private void ResetState(bool preserveHeight = false)
    {
        engaged = false; owed = Vector3.zero; lastTravel = Vector3.zero; counter = Vector3.zero;
        if (offsetRoot != null && restCaptured)
        {
            Vector3 rest = restLocalPosition;
            if (preserveHeight) rest.y = offsetRoot.localPosition.y;
            offsetRoot.localPosition = rest;
        }
    }
}
