using UnityEngine;

public static class MeleeEvadeCancelPolicy
{
    public const bool ResetComboOnCancel = true;

    public static bool CanCancel(bool isAttackInProgress, bool usesCombo)
    {
        return isAttackInProgress && usesCombo; // 실제 근접 콤보만 전 구간 허용
    }
}

public partial class MeleeRuntime : MonoBehaviour, IWeaponActionPort // 근접 런타임
{
    private const float DamageOverTimeTickInterval = 1f; // 지속 피해 주기
    private const float MinAttackDuration = 0.2f; // 공격 액션 최소 길이
    private const float MeleeCombatStanceCritChanceBonus = 10f; // 자세 치명 보너스
    private const float MeleeCombatStanceKnockbackMultiplier = 1.5f; // 자세 넉백 배율

    [Header("References")]
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerAnimation playerAnimatorController;
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private Camera attackCamera;
    [SerializeField] private CombatTarget combatTarget;
    [SerializeField] private PlayerActorRuntime playerActorRuntime;

    private WeaponFinalStats activeStats; // 공격 스탯
    private WeaponItemData activeWeaponData; // 공격 무기
    private MeleeComboDefinition activeComboDefinition; // 근접 콤보 정의
    private MeleeHeavyAttackDefinition activeHeavyDefinition;
    private OverburstElementEnergy activeHeavyEnergy;
    private OverburstElementDischarge activeDischarge;
    private bool heavyDischargeCommitted;
    private bool heavyParried; // 이번 강공이 패링에 성공했는지(에너지 절반 환급)
    private bool resolvingHeavyBlast;
    private bool activeAttackIsHeavy;
    private float activeAttackDamageMultiplier = 1f;
    private bool isAttacking; // 공격 중
    private float attackStartTime; // 시작 시간
    private float attackDuration; // 공격 시간
    private int comboStepIndex = -1;
    private float lastComboWindowTime = -999f;
    private bool activeAttackUsesCombo;
    private AnimationClip activeAttackAnimationClip; // 공격 애니
    private float activeAttackAnimationSpeed = 1f; // 공격 애니 속도
    private float activeAttackTransitionDuration; // 공격 전환 보간
    private AttackPhaseData[] activeAttackPhases;
    private readonly AttackPhaseExecutor attackPhaseExecutor = new AttackPhaseExecutor();
    private readonly AttackVisualHeightExecutor attackVisualHeight = new AttackVisualHeightExecutor();
    private readonly AttackMovementExecutor attackMovementExecutor = new AttackMovementExecutor();
    private readonly ComboMovementCollisionPusher comboMovementCollisionPusher = new ComboMovementCollisionPusher();
    private readonly AttackTrailExecutor attackTrailExecutor = new AttackTrailExecutor();
    private readonly MeleeHeavyDischargeExecutor heavyDischargeExecutor = new MeleeHeavyDischargeExecutor();
    private AttackPatternDebugRenderer attackPatternDebugRenderer;
    private Vector3 activeAttackDirection; // 공격 방향
    private IWeaponTrailController activeAttackTrail;
    private MeleeComboStepData activeAttackStep;
    private bool activeAttackUsedMeleeCombatStance; // 전투 자세
    private ItemData activeAttackWeaponItem; // 공격 아이템
    private bool manualInputEnabled = true; // 파티 전환 중 기존 공격은 유지하고 신규 입력만 막는다.
    private bool suppressHandoffMoveCancelUntilRelease; // 인계 전 이동키 무시
    private bool bufferedHandoffComboContinuation; // 교체 프레임 콤보 입력 보존
    private int nextActionId = 1; // 행동 식별자
    private int nextHitFeedbackSequenceId = 1; // 타수별 피드백 식별자
    private int activeHitFeedbackSequenceId; // 현재 타수 피드백 식별자
    private int activeActionId; // 진행 행동
    private int terminalActionId; // 마지막 종료 행동
    private WeaponActionSource activeActionSource; // 행동 요청 원본
    private CombatTarget activeRequestedTarget; // AI 고정 타깃
    private WeaponActionState terminalActionState; // 마지막 종료 결과
    private PlayerInputFacade inputFacade; // GOAL A2 파사드 캐시
    private PlayerStateCoordinator stateCoordinator; // GOAL A2 상태 보고
    private PlayerEvadeController inputEvadeController;

