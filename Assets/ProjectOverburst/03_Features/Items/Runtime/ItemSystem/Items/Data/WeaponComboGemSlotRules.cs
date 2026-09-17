public enum ComboGemType // 콤보 보석 역할
{
    Unspecified = 0,
    Element = 1,
    Link = 2,
    Enhancement = 3
}

public enum WeaponComboGemSlotValidationFailureReason // 슬롯 규칙 실패 이유
{
    None = 0,
    InvalidSlot = 1,
    InvalidUnlockedSlotCount = 2,
    SlotLocked = 3,
    UnclassifiedGem = 4,
    GemTypeMismatch = 5
}

public readonly struct WeaponComboGemSlotValidationResult // 순수 슬롯 검증 결과
{
    public readonly bool Succeeded;
    public readonly WeaponComboGemSlotValidationFailureReason FailureReason;
    public readonly int SlotIndex;
    public readonly ComboGemType GemType;

    private WeaponComboGemSlotValidationResult(
        bool succeeded,
        WeaponComboGemSlotValidationFailureReason failureReason,
        int slotIndex,
        ComboGemType gemType)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        SlotIndex = slotIndex;
        GemType = gemType;
    }

    internal static WeaponComboGemSlotValidationResult Success(int slotIndex, ComboGemType gemType)
    {
        return new WeaponComboGemSlotValidationResult(
            true,
            WeaponComboGemSlotValidationFailureReason.None,
            slotIndex,
            gemType);
    }

    internal static WeaponComboGemSlotValidationResult Fail(
        WeaponComboGemSlotValidationFailureReason failureReason,
        int slotIndex,
        ComboGemType gemType)
    {
        return new WeaponComboGemSlotValidationResult(false, failureReason, slotIndex, gemType);
    }
}

public static partial class WeaponComboGemSlotRules // 70번대 슬롯 순수 규칙
{
    public const int SlotCapacity = 4;
    public const int InitialUnlockedSlotCount = 3;
    public const int ElementSlotIndex = 0;

    public static bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < SlotCapacity;
    }

    public static bool IsValidUnlockedSlotCount(int unlockedSlotCount)
    {
        return unlockedSlotCount >= InitialUnlockedSlotCount && unlockedSlotCount <= SlotCapacity;
    }

    public static bool IsSlotUnlocked(int slotIndex, int unlockedSlotCount)
    {
        return IsValidSlotIndex(slotIndex)
            && IsValidUnlockedSlotCount(unlockedSlotCount)
            && slotIndex < unlockedSlotCount;
    }

    public static bool IsGemTypeAllowed(int slotIndex, ComboGemType gemType)
    {
        if (!IsValidSlotIndex(slotIndex))
            return false;

        if (slotIndex == ElementSlotIndex)
            return gemType == ComboGemType.Element;

        return gemType == ComboGemType.Link || gemType == ComboGemType.Enhancement;
    }

    public static ComboGemType[] GetAllowedTypes(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
            return new ComboGemType[0];

        if (slotIndex == ElementSlotIndex)
            return new[] { ComboGemType.Element };

        return new[] { ComboGemType.Link, ComboGemType.Enhancement };
    }

    public static WeaponComboGemSlotValidationResult Validate(
        int slotIndex,
        int unlockedSlotCount,
        ComboGemType gemType)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            return WeaponComboGemSlotValidationResult.Fail(
                WeaponComboGemSlotValidationFailureReason.InvalidSlot,
                slotIndex,
                gemType);
        }

        if (!IsValidUnlockedSlotCount(unlockedSlotCount))
        {
            return WeaponComboGemSlotValidationResult.Fail(
                WeaponComboGemSlotValidationFailureReason.InvalidUnlockedSlotCount,
                slotIndex,
                gemType);
        }

        if (!IsSlotUnlocked(slotIndex, unlockedSlotCount))
        {
            return WeaponComboGemSlotValidationResult.Fail(
                WeaponComboGemSlotValidationFailureReason.SlotLocked,
                slotIndex,
                gemType);
        }

        if (gemType == ComboGemType.Unspecified)
        {
            return WeaponComboGemSlotValidationResult.Fail(
                WeaponComboGemSlotValidationFailureReason.UnclassifiedGem,
                slotIndex,
                gemType);
        }

        if (!IsGemTypeAllowed(slotIndex, gemType))
        {
            return WeaponComboGemSlotValidationResult.Fail(
                WeaponComboGemSlotValidationFailureReason.GemTypeMismatch,
                slotIndex,
                gemType);
        }

        return WeaponComboGemSlotValidationResult.Success(slotIndex, gemType);
    }
}
