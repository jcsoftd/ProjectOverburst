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
        var originalSession = Overburst.Persistence.AccountGameplaySession.Current;
        originalSession?.Detach();
        property.SetValue(null, isolated);
        try
        {
            var bagData = ScriptableObject.CreateInstance<BagItemData>(); objects.Add(bagData);
            var consumable = ScriptableObject.CreateInstance<ConsumableItemData>(); objects.Add(consumable);
            var bag = new ItemData(bagData, 1, ItemGrade.Mythic);
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
            bag.EnsureRuntimeState();
            if (bag.bagOptions.Count != 0 || !BagQuality.IsValid(bag.bagState, bag.grade))
                throw new Exception("Fresh bag quality changed on UI recreation");
            if (Mathf.Abs(health.MaxHp - 100) > 0.01f) throw new Exception("Bag altered combat HP");
            UnityEngine.Object.DestroyImmediate(replacement);
            if (Mathf.Abs(health.MaxHp - 100) > 0.01f) throw new Exception("UI destruction removed bag stats");
            service.RefreshBagBonuses(null, null);
            if (Mathf.Abs(health.MaxHp - 100) > 0.01f) throw new Exception("Old actor bonuses not removed");
            return "PASS quickslot and fresh bag survive UI recreation; bag never alters combat HP; UI destruction preserves quality";
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            property.SetValue(null, original);
            originalSession?.Attach();
        }
    }

    private static GameObject NewInactive(string name, List<UnityEngine.Object> objects)
    {
        var value = new GameObject(name); value.SetActive(false); objects.Add(value); return value;
    }
}
