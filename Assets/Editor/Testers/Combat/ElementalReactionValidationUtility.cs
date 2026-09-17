using System;
using UnityEditor;
using UnityEngine;

public static class ElementalReactionValidationUtility
{
    [MenuItem("OVERBURST/Codex/Validate/Elemental Reaction Runtime")]
    public static void ValidateFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        ValidateResolver();
        ValidateRules();
        ValidateProcessorAndVaporize();
        ValidateThermalFracture();
        ValidateStoredReactionResults();
        ValidateFreezeConsumerLock();
        ValidateChainAndColdCharge();
        Debug.Log("[ElementalReactionValidation] 6반응·원형 범위 연속 감쇠·비재귀 계약 검증 완료");
    }

    private static void ValidateResolver()
    {
        ElementalBasicStatusSet wet = new ElementalBasicStatusSet(false, true, false, false);
        ElementalBasicStatusSet burning = new ElementalBasicStatusSet(true, false, false, false);
        RequireVaporize(wet, WeaponElement.Fire, "Wet + Fire");
        RequireVaporize(burning, WeaponElement.Water, "Burning + Water");
        RequireThermalFracture(burning, WeaponElement.Ice, "Burning + Ice");
        RequireThermalFracture(
            new ElementalBasicStatusSet(false, false, true, false),
            WeaponElement.Fire,
            "Chilled + Fire");
        RequireReaction(burning, WeaponElement.Electric, ElementalReactionType.Plasma, "Burning + Electric");
        RequireReaction(
            new ElementalBasicStatusSet(false, false, false, true),
            WeaponElement.Fire,
            ElementalReactionType.Plasma,
            "Shocked + Fire");
        RequireReaction(wet, WeaponElement.Ice, ElementalReactionType.Freeze, "Wet + Ice");
        RequireReaction(
            new ElementalBasicStatusSet(false, false, true, false),
            WeaponElement.Water,
            ElementalReactionType.Freeze,
            "Chilled + Water");
        RequireReaction(
            new ElementalBasicStatusSet(false, false, false, true),
            WeaponElement.Water,
            ElementalReactionType.ChainElectricity,
            "Shocked + Water");
        RequireReaction(wet, WeaponElement.Electric, ElementalReactionType.ChainElectricity, "Wet + Electric");
        RequireReaction(
            new ElementalBasicStatusSet(false, false, false, true),
            WeaponElement.Ice,
            ElementalReactionType.ColdCharge,
            "Shocked + Ice");
        RequireReaction(
            new ElementalBasicStatusSet(false, false, true, false),
            WeaponElement.Electric,
            ElementalReactionType.ColdCharge,
            "Chilled + Electric");

        ElementalBasicStatusSet vaporizePriority = new ElementalBasicStatusSet(false, true, true, false);
        RequireVaporize(vaporizePriority, WeaponElement.Fire, "Wet+Chilled + Fire 우선순위");
        ElementalBasicStatusSet thermalPriority = new ElementalBasicStatusSet(false, false, true, true);
        RequireThermalFracture(thermalPriority, WeaponElement.Fire, "Chilled+Shocked + Fire 우선순위");
        RequireReaction(
            new ElementalBasicStatusSet(false, false, true, true),
            WeaponElement.Water,
            ElementalReactionType.Freeze,
            "Freeze > ChainElectricity");
        RequireReaction(
            new ElementalBasicStatusSet(false, true, true, false),
            WeaponElement.Electric,
            ElementalReactionType.ChainElectricity,
            "ChainElectricity > ColdCharge");

        RequireNoReaction(wet, WeaponElement.Water, "Wet + Water");
        RequireNoReaction(default, WeaponElement.Fire, "상태 없음");
    }

    private static void ValidateRules()
    {
        AssertApproximately(2.5f, ElementalReactionRules.VaporizeRadius, "증발 반경");
        AssertApproximately(0.45f, ElementalReactionRules.VaporizeDamageCoefficient, "증발 단일 피해 계수");
        AssertApproximately(45f, ElementalReactionRules.ResolveVaporizeDamage(100f), "증발 단일 피해");
        AssertApproximately(0.3f, ElementalReactionRules.FractureDamageCoefficient, "균열 추가 피해 계수");
        AssertApproximately(30f, ElementalReactionRules.ResolveFractureDamage(100f), "균열 추가 피해");
        AssertApproximately(0.3f, ElementalReactionRules.PlasmaDamageCoefficient, "플라즈마 피해 계수");
        AssertApproximately(3f, ElementalReactionRules.PlasmaRadius, "플라즈마 반경");
        AssertApproximately(2.5f, ElementalReactionRules.PlasmaKnockback, "플라즈마 넉백");
        AssertApproximately(3f, ElementalReactionRules.FreezeDuration, "빙결 지속시간");
        AssertApproximately(0.4f, ElementalReactionRules.ShatterDamageCoefficient, "쇄빙 피해 계수");
        AssertApproximately(4f, ElementalReactionRules.ChainRadius, "연쇄감전 반경");
        if (ElementalReactionRules.ChainMaximumTargetCount != 5
            || ElementalReactionRules.ChainDamageCoefficients.Length != 5)
            throw new InvalidOperationException("연쇄감전 최대 대상 또는 계수 수가 5가 아닙니다.");
        float[] chainExpected = { 1f, 0.2f, 0.15f, 0.1f, 0.05f };
        for (int i = 0; i < chainExpected.Length; i++)
            AssertApproximately(chainExpected[i], ElementalReactionRules.ChainDamageCoefficients[i], $"연쇄감전 {i} 계수");
        AssertApproximately(6f, ElementalReactionRules.ColdChargeDuration, "냉전하 지속시간");
        AssertApproximately(0.05f, ElementalReactionRules.ColdChargeDamageCoefficient, "냉전하 피해 계수");
        AssertApproximately(2.5f, ElementalReactionRules.ColdChargeRadius, "냉전하 반경");
        AssertApproximately(2f, ElementalReactionRules.ColdChargeKnockback, "냉전하 넉백");
        AssertApproximately(0.5f, ElementalReactionRules.ChainFallbackDamageMultiplier, "연쇄감전 비상태 피해 배율");
        AssertApproximately(0.3f, ElementalReactionRules.CircularAreaMinimumDamageMultiplier, "원형 범위 최소 피해 배율");
        float radiusSquared = 100f;
        float[] falloffDistances = { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 10f };
        for (int i = 0; i < falloffDistances.Length; i++)
        {
            float distance = falloffDistances[i];
            float expected = Mathf.Lerp(
                1f,
                ElementalReactionRules.CircularAreaMinimumDamageMultiplier,
                Mathf.Clamp01(distance * distance / radiusSquared));
            AssertApproximately(
                expected,
                ElementalReactionRules.ResolveCircularAreaDamageMultiplier(distance * distance, radiusSquared),
                $"원형 범위 {distance * 10f:0}% 거리 피해 배율");
        }
        float before = ElementalReactionRules.ResolveCircularAreaDamageMultiplier(24.99f, radiusSquared);
        float after = ElementalReactionRules.ResolveCircularAreaDamageMultiplier(25.01f, radiusSquared);
        if (!(before > after) || Mathf.Abs(before - after) > 0.001f)
            throw new InvalidOperationException("원형 범위 피해 감쇠가 중간 지점에서 연속적이지 않습니다.");

        DamageInfo normalDamage = new DamageInfo(1f, Vector3.zero);
        if (normalDamage.elementalReactionType != ElementalReactionType.None)
            throw new InvalidOperationException("일반 피해의 반응 식별 기본값은 None이어야 합니다.");
    }

