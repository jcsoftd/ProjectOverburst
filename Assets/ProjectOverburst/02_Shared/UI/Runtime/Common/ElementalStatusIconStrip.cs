using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ElementalStatusIconStrip : MonoBehaviour
{
    private static readonly WeaponElement[] DisplayOrder =
    {
        WeaponElement.Fire,
        WeaponElement.Ice,
        WeaponElement.Electric,
        WeaponElement.Dark,
        WeaponElement.Light
    };

    [Header("Layout")]
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private RectTransform slotRoot;
    [SerializeField] private Image iconImage;

    [Header("Element Icons")]
    [SerializeField] private Sprite fireIcon;
    [SerializeField] private Sprite iceIcon;
    [SerializeField] private Sprite electricIcon;
    [SerializeField] private Sprite darkIcon;
    [SerializeField] private Sprite lightIcon;
    [SerializeField] private Sprite freezeIcon;
    [SerializeField] private Sprite stunIcon;
    private static Sprite darkFallbackIcon;
    private static Sprite lightFallbackIcon;

    private ElementalStatusController statusController;
    private ElementalStatusController subscribedController;
    private bool hasVisibleStatus;
    private bool presentationVisible = true;
    private EnemyMovementReaction movementReaction;
    private bool lastStunned;
    public Sprite DisplayedSprite => iconImage != null && hasVisibleStatus ? iconImage.sprite : null;

    public event Action<bool> VisibilityChanged;

    public bool HasVisibleStatus => hasVisibleStatus;

    // 2026-10-01: 상단 대상·보스 HUD의 원소 상태 칸(EnemyTargetStatusRow)이 같은 아이콘 그림을 쓴다.
    public bool TryGetIcon(WeaponElement element, out Sprite sprite)
    {
        sprite = ResolveSprite(element);
        return sprite != null;
    }

    public bool TryGetStun(out float remaining, out Sprite sprite)
    {
        remaining = movementReaction != null ? movementReaction.ParryStunRemaining : 0f;
        sprite = StatusBuffIcons.Status("stun") ?? stunIcon;
        return remaining > 0f && sprite != null;
    }

    private void LateUpdate()
    {
        bool stunned = movementReaction != null && movementReaction.IsParryStunned;
        if (stunned != lastStunned) { lastStunned = stunned; Refresh(); }
    }

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
        movementReaction = health != null ? health.GetComponent<EnemyMovementReaction>() : null;
        if (movementReaction == null && health != null) movementReaction = health.GetComponentInParent<EnemyMovementReaction>();
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
        movementReaction = null;
        lastStunned = false;
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
        subscribedController.ReactionStateChanged += HandleReactionChanged;
        subscribedController.ReactionStateRemoved += HandleReactionRemoved;
    }

    private void Unsubscribe()
    {
        if (subscribedController == null)
            return;

        subscribedController.StatusChanged -= HandleStatusChanged;
        subscribedController.StatusRemoved -= HandleStatusRemoved;
        subscribedController.StatusesCleared -= HandleStatusesCleared;
        subscribedController.ReactionStateChanged -= HandleReactionChanged;
        subscribedController.ReactionStateRemoved -= HandleReactionRemoved;
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

    private void HandleReactionChanged(ElementalReactionStateSnapshot snapshot, ElementalReactionStateChangeReason reason) => Refresh();
    private void HandleReactionRemoved(ElementalReactionType type, ElementalReactionStateRemoveReason reason) => Refresh();

    private void Refresh()
    {
        Sprite visibleSprite = null;
        if (TryGetStun(out _, out Sprite stunnedSprite)) visibleSprite = stunnedSprite;
        else if (statusController != null && statusController.IsFrozen)
            visibleSprite = StatusBuffIcons.Status("freeze") ?? freezeIcon;
        if (visibleSprite == null && statusController != null)
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
        Sprite selected = StatusBuffIcons.Element(element, element == WeaponElement.Ice && statusController != null && statusController.IsFrozen);
        if (selected != null) return selected;
        switch (element)
        {
            case WeaponElement.Fire:
                return fireIcon;
            case WeaponElement.Ice:
                return iceIcon;
            case WeaponElement.Electric:
                return electricIcon;
            case WeaponElement.Dark:
                if (darkIcon != null) return darkIcon;
                if (darkFallbackIcon == null)
                {
                    FlaskItemData flask = Resources.Load<FlaskItemData>("Items/Flasks/Flask_Dark");
                    if (flask != null) darkFallbackIcon = flask.icon;
                }
                return darkFallbackIcon;
            case WeaponElement.Light:
                if (lightIcon != null) return lightIcon;
                if (lightFallbackIcon == null)
                {
                    FlaskItemData flask = Resources.Load<FlaskItemData>("Items/Flasks/Flask_Light");
                    if (flask != null) lightFallbackIcon = flask.icon;
                }
                return lightFallbackIcon;
            default:
                return null;
        }
    }
}
