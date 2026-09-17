using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed class SlotVefectsRuntimeControls
{
    private const string RootName = "SlotVefectsRuntimeControlPanel";
    private const float PanelWidth = 304f;
    private const float PanelHeight = 268f;
    private const float RowHeight = 26f;
    private const float RowSpacing = 5f;
    private const float LabelWidth = 68f;
    private const float ButtonHeight = 24f;

    private static bool warnedMissingEventSystem;

    private readonly GameObject root;
    private readonly SlotVefectsTopOverlayRuntime runtime;
    private TextMeshProUGUI activeButtonLabel;
    private TextMeshProUGUI motionButtonLabel;
    private TextMeshProUGUI thicknessValueLabel;
    private TextMeshProUGUI offsetValueLabel;
    private TextMeshProUGUI countButtonLabel;
    private TextMeshProUGUI tailValueLabel;
    private TextMeshProUGUI speedValueLabel;
    private TextMeshProUGUI vefectsButtonLabel;

    private SlotVefectsRuntimeControls(GameObject root, SlotVefectsTopOverlayRuntime runtime)
    {
        this.root = root;
        this.runtime = runtime;
    }

    public bool IsValid
    {
        get { return root != null; }
    }

    public static bool IsAllowed()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    public static SlotVefectsRuntimeControls CreateOrReuse(RectTransform parent, SlotVefectsTopOverlayRuntime runtime)
    {
        if (parent == null || runtime == null)
            return null;

        Transform existing = parent.Find(RootName);
        if (existing != null)
            Object.Destroy(existing.gameObject);

        GameObject rootObject = new GameObject(RootName, typeof(RectTransform), typeof(Image));
        rootObject.transform.SetParent(parent, false);

        RectTransform rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        rootRect.anchoredPosition = new Vector2(18f, -54f);

        Image background = rootObject.GetComponent<Image>();
        background.color = new Color(0.035f, 0.045f, 0.052f, 0.82f);
        background.raycastTarget = true;

        SlotVefectsFontReference fontReference = ResolveFontReference(parent);
        SlotVefectsRuntimeControls controls = new SlotVefectsRuntimeControls(rootObject, runtime);
        controls.BuildRows(rootRect);
        ApplyFont(rootRect, fontReference);
        controls.RefreshVisual();
        controls.BringToFront();
        if (rootObject.activeInHierarchy)
            WarnIfMissingEventSystem();
        return controls;
    }

    public void BringToFront()
    {
        if (root != null)
            root.transform.SetAsLastSibling();
    }

    public void RefreshVisual()
    {
        if (runtime == null)
            return;

        if (activeButtonLabel != null)
            activeButtonLabel.text = runtime.RuntimeOverlayVisible ? "VFX ON" : "VFX OFF";

        if (motionButtonLabel != null)
            motionButtonLabel.text = "모션 " + runtime.RuntimeMotionKindLabel;

        if (thicknessValueLabel != null)
            thicknessValueLabel.text = runtime.RuntimeThickness.ToString("0.00");

        if (offsetValueLabel != null)
            offsetValueLabel.text = runtime.RuntimeOverlayOffset.ToString("+0;-0;0") + "px";

        if (countButtonLabel != null)
            countButtonLabel.text = "개수 + " + runtime.RuntimeRunnerCount + "/4";

        if (tailValueLabel != null)
            tailValueLabel.text = runtime.RuntimeTailLevel.ToString("0") + "/" + SlotVefectsTopOverlayRuntime.TailLevelMax.ToString("0");

        if (speedValueLabel != null)
            speedValueLabel.text = runtime.RuntimeFramesPerSecond.ToString("0") + "fps";

        if (vefectsButtonLabel != null)
            vefectsButtonLabel.text = runtime.RuntimeVefectsKindAvailable
                ? "VFX " + runtime.RuntimeVefectsKindLabel
                : "VFX " + runtime.RuntimeVefectsKindLabel + " 없음";

    }

    private void BuildRows(RectTransform rootRect)
    {
        float y = 10f;
        CreateLabel(rootRect, "ActiveLabel", 10f, y, LabelWidth, ButtonHeight, "1 표시");
        activeButtonLabel = CreateButton(rootRect, "ActiveButton", 86f, y, 190f, ButtonHeight, "VFX ON", runtime.ToggleRuntimeOverlayVisible);

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "MotionLabel", 10f, y, LabelWidth, ButtonHeight, "2 모션");
        motionButtonLabel = CreateButton(rootRect, "MotionButton", 86f, y, 190f, ButtonHeight, "모션", runtime.CycleRuntimeMotionKind);

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "ThicknessLabel", 10f, y, LabelWidth, ButtonHeight, "3 두께");
        CreateButton(rootRect, "ThicknessDownButton", 86f, y, 34f, ButtonHeight, "-", () => runtime.AdjustRuntimeThickness(-SlotVefectsTopOverlayRuntime.ThicknessStep));
        thicknessValueLabel = CreateValue(rootRect, "ThicknessValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "ThicknessUpButton", 224f, y, 34f, ButtonHeight, "+", () => runtime.AdjustRuntimeThickness(SlotVefectsTopOverlayRuntime.ThicknessStep));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "OffsetLabel", 10f, y, LabelWidth, ButtonHeight, "4 오프셋");
        CreateButton(rootRect, "OffsetDownButton", 86f, y, 34f, ButtonHeight, "-", () => runtime.AdjustRuntimeOverlayOffset(-SlotVefectsTopOverlayRuntime.OverlayOffsetStep));
        offsetValueLabel = CreateValue(rootRect, "OffsetValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "OffsetUpButton", 224f, y, 34f, ButtonHeight, "+", () => runtime.AdjustRuntimeOverlayOffset(SlotVefectsTopOverlayRuntime.OverlayOffsetStep));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "CountLabel", 10f, y, LabelWidth, ButtonHeight, "5 수량");
        countButtonLabel = CreateButton(rootRect, "CountButton", 86f, y, 190f, ButtonHeight, "개수 +", runtime.IncrementRuntimeRunnerCount);

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "TailLabel", 10f, y, LabelWidth, ButtonHeight, "6 꼬리");
        CreateButton(rootRect, "TailDownButton", 86f, y, 34f, ButtonHeight, "-", () => runtime.AdjustRuntimeTailLevel(-1));
        tailValueLabel = CreateValue(rootRect, "TailValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "TailUpButton", 224f, y, 34f, ButtonHeight, "+", () => runtime.AdjustRuntimeTailLevel(1));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "SpeedLabel", 10f, y, LabelWidth, ButtonHeight, "7 속도");
        CreateButton(rootRect, "SpeedDownButton", 86f, y, 34f, ButtonHeight, "-", () => runtime.AdjustRuntimeFramesPerSecond(-SlotVefectsTopOverlayRuntime.FramesPerSecondStep));
        speedValueLabel = CreateValue(rootRect, "SpeedValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "SpeedUpButton", 224f, y, 34f, ButtonHeight, "+", () => runtime.AdjustRuntimeFramesPerSecond(SlotVefectsTopOverlayRuntime.FramesPerSecondStep));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "VefectsLabel", 10f, y, LabelWidth, ButtonHeight, "8 VFX");
        vefectsButtonLabel = CreateButton(rootRect, "VefectsButton", 86f, y, 190f, ButtonHeight, "VFX", runtime.CycleRuntimeVefectsKind);
    }

    private static TextMeshProUGUI CreateLabel(RectTransform parent, string name, float x, float y, float width, float height, string text)
    {
        TextMeshProUGUI label = CreateText(parent, name, x, y, width, height, text);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontSize = 12f;
        label.color = new Color(0.78f, 0.88f, 0.92f, 1f);
        return label;
    }

    private static TextMeshProUGUI CreateValue(RectTransform parent, string name, float x, float y, float width, float height)
    {
        TextMeshProUGUI label = CreateText(parent, name, x, y, width, height, string.Empty);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 12.5f;
        label.color = new Color(0.95f, 0.98f, 1f, 1f);
        return label;
    }

    private static TextMeshProUGUI CreateButton(RectTransform parent, string name, float x, float y, float width, float height, string text, UnityAction action)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        ConfigureRect(buttonObject.GetComponent<RectTransform>(), x, y, width, height);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.16f, 0.19f, 0.96f);
        image.raycastTarget = true;

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
        ConfigureButtonColors(button);

        TextMeshProUGUI label = CreateText(buttonObject.transform as RectTransform, "Label", 0f, 0f, width, height, text);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = width <= 40f ? 15f : 12.5f;
        label.color = new Color(0.93f, 0.98f, 1f, 1f);
        return label;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, float x, float y, float width, float height, string text)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        ConfigureRect(textObject.GetComponent<RectTransform>(), x, y, width, height);

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private static void ConfigureRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    private static void ConfigureButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.08f, 0.16f, 0.19f, 0.96f);
        colors.highlightedColor = new Color(0.13f, 0.25f, 0.29f, 1f);
        colors.pressedColor = new Color(0.04f, 0.34f, 0.36f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private static SlotVefectsFontReference ResolveFontReference(RectTransform parent)
    {
        SlotVefectsFontReference reference = FindFontReferenceInScene(parent != null ? parent.root : null);
        if (reference.IsValid)
            return reference;

        return FindFontReferenceInScene(null);
    }

    private static SlotVefectsFontReference FindFontReferenceInScene(Transform root)
    {
        TextMeshProUGUI[] labels = root != null
            ? root.GetComponentsInChildren<TextMeshProUGUI>(true)
            : Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI source = labels[i];
            if (source == null || source.font == null)
                continue;

            if (!SupportsKorean(source.font))
                continue;

            return new SlotVefectsFontReference(source.font, source.fontSharedMaterial);
        }

        return default;
    }

    private static bool SupportsKorean(TMP_FontAsset font)
    {
        if (font == null)
            return false;

        return font.HasCharacter('한') || font.name.Contains("Noto") || font.name.Contains("KR");
    }

    private static void ApplyFont(RectTransform root, SlotVefectsFontReference fontReference)
    {
        if (root == null || !fontReference.IsValid)
            return;

        TextMeshProUGUI[] labels = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI target = labels[i];
            if (target == null)
                continue;

            target.font = fontReference.Font;
            if (fontReference.Material != null)
                target.fontSharedMaterial = fontReference.Material;
        }
    }

    private static void WarnIfMissingEventSystem()
    {
        if (warnedMissingEventSystem || EventSystem.current != null)
            return;

        warnedMissingEventSystem = true;
        Debug.LogWarning("[SlotVefectsControls] EventSystem이 없어 슬롯 VFX 런타임 버튼 클릭 입력을 받을 수 없습니다.");
    }

    private readonly struct SlotVefectsFontReference
    {
        public readonly TMP_FontAsset Font;
        public readonly Material Material;

        public SlotVefectsFontReference(TMP_FontAsset font, Material material)
        {
            Font = font;
            Material = material;
        }

        public bool IsValid
        {
            get { return Font != null; }
        }
    }
}
