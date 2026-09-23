using System;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
[RequireComponent(typeof(EnemyMotor))]
public sealed class EnemyMovementReaction : MonoBehaviour // 피격 경직과 제어형 넉백 전담
{
    private const float EliteMinimumKnockbackReductionPercent = 15f;
    private const float PartyCollisionClearance = 0.03f;
    private const int PartyCollisionQueryCapacity = 16;
    private const string PartyPhysicalLayerName = "Player";

    [SerializeField] private CombatHealth health; // 사망 확인
    [SerializeField] private EnemyMotor motor; // 넉백 위치 적용
    [SerializeField] private EnemyMovement movement; // 개별 넉백 감소율
    [SerializeField] private EnemyRank rank; // 엘리트 보정
    [SerializeField] private CombatTarget combatTarget; // 물리 부피
    [SerializeField, Min(0f)] private float knockbackDistancePerStrength = 0.1f; // 넉백 수치당 거리
    [SerializeField, Min(0f)] private float knockbackTravelDuration = 0.12f; // 실제 이동 시간
    [SerializeField] private Transform visualReactionRoot; // 모델 부모만 들고 충돌체/공격점은 지면 유지
    private Vector3 visualBasePosition;
    private bool visualBaseCaptured;
    private float liftStartedAt, liftHeight, liftDuration, nextWeightedReaction;
    public EnemyHitWeightProfile HitWeightProfile => movement != null && movement.Profile != null ? movement.Profile.HitWeightProfile : null;
    public float VisualLift { get; private set; }
    public Transform VisualReactionRoot => visualReactionRoot;
    public bool CanApplyWeightedHit => HitWeightProfile != null && !IsDead() && Time.time >= nextWeightedReaction;
    public Vector3 VisualRestPosition => visualBaseCaptured ? visualBasePosition
        : visualReactionRoot != null ? visualReactionRoot.localPosition : Vector3.zero;

    public void ConfigureVisualReactionRoot(Transform root)
    {
        ResetVisualLift(); visualReactionRoot=root; visualBaseCaptured=false;
    }

    public bool TryApplyWeightedHit(DamageInfo info)
    {
        var profile=HitWeightProfile;
        if(!CanApplyWeightedHit || info.isDamageOverTime || !info.triggersOnHitEffects) return false;
        nextWeightedReaction=Time.time+profile.ReactionCooldown;
        Vector3 direction=info.direction;
        if(direction.sqrMagnitude<.0001f && info.source!=null) direction=transform.position-info.source.transform.position;
        ApplyKnockback(direction,info.knockback);
        float stagger=info.hitReaction.overridesTargetDefaults
            ? (info.knockback>0f?info.hitReaction.knockbackReactionDuration:info.hitReaction.hitStunDuration)
            : profile.StaggerDuration;
        if(IsKnockbackActive) ExtendKnockbackReaction(stagger);
        else ApplyHitStun(stagger);
        if(visualReactionRoot!=null && profile.VisualLiftHeight>0f)
        {
            if(!visualBaseCaptured){visualBasePosition=visualReactionRoot.localPosition;visualBaseCaptured=true;}
            liftStartedAt=Time.time;liftHeight=profile.VisualLiftHeight;liftDuration=profile.VisualLiftDuration;
        }
        return true;
    }

    private void LateUpdate()
    {
        if(liftHeight<=0f || visualReactionRoot==null) return;
        if(IsDead()){ResetVisualLift();return;}
        float t=Mathf.Clamp01((Time.time-liftStartedAt)/liftDuration);
        // Smooth takeoff and landing; no accumulated lift on repeated hits.
        float bell=Mathf.Sin(Mathf.PI*t);VisualLift=liftHeight*bell*bell;
        visualReactionRoot.localPosition=visualBasePosition+Vector3.up*VisualLift;
        if(t>=1f) ResetVisualLift();
    }

    private void ResetVisualLift()
    {
        if(visualReactionRoot!=null && visualBaseCaptured) visualReactionRoot.localPosition=visualBasePosition;
        liftHeight=0f;VisualLift=0f;
    }

    private void OnDisable() { ResetReaction(); }

    private float knockbackEndTime; // 넉백 반응 종료
    private float hitStunEndTime; // 제자리 경직 종료
    private Vector3 knockbackStartPosition; // 넉백 시작 위치
    private Vector3 knockbackTargetPosition; // 넉백 목표 위치
    private float knockbackTravelStartTime; // 이동 시작 시각
    private float knockbackTravelEndTime; // 이동 종료 시각
    private int partyCollisionLayerMask;
    private readonly RaycastHit[] partyCollisionHits = new RaycastHit[PartyCollisionQueryCapacity];
    private readonly Collider[] partyCollisionOverlaps = new Collider[PartyCollisionQueryCapacity];

