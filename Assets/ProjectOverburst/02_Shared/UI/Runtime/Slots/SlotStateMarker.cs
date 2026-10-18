using UnityEngine;
using UnityEngine.UI;

// 승인된 UIWorkbench 시안2: 등급 연출 밖의 금속 모서리와 고정 상태 표식.
[DefaultExecutionOrder(200)]
public sealed class SlotStateMarker : MonoBehaviour
{
    private static Material frameMaterial;
    private static Sprite sharedFrame;
    private static int materialUsers;
    private readonly RawImage[] corners = new RawImage[4];
    private RectTransform root;
    private Image badgeBorder;
    private Text badgeText;
    private Vector4 frameUv;
    private bool ownsMaterial;
    private bool hasFrame;

    public static SlotStateMarker Create(Transform slot)
    {
        Sprite frame = null;
        foreach (Image image in slot.GetComponentsInChildren<Image>(true))
            if (image.sprite != null && image.sprite.name == "SpellSlot_Border") { frame = image.sprite; break; }

        if (frame != null) sharedFrame = frame;
        else frame = sharedFrame;

        var go = new GameObject("Slot State Marker", typeof(RectTransform));
        go.layer = slot.gameObject.layer;
        go.transform.SetParent(slot, false);
        var marker = go.AddComponent<SlotStateMarker>();
        marker.Build(frame);
        return marker;
    }

    public void Present(string label, bool showCorners, bool warning, bool hovered)
    {
        gameObject.SetActive(true);
        root.offsetMin = Vector2.one * (showCorners ? -4f : 0f);
        root.offsetMax = Vector2.one * (showCorners ? 4f : 0f);
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i].gameObject.SetActive(showCorners && hasFrame);
            corners[i].color = warning ? new Color(.72f, .52f, .43f, 1f) : Color.white;
        }
        badgeBorder.gameObject.SetActive(!string.IsNullOrEmpty(label));
        badgeText.text = label ?? string.Empty;
        badgeText.color = warning ? new Color32(240, 194, 181, 255) : new Color32(241, 231, 210, 255);
        badgeBorder.color = warning ? new Color32(151, 113, 104, 255) : new Color32(143, 129, 103, 255);
        badgeBorder.rectTransform.anchoredPosition = new Vector2(showCorners ? -8f : -4f, showCorners ? 0f : -4f);
        badgeBorder.rectTransform.sizeDelta = new Vector2(Mathf.Max(32f, badgeText.preferredWidth + 10f), 16f);
        UpdateCornerUvs();
        BringToFront();
    }

    private void Build(Sprite frame)
    {
        root = (RectTransform)transform;
        hasFrame = frame != null;
        Stretch(root);
        if (frameMaterial == null)
        {
            frameMaterial = new Material(Graphic.defaultGraphicMaterial) { name = "Slot State Metal", hideFlags = HideFlags.HideAndDontSave };
            frameMaterial.SetColor("_Color", new Color(3f, 3f, 3f, 1f));
        }
        materialUsers++;
        ownsMaterial = true;
        frameUv = frame != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(frame) : new Vector4(0f, 0f, 1f, 1f);

        // 등급 테두리 위를 덮던 전체 프레임·검은 음영은 사용하지 않는다.
        // 승인된 바깥 모서리와 작은 상태 표식만 남긴다.
        for (int i = 0; i < corners.Length; i++)
        {
            var image = corners[i] = Child<RawImage>("Metal corner " + i, root);
            image.texture = frame != null ? frame.texture : null;
            image.material = frameMaterial;
            Vector2 anchor = new Vector2(i % 2, i / 2);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = anchor;
            image.rectTransform.anchoredPosition = Vector2.zero;
            image.rectTransform.sizeDelta = Vector2.one * 20f;
        }

        badgeBorder = Child<Image>("State badge", root);
        var badgeRect = badgeBorder.rectTransform;
        badgeRect.anchorMin = badgeRect.anchorMax = badgeRect.pivot = new Vector2(1f, 0f);
        var background = Child<Image>("Background", badgeRect);
        Stretch(background.rectTransform);
        background.rectTransform.offsetMin = Vector2.one;
        background.rectTransform.offsetMax = -Vector2.one;
        background.color = new Color32(23, 22, 16, 255);
        badgeText = Child<Text>("Label", badgeRect);
        Stretch(badgeText.rectTransform);
        badgeText.font = Resources.Load<Font>("UI/Fonts/DamageFloating/Pretendard_Medium");
        badgeText.fontSize = 12;
        badgeText.alignment = TextAnchor.MiddleCenter;
        badgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        badgeText.verticalOverflow = VerticalWrapMode.Overflow;
        UpdateCornerUvs();
    }

    private T Child<T>(string name, Transform parent) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(T));
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        T graphic = go.GetComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void OnRectTransformDimensionsChange() => UpdateCornerUvs();

    private void UpdateCornerUvs()
    {
        if (root == null || corners[0] == null) return;
        float width = (frameUv.z - frameUv.x) * Mathf.Clamp01(20f / Mathf.Max(1f, root.rect.width));
        float height = (frameUv.w - frameUv.y) * Mathf.Clamp01(20f / Mathf.Max(1f, root.rect.height));
        for (int i = 0; i < corners.Length; i++)
            if (corners[i] != null) corners[i].uvRect = new Rect(i % 2 == 0 ? frameUv.x : frameUv.z - width,
                i / 2 == 0 ? frameUv.y : frameUv.w - height, width, height);
    }

    // 기존 고등급 오버레이가 LateUpdate에서 뒤에 붙어도 선택 표식은 그 위에 둔다.
    // 별도 Canvas를 만들지 않아 기존 ScrollRect 마스크와 창 정렬을 따른다.
    private void LateUpdate() => BringToFront();
    private void BringToFront()
    {
        if (transform.parent != null && transform.GetSiblingIndex() != transform.parent.childCount - 1)
            transform.SetAsLastSibling();
    }

    private void OnDestroy()
    {
        if (!ownsMaterial) return;
        materialUsers--;
        if (materialUsers == 0 && frameMaterial != null)
        {
            if (Application.isPlaying) Destroy(frameMaterial);
            else DestroyImmediate(frameMaterial);
            frameMaterial = null;
        }
    }
}
