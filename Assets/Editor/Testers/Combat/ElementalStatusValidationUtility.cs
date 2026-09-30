using System;
using UnityEditor;
using UnityEngine;

public static class ElementalStatusValidationUtility
{
    [MenuItem("OVERBURST/Codex/Validate/Five Element Status Runtime")]
    public static void ValidateFromMenu() => RunFromCommandLine();

    public static void RunFromCommandLine()
    {
        GameObject root = new GameObject("FiveElementStatusValidation");
        GameObject source = new GameObject("FiveElementStatusValidationSource");
        try
        {
            CombatHealth health = root.AddComponent<CombatHealth>();
            health.ResetHealth();
            ElementalStatusController status = root.AddComponent<ElementalStatusController>();
            var serialized = new SerializedObject(status);
            serialized.FindProperty("combatHealth").objectReferenceValue = health;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Apply(status, WeaponElement.Fire, source);
            if (!status.HasStatus(WeaponElement.Fire))
                throw new InvalidOperationException("Fire status did not apply.");
            foreach (WeaponElement active in new[] { WeaponElement.Dark, WeaponElement.Light })
            {
                Apply(status, active, source);
                if (!status.HasStatus(active))
                    throw new InvalidOperationException("Active status missing: " + active);
            }
            if (TryApply(status, WeaponElement.Water, source) || status.HasStatus(WeaponElement.Water))
                throw new InvalidOperationException("Retired Water status applied.");

            for (int i = 0; i < OverburstElementTuning.Current.maximumStacks; i++)
                Apply(status, WeaponElement.Ice, source);
            if (!status.IsFrozen || status.MoveSpeedMultiplier != 0f)
                throw new InvalidOperationException("Ice freeze threshold failed.");
            status.ClearAllStatuses(ElementalStatusClearReason.Explicit);
            if (status.HasStatus(WeaponElement.Fire) || status.HasStatus(WeaponElement.Ice)
                || status.HasStatus(WeaponElement.Dark) || status.HasStatus(WeaponElement.Light)
                || status.MoveSpeedMultiplier != 1f)
                throw new InvalidOperationException("Status cleanup failed.");
            Apply(status, WeaponElement.Dark, source);
            Apply(status, WeaponElement.Light, source);
            if (status.ConsumeForDischarge(WeaponElement.Dark, out _) != 1
                || status.HasStatus(WeaponElement.Dark) || !status.HasStatus(WeaponElement.Light))
                throw new InvalidOperationException("Dark consumption affected Light.");
            status.ClearAllStatuses();
            int expiredEvents = 0;
            WeaponElement expiredElement = WeaponElement.None;
            status.StatusRemoved += (element, reason) =>
            {
                if (reason != ElementalStatusRemoveReason.Expired) return;
                expiredEvents++;
                expiredElement = element;
            };
            Apply(status, WeaponElement.Fire, source);
            status.AdvanceReactionStatesForValidation(Time.time + OverburstElementTuning.Current.statusDuration + 1f);
            if (expiredEvents != 1 || expiredElement != WeaponElement.Fire)
                throw new InvalidOperationException("Expiry emitted removal for an inactive element.");
            Apply(status, WeaponElement.Electric, source);
            if (!status.HasStatus(WeaponElement.Electric))
                throw new InvalidOperationException("Electric status did not apply.");
            Debug.Log("[FiveElementStatus] Five active elements apply, Water rejects, freeze and clear PASS.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(source);
            ElementalStatusScheduler.ClearForValidation();
        }
    }

    private static void Apply(ElementalStatusController status, WeaponElement element, GameObject source)
    {
        if (!TryApply(status, element, source))
            throw new InvalidOperationException("Status did not apply: " + element);
    }

    private static bool TryApply(ElementalStatusController status, WeaponElement element, GameObject source) =>
        status.TryApplyDirectHit(new ElementalStatusApplication(
            element, 100f, source, "validation", true, false,
            status.transform.position, Vector3.forward));
}
