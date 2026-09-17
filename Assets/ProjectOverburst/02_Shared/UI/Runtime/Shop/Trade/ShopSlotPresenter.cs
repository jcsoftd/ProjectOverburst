using System.Collections.Generic;

public sealed class ShopSlotPresenter // 상점 슬롯 표시
{
    public void RefreshMerchantInventory(
        SlotUI[] slots,
        MerchantInventory inventory,
        MerchantTradeSession session)
    {
        if (slots == null)
            return;

        int capacity = inventory != null ? inventory.Capacity : 0;
        for (int i = 0; i < slots.Length; i++)
        {
            SlotUI slot = slots[i];
            if (slot == null)
                continue;

            bool locked = i >= capacity;
            ItemData item = !locked && inventory != null ? inventory.GetItemAt(i) : null;
            slot.SetDisplayItem(item);
            slot.SetLocked(locked);
            slot.SetNewItemMarker(false);
            slot.SetActiveWeaponSlot(false);
            slot.SetContextSelected(!locked && session != null && session.ContainsSource(MerchantTradeOfferSide.Merchant, i));
        }
    }

    public void RefreshOffers(SlotUI[] slots, IReadOnlyList<MerchantTradeOffer> offers)
    {
        if (slots == null)
            return;

        int count = offers != null ? offers.Count : 0;
        for (int i = 0; i < slots.Length; i++)
        {
            SlotUI slot = slots[i];
            if (slot == null)
                continue;

            ItemData item = i < count ? offers[i].Item : null;
            slot.SetDisplayItem(item);
            slot.SetLocked(false);
            slot.SetNewItemMarker(false);
            slot.SetActiveWeaponSlot(false);
            slot.SetContextSelected(false);
        }
    }
}
