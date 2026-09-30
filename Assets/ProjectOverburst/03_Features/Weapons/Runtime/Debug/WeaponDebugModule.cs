#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools;
using UnityEngine;

/// <summary>무기 쪽 디버그 항목. 지금은 조준선 토글 하나(90C 7.2). 상태는 <see cref="UnifiedDebugAimLine"/>이 소유한다.</summary>
internal static class WeaponDebugModule
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugRegistry.Section(DebugTabs.Combat, "표시", 10)
            .Toggle("조준선",
                () => UnifiedDebugAimLine.IsActive && UnifiedDebugAimLine.DebugLineEnabled,
                SetAimLine)
            .WithId("combat.display.aimLine")
            .EnabledWhen(() => UnifiedDebugAimLine.IsActive, "플레이어가 있는 씬에서만")
            .Keywords("aim", "조준")
            .Tip("무기 조준 방향을 선으로 그린다.");
    }

    private static void SetAimLine(bool enabled)
    {
        UnifiedDebugAimLine line = Object.FindFirstObjectByType<UnifiedDebugAimLine>();
        if (line != null)
            line.SetDebugLineEnabled(enabled);
    }
}
#endif
