using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;

public static partial class CombatBalanceGoal3Verifier
{
    static IEnumerator VerifyParryCadence()
    {
        var spawned = new List<EnemyActor>();
        var fixtures = new List<EnemyAbilitySet>();
        EnemySpawnService spawn = null;
        MeleeRuntime melee = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
            Check(Overburst.Persistence.AccountBootstrap.SaveDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Isolated account");
            var player = PlayerInputFacade.Current;
            var playerActor = PlayerContext.GetOrCreate().CurrentActor;
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(playerActor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Equip fixture");
            var entry = UnityEngine.Object.FindFirstObjectByType<DungeonDebugEntry>(FindObjectsInactive.Include);
            Check(entry != null && entry.TryEnter(1, false), "Dungeon entry");
            while (entry.IsEntering || PersistentSceneFlow.Instance.IsSwitching) yield return null;
            Check(WorldSessionState.Phase == WorldPhase.Run, "Actual dungeon");
            var world = UnityEngine.Object.FindFirstObjectByType<DiamondDungeonWorld>();
            foreach (var field in world.Fields) field.enabled = false;
            foreach (var evt in world.EventDirector.Events) evt.enabled = false;
            spawn = world.SpawnBudget.GetComponent<EnemySpawnService>();
            var ranks = new List<EnemyRank>(); EnemyRank.CollectActive(ranks);
            foreach (var rank in ranks) { var e = rank.GetComponent<EnemyActor>(); if (e != null && e.IsLeased) spawn.Release(e); }
            var ui = EnemyThemeTrialHarness.Current;
            foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), error);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null && !d.EnemyId.StartsWith("DeathHarvest_")).Distinct().ToArray();
            playerActor.Health.SetMaxHp(1000000, true);
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee = player.GetComponent<MeleeRuntime>(); melee.SetManualInputEnabled(true);
            foreach (bool elite in new[] { false, true })
            {
                var d = defs.First(x => (x.Grade.GradeType == EnemyGradeType.Elite || x.Grade.GradeType == EnemyGradeType.GreaterElite) == elite
                    && (elite || x.MovementProfile.HitWeightProfile.Weight == EnemyHitWeight.Standard)
                    && Enumerable.Range(0, x.AbilitySet.Count).Any(i => x.AbilitySet.GetAbility(i).ExecutionMode == EnemyAbilityExecutionMode.MeleeArc && !x.AbilitySet.GetAbility(i).IsTelegraphedStrongAttack));
                var a = Enumerable.Range(0, d.AbilitySet.Count).Select(i => d.AbilitySet.GetAbility(i)).First(x => x.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc && !x.IsTelegraphedStrongAttack);
                var enemy = SpawnCadence(d, a, player, spawn, Vector3.forward, fixtures); spawned.Add(enemy);
                yield return null;
                Damage(enemy, player, elite ? 9002 : 9001, 0);
                var reaction = enemy.GetComponent<EnemyMovementReaction>();
                Check(reaction.IsStunned, "Flinch entered " + d.EnemyId);
                bool started = enemy.AbilityController.TryStart(player.transform);
                Check(started == elite, "Hit-to-attack rule " + d.EnemyId);
                if (!elite)
                {
                    float limit = Time.unscaledTime + 4f;
                    while (!enemy.AbilityController.TryStart(player.transform)) { Check(Time.unscaledTime < limit, "Medium recovery/start"); yield return null; }
                }
                float start = Time.time; bool animated = false, warning = false;
                while (enemy.AbilityController.IsExecuting)
                {
                    animated |= enemy.AnimationBridge.TryGetAttackNormalizedTime(a.AnimatorTrigger, out float n) && n > .05f;
                    warning |= enemy.GetComponent<EnemyStrongAttackWarning>().IsVisible;
                    if (elite) Damage(enemy, player, 9100 + Time.frameCount, 0);
                    Check(Time.time - start < 3f, "Attack did not finish");
                    yield return null;
                }
                results.Add(new { rule = "ordinary-hit", d.EnemyId, elite, startsDuringHit = started, animated, warning, duration = Time.time - start });
                Check(animated && warning, "Missing actual motion/warning " + d.EnemyId);
                spawn.Release(enemy); spawned.Remove(enemy);
                yield return null;
            }
            // Two actual attack directions; player orientation does not gate the parry.
            var dual = defs.First(x => x.Grade.GradeType == EnemyGradeType.Elite && Enumerable.Range(0, x.AbilitySet.Count).Any(i => x.AbilitySet.GetAbility(i).ExecutionMode == EnemyAbilityExecutionMode.MeleeArc && !x.AbilitySet.GetAbility(i).IsTelegraphedStrongAttack));
            var ability = Enumerable.Range(0, dual.AbilitySet.Count).Select(i => dual.AbilitySet.GetAbility(i)).First(x => x.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc && !x.IsTelegraphedStrongAttack);
            foreach (var direction in new[] { Vector3.forward, Vector3.back }) spawned.Add(SpawnCadence(dual, ability, player, spawn, direction, fixtures));
            yield return null;
            foreach (var e in spawned) Check(e.AbilityController.TryStart(player.transform), "Dual start");
            var target = player.GetComponent<CombatTarget>();
            float wait = Time.unscaledTime + 2f;
            while (!spawned.All(e => e.AbilityController.IsParryThreatTo(target))) { Check(Time.unscaledTime < wait, "Dual actual threat geometry"); yield return null; }
            UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Output, "dungeon-warning.png"));
            yield return null; // Let the warning screenshot capture its own frame.
            var parry = player.GetComponent<PlayerParryController>(); int before = parry != null ? parry.SuccessCount : 0;
            float hp = playerActor.Health.CurrentHp;
            Check(melee.TryStartHeavyAttack(Vector3.right) == WeaponActionResult.Accepted, "Accepted heavy");
            parry = player.GetComponent<PlayerParryController>();
            Check(parry != null, "Heavy creates parry controller");
            yield return null;
            Check(parry.SuccessCount == before + 1, "One multi-parry success");
            foreach (var e in spawned)
            {
                Check(!e.AbilityController.IsExecuting && e.GetComponent<EnemyMovementReaction>().IsParryStunned, "All threats stunned");
                Check(!e.AbilityController.TryStart(player.transform), "Elite restarted while parry-stunned");
            }
            Check(playerActor.Health.CurrentHp == hp && Time.timeScale < 1f, "No damage + slow");
            UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Output, "dungeon-parry.png"));
            results.Add(new { multiParry = spawned.Count, successEvents = parry.SuccessCount - before, hpUnchanged = true, slow = Time.timeScale });
            float settle = Time.unscaledTime + 1.5f; while (Time.unscaledTime < settle) yield return null;
            Check(Mathf.Abs(Time.timeScale - 1f) < .001f, "Slow restored");
            foreach (var e in spawned) spawn.Release(e); spawned.Clear();
            melee.CancelCurrentAttackState();
            PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon();
            while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
            results.Add(new { dungeonExit = true, accountIsolated = true });
        }
        finally
        {
            melee?.CancelCurrentAttackState();
            foreach (var e in spawned) if (e != null && e.IsLeased && spawn != null) spawn.Release(e);
            foreach (var f in fixtures) UnityEngine.Object.Destroy(f);
        }
    }
    static EnemyActor SpawnCadence(EnemyDefinition definition, EnemyAbilityDefinition ability, PlayerInputFacade player,
        EnemySpawnService spawn, Vector3 direction, List<EnemyAbilitySet> fixtures)
    {
        Vector3 p = player.transform.position + direction * Mathf.Max(.9f, ability.Range * .6f);
        Check(Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out var floor, 9f, LayerMask.GetMask("Default", "Environment", "Ground")), "Spawn floor");
        Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-direction), player.transform), out var enemy), "Fixture spawn");
        enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        enemy.Health.SetMaxHp(100000f, true);
        var fixture = ScriptableObject.CreateInstance<EnemyAbilitySet>(); fixtures.Add(fixture);
        var so = new SerializedObject(fixture); so.FindProperty("abilitySetId").stringValue = "cadence-fixture";
        var arr = so.FindProperty("abilities"); arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = ability; so.ApplyModifiedPropertiesWithoutUndo();
        enemy.AbilityController.Configure(fixture, 1f, 1f);
        return enemy;
    }
}
