using System;
using UnityEngine;

public static class CombatDebugSettings
{
    public const float ReducedIncomingPlayerDamageMultiplier = 0.001f;

    private static bool showAttackPatternDebug = true;
    private static bool showEnemyAiStateDebug = true;
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        showAttackPatternDebug = true;
        showEnemyAiStateDebug = true;
        showEnemySquadGeometryDebug = false;
        reduceIncomingPlayerDamageBy99_9Percent = false;
        spawnHideoutMonsters = false;
        AttackPatternDebugChanged = null;
        EnemyAiStateDebugChanged = null;
        EnemySquadGeometryDebugChanged = null;
        PlayerDamageReductionDebugChanged = null;
        HideoutMonsterSpawnChanged = null;
    }
}
