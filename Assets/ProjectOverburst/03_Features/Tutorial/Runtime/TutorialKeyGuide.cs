using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class TutorialKeyGuide : MonoBehaviour
{
    public RectTransform content;
    public RectTransform[] slots;
    public Image[] icons;
    public TMP_Text[] glyphs, captions;
    public Sprite keycap, mouseLeft, mouseRight;
    public float VisibleWidth => width * content.localScale.x;
    private int count;
    private float width;

    public void Clear()
    {
        count = 0; width = 0; content.localScale = Vector3.one;
        foreach (var slot in slots) slot.gameObject.SetActive(false);
    }
    public void Text(string text) => Add(null, "", text, 0);
    public void MouseRight(string label) => Add(mouseRight, "", label, 30);
    public void Key(InputActionAsset asset, string actionName, string label = "", string part = null)
    {
        var action = asset ? asset.FindAction(PlayerInputFacade.GameplayMapName + "/" + actionName)
            ?? asset.FindAction("UI/" + actionName) : null;
        int index = OverburstKeyBindingRow.ResolveBindingIndex(action, part);
        string path = index >= 0 ? action.bindings[index].effectivePath : null;
        if (path == "<Mouse>/leftButton") Add(mouseLeft, "", label, 30);
        else if (path == "<Mouse>/rightButton") Add(mouseRight, "", label, 30);
        else
        {
            string key = string.IsNullOrEmpty(path) ? "미지정" : OverburstKeyBindingRow.ShortName(path);
            Add(keycap, key, label, key.Length > 1 ? 58 : 36);
        }
    }
    private void Add(Sprite sprite, string key, string label, float iconWidth)
    {
        if (count >= slots.Length) throw new InvalidOperationException("튜토리얼 키 안내 칸이 부족합니다.");
        int i = count++; var slot = slots[i]; slot.gameObject.SetActive(true);
        slot.anchoredPosition = new Vector2(width, 0);
        var icon = icons[i]; icon.sprite = sprite; icon.gameObject.SetActive(sprite);
        bool keyboard = sprite == keycap;
        icon.preserveAspect = !keyboard;
        icon.rectTransform.anchoredPosition = new Vector2(iconWidth * .5f, keyboard ? 0 : -1);
        icon.rectTransform.sizeDelta = new Vector2(keyboard ? iconWidth + 16 : 52, keyboard ? 52 : 56);
        var glyph = glyphs[i]; glyph.text = key; glyph.gameObject.SetActive(key.Length > 0);
        glyph.fontSize = key.Length > 1 ? 14 : 16;
        glyph.enableAutoSizing = true; glyph.fontSizeMin = 10; glyph.fontSizeMax = glyph.fontSize;
        glyph.rectTransform.anchoredPosition = Vector2.zero;
        glyph.rectTransform.sizeDelta = new Vector2(iconWidth, 36);
        var caption = captions[i]; caption.text = label; caption.gameObject.SetActive(label.Length > 0);
        float labelWidth = label.Length > 0 ? caption.GetPreferredValues(label, float.PositiveInfinity, float.PositiveInfinity).x + 2 : 0;
        float labelX = sprite && labelWidth > 0 ? iconWidth + 9 : iconWidth;
        caption.rectTransform.anchoredPosition = new Vector2(labelX, 0);
        caption.rectTransform.sizeDelta = new Vector2(labelWidth, 36);
        float slotWidth = labelX + labelWidth;
        slot.sizeDelta = new Vector2(slotWidth, 36); width += slotWidth + 9;
    }
    public void Finish()
    {
        width = Mathf.Max(0, width - 9); content.sizeDelta = new Vector2(width, 36);
        float available = ((RectTransform)transform).rect.width;
        content.localScale = Vector3.one * (width > available ? available / width : 1);
    }
}
