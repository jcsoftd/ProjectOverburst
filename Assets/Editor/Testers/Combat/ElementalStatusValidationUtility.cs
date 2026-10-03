using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ElementalStatusValidationUtility
{
    [MenuItem("OVERBURST/Codex/Validate/Five Element Status Runtime")]
    public static void ValidateFromMenu() => RunFromCommandLine();

    public static void RunFromCommandLine()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Five Element Status validation requires Edit Mode.");

        int[] schedulerDriversBefore = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
            .Where(behaviour => behaviour != null && behaviour.GetType().DeclaringType == typeof(ElementalStatusScheduler))
            .Select(behaviour => behaviour.GetInstanceID()).ToArray();
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        GameObject source = null;
        ElementalStatusController status = null;
        try
        {
            root = new GameObject("FiveElementStatusValidation");
            source = new GameObject("FiveElementStatusValidationSource");
            SceneManager.MoveGameObjectToScene(root, preview);
            SceneManager.MoveGameObjectToScene(source, preview);
            CombatHealth health = root.AddComponent<CombatHealth>();
            health.ResetHealth();
            status = root.AddComponent<ElementalStatusController>();
            var serialized = new SerializedObject(status);
            serialized.FindProperty("combatHealth").objectReferenceValue = health;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Apply(status, WeaponElement.Fire, source);
            if (!status.HasStatus(WeaponElement.Fire))
                throw new InvalidOperationException("Fire status did not apply.");
            Apply(status, WeaponElement.Dark, source);
            if (!status.TryGetStatus(WeaponElement.Dark, out ElementalStatusSnapshot corrosion)
                || corrosion.StackCount != 1)
                throw new InvalidOperationException("Dark corrosion status did not apply.");
            if (TryApply(status, WeaponElement.Light, source) || status.HasStatus(WeaponElement.Light)
                || status.GetStackCount(WeaponElement.Light) != 0)
                throw new InvalidOperationException("Light applied an enemy status.");
            if (status.GetStackCount(WeaponElement.Dark) != 1 || !status.HasStatus(WeaponElement.Fire))
                throw new InvalidOperationException("Light rejection changed existing enemy statuses.");
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
            Apply(status, WeaponElement.Fire, source);
            if (status.ConsumeForDischarge(WeaponElement.Dark, out _) != 1
                || status.HasStatus(WeaponElement.Dark) || !status.HasStatus(WeaponElement.Fire)
                || status.HasStatus(WeaponElement.Light))
                throw new InvalidOperationException("Dark consumption changed another element's status.");
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
            status.AdvanceReactionStatesForValidation(Time.time + OverburstElementTuning.Current.StatusDuration(WeaponElement.Fire) + 1f);
            if (expiredEvents != 1 || expiredElement != WeaponElement.Fire)
                throw new InvalidOperationException("Expiry emitted removal for an inactive element.");
            Apply(status, WeaponElement.Electric, source);
            if (!status.HasStatus(WeaponElement.Electric))
                throw new InvalidOperationException("Electric status did not apply.");

            status.ClearAllStatuses();
            DamageInfo confirmedHit = new DamageInfo(100f, status.transform.position, source,
                element: WeaponElement.Dark, sourceWeaponRuntimeInstanceId: "validation", sourceAttackSequenceId: 1);
            if (!status.ApplyConfirmedHit(confirmedHit, 100f) || status.GetStackCount(WeaponElement.Dark) != 1)
                throw new InvalidOperationException("Confirmed Dark hit did not apply corrosion.");
            confirmedHit.element = WeaponElement.Light;
            confirmedHit.sourceAttackSequenceId = 2;
            if (status.ApplyConfirmedHit(confirmedHit, 100f) || status.HasStatus(WeaponElement.Light)
                || status.GetStackCount(WeaponElement.Dark) != 1)
                throw new InvalidOperationException("Confirmed Light hit changed enemy statuses.");

            Debug.Log("[FiveElementStatus] Fire/Ice/Electric/Dark apply, Light/Water reject, direct/confirmed hits, freeze, consumption, expiry and clear PASS.");
        }
        finally
        {
            try
            {
                if (status != null) status.ClearAllStatuses();
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
                EditorSceneManager.ClosePreviewScene(preview);
                foreach (MonoBehaviour driver in Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                    .Where(behaviour => behaviour != null && behaviour.GetType().DeclaringType == typeof(ElementalStatusScheduler)
                        && !schedulerDriversBefore.Contains(behaviour.GetInstanceID())))
                    UnityEngine.Object.DestroyImmediate(driver.gameObject);
            }
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
