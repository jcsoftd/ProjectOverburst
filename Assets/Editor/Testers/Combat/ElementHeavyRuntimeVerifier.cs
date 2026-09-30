using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Drives the public heavy-attack entry and normal frame updates in an isolated-save Play session.
public static class ElementHeavyRuntimeVerifier
{
    static IEnumerator routine;
    static PlayerActorRuntime actor;
    static MeleeRuntime melee;
    static GameObject root;
    static ItemData previousWeapon;
    static bool background;
    static int frame;
    static double deadline;
    static readonly List<string> results = new List<string>();
    public static string Result { get; private set; } = "NOT_RUN";
    static object Field(string name) => typeof(MeleeRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(melee);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static string Begin()
    {
        Require(Application.isPlaying && routine == null, "idle Play required");
        background = Application.runInBackground; Application.runInBackground = true;
        results.Clear(); Result = "RUNNING"; frame = -1;
        deadline = EditorApplication.timeSinceStartup + 120;
        routine = Run(); EditorApplication.update += Tick;
        return Result;
    }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            Require(Application.isPlaying && EditorApplication.timeSinceStartup < deadline, "interrupted/timeout");
            if (!routine.MoveNext()) Finish(null);
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static IEnumerator Run()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName) yield return null;
        actor = PlayerContext.GetOrCreate().CurrentActor;
        Require(actor != null, "player missing");
        melee = actor.GetComponent<MeleeRuntime>(); previousWeapon = actor.Equipment.CurrentWeaponItem;
        var sword = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Require(sword != null, "greatsword missing");
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        while (!actor.Movement.IsGrounded) yield return null;
        root = new GameObject("ElementHeavyRuntimeVerifier");
        var targets = new CombatHealth[4]; var heavyHits = new int[4]; var secondaryHits = new int[4];
        for (int i = 0; i < targets.Length; i++)
        {
            int index = i;
            var g = new GameObject("HeavyTarget" + i); g.transform.SetParent(root.transform);
            var h = g.AddComponent<CombatHealth>(); h.SetMaxHp(100000, true);
            g.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
            var t = g.AddComponent<CombatTarget>(); t.Configure(CombatTeam.Enemy, true); t.ConfigureHurtVolume(Vector3.zero, .05f, .2f);
            g.AddComponent<ElementalStatusController>(); targets[i] = h;
            h.OnDamageResolved += (health, info, actual, lethal) =>
            {
                if (actual <= 0 || info.isDamageOverTime) return;
                if ((info.playerAttackKind & PlayerAttackKind.Heavy) != 0) heavyHits[index]++;
                else if ((info.playerAttackKind & PlayerAttackKind.Elemental) != 0) secondaryHits[index]++;
            };
        }
        foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric })
        foreach (int amount in new[] { 0, 50, 100 })
        foreach (bool prepared in new[] { false, true })
        {
            melee.CancelCurrentAttackState();
            var item = new ItemData(sword, 1, ItemGrade.Common, 1, element);
            Require(actor.Equipment.EquipWeaponItem(item), "equip " + element);
            yield return null; yield return null;
            var energy = actor.GetComponent<OverburstElementEnergy>() ?? actor.gameObject.AddComponent<OverburstElementEnergy>();
            energy.Clear(); energy.BindWeapon(item.runtimeInstanceId, element);
            for (int n = 0; n < amount / 10; n++) energy.RecordConfirmedHit(item.runtimeInstanceId, element, n + 1, 10);
            Array.Clear(heavyHits, 0, 4); Array.Clear(secondaryHits, 0, 4);
            foreach (var h in targets) { h.ResetHealth(); h.GetComponent<ElementalStatusController>().ClearAllStatuses(); }
            if (prepared)
                for (int i = 0; i < 3; i++)
                    for (int stack = 0; stack < 5; stack++)
                        targets[i].GetComponent<ElementalStatusController>().TryApplyDirectHit(new ElementalStatusApplication(
                            element, 100, actor.gameObject, item.runtimeInstanceId, true, false, targets[i].transform.position, Vector3.forward));
            Require(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "start " + element + amount);
            float radius = 1.5f + 2.5f * amount / 100f;
            double end = EditorApplication.timeSinceStartup + 12;
            do
            {
                var phases = (AttackPhaseData[])Field("activeAttackPhases");
                var stats = (WeaponFinalStats)Field("activeStats");
                var pattern = phases[0].ResolvePattern(stats.range, stats.meleeSlashAngle, sword.GetMeleeDefinition().baseSettings.hitWidth);
                var direction = (Vector3)Field("activeAttackDirection");
                Vector3 center = actor.transform.position + direction * pattern.ForwardOffset;
                float[] distances = { .1f, radius - .25f, radius + .25f, radius + 10 };
                for (int i = 0; i < 4; i++)
                {
                    targets[i].transform.position = center + Vector3.right * distances[i];
                    CombatTargetRegistry.NotifySpatialChanged(targets[i].transform);
                }
                yield return null;
                Require(EditorApplication.timeSinceStartup < end, "attack timeout");
            } while (melee.IsAttackInProgress);
            Require(heavyHits[0] == 1 && heavyHits[1] == 1 && heavyHits[2] == 0 && heavyHits[3] == 0,
                element + amount + " circular hits=" + string.Join(",", heavyHits));
            Require(energy.Amount == 0, "heavy recharged energy");
            for (int i = 0; i < 2; i++) Require(!targets[i].GetComponent<ElementalStatusController>().HasStatus(element), "heavy reapplied status");
            if (!prepared)
                foreach (int hits in secondaryHits) Require(hits == 0, "unprepared target triggered secondary");
            else if (element == WeaponElement.Ice)
            {
                Require(secondaryHits[0] == 1 && secondaryHits[1] == 1 && secondaryHits[2] == 0, "shatter hits=" + string.Join(",", secondaryHits));
                Require(targets[2].GetComponent<ElementalStatusController>().IsFrozen, "outside freeze consumed");
            }
            else
            {
                Require(secondaryHits[2] > 0 && secondaryHits[3] == 0, "chain propagation range=" + string.Join(",", secondaryHits));
                foreach (int hits in secondaryHits) Require(hits <= 2, "secondary hit cap");
                Require(targets[2].GetComponent<ElementalStatusController>().HasStatus(element) == (element == WeaponElement.Electric), "outside chain status consumption");
            }
            results.Add("PASS " + element + " E" + amount + " prepared=" + prepared + ": public heavy/frame updates, first=" + string.Join(",", heavyHits)
                + " derived=" + string.Join(",", secondaryHits) + ", consumption/energy correct");
        }
    }
    static void Finish(string error)
    {
        EditorApplication.update -= Tick; routine = null;
        if (melee != null) melee.CancelCurrentAttackState();
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
        if (actor != null)
        {
            if (previousWeapon != null) actor.Equipment.EquipWeaponItem(previousWeapon); else actor.Equipment.ClearCurrentWeapon();
            actor.GetComponent<OverburstElementEnergy>()?.Clear();
        }
        Application.runInBackground = background;
        Result = (error == null ? "PASS\n" : "FAIL " + error + "\n") + string.Join("\n", results);
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/HeavyRuntimeResult.txt"), Result);
    }
}
