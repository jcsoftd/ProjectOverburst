using System;
using UnityEditor;
using UnityEngine;

public static class MeleeElementHitSetupUtility
{
    private static readonly WeaponElement[] HitElements =
        { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
    [MenuItem("OVERBURST/Codex/Validate/Five Element Hit VFX")]
    public static void ValidateFromMenu() => ValidateFromCommandLine();
    public static void RunOnceFromCommandLine() => ValidateFromCommandLine();
    public static void ValidateFromCommandLine()
    {
        var catalog = Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);
        if (catalog == null) throw new InvalidOperationException("Missing hit catalog.");
        foreach (var element in HitElements)
        {
            if (!catalog.TryResolve(element, out var root) || root == null)
                throw new InvalidOperationException("Missing runtime pool: " + element);
            var controller = root.GetComponent<MeleeElementHitVfxController>();
            if (controller == null || !controller.HasPlayableContent(element) || catalog.ResolveLifetime(element) <= 0f)
                throw new InvalidOperationException("Unplayable runtime pool: " + element);
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                    throw new InvalidOperationException("Missing script: " + root.name);
        }
        foreach (var element in new[] { WeaponElement.Water, WeaponElement.Wind, WeaponElement.Earth })
            if (catalog.TryResolve(element, out _)) throw new InvalidOperationException("Retired element: " + element);
        Debug.Log("[FiveElementHit] Five dedicated playable pools, no retired elements PASS.");
    }
}
