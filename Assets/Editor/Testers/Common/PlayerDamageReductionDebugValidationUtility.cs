using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class PlayerDamageReductionDebugValidationUtility
{
    private const string PersistentScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const string ButtonName = "PlayerDamageReductionDebugToggleUI";

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
        Debug.Log("[ProjectVTP] Player damage reduction debug validation passed: 100 -> 0.1, setting event valid, DebugPanel wiring valid.");
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
        Transform debugPanel = hudCanvas != null ? hudCanvas.transform.Find("DebugPanel") : null;
        Transform buttonTransform = debugPanel != null ? debugPanel.Find(ButtonName) : null;
        if (buttonTransform == null)
            throw new InvalidOperationException($"HUDCanvas/DebugPanel/{ButtonName} was not found.");

        GameObject buttonObject = buttonTransform.gameObject;
        if (buttonObject.GetComponent<Button>() == null
            || buttonObject.GetComponentInChildren<TextMeshProUGUI>(true) == null
            || buttonObject.GetComponent<PlayerDamageReductionDebugToggleUI>() == null)
        {
            throw new InvalidOperationException($"{ButtonName} is missing Button, TMP label, or toggle component.");
        }

        Transform panelToggleTransform = debugPanel.Find("DebugPanelToggleButtonUI");
        DebugPanelToggleUI panelToggle = panelToggleTransform != null
            ? panelToggleTransform.GetComponent<DebugPanelToggleUI>()
            : null;
        if (panelToggle == null)
            throw new InvalidOperationException("DebugPanelToggleButtonUI wiring was not found.");

        SerializedObject serializedToggle = new SerializedObject(panelToggle);
        SerializedProperty controlledObjects = serializedToggle.FindProperty("controlledObjects");
        bool containsButton = false;
        for (int i = 0; controlledObjects != null && i < controlledObjects.arraySize; i++)
        {
            GameObject controlled = controlledObjects.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (controlled != null && controlled.name == ButtonName)
            {
                containsButton = true;
                break;
            }
        }

        if (!containsButton)
            throw new InvalidOperationException($"DebugPanelToggleUI.controlledObjects does not contain {ButtonName}.");
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
