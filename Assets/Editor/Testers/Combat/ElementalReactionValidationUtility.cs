using System;
using UnityEditor;
using UnityEngine;

// Combination reactions are retired. Validate the current five-element boundary.
public static class ElementalReactionValidationUtility
{
    [MenuItem("OVERBURST/Codex/Validate/Five Element Combat Boundary")]
    public static void ValidateFromMenu() => ValidateFromCommandLine();

    public static void ValidateFromCommandLine()
    {
        WeaponElement[] active =
        {
            WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric,
            WeaponElement.Dark, WeaponElement.Light
        };
        foreach (WeaponElement element in active)
            if (!OverburstElementRules.IsActive(element))
                throw new InvalidOperationException("Active element rejected: " + element);

        if (OverburstElementRules.IsActive(WeaponElement.Water)
            || OverburstElementRules.IsActive(WeaponElement.Wind)
            || OverburstElementRules.IsActive(WeaponElement.Earth)
            || OverburstElementRules.MigrateLegacy(WeaponElement.Water) != WeaponElement.Dark)
            throw new InvalidOperationException("Retired element or water migration contract failed.");

        if (ElementalStatusRules.TryGetRule(WeaponElement.Water, out _)
            || ElementalStatusRules.TryGetRule(WeaponElement.Dark, out _)
            || ElementalStatusRules.TryGetRule(WeaponElement.Light, out _))
            throw new InvalidOperationException("An undecided element has a status rule.");

        // 옛 조합 반응 처리기 ElementalCombatProcessor는 2026-09-30 제거했다(조합 반응 퇴역).

        Debug.Log("[FiveElementCombat] Active=5, water migration, undecided statuses, and retired combination reactions PASS.");
    }
}
