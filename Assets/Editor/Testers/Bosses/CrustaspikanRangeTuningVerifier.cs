using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Verifies real collision/damage at the reduced boundary without generating a temporary scene asset.
[InitializeOnLoad]
public static class CrustaspikanRangeTuningVerifier
{
    const string Key = "Overburst.CrustaspikanRangeTuningVerifier.plan";
    sealed class Plan { public string output, phase, previousStart; public JArray scenes; public bool background; public float capture, timeScale; public double deadline; }
    static Plan plan;
    static readonly List<Object> owned = new List<Object>();
    static readonly List<EnemyActor> leases = new List<EnemyActor>();
    static readonly JArray cases = new JArray();
    static EnemySpawnService service;
    static EnemyMotor host;
    static Coroutine routine;
    static CombatTarget previousPlayerTarget;
    static readonly Vector3 Origin = new Vector3(1600, .035f, 1600);
    static string Account => Path.Combine(plan.output, "Account");
    static bool OwnPlay => plan != null && string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);
    static JArray Scenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount }));
    static void Require(bool pass, string error) { if (!pass) throw new InvalidOperationException(error); }
    static void Persist() => SessionState.SetString(Key, JsonConvert.SerializeObject(plan));
    static CrustaspikanRangeTuningVerifier()
    {
        string saved = SessionState.GetString(Key, ""); if (!string.IsNullOrEmpty(saved)) plan = JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update += Tick;
    }
    public static string Start(string output)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        Require(plan == null && !EditorUtility.scriptCompilationFailed, "A verifier is pending or compilation failed.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Require(!Directory.Exists(output), "Fresh private evidence directory required."); Directory.CreateDirectory(output);
        plan = new Plan { output = output, phase = "booting", scenes = Scenes(), previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            background = Application.runInBackground, capture = Time.captureDeltaTime, timeScale = Time.timeScale, deadline = EditorApplication.timeSinceStartup + 600 };
        cases.Clear(); Persist(); File.WriteAllText(Path.Combine(output, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        Application.runInBackground = true; IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return "Started isolated range collision and damage checks.";
    }
    static void Tick()
    {
        if (plan == null) return;
        if (plan.phase == "returning") { Return(); return; }
        if (EditorApplication.timeSinceStartup > plan.deadline) { Finish("Range verification timed out."); return; }
        if (plan.phase != "booting" || !EditorApplication.isPlaying || !OwnPlay) return;
        plan.phase = "running"; Persist(); previousPlayerTarget = EnemyStrongAttackWarning.PlayerTarget;
        var root = new GameObject("Owned boss range verifier"); owned.Add(root); host = root.AddComponent<EnemyMotor>();
        root.GetComponent<Rigidbody>().isKinematic = true; routine = host.StartCoroutine(Drive(Run()));
    }
    static IEnumerator Drive(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(body);
        while (stack.Count > 0)
        {
            bool more; object current;
            try { more = stack.Peek().MoveNext(); current = more ? stack.Peek().Current : null; }
            catch (Exception error) { foreach (var item in stack) (item as IDisposable)?.Dispose(); Finish(error.ToString()); yield break; }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        Finish(null);
    }
    static void Position(Transform target, Vector3 point) { target.position = point; Physics.SyncTransforms(); }
    static void Record(JObject row) { row["pass"] = true; cases.Add(row); Write("RUNNING", null); }
    static void Write(string status, string error) => File.WriteAllText(Path.Combine(plan.output, "result.json"), new JObject { ["status"] = status, ["failure"] = error, ["cases"] = cases }.ToString());
    static IEnumerator Run()
    {
        float wait = Time.realtimeSinceStartup + 120f;
        while (Time.realtimeSinceStartup < wait && (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout || PlayerContext.GetOrCreate().CurrentActor == null)) yield return null;
        Require(Time.realtimeSinceStartup < wait, "Isolated hideout boot timed out.");
        Require(string.Equals(Overburst.Persistence.AccountBootstrap.SaveDirectory, Account, StringComparison.OrdinalIgnoreCase), "Account differs.");
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        service = EnemySpawnService.Current;
        if (service == null)
        {
            var root = new GameObject("Owned range spawn service"); owned.Add(root);
            var inactive = new GameObject("Owned inactive actors"); inactive.transform.SetParent(root.transform, false); inactive.SetActive(false);
            var pool = root.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
            service = root.AddComponent<EnemySpawnService>(); service.Configure(collection.catalog, pool);
        }
        Require(service.RegisterAdditionalCatalog(collection.catalog, out var reason), reason);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Origin + Vector3.down * .535f; floor.transform.localScale = new Vector3(100, 1, 100);
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var physical = playerPrefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c => c.enabled && !c.isTrigger);
        var victim = new GameObject("Owned saved player physical body"); owned.Add(victim); victim.layer = playerPrefab.layer;
        var capsule = victim.AddComponent<CapsuleCollider>(); capsule.center = playerPrefab.transform.InverseTransformPoint(physical.transform.TransformPoint(physical.center));
        capsule.radius = physical.radius * Mathf.Max(Mathf.Abs(physical.transform.lossyScale.x), Mathf.Abs(physical.transform.lossyScale.z)); capsule.height = physical.height * Mathf.Abs(physical.transform.lossyScale.y);
        var target = victim.AddComponent<CombatTarget>(); target.Configure(CombatTeam.PlayerParty, false);
        var volume = playerPrefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero); target.ConfigureVolume(volume.Center, volume.Radius, volume.HalfHeight * 2);
        var health = victim.GetComponent<CombatHealth>(); health.SetMaxHp(100000, true); EnemyStrongAttackWarning.PlayerTarget = target;
        var oldRows = JArray.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(plan.output), "geometry-comparison.json"))).ToDictionary(r => (string)r["id"]);
        var probeWarning = typeof(CrustaspikanMaterialVerifier).GetMethod("ProbeWarning", BindingFlags.Static | BindingFlags.NonPublic);
        Time.timeScale = 1f; Time.captureDeltaTime = 0f;
        foreach (var material in collection.attacks.Where(m => m.delivery == EnemyBossMaterialDelivery.Melee))
        foreach (string scenario in new[] { "inside", "removed-outer-band" })
        {
            Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition, Origin, Quaternion.identity, target.transform, context: EncounterContext.Test), out var actor), "Saved actor spawn failed.");
            leases.Add(actor); actor.AI.enabled = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var c in actor.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(c, capsule);
            var executor = actor.GetComponent<EnemyBossMaterialExecutor>(); var damage = new JArray(); var warnings = new JArray();
            Action<CombatHealth, DamageInfo> observer = (_, info) => damage.Add(new JObject { ["phase"] = info.sourceAttackPhaseIndex, ["damage"] = info.damage });
            health.OnDamaged += observer;
            try
            {
                Action<int> place = phase =>
                {
                    var s = material.strikes[phase]; float distance = scenario == "inside" ? Mathf.Lerp(s.innerRadius, s.radius, .65f) : ((float)oldRows[material.runtimeClip.name]["before"]["strikes"][phase]["radius"] + s.radius) * .5f;
                    Position(victim.transform, s.Origin(actor.transform) + s.Rotation(actor.transform) * Vector3.forward * distance);
                    Require(s.Intersects(capsule, actor.transform) == (scenario == "inside"), "Actual body geometry differs from scenario.");
                };
                place(0);
                var first = material.strikes[0];
                Position(victim.transform, first.Origin(actor.transform) + first.Rotation(actor.transform) * Vector3.forward * Mathf.Lerp(first.innerRadius, first.radius, .65f));
                yield return new WaitForFixedUpdate();
                Require(actor.AbilityController.TryStartAbility(material.ability, target.transform, EnemyAbilityStartContext.RearCounter(target.transform.position, actor.Movement.PhysicalRotation)), "Attack start failed: " + material.name);
                place(0);
                float deadline = Time.realtimeSinceStartup + material.runtimeClip.length * 3f + 10f;
                while (actor.AbilityController.IsExecuting && Time.realtimeSinceStartup < deadline)
                {
                    int phase = 0; while (phase < material.strikes.Length - 1 && executor.NormalizedTime > material.strikes[phase].contactEnd) phase++;
                    place(phase);
                    if (!warnings.Any(w => (int)w["phase"] == phase) && executor.NormalizedTime < material.strikes[phase].impact)
                        probeWarning.Invoke(null, new object[] { executor, material, phase, warnings });
                    yield return null;
                }
                Require(!actor.AbilityController.IsExecuting, "Attack deadline exceeded: " + material.name);
                Require(executor.ImpactCount == material.strikes.Length, "Actual strikes did not execute.");
                Require(warnings.Count == material.strikes.Length, "Native telegraph checks incomplete.");
                Require(damage.Count == (scenario == "inside" ? material.strikes.Length : 0), "Damage entered the removed band or missed valid contact: " + material.name + " / " + scenario + " / " + damage.Count);
                Record(new JObject { ["attack"] = material.runtimeClip.name, ["scenario"] = scenario, ["strikes"] = material.strikes.Length, ["impacts"] = executor.ImpactCount, ["damage"] = damage, ["warnings"] = warnings });
            }
            finally { health.OnDamaged -= observer; if (actor.IsLeased) service.Release(actor); }
            yield return null;
        }
    }
    static void Finish(string error)
    {
        if (plan == null) return;
        foreach (var actor in leases) if (actor != null && actor.IsLeased) service?.Release(actor); leases.Clear();
        EnemyStrongAttackWarning.PlayerTarget = previousPlayerTarget; Write(error == null ? "PASS" : "FAIL", error);
        plan.phase = "returning"; Persist(); if (OwnPlay) EditorApplication.isPlaying = false;
    }
    static void Return()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) return;
        var prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""); var env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if (!string.IsNullOrEmpty(prepared) && !string.Equals(prepared, Account, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(env) && !string.Equals(env, Account, StringComparison.OrdinalIgnoreCase)) return;
        if (host != null && routine != null) host.StopCoroutine(routine);
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.Clear(); host = null; routine = null;
        Time.captureDeltaTime = plan.capture; Time.timeScale = plan.timeScale; Application.runInBackground = plan.background;
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(plan.previousStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart); IsolatedSavePlayGuard.UseRealAccount();
        File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = JToken.DeepEquals(plan.scenes, Scenes()) && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL", ["scenesBefore"] = plan.scenes, ["scenesAfter"] = Scenes(), ["guardChoice"] = IsolatedSavePlayGuard.RequiresAccountChoice,
            ["active"] = IsolatedSavePlayGuard.ActiveDirectory, ["prepared"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), ["expires"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), ["environment"] = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) }.ToString());
        SessionState.EraseString(Key); plan = null;
    }
}
