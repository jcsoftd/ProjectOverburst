using System.Collections.Generic;
using UnityEngine;

// 2026-10-01 대표 보스 지휘. 패턴 선택 트리·그로기·패링 경직·페이즈 전환·패링 불가 표시를 소유한다.
// 이동·회전·공격 시작은 기존 AI/EnemyAbilityController 한 경로만 쓰고, 여기서는 허용할 패턴과 경직만 정한다.
// 우선순위: 사망 > 그로기 > 패링 경직 > 페이즈 전환(대기 후 실행) > 공격 선택. 체력은 어떤 상태에서도 잠그지 않는다.
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyBossPhaseController))]
public sealed class EnemyBossCombatDirector : MonoBehaviour
{
    [SerializeField] private EnemyBossCombatProfile profile;

    private EnemyActor actor;
    private CombatHealth health;
    private EnemyMovementReaction reaction;
    private EnemyAbilityController abilities;
    private EnemyBossPhaseController phases;
    private AudioSource voice;
    private EnemyStrongAttackWarning dangerCue;
    private ParticleSystem[] dangerSystems;
    private EnemyBossNode tree;
    private readonly EnemyBossCandidateBuffer candidates = new EnemyBossCandidateBuffer();
    private readonly HashSet<long> heavySequences = new HashSet<long>();
    private readonly List<string> commitLog = new List<string>(64);

    private Transform decisionTarget;
    private float decisionDistance, decisionHealth, nextDecisionAt, repeatAllowedAt;
    private EnemyBossCombatProfile.Pattern decision, lastPattern, lastStrongPattern, pendingFollowUp;
    private float followUpDeadline;
    private float groggy, lastGainAt, groggyUntil, protectedUntil, groggyAnimAt, transitionUntil;
    private bool groggyActive, groggyAnimPlayed, transitionPending, transitionActive;
    private EnemyAbilityDefinition committedAbility, cueAbility;
    private float committedAt, committedImpactAt;
    private bool subscribed;
    private int highestTransitionPhase;

    public EnemyBossCombatProfile Profile => profile;
    public float Groggy01 => groggyActive ? Mathf.Clamp01((groggyUntil - Time.time) / Mathf.Max(.01f, profile.groggyDuration))
        : profile != null ? Mathf.Clamp01(groggy / profile.groggyMax) : 0f;
    public bool IsGroggy => groggyActive;
    public bool IsProtected => Time.time < protectedUntil;
    public bool IsTransitioning => transitionActive;
    public bool IsTransitionPending => transitionPending;
    public bool IsParryRecoiling => !groggyActive && reaction != null && reaction.IsParryStunned;
    public bool IsLockedOut => health == null || health.IsDead || groggyActive || transitionActive
        || (reaction != null && reaction.IsParryStunned);
    public bool CanReceiveParry => isActiveAndEnabled && health != null && !health.IsDead && !transitionActive && !groggyActive;
    public string CurrentDecisionId => decision != null ? decision.patternId : string.Empty;
    public bool IsDangerCueVisible => dangerCue != null && dangerCue.IsVisible;
    public float DangerCueRadius { get; private set; }
    public int ParryCount { get; private set; }
    public int GroggyCount { get; private set; }
    public int TransitionCount { get; private set; }
    public int DangerCueCount { get; private set; }
    public IReadOnlyList<string> CommitLog => commitLog;
    // 보스 HUD는 교전이 시작된 뒤에만 띄운다(지도 반대편에 서 있는 동안 표시하지 않는다).
    public bool IsEngaged => health != null && (health.NormalizedHp < .999f
        || (actor != null && actor.AI != null && actor.AI.IsAggroActive));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 검증 전용: 다음 결정을 이 패턴으로 선호한다. 거리·쿨다운·강공 빈도 조건은 그대로 검사하고
    // 실제 시작은 기존 AI → EnemyAbilityController 경로가 한다.
    public bool PreferNextPattern(string patternId)
    {
        var pattern = profile != null ? profile.Find(patternId) : null;
        if (pattern == null) return false;
        pendingFollowUp = pattern; followUpDeadline = float.MaxValue; decision = null; nextDecisionAt = 0f;
        return true;
    }
#endif

    public void Configure(EnemyBossCombatProfile combatProfile) { profile = combatProfile; }

