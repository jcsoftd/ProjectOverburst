using System;
using System.Collections.Generic;
using UnityEngine;

public enum RunSceneReadinessState
{
    None = 0,
    Loading,
    Ready,
    Failed
}

public static class RunSceneReadinessRegistry
{
    private sealed class Entry
    {
        public RunSceneReadinessState State;
        public string Message;
    }

    private static readonly Dictionary<string, Entry> Entries =
        new(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetBeforeSceneLoad()
    {
        Entries.Clear();
    }

    public static void Begin(string sceneName)
    {
        Set(sceneName, RunSceneReadinessState.Loading, string.Empty);
    }

    public static void MarkReady(string sceneName)
    {
        Set(sceneName, RunSceneReadinessState.Ready, string.Empty);
    }

    public static void MarkFailed(string sceneName, string message)
    {
        Set(sceneName, RunSceneReadinessState.Failed, message);
    }

    public static RunSceneReadinessState GetState(string sceneName)
    {
        return TryGetEntry(sceneName, out Entry entry)
            ? entry.State
            : RunSceneReadinessState.None;
    }

    public static string GetMessage(string sceneName)
    {
        return TryGetEntry(sceneName, out Entry entry)
            ? entry.Message
            : string.Empty;
    }

    public static bool IsTerminal(string sceneName)
    {
        RunSceneReadinessState state = GetState(sceneName);
        return state == RunSceneReadinessState.Ready
            || state == RunSceneReadinessState.Failed;
    }

    public static void Clear(string sceneName)
    {
        if (!string.IsNullOrWhiteSpace(sceneName))
            Entries.Remove(sceneName);
    }

    private static void Set(
        string sceneName,
        RunSceneReadinessState state,
        string message)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        if (!Entries.TryGetValue(sceneName, out Entry entry))
        {
            entry = new Entry();
            Entries.Add(sceneName, entry);
        }

        entry.State = state;
        entry.Message = message ?? string.Empty;
    }

    private static bool TryGetEntry(string sceneName, out Entry entry)
    {
        entry = null;
        return !string.IsNullOrWhiteSpace(sceneName)
            && Entries.TryGetValue(sceneName, out entry);
    }
}
