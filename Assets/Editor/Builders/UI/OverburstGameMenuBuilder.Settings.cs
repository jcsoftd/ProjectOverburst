using System;
using System.Linq;
using DuloGames.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class OverburstGameMenuBuilder
{
    const float SettingsRowHeight = 160f;
    [MenuItem("OVERBURST/Codex/Objectizers/UI/Upgrade Settings and Blood Options")]
    public static string UpgradeSettingsPresentation()
    {
        PlayerCombatFacingVfxBuilder.RequireIdle();
        LoadFonts();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (asset == null || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Menu prefab missing or dirty");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            UpgradeSettingsLayout(root.GetComponentInChildren<OverburstSettingsPanel>(true));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Settings prefab save failed");
            return "Settings presentation and saved blood options updated";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static void UpgradeSettingsLayout(OverburstSettingsPanel panel)
    {
        if (panel == null || panel.pages.Length != 4 || panel.combatScroll == null) throw new InvalidOperationException("Settings contract changed");
        var list = panel.combatScroll.content;
        if (panel.bloodStyle == null)
            panel.bloodStyle = SelectRow(list, 0, "혈흔 효과", "혈흔 A·B·C 중 사용할 효과를 선택합니다", out _);
        if (panel.bloodPalette == null)
            panel.bloodPalette = SelectRow(list, 0, "혈흔 색상", "몬스터별 색상 또는 모두 붉은색으로 표시합니다", out _);
        var bloodDescription = panel.bloodStyle.transform.parent.parent.Find("Description");
        if (bloodDescription) bloodDescription.GetComponent<Text>().text = "혈흔 A·B·C 중 사용할 효과를 선택합니다";
        panel.bloodStyle.options.Clear(); panel.bloodStyle.options.AddRange(new[] { "A · 기존 혈흔", "B · Blood Effects Pack", "C · Volumetric Blood" });
        panel.bloodStyle.SelectOptionByIndex(0);
        panel.bloodPalette.options.Clear(); panel.bloodPalette.options.AddRange(new[] { "몬스터별 색상", "전체 붉은색" });
        panel.bloodPalette.SelectOptionByIndex(0);
        var previousRows = panel.bloodRows ?? Array.Empty<OverburstSettingsNumberRow>();
        var retainedRows = previousRows.Where(row => row && (int)row.control < 4).ToArray();
        panel.bloodRows = new OverburstSettingsNumberRow[4];
        string[] names = { "혈흔 크기", "비산 밝기", "바닥 크기", "바닥 밝기" };
        string[] descriptions = { "비산·이동 핏방울·바닥에 함께 적용합니다", "공중에 흩어지는 혈흔의 밝기", "전체 크기에 추가로 곱하는 바닥 자국 배율", "이미 생긴 자국에도 밝기를 적용합니다" };
        for (int i = 0; i < panel.bloodRows.Length; i++)
        {
            var control = (BloodComparisonTuning.Control)i;
            panel.bloodRows[i] = retainedRows.FirstOrDefault(row => row.control == control) ?? BloodNumberRow(list, names[i], descriptions[i], control);
        }
        foreach (var row in previousRows.Where(row => row && (int)row.control >= 4)) Object.DestroyImmediate(row.gameObject);
        if (panel.bloodResetButton == null)
        {
            Row(list, 0, "혈흔 조절값 초기화", "현재 선택한 혈흔의 크기·밝기를 되돌립니다", out var slot, true);
            panel.bloodResetButton = KitButton("Controls/Buttons/Rectangular/Button (Simple).prefab", slot, "Reset Blood", "선택한 혈흔 초기화", 34);
            Place((RectTransform)panel.bloodResetButton.transform, new Vector2(1f,.5f), Vector2.zero, new Vector2(700f,104f), new Vector2(1f,.5f));
        }
        ControlRow(panel.bloodResetButton).Find("Description").GetComponent<Text>().text = "현재 선택한 혈흔의 크기·밝기를 되돌립니다";
        float y = 24f;
        Header(list, "Feedback Section", "피격과 화면", ref y);
        foreach (var control in new Component[] { panel.cameraShake, panel.hitEffect, panel.motionBlur, panel.edgeBlur, panel.combatEdgeBlurIntensity }) ArrangeControlRow(control, ref y);
        Header(list, "Facing Section", "전투 방향 표시", ref y);
        foreach (var control in new Component[] { panel.combatFacingIndicator, panel.combatFacingStyle, panel.combatFacingBrightness }) ArrangeControlRow(control, ref y);
        Header(list, "Blood Section", "혈흔", ref y);
        ArrangeControlRow(panel.bloodStyle, ref y); ArrangeControlRow(panel.bloodPalette, ref y);
        foreach (var row in panel.bloodRows)
        {
            ArrangeRow((RectTransform)row.transform, ref y);
            // Slider drives both vertical anchors; compensate its 64-high parent.
            row.slider.handleRect.sizeDelta = new Vector2(28f, -12f);
            row.slider.handleRect.anchoredPosition = Vector2.zero;
        }
        ArrangeControlRow(panel.bloodResetButton, ref y);
        list.sizeDelta = new Vector2(0f, y + 24f);
        list.anchoredPosition = Vector2.zero;
        panel.combatScroll.scrollSensitivity = 90f;
        panel.combatScroll.verticalNormalizedPosition = 1f;
        panel.combatScroll.decelerationRate = .08f;
        foreach (int index in new[] { 0, 1 })
        {
            y = 24f;
            var page = panel.pages[index].transform;
            foreach (var row in page.Cast<Transform>().Where(t => t.name.StartsWith("Row • ")).ToArray()) ArrangeRow((RectTransform)row, ref y);
        }
        var header = panel.transform.Find("Header/Text").GetComponent<Text>();
        header.alignment = TextAnchor.MiddleCenter; header.alignByGeometry = true;
        header.rectTransform.anchoredPosition = new Vector2(header.rectTransform.anchoredPosition.x,-24f);
        foreach (var tab in panel.tabs)
        {
            var label = tab.transform.Find("Text").GetComponent<Text>();
            label.alignment = TextAnchor.MiddleCenter; label.alignByGeometry = true;
            var layout = tab.GetComponent<HorizontalLayoutGroup>();
            if (layout) layout.enabled = false;
            var element = tab.GetComponent<LayoutElement>() ?? tab.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 360f; element.preferredHeight = 112f;
            element.flexibleWidth = 0f; element.flexibleHeight = 0f;
            label.rectTransform.anchorMin = new Vector2(0f,.5f); label.rectTransform.anchorMax = new Vector2(1f,.5f);
            label.rectTransform.pivot = new Vector2(.5f,.5f); label.rectTransform.anchoredPosition = new Vector2(0f,-6.5f);
            label.rectTransform.sizeDelta = new Vector2(-40f,72f);
        }
        foreach (var row in panel.keyRows)
        {
            row.label.alignByGeometry = true;
            CenterButtonLabel(row.keyText);
        }
        var fixedKey = panel.pages[3].transform.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Key Button (Fixed)");
        if (fixedKey) CenterButtonLabel(fixedKey.transform.Find("Text").GetComponent<Text>());
        var footer = panel.transform.Find("Button Group");
        Place((RectTransform)panel.resetButton.transform, new Vector2(0f,0f), new Vector2(RowInset,116f), new Vector2(360f,104f), new Vector2(0f,.5f));
        Place((RectTransform)panel.footerCloseButton.transform, new Vector2(1f,0f), new Vector2(-RowInset,116f), new Vector2(360f,104f), new Vector2(1f,.5f));
        Place(panel.statusText.rectTransform, new Vector2(.5f,0f), new Vector2(0f,116f), new Vector2(1120f,104f), new Vector2(.5f,.5f));
        panel.statusText.alignment = TextAnchor.MiddleCenter; panel.statusText.alignByGeometry = true;
        CenterButtonLabel(panel.resetButton.transform.Find("Text").GetComponent<Text>());
        CenterButtonLabel(panel.footerCloseButton.transform.Find("Text").GetComponent<Text>());
        foreach (var selectable in panel.GetComponentsInChildren<Selectable>(true))
            if (!selectable.GetComponent<OverburstMenuSoundHook>()) selectable.gameObject.AddComponent<OverburstMenuSoundHook>();
    }
    static RectTransform ControlRow(Component control)
    {
        var cursor = control.transform;
        while (cursor != null && !cursor.name.StartsWith("Row • ")) cursor = cursor.parent;
        if (cursor == null) throw new InvalidOperationException("No row for " + control.name);
        return (RectTransform)cursor;
    }
    static void ArrangeControlRow(Component control, ref float y) => ArrangeRow(ControlRow(control), ref y);
    static void ArrangeRow(RectTransform row, ref float y)
    {
        row.SetAsLastSibling();
        row.anchorMin = new Vector2(0f,1f); row.anchorMax = Vector2.one; row.pivot = new Vector2(.5f,1f);
        row.anchoredPosition = new Vector2(0f,-y); row.sizeDelta = new Vector2(0f,SettingsRowHeight);
        var label = row.Find("Label").GetComponent<Text>();
        Place(label.rectTransform,new Vector2(0f,1f),new Vector2(RowInset,-28f),new Vector2(850f,52f),new Vector2(0f,1f));
        label.fontSize = 40; label.alignment = TextAnchor.MiddleLeft; label.alignByGeometry = true;
        var description = row.Find("Description").GetComponent<Text>();
        Place(description.rectTransform,new Vector2(0f,1f),new Vector2(RowInset,-84f),new Vector2(850f,44f),new Vector2(0f,1f));
        description.fontSize = 28; description.alignment = TextAnchor.MiddleLeft; description.alignByGeometry = true;
        description.verticalOverflow = VerticalWrapMode.Truncate;
        var slot = (RectTransform)row.Find("Control");
        Place(slot,new Vector2(1f,.5f),new Vector2(-RowInset,0f),new Vector2(900f,104f),new Vector2(1f,.5f));
        var line = row.Find("Rule") ?? NewImage(row,"Rule",Rule).transform;
        Place((RectTransform)line,new Vector2(0f,0f),new Vector2(RowInset,0f),new Vector2(RowWidth,2f),new Vector2(0f,0f));
        if (row.GetComponent<OverburstSettingsNumberRow>() == null)
        {
            foreach (var text in slot.GetComponentsInChildren<Text>(true)) { text.alignment = TextAnchor.MiddleCenter; text.alignByGeometry = true; }
            foreach (var select in slot.GetComponentsInChildren<UISwitchSelect>(true))
            {
                var text = select.transform.Find("Text").GetComponent<Text>();
                text.rectTransform.anchorMin = new Vector2(0f,.5f); text.rectTransform.anchorMax = new Vector2(1f,.5f);
                text.rectTransform.pivot = new Vector2(.5f,.5f); text.rectTransform.anchoredPosition = Vector2.zero;
                text.rectTransform.sizeDelta = new Vector2(-200f,72f);
            }
            foreach (var toggle in slot.GetComponentsInChildren<Toggle>(true))
            {
                toggle.transform.localScale = Vector3.one * 1.15f;
                foreach (var pair in new[] { ("On Text",49f), ("Off Text",145f) })
                {
                    var text = toggle.transform.Find(pair.Item1)?.GetComponent<Text>();
                    if (!text) continue;
                    Place(text.rectTransform,new Vector2(0f,.5f),new Vector2(pair.Item2,0f),new Vector2(80f,64f),new Vector2(.5f,.5f));
                    text.alignment = TextAnchor.MiddleCenter; text.alignByGeometry = true; text.raycastTarget = false;
                }
            }
            foreach (var slider in slot.GetComponentsInChildren<Slider>(true)) ArrangePercentageSlider(slider, slot.GetComponentInChildren<Toggle>(true) != null);

        }
        y += SettingsRowHeight;
    }
    static void ArrangePercentageSlider(Slider slider, bool combined)
    {
        var body = (RectTransform)slider.transform;
        body.anchorMin = new Vector2(combined ? .34f : 0f,.5f); body.anchorMax = new Vector2(1f,.5f);
        body.pivot = new Vector2(.5f,.5f); body.sizeDelta = new Vector2(0f,104f); body.anchoredPosition = Vector2.zero;
        var value = slider.transform.Find("Text")?.GetComponent<Text>();
        if (value)
        {
            Place(value.rectTransform,new Vector2(1f,.5f),new Vector2(-20f,0f),new Vector2(150f,64f),new Vector2(1f,.5f));
            value.fontSize = 34; value.raycastTarget = false;
        }
        var fillArea = slider.fillRect.parent as RectTransform;
        fillArea.anchorMin = new Vector2(0f,.5f); fillArea.anchorMax = new Vector2(1f,.5f);
        fillArea.offsetMin = new Vector2(46f,-10f); fillArea.offsetMax = new Vector2(-226f,10f);
        var handleArea = slider.handleRect.parent as RectTransform;
        handleArea.anchorMin = new Vector2(0f,.5f); handleArea.anchorMax = new Vector2(1f,.5f);
        handleArea.offsetMin = new Vector2(46f,-40f); handleArea.offsetMax = new Vector2(-226f,40f);
        slider.handleRect.pivot = new Vector2(.5f,.5f); slider.handleRect.sizeDelta = new Vector2(40f,-16f);
        slider.handleRect.anchoredPosition = Vector2.zero;
    }
    static void Header(RectTransform list, string name, string caption, ref float y)
    {
        var row = list.Find(name) as RectTransform;
        if (row == null)
        {
            row = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); row.SetParent(list,false);
            NewText(row,"Heading",caption,sans,36,Gold,TextAnchor.MiddleLeft,new Vector2(0f,.5f),new Vector2(RowInset,0f),new Vector2(900f,64f),new Vector2(0f,.5f));
        }
        row.SetAsLastSibling(); row.anchorMin = new Vector2(0f,1f); row.anchorMax = Vector2.one; row.pivot = new Vector2(.5f,1f);
        row.anchoredPosition = new Vector2(0f,-y); row.sizeDelta = new Vector2(0f,80f); y += 80f;
    }
    static void CenterButtonLabel(Text text)
    {
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(12f,0f); text.rectTransform.offsetMax = new Vector2(-12f,0f);
        text.alignment = TextAnchor.MiddleCenter; text.alignByGeometry = true;
    }
    static OverburstSettingsNumberRow BloodNumberRow(RectTransform list, string name, string description, BloodComparisonTuning.Control control)
    {
        var row = Row(list,0,name,description,out var slot,false);
        var number = row.gameObject.AddComponent<OverburstSettingsNumberRow>(); number.control = control;
        number.decrease = KitButton("Controls/Buttons/Rectangular/Button (Simple).prefab",slot,"Decrease","-",40);
        number.increase = KitButton("Controls/Buttons/Rectangular/Button (Simple).prefab",slot,"Increase","+",40);
        Place((RectTransform)number.decrease.transform,new Vector2(0f,.5f),Vector2.zero,new Vector2(88f,88f),new Vector2(0f,.5f));
        Place((RectTransform)number.increase.transform,new Vector2(1f,.5f),Vector2.zero,new Vector2(88f,88f),new Vector2(1f,.5f));
        CenterButtonLabel(number.decrease.transform.Find("Text").GetComponent<Text>());
        CenterButtonLabel(number.increase.transform.Find("Text").GetComponent<Text>());
        number.valueLabel = NewText(slot,"Value","1.0",sans,38,Bright,TextAnchor.MiddleCenter,new Vector2(0f,.5f),new Vector2(702f,0f),new Vector2(160f,64f),new Vector2(.5f,.5f));
        var body = new GameObject("Value Slider",typeof(RectTransform),typeof(Slider)).GetComponent<RectTransform>(); body.SetParent(slot,false);
        Place(body,new Vector2(0f,.5f),new Vector2(116f,0f),new Vector2(480f,64f),new Vector2(0f,.5f));
        var background = NewImage(body,"Background",Hex("403B30"));
        Place(background.rectTransform,new Vector2(.5f,.5f),Vector2.zero,new Vector2(480f,12f),new Vector2(.5f,.5f)); background.raycastTarget = true;
        var fillArea = new GameObject("Fill Area",typeof(RectTransform)).GetComponent<RectTransform>(); fillArea.SetParent(body,false); Stretch(fillArea); fillArea.offsetMin = new Vector2(14f,26f); fillArea.offsetMax = new Vector2(-14f,-26f);
        var fill = NewImage(fillArea,"Fill",new Color(Gold.r,Gold.g,Gold.b,.75f)); Stretch(fill.transform);
        var handleArea = new GameObject("Handle Slide Area",typeof(RectTransform)).GetComponent<RectTransform>(); handleArea.SetParent(body,false); Stretch(handleArea); handleArea.offsetMin = new Vector2(14f,0f); handleArea.offsetMax = new Vector2(-14f,0f);
        var handle = NewImage(handleArea,"Handle",Bright); Place(handle.rectTransform,new Vector2(.5f,.5f),Vector2.zero,new Vector2(28f,-12f),new Vector2(.5f,.5f)); handle.raycastTarget = true;
        number.slider = body.GetComponent<Slider>(); number.slider.fillRect = fill.rectTransform; number.slider.handleRect = handle.rectTransform; number.slider.targetGraphic = handle;
        number.slider.minValue = BloodComparisonTuning.Minimum(control)*10f; number.slider.maxValue = BloodComparisonTuning.Maximum(control)*10f; number.slider.wholeNumbers = true;
        number.slider.SetValueWithoutNotify(10f);
        return number;
    }
}
