using System;
using UnityEditor;
using UnityEngine;

public static class MeleeEarthVfxSlotSetupUtility
{
    private static bool scheduled;

    [InitializeOnLoadMethod]
    private static void ScheduleMissingSlotSetup()
    {
        if (!MeleeElementCircularSlashSetupUtility.NeedsEarthSlotSetup()
            && !MeleeElementHitSetupUtility.NeedsEarthSlotSetup())
        {
            return;
        }

        if (scheduled)
            return;

        scheduled = true;
        EditorApplication.delayCall += ApplyWhenEditorIsReady;
    }

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Earth Melee VFX Slots")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        MeleeElementCircularSlashSetupUtility.EnsureEarthSlotsFromCommandLine();
        MeleeElementHitSetupUtility.EnsureEarthSlotFromCommandLine();
        MeleeElementVfxMissingScriptValidationUtility.ValidateFromCommandLine();
        Debug.Log(
            "[ProjectVTP] 대지 근접 VFX 빈 슬롯 생성·연결 완료: 일반 Slash / 원형 Slash / Hit");
    }

    private static void ApplyWhenEditorIsReady()
    {
        if (EditorApplication.isCompiling
            || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += ApplyWhenEditorIsReady;
            return;
        }

        scheduled = false;
        try
        {
            RunFromCommandLine();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
