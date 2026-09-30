using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 받는 피해 99.9% 감소 디버그 검증. 2026-10-01 옛 HUD 버튼(HUDCanvas/DebugPanel)을 지우고 디버그 창(F1 > 플레이어 > 생존)으로
/// 옮겼으므로, 씬 검사는 옛 패널이 남아 있지 않은지와 빠진 스크립트가 없는지를 본다.
/// </summary>
public static class PlayerDamageReductionDebugValidationUtility
{
    private const string PersistentScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";

    [MenuItem("OVERBURST/Codex/Validation/Validate Player Damage Reduction Debug")]
    public static void RunFromMenu()
    {
        ValidateAll();
    }

    public static void RunOnceFromCommandLine()
    {
        ValidateAll();
    }

    private static void ValidateAll()
    {
        ValidateDamageCalculationAndSettingEvent();
        ValidatePersistentSceneWiring();
        Debug.Log("[ProjectVTP] Player damage reduction debug validation passed: 100 -> 0.1, setting event valid, old DebugPanel removed.");
    }

    private static void ValidateDamageCalculationAndSettingEvent()
    {
        int settingEventCount = 0;
        bool lastSettingValue = false;
        Action<bool> handleChanged = enabled =>
        {
            settingEventCount++;
            lastSettingValue = enabled;
        };

        CombatDebugSettings.PlayerDamageReductionDebugChanged += handleChanged;
        try
        {
            CombatDebugSettings.SetPlayerDamageReductionDebug(false);
            AssertApproximately(100f, CombatDebugSettings.ApplyPlayerDamageReductionDebug(100f), "Disabled multiplier");
            CombatDebugSettings.SetPlayerDamageReductionDebug(true);
            AssertApproximately(0.1f, CombatDebugSettings.ApplyPlayerDamageReductionDebug(100f), "Enabled multiplier");
            if (settingEventCount != 1 || !lastSettingValue)
                throw new InvalidOperationException("Player damage reduction setting event did not publish the enabled state exactly once.");
        }
        finally
        {
            CombatDebugSettings.SetPlayerDamageReductionDebug(false);
            CombatDebugSettings.PlayerDamageReductionDebugChanged -= handleChanged;
        }
    }

    private static void ValidatePersistentSceneWiring()
    {
        var scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
        var missingScriptPaths = new List<string>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[transformIndex].gameObject);
                for (int missingIndex = 0; missingIndex < count; missingIndex++)
                    missingScriptPaths.Add(GetHierarchyPath(transforms[transformIndex]));
            }
        }

        if (missingScriptPaths.Count > 0)
        {
            Debug.LogWarning(
                $"PersistentScene contains {missingScriptPaths.Count} missing script component(s): {string.Join(", ", missingScriptPaths)}");
        }

        GameObject hudCanvas = GameObject.Find("HUDCanvas");
        if (hudCanvas == null)
            throw new InvalidOperationException("HUDCanvas was not found.");
        if (hudCanvas.transform.Find("DebugPanel") != null)
            throw new InvalidOperationException("HUDCanvas/DebugPanel still exists; the debug window (F1) replaced it.");
    }

    private static void AssertApproximately(float expected, float actual, string label)
    {
        if (!Mathf.Approximately(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}.");
    }

    private static string GetHierarchyPath(Transform target)
    {
        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
