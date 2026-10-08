using System;
using System.Collections.Generic;
using UnityEngine;

// 전투 지휘만 소유한다. 접촉 판정·피해·패링 경고는 기존 재료 실행기로 보낸다.
public sealed partial class CrustaspikanEncounterBrain : IDisposable, IEnemyBossHudSource
{
    public EnemyActor Actor { get; private set; }
    public Transform Target => HasLivingTarget ? player.transform : null;
    public CrustaspikanCombatContext Context { get; private set; }
    public string CurrentPatternId => current?.id ?? "";
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
    private readonly float[] phaseThresholds;
    private readonly List<UnityEngine.Object> clones = new List<UnityEngine.Object>();
    private readonly Dictionary<string, EnemyBossAttackMaterial> attacks = new Dictionary<string, EnemyBossAttackMaterial>();
    private readonly Dictionary<string, float> cooldowns = new Dictionary<string, float>();
    private readonly HashSet<long> hitSequences = new HashSet<long>();
    private readonly List<CrustaspikanEncounterSettings.Pattern> candidates = new List<CrustaspikanEncounterSettings.Pattern>();
    private CrustaspikanNode tree;
    private CrustaspikanEncounterSettings.Pattern current;
    private int stepIndex, lastParry;
    private bool stepStarted, transitionStarted, disposed, stateApplied, patternCommitted;
    private float stepStartedAt, stepUntil, readyAt, groggyUntil, protectionUntil, lastPoiseAt, nextDodgeAt;
    private Vector3 moveDestination;
    private string lastFamily = "";
    private int consecutiveFamily;
    private float transitionDeadline;
    private float stepAttemptAt;
    private MeleeRuntime melee;
    private OverburstElementEnergy energy;

