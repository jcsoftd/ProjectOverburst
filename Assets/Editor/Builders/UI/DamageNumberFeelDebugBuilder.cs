using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DamageNumberFeelDebugBuilder
{
    private const string Folder = "Assets/ProjectOverburst/Resources/UI/Debug";
    private const string PrefabPath = Folder + "/PF_DamageNumberFeelDebugUI.prefab";
    private const string FontPath = "Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium SDF.asset";

    [MenuItem("OVERBURST/Codex/Build Damage Number Feel Debug UI")]
    public static void BuildFromMenu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play before authoring the debug prefab.");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/ProjectOverburst/Resources/UI", "Debug");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new InvalidOperationException("Missing debug UI font: " + FontPath);

        var root = new GameObject("PF_DamageNumberFeelDebugUI", typeof(RectTransform),
            typeof(Image), typeof(DamageNumberFeelDebugUI));
        try
        {
            var rect = (RectTransform)root.transform;
            Place(rect, new Vector2(490f, 245f), new Vector2(236f, 426f));
            var background = root.GetComponent<Image>();
            background.color = new Color(.055f, .075f, .1f, .94f);
            background.raycastTarget = false;

            Button collapse = AddButton(rect, "Collapse", font,
                new Vector2(8f, 397f), new Vector2(220f, 25f), out TextMeshProUGUI status);
            status.fontSize = 12.5f;
            status.text = "강타·스포카·보통·기존 ▼";
            var contentRoot = new GameObject("Details", typeof(RectTransform));
            contentRoot.transform.SetParent(rect, false);
            Place((RectTransform)contentRoot.transform, Vector2.zero, new Vector2(236f, 426f));

            var buttons = new Button[5];
            var labels = new TextMeshProUGUI[5];
            for (int i = 0; i < buttons.Length; i++)
            {
                int column = i % 2;
                int row = i / 2;
                buttons[i] = AddButton(rect, "Preset " + i, font,
                    new Vector2(8f + column * 112f, 355f - row * 34f),
                    new Vector2(108f, 30f), out labels[i]);
                var preset = (DamageNumberFeelPreset)i;
                labels[i].text = DamageNumberPopup.PresetLabel(preset)
                    + " " + DamageNumberPopup.PresetLifetime(preset).ToString("0.00") + "초";
            }
            Button cycle = AddButton(rect, "Cycle", font,
                new Vector2(120f, 287f), new Vector2(108f, 30f), out TextMeshProUGUI cycleLabel);
            cycleLabel.text = "다음 효과 ▶";

            TextMeshProUGUI fontHeading = AddLabel(rect, "Font Heading", font,
                new Vector2(8f, 261f), new Vector2(220f, 22f), 13f);
            fontHeading.text = "9팀 폰트 / 숫자 전용";

            var fontButtons = new Button[4];
            var fontLabels = new TextMeshProUGUI[4];
            for (int i = 0; i < fontButtons.Length; i++)
            {
                fontButtons[i] = AddButton(rect, "Font " + i, font,
                    new Vector2(8f + (i % 2) * 112f, 229f - (i / 2) * 34f),
                    new Vector2(108f, 30f), out fontLabels[i]);
                fontLabels[i].text = DamageNumberPopup.FontLabel((DamageNumberFontChoice)i);
            }
            Button fontCycle = AddButton(rect, "Font Cycle", font,
                new Vector2(8f, 163f), new Vector2(220f, 28f), out TextMeshProUGUI fontCycleLabel);
            fontCycleLabel.text = "다음 글꼴 ▶";

            var weightButtons = new Button[2];
            var weightLabels = new TextMeshProUGUI[2];
            for (int i = 0; i < weightButtons.Length; i++)
            {
                weightButtons[i] = AddButton(rect, "Weight " + i, font,
                    new Vector2(8f + i * 112f, 129f), new Vector2(108f, 30f), out weightLabels[i]);
                weightLabels[i].text = DamageNumberPopup.WeightLabel((DamageNumberWeightChoice)i);
            }
            Button weightCycle = AddButton(rect, "Weight Cycle", font,
                new Vector2(8f, 97f), new Vector2(220f, 28f), out TextMeshProUGUI weightCycleLabel);
            weightCycleLabel.text = "다음 굵기 ▶";

            var sizeButtons = new Button[3];
            var sizeLabels = new TextMeshProUGUI[3];
            for (int i = 0; i < sizeButtons.Length; i++)
            {
                sizeButtons[i] = AddButton(rect, "Size " + i, font,
                    new Vector2(8f + i * 74f, 65f), new Vector2(70f, 28f), out sizeLabels[i]);
                sizeLabels[i].text = DamageNumberPopup.SizeLabel((DamageNumberSizeChoice)i);
            }
            Button sizeCycle = AddButton(rect, "Size Cycle", font,
                new Vector2(8f, 8f), new Vector2(108f, 43f), out TextMeshProUGUI sizeCycleLabel);
            sizeCycleLabel.text = "다음 크기 ▶";
            Button preview = AddButton(rect, "Preview", font,
                new Vector2(120f, 8f), new Vector2(108f, 43f), out TextMeshProUGUI previewLabel);
            previewLabel.text = "다시 보기";

            for (int i = rect.childCount - 1; i >= 0; i--)
            {
                Transform child = rect.GetChild(i);
                if (child != collapse.transform && child != contentRoot.transform)
                    child.SetParent(contentRoot.transform, false);
            }

            var component = root.GetComponent<DamageNumberFeelDebugUI>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("collapseButton").objectReferenceValue = collapse;
            serialized.FindProperty("contentRoot").objectReferenceValue = contentRoot;
            WireGroup(serialized, "presetButtons", "presetLabels", buttons, labels);
            WireGroup(serialized, "fontButtons", "fontLabels", fontButtons, fontLabels);
            WireGroup(serialized, "weightButtons", "weightLabels", weightButtons, weightLabels);
            WireGroup(serialized, "sizeButtons", "sizeLabels", sizeButtons, sizeLabels);
            serialized.FindProperty("cycleButton").objectReferenceValue = cycle;
            serialized.FindProperty("fontCycleButton").objectReferenceValue = fontCycle;
            serialized.FindProperty("weightCycleButton").objectReferenceValue = weightCycle;
            serialized.FindProperty("sizeCycleButton").objectReferenceValue = sizeCycle;
            serialized.FindProperty("previewButton").objectReferenceValue = preview;
            serialized.FindProperty("statusLabel").objectReferenceValue = status;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (saved == null || saved.GetComponent<DamageNumberFeelDebugUI>() == null
                || saved.GetComponentsInChildren<Button>(true).Length != 20)
                throw new InvalidOperationException("Damage number debug prefab did not reload correctly.");
            return "Saved " + PrefabPath + " with collapsible motion, font, weight, size, and preview controls.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void WireGroup(SerializedObject serialized, string buttonName, string labelName,
        Button[] buttons, TextMeshProUGUI[] labels)
    {
        SerializedProperty buttonArray = serialized.FindProperty(buttonName);
        SerializedProperty labelArray = serialized.FindProperty(labelName);
        buttonArray.arraySize = buttons.Length;
        labelArray.arraySize = labels.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            buttonArray.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            labelArray.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
        }
    }

    private static Button AddButton(RectTransform parent, string name, TMP_FontAsset font,
        Vector2 position, Vector2 size, out TextMeshProUGUI label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, position, size);
        var image = go.GetComponent<Image>();
        image.color = new Color(.16f, .19f, .24f, .92f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = new Color(.38f, .56f, .68f, 1f);
        colors.pressedColor = new Color(.12f, .38f, .5f, 1f);
        button.colors = colors;
        label = AddLabel((RectTransform)go.transform, "Label", font,
            Vector2.zero, size, 13f);
        return button;
    }

    private static TextMeshProUGUI AddLabel(RectTransform parent, string name, TMP_FontAsset font,
        Vector2 position, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, position, size);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.fontSize = fontSize;
        label.color = new Color(.95f, .96f, .98f, 1f);
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
