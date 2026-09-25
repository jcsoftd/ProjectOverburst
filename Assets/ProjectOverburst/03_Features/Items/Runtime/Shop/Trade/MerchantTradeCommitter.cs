using System.Collections.Generic;
using UnityEngine;

public sealed class MerchantTradeCommitter
{
    public MerchantTradeResult Commit(
        MerchantTradeTransactionPlan plan,
        PlayerInventory playerInventory,
        MerchantInventory merchantInventory,
        StashCurrencyService stashCurrencyService)
    {
        if (plan == null)
            return MerchantTradeResult.Fail(MerchantTradeFailureReason.MissingService, "거래 계획이 없습니다.", 0, 0, 0, 0);

        if (playerInventory == null || merchantInventory == null)
            return plan.ToFailureResult(MerchantTradeFailureReason.MissingService, "거래 참조가 부족합니다.");

        var accountSession = Overburst.Persistence.AccountGameplaySession.Current;
        if (accountSession != null && !accountSession.IsEditing)
        {
            MerchantTradeResult committedResult = default;
            try
            {
                bool committed = accountSession.Execute(() =>
                {
                    committedResult = Commit(plan, playerInventory, merchantInventory, stashCurrencyService);
                    return committedResult.success;
                });
                return committed ? committedResult : committedResult.success
                    ? plan.ToFailureResult(MerchantTradeFailureReason.TransferFailed, "거래를 저장하지 못했습니다.") : committedResult;
            }
            catch (System.IO.IOException)
            {
                return plan.ToFailureResult(MerchantTradeFailureReason.TransferFailed, "저장에 실패해 거래를 취소했습니다. 저장 공간과 파일 접근 상태를 확인해 주세요.");
            }
        }

        IReadOnlyList<MerchantTradeItemMove> playerToMerchantMoves = plan.PlayerToMerchantMoves;
        IReadOnlyList<MerchantTradeItemMove> merchantToPlayerMoves = plan.MerchantToPlayerMoves;

        for (int i = 0; i < playerToMerchantMoves.Count; i++)
        {
            MerchantTradeItemMove move = playerToMerchantMoves[i];
            if (!playerInventory.ConsumeItem(move.SourceItem, move.StackCount))
                return plan.ToFailureResult(MerchantTradeFailureReason.TransferFailed, "플레이어 아이템 이동에 실패했습니다.");
        }

        for (int i = 0; i < merchantToPlayerMoves.Count; i++)
        {
            MerchantTradeItemMove move = merchantToPlayerMoves[i];
            if (!merchantInventory.RemoveItemAt(move.SourceSlotIndex, move.SourceItem, move.StackCount))
                return plan.ToFailureResult(MerchantTradeFailureReason.TransferFailed, "상인 아이템 이동에 실패했습니다.");
        }

        if (plan.PlayerGoldPayment.HasValue)
        {
            if (!TrySpendAutoGold(plan.PlayerPaymentGold, playerInventory, stashCurrencyService)
                || !merchantInventory.TryAddCurrency(CurrencyType.Gold, plan.PlayerPaymentGold))
                return plan.ToFailureResult(MerchantTradeFailureReason.PaymentFailed, "Gold 결제에 실패했습니다.");
        }

        for (int i = 0; i < playerToMerchantMoves.Count; i++)
        {
            if (!merchantInventory.AddItem(playerToMerchantMoves[i].Item))
                return plan.ToFailureResult(MerchantTradeFailureReason.TransferFailed, "상인 인벤토리 지급에 실패했습니다.");
        }

        for (int i = 0; i < merchantToPlayerMoves.Count; i++)
        {
            if (!playerInventory.AddItem(merchantToPlayerMoves[i].Item))
                return plan.ToFailureResult(MerchantTradeFailureReason.TransferFailed, "아이템 지급에 실패했습니다.");
        }

        if (plan.MerchantGoldPayout.HasValue)
        {
            if (!merchantInventory.TrySpendCurrency(CurrencyType.Gold, plan.MerchantPayoutGold))
                return plan.ToFailureResult(MerchantTradeFailureReason.PaymentFailed, "상인의 Gold 지급에 실패했습니다.");

            ItemData payoutGold = MerchantTradeItemUtility.CreateGoldItem(plan.MerchantPayoutGold);
            if (payoutGold == null || !playerInventory.AddItem(payoutGold))
                return plan.ToFailureResult(MerchantTradeFailureReason.TransferFailed, "Gold 지급에 실패했습니다.");
        }

        return plan.ToSuccessResult();
    }

    private bool TrySpendAutoGold(int amount, PlayerInventory playerInventory, StashCurrencyService stashCurrencyService)
    {
        int requested = Mathf.Max(0, amount);
        if (requested <= 0)
            return true;

        if (!CanPayAutoGold(requested, playerInventory, stashCurrencyService))
            return false;

        int inventorySpend = Mathf.Min(requested, MerchantTradeItemUtility.GetInventoryGoldAmount(playerInventory));
        int stashSpend = requested - inventorySpend;
        if (!TrySpendInventoryGold(inventorySpend, playerInventory))
            return false;

        CurrencyAmount stashCost = new CurrencyAmount(CurrencyType.Gold, stashSpend);
        return stashSpend <= 0 || stashCurrencyService != null && stashCurrencyService.TrySpend(stashCost);
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

    private bool TrySpendInventoryGold(int amount, PlayerInventory playerInventory)
    {
        int remaining = Mathf.Max(0, amount);
        int limit = playerInventory != null ? playerInventory.UnlockedSlotCount : 0;
        for (int i = 0; i < limit && remaining > 0; i++)
        {
            ItemData item = playerInventory.GetItemAt(i);
            if (!MerchantTradeItemUtility.IsGold(item))
                continue;

            int spend = Mathf.Min(item.stackCount, remaining);
            if (!playerInventory.ConsumeItem(item, spend))
                return false;

            remaining -= spend;
        }

        return remaining <= 0;
    }
}
