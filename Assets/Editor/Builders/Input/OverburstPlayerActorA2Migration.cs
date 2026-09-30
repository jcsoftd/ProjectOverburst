using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// GOAL A2: PF_PlayerActor에 PlayerInputFacade + PlayerStateCoordinator를 Editor API로만 연결한다.
// - 프리팹/.inputactions를 텍스트/YAML/JSON으로 직접 패치하지 않는다.
// - PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset와 SerializedObject를 사용하고 GUID/meta를 보존한다.
// - MenuItem은 지금 실행하지 않는다. Codex가 리뷰 뒤 백업 해시를 확인하고 실행한다.
public static class OverburstPlayerActorA2Migration
{
    public const string PrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string AssetPath = "Assets/ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions";
    public const string MenuPath = "OVERBURST/Codex/Setup/Player/Apply GOAL A2 PlayerActor Migration";

    [MenuItem(MenuPath)]
    public static void MigrateFromMenu()
    {
        Debug.Log(MigrateAndReport());
    }

    public static string MigrateAndReport()
    {
        // 사전조건: 프리팹/에셋 존재와 Gameplay 18-action 계약을 먼저 확인한다.
        Require(File.Exists(ResolveProjectPath(PrefabPath)), "프리팹을 찾을 수 없다: " + PrefabPath);
        InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
        Require(inputAsset != null, "Input Action 에셋을 찾을 수 없다: " + AssetPath);
        InputActionMap gameplayMap = inputAsset.FindActionMap(PlayerInputFacade.GameplayMapName);
        Require(gameplayMap != null, "Gameplay map이 없다. A2 Input 마이그레이션을 먼저 적용하라.");
        foreach (string name in PlayerInputFacade.RequiredGameplayActions)
            Require(gameplayMap.FindAction(name) != null,
                "Gameplay/" + name + "이 없다. A2 Input 마이그레이션을 먼저 적용하라.");

        string prefabFilePath = ResolveProjectPath(PrefabPath);
        byte[] originalBytes = File.ReadAllBytes(prefabFilePath);

        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        Require(contents != null, "프리팹 로드에 실패했다: " + PrefabPath);
        try
        {
            // 액터 루트(PlayerActorRuntime 보유)를 찾는다.
            PlayerActorRuntime[] actors = contents.GetComponentsInChildren<PlayerActorRuntime>(true);
            Require(actors.Length == 1, "PlayerActorRuntime이 1개가 아니다: " + actors.Length);
            GameObject actorObject = actors[0].gameObject;

            PlayerInputFacade[] facades = actorObject.GetComponents<PlayerInputFacade>();
            Require(facades.Length <= 1, "PlayerInputFacade가 2개 이상이다. 수동 정리가 필요하다.");
            PlayerStateCoordinator[] coordinators = actorObject.GetComponents<PlayerStateCoordinator>();
            Require(coordinators.Length <= 1, "PlayerStateCoordinator가 2개 이상이다. 수동 정리가 필요하다.");

            bool changed = false;
            PlayerInputFacade facade = facades.Length == 1 ? facades[0] : null;
            if (facade == null)
            {
                facade = actorObject.AddComponent<PlayerInputFacade>();
                changed = true;
            }
            PlayerStateCoordinator coordinator = coordinators.Length == 1 ? coordinators[0] : null;
            if (coordinator == null)
            {
                actorObject.AddComponent<PlayerStateCoordinator>();
                changed = true;
            }

            // sourceAsset을 안전한 공개 설정 API(SerializedObject)로 연결한다.
            SerializedObject facadeSettings = new SerializedObject(facade);
            SerializedProperty sourceAssetProperty = facadeSettings.FindProperty("sourceAsset");
            Require(sourceAssetProperty != null, "PlayerInputFacade.sourceAsset 속성을 찾을 수 없다.");
            if (sourceAssetProperty.objectReferenceValue != inputAsset)
            {
                sourceAssetProperty.objectReferenceValue = inputAsset;
                facadeSettings.ApplyModifiedProperties();
                changed = true;
            }

            // 메모리의 적용 결과를 실제 asset에 쓰기 전에 검증한다.
            PlayerInputFacade[] afterFacades = contents.GetComponentsInChildren<PlayerInputFacade>(true);
            PlayerStateCoordinator[] afterCoordinators = contents.GetComponentsInChildren<PlayerStateCoordinator>(true);
            Require(afterFacades.Length == 1, "적용 후 PlayerInputFacade가 1개가 아니다.");
            Require(afterCoordinators.Length == 1, "적용 후 PlayerStateCoordinator가 1개가 아니다.");
            Require(afterFacades[0].SourceAsset == inputAsset, "적용 후 sourceAsset이 연결되지 않았다.");

            if (!changed)
            {
                string noChangeReport = OverburstGoalA2Validator.ValidateAndReport();
                return "[OverburstPlayerActorA2Migration] NO_CHANGE"
                    + "\n- prefab: " + PrefabPath
                    + "\n" + noChangeReport;
            }

            try
            {
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
                string appliedReport = OverburstGoalA2Validator.ValidateAndReport();
                return "[OverburstPlayerActorA2Migration] APPLIED"
                    + "\n- prefab: " + PrefabPath
                    + "\n- facade/coordinator 각 1개, sourceAsset 연결"
                    + "\n" + appliedReport;
            }
            catch (Exception)
            {
                // 실패 시 원본 복구.
                File.WriteAllBytes(prefabFilePath, originalBytes);
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static string ResolveProjectPath(string assetPath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string filePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Require(
            filePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase),
            "에셋 파일 경로가 프로젝트 루트 하위가 아니다: " + filePath);
        return filePath;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
