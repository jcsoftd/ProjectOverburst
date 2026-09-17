using UnityEngine;

public enum EnemyBehaviorTendency
{
    Assault,
    Disruptor,
    Defender
}

public enum EnemyRepositionStyle
{
    Backpedal,
    Dodge
}

[CreateAssetMenu(fileName = "EBP_Enemy", menuName = "OVERBURST/Enemies/Behavior Profile")]
public sealed class EnemyBehaviorProfile : ScriptableObject // 몬스터별 행동 성향 단일 원본
{
    public const float DefaultMemberEngageRange = 2.5f;
    public const float DefaultMemberEngageVerticalTolerance = 1.5f;

    [SerializeField] private string profileId = "Default";
    [SerializeField] private EnemyBehaviorTendency tendency = EnemyBehaviorTendency.Assault;
    [SerializeField] private EnemyRepositionStyle repositionStyle = EnemyRepositionStyle.Backpedal;

    [Header("Peace")]
    [SerializeField, Range(0f, 1f)] private float patrolChance = 0.5f;
    [SerializeField, Min(0.1f)] private float idleDurationMin = 1.5f;
    [SerializeField, Min(0.1f)] private float idleDurationMax = 4f;
    [SerializeField, Min(0.5f)] private float patrolRadius = 4f;
    [SerializeField, Range(0.25f, 1f)] private float patrolSpeedMultiplier = 0.65f;
    [SerializeField, Min(0.1f)] private float patrolStuckCheckDuration = 0.75f;
    [SerializeField, Min(0.01f)] private float patrolStuckMinDistance = 0.08f;

    [Header("Awareness")]
    [SerializeField, Min(0.5f)] private float noticeRange = 14f;
    [SerializeField, Min(0.5f)] private float investigateRange = 10f;
    [SerializeField, Min(0.5f)] private float discoverRange = 6f;
    [SerializeField, Range(1f, 360f)] private float discoverFieldOfView = 150f;
    [SerializeField, Min(0.1f)] private float noticeDuration = 0.7f;
    [SerializeField, Min(0.1f)] private float noticeCooldown = 3.5f;
    [SerializeField, Min(0.1f)] private float investigateMemoryDuration = 2.5f;
    [SerializeField, Range(0.25f, 1f)] private float investigateSpeedMultiplier = 0.55f;
    [SerializeField, Min(0f)] private float supportCallRange = 12f;
    [SerializeField, Min(0f)] private float alertDuration = 0.25f;
    [SerializeField] private bool playTauntOnAlert;
    [SerializeField, Min(0f)] private float idleBreakMinInterval = 5f;
    [SerializeField, Min(0f)] private float idleBreakMaxInterval = 9f;

    [Header("Combat Rhythm")]
    [SerializeField, Min(0f)] private float recoveryDuration = 0.35f;
    [SerializeField, Min(0f)] private float preferredMinDistance = 0.8f;
    [SerializeField, Min(0.1f)] private float repositionDistance = 1.25f;
    [SerializeField, Min(0.1f)] private float repositionDuration = 0.55f;
    [SerializeField, Range(0f, 1f)] private float lowHealthRepositionThreshold = 0.25f;
    [SerializeField, Min(0.5f)] private float lowHealthRepositionDistance = 1.5f;
    [SerializeField, Min(0.1f)] private float lowHealthRepositionDuration = 0.6f;

    [Header("Dodge Lunge")]
    [SerializeField, Min(0.5f)] private float dodgeLungeMinDistance = 2.3f;
    [SerializeField, Min(0.5f)] private float dodgeLungeMaxDistance = 5f;
    [SerializeField, Min(0.5f)] private float dodgeLungeDistance = 2.4f;
    [SerializeField, Min(0.1f)] private float dodgeLungeDuration = 0.65f;
    [SerializeField, Min(0f)] private float dodgeLungeCooldown = 3f;
    [SerializeField, Range(0f, 1f)] private float dodgeLungeChance = 0.35f;
    [SerializeField, Min(0f)] private float dodgeVisualHeight = 0.75f;

