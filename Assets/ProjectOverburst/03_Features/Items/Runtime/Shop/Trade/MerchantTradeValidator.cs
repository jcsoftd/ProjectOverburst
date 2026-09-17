using System.Collections.Generic;
using UnityEngine;

public sealed class MerchantTradeValidator
{
    public MerchantTradeResult Validate(
        MerchantTradeTransactionPlan plan,
        PlayerInventory playerInventory,
        MerchantInventory merchantInventory,
        StashCurrencyService stashCurrencyService,
        MerchantTradeValueCalculator valueCalculator)
    {
        if (plan == null)
            return MerchantTradeResult.Fail(MerchantTradeFailureReason.MissingService, "거래 계획이 없습니다.", 0, 0, 0, 0);

        if (playerInventory == null || merchantInventory == null || valueCalculator == null)
            return plan.ToFailureResult(MerchantTradeFailureReason.MissingService, "거래 참조가 부족합니다.");

        if (plan.IsEmpty)
            return plan.ToFailureResult(MerchantTradeFailureReason.EmptyTrade, "거래할 아이템이 없습니다.");

        if (HasDuplicateSources(plan.MerchantOffers) || HasDuplicateSources(plan.PlayerOffers))
            return plan.ToFailureResult(MerchantTradeFailureReason.InvalidSource, "거래 대상 아이템 참조가 중복되었습니다.");

        if (!ValidateSources(plan, playerInventory, merchantInventory))
            return plan.ToFailureResult(MerchantTradeFailureReason.InvalidSource, "거래 대상 아이템을 찾을 수 없습니다.");

        if ((plan.PlayerPaymentGold > 0 || plan.MerchantPayoutGold > 0) && CurrencyItemRegistry.Get(CurrencyType.Gold) == null)
            return plan.ToFailureResult(MerchantTradeFailureReason.MissingService, "Gold 데이터가 없습니다.");

        if (plan.PlayerPaymentGold > GetTotalGoldAmount(playerInventory, stashCurrencyService))
            return plan.ToFailureResult(MerchantTradeFailureReason.NotEnoughGold, "Gold가 부족합니다.");

        if (plan.MerchantPayoutGold > 0 && !merchantInventory.CanSpendCurrency(CurrencyType.Gold, plan.MerchantPayoutGold))
            return plan.ToFailureResult(MerchantTradeFailureReason.MerchantNotEnoughGold, "상인의 Gold가 부족합니다.");

        if (!CanPlayerReceiveAfterTrade(plan, playerInventory))
        {
            MerchantTradeFailureReason reason = plan.MerchantPayoutGold > 0
                ? MerchantTradeFailureReason.GoldReceiveInventoryFull
                : MerchantTradeFailureReason.InventoryFull;
            string message = plan.MerchantPayoutGold > 0 ? "Gold를 받을 공간이 부족합니다." : "플레이어 인벤토리 공간이 부족합니다.";
            return plan.ToFailureResult(reason, message);
        }

        if (!merchantInventory.CanAcceptItemsAfterOutgoingOffers(plan.MerchantOffers, plan.MerchantIncomingItems))
            return plan.ToFailureResult(MerchantTradeFailureReason.MerchantInventoryFull, "상인 인벤토리 공간이 부족합니다.");

        return plan.ToSuccessResult();
    }

    public MerchantTradeResult ValidateCurrent(
        MerchantTradeTransactionPlan plan,
        PlayerInventory playerInventory,
        MerchantInventory merchantInventory,
        StashCurrencyService stashCurrencyService)
    {
        if (plan == null)
            return MerchantTradeResult.Fail(MerchantTradeFailureReason.MissingService, "거래 계획이 없습니다.", 0, 0, 0, 0);

        if (playerInventory == null || merchantInventory == null)
            return plan.ToFailureResult(MerchantTradeFailureReason.MissingService, "거래 참조가 부족합니다.");

        if (!ValidateSources(plan, playerInventory, merchantInventory))
            return plan.ToFailureResult(MerchantTradeFailureReason.InvalidSource, "거래 대상 아이템을 찾을 수 없습니다.");

        if (plan.PlayerPaymentGold > 0 && !CanPayAutoGold(plan.PlayerPaymentGold, playerInventory, stashCurrencyService))
            return plan.ToFailureResult(MerchantTradeFailureReason.NotEnoughGold, "Gold가 부족합니다.");

        if (plan.MerchantPayoutGold > 0 && !merchantInventory.CanSpendCurrency(CurrencyType.Gold, plan.MerchantPayoutGold))
            return plan.ToFailureResult(MerchantTradeFailureReason.MerchantNotEnoughGold, "상인의 Gold가 부족합니다.");

        if (!CanPlayerReceiveAfterTrade(plan, playerInventory))
            return plan.ToFailureResult(MerchantTradeFailureReason.InventoryFull, "플레이어 인벤토리 공간이 부족합니다.");

        if (!merchantInventory.CanAcceptItemsAfterOutgoingOffers(plan.MerchantOffers, plan.MerchantIncomingItems))
            return plan.ToFailureResult(MerchantTradeFailureReason.MerchantInventoryFull, "상인 인벤토리 공간이 부족합니다.");

        return plan.ToSuccessResult();
    }

    private bool ValidateSources(MerchantTradeTransactionPlan plan, PlayerInventory playerInventory, MerchantInventory merchantInventory)
    {
        for (int i = 0; i < plan.MerchantOffers.Count; i++)
        {
            MerchantTradeOffer offer = plan.MerchantOffers[i];
            ItemData current = merchantInventory.GetItemAt(offer.SourceSlotIndex);
            if (!MerchantTradeItemUtility.IsValidOfferSource(current, offer))
                return false;
        }

        for (int i = 0; i < plan.PlayerOffers.Count; i++)
        {
            MerchantTradeOffer offer = plan.PlayerOffers[i];
            ItemData current = playerInventory != null ? playerInventory.GetItemAt(offer.SourceSlotIndex) : null;
            if (!MerchantTradeItemUtility.IsValidOfferSource(current, offer))
                return false;
        }

        return true;
    }

