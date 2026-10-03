#if UNITY_EDITOR
using Overburst.DebugTools;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>정식 디버그 프리팹에 작은 우측 하단 재생 안내를 제작한다.</summary>
public static class VisualPlayOverlayBuilder
{
    public static void Build(RectTransform root, DebugHubStyle style)
    {
        var controller = root.gameObject.AddComponent<VisualPlayOverlay>();
        var view = new SerializedObject(controller);
        var panel = Rect(root, "Visual Play Overlay", new Vector2(390, 112), new Vector2(-18, 18));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 0);
        var image = panel.gameObject.AddComponent<Image>(); image.color = new Color(.055f, .07f, .09f, .94f);
        var outline = panel.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(.3f, .38f, .46f, .9f); outline.effectDistance = new Vector2(1, -1);
        Set(view, "panel", panel.gameObject);
        Set(view, "title", Label(panel, style, "Current", new Vector2(12, -10), new Vector2(298, 35), 14, new Color(.94f, .96f, 1f)));
        Set(view, "progress", Label(panel, style, "Progress", new Vector2(12, -48), new Vector2(366, 20), 12, new Color(.63f, .75f, .83f)));
        Set(view, "expand", MakeButton(panel, style, "expand", "설명", new Vector2(316, -11), new Vector2(62, 26), false, out var expandLabel));
        Set(view, "expandLabel", expandLabel);
        string[] fields = { "pause", "replay", "next", "stop" };
        string[] labels = { "항목 뒤 멈춤", "다시 보기", "다음", "중단" };
        for (int i = 0; i < fields.Length; i++)
        {
            Set(view, fields[i], MakeButton(panel, style, fields[i], labels[i], new Vector2(12 + i * 93, -74), new Vector2(87, 28), i == 3, out var text));
            if (i == 0) Set(view, "pauseLabel", text);
        }
        var details = Rect(panel, "Details", new Vector2(366, 114), new Vector2(12, -112));
        Set(view, "details", details.gameObject);
        Set(view, "observe", Label(details, style, "Observe", Vector2.zero, new Vector2(366, 68), 13, new Color(.77f, .83f, .89f)));
        string[] reviewFields = { "checkedButton", "problemButton", "open" };
        string[] reviewLabels = { "확인함", "문제 있음", "패널 열기" };
        for (int i = 0; i < reviewFields.Length; i++)
            Set(view, reviewFields[i], MakeButton(details, style, reviewFields[i], reviewLabels[i], new Vector2(i * 124, -78), new Vector2(118, 28), false, out _));
        details.gameObject.SetActive(false); panel.gameObject.SetActive(false); view.ApplyModifiedPropertiesWithoutUndo();
    }
    static TMP_Text Label(Transform parent, DebugHubStyle style, string name, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var rect = Rect(parent, name, size, position);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = style.font; text.fontSize = fontSize; text.color = color;
        text.alignment = TextAlignmentOptions.TopLeft; text.textWrappingMode = TextWrappingModes.Normal; text.raycastTarget = false; text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
    static Button MakeButton(Transform parent, DebugHubStyle style, string name, string label, Vector2 position, Vector2 size, bool danger, out TMP_Text text)
    {
        var rect = Rect(parent, name, size, position);
        var background = rect.gameObject.AddComponent<Image>(); background.color = danger ? new Color(.35f, .15f, .17f) : new Color(.14f, .2f, .26f);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = background;
        var colors = button.colors; colors.fadeDuration = 0; button.colors = colors;
        text = Label(rect, style, "Label", Vector2.zero, size, 12, Color.white); text.text = label; text.alignment = TextAlignmentOptions.Center;
        return button;
    }
    static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); result.gameObject.layer = 5;
        result.SetParent(parent, false); result.sizeDelta = size; result.anchorMin = result.anchorMax = result.pivot = new Vector2(0, 1); result.anchoredPosition = position; return result;
    }
    static void Set(SerializedObject target, string field, Object value) => target.FindProperty(field).objectReferenceValue = value;
}
#endif
