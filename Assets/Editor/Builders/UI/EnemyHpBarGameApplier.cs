using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Local authoring tool. The three game prefabs are built from the approved concept visuals.
public static class EnemyHpBarGameApplier
{
    private const string ConceptFolder = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/HpBarConcepts";
    private const string LiveFolder = "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars";
    private const string PersistentScene = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const string ManagementScene = "Assets/ProjectOverburst/00_Scenes/DEV_UIManagement.unity";
    private const string FontPath = "Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf";

    [MenuItem("OVERBURST/UI/Apply Monster HP Bars To Game")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Exit Play Mode and wait for compilation before applying HP bars.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the currently open scene before applying HP bars.");

        BuildPrefabsOnly();
        ApplyPersistentScene();
        ApplyManagementScene();
        AssetDatabase.SaveAssets();
        Debug.Log("[EnemyHpBarGameApplier] Three live HP bars, PersistentScene and UI management preview updated.");
    }

    public static void BuildPrefabsOnly()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Exit Play Mode and wait for compilation before building HP bars.");
        RefuseRebuildOfTunedLiveBars();
        BuildLivePrefab("Normal", "Small");
        BuildLivePrefab("Normal", "Medium");
        BuildLivePrefab("Elite", "Elite");
        AssetDatabase.SaveAssets();
    }

    // 2026-10-01: 게임 HP 바(소형·중형·정예)는 적용 뒤 원소 상태 줄 등 조정값이 원본이다. 이미 있으면 컨셉에서 다시 만들지 않는다.
    private static void RefuseRebuildOfTunedLiveBars()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LivePath("Small")) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(LivePath("Medium")) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(LivePath("Elite")) != null)
            throw new InvalidOperationException("Apply Monster HP Bars To Game: live HP bars already exist and were tuned afterwards "
                + "(elemental status strip, layout). Delete them first to rebuild from the concepts.");
    }

    public static void ApplyManagementSceneOnly()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Exit Play Mode before editing the UI management scene.");
        ApplyManagementScene();
    }

    public static void ApplyPersistentSceneOnly()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Exit Play Mode before editing PersistentScene.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save or explicitly recover the open scene before editing PersistentScene.");
        ApplyPersistentScene();
    }

    public static string CaptureManagementBoard()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Exit Play Mode before capturing the HP bar gallery.");
        Scene scene = EditorSceneManager.OpenScene(ManagementScene, OpenSceneMode.Additive);
        OverburstUIWorkshop workshop = null;
        Camera camera = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (workshop == null) workshop = root.GetComponentInChildren<OverburstUIWorkshop>(true);
            if (camera == null) camera = root.GetComponentInChildren<Camera>(true);
        }
        if (workshop == null || camera == null)
            throw new InvalidOperationException("UI management scene is missing the workshop or camera.");
        workshop.ShowEnemies();
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.SaveScene(scene);

        const int width = 1920;
        const int height = 1080;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../개인파일/코덱스산출/UI/20260923_HpBarGameIntegration"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "ui-management-hp-bars.png");
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D pixels = null;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (pixels != null) Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
            EditorSceneManager.CloseScene(scene, true);
        }
        return path;
    }

    private static void BuildLivePrefab(string oldTier, string tier)
    {
        string oldPath = LiveFolder + "/PF_EnemyHpBar_" + oldTier + ".prefab";
        string conceptPath = ConceptFolder + "/PF_EnemyHpBar_Concept_" + tier + ".prefab";
        string outputPath = LivePath(tier);
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(oldPath);
        GameObject concept = AssetDatabase.LoadAssetAtPath<GameObject>(conceptPath);
        if (source == null || concept == null)
            throw new InvalidOperationException("Missing live source or concept prefab: " + tier);

        GameObject root = PrefabUtility.LoadPrefabContents(oldPath);
        try
        {
            root.name = "PF_EnemyHpBar_" + tier;
            EnemyHpBarView view = root.GetComponent<EnemyHpBarView>();
            if (view == null)
                throw new InvalidOperationException("Missing EnemyHpBarView: " + oldPath);

            Transform statusStrip = root.transform.Find("ElementalStatusStrip");
            if (statusStrip == null || root.GetComponent<ElementalStatusIconStrip>() == null)
                throw new InvalidOperationException("Missing elemental status strip: " + oldPath);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child != statusStrip)
                    Object.DestroyImmediate(child.gameObject);
            }

            Image oldBackground = root.GetComponent<Image>();
            if (oldBackground != null)
            {
                oldBackground.enabled = false;
                oldBackground.raycastTarget = false;
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(concept, root.transform);
            if (visual == null)
                throw new InvalidOperationException("Could not instantiate concept: " + conceptPath);
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "TierNameplate";
            visual.transform.SetAsFirstSibling();
            EnemyHpBarConceptPreview preview = visual.GetComponent<EnemyHpBarConceptPreview>();
            if (preview == null || preview.CurrentFill == null || preview.DamageTrail == null)
                throw new InvalidOperationException("Concept references incomplete: " + conceptPath);
            Image current = preview.CurrentFill;
            Image trail = preview.DamageTrail;
            Object.DestroyImmediate(preview);

            CanvasGroup visualGroup = visual.GetComponent<CanvasGroup>();
            visualGroup.alpha = 1f;
            visualGroup.interactable = false;
            visualGroup.blocksRaycasts = false;
            RectTransform visualRect = (RectTransform)visual.transform;
            visualRect.anchorMin = visualRect.anchorMax = visualRect.pivot = new Vector2(0.5f, 0.5f);
            visualRect.anchoredPosition = Vector2.zero;
            visualRect.localScale = Vector3.one;
            ((RectTransform)root.transform).sizeDelta = visualRect.sizeDelta;
            ((RectTransform)statusStrip).anchoredPosition = new Vector2(0f, -visualRect.sizeDelta.y * 0.5f - 8f);
            SetUiLayer(root.transform);

            SerializedObject serialized = new SerializedObject(view);
            serialized.FindProperty("rpgView").objectReferenceValue = visual.GetComponentInChildren<OverburstEnemyHealthBarView>(true);
            serialized.FindProperty("barRoot").objectReferenceValue = root.GetComponent<RectTransform>();
            serialized.FindProperty("backgroundImage").objectReferenceValue = oldBackground;
            serialized.FindProperty("fillImage").objectReferenceValue = null;
            serialized.FindProperty("segmentLineRoot").objectReferenceValue = null;
            serialized.FindProperty("segmentLinePrefab").objectReferenceValue = null;
            serialized.FindProperty("visualCanvasGroup").objectReferenceValue = visualGroup;
            serialized.FindProperty("currentHealthImage").objectReferenceValue = current;
            serialized.FindProperty("damageTrailImage").objectReferenceValue = trail;
            Transform name = visual.transform.Find("Name");
            serialized.FindProperty("compactNameText").objectReferenceValue = name != null ? name.GetComponent<TextMeshProUGUI>() : null;
            serialized.FindProperty("hideUntilDamaged").boolValue = tier != "Elite";
            serialized.FindProperty("visibleSeconds").floatValue = 1.35f;
            serialized.FindProperty("fadeSeconds").floatValue = 0.22f;
            serialized.FindProperty("trailHoldSeconds").floatValue = 0.09f;
            serialized.FindProperty("trailCatchupSeconds").floatValue = 0.42f;
            serialized.FindProperty("showSegments").boolValue = false;
            serialized.FindProperty("useRoundedStyle").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, outputPath, out bool success);
            if (!success)
                throw new InvalidOperationException("Could not save live prefab: " + outputPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ApplyPersistentScene()
    {
        Scene scene = EditorSceneManager.OpenScene(PersistentScene, OpenSceneMode.Single);
        WorldUiOverlayService service = Object.FindFirstObjectByType<WorldUiOverlayService>(FindObjectsInactive.Include);
        if (service == null)
            throw new InvalidOperationException("PersistentScene has no WorldUiOverlayService.");

        SerializedObject serialized = new SerializedObject(service);
        // 2026-10-01: 이미 게임 HP 바가 연결돼 있으면 미리 만들기 수(prewarm) 조정값을 되돌리지 않는다.
        if (serialized.FindProperty("normalHealthBarPrefab").objectReferenceValue == LoadLive("Small")
            && serialized.FindProperty("mediumHealthBarPrefab").objectReferenceValue == LoadLive("Medium")
            && serialized.FindProperty("eliteHealthBarPrefab").objectReferenceValue == LoadLive("Elite"))
            return;
        serialized.FindProperty("normalHealthBarPrefab").objectReferenceValue = LoadLive("Small");
        serialized.FindProperty("mediumHealthBarPrefab").objectReferenceValue = LoadLive("Medium");
        serialized.FindProperty("eliteHealthBarPrefab").objectReferenceValue = LoadLive("Elite");
        serialized.FindProperty("normalHealthBarPrewarm").intValue = 16;
        serialized.FindProperty("mediumHealthBarPrewarm").intValue = 8;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void ApplyManagementScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ManagementScene, OpenSceneMode.Additive);
        Transform group = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            group = FindDescendant(root.transform, "30 Monster Health Preview");
            if (group != null) break;
        }
        if (group == null)
            throw new InvalidOperationException("UI management scene has no Monster Health Preview group.");

        for (int i = group.childCount - 1; i >= 0; i--)
        {
            Transform child = group.GetChild(i);
            if (child.name.StartsWith("PF_OverburstEnemyHealthBar_Rpg11", StringComparison.Ordinal)
                || child.name == "HP Tier Gallery")
                Object.DestroyImmediate(child.gameObject);
        }

        GameObject galleryObject = new GameObject("HP Tier Gallery", typeof(RectTransform));
        galleryObject.layer = 5;
        galleryObject.transform.SetParent(group, false);
        RectTransform gallery = (RectTransform)galleryObject.transform;
        gallery.anchorMin = Vector2.zero;
        gallery.anchorMax = Vector2.one;
        gallery.offsetMin = gallery.offsetMax = Vector2.zero;

        GameObject backdropObject = new GameObject("Gallery Backdrop", typeof(RectTransform), typeof(Image));
        backdropObject.layer = 5;
        backdropObject.transform.SetParent(gallery, false);
        RectTransform backdrop = (RectTransform)backdropObject.transform;
        backdrop.anchorMin = Vector2.zero;
        backdrop.anchorMax = Vector2.one;
        backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
        Image backdropImage = backdropObject.GetComponent<Image>();
        backdropImage.color = new Color(0.085f, 0.065f, 0.055f, 1f);
        backdropImage.raycastTarget = false;

        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null)
            throw new InvalidOperationException("Project Korean font is missing.");
        Label(gallery, font, "Title", "몬스터 체력바 · 게임 연결", new Vector2(0f, 410f), 36,
            new Color(0.94f, 0.87f, 0.76f), new Vector2(950f, 56f));
        Label(gallery, font, "Subtitle", "피격 직후 64% · 빨강은 현재 체력, 주황은 방금 잃은 체력", new Vector2(0f, 358f), 19,
            new Color(0.68f, 0.59f, 0.48f), new Vector2(1150f, 36f));

        string[] tiers = { "Small", "Medium", "Elite" };
        string[] captions = { "소형", "중형", "정예" };
        string[] details = { "74px · 피격 시 표시", "138px · 피격 시 표시", "전체 프레임 · 상시 표시" };
        for (int i = 0; i < 3; i++)
        {
            float x = -550f + i * 550f;
            Panel(gallery, new Vector2(x, 0f), new Vector2(510f, 570f));
            Label(gallery, font, "Tier " + tiers[i], captions[i], new Vector2(x, 232f), 26,
                new Color(0.94f, 0.87f, 0.76f), new Vector2(400f, 40f));
            Label(gallery, font, "Rule " + tiers[i], details[i], new Vector2(x, 194f), 16,
                new Color(0.68f, 0.59f, 0.48f), new Vector2(460f, 30f));
            AddSpecimen(gallery, tiers[i], new Vector2(x, 72f), 2f, "Large");
            Label(gallery, font, "Actual " + tiers[i], "실제 UI 크기 1×", new Vector2(x, -81f), 15,
                new Color(0.68f, 0.59f, 0.48f), new Vector2(350f, 28f));
            AddSpecimen(gallery, tiers[i], new Vector2(x, -150f), 1f, "Actual");
        }
        Label(gallery, font, "Footer", "게임은 실제 체력과 이름을 표시하며, 소형·중형은 평소 숨었다가 피격 후 사라집니다.",
            new Vector2(0f, -366f), 17, new Color(0.68f, 0.59f, 0.48f), new Vector2(1300f, 34f));
        backdrop.SetAsFirstSibling();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorSceneManager.CloseScene(scene, true);
    }

    private static EnemyHpBarView LoadLive(string tier)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LivePath(tier));
        EnemyHpBarView view = prefab != null ? prefab.GetComponent<EnemyHpBarView>() : null;
        if (view == null)
            throw new InvalidOperationException("Missing live HP bar: " + tier);
        return view;
    }

    private static string LivePath(string tier) =>
        LiveFolder + "/PF_EnemyHpBar_" + (tier == "Elite" ? "Elite_Tier" : tier) + ".prefab";

    private static void AddSpecimen(Transform parent, string tier, Vector2 position, float scale, string label)
    {
        EnemyHpBarView source = LoadLive(tier);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject, parent);
        instance.name = "HP Sample " + tier + " " + label;
        RectTransform rect = (RectTransform)instance.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one * scale;
        EnemyHpBarView view = instance.GetComponent<EnemyHpBarView>();
        view.enabled = false;
        CanvasGroup rootGroup = instance.GetComponent<CanvasGroup>();
        if (rootGroup != null)
            rootGroup.alpha = 1f;
        SerializedObject serialized = new SerializedObject(view);
        Image current = serialized.FindProperty("currentHealthImage").objectReferenceValue as Image;
        Image trail = serialized.FindProperty("damageTrailImage").objectReferenceValue as Image;
        CanvasGroup visual = serialized.FindProperty("visualCanvasGroup").objectReferenceValue as CanvasGroup;
        TextMeshProUGUI name = serialized.FindProperty("compactNameText").objectReferenceValue as TextMeshProUGUI;
        if (current == null || trail == null || visual == null)
            throw new InvalidOperationException("Live sample references incomplete: " + tier);
        current.fillAmount = 0.64f;
        trail.fillAmount = 1f;
        visual.alpha = 1f;
        if (name != null) name.text = "암굴 추적자";
        OverburstEnemyHealthBarView rpg = instance.GetComponentInChildren<OverburstEnemyHealthBarView>(true);
        if (rpg != null) rpg.PresentTarget("암굴 변이 정예", "Elite", 0.64f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
        PrefabUtility.RecordPrefabInstancePropertyModifications(view);
        if (rootGroup != null) PrefabUtility.RecordPrefabInstancePropertyModifications(rootGroup);
        PrefabUtility.RecordPrefabInstancePropertyModifications(current);
        PrefabUtility.RecordPrefabInstancePropertyModifications(trail);
        PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
        if (name != null) PrefabUtility.RecordPrefabInstancePropertyModifications(name);
        foreach (Text text in instance.GetComponentsInChildren<Text>(true))
            PrefabUtility.RecordPrefabInstancePropertyModifications(text);
    }

    private static Transform FindDescendant(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindDescendant(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void SetUiLayer(Transform root)
    {
        root.gameObject.layer = 5;
        foreach (Transform child in root) SetUiLayer(child);
    }

    private static void Panel(Transform parent, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject("Tier Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Image image = go.GetComponent<Image>();
        image.color = new Color(0.15f, 0.115f, 0.105f, 0.92f);
        image.raycastTarget = false;
        go.transform.SetAsFirstSibling();
    }

    private static void Label(Transform parent, Font font, string objectName, string value,
        Vector2 position, int size, Color color, Vector2 bounds)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = bounds;
        rect.anchoredPosition = position;
        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.text = value;
    }
}
