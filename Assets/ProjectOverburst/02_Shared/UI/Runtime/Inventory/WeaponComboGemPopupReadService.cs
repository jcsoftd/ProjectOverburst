using System.Collections.Generic;
using System.Text;
using UnityEngine;

public interface IWeaponComboGemPopupReadService
{
    bool TryBuild(ItemData weapon, out WeaponComboGemPopupViewData viewData, out string error);
}

public sealed class ItemDataWeaponComboGemPopupReadService : IWeaponComboGemPopupReadService
{
    public bool TryBuild(ItemData weapon, out WeaponComboGemPopupViewData viewData, out string error)
    {
        viewData = null;
        if (!WeaponComboGemEquipService.TryCreateSnapshot(weapon, out WeaponComboGemWeaponSnapshot snapshot, out error))
            return false;

        int comboCount = snapshot.Loadouts != null ? snapshot.Loadouts.Count : 0;
        List<WeaponComboGemRowViewData> rows = new List<WeaponComboGemRowViewData>(comboCount);
        for (int comboIndex = 0; comboIndex < comboCount; comboIndex++)
        {
            WeaponComboGemLoadoutSnapshot loadout = snapshot.Loadouts[comboIndex];
            List<WeaponComboGemSlotViewData> slots = new List<WeaponComboGemSlotViewData>(WeaponComboGemLoadout.SlotCapacity);
            for (int slotIndex = 0; slotIndex < WeaponComboGemLoadout.SlotCapacity; slotIndex++)
            {
                WeaponComboGemSlotSnapshot slot = loadout.Slots[slotIndex];
                ItemData gem = slot.Gem;
                ComboGemItemData gemData = gem != null ? gem.baseData as ComboGemItemData : null;
                WeaponComboGemSlotRules.TryGetClassifiedType(gemData, out ComboGemType gemType);

                slots.Add(new WeaponComboGemSlotViewData(
                    slot.SlotIndex,
                    slotIndex == 0 ? WeaponComboGemSlotRole.Element : WeaponComboGemSlotRole.LinkOrEnhance,
                    slot.IsUnlocked,
                    gem != null,
                    gem != null ? gem.runtimeInstanceId : string.Empty,
                    gem != null ? gem.itemName : string.Empty,
                    gem != null ? gem.icon : null,
                    gem != null ? gem.iconColor : Color.white,
                    gem != null ? gem.grade : ItemGrade.Common,
                    BuildTypeText(gemData, gemType),
                    BuildOptionText(gem, gemData),
                    BuildEffectDescription(gemData)));
            }

            rows.Add(new WeaponComboGemRowViewData(comboIndex, loadout.AttackId, slots));
        }

        viewData = new WeaponComboGemPopupViewData(
            snapshot.WeaponRuntimeInstanceId,
            snapshot.Revision,
            weapon != null ? weapon.itemName : string.Empty,
            weapon != null ? weapon.icon : null,
            weapon != null ? weapon.iconColor : Color.white,
            rows);
        error = null;
        return true;
    }

    private static string BuildTypeText(ComboGemItemData gemData, ComboGemType gemType)
    {
        string role = ItemTooltipFormatter.GetComboGemTypeName(gemType);
        if (gemData is ElementComboGemItemData elementData && elementData.TryGetElementDefinition(out WeaponElement element))
            return role + " · " + ItemTooltipFormatter.GetWeaponElementName(element);
        return role;
    }

    private static string BuildOptionText(ItemData gem, ComboGemItemData gemData)
    {
        if (gem == null || gem.comboGemOptions == null || gem.comboGemOptions.Count == 0)
            return string.Empty;

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < gem.comboGemOptions.Count; i++)
        {
            string line = ItemTooltipFormatter.FormatComboGemOptionWithRollRange(gem.comboGemOptions[i], gemData, gem.grade, "#8A8A8A");
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(line);
        }
        return builder.ToString();
    }

    private static string BuildEffectDescription(ComboGemItemData gemData)
    {
        return gemData != null && !string.IsNullOrWhiteSpace(gemData.description)
            ? gemData.description
            : "효과 설명 없음";
    }
}