    // PlayerParryController가 보스 등급 중 이 컴포넌트가 받아들이는 보스만 패링 후보로 본다.
    public static bool AcceptsParry(Component enemy)
    {
        return enemy != null && enemy.TryGetComponent(out EnemyBossCombatDirector director) && director.CanReceiveParry;
    }

    private void Awake() { Resolve(); BuildTree(); }

    private void OnEnable()
    {
        Resolve();
        ResetState();
        if (subscribed || health == null) return;
        health.OnDamageResolved += HandleDamage;
        health.OnDead += HandleDead;
        health.OnReset += HandleReset;
        if (phases != null) phases.PhaseChanged += HandlePhaseChanged;
        subscribed = true;
    }

    private void OnDisable()
    {
        if (subscribed && health != null)
        {
            health.OnDamageResolved -= HandleDamage;
            health.OnDead -= HandleDead;
            health.OnReset -= HandleReset;
            if (phases != null) phases.PhaseChanged -= HandlePhaseChanged;
        }
        subscribed = false;
        ResetState();
    }

    private void Resolve()
    {
        if (actor == null) actor = GetComponent<EnemyActor>();
        if (health == null) health = GetComponent<CombatHealth>();
        if (reaction == null) reaction = GetComponent<EnemyMovementReaction>();
        if (abilities == null) abilities = GetComponent<EnemyAbilityController>();
        if (phases == null) phases = GetComponent<EnemyBossPhaseController>();
    }

    private void HandleReset(CombatHealth source) => ResetState();

    // 풀 반환·재대여·사망에서 타이머·게이지·예약·표시를 모두 비운다.
    private void ResetState()
    {
        groggy = 0f; lastGainAt = 0f; groggyUntil = 0f; protectedUntil = 0f; transitionUntil = 0f;
        groggyActive = groggyAnimPlayed = transitionPending = transitionActive = false;
        highestTransitionPhase = 0;
        decision = lastPattern = lastStrongPattern = pendingFollowUp = null; decisionTarget = null;
        nextDecisionAt = repeatAllowedAt = followUpDeadline = 0f;
        committedAbility = cueAbility = null;
        heavySequences.Clear(); commitLog.Clear();
        ParryCount = GroggyCount = TransitionCount = DangerCueCount = 0;
        HideDangerCue();
    }

    // ---------- 패턴 선택 ----------
    private void BuildTree()
    {
        tree = new EnemyBossSelector(
            new EnemyBossSequence(new EnemyBossCondition(() => IsLockedOut || profile == null), new EnemyBossAction(ChooseNone)),
            new EnemyBossSequence(new EnemyBossCondition(HasValidFollowUp), new EnemyBossAction(ChooseFollowUp)),
            new EnemyBossSequence(new EnemyBossCondition(CollectStrongSlot), new EnemyBossAction(ChooseWeighted)),
            new EnemyBossSequence(new EnemyBossCondition(CollectCandidates), new EnemyBossAction(ChooseWeighted)),
            new EnemyBossAction(ChooseNone));
    }

    public bool OwnsPattern(EnemyAbilityDefinition ability) => profile != null && profile.Find(ability) != null;

    public bool Allows(EnemyAbilityDefinition ability, Transform target)
    {
        if (ability == null || target == null || profile == null) return false;
        if (decision == null || target != decisionTarget || Time.time >= nextDecisionAt || !PassesConditions(decision, target, true))
            Decide(target);
        return decision != null && decision.ability == ability;
    }

    private void Decide(Transform target)
    {
        decisionTarget = target;
        Vector3 aim = abilities != null ? abilities.ResolveAimPosition(target) : target.position;
        Vector3 delta = aim - transform.position; delta.y = 0f;
        decisionDistance = delta.magnitude;
        decisionHealth = health != null ? health.NormalizedHp : 1f;
        nextDecisionAt = Time.time + profile.decisionInterval;
        tree.Tick();
    }

    private bool ChooseNone() { decision = null; return true; }

    private bool HasValidFollowUp()
    {
        if (pendingFollowUp == null) return false;
        if (Time.time > followUpDeadline) { pendingFollowUp = null; return false; }
        return PassesConditions(pendingFollowUp, decisionTarget, false);
    }

    private bool ChooseFollowUp() { decision = pendingFollowUp; return true; }

