using System;
using System.Collections.Generic;

public static class EnemyBossEncounterRegistry
{
    private static readonly List<EnemyBossPhaseController> activeBosses = new();

    public static event Action<EnemyBossPhaseController> EncounterStarted;
    public static event Action<EnemyBossPhaseController, int, int> PhaseChanged;
    public static event Action<EnemyBossPhaseController> BossDefeated;
    public static event Action<EnemyBossPhaseController> EncounterEnded;

    public static EnemyBossPhaseController Current =>
        activeBosses.Count > 0 ? activeBosses[activeBosses.Count - 1] : null;
    public static int ActiveCount => activeBosses.Count;

    internal static void Register(EnemyBossPhaseController boss)
    {
        if (boss == null || activeBosses.Contains(boss))
            return;

        activeBosses.Add(boss);
        EncounterStarted?.Invoke(boss);
    }

    internal static void NotifyPhaseChanged(
        EnemyBossPhaseController boss,
        int previousPhase,
        int currentPhase)
    {
        if (boss != null && activeBosses.Contains(boss))
            PhaseChanged?.Invoke(boss, previousPhase, currentPhase);
    }

    internal static void NotifyDefeated(EnemyBossPhaseController boss)
    {
        if (boss != null && activeBosses.Contains(boss))
            BossDefeated?.Invoke(boss);
    }

    internal static void Unregister(EnemyBossPhaseController boss)
    {
        if (boss == null || !activeBosses.Remove(boss))
            return;

        EncounterEnded?.Invoke(boss);
    }

    public static void Clear()
    {
        for (int i = activeBosses.Count - 1; i >= 0; i--)
            EncounterEnded?.Invoke(activeBosses[i]);
        activeBosses.Clear();
    }
}
