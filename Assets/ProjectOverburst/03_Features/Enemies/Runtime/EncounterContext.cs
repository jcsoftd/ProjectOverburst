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
    public string OutdoorSessionId { get; }
    public bool IsOutdoor => !string.IsNullOrEmpty(OutdoorSessionId);
    public bool IsRun => !string.IsNullOrEmpty(RunId);
    public bool RewardsAllowed { get; }
    public static EncounterContext Test { get; } = new EncounterContext(null, 1, ItemGrade.Common);

    public EncounterContext(string runId, int mapLevel, ItemGrade mapGrade, float experienceMultiplier = 1f,
        MapInstanceState map = null, string outdoorSessionId = null, bool rewardsAllowed = true)
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
        if (!string.IsNullOrEmpty(outdoorSessionId) && !string.IsNullOrEmpty(runId)) throw new ArgumentException("Outdoor encounters cannot own a run.");
        OutdoorSessionId = outdoorSessionId;
        RewardsAllowed = rewardsAllowed;
    }

    public bool MatchesRun(RunSnapshot run)
    {
        bool active = run != null && AccountInvariants.IsRunning(run.phase);
        return IsRun ? active && string.Equals(run.runId, RunId, StringComparison.Ordinal) : !active;
    }

    // Reward eligibility needs current identity and phase, not a serialized copy of the entire run.
    bool MatchesCurrentRun(AccountGameplaySession account) => IsRun
        ? account != null && account.CanAcquireFromRun(RunId)
        : MatchesRun(account?.ReadRun());

    public bool CanGrantRewards => RewardsAllowed && MatchesCurrentRun(AccountGameplaySession.Current)
        && (IsRun ? WorldSessionState.Phase == WorldPhase.Run : WorldSessionState.IsHideout)
        && (!IsOutdoor || (OutdoorWorldSpawner.Current && OutdoorWorldSpawner.Current.SessionId == OutdoorSessionId));
}
