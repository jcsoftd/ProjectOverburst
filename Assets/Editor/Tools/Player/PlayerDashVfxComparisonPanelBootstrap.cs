using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerDashVfxComparisonPanelBootstrap
{
    static double deadline;
    static PlayerDashVfxComparisonPanel panel;

    static PlayerDashVfxComparisonPanelBootstrap()
    {
        EditorApplication.playModeStateChanged += PlayChanged;
    }
    static void PlayChanged(PlayModeStateChange state)
    {
        EditorApplication.update -= TryAttach;
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        // Automated isolated input tests own their pointers and do not show this panel.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))) return;
        deadline = EditorApplication.timeSinceStartup + 20d;
        EditorApplication.update += TryAttach;
    }
    static void TryAttach()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
        { EditorApplication.update -= TryAttach; return; }
        var effect = PlayerContext.GetOrCreate()?.CurrentActor?.GetComponent<PlayerDashVfx>();
        if (effect == null) return;
        Attach(effect);
        EditorApplication.update -= TryAttach;
    }
    public static PlayerDashVfxComparisonPanel Attach(PlayerDashVfx effect)
    {
        if (panel != null) { panel.Bind(effect); return panel; }
        var owner = new GameObject("대시 잔상 임시 비교") { hideFlags = HideFlags.DontSave };
        UnityEngine.Object.DontDestroyOnLoad(owner);
        panel = owner.AddComponent<PlayerDashVfxComparisonPanel>();
        panel.Bind(effect);
        return panel;
    }
    [MenuItem("OVERBURST/Player/Show Dash VFX Comparison Buttons")]
    public static void Show()
    {
        if (!EditorApplication.isPlaying) { Debug.Log("Play에서 대시 잔상 비교 버튼이 자동으로 열립니다."); return; }
        var effect = PlayerContext.GetOrCreate()?.CurrentActor?.GetComponent<PlayerDashVfx>();
        if (effect != null) Attach(effect);
    }
}