    public bool IsKnockbackActive { get { return Time.time < knockbackEndTime; } }
    public bool IsHitStunActive { get { return Time.time < hitStunEndTime; } }
    public bool IsStunned { get { return IsKnockbackActive || IsHitStunActive; } }
    public float KnockbackReductionPercent { get { return ResolveKnockbackReductionPercent(); } }

    public event Action ReactionStarted;

    private void Awake()
    {
        ResolveReferences();
        int partyLayer = LayerMask.NameToLayer(PartyPhysicalLayerName);
        partyCollisionLayerMask = partyLayer >= 0 ? 1 << partyLayer : 0;
        if (partyCollisionLayerMask == 0)
            Debug.LogError("[EnemyMovementReaction] Player physical layer is missing.", this);
    }

    public void ApplyHitStun(float duration)
    {
        if (IsDead() || IsKnockbackActive)
            return;

        float resolvedDuration = Mathf.Max(0f, duration);
        if (resolvedDuration <= 0f)
            return;

        hitStunEndTime = Mathf.Max(hitStunEndTime, Time.time + resolvedDuration);
        motor?.HoldPosition();
        ReactionStarted?.Invoke();
    }

    public void ApplyKnockback(Vector3 direction, float strength)
    {
        if (IsDead())
            return;

        direction.y = 0f;
        float resolvedStrength = Mathf.Max(0f, strength);
        if (direction.sqrMagnitude <= 0.0001f || resolvedStrength <= 0f)
            return;

        ResolveReferences();
        motor?.Stop();

        Vector3 startPosition = transform.position;
        float distance = ResolveKnockbackDistance(resolvedStrength);
        float travelDuration = Mathf.Max(Time.fixedDeltaTime, HitWeightProfile != null ? HitWeightProfile.TravelDuration : knockbackTravelDuration);
        knockbackStartPosition = startPosition;
        knockbackTargetPosition = startPosition + direction.normalized * distance;
        knockbackTravelStartTime = Time.time;
        knockbackTravelEndTime = Time.time + travelDuration;
        knockbackEndTime = Mathf.Max(knockbackEndTime, knockbackTravelEndTime);
        hitStunEndTime = 0f;
        ReactionStarted?.Invoke();
    }

    public void ExtendKnockbackReaction(float duration)
    {
        if (IsDead())
            return;

        float resolvedDuration = Mathf.Max(0f, duration);
        if (resolvedDuration <= 0f)
            return;

        knockbackEndTime = Mathf.Max(knockbackEndTime, Time.time + resolvedDuration);
        hitStunEndTime = 0f;
    }

    public float ResolveKnockbackDistance(float strength)
    {
        if(HitWeightProfile!=null) return HitWeightProfile.ResolveDistance(strength);
        float baseDistance = Mathf.Max(0f, strength) * Mathf.Max(0f, knockbackDistancePerStrength);
        return baseDistance * (1f - KnockbackReductionPercent * 0.01f);
    }

    public void TickFixed()
    {
        if (!IsKnockbackActive)
            return;

        if (Time.time >= knockbackTravelEndTime)
        {
            motor?.HoldPosition(); // 넉백 이동 후 남은 경직 중 군집 밀림 차단
            return;
        }

        motor?.Stop();
        float duration = Mathf.Max(Time.fixedDeltaTime, knockbackTravelEndTime - knockbackTravelStartTime);
        float normalizedTime = Mathf.Clamp01((Time.time - knockbackTravelStartTime) / duration);
        float punchProgress = 1f - Mathf.Pow(1f - normalizedTime, 3f);
        Vector3 candidate = Vector3.LerpUnclamped(knockbackStartPosition, knockbackTargetPosition, punchProgress);
        Vector3 resolvedPosition = candidate;
        if (movement != null
            && !movement.TryResolveCrowdPosition(candidate, false, out resolvedPosition))
        {
            knockbackTargetPosition = resolvedPosition;
            knockbackTravelEndTime = Time.time; // 군집에 막히면 남은 이동만 중단
        }

        if (!TryResolvePartyClearance(resolvedPosition, out Vector3 partySafePosition))
        {
            resolvedPosition = partySafePosition;
            knockbackTargetPosition = partySafePosition;
            knockbackTravelEndTime = Time.time; // 파티 캡슐에 막히면 이동만 중단
        }

        motor?.MoveToPosition(resolvedPosition);
    }

