using System.Collections;
using UnityEngine;

public class EnemyAnimationBridge : MonoBehaviour
{
    private const float LocomotionDampTime = 0.08f; // 걷기·달리기 전환 급변 완화
    private const float ActionStateEntryTimeout = 0.75f; // Trigger가 상태에 진입하지 못했을 때 이동 잠금 자동 해제

    [SerializeField] private Animator animator;
    [SerializeField] private string moveParameter = "Locomotion";
    [SerializeField] private string locomotionStateName = "Locomotion";
    [SerializeField] private string attackTrigger = "Attack1";
    [SerializeField] private string hitTrigger = "GotHit";
    [SerializeField] private string hitStateName = "Get_hit";
    [SerializeField] private string deathTrigger = "Death";
    [SerializeField] private string moveAnimSpeedParameter = "MoveAnimSpeed";
    [SerializeField] private string attackAnimSpeedParameter = "AttackAnimSpeed";
    [SerializeField] private bool disableRootMotionOnAwake = true;
    [SerializeField] private EnemyMovementReaction movementReaction; // 피격 경직 연결
    [SerializeField] private EnemyDefenseController defenseController; // 방패 방어 연결
    [SerializeField] private float hitStunDuration = 0.4f;
    [SerializeField] private float knockbackReactionDuration = 0.4f;
    [SerializeField] private float hitReactionAnimationSpeedMultiplier = 2.5f;
    [SerializeField] private float hitAnimationSpeedBoostDuration = 0.45f;

    private CombatHealth health;
    private int moveParameterHash;
    private int attackTriggerHash;
    private int hitTriggerHash;
    private int deathTriggerHash;
    private int moveAnimSpeedHash;
    private int attackAnimSpeedHash;
    private bool hasMoveParameter;
    private bool hasAttackTrigger;
    private bool hasHitTrigger;
    private bool hasDeathTrigger;
    private bool hasMoveAnimSpeedParameter;
    private bool hasAttackAnimSpeedParameter;
    private bool hasDirectionalHit;
    private static readonly int HitXHash = Animator.StringToHash("HitX");
    private static readonly int HitZHash = Animator.StringToHash("HitZ");
    private bool isDead;
    private Coroutine hitSpeedRoutine;
    private float animatorSpeedBeforeHit = 1f;
    private string blockingActionStateName;
    private float blockingActionRequestTime;
    private bool blockingActionEntered;
    private bool hasAllowedLocomotionMode;
    private EnemyLocomotionMode allowedLocomotionMode;
    private bool isFrozen;
    private bool hasFrozenAnimatorSpeed;
    private float animatorSpeedBeforeFreeze = 1f;

