using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonRunSceneValidator
{
    private const string LogPath =
        "Logs/DungeonRunSceneValidation.log";
    private const int DungeonRunBuildIndex = 3;

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/Validate Run Scene")]
    public static void ValidateFromMenu()
    {
        Debug.Log(ValidateOrThrow());
    }

    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = ValidateOrThrow();
            File.WriteAllText(LogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(LogPath, exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string ValidateOrThrow()
    {
        Scene scene = SceneManager.GetSceneByName(
            PersistentSceneFlow.DungeonRunSceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(
                DungeonRunSceneAuthoringBuilder.ScenePath,
                OpenSceneMode.Single);
        }

        Require(scene.IsValid() && scene.isLoaded,
            "DungeonRunScene 로드 실패");
        GameObject[] roots = scene.GetRootGameObjects();
        GameObject[] runtimeRoots = roots
            .Where(root =>
                !IsIgnoredForRuntimeValidation(root.transform))
            .ToArray();
        Require(runtimeRoots.Length == 1,
            $"DungeonRunScene Runtime Root 수={runtimeRoots.Length}");

        DungeonRunFlow[] flows =
            FindComponentsInScene<DungeonRunFlow>(scene);
        RuntimeDungeon[] runtimeDungeons =
            FindComponentsInScene<RuntimeDungeon>(scene);
        DungeonPlayerSpawnPlacer[] partySpawnPlacers =
            FindComponentsInScene<DungeonPlayerSpawnPlacer>(scene);
        DungeonRunDebugReturnInput[] debugReturnInputs =
            FindComponentsInScene<DungeonRunDebugReturnInput>(scene);
        DungeonBossEncounterBridge[] bossEncounterBridges =
            FindComponentsInScene<DungeonBossEncounterBridge>(scene);
        AdjacentRoomCulling[] roomCullings =
            FindComponentsInScene<AdjacentRoomCulling>(scene);
        DungeonRoomVisibilityController[] visibilityControllers =
            FindComponentsInScene<DungeonRoomVisibilityController>(scene);
        DungeonRunSceneAuthoringMarker[] markers =
            FindComponentsInScene<DungeonRunSceneAuthoringMarker>(scene);
        Light[] sceneLights =
            FindComponentsInScene<Light>(scene);
        Require(flows.Length == 1,
            $"DungeonRunFlow 수={flows.Length}");
        Require(runtimeDungeons.Length == 1,
            $"RuntimeDungeon 수={runtimeDungeons.Length}");
        Require(partySpawnPlacers.Length == 1,
            $"DungeonPlayerSpawnPlacer count={partySpawnPlacers.Length}");
        Require(debugReturnInputs.Length == 1,
            $"DungeonRunDebugReturnInput count={debugReturnInputs.Length}");
        Require(bossEncounterBridges.Length == 1,
            $"DungeonBossEncounterBridge count={bossEncounterBridges.Length}");
        Require(roomCullings.Length == 1,
            $"AdjacentRoomCulling count={roomCullings.Length}");
        Require(visibilityControllers.Length == 1,
            "DungeonRoomVisibilityController count="
            + visibilityControllers.Length);
        Require(markers.Length == 1,
            $"Scene Authoring Marker 수={markers.Length}");

        Require(
            sceneLights.Length == 1,
            "DungeonRunScene Light count="
            + sceneLights.Length);

        DungeonRunFlow flow = flows[0];
        RuntimeDungeon runtimeDungeon = runtimeDungeons[0];
        Light shadowLight = sceneLights[0];
        Require(flow.RuntimeDungeon == runtimeDungeon,
            "DungeonRunFlow RuntimeDungeon 연결 오류");
        Require(flow.Definition != null
            && flow.Definition.DungeonFlow != null,
            "DungeonRunDefinition 또는 Flow 연결 누락");
        Require(
            flow.Definition.PlayRoomCatalog == null,
            "자동 PlayRoom 카탈로그가 다시 연결되었습니다.");
        Require(
            flow.Definition.ArenaTileSet != null
            && AssetDatabase.GetAssetPath(
                flow.Definition.ArenaTileSet)
                == DungeonContentAuthoringBuilder
                    .ArenaTileSetOutputPath,
            "DungeonRunDefinition Arena TileSet 연결 오류");
        Require(
            flow.Definition.EndCapTileSet != null
            && AssetDatabase.GetAssetPath(
                flow.Definition.EndCapTileSet)
                == DungeonContentAuthoringBuilder
                    .EndCapTileSetOutputPath,
            "DungeonRunDefinition EndCap TileSet 연결 오류");
        Require(
            flow.Definition.TopologyRepairCatalog != null
            && AssetDatabase.GetAssetPath(
                flow.Definition.TopologyRepairCatalog)
                == DungeonTopologyRepairAuthoringUtility
                    .CatalogPath,
            "DungeonRunDefinition 형태 보정 Catalog 연결 오류");
        Require(
            flow.Definition.DefaultArenaCount == 4
            && flow.Definition.DefaultTotalMainPathTileCount == 24,
            "DungeonRunDefinition 직접 길이 기본값 불일치");
        Require(
            flow.Definition.LayoutCandidateCount
                == DungeonLayoutQualityEvaluator.DefaultCandidateCount
            && flow.Definition.MinimumBranchTileCount
                == DungeonLayoutQualityEvaluator
                    .DefaultMinimumBranchTileCount
            && Mathf.Approximately(
                flow.Definition.MaximumPlanarAspectRatio,
                DungeonLayoutQualityEvaluator
                    .DefaultMaximumPlanarAspectRatio),
            "Dungeon layout quality policy mismatch");
        Require(flow.GeneratedRoot != null
            && runtimeDungeon.Root == flow.GeneratedRoot,
            "GeneratedDungeon Root 연결 오류");
        Require(
            flow.PlayerSpawnPlacer == partySpawnPlacers[0]
            && partySpawnPlacers[0].GeneratedRoot
                == flow.GeneratedRoot,
            "Dungeon party spawn placer reference mismatch");
        Require(
            flow.ExitPortalPrefab != null
            && flow.ExitPortalPrefab.GetComponent<
                DungeonPortalExit>() != null,
            "Dungeon exit portal prefab reference mismatch");
        Require(
            flow.DebugReturnInput == debugReturnInputs[0]
            && debugReturnInputs[0].RunFlow == flow,
            "Dungeon debug return input reference mismatch");
        Require(
            bossEncounterBridges[0].RunFlow == flow,
            "Dungeon boss encounter bridge reference mismatch");
        Require(
            flow.RoomVisibilityController
                == visibilityControllers[0]
            && visibilityControllers[0].RuntimeDungeon
                == runtimeDungeon
            && visibilityControllers[0].RoomCulling
                == roomCullings[0],
            "Dungeon room visibility reference mismatch");
        Require(
            !roomCullings[0].enabled
            && roomCullings[0].AdjacentTileDepth == 2
            && !roomCullings[0].CullBehindClosedDoors
            && !roomCullings[0].IncludeDisabledComponents,
            "Dungeon room culling policy mismatch");
        Require(
            shadowLight.name
                == DungeonRunSceneAuthoringBuilder
                    .ShadowLightObjectName
            && DungeonRunSceneAuthoringBuilder
                .IsShadowLightPolicyCurrent(shadowLight)
            && RenderSettings.sun == shadowLight,
            "Dungeon shadow light policy mismatch");
        Require(!runtimeDungeon.GenerateOnStart,
            "RuntimeDungeon GenerateOnStart가 켜져 있음");
        Require(
            runtimeDungeon.Generator != null
            && runtimeDungeon.Generator.MaxAttemptCount
                == flow.Definition.MaxAttemptCount,
            "RuntimeDungeon MaxAttemptCount가 RunDefinition과 불일치");
        const float defaultLengthMultiplier = 1f;
        Require(
            Mathf.Approximately(
                runtimeDungeon.Generator.LengthMultiplier,
                defaultLengthMultiplier),
            "RuntimeDungeon LengthMultiplier does not match "
            + "DungeonRunDefinition.");

        int missingScriptCount = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            missingScriptCount +=
                GameObjectUtility
                    .GetMonoBehavioursWithMissingScriptCount(roots[i]);
        }
        Require(missingScriptCount == 0,
            $"DungeonRunScene Missing Script={missingScriptCount}");
        Require(
            FindComponentsInScene<Camera>(scene).Length == 0,
            "DungeonRunScene에 독립 Camera가 존재함");

        EditorBuildSettingsScene[] buildScenes =
            EditorBuildSettings.scenes;
        Require(buildScenes.Length > DungeonRunBuildIndex,
            $"Build Settings Index {DungeonRunBuildIndex} 누락");
        Require(
            buildScenes[DungeonRunBuildIndex].enabled
            && buildScenes[DungeonRunBuildIndex].path
                == DungeonRunSceneAuthoringBuilder.ScenePath,
            $"DungeonRunScene Build Settings Index "
            + $"{DungeonRunBuildIndex} 연결 오류");
        Require(
            buildScenes.Count(buildScene =>
                buildScene.path
                    == DungeonRunSceneAuthoringBuilder.ScenePath) == 1,
            "DungeonRunScene Build Settings 중복");

        return "[DungeonRunSceneValidator] PASS\n"
            + "RootCount=1\n"
            + "DungeonRunFlowCount=1\n"
            + "RuntimeDungeonCount=1\n"
            + "DungeonPartySpawnPlacerCount=1\n"
            + "DungeonRunDebugReturnInputCount=1\n"
            + "DungeonBossEncounterBridgeCount=1\n"
            + "AdjacentRoomCullingCount=1\n"
            + "DungeonRoomVisibilityControllerCount=1\n"
            + "DungeonShadowLightCount=1\n"
            + "DungeonShadowLightMode=RealtimeSoft\n"
            + "DungeonShadowLightIntensity="
            + shadowLight.intensity.ToString("F2") + "\n"
            + "RoomCullingEnabled=0\n"
            + "AdjacentTileDepth=2\n"
            + "CullBehindClosedDoors=0\n"
            + "DungeonExitPortalPrefab=1\n"
            + "LayoutCandidateCount="
            + flow.Definition.LayoutCandidateCount + "\n"
            + "MinimumBranchTileCount="
            + flow.Definition.MinimumBranchTileCount + "\n"
            + "MaximumPlanarAspectRatio="
            + flow.Definition.MaximumPlanarAspectRatio.ToString("F2")
            + "\n"
            + "DifficultyLengthMode=Disabled\n"
            + "DefaultTotalMainPath="
            + flow.Definition.DefaultTotalMainPathTileCount + "\n"
            + "DefaultArenaCount="
            + flow.Definition.DefaultArenaCount + "\n"
            + "EndCapTileSet="
            + flow.Definition.EndCapTileSet.name + "\n"
            + "LegacyDifficulty1LengthMultiplier="
            + flow.Definition.Difficulty1LengthMultiplier.ToString("F2")
            + "\n"
            + "LegacyDifficulty30LengthMultiplier="
            + flow.Definition.Difficulty30LengthMultiplier.ToString("F2")
            + "\n"
            + "DefaultLengthMultiplier="
            + defaultLengthMultiplier.ToString("F2") + "\n"
            + "MissingScriptCount=0\n"
            + "StandaloneCameraCount=0\n"
            + $"BuildSettingsIndex={DungeonRunBuildIndex}";
    }

    private static T[] FindComponentsInScene<T>(Scene scene)
        where T : Component
    {
        List<T> results = new();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            results.AddRange(
                roots[i].GetComponentsInChildren<T>(true)
                    .Where(component =>
                        !IsIgnoredForRuntimeValidation(
                            component.transform)));
        }
        return results.ToArray();
    }

    private static bool IsIgnoredForRuntimeValidation(
        Transform transform)
    {
        for (Transform current = transform;
             current != null;
             current = current.parent)
        {
            GameObject gameObject = current.gameObject;
            if (gameObject.CompareTag("EditorOnly")
                || (gameObject.hideFlags
                    & HideFlags.HideInHierarchy) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