    public void ResetReaction()
    {
        ResetVisualLift(); nextWeightedReaction=0f;
        knockbackEndTime = 0f;
        hitStunEndTime = 0f;
        knockbackTravelEndTime = 0f;
        motor?.HoldPosition();
    }

    public void ResolveReferences()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (motor == null)
            motor = GetComponent<EnemyMotor>();
        if (movement == null)
            movement = GetComponent<EnemyMovement>();
        if (rank == null)
            rank = GetComponent<EnemyRank>();
        if (combatTarget == null)
            combatTarget = GetComponent<CombatTarget>();
    }

    private bool TryResolvePartyClearance(Vector3 candidate, out Vector3 resolvedPosition)
    {
        resolvedPosition = candidate;
        Vector3 currentPosition = transform.position;
        Vector3 displacement = candidate - currentPosition;
        displacement.y = 0f;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f || combatTarget == null)
            return true;
        if (partyCollisionLayerMask == 0)
        {
            resolvedPosition = currentPosition;
            return false;
        }

        CombatTargetVolume volume = combatTarget.CurrentVolume;
        float bodyRadius = Mathf.Max(0.01f, volume.Radius);
        float radius = bodyRadius + PartyCollisionClearance;
        float halfSegment = Mathf.Max(0f, volume.HalfHeight - bodyRadius);
        Vector3 top = volume.Center + Vector3.up * halfSegment;
        Vector3 bottom = volume.Center - Vector3.up * halfSegment;
        Vector3 direction = displacement / distance;

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            top,
            bottom,
            radius,
            partyCollisionOverlaps,
            partyCollisionLayerMask,
            QueryTriggerInteraction.Ignore);
        if (overlapCount >= partyCollisionOverlaps.Length)
        {
            resolvedPosition = currentPosition;
            return false; // 파티 조회 포화 시 관통보다 정지 우선
        }

        for (int i = 0; i < overlapCount; i++)
        {
            Collider overlap = partyCollisionOverlaps[i];
            if (IsPartyPhysicalBlocker(overlap)
                && IsMovingTowardCollider(direction, overlap, volume.Center))
            {
                resolvedPosition = currentPosition;
                return false;
            }
        }

        int hitCount = Physics.CapsuleCastNonAlloc(
            top,
            bottom,
            radius,
            direction,
            partyCollisionHits,
            distance,
            partyCollisionLayerMask,
            QueryTriggerInteraction.Ignore);
        if (hitCount >= partyCollisionHits.Length)
        {
            resolvedPosition = currentPosition;
            return false; // 가장 가까운 파티원 누락 가능성 차단
        }

        float allowedDistance = distance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = partyCollisionHits[i];
            if (!IsPartyPhysicalBlocker(hit.collider))
                continue;

            if (hit.distance <= 0.001f
                && !IsMovingTowardCollider(direction, hit.collider, volume.Center))
            {
                continue;
            }

            allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(0f, hit.distance));
        }

        if (allowedDistance >= distance - 0.0001f)
            return true;

        Vector3 planarPosition = currentPosition + direction * allowedDistance;
        resolvedPosition = new Vector3(planarPosition.x, candidate.y, planarPosition.z);
        return false;
    }

    private bool IsPartyPhysicalBlocker(Collider candidate)
    {
        if (candidate == null
            || !candidate.enabled
            || candidate.isTrigger
            || candidate.transform == transform
            || candidate.transform.IsChildOf(transform))
        {
            return false;
        }

        CombatTarget candidateTarget = CombatTarget.Resolve(candidate);
        return candidateTarget != null
            && candidateTarget != combatTarget
            && candidateTarget.Team == CombatTeam.PlayerParty;
    }

    private static bool IsMovingTowardCollider(
        Vector3 direction,
        Collider candidate,
        Vector3 sourceCenter)
    {
        Vector3 toCandidate = candidate.bounds.center - sourceCenter;
        toCandidate.y = 0f;
        if (toCandidate.sqrMagnitude <= 0.0001f)
            return true;

        return Vector3.Dot(direction, toCandidate.normalized) > 0.001f;
    }

    private float ResolveKnockbackReductionPercent()
    {
        float reduction = movement != null && movement.Profile != null
            ? movement.Profile.KnockbackReductionPercent
            : 0f;
        if (rank != null && rank.Rank == EnemyRankType.Elite)
            reduction = Mathf.Max(reduction, EliteMinimumKnockbackReductionPercent);
        return Mathf.Clamp(reduction, 0f, 100f);
    }

    private bool IsDead()
    {
        return health != null && health.IsDead;
    }
}