    private bool CollectCandidates()
    {
        candidates.Clear();
        EnemyBossCombatProfile.Pattern repeatOnly = null;
        int phase = phases != null ? Mathf.Max(0, phases.CurrentPhaseIndex) : 0;
        foreach (var pattern in profile.patterns)
        {
            if (pattern == null || pattern.ability == null || !pattern.AllowsPhase(phase)) continue;
            if (!PassesConditions(pattern, decisionTarget, false)) continue;
            if (pattern == lastPattern) { repeatOnly = pattern; continue; }
            candidates.Add(pattern);
        }
        // 같은 패턴 연속 금지. 쓸 수 있는 것이 그것뿐이면 잠시 기다린 뒤 허용해 교착을 막는다.
        if (candidates.Items.Count == 0 && repeatOnly != null)
        {
            if (repeatAllowedAt <= 0f) repeatAllowedAt = Time.time + profile.repeatDelay;
            if (Time.time >= repeatAllowedAt) candidates.Add(repeatOnly);
        }
        else repeatAllowedAt = 0f;
        return candidates.Items.Count > 0;
    }

    private bool ChooseWeighted() { decision = candidates.Pick(Random.value); return decision != null; }

    // 강공 빈도 잠금이 풀린 순간(평타·광역 N회 뒤)에는 쓸 수 있는 패링 가능 강공을 먼저 쓴다.
    // 가중 추첨에 맡기면 강공 칸이 비강공에 밀려 패링 기회가 드물어지고 그로기까지 이어지지 않는다.
    // 거리상 쓸 수 있는 강공이 여럿이면 직전 강공과 다른 것을 고른다. 같은 패턴 연속 금지는 그대로다.
    private bool CollectStrongSlot()
    {
        candidates.Clear();
        if (abilities == null || abilities.IsStrongAttackLocked) return false;
        int phase = phases != null ? Mathf.Max(0, phases.CurrentPhaseIndex) : 0;
        EnemyBossCombatProfile.Pattern sameAsLastStrong = null;
        foreach (var pattern in profile.patterns)
        {
            if (pattern == null || pattern.ability == null || !pattern.ability.IsTelegraphedStrongAttack) continue;
            if (!pattern.AllowsPhase(phase) || pattern == lastPattern || !PassesConditions(pattern, decisionTarget, false)) continue;
            if (pattern == lastStrongPattern) { sameAsLastStrong = pattern; continue; }
            candidates.Add(pattern);
        }
        if (candidates.Items.Count == 0 && sameAsLastStrong != null) candidates.Add(sameAsLastStrong);
        return candidates.Items.Count > 0;
    }

