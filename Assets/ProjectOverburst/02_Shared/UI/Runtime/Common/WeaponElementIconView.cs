using UnityEngine;
using UnityEngine.UI;

/// <summary>무기 인스턴스의 확정 원소를 표시한다. 무속성·다른 아이템에는 표시하지 않는다.</summary>
[DisallowMultipleComponent]
public sealed class WeaponElementIconView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private GameObject badgeSurface;
    [SerializeField] private Sprite fire, ice, electric, dark, light;

    public WeaponElement DisplayedElement { get; private set; }
    public Sprite DisplayedSprite => icon != null ? icon.sprite : null;
    public bool IsVisible => icon != null && icon.enabled && icon.gameObject.activeInHierarchy;

    public void Configure(Image image, GameObject surface, Sprite fireIcon, Sprite iceIcon,
        Sprite electricIcon, Sprite darkIcon, Sprite lightIcon)
    {
        icon = image;
        badgeSurface = surface;
        fire = fireIcon; ice = iceIcon; electric = electricIcon; dark = darkIcon; light = lightIcon;
        Present(WeaponElement.None);
    }

    public void Present(ItemData item) => Present(item != null ? item.ResolvedElement : WeaponElement.None);

    public void Present(WeaponElement element)
    {
        Sprite sprite = Resolve(element);
        DisplayedElement = sprite != null ? element : WeaponElement.None;
        if (icon != null)
        {
            if (icon.sprite != sprite) icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.color = Color.white;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }
        if (badgeSurface != null && badgeSurface.activeSelf != (sprite != null))
            badgeSurface.SetActive(sprite != null);
    }

    private Sprite Resolve(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fire;
            case WeaponElement.Ice: return ice;
            case WeaponElement.Electric: return electric;
            case WeaponElement.Dark: return dark;
            case WeaponElement.Light: return light;
            default: return null;
        }
    }
}