    [Header("Approach")]
    [SerializeField, Min(0f)] private float runApproachMinDistance = 6f;
    [SerializeField, Min(0.5f)] private float preferredApproachDistance = 1.35f;
    [SerializeField, Range(0f, 90f)] private float approachSideAngle = 15f;
    [SerializeField, Min(0.5f)] private float separationRadius = 1.5f;
    [SerializeField, Range(0f, 2f)] private float separationWeight = 1f;
    [SerializeField, Min(0.5f)] private float approachDirectionMinDuration = 2f;
    [SerializeField, Min(0.5f)] private float approachDirectionMaxDuration = 4f;

    [Header("Combat Coordination")]
    [SerializeField, Min(0f)] private float attackTurnCooldown = 1f;
    [SerializeField, Min(0.1f)] private float attackWaitDuration = 0.45f;
    [SerializeField, Min(0.1f)] private float memberEngageRange = DefaultMemberEngageRange;
    [SerializeField, Min(0f)] private float memberEngageVerticalTolerance = DefaultMemberEngageVerticalTolerance;

    [Header("Shield Defense")]
    [SerializeField] private bool hasShield;
    [SerializeField, Range(0f, 1f)] private float defendChance;
    [SerializeField, Min(0.1f)] private float defendDuration = 0.9f;
    [SerializeField, Range(0f, 1f)] private float blockedDamageMultiplier = 0.35f;
    [SerializeField, Range(0f, 180f)] private float blockAngle = 120f;

    public string ProfileId => profileId;
    public EnemyBehaviorTendency Tendency => tendency;
    public EnemyRepositionStyle RepositionStyle => repositionStyle;
    public float PatrolChance => Mathf.Clamp01(patrolChance);
    public float IdleDurationMin => Mathf.Max(0.1f, idleDurationMin);
    public float IdleDurationMax => Mathf.Max(IdleDurationMin, idleDurationMax);
    public float PatrolRadius => Mathf.Max(0.5f, patrolRadius);
    public float PatrolSpeedMultiplier => Mathf.Clamp(patrolSpeedMultiplier, 0.25f, 1f);
    public float PatrolStuckCheckDuration => Mathf.Max(0.1f, patrolStuckCheckDuration);
    public float PatrolStuckMinDistance => Mathf.Max(0.01f, patrolStuckMinDistance);
    public float DiscoverRange => Mathf.Max(0.5f, discoverRange);
    public float InvestigateRange => Mathf.Max(DiscoverRange, investigateRange);
    public float NoticeRange => Mathf.Max(InvestigateRange, noticeRange);
    public float DiscoverFieldOfView => Mathf.Clamp(discoverFieldOfView, 1f, 360f);
    public float NoticeDuration => Mathf.Max(0.1f, noticeDuration);
    public float NoticeCooldown => Mathf.Max(0.1f, noticeCooldown);
    public float InvestigateMemoryDuration => Mathf.Max(0.1f, investigateMemoryDuration);
    public float InvestigateSpeedMultiplier => Mathf.Clamp(investigateSpeedMultiplier, 0.25f, 1f);
    public float SupportCallRange => Mathf.Max(0f, supportCallRange);
    public float AlertDuration => Mathf.Max(0f, alertDuration);
    public bool PlayTauntOnAlert => playTauntOnAlert;
    public float IdleBreakMinInterval => Mathf.Max(0f, idleBreakMinInterval);
    public float IdleBreakMaxInterval => Mathf.Max(IdleBreakMinInterval, idleBreakMaxInterval);
    public float RecoveryDuration => Mathf.Max(0f, recoveryDuration);
    public float PreferredMinDistance => Mathf.Max(0f, preferredMinDistance);
    public float RepositionDistance => Mathf.Max(0.1f, repositionDistance);
    public float RepositionDuration => Mathf.Max(0.1f, repositionDuration);
    public float LowHealthRepositionThreshold => Mathf.Clamp01(lowHealthRepositionThreshold);
    public float LowHealthRepositionDistance => Mathf.Max(0.5f, lowHealthRepositionDistance);
    public float LowHealthRepositionDuration => Mathf.Max(0.1f, lowHealthRepositionDuration);
    public float DodgeLungeMinDistance => Mathf.Max(0.5f, dodgeLungeMinDistance);
    public float DodgeLungeMaxDistance => Mathf.Max(DodgeLungeMinDistance, dodgeLungeMaxDistance);
    public float DodgeLungeDistance => Mathf.Max(0.5f, dodgeLungeDistance);
    public float DodgeLungeDuration => Mathf.Max(0.1f, dodgeLungeDuration);
    public float DodgeLungeCooldown => Mathf.Max(0f, dodgeLungeCooldown);
    public float DodgeLungeChance => Mathf.Clamp01(dodgeLungeChance);
    public float DodgeVisualHeight => Mathf.Max(0f, dodgeVisualHeight);
    public float RunApproachMinDistance => Mathf.Max(0f, runApproachMinDistance);
    public float PreferredApproachDistance => Mathf.Max(0.5f, preferredApproachDistance);
    public float ApproachSideAngle => Mathf.Clamp(approachSideAngle, 0f, 90f);
    public float SeparationRadius => Mathf.Max(0.5f, separationRadius);
    public float SeparationWeight => Mathf.Clamp(separationWeight, 0f, 2f);
    public float ApproachDirectionMinDuration => Mathf.Max(0.5f, approachDirectionMinDuration);
    public float ApproachDirectionMaxDuration => Mathf.Max(ApproachDirectionMinDuration, approachDirectionMaxDuration);
    public float AttackTurnCooldown => Mathf.Max(0f, attackTurnCooldown);
    public float AttackWaitDuration => Mathf.Max(0.1f, attackWaitDuration);
    public float MemberEngageRange => Mathf.Max(0.1f, memberEngageRange);
    public float MemberEngageVerticalTolerance => Mathf.Max(0f, memberEngageVerticalTolerance);
    public bool HasShield => hasShield;
    public float DefendChance => hasShield ? Mathf.Clamp01(defendChance) : 0f;
    public float DefendDuration => Mathf.Max(0.1f, defendDuration);
    public float BlockedDamageMultiplier => Mathf.Clamp01(blockedDamageMultiplier);
    public float BlockAngle => Mathf.Clamp(blockAngle, 0f, 180f);

