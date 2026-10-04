using System;
using System.Collections.Generic;
using UnityEngine;

// 전투 지휘만 소유한다. 접촉 판정·피해·패링 경고는 기존 재료 실행기로 보낸다.
public sealed class CrustaspikanEncounterBrain : IDisposable
{
    public EnemyActor Actor { get; private set; }
    public CrustaspikanStyleObserver Observer { get; private set; }
    public int Phase { get; private set; } = 1;
    public float Poise { get; private set; }
    public int GroggyCount { get; private set; }
    public int PatternCount { get; private set; }
    public int DodgeCount { get; private set; }
    public int BackHitCount { get; private set; }
    public bool IsGroggy => Time.time < groggyUntil;
    public bool IsTransitioning => transitionStarted;
    public string State { get; private set; } = "준비";
    public string LastPattern { get; private set; } = "";
    public EnemyBossMaterialCollection RuntimeMaterials => runtimeMaterials;
    public EnemyBossCompositePatternExecutor Composite => composite;
    public bool ReviewMode { get; set; }
    private readonly CrustaspikanEncounter encounter;
    private readonly CrustaspikanEncounterSettings settings;
    private readonly PlayerActorRuntime player;
    private readonly EnemyBossMaterialExecutor executor;
    private readonly EnemyBossCompositePatternExecutor composite;
    private readonly EnemyBossCompositePatternSet originalComposite;
    private readonly EnemyBossCombatDirector parryDirector;
    private readonly EnemyMovementReaction reaction;
    private readonly EnemyBossCombatProfile originalProfile;
    private readonly EnemyBossMaterialCollection originalMaterials;
    private readonly EnemyAbilitySet originalAbilities;
    private readonly bool originalAI, originalPhase;
    private readonly EnemyTargetHpReporter hpReporter;
    private readonly EnemyOverheadHpBar overhead;
    private readonly bool originalReporter, originalOverhead;
    private readonly uint leaseVersion;
    private readonly List<UnityEngine.Object> clones = new List<UnityEngine.Object>();
    private readonly Dictionary<string, EnemyBossAttackMaterial> attacks = new Dictionary<string, EnemyBossAttackMaterial>();
    private readonly Dictionary<string, float> cooldowns = new Dictionary<string, float>();
    private readonly HashSet<long> hitSequences = new HashSet<long>();
    private readonly List<CrustaspikanEncounterSettings.Pattern> candidates = new List<CrustaspikanEncounterSettings.Pattern>();
    private CrustaspikanNode tree;
    private CrustaspikanEncounterSettings.Pattern current;
    private int stepIndex, lastParry;
    private bool stepStarted, transitionStarted, disposed;
    private float stepStartedAt, stepUntil, readyAt, groggyUntil, protectionUntil, lastPoiseAt, nextDodgeAt, dashAttackUntil;
    private Vector3 moveDestination;
    private string lastFamily = "";
    private int consecutiveFamily;
    private float transitionDeadline;
    private float stepAttemptAt;
    private MeleeRuntime melee;
    private OverburstElementEnergy energy;

