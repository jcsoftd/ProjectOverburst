using System;
using System.IO;
using System.Linq;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonSourceReferenceCaptureTool
{
    private const string SourceScenePath =
        "Assets/ThirdParty/04_환경맵/Multistory Dungeons 2/"
        + "DunGen Presets/Demo/Top-Down Test Scene.unity";
    private const string OutputDirectory =
        "Logs/DungeonSourceReference";

    public static void RunFromCommandLine()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            EditorSceneManager.OpenScene(
                SourceScenePath,
                OpenSceneMode.Single);

            GameObject layout = SceneManager.GetActiveScene()
                .GetRootGameObjects()
                .FirstOrDefault(root => root.name == "Dungeon Layout");
            Require(layout != null, "공급사 Dungeon Layout 누락");

            Camera sourceCamera = UnityEngine.Object
                .FindObjectsByType<Camera>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(camera =>
                    camera.CompareTag("MainCamera"));
            Require(sourceCamera != null, "공급사 Main Camera 누락");

            CaptureCamera(
                sourceCamera,
                Path.Combine(
                    OutputDirectory,
                    "01_source_authored_camera.png"));

            Bounds bounds = CalculateRendererBounds(layout);
            GameObject overviewObject =
                new("Dungeon Source Reference Overview Camera");
            Camera overview = overviewObject.AddComponent<Camera>();
            overview.CopyFrom(sourceCamera);
            overview.enabled = false;
            overview.clearFlags = CameraClearFlags.Color;
            overview.backgroundColor =
                new Color(0.025f, 0.03f, 0.04f, 1f);
            overview.orthographic = true;
            overview.orthographicSize = Mathf.Max(
                bounds.extents.x * 0.78f,
                bounds.extents.z * 1.3f,
                8f);
            overview.transform.position =
                bounds.center
                + new Vector3(
                    bounds.extents.x * 0.9f,
                    Mathf.Max(45f, bounds.extents.y * 3f),
                    -bounds.extents.z * 0.9f);
            overview.transform.LookAt(bounds.center);

            CaptureCamera(
                overview,
                Path.Combine(
                    OutputDirectory,
                    "02_source_overview.png"));

            Vector3 authoredCameraLocalPosition =
                sourceCamera.transform.localPosition;
            Quaternion authoredCameraLocalRotation =
                sourceCamera.transform.localRotation;
            DungeonFlow projectFlow =
                AssetDatabase.LoadAssetAtPath<DungeonFlow>(
                    DungeonContentAuthoringBuilder.FlowOutputPath);
            Require(projectFlow != null, "Project DungeonFlow 누락");

            GameObject generatedRoot =
                new("Project Standalone Tile Comparison Dungeon");
            generatedRoot.transform.position =
                new Vector3(500f, 0f, 0f);
            DungeonGenerator generator =
                new(generatedRoot)
                {
                    DungeonFlow = projectFlow,
                    Seed = 27008,
                    ShouldRandomizeSeed = false,
                    MaxAttemptCount = 100,
                    GenerateAsynchronously = false,
                    PlaceTileTriggers = true,
                    TileTriggerLayer = 2
                };
            generator.Generate();
            Require(
                generator.Status == GenerationStatus.Complete,
                "Project 독립 타일 비교 생성 실패");

            Tile projectStartTile =
                generator.CurrentDungeon.MainPathTiles[0];
            DungeonRoomAnchor projectStartAnchor =
                projectStartTile
                    .GetComponentsInChildren<DungeonRoomAnchor>(true)
                    .FirstOrDefault(anchor =>
                        anchor.Supports(
                            DungeonRoomAnchorRole.Start));
            Require(
                projectStartAnchor != null,
                "Project 시작 Anchor 누락");

            GameObject comparisonRig =
                new("Project Standalone Tile Comparison Camera Rig");
            comparisonRig.transform.SetPositionAndRotation(
                projectStartAnchor.transform.position,
                projectStartAnchor.transform.rotation);
            GameObject comparisonCameraObject =
                new("Project Standalone Tile Comparison Camera");
            comparisonCameraObject.transform.SetParent(
                comparisonRig.transform,
                false);
            comparisonCameraObject.transform.localPosition =
                authoredCameraLocalPosition;
            comparisonCameraObject.transform.localRotation =
                authoredCameraLocalRotation;
            Camera comparisonCamera =
                comparisonCameraObject.AddComponent<Camera>();
            comparisonCamera.CopyFrom(sourceCamera);
            comparisonCamera.enabled = false;
            CaptureCamera(
                comparisonCamera,
                Path.Combine(
                    OutputDirectory,
                    "03_project_standalone_same_camera.png"));

            File.WriteAllText(
                Path.Combine(OutputDirectory, "capture_report.txt"),
                "[DungeonSourceReferenceCaptureTool] PASS"
                + Environment.NewLine
                + $"LayoutChildCount={layout.transform.childCount}"
                + Environment.NewLine
                + $"RendererCount={layout.GetComponentsInChildren<Renderer>(true).Length}"
                + Environment.NewLine
                + $"BoundsCenter={bounds.center}"
                + Environment.NewLine
                + $"BoundsSize={bounds.size}"
                + Environment.NewLine
                + $"ProjectSeed={generator.Seed}"
                + Environment.NewLine
                + $"ProjectTileCount={generator.CurrentDungeon.AllTiles.Count}"
                + Environment.NewLine
                + $"ProjectRendererCount={generatedRoot.GetComponentsInChildren<Renderer>(true).Length}"
                + Environment.NewLine);

            UnityEngine.Object.DestroyImmediate(comparisonCameraObject);
            UnityEngine.Object.DestroyImmediate(comparisonRig);
            UnityEngine.Object.DestroyImmediate(generatedRoot);
            UnityEngine.Object.DestroyImmediate(overviewObject);
            Debug.Log("[DungeonSourceReferenceCaptureTool] PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static Bounds CalculateRendererBounds(GameObject root)
    {
        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);
        Require(renderers.Length > 0, "공급사 Renderer 누락");

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private static void CaptureCamera(
        Camera camera,
        string outputPath)
    {
        RenderTexture renderTexture = new(
            1600,
            1000,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        Texture2D texture = new(
            1600,
            1000,
            TextureFormat.RGBA32,
            false,
            false);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            texture.ReadPixels(
                new Rect(0, 0, 1600, 1000),
                0,
                0,
                false);
            texture.Apply(false, false);
            File.WriteAllBytes(outputPath, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(renderTexture);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
