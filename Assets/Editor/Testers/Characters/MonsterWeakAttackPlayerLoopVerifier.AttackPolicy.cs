using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    static EnemyDefinition[] AttackPolicyRoster()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");
        if (catalog == null || !catalog.Validate(out _)) throw new InvalidOperationException("Invalid saved roster.");
        return Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToArray();
    }
    static bool PolicyElite(EnemyDefinition d) => d.Grade.GradeType == EnemyGradeType.Elite || d.Grade.GradeType == EnemyGradeType.GreaterElite;
    static bool PolicyMedium(EnemyDefinition d) => !PolicyElite(d) && d.Grade.GradeType != EnemyGradeType.Boss
        && d.MovementProfile.HitWeightProfile.Weight == EnemyHitWeight.Standard;

    public static string StartAttackPolicy(string output)
    {
        Directory.CreateDirectory(output);
        var selected = AttackPolicyRoster().Where(d => PolicyElite(d) || PolicyMedium(d)).Concat(
            AttackPolicyRoster().Where(d => !PolicyElite(d) && !PolicyMedium(d)).Take(2)).ToArray();
        string batch = Path.Combine(output, "attack-policy-batch.json");
        File.WriteAllText(batch, new JObject { ["entries"] = new JArray(), ["verifyAttackPolicy"] = true,
            ["attackPolicyCases"] = selected.Length * 2, ["definitions"] = JArray.FromObject(selected.Select(AssetDatabase.GetAssetPath)) }.ToString());
        return StartSavedAttackBatch(output, batch);
    }

    public static string VerifyAttackPolicyNative(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required.");
        var baseline = JObject.Parse(File.ReadAllText(Path.Combine(output, "baseline.json")));
        var checks = new JArray(); var samples = new JArray();
        void Check(bool pass, string label) { checks.Add(new JObject { ["pass"] = pass, ["name"] = label }); }
        var definitions = AttackPolicyRoster();
        Check(definitions.Length == 60 && definitions.Count(PolicyElite) == 14 && definitions.Count(PolicyMedium) == 23, "saved 60-species / 14 elite / 23 medium roster");
        foreach (var d in definitions)
        {
            var row = baseline["rows"].Single(r => (string)r["id"] == d.EnemyId);
            Check(Mathf.Abs(d.ResolveRuntimeStats().AttackInterval - (PolicyElite(d) ? 2f : (float)row["interval"])) < .0001f, d.EnemyId + ": saved interval");
            Check(d.AbilitySet.Count == row["abilities"].Count(), d.EnemyId + ": attack assets preserved");
            for (int i = 0; i < d.AbilitySet.Count; i++)
            {
                var a = d.AbilitySet.GetAbility(i); var old = row["abilities"][i];
                Check(a.IsValid && AssetDatabase.GetAssetPath(a) == (string)old["path"], d.EnemyId + ": saved attack identity " + i);
                Check(Mathf.Abs(a.Cooldown - (float)old["cooldown"] * (PolicyElite(d) && a.IsTelegraphedStrongAttack ? 1.25f : 1f)) < .0001f,
                    a.name + ": saved cooldown");
            }
        }
        var scene = EditorSceneManager.NewPreviewScene(); var temporary = new List<Object>();
        var random = UnityEngine.Random.state;
        try
        {
            var go = new GameObject("Attack policy contract fixture"); temporary.Add(go); SceneManager.MoveGameObjectToScene(go, scene);
            var rank = go.AddComponent<EnemyRank>();
            var movement = go.AddComponent<EnemyMovement>();
            var reaction = go.GetComponent<EnemyMovementReaction>() ?? go.AddComponent<EnemyMovementReaction>();
            var reactionData = new SerializedObject(reaction);
            reactionData.FindProperty("rank").objectReferenceValue = rank;
            reactionData.FindProperty("movement").objectReferenceValue = movement; reactionData.ApplyModifiedPropertiesWithoutUndo();
            var executor = go.AddComponent<MonsterWeakAttackSelectionFixtureExecutor>();
            var controller = go.AddComponent<EnemyAbilityController>();
            var binding = new SerializedObject(controller); var executors = binding.FindProperty("executors");
            executors.arraySize = 1; executors.GetArrayElementAtIndex(0).objectReferenceValue = executor; binding.ApplyModifiedPropertiesWithoutUndo();
            var target = new GameObject("Attack policy target"); temporary.Add(target); SceneManager.MoveGameObjectToScene(target, scene);
            target.transform.position = Vector3.forward * .5f;
            var set = ScriptableObject.CreateInstance<EnemyAbilitySet>(); temporary.Add(set);
            EnemyAbilityDefinition Make(string id, bool strong, int priority)
            {
                var a = ScriptableObject.CreateInstance<EnemyAbilityDefinition>(); temporary.Add(a);
                a.Configure(id, "Attack1", 10, 10, 1, 360, 1, .4f, .4f, 1, 1, false, animationDuration: 1);
                a.ConfigureUsePolicy(0, priority);
                var so = new SerializedObject(a); so.FindProperty("telegraphedStrongAttack").boolValue = strong; so.ApplyModifiedPropertiesWithoutUndo(); return a;
            }
            var strong = Make("fixture_strong", true, -100);
            var weak = Enumerable.Range(0, 7).Select(i => Make("fixture_weak_" + i, false, 100 + i)).ToArray();
            var select = typeof(EnemyAbilityController).GetMethod("TrySelectAbility", Private);
            var clear = typeof(EnemyAbilityController).GetMethod("ClearAttackChoice", Private);
            var cooldowns = (Dictionary<EnemyAbilityDefinition, float>)typeof(EnemyAbilityController).GetField("readyTimeByAbility", Private).GetValue(controller);
            EnemyAbilityDefinition Select()
            {
                var args = new object[] { target.transform, null };
                return (bool)select.Invoke(controller, args) ? (EnemyAbilityDefinition)args[1].GetType().GetProperty("Ability").GetValue(args[1]) : null;
            }
            void Configure(EnemyDefinition d, int count)
            {
                rank.ConfigureFromDefinition(d); movement.SetProfile(d.MovementProfile);
                set.Configure("attack_policy_fixture", weak.Take(count).Concat(new[] { strong }).ToArray());
                controller.Configure(set, 1, 1, d.ResolveRuntimeStats().AttackInterval); executor.Blocked.Clear();
            }
            foreach (var d in definitions.Where(PolicyElite))
            {
                Configure(d, 7);
                Check(!controller.IsStrongAttackLocked && Select() == strong, d.EnemyId + ": first attack strong despite weak priority/count");
                Check(controller.TryStartAbility(weak[0], target.transform) == false, d.EnemyId + ": explicit weak rejected");
                cooldowns[strong] = Time.time + 100;
                Check(Select() == null && !controller.HasAvailableAbility(target.transform), d.EnemyId + ": cooldown cannot fall back to weak");
                Check(!controller.TryGetRangedPositioningAbility(out _), d.EnemyId + ": no disabled ranged positioning");
            }
            UnityEngine.Random.InitState(170316);
            foreach (var d in definitions.Where(PolicyMedium))
            {
                foreach (int count in new[] { 1, 3, 7 })
                {
                    Configure(d, count); int strongCount = 0; const int n = 3000;
                    for (int i = 0; i < n; i++) { clear.Invoke(controller, null); if (Select() == strong) strongCount++; }
                    float ratio = strongCount / (float)n;
                    Check(ratio > .27f && ratio < .33f && !controller.IsStrongAttackLocked, d.EnemyId + ": 70/30 independent of weak count/priority " + count);
                    samples.Add(new JObject { ["id"] = d.EnemyId, ["weakCount"] = count, ["draws"] = n, ["strong"] = strongCount, ["ratio"] = ratio });
                }
            }
            var medium = definitions.First(PolicyMedium); Configure(medium, 3);
            cooldowns[strong] = Time.time + 100; Check(Select() != strong, "medium strong cooldown falls back to weak"); cooldowns.Clear();
            foreach (var a in weak) executor.Blocked.Add(a);
            clear.Invoke(controller, null); Check(Select() == strong, "only eligible strong remains usable"); executor.Blocked.Clear();
            for (int i = 0; i < 100; i++)
            {
                clear.Invoke(controller, null); if (Select() != strong) break;
            }
            controller.BeginStrongOnlyPass();
            try
            {
                var state = UnityEngine.Random.state;
                Check(Enumerable.Range(0, 100).All(_ => Select() == null && !controller.HasAvailableAbility(target.transform)), "weak draw survives 100 hit-stun retries");
                Check(state.Equals(UnityEngine.Random.state), "hit-stun retries and availability never reroll weak draw");
            }
            finally { controller.EndStrongOnlyPass(); }
            Check(Select() != null && Select() != strong, "weak draw resumes after hit-stun");
            controller.ClearTarget();
            Check(typeof(EnemyAbilityController).GetField("mediumStrongChoice", Private).GetValue(controller) == null, "target loss clears pending choice");
            controller.ResetForReuse(); Check(controller.RemainingAttackInterval == 0, "pool reuse clears attack interval");
        }
        finally
        {
            UnityEngine.Random.state = random;
            for (int i = temporary.Count - 1; i >= 0; i--) if (temporary[i] != null) Object.DestroyImmediate(temporary[i]);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        int failed = checks.Count(c => !(bool)c["pass"]);
        string path = Path.Combine(output, "native-results.json");
        File.WriteAllText(path, new JObject { ["status"] = failed == 0 ? "PASS_SCOPED" : "FAIL", ["failed"] = failed, ["checks"] = checks, ["selectionSamples"] = samples }.ToString());
        if (failed != 0) throw new InvalidOperationException("Attack policy native failures: " + failed);
        return path;
    }

    static IEnumerator RunAttackPolicyCases()
    {
        EditorApplication.LockReloadAssemblies(); AssetDatabase.DisallowAutoRefresh();
        try
        {
        Time.captureDeltaTime = 1f / 30; Time.timeScale = 2;
        var serviceRoot = new GameObject("Attack policy spawn services"); owned.Add(serviceRoot);
        var poolRoot = new GameObject("Attack policy inactive pool"); poolRoot.transform.SetParent(serviceRoot.transform, false); poolRoot.SetActive(false);
        var pool = serviceRoot.AddComponent<EnemyPoolService>(); pool.Configure(poolRoot.transform, 0);
        var service = serviceRoot.AddComponent<EnemySpawnService>();
        service.Configure(AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset"), pool);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = new Vector3(0, -.5f, 0); floor.transform.localScale = new Vector3(100, 1, 100);
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var playerBody = playerPrefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c => c.enabled && !c.isTrigger);
        var volume = playerPrefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);
        var targetRoot = new GameObject("Attack policy player capsule"); owned.Add(targetRoot); targetRoot.layer = playerPrefab.layer;
        var capsule = targetRoot.AddComponent<CapsuleCollider>(); capsule.center = playerPrefab.transform.InverseTransformPoint(playerBody.transform.TransformPoint(playerBody.center)); capsule.radius = playerBody.radius; capsule.height = playerBody.height;
        var body = targetRoot.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        var target = targetRoot.AddComponent<CombatTarget>(); target.Configure(CombatTeam.PlayerParty, false);
        target.ConfigureVolume(volume.Center, volume.Radius, volume.HalfHeight * 2);
        var health = targetRoot.GetComponent<CombatHealth>(); health.SetMaxHp(1000000, true);
        var hpSettings = new SerializedObject(health); hpSettings.FindProperty("showDamageNumbers").boolValue = false; hpSettings.ApplyModifiedPropertiesWithoutUndo();
        int damageEvents = 0; health.OnDamaged += (_, info) => { if (info.damage > 0) damageEvents++; };
        EnemyStrongAttackWarning.PlayerTarget = target;
        yield return null; yield return new WaitForFixedUpdate();
        var batch = JObject.Parse(File.ReadAllText(plan.attackBatch));
        foreach (var path in batch["definitions"].Values<string>())
        {
            var d = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path); EnemyActor previous = null;
            for (int lease = 0; lease < 2; lease++)
            {
                damageEvents = 0; health.SetMaxHp(1000000, true); targetRoot.transform.position = Vector3.forward * 4.5f;
                var request = new EnemySpawnRequest(d, Vector3.zero, Quaternion.identity, targetRoot.transform, null, targetRoot.transform, null, 1, 1, 71 + lease);
                if (!service.TrySpawn(request, out var actor)) throw new Exception("Policy spawn failed: " + d.EnemyId);
                actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                bool fresh = !actor.AbilityController.IsExecuting && actor.AbilityController.RemainingAttackInterval == 0 && (lease == 0 || actor == previous);
                var records = new JArray(); float lastCommit = -1, endedAt = -1; bool wasExecuting = false;
                bool gaps = true, cooldowns = true, damage = false, weakRejected = true; var readyByAbility = new Dictionary<EnemyAbilityDefinition, float>();
                int wanted = PolicyElite(d) ? 2 : PolicyMedium(d) && d.EnemyId == "CavernMutants_Gasterobrach" ? 5 : 2;
                float startedAt = Time.time, nextHit = Time.time, deadline = Time.time + 65; int hitsApplied = 0;
                actor.AI.RequestAggro(targetRoot.transform);
                while (Time.time < deadline)
                {
                    var ac = actor.AbilityController;
                    if (d.EnemyId == "CavernMutants_Gasterobrach" && records.Count > 0 && Time.time < startedAt + 8 && Time.time >= nextHit)
                    {
                        nextHit = Time.time + .2f; hitsApplied++;
                        actor.Health.TakeDamage(new DamageInfo(1, actor.transform.position, targetRoot, Vector3.back, 0,
                            sourceAttackSequenceId: 900000 + hitsApplied, playerAttackKind: PlayerAttackKind.Weak,
                            sourceAttackPhaseIndex: 0, weakKnockbackDistance: 0));
                    }
                    if (PolicyElite(d))
                    {
                        for (int i = 0; i < d.AbilitySet.Count; i++)
                        {
                            var a = d.AbilitySet.GetAbility(i);
                            if (!a.IsTelegraphedStrongAttack) weakRejected &= !ac.TryStartAbility(a, targetRoot.transform);
                        }
                    }
                    bool executing = ac.IsExecuting;
                    if (!executing && wasExecuting) endedAt = Time.time;
                    float committed = (float)typeof(EnemyAbilityController).GetField("lastCommittedAt", Private).GetValue(ac);
                    if (ac.LastCommittedAbility != null && committed != lastCommit)
                    {
                        var a = ac.LastCommittedAbility; float gap = endedAt < 0 ? -1 : Time.time - endedAt;
                        gaps &= endedAt < 0 || gap + .08f >= d.ResolveRuntimeStats().AttackInterval;
                        cooldowns &= !readyByAbility.TryGetValue(a, out float ready) || Time.time + .08f >= ready;
                        readyByAbility[a] = committed + Mathf.Max(a.Cooldown, a.ResolveExecutionDuration(actor.Melee.AbilityAnimationSpeed));
                        records.Add(new JObject { ["ability"] = a.name, ["strong"] = a.IsTelegraphedStrongAttack, ["t"] = committed, ["afterPreviousEnd"] = gap, ["cooldown"] = a.Cooldown });
                        lastCommit = committed;
                    }
                    wasExecuting = executing; damage |= damageEvents > 0;
                    if (records.Count >= wanted && !executing) break;
                    yield return null;
                }
                bool eliteOnly = !PolicyElite(d) || records.All(r => (bool)r["strong"]);
                bool smallOnly = PolicyElite(d) || PolicyMedium(d) || records.All(r => !(bool)r["strong"]);
                bool noStrongDefinition = !Enumerable.Range(0, d.AbilitySet.Count).Any(i => d.AbilitySet.GetAbility(i).IsTelegraphedStrongAttack);
                bool exceptionKept = !noStrongDefinition || records.All(r => !(bool)r["strong"]);
                previous = actor; service.Release(actor); yield return null; yield return new WaitForFixedUpdate();
                bool reset = !actor.IsLeased && !actor.gameObject.activeSelf && !actor.AbilityController.IsExecuting && actor.AbilityController.RemainingAttackInterval == 0
                    && pool.LeasedCount == 0 && pool.PendingReturnCount == 0;
                bool hitRoute = d.EnemyId != "CavernMutants_Gasterobrach" || hitsApplied > 0;
                bool pass = fresh && records.Count >= wanted && gaps && cooldowns && eliteOnly && smallOnly && exceptionKept && weakRejected && damage && reset && hitRoute;
                cases.Add(new JObject { ["id"] = d.EnemyId, ["lease"] = lease, ["attackPolicy"] = true, ["pass"] = pass, ["fresh"] = fresh,
                    ["attacks"] = records, ["requiredAttacks"] = wanted, ["intervalsValid"] = gaps, ["cooldownsValid"] = cooldowns, ["eliteStrongOnly"] = eliteOnly,
                    ["smallWeakOnly"] = smallOnly, ["noStrongExceptionPreserved"] = exceptionKept, ["explicitWeakRejected"] = weakRejected, ["actualDamage"] = damage, ["poolReset"] = reset,
                    ["playerWeakHitsApplied"] = hitsApplied, ["hitRouteCovered"] = hitRoute });
                WriteResult("RUNNING");
                if (!pass) throw new Exception("Policy Play case failed: " + d.EnemyId + "/" + lease);
            }
        }
        EnemyStrongAttackWarning.PlayerTarget = null;
        }
        finally
        {
            EnemyStrongAttackWarning.PlayerTarget = null;
            AssetDatabase.AllowAutoRefresh(); EditorApplication.UnlockReloadAssemblies();
        }
    }
}
