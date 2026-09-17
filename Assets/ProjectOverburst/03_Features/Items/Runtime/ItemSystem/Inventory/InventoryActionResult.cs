public enum InventoryActionFailureReason // 실패 이유
{
    None,
    InvalidSource,
    InvalidTarget,
    PointerInsideInventory,
    BlockedSlotType,
    MissingInventory,
    ItemMismatch,
    WorldCreationFailed,
    InventoryRemoveFailed
}

public readonly struct InventoryActionResult // 처리 결과
{
    public bool Succeeded { get; }
    public InventoryActionFailureReason FailureReason { get; }
    public string Message { get; }

    private InventoryActionResult(bool succeeded, InventoryActionFailureReason failureReason, string message)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        Message = message;
    }

    public static InventoryActionResult Success()
    {
        return new InventoryActionResult(true, InventoryActionFailureReason.None, string.Empty);
    }

    public static InventoryActionResult Fail(InventoryActionFailureReason failureReason, string message)
    {
        return new InventoryActionResult(false, failureReason, message);
    }
}
