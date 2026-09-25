using System;
using UnityEngine;

public enum WorldPhase { Booting, Hideout, Loading, Run, Settling }

// Shared presentation/interaction state. Scene adapters publish readiness;
// account transactions remain responsible for rewards and settlement.
public static class WorldSessionState
{
    public static WorldPhase Phase { get; private set; } = WorldPhase.Booting;
    public static UnityEngine.SceneManagement.Scene ContentScene { get; private set; }
    internal static void SetContentScene(UnityEngine.SceneManagement.Scene scene) => ContentScene = scene;
    public static bool IsHideout => Phase == WorldPhase.Hideout;
    public static event Action<WorldPhase> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Phase = WorldPhase.Booting;
        ContentScene = default;
        Changed = null;
    }

    internal static void SetPhase(WorldPhase phase)
    {
        if (Phase == phase) return;
        Phase = phase;
        var subscribers = Changed;
        if (subscribers == null) return;
        foreach (Action<WorldPhase> callback in subscribers.GetInvocationList())
            try { callback(phase); } catch (Exception error) { Debug.LogException(error); }
    }
}
