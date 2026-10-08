using TMPro;
using UnityEngine;

[DefaultExecutionOrder(10004)]
[DisallowMultipleComponent]
public sealed class BuffTooltipUI : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text remainingText;
    [SerializeField] private TMP_Text effectText;
    private readonly Vector3[] corners = new Vector3[4];
    private BuffIconSlotUI owner;
    private Canvas canvas;
    private string shownName, shownEffect, shownDuration;
    public bool IsShown => owner != null && panel != null && panel.gameObject.activeSelf;
    public BuffIconSlotUI Owner => owner;

    public void Show(BuffIconSlotUI slot)
    {
        if (slot == null || panel == null || string.IsNullOrEmpty(slot.TooltipName)) return;
        owner = slot;
        panel.gameObject.SetActive(true);
        panel.SetAsLastSibling();
        Refresh();
    }

    public void Hide(BuffIconSlotUI slot)
    {
        if (owner != slot) return;
        owner = null;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        owner = null;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (owner == null) return;
        if (!owner.isActiveAndEnabled || string.IsNullOrEmpty(owner.DisplayedKey) || string.IsNullOrEmpty(owner.TooltipName))
        { Hide(owner); return; }
        Refresh();
    }

    private void Refresh()
    {
        if (shownName != owner.TooltipName) { shownName = owner.TooltipName; nameText.text = shownName; }
        if (shownEffect != owner.TooltipEffect)
        {
            shownEffect = owner.TooltipEffect;
            effectText.text = "<line-height=21.45>" + shownEffect + "</line-height>";
            effectText.ForceMeshUpdate(true, true);
            float bodyHeight = Mathf.Max(1, effectText.textInfo.lineCount) * 21.45f;
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 62.2f + bodyHeight);
            effectText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bodyHeight);
        }
        string duration = owner.TooltipPermanent ? owner.TooltipLifetime : BuffTooltipText.Number(Mathf.Ceil(Mathf.Max(0f, owner.TooltipRemaining) * 10f) * .1f) + "초";
        if (shownDuration != duration) { shownDuration = duration; remainingText.text = duration; }
        Position();
    }

    private void Position()
    {
        var parent = panel.parent as RectTransform;
        var icon = owner.transform as RectTransform;
        if (parent == null || icon == null) return;
        icon.GetWorldCorners(corners);
        Vector3 bottom = parent.InverseTransformPoint(corners[0]);
        Vector3 top = parent.InverseTransformPoint(corners[1]);
        Vector3 right = parent.InverseTransformPoint(corners[2]);
        Rect bounds = parent.rect;
        float width = panel.rect.width, height = panel.rect.height;
        float minX = bounds.xMin + 12f, maxX = Mathf.Max(minX, bounds.xMax - width - 12f);
        float x = Mathf.Clamp((top.x + right.x) * .5f - width * .5f, minX, maxX);
        float y = top.y + 12f;
        if (y + height > bounds.yMax - 12f) y = bottom.y - height - 12f;
        y = Mathf.Clamp(y, bounds.yMin + 12f, Mathf.Max(bounds.yMin + 12f, bounds.yMax - height - 12f));
        panel.localPosition = new Vector3(x, y, 0f);
        // Keep the exported 1px stroke on physical pixel boundaries instead of blending it across two rows.
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        Canvas rootCanvas = canvas.rootCanvas;
        Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        Vector2 screenTopLeft = RectTransformUtility.WorldToScreenPoint(camera, parent.TransformPoint(new Vector3(x, y + height, 0f)));
        screenTopLeft = new Vector2(Mathf.Round(screenTopLeft.x), Mathf.Round(screenTopLeft.y));
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenTopLeft, camera, out Vector2 snappedTopLeft))
            panel.localPosition = new Vector3(snappedTopLeft.x, snappedTopLeft.y - height, 0f);
    }
}
