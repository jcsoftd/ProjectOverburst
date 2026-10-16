using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    internal static void ValidateStrongWarningReview(JObject approved, JObject dataset)
    {
        bool legacy = (int?)approved["schemaVersion"] == 3 && (string)approved["purpose"] == "monster-danger-warning-cue-only-review";
        bool current = (int?)approved["schemaVersion"] == 4 && (string)approved["purpose"] == "monster-warning-parry-window-review"
            && (bool?)approved["parryStartsAtWarning"] == true && (bool?)approved["parryTimingLocked"] == false;
        if (!(legacy || current) || (string)approved["datasetKey"] != (string)dataset["datasetKey"]
            || !(approved["monsters"] is JArray rows) || rows.Count != 35)
            throw new ArgumentException("Current approved warning review required.");
    }

    public static string StartStrongWarningTiming(string output, string approvedPath, string datasetPath)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        var approved = JObject.Parse(File.ReadAllText(approvedPath));
        var dataset = JObject.Parse(File.ReadAllText(datasetPath));
        ValidateStrongWarningReview(approved, dataset);
        Directory.CreateDirectory(output);
        string batch = Path.Combine(output, "strong-warning-batch.json");
        File.WriteAllText(batch, new JObject { ["entries"] = new JArray(), ["verifyStrongWarningTiming"] = true,
            ["attackPolicyCases"] = 70, ["approved"] = approved, ["dataset"] = dataset }.ToString());
        return StartSavedAttackBatch(output, batch);
    }

    static IEnumerator RunStrongWarningTimingCases()
    {
        EditorApplication.LockReloadAssemblies(); AssetDatabase.DisallowAutoRefresh();
        try
        {
            Time.captureDeltaTime = 1f / 60; Time.timeScale = 2;
            var root = new GameObject("Strong warning timing services"); owned.Add(root);
            var inactive = new GameObject("Strong warning inactive pool"); inactive.transform.SetParent(root.transform, false); inactive.SetActive(false);
            var pool = root.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
            var service = root.AddComponent<EnemySpawnService>();
            var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");
            service.Configure(catalog, pool);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor);
            floor.transform.position = new Vector3(0, -.5f, 0); floor.transform.localScale = new Vector3(100, 1, 100);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
            var savedCapsule = prefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c => c.enabled && !c.isTrigger);
            var volume = prefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);
            var targetRoot = new GameObject("Strong warning player capsule"); owned.Add(targetRoot); targetRoot.layer = prefab.layer;
            var capsule = targetRoot.AddComponent<CapsuleCollider>();
            capsule.center = prefab.transform.InverseTransformPoint(savedCapsule.transform.TransformPoint(savedCapsule.center));
            capsule.radius = savedCapsule.radius; capsule.height = savedCapsule.height;
            var rigid = targetRoot.AddComponent<Rigidbody>(); rigid.isKinematic = true; rigid.useGravity = false;
            var target = targetRoot.AddComponent<CombatTarget>(); target.Configure(CombatTeam.PlayerParty, false);
            target.ConfigureVolume(volume.Center, volume.Radius, volume.HalfHeight * 2);
            var health = targetRoot.GetComponent<CombatHealth>(); health.SetMaxHp(1000000, true);
            var healthData = new SerializedObject(health); healthData.FindProperty("showDamageNumbers").boolValue = false; healthData.ApplyModifiedPropertiesWithoutUndo();
            EnemyStrongAttackWarning.PlayerTarget = target;
            var batch = JObject.Parse(File.ReadAllText(plan.attackBatch));
            var baseline = batch["dataset"]["rows"].ToDictionary(r => (string)r["id"]);
            var roster = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToDictionary(d => d.EnemyId);
            yield return null; yield return new WaitForFixedUpdate();
            foreach (var row in batch["approved"]["monsters"])
            {
                var definition = roster[(string)row["id"]]; var original = baseline[definition.EnemyId]; EnemyActor previous = null;
                foreach (float growth in new[] { 1f, 1.2f })
                {
                    EnemyActor actor = null; Action<CombatHealth, DamageInfo, float, bool> observer = null;
                    try
                    {
                        health.SetMaxHp(1000000, true); targetRoot.transform.position = Vector3.forward * 4;
                        if (!service.TrySpawn(new EnemySpawnRequest(definition, Vector3.up * .035f, Quaternion.identity, targetRoot.transform,
                            context: EncounterContext.Test), out actor)) throw new Exception("Warning spawn failed: " + definition.EnemyId);
                        foreach (var component in actor.GetComponentsInChildren<Behaviour>(true))
                            if (component != null && (component == actor.AI || new[] { "EnemyCrowdAgent", "EnemySensor" }.Contains(component.GetType().Name))) component.enabled = false;
                        actor.Movement.StopMovement(); actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        var ability = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>((string)row["abilityPath"]);
                        float speed = (float)original["authoredSpeed"] * growth;
                        actor.AbilityController.Configure(definition.AbilitySet, 1, speed, definition.ResolveRuntimeStats().AttackInterval);
                        bool reused = previous == null || previous == actor;
                        float distance = Mathf.Min(Mathf.Max(1.6f, ability.MinimumRange + .65f,
                            EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor, ability) + .65f), EnemyAttackThreatGeometry.ResolveStartRange(actor, ability) - .12f);
                        targetRoot.transform.position = actor.transform.position + Vector3.forward * distance;
                        Physics.SyncTransforms(); yield return null; yield return new WaitForFixedUpdate();
                        var damage = new JArray(); float firstDamageAt = -1;
                        observer = (victim, info, loss, dead) =>
                        {
                            if (info.enemyAbility != ability || info.source != actor.gameObject || loss <= 0) return;
                            if (firstDamageAt < 0) firstDamageAt = Time.time;
                            damage.Add(new JObject { ["strike"] = info.sourceAttackPhaseIndex, ["at"] = Time.time, ["hpLoss"] = loss });
                        };
                        health.OnDamageResolved += observer;
                        var controller = actor.AbilityController; controller.ClearPreparedAim();
                        if (!controller.TryStartAbility(ability, targetRoot.transform)) throw new Exception("Warning strong start refused: " + definition.EnemyId);
                        float deadline = Time.time + ability.ResolveExecutionDuration(speed) + 5;
                        float cueAt = -1, cuePhase = -1; bool cueEligible = false, eligibilityValid = true, parrySeen = false;
                        bool fullWindowOpen = true; int eligibleSamples = 0;
                        var warning = actor.GetComponent<EnemyStrongAttackWarning>();
                        ability.TryGetParryMotionWindow(0, out var window);
                        while (controller.IsExecuting && Time.time < deadline)
                        {
                            if (actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float phase))
                            {
                                bool eligible = controller.IsParryThreatTo(target);
                                eligibilityValid &= !eligible || phase >= window.x && phase <= window.y;
                                if (phase >= window.x && phase < window.y && damage.Count == 0)
                                { fullWindowOpen &= eligible; eligibleSamples++; }
                                parrySeen |= eligible;
                                if (cueAt < 0 && warning.ParrySignalCount > 0)
                                { cueAt = Time.time; cuePhase = phase; cueEligible = eligible; }
                            }
                            yield return null;
                        }
                        float expected = (float)row["leadBaseSeconds"] / growth;
                        float actual = firstDamageAt - cueAt;
                        float cueDelay = cuePhase < 0 ? 100 : ability.ResolvePacedTime(cuePhase, speed) - ability.ResolvePacedTime(window.x, speed);
                        bool coupled = Math.Abs(window.x - (double)row["selectedWarningNormalized"]) < .000001 && cueEligible;
                        bool pass = !controller.IsExecuting && cueAt >= 0 && firstDamageAt > cueAt && damage.Count == ability.HitCount
                            && warning.ParrySignalCount == 1 && cueDelay >= -.001f && cueDelay <= .08f
                            && Mathf.Abs(actual - expected) <= .085f && eligibilityValid && parrySeen && coupled
                            && fullWindowOpen && eligibleSamples > 0 && reused;
                        int originalCueCount = warning.ParrySignalCount;
                        JObject cooldownProof = null;
                        if ((bool?)batch["verifyMediumStrongCooldown"] == true)
                        {
                            cooldownProof = new JObject();
                            yield return VerifyMediumStrongCooldownAfterAttack(actor, ability, targetRoot.transform, distance, cooldownProof);
                            pass &= (bool)cooldownProof["pass"];
                        }
                        JObject rangeProof = null;
                        if ((bool?)batch["verifyStrongBalanceBoundary"] == true)
                        {
                            rangeProof = MonsterStrongBalanceVerifier.CheckBoundary(actor, ability, target);
                            pass &= (bool)rangeProof["pass"];
                        }
                        JObject liveBoundary = null;
                        if ((bool?)batch["verifyLiveStrongBoundaryDamage"] == true)
                        {
                            health.OnDamageResolved -= observer;
                            liveBoundary = new JObject();
                            yield return VerifyLiveStrongBoundaryDamage(actor, ability, target, distance, speed, liveBoundary);
                            pass &= (bool)liveBoundary["pass"];
                        }
                        previous = actor; controller.Cancel(); service.Release(actor);
                        yield return null; yield return new WaitForFixedUpdate();
                        bool reset = !actor.IsLeased && !actor.gameObject.activeSelf && !warning.FinalSignal
                            && EnemyStrongAttackWarning.ActiveThreatSignalCount == 0 && pool.LeasedCount == 0 && pool.PendingReturnCount == 0
                            && controller.GetRemainingCooldown(ability) == 0;
                        cases.Add(new JObject { ["id"] = definition.EnemyId, ["growth"] = growth, ["pass"] = pass && reset,
                            ["strongWarningTiming"] = true, ["expectedCueToHpLoss"] = expected, ["actualCueToHpLoss"] = actual,
                            ["cuePhase"] = cuePhase, ["selectedWarningPhase"] = window.x,
                            ["cueTimelineDelay"] = cueDelay, ["parryWindowMatchesCue"] = coupled,
                            ["parryOpenForAllPreImpactSamples"] = fullWindowOpen, ["preImpactEligibleSamples"] = eligibleSamples,
                            ["parryClosesOutsideWindow"] = eligibilityValid, ["cueCount"] = originalCueCount, ["mediumStrongCooldown"] = cooldownProof,
                            ["damageEvents"] = damage, ["rangeBoundary"] = rangeProof, ["liveBoundaryDamage"] = liveBoundary,
                            ["poolReuse"] = reused, ["poolReset"] = reset, ["animationSpeed"] = speed });
                        WriteResult("RUNNING");
                        if (!pass || !reset) throw new Exception("Strong warning timing failed: " + definition.EnemyId + "/" + growth);
                    }
                    finally
                    {
                        if (observer != null) health.OnDamageResolved -= observer;
                        if (actor != null && actor.IsLeased) { actor.AbilityController.Cancel(); service.Release(actor); }
                    }
                }
            }
        }
        finally
        {
            EnemyStrongAttackWarning.PlayerTarget = null;
            AssetDatabase.AllowAutoRefresh(); EditorApplication.UnlockReloadAssemblies();
        }
    }
}
