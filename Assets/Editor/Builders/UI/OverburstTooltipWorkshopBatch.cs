using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Applies the fixed-size quality mark layout and refreshes only tooltip instances in the workshop.</summary>
public static class OverburstTooltipWorkshopBatch
{
    private const string ExpectedSceneSha256 = "D60180CA33B9581E9EDCFA6AF640F29B8E991F91401C63BE5B9B53A0C08C09ED";
    private const string OutputFolder = "개인파일/코덱스산출/UI/20260923_TooltipMaxMarks";

    public static void Run()
    {
        VerifySceneHash();
        Debug.Log("[TooltipWorkshop] " + OverburstTooltipHybridApplier.ApplyToPrefab());
        Debug.Log("[TooltipWorkshop] " + OverburstUIWorkshopBuilder.SyncApprovedTooltipScene(ExpectedSceneSha256));
        Scene previousActive = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(OverburstUIWorkshopBuilder.ScenePath);
        bool openedAdditively = !scene.IsValid() || !scene.isLoaded;
        if (openedAdditively)
            scene = EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath, OpenSceneMode.Additive);
        string result;
        try
        {
            VerifySceneInstances(scene);
            SceneManager.SetActiveScene(scene);
            result = OverburstTooltipHybridValidation.Run();
            CaptureBoard(scene);
        }
        finally
        {
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (openedAdditively && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", OutputFolder));
        File.WriteAllText(Path.Combine(root, "batch-result.txt"), result + Environment.NewLine +
            "PASS management hover, three screen samples, three board samples, linked approved prefab, missing scripts 0" + Environment.NewLine);
        Debug.Log("[TooltipWorkshop] " + result + "; management scene PASS");
    }

    private static void VerifySceneHash()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OverburstUIWorkshopBuilder.ScenePath));
        using (SHA256 sha = SHA256.Create())
        using (FileStream file = File.OpenRead(path))
        {
            string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", string.Empty);
            if (!string.Equals(actual, ExpectedSceneSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Workshop scene changed after backup: " + actual);
        }
    }

    private static void VerifySceneInstances(Scene scene)
    {
        if (scene.path != OverburstUIWorkshopBuilder.ScenePath || scene.isDirty)
            throw new InvalidOperationException("Workshop scene was not saved cleanly.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstUIWorkshopBuilder.TooltipPath);
        OverburstUIWorkshop workshop = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<OverburstUIWorkshop>(true)).FirstOrDefault();
        GameObject gallery = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == OverburstUIWorkshopBuilder.GalleryName);
        if (!workshop || !gallery) throw new InvalidOperationException("Workshop or gallery missing.");
        Transform board = gallery.transform.GetChild(4).Find(OverburstUIWorkshopBuilder.BoardNames[13]);
        Transform hover = workshop.transform.Find("80 Item Tooltip Hover");
        if (!board || !hover || !hover.GetComponent<OverburstUITooltipHost>())
            throw new InvalidOperationException("Tooltip board or hover binding missing.");
        CheckPanel(hover.GetComponentInChildren<OverburstUITooltipView>(true).gameObject, prefab);
        CheckSamples(workshop.transform.Find("Tooltip Samples"), prefab);
        CheckSamples(board.Find("Tooltip Samples"), prefab);
    }

    private static void CheckSamples(Transform root, GameObject prefab)
    {
        if (!root || root.childCount != 3)
            throw new InvalidOperationException("Expected exactly three tooltip samples.");
        foreach (Transform child in root) CheckPanel(child.gameObject, prefab);
    }

    private static void CheckPanel(GameObject panel, GameObject prefab)
    {
        if (PrefabUtility.GetCorrespondingObjectFromSource(panel) != prefab ||
            !panel.GetComponent<OverburstTooltipHybridSkin>())
            throw new InvalidOperationException("Tooltip sample lost its approved prefab link: " + panel.name);
        foreach (Transform child in panel.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                throw new InvalidOperationException("Tooltip sample has a missing script: " + child.name);
    }

    private static void CaptureBoard(Scene scene)
    {
        GameObject gallery = scene.GetRootGameObjects()
            .First(root => root.name == OverburstUIWorkshopBuilder.GalleryName);
        Transform board = gallery.transform.GetChild(4).Find(OverburstUIWorkshopBuilder.BoardNames[13]);
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", OutputFolder,
            "Captures", "management-board14.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        GameObject cameraObject = new GameObject("Temporary tooltip board capture camera", typeof(Camera));
        RenderTexture target = null;
        Texture2D pixels = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 540f;
            camera.aspect = 1920f / 1080f;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.018f, .016f, .017f);
            camera.transform.position = board.position + new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            camera.targetTexture = null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels) Object.DestroyImmediate(pixels);
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            Object.DestroyImmediate(cameraObject);
        }
    }
}
