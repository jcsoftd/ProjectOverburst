using UnityEngine;

public struct InventoryWorldDropRequest // 월드 드롭 요청
{
    public PlayerInventory Inventory; // 소스 인벤토리
    public InventoryUI InventoryUI; // UI 영역
    public SlotUI SourceSlot; // 소스 슬롯
    public PlayerEquipment PlayerEquipment; // 플레이어 기준
    public Transform FallbackTransform; // fallback 기준
    public PickupGradeVfxSet PickupGradeVfxSet; // 등급 VFX
    public Vector2 ScreenPosition; // 포인터 위치
    public float ForwardDistance; // 전방 거리
    public float SpawnHeight; // 생성 높이
    public float GroundProbeHeight; // 지면 탐색 높이
    public float GroundProbeDistance; // 지면 탐색 거리
}

public static class InventoryWorldDrop // 인벤토리 월드 드롭
{
    public static InventoryActionResult TryDropInventorySlotToWorld(InventoryWorldDropRequest request)
    {
        if (request.SourceSlot == null || request.SourceSlot.DisplayItem == null)
            return InventoryActionResult.Fail(InventoryActionFailureReason.InvalidSource, "드롭할 인벤토리 슬롯이 없습니다.");

        if (IsPointerInsideInventoryUI(request))
            return InventoryActionResult.Fail(InventoryActionFailureReason.PointerInsideInventory, "인벤토리 내부 드롭은 월드 드롭이 아닙니다.");

        if (request.SourceSlot.IsWeaponSlot || request.SourceSlot.IsBagSlot || request.SourceSlot.IsLocked)
            return InventoryActionResult.Fail(InventoryActionFailureReason.BlockedSlotType, "장착 슬롯/잠긴 슬롯 월드 드롭은 아직 지원하지 않습니다.");

        if (request.Inventory == null)
            return InventoryActionResult.Fail(InventoryActionFailureReason.MissingInventory, "인벤토리 참조가 없습니다.");

        ItemData item = request.SourceSlot.DisplayItem; // 드롭 아이템

        if (!IsSameRuntimeItem(request.Inventory.GetItemAt(request.SourceSlot.SlotIndex), item))
            return InventoryActionResult.Fail(InventoryActionFailureReason.ItemMismatch, "슬롯의 아이템 상태가 변경되었습니다.");

        GameObject pickupObject = CreateWorldDropObject(item, request); // 기존 ItemData 이동

        if (pickupObject == null)
            return InventoryActionResult.Fail(InventoryActionFailureReason.WorldCreationFailed, "월드 아이템 생성에 실패했습니다.");

        if (!request.Inventory.ClearSlot(request.SourceSlot.SlotIndex))
        {
            Object.Destroy(pickupObject);
            return InventoryActionResult.Fail(InventoryActionFailureReason.InventoryRemoveFailed, "월드 생성 후 인벤토리 제거에 실패했습니다.");
        }

        return InventoryActionResult.Success();
    }

    private static GameObject CreateWorldDropObject(ItemData item, InventoryWorldDropRequest request)
    {
        Vector3 position = GetWorldDropPosition(request);
        if (item.baseData is CurrencyItemData)
        {
            CurrencyWorldPickup currencyPickup = WorldItemDropFactory.CreateCurrencyWorldPickupFromExistingItem(
                item,
                position,
                request.Inventory);

            return currencyPickup != null ? currencyPickup.gameObject : null;
        }

        WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickupFromExistingItem(
            item,
            position,
            request.Inventory,
            GetPlayerTransform(request),
            request.PickupGradeVfxSet);

        return pickup != null ? pickup.gameObject : null;
    }

    private static bool IsPointerInsideInventoryUI(InventoryWorldDropRequest request)
    {
        if (request.ScreenPosition == Vector2.zero || request.InventoryUI == null)
            return false;

        return request.InventoryUI.ContainsScreenPoint(request.ScreenPosition);
    }

    private static Vector3 GetWorldDropPosition(InventoryWorldDropRequest request)
    {
        Transform playerTransform = GetPlayerTransform(request); // 플레이어 기준
        Transform fallbackTransform = request.FallbackTransform; // fallback 기준
        Vector3 origin = playerTransform != null ? playerTransform.position : fallbackTransform != null ? fallbackTransform.position : Vector3.zero; // 시작점
        Vector3 forward = playerTransform != null ? playerTransform.forward : fallbackTransform != null ? fallbackTransform.forward : Vector3.forward; // 전방
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        forward.Normalize();
        Vector3 target = origin + forward * Mathf.Max(0.25f, request.ForwardDistance); // 후보 위치
        Vector3 rayOrigin = target + Vector3.up * Mathf.Max(0.1f, request.GroundProbeHeight); // 지면 ray 시작

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, Mathf.Max(0.1f, request.GroundProbeDistance), ~0, QueryTriggerInteraction.Ignore))
            target.y = hit.point.y + Mathf.Max(0.05f, request.SpawnHeight);
        else
            target.y = origin.y + Mathf.Max(0.05f, request.SpawnHeight);

        return target;
    }

    private static Transform GetPlayerTransform(InventoryWorldDropRequest request)
    {
        if (request.PlayerEquipment != null)
            return request.PlayerEquipment.transform;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player"); // 플레이어 대체 검색
        return playerObject != null ? playerObject.transform : null;
    }

    private static bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }
}
