using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class CombatBalanceGoal3Verifier
{
    static IEnumerator VerifyGoal4()
    {
        EnemyActor current = null; EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null;
        EnemyAbilitySet fixture = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            Check(Overburst.Persistence.AccountBootstrap.SaveDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Account isolation");
            var player = PlayerInputFacade.Current;
            var playerHealth = PlayerContext.GetOrCreate().CurrentActor.Health;
            playerHealth.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current;
            if (!ui.InArena) ui.ToggleArena();
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service");
            foreach (var t in ui.tables) Check(spawn.RegisterAdditionalCatalog(t.Catalog, out string error), error);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct().ToArray();
            var samples = defs.SelectMany(d => Enumerable.Range(0, d.AbilitySet.Count).Select(i => new { d, a = d.AbilitySet.GetAbility(i) }))
                .Where(x => x.a.IsTelegraphedStrongAttack).GroupBy(x => x.a.ExecutionMode).Select(g => g.First()).ToArray();
            Check(samples.Length >= 3, "Missing strong execution samples");
            foreach (int level in new[] { 1, 100 }) foreach (var sample in samples)
            {
                var d = sample.d; var a = sample.a;
                float distance = Mathf.Lerp(a.MinimumRange, a.Range, .65f);
                var p = player.transform.position + Vector3.forward * distance;
                Check(Physics.Raycast(p + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "Floor");
                var request = new EnemySpawnRequest(d, floor.point + Vector3.up * .035f, Quaternion.LookRotation(Vector3.back), player.transform, context: new EncounterContext(null, level, ItemGrade.Common));
                Check(spawn.TrySpawn(request, out current), d.name + " spawn");
                current.AI.enabled = false; current.Movement.StopMovement(); current.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                fixture = ScriptableObject.CreateInstance<EnemyAbilitySet>();
                var so = new SerializedObject(fixture); so.FindProperty("abilitySetId").stringValue = "balance-fixture";
                var arr = so.FindProperty("abilities"); arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = a; so.ApplyModifiedPropertiesWithoutUndo();
                float speed = current.Melee.AbilityAnimationSpeed;
                current.AbilityController.Configure(fixture, current.RuntimeStats.DamageMultiplier, speed);
                float timeout = Time.time + 3;
                while (!current.AbilityController.TryStart(player.transform)) { Check(Time.time < timeout, a.name + " could not start"); yield return null; }
                float start = Time.time;
                var warning = current.GetComponent<EnemyStrongAttackWarning>();
                Check(warning != null && warning.IsVisible, a.name + " warning missing");
                float first = a.ResolveFirstImpactTime(current.Melee.AbilityAnimationSpeed);
                float actualHit = -1; int playerHits = 0;
                Action<CombatHealth, DamageInfo> record = (h, hit) => { if (hit.source == current.gameObject) { if (actualHit < 0) actualHit = Time.time - start; playerHits++; } };
                playerHealth.OnDamaged += record;
                bool finalSeen = false, protectionSeen = false, fired = false;
                try
                {
                    while (current.AbilityController.IsExecuting)
                    {
                        float elapsed = Time.time - start;
                        finalSeen |= warning.FinalSignal;
                        if (!protectionSeen && elapsed >= first - .2f && elapsed < first - .05f)
                        {
                            Check(current.AbilityController.IsOrdinaryHitProtected, a.name + " final protection missing");
                            int before = current.GetComponent<EnemyHitResponseCoordinator>().FlinchCount;
                            Damage(current, player, 4001, 0);
                            Check(current.AbilityController.IsExecuting && current.GetComponent<EnemyHitResponseCoordinator>().FlinchCount == before, a.name + " protected attack cancelled");
                            protectionSeen = true;
                        }
                        var special = current.GetComponent<EnemyThemeSpecialExecutor>();
                        if (special != null && special.LaunchCount > 0) { fired = true; Check(elapsed + .03f >= a.MinimumWarningTime, "Early projectile"); }
                        Check(elapsed < a.ResolveExecutionDuration(speed) + 4f, a.name + " execution stuck");
                        yield return null;
                    }
                }
                finally { playerHealth.OnDamaged -= record; }
                Check(finalSeen && protectionSeen, a.name + " final window not observed");
                Check(actualHit < 0 || actualHit + .03f >= a.MinimumWarningTime, a.name + " early collision");
                Check(a.ExecutionMode != EnemyAbilityExecutionMode.Projectile || fired, a.name + " no projectile");
                Check(Time.time - start + .03f >= a.ResolveExecutionDuration(speed), a.name + " recovery shortened");
                results.Add(new { a.AbilityId, level, speed, first, actualHit, playerHits, finalSeen, protectionSeen, duration = Time.time - start, fired });
                spawn.Release(current); current = null; UnityEngine.Object.Destroy(fixture); fixture = null; yield return null;
            }
        }
        finally { if (current != null && current.IsLeased && spawn != null) spawn.Release(current); if (fixture != null) UnityEngine.Object.Destroy(fixture); if (ui != null && ui.InArena) ui.ToggleArena(); }
    }
}
