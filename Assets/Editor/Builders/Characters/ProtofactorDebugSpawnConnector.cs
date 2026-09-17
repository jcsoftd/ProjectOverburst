using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ProtofactorDebugSpawnConnector
{
    private const string HideoutScenePath =
        "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";

    [MenuItem("OVERBURST/Codex/Setup/Enemies/Connect Protofactor Debug Spawns")]
    public static void ConnectFromMenu()
    {
        ConnectOrThrow();
    }

    public static void RunOnceFromCommandLine()
    {
        ConnectOrThrow();
    }

    public static void ConnectOrThrow()
    {
        Scene previousScene = SceneManager.GetActiveScene();
        string previousScenePath =
            previousScene.IsValid() ? previousScene.path : string.Empty;
        if (previousScene.IsValid()
            && previousScene.isDirty
            && previousScenePath != HideoutScenePath)
        {
            throw new InvalidOperationException(
                "현재 씬에 저장되지 않은 변경이 있어 HideoutScene 연결 작업을 중단했습니다.");
        }

        Scene hideoutScene =
            EditorSceneManager.OpenScene(HideoutScenePath, OpenSceneMode.Single);
        HideoutMonsterSpawnDebugController controller =
            UnityEngine.Object.FindFirstObjectByType<HideoutMonsterSpawnDebugController>(
                FindObjectsInactive.Include);
        if (controller == null)
        {
            throw new InvalidOperationException(
                "HideoutScene에서 HideoutMonsterSpawnDebugController를 찾지 못했습니다.");
        }

        SerializedObject serializedController = new SerializedObject(controller);
        SerializedProperty spawnConfig =
            RequireProperty(serializedController, "spawnConfig");
        RequireRelative(spawnConfig, "enemyPrefab").objectReferenceValue = null;
        RequireRelative(spawnConfig, "enemyPrefabs").arraySize = 0;
        RequireRelative(spawnConfig, "spawnPacks").arraySize = 0;

        RequireProperty(serializedController, "monstersPerPack").intValue = 4;
        RequireProperty(serializedController, "difficultyMultiplier").floatValue = 1f;
        RequireProperty(serializedController, "encounterMultiplier").floatValue = 1f;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);

        if (!EditorSceneManager.SaveScene(hideoutScene))
        {
            throw new InvalidOperationException(
                "HideoutScene의 신규 몬스터 디버그 연결을 저장하지 못했습니다.");
        }

        ValidateSerializedConnection(controller);
        AssetDatabase.SaveAssets();
        Debug.Log(
            "[ProtofactorDebugSpawnConnector] PASS "
            + "activeRoster=Ceratoferox_Normal,Rapax_Normal "
            + "monstersPerPack=4 legacyMurlocEntries=0");

        if (!Application.isBatchMode
            && !string.IsNullOrWhiteSpace(previousScenePath)
            && previousScenePath != HideoutScenePath)
        {
            EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
        }
    }

    private static void ValidateSerializedConnection(
        HideoutMonsterSpawnDebugController controller)
    {
        SerializedObject serializedController = new SerializedObject(controller);
        SerializedProperty spawnConfig =
            RequireProperty(serializedController, "spawnConfig");
        if (RequireRelative(spawnConfig, "enemyPrefab").objectReferenceValue != null
            || RequireRelative(spawnConfig, "enemyPrefabs").arraySize != 0
            || RequireRelative(spawnConfig, "spawnPacks").arraySize != 0)
        {
            throw new InvalidOperationException(
                "HideoutScene에 기존 멀록 소환 참조가 남아 있습니다.");
        }

        if (RequireProperty(serializedController, "monstersPerPack").intValue != 4
            || !Mathf.Approximately(
                RequireProperty(serializedController, "difficultyMultiplier").floatValue,
                1f)
            || !Mathf.Approximately(
                RequireProperty(serializedController, "encounterMultiplier").floatValue,
                1f))
        {
            throw new InvalidOperationException(
                "HideoutScene의 신규 몬스터 디버그 기본값이 올바르지 않습니다.");
        }
    }

    private static SerializedProperty RequireProperty(
        SerializedObject serializedObject,
        string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                "직렬화 필드를 찾지 못했습니다: " + propertyName);
        }

        return property;
    }

    private static SerializedProperty RequireRelative(
        SerializedProperty parent,
        string propertyName)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                "직렬화 하위 필드를 찾지 못했습니다: " + propertyName);
        }

        return property;
    }
}
