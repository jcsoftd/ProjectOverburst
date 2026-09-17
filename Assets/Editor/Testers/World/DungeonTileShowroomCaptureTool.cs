using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonTileShowroomCaptureTool
{
    private const string OutputDirectory =
        "Logs/DungeonTileShowroom";
    private const string OutputPath =
        OutputDirectory + "/overview.png";
    private const int CaptureWidth = 1200;
    private const int CaptureHeight = 2000;

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Capture Tile Showroom")]
    public static void CaptureFromMenu()
    {
        Debug.Log(Capture());
        AssetDatabase.Refresh();
    }

    public static void RunFromCommandLine()
    {
        try
        {
            string report = Capture();
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static string Capture()
    {
        Directory.CreateDirectory(OutputDirectory);
        EditorSceneManager.OpenScene(
            DungeonRunSceneAuthoringBuilder.ScenePath,
            OpenSceneMode.Single);

        DungeonTileShowroomMarker marker =
            UnityEngine.Object.FindFirstObjectByType<
                DungeonTileShowroomMarker>(
                FindObjectsInactive.Include);
        Require(marker != null,
            "Dungeon 타일 쇼룸을 찾지 못했습니다.");
        Require(marker.gameObject.activeInHierarchy,
            "Dungeon 타일 쇼룸이 비활성 상태입니다.");

        MeshRenderer[] renderers =
            marker.GetComponentsInChildren<MeshRenderer>(true);
        Require(renderers.Length > 0,
            "쇼룸 MeshRenderer가 없습니다.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        GameObject cameraObject =
            new("DungeonTileShowroomCaptureCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor =
            new Color(0.035f, 0.04f, 0.05f, 1f);
        camera.orthographic = true;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane =
            Mathf.Max(500f, bounds.size.y * 5f);
        camera.transform.position =
            bounds.center
            + Vector3.up * Mathf.Max(120f, bounds.size.y * 3f);
        camera.transform.rotation =
            Quaternion.Euler(90f, 0f, 0f);

        float aspect = CaptureWidth / (float)CaptureHeight;
        camera.orthographicSize = Mathf.Max(
            bounds.extents.z * 1.06f,
            bounds.extents.x / aspect * 1.06f,
            10f);

        RenderTexture renderTexture = new(
            CaptureWidth,
            CaptureHeight,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        Texture2D texture = new(
            CaptureWidth,
            CaptureHeight,
            TextureFormat.RGBA32,
            false,
            false);
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            texture.ReadPixels(
                new Rect(
                    0f,
                    0f,
                    CaptureWidth,
                    CaptureHeight),
                0,
                0,
                false);
            texture.Apply(false, false);
            File.WriteAllBytes(
                OutputPath,
                texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        int prefabRootCount = marker
            .GetComponentsInChildren<Transform>(true)
            .Count(transform =>
                PrefabUtility.IsAnyPrefabInstanceRoot(
                    transform.gameObject));
        return "[DungeonTileShowroomCaptureTool] PASS\n"
            + $"Output={OutputPath}\n"
            + $"Resolution={CaptureWidth}x{CaptureHeight}\n"
            + $"PrefabRootCount={prefabRootCount}\n"
            + $"BoundsSize={bounds.size}";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
