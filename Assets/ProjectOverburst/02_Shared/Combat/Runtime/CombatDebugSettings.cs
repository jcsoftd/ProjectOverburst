using System;
using UnityEngine;

/// <summary>던전 런 몬스터의 물약·장비 드롭(Flask/GearLootPolicy) 확률 디버그 강제. 기본은 규칙 그대로다.</summary>
public enum RunLootDebugOverride { Default, Always, Never }

public static class CombatDebugSettings
{
    public const float ReducedIncomingPlayerDamageMultiplier = 0.001f;

    // 2026-09-30: 플레이어 공격 판정창과 적 AI 상태 글자는 기본으로 끈다(디버그 창에서 켤 수 있다).
    private static bool showAttackPatternDebug;
    private static bool showEnemyAiStateDebug;
    private static bool showEnemySquadGeometryDebug;
    private static bool reduceIncomingPlayerDamageBy99_9Percent;
    private static bool spawnHideoutMonsters;

    public static bool ShowAttackPatternDebug => showAttackPatternDebug;
    public static bool ShowEnemyAiStateDebug => showEnemyAiStateDebug;
    public static bool ShowEnemySquadGeometryDebug => showEnemySquadGeometryDebug;
    public static bool ReduceIncomingPlayerDamageBy99_9Percent => reduceIncomingPlayerDamageBy99_9Percent;
    public static bool SpawnHideoutMonsters => spawnHideoutMonsters;
    public static event Action<bool> AttackPatternDebugChanged;
    public static event Action<bool> EnemyAiStateDebugChanged;
    public static event Action<bool> EnemySquadGeometryDebugChanged;
    public static event Action<bool> PlayerDamageReductionDebugChanged;
    public static event Action<bool> HideoutMonsterSpawnChanged;

    public static void SetAttackPatternDebug(bool visible)
    {
        if (showAttackPatternDebug == visible)
            return;

        showAttackPatternDebug = visible;
        AttackPatternDebugChanged?.Invoke(visible);
    }

    public static void ToggleAttackPatternDebug()
    {
        SetAttackPatternDebug(!showAttackPatternDebug);
    }

    public static void SetEnemyAiStateDebug(bool visible)
    {
        if (showEnemyAiStateDebug == visible)
            return;

        showEnemyAiStateDebug = visible;
        EnemyAiStateDebugChanged?.Invoke(visible);
    }

    public static void ToggleEnemyAiStateDebug()
    {
        SetEnemyAiStateDebug(!showEnemyAiStateDebug);
    }

    public static void SetEnemySquadGeometryDebug(bool visible)
    {
        if (showEnemySquadGeometryDebug == visible)
            return;

        showEnemySquadGeometryDebug = visible;
        EnemySquadGeometryDebugChanged?.Invoke(visible);
    }

    public static void ToggleEnemySquadGeometryDebug()
    {
        SetEnemySquadGeometryDebug(!showEnemySquadGeometryDebug);
    }

    public static void SetPlayerDamageReductionDebug(bool enabled)
    {
        if (reduceIncomingPlayerDamageBy99_9Percent == enabled)
            return;

        reduceIncomingPlayerDamageBy99_9Percent = enabled;
        PlayerDamageReductionDebugChanged?.Invoke(enabled);
    }

    public static void TogglePlayerDamageReductionDebug()
    {
        SetPlayerDamageReductionDebug(!reduceIncomingPlayerDamageBy99_9Percent);
    }

    public static float ApplyPlayerDamageReductionDebug(float incomingDamage)
    {
        float damage = Mathf.Max(0f, incomingDamage);
        return reduceIncomingPlayerDamageBy99_9Percent
            ? damage * ReducedIncomingPlayerDamageMultiplier
            : damage;
    }

    public static void SetHideoutMonsterSpawn(bool enabled)
    {
        if (spawnHideoutMonsters == enabled)
            return;

        spawnHideoutMonsters = enabled;
        HideoutMonsterSpawnChanged?.Invoke(enabled);
    }

    public static void ToggleHideoutMonsterSpawn()
    {
        SetHideoutMonsterSpawn(!spawnHideoutMonsters);
    }

    // 2026-10-01 디버그 창(90C 11절 5단계): 드롭 규칙 확인용. 기본값이면 확률을 그대로 돌려준다(난수 사용 순서도 같다).
    private static RunLootDebugOverride runLootOverride;
    public static RunLootDebugOverride RunLootOverride => runLootOverride;

    public static void SetRunLootOverride(RunLootDebugOverride value) => runLootOverride = value;

    public static float ApplyRunLootChance(float chance)
    {
        switch (runLootOverride)
        {
            case RunLootDebugOverride.Always: return 1f;
            case RunLootDebugOverride.Never: return 0f;
            default: return chance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        showAttackPatternDebug = false;
        showEnemyAiStateDebug = false;
        showEnemySquadGeometryDebug = false;
        reduceIncomingPlayerDamageBy99_9Percent = false;
        spawnHideoutMonsters = false;
        runLootOverride = RunLootDebugOverride.Default;
        AttackPatternDebugChanged = null;
        EnemyAiStateDebugChanged = null;
        EnemySquadGeometryDebugChanged = null;
        PlayerDamageReductionDebugChanged = null;
        HideoutMonsterSpawnChanged = null;
    }
}
