using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>아이템 종류 표시. 원소 배지와 같은 기준 크기에서 투명 여백만 보정한다.</summary>
[DisallowMultipleComponent]
public sealed class ItemTypeIconView : MonoBehaviour
{
    [Serializable]
    public struct Artwork
    {
        public Sprite sprite;
        public Rect bounds;
        public float opticalScale;
    }

    [SerializeField] private Image icon;
    [SerializeField] private Artwork[] artwork;
    [SerializeField] private float referenceSize = 23f;
    public Sprite DisplayedSprite => icon != null ? icon.sprite : null;
    public bool IsVisible => icon != null && icon.enabled && gameObject.activeInHierarchy;
    public float ReferenceSize => referenceSize;

    public void Configure(Image image, Artwork[] values, float size)
    {
        icon = image;
        artwork = values;
        referenceSize = size;
        Present(null);
    }

    public void Present(ItemData item)
    {
        int index = ResolveIndex(item);
        bool visible = icon != null && artwork != null && index >= 0
            && index < artwork.Length && artwork[index].sprite != null;
        if (icon != null)
        {
            icon.sprite = visible ? artwork[index].sprite : null;
            icon.enabled = visible;
            icon.color = Color.white;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (visible)
            {
                Artwork value = artwork[index];
                float extent = Mathf.Max(value.bounds.width, value.bounds.height);
                float size = referenceSize * .87f * Mathf.Max(1f, value.opticalScale) / Mathf.Max(.01f, extent);
                RectTransform rect = icon.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                rect.sizeDelta = Vector2.one * size;
                rect.anchoredPosition = (Vector2.one * .5f - value.bounds.center) * size;
            }
        }
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    public static int ResolveIndex(ItemData item)
    {
        if (item == null || !item.HasValidBaseData) return -1;
        if (item.baseData is WeaponItemData) return 0;
        if (item.baseData is BagItemData) return 7;
        if (item.baseData is GearItemData gear)
        {
            switch (gear.kind)
            {
                case GearKind.Helmet: return 1;
                case GearKind.Chest: return 2;
                case GearKind.Gloves: return 3;
                case GearKind.Boots: return 4;
                case GearKind.Necklace: return 5;
                case GearKind.Earring: return 6;
            }
        }
        return -1;
    }
}
