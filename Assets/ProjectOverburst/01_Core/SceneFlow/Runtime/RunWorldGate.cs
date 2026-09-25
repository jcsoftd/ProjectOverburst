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

    public void BeginPreparation(RunSnapshot run)
    {
        if (run == null || run.phase != RunPhase.EntryPending) throw new ArgumentException("Pending run required.");
        Context = new EncounterContext(run.runId, run.map.level, run.map.grade);
        Map = ItemSnapshotCodec.CopyValues(run.map);
        IsReady = false; EntryPoint = null; Error = null;
    }

    public bool CompletePreparation(string runId, Transform entryPoint)
    {
        if (Context == null || Context.RunId != runId || IsReady || Error != null
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