    public WeaponRuntimeKind RuntimeKind => WeaponRuntimeKind.Melee;
    public bool CanUseCurrentWeapon => playerEquipment != null && playerEquipment.CanCurrentWeaponUseMeleeSlash;
    public bool IsBusy => IsAttackInProgress;
    public bool IsReady => IsAttackReady;
    public float CooldownRemaining => 0f;
    public float CooldownProgress01 => 1f;

    public bool IsAttackReady => CanUseCurrentWeapon && !isAttacking;
    public bool IsHeavyAttackInProgress => isAttacking && activeAttackIsHeavy;

    public bool IsAttackInProgress
    {
        get { return isAttacking; }
    }

    public bool CanCancelActiveComboForEvade
    {
        get { return MeleeEvadeCancelPolicy.CanCancel(isAttacking, activeAttackUsesCombo); }
    }

    private void Awake()
    {
        ResolveReferences();
        ResolveAttackPatternDebugRenderer();
    }

    private void OnDisable()
    {
        ReleaseAttackStates();
        if (Application.isPlaying)
            CancelActiveAttack(WeaponActionCompletionReason.RuntimeDisabled, true);
        else
            attackPatternDebugRenderer?.Hide();
    }

    private void Update()
    {
        ResolveReferences();
        ResetStateIfWeaponChanged();

        // Resolve an executable evade before accepting an attack, independent of
        // MonoBehaviour Update order. Cooldown/locks/cost remain owned by evade.
        if (manualInputEnabled && inputEvadeController != null
            && inputEvadeController.TryExecuteBufferedEvade())
            return;

        if (!CombatDebugSettings.ShowAttackPatternDebug)
            attackPatternDebugRenderer?.Hide();

        if (IsPlayerEvading())
        {
            if (CanCancelActiveComboForEvade)
                CancelActiveComboForEvade(); // 입력 순서와 무관하게 같은 회피 취소 계약 적용

            return;
        }

        if (ShouldStartHeavyAttack())
        {
            TryStartHeavyAttack(CaptureAttackStartDirection());
        }
        else if (ShouldStartAttack())
        {
            WeaponActionRequest request = new WeaponActionRequest(
                WeaponActionSource.PlayerInput,
                null,
                CaptureAttackStartDirection());
            TryStartAction(request, out _);
        }

        UpdateAttack();
    }

    private void ResolveReferences()
    {
        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        if (playerAnimatorController == null)
            playerAnimatorController = GetComponent<PlayerAnimation>();

        if (playerController == null)
            playerController = GetComponent<PlayerMovement>();

        if (attackCamera == null)
            attackCamera = Camera.main;

        if (combatTarget == null)
            combatTarget = GetComponent<CombatTarget>();

        if (playerActorRuntime == null)
            playerActorRuntime = GetComponent<PlayerActorRuntime>();

        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();

        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (inputEvadeController == null)
            inputEvadeController = GetComponent<PlayerEvadeController>();
    }

    private int AllocateActionId()
    {
        if (nextActionId <= 0)
            nextActionId = 1;

        int actionId = nextActionId;
        nextActionId++;
        return actionId;
    }

    private int AllocateHitFeedbackSequenceId()
    {
        if (nextHitFeedbackSequenceId <= 0)
            nextHitFeedbackSequenceId = 1;

        int sequenceId = nextHitFeedbackSequenceId;
        nextHitFeedbackSequenceId++;
        return sequenceId;
    }

    private bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }

    private PlayerInputFacade ResolveFacade()
    {
        if (inputFacade == null)
            inputFacade = GetComponent<PlayerInputFacade>();
        if (inputFacade == null)
            inputFacade = PlayerInputFacade.Current;
        return inputFacade;
    }

    private PlayerStateCoordinator ResolveStateCoordinator()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        return stateCoordinator;
    }

}
