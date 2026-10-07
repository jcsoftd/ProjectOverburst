using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static partial class CrustaspikanEncounterSafetyVerifier
{
    const string Key = "Overburst.CrustaspikanEncounterSafetyVerifier.plan";
    sealed class Plan
    {
        public string output, phase, previousStart, inputBackground;
        public JArray scenes;
        public bool background;
        public float capture, timeScale;
        public double deadline;
    }
    static Plan plan;
    static readonly JArray playCases = new JArray(), errors = new JArray(), warnings = new JArray();
    static readonly List<Object> playOwned = new List<Object>();
    static readonly Dictionary<EnemyBossMaterialExecutor, EnemyBossMaterialCollection> alteredExecutors = new Dictionary<EnemyBossMaterialExecutor, EnemyBossMaterialCollection>();
    static CrustaspikanEncounterHost encounterHost;
    static CrustaspikanEncounterSettings originalSettings;
    static EnemyMotor coroutineHost;
    static Coroutine routine;
    static string Account => Path.Combine(plan.output, "Account");
    static bool OwnPlay => plan != null && string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);
    static bool ForeignReservation => Different(IsolatedSavePlayGuard.ActiveDirectory)
        || Different(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
        || Different(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable));
    static bool Different(string directory) => !string.IsNullOrEmpty(directory) && !string.Equals(directory, Account, StringComparison.OrdinalIgnoreCase);
    static void Persist() => SessionState.SetString(Key, JsonConvert.SerializeObject(plan));
    static CrustaspikanEncounterSafetyVerifier()
    {
        string saved = SessionState.GetString(Key, "");
        if (!string.IsNullOrEmpty(saved)) plan = JsonConvert.DeserializeObject<Plan>(saved);
        if (plan != null && plan.phase != "deferred") EditorApplication.update += Tick;
    }
    public static string StartPlay(string directory)
    {
        Idle(); Require(plan == null, "An owned verification is already pending.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        Require(!Directory.Exists(directory), "Fresh private evidence directory required."); Directory.CreateDirectory(directory);
        plan = new Plan { output = directory, phase = "booting", scenes = Scenes(), previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            inputBackground = InputSystem.settings.backgroundBehavior.ToString(), background = Application.runInBackground,
            capture = Time.captureDeltaTime, timeScale = Time.timeScale, deadline = EditorApplication.timeSinceStartup + 300 };
        playCases.Clear(); errors.Clear(); warnings.Clear(); Persist();
        File.WriteAllText(Path.Combine(directory, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
        EditorApplication.update += Tick;
        try
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
            Application.runInBackground = true; IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return "STARTED";
        }
        catch { plan.phase = "returning"; plan.deadline = EditorApplication.timeSinceStartup + 120; Persist(); throw; }
    }
    static void Tick()
    {
        if (plan == null) return;
        if (plan.phase == "returning") { Return(); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode && !OwnPlay || ForeignReservation) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && (plan.phase == "running" || plan.phase == "booting" && IsolatedSavePlayGuard.RequiresAccountChoice))
        { Finish("Owned Play stopped before verification completed."); return; }
        if (EditorApplication.timeSinceStartup > plan.deadline) { Finish("Verification timed out."); return; }
        if (plan.phase != "booting" || !EditorApplication.isPlaying || !OwnPlay) return;
        plan.phase = "running"; Persist();
        var root = new GameObject("Owned encounter safety verifier"); playOwned.Add(root); coroutineHost = root.AddComponent<EnemyMotor>();
        root.GetComponent<Rigidbody>().isKinematic = true; routine = coroutineHost.StartCoroutine(Drive(RunPlay()));
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
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
        else if (type == LogType.Warning && message.StartsWith("[Crustaspikan]")) warnings.Add(message);
    }
    static void Record(string label, JObject detail = null)
    {
        var row = detail ?? new JObject(); row["case"] = label; row["pass"] = true; playCases.Add(row); WritePlay("RUNNING", null);
    }
    static void SetSettings(CrustaspikanEncounterSettings value)
        => typeof(CrustaspikanEncounterHost).GetField("<Settings>k__BackingField", Private).SetValue(encounterHost, value);
    static int RuntimeCopies() => Resources.FindObjectsOfTypeAll<ScriptableObject>().Count(o => !EditorUtility.IsPersistent(o)
        && (o is EnemyBossMaterialCollection || o is EnemyBossAttackMaterial || o is EnemyAbilityDefinition || o is EnemyAbilitySet
            || o is EnemyBossCombatProfile || o is EnemyBossCompositePatternSet));
    sealed class ReturnState
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool protection;
        public float hp, framing, distance;
        public Collider confiner;
        public float slowing;
        public Dictionary<EnemyTargetHpHud, bool> targetHuds;
    }
    static ReturnState Capture(PlayerActorRuntime player)
    {
        var camera = QuarterViewCamera.ActiveInstance; var confiner = camera?.CinemachineRig?.Confiner;
        return new ReturnState { position = player.transform.position, rotation = player.transform.rotation, hp = player.Health.CurrentHp, protection = player.Health.IsDeathFromDamagePrevented,
            framing = camera != null ? camera.FramingDistanceScale : 1f, distance = camera != null ? camera.CurrentDistance : 0f,
            confiner = confiner?.BoundingVolume, slowing = confiner != null ? confiner.SlowingDistance : 0f,
            targetHuds = Object.FindObjectsByType<EnemyTargetHpHud>(FindObjectsSortMode.None).ToDictionary(h => h, h => h.enabled) };
    }
    static void AssertReturned(PlayerActorRuntime player, EnemyPoolService pool, int leases, ReturnState before, int runtimeCopies, string label)
    {
        Require(encounterHost.ActiveEncounter == null && encounterHost.CanEnter && encounterHost.Entrance != null
            && encounterHost.Entrance.gameObject.activeSelf, label + ": entry portal did not return.");
        Require(pool.LeasedCount == leases && pool.PendingReturnCount == 0, label + ": boss lease leaked.");
        float displacement = new Vector2(player.transform.position.x - before.position.x, player.transform.position.z - before.position.z).magnitude;
        Require(displacement < .02f, label + $": return position changed (XZ={displacement:0.######}m).");
        Require(player.Health.IsDeathFromDamagePrevented == before.protection, label + ": death prevention leaked.");
        Require(Mathf.Abs(player.Health.CurrentHp - before.hp) < .001f, label + ": entry health was not restored.");
        var camera = QuarterViewCamera.ActiveInstance; var confiner = camera?.CinemachineRig?.Confiner;
        Require(camera == null || Mathf.Abs(camera.FramingDistanceScale - before.framing) < .001f
            && Mathf.Abs(camera.CurrentDistance - before.distance) < .01f, label + ": framing was not restored.");
        Require(confiner == null || confiner.BoundingVolume == before.confiner && Mathf.Abs(confiner.SlowingDistance - before.slowing) < .001f, label + ": confiner was not restored.");
        Require(before.targetHuds.All(p => p.Key == null || p.Key.enabled == p.Value), label + ": target HUD state changed.");
        Require(!Object.FindObjectsByType<EnemyBossHudView>(FindObjectsSortMode.None).Any(v => v.BoundEncounterSource != null), label + ": boss HUD binding leaked.");
        Require(!GameplayInputBlocker.IsGameplayInputBlocked, label + ": gameplay input remained blocked.");
        Require(RuntimeCopies() == runtimeCopies, label + ": temporary material/ability/profile copies leaked.");
        Record(label, new JObject { ["leased"] = pool.LeasedCount, ["pendingReturns"] = pool.PendingReturnCount, ["runtimeCopies"] = runtimeCopies });
    }
    static void AssertImmediatePose(PlayerActorRuntime player, ReturnState before, string label)
    {
        float displacement = Vector3.Distance(player.transform.position, before.position), rotation = Quaternion.Angle(player.transform.rotation, before.rotation);
        Require(displacement < .001f && rotation < .1f, label + ": entry pose not restored before normal control resumes.");
        Record(label, new JObject { ["displacement"] = displacement, ["rotationDegrees"] = rotation });
    }
    static IEnumerator RunPlay()
    {
        float deadline = Time.realtimeSinceStartup + 90f;
        while (Time.realtimeSinceStartup < deadline && (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || !WorldSessionState.IsHideout || PlayerContext.GetOrCreate().CurrentActor == null || CrustaspikanEncounterHost.Current?.Entrance == null)) yield return null;
        Require(Time.realtimeSinceStartup < deadline, "Isolated hideout boot timed out.");
        Require(string.Equals(AccountBootstrap.SaveDirectory, Account, StringComparison.OrdinalIgnoreCase), "Save directory differs from owned isolated account.");
        encounterHost = CrustaspikanEncounterHost.Current; originalSettings = encounterHost.Settings;
        var settings = Object.Instantiate(originalSettings); playOwned.Add(settings); settings.entrance.enabled = false; SetSettings(settings);
        var service = EnemySpawnService.Current;
        if (service == null)
        {
            var root = new GameObject("Owned encounter safety spawn service"); playOwned.Add(root);
            var inactive = new GameObject("Owned inactive actors"); inactive.transform.SetParent(root.transform, false); inactive.SetActive(false);
            var ownedPool = root.AddComponent<EnemyPoolService>(); ownedPool.Configure(inactive.transform, 0);
            service = root.AddComponent<EnemySpawnService>(); service.Configure(settings.materials.catalog, ownedPool);
        }
        var player = PlayerContext.GetOrCreate().CurrentActor; var pool = service.Pool; int leases = pool.LeasedCount;
        var before = Capture(player); int runtimeCopies = RuntimeCopies(); Application.logMessageReceived += Log;
        Record("isolated-hideout-ready", new JObject { ["editorPid"] = System.Diagnostics.Process.GetCurrentProcess().Id, ["baselineLeased"] = leases });
        Require(encounterHost.Enter(player), "Normal entry rejected: " + encounterHost.LastFailure);
        var encounter = encounterHost.ActiveEncounter; var brain = encounter.Brain; brain.ReviewMode = true;
        Require(brain.IsActive && encounter.BossHud.BoundEncounterSource == brain && pool.LeasedCount == leases + 1, "Normal encounter not bound or leased.");
        Require(brain.RuntimeMaterials.attacks.All(m => m.IsValid), "Runtime composed materials invalid.");
        Record("normal-entry-and-final-materials");
        var combo = brain.RuntimeMaterials.attacks.Single(m => m.runtimeClip.name == "2HitComboAttack");
        bool IntersectsCombo() => combo.strikes.Any(s => s.Intersects(player.CharacterController, brain.Actor.transform,
            brain.Actor.transform.position + brain.Actor.Movement.PhysicalRotation * Vector3.Scale(s.localOrigin, brain.Actor.transform.lossyScale),
            brain.Actor.Movement.PhysicalRotation * Quaternion.Euler(0, s.yaw, 0)));
        bool initialFootprint = IntersectsCombo();
        bool controllerEnabled = player.CharacterController.enabled; player.CharacterController.enabled = false;
        player.transform.position = brain.Actor.transform.position + brain.Actor.Movement.PhysicalRotation * Vector3.forward * 7f;
        player.CharacterController.enabled = controllerEnabled; player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms();
        Require(IntersectsCombo(), "Attack probe target is outside the approved footprint.");
        Record("attack-probe-target-placement", new JObject { ["entryTargetInsideCombo"] = initialFootprint, ["probeTargetInsideCombo"] = true, ["probeDistance"] = 7f });
        Require(brain.StartPatternForReview("combo"), "Two-hit review pattern rejected.");
        deadline = Time.realtimeSinceStartup + 18f; bool executing = false;
        while (Time.realtimeSinceStartup < deadline && (!executing || brain.CurrentPatternId != ""))
        {
            executing |= brain.Actor.AbilityController.IsExecuting; yield return null;
        }
        Require(executing && brain.CurrentPatternId == "", "Normal attack did not execute and finish: " + brain.State + ", pattern=" + brain.CurrentPatternId + ", observedExecution=" + executing); Record("normal-two-hit-pattern-execution");
        var oldActor = brain.Actor; uint oldVersion = oldActor.LeaseVersion;
        Require(encounter.TryRestart(), "Normal restart rejected: " + encounter.LastFailure); encounter.Brain.ReviewMode = true;
        Require(!brain.IsActive && encounter.Brain.IsActive && pool.LeasedCount == leases + 1
            && (encounter.Brain.Actor != oldActor || encounter.Brain.Actor.LeaseVersion != oldVersion), "Restart retained old brain or lease."); Record("normal-restart-new-lease");
        var faultActor = encounter.Brain.Actor; var executor = faultActor.GetComponent<EnemyBossMaterialExecutor>();
        encounter.Exit(true);
        AssertImmediatePose(player, before, "normal-exit-immediate-pose");
        yield return null; yield return null;
        AssertReturned(player, pool, leases, before, runtimeCopies, "normal-exit-restores-all-scopes");

        // The saved settings stay valid; only the already pooled instance is faulty after successful acquisition.
        var badCollection = Object.Instantiate(originalSettings.materials); playOwned.Add(badCollection);
        badCollection.attacks = badCollection.attacks.Concat(new[] { badCollection.attacks[0] }).ToArray();
        alteredExecutors[executor] = executor.Collection; executor.Configure(badCollection); uint faultVersion = faultActor.LeaseVersion;
        int faultCopies = RuntimeCopies();
        Require(settings.Validate(out var reason), "Fault fixture invalidated preflight: " + reason);
        before = Capture(player);
        Require(!encounterHost.Enter(player) && encounterHost.LastFailure.Contains("중복"), "Constructor fault was not rejected: " + encounterHost.LastFailure);
        Require(faultActor.LeaseVersion != faultVersion && !faultActor.IsLeased, "Fault did not occur after borrowing or actor remained leased.");
        AssertImmediatePose(player, before, "post-acquisition-failure-immediate-pose");
        yield return null; yield return null;
        AssertReturned(player, pool, leases, before, faultCopies, "post-acquisition-failure-restores-all-scopes");
        executor.Configure(alteredExecutors[executor]); alteredExecutors.Remove(executor);
        Object.Destroy(badCollection); playOwned.Remove(badCollection); yield return null;

        before = Capture(player);
        Require(encounterHost.Enter(player), "Entry after failed construction rejected."); encounter = encounterHost.ActiveEncounter; encounter.Brain.ReviewMode = true;
        Record("re-entry-after-constructor-failure");
        settings.materialRules = settings.materialRules.Concat(new[] { settings.materialRules[0] }).ToArray();
        Require(!encounter.TryRestart() && encounter.LastFailure.Contains("중복"), "Invalid restart announced success or remained in the arena.");
        AssertImmediatePose(player, before, "failed-restart-immediate-pose");
        yield return null; yield return null;
        AssertReturned(player, pool, leases, before, runtimeCopies, "failed-restart-exits-to-hideout");
        settings.materialRules = originalSettings.materialRules.Select(r => new CrustaspikanEncounterSettings.MaterialRule {
            clip = r.clip, speed = r.speed, damage = r.damage, finalHitParry = r.finalHitParry, firstHitParry = r.firstHitParry }).ToArray();
        before = Capture(player);
        Require(encounterHost.Enter(player), "Entry after failed restart rejected."); encounterHost.ActiveEncounter.Brain.ReviewMode = true;
        var optionalLootActor = encounterHost.ActiveEncounter.Brain.Actor;
        encounterHost.ActiveEncounter.Exit(true); AssertImmediatePose(player, before, "repeat-entry-exit-immediate-pose"); yield return null; yield return null;
        AssertReturned(player, pool, leases, before, runtimeCopies, "repeat-entry-and-exit-after-failure");
        // This runtime-only pooled instance has no optional dropper; lease ownership must still work.
        var optionalLoot = optionalLootActor.GetComponent<EnemyLootDropper>(); if (optionalLoot != null) Object.DestroyImmediate(optionalLoot);
        Require(optionalLootActor.IsAuthoringValid, "Removing the optional dropper invalidated required actor references.");
        before = Capture(player);
        Require(encounterHost.Enter(player), "Boss without optional loot could not enter."); encounter = encounterHost.ActiveEncounter; encounter.Brain.ReviewMode = true;
        Require(encounter.Brain.Actor.GetComponent<EnemyLootDropper>() == null, "No-loot fixture did not borrow the altered instance.");
        encounter.Exit(true); AssertImmediatePose(player, before, "no-loot-exit-immediate-pose"); yield return null; yield return null;
        AssertReturned(player, pool, leases, before, runtimeCopies, "no-loot-boss-lease-return");
        Require(errors.Count == 0, "Unexpected runtime errors: " + errors); Record("no-unexpected-runtime-errors");
    }
    static void WritePlay(string status, string failure)
        => File.WriteAllText(Path.Combine(plan.output, "result.json"), new JObject { ["status"] = status, ["failure"] = failure,
            ["scope"] = "Isolated Editor Play: actual hideout entry, attack, restart, exit and post-acquisition/restart failure recovery; no Player build.",
            ["cases"] = playCases, ["unexpectedErrors"] = errors, ["handledFailureWarnings"] = warnings }.ToString());
    static void Finish(string failure)
    {
        if (plan == null || plan.phase == "returning") return;
        try
        {
            if (encounterHost != null) { encounterHost.ActiveEncounter?.Exit(true); if (originalSettings != null) SetSettings(originalSettings); }
            foreach (var pair in alteredExecutors) if (pair.Key != null) pair.Key.Configure(pair.Value); alteredExecutors.Clear();
            WritePlay(failure == null ? "PASS" : "FAIL", failure);
        }
        finally
        {
            Application.logMessageReceived -= Log; plan.phase = "returning"; plan.deadline = EditorApplication.timeSinceStartup + 120; Persist(); if (OwnPlay) EditorApplication.isPlaying = false;
        }
    }
    public static string ResumeReturn()
    {
        Require(plan != null && plan.phase == "deferred", "No owned deferred return exists.");
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !ForeignReservation,
            "Wait for a safe idle Editor; another account reservation must remain untouched.");
        plan.phase = "returning"; plan.deadline = EditorApplication.timeSinceStartup + 120; Persist(); EditorApplication.update += Tick; return "RETURN_QUEUED";
    }
    static void Return()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || ForeignReservation)
        {
            if (EditorApplication.timeSinceStartup > plan.deadline)
            {
                plan.phase = "deferred"; Persist(); EditorApplication.update -= Tick;
                File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = "DEFERRED_EDITOR_OR_ACCOUNT_BUSY", ["ownPending"] = true,
                    ["resume"] = "CrustaspikanEncounterSafetyVerifier.ResumeReturn" }.ToString());
            }
            return;
        }
        if (coroutineHost != null && routine != null) coroutineHost.StopCoroutine(routine);
        for (int i = playOwned.Count - 1; i >= 0; i--) if (playOwned[i] != null) Object.DestroyImmediate(playOwned[i]); playOwned.Clear();
        coroutineHost = null; routine = null; encounterHost = null; originalSettings = null;
        Time.captureDeltaTime = plan.capture; Time.timeScale = plan.timeScale; Application.runInBackground = plan.background;
        InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)Enum.Parse(typeof(InputSettings.BackgroundBehavior), plan.inputBackground);
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(plan.previousStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart);
        IsolatedSavePlayGuard.UseRealAccount(); SessionState.EraseString(Key); EditorApplication.update -= Tick;
        var after = Scenes(); bool returned = JToken.DeepEquals(plan.scenes, after) && !IsolatedSavePlayGuard.RequiresAccountChoice
            && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable));
        File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = returned ? "PASS" : "FAIL", ["scenesBefore"] = plan.scenes, ["scenesAfter"] = after,
            ["guardChoice"] = IsolatedSavePlayGuard.RequiresAccountChoice, ["active"] = IsolatedSavePlayGuard.ActiveDirectory,
            ["prepared"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), ["expires"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""),
            ["environment"] = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), ["startScene"] = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            ["inputBackground"] = InputSystem.settings.backgroundBehavior.ToString(), ["runInBackground"] = Application.runInBackground, ["ownPending"] = SessionState.GetString(Key, "") }.ToString());
        plan = null;
    }
}
