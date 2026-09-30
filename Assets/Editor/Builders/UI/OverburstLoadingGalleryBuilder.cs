using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Adds connected loading-state boards without rebuilding the existing UI workshop.</summary>
public static class OverburstLoadingGalleryBuilder
{
    private const string ScenePath = "Assets/ProjectOverburst/00_Scenes/DEV_UIManagement.unity";
    private const string PrefabPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstLoadingBar_Rpg11.prefab";
    private const string GroupName = "07_Loading • 진행 상태";

    [MenuItem("OVERBURST/UI/Add Loading Boards To Management Scene")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before updating the UI management scene.");

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!source)
            throw new InvalidOperationException("The project loading bar prefab is missing.");

        Scene previous = SceneManager.GetActiveScene();
        Scene gallery = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !gallery.IsValid() || !gallery.isLoaded;
        if (openedHere)
            gallery = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            Transform root = gallery.GetRootGameObjects()
                .FirstOrDefault(go => go.name == OverburstUIWorkshopBuilder.GalleryName)?.transform;
            if (!root)
                throw new InvalidOperationException("UI gallery root is missing from the management scene.");

            Transform group = root.Find(GroupName);
            if (!group)
            {
                var groupObject = new GameObject(GroupName);
                groupObject.transform.SetParent(root, false);
                group = groupObject.transform;
            }

            Font titleFont = root.GetComponentsInChildren<Text>(true)
                .FirstOrDefault(label => label.name == "Board Title")?.font;
            if (!titleFont)
                throw new InvalidOperationException("A workshop title font is required for matching board labels.");

            AddBoard(group, source, titleFont, "16 로딩 · 진행 중", 0f, .57f);
            AddBoard(group, source, titleFont, "17 로딩 · 완료", 2120f, 1f);
            EditorSceneManager.MarkSceneDirty(gallery);
            if (!EditorSceneManager.SaveScene(gallery))
                throw new InvalidOperationException("Saving the UI management scene failed.");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
            if (openedHere && gallery.IsValid() && gallery.isLoaded)
                EditorSceneManager.CloseScene(gallery, true);
        }
    }

    private static void AddBoard(Transform group, GameObject source, Font font, string name, float x, float progress)
    {
        Transform oldBoard = group.Find(name);
        if (oldBoard)
            UnityEngine.Object.DestroyImmediate(oldBoard.gameObject);

        var boardObject = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        boardObject.transform.SetParent(group, false);
        RectTransform board = (RectTransform)boardObject.transform;
        board.sizeDelta = new Vector2(1920f, 1080f);
        board.anchoredPosition = new Vector2(x, -7920f);
        board.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        RectTransform backdrop = AddRect("Backdrop", board, Vector2.zero, new Vector2(1920f, 1080f));
        Image background = backdrop.gameObject.AddComponent<Image>();
        background.color = new Color(.018f, .013f, .015f, 1f);
        background.raycastTarget = false;

        AddLabel(board, font, "Board Title", name, new Vector2(0f, 600f), new Vector2(1920f, 50f), 32, new Color(.89f, .71f, .39f));
        AddLabel(board, font, "Board Note", "1920 × 1080   /   Connected Prefab   /   배경 이미지 교체 예정",
            new Vector2(0f, 560f), new Vector2(1920f, 28f), 18, new Color(.71f, .68f, .63f));

        GameObject barObject = (GameObject)PrefabUtility.InstantiatePrefab(source, board);
        barObject.name = source.name;
        RectTransform bar = (RectTransform)barObject.transform;
        bar.anchorMin = bar.anchorMax = new Vector2(.5f, 0f);
        bar.pivot = new Vector2(.5f, .5f);
        bar.anchoredPosition = new Vector2(0f, 98f);
        bar.sizeDelta = new Vector2(3270f, 154f);
        bar.localScale = Vector3.one * .45f;

        RectTransform fill = (RectTransform)bar.Find("Fill Rect/Fill Mask");
        Text percentage = bar.Find("Text Pct").GetComponent<Text>();
        fill.sizeDelta = new Vector2(3194f * progress, fill.sizeDelta.y);
        percentage.text = Mathf.RoundToInt(progress * 100f) + "%";
        PrefabUtility.RecordPrefabInstancePropertyModifications(bar);
        PrefabUtility.RecordPrefabInstancePropertyModifications(fill);
        PrefabUtility.RecordPrefabInstancePropertyModifications(percentage);

        foreach (Transform child in board.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = 31;
            child.gameObject.tag = "EditorOnly";
            if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
        }
        group.gameObject.tag = "EditorOnly";
    }

    private static RectTransform AddRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static void AddLabel(Transform parent, Font font, string name, string content, Vector2 position,
        Vector2 size, int fontSize, Color color)
    {
        RectTransform rect = AddRect(name, parent, position, size);
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = TextAnchor.MiddleLeft;
        label.text = content;
        label.raycastTarget = false;
    }
}
