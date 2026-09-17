using System;
using System.Collections.Generic;
using System.IO;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonRunSceneAuthoringBuilder
{
    public const string ScenePath =
        "Assets/ProjectOverburst/00_Scenes/DungeonRunScene.unity";
    public const string DefinitionPath =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Data/Definitions/"
        + "DRD_MultistoryDungeon_Pilot.asset";
    public const string ShadowLightObjectName =
        "DungeonShadowLight";
    public const float ShadowLightIntensity = 0.55f;
    public const float ShadowStrength = 0.65f;
    public static readonly Color ShadowLightColor =
        new(0.82f, 0.86f, 0.94f, 1f);
    public static readonly Vector3 ShadowLightEuler =
        new(52f, -35f, 0f);

    private const int AuthoringVersion = 10;
    private const int BuildSettingsIndex = 4;
    private const int DefaultSeed = 20260726;
    private const int DefaultArenaCount = 4;
    private const int DefaultTotalMainPathTileCount = 24;
    private const int MaximumGenerationAttempts = 100;
    private const string LogPath = "Logs/DungeonRunSceneAuthoring.log";

    [MenuItem("OVERBURST/Codex/Setup/World/Dungeon/Build Run Scene")]
    public static void BuildFromMenu()
    {
        Debug.Log(BuildAndValidate());
    }

    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = BuildAndValidate();
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

    public static string BuildAndValidate()
    {
        EnsureFolder(
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Data/Definitions");
        DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(
            DungeonContentAuthoringBuilder.FlowOutputPath);
        Require(flow != null, "Project DungeonFlow 누락");
        TileSet arenaTileSet = AssetDatabase.LoadAssetAtPath<TileSet>(
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath);
        Require(arenaTileSet != null, "Project Arena TileSet 누락");
        TileSet endCapTileSet = AssetDatabase.LoadAssetAtPath<TileSet>(
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath);
        Require(endCapTileSet != null, "Project EndCap TileSet 누락");
        DungeonTopologyRepairCatalog topologyRepairCatalog =
            AssetDatabase.LoadAssetAtPath<
                DungeonTopologyRepairCatalog>(
                DungeonTopologyRepairAuthoringUtility.CatalogPath);
        Require(
            topologyRepairCatalog != null,
            "Project 형태 보정 Catalog 누락");

        DungeonRunDefinition definition =
            AssetDatabase.LoadAssetAtPath<DungeonRunDefinition>(
                DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<
                DungeonRunDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }

        definition.Configure(
            flow,
            DefaultSeed,
            MaximumGenerationAttempts,
            5f,
            45f);
        definition.ConfigurePlayRooms(null);
        definition.ConfigureArenaTiles(
            arenaTileSet,
            DefaultArenaCount,
            DefaultTotalMainPathTileCount);
        definition.ConfigureEndCapTiles(endCapTileSet);
        definition.ConfigureTopologyRepairs(topologyRepairCatalog);
        definition.ConfigureLayoutQuality(
            DungeonLayoutQualityEvaluator.DefaultCandidateCount,
            DungeonLayoutQualityEvaluator.DefaultMinimumBranchTileCount,
            DungeonLayoutQualityEvaluator.DefaultMaximumPlanarAspectRatio);
        definition.ConfigureRunScale(
            1,
            8,
            0.25f,
            4f);
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        GameObject exitPortalPrefabAsset =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                DungeonPortalAuthoringBuilder.ExitPrefabPath);
        Require(
            exitPortalPrefabAsset != null
            && exitPortalPrefabAsset.GetComponent<DungeonPortalExit>()
                != null,
            "Dungeon exit portal prefab 누락");

        Scene scene = OpenOrCreateScene();
        if (!IsCurrentSceneAuthoring(
                scene,
                definition,
                exitPortalPrefabAsset))
        {
            RebuildScene(
                scene,
                definition,
                exitPortalPrefabAsset);
            Require(
                EditorSceneManager.SaveScene(scene, ScenePath),
                "DungeonRunScene 저장 실패");
        }

        EnsureBuildSettingsEntry();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string validation = DungeonRunSceneValidator.ValidateOrThrow();
        return "[DungeonRunSceneAuthoringBuilder] 완료\n" + validation;
    }

    private static Scene OpenOrCreateScene()
    {
        SceneAsset sceneAsset =
            AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (sceneAsset != null)
        {
            return EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
        }

        return EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
    }

    private static bool IsCurrentSceneAuthoring(
        Scene scene,
        DungeonRunDefinition definition,
        GameObject exitPortalPrefab)
    {
        DungeonRunSceneAuthoringMarker[] markers =
            FindComponentsInScene<DungeonRunSceneAuthoringMarker>(scene);
        DungeonRunFlow[] flows =
            FindComponentsInScene<DungeonRunFlow>(scene);
        if (markers.Length != 1
            || markers[0].AuthoringVersion != AuthoringVersion
            || flows.Length != 1)
        {
            return false;
        }

        DungeonRunFlow flow = flows[0];
        DungeonBossEncounterBridge bossBridge =
            flow.GetComponent<DungeonBossEncounterBridge>();
        DungeonRoomVisibilityController visibilityController =
            flow.RoomVisibilityController;
        Transform shadowLightTransform =
            flow.transform.Find(ShadowLightObjectName);
        Light shadowLight = shadowLightTransform != null
            ? shadowLightTransform.GetComponent<Light>()
            : null;
        const float defaultLengthMultiplier = 1f;
        return flow.Definition == definition
            && flow.RuntimeDungeon != null
            && flow.GeneratedRoot != null
            && flow.PlayerSpawnPlacer != null
            && flow.PlayerSpawnPlacer.GeneratedRoot
                == flow.GeneratedRoot
            && flow.ExitPortalPrefab == exitPortalPrefab
            && flow.DebugReturnInput != null
            && flow.DebugReturnInput.RunFlow == flow
            && bossBridge != null
            && bossBridge.RunFlow == flow
            && visibilityController != null
            && visibilityController.RuntimeDungeon
                == flow.RuntimeDungeon
            && visibilityController.RoomCulling != null
            && !visibilityController.RoomCulling.enabled
            && visibilityController.RoomCulling.AdjacentTileDepth == 2
            && !visibilityController.RoomCulling
                .CullBehindClosedDoors
            && flow.RuntimeDungeon.Root == flow.GeneratedRoot
            && flow.RuntimeDungeon.Generator != null
            && flow.RuntimeDungeon.Generator.MaxAttemptCount
                == definition.MaxAttemptCount
            && Mathf.Approximately(
                flow.RuntimeDungeon.Generator.LengthMultiplier,
                defaultLengthMultiplier)
            && !flow.RuntimeDungeon.GenerateOnStart
            && IsShadowLightPolicyCurrent(shadowLight)
            && RenderSettings.sun == shadowLight;
    }

    private static void RebuildScene(
        Scene scene,
        DungeonRunDefinition definition,
        GameObject exitPortalPrefab)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            UnityEngine.Object.DestroyImmediate(roots[i]);

        GameObject flowRoot = new("DungeonRun");
        SceneManager.MoveGameObjectToScene(flowRoot, scene);
        RuntimeDungeon runtimeDungeon =
            flowRoot.AddComponent<RuntimeDungeon>();
        runtimeDungeon.GenerateOnStart = false;
        runtimeDungeon.Generator.MaxAttemptCount =
            definition.MaxAttemptCount;
        runtimeDungeon.Generator.LengthMultiplier = 1f;

        DungeonRunFlow flow = flowRoot.AddComponent<DungeonRunFlow>();
        DungeonBossEncounterBridge bossBridge =
            flowRoot.AddComponent<DungeonBossEncounterBridge>();
        DungeonPlayerSpawnPlacer playerSpawnPlacer =
            flowRoot.AddComponent<DungeonPlayerSpawnPlacer>();
        DungeonRunDebugReturnInput debugReturnInput =
            flowRoot.AddComponent<DungeonRunDebugReturnInput>();
        AdjacentRoomCulling roomCulling =
            flowRoot.AddComponent<AdjacentRoomCulling>();
        roomCulling.enabled = false; // 임시 전체 타일 표시
        roomCulling.AdjacentTileDepth = 2;
        roomCulling.CullBehindClosedDoors = false;
        roomCulling.IncludeDisabledComponents = false;
        DungeonRoomVisibilityController visibilityController =
            flowRoot.AddComponent<DungeonRoomVisibilityController>();
        visibilityController.Configure(runtimeDungeon, roomCulling);
        DungeonRunSceneAuthoringMarker marker =
            flowRoot.AddComponent<DungeonRunSceneAuthoringMarker>();
        marker.Configure(AuthoringVersion);
        Light shadowLight = CreateShadowLight(flowRoot.transform);
        RenderSettings.sun = shadowLight;

        GameObject generatedRoot = new("GeneratedDungeon");
        generatedRoot.transform.SetParent(flowRoot.transform, false);
        runtimeDungeon.Root = generatedRoot;
        playerSpawnPlacer.Configure(generatedRoot);
        flow.Configure(
            runtimeDungeon,
            definition,
            generatedRoot,
            playerSpawnPlacer,
            exitPortalPrefab,
            debugReturnInput,
            visibilityController);
        bossBridge.Configure(flow);
        EditorUtility.SetDirty(flowRoot);
    }

    public static bool IsShadowLightPolicyCurrent(Light light)
    {
        return light != null
            && light.type == LightType.Directional
            && Mathf.Approximately(
                light.intensity,
                ShadowLightIntensity)
            && Mathf.Approximately(
                light.shadowStrength,
                ShadowStrength)
            && Approximately(light.color, ShadowLightColor)
            && light.shadows == LightShadows.Soft
            && light.lightmapBakeType == LightmapBakeType.Realtime
            && Quaternion.Angle(
                light.transform.localRotation,
                Quaternion.Euler(ShadowLightEuler)) < 0.01f;
    }

    private static Light CreateShadowLight(Transform parent)
    {
        GameObject lightObject = new(ShadowLightObjectName);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localPosition = Vector3.zero;
        lightObject.transform.localRotation =
            Quaternion.Euler(ShadowLightEuler);
        lightObject.transform.localScale = Vector3.one;

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = ShadowLightColor;
        light.intensity = ShadowLightIntensity;
        light.bounceIntensity = 0f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = ShadowStrength;
        light.shadowBias = 0.05f;
        light.shadowNormalBias = 0.4f;
        light.shadowNearPlane = 0.2f;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.renderMode = LightRenderMode.Auto;
        light.cullingMask = Physics.AllLayers;
        return light;
    }

    private static bool Approximately(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) < 0.001f
            && Mathf.Abs(left.g - right.g) < 0.001f
            && Mathf.Abs(left.b - right.b) < 0.001f
            && Mathf.Abs(left.a - right.a) < 0.001f;
    }

    private static void EnsureBuildSettingsEntry()
    {
        List<EditorBuildSettingsScene> scenes =
            new(EditorBuildSettings.scenes);
        scenes.RemoveAll(scene =>
            string.Equals(
                scene.path,
                ScenePath,
                StringComparison.Ordinal));
        int index = Mathf.Min(BuildSettingsIndex, scenes.Count);
        scenes.Insert(index, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static T[] FindComponentsInScene<T>(Scene scene)
        where T : Component
    {
        List<T> results = new();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            results.AddRange(roots[i].GetComponentsInChildren<T>(true));
        return results.ToArray();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent =
            Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        Require(
            !string.IsNullOrWhiteSpace(parent),
            "폴더 부모 경로 누락: " + folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
