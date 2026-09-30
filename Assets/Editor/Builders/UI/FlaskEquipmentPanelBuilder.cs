using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FlaskEquipmentPanelBuilder
{
    private const string ScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const string FontPath = "Assets/ProjectOverburst/Resources/UI/Tooltip/FlaskTooltipFont.asset";
    private static readonly Color Ink = new Color(.08f, .095f, .12f, .96f);
    private static readonly Color Edge = new Color(.54f, .58f, .64f, .65f);
    private static readonly Color Text = new Color(.9f, .92f, .95f);
    private static TMP_FontAsset font;

    [MenuItem("JC Tool/UI/Build Flask Equipment Panel")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Edit mode required");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) throw new System.InvalidOperationException("Build flask font first");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RectTransform top = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                if (rect.name == "TopPanel" && rect.parent != null && rect.parent.name == "InventoryPanel")
                    top = rect;
        if (top == null) throw new System.InvalidOperationException("Inventory TopPanel missing");
        Transform previous = top.Find("EquipmentSlotsPanel");
        if (previous != null) Object.DestroyImmediate(previous.gameObject);

        RectTransform panel = Rect("EquipmentSlotsPanel", top, Vector2.zero, new Vector2(600f, 370f));
        AddText("EquipmentHeading", panel, "방어구 · 장신구", new Vector2(40f, 118f),
            new Vector2(310f, 26f), 17f, TextAlignmentOptions.Left, new Color(.93f, .79f, .53f));
        string[] gear = { "투구", "갑옷", "장갑", "신발", "귀걸이 1", "귀걸이 2", "목걸이" };
        Vector2[] positions =
        {
            new Vector2(-65f, 60f), new Vector2(20f, 60f), new Vector2(105f, 60f), new Vector2(190f, 60f),
            new Vector2(-20f, -20f), new Vector2(75f, -20f), new Vector2(170f, -20f)
        };
        for (int i = 0; i < gear.Length; i++)
        {
            RectTransform card = Card("GearSlot_" + (i + 1).ToString("00"), panel, positions[i], new Vector2(76f, 70f));
            AddText("Icon", card, "•", new Vector2(0f, 8f), new Vector2(70f, 25f), 21f,
                TextAlignmentOptions.Center, new Color(.45f, .53f, .62f));
            AddText("Label", card, gear[i], new Vector2(0f, -19f), new Vector2(74f, 20f), 12.5f,
                TextAlignmentOptions.Center, Text);
        }

        AddText("FlaskHeading", panel, "장착 물약 · 최대 3개", new Vector2(10f, -68f),
            new Vector2(220f, 24f), 16f, TextAlignmentOptions.Left, new Color(.93f, .79f, .53f));
        var buttons = new Button[PlayerFlaskController.SlotCount];
        var icons = new Image[PlayerFlaskController.SlotCount];
        var names = new TMP_Text[PlayerFlaskController.SlotCount];
        var keys = new TMP_Text[PlayerFlaskController.SlotCount];
        for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
        {
            RectTransform card = Card("FlaskEquipSlot_" + (i + 1).ToString("00"), panel,
                new Vector2(-40f + i * 123f, -121f), new Vector2(115f, 55f));
            buttons[i] = card.gameObject.AddComponent<Button>();
            buttons[i].targetGraphic = card.GetComponent<Image>();
            RectTransform icon = Rect("Icon", card, new Vector2(-39f, 0f), new Vector2(31f, 31f));
            icons[i] = icon.gameObject.AddComponent<Image>();
            icons[i].raycastTarget = false;
            icons[i].enabled = false;
            names[i] = AddText("Name", card, "물약 " + (i + 1), new Vector2(15f, 10f),
                new Vector2(74f, 21f), 11.5f, TextAlignmentOptions.Left, Text);
            keys[i] = AddText("QuickKey", card, "빈 장착칸", new Vector2(15f, -11f),
                new Vector2(74f, 19f), 10.5f, TextAlignmentOptions.Left, new Color(.58f, .7f, .78f));
        }

        RectTransform picker = Card("FlaskKeyPicker", panel, new Vector2(0f, -20f), new Vector2(328f, 90f));
        AddText("PickerTitle", picker, "퀵슬롯 번호 선택", new Vector2(-34f, 28f),
            new Vector2(240f, 24f), 15f, TextAlignmentOptions.Center, Text);
        var keyButtons = new Button[InventoryQuickSlotBindingController.SlotCount];
        var keyLabels = new TMP_Text[InventoryQuickSlotBindingController.SlotCount];
        for (int i = 0; i < keyButtons.Length; i++)
        {
            RectTransform card = Card("Key_" + (i + 1), picker, new Vector2(-129f + i * 43f, -13f), new Vector2(38f, 36f));
            keyButtons[i] = card.gameObject.AddComponent<Button>();
            keyButtons[i].targetGraphic = card.GetComponent<Image>();
            keyLabels[i] = AddText("Label", card, (i + 1) + "번", Vector2.zero, new Vector2(36f, 30f),
                13f, TextAlignmentOptions.Center, Text);
        }
        RectTransform close = Card("Close", picker, new Vector2(145f, 28f), new Vector2(24f, 23f));
        Button closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = close.GetComponent<Image>();
        AddText("Label", close, "X", Vector2.zero, new Vector2(22f, 22f),
            15f, TextAlignmentOptions.Center, Text);
        picker.gameObject.SetActive(false);

        FlaskEquipmentPanelUI ui = panel.gameObject.AddComponent<FlaskEquipmentPanelUI>();
        ui.Configure(buttons, icons, names, keys, picker.gameObject, keyButtons, keyLabels, closeButton);
        EditorUtility.SetDirty(ui);
        RectTransform sort = top.parent.Find("BottomPanel/InventorySortControls") as RectTransform;
        if (sort != null) sort.anchoredPosition = new Vector2(sort.anchoredPosition.x, 20f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FlaskEquipmentPanelBuilder] 3 flask slots + 7 temporary gear slots built");
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static RectTransform Card(string name, Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform rect = Rect(name, parent, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = Ink;
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = Edge;
        outline.effectDistance = new Vector2(1f, -1f);
        return rect;
    }

    private static TMP_Text AddText(string name, Transform parent, string value, Vector2 position,
        Vector2 size, float fontSize, TextAlignmentOptions alignment, Color color)
    {
        RectTransform rect = Rect(name, parent, position, size);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.text = value;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }
}
