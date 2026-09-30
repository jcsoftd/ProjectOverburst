using System;
using UnityEditor;
using UnityEngine;

public static class FiveElementCombatAssetValidationUtility
{
    [MenuItem("OVERBURST/Codex/Validate/Five Element Combat Assets")]
    public static void ValidateFromMenu() => ValidateFromCommandLine();

    public static void ValidateFromCommandLine()
    {
        ElementalReactionValidationUtility.ValidateFromCommandLine();
        MeleeElementHitSetupUtility.ValidateFromCommandLine();
        MeleeElementStatusAuraSetupUtility.ValidateFromCommandLine();
        Debug.Log("[FiveElementCombatAssets] All current combat-asset boundaries PASS.");
    }
}
