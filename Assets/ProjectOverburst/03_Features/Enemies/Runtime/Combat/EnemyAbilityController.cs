using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMeleeAttackController))]
public sealed partial class EnemyAbilityController : MonoBehaviour // 선택·쿨다운·실행기 라우팅 공용 표면
{
    [SerializeField] private EnemyMeleeAttackController meleeExecutor;
    [SerializeField] private EnemyAbilitySet abilitySet;
    [SerializeField] private EnemyAbilityExecutor[] executors;
    [SerializeField] private bool avoidSameAbilityTwice = true;

    private readonly Dictionary<EnemyAbilityDefinition, float> readyTimeByAbility =
        new Dictionary<EnemyAbilityDefinition, float>();
    private readonly List<AbilityCandidate> candidates = new List<AbilityCandidate>(8);
    private CombatHealth health;
    private int lastCommittedAbilityIndex = -1;
    private EnemyAbilityDefinition lastCommittedAbility;
    private float lastCommittedAt;
    private float firstImpactAt, lastImpactAt;
    private Transform strongTarget;
    private Vector3 strongAim;
    private EnemyStrongAttackWarning strongWarning;
    private EnemyActor actor;
    private EnemyThemeSpecialExecutor themeExecutor;
    private EnemyBossMaterialExecutor bossMaterialExecutor;
    private bool finalImpactDelivered;
    private int nextImpactIndex;
    private bool warningAimLocked;
    private bool bossOwnsCommittedAim;
    public void NotifyAbilityImpact(EnemyAbilityDefinition ability, int index)
    {
        if (ability != lastCommittedAbility) return;
        nextImpactIndex = Mathf.Min(index + 1, ability.HitCount - 1);
        if (index >= ability.HitCount - 1) finalImpactDelivered = true;
        if (ability.HasParryMotionWindows) strongWarning?.SetParryWindow(0f, false, index);
    }
    public bool IsOrdinaryHitProtected
    {
        get
        {
            return IsExecuting && reaction != null
                && (reaction.CanActThroughOrdinaryHit || reaction.IsStrongAttackActive);
        }
    }
    // 2026-09-30: 정예는 어떤 타격에도 공격이 끊기지 않는다. 중형 강공은 플레이어 강공 본타(PlayerAttackKind.Heavy)에만 끊긴다.
    // 강공에 딸린 원소 폭발·번개 연쇄 같은 원소 피해(Elemental)로는 끊기지 않는다.
    public bool IsProtectedFrom(in DamageInfo info)
    {
        if (!IsOrdinaryHitProtected) return false;
        return reaction.CanActThroughOrdinaryHit || (info.playerAttackKind & PlayerAttackKind.Heavy) == 0;
    }
    // 중형이 평타 경직 중에 강공만 골라 시작할 때 AI가 감싸 쓰는 구간.
    private bool strongOnlyPass;
    public void BeginStrongOnlyPass()
    {
        ResolveReferences();
        strongOnlyPass = true;
        reaction?.SetStrongStartPass(true);
    }
    public void EndStrongOnlyPass()
    {
        strongOnlyPass = false;
        reaction?.SetStrongStartPass(false);
    }
    // 2026-09-30: 핵앤슬래시 기준으로 인정 구간을 첫 타격 0.70초 전부터 연다(신호와 동일).
    public const float ParryLeadSeconds = .70f;
    // 2026-10-01: 강공 빈도 규칙. 강공을 쓴 뒤에는 강공이 아닌 공격(평타·원거리)을 중형 3번, 대형(정예) 2번 써야 다음 강공이 열린다.
    // 싸움 첫 공격은 평타 한 번(그 뒤부터 강공 가능). 경직 중 강공 시작(strongOnlyPass)도 같은 조건이라, 잠겨 있으면 그냥 경직된다.
    // 강공밖에 없는 공격 목록(은신처 패링 연습대)은 셀 평타가 없으므로 잠그지 않는다.
    public const int MediumAttacksBetweenStrong = 3;
    public const int LargeAttacksBetweenStrong = 2;
    private int attacksSinceStrong;
    private bool strongUsedInFight;
    private int RequiredAttacksBetweenStrong => !strongUsedInFight ? 1
        : reaction != null && reaction.CanActThroughOrdinaryHit ? LargeAttacksBetweenStrong : MediumAttacksBetweenStrong;
    public bool IsStrongAttackLocked => bossMaterialExecutor == null && attacksSinceStrong < RequiredAttacksBetweenStrong && HasNonStrongAbility();
    private bool IsSelectable(EnemyAbilityDefinition ability) => IsCooldownReady(ability)
        && !(ability.IsTelegraphedStrongAttack && IsStrongAttackLocked);
    private bool HasNonStrongAbility()
    {
        if (abilitySet == null) return false;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            var ability = abilitySet.GetAbility(i);
            if (ability != null && ability.IsValid && !ability.IsTelegraphedStrongAttack) return true;
        }
        return false;
    }
    private void ResetStrongCadence() { attacksSinceStrong = 0; strongUsedInFight = false; }
    private void CountCommittedAttack(EnemyAbilityDefinition ability)
    {
        if (ability.IsTelegraphedStrongAttack) { attacksSinceStrong = 0; strongUsedInFight = true; }
        else if (attacksSinceStrong < int.MaxValue) attacksSinceStrong++;
    }
    private bool strongWarningShown;
    public bool IsParryThreatTo(CombatTarget player)
    {
        EnemyAbilityDefinition ability = lastCommittedAbility;
        if (bossMaterialExecutor != null && bossMaterialExecutor.CurrentMaterial?.ability == ability)
            return bossMaterialExecutor.IsParryThreatTo(player);
        if (ability != null && ability.HasParryMotionWindows)
            return player != null && player.IsAlive && TryGetActiveParryMotionWindow(out _)
                && EnemyAttackThreatGeometry.WouldHit(actor, ability, player);
        if (player == null || ability == null || !ability.IsParryable
            || !IsExecuting || finalImpactDelivered
            || Time.time < firstImpactAt - ParryLeadSeconds || Time.time > lastImpactAt + .03f)
            return false;
        if (actor == null) actor = GetComponent<EnemyActor>();
        return EnemyAttackThreatGeometry.WouldHit(actor, ability, player);
    }
    private EnemyMovement movement;
    private EnemyMovementReaction reaction;
    private EnemyAnimationBridge animationBridge;
    private bool smoothFacingAttackCommitted;
    private Transform preparedTarget;
    private Vector3 preparedPosition;

    public bool UsesCommittedAim => movement != null && (movement.UsesSmoothCombatFacing
        || movement.Profile != null && movement.Profile.HasTurnAnimation);
    public bool HasPreparedAim(Transform target) => UsesCommittedAim && target != null && preparedTarget == target;
    public Vector3 ResolveAimPosition(Transform target) => strongTarget != null && strongTarget == target
        ? strongAim : HasPreparedAim(target) ? preparedPosition : target != null ? target.position : transform.position;
    private void Update()
    {
        ObserveAttackCompletion();
        if (smoothFacingAttackCommitted && !IsExecuting) ClearPreparedAim();
        if (strongTarget == null) return;
        if (!IsExecuting) { EndStrongWarning(); return; }
        if (bossOwnsCommittedAim || bossMaterialExecutor != null && bossMaterialExecutor.IsExecuting) return;
        if (finalImpactDelivered) { strongWarning?.Hide(); return; }
        var currentAbility = lastCommittedAbility;
        if (currentAbility != null && currentAbility.UsesPacedTimeline && animationBridge != null
            && animationBridge.TryGetAttackNormalizedTime(currentAbility.AnimatorTrigger, out float progress))
        {
            float speed = meleeExecutor != null ? meleeExecutor.AbilityAnimationSpeed : 1f;
            float elapsed = currentAbility.ResolvePacedTime(progress, speed);
            firstImpactAt = Time.time + Mathf.Max(0f,
                currentAbility.ResolvePacedTime(currentAbility.GetHitNormalizedTime(nextImpactIndex), speed) - elapsed);
            lastImpactAt = Time.time + Mathf.Max(0f, currentAbility.ResolveLastImpactTime(speed) - elapsed);
        }
        if (Time.time >= firstImpactAt - .30f) warningAimLocked = true;
        if (!warningAimLocked)
        {
            strongAim = strongTarget.position;
            Vector3 direction = strongAim - transform.position; direction.y = 0f;
            if (direction.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360f * Time.deltaTime);
        }
        if (strongWarning != null && strongWarningShown)
        {
            EnemyAbilityDefinition ability = lastCommittedAbility;
            Vector3 center = ability != null && (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge
                    || ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
                ? transform.position : EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability,
                    meleeExecutor != null && meleeExecutor.AttackPoint != null ? meleeExecutor.AttackPoint.position : transform.position);
            strongWarning.SetCenter(center);
            if (ability != null && (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge
                || ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile))
            {
                strongWarning.SetFacing(ability.ExecutionMode == EnemyAbilityExecutionMode.Charge && themeExecutor != null
                    ? themeExecutor.ChargeDirection : strongAim - transform.position);
            }
            float remaining = firstImpactAt - Time.time;
            CombatTarget player = EnemyStrongAttackWarning.PlayerTarget;
            if (actor == null) actor = GetComponent<EnemyActor>();
            bool threatens = ability != null && ability.IsParryable && player != null && player.IsAlive
                && remaining <= ParryLeadSeconds + .05f
                && EnemyAttackThreatGeometry.IsLikelyThreatTo(actor, ability, center, player.transform.position);
            if (ability != null && ability.HasParryMotionWindows)
                strongWarning.SetParryWindow(remaining, IsParryThreatTo(player), nextImpactIndex);
            else strongWarning.SetRemaining(remaining, threatens);
        }
    }
    private void EndStrongWarning()
    {
        reaction?.SetStrongAttackActive(false);
        strongTarget = null;
        bossOwnsCommittedAim = false;
        strongWarningShown = false;
        strongWarning?.Hide();
        EnemyCombatCoordinator.ReleaseStrongAttack(this);
    }
    public Vector3 PrepareAttackAim(Transform target)
    {
        ResolveReferences();
        if (UsesCommittedAim && target != null && !HasPreparedAim(target)
            && !IsExecuting && !movement.IsActionLocked && !movement.IsStatusMovementLocked
            && (animationBridge == null || !animationBridge.BlocksAttackStart)
            && (reaction == null || !reaction.BlocksAttack))
        {
            preparedTarget = target;
            preparedPosition = target.position;
        }
        return ResolveAimPosition(target);
    }
    public void ClearPreparedAim() { preparedTarget = null; smoothFacingAttackCommitted = false; }
    private void OnEnable()
    {
        ResolveReferences(); ClearPreparedAim();
        if (health != null)
        {
            if (GetComponent<EnemyHitResponseCoordinator>() == null)
                health.OnDamaged += ClearAimOnDamage;
            health.OnDead += ClearAimOnDamage;
        }
        if (reaction != null) reaction.ReactionStarted += ClearPreparedAim;
    }
    private void OnDisable()
    {
        EndStrongWarning();
        if (health != null) { health.OnDamaged -= ClearAimOnDamage; health.OnDead -= ClearAimOnDamage; }
        if (reaction != null) reaction.ReactionStarted -= ClearPreparedAim;
        ClearPreparedAim();
    }
    private void ClearAimOnDamage(CombatHealth source, DamageInfo info)
    {
        if (source.IsDead || !info.isDamageOverTime && info.triggersOnHitEffects && !info.suppressRepeatedAttackReaction) ClearPreparedAim();
    }

    public EnemyAbilitySet AbilitySet => abilitySet;
    public float AttackRange => ResolveMaximumAttackRange();
    public bool IsExecuting => ResolveIsExecuting();
    public int LastCommittedAbilityIndex => lastCommittedAbilityIndex;
    public EnemyAbilityDefinition LastCommittedAbility => lastCommittedAbility;

    public float ResolveEngagementRange(Transform target)
    {
        if (target == null || abilitySet == null || !abilitySet.IsValid) return AttackRange;
        Vector3 delta = ResolveAimPosition(target) - transform.position;
        delta.y = 0f;
        float distance = delta.magnitude;
        float hp = health != null ? health.NormalizedHp : 1f;
        float readyRange = 0f, fallbackRange = float.PositiveInfinity;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            var ability = abilitySet.GetAbility(i);
            if (ability == null || !ability.IsValid
                || hp < ability.MinimumSelfHealthNormalized || hp > ability.MaximumSelfHealthNormalized)
                continue;
            if (ability.HasWeakAttackExecution && (animationBridge == null
                || !animationBridge.CanPlayAttackMotion(ability.AnimatorTrigger, ability.WeakAttackExecution.RuntimeClip))) continue;
            float startRange = EnemyAttackThreatGeometry.ResolveStartRange(actor, ability);
            fallbackRange = Mathf.Min(fallbackRange, startRange);
            // A long-range cooldown or a projectile's minimum-range dead zone
            // must not stop an actor outside its available close attack range.
            // A strong attack locked by the cadence rule is treated like one still cooling down.
            if (distance >= ability.MinimumRange && IsSelectable(ability))
                readyRange = Mathf.Max(readyRange, startRange);
        }
        return readyRange > 0f ? readyRange : float.IsPositiveInfinity(fallbackRange) ? AttackRange : fallbackRange;
    }

    // Read-only geometry selection: cooldown, facing, prepared aim and reservations are untouched.
    public bool TryGetRangedPositioningAbility(out EnemyAbilityDefinition selected)
    {
        selected = null;
        if (abilitySet == null) return false;
        float hp = health != null ? health.NormalizedHp : 1f;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            var ability = abilitySet.GetAbility(i);
            if (ability == null || !ability.IsValid || ability.ExecutionMode != EnemyAbilityExecutionMode.Projectile
                || hp < ability.MinimumSelfHealthNormalized || hp > ability.MaximumSelfHealthNormalized
                || !(FindExecutor(ability) is EnemyThemeSpecialExecutor)) continue;
            if (selected == null || ability.Range > selected.Range) selected = ability;
        }
        return selected != null;
    }
    public bool HasRangedPositioningLine(EnemyAbilityDefinition ability, Transform target, Vector3 originPosition)
    {
        return FindExecutor(ability) is EnemyThemeSpecialExecutor executor
            && executor.HasPositioningLine(target, originPosition, target != null ? target.position : originPosition);
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public void Configure(
        EnemyAbilitySet configuredAbilitySet,
        float damageMultiplier,
        float attackSpeedMultiplier,
        float attackIntervalSeconds = 0f)
    {
        ResolveReferences();
        abilitySet = configuredAbilitySet;
        ConfigureAttackInterval(attackIntervalSeconds);
        ClearPreparedAim();
        readyTimeByAbility.Clear();
        lastCommittedAbilityIndex = -1;
        lastCommittedAbility = null;
        ResetStrongCadence();
        if (meleeExecutor == null)
            return;

        meleeExecutor.Configure(configuredAbilitySet, damageMultiplier);
        meleeExecutor.SetRuntimeAttackSpeedMultiplier(attackSpeedMultiplier);
    }

    public bool TryStart(Transform target)
    {
        ResolveReferences();
        if (target == null || ResolveIsExecuting() || !IsAttackIntervalReady)
            return false;

        PrepareAttackAim(target);

        if (abilitySet == null || !abilitySet.IsValid || executors.Length == 0)
            return meleeExecutor != null && meleeExecutor.TryStartAttack(target);

        if (!TrySelectAbility(target, out AbilityCandidate selected))
            return false;
        return CommitAbility(selected, target);
    }

    // Explicit composition uses the same eligibility, cooldown and commit route as weighted AI selection.
    public bool TryStartAbility(EnemyAbilityDefinition requested, Transform target) => TryStartAbility(requested, target, default);

    public bool TryStartAbility(EnemyAbilityDefinition requested, Transform target, in EnemyAbilityStartContext startContext)
    {
        ResolveReferences();
        if (requested == null || target == null || ResolveIsExecuting() || !IsAttackIntervalReady || abilitySet == null || !abilitySet.IsValid) return false;
        if (!startContext.IsPrepared) PrepareAttackAim(target);
        else if (animationBridge == null || !animationBridge.CanCommitPreparedAttack(startContext)) return false;
        Vector3 delta = (startContext.IsPrepared || startContext.KeepCurrentFacing ? startContext.AimPosition : ResolveAimPosition(target)) - transform.position; delta.y = 0f;
        float hp = health != null ? health.NormalizedHp : 1f;
        for (int index = 0; index < abilitySet.Count; index++)
        {
            if (abilitySet.GetAbility(index) != requested) continue;
            if (!TryResolveAvailableCandidate(requested, target, delta.magnitude, hp, out var executor, startContext)) return false;
            return CommitAbility(new AbilityCandidate(requested, executor, index), target, startContext);
        }
        return false;
    }

    private bool CommitAbility(AbilityCandidate selected, Transform target, EnemyAbilityStartContext startContext = default)
    {
        float speed = meleeExecutor != null ? meleeExecutor.AbilityAnimationSpeed : 1f;
        float first = selected.Ability.ResolveFirstImpactTime(speed);
        float duration = selected.Ability.ResolveExecutionDuration(speed);
        if (selected.Ability.IsTelegraphedStrongAttack
            && !EnemyCombatCoordinator.TryReserveStrongAttack(this, startContext.IsPrepared || startContext.KeepCurrentFacing ? startContext.AimPosition : ResolveAimPosition(target), Time.time + first, Time.time + duration)) return false;
        if (!selected.Executor.TryStart(selected.Ability, selected.Index, target, startContext))
        {
            EnemyCombatCoordinator.ReleaseStrongAttack(this);
            return false;
        }

        readyTimeByAbility[selected.Ability] =
            Time.time + Mathf.Max(selected.Ability.Cooldown, duration);
        lastCommittedAbilityIndex = selected.Index;
        lastCommittedAbility = selected.Ability;
        lastCommittedAt = Time.time;
        committedAttackRunning = true;
        smoothFacingAttackCommitted = movement != null && movement.UsesSmoothCombatFacing;
        CountCommittedAttack(selected.Ability);
        if (selected.Executor is EnemyBossMaterialExecutor) GetComponent<EnemyBossCombatDirector>()?.NotifyCommitted(selected.Ability);
        if (selected.Ability.IsTelegraphedStrongAttack) reaction?.SetStrongAttackActive(true);
        firstImpactAt = Time.time + first;
        lastImpactAt = Time.time + selected.Ability.ResolveLastImpactTime(speed);
        finalImpactDelivered = false;
        nextImpactIndex = 0;
        warningAimLocked = movement != null && movement.UsesSmoothCombatFacing;
        // 보스 장판과 조준은 재료 실행기가 소유한다. 공용 추적이 확정 방향을 덮어쓰지 않는다.
        bossOwnsCommittedAim = selected.Executor is EnemyBossMaterialExecutor || selected.Executor is EnemyBossCompositePatternExecutor;
        strongWarningShown = selected.Ability.IsMeleeStrongAttack && !bossOwnsCommittedAim;
        if (selected.Ability.IsTelegraphedAttack)
        {
            strongTarget = target; strongAim = startContext.IsPrepared || startContext.KeepCurrentFacing ? startContext.AimPosition : ResolveAimPosition(target);
        }
        if (!strongWarningShown) strongWarning?.Hide();
        else
        {
            if (strongWarning == null) strongWarning = gameObject.AddComponent<EnemyStrongAttackWarning>();
            if (actor == null) actor = GetComponent<EnemyActor>();
            if (selected.Ability.TryResolveAttackCue(actor, out var cueSocket, out var cuePosition))
                strongWarning.SetAttackCue(cueSocket, Vector3.zero, cuePosition, selected.Ability.ParryCueScale);
            else strongWarning.SetAttackCue(null, Vector3.zero);
            strongWarning.Show(EnemyAttackThreatGeometry.ResolveRadius(actor, selected.Ability),
                selected.Ability.IsParryable,
                EnemyAttackThreatGeometry.ResolveHitAngle(actor, selected.Ability),
                selected.Ability.ExecutionMode == EnemyAbilityExecutionMode.Charge,
                true, first, EnemyAttackThreatGeometry.ChargeHalfWidth,
                EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor, selected.Ability),
                deferParrySignal: selected.Ability.HasParryMotionWindows);
        }
        return true;
    }

    // Readiness is checked before reserving an attack turn. A facing/animation
    // refusal must not incur recovery or the coordinator's re-entry cooldown.
    public bool HasAvailableAbility(Transform target)
    {
        ResolveReferences();
        if (target == null || ResolveIsExecuting() || !IsAttackIntervalReady) return false;
        if (abilitySet == null || !abilitySet.IsValid || executors.Length == 0)
            return true; // Preserve the legacy melee-only start path.
        Vector3 delta = ResolveAimPosition(target) - transform.position; delta.y = 0f;
        float distance = delta.magnitude;
        float hp = health != null ? health.NormalizedHp : 1f;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            var ability = abilitySet.GetAbility(i);
            if (TryResolveAvailableCandidate(ability, target, distance, hp, out _)) return true;
        }
        // If the completed aim cannot be used (wall, cooldown, range), release it
        // before the next decision. Do not invalidate a turn still in progress.
        if (HasPreparedAim(target) && movement.IsFacingForAttack(preparedPosition)) ClearPreparedAim();
        return false;
    }

    public bool IsCooldownReady(EnemyAbilityDefinition ability)
    {
        return ability != null
            && (!readyTimeByAbility.TryGetValue(ability, out float readyTime)
                || Time.time >= readyTime);
    }

    public float GetRemainingCooldown(EnemyAbilityDefinition ability)
    {
        if (ability == null
            || !readyTimeByAbility.TryGetValue(ability, out float readyTime))
        {
            return 0f;
        }

        return Mathf.Max(0f, readyTime - Time.time);
    }

    public void Cancel()
    {
        EndStrongWarning();
        ClearPreparedAim();
        ResolveReferences();
        for (int i = 0; i < executors.Length; i++)
            executors[i]?.Cancel();
        if (executors.Length == 0)
            meleeExecutor?.CancelAttack();
        ObserveAttackCompletion();
    }

    public void ClearTarget()
    {
        ClearPreparedAim();
        meleeExecutor?.ClearTarget();
    }

    public void ResetForReuse()
    {
        EndStrongWarning();
        ClearPreparedAim();
        ResolveReferences();
        for (int i = 0; i < executors.Length; i++)
            executors[i]?.ResetForReuse();
        if (executors.Length == 0 && meleeExecutor != null)
        {
            meleeExecutor.CancelAttack();
            meleeExecutor.ClearTarget();
            meleeExecutor.SetStatusActionSpeedMultiplier(1f);
            meleeExecutor.SetRuntimeAttackSpeedMultiplier(1f);
        }

        readyTimeByAbility.Clear();
        candidates.Clear();
        ConfigureAttackInterval(0f);
        lastCommittedAbilityIndex = -1;
        lastCommittedAbility = null;
        ResetStrongCadence();
    }

    private bool TrySelectAbility(
        Transform target,
        out AbilityCandidate selected)
    {
        selected = default;
        candidates.Clear();
        Vector3 delta = ResolveAimPosition(target) - transform.position;
        delta.y = 0f;
        float distance = delta.magnitude;
        float selfHealth = health != null ? health.NormalizedHp : 1f;
        bool hasStationaryWeak = false;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            var ability = abilitySet.GetAbility(i);
            if (!TryResolveAvailableCandidate(ability, target, distance, selfHealth, out var executor)) continue;
            candidates.Add(new AbilityCandidate(ability, executor, i));
            hasStationaryWeak |= IsStationaryWeakMelee(ability);
        }
        // Choose the eligible motion family before Priority/Weight. Otherwise a
        // high-priority advance can erase a ready stationary attack while close.
        int highestPriority = int.MinValue;
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            var ability = candidates[i].Ability;
            if (hasStationaryWeak && ability.HasWeakAttackExecution && !ability.IsTelegraphedStrongAttack
                && ability.WeakAttackExecution.UsesAdvance)
            { candidates.RemoveAt(i); continue; }
            highestPriority = Mathf.Max(highestPriority, ability.Priority);
        }
        for (int i = candidates.Count - 1; i >= 0; i--)
            if (candidates[i].Ability.Priority != highestPriority) candidates.RemoveAt(i);

        if (candidates.Count == 0)
            return false;

        bool excludeLast = avoidSameAbilityTwice
            && candidates.Count > 1
            && lastCommittedAbility != null;
        float totalWeight = CalculateWeight(excludeLast);
        if (totalWeight <= 0f && excludeLast)
        {
            excludeLast = false;
            totalWeight = CalculateWeight(false);
        }
        if (totalWeight <= 0f)
            return false;

        float cursor = Random.value * totalWeight;
        for (int i = 0; i < candidates.Count; i++)
        {
            AbilityCandidate candidate = candidates[i];
            if (excludeLast && candidate.Ability == lastCommittedAbility)
                continue;

            cursor -= candidate.Ability.Weight;
            if (cursor > 0f)
                continue;
            selected = candidate;
            return true;
        }

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            AbilityCandidate candidate = candidates[i];
            if (!excludeLast || candidate.Ability != lastCommittedAbility)
            {
                selected = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryResolveAvailableCandidate(EnemyAbilityDefinition ability, Transform target,
        float distance, float selfHealth, out EnemyAbilityExecutor executor, EnemyAbilityStartContext startContext = default)
    {
        executor = null;
        if (ability == null || !ability.IsValid
            || !EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability, distance, selfHealth)
            || !IsSelectable(ability) || strongOnlyPass && !ability.IsTelegraphedStrongAttack) return false;
        executor = FindExecutor(ability);
        return executor != null && executor.CanStart(ability, target, startContext);
    }

    private static bool IsStationaryWeakMelee(EnemyAbilityDefinition ability)
        => ability.HasWeakAttackExecution && !ability.IsTelegraphedStrongAttack
            && ability.WeakAttackExecution.IsStationaryMotion
            && (ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc
                || ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget
                || ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam);

    private float CalculateWeight(bool excludeLast)
    {
        float total = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            EnemyAbilityDefinition ability = candidates[i].Ability;
            if (!excludeLast || ability != lastCommittedAbility)
                total += ability.Weight;
        }
        return total;
    }

    private EnemyAbilityExecutor FindExecutor(EnemyAbilityDefinition ability)
    {
        for (int i = 0; i < executors.Length; i++)
        {
            EnemyAbilityExecutor executor = executors[i];
            if (executor != null && executor.Supports(ability))
                return executor;
        }
        return null;
    }

    private bool ResolveIsExecuting()
    {
        if (executors != null)
        {
            for (int i = 0; i < executors.Length; i++)
            {
                if (executors[i] != null && executors[i].IsExecuting)
                    return true;
            }
        }

        return (executors == null || executors.Length == 0)
            && meleeExecutor != null
            && meleeExecutor.IsAttacking;
    }

    private float ResolveMaximumAttackRange()
    {
        if (abilitySet == null || !abilitySet.IsValid)
            return meleeExecutor != null ? meleeExecutor.AttackRange : 0f;

        float maximum = 0f;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition ability = abilitySet.GetAbility(i);
            if (ability != null && ability.IsValid)
                maximum = Mathf.Max(maximum, EnemyAttackThreatGeometry.ResolveStartRange(actor, ability));
        }
        return maximum;
    }

    private void ResolveReferences()
    {
        if (actor == null) actor = GetComponent<EnemyActor>();
        if (themeExecutor == null) themeExecutor = GetComponent<EnemyThemeSpecialExecutor>();
        if (bossMaterialExecutor == null) bossMaterialExecutor = GetComponent<EnemyBossMaterialExecutor>();
        if (movement == null) movement = GetComponent<EnemyMovement>();
        if (reaction == null) reaction = GetComponent<EnemyMovementReaction>();
        if (animationBridge == null) animationBridge = GetComponent<EnemyAnimationBridge>();
        if (meleeExecutor == null)
            meleeExecutor = GetComponent<EnemyMeleeAttackController>();
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (executors == null || executors.Length == 0)
            executors = GetComponents<EnemyAbilityExecutor>();
    }

    private readonly struct AbilityCandidate
    {
        public AbilityCandidate(
            EnemyAbilityDefinition ability,
            EnemyAbilityExecutor executor,
            int index)
        {
            Ability = ability;
            Executor = executor;
            Index = index;
        }

        public EnemyAbilityDefinition Ability { get; }
        public EnemyAbilityExecutor Executor { get; }
        public int Index { get; }
    }
}
