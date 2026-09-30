using System;
using UnityEditor;
using UnityEngine;

// The authored hit modules are stable. This local check guards their five-element boundary.
public static class MeleeElementHitSetupUtility
{
    private const string PrefabPath = "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementHit/PF_VFX_MeleeElementHit.prefab";
    private const string CatalogPath = "Assets/ProjectOverburst/Resources/Combat/VFX/MeleeElementHitVfxCatalog.asset";
    private static readonly WeaponElement[] HitElements =
        { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric };

    [MenuItem("OVERBURST/Codex/Validate/Five Element Hit VFX")]
    public static void ValidateFromMenu() => ValidateFromCommandLine();

    public static void RunOnceFromCommandLine() => ValidateFromCommandLine();

    public static void ValidateFromCommandLine()
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        MeleeElementHitVfxCatalog catalog = AssetDatabase.LoadAssetAtPath<MeleeElementHitVfxCatalog>(CatalogPath);
        if (root == null || catalog == null || root.transform.childCount != HitElements.Length)
            throw new InvalidOperationException("Five-element hit prefab/catalog structure is missing.");
        MeleeElementHitVfxController controller = root.GetComponent<MeleeElementHitVfxController>();
        if (controller == null || catalog.sharedHitPrefab != root)
            throw new InvalidOperationException("Hit controller/catalog binding is missing.");

        foreach (WeaponElement element in HitElements)
        {
            GameObject slot = controller.GetElementObject(element);
            if (slot == null || slot.transform.parent != root.transform || slot.activeSelf
                || slot.name != element + "_Hit" || !controller.HasPlayableContent(element)
                || !catalog.TryResolve(element, out GameObject resolved) || resolved != root
                || catalog.ResolveLifetime(element) <= 0f)
                throw new InvalidOperationException("Hit slot is invalid: " + element);
        }

        foreach (WeaponElement element in new[]
            { WeaponElement.Water, WeaponElement.Wind, WeaponElement.Earth,
              WeaponElement.Dark, WeaponElement.Light })
            if (controller.GetElementObject(element) != null || catalog.TryResolve(element, out _))
                throw new InvalidOperationException("Undecided/retired hit slot is active: " + element);

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                throw new InvalidOperationException("Missing script in hit prefab: " + child.name);
        Debug.Log("[FiveElementHit] Fire/Ice/Electric hit, Dark/Light fallback, retired slots PASS.");
    }
}
