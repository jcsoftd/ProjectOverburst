using System;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class WorldItemNameplateBridge : MonoBehaviour
{
    public event Action<WorldItemPickup> PointerHoverEntered;
    public event Action<WorldItemPickup> PointerHoverExited;

    public WorldLootPickupRequestResult LastRequestResult { get; private set; }

    public void HandlePointerDown(WorldItemPickup pickup, PointerEventData eventData)
    {
        PlayerPickupInteractor core = PlayerPickupInteractor.ActiveCore;
        WorldLootInteractionSnapshot snapshot = core != null ? core.CurrentSnapshot : null;
        if (core == null
            || snapshot == null
            || snapshot.Mode != WorldLootInteractionMode.LootFocus
            || snapshot.IsInputBlocked)
        {
            return;
        }

        eventData?.Use(); // LootFocus 라벨이 최초 PointerDown 소유
        LastRequestResult = core.RequestPickupByLabelPointerDown(pickup); // 50 코어에 의도 전달
    }

    public void HandlePointerReleased(PointerEventData eventData)
    {
        PlayerPickupInteractor core = PlayerPickupInteractor.ActiveCore;
        if (core == null || !PlayerPickupInteractor.IsPrimaryAttackSuppressed)
            return;

        eventData?.Use();
        core.NotifyPrimaryPointerReleased(); // release 뒤 공격 입력 복구
    }

    public void HandlePointerEnter(WorldItemPickup pickup)
    {
        if (pickup != null)
            PointerHoverEntered?.Invoke(pickup);
    }

    public void HandlePointerExit(WorldItemPickup pickup)
    {
        if (pickup != null)
            PointerHoverExited?.Invoke(pickup);
    }
}
