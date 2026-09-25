using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class RunUiLayout
{
    public static readonly Color Ivory = new Color(.94f, .90f, .82f);
    public static readonly Color Gold = new Color(.86f, .71f, .46f);
    public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
        return r;
    }
    public static Image Image(Transform parent, string name, Sprite sprite, Color color,
        float x, float y, float w, float h, bool sliced = false)
    {
        var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        image.sprite = sprite; image.color = color; image.raycastTarget = false;
        image.type = sliced && sprite != null && sprite.border.sqrMagnitude > 0 ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        image.preserveAspect = !sliced && sprite != null;
        if (sliced) image.pixelsPerUnitMultiplier = 2f;
        return image;
    }
    public static TMP_Text Text(Transform parent, string name, string value, TMP_FontAsset font,
        float x, float y, float w, float h, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        var label = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = value; label.fontSize = size; label.color = color;
        label.alignment = alignment; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal; label.richText = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }
    public static Button Button(Transform parent, string name, string caption, TMP_FontAsset font,
        Sprite sprite, float x, float y, float w, float h, Action action)
    {
        var bg = Image(parent, name, sprite, new Color(.63f, .36f, .32f), x, y, w, h, true);
        bg.raycastTarget = true;
        var button = bg.gameObject.AddComponent<Button>(); button.targetGraphic = bg;
        var colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1.25f, 1.16f, 1.06f);
        colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.8f, .7f, .6f);
        colors.disabledColor = new Color(.45f, .45f, .45f, .75f); button.colors = colors;
        Text(bg.transform, "Label", caption, font, 0, 0, w - 24, h - 6, 20, Ivory);
        if (action != null) button.onClick.AddListener(() => action());
        return button;
    }
}


