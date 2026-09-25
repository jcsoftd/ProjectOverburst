using System;
using System.Collections.Generic;
using UnityEngine;

// This component lives on the dungeon world and disappears with the run scene.
[DisallowMultipleComponent]
public sealed class MapRunBuffs : MonoBehaviour
{
    private readonly float[] bonuses = new float[8];
    private readonly List<MapCardChoice> selected = new List<MapCardChoice>();
    private readonly HashSet<string> appliedEvents = new HashSet<string>(StringComparer.Ordinal);
    private string runId;
    public static MapRunBuffs Current { get; private set; }
    public IReadOnlyList<MapCardChoice> Selected => selected;
    public event Action Changed;

    public void Configure(string activeRunId)
    {
        runId = activeRunId;
        Current = this;
    }

    public static float Bonus(MapBuffKind kind)
    {
        MapRunBuffs active = Current;
        return active != null && WorldSessionState.Phase == WorldPhase.Run
            ? active.bonuses[(int)kind] : 0f;
    }

    private bool IsCurrentRun => !string.IsNullOrEmpty(runId)
        && Overburst.Persistence.AccountGameplaySession.Current?.ReadRun()?.runId == runId
        && WorldSessionState.Phase == WorldPhase.Run;

    public bool Add(string eventId, MapCardChoice card)
    {
        if (!IsCurrentRun || string.IsNullOrEmpty(eventId) || card == null || card.Kind != MapCardKind.Buff)
            return false;
        if (!appliedEvents.Add(eventId)) return true;
        int index = (int)card.Buff;
        float cap = card.Buff == MapBuffKind.Armor ? 500f
            : card.Buff == MapBuffKind.MoveSpeed ? .60f
            : card.Buff == MapBuffKind.AttackSpeed ? .80f : 2f;
        bonuses[index] = Mathf.Min(cap, bonuses[index] + Mathf.Max(0f, card.Value));
        selected.Add(card);
        PlayerProgression.Current?.RefreshStats();
        Changed?.Invoke();
        return true;
    }

    private void OnDestroy()
    {
        if (Current != this) return;
        Current = null;
        PlayerProgression.Current?.RefreshStats();
    }
}
