using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ElementalStatusIconStrip : MonoBehaviour
{
    private static readonly WeaponElement[] DisplayOrder =
    {
        WeaponElement.Fire,
        WeaponElement.Water,
        WeaponElement.Ice,
        WeaponElement.Electric
    };

    [Header("Layout")]
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private RectTransform slotRoot;
    [SerializeField] private Image iconImage;

    [Header("Element Icons")]
    [SerializeField] private Sprite fireIcon;
    [SerializeField] private Sprite waterIcon;
    [SerializeField] private Sprite iceIcon;
    [SerializeField] private Sprite electricIcon;
    [SerializeField] private Sprite windIcon;
    [SerializeField] private Sprite natureIcon;
    [SerializeField] private Sprite earthIcon;

    private ElementalStatusController statusController;
    private ElementalStatusController subscribedController;
    private bool hasVisibleStatus;
    private bool presentationVisible = true;

    public event Action<bool> VisibilityChanged;

    public bool HasVisibleStatus => hasVisibleStatus;

    private void Awake()
    {
        HideAll();
    }

    private void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void Bind(CombatHealth health)
    {
        ElementalStatusController nextController = null;
        if (health != null)
        {
            nextController = health.GetComponent<ElementalStatusController>();
            if (nextController == null)
                nextController = health.GetComponentInParent<ElementalStatusController>();
        }

        if (statusController == nextController && subscribedController == nextController)
        {
            Refresh();
            return;
        }

        Unsubscribe();
        statusController = nextController;
        Subscribe();
        Refresh();
    }

    public void Unbind()
    {
        Unsubscribe();
        statusController = null;
        HideAll();
    }

    public void SetPresentationVisible(bool visible)
    {
        if (presentationVisible == visible)
            return;

        presentationVisible = visible;
        ApplyContentVisibility();
    }

    private void Subscribe()
    {
        if (!isActiveAndEnabled || statusController == null || subscribedController == statusController)
            return;

        Unsubscribe();
        subscribedController = statusController;
        subscribedController.StatusChanged += HandleStatusChanged;
        subscribedController.StatusRemoved += HandleStatusRemoved;
        subscribedController.StatusesCleared += HandleStatusesCleared;
    }

    private void Unsubscribe()
    {
        if (subscribedController == null)
            return;

        subscribedController.StatusChanged -= HandleStatusChanged;
        subscribedController.StatusRemoved -= HandleStatusRemoved;
        subscribedController.StatusesCleared -= HandleStatusesCleared;
        subscribedController = null;
    }

    private void HandleStatusChanged(
        ElementalStatusSnapshot snapshot,
        ElementalStatusChangeReason reason)
    {
        Refresh();
    }

    private void HandleStatusRemoved(
        WeaponElement element,
        ElementalStatusRemoveReason reason)
    {
        Refresh();
    }

    private void HandleStatusesCleared(ElementalStatusClearReason reason)
    {
        HideAll();
    }

    private void Refresh()
    {
        Sprite visibleSprite = null;
        if (statusController != null)
        {
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                WeaponElement element = DisplayOrder[i];
                if (!statusController.TryGetStatus(element, out _))
                    continue;

                visibleSprite = ResolveSprite(element);
                if (visibleSprite != null)
                    break;
            }
        }

        bool visible = visibleSprite != null && slotRoot != null && iconImage != null;
        if (visible)
        {
            iconImage.sprite = visibleSprite;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        if (slotRoot != null)
            slotRoot.gameObject.SetActive(visible);
        SetVisible(visible);
    }

    private void HideAll()
    {
        if (slotRoot != null)
            slotRoot.gameObject.SetActive(false);

        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (hasVisibleStatus == visible)
        {
            ApplyContentVisibility();
            return;
        }

        hasVisibleStatus = visible;
        ApplyContentVisibility();
        VisibilityChanged?.Invoke(visible);
    }

    private void ApplyContentVisibility()
    {
        if (contentRoot != null)
            contentRoot.gameObject.SetActive(hasVisibleStatus && presentationVisible);
    }

    private Sprite ResolveSprite(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire:
                return fireIcon;
            case WeaponElement.Water:
                return waterIcon;
            case WeaponElement.Ice:
                return iceIcon;
            case WeaponElement.Electric:
                return electricIcon;
            case WeaponElement.Wind:
                return windIcon;
            default:
                return null;
        }
    }
}
