using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerDashVfxComparisonPanelBootstrap
{
    // Temporarily keep the comparison panel opt-in; Show() remains available in Play.
    static readonly bool AutoShowOnPlay = false;
    static double deadline;
    static PlayerDashVfxComparisonPanel panel;

    static PlayerDashVfxComparisonPanelBootstrap()
    {
        EditorApplication.playModeStateChanged += PlayChanged;
    }
    static void PlayChanged(PlayModeStateChange state)
    {
        EditorApplication.update -= TryAttach;
        if (!AutoShowOnPlay || state != PlayModeStateChange.EnteredPlayMode) return;
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
        if (panel != null) { panel.Bind(effect); panel.enabled = true; return panel; }
        var owner = new GameObject("대시 잔상 임시 비교") { hideFlags = HideFlags.DontSave };
        UnityEngine.Object.DontDestroyOnLoad(owner);
        panel = owner.AddComponent<PlayerDashVfxComparisonPanel>();
        panel.Bind(effect);
        return panel;
    }
    [MenuItem("OVERBURST/Player/Show Dash VFX Comparison Buttons")]
    public static void Show()
    {
        if (!EditorApplication.isPlaying) { Debug.Log("Play 중 이 메뉴를 선택하면 대시 잔상 비교 버튼이 열립니다."); return; }
        var effect = PlayerContext.GetOrCreate()?.CurrentActor?.GetComponent<PlayerDashVfx>();
        if (effect != null) Attach(effect);
    }
}