    public bool IsActive => !disposed && Actor != null && Actor.IsLeased && Actor.LeaseVersion == leaseVersion;
    private bool HasLivingTarget => player != null && player.isActiveAndEnabled && player.Health != null && !player.Health.IsDead;
    public bool IsDefeated => encounter.Defeated || !IsActive || Actor.Health.IsDead;
    CombatHealth IEnemyBossHudSource.Health => IsActive ? Actor.Health : null;
    string IEnemyBossHudSource.DisplayName => "크러스피칸";
    int IEnemyBossHudSource.Level => IsActive ? Actor.GetComponent<EnemyRank>()?.Level ?? 1 : 1;
    int IEnemyBossHudSource.PhaseIndex => Phase - 1;
    float[] IEnemyBossHudSource.PhaseThresholds => phaseThresholds;
    float IEnemyBossHudSource.Groggy01 => settings.groggyMax > 0f ? Poise / settings.groggyMax : 0f;
    EnemyBossEmblemFxMode IEnemyBossHudSource.EmblemFx => Actor.BossPhaseController.BossDefinition != null
        ? Actor.BossPhaseController.BossDefinition.EmblemFx : EnemyBossEmblemFxMode.Fire;
    public CrustaspikanEncounterBrain(CrustaspikanEncounter encounter, EnemyActor actor, PlayerActorRuntime player)
    {
        this.encounter = encounter; settings = encounter.Settings; Actor = actor; this.player = player;
        if (actor.AnimationBridge.HasInvalidMotionProfile) throw new System.InvalidOperationException(actor.AnimationBridge.MotionConfigurationError);
        phaseThresholds = new[] { settings.phaseTwoHp };
        executor = actor.GetComponent<EnemyBossMaterialExecutor>();
        composite = actor.GetComponent<EnemyBossCompositePatternExecutor>(); originalComposite = composite.Patterns;
        parryDirector = actor.GetComponent<EnemyBossCombatDirector>(); reaction = actor.GetComponent<EnemyMovementReaction>();
        originalMaterials = executor.Collection; originalAbilities = actor.AbilityController.AbilitySet;
        originalProfile = parryDirector.Profile; originalAI = actor.AI.enabled;
        originalPhase = actor.BossPhaseController.enabled;
        leaseVersion = actor.LeaseVersion;
        hpReporter = actor.GetComponent<EnemyTargetHpReporter>(); overhead = actor.GetComponent<EnemyOverheadHpBar>();
        originalReporter = hpReporter != null && hpReporter.enabled; originalOverhead = overhead != null && overhead.enabled;
        try
        {
            BuildMaterials();
            stateApplied = true;
            if (hpReporter != null) hpReporter.enabled = false;
            if (overhead != null) overhead.enabled = false;
            actor.AI.enabled = false;
            actor.AI.SetTarget(player.transform);
            actor.BossPhaseController.ResetForPool(); actor.BossPhaseController.enabled = false;
            composite.Configure(runtimeComposite);
            executor.Configure(runtimeMaterials); Actor.AbilityController.Configure(runtimeAbilities, 1f, 1f);
            // 공통 디렉터는 패링 접수와 반동만 담당. 수치/페이즈/선택은 이 전투가 한 번만 처리한다.
            var proxy = ScriptableObject.CreateInstance<EnemyBossCombatProfile>(); clones.Add(proxy);
            proxy.name = "Crustaspikan Parry Reception (Runtime)"; proxy.parryGain = 0; proxy.heavyHitGain = 0;
            proxy.groggyMax = settings.groggyMax; proxy.patterns = Array.Empty<EnemyBossCombatProfile.Pattern>();
            parryDirector.Configure(proxy); lastParry = parryDirector.ParryCount;
            actor.Health.SetMaxHp(settings.bossHp, true);
            actor.Health.OnDamageResolved += OnDamage;
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
        catch { Dispose(); throw; }
    }
    private static CrustaspikanNode Node(Func<bool> condition, Func<CrustaspikanNodeStatus> action) => new CrustaspikanActionNode(condition, action);
    private void BuildMaterials()
    {
        var recoil = Actor.GetComponent<CrustaspikanTemporaryReaction>()?.ParryRecoilProfile;
        if (!CrustaspikanEncounterMaterialResolver.ValidateCollection(settings, originalMaterials, recoil, out var reason))
            throw new InvalidOperationException(reason);
        runtimeMaterials = UnityEngine.Object.Instantiate(originalMaterials); clones.Add(runtimeMaterials);
        runtimeMaterials.name = "Crustaspikan Encounter Materials (Runtime)";
        var list = new List<EnemyBossAttackMaterial>(); var abilities = new List<EnemyAbilityDefinition>();
        foreach (var source in originalMaterials.attacks)
        {
            var m = UnityEngine.Object.Instantiate(source); clones.Add(m);
            m.ability = UnityEngine.Object.Instantiate(source.ability); clones.Add(m.ability);
            if (!CrustaspikanEncounterMaterialResolver.TryResolveTuning(settings, m, recoil, out m.tuning, out reason))
                throw new InvalidOperationException(reason);
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
        if (!compound.IsValid) throw new InvalidOperationException("전투용 복합 공격의 최종 연결이 유효하지 않습니다.");
        runtimeComposite = compound;
        var set = ScriptableObject.CreateInstance<EnemyAbilitySet>(); clones.Add(set);
        set.Configure("CrustaspikanEncounterRuntime", abilities.ToArray());
        if (!set.IsValid) throw new InvalidOperationException("전투용 공격 능력 연결이 유효하지 않습니다.");
        runtimeAbilities = set;
    }
    private EnemyBossMaterialCollection runtimeMaterials;
    private EnemyBossCompositePatternSet runtimeComposite;
    private EnemyAbilitySet runtimeAbilities;
    public void BeginEntrance()
    {
        CancelPattern(); Actor.AbilityController.Cancel(); Actor.Movement.StopMovement(); State = "등장";
    }
    public void FinishEntrance(float graceSeconds)
    {
        if (!IsActive) return;
        executor.Cancel(); Actor.Movement.CancelActionLock(); Actor.Movement.StopMovement();
        if (!UsesMotion) Actor.Animator.Play("Locomotion", 0, 0f); readyAt = Time.time + Mathf.Max(0f, graceSeconds); State = "준비";
    }
    public void Tick()
    {
        if (!IsActive || encounter.Defeated) return;
        if (Actor.Health.IsDead) { tree.Tick(); return; }
        if (!HasLivingTarget)
        {
            CancelPattern(); Actor.Movement.StopMovement(); State = "유효한 대상 대기"; return;
        }
        if (Actor.AI.Target != player.transform) Actor.AI.SetTarget(player.transform);
        Context = ReadContext();
        while (lastParry < parryDirector.ParryCount)
        {
            lastParry++; CancelPattern();
            var parry = player.GetComponent<PlayerParryController>();
            float value = parry != null && parry.ActionGrade == ParryGrade.Incomplete ? 5f
                : parry != null && parry.ActionGrade == ParryGrade.Normal ? 20f : settings.perfectParryPoise;
            AddPoise(value); readyAt = Time.time + 1f;
        }
        if (!IsGroggy && Time.time > lastPoiseAt + settings.poiseDecayDelay)
            Poise = Mathf.Max(0, Poise - settings.poiseDecayPerSecond * Time.deltaTime);
        if (groggyUntil > 0 && !IsGroggy)
        {
            groggyUntil = 0; Poise = 0; if (!UsesMotion) Actor.Animator.speed = 1f;
            var idle = runtimeMaterials.FindMotion("IdleBreathe");
            if (!UsesMotion && idle != null) Actor.Animator.CrossFadeInFixedTime(idle.state, .1f);
            readyAt = Time.time + .5f;
        }
        tree.Tick();
    }
    private CrustaspikanNodeStatus Dead() { CancelPattern(); State = "격파"; encounter.BossDefeated(); return CrustaspikanNodeStatus.Running; }
    private CrustaspikanNodeStatus Suspended()
    { CancelPattern(); Actor.Movement.StopMovement(); State = IsGroggy ? "그로기 · 공격 기회" : "패링 반동"; return CrustaspikanNodeStatus.Running; }
    private CrustaspikanNodeStatus Wait()
    {
        State = "다음 공방 준비"; Actor.Movement.StopMovement();
        FaceTarget();
        return CrustaspikanNodeStatus.Running;
    }
    private void FaceTarget()
    {
        if (!Actor.Movement.IsActionLocked && !Actor.Movement.IsStatusMovementLocked)
            Actor.Movement.FacePosition(ResolveFacingTarget());
    }
    private Vector3 ResolveFacingTarget()
    {
        Vector3 target = player.transform.position;
        if (ReviewMode || !UsesMotion || Actor.Movement.IsFacingForAttack(target, settings.attackFacingTolerance)) return target;
        Vector3 delta = target - Actor.transform.position; delta.y = 0f;
        if (delta.sqrMagnitude < 1f) return target;
        float angularSpeed = Vector3.Dot(Vector3.Cross(delta, encounter.PlayerVelocity), Vector3.up) / delta.sqrMagnitude * Mathf.Rad2Deg;
        if (Mathf.Abs(angularSpeed) < 5f) return target;
        var profile = Actor.AnimationBridge.PlaybackProfile;
        float bestError = float.PositiveInfinity;
        // Predict only the turn landing; the accepted attack still freezes its actual aim.
        for (int i = 0; i <= 12; i++)
        {
            float horizon = .6f + i * .1f;
            Vector3 predicted = Quaternion.AngleAxis(Mathf.Clamp(angularSpeed * horizon, -90f, 90f), Vector3.up) * delta;
            float angle = Vector3.SignedAngle(Actor.Movement.PhysicalRotation * Vector3.forward, predicted, Vector3.up);
            bool halfTurn = Mathf.Abs(angle) > 90f && !Mathf.Approximately(Mathf.Abs(angle), 90f);
            var binding = profile.Find(halfTurn ? (angle < 0f ? "Turn180Left" : "Turn180Right") : (angle < 0f ? "Turn90Left" : "Turn90Right"));
            var clip = profile.ResolveClip(binding);
            if (clip == null || Mathf.Abs(angle) <= profile.FacingTolerance) continue;
            float rate = binding.rate * Mathf.Clamp(binding.authoredYaw / Mathf.Abs(angle), 1f, 2f);
            float error = Mathf.Abs(horizon - (clip.length / rate + binding.settleSeconds + binding.blendOut + .02f));
            if (error >= bestError) continue;
            bestError = error; target = Actor.transform.position + predicted;
        }
        return target;
    }
    private Quaternion CandidateFacing(CrustaspikanEncounterSettings.Pattern pattern)
    {
        // Rear counters keep the pose the player moved behind; forward attacks prepare a new facing.
        return pattern.rearOnly || Context.Delta.sqrMagnitude < .001f
            ? Actor.transform.rotation : Quaternion.LookRotation(Context.Delta);
    }
    private bool IntersectsTarget(EnemyBossAttackMaterial material, Quaternion facing, Vector3 prediction = default)
    {
        return IntersectsTargetFrom(material, Actor.transform.position, facing, prediction);
    }
    private bool IntersectsTargetFrom(EnemyBossAttackMaterial material, Vector3 position, Quaternion facing, Vector3 prediction = default)
    {
        foreach (var strike in material.strikes)
        {
            Vector3 origin = position + facing * Vector3.Scale(strike.localOrigin, Actor.transform.lossyScale) - prediction;
            if (strike.Intersects(player.CharacterController, Actor.transform, origin, facing * Quaternion.Euler(0, strike.yaw, 0))) return true;
        }
        return false;
    }
    private CrustaspikanNodeStatus Transition()
    {
        if (UsesMotion) return RunOwnedPhaseTransition();
        State = "2페이즈 전환";
        if (!transitionStarted)
        {
            transitionStarted = true; Phase = 2;
            Actor.Movement.StopMovement(); Actor.AbilityController.Cancel();
            executor.TryPlayMotion("Roar2"); transitionDeadline = Time.time + 6f;
            encounter.Announce("2페이즈 · 군락의 분노", 5f);
        }
        if (!executor.IsExecuting || Time.time > transitionDeadline)
        { executor.Cancel(); transitionStarted = false; readyAt = Time.time + 1f; }
        return CrustaspikanNodeStatus.Running;
    }
    private CrustaspikanCombatContext ReadContext() => new CrustaspikanCombatContext(
        Actor.transform.position, Actor.transform.forward, player.transform.position, encounter.PlayerVelocity,
        energy != null ? energy.Normalized : 0f, settings.groggyMax > 0f ? Poise / settings.groggyMax : 0f, encounter.AliveAdds, Phase);
    private bool CanDodge()
    {
        if (!settings.enableBossEvasion || melee == null || !melee.IsHeavyMovementAfterimageWindow || Time.time < nextDodgeAt) return false;
        var context = ReadContext();
        return context.Distance < settings.evasionTriggerDistance && context.ForwardDot > .2f
            && TryMoveDestination(new Vector3(0, 0, -settings.evasionDistance), out _);
    }
    private CrustaspikanNodeStatus Dodge()
    {
        nextDodgeAt = Time.time + settings.evasionCooldown; DodgeCount++;
        current = new CrustaspikanEncounterSettings.Pattern { id = "reactive_backstep", label = "강공 준비 견제 · 후퇴 반격", family = "evade", steps = new[] {
            new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Move, localDisplacement = new Vector3(0,0,-settings.evasionDistance), seconds = settings.evasionSeconds },
            new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Wait, seconds = .35f },
            new CrustaspikanEncounterSettings.Step { kind = CrustaspikanStepKind.Attack, materialOrMotion = "RightHandAttack" } } };
        BeginPattern(); encounter.Announce("강공 견제", 1.5f); return CrustaspikanNodeStatus.Running;
    }
    private CrustaspikanNodeStatus SelectPattern()
    {
        Context = ReadContext(); candidates.Clear(); float total = 0f;
        foreach (var p in settings.patterns)
        {
            if (!Eligible(p)) continue; candidates.Add(p); total += Weight(p);
        }
        if (total <= 0f)
        {
            if (Context.Distance > settings.approachDistance + .5f)
            {
                State = "거리 좁히기";
                float speedMultiplier = settings.approachSpeed / Mathf.Max(.1f, Actor.Movement.Profile != null ? Actor.Movement.Profile.MoveSpeed : EnemyMovementProfile.MinimumMoveSpeed);
                Actor.Movement.SetDestination(encounter.ClampArena(player.transform.position, 5f), settings.approachDistance, EnemyLocomotionMode.Walk, speedMultiplier);
            }
            else
            {
                State = "방향 정렬 · 재사용 대기";
                Actor.Movement.StopMovement(); FaceTarget();
            }
            return CrustaspikanNodeStatus.Running;
        }
        float draw = UnityEngine.Random.value * total;
        foreach (var p in candidates) { draw -= Weight(p); if (draw <= 0f) { current = p; break; } }
        if (current == null) current = candidates[candidates.Count - 1];
        BeginPattern(); return CrustaspikanNodeStatus.Running;
    }
    private float Weight(CrustaspikanEncounterSettings.Pattern p)
    {
        float w = CrustaspikanCombatDecision.Weight(p, Context, LastPattern, lastFamily, consecutiveFamily);
        if (p.steps[0].kind == CrustaspikanStepKind.Attack && Context.Velocity.sqrMagnitude > .25f)
        {
            var m = attacks[p.steps[0].materialOrMotion];
            Quaternion facing = CandidateFacing(p);
            Vector3 predictedOffset = Vector3.ClampMagnitude(Context.Velocity * .25f, 2f);
            if (IntersectsTarget(m, facing, predictedOffset)) w *= 1.15f;
        }
        return w;
    }
    private bool Eligible(CrustaspikanEncounterSettings.Pattern p)
    {
        if ((p.phaseMask & (1 << (Phase - 1))) == 0) return false;
        if (cooldowns.TryGetValue(p.id, out float until) && Time.time < until) return false;
        if (Context.Distance < p.minimumDistance || Context.Distance > p.maximumDistance) return false;
        if (p.rearOnly && Context.ForwardDot > -.2f) return false;
        if (CrustaspikanCombatDecision.NeedsSummonSlot(p) && Context.AliveAdds >= settings.maximumAdds) return false;
        // 모션/이동으로 시작하는 조립도 뒤에 쓸 공격의 재사용을 먼저 확인한다.
        Vector3 plannedPosition = Actor.transform.position;
        Quaternion plannedFacing = Actor.Movement.PhysicalRotation;
        bool moved = false, prepared = false;
        foreach (var step in p.steps)
        {
            if (step.kind == CrustaspikanStepKind.Move)
            {
                if (!prepared && step.localDisplacement.z > 0f) plannedFacing = FacingTowardTarget(plannedPosition);
                if (!TryMoveDestinationFrom(plannedPosition, plannedFacing, step.localDisplacement, out plannedPosition)) return false;
                moved |= step.localDisplacement.sqrMagnitude > .000001f;
            }
            else if (step.kind == CrustaspikanStepKind.LiftElite || step.kind == CrustaspikanStepKind.Motion && step.materialOrMotion == "UnearthRock")
            { plannedFacing = FacingTowardTarget(plannedPosition); prepared = true; }
            string key = step.kind == CrustaspikanStepKind.ThrowElite ? "ThrowRock"
                : step.kind == CrustaspikanStepKind.Attack ? step.materialOrMotion : null;
            if (key != null)
            {
                var material = attacks[key];
                if (!Actor.AbilityController.IsCooldownReady(material.ability)) return false;
                if (moved && material.delivery == EnemyBossMaterialDelivery.Melee)
                {
                    Quaternion facing = p.rearOnly || prepared ? plannedFacing : FacingTowardTarget(plannedPosition);
                    Vector3 aimDelta = Actor.AbilityController.ResolveAimPosition(player.transform) - plannedPosition; aimDelta.y = 0f;
                    if (!EnemyAttackThreatGeometry.MatchesUseConditions(Actor, material.ability, aimDelta.magnitude, Actor.Health.NormalizedHp)
                        || !IntersectsTargetFrom(material, plannedPosition, facing)) return false;
                }
            }
        }
        var first = p.steps[0];
        if (first.kind != CrustaspikanStepKind.Attack) return true;
        var m = attacks[first.materialOrMotion];
        if (m.delivery != EnemyBossMaterialDelivery.Melee) return true;
        return IntersectsTarget(m, CandidateFacing(p));
    }
    private bool TryMoveDestination(Vector3 local, out Vector3 destination)
    {
        Quaternion facing = UsesMotion ? Actor.Movement.PhysicalRotation : FacingTowardTarget(Actor.transform.position);
        return TryMoveDestinationFrom(Actor.transform.position, facing, local, out destination);
    }
    private Quaternion FacingTowardTarget(Vector3 position)
    {
        Vector3 delta = player.transform.position - position; delta.y = 0f;
        return delta.sqrMagnitude > .001f ? Quaternion.LookRotation(delta) : Actor.Movement.PhysicalRotation;
    }
    private bool TryMoveDestinationFrom(Vector3 position, Quaternion facing, Vector3 local, out Vector3 destination)
    {
        Vector3 desired = position + facing * local;
        destination = encounter.ClampArena(desired, 5f);
        Vector3 travel = destination - position; travel.y = 0f;
        Vector3 requested = facing * local; requested.y = 0f;
        return requested.sqrMagnitude < .001f || travel.magnitude >= Mathf.Min(.75f, requested.magnitude * .5f)
            && Vector3.Dot(travel.normalized, requested.normalized) > .5f && Actor.Movement.IsWalkablePosition(destination);
    }
    private void BeginPattern()
    {
        stepIndex = 0; stepStarted = patternCommitted = false;
        ResetMoveStep();
        stepAttemptAt = Time.time;
        Actor.Movement.StopMovement(); Actor.AbilityController.ClearPreparedAim();
        if (UsesMotion) BeginMotionPattern();
    }
    private void CommitPatternStep()
    {
        if (patternCommitted || current == null) return;
        patternCommitted = true; LastPattern = current.id; PatternCount++;
        consecutiveFamily = current.family == lastFamily ? consecutiveFamily + 1 : 1; lastFamily = current.family;
        cooldowns[current.id] = Time.time + current.cooldown;
    }
    private CrustaspikanNodeStatus RunPattern()
    {
        if (UsesMotion) return RunOwnedPattern();
        State = current.label;
        if (stepIndex >= current.steps.Length) { FinishPattern(); return CrustaspikanNodeStatus.Success; }
        var s = current.steps[stepIndex];
        if (s.kind == CrustaspikanStepKind.Move) return RunMoveStep(s);
        if (!stepStarted)
        {
            bool preparesAim = s.kind == CrustaspikanStepKind.Attack || s.kind == CrustaspikanStepKind.ThrowElite
                || s.kind == CrustaspikanStepKind.LiftElite || s.kind == CrustaspikanStepKind.Motion && s.materialOrMotion == "UnearthRock";
            if (preparesAim && !PrepareStepFacing()) return CrustaspikanNodeStatus.Running;
            stepStartedAt = Time.time; stepUntil = Time.time + s.seconds;
            Actor.AbilityController.ClearPreparedAim();
            switch (s.kind)
            {
                case CrustaspikanStepKind.Attack:
                case CrustaspikanStepKind.ThrowElite:
                    var key = s.kind == CrustaspikanStepKind.ThrowElite ? "ThrowRock" : s.materialOrMotion;
                    // The player can leave the chosen footprint while the boss turns or approaches.
                    // Re-evaluate at commitment instead of releasing a strike into empty space.
                    if (attacks[key].delivery == EnemyBossMaterialDelivery.Melee && !IntersectsTarget(attacks[key], Actor.transform.rotation))
                    { CancelPattern(); readyAt = Time.time + .1f; return CrustaspikanNodeStatus.Running; }
                    if (s.kind == CrustaspikanStepKind.ThrowElite) composite.SetNextThrowPayload(EnemyBossThrowPayload.Elite);
                    if (!Actor.AbilityController.TryStartAbility(attacks[key].ability, player.transform))
                    { if (Time.time > stepAttemptAt + settings.attackPreparationTimeout) { CancelPattern(); readyAt = Time.time + .4f; } return CrustaspikanNodeStatus.Running; }
                    break;
                case CrustaspikanStepKind.Motion:
                    if (s.materialOrMotion == "UnearthRock") composite.SetNextThrowPayload(EnemyBossThrowPayload.Rock);
                    if (!executor.TryPlayMotion(s.materialOrMotion, s.materialOrMotion == "UnearthRock")) { CancelPattern(); return CrustaspikanNodeStatus.Failure; } break;
                case CrustaspikanStepKind.LiftElite:
                    composite.SetNextThrowPayload(EnemyBossThrowPayload.Elite); executor.TryPlayMotion("UnearthRock", true); break;
            }
            stepStarted = true;
            if (s.kind != CrustaspikanStepKind.Wait) CommitPatternStep();
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
        }
        if (done) { Actor.Movement.StopMovement(); stepIndex++; stepStarted = false; stepAttemptAt = Time.time; }
        return CrustaspikanNodeStatus.Running;
    }
    private bool PrepareStepFacing()
    {
        if (current.rearOnly) return true;
        Vector3 delta = player.transform.position - Actor.transform.position; delta.y = 0f;
        if (delta.sqrMagnitude < .001f || Vector3.Angle(Actor.transform.forward, delta) <= settings.attackFacingTolerance) return true;
        State = "대상 조준 · " + current.label;
        Actor.Movement.StopMovement(); FaceTarget();
        if (Time.time > stepAttemptAt + settings.attackPreparationTimeout)
        { CancelPattern(); readyAt = Time.time + .1f; }
        return false;
    }
    private void FinishPattern()
    { current = null; stepStarted = false; ResetMoveStep(); readyAt = Time.time + (Phase == 1 ? settings.betweenPatterns : settings.phaseTwoBetweenPatterns); }
    private void CancelPattern()
    {
        ResetMoveStep();
        if (current == null) return;
        Actor.AbilityController.Cancel(); Actor.Movement.CancelActionLock(); Actor.Movement.StopMovement();
        current = null; stepStarted = false;
        if (UsesMotion) Actor.Movement.SetMoveFacingPolicy(false);
    }
    private void OnDamage(CombatHealth health, DamageInfo info, float damage, bool fatal)
    {
        if (fatal || damage <= 0 || info.isDamageOverTime || !info.triggersOnHitEffects || info.source == null
            || info.source.GetComponentInParent<PlayerActorRuntime>() != player) return;
        if ((info.playerAttackKind & (PlayerAttackKind.Weak | PlayerAttackKind.Heavy)) == 0) return;
        long key = ((long)info.source.GetInstanceID() << 32) ^ (uint)info.sourceAttackSequenceId;
        if (info.sourceAttackSequenceId != 0 && !hitSequences.Add(key)) return;
        float gain = (info.playerAttackKind & PlayerAttackKind.Heavy) != 0 ? settings.heavyPoise : settings.weakPoise;
        Vector3 delta = player.transform.position - Actor.transform.position; delta.y = 0;
        if (Vector3.Dot(Actor.transform.forward, delta.normalized) < -.5f)
        { gain *= settings.backPoiseMultiplier; BackHitCount++; encounter.Announce("백어택", .8f); }
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
        var temporary = Actor.GetComponent<CrustaspikanTemporaryReaction>();
        if (temporary == null || !temporary.TryPlayGroggy(settings.groggySeconds)) reaction.ApplyBossStun(settings.groggySeconds);
        var motion = runtimeMaterials.FindMotion("GetHitFront");
        if (!UsesMotion && motion != null) { Actor.Animator.CrossFadeInFixedTime(motion.state, .08f); Actor.Animator.speed = .25f; }
        encounter.Announce("그로기", settings.groggySeconds);
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
        try
        {
            if (stateApplied && Actor != null && Actor.LeaseVersion == leaseVersion)
            {
                Actor.Health.OnDamageResolved -= OnDamage;
                CancelPattern(); Actor.AbilityController.Cancel(); if (!UsesMotion) Actor.Animator.speed = 1f;
                executor.Configure(originalMaterials); Actor.AbilityController.Configure(originalAbilities, 1f, 1f);
                composite.ReleaseSummons(); composite.Configure(originalComposite);
                parryDirector.Configure(originalProfile); Actor.AI.enabled = originalAI; Actor.BossPhaseController.enabled = originalPhase;
                if (hpReporter != null) hpReporter.enabled = originalReporter;
                if (overhead != null) overhead.enabled = originalOverhead;
            }
        }
        finally
        {
            foreach (var clone in clones) if (clone != null) UnityEngine.Object.Destroy(clone);
            clones.Clear(); stateApplied = false;
        }
    }
}