    public void Configure(
        string id,
        EnemyBehaviorTendency behaviorTendency,
        EnemyRepositionStyle newRepositionStyle,
        float newAlertDuration,
        bool tauntOnAlert,
        float newRecoveryDuration,
        float newPreferredMinDistance,
        float newRepositionDistance,
        float newRepositionDuration,
        float newLowHealthRepositionThreshold,
        float newLowHealthRepositionDistance,
        float newLowHealthRepositionDuration,
        bool shield,
        float newDefendChance,
        float newDefendDuration,
        float newBlockedDamageMultiplier,
        float newBlockAngle)
    {
        profileId = string.IsNullOrWhiteSpace(id) ? "Default" : id;
        tendency = behaviorTendency;
        repositionStyle = newRepositionStyle;
        alertDuration = Mathf.Max(0f, newAlertDuration);
        playTauntOnAlert = tauntOnAlert;
        recoveryDuration = Mathf.Max(0f, newRecoveryDuration);
        preferredMinDistance = Mathf.Max(0f, newPreferredMinDistance);
        repositionDistance = Mathf.Max(0.1f, newRepositionDistance);
        repositionDuration = Mathf.Max(0.1f, newRepositionDuration);
        lowHealthRepositionThreshold = Mathf.Clamp01(newLowHealthRepositionThreshold);
        lowHealthRepositionDistance = Mathf.Max(0.5f, newLowHealthRepositionDistance);
        lowHealthRepositionDuration = Mathf.Max(0.1f, newLowHealthRepositionDuration);
        hasShield = shield;
        defendChance = shield ? Mathf.Clamp01(newDefendChance) : 0f;
        defendDuration = Mathf.Max(0.1f, newDefendDuration);
        blockedDamageMultiplier = Mathf.Clamp01(newBlockedDamageMultiplier);
        blockAngle = Mathf.Clamp(newBlockAngle, 0f, 180f);
    }

