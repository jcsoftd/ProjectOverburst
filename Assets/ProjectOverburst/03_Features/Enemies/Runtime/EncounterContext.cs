using System;
using Overburst.Persistence;

// Immutable spawn facts: reused actors receive a new context for each lease.
public sealed class EncounterContext
{
    public string RunId { get; }
    public int MapLevel { get; }
    public ItemGrade MapGrade { get; }
    public float ExperienceMultiplier { get; }
    public bool IsRun => !string.IsNullOrEmpty(RunId);
    public static EncounterContext Test { get; } = new EncounterContext(null, 1, ItemGrade.Common);

    public EncounterContext(string runId, int mapLevel, ItemGrade mapGrade, float experienceMultiplier = 1f)
    {
        if (mapLevel < 1 || mapLevel > 100) throw new ArgumentOutOfRangeException(nameof(mapLevel));
        if (!Enum.IsDefined(typeof(ItemGrade), mapGrade)) throw new ArgumentOutOfRangeException(nameof(mapGrade));
        if (float.IsNaN(experienceMultiplier) || float.IsInfinity(experienceMultiplier) || experienceMultiplier < 0f)
            throw new ArgumentOutOfRangeException(nameof(experienceMultiplier));
        RunId = runId;
        MapLevel = mapLevel;
        MapGrade = mapGrade;
        ExperienceMultiplier = experienceMultiplier;
    }

    public bool MatchesRun(RunSnapshot run)
    {
        bool active = run != null && (run.phase == RunPhase.Active || run.phase == RunPhase.BossCleared);
        return IsRun ? active && string.Equals(run.runId, RunId, StringComparison.Ordinal) : !active;
    }

    public bool CanGrantRewards => MatchesRun(AccountGameplaySession.Current?.Read().run)
        && (IsRun ? WorldSessionState.Phase == WorldPhase.Run : WorldSessionState.IsHideout);
}
