using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Execute in Play Mode. Temporary objects only; never saves scenes, prefabs, or player inventory.
public static class OverburstElementFoundationVerifier
{
    private static readonly List<string> passed = new List<string>();
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }
    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Mode required");
        passed.Clear();
        GameObject root = new GameObject("ElementFoundation_Verification");
        WeaponItemData transient = ScriptableObject.CreateInstance<WeaponItemData>();
        transient.itemName = "시험검";
        UnityEngine.Random.State random = UnityEngine.Random.state;
        try
        {
            var fire = new ItemData(transient, 1, ItemGrade.Common, 1, WeaponElement.Fire);
            var ice = new ItemData(transient, 1, ItemGrade.Common, 1, WeaponElement.Ice);
            Check(fire.itemName == "시험검 (불)" && ice.itemName == "시험검 (얼음)", "instance-specific suffix");
            for (int i = 0; i < 20; i++) fire.EnsureRuntimeState();
            Check(fire.ResolvedElement == WeaponElement.Fire && transient.defaultElement == WeaponElement.None, "ensure does not reroll or mutate SO");
            string json = JsonUtility.ToJson(fire);
            ItemData restored = JsonUtility.FromJson<ItemData>(json);
            Check(restored.ResolvedElement == WeaponElement.Fire && restored.runtimeInstanceId == fire.runtimeInstanceId, "Unity serialization retains element and identity");
            Check(!OverburstElementRules.IsActive(WeaponElement.Earth) && !OverburstElementRules.IsActive(WeaponElement.Wind), "retired elements rejected");
            for (int i = 0; i < 100; i++) CheckActiveRoll(transient);
            passed.Add("100 new weapon rolls active-only");

            GameObject source = Actor(root.transform, "Source", CombatTeam.PlayerParty);
            OverburstElementEnergy energy = source.AddComponent<OverburstElementEnergy>();
            energy.BindWeapon("fire-instance", WeaponElement.Fire);
            Check(!energy.RecordConfirmedHit("fire-instance", WeaponElement.Fire, 1, 0f), "zero damage rejected");
            Check(!energy.RecordConfirmedHit("wrong-instance", WeaponElement.Fire, 1, 5f), "stale weapon rejected");
            Check(energy.RecordConfirmedHit("fire-instance", WeaponElement.Fire, 1, 5f), "first confirmed hit charges");
            for (int i = 0; i < 50; i++) energy.RecordConfirmedHit("fire-instance", WeaponElement.Fire, 1, 5f);
            Check(Mathf.Approximately(energy.Amount, OverburstElementTuning.Current.energyPerAttack), "50 targets and duplicate phases charge once");
            for (int i = 2; i <= 30; i++) energy.RecordConfirmedHit("fire-instance", WeaponElement.Fire, i, 5f);
            Check(Mathf.Approximately(energy.Normalized, 1f), "repeating arbitrary combo reaches capacity without overflow");

            GameObject target = Actor(root.transform, "Target", CombatTeam.Enemy);
            CombatHealth health = target.GetComponent<CombatHealth>();
            ElementalStatusController statuses = target.AddComponent<ElementalStatusController>();
            DamageInfo hit = new DamageInfo(5f, target.transform.position, source, element: WeaponElement.Fire,
                sourceWeaponRuntimeInstanceId: "fire-instance", sourceAttackSequenceId: 1);
            Check(statuses.ApplyConfirmedHit(hit, 5f), "first target status hit");
            Check(!statuses.ApplyConfirmedHit(hit, 5f) && statuses.GetStackCount(WeaponElement.Fire) == 1, "target duplicate blocked");
            hit.sourceAttackSequenceId = 2; hit.element = WeaponElement.Dark;
            Check(statuses.ApplyConfirmedHit(hit, 5f) && statuses.HasStatus(WeaponElement.Dark), "dark builds independent status");
            Check(energy.TryCommitDischarge(20f, out OverburstElementDischarge burst) && energy.Amount == 0f, "commit consumes energy once");
            Check(!energy.TryCommitDischarge(20f, out _), "empty energy cannot commit twice");
            Check(burst.TryResolveConfirmedHit(health, 5f, out OverburstDischargeResult prepared)
                && prepared.ConsumedStacks == 1 && prepared.BonusDamage > 0f, "prepared target gives consumed bonus");
            Check(statuses.HasStatus(WeaponElement.Dark) && !statuses.HasStatus(WeaponElement.Fire), "only matching state consumed");
            Check(!burst.TryResolveConfirmedHit(health, 5f, out _), "same target cannot consume twice");
            GameObject fresh = Actor(root.transform, "Unprepared", CombatTeam.Enemy);
            Check(burst.TryResolveConfirmedHit(fresh.GetComponent<CombatHealth>(), 5f, out OverburstDischargeResult unprepared)
                && unprepared.ConsumedStacks == 0 && unprepared.BonusDamage > 0f
                && prepared.BonusDamage > unprepared.BonusDamage, "unprepared target receives base discharge and prepared bonus is additive");
            GameObject friendly = Actor(root.transform, "Friendly", CombatTeam.PlayerParty);
            Check(!burst.TryResolveConfirmedHit(friendly.GetComponent<CombatHealth>(), 5f, out _), "friendly consumption rejected");
            GameObject lethal = Actor(root.transform, "Lethal", CombatTeam.Enemy);
            var lethalStatus = lethal.AddComponent<ElementalStatusController>();
            hit.element = WeaponElement.Fire; hit.sourceAttackSequenceId = 50;
            lethalStatus.ApplyConfirmedHit(hit, 5f);
            Check(burst.TryCaptureTarget(lethal.GetComponent<CombatHealth>(), out var lethalSnapshot), "capture state before lethal direct hit");
            lethal.GetComponent<CombatHealth>().TakeDamage(new DamageInfo(1000f, lethal.transform.position, source, triggersOnHitEffects: false));
            Check(burst.TryResolveConfirmedHit(lethalSnapshot, 100f, out var lethalResult) && lethalResult.ConsumedStacks == 1,
                "lethal direct hit retains prepared bonus snapshot");
            Check(!lethalStatus.HasStatus(WeaponElement.Fire), "death clears state");
            GameObject pooled = Actor(root.transform, "PooledBetweenCaptureAndHit", CombatTeam.Enemy);
            pooled.AddComponent<ElementalStatusController>();
            Check(burst.TryCaptureTarget(pooled.GetComponent<CombatHealth>(), out var staleSnapshot), "capture pooled target");
            pooled.SetActive(false); pooled.SetActive(true);
            Check(!burst.TryResolveConfirmedHit(staleSnapshot, 10f, out _), "pool reuse rejects old hit snapshot");
            energy.BindWeapon("ice-instance", WeaponElement.Ice);
            GameObject late = Actor(root.transform, "Late", CombatTeam.Enemy);
            Check(!burst.TryResolveConfirmedHit(late.GetComponent<CombatHealth>(), 5f, out _), "weapon change invalidates pending burst");

            statuses.ClearAllStatuses();
            hit.element = WeaponElement.Ice; hit.sourceWeaponRuntimeInstanceId = "ice-instance";
            int max = OverburstElementTuning.Current.maximumStacks;
            for (int i = 1; i <= max; i++) { hit.sourceAttackSequenceId = 100 + i; statuses.ApplyConfirmedHit(hit, 5f); }
            Check(statuses.IsFrozen && statuses.GetStackCount(WeaponElement.Ice) == max, "cold threshold freezes");
            statuses.TryGetReactionState(ElementalReactionType.Freeze, out ElementalReactionStateSnapshot frozen);
            hit.sourceAttackSequenceId = 200; statuses.ApplyConfirmedHit(hit, 5f);
            statuses.TryGetReactionState(ElementalReactionType.Freeze, out ElementalReactionStateSnapshot afterLight);
            Check(statuses.IsFrozen && afterLight.RemainingDuration <= frozen.RemainingDuration, "light hit neither shatters nor extends freeze");
            Check(statuses.ConsumeForDischarge(WeaponElement.Ice, out bool shattered) == max && shattered && !statuses.IsFrozen, "explicit discharge shatters and clears");
            hit.sourceAttackSequenceId = 201; statuses.ApplyConfirmedHit(hit, 5f);
            Check(statuses.ConsumeForDischarge(WeaponElement.Ice, out _) == 0 && statuses.GetStackCount(WeaponElement.Ice) == 1, "unfrozen cold is not shatter-consumed");
            statuses.AdvanceReactionStatesForValidation(Time.time + OverburstElementTuning.Current.statusDuration + 1f);
            Check(!statuses.HasStatus(WeaponElement.Ice), "status expires");
            hit.element = WeaponElement.Electric; hit.sourceAttackSequenceId = 202; hit.isDamageOverTime = true;
            Check(!statuses.ApplyConfirmedHit(hit, 5f), "DoT does not build state");
            hit.isDamageOverTime = false; hit.triggersOnHitEffects = false;
            Check(!statuses.ApplyConfirmedHit(hit, 5f), "derived damage does not build state");
            hit.triggersOnHitEffects = true; statuses.ApplyConfirmedHit(hit, 5f);
            health.ResetHealth();
            Check(!statuses.HasStatus(WeaponElement.Electric), "health reset clears target state");
            hit.sourceAttackSequenceId = 203; statuses.ApplyConfirmedHit(hit, 5f);
            target.SetActive(false); target.SetActive(true);
            Check(!statuses.HasStatus(WeaponElement.Electric) && statuses.MoveSpeedMultiplier == 1f, "pool reuse clears control and stacks");
            energy.RecordConfirmedHit("ice-instance", WeaponElement.Ice, 300, 5f);
            source.SetActive(false); source.SetActive(true);
            Check(energy.Amount == 0f, "player disable clears energy");
            var monsterAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/Resources/Enemies/Themes/Actors/PF_SpiderBrood_Horridomorph.prefab");
            Check(monsterAsset != null, "real monster prefab loads");
            var monster = UnityEngine.Object.Instantiate(monsterAsset, new Vector3(5000,0,5000), Quaternion.identity, root.transform);
            var realStatus = monster.GetComponent<ElementalStatusController>();
            Check(realStatus != null, "real monster status binding");
            hit.element = WeaponElement.Ice;
            for (int i = 0; i < max; i++) { hit.sourceAttackSequenceId = 500 + i; realStatus.ApplyConfirmedHit(hit, 5f); }
            var motor = monster.GetComponent<EnemyMotor>();
            var animation = monster.GetComponent<EnemyAnimationBridge>();
            Check(realStatus.IsFrozen && motor != null && motor.IsFrozen && animation != null && animation.IsFrozen, "real prefab freeze locks motor and animation");
            realStatus.ConsumeForDischarge(WeaponElement.Ice, out _);
            Check(!motor.IsFrozen && !animation.IsFrozen, "real prefab shatter restores motor and animation");
            return "PASS " + passed.Count + "\n" + string.Join("\n", passed);
        }
        finally
        {
            UnityEngine.Random.state = random;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(transient);
        }
    }
    private static void CheckActiveRoll(WeaponItemData weapon)
    { if (!OverburstElementRules.IsActive(new ItemData(weapon, 1, ItemGrade.Common).ResolvedElement)) throw new InvalidOperationException("retired/new roll"); }
    private static GameObject Actor(Transform parent, string name, CombatTeam team)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent); go.transform.position = new Vector3(5000, 0, 5000);
        go.AddComponent<CombatHealth>();
        go.AddComponent<CombatAffiliation>().Configure(team);
        go.AddComponent<CombatTarget>();
        return go;
    }
}
