using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class UiQualityTool // UI 품질 유틸
{
    private const string PersistentScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity"; // 대상 씬
    private const string KoreanFontPath = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-VariableFont_wght SDF.asset"; // 한글 폰트

    [MenuItem("OVERBURST/Codex/Objectizers/UI/[위험] Apply UI Quality Fix 1st Pass")]
    public static void ApplyUiQualityFixFirstPass()
    {
        if (!ConfirmPersistentSceneQualityFix("UI Quality Fix 1st Pass"))
            return;

        ApplyUiQualityFixFirstPassInternal();
    }

    public static void RunOnceFromCommandLine()
    {
        ApplyUiQualityFixFirstPassInternal();
    }

    [MenuItem("OVERBURST/Codex/Objectizers/UI/[위험] Validate And Fix Canvas Scale")]
    public static void ValidateAndFixCanvasScale()
    {
        if (!ConfirmPersistentSceneQualityFix("Validate And Fix Canvas Scale"))
            return;

        Scene scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
        ValidateAndFixCanvasScaleInternal(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("OVERBURST/Codex/Objectizers/UI/[위험] Create Minimap Zoom Size Text")]
    public static void CreateMinimapZoomSizeText()
    {
        if (!ConfirmPersistentSceneQualityFix("Create Minimap Zoom Size Text"))
            return;

        Scene scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
        CreateMinimapZoomSizeTextInternal(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("OVERBURST/Codex/Objectizers/UI/[위험] Fix Sort Dropdown Hierarchy")]
    public static void FixSortDropdownHierarchy()
    {
        if (!ConfirmPersistentSceneQualityFix("Fix Sort Dropdown Hierarchy"))
            return;

        Scene scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
        FixSortDropdownHierarchyInternal(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectVTP] Sort dropdown hierarchy fix completed. PersistentScene saved.");
    }

    private static void ApplyUiQualityFixFirstPassInternal()
    {
        Scene scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single); // 씬 열기
        ValidateAndFixCanvasScaleInternal(scene);
        CreateMinimapZoomSizeTextInternal(scene);
        FixSortDropdownHierarchyInternal(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectVTP] UI quality fix 1st pass completed. PersistentScene saved.");
    }

    private static bool ConfirmPersistentSceneQualityFix(string targetName)
    {
        return ProjectVtpEditorMenuGuard.ConfirmDangerousAction(
            targetName,
            "PersistentScene의 UI 품질 / 계층 / 텍스트 오브젝트를 갱신하고 PersistentScene을 저장합니다.");
    }

    private static void ValidateAndFixCanvasScaleInternal(Scene scene)
    {
        FixCanvasScale(scene, "HUDCanvas");
        FixCanvasScale(scene, "PlayerInventoryCanvas");
    }

    private static void FixCanvasScale(Scene scene, string objectName)
    {
        GameObject target = FindInScene(scene, objectName); // Canvas 후보
        if (target == null)
        {
            Debug.LogWarning($"[ProjectVTP] {objectName} was not found in PersistentScene while checking canvas scale.");
            return;
        }

        Transform targetTransform = target.transform; // Scale 대상
        string path = GetHierarchyPath(targetTransform); // 로그 경로
        Debug.Log($"[ProjectVTP] {path} localScale = {targetTransform.localScale}");

        if (IsNormalScale(targetTransform.localScale))
        {
            Debug.Log($"[ProjectVTP] {path} localScale is already 1,1,1.");
            return;
        }

        Debug.LogWarning($"[ProjectVTP] {path} localScale was {targetTransform.localScale}. Restoring to 1,1,1.");
        Undo.RecordObject(targetTransform, $"Fix {objectName} Scale");
        targetTransform.localScale = Vector3.one;
        EditorUtility.SetDirty(targetTransform);
    }

    private static bool IsNormalScale(Vector3 scale)
    {
        return Mathf.Approximately(scale.x, 1f)
            && Mathf.Approximately(scale.y, 1f)
            && Mathf.Approximately(scale.z, 1f);
    }

    private static void CreateMinimapZoomSizeTextInternal(Scene scene)
    {
        GameObject rootObject = FindInScene(scene, "WorldMinimapRoot"); // 미니맵 root
        if (rootObject == null)
        {
            Debug.LogWarning(
                "[ProjectVTP] WorldMinimapRoot was not found "
                + "in PersistentScene.");
            return;
        }

        RectTransform root = rootObject.transform as RectTransform;
        if (root == null)
        {
            Debug.LogWarning(
                "[ProjectVTP] WorldMinimapRoot does not have a RectTransform.");
            return;
        }

        TMP_FontAsset koreanFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath); // 한글 폰트
        RectTransform textRect = EnsureRectChild(root, "MinimapZoomSizeText"); // 줌 텍스트
        textRect.anchorMin = new Vector2(1f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = new Vector2(-30f, -102f);
        textRect.sizeDelta = new Vector2(72f, 20f);

        TextMeshProUGUI zoomText = EnsureComponent<TextMeshProUGUI>(textRect.gameObject);
        Undo.RecordObject(zoomText, "Configure Minimap Zoom Size Text");
        zoomText.text = "x1.0";
        zoomText.fontSize = 12f;
        zoomText.alignment = TextAlignmentOptions.Center;
        zoomText.color = new Color(0.84f, 0.96f, 0.92f, 0.92f);
        zoomText.raycastTarget = false;
        zoomText.textWrappingMode = TextWrappingModes.NoWrap;
        if (koreanFont != null)
        {
            zoomText.font = koreanFont;
            zoomText.fontSharedMaterial = koreanFont.material;
        }

        RestoreZoomButtonLabel(root, "MinimapZoomInButton", "+");
        RestoreZoomButtonLabel(root, "MinimapZoomOutButton", "-");

        WorldMinimapController controller =
            rootObject.GetComponent<WorldMinimapController>(); // 미니맵 컨트롤러
        if (controller == null)
        {
            Debug.LogWarning(
                "[ProjectVTP] WorldMinimapController was not found "
                + "on WorldMinimapRoot.");
            return;
        }

        SerializedObject serialized = new SerializedObject(controller); // serialized 연결
        SerializedProperty zoomValueText = serialized.FindProperty("zoomValueText"); // 줌 값 참조
        if (zoomValueText != null)
            zoomValueText.objectReferenceValue = zoomText;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(zoomText);
        EditorUtility.SetDirty(textRect);
        Debug.Log(
            "[ProjectVTP] MinimapZoomSizeText exists and is linked "
            + "to WorldMinimapController.zoomValueText.");
    }

    private static void FixSortDropdownHierarchyInternal(Scene scene)
    {
        TMP_FontAsset koreanFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath); // 한글 폰트
        InventoryUI inventoryUI = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        StashUI stashUI = Object.FindFirstObjectByType<StashUI>(FindObjectsInactive.Include);

        RectTransform inventoryPanel = FindInScene(scene, "InventoryPanel")?.transform as RectTransform;
        RectTransform stashPanel = FindInScene(scene, "StashPanel")?.transform as RectTransform;
        if (inventoryPanel == null)
            Debug.LogWarning("[ProjectVTP] InventoryPanel was not found while fixing sort dropdown hierarchy.");
        if (stashPanel == null)
            Debug.LogWarning("[ProjectVTP] StashPanel was not found while fixing sort dropdown hierarchy.");

        TMP_Dropdown inventoryDropdown = null;
        Button inventoryRefreshButton = null;
        TextMeshProUGUI inventoryRefreshText = null;
        TextMeshProUGUI inventoryStatusText = null;

        if (inventoryPanel != null)
        {
            RectTransform controls = MoveOrCreateRect(scene, "InventorySortControls", inventoryPanel); // 인벤 정렬 root
            controls.anchorMin = new Vector2(1f, 1f);
            controls.anchorMax = new Vector2(1f, 1f);
            controls.pivot = new Vector2(1f, 1f);
            controls.anchoredPosition = new Vector2(-18f, -18f);
            controls.sizeDelta = new Vector2(210f, 50f);
            controls.localScale = Vector3.one;

            inventoryDropdown = ConfigureSortDropdown(MoveOrCreateRect(scene, "InventorySortDropdown", controls), new Vector2(0f, 0f), new Vector2(112f, 28f), koreanFont);
            inventoryRefreshButton = ConfigureButton(MoveOrCreateRect(scene, "InventorySortRefreshButton", controls), "리프레시", new Vector2(118f, 0f), new Vector2(78f, 28f), koreanFont);
            inventoryRefreshText = inventoryRefreshButton.GetComponentInChildren<TextMeshProUGUI>(true);
            inventoryStatusText = ConfigureText(MoveOrCreateRect(scene, "InventorySortStatusText", controls), string.Empty, 11f, TextAlignmentOptions.TopRight, koreanFont);
            inventoryStatusText.rectTransform.anchorMin = new Vector2(0f, 0f);
            inventoryStatusText.rectTransform.anchorMax = new Vector2(1f, 0f);
            inventoryStatusText.rectTransform.pivot = new Vector2(1f, 0f);
            inventoryStatusText.rectTransform.anchoredPosition = Vector2.zero;
            inventoryStatusText.rectTransform.sizeDelta = new Vector2(0f, 18f);
        }

        TMP_Dropdown stashDropdown = null;
        Button stashRefreshButton = null;
        TextMeshProUGUI stashRefreshText = null;
        Button storeAllButton = null;
        TextMeshProUGUI storeAllText = null;
        TextMeshProUGUI stashStatusText = null;
        Button[] tabButtons = new Button[3];
        TextMeshProUGUI[] tabTexts = new TextMeshProUGUI[3];

        if (stashPanel != null)
        {
            RectTransform toolbar = MoveOrCreateRect(scene, "StashToolbar", stashPanel); // 창고 툴바
            toolbar.anchorMin = new Vector2(0f, 1f);
            toolbar.anchorMax = new Vector2(1f, 1f);
            toolbar.pivot = new Vector2(0.5f, 1f);
            toolbar.anchoredPosition = new Vector2(0f, -14f);
            toolbar.sizeDelta = new Vector2(-36f, 36f);
            toolbar.localScale = Vector3.one;

            for (int i = 0; i < tabButtons.Length; i++)
            {
                tabButtons[i] = ConfigureButton(MoveOrCreateRect(scene, "StashTabButton_0" + (i + 1), toolbar), (i + 1).ToString(), new Vector2(i * 36f, 0f), new Vector2(30f, 28f), koreanFont);
                tabTexts[i] = tabButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            }

            stashDropdown = ConfigureSortDropdown(MoveOrCreateRect(scene, "StashSortDropdown", toolbar), new Vector2(124f, 0f), new Vector2(112f, 28f), koreanFont);
            stashRefreshButton = ConfigureButton(MoveOrCreateRect(scene, "StashSortRefreshButton", toolbar), "리프레시", new Vector2(242f, 0f), new Vector2(78f, 28f), koreanFont);
            stashRefreshText = stashRefreshButton.GetComponentInChildren<TextMeshProUGUI>(true);
            storeAllButton = ConfigureButton(MoveOrCreateRect(scene, "StashStoreAllButton", toolbar), "전체보관", new Vector2(326f, 0f), new Vector2(86f, 28f), koreanFont);
            storeAllText = storeAllButton.GetComponentInChildren<TextMeshProUGUI>(true);
            stashStatusText = ConfigureText(MoveOrCreateRect(scene, "StashStatusText", toolbar), string.Empty, 11f, TextAlignmentOptions.MidlineRight, koreanFont);
            stashStatusText.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            stashStatusText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            stashStatusText.rectTransform.pivot = new Vector2(1f, 0.5f);
            stashStatusText.rectTransform.anchoredPosition = Vector2.zero;
            stashStatusText.rectTransform.sizeDelta = new Vector2(240f, 32f);

            RectTransform slotGrid = MoveOrCreateRect(scene, "StashSlotGrid", stashPanel); // 슬롯 grid
            slotGrid.anchorMin = new Vector2(0f, 1f);
            slotGrid.anchorMax = new Vector2(0f, 1f);
            slotGrid.pivot = new Vector2(0f, 1f);
            slotGrid.anchoredPosition = new Vector2(20f, -58f);
            slotGrid.sizeDelta = new Vector2(448f, 643f);
            slotGrid.localScale = Vector3.one;
        }

        if (inventoryUI != null)
        {
            SerializedObject serialized = new SerializedObject(inventoryUI);
            SetObject(serialized, "koreanFontAsset", koreanFont);
            SetObject(serialized, "sortDropdown", inventoryDropdown);
            SetObject(serialized, "sortRefreshButton", inventoryRefreshButton);
            SetObject(serialized, "sortRefreshButtonText", inventoryRefreshText);
            SetObject(serialized, "sortStatusText", inventoryStatusText);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(inventoryUI);
        }

        if (stashUI != null)
        {
            SerializedObject serialized = new SerializedObject(stashUI);
            SetObject(serialized, "koreanFontAsset", koreanFont);
            SetObject(serialized, "sortDropdown", stashDropdown);
            SetObject(serialized, "sortRefreshButton", stashRefreshButton);
            SetObject(serialized, "sortRefreshButtonText", stashRefreshText);
            SetObject(serialized, "storeAllButton", storeAllButton);
            SetObject(serialized, "storeAllButtonText", storeAllText);
            SetObject(serialized, "actionStatusText", stashStatusText);
            SetButtonArray(serialized, "tabButtons", tabButtons);
            SetTextArray(serialized, "tabButtonTexts", tabTexts);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(stashUI);
        }

        Debug.Log("[ProjectVTP] Sort dropdown hierarchy fixed under InventoryPanel and StashPanel.");
    }

    private static TMP_Dropdown ConfigureSortDropdown(RectTransform rect, Vector2 position, Vector2 size, TMP_FontAsset font)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;

        Image background = EnsureComponent<Image>(rect.gameObject); // 드롭다운 배경
        background.color = new Color(0.13f, 0.16f, 0.19f, 0.96f);
        background.raycastTarget = true;

        TMP_Dropdown dropdown = EnsureComponent<TMP_Dropdown>(rect.gameObject); // 드롭다운
        TextMeshProUGUI caption = ConfigureText(EnsureRectChild(rect, "Label"), ItemSortComparer.GetDisplayName(ItemSortMode.Default), 13f, TextAlignmentOptions.MidlineLeft, font);
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = new Vector2(8f, 0f);
        caption.rectTransform.offsetMax = new Vector2(-22f, 0f);

        TextMeshProUGUI arrow = ConfigureText(EnsureRectChild(rect, "Arrow"), "▼", 13f, TextAlignmentOptions.Center, font);
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchoredPosition = new Vector2(-4f, 0f);
        arrow.rectTransform.sizeDelta = new Vector2(18f, 0f);

        RectTransform template = ConfigureDropdownTemplate(EnsureRectChild(rect, "Template"), size.x, font); // 옵션 템플릿
        Toggle itemToggle = template.GetComponentInChildren<Toggle>(true);
        TextMeshProUGUI itemText = itemToggle != null ? itemToggle.GetComponentInChildren<TextMeshProUGUI>(true) : null;

        dropdown.captionText = caption;
        dropdown.template = template;
        dropdown.itemText = itemText;
        dropdown.options = CreateSortOptions();
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
        EditorUtility.SetDirty(dropdown);
        return dropdown;
    }

    private static RectTransform ConfigureDropdownTemplate(RectTransform template, float width, TMP_FontAsset font)
    {
        template.gameObject.SetActive(false);
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(0f, 0f);
        template.pivot = new Vector2(0f, 1f);
        template.anchoredPosition = new Vector2(0f, -2f);
        template.sizeDelta = new Vector2(width, 112f);
        template.localScale = Vector3.one;

        Image image = EnsureComponent<Image>(template.gameObject);
        image.color = new Color(0.12f, 0.15f, 0.18f, 0.98f);
        image.raycastTarget = true;

        ScrollRect scrollRect = EnsureComponent<ScrollRect>(template.gameObject); // 옵션 스크롤
        RectTransform viewport = EnsureRectChild(template, "Viewport"); // 마스크 영역
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;

        Image viewportImage = EnsureComponent<Image>(viewport.gameObject);
        viewportImage.color = Color.white;
        viewportImage.raycastTarget = false;
        Mask mask = EnsureComponent<Mask>(viewport.gameObject);
        mask.showMaskGraphic = false;

        RectTransform content = EnsureRectChild(viewport, "Content"); // 옵션 root
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 112f);
        content.localScale = Vector3.one;

        Toggle item = EnsureComponent<Toggle>(EnsureRectChild(content, "Item").gameObject); // 옵션 item
        RectTransform itemRect = item.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0f, 1f);
        itemRect.anchorMax = new Vector2(1f, 1f);
        itemRect.pivot = new Vector2(0.5f, 1f);
        itemRect.anchoredPosition = Vector2.zero;
        itemRect.sizeDelta = new Vector2(0f, 28f);

        Image itemBackground = EnsureComponent<Image>(itemRect.gameObject);
        itemBackground.color = new Color(0.18f, 0.22f, 0.26f, 1f);
        itemBackground.raycastTarget = true;
        item.targetGraphic = itemBackground;

        TextMeshProUGUI label = ConfigureText(EnsureRectChild(itemRect, "Item Label"), ItemSortComparer.GetDisplayName(ItemSortMode.Default), 13f, TextAlignmentOptions.MidlineLeft, font);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(8f, 0f);
        label.rectTransform.offsetMax = new Vector2(-8f, 0f);

        scrollRect.content = content;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        return template;
    }

    private static Button ConfigureButton(RectTransform rect, string text, Vector2 position, Vector2 size, TMP_FontAsset font)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;

        Image image = EnsureComponent<Image>(rect.gameObject); // 버튼 배경
        image.color = new Color(0.13f, 0.16f, 0.19f, 0.96f);
        image.raycastTarget = true;

        Button button = EnsureComponent<Button>(rect.gameObject); // 버튼
        TextMeshProUGUI label = ConfigureText(EnsureRectChild(rect, "Text"), text, 13f, TextAlignmentOptions.Center, font);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        EditorUtility.SetDirty(button);
        return button;
    }

    private static TextMeshProUGUI ConfigureText(RectTransform rect, string text, float fontSize, TextAlignmentOptions alignment, TMP_FontAsset font)
    {
        TextMeshProUGUI label = EnsureComponent<TextMeshProUGUI>(rect.gameObject); // 텍스트 구성
        label.text = text;
        label.fontSize = fontSize;
        label.fontSizeMin = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.alpha = 1f;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.enabled = true;
        if (font != null)
        {
            label.font = font;
            label.fontSharedMaterial = font.material;
        }

        label.SetAllDirty();
        label.ForceMeshUpdate(true, true);
        EditorUtility.SetDirty(label);
        return label;
    }

    private static RectTransform MoveOrCreateRect(Scene scene, string objectName, RectTransform parent)
    {
        GameObject existing = FindInScene(scene, objectName);
        RectTransform rect = existing != null
            ? existing.transform as RectTransform
            : null;

        if (rect == null)
        {
            GameObject childObject = new GameObject(objectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(childObject, $"Create {objectName}");
            rect = childObject.transform as RectTransform;
        }

        if (rect.parent != parent)
        {
            Undo.SetTransformParent(rect, parent, $"Move {objectName}");
            rect.SetParent(parent, false);
        }

        return rect;
    }

    private static System.Collections.Generic.List<TMP_Dropdown.OptionData> CreateSortOptions()
    {
        System.Collections.Generic.List<TMP_Dropdown.OptionData> options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < ItemSortComparer.Modes.Length; i++)
            options.Add(new TMP_Dropdown.OptionData(ItemSortComparer.GetDisplayName(ItemSortComparer.Modes[i])));
        return options;
    }

    private static void SetButtonArray(SerializedObject serialized, string propertyName, Button[] values)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            return;

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetTextArray(SerializedObject serialized, string propertyName, TextMeshProUGUI[] values)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            return;

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetObject(SerializedObject serialized, string propertyName, Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static GameObject FindInScene(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objectName)
                return root;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == objectName)
                    return child.gameObject;
            }
        }

        return null;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static void RestoreZoomButtonLabel(RectTransform root, string buttonName, string label)
    {
        Transform button = root.Find(buttonName);
        if (button == null)
            return;

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null)
            return;

        Undo.RecordObject(text, "Restore Minimap Zoom Button Label");
        text.text = label;
        text.fontSize = 16f;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        EditorUtility.SetDirty(text);
    }

    private static RectTransform EnsureRectChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            GameObject childObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(childObject, $"Create {name}");
            childObject.transform.SetParent(parent, false);
            child = childObject.transform;
        }

        return child as RectTransform;
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
            component = Undo.AddComponent<T>(target);
        return component;
    }
}
