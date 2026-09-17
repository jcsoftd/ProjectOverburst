using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonHandcraftedDemoAuditTool
{
    private const string DemoScenePath =
        "Assets/ThirdParty/04_환경맵/Multistory Dungeons 2/"
        + "Scenes/Top-Down Demo 01.unity";
    private const string OutputDirectory =
        "Logs/DungeonHandcraftedDemo";

    public static void RunFromCommandLine()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            Scene scene = EditorSceneManager.OpenScene(
                DemoScenePath,
                OpenSceneMode.Single);
            Camera sourceCamera = FindSceneComponents<Camera>(scene)
                .FirstOrDefault(camera =>
                    camera.CompareTag("MainCamera"));
            Require(sourceCamera != null, "수작업 데모 Main Camera 누락");

            CaptureCamera(
                sourceCamera,
                Path.Combine(
                    OutputDirectory,
                    "01_handcrafted_camera.png"));

            Renderer[] renderers = FindSceneComponents<Renderer>(scene)
                .Where(renderer =>
                    renderer != null
                    && renderer.gameObject.activeInHierarchy)
                .ToArray();
            Require(renderers.Length > 0, "수작업 데모 Renderer 누락");
            Bounds bounds = CalculateBounds(renderers);

            GameObject overviewObject =
                new("Handcrafted Demo Top-down Camera");
            SceneManager.MoveGameObjectToScene(overviewObject, scene);
            Camera overview = overviewObject.AddComponent<Camera>();
            overview.CopyFrom(sourceCamera);
            overview.enabled = false;
            overview.clearFlags = CameraClearFlags.Color;
            overview.backgroundColor =
                new Color(0.025f, 0.03f, 0.04f, 1f);
            overview.orthographic = true;
            overview.orthographicSize = Mathf.Max(
                bounds.extents.x * 0.65f,
                bounds.extents.z * 1.08f,
                8f);
            overview.transform.position =
                bounds.center + Vector3.up * 250f;
            overview.transform.rotation =
                Quaternion.Euler(90f, 0f, 0f);
            CaptureCamera(
                overview,
                Path.Combine(
                    OutputDirectory,
                    "02_handcrafted_overview.png"));

            Dictionary<string, int> prefabCounts =
                CountPrefabRoots(scene);
            List<string> report = new()
            {
                "[DungeonHandcraftedDemoAuditTool] PASS",
                $"RootCount={scene.rootCount}",
                $"RendererCount={renderers.Length}",
                $"LightCount={FindSceneComponents<Light>(scene).Length}",
                $"ParticleSystemCount="
                    + $"{FindSceneComponents<ParticleSystem>(scene).Length}",
                $"BoundsCenter={bounds.center}",
                $"BoundsSize={bounds.size}",
                $"OutermostPrefabInstanceCount="
                    + $"{prefabCounts.Values.Sum()}",
                $"UniquePrefabAssetCount={prefabCounts.Count}",
                "TopPrefabAssets:"
            };
            report.AddRange(
                prefabCounts
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(80)
                    .Select(pair => $"{pair.Value}\t{pair.Key}"));
            File.WriteAllLines(
                Path.Combine(OutputDirectory, "audit_report.txt"),
                report);

            UnityEngine.Object.DestroyImmediate(overviewObject);
            Debug.Log("[DungeonHandcraftedDemoAuditTool] PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static Dictionary<string, int> CountPrefabRoots(Scene scene)
    {
        Dictionary<string, int> counts =
            new(StringComparer.Ordinal);
        Transform[] transforms = FindSceneComponents<Transform>(scene);
        for (int i = 0; i < transforms.Length; i++)
        {
            GameObject gameObject = transforms[i].gameObject;
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
                continue;

            string path =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    gameObject);
            if (string.IsNullOrWhiteSpace(path))
                path = "<Unknown>";
            counts.TryGetValue(path, out int count);
            counts[path] = count + 1;
        }

        return counts;
    }

    private static Bounds CalculateBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static T[] FindSceneComponents<T>(Scene scene)
        where T : Component
    {
        List<T> results = new();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            results.AddRange(roots[i].GetComponentsInChildren<T>(true));
        return results.ToArray();
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
            File.WriteAllBytes(
                outputPath,
                texture.EncodeToPNG());
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
            throw new InvalidOperationException(message);
    }
}
