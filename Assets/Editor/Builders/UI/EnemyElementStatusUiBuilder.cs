using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class EnemyElementStatusUiBuilder
{
    private const string PersistentScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const string NormalHpBarPath =
        "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Normal.prefab";
    private const string EliteHpBarPath =
        "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Elite.prefab";
    private const string IconFolder = "Assets/ProjectOverburst/05_Art/UI/Icons/Elements";

    private static readonly string[] IconPropertyNames =
    {
        "fireIcon",
        "waterIcon",
        "iceIcon",
        "electricIcon",
        "windIcon",
        "natureIcon",
        "earthIcon"
    };

    private static readonly string[] IconAssetNames =
    {
        "Icon_Element_Fire.png",
        "Icon_Element_Water.png",
        "Icon_Element_Ice.png",
        "Icon_Element_Electric.png",
        "Icon_Element_Wind.png",
        "Icon_Element_Nature.png",
        "Icon_Element_Earth.png"
    };

    [MenuItem("Tools/Project VTP/UI/몬스터 원소 상태 아이콘 다시 만들기")]
    public static void RebuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Rebuild();
        Debug.Log("[EnemyElementStatusUiBuilder] 몬스터 원소 상태 아이콘 UI 구성을 완료했습니다.");
    }

    public static void RunOnceFromCommandLine()
    {
        try
        {
            Rebuild();
            Debug.Log("[EnemyElementStatusUiBuilder] COMMAND_LINE_SUCCESS");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Rebuild()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Sprite[] icons = ImportAndLoadIcons();
        ConfigureHpBarPrefab(NormalHpBarPath, icons);
        ConfigureHpBarPrefab(EliteHpBarPath, icons);
        ConfigurePersistentHud(icons);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ValidateAuthoredAssets();
    }

    private static Sprite[] ImportAndLoadIcons()
    {
        Sprite[] icons = new Sprite[IconAssetNames.Length];
        for (int i = 0; i < IconAssetNames.Length; i++)
        {
            string path = IconFolder + "/" + IconAssetNames[i];
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("원소 아이콘 TextureImporter를 찾지 못했습니다: " + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 128;
            importer.SaveAndReimport();

            icons[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (icons[i] == null)
                throw new InvalidOperationException("원소 아이콘 Sprite를 불러오지 못했습니다: " + path);
        }

        return icons;
    }

    private static void ConfigureHpBarPrefab(string prefabPath, Sprite[] icons)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            EnemyHpBarView hpBarView = root.GetComponent<EnemyHpBarView>();
            if (hpBarView == null)
                throw new InvalidOperationException("EnemyHpBarView가 없습니다: " + prefabPath);

            ElementalStatusIconStrip strip = ConfigureStrip(
                root,
                icons,
                14f,
                new Vector2(14f, 14f),
                new Vector2(0f, 11f),
                Vector2.one * 0.5f,
                Vector2.one * 0.5f);

            SerializedObject viewObject = new SerializedObject(hpBarView);
            viewObject.FindProperty("elementalStatusIcons").objectReferenceValue = strip;
            viewObject.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigurePersistentHud(Sprite[] icons)
    {
        Scene previousScene = SceneManager.GetActiveScene();
        string previousScenePath = previousScene.path;
        Scene scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
        try
        {
            EnemyTargetHpSlotUI slot = UnityEngine.Object.FindFirstObjectByType<EnemyTargetHpSlotUI>(
                FindObjectsInactive.Include);
            if (slot == null)
                throw new InvalidOperationException("PersistentScene에서 EnemyTargetHpSlotUI를 찾지 못했습니다.");

            RectTransform hudRect = slot.transform as RectTransform;
            if (hudRect == null)
                throw new InvalidOperationException("EnemyTargetHpSlotUI의 RectTransform이 없습니다.");

            hudRect.sizeDelta = new Vector2(hudRect.sizeDelta.x, 90f);
            ElementalStatusIconStrip strip = ConfigureStrip(
                slot.gameObject,
                icons,
                22f,
                new Vector2(22f, 22f),
                new Vector2(0f, -35f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f));

            SerializedObject slotObject = new SerializedObject(slot);
            slotObject.FindProperty("elementalStatusIcons").objectReferenceValue = strip;
            slotObject.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (!string.IsNullOrEmpty(previousScenePath)
                && !string.Equals(previousScenePath, PersistentScenePath, StringComparison.OrdinalIgnoreCase))
            {
                EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }
        }
    }

    private static ElementalStatusIconStrip ConfigureStrip(
        GameObject owner,
        Sprite[] icons,
        float slotSize,
        Vector2 contentSize,
        Vector2 anchoredPosition,
        Vector2 anchor,
        Vector2 pivot)
    {
        Transform oldContent = owner.transform.Find("ElementalStatusStrip");
        if (oldContent != null)
            UnityEngine.Object.DestroyImmediate(oldContent.gameObject);

        ElementalStatusIconStrip strip = owner.GetComponent<ElementalStatusIconStrip>();
        if (strip == null)
            strip = owner.AddComponent<ElementalStatusIconStrip>();

        GameObject contentObject = new GameObject(
            "ElementalStatusStrip",
            typeof(RectTransform));
        contentObject.layer = owner.layer;
        contentObject.transform.SetParent(owner.transform, false);

        RectTransform contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = anchor;
        contentRect.anchorMax = anchor;
        contentRect.pivot = pivot;
        contentRect.anchoredPosition = anchoredPosition;
        contentRect.sizeDelta = contentSize;
        contentRect.localScale = Vector3.one;

        GameObject slotObject = new GameObject(
            "StatusBadge",
            typeof(RectTransform));
        slotObject.layer = owner.layer;
        slotObject.transform.SetParent(contentRect, false);

        RectTransform slotRect = slotObject.GetComponent<RectTransform>();
        slotRect.anchorMin = Vector2.one * 0.5f;
        slotRect.anchorMax = Vector2.one * 0.5f;
        slotRect.pivot = Vector2.one * 0.5f;
        slotRect.anchoredPosition = Vector2.zero;
        slotRect.sizeDelta = Vector2.one * slotSize;
        slotRect.localScale = Vector3.one;

        GameObject iconObject = new GameObject(
            "Icon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        iconObject.layer = owner.layer;
        iconObject.transform.SetParent(slotRect, false);

        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        iconRect.localScale = Vector3.one;

        Image iconImage = iconObject.GetComponent<Image>();
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        slotObject.SetActive(false);

        SerializedObject stripObject = new SerializedObject(strip);
        stripObject.FindProperty("contentRoot").objectReferenceValue = contentRect;
        stripObject.FindProperty("slotRoot").objectReferenceValue = slotRect;
        stripObject.FindProperty("iconImage").objectReferenceValue = iconImage;
        for (int i = 0; i < IconPropertyNames.Length; i++)
            stripObject.FindProperty(IconPropertyNames[i]).objectReferenceValue = icons[i];
        stripObject.ApplyModifiedPropertiesWithoutUndo();

        contentObject.SetActive(false);
        EditorUtility.SetDirty(strip);
        return strip;
    }

    private static void ValidateAuthoredAssets()
    {
        ValidatePrefab(NormalHpBarPath);
        ValidatePrefab(EliteHpBarPath);

        string previousScenePath = SceneManager.GetActiveScene().path;
        Scene scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
        try
        {
            EnemyTargetHpSlotUI slot = UnityEngine.Object.FindFirstObjectByType<EnemyTargetHpSlotUI>(
                FindObjectsInactive.Include);
            if (slot == null)
                throw new InvalidOperationException("화면 상단 몬스터 HP UI의 원소 상태 아이콘 검증에 실패했습니다.");

            ValidateStrip(slot.gameObject, "PersistentScene/EnemyTargetHpHud");

            Debug.Log("[EnemyElementStatusUiBuilder] VALIDATION_SUCCESS scene=" + scene.path);
        }
        finally
        {
            if (!string.IsNullOrEmpty(previousScenePath)
                && !string.Equals(previousScenePath, PersistentScenePath, StringComparison.OrdinalIgnoreCase))
            {
                EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }
        }
    }

    private static void ValidatePrefab(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new InvalidOperationException("머리 위 HP바 원소 상태 아이콘 검증에 실패했습니다: " + prefabPath);

        ValidateStrip(prefab, prefabPath);
    }

    private static void ValidateStrip(GameObject owner, string context)
    {
        ElementalStatusIconStrip strip = owner.GetComponent<ElementalStatusIconStrip>();
        Transform content = owner.transform.Find("ElementalStatusStrip");
        if (strip == null
            || content == null
            || content.childCount != 1
            || content.GetChild(0).name != "StatusBadge"
            || content.GetChild(0).GetComponent<Graphic>() != null)
        {
            throw new InvalidOperationException("배경 없는 단일 원소 상태 배지 구조가 아닙니다: " + context);
        }

        SerializedObject stripObject = new SerializedObject(strip);
        if (stripObject.FindProperty("contentRoot").objectReferenceValue == null
            || stripObject.FindProperty("slotRoot").objectReferenceValue == null
            || stripObject.FindProperty("iconImage").objectReferenceValue == null)
        {
            throw new InvalidOperationException("원소 상태 배지 UI 참조가 비어 있습니다: " + context);
        }

        for (int i = 0; i < IconPropertyNames.Length; i++)
        {
            if (stripObject.FindProperty(IconPropertyNames[i]).objectReferenceValue == null)
                throw new InvalidOperationException("원소 아이콘 참조가 비어 있습니다: " + context);
        }

        Transform[] transforms = owner.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[i].gameObject) > 0)
                throw new InvalidOperationException("Missing Script가 있습니다: " + context + "/" + transforms[i].name);
        }
    }
}
