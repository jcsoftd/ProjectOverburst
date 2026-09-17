using System;
using UnityEditor;
using UnityEngine;

public static class ElementalStatusValidationUtility
{
    [MenuItem("OVERBURST/Codex/Validate/Elemental Status Runtime")]
    public static void ValidateFromMenu()
    {
        Validate();
        Debug.Log("[ElementalStatusValidation] 검증 완료");
    }

    public static void RunFromCommandLine()
    {
        Validate();
        Debug.Log("[ElementalStatusValidation] 명령줄 검증 완료");
    }

    private static void Validate()
    {
        ValidateRules();
        ValidateControllerBoundary();
        ValidateScheduledTickBudget();
        ValidateAuraLifecycle();
    }

    private static void ValidateAuraLifecycle()
    {
        ElementalStatusScheduler.ClearForValidation();
        GameObject root = new GameObject("ElementalStatusAuraValidationRoot");
        GameObject source = new GameObject("ElementalStatusAuraValidationSource");
        try
        {
            CombatHealth health = root.AddComponent<CombatHealth>();
            health.ResetHealth();
            MeleeElementStatusAuraController auraController =
                root.AddComponent<MeleeElementStatusAuraController>();

            ElementalStatusController statusController = root.AddComponent<ElementalStatusController>();
            SerializedObject serializedStatus = new SerializedObject(statusController);
            serializedStatus.FindProperty("combatHealth").objectReferenceValue = health;
            serializedStatus.FindProperty("auraController").objectReferenceValue = auraController;
            serializedStatus.ApplyModifiedPropertiesWithoutUndo();

            Apply(statusController, WeaponElement.Fire, source);
            Apply(statusController, WeaponElement.Water, source);
            Apply(statusController, WeaponElement.Ice, source);
            if (auraController.IsAuraActive(MeleeElementStatusAuraType.Burning)
                || auraController.IsAuraActive(MeleeElementStatusAuraType.Wet)
                || auraController.IsAuraActive(MeleeElementStatusAuraType.Chilled))
            {
                throw new InvalidOperationException("연소·젖음·냉각 상시 Aura가 활성화됐습니다.");
            }

            statusController.ClearAllStatuses(ElementalStatusClearReason.Explicit);
            Apply(statusController, WeaponElement.Electric, source);
            if (!auraController.IsAuraActive(MeleeElementStatusAuraType.Shocked))
                throw new InvalidOperationException("감전 상시 Aura가 활성화되지 않았습니다.");

            Apply(statusController, WeaponElement.Electric, source);
            if (!auraController.IsAuraActive(MeleeElementStatusAuraType.Shocked))
                throw new InvalidOperationException("감전 상태 갱신에서 Aura가 사라졌습니다.");

            statusController.ClearAllStatuses(ElementalStatusClearReason.Reset);
            auraController.StopAndClearVfx();
            if (auraController.IsAuraActive(MeleeElementStatusAuraType.Shocked))
                throw new InvalidOperationException("Reset·풀 정리 경계 뒤 감전 Aura가 남았습니다.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(root);
            ElementalStatusScheduler.ClearForValidation();
        }
    }

    private static void ValidateScheduledTickBudget()
    {
        ElementalStatusScheduler.ClearForValidation();
        const int actorCount = 3;
        GameObject[] roots = new GameObject[actorCount];
        CombatHealth[] healths = new CombatHealth[actorCount];
        ElementalStatusController[] controllers = new ElementalStatusController[actorCount];
        GameObject source = new GameObject("ElementalStatusTickBudgetSource");
        try
        {
            float totalHpBefore = 0f;
            for (int i = 0; i < actorCount; i++)
            {
                GameObject root = new GameObject("ElementalStatusTickBudgetTarget_" + i);
                roots[i] = root;
                CombatHealth health = root.AddComponent<CombatHealth>();
                health.ResetHealth();
                healths[i] = health;
                ElementalStatusController controller = root.AddComponent<ElementalStatusController>();
                controllers[i] = controller;
                SerializedObject serializedController = new SerializedObject(controller);
                serializedController.FindProperty("combatHealth").objectReferenceValue = health;
                serializedController.ApplyModifiedPropertiesWithoutUndo();
                Apply(controller, WeaponElement.Fire, source);
                totalHpBefore += health.CurrentHp;
            }

            float settlementTime = Time.time + 5.01f;
            int executedTicks = 0;
            int advanceCount = 0;
            while (ElementalStatusScheduler.ActiveControllerCount > 0 && advanceCount < 32)
            {
                ElementalStatusScheduler.AdvanceForValidation(settlementTime, actorCount, 1);
                int frameTicks = ElementalStatusScheduler.LastExecutedTickCountForValidation;
                if (frameTicks > 1)
                    throw new InvalidOperationException("상태 tick 프레임 예산 1을 초과했습니다.");

                executedTicks += frameTicks;
                advanceCount++;
            }

            float totalHpAfter = 0f;
            for (int i = 0; i < actorCount; i++)
                totalHpAfter += healths[i].CurrentHp;

            if (advanceCount <= 1
                || executedTicks != actorCount * 5
                || ElementalStatusScheduler.ActiveControllerCount != 0)
            {
                throw new InvalidOperationException("밀린 연소 tick의 분산 실행 또는 자연 만료가 누락됐습니다.");
            }
            AssertApproximately(actorCount * 10f, totalHpBefore - totalHpAfter, "분산 연소 최종 총피해");
        }
        finally
        {
            for (int i = roots.Length - 1; i >= 0; i--)
            {
                if (roots[i] != null)
                    UnityEngine.Object.DestroyImmediate(roots[i]);
            }
            UnityEngine.Object.DestroyImmediate(source);
            ElementalStatusScheduler.ClearForValidation();
        }
    }

    private static void ValidateRules()
    {
        AssertRule(WeaponElement.Fire, 5, 5f, 1f, 0.02f);
        AssertRule(WeaponElement.Water, 1, 6f, 0f, 0f);
        AssertRule(WeaponElement.Ice, 5, 5f, 0f, 0f);
        AssertRule(WeaponElement.Electric, 3, 4f, 1.5f, 0.06f);

        if (ElementalStatusRules.TryGetRule(WeaponElement.None, out _)
            || ElementalStatusRules.TryGetRule(WeaponElement.Wind, out _))
        {
            throw new InvalidOperationException("None/Wind가 지속 상태 규칙으로 등록됐습니다.");
        }

        AssertApproximately(1f, ElementalStatusRules.ResolveControlEffectMultiplier(EnemyRankType.Normal), "Normal 저항");
        AssertApproximately(0.5f, ElementalStatusRules.ResolveControlEffectMultiplier(EnemyRankType.Elite), "Elite 저항");
        AssertApproximately(0.5f, ElementalStatusRules.ResolveControlEffectMultiplier((EnemyRank)null), "미식별 저항");

        ElementalStatusRules.ResolveSpeedMultipliers(true, 5, 1f, out float normalMove, out float normalAction);
        AssertApproximately(0.7f, normalMove, "Normal 이동 배율");
        AssertApproximately(0.8f, normalAction, "Normal 행동 배율");

        ElementalStatusRules.ResolveSpeedMultipliers(true, 5, 0.5f, out float eliteMove, out float eliteAction);
        AssertApproximately(0.85f, eliteMove, "Elite 이동 배율");
        AssertApproximately(0.9f, eliteAction, "Elite 행동 배율");
        AssertApproximately(10f, ElementalStatusRules.ResolveTickDamage(100f, 0.02f, 5), "연소 tick");
        AssertApproximately(18f, ElementalStatusRules.ResolveTickDamage(100f, 0.06f, 3), "감전 tick");
    }

    private static void ValidateControllerBoundary()
    {
        ElementalStatusScheduler.ClearForValidation();
        GameObject root = new GameObject("ElementalStatusValidationRoot");
        try
        {
            CombatHealth health = root.AddComponent<CombatHealth>();
            health.ResetHealth(); // 동적 검증 대상 HP 명시 초기화
            EnemyRank rank = root.AddComponent<EnemyRank>();
            ElementalStatusController controller = root.AddComponent<ElementalStatusController>();
            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("combatHealth").objectReferenceValue = health;
            serializedController.FindProperty("enemyRank").objectReferenceValue = rank;
            serializedController.ApplyModifiedPropertiesWithoutUndo(); // EditMode 참조 주입
            GameObject source = new GameObject("ElementalStatusValidationSource");
            try
            {
                Apply(controller, WeaponElement.Fire, source);
                Apply(controller, WeaponElement.Water, source);
                for (int i = 0; i < 5; i++)
                    Apply(controller, WeaponElement.Ice, source);
                Apply(controller, WeaponElement.Electric, source);

                AssertStatus(controller, WeaponElement.Fire, 1);
                AssertStatus(controller, WeaponElement.Water, 1);
                AssertStatus(controller, WeaponElement.Ice, 5);
                AssertStatus(controller, WeaponElement.Electric, 1);
                AssertApproximately(0.7f, controller.MoveSpeedMultiplier, "동시 상태 이동 배율");
                AssertApproximately(0.8f, controller.ActionSpeedMultiplier, "동시 상태 행동 배율");
                if (ElementalStatusScheduler.ActiveControllerCount != 1)
                    throw new InvalidOperationException("활성 상태 컨트롤러가 중앙 스케줄러에 한 번만 등록되지 않았습니다.");

                controller.ClearAllStatuses(ElementalStatusClearReason.Explicit);
                Apply(controller, WeaponElement.Fire, source); // 동일 원소 재적용 회귀

                int hitReactionEventCount = 0;
                health.OnDamaged += (_, __) => hitReactionEventCount++;
                health.TakeDamage(new DamageInfo(
                    10f,
                    root.transform.position,
                    source,
                    Vector3.forward,
                    0f,
                    false,
                    true,
                    false,
                    default,
                    false,
                    WeaponElement.Fire,
                    "weapon_combat"));
                AssertStatus(controller, WeaponElement.Fire, 2, "weapon_combat");

                health.TakeDamage(new DamageInfo(
                    1f,
                    root.transform.position,
                    source,
                    Vector3.zero,
                    0f,
                    false,
                    false,
                    true,
                    default,
                    true,
                    WeaponElement.Fire,
                    "weapon_combat"));
                AssertStatus(controller, WeaponElement.Fire, 2, "weapon_combat");
                if (hitReactionEventCount != 2)
                    throw new InvalidOperationException("OnDamaged가 모든 실제 피해를 통지하지 않았습니다.");

                ElementalStatusApplication dotApplication = new ElementalStatusApplication(
                    WeaponElement.Fire,
                    100f,
                    source,
                    "weapon_test",
                    false,
                    true,
                    root.transform.position,
                    Vector3.forward);
                if (controller.TryApplyDirectHit(dotApplication))
                    throw new InvalidOperationException("DoT가 상태를 재적용했습니다.");

                if (controller.TryApplyDirectHit(new ElementalStatusApplication(
                    WeaponElement.Wind,
                    100f,
                    source,
                    "weapon_test",
                    true,
                    false,
                    root.transform.position,
                    Vector3.forward)))
                {
                    throw new InvalidOperationException("Wind가 지속 상태를 적용했습니다.");
                }

                controller.ClearAllStatuses(ElementalStatusClearReason.Explicit);
                if (controller.HasStatus(WeaponElement.Fire)
                    || controller.HasStatus(WeaponElement.Water)
                    || controller.HasStatus(WeaponElement.Ice)
                    || controller.HasStatus(WeaponElement.Electric))
                {
                    throw new InvalidOperationException("전체 정리 뒤 상태가 남았습니다.");
                }
                if (ElementalStatusScheduler.ActiveControllerCount != 0)
                    throw new InvalidOperationException("상태 전체 정리 뒤 중앙 스케줄러 등록이 남았습니다.");

                Apply(controller, WeaponElement.Water, source);
                ElementalStatusScheduler.AdvanceForValidation(Time.time + 6.01f);
                if (controller.HasStatus(WeaponElement.Water)
                    || ElementalStatusScheduler.ActiveControllerCount != 0)
                {
                    throw new InvalidOperationException("중앙 스케줄러가 시간 만료 상태를 제거하지 못했습니다.");
                }

                if (!controller.TryApplyReactionState(new ElementalReactionStateApplication(
                    ElementalReactionType.Plasma,
                    0f,
                    1f,
                    default,
                    100f)))
                {
                    throw new InvalidOperationException("무기한 플라즈마 상태 적용에 실패했습니다.");
                }
                if (ElementalStatusScheduler.ActiveControllerCount != 0)
                    throw new InvalidOperationException("만료 시계가 없는 플라즈마가 중앙 스케줄러에 등록됐습니다.");
                controller.ClearAllReactionStates(ElementalStatusClearReason.Explicit);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            health.ResetHealth();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            ElementalStatusScheduler.ClearForValidation();
        }
    }

    private static void Apply(ElementalStatusController controller, WeaponElement element, GameObject source)
    {
        bool applied = controller.TryApplyDirectHit(new ElementalStatusApplication(
            element,
            100f,
            source,
            "weapon_test",
            true,
            false,
            controller.transform.position,
            Vector3.forward));
        if (!applied)
        {
            CombatHealth health = controller.GetComponent<CombatHealth>();
            throw new InvalidOperationException(
                $"{element} 직접 Hit 적용에 실패했습니다. "
                + $"HealthNull={health == null}, IsDead={health != null && health.IsDead}, CurrentHp={health?.CurrentHp}");
        }
    }

    private static void AssertStatus(
        ElementalStatusController controller,
        WeaponElement element,
        int expectedStacks,
        string expectedWeaponId = "weapon_test")
    {
        if (!controller.TryGetStatus(element, out ElementalStatusSnapshot snapshot)
            || snapshot.StackCount != expectedStacks
            || snapshot.Owner.SourceWeaponRuntimeInstanceId != expectedWeaponId)
        {
            throw new InvalidOperationException($"{element} snapshot이 예상과 다릅니다.");
        }
    }

    private static void AssertRule(
        WeaponElement element,
        int stacks,
        float duration,
        float interval,
        float coefficient)
    {
        if (!ElementalStatusRules.TryGetRule(element, out ElementalStatusRule rule)
            || rule.MaxStacks != stacks)
        {
            throw new InvalidOperationException($"{element} 상태 규칙이 누락되거나 중첩이 다릅니다.");
        }

        AssertApproximately(duration, rule.Duration, $"{element} 지속시간");
        AssertApproximately(interval, rule.TickInterval, $"{element} tick 간격");
        AssertApproximately(coefficient, rule.TickDamageCoefficient, $"{element} tick 계수");
    }

    private static void AssertApproximately(float expected, float actual, string label)
    {
        if (!Mathf.Approximately(expected, actual))
            throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
    }
}
