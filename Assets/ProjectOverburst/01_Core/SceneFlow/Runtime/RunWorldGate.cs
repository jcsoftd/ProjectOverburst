using System;
using Overburst.Persistence;
using UnityEngine;

// The world builder completes this gate only after terrain, navigation and spawn are usable.
public sealed class RunWorldGate : MonoBehaviour
{
    public EncounterContext Context { get; private set; }
    public MapInstanceState Map { get; private set; }
    public bool IsReady { get; private set; }
    public Transform EntryPoint { get; private set; }
    public string Error { get; private set; }
    public MapRunRoster Roster { get; private set; }
    public MapSpawnBudget SpawnBudget { get; internal set; }
    public MapRunCompletion Completion { get; internal set; }
    public MapEmergencyExit EmergencyExit { get; internal set; }
    public MapBossArena BossArena { get; internal set; }
    public string PreparationStage { get; internal set; }

    public void BeginPreparation(RunSnapshot run)
    {
        if (run == null || run.phase != RunPhase.EntryPending) throw new ArgumentException("Pending run required.");
        Map = ItemSnapshotCodec.CopyValues(run.map);
        Map.areaCount = MapGenerationPolicy.EffectiveAreaCount(Map);
        Context = new EncounterContext(run.runId, Map.level, Map.grade,
            MapOptionPolicy.ExperienceMultiplier(Map), Map);
        IsReady = false; EntryPoint = null; Error = null; Roster = null; PreparationStage = null;
    }

    public bool SetRoster(MapRunRoster roster)
    {
        if (Context == null || IsReady || Error != null || Roster != null || roster == null || !roster.PlacementReady
            || roster.RunId != Context.RunId || roster.Seed != Map.seed || roster.ThemeId != Map.monsterThemeId
            || roster.Terrain != Map.terrain || roster.Areas.Count != Map.areaCount || roster.Total <= 0
            || roster.Kills != 0 || roster.Active != 0) return false;
        Roster = roster; return true;
    }

    public bool CompletePreparation(string runId, Transform entryPoint)
    {
        if (Context == null || Context.RunId != runId || IsReady || Error != null
            || (Map.generationVersion > 0 && (Roster == null || !Roster.PlacementReady))
            || entryPoint == null || entryPoint.gameObject.scene != gameObject.scene) return false;
        EntryPoint = entryPoint; IsReady = true;
        return true;
    }

    public bool RejectPreparation(string runId, string reason)
    {
        if (Context == null || Context.RunId != runId || IsReady || Error != null) return false;
        Error = string.IsNullOrWhiteSpace(reason) ? "월드 준비에 실패했습니다." : reason;
        return true;
    }
}
