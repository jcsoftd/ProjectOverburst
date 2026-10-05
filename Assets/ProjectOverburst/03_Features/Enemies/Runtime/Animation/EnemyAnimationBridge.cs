using System.Collections;
using System.Collections.Generic;
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

    private CombatHealth health;
    private EnemyHitResponseCoordinator hitResponseCoordinator;
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
    private string blockingActionStateName;
    private float blockingActionRequestTime;
    private bool blockingActionEntered;
    private bool hasAllowedLocomotionMode;
    private EnemyLocomotionMode allowedLocomotionMode;
    private bool isFrozen;
    private bool hasFrozenAnimatorSpeed;
    private float animatorSpeedBeforeFreeze = 1f;
    private Coroutine parryStunRoutine;
    private bool normalParryActive, normalParryClockHeld;
    private float animatorSpeedBeforeNormalParryHold;
    private bool parryStunActive; // 패링 무너짐·기절 루프·회복 재생 중
    private bool parryStunRecovering, parryHitReturnPending, parryHitEntered;
    private float parryHitRequestTime;

    public bool HasAnimator { get { return animator != null; } }
    public Animator MotionAnimator => animator;
    public bool IsFrozen { get { return isFrozen; } }
    public bool BlocksAttackStart => parryStunActive || IsBlockingActionActive
        && !(blockingActionStateName == hitStateName && movementReaction != null
            && movementReaction.ActsThroughOrdinaryHit && !movementReaction.BlocksAttack);
    public bool IsBlockingActionActive
    {
        get
        {
            if (parryStunActive) return true; // 패링 반응 중에는 회전·공격·이동 시작을 막는다
            RefreshBlockingAction();
            return !string.IsNullOrEmpty(blockingActionStateName);
        }
    }
    public bool IsParryStunAnimating => parryStunActive;
    public bool IsNormalParryReacting => normalParryActive;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator != null && disableRootMotionOnAwake)
            animator.applyRootMotion = false; // 루트 모션 비활성

        CacheParameters();
        health = GetComponent<CombatHealth>();
        hitResponseCoordinator = GetComponent<EnemyHitResponseCoordinator>();
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
        if (hitResponseCoordinator == null)
            hitResponseCoordinator = GetComponent<EnemyHitResponseCoordinator>();

        if (health != null)
        {
            if (hitResponseCoordinator == null)
                health.OnDamaged += HandleDamaged;
            health.OnDead += HandleDead;
        }
    }

    private void OnDisable()
    {
        StopParryStun();
        RestoreFrozenAnimatorSpeed();
        isFrozen = false;
        ClearBlockingAction();

        if (health != null)
        {
            health.OnDamaged -= HandleDamaged;
            health.OnDead -= HandleDead;
        }
    }

    public void SetAnimator(Animator targetAnimator)
    {
        StopParryStun();
        RestoreFrozenAnimatorSpeed();
        animator = targetAnimator;
        attackMotionController = null; availableAttackMotions.Clear();
        ClearBlockingAction();

        if (animator != null && disableRootMotionOnAwake)
            animator.applyRootMotion = false; // 루트 모션 비활성

        CacheParameters();
        if (isFrozen)
            ForceFrozenIdle();
    }

    public void ResetForReuse()
    {
        StopParryStun();
        RestoreFrozenAnimatorSpeed();
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

        string stateName = ResolveAttackStateName(triggerName);
        bool interruptHit = (blockingActionStateName == hitStateName || IsHitAnimationActive()) && movementReaction != null
            && movementReaction.ActsThroughOrdinaryHit && !movementReaction.BlocksAttack;
        if (interruptHit && animator != null && HasState(stateName))
        {
            if (hasHitTrigger) animator.ResetTrigger(hitTriggerHash);
            BeginBlockingAction(stateName);
            int full = Animator.StringToHash("Base Layer." + stateName);
            animator.CrossFadeInFixedTime(animator.HasState(0, full) ? full
                : Animator.StringToHash(stateName), .07f, 0, 0f);
            return;
        }
        PlayBlockingTrigger(triggerName, stateName);
    }

    public void PlayHit()
    {
        if (isDead || isFrozen)
            return;
        if (parryStunActive)
        {
            // 기절의 종료 시각과 행동 잠금은 유지하고 피격 자세만 잠깐 재생한다.
            // 일반 패링의 끝 자세 정지와 기상 동작은 기존대로 마친다.
            if (!normalParryActive && !parryStunRecovering && animator != null
                && animator.isActiveAndEnabled && HasState(hitStateName))
            {
                if (hasHitTrigger) animator.ResetTrigger(hitTriggerHash);
                parryHitReturnPending = true;
                parryHitEntered = false;
                parryHitRequestTime = Time.time;
                int fullPath = BaseLayerHash(hitStateName);
                animator.CrossFadeInFixedTime(animator.HasState(0, fullPath) ? fullPath
                    : Animator.StringToHash(hitStateName), .07f, 0, 0f);
            }
            return;
        }

        // Hit/Death state speeds are authored independently; never accelerate the whole Animator.
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

    // 2026-10-01 패링 반응: 공격 되감기 연출을 없애고 몬스터별 3클립을 재생한다.
    // Parry_Collapse(무너짐) -> Stunned_Loop(기절 루프) -> Stun_Recover(회복). 무너짐->루프, 회복->Locomotion 전이는
    // 컨트롤러가 클립 끝에서, 루프->회복은 기절 시간이 끝날 때 코드가 넘긴다. 무너짐 1.3배속, 기절 루프 2초(사용자 조정).
    public const string ParryCollapseStateName = "Parry_Collapse";
    public const string StunnedLoopStateName = "Stunned_Loop";
    public const string StunRecoverStateName = "Stun_Recover";
    public const float ParryStunnedSeconds = 2f;   // 기절 루프 시간. 루프 클립은 모두 2초 이상이다
    public const float ParryCollapseSpeed = 1.3f;  // 무너짐 재생 속도. 컨트롤러 Parry_Collapse 상태 speed와 같아야 한다
    private const float ParryCollapseBlend = .1f; // 공격 자세 -> 무너짐 첫 자세
    private const float StunRecoverBlend = .25f;  // 루프 중간 자세 -> 회복 첫 자세

    public const float NormalParryHoldSeconds = .2f;
    private sealed class ParryStunClipSet { public bool valid; public float collapse, loop, recover; }
    private static readonly Dictionary<RuntimeAnimatorController, ParryStunClipSet> ParryStunClipCache =
        new Dictionary<RuntimeAnimatorController, ParryStunClipSet>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetParryStunClipCache() { ParryStunClipCache.Clear(); }

    // Normal parry shows the collapsed pose, briefly holds it, then recovers without a stun loop.
    public bool TryPlayNormalParryReaction(out float reactionSeconds)
    {
        reactionSeconds = 0f;
        if (isDead || isFrozen || animator == null || !isActiveAndEnabled
            || !TryGetParryStunClips(out ParryStunClipSet clips)) return false;
        StopParryStun();
        ClearBlockingAction();
        ResetActionTriggers();
        reactionSeconds = clips.collapse / ParryCollapseSpeed + NormalParryHoldSeconds + clips.recover;
        parryStunActive = normalParryActive = true;
        SetMoveAmount(0f);
        animator.CrossFadeInFixedTime(BaseLayerHash(ParryCollapseStateName), ParryCollapseBlend, 0, 0f);
        parryStunRoutine = StartCoroutine(RunNormalParryReaction(clips.collapse, clips.recover));
        return true;
    }

    private IEnumerator RunNormalParryReaction(float collapseSeconds, float recoverSeconds)
    {
        try
        {
            float entryWait = 0f;
            while (!animator.GetCurrentAnimatorStateInfo(0).IsName(ParryCollapseStateName))
            {
                entryWait += Time.deltaTime;
                if (entryWait > ActionStateEntryTimeout) yield break;
                yield return null;
            }
            while (true)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                float nextProgress = Time.deltaTime * state.speed * state.speedMultiplier
                    * Mathf.Max(0f, animator.speed) / Mathf.Max(.01f, collapseSeconds);
                if (state.normalizedTime + nextProgress >= .98f) break;
                yield return null;
            }
            // Stay before the authored exit at 1.0 so normal parry never enters Stunned_Loop.
            animatorSpeedBeforeNormalParryHold = animator.speed;
            normalParryClockHeld = true;
            animator.speed = 0f;
            animator.Play(BaseLayerHash(ParryCollapseStateName), 0, .999f);
            animator.Update(0f);
            for (float t = 0f; t < NormalParryHoldSeconds; t += Time.deltaTime) yield return null;
            RestoreNormalParryClock();
            animator.CrossFadeInFixedTime(BaseLayerHash(StunRecoverStateName), StunRecoverBlend, 0, 0f);
            for (float t = 0f; t < recoverSeconds; t += Time.deltaTime) yield return null;
        }
        finally
        {
            RestoreNormalParryClock();
            parryStunRoutine = null;
            parryStunActive = normalParryActive = false;
        }
    }

    private void RestoreNormalParryClock()
    {
        if (!normalParryClockHeld) return;
        normalParryClockHeld = false;
        if (animator != null) animator.speed = animatorSpeedBeforeNormalParryHold;
    }

    // 전용 클립이 있으면 무너짐을 재생하고, 공격을 막는 기절 시간(무너짐 + 기절 루프)을 돌려준다.
    // 회복 동작은 그 뒤에 재생되며 끝날 때까지 회전·이동·공격 시작을 막는다.
    public bool TryPlayParryStun(out float stunSeconds)
    {
        stunSeconds = 0f;
        if (isDead || isFrozen || animator == null || !isActiveAndEnabled
            || !TryGetParryStunClips(out ParryStunClipSet clips))
            return false;

        StopParryStun();
        ClearBlockingAction();
        ResetActionTriggers();
        stunSeconds = clips.collapse / ParryCollapseSpeed + ParryStunnedSeconds;
        parryStunActive = true;
        SetMoveAmount(0f);
        animator.CrossFadeInFixedTime(BaseLayerHash(ParryCollapseStateName), ParryCollapseBlend, 0, 0f);
        parryStunRoutine = StartCoroutine(RunParryStun(stunSeconds, clips.recover));
        return true;
    }

    private IEnumerator RunParryStun(float stunSeconds, float recoverSeconds)
    {
        try
        {
            // 피격 재시작은 이 시간을 늘리지 않는다. 히트스톱·슬로우는 애니메이터와 같이 따른다.
            for (float t = 0f; t < stunSeconds; t += Time.deltaTime)
            {
                ResumeParryStunAfterHit();
                yield return null;
            }
            parryHitReturnPending = parryHitEntered = false;
            parryStunRecovering = true;
            if (animator != null)
                animator.CrossFadeInFixedTime(BaseLayerHash(StunRecoverStateName), StunRecoverBlend, 0, 0f);
            for (float t = 0f; t < recoverSeconds; t += Time.deltaTime)
                yield return null;
        }
        finally
        {
            parryStunRoutine = null;
            parryStunActive = parryStunRecovering = parryHitReturnPending = parryHitEntered = false;
        }
    }

    private void ResumeParryStunAfterHit()
    {
        if (!parryHitReturnPending || animator == null) return;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        bool currentHit = IsMatchingState(state, hitStateName);
        bool transitioning = animator.IsInTransition(0);
        bool nextHit = transitioning && IsMatchingState(animator.GetNextAnimatorStateInfo(0), hitStateName);
        if (currentHit || nextHit) parryHitEntered = true;
        if (!parryHitEntered && Time.time < parryHitRequestTime + ActionStateEntryTimeout) return;
        if (nextHit || currentHit && !transitioning && state.normalizedTime < 1f) return;

        // Get_hit의 Locomotion 전이 대신 남아 있는 기절 루프로 돌아간다.
        parryHitReturnPending = parryHitEntered = false;
        animator.CrossFadeInFixedTime(BaseLayerHash(StunnedLoopStateName), ParryCollapseBlend, 0, 0f);
    }

    // 빙결이 기절 동작을 끊었다가 풀리면, 남은 기절 시간만큼 기절 루프부터 이어 간다.
    private void ResumeParryStunAfterFreeze()
    {
        if (movementReaction == null)
            ResolveMovementReaction();
        if (movementReaction == null || !movementReaction.IsParryStunned || animator == null || !isActiveAndEnabled
            || !TryGetParryStunClips(out ParryStunClipSet clips))
            return;

        parryStunActive = true;
        animator.CrossFadeInFixedTime(BaseLayerHash(StunnedLoopStateName), StunRecoverBlend, 0, 0f);
        parryStunRoutine = StartCoroutine(RunParryStun(movementReaction.ParryStunRemaining, clips.recover));
    }

    private void StopParryStun()
    {
        if (parryStunRoutine != null)
            StopCoroutine(parryStunRoutine);
        RestoreNormalParryClock();
        parryStunRoutine = null;
        parryStunActive = normalParryActive = false;
        parryStunRecovering = parryHitReturnPending = parryHitEntered = false;
        parryHitRequestTime = 0f;
    }

    private bool TryGetParryStunClips(out ParryStunClipSet clips)
    {
        clips = null;
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null)
            return false;

        if (!ParryStunClipCache.TryGetValue(controller, out clips))
        {
            clips = new ParryStunClipSet();
            EnemyAnimationRoleResolver.ResolveParryDurations(controller, GetComponent<EnemyActor>()?.Definition?.AnimationProfile,
                out clips.collapse, out clips.loop, out clips.recover);
            clips.valid = clips.collapse > 0f && clips.loop > 0f && clips.recover > 0f
                && HasState(ParryCollapseStateName) && HasState(StunnedLoopStateName) && HasState(StunRecoverStateName);
            ParryStunClipCache[controller] = clips;
        }
        return clips.valid;
    }

    // 무너짐 직전에 걸려 있던 공격·피격 트리거가 AnyState로 기절을 끊지 않게 비운다(사망은 남긴다).
    private void ResetActionTriggers()
    {
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Trigger && parameters[i].nameHash != deathTriggerHash)
                animator.ResetTrigger(parameters[i].nameHash);
        }
    }

    private static int BaseLayerHash(string stateName)
    {
        return Animator.StringToHash("Base Layer." + stateName);
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
        StopParryStun();
        RestoreFrozenAnimatorSpeed();
        isFrozen = false; // 사망 표현이 빙결보다 우선
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

    private readonly List<AnimatorClipInfo> attackMotionClipBuffer = new List<AnimatorClipInfo>(4);
    private RuntimeAnimatorController attackMotionController;
    private readonly HashSet<AnimationClip> availableAttackMotions = new HashSet<AnimationClip>();

    public bool CanPlayAttackMotion(string triggerName, AnimationClip expectedClip)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null
            || expectedClip == null || string.IsNullOrWhiteSpace(triggerName)
            || !HasParameter(triggerName, AnimatorControllerParameterType.Trigger)
            || !HasState(ResolveAttackStateName(triggerName))) return false;
        if (attackMotionController != animator.runtimeAnimatorController)
        {
            attackMotionController = animator.runtimeAnimatorController;
            availableAttackMotions.Clear();
            foreach (var clip in attackMotionController.animationClips) if (clip != null) availableAttackMotions.Add(clip);
        }
        return availableAttackMotions.Contains(expectedClip);
    }

    // V3 reads the selected native clip, not just a same-named Animator state.
    // Prefer the incoming state when the same attack is restarted during a blend.
    public bool TryGetAttackMotionTime(string triggerName, AnimationClip expectedClip, out float normalizedTime)
    {
        normalizedTime = 0f;
        if (animator == null || !animator.isActiveAndEnabled || expectedClip == null
            || string.IsNullOrWhiteSpace(triggerName)) return false;
        string stateName = ResolveAttackStateName(triggerName);
        if (animator.IsInTransition(0) && IsMatchingState(animator.GetNextAnimatorStateInfo(0), stateName)
            && HasAttackMotion(expectedClip, true))
        { normalizedTime = animator.GetNextAnimatorStateInfo(0).normalizedTime; return true; }
        if (!IsMatchingState(animator.GetCurrentAnimatorStateInfo(0), stateName) || !HasAttackMotion(expectedClip, false)) return false;
        normalizedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        return true;
    }

    private bool HasAttackMotion(AnimationClip expected, bool next)
    {
        if (next) animator.GetNextAnimatorClipInfo(0, attackMotionClipBuffer);
        else animator.GetCurrentAnimatorClipInfo(0, attackMotionClipBuffer);
        for (int i = 0; i < attackMotionClipBuffer.Count; i++)
            if (attackMotionClipBuffer[i].clip == expected && attackMotionClipBuffer[i].weight > .001f) return true;
        return false;
    }

    public bool AllowsMovement(EnemyLocomotionMode locomotionMode)
    {
        if (isFrozen || parryStunActive)
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
        if (info.isDamageOverTime || !info.triggersOnHitEffects || info.suppressRepeatedAttackReaction)
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

        PlayResolvedHit(info);

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
            movementReaction.ExtendKnockbackReaction(resolvedKnockbackReactionDuration, true); // 넉백 경직
        else
            movementReaction.ApplyHitStun(resolvedHitStunDuration, true); // 피격 경직
    }

    // Invoked by the single hit-response owner after it allows a flinch.
    public void PlayResolvedHit(DamageInfo info)
    {
        if (isDead || isFrozen)
            return;

        if (hasDirectionalHit && animator != null)
        {
            Vector3 towardSource = info.source != null ? info.source.transform.position - transform.position : -info.direction;
            Vector3 local = transform.InverseTransformDirection(towardSource);
            local.y = 0f;
            local = local.sqrMagnitude > .0001f ? local.normalized : Vector3.forward;
            animator.SetFloat(HitXHash, local.x);
            animator.SetFloat(HitZHash, local.z);
        }
        PlayHit();
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
        if (isDead || isFrozen || parryStunActive || animator == null || string.IsNullOrWhiteSpace(triggerName))
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
            ResumeParryStunAfterFreeze();
            return;
        }

        ForceFrozenIdle();
    }

    private void ForceFrozenIdle()
    {
        StopParryStun(); // 빙결 자세가 우선. 풀리면 남은 기절을 루프부터 잇는다
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
