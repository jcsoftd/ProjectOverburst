using System.Collections.Generic;
using UnityEngine;

public enum WeaponComboGemSlotRole
{
    Element,
    LinkOrEnhance
}

public readonly struct WeaponComboGemSlotTargetIntent
{
    public readonly string AttackId;
    public readonly int SlotIndex;
    public readonly bool IsUnlocked;
    public readonly bool IsOccupied;

    public WeaponComboGemSlotTargetIntent(string attackId, int slotIndex, bool isUnlocked, bool isOccupied)
    {
        AttackId = attackId ?? string.Empty;
        SlotIndex = slotIndex;
        IsUnlocked = isUnlocked;
        IsOccupied = isOccupied;
    }
}

public readonly struct WeaponComboGemSlotDropIntent
{
    public readonly WeaponComboGemSlotTargetIntent Target;
    public readonly SlotUI OriginSlot;

    public WeaponComboGemSlotDropIntent(WeaponComboGemSlotTargetIntent target, SlotUI originSlot)
    {
        Target = target;
        OriginSlot = originSlot;
    }
}

public readonly struct WeaponComboGemInstalledDragIntent
{
    public readonly WeaponComboGemSlotView SourceView;
    public readonly string AttackId;
    public readonly int SlotIndex;
    public readonly string GemRuntimeInstanceId;
    public readonly Vector2 ScreenPosition;

    public WeaponComboGemInstalledDragIntent(
        WeaponComboGemSlotView sourceView,
        string attackId,
        int slotIndex,
        string gemRuntimeInstanceId,
        Vector2 screenPosition)
    {
        SourceView = sourceView;
        AttackId = attackId ?? string.Empty;
        SlotIndex = slotIndex;
        GemRuntimeInstanceId = gemRuntimeInstanceId ?? string.Empty;
        ScreenPosition = screenPosition;
    }
}

public sealed class WeaponComboGemSlotViewData
{
    public int SlotIndex { get; }
    public WeaponComboGemSlotRole Role { get; }
    public bool IsUnlocked { get; }
    public bool IsOccupied { get; }
    public string GemRuntimeInstanceId { get; }
    public string GemName { get; }
    public Sprite GemIcon { get; }
    public Color GemIconColor { get; }
    public ItemGrade GemGrade { get; }
    public string TypeText { get; }
    public string OptionText { get; }
    public string EffectDescription { get; }

    public WeaponComboGemSlotViewData(
        int slotIndex,
        WeaponComboGemSlotRole role,
        bool isUnlocked,
        bool isOccupied,
        string gemRuntimeInstanceId,
        string gemName,
        Sprite gemIcon,
        Color gemIconColor,
        ItemGrade gemGrade,
        string typeText,
        string optionText,
        string effectDescription)
    {
        SlotIndex = slotIndex;
        Role = role;
        IsUnlocked = isUnlocked;
        IsOccupied = isOccupied;
        GemRuntimeInstanceId = gemRuntimeInstanceId ?? string.Empty;
        GemName = gemName ?? string.Empty;
        GemIcon = gemIcon;
        GemIconColor = gemIconColor;
        GemGrade = gemGrade;
        TypeText = typeText ?? string.Empty;
        OptionText = optionText ?? string.Empty;
        EffectDescription = effectDescription ?? string.Empty;
    }
}

public sealed class WeaponComboGemRowViewData
{
    public int ComboStepIndex { get; }
    public string AttackId { get; }
    public IReadOnlyList<WeaponComboGemSlotViewData> Slots { get; }

    public WeaponComboGemRowViewData(int comboStepIndex, string attackId, IReadOnlyList<WeaponComboGemSlotViewData> slots)
    {
        ComboStepIndex = comboStepIndex;
        AttackId = attackId ?? string.Empty;
        Slots = slots;
    }
}

public sealed class WeaponComboGemPopupViewData
{
    public string WeaponRuntimeInstanceId { get; }
    public long Revision { get; }
    public string WeaponName { get; }
    public Sprite WeaponIcon { get; }
    public Color WeaponIconColor { get; }
    public IReadOnlyList<WeaponComboGemRowViewData> Rows { get; }

    public WeaponComboGemPopupViewData(
        string weaponRuntimeInstanceId,
        long revision,
        string weaponName,
        Sprite weaponIcon,
        Color weaponIconColor,
        IReadOnlyList<WeaponComboGemRowViewData> rows)
    {
        WeaponRuntimeInstanceId = weaponRuntimeInstanceId ?? string.Empty;
        Revision = revision;
        WeaponName = weaponName ?? string.Empty;
        WeaponIcon = weaponIcon;
        WeaponIconColor = weaponIconColor;
        Rows = rows;
    }
}
