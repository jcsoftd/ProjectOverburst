using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class AccountUiLifetimeVerifier
{
    public static string Run()
    {
        const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
        var property = typeof(PlayerAccountInventoryService).GetProperty("Loadout", hidden);
        object original = property.GetValue(null);
        object isolated = Activator.CreateInstance(property.PropertyType, true);
        var objects = new List<UnityEngine.Object>();
        property.SetValue(null, isolated);
        try
        {
            var bagData = ScriptableObject.CreateInstance<BagItemData>(); objects.Add(bagData);
            var consumable = ScriptableObject.CreateInstance<ConsumableItemData>(); objects.Add(consumable);
            var bag = new ItemData(bagData, 1, ItemGrade.Mythic);
            bag.bagOptions = new List<BagRandomOptionRoll>
            {
                new BagRandomOptionRoll { optionType = BagRandomOptionType.MaxHp, value = 17 },
                new BagRandomOptionRoll { optionType = BagRandomOptionType.MaxStamina, value = 11 },
                new BagRandomOptionRoll { optionType = BagRandomOptionType.MoveSpeedPercent, value = 5 }
            };
            property.PropertyType.GetField("Bags", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(isolated, new[] { bag });
            var ui = NewInactive("UI lifetime fixture", objects);
            var bindings = ui.AddComponent<InventoryQuickSlotBindingController>();
            if (!bindings.Bind(1, new ItemData(consumable, 1, ItemGrade.Common))) throw new Exception("Binding rejected");
            UnityEngine.Object.DestroyImmediate(ui);
            var replacement = NewInactive("UI replacement fixture", objects);
            var nextBindings = replacement.AddComponent<InventoryQuickSlotBindingController>();
            if (nextBindings.GetBoundConsumable(1) != consumable) throw new Exception("Quickslot lost on UI recreation");
            var bridge = replacement.AddComponent<InventorySlotBridge>();
            var bags = (ItemData[])typeof(InventorySlotBridge).GetProperty("equippedBags", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bridge);
            if (!ReferenceEquals(bags[0], bag)) throw new Exception("Bag lost on UI recreation");
            var accountObject = NewInactive("Account fixture", objects);
            var service = accountObject.AddComponent<PlayerAccountInventoryService>();
            var actor = NewInactive("Actor fixture", objects);
            var health = actor.AddComponent<CombatHealth>();
            health.SetMaxHp(100, true);
            service.RefreshBagBonuses(null, health);
            service.RefreshBagBonuses(null, health);
            // 2026-09-30 스태미너 삭제: 구 스태미너 옵션(11)은 불러올 때 최대 체력(17)에 합쳐져 한 줄이 된다.
            if (bag.bagOptions.Count != 2 || bag.bagOptions.Exists(o => o.optionType == BagRandomOptionType.MaxStamina))
                throw new Exception("Legacy stamina option not migrated");
            if (Mathf.Abs(health.MaxHp - 128) > 0.01f) throw new Exception("Bag stats compounded");
            UnityEngine.Object.DestroyImmediate(replacement);
            if (Mathf.Abs(health.MaxHp - 128) > 0.01f) throw new Exception("UI destruction removed bag stats");
            service.RefreshBagBonuses(null, null);
            if (Mathf.Abs(health.MaxHp - 100) > 0.01f) throw new Exception("Old actor bonuses not removed");
            return "PASS quickslot and bag survive UI recreation; legacy stamina merges into max HP; bag stats apply once; UI destruction preserves bonuses; actor release removes bonuses";
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            property.SetValue(null, original);
        }
    }

    private static GameObject NewInactive(string name, List<UnityEngine.Object> objects)
    {
        var value = new GameObject(name); value.SetActive(false); objects.Add(value); return value;
    }
}
