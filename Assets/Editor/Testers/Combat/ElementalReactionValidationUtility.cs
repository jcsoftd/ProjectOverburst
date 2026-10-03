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

        if (ElementalStatusRules.TryGetRule(WeaponElement.Water, out _))
            throw new InvalidOperationException("Retired Water has a status rule.");

        if (!ElementalStatusRules.TryGetRule(WeaponElement.Dark, out ElementalStatusRule darkRule)
            || darkRule.Element != WeaponElement.Dark || darkRule.MaxStacks <= 0 || darkRule.Duration <= 0f)
            throw new InvalidOperationException("Dark corrosion status rule is missing or invalid.");

        if (!ElementalStatusRules.TryGetRule(WeaponElement.Light, out ElementalStatusRule lightRule)
            || lightRule.Element != WeaponElement.Light)
            throw new InvalidOperationException("Active Light rule lookup failed.");

        // Rule lookup does not imply that the element can mark an enemy (60D: Light buffs the player).
        ElementalStatusValidationUtility.RunFromCommandLine();

        // 옛 조합 반응 처리기 ElementalCombatProcessor는 2026-09-30 제거했다(조합 반응 퇴역).

        Debug.Log("[FiveElementCombat] Active=5, water migration/rejection, Dark corrosion, Light rule lookup/enemy status rejection PASS.");
    }
}