    private bool HasDuplicateSources(IReadOnlyList<MerchantTradeOffer> offers)
    {
        if (offers == null || offers.Count <= 1)
            return false;

        for (int i = 0; i < offers.Count; i++)
        {
            MerchantTradeOffer left = offers[i];
            for (int j = i + 1; j < offers.Count; j++)
            {
                MerchantTradeOffer right = offers[j];
                if (left != null
                    && right != null
                    && left.SourceSlotIndex == right.SourceSlotIndex
                    && left.SourceItem != null
                    && right.SourceItem != null
                    && left.SourceItem.IsSameRuntimeItem(right.SourceItem))
                    return true;
            }
        }

        return false;
    }

    private bool CanPlayerReceiveAfterTrade(MerchantTradeTransactionPlan plan, PlayerInventory playerInventory)
    {
        List<ItemData> simulation = new List<ItemData>();
        int capacity = playerInventory.UnlockedSlotCount;
        for (int i = 0; i < capacity; i++)
            simulation.Add(playerInventory.GetItemAt(i));

        for (int i = 0; i < plan.PlayerOffers.Count; i++)
        {
            if (!RemoveOfferFromPlayerSimulation(simulation, plan.PlayerOffers[i]))
                return false;
        }

        SimulateSpendInventoryGold(simulation, plan.PlayerPaymentGold);

        for (int i = 0; i < plan.PlayerIncomingItems.Count; i++)
        {
            if (!SimulateAddToInventory(simulation, plan.PlayerIncomingItems[i]))
                return false;
        }

        if (plan.MerchantPayoutGold > 0)
        {
            ItemData payoutGold = MerchantTradeItemUtility.CreateGoldItem(plan.MerchantPayoutGold);
            if (payoutGold == null || !SimulateAddToInventory(simulation, payoutGold))
                return false;
        }

        return true;
    }

    private void SimulateSpendInventoryGold(List<ItemData> simulation, int amount)
    {
        int remaining = Mathf.Max(0, amount);
        for (int i = 0; i < simulation.Count && remaining > 0; i++)
        {
            ItemData item = simulation[i];
            if (!MerchantTradeItemUtility.IsGold(item))
                continue;

            int spend = Mathf.Min(item.stackCount, remaining);
            if (item.stackCount <= spend)
                simulation[i] = null;
            else
                simulation[i] = new ItemData(item.baseData, item.level, item.grade, item.stackCount - spend);

            remaining -= spend;
        }
    }

    private bool RemoveOfferFromPlayerSimulation(List<ItemData> simulation, MerchantTradeOffer offer)
    {
        int index = offer.SourceSlotIndex;
        if (index < 0 || index >= simulation.Count)
            return false;

        ItemData current = simulation[index];
        if (!MerchantTradeItemUtility.IsValidOfferSource(current, offer))
            return false;

        int removeCount = Mathf.Max(1, offer.StackCount);
        if (current.stackCount > removeCount)
            simulation[index] = new ItemData(current.baseData, current.level, current.grade, current.stackCount - removeCount);
        else
            simulation[index] = null;

        return true;
    }

    private bool SimulateAddToInventory(List<ItemData> simulation, ItemData item)
    {
        if (item == null || !item.HasValidBaseData)
            return true;

        int remaining = Mathf.Max(1, item.stackCount);
        int maxStack = MerchantTradeItemUtility.GetMaxStack(item);

        if (MerchantTradeItemUtility.IsStackableItem(item))
        {
            for (int i = 0; i < simulation.Count && remaining > 0; i++)
            {
                ItemData target = simulation[i];
                if (!MerchantTradeItemUtility.CanStackItems(item, target))
                    continue;

                int addCount = Mathf.Min(maxStack - target.stackCount, remaining);
                simulation[i] = new ItemData(target.baseData, target.level, target.grade, target.stackCount + addCount);
                remaining -= addCount;
            }
        }

        while (remaining > 0)
        {
            int empty = MerchantTradeItemUtility.FindFirstEmptySlot(simulation);
            if (empty < 0)
                return false;

            int stackCount = Mathf.Min(maxStack, remaining);
            simulation[empty] = new ItemData(item.baseData, item.level, item.grade, stackCount);
            remaining -= stackCount;
        }

        return true;
    }

    private int GetTotalGoldAmount(PlayerInventory playerInventory, StashCurrencyService stashCurrencyService)
    {
        int stashGold = stashCurrencyService != null ? stashCurrencyService.GetAmount(CurrencyType.Gold) : 0;
        return MerchantTradeItemUtility.GetInventoryGoldAmount(playerInventory) + stashGold;
    }

    private bool CanPayAutoGold(int amount, PlayerInventory playerInventory, StashCurrencyService stashCurrencyService)
    {
        int requested = Mathf.Max(0, amount);
        if (requested <= 0)
            return true;

        int inventorySpend = Mathf.Min(requested, MerchantTradeItemUtility.GetInventoryGoldAmount(playerInventory));
        int stashSpend = requested - inventorySpend;
        CurrencyAmount stashCost = new CurrencyAmount(CurrencyType.Gold, stashSpend);
        return stashSpend <= 0 || stashCurrencyService != null && stashCurrencyService.CanPay(stashCost);
    }
}