    public CrustaspikanEncounterBrain(CrustaspikanEncounter encounter, EnemyActor actor, PlayerActorRuntime player)
    {
        this.encounter = encounter; settings = encounter.Settings; Actor = actor; this.player = player;
        executor = actor.GetComponent<EnemyBossMaterialExecutor>();
        composite = actor.GetComponent<EnemyBossCompositePatternExecutor>(); originalComposite = composite.Patterns;
        parryDirector = actor.GetComponent<EnemyBossCombatDirector>(); reaction = actor.GetComponent<EnemyMovementReaction>();
        originalMaterials = executor.Collection; originalAbilities = actor.AbilityController.AbilitySet;
        originalProfile = parryDirector.Profile; originalAI = actor.AI.enabled;
        originalPhase = actor.BossPhaseController.enabled;
        leaseVersion = actor.LeaseVersion;
        hpReporter = actor.GetComponent<EnemyTargetHpReporter>(); overhead = actor.GetComponent<EnemyOverheadHpBar>();
        originalReporter = hpReporter != null && hpReporter.enabled; originalOverhead = overhead != null && overhead.enabled;
        if (hpReporter != null) hpReporter.enabled = false;
        if (overhead != null) overhead.enabled = false;
        actor.AI.enabled = false;
        actor.BossPhaseController.ResetForPool(); actor.BossPhaseController.enabled = false;
        BuildMaterials();
        // 공통 디렉터는 패링 접수와 반동만 담당. 수치/페이즈/선택은 이 전투가 한 번만 처리한다.
        var proxy = ScriptableObject.CreateInstance<EnemyBossCombatProfile>(); clones.Add(proxy);
        proxy.name = "Crustaspikan Parry Reception (Runtime)"; proxy.parryGain = 0; proxy.heavyHitGain = 0;
        proxy.groggyMax = settings.groggyMax; proxy.patterns = Array.Empty<EnemyBossCombatProfile.Pattern>();
        parryDirector.Configure(proxy); lastParry = parryDirector.ParryCount;
        actor.Health.SetMaxHp(settings.bossHp, true);
        actor.Health.OnDamageResolved += OnDamage;
        Observer = new CrustaspikanStyleObserver(); Observer.Bind(player, actor, encounter);
        melee = player.GetComponent<MeleeRuntime>();
        energy = player.GetComponent<OverburstElementEnergy>();
        readyAt = Time.time + 2f;
        tree = new CrustaspikanSelector(
            Node(() => Actor.Health.IsDead, Dead),
            Node(() => IsGroggy || reaction.BlocksAttack, Suspended),
            Node(() => Phase == 1 && Actor.Health.NormalizedHp <= settings.phaseTwoHp && current == null || transitionStarted, Transition),
            Node(() => current != null, RunPattern),
            Node(() => Time.time < readyAt || ReviewMode, Wait),
            Node(CanDodge, Dodge),
            Node(() => true, SelectPattern));
    }
    private static CrustaspikanNode Node(Func<bool> condition, Func<CrustaspikanNodeStatus> action) => new CrustaspikanActionNode(condition, action);
    private void BuildMaterials()
    {
        runtimeMaterials = UnityEngine.Object.Instantiate(originalMaterials); clones.Add(runtimeMaterials);
        runtimeMaterials.name = "Crustaspikan Encounter Materials (Runtime)";
        var list = new List<EnemyBossAttackMaterial>(); var abilities = new List<EnemyAbilityDefinition>();
        foreach (var source in originalMaterials.attacks)
        {
            var m = UnityEngine.Object.Instantiate(source); clones.Add(m);
            m.ability = UnityEngine.Object.Instantiate(source.ability); clones.Add(m.ability);
            var rule = settings.Rule(source.runtimeClip.name);
            m.tuning = new EnemyBossAttackTuning { animationSpeedMultiplier = rule?.speed ?? 1f, damageMultiplier = rule?.damage ?? 1f,
                parries = new EnemyBossStrikeParryTuning[m.strikes.Length] };
            for (int i = 0; i < m.strikes.Length; i++) m.tuning.parries[i] = new EnemyBossStrikeParryTuning
                { canParry = (rule?.finalHitParry ?? true) && i == m.strikes.Length - 1 && source.delivery == EnemyBossMaterialDelivery.Melee };
            attacks.Add(source.runtimeClip.name, m); list.Add(m); abilities.Add(m.ability);
        }
        runtimeMaterials.attacks = list.ToArray();
        var compound = UnityEngine.Object.Instantiate(settings.composites); clones.Add(compound);
        compound.throwMaterial = attacks["ThrowRock"]; compound.throwPayload = EnemyBossThrowPayload.Rock;
        // 현재 두 소형 종 + 중형 + 정예의 종별 예약 상한 합을 방의 상한 이하로 둔다.
        int smallCap = Mathf.Max(1,(settings.maximumAdds - 2) / 2);
        foreach (var pattern in compound.spitPatterns)
        {
            pattern.material = attacks[pattern.material.runtimeClip.name];
            foreach (var emission in pattern.emissions)
                emission.payload.maximumAlive = emission.payload.definition.EnemyId == "CavernMutants_Gasterobrach" ? 1 : smallCap;
        }
        compound.elite.maximumAlive = 1;
        composite.Configure(compound);
        var set = ScriptableObject.CreateInstance<EnemyAbilitySet>(); clones.Add(set);
        set.Configure("CrustaspikanEncounterRuntime", abilities.ToArray());
        executor.Configure(runtimeMaterials); Actor.AbilityController.Configure(set, 1f, 1f);
    }
    private EnemyBossMaterialCollection runtimeMaterials;
    public void Tick()
    {
        if (disposed || Actor == null || !Actor.IsLeased || player == null) return;
        Observer.Tick();
        if (melee != null && melee.IsDashHeavyWindupActive) dashAttackUntil = Time.time + 1.5f;
        while (lastParry < parryDirector.ParryCount)
        {
            lastParry++; Observer.Parried(); CancelPattern();
            var parry = player.GetComponent<PlayerParryController>();
            float value = parry != null && parry.ActionGrade == ParryGrade.Incomplete ? 5f
                : parry != null && parry.ActionGrade == ParryGrade.Normal ? 20f : settings.perfectParryPoise;
            AddPoise(value); readyAt = Time.time + 1f;
        }
        if (!IsGroggy && Time.time > lastPoiseAt + settings.poiseDecayDelay)
            Poise = Mathf.Max(0, Poise - settings.poiseDecayPerSecond * Time.deltaTime);
        if (groggyUntil > 0 && !IsGroggy)
        {
            groggyUntil = 0; Poise = 0; Actor.Animator.speed = 1f;
            var idle = runtimeMaterials.FindMotion("IdleBreathe");
            if (idle != null) Actor.Animator.CrossFadeInFixedTime(idle.state, .1f);
            readyAt = Time.time + .5f;
        }
        tree.Tick();
    }
    private CrustaspikanNodeStatus Dead() { CancelPattern(); State = "격파"; encounter.BossDefeated(); return CrustaspikanNodeStatus.Running; }
    private CrustaspikanNodeStatus Suspended()
    { CancelPattern(); Actor.Movement.StopMovement(); State = IsGroggy ? "그로기 · 공격 기회" : "패링 반동"; return CrustaspikanNodeStatus.Running; }
    private CrustaspikanNodeStatus Wait() { State = "다음 공방 준비"; Actor.Movement.StopMovement(); return CrustaspikanNodeStatus.Running; }
    private CrustaspikanNodeStatus Transition()
    {
        State = "2페이즈 전환";
        if (!transitionStarted)
        {
            transitionStarted = true; Phase = 2; Observer.Freeze(settings.enableAdaptiveTactics);
            Actor.Movement.StopMovement(); Actor.AbilityController.Cancel();
            executor.TryPlayMotion("Roar2"); transitionDeadline = Time.time + 6f;
            encounter.Announce("보스가 당신의 전투 스타일을 분석했습니다\n" + Observer.Explanation, 7f);
        }
        if (!executor.IsExecuting || Time.time > transitionDeadline)
        { executor.Cancel(); transitionStarted = false; readyAt = Time.time + 1f; }
        return CrustaspikanNodeStatus.Running;
    }
    private bool CanDodge()
    {
        if (!settings.enableBossEvasion || melee == null || !melee.IsHeavyAttackInProgress || Time.time < nextDodgeAt) return false;
        return Vector3.Distance(Actor.transform.position, player.transform.position) < 7f;
    }
    private CrustaspikanNodeStatus Dodge()
    {
        nextDodgeAt = Time.time + 12f; DodgeCount++;
        current = new CrustaspikanEncounterSettings.Pattern { id = "reactive_backstep", label = "강공 관측 · 후퇴 반격", family = "evade", steps = new[] {
            new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Move, localDisplacement = new Vector3(0,0,-3f), seconds = 1f },
            new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Wait, seconds = .35f },
            new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Attack, materialOrMotion = "RightHandAttack" } } };
        BeginPattern(); encounter.Announce("보스가 강공을 보고 물러납니다", 1.5f); return CrustaspikanNodeStatus.Running;
    }
    private CrustaspikanNodeStatus SelectPattern()
    {
        candidates.Clear(); float total = 0f;
        foreach (var p in settings.patterns)
        {
            if (!Eligible(p)) continue; candidates.Add(p); total += Weight(p);
        }
        if (total <= 0)
        {
            State = "거리 좁히기";
            Actor.Movement.SetDestination(player.transform.position, 6f, EnemyLocomotionMode.Walk, .85f);
            return CrustaspikanNodeStatus.Running;
        }
        float draw = UnityEngine.Random.value * total;
        foreach (var p in candidates) { draw -= Weight(p); if (draw <= 0) { current = p; break; } }
        if (current == null) current = candidates[candidates.Count - 1];
        BeginPattern(); return CrustaspikanNodeStatus.Running;
    }
    private float Weight(CrustaspikanEncounterSettings.Pattern p)
    {
        float w = p.weight;
        if (Phase == 2 && p.counters != CrustaspikanTactic.Balanced && p.counters == Observer.Tactic) w *= settings.tacticWeightMultiplier;
        if (p.id == LastPattern) w *= .4f;
        Vector3 delta = player.transform.position - Actor.transform.position; delta.y = 0;
        if (p.family == "ranged" && delta.magnitude > 12) w *= 1.3f;
        if (p.family == "rear" && Vector3.Dot(Actor.transform.forward, delta.normalized) < -.35f) w *= 1.4f;
        // 현재 게이지는 작은 상황 보정에만 쓴다. 확정된 공격의 판정이나 리듬은 바꾸지 않는다.
        if (energy != null && energy.Normalized >= .8f && p.counters == CrustaspikanTactic.Parry) w *= 1.15f;
        if (p.steps[0].kind == CrustaspikanStepKind.Attack && encounter.PlayerVelocity.sqrMagnitude > .25f)
        {
            var m = attacks[p.steps[0].materialOrMotion];
            Quaternion facing = delta.sqrMagnitude > .001f ? Quaternion.LookRotation(delta) : Actor.transform.rotation;
            Vector3 predictedOffset = encounter.PlayerVelocity * .25f;
            foreach (var strike in m.strikes)
                if (strike.Intersects(player.CharacterController, Actor.transform,
                    Actor.transform.position + facing * strike.localOrigin - predictedOffset,
                    facing * Quaternion.Euler(0,strike.yaw,0))) { w *= 1.15f; break; }
        }
        return w;
    }
    private bool Eligible(CrustaspikanEncounterSettings.Pattern p)
    {
        if ((p.phaseMask & (1 << (Phase - 1))) == 0 || cooldowns.TryGetValue(p.id, out float until) && Time.time < until) return false;
        Vector3 delta = player.transform.position - Actor.transform.position; delta.y = 0; float distance = delta.magnitude;
        if (distance < p.minimumDistance || distance > p.maximumDistance) return false;
        if (p.rearOnly && Vector3.Dot(Actor.transform.forward, delta.normalized) > -.2f) return false;
        if (p.family == lastFamily && consecutiveFamily >= 2) return false;
        if ((p.family == "summon" || p.id == "elite_throw") && encounter.AliveAdds >= settings.maximumAdds) return false;
        var first = p.steps[0];
        if (first.kind != CrustaspikanStepKind.Attack) return true;
        var m = attacks[first.materialOrMotion];
        if (!Actor.AbilityController.IsCooldownReady(m.ability)) return false;
        if (m.delivery != EnemyBossMaterialDelivery.Melee) return true;
        Quaternion facing = delta.sqrMagnitude > .001f ? Quaternion.LookRotation(delta) : Actor.transform.rotation;
        Collider body = player.CharacterController;
        foreach (var s in m.strikes)
            if (s.Intersects(body, Actor.transform, Actor.transform.position + facing * s.localOrigin, facing * Quaternion.Euler(0,s.yaw,0))) return true;
        return false;
    }
    private void BeginPattern()
    {
        stepIndex = 0; stepStarted = false; LastPattern = current.id; PatternCount++;
        stepAttemptAt = Time.time;
        consecutiveFamily = current.family == lastFamily ? consecutiveFamily + 1 : 1; lastFamily = current.family;
        cooldowns[current.id] = Time.time + current.cooldown;
        Actor.Movement.StopMovement(); Actor.AbilityController.ClearPreparedAim();
    }
    private CrustaspikanNodeStatus RunPattern()
    {
        State = current.label;
        if (stepIndex >= current.steps.Length) { FinishPattern(); return CrustaspikanNodeStatus.Success; }
        var s = current.steps[stepIndex];
        if (!stepStarted)
        {
            stepStartedAt = Time.time; stepUntil = Time.time + s.seconds;
            Actor.AbilityController.ClearPreparedAim();
            switch (s.kind)
            {
                case CrustaspikanStepKind.Attack:
                case CrustaspikanStepKind.ThrowElite:
                    var key = s.kind == CrustaspikanStepKind.ThrowElite ? "ThrowRock" : s.materialOrMotion;
                    if (s.kind == CrustaspikanStepKind.ThrowElite) composite.SetNextThrowPayload(EnemyBossThrowPayload.Elite);
                    if (!Actor.AbilityController.TryStartAbility(attacks[key].ability, player.transform))
                    { Actor.Movement.FacePosition(player.transform.position); if (Time.time > stepAttemptAt + 4f) { CancelPattern(); readyAt = Time.time + .4f; } return CrustaspikanNodeStatus.Running; }
                    if (attacks[key].IsStrikeParryable(attacks[key].strikes.Length - 1)) Observer.Opportunity();
                    break;
                case CrustaspikanStepKind.Motion:
                    if (s.materialOrMotion == "UnearthRock") composite.SetNextThrowPayload(EnemyBossThrowPayload.Rock);
                    if (!executor.TryPlayMotion(s.materialOrMotion, s.materialOrMotion == "UnearthRock")) { CancelPattern(); return CrustaspikanNodeStatus.Failure; } break;
                case CrustaspikanStepKind.Move:
                    moveDestination = encounter.ClampArena(Actor.transform.position + Actor.transform.rotation * s.localDisplacement, 5f);
                    break;
                case CrustaspikanStepKind.LiftElite:
                    composite.SetNextThrowPayload(EnemyBossThrowPayload.Elite); executor.TryPlayMotion("UnearthRock", true); break;
            }
            stepStarted = true;
        }
        bool done = false;
        switch (s.kind)
        {
            case CrustaspikanStepKind.Attack:
            case CrustaspikanStepKind.ThrowElite:
            case CrustaspikanStepKind.Motion:
            case CrustaspikanStepKind.LiftElite:
                done = !Actor.AbilityController.IsExecuting;
                if (Time.time > stepStartedAt + 15f) { Actor.AbilityController.Cancel(); done = true; }
                break;
            case CrustaspikanStepKind.Wait: done = Time.time >= stepUntil; break;
            case CrustaspikanStepKind.Move:
                Actor.Movement.SetFacingDestination(moveDestination, .3f, player.transform.position, EnemyLocomotionMode.Walk, 1.5f);
                done = Time.time >= stepUntil || Vector3.Distance(Actor.transform.position, moveDestination) < .5f;
                break;
        }
        if (done) { Actor.Movement.StopMovement(); stepIndex++; stepStarted = false; stepAttemptAt = Time.time; }
        return CrustaspikanNodeStatus.Running;
    }
    private void FinishPattern()
    { current = null; stepStarted = false; readyAt = Time.time + (Phase == 1 ? settings.betweenPatterns : settings.phaseTwoBetweenPatterns); }
    private void CancelPattern()
    {
        if (current == null) return;
        Actor.AbilityController.Cancel(); Actor.Movement.CancelActionLock(); Actor.Movement.StopMovement();
        current = null; stepStarted = false;
    }
    private void OnDamage(CombatHealth health, DamageInfo info, float damage, bool fatal)
    {
        if (fatal || damage <= 0 || info.isDamageOverTime || !info.triggersOnHitEffects || info.source == null
            || info.source.GetComponentInParent<PlayerActorRuntime>() != player) return;
        Observer.ObserveHit(info, Time.time < dashAttackUntil);
        if ((info.playerAttackKind & (PlayerAttackKind.Weak | PlayerAttackKind.Heavy)) == 0) return;
        long key = ((long)info.source.GetInstanceID() << 32) ^ (uint)info.sourceAttackSequenceId;
        if (info.sourceAttackSequenceId != 0 && !hitSequences.Add(key)) return;
        float gain = (info.playerAttackKind & PlayerAttackKind.Heavy) != 0 ? settings.heavyPoise : settings.weakPoise;
        Vector3 delta = player.transform.position - Actor.transform.position; delta.y = 0;
        if (Vector3.Dot(Actor.transform.forward, delta.normalized) < -.5f)
        { gain *= settings.backPoiseMultiplier; BackHitCount++; encounter.Announce("백어택 · 그로기 축적 ×" + settings.backPoiseMultiplier.ToString("0.00"), .8f); }
        AddPoise(gain);
    }
    private void AddPoise(float amount)
    {
        if (amount <= 0 || IsGroggy || IsTransitioning || Time.time < protectionUntil || Actor.Health.IsDead) return;
        Poise = Mathf.Min(settings.groggyMax, Poise + amount); lastPoiseAt = Time.time;
        if (Poise < settings.groggyMax) return;
        GroggyCount++; CancelPattern(); Actor.AbilityController.Cancel(); Actor.Movement.StopMovement();
        groggyUntil = Time.time + settings.groggySeconds;
        protectionUntil = groggyUntil + settings.groggyProtection;
        reaction.ApplyBossStun(settings.groggySeconds);
        var motion = runtimeMaterials.FindMotion("GetHitFront");
        if (motion != null) { Actor.Animator.CrossFadeInFixedTime(motion.state, .08f); Actor.Animator.speed = .25f; }
        encounter.Announce("그로기! · 4.5초 공격 기회", settings.groggySeconds);
    }
    public bool StartPatternForReview(string id)
    {
        if (disposed || IsGroggy || IsTransitioning || Actor.Health.IsDead) return false;
        foreach (var p in settings.patterns) if (p.id == id) { CancelPattern(); current = p; BeginPattern(); return true; }
        return false;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        Observer.Dispose();
        if (Actor != null && Actor.LeaseVersion == leaseVersion)
        {
            Actor.Health.OnDamageResolved -= OnDamage;
            CancelPattern(); Actor.AbilityController.Cancel(); Actor.Animator.speed = 1f;
            executor.Configure(originalMaterials); Actor.AbilityController.Configure(originalAbilities, 1f, 1f);
            composite.ReleaseSummons(); composite.Configure(originalComposite);
            parryDirector.Configure(originalProfile); Actor.AI.enabled = originalAI; Actor.BossPhaseController.enabled = originalPhase;
            if (hpReporter != null) hpReporter.enabled = originalReporter;
            if (overhead != null) overhead.enabled = originalOverhead;
        }
        foreach (var clone in clones) if (clone != null) UnityEngine.Object.Destroy(clone);
        clones.Clear();
    }
}
