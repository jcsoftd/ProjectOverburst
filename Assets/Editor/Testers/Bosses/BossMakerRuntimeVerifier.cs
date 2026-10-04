using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Runs the assets saved by the native authoring verifier, then restores normal Play.
[InitializeOnLoad]
public static class BossMakerRuntimeVerifier
{
    const string Key = "Overburst.BossMakerRuntimeVerifier.";
    sealed class Plan
    {
        public string output, collection, folder, folderGuid, fixture, phase, previousStart, failure;
        public bool background;
        public float captureDelta, timeScale;
        public double deadline;
        public JArray scenes, cases;
    }
    static Plan plan;
    static readonly List<Object> owned = new List<Object>();
    static EnemyMotor host;
    static Coroutine routine;
    static string Account => Path.Combine(plan.output, "Account");
    static string Prepared => SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
    static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
        && string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
    static bool OwnPlay => plan != null && EditorApplication.isPlaying && Same(IsolatedSavePlayGuard.ActiveDirectory, Account);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static JArray Scenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    { var s = SceneManager.GetSceneAt(i); return new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount }; }));
    static void Persist() => SessionState.SetString(Key + "plan", JsonConvert.SerializeObject(plan));
    static void Result(string status) => File.WriteAllText(Path.Combine(plan.output, "result.json"),
        new JObject { ["status"] = status, ["failure"] = plan.failure, ["cases"] = plan.cases }.ToString());
    static BossMakerRuntimeVerifier()
    {
        string saved = SessionState.GetString(Key + "plan", "");
        if (!string.IsNullOrEmpty(saved)) plan = JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Changed;
        AssemblyReloadEvents.beforeAssemblyReload += Reload;
    }
    public static string Start(string authoringPlan, string destination)
    {
        Require(plan == null && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
            && !EditorApplication.isUpdating && !IsolatedSavePlayGuard.RequiresAccountChoice, "Idle normal Editor required.");
        Require(string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && string.IsNullOrEmpty(Prepared)
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "Unoccupied account required.");
        string output = Path.GetFullPath(destination), allowed = Path.GetFullPath(Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        Require(output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(output, "plan.json")), "Fresh private output required.");
        var input = JObject.Parse(File.ReadAllText(authoringPlan));
        string folder = (string)input["fixtureFolder"], guid = (string)input["folderGuid"];
        Require(folder.StartsWith("Assets/Editor/Testers/Bosses/BossMakerFixture_", StringComparison.Ordinal)
            && AssetDatabase.AssetPathToGUID(folder) == guid, "Owned authoring fixture required.");
        Require(JToken.DeepEquals(input["scenes"], Scenes()), "Authoring scene baseline changed.");
        Directory.CreateDirectory(output);
        plan = new Plan { output = output, collection = (string)input["collection"], folder = folder, folderGuid = guid,
            fixture = folder + "/Runtime.unity", phase = "booting", previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            background = Application.runInBackground, captureDelta = Time.captureDeltaTime, timeScale = Time.timeScale,
            deadline = EditorApplication.timeSinceStartup + 360, scenes = Scenes(), cases = new JArray() };
        Persist(); File.WriteAllText(Path.Combine(output, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
        var active = SceneManager.GetActiveScene(); Scene fixture = default;
        try
        {
            fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            Require(EditorSceneManager.SaveScene(fixture, plan.fixture), "Fixture scene save failed.");
            EditorSceneManager.CloseScene(fixture, true); fixture = default; SceneManager.SetActiveScene(active);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture);
            Application.runInBackground = true; IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return "STARTED";
        }
        catch (Exception e)
        { if (fixture.IsValid()) EditorSceneManager.CloseScene(fixture, true); if (active.IsValid()) SceneManager.SetActiveScene(active); Return(e.ToString()); throw; }
    }
    static void Tick()
    {
        if (plan == null) return;
        try
        {
            if (plan.phase == "booting" && EditorApplication.isPlaying)
            {
                Require(OwnPlay, "Isolated account mismatch.");
                var root = new GameObject("Boss maker saved-material verification"); owned.Add(root);
                host = root.AddComponent<EnemyMotor>(); root.GetComponent<Rigidbody>().isKinematic = true;
                plan.phase = "running"; Persist(); routine = host.StartCoroutine(Drive(Run()));
            }
            if (plan.phase == "running") { Require(OwnPlay, "Own Play interrupted."); EditorApplication.QueuePlayerLoopUpdate(); }
            if (plan.phase != "returning" && EditorApplication.timeSinceStartup > plan.deadline) Return("Runtime verification timeout.");
            if (plan.phase == "returning") FinishReturn();
        }
        catch (Exception e) { Return(e.ToString()); }
    }
    static IEnumerator Drive(IEnumerator body)
    {
        while (true)
        {
            bool more; object value;
            try { more = body.MoveNext(); value = more ? body.Current : null; }
            catch (Exception e) { (body as IDisposable)?.Dispose(); Return(e.ToString()); yield break; }
            if (!more) break; yield return value;
        }
        (body as IDisposable)?.Dispose(); Return(null);
    }
    static void Position(Transform transform, Vector3 position)
    { transform.position = position; var rb = transform.GetComponent<Rigidbody>(); if (rb != null) { rb.position = position; rb.linearVelocity = Vector3.zero; } Physics.SyncTransforms(); }
    static Vector3 Contact(EnemyBossMaterialStrike s, Transform actor)
    {
        float distance = s.shape == GroundIndicatorShape.Rectangle ? s.length * .65f : Mathf.Lerp(s.innerRadius, s.radius, .65f);
        return s.Origin(actor) + s.Rotation(actor) * Vector3.forward * distance;
    }
    static IEnumerator Run()
    {
        Time.captureDeltaTime = 1f / 60f; Time.timeScale = 1f;
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(plan.collection);
        Require(collection != null && collection.attacks.Length == 3 && collection.attacks.All(m => m.IsValid), "Saved authoring assets invalid.");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Vector3.down * .5f; floor.transform.localScale = new Vector3(100, 1, 100);
        var services = new GameObject("Boss maker owned spawn services"); owned.Add(services);
        var inactive = new GameObject("Inactive pool"); inactive.transform.SetParent(services.transform, false); inactive.SetActive(false);
        var pool = services.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
        var service = services.AddComponent<EnemySpawnService>(); service.Configure(collection.catalog, pool);
        var victim = new GameObject("Physical player target"); owned.Add(victim); victim.layer = LayerMask.NameToLayer("Player");
        var capsule = victim.AddComponent<CapsuleCollider>(); capsule.radius = .2f; capsule.height = 1.55f; capsule.center = Vector3.up * .775f;
        var target = victim.AddComponent<CombatTarget>(); target.Configure(CombatTeam.PlayerParty, false); target.ConfigureVolume(capsule.center, .5f, 1.55f);
        var health = victim.GetComponent<CombatHealth>(); health.SetMaxHp(100000, true);
        var previousTarget = EnemyStrongAttackWarning.PlayerTarget; EnemyStrongAttackWarning.PlayerTarget = target;
        try
        {
            foreach (var material in collection.attacks)
            {
                Position(victim.transform, new Vector3(0, .035f, material.delivery == EnemyBossMaterialDelivery.Melee ? 8 : 12));
                Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition, Vector3.up * .035f, Quaternion.identity,
                    victim.transform, context: EncounterContext.Test), out var actor), "Saved actor spawn failed.");
                actor.AI.enabled = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var executor = actor.GetComponent<EnemyBossMaterialExecutor>(); executor.Configure(collection);
                foreach (var body in actor.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(body, capsule);
                yield return null; yield return new WaitForFixedUpdate();
                var damages = new List<DamageInfo>(); var releases = new List<float>();
                Action<CombatHealth, DamageInfo> damaged = (_, info) => damages.Add(info);
                Action<EnemyBossAttackMaterial, int> released = (_, phase) => releases.Add(Time.time);
                health.OnDamaged += damaged; executor.StrikeReleased += released;
                float expected = material.ability.ResolveDamage(actor.GetComponent<EnemyRank>()?.Level ?? 1)
                    * actor.RuntimeStats.DamageMultiplier * material.DamageMultiplier;
                float speed = actor.Melee.AbilityAnimationSpeed * material.AnimationSpeedMultiplier;
                bool beforeWindow = false, inWindow = false, afterWindow = false, disabledFirst = false, snapshot = false;
                try
                {
                    Require(executor.TryStart(material.ability, 0, victim.transform), "Saved material rejected by actual executor: " + material.name);
                    float deadline = Time.time + material.ability.ResolveExecutionDuration(speed) + 8;
                    while (executor.IsExecuting)
                    {
                        Require(Time.time < deadline, "Saved attack did not finish: " + material.name + "/" + executor.LastFailure);
                        if (material.delivery == EnemyBossMaterialDelivery.Melee)
                        {
                            int phase = Mathf.Clamp(executor.ImpactCount, 0, material.strikes.Length - 1);
                            Position(victim.transform, Contact(material.strikes[phase], actor.transform));
                            if (executor.HasEnteredMotion && executor.ImpactCount == 0 && executor.NormalizedTime < material.strikes[0].impact)
                            { Require(!executor.IsParryThreatTo(target), "Disabled first-strike parry unexpectedly open."); disabledFirst = true; }
                            if (executor.HasEnteredMotion && executor.ImpactCount == 1)
                            {
                                var p = material.tuning.parries[1]; float at = executor.NormalizedTime;
                                if (at < p.startNormalized - .002f) { Require(!executor.IsParryThreatTo(target), "Custom window opened too early."); beforeWindow = true; }
                                if (at > p.startNormalized + .002f && at < p.endNormalized - .002f)
                                {
                                    Require(executor.IsParryThreatTo(target), "Saved custom parry window did not open."); inWindow = true;
                                    Require(executor.TryGetParryDamageSnapshot(target, out var info) && Mathf.Abs(info.damage - expected) < .001f
                                        && info.sourceAttackPhaseIndex == 1, "Parry damage snapshot ignores attack tuning."); snapshot = true;
                                }
                                if (at > p.endNormalized + .002f && at < material.strikes[1].impact)
                                { Require(!executor.IsParryThreatTo(target), "Custom window stayed open too late."); afterWindow = true; }
                            }
                        }
                        yield return null;
                    }
                    Require(string.IsNullOrEmpty(executor.LastFailure) && damages.Count == material.strikes.Length && releases.Count == material.strikes.Length,
                        "Saved attack release/contact count differs: " + material.name + "/" + executor.LastFailure + "/" + damages.Count);
                    Require(damages.All(d => Mathf.Abs(d.damage - expected) < .001f && d.enemyAbility == material.ability), "Saved per-attack damage coefficient not applied.");
                    var row = new JObject { ["pass"] = true, ["attack"] = material.materialId, ["delivery"] = material.delivery.ToString(),
                        ["multiplier"] = material.DamageMultiplier, ["speedMultiplier"] = material.AnimationSpeedMultiplier,
                        ["damagePerHit"] = expected, ["hitCount"] = damages.Count, ["releaseTimes"] = new JArray(releases), ["projectilesAfter"] = executor.ActiveProjectileCount };
                    if (material.delivery == EnemyBossMaterialDelivery.Melee)
                    {
                        Require(disabledFirst && beforeWindow && inWindow && afterWindow && snapshot, "Not all saved parry boundaries were observed.");
                        float expectedInterval = material.ability.ResolvePacedTime(material.strikes[1].impact, speed) - material.ability.ResolvePacedTime(material.strikes[0].impact, speed);
                        Require(Mathf.Abs(releases[1] - releases[0] - expectedInterval) < .15f, "Actual animation interval ignores authored speed.");
                        row["expectedInterval"] = expectedInterval; row["actualInterval"] = releases[1] - releases[0]; row["customParryBoundaries"] = true;
                    }
                    if (material.delivery == EnemyBossMaterialDelivery.Boulder) { row["flightSeconds"] = material.flightSeconds; row["arcHeight"] = material.arcHeight; }
                    plan.cases.Add(row); Persist(); Result("RUNNING");
                }
                finally { health.OnDamaged -= damaged; executor.StrikeReleased -= released; if (actor != null && actor.IsLeased) service.Release(actor); }
                yield return null;
            }
        }
        finally { EnemyStrongAttackWarning.PlayerTarget = previousTarget; }
    }
    static void Return(string error)
    {
        if (plan == null) return;
        if (!string.IsNullOrEmpty(error)) plan.failure = error;
        Result(plan.failure == null ? "PASS" : "FAIL"); plan.phase = "returning"; Persist(); if (OwnPlay) EditorApplication.isPlaying = false;
    }
    static void Changed(PlayModeStateChange state)
    { if (plan != null && state == PlayModeStateChange.EnteredEditMode) { if (plan.phase != "returning") plan.failure = "Play stopped before verification completed."; plan.phase = "returning"; Persist(); } }
    static void Reload()
    { if (plan == null) return; if (plan.phase == "running") { plan.failure = "Runtime verifier interrupted by compilation."; plan.phase = "returning"; Result("FAIL"); } Persist(); }
    static void FinishReturn()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if (!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(env) && !Same(env, Account)
            || !string.IsNullOrEmpty(Prepared) && !Same(Prepared, Account)) return;
        if (host != null && routine != null) host.StopCoroutine(routine);
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.Clear(); host = null; routine = null;
        Time.captureDeltaTime = plan.captureDelta; Time.timeScale = plan.timeScale; Application.runInBackground = plan.background;
        if (AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == plan.fixture)
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(plan.previousStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart);
        IsolatedSavePlayGuard.UseRealAccount();
        if (AssetDatabase.AssetPathToGUID(plan.folder) == plan.folderGuid) AssetDatabase.DeleteAsset(plan.folder);
        var after = Scenes(); bool valid = !IsolatedSavePlayGuard.RequiresAccountChoice && JToken.DeepEquals(plan.scenes, after)
            && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && string.IsNullOrEmpty(Prepared)
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable));
        File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = valid ? "PASS" : "FAIL",
            ["scenesBefore"] = plan.scenes, ["scenesAfter"] = after, ["guardChoice"] = IsolatedSavePlayGuard.RequiresAccountChoice,
            ["active"] = IsolatedSavePlayGuard.ActiveDirectory, ["prepared"] = Prepared, ["expires"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""),
            ["environment"] = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), ["startScene"] = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            ["ownedFixtureDeleted"] = !Directory.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, plan.folder)), ["pendingAfter"] = "" }.ToString());
        Result(plan.failure == null ? "PASS" : "FAIL"); SessionState.EraseString(Key + "plan"); plan = null;
    }
}
