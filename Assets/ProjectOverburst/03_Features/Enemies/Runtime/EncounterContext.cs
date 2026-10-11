using System;
using Overburst.Persistence;

// Immutable spawn facts: reused actors receive a new context for each lease.
public sealed class EncounterContext
{
    public string RunId { get; }
    public int MapLevel { get; }
    public ItemGrade MapGrade { get; }
    public float ExperienceMultiplier { get; }
    public MapInstanceState Map { get; }
    public bool RewardsAllowed { get; }
    public bool IsRun => !string.IsNullOrEmpty(RunId);
    public static EncounterContext Test { get; } = new EncounterContext(null, 1, ItemGrade.Common);

    public EncounterContext(string runId, int mapLevel, ItemGrade mapGrade, float experienceMultiplier = 1f,
        MapInstanceState map = null, bool rewardsAllowed = true)
    {
        if (mapLevel < 1 || mapLevel > 100) throw new ArgumentOutOfRangeException(nameof(mapLevel));
        if (!Enum.IsDefined(typeof(ItemGrade), mapGrade)) throw new ArgumentOutOfRangeException(nameof(mapGrade));
        if (float.IsNaN(experienceMultiplier) || float.IsInfinity(experienceMultiplier) || experienceMultiplier < 0f)
            throw new ArgumentOutOfRangeException(nameof(experienceMultiplier));
        RunId = runId;
        MapLevel = mapLevel;
        MapGrade = mapGrade;
        ExperienceMultiplier = experienceMultiplier;
        Map = map;
        RewardsAllowed = rewardsAllowed;
    }

    public bool MatchesRun(RunSnapshot run)
    {
        bool active = run != null && AccountInvariants.IsRunning(run.phase);
        return IsRun ? active && string.Equals(run.runId, RunId, StringComparison.Ordinal) : !active;
    }

    public bool CanGrantRewards => RewardsAllowed && MatchesRun(AccountGameplaySession.Current?.ReadRun())
        && (IsRun ? WorldSessionState.Phase == WorldPhase.Run : WorldSessionState.IsHideout);
}