    public void ConfigureRunApproach(float newRunApproachMinDistance)
    {
        runApproachMinDistance = Mathf.Max(0f, newRunApproachMinDistance);
    }

    public void ConfigureDodgeLunge(
        float newMinDistance,
        float newMaxDistance,
        float newDistance,
        float newDuration,
        float newCooldown,
        float newChance,
        float newVisualHeight)
    {
        repositionStyle = EnemyRepositionStyle.Dodge;
        dodgeLungeMinDistance = Mathf.Max(0.5f, newMinDistance);
        dodgeLungeMaxDistance = Mathf.Max(dodgeLungeMinDistance, newMaxDistance);
        dodgeLungeDistance = Mathf.Max(0.5f, newDistance);
        dodgeLungeDuration = Mathf.Max(0.1f, newDuration);
        dodgeLungeCooldown = Mathf.Max(0f, newCooldown);
        dodgeLungeChance = Mathf.Clamp01(newChance);
        dodgeVisualHeight = Mathf.Max(0f, newVisualHeight);
    }

    public void ConfigurePeace(float newPatrolChance, float newPatrolRadius, float newPatrolSpeedMultiplier)
    {
        patrolChance = Mathf.Clamp01(newPatrolChance);
        patrolRadius = Mathf.Max(0.5f, newPatrolRadius);
        patrolSpeedMultiplier = Mathf.Clamp(newPatrolSpeedMultiplier, 0.25f, 1f);
    }

    public void ConfigureApproach(
        float newPreferredApproachDistance,
        float newApproachSideAngle,
        float newSeparationRadius,
        float newSeparationWeight)
    {
        preferredApproachDistance = Mathf.Max(0.5f, newPreferredApproachDistance);
        approachSideAngle = Mathf.Clamp(newApproachSideAngle, 0f, 90f);
        separationRadius = Mathf.Max(0.5f, newSeparationRadius);
        separationWeight = Mathf.Clamp(newSeparationWeight, 0f, 2f);
        approachDirectionMinDuration = 2f;
        approachDirectionMaxDuration = 4f;
    }

    public void ConfigureAttackRhythm(float newAttackTurnCooldown, float newAttackWaitDuration)
    {
        attackTurnCooldown = Mathf.Max(0f, newAttackTurnCooldown);
        attackWaitDuration = Mathf.Max(0.1f, newAttackWaitDuration);
    }

    public void ConfigurePartyTargeting(float newMemberEngageRange, float newMemberEngageVerticalTolerance)
    {
        memberEngageRange = Mathf.Max(0.1f, newMemberEngageRange);
        memberEngageVerticalTolerance = Mathf.Max(0f, newMemberEngageVerticalTolerance);
    }

    public void ConfigureAwareness(
        float newNoticeRange,
        float newInvestigateRange,
        float newDiscoverRange,
        float newDiscoverFieldOfView,
        float newNoticeDuration,
        float newNoticeCooldown,
        float newInvestigateMemoryDuration,
        float newInvestigateSpeedMultiplier,
        float newSupportCallRange)
    {
        discoverRange = Mathf.Max(0.5f, newDiscoverRange);
        investigateRange = Mathf.Max(discoverRange, newInvestigateRange);
        noticeRange = Mathf.Max(investigateRange, newNoticeRange);
        discoverFieldOfView = Mathf.Clamp(newDiscoverFieldOfView, 1f, 360f);
        noticeDuration = Mathf.Max(0.1f, newNoticeDuration);
        noticeCooldown = Mathf.Max(0.1f, newNoticeCooldown);
        investigateMemoryDuration = Mathf.Max(0.1f, newInvestigateMemoryDuration);
        investigateSpeedMultiplier = Mathf.Clamp(newInvestigateSpeedMultiplier, 0.25f, 1f);
        supportCallRange = Mathf.Max(0f, newSupportCallRange);
    }
}