#if false
    private static void ValidateThermalFracture()
    {
        GameObject sourceRoot = CreateActor(
            "ThermalSource",
            CombatTeam.PlayerParty,
            out _,
            out _,
            out _);
        GameObject targetRoot = CreateActor(
            "ThermalTarget",
            CombatTeam.Enemy,
            out CombatHealth targetHealth,
            out CombatTarget target,
            out ElementalStatusController status);
        GameObject independentRoot = CreateActor(
            "ThermalIndependentTarget",
            CombatTeam.Enemy,
            out CombatHealth independentHealth,
            out _,
            out ElementalStatusController independentStatus);
        try
        {
            SetMaxHealth(targetHealth, 1000f);
            SetMaxHealth(independentHealth, 1000f);
            int reactionStartedCount = 0;
            int stateAppliedCount = 0;
            int stateRefreshedCount = 0;
            int stateRemovedCount = 0;
            void HandleStarted(ElementalReactionEvent reactionEvent)
            {
                if (reactionEvent.ReactionType == ElementalReactionType.ThermalFracture)
                    reactionStartedCount++;
            }
            void HandleStateChanged(
                ElementalReactionStateSnapshot _,
                ElementalReactionStateChangeReason reason)
            {
                if (reason == ElementalReactionStateChangeReason.Applied)
                    stateAppliedCount++;
                else if (reason == ElementalReactionStateChangeReason.Refreshed)
                    stateRefreshedCount++;
            }
            void HandleStateRemoved(
                ElementalReactionType reactionType,
                ElementalReactionStateRemoveReason _)
            {
                if (reactionType == ElementalReactionType.ThermalFracture)
                    stateRemovedCount++;
            }

            ElementalReactionEvents.ReactionStarted += HandleStarted;
            status.ReactionStateChanged += HandleStateChanged;
            status.ReactionStateRemoved += HandleStateRemoved;
            try
            {
                Apply(status, WeaponElement.Fire, sourceRoot);
                Apply(status, WeaponElement.Water, sourceRoot);
                Apply(status, WeaponElement.Electric, sourceRoot);
                float hpBeforeTrigger = targetHealth.CurrentHp;
                targetHealth.TakeDamage(new DamageInfo(
                    100f,
                    target.WorldCenter,
                    sourceRoot,
                    Vector3.forward,
                    element: WeaponElement.Ice,
                    sourceWeaponRuntimeInstanceId: "weapon_thermal"));
                AssertApproximately(100f, hpBeforeTrigger - targetHealth.CurrentHp, "열균열 발동 Hit 미증폭");

                if (status.HasStatus(WeaponElement.Fire)
                    || status.HasStatus(WeaponElement.Water)
                    || status.HasStatus(WeaponElement.Ice)
                    || status.HasStatus(WeaponElement.Electric))
                {
                    throw new InvalidOperationException("열균열 뒤 기본 상태 4종 또는 incoming 상태가 남았습니다.");
                }
                if (!status.TryGetReactionState(
                        ElementalReactionType.ThermalFracture,
                        out ElementalReactionStateSnapshot thermal)
                    || thermal.Owner.SourceActor != sourceRoot
                    || thermal.Owner.SourceWeaponRuntimeInstanceId != "weapon_thermal"
                    || thermal.Owner.IncomingElement != WeaponElement.Ice)
                {
                    throw new InvalidOperationException("열균열 결과 상태 또는 owner snapshot이 누락됐습니다.");
                }
                AssertApproximately(6f, thermal.RemainingDuration, "열균열 최초 지속시간");
                AssertApproximately(1.2f, thermal.IncomingDamageMultiplier, "열균열 비중첩 배율");
                if (reactionStartedCount != 1 || stateAppliedCount != 1)
                    throw new InvalidOperationException("열균열 시작 이벤트가 정확히 한 번 발행되지 않았습니다.");

                Apply(status, WeaponElement.Water, sourceRoot);
                if (!status.HasStatus(WeaponElement.Water))
                    throw new InvalidOperationException("열균열 지속 중 새 기본 상태가 차단됐습니다.");
                status.ClearAllStatuses(ElementalStatusClearReason.Explicit);
                Apply(status, WeaponElement.Fire, sourceRoot);
                ElementalApplicationResultType refreshResult = ElementalCombatProcessor.Process(CreateContext(
                    targetHealth,
                    target,
                    status,
                    WeaponElement.Ice,
                    sourceRoot,
                    "weapon_thermal_refresh"));
                if (refreshResult != ElementalApplicationResultType.ReactionApplied
                    || reactionStartedCount != 2
                    || stateRefreshedCount != 1
                    || !status.TryGetReactionState(ElementalReactionType.ThermalFracture, out thermal))
                {
                    throw new InvalidOperationException("열균열 재발동 갱신 또는 이벤트 계약이 누락됐습니다.");
                }
                AssertApproximately(6f, thermal.RemainingDuration, "열균열 갱신 지속시간");
                AssertApproximately(1.2f, thermal.IncomingDamageMultiplier, "열균열 갱신 비중첩 배율");

                AssertAmplifiedDamage(targetHealth, sourceRoot, false, false, "이후 직접 피해");
                AssertAmplifiedDamage(targetHealth, sourceRoot, true, false, "상태 DoT 피해");
                AssertAmplifiedDamage(targetHealth, sourceRoot, false, true, "비재귀 반응 피해");

                Apply(independentStatus, WeaponElement.Ice, sourceRoot);
                independentHealth.TakeDamage(new DamageInfo(
                    10f,
                    independentRoot.transform.position,
                    sourceRoot,
                    Vector3.forward,
                    element: WeaponElement.Fire));
                if (!independentStatus.TryGetReactionState(ElementalReactionType.ThermalFracture, out _))
                    throw new InvalidOperationException("두 번째 대상의 열균열이 독립 적용되지 않았습니다.");
                status.ClearAllReactionStates(ElementalStatusClearReason.Explicit);
                if (status.TryGetReactionState(ElementalReactionType.ThermalFracture, out _)
                    || !independentStatus.TryGetReactionState(ElementalReactionType.ThermalFracture, out _))
                {
                    throw new InvalidOperationException("다수 대상 열균열 생명주기가 서로 간섭했습니다.");
                }

                Apply(status, WeaponElement.Fire, sourceRoot);
                ElementalCombatProcessor.Process(CreateContext(
                    targetHealth,
                    target,
                    status,
                    WeaponElement.Ice,
                    sourceRoot,
                    "weapon_thermal_expire"));
                status.AdvanceReactionStatesForValidation(Time.time + 6.01f);
                if (status.TryGetReactionState(ElementalReactionType.ThermalFracture, out _)
                    || stateRemovedCount < 2)
                {
                    throw new InvalidOperationException("열균열 자연 만료 또는 종료 이벤트가 누락됐습니다.");
                }

                Apply(status, WeaponElement.Ice, sourceRoot);
                ElementalCombatProcessor.Process(CreateContext(
                    targetHealth,
                    target,
                    status,
                    WeaponElement.Fire,
                    sourceRoot,
                    "weapon_thermal_reset"));
                targetHealth.ResetHealth();
                status.ClearAllReactionStates(ElementalStatusClearReason.Reset); // EditMode 생명주기 대체
                if (status.TryGetReactionState(ElementalReactionType.ThermalFracture, out _))
                    throw new InvalidOperationException("Reset 뒤 열균열이 남았습니다.");
            }
            finally
            {
                ElementalReactionEvents.ReactionStarted -= HandleStarted;
                status.ReactionStateChanged -= HandleStateChanged;
                status.ReactionStateRemoved -= HandleStateRemoved;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(independentRoot);
            UnityEngine.Object.DestroyImmediate(targetRoot);
            UnityEngine.Object.DestroyImmediate(sourceRoot);
        }
    }

    private static void AssertAmplifiedDamage(
        CombatHealth health,
        GameObject source,
        bool isDamageOverTime,
        bool isReactionDamage,
        string label)
    {
        float hpBefore = health.CurrentHp;
        health.TakeDamage(new DamageInfo(
            10f,
            health.transform.position,
            source,
            Vector3.zero,
            0f,
            false,
            !isDamageOverTime && !isReactionDamage,
            isDamageOverTime,
            default,
            true,
            WeaponElement.None,
            "weapon_amplified",
            isReactionDamage ? ElementalReactionType.Vaporize : ElementalReactionType.None));
        AssertApproximately(12f, hpBefore - health.CurrentHp, label);
    }

#endif

    private static void ValidateThermalFracture()
    {
        GameObject sourceRoot = CreateActor("FractureSource", CombatTeam.PlayerParty, out _, out _, out _);
        GameObject targetRoot = CreateActor(
            "FractureTarget",
            CombatTeam.Enemy,
            out CombatHealth targetHealth,
            out CombatTarget target,
            out ElementalStatusController status);
        try
        {
            SetMaxHealth(targetHealth, 1000f);
            int fractureStarted = 0;
            int fractureDamageCount = 0;
            void HandleStarted(ElementalReactionEvent reactionEvent)
            {
                if (reactionEvent.ReactionType == ElementalReactionType.ThermalFracture)
                    fractureStarted++;
            }
            void HandleDamaged(CombatHealth _, DamageInfo info)
            {
                if (info.elementalReactionType == ElementalReactionType.ThermalFracture)
                    fractureDamageCount++;
            }

            ElementalReactionEvents.ReactionStarted += HandleStarted;
            targetHealth.OnDamaged += HandleDamaged;
            try
            {
                Apply(status, WeaponElement.Fire, sourceRoot);
                float beforeIce = targetHealth.CurrentHp;
                targetHealth.TakeDamage(new DamageInfo(
                    100f,
                    target.WorldCenter,
                    sourceRoot,
                    Vector3.forward,
                    element: WeaponElement.Ice,
                    sourceWeaponRuntimeInstanceId: "fracture_ice"));
                AssertApproximately(130f, beforeIce - targetHealth.CurrentHp, "연소+냉기 직접·균열 피해");
                RequireNoBasicStatuses(status, "연소+냉기 균열");
                if (status.TryGetReactionState(ElementalReactionType.ThermalFracture, out _))
                    throw new InvalidOperationException("균열이 지속 디버프 상태를 생성했습니다.");

                targetHealth.ResetHealth();
                status.ClearAllStatuses(ElementalStatusClearReason.Explicit);
                Apply(status, WeaponElement.Ice, sourceRoot);
                float beforeFire = targetHealth.CurrentHp;
                targetHealth.TakeDamage(new DamageInfo(
                    100f,
                    target.WorldCenter,
                    sourceRoot,
                    Vector3.forward,
                    element: WeaponElement.Fire,
                    sourceWeaponRuntimeInstanceId: "fracture_fire"));
                AssertApproximately(130f, beforeFire - targetHealth.CurrentHp, "냉각+불 직접·균열 피해");
                RequireNoBasicStatuses(status, "냉각+불 균열");

                if (fractureStarted != 2 || fractureDamageCount != 2)
                    throw new InvalidOperationException("균열 시작 또는 별도 추가 피해 플로팅 경계가 누락됐습니다.");
            }
            finally
            {
                ElementalReactionEvents.ReactionStarted -= HandleStarted;
                targetHealth.OnDamaged -= HandleDamaged;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetRoot);
            UnityEngine.Object.DestroyImmediate(sourceRoot);
        }
    }

    private static void RequireNoBasicStatuses(ElementalStatusController status, string label)
    {
        if (status.HasStatus(WeaponElement.Fire)
            || status.HasStatus(WeaponElement.Water)
            || status.HasStatus(WeaponElement.Ice)
            || status.HasStatus(WeaponElement.Electric))
        {
            throw new InvalidOperationException(label + " 뒤 기본 상태가 남았습니다.");
        }
    }

    private static void ValidateStoredReactionResults()
    {
        GameObject sourceRoot = CreateActor("StoredProcSource", CombatTeam.PlayerParty, out CombatHealth sourceHealth, out _, out _);
        GameObject targetRoot = CreateActor("StoredProcTarget", CombatTeam.Enemy, out CombatHealth targetHealth, out CombatTarget target, out ElementalStatusController status);
        GameObject nearRoot = CreateActor("StoredProcNear", CombatTeam.Enemy, out CombatHealth nearHealth, out CombatTarget nearTarget, out _);
        GameObject farRoot = CreateActor("StoredProcFar", CombatTeam.Enemy, out CombatHealth farHealth, out _, out _);
        nearRoot.transform.position = Vector3.right;
        farRoot.transform.position = Vector3.right * 4f;
        try
        {
            SetMaxHealth(sourceHealth, 2000f);
            SetMaxHealth(targetHealth, 2000f);
            SetMaxHealth(nearHealth, 2000f);
            SetMaxHealth(farHealth, 2000f);
            int plasmaStarted = 0;
            int freezeStarted = 0;
            int plasmaDamageCount = 0;
            int shatterDamageCount = 0;
            float plasmaKnockback = 0f;
            Vector3 plasmaDirection = Vector3.zero;
            var procOrder = new System.Collections.Generic.List<ElementalReactionProcType>();
            void HandleStarted(ElementalReactionEvent reactionEvent)
            {
                if (reactionEvent.ReactionType == ElementalReactionType.Plasma) plasmaStarted++;
                if (reactionEvent.ReactionType == ElementalReactionType.Freeze) freezeStarted++;
            }
            void HandleProc(ElementalReactionProcEvent procEvent) { procOrder.Add(procEvent.ProcType); }
            void HandleDamage(CombatHealth _, DamageInfo info)
            {
                if (info.elementalReactionType == ElementalReactionType.Plasma)
                {
                    if (info.triggersOnHitEffects || info.isDamageOverTime)
                        throw new InvalidOperationException("플라즈마 거리 감쇠 피해의 비재귀 DamageInfo 계약이 잘못됐습니다.");
                    plasmaDamageCount++;
                    plasmaKnockback = info.knockback;
                    plasmaDirection = info.direction;
                }
                if (info.elementalReactionType == ElementalReactionType.Shatter)
                {
                    if (info.triggersOnHitEffects || info.isDamageOverTime)
                        throw new InvalidOperationException("쇄빙 단일 피해의 비재귀 DamageInfo 계약이 잘못됐습니다.");
                    shatterDamageCount++;
                }
            }

            ElementalReactionEvents.ReactionStarted += HandleStarted;
            ElementalReactionEvents.ReactionProcExecuted += HandleProc;
            targetHealth.OnDamaged += HandleDamage;
            try
            {
                Apply(status, WeaponElement.Fire, sourceRoot);
                float hpBeforePlasmaCreation = targetHealth.CurrentHp;
                targetHealth.TakeDamage(new DamageInfo(
                    100f, target.WorldCenter, sourceRoot, Vector3.forward,
                    element: WeaponElement.Electric,
                    sourceWeaponRuntimeInstanceId: "plasma_create"));
                AssertApproximately(100f, hpBeforePlasmaCreation - targetHealth.CurrentHp, "플라즈마 생성 Hit 즉시 소비 금지");
                if (!status.TryGetReactionState(ElementalReactionType.Plasma, out ElementalReactionStateSnapshot plasma)
                    || procOrder.Count != 0
                    || plasmaStarted != 1)
                    throw new InvalidOperationException("플라즈마 생성 상태 또는 시작 이벤트가 누락됐습니다.");
                AssertApproximately(100f, plasma.StoredDamage, "플라즈마 저장 P");

                Apply(status, WeaponElement.Fire, sourceRoot);
                ElementalCombatProcessor.Process(CreateContext(
                    targetHealth, target, status, WeaponElement.Electric, sourceRoot, "plasma_replace", 200f));
                if (!status.TryGetReactionState(ElementalReactionType.Plasma, out plasma)
                    || plasma.Owner.SourceWeaponRuntimeInstanceId != "plasma_replace")
                    throw new InvalidOperationException("플라즈마 최신 owner 교체가 누락됐습니다.");
                AssertApproximately(200f, plasma.StoredDamage, "플라즈마 최신 P 교체");

                Apply(status, WeaponElement.Water, sourceRoot);
                float targetBeforeProc = targetHealth.CurrentHp;
                float nearBeforeProc = nearHealth.CurrentHp;
                float farBeforeProc = farHealth.CurrentHp;
                targetHealth.TakeDamage(new DamageInfo(
                    50f, target.WorldCenter, sourceRoot, Vector3.forward,
                    element: WeaponElement.Fire,
                    sourceWeaponRuntimeInstanceId: "plasma_trigger"));
                AssertApproximately(132.5f, targetBeforeProc - targetHealth.CurrentHp, "플라즈마 뒤 증발 단일 피해 중심 대상 포함");
                float expectedNearStoredDamage =
                    ElementalReactionRules.ResolveCircularAreaDamage(
                        60f, target.WorldCenter, nearTarget.WorldCenter, ElementalReactionRules.PlasmaRadius)
                    + ElementalReactionRules.ResolveCircularAreaDamage(
                        22.5f, target.WorldCenter, nearTarget.WorldCenter, ElementalReactionRules.VaporizeRadius);
                AssertWithin(expectedNearStoredDamage, nearBeforeProc - nearHealth.CurrentHp, 0.001f, "플라즈마·증발 연속 거리 감쇠 반경 적대 대상");
                AssertApproximately(0f, farBeforeProc - farHealth.CurrentHp, "플라즈마 반경 밖 제외");
                if (status.TryGetReactionState(ElementalReactionType.Plasma, out _)
                    || plasmaDamageCount != 1
                    || procOrder.Count != 1
                    || procOrder[0] != ElementalReactionProcType.Plasma)
                    throw new InvalidOperationException("플라즈마 소비 또는 소비 뒤 신규 증발 판정이 누락됐습니다.");
                AssertApproximately(2.5f, plasmaKnockback, "플라즈마 넉백");
                if (plasmaDirection.sqrMagnitude <= 0.9f)
                    throw new InvalidOperationException("플라즈마 중심 대상 안전 방향이 누락됐습니다.");

                status.ClearAllStatuses(ElementalStatusClearReason.Explicit);
                Apply(status, WeaponElement.Water, sourceRoot);
                int procCountBeforeFreeze = procOrder.Count;
                float hpBeforeFreezeCreation = targetHealth.CurrentHp;
                targetHealth.TakeDamage(new DamageInfo(
                    100f, target.WorldCenter, sourceRoot, Vector3.forward,
                    element: WeaponElement.Ice,
                    sourceWeaponRuntimeInstanceId: "freeze_create"));
                AssertApproximately(100f, hpBeforeFreezeCreation - targetHealth.CurrentHp, "빙결 생성 Hit 즉시 쇄빙 금지");
                if (!status.TryGetReactionState(ElementalReactionType.Freeze, out ElementalReactionStateSnapshot freeze)
                    || procOrder.Count != procCountBeforeFreeze
                    || freezeStarted != 1)
                    throw new InvalidOperationException("빙결 생성 상태 또는 시작 이벤트가 누락됐습니다.");
                AssertApproximately(3f, freeze.RemainingDuration, "빙결 지속시간");
                AssertApproximately(0f, status.MoveSpeedMultiplier, "빙결 이동 정지");
                AssertApproximately(0f, status.ActionSpeedMultiplier, "빙결 행동 정지");

                Apply(status, WeaponElement.Water, sourceRoot);
                ElementalCombatProcessor.Process(CreateContext(
                    targetHealth, target, status, WeaponElement.Ice, sourceRoot, "freeze_refresh", 120f));
                if (!status.TryGetReactionState(ElementalReactionType.Freeze, out freeze)
                    || freeze.Owner.SourceWeaponRuntimeInstanceId != "freeze_refresh"
                    || freezeStarted != 2)
                    throw new InvalidOperationException("빙결 갱신 또는 owner 교체가 누락됐습니다.");
                AssertApproximately(3f, freeze.RemainingDuration, "빙결 갱신 지속시간");

                float hpBeforeShatter = targetHealth.CurrentHp;
                targetHealth.TakeDamage(new DamageInfo(
                    50f, target.WorldCenter, sourceRoot, Vector3.forward,
                    sourceWeaponRuntimeInstanceId: "shatter_trigger"));
                AssertApproximately(70f, hpBeforeShatter - targetHealth.CurrentHp, "쇄빙 B×40% 단일 피해");
                if (status.TryGetReactionState(ElementalReactionType.Freeze, out _)
                    || shatterDamageCount != 1
                    || status.MoveSpeedMultiplier != 1f
                    || status.ActionSpeedMultiplier != 1f)
                    throw new InvalidOperationException("쇄빙 소비 또는 제어 배율 원복이 누락됐습니다.");

                Apply(status, WeaponElement.Water, sourceRoot);
                ElementalCombatProcessor.Process(CreateContext(
                    targetHealth, target, status, WeaponElement.Ice, sourceRoot, "freeze_expire"));
                int shatterBeforeExpire = shatterDamageCount;
                status.AdvanceReactionStatesForValidation(Time.time + 3.01f);
                if (status.TryGetReactionState(ElementalReactionType.Freeze, out _)
                    || shatterDamageCount != shatterBeforeExpire
                    || status.MoveSpeedMultiplier != 1f)
                    throw new InvalidOperationException("빙결 자연 만료 또는 무쇄빙 계약이 누락됐습니다.");

                ElementalApplicationContext storedOwnerContext = CreateContext(
                    targetHealth, target, status, WeaponElement.Fire, sourceRoot, "simultaneous_owner", 100f);
                ElementalReactionOwnerSnapshot storedOwner = new ElementalReactionOwnerSnapshot(storedOwnerContext);
                status.TryApplyReactionState(new ElementalReactionStateApplication(
                    ElementalReactionType.Plasma, 0f, 1f, storedOwner, 100f));
                status.TryApplyReactionState(new ElementalReactionStateApplication(
                    ElementalReactionType.Freeze, 3f, 1f, storedOwner, 0f, 0f, 0f));
                status.TryApplyReactionState(new ElementalReactionStateApplication(
                    ElementalReactionType.ColdCharge, 6f, 1f, storedOwner));
                procOrder.Clear();
                targetHealth.TakeDamage(new DamageInfo(10f, target.WorldCenter, sourceRoot, Vector3.forward));
                if (procOrder.Count != 3
                    || procOrder[0] != ElementalReactionProcType.Shatter
                    || procOrder[1] != ElementalReactionProcType.Plasma
                    || procOrder[2] != ElementalReactionProcType.ColdCharge)
                    throw new InvalidOperationException("동시 저장 proc의 쇄빙→플라즈마 고정 순서가 깨졌습니다.");
            }
            finally
            {
                ElementalReactionEvents.ReactionStarted -= HandleStarted;
                ElementalReactionEvents.ReactionProcExecuted -= HandleProc;
                targetHealth.OnDamaged -= HandleDamage;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(farRoot);
            UnityEngine.Object.DestroyImmediate(nearRoot);
            UnityEngine.Object.DestroyImmediate(targetRoot);
            UnityEngine.Object.DestroyImmediate(sourceRoot);
        }
    }

    private static void SetMaxHealth(CombatHealth health, float maxHealth)
    {
        SerializedObject serialized = new SerializedObject(health);
        serialized.FindProperty("maxHp").floatValue = maxHealth;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        health.ResetHealth();
    }

    private static void ValidateFreezeConsumerLock()
    {
        GameObject root = new GameObject("FreezeConsumerLockTarget");
        try
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            CombatHealth health = root.AddComponent<CombatHealth>();
            SetMaxHealth(health, 100f);
            root.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
            CombatTarget target = root.AddComponent<CombatTarget>();
            target.Configure(CombatTeam.Enemy, false);
            EnemyAnimationBridge animationBridge = root.AddComponent<EnemyAnimationBridge>();
            EnemyMovement movement = root.AddComponent<EnemyMovement>();
            EnemyMotor motor = root.GetComponent<EnemyMotor>();
            EnemyLocomotionAnimator locomotionAnimator = root.GetComponent<EnemyLocomotionAnimator>();
            EnemyMeleeAttackController attack = root.AddComponent<EnemyMeleeAttackController>();
            ElementalStatusController status = root.AddComponent<ElementalStatusController>();
            movement.ResolveReferences();

            ElementalApplicationContext ownerContext = CreateContext(
                health,
                target,
                status,
                WeaponElement.Ice,
                root,
                "freeze_consumer_lock");
            ElementalReactionOwnerSnapshot owner = new ElementalReactionOwnerSnapshot(ownerContext);
            ElementalReactionStateApplication freezeApplication = new ElementalReactionStateApplication(
                    ElementalReactionType.Freeze,
                    ElementalReactionRules.FreezeDuration,
                    1f,
                    owner,
                    0f,
                    0f,
                    0f);
            if (!status.TryApplyReactionState(freezeApplication))
            {
                throw new InvalidOperationException("빙결 소비자 하드락 상태 적용에 실패했습니다.");
            }

            AssertApproximately(0f, movement.StatusMoveSpeedMultiplier, "빙결 이동 소비자 0배율");
            AssertApproximately(0f, attack.StatusActionSpeedMultiplier, "빙결 공격 소비자 0배율");
            if (!movement.IsStatusMovementLocked
                || motor == null
                || !motor.IsFrozen
                || locomotionAnimator == null
                || !locomotionAnimator.IsFrozen
                || !animationBridge.IsFrozen)
            {
                throw new InvalidOperationException("빙결 이동·애니메이션 하드락이 소비자 전체에 전달되지 않았습니다.");
            }
            if ((body.constraints & RigidbodyConstraints.FreezeRotationY) == 0
                || (body.constraints & RigidbodyConstraints.FreezePositionX) == 0
                || (body.constraints & RigidbodyConstraints.FreezePositionZ) == 0)
            {
                throw new InvalidOperationException("빙결 Rigidbody 위치·Y회전 제약이 적용되지 않았습니다.");
            }

            Quaternion frozenRotation = root.transform.rotation;
            movement.FacePosition(root.transform.position + Vector3.right);
            motor.Face(Vector3.right, 720f);
            if (root.transform.rotation != frozenRotation)
                throw new InvalidOperationException("빙결 중 회전 명령이 대상을 회전시켰습니다.");
            if (attack.TryStartAttack(root.transform))
                throw new InvalidOperationException("빙결 중 공격 시작이 허용됐습니다.");

            status.ClearAllReactionStates(ElementalStatusClearReason.Explicit);
            AssertApproximately(1f, movement.StatusMoveSpeedMultiplier, "빙결 해제 이동 배율 복원");
            AssertApproximately(1f, attack.StatusActionSpeedMultiplier, "빙결 해제 공격 배율 복원");
            if (movement.IsStatusMovementLocked
                || motor.IsFrozen
                || locomotionAnimator.IsFrozen
                || animationBridge.IsFrozen
                || (body.constraints & RigidbodyConstraints.FreezeRotationY) != 0)
            {
                throw new InvalidOperationException("빙결 해제 후 이동·애니메이션·회전 하드락이 남았습니다.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ValidateChainAndColdCharge()
    {
        ElementalReactionTargetQuery.ResetSharedFrameCacheForValidation();
        GameObject sourceRoot = CreateActor("ChainColdSource", CombatTeam.PlayerParty, out _, out _, out _);
        GameObject primaryRoot = CreateActor(
            "ChainColdPrimary",
            CombatTeam.Enemy,
            out CombatHealth primaryHealth,
            out CombatTarget primaryTarget,
            out ElementalStatusController primaryStatus);
        const int candidateCount = 6;
        GameObject[] candidateRoots = new GameObject[candidateCount];
        CombatHealth[] candidateHealths = new CombatHealth[candidateCount];
        CombatTarget[] candidateTargets = new CombatTarget[candidateCount];
        ElementalStatusController[] candidateStatuses = new ElementalStatusController[candidateCount];
        try
        {
            SetMaxHealth(primaryHealth, 2000f);
            for (int i = 0; i < candidateCount; i++)
            {
                candidateRoots[i] = CreateActor(
                    $"ChainCandidate_{i}",
                    CombatTeam.Enemy,
                    out candidateHealths[i],
                    out candidateTargets[i],
                    out candidateStatuses[i]);
                SetMaxHealth(candidateHealths[i], 2000f);
                float[] chainPositions = { 2f, 4f, 5f, 7f, 8f, 10f };
                candidateRoots[i].transform.position = Vector3.right * chainPositions[i];
                CombatTargetRegistry.NotifySpatialChanged(candidateTargets[i]);
                if (i < 2)
                    Apply(candidateStatuses[i], WeaponElement.Electric, sourceRoot);
            }

            var hops = new System.Collections.Generic.List<ElementalReactionChainHopEvent>();
            int chainStarted = 0;
            int coldStarted = 0;
            int coldProcCount = 0;
            int coldDamageCount = 0;
            void HandleStarted(ElementalReactionEvent reactionEvent)
            {
                if (reactionEvent.ReactionType == ElementalReactionType.ChainElectricity) chainStarted++;
                if (reactionEvent.ReactionType == ElementalReactionType.ColdCharge) coldStarted++;
            }
            void HandleHop(ElementalReactionChainHopEvent hopEvent) { hops.Add(hopEvent); }
            void HandleProc(ElementalReactionProcEvent procEvent)
            {
                if (procEvent.ProcType == ElementalReactionProcType.ColdCharge) coldProcCount++;
            }
            void HandlePrimaryDamaged(CombatHealth _, DamageInfo info)
            {
                if (info.elementalReactionType != ElementalReactionType.ColdCharge)
                    return;
                if (info.triggersOnHitEffects || info.isDamageOverTime)
                    throw new InvalidOperationException("냉전하 거리 감쇠 피해의 비재귀 DamageInfo 계약이 잘못됐습니다.");
                AssertApproximately(ElementalReactionRules.ColdChargeKnockback, info.knockback, "냉전하 넉백");
                coldDamageCount++;
            }

            ElementalReactionEvents.ReactionStarted += HandleStarted;
            ElementalReactionEvents.ChainHopExecuted += HandleHop;
            ElementalReactionEvents.ReactionProcExecuted += HandleProc;
            primaryHealth.OnDamaged += HandlePrimaryDamaged;
            try
            {
                Apply(primaryStatus, WeaponElement.Electric, sourceRoot);
                float primaryBefore = primaryHealth.CurrentHp;
                primaryHealth.TakeDamage(new DamageInfo(
                    100f,
                    primaryTarget.WorldCenter,
                    sourceRoot,
                    Vector3.forward,
                    element: WeaponElement.Water,
                    sourceWeaponRuntimeInstanceId: "chain_weapon"));
                AssertApproximately(200f, primaryBefore - primaryHealth.CurrentHp, "연쇄감전 최초 직접+추가 피해");
                if (chainStarted != 1 || hops.Count != 5)
                    throw new InvalidOperationException("연쇄감전 시작 1회 또는 최대 5 hop 계약이 누락됐습니다.");
                float[] expectedChainCoefficients =
                {
                    1f,
                    0.2f,
                    0.15f,
                    0.1f * ElementalReactionRules.ChainFallbackDamageMultiplier,
                    0.05f * ElementalReactionRules.ChainFallbackDamageMultiplier
                };
                for (int i = 0; i < hops.Count; i++)
                {
                    AssertApproximately(expectedChainCoefficients[i], hops[i].Coefficient, $"연쇄감전 hop {i} 계수");
                    if (hops[i].HopIndex != i || hops[i].SequenceId != hops[0].SequenceId)
                        throw new InvalidOperationException("연쇄감전 hop 순번 또는 sequence가 일관되지 않습니다.");
                }

                if (hops[1].Target != candidateTargets[0]
                    || hops[2].Target != candidateTargets[1]
                    || hops[3].Target != candidateTargets[2]
                    || hops[4].Target != candidateTargets[3])
                {
                    throw new InvalidOperationException("연쇄감전 최단거리 상태 우선·비상태 fallback 순서가 깨졌습니다.");
                }
                if (!primaryStatus.TryGetStatus(WeaponElement.Electric, out _)
                    || !candidateStatuses[0].TryGetStatus(WeaponElement.Electric, out _)
                    || !candidateStatuses[1].TryGetStatus(WeaponElement.Electric, out _))
                {
                    throw new InvalidOperationException("연쇄감전 호스트·후속 대상의 감전 상태가 소비됐습니다.");
                }
                for (int i = 0; i < candidateTargets.Length; i++)
                {
                    IElementalStatusReceiver first = candidateTargets[i].ElementalStatusReceiver;
                    IElementalStatusReceiver second = candidateTargets[i].ElementalStatusReceiver;
                    if (!ReferenceEquals(first, candidateStatuses[i])
                        || !ReferenceEquals(first, second)
                        || candidateTargets[i].ElementalStatusReceiverResolveCountForValidation != 1)
                    {
                        throw new InvalidOperationException("연쇄감전 상태 Receiver가 CombatTarget에서 1회 캐싱되지 않았습니다.");
                    }
                }

                candidateRoots[0].transform.position = Vector3.right;
                CombatTargetRegistry.NotifySpatialChanged(candidateTargets[0]);
                primaryStatus.ClearAllStatuses(ElementalStatusClearReason.Explicit);
                primaryStatus.ClearAllReactionStates(ElementalStatusClearReason.Explicit);
                Apply(primaryStatus, WeaponElement.Electric, sourceRoot);
                ElementalApplicationResultType coldResult = ElementalCombatProcessor.Process(CreateContext(
                    primaryHealth,
                    primaryTarget,
                    primaryStatus,
                    WeaponElement.Ice,
                    sourceRoot,
                    "cold_owner"));
                if (coldResult != ElementalApplicationResultType.ReactionApplied
                    || coldStarted != 1
                    || coldProcCount != 0
                    || !primaryStatus.TryGetReactionState(ElementalReactionType.ColdCharge, out ElementalReactionStateSnapshot cold))
                {
                    throw new InvalidOperationException("냉전하 생성 또는 생성 Hit proc 금지 계약이 누락됐습니다.");
                }
                AssertApproximately(6f, cold.RemainingDuration, "냉전하 최초 지속시간");
                ElementalReactionOwnerSnapshot refreshedColdOwner = new ElementalReactionOwnerSnapshot(CreateContext(
                    primaryHealth,
                    primaryTarget,
                    primaryStatus,
                    WeaponElement.Ice,
                    sourceRoot,
                    "cold_owner_refresh"));
                if (!primaryStatus.TryApplyReactionState(new ElementalReactionStateApplication(
                        ElementalReactionType.ColdCharge,
                        ElementalReactionRules.ColdChargeDuration,
                        1f,
                        refreshedColdOwner))
                    || !primaryStatus.TryGetReactionState(ElementalReactionType.ColdCharge, out cold)
                    || cold.Owner.SourceWeaponRuntimeInstanceId != "cold_owner_refresh")
                {
                    throw new InvalidOperationException("냉전하 비중첩 시간·owner 갱신이 누락됐습니다.");
                }
                AssertApproximately(6f, cold.RemainingDuration, "냉전하 갱신 지속시간");
                if (primaryStatus.TryApplyDirectHit(new ElementalStatusApplication(
                        WeaponElement.Fire,
                        10f,
                        sourceRoot,
                        "blocked_status",
                        true,
                        false,
                        primaryTarget.WorldCenter,
                        Vector3.forward)))
                {
                    throw new InvalidOperationException("냉전하 중 기본 원소 상태가 부여됐습니다.");
                }

                float beforeColdProc = primaryHealth.CurrentHp;
                float beforeColdNear = candidateHealths[0].CurrentHp;
                primaryHealth.TakeDamage(new DamageInfo(
                    100f,
                    primaryTarget.WorldCenter,
                    sourceRoot,
                    Vector3.forward,
                    sourceWeaponRuntimeInstanceId: "cold_trigger"));
                AssertApproximately(105f, beforeColdProc - primaryHealth.CurrentHp, "냉전하 중심 직접+5% 피해");
                float expectedColdNearDamage = ElementalReactionRules.ResolveCircularAreaDamage(
                    5f,
                    primaryTarget.WorldCenter,
                    candidateTargets[0].WorldCenter,
                    ElementalReactionRules.ColdChargeRadius);
                AssertWithin(expectedColdNearDamage, beforeColdNear - candidateHealths[0].CurrentHp, 0.001f, "냉전하 1m 대상 연속 거리 감쇠");
                if (coldProcCount != 1
                    || coldDamageCount != 1
                    || !primaryStatus.TryGetReactionState(ElementalReactionType.ColdCharge, out _))
                    throw new InvalidOperationException("냉전하 반복 proc 또는 비소비 지속 계약이 누락됐습니다.");

                primaryHealth.TakeDamage(new DamageInfo(
                    100f,
                    primaryTarget.WorldCenter,
                    sourceRoot,
                    Vector3.forward,
                    sourceWeaponRuntimeInstanceId: "cold_trigger_2"));
                if (coldProcCount != 2)
                    throw new InvalidOperationException("냉전하 무쿨다운 유효 Hit별 proc가 누락됐습니다.");
                if (ElementalReactionTargetQuery.SharedFrameSpatialQueryCountForValidation != 1
                    || ElementalReactionTargetQuery.SharedFrameCacheHitCountForValidation < 1)
                {
                    throw new InvalidOperationException("냉전하 동일 대상·동일 프레임 후보 검색이 공유되지 않았습니다.");
                }

                candidateRoots[0].transform.position += Vector3.right * 20f;
                CombatTargetRegistry.NotifySpatialChanged(candidateTargets[0]);
                primaryHealth.TakeDamage(new DamageInfo(
                    100f,
                    primaryTarget.WorldCenter,
                    sourceRoot,
                    Vector3.forward,
                    sourceWeaponRuntimeInstanceId: "cold_trigger_after_spatial_change"));
                if (coldProcCount != 3
                    || ElementalReactionTargetQuery.SharedFrameSpatialQueryCountForValidation != 2)
                {
                    throw new InvalidOperationException("냉전하 후보 캐시가 Spatial 변경 뒤 무효화되지 않았습니다.");
                }

                primaryStatus.AdvanceReactionStatesForValidation(Time.time + 6.01f);
                if (primaryStatus.TryGetReactionState(ElementalReactionType.ColdCharge, out _))
                    throw new InvalidOperationException("냉전하 자연 만료가 누락됐습니다.");
                Apply(primaryStatus, WeaponElement.Fire, sourceRoot);
            }
            finally
            {
                ElementalReactionEvents.ReactionStarted -= HandleStarted;
                ElementalReactionEvents.ChainHopExecuted -= HandleHop;
                ElementalReactionEvents.ReactionProcExecuted -= HandleProc;
                primaryHealth.OnDamaged -= HandlePrimaryDamaged;
            }
        }
        finally
        {
            for (int i = candidateRoots.Length - 1; i >= 0; i--)
            {
                if (candidateRoots[i] != null)
                    UnityEngine.Object.DestroyImmediate(candidateRoots[i]);
            }
            UnityEngine.Object.DestroyImmediate(primaryRoot);
            UnityEngine.Object.DestroyImmediate(sourceRoot);
        }
    }

    private static void ValidateProcessorAndVaporize()
    {
        GameObject sourceRoot = CreateActor("ReactionSource", CombatTeam.PlayerParty, out _, out _, out _);
        GameObject targetRoot = CreateActor("ReactionTarget", CombatTeam.Enemy, out CombatHealth targetHealth, out CombatTarget target, out ElementalStatusController status);
        GameObject nearRoot = CreateActor("ReactionNear", CombatTeam.Enemy, out CombatHealth nearHealth, out CombatTarget nearTarget, out _);
        GameObject farRoot = CreateActor("ReactionFar", CombatTeam.Enemy, out CombatHealth farHealth, out _, out _);
        GameObject allyRoot = CreateActor("ReactionAlly", CombatTeam.PlayerParty, out CombatHealth allyHealth, out _, out _);
        nearRoot.transform.position = Vector3.right;
        farRoot.transform.position = Vector3.right * 4f;
        allyRoot.transform.position = Vector3.left;
        try
        {
            SetMaxHealth(targetHealth, 2000f);
            SetMaxHealth(nearHealth, 2000f);
            SetMaxHealth(farHealth, 2000f);
            SetMaxHealth(allyHealth, 2000f);
            Apply(status, WeaponElement.Fire, sourceRoot);
            Apply(status, WeaponElement.Water, sourceRoot);
            Apply(status, WeaponElement.Ice, sourceRoot);
            Apply(status, WeaponElement.Electric, sourceRoot);

            ElementalReactionEvent started = default;
            int startedCount = 0;
            int targetDamageCount = 0;
            int nearDamageCount = 0;
            void HandleStarted(ElementalReactionEvent reactionEvent)
            {
                started = reactionEvent;
                startedCount++;
            }
            void ValidateVaporizeDamage(DamageInfo info)
            {
                if (info.elementalReactionType != ElementalReactionType.Vaporize
                    || info.triggersOnHitEffects
                    || info.isDamageOverTime)
                {
                    throw new InvalidOperationException("증발 단일 피해의 비재귀 DamageInfo 계약이 잘못됐습니다.");
                }
            }
            void HandleTargetDamaged(CombatHealth _, DamageInfo info)
            {
                ValidateVaporizeDamage(info);
                targetDamageCount++;
            }
            void HandleNearDamaged(CombatHealth _, DamageInfo info)
            {
                ValidateVaporizeDamage(info);
                nearDamageCount++;
            }

            ElementalReactionEvents.ReactionStarted += HandleStarted;
            targetHealth.OnDamaged += HandleTargetDamaged;
            nearHealth.OnDamaged += HandleNearDamaged;
            try
            {
                float targetBefore = targetHealth.CurrentHp;
                float nearBefore = nearHealth.CurrentHp;
                float farBefore = farHealth.CurrentHp;
                float allyBefore = allyHealth.CurrentHp;
                ElementalApplicationResultType result = ElementalCombatProcessor.Process(CreateContext(
                    targetHealth,
                    target,
                    status,
                    WeaponElement.Fire,
                    sourceRoot,
                    "weapon_vaporize"));
                if (result != ElementalApplicationResultType.ReactionApplied || startedCount != 1)
                    throw new InvalidOperationException("증발 단일 피해 또는 시작 이벤트가 누락됐습니다.");
                if (status.HasStatus(WeaponElement.Fire)
                    || status.HasStatus(WeaponElement.Water)
                    || status.HasStatus(WeaponElement.Ice)
                    || status.HasStatus(WeaponElement.Electric))
                {
                    throw new InvalidOperationException("증발 뒤 기본 상태 4종 또는 incoming 상태가 남았습니다.");
                }
                if (started.Owner.SourceActor != sourceRoot
                    || started.Owner.SourceWeaponRuntimeInstanceId != "weapon_vaporize"
                    || started.Owner.IncomingElement != WeaponElement.Fire)
                {
                    throw new InvalidOperationException("증발 source/무기/원소 snapshot이 유실됐습니다.");
                }

                AssertApproximately(45f, targetBefore - targetHealth.CurrentHp, "증발 중심 대상 45% 단일 피해");
                float expectedNearVaporizeDamage = ElementalReactionRules.ResolveCircularAreaDamage(
                    45f,
                    target.WorldCenter,
                    nearTarget.WorldCenter,
                    ElementalReactionRules.VaporizeRadius);
                AssertWithin(expectedNearVaporizeDamage, nearBefore - nearHealth.CurrentHp, 0.001f, "증발 1m 대상 연속 거리 감쇠");
                AssertApproximately(0f, farBefore - farHealth.CurrentHp, "증발 반경 밖 제외");
                AssertApproximately(0f, allyBefore - allyHealth.CurrentHp, "증발 아군 제외");
                if (targetDamageCount != 1 || nearDamageCount != 1)
                    throw new InvalidOperationException("증발 피해가 대상별 정확히 한 번 적용되지 않았습니다.");

                Apply(status, WeaponElement.Water, sourceRoot);
                ElementalApplicationResultType statusResult = ElementalCombatProcessor.Process(CreateContext(
                    targetHealth,
                    target,
                    status,
                    WeaponElement.Water,
                    sourceRoot,
                    "weapon_status"));
                if (statusResult != ElementalApplicationResultType.StatusApplied
                    || !status.HasStatus(WeaponElement.Water))
                {
                    throw new InvalidOperationException("동일 원소 재적용 상태 경로가 누락됐습니다.");
                }

                ElementalApplicationContext blocked = new ElementalApplicationContext(
                    targetHealth,
                    target,
                    status,
                    WeaponElement.Fire,
                    15f,
                    sourceRoot,
                    "weapon_reaction",
                    target.WorldCenter,
                    Vector3.zero,
                    false,
                    false,
                    false,
                    false);
                if (ElementalCombatProcessor.Process(blocked) != ElementalApplicationResultType.Ignored)
                    throw new InvalidOperationException("반응 피해의 상태·반응 재귀가 차단되지 않았습니다.");
            }
            finally
            {
                ElementalReactionEvents.ReactionStarted -= HandleStarted;
                targetHealth.OnDamaged -= HandleTargetDamaged;
                nearHealth.OnDamaged -= HandleNearDamaged;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(allyRoot);
            UnityEngine.Object.DestroyImmediate(farRoot);
            UnityEngine.Object.DestroyImmediate(nearRoot);
            UnityEngine.Object.DestroyImmediate(targetRoot);
            UnityEngine.Object.DestroyImmediate(sourceRoot);
        }
    }

    private static GameObject CreateActor(
        string name,
        CombatTeam team,
        out CombatHealth health,
        out CombatTarget target,
        out ElementalStatusController status)
    {
        GameObject root = new GameObject(name);
        health = root.AddComponent<CombatHealth>();
        health.ResetHealth();
        root.AddComponent<CombatAffiliation>().Configure(team);
        target = root.AddComponent<CombatTarget>();
        target.Configure(team, false);
        status = root.AddComponent<ElementalStatusController>();
        SerializedObject serialized = new SerializedObject(status);
        serialized.FindProperty("combatHealth").objectReferenceValue = health;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static ElementalApplicationContext CreateContext(
        CombatHealth health,
        CombatTarget target,
        ElementalStatusController status,
        WeaponElement element,
        GameObject source,
        string weaponId,
        float actualDamage = 100f)
    {
        return new ElementalApplicationContext(
            health,
            target,
            status,
            element,
            actualDamage,
            source,
            weaponId,
            target.WorldCenter,
            Vector3.forward,
            true,
            true,
            false,
            true);
    }

    private static void Apply(ElementalStatusController status, WeaponElement element, GameObject source)
    {
        if (!status.TryApplyDirectHit(new ElementalStatusApplication(
            element,
            100f,
            source,
            "weapon_setup",
            true,
            false,
            status.transform.position,
            Vector3.forward)))
        {
            throw new InvalidOperationException($"{element} 검증 상태 준비 실패");
        }
    }

    private static void RequireVaporize(ElementalBasicStatusSet statuses, WeaponElement incoming, string label)
    {
        if (!ElementalReactionResolver.TryResolve(statuses, incoming, out ElementalReactionType reaction)
            || reaction != ElementalReactionType.Vaporize)
        {
            throw new InvalidOperationException(label + " 증발 판정 실패");
        }
    }

    private static void RequireNoReaction(ElementalBasicStatusSet statuses, WeaponElement incoming, string label)
    {
        if (ElementalReactionResolver.TryResolve(statuses, incoming, out _))
            throw new InvalidOperationException(label + " 조합이 미구현 반응으로 소비됐습니다.");
    }

    private static void RequireThermalFracture(
        ElementalBasicStatusSet statuses,
        WeaponElement incoming,
        string label)
    {
        if (!ElementalReactionResolver.TryResolve(statuses, incoming, out ElementalReactionType reaction)
            || reaction != ElementalReactionType.ThermalFracture)
        {
            throw new InvalidOperationException(label + " 균열 판정 실패");
        }
    }

    private static void RequireReaction(
        ElementalBasicStatusSet statuses,
        WeaponElement incoming,
        ElementalReactionType expected,
        string label)
    {
        if (!ElementalReactionResolver.TryResolve(statuses, incoming, out ElementalReactionType reaction)
            || reaction != expected)
            throw new InvalidOperationException(label + " 반응 판정 실패");
    }

    private static void AssertApproximately(float expected, float actual, string label)
    {
        if (!Mathf.Approximately(expected, actual))
            throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
    }

    private static void AssertWithin(float expected, float actual, float tolerance, string label)
    {
        if (Mathf.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}, tolerance={tolerance}");
    }
}