    // 거리·체력 조건·쿨다운·전역 강공 빈도 잠금을 기존 계산으로 확인한다(예고·판정과 같은 원본).
    private bool PassesConditions(EnemyBossCombatProfile.Pattern pattern, Transform target, bool refreshDistance)
    {
        if (pattern == null || pattern.ability == null || abilities == null || target == null) return false;
        int phase = phases != null ? Mathf.Max(0, phases.CurrentPhaseIndex) : 0;
        if (!pattern.AllowsPhase(phase) && pattern != pendingFollowUp) return false;
        float distance = decisionDistance;
        if (refreshDistance)
        {
            Vector3 delta = abilities.ResolveAimPosition(target) - transform.position; delta.y = 0f;
            distance = delta.magnitude;
        }
        var ability = pattern.ability;
        return EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability, distance, health != null ? health.NormalizedHp : 1f)
            && abilities.IsCooldownReady(ability)
            && !(ability.IsTelegraphedStrongAttack && abilities.IsStrongAttackLocked);
    }

    // EnemyBossPatternExecutor가 실제 시작에 성공했을 때만 부른다.
    public void NotifyCommitted(EnemyAbilityDefinition ability)
    {
        var pattern = profile != null ? profile.Find(ability) : null;
        if (pattern == null) return;
        lastPattern = pattern;
        if (ability.IsTelegraphedStrongAttack) lastStrongPattern = pattern;
        repeatAllowedAt = 0f;
        decision = null; nextDecisionAt = 0f;
        if (pendingFollowUp == pattern) pendingFollowUp = null;
        int phase = phases != null ? phases.CurrentPhaseIndex : 0;
        var follow = phase >= 1 ? profile.Find(pattern.phaseTwoFollowUp) : null;
        pendingFollowUp = follow;
        followUpDeadline = float.MaxValue;
        committedAbility = ability;
        committedAt = Time.time;
        float speed = actor != null && actor.Melee != null ? actor.Melee.AbilityAnimationSpeed : 1f;
        committedImpactAt = committedAt + ability.ResolveFirstImpactTime(speed);
        if (commitLog.Count < 256) commitLog.Add(pattern.patternId + "@" + Mathf.Max(0, phase));
        if (pattern.unparryableDangerCue) ShowDangerCue(ability);
    }

    // ---------- 패링·그로기 ----------
    // PlayerParryController.CancelAndStun이 공격 취소 직후 부른다. 기존 패링 성공 연출·에너지 환급은 그대로다.
    public void NotifyParried() => NotifyParried(ParryGrade.Perfect);

    public void NotifyParried(ParryGrade grade)
    {
        if (profile == null || reaction == null || health == null || health.IsDead) return;
        ParryCount++;
        pendingFollowUp = null;
        HideDangerCue();
        if (grade == ParryGrade.Perfect) reaction.ApplyBossStun(profile.parryRecoil);
        float fraction = grade == ParryGrade.Perfect ? 1f : grade == ParryGrade.Normal ? 20f / 35f : 5f / 35f;
        AddGroggy(profile.parryGain * fraction);
    }

    private void HandleDamage(CombatHealth source, DamageInfo info, float actualDamage, bool fatal)
    {
        if (fatal || profile == null || actualDamage <= 0f || info.isDamageOverTime || !info.triggersOnHitEffects) return;
        if ((info.playerAttackKind & PlayerAttackKind.Heavy) == 0) return;
        long key = ((long)(info.source != null ? info.source.GetInstanceID() : 0) << 32) ^ (uint)info.sourceAttackSequenceId;
        if (info.sourceAttackSequenceId != 0 && !heavySequences.Add(key)) return; // 한 강공의 여러 적중은 한 번만
        if (heavySequences.Count > 64) heavySequences.Clear();
        AddGroggy(profile.heavyHitGain);
    }

    private void AddGroggy(float amount)
    {
        if (amount <= 0f || groggyActive || transitionActive || Time.time < protectedUntil || health.IsDead) return;
        groggy = Mathf.Min(profile.groggyMax, groggy + amount);
        lastGainAt = Time.time;
        if (groggy >= profile.groggyMax) BeginGroggy();
    }

    private void BeginGroggy()
    {
        groggyActive = true; GroggyCount++;
        groggy = profile.groggyMax;
        groggyUntil = Time.time + profile.groggyDuration;
        groggyAnimAt = Time.time + .2f; groggyAnimPlayed = false; // 패링 되감기 연출(0.15초)이 끝난 뒤 비틀거림
        pendingFollowUp = null;
        HideDangerCue();
        abilities?.Cancel();
        var temporary = actor != null ? actor.GetComponent<CrustaspikanTemporaryReaction>() : null;
        if (temporary == null || !temporary.TryPlayGroggy(profile.groggyDuration)) reaction.ApplyBossStun(profile.groggyDuration);
        if (actor != null) EnemyParryStunIndicator.Show(actor);
        PlaySfx(profile.groggyClip);
    }

    private void EndGroggy()
    {
        groggyActive = false;
        groggy = 0f;
        protectedUntil = Time.time + profile.groggyProtection;
        CrossFade("Locomotion", .25f);
    }

    // ---------- 페이즈 전환 ----------
    private void HandlePhaseChanged(EnemyBossPhaseController boss, int previous, int current)
    {
        if (current > previous && current > highestTransitionPhase) { transitionPending = true; highestTransitionPhase = current; }
        var behavior = profile != null ? profile.BehaviorForPhase(current) : null;
        if (behavior != null && actor != null && actor.AI != null) actor.AI.SetBehaviorProfile(behavior);
    }

    private void BeginTransition()
    {
        transitionPending = false; transitionActive = true; TransitionCount++;
        transitionUntil = Time.time + profile.phaseTransitionDuration;
        pendingFollowUp = null; lastPattern = null;
        HideDangerCue();
        abilities?.Cancel();
        reaction.ApplyHitStun(profile.phaseTransitionDuration, false); // 강제 반응: 공격 불가. 피해 보너스·기절 표시는 없다
        CrossFade(profile.transitionState, .12f);
        PlaySfx(profile.roarClip);
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        groggyActive = transitionActive = transitionPending = false;
        pendingFollowUp = null; decision = null;
        HideDangerCue();
    }

    private void Update()
    {
        if (profile == null || health == null || health.IsDead) return;
        float now = Time.time;
        if (groggyActive)
        {
            if (!groggyAnimPlayed && now >= groggyAnimAt) { groggyAnimPlayed = true; CrossFade(profile.groggyState, .12f); }
            if (now >= groggyUntil) EndGroggy();
        }
        else if (groggy > 0f && now - lastGainAt > profile.decayDelay)
            groggy = Mathf.Max(0f, groggy - profile.decayPerSecond * Time.deltaTime);

        if (transitionActive && now >= transitionUntil) transitionActive = false;
        if (transitionPending && !groggyActive && (reaction == null || !reaction.IsParryStunned)) BeginTransition();

        bool executing = abilities != null && abilities.IsExecuting;
        if (pendingFollowUp != null && executing && abilities.LastCommittedAbility == committedAbility)
            followUpDeadline = now + profile.followUpWindow; // 창은 앞 공격이 끝난 순간부터 센다
        UpdateDangerCue(now, executing);
    }

    // ---------- 패링 불가 표시 ----------
    private void ShowDangerCue(EnemyAbilityDefinition ability)
    {
        if (dangerCue == null)
        {
            var host = new GameObject("Boss unparryable cue");
            host.transform.SetParent(transform, false);
            dangerCue = host.AddComponent<EnemyStrongAttackWarning>();
        }
        float lead = Mathf.Max(.1f, committedImpactAt - Time.time);
        DangerCueRadius = EnemyAttackThreatGeometry.ResolveRadius(actor, ability); // 피해 판정과 같은 반경
        dangerCue.Show(DangerCueRadius, false, EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability), false, true, lead);
        dangerCue.SetCenter(transform.position);
        dangerSystems = dangerCue.GetComponentsInChildren<ParticleSystem>(true);
        if (!dangerCue.UsesStandardIndicator) foreach (var system in dangerSystems)
        {
            var main = system.main;
            var color = main.startColor;
            Color tint = profile.dangerTint;
            if (color.mode == ParticleSystemGradientMode.Color)
                color.color = new Color(tint.r, tint.g, tint.b, color.color.a);
            else if (color.mode == ParticleSystemGradientMode.TwoColors)
            {
                color.colorMin = new Color(tint.r, tint.g, tint.b, color.colorMin.a);
                color.colorMax = new Color(tint.r, tint.g, tint.b, color.colorMax.a);
            }
            else color = new ParticleSystem.MinMaxGradient(tint);
            main.startColor = color;
        }
        cueAbility = ability; DangerCueCount++;
        PlaySfx(profile.dangerClip);
    }

    private void UpdateDangerCue(float now, bool executing)
    {
        if (cueAbility == null) return;
        bool same = executing && abilities.LastCommittedAbility == cueAbility;
        float remaining = committedImpactAt - now;
        if (!same || remaining < -.08f) { HideDangerCue(); return; }
        dangerCue.SetCenter(transform.position);
        dangerCue.SetRemaining(remaining, false);
    }

    private void HideDangerCue()
    {
        cueAbility = null;
        if (dangerCue != null) dangerCue.Hide();
    }

    // ---------- 공용 ----------
    private void CrossFade(string state, float seconds)
    {
        if (actor != null && actor.AnimationBridge != null && actor.AnimationBridge.UsesOwnedMotion)
        {
            if (actor.GetComponent<CrustaspikanTemporaryReaction>()?.BlocksActions != true)
                actor.AnimationBridge.TryPlayOwnedStatePresentation(state, this);
            return;
        }
        var animator = actor != null ? actor.Animator : null;
        if (animator == null || string.IsNullOrEmpty(state) || !animator.isActiveAndEnabled) return;
        int hash = Animator.StringToHash("Base Layer." + state);
        if (!animator.HasState(0, hash)) return;
        animator.CrossFadeInFixedTime(hash, seconds, 0, 0f);
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null) return;
        if (voice == null)
        {
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false; voice.spatialBlend = .6f; voice.minDistance = 6f; voice.maxDistance = 40f;
        }
        voice.PlayOneShot(clip, profile.sfxVolume);
    }
}
