using Overburst.Persistence;
using UnityEngine;

/// <summary>테스트 편의용 보유 효과. 장착·버프 상태를 남기지 않고 현재 인벤토리를 확인한다.</summary>
[CreateAssetMenu(fileName = "TesterSupportCharm", menuName = "Items/Tester Support")]
public sealed class TesterSupportItemData : JunkItemData
{
    public const string ContentId = "item.tester.support-charm";

    public static int CurrentMultiplier => Multiplier(PlayerAccountInventoryService.SharedInventory);

    public static int Multiplier(PlayerInventory inventory)
    {
        if (inventory != null)
            foreach (var item in inventory.Items)
                if (item?.baseData is TesterSupportItemData && item.stackCount > 0) return 2;
        return 1;
    }

    public static int Multiplier(AccountSnapshot state)
    {
        foreach (var item in state.items)
            if (item.contentId == ContentId && item.count > 0 && state.inventory.Contains(item.instanceId)) return 2;
        return 1;
    }

    public static float ApplyDropChance(float chance) => Mathf.Clamp01(chance * CurrentMultiplier);
}
