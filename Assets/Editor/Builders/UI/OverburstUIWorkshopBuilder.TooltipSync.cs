using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class OverburstUIWorkshopBuilder
{
    /// <summary>Refreshes only the approved tooltip previews in the existing management scene.</summary>
    public static string SyncApprovedTooltipScene(string expectedSceneSha256)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        if (string.IsNullOrWhiteSpace(expectedSceneSha256)) throw new ArgumentException("Expected scene hash is required.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded && scene.isDirty)
            throw new InvalidOperationException("Preserve the workshop scene's unsaved changes first.");

        string sceneFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ScenePath));
        using (SHA256 sha = SHA256.Create())
        using (FileStream file = File.OpenRead(sceneFile))
        {
            string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", string.Empty);
            if (!string.Equals(actual, expectedSceneSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Management scene changed after the backup: " + actual);
        }

        GameObject tooltipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TooltipPath);
        if (!tooltipPrefab || !tooltipPrefab.GetComponent<OverburstTooltipHybridSkin>())
            throw new InvalidOperationException("The approved tooltip prefab is missing.");

        Scene previousActive = SceneManager.GetActiveScene();
        bool openedAdditively = !scene.IsValid() || !scene.isLoaded;
        if (openedAdditively) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
        OverburstUIWorkshop workshop = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<OverburstUIWorkshop>(true)).FirstOrDefault();
        GameObject gallery = scene.GetRootGameObjects().FirstOrDefault(root => root.name == GalleryName);
        if (!workshop || !gallery || gallery.transform.childCount < 5)
            throw new InvalidOperationException("The workshop controller or gallery is missing.");
        Transform board = gallery.transform.GetChild(4).Find(BoardNames[13]);
        if (!board) throw new InvalidOperationException("Tooltip gallery board 14 is missing.");

        Transform oldHover = workshop.transform.Find("80 Item Tooltip Hover");
        if (oldHover) Object.DestroyImmediate(oldHover.gameObject);
        RectTransform hover = Rect("80 Item Tooltip Hover", workshop.transform, Vector2.zero, Vector2.zero);
        Stretch(hover);
        Canvas overlay = hover.gameObject.AddComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = 32760;
        GameObject hoverPanel = (GameObject)PrefabUtility.InstantiatePrefab(tooltipPrefab, hover);
        hover.gameObject.AddComponent<OverburstUITooltipHost>()
            .Configure(hoverPanel.GetComponent<OverburstUITooltipView>(), TooltipCatalog());

        Transform oldSamples = workshop.transform.Find("Tooltip Samples");
        if (oldSamples) Object.DestroyImmediate(oldSamples.gameObject);
        GameObject screenSamples = BuildTooltipSamples(workshop.transform);
        workshop.ConfigureTooltips(screenSamples);

        Transform oldBoardSamples = board.Find("Tooltip Samples");
        if (oldBoardSamples) Object.DestroyImmediate(oldBoardSamples.gameObject);
        GameObject boardSamples = BuildTooltipSamples(board);
        foreach (Transform child in boardSamples.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = 31;
            child.gameObject.tag = "EditorOnly";
            if (!PrefabUtility.IsPartOfPrefabInstance(child.gameObject)) continue;
            PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            foreach (Component component in child.GetComponents<Component>())
                if (component) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        workshop.ShowTooltips();
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the management scene.");
        return "PASS management tooltip hover, three screen samples and board 14 synced to " + TooltipPath;
        }
        finally
        {
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (openedAdditively && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}