    public bool HasAnimator { get { return animator != null; } }
    public bool IsFrozen { get { return isFrozen; } }
    public bool IsBlockingActionActive
    {
        get
        {
            RefreshBlockingAction();
            return !string.IsNullOrEmpty(blockingActionStateName);
        }
    }

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator != null && disableRootMotionOnAwake)
            animator.applyRootMotion = false; // 루트 모션 비활성

        CacheParameters();
        health = GetComponent<CombatHealth>();
        ResolveMovementReaction();
        ResolveDefenseController();
    }

    private void OnEnable()
    {
        isDead = health != null && health.IsDead;

        if (health == null)
            health = GetComponent<CombatHealth>();

        ResolveMovementReaction();
        ResolveDefenseController();

        if (health != null)
        {
            health.OnDamaged += HandleDamaged;
            health.OnDead += HandleDead;
        }
    }

    private void OnDisable()
    {
        RestoreFrozenAnimatorSpeed();
        isFrozen = false;
        ResetHitAnimationSpeed();
        ClearBlockingAction();

        if (health != null)
        {
            health.OnDamaged -= HandleDamaged;
            health.OnDead -= HandleDead;
        }
    }

    public void SetAnimator(Animator targetAnimator)
    {
        RestoreFrozenAnimatorSpeed();
        animator = targetAnimator;
        ClearBlockingAction();

        if (animator != null && disableRootMotionOnAwake)
            animator.applyRootMotion = false; // 루트 모션 비활성

        CacheParameters();
        if (isFrozen)
            ForceFrozenIdle();
    }

    public void ResetForReuse()
    {
        RestoreFrozenAnimatorSpeed();
        ResetHitAnimationSpeed();
        ClearBlockingAction();
        isDead = false;
        isFrozen = false;
        if (animator == null)
            return;

        animator.speed = 1f;
        if (animator.isActiveAndEnabled && animator.gameObject.activeInHierarchy)
        {
            animator.Rebind();
            animator.Update(0f);
        }
        CacheParameters();
        SetMoveAmount(0f);
        SetMoveAnimSpeed(1f);
        SetAttackAnimSpeed(1f);
    }

    public void SetMoveAmount(float value)
    {
        if (animator == null || !hasMoveParameter)
            return;

        float resolvedValue = isDead || isFrozen ? 0f : value;
        animator.SetFloat(
            moveParameterHash,
            Mathf.Clamp(resolvedValue, -1f, 2f),
            LocomotionDampTime,
            Mathf.Max(0.001f, Time.deltaTime)); // 후진·걷기·달리기 자연스러운 전환
    }

    public void SetMoveAnimSpeed(float value)
    {
        if (animator == null || !hasMoveAnimSpeedParameter)
            return;

        animator.SetFloat(
            moveAnimSpeedHash,
            Mathf.Max(0.01f, value),
            LocomotionDampTime,
            Mathf.Max(0.001f, Time.deltaTime)); // 이동 배속 급변 완화
    }

    public void SetAttackAnimSpeed(float value)
    {
        if (animator == null || !hasAttackAnimSpeedParameter)
            return;

        animator.SetFloat(attackAnimSpeedHash, Mathf.Max(0.01f, value)); // 공격 속도
    }

    public void PlayAttack()
    {
        if (isDead || isFrozen)
            return;

        PlayBlockingTrigger(attackTrigger, ResolveAttackStateName(attackTrigger));
    }

    public void PlayAttack(string triggerName)
    {
        if (isDead || isFrozen)
            return;

        if (string.IsNullOrWhiteSpace(triggerName))
        {
            PlayAttack();
            return;
        }

        PlayBlockingTrigger(triggerName, ResolveAttackStateName(triggerName));
    }

    public void PlayHit()
    {
        if (isDead || isFrozen)
            return;

        BeginHitAnimationSpeedBoost();
        BeginBlockingAction(hitStateName);

        if (IsHitAnimationActive())
        {
            if (animator != null && hasHitTrigger)
                animator.ResetTrigger(hitTriggerHash);

            int fullPathHash = Animator.StringToHash("Base Layer." + hitStateName);
            int shortNameHash = Animator.StringToHash(hitStateName);
            int stateHash = animator != null && animator.HasState(0, fullPathHash) ? fullPathHash : shortNameHash;
            if (animator != null && animator.HasState(0, stateHash))
            {
                animator.Play(stateHash, 0, 0f); // 연속 피격은 기존 모션을 취소하고 처음부터 재생
                animator.Update(0f);
                return;
            }
        }

        SetTrigger(hitTriggerHash, hasHitTrigger);
    }

    public void PlayTaunt()
    {
        PlayBlockingTrigger("Taunt", "Taunt");
    }

    public void PlayIdleBreak()
    {
        PlayBlockingTrigger("IdleBreak", "Idle_break");
    }

    public void PlayBlockStart()
    {
        PlayBlockingTrigger("BlockStart", "Block_Start");
    }

    public void PlayBlockStop()
    {
        PlayBlockingTrigger("BlockStop", "Block_End");
    }

    public void PlayBlockHit()
    {
        PlayBlockingTrigger("BlockGotHit", "Block_GetHitfbx");
    }

    public bool PlayDodge()
    {
        if (isDead || isFrozen || animator == null)
            return false;

        const string dodgeTrigger = "Dodge";
        return PlayBlockingTrigger(dodgeTrigger, "Dodge", EnemyLocomotionMode.Dodge); // Dodge 중 해당 이동만 허용
    }

    public void PlayDeath()
    {
        RestoreFrozenAnimatorSpeed();
        isFrozen = false; // 사망 표현이 빙결보다 우선
        ResetHitAnimationSpeed();
        ClearBlockingAction();
        isDead = true;
        SetMoveAmount(0f);
        SetTrigger(deathTriggerHash, hasDeathTrigger);
    }

    public bool TryGetAttackNormalizedTime(string triggerName, out float normalizedTime)
    {
        normalizedTime = 0f;
        if (animator == null || string.IsNullOrWhiteSpace(triggerName))
            return false;

        string stateName = ResolveAttackStateName(triggerName);
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (IsMatchingState(currentState, stateName))
        {
            normalizedTime = currentState.normalizedTime;
            return true;
        }

        if (!animator.IsInTransition(0))
            return false;

        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        if (!IsMatchingState(nextState, stateName))
            return false;

        normalizedTime = nextState.normalizedTime;
        return true;
    }

    public bool AllowsMovement(EnemyLocomotionMode locomotionMode)
    {
        if (isFrozen)
            return false;

        RefreshBlockingAction();
        if (string.IsNullOrEmpty(blockingActionStateName))
            return true;

        return hasAllowedLocomotionMode && allowedLocomotionMode == locomotionMode;
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (isFrozen)
            return; // 빙결 Idle을 피격 모션이 덮지 않음
        if (info.isDamageOverTime || !info.triggersOnHitEffects)
            return; // 직접 피격 반응만 처리
        if (source != null && source.CurrentHp <= 0f)
            return; // 사망 우선
        if (isDead)
            return;

        if (defenseController == null)
            ResolveDefenseController();
        if (defenseController != null && defenseController.ConsumeBlockedHit())
            return; // 방패 반응이 일반 피격보다 우선

        if (movementReaction == null) ResolveMovementReaction();
        bool weighted = movementReaction != null && movementReaction.HitWeightProfile != null;
        if (weighted && !movementReaction.TryApplyWeightedHit(info)) return;

        if (hasDirectionalHit && animator != null)
        {
            Vector3 towardSource = info.source != null ? info.source.transform.position - transform.position : -info.direction;
            Vector3 local = transform.InverseTransformDirection(towardSource);local.y = 0f;
            local = local.sqrMagnitude > .0001f ? local.normalized : Vector3.forward;
            animator.SetFloat(HitXHash, local.x);animator.SetFloat(HitZHash, local.z);
        }
        PlayHit();

        if (weighted) return; // 공유 무게 프로필이 이동/경직 시간을 함께 소유

        if (movementReaction == null)
            ResolveMovementReaction(); // 지연 연결

        if (movementReaction == null)
        {
            Debug.LogWarning("[ProjectVTP] EnemyAnimationBridge could not find EnemyMovementReaction. Hit reaction skipped.", this);
            return;
        }

        float resolvedHitStunDuration = info.hitReaction.overridesTargetDefaults
            ? info.hitReaction.hitStunDuration
            : hitStunDuration;
        float resolvedKnockbackReactionDuration = info.hitReaction.overridesTargetDefaults
            ? info.hitReaction.knockbackReactionDuration
            : knockbackReactionDuration;

        if (info.knockback > 0f)
            movementReaction.ExtendKnockbackReaction(resolvedKnockbackReactionDuration); // 넉백 경직
        else
            movementReaction.ApplyHitStun(resolvedHitStunDuration); // 피격 경직
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        isDead = true;
        PlayDeath();
    }

    private void SetTrigger(int triggerHash, bool hasParameter)
    {
        if (animator == null || !hasParameter)
            return;

        animator.SetTrigger(triggerHash);
    }

    private bool PlayBlockingTrigger(
        string triggerName,
        string stateName,
        EnemyLocomotionMode? allowedMode = null)
    {
        if (isDead || isFrozen || animator == null || string.IsNullOrWhiteSpace(triggerName))
            return false;

        bool hasTrigger = HasParameter(triggerName, AnimatorControllerParameterType.Trigger);
        if (!hasTrigger || !BeginBlockingAction(stateName, allowedMode))
            return false;

        SetMoveAmount(0f);
        SetTrigger(Animator.StringToHash(triggerName), true);
        return true;
    }

    private bool BeginBlockingAction(string stateName, EnemyLocomotionMode? allowedMode = null)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName) || !HasState(stateName))
            return false;

        blockingActionStateName = stateName;
        blockingActionRequestTime = Time.time;
        blockingActionEntered = IsStateActive(stateName);
        hasAllowedLocomotionMode = allowedMode.HasValue;
        allowedLocomotionMode = allowedMode.GetValueOrDefault();
        return true;
    }

    private void RefreshBlockingAction()
    {
        if (string.IsNullOrEmpty(blockingActionStateName))
            return;
        if (animator == null)
        {
            ClearBlockingAction();
            return;
        }

        bool isActive = IsStateActive(blockingActionStateName);
        if (!blockingActionEntered)
        {
            if (isActive)
                blockingActionEntered = true;
            else if (Time.time >= blockingActionRequestTime + ActionStateEntryTimeout)
                ClearBlockingAction();
            return;
        }

        if (!isActive)
            ClearBlockingAction();
    }

    private bool IsStateActive(string stateName)
    {
        if (animator == null)
            return false;

        if (IsMatchingState(animator.GetCurrentAnimatorStateInfo(0), stateName))
            return true;

        return animator.IsInTransition(0)
            && IsMatchingState(animator.GetNextAnimatorStateInfo(0), stateName);
    }

    private bool HasState(string stateName)
    {
        int fullPathHash = Animator.StringToHash("Base Layer." + stateName);
        int shortNameHash = Animator.StringToHash(stateName);
        return animator != null
            && (animator.HasState(0, fullPathHash) || animator.HasState(0, shortNameHash));
    }

    private void ClearBlockingAction()
    {
        blockingActionStateName = null;
        blockingActionRequestTime = 0f;
        blockingActionEntered = false;
        hasAllowedLocomotionMode = false;
        allowedLocomotionMode = EnemyLocomotionMode.Idle;
    }

    public void SetFrozen(bool frozen)
    {
        if (isDead)
        {
            if (!frozen)
            {
                RestoreFrozenAnimatorSpeed();
                isFrozen = false;
            }
            return;
        }
        if (isFrozen == frozen)
            return;

        isFrozen = frozen;
        if (!frozen)
        {
            RestoreFrozenAnimatorSpeed();
            return;
        }

        ForceFrozenIdle();
    }

    private void ForceFrozenIdle()
    {
        ResetHitAnimationSpeed();
        ClearBlockingAction();
        if (animator == null)
            return;

        if (hasAttackTrigger)
            animator.ResetTrigger(attackTriggerHash);
        if (hasHitTrigger)
            animator.ResetTrigger(hitTriggerHash);
        if (hasMoveParameter)
            animator.SetFloat(moveParameterHash, 0f); // 감쇠 없이 즉시 Idle 값

        int fullPathHash = Animator.StringToHash("Base Layer." + locomotionStateName);
        int shortNameHash = Animator.StringToHash(locomotionStateName);
        int stateHash = animator.HasState(0, fullPathHash) ? fullPathHash : shortNameHash;
        if (animator.HasState(0, stateHash))
        {
            animator.Play(stateHash, 0, 0f); // 진행 공격·피격 모션 즉시 중단
            animator.Update(0f);
        }

        animatorSpeedBeforeFreeze = animator.speed;
        hasFrozenAnimatorSpeed = true;
        animator.speed = 0f; // Idle 첫 자세에서 재생 자체도 멈춰 얼어붙은 포즈 유지
    }

    private void RestoreFrozenAnimatorSpeed()
    {
        if (!hasFrozenAnimatorSpeed)
            return;

        if (animator != null)
            animator.speed = animatorSpeedBeforeFreeze;
        hasFrozenAnimatorSpeed = false;
    }

    private void CacheParameters()
    {
        moveParameterHash = Animator.StringToHash(moveParameter);
        attackTriggerHash = Animator.StringToHash(attackTrigger);
        hitTriggerHash = Animator.StringToHash(hitTrigger);
        deathTriggerHash = Animator.StringToHash(deathTrigger);
        moveAnimSpeedHash = Animator.StringToHash(moveAnimSpeedParameter);
        attackAnimSpeedHash = Animator.StringToHash(attackAnimSpeedParameter);

        hasMoveParameter = HasParameter(moveParameter, AnimatorControllerParameterType.Float);
        hasAttackTrigger = HasParameter(attackTrigger, AnimatorControllerParameterType.Trigger);
        hasHitTrigger = HasParameter(hitTrigger, AnimatorControllerParameterType.Trigger);
        hasDeathTrigger = HasParameter(deathTrigger, AnimatorControllerParameterType.Trigger);
        hasMoveAnimSpeedParameter = HasParameter(moveAnimSpeedParameter, AnimatorControllerParameterType.Float);
        hasAttackAnimSpeedParameter = HasParameter(attackAnimSpeedParameter, AnimatorControllerParameterType.Float);
        hasDirectionalHit = HasParameter("HitX", AnimatorControllerParameterType.Float)
            && HasParameter("HitZ", AnimatorControllerParameterType.Float);
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == parameterType && parameter.name == parameterName)
                return true;
        }

        return false;
    }

    private void ResolveMovementReaction()
    {
        if (movementReaction != null)
            return;

        movementReaction = GetComponent<EnemyMovementReaction>();
        if (movementReaction == null)
            movementReaction = GetComponentInParent<EnemyMovementReaction>();
        if (movementReaction == null)
            movementReaction = GetComponentInChildren<EnemyMovementReaction>(true);
    }

    private void ResolveDefenseController()
    {
        if (defenseController == null)
            defenseController = GetComponentInParent<EnemyDefenseController>();
        if (defenseController == null)
            defenseController = GetComponentInChildren<EnemyDefenseController>(true);
    }

    private void BeginHitAnimationSpeedBoost()
    {
        if (animator == null)
            return;

        if (hitSpeedRoutine != null)
        {
            StopCoroutine(hitSpeedRoutine);
            animator.speed = animatorSpeedBeforeHit;
        }

        animatorSpeedBeforeHit = animator.speed;
        animator.speed = animatorSpeedBeforeHit * Mathf.Max(0.01f, hitReactionAnimationSpeedMultiplier);
        hitSpeedRoutine = StartCoroutine(RestoreHitAnimationSpeedAfterDelay());
    }

    private IEnumerator RestoreHitAnimationSpeedAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, hitAnimationSpeedBoostDuration));
        if (animator != null)
            animator.speed = animatorSpeedBeforeHit;
        hitSpeedRoutine = null;
    }

    private void ResetHitAnimationSpeed()
    {
        if (hitSpeedRoutine == null)
            return;

        StopCoroutine(hitSpeedRoutine);
        hitSpeedRoutine = null;
        if (animator != null)
            animator.speed = animatorSpeedBeforeHit;
    }

    private static string ResolveAttackStateName(string triggerName)
    {
        const string AttackPrefix = "Attack";
        if (triggerName.StartsWith(AttackPrefix) && triggerName.Length > AttackPrefix.Length)
            return AttackPrefix + "_" + triggerName.Substring(AttackPrefix.Length);

        return triggerName;
    }

    private static bool IsMatchingState(AnimatorStateInfo stateInfo, string stateName)
    {
        return stateInfo.IsName(stateName) || stateInfo.IsName("Base Layer." + stateName);
    }

    private bool IsHitAnimationActive()
    {
        if (animator == null || string.IsNullOrWhiteSpace(hitStateName))
            return false;

        if (IsMatchingState(animator.GetCurrentAnimatorStateInfo(0), hitStateName))
            return true;

        return animator.IsInTransition(0)
            && IsMatchingState(animator.GetNextAnimatorStateInfo(0), hitStateName);
    }
}
