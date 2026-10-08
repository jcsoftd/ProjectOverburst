using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CrustaspikanReturnVerifier
{
    const string Key = "Overburst.CrustaspikanD4.";
    static CrustaspikanReturnVerifier()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        Application.logMessageReceived += Log;
        EditorApplication.delayCall += CompleteStop;
    }
    public static string Start(string output, string mode)
    {
        string account = Path.GetFullPath(Path.Combine(output, "Play01/IsolatedSave"));
        if (!EditorApplication.isPlaying || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) != account
            || SessionState.GetString("Overburst.CrustaspikanTempoAudit.output", "") != output
            || !SessionState.GetBool("Overburst.CrustaspikanTempoAudit.pending", false)
            || File.Exists(Path.Combine(output, "Play01/result.json")) || UnityEngine.Object.FindFirstObjectByType<CrustaspikanReturnObserver>() != null)
            throw new InvalidOperationException("New owned isolated return test required.");
        if (!new[] { "baseline-stop", "baseline-root", "cases", "stop-zero", "stop-small", "stop-mixed" }.Contains(mode)) throw new ArgumentException(mode);
        var root = new GameObject("Owned Crustaspikan Return Observer"); UnityEngine.Object.DontDestroyOnLoad(root);
        root.AddComponent<CrustaspikanReturnObserver>().Initialize(output, mode); return "D4 actual lifecycle observer: " + mode;
    }
    public static string DirectStop(string output)
    {
        string account = Path.GetFullPath(Path.Combine(output, "Play01/IsolatedSave"));
        if (!EditorApplication.isPlaying || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) != account
            || SessionState.GetString("Overburst.CrustaspikanTempoAudit.output", "") != output
            || !SessionState.GetBool("Overburst.CrustaspikanTempoAudit.pending", false)
            || SessionState.GetBool("Overburst.CrustaspikanTempoAudit.stopPending", false)
            || !string.IsNullOrEmpty(SessionState.GetString(Key + "stopOutput", ""))) throw new InvalidOperationException("Own unstopped session required.");
        var result = JObject.Parse(File.ReadAllText(Path.Combine(output, "Play01/result.json")));
        if ((string)result["status"] != "READY_DIRECT_STOP") throw new InvalidOperationException("Actual live roster readiness required.");
        int[] actors = UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(a => a.gameObject.scene.IsValid()).Select(a => a.GetInstanceID()).ToArray();
        File.WriteAllText(Path.Combine(output, "Play01/stop-queued.json"), JsonConvert.SerializeObject(new { utc = DateTime.UtcNow, actors, normalExitCalled = false }, Formatting.Indented));
        SessionState.SetString(Key + "stopOutput", output); SessionState.SetBool("Overburst.CrustaspikanTempoAudit.stopPending", true);
        double after = EditorApplication.timeSinceStartup + 1;
        EditorApplication.CallbackFunction stop = null;
        stop = () => {
            if (EditorApplication.timeSinceStartup < after) return;
            EditorApplication.update -= stop;
            if (!EditorApplication.isPlaying || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) != account) return;
            EditorApplication.isPlaying = false;
        };
        EditorApplication.update += stop; return "Native Editor direct Stop queued once; no normal encounter Exit.";
    }
    static void Log(string message, string stack, LogType type)
    {
        string output = SessionState.GetString(Key + "stopOutput", "");
        if (output == "" || type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string path = Path.Combine(output, "Play01/stop-errors.json");
        var rows = File.Exists(path) ? JArray.Parse(File.ReadAllText(path)) : new JArray();
        rows.Add(new JObject { ["utc"] = DateTime.UtcNow.ToString("o"), ["message"] = message, ["stack"] = stack, ["type"] = type.ToString() });
        File.WriteAllText(path, rows.ToString(Formatting.Indented));
    }
    static void StateChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.EnteredEditMode) CompleteStop(); }
    static void CompleteStop()
    {
        string output = SessionState.GetString(Key + "stopOutput", "");
        if (output == "" || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string resultPath = Path.Combine(output, "Play01/direct-stop.json"); if (File.Exists(resultPath)) return;
        var queued = JObject.Parse(File.ReadAllText(Path.Combine(output, "Play01/stop-queued.json")));
        var queuedIds = new HashSet<int>(queued["actors"].Values<int>());
        int alive = UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(a => queuedIds.Contains(a.GetInstanceID()));
        string errorsPath = Path.Combine(output, "Play01/stop-errors.json"); var errors = File.Exists(errorsPath) ? JArray.Parse(File.ReadAllText(errorsPath)) : new JArray();
        bool baseline = (string)JObject.Parse(File.ReadAllText(Path.Combine(output, "Play01/result.json")))["mode"] == "baseline-stop";
        int hierarchyErrors = errors.Count(e => ((string)e["message"]).IndexOf("parent", StringComparison.OrdinalIgnoreCase) >= 0);
        string status = baseline ? hierarchyErrors > 0 && alive == 0 ? "REPRODUCED_BASELINE" : "BASELINE_NOT_REPRODUCED"
            : errors.Count == 0 && alive == 0 ? "PASS_DIRECT_STOP" : "FAIL_DIRECT_STOP";
        File.WriteAllText(resultPath, JsonConvert.SerializeObject(new { status, utc = DateTime.UtcNow, seconds = (DateTime.UtcNow - (DateTime)queued["utc"]).TotalSeconds,
            capturedActors = queued["actors"].Count(), survivingActors = alive, errorCount = errors.Count, hierarchyErrors, normalExitCalled = false,
            poolReturnClaim = "SESSION_OBJECTS_DESTROYED_BY_ENGINE", PlayerBuild = "FORBIDDEN" }, Formatting.Indented));
        SessionState.SetString(Key + "stopOutput", ""); SessionState.SetBool("Overburst.CrustaspikanTempoAudit.stopPending", false);
    }
    public static string Native(string output)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        if (typeof(EnemyPoolService).GetMethod("ReleaseDeferred", new[] { typeof(EnemyActor), typeof(uint) })?.ReturnType != typeof(bool)
            || typeof(EnemySpawnService).GetMethod("ReleaseDeferred", new[] { typeof(EnemyActor), typeof(uint) })?.ReturnType != typeof(bool)
            || typeof(EnemyActor).GetMethod("RequestPoolRelease")?.ReturnType != typeof(bool)) throw new InvalidOperationException("Pool APIs missing.");
        var settings = Resources.Load<CrustaspikanEncounterSettings>("Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan");
        if (!settings.Validate(out var reason)) throw new InvalidOperationException(reason);
        File.WriteAllText(Path.Combine(output, "native.json"), JsonConvert.SerializeObject(new { status = "PASS_NATIVE", checks = 4, scope = "Native compile/API and encounter validation; actual ownership verified in Play", utc = DateTime.UtcNow }, Formatting.Indented));
        return "D4 native API/encounter checks4.";
    }
}

[DefaultExecutionOrder(20000)]
public sealed class CrustaspikanReturnObserver : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    string output, mode, caseId = "boot"; bool finished; float bootAt; IEnumerator flow;
    CrustaspikanEncounter encounter; PlayerActorRuntime player; EnemySpawnService service; EnemyPoolService pool;
    readonly List<object> cases = new List<object>();
    CrustaspikanEncounterBrain Brain => encounter.Brain;
    EnemyActor Boss => Brain.Actor;
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    EnemyActor[] Adds()
    {
        var list = (IEnumerable)typeof(EnemyBossCompositePatternExecutor).GetField("adds", Private).GetValue(Brain.Composite);
        return list.Cast<object>().Select(a => (EnemyActor)a.GetType().GetField("actor").GetValue(a)).Where(a => a != null && a.IsLeased).ToArray();
    }
    bool Deferred(EnemyActor actor, uint lease) => (bool)typeof(EnemySpawnService).GetMethod("ReleaseDeferred").Invoke(service, new object[] { actor, lease });
    public void Initialize(string folder, string testMode) { output = folder; mode = testMode; bootAt = Time.realtimeSinceStartup; flow = Run(); Application.runInBackground = true; Write("RUNNING", null); }
    void Update()
    {
        if (finished) return;
        try
        {
            Check(Time.timeScale == 1 && Time.captureDeltaTime == 0, "Live clock changed.");
            Check(Time.realtimeSinceStartup - bootAt < 300f, "Bounded lifecycle deadline.");
            if (!flow.MoveNext())
            {
                if (mode == "baseline-root") Check(caseId == "missing-root-falsely-returned-baseline" && cases.Count == 1, "Actual baseline branch did not execute.");
                finished = true; if (encounter != null && encounter.Brain != null) Brain.ReviewMode = true;
                Write(mode == "baseline-root" ? "REPRODUCED_BASELINE" : mode == "cases" ? "PASS_PLAY" : "READY_DIRECT_STOP", null);
            }
        }
        catch (Exception error) { finished = true; Write(caseId == "boot" ? "FAIL_PREPARATION" : "FAIL_PLAY", error.ToString()); }
    }
    IEnumerator Run()
    {
        bool hub = false;
        while (encounter == null)
        {
            player = PlayerContext.Instance?.CurrentActor; var host = CrustaspikanEncounterHost.Current; var scene = PersistentSceneFlow.Instance;
            if (!hub && player != null && scene != null && !scene.IsSwitching && scene.CurrentSubSceneName == PersistentSceneFlow.MainSceneName)
            {
                var portal = UnityEngine.Object.FindObjectsByType<HubScenePortal>(FindObjectsSortMode.None).FirstOrDefault(p => p.TargetSceneName == PersistentSceneFlow.HideoutSceneName && p.IsInteractionAvailable(player));
                if (portal != null) { Warp(portal.transform.position + Vector3.back); Check(portal.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Actual hub portal rejected."); hub = true; }
            }
            if (player != null && host != null && host.CanEnter && host.Entrance != null)
            {
                Warp(host.Entrance.transform.position + Vector3.back); Check(host.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Actual boss portal rejected."); encounter = host.ActiveEncounter; Brain.ReviewMode = true;
            }
            Check(Time.realtimeSinceStartup - bootAt < 80, "Portal boot timeout."); yield return null;
        }
        while (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); yield return null; }
        service = EnemySpawnService.Current; pool = service.Pool; Check(pool != null && pool.isActiveAndEnabled, "Actual live pool required.");
        if (mode == "baseline-root")
        {
            var spawn = Spawn(false); while (spawn.MoveNext()) yield return null;
            caseId = "missing-root-falsely-returned-baseline";
            var baselineActor = Adds()[0]; uint version = baselineActor.LeaseVersion; Transform baselineRoot = pool.InactivePoolRoot; int baselinePrewarm = pool.DefaultPrewarmCount, baselineAvailable = pool.AvailableCount;
            pool.Configure(null, baselinePrewarm);
            try
            {
                baselineActor.gameObject.SetActive(false); service.Release(baselineActor); yield return null; yield return null;
                Check(!baselineActor.IsLeased && pool.PendingReturnCount == 0 && pool.AvailableCount == baselineAvailable + 1 && baselineActor.transform.parent == null,
                    "Missing-baselineRoot failure did not reproduce.");
                cases.Add(new { id = caseId, status = "REPRODUCED", version, incorrectlyAvailable = true, lostPendingRequest = true, diagnosticRuntimeRootReferenceOnly = true });
            }
            finally { pool.Configure(baselineRoot, baselinePrewarm); if (baselineActor != null && !baselineActor.IsLeased) baselineActor.transform.SetParent(baselineRoot, false); }
            yield break;
        }
        if (mode != "cases")
        {
            if (mode != "stop-zero") { var spawn = Spawn(mode == "stop-mixed"); while (spawn.MoveNext()) yield return null; }
            caseId = "live-direct-stop-roster"; var adds = Adds();
            Check(mode == "stop-zero" ? adds.Length == 0 : adds.Length >= 12, "Direct Stop roster not ready.");
            cases.Add(new { id = caseId, status = "READY", adds = adds.Length, kinds = adds.GroupBy(a => a.Definition.EnemyId).Select(g => new { id = g.Key, count = g.Count() }).ToArray(),
                bossLease = Boss.LeaseVersion, pool.LeasedCount, pool.PendingReturnCount, normalExitCalled = false }); yield break;
        }
        var test = Spawn(false); while (test.MoveNext()) yield return null;
        caseId = "normal-immediate-return"; var actors = Adds(); int available = pool.AvailableCount;
        encounter.ClearSummons(); Check(actors.All(a => !a.IsLeased) && pool.PendingReturnCount == 0 && pool.AvailableCount == available + actors.Length, "Normal immediate return changed.");
        Pass(new { id = caseId, status = "PASS", actors = actors.Length, immediate = true });

        test = Spawn(false); while (test.MoveNext()) yield return null;
        caseId = "explicit-duplicate-disable-and-stale-lease"; actors = Adds(); var actor = actors[0]; uint lease = actor.LeaseVersion; var definition = actor.Definition; available = pool.AvailableCount;
        Check(Deferred(actor, lease) && Deferred(actor, lease) && pool.PendingReturnCount == 1 && actor.IsLeased, "Duplicate/early deferred ownership mismatch.");
        actor.gameObject.SetActive(false); Check(pool.PendingReturnCount == 1, "OnDisable overwrote explicit request."); yield return null; yield return null;
        Check(!actor.IsLeased && pool.PendingReturnCount == 0 && pool.AvailableCount == available + 1, "Deferred return duplicated/lost.");
        Check(service.TrySpawn(new EnemySpawnRequest(definition, encounter.ArenaCenter + Vector3.right * 10f, Quaternion.identity, player.transform, spawnParent: encounter.transform), out var rented), "Actual reacquire failed.");
        Check(rented == actor && rented.LeaseVersion != lease, "Actual same actor was not re-leased."); uint newLease = rented.LeaseVersion;
        Check(!Deferred(rented, lease) && rented.IsLeased && rented.LeaseVersion == newLease && pool.PendingReturnCount == 0, "Stale lease harmed new actor.");
        Check(rented.RequestPoolRelease(), "Normal RequestPoolRelease completion changed.");
        Pass(new { id = caseId, status = "PASS", lease, newLease, immediateRequestPoolRelease = true });

        test = Spawn(false); while (test.MoveNext()) yield return null;
        caseId = "automatic-disable-reactivate-contract"; actors = Adds(); actor = actors[0]; lease = actor.LeaseVersion;
        actor.gameObject.SetActive(false); actor.gameObject.SetActive(true); yield return null; yield return null;
        Check(actor.IsLeased && actor.LeaseVersion == lease && actor.gameObject.activeInHierarchy && pool.PendingReturnCount == 0, "Automatic reactivation contract changed.");
        Pass(new { id = caseId, status = "PASS", lease });

        foreach (bool sameFrame in new[] { true, false })
        {
            test = Spawn(false); while (test.MoveNext()) yield return null;
            caseId = sameFrame ? "parent-disable-same-frame-reactivate" : "parent-remains-disabled";
            actors = Adds(); lease = Boss.LeaseVersion; var boss = Boss; available = pool.AvailableCount;
            encounter.gameObject.SetActive(false); if (sameFrame) encounter.gameObject.SetActive(true);
            yield return null; yield return null;
            bool poolSuspendedWithArena = !pool.isActiveAndEnabled;
            if (poolSuspendedWithArena)
            {
                Check(pool.PendingReturnCount >= actors.Length && actors.All(a => a != null && a.IsLeased) && pool.AvailableCount == available,
                    "Inactive owned pool lost requests before its next update.");
                encounter.gameObject.SetActive(true); yield return null; yield return null;
            }
            Check(actors.All(a => a != null && !a.IsLeased) && pool.PendingReturnCount == 0 && pool.AvailableCount == available + actors.Length, "Explicit parent return not completed exactly once.");
            Check(boss.IsLeased && boss.LeaseVersion == lease, "Parent disable returned the resumable boss.");
            if (!sameFrame) encounter.gameObject.SetActive(true);
            Pass(new { id = caseId, status = "PASS", actors = actors.Length, bossLease = lease, poolSuspendedWithArena, returnedExactlyOnce = true });
        }
        test = Spawn(false); while (test.MoveNext()) yield return null;
        caseId = "missing-live-pool-root-recovery"; actor = Adds()[0]; lease = actor.LeaseVersion;
        Transform root = pool.InactivePoolRoot; int prewarm = pool.DefaultPrewarmCount; available = pool.AvailableCount;
        pool.Configure(null, prewarm);
        try
        {
            service.Release(actor);
            Check(actor.IsLeased && pool.PendingReturnCount == 1 && Deferred(actor, lease), "Active normal release did not retain the missing-root request."); yield return null; yield return null;
            Check(actor.IsLeased && pool.PendingReturnCount == 1 && pool.AvailableCount == available
                && !string.IsNullOrEmpty((string)typeof(EnemyPoolService).GetProperty("LastReturnFailure").GetValue(pool)), "Missing root was falsely reported as returned.");
        }
        finally { pool.Configure(root, prewarm); }
        yield return null; yield return null; Check(!actor.IsLeased && pool.PendingReturnCount == 0, "Restored root did not complete retained request.");
        Pass(new { id = caseId, status = "PASS", diagnosticRuntimeRootReferenceOnly = true });

        test = Spawn(false); while (test.MoveNext()) yield return null;
        caseId = "normal-exit-with-12-adds"; actors = Adds(); var normal = encounter; normal.Exit(true);
        bool returnedBeforeArenaDestruction = actors.All(a => a != null && !a.IsLeased) && pool.PendingReturnCount == 0;
        encounter = null; yield return null; yield return null;
        bool poolDisposedWithArena = pool == null;
        Check(returnedBeforeArenaDestruction && (poolDisposedWithArena ? actors.All(a => a == null) : actors.All(a => a != null && !a.IsLeased)), "Normal exit ownership/arena teardown mismatch.");
        Pass(new { id = caseId, status = "PASS", actors = actors.Length, returnedBeforeArenaDestruction, poolDisposedWithArena });
        var hostAgain = CrustaspikanEncounterHost.Current;
        Warp(hostAgain.Entrance.transform.position + Vector3.back); Check(hostAgain.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "Actual reentry failed."); encounter = hostAgain.ActiveEncounter; Brain.ReviewMode = true;
        while (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); yield return null; }
        service = EnemySpawnService.Current; pool = service.Pool;
        test = Spawn(true); while (test.MoveNext()) yield return null;
        caseId = "encounter-root-destruction"; actors = Adds(); var ownedBoss = Boss; UnityEngine.Object.Destroy(encounter.gameObject);
        yield return null; yield return null; yield return null;
        Check(encounter == null && ownedBoss == null && actors.All(a => a == null) && pool.PendingReturnCount == 0, "Engine destruction/pending cleanup mismatch.");
        Pass(new { id = caseId, status = "PASS", actorsDestroyedByEngine = actors.Length, claimedPoolReturn = false });
    }
    IEnumerator Spawn(bool mixed)
    {
        caseId = "spawn-preparation"; Check(encounter.TryRestart(), "Actual restart failed."); Brain.ReviewMode = true;
        Warp(Boss.transform.position + Boss.Movement.PhysicalRotation * Vector3.forward * 17f + Vector3.up * .1f);
        if (mixed)
        {
            Boss.Health.TakeDamage(new DamageInfo(Boss.Health.MaxHp * .6f, Boss.transform.position, player.gameObject)); float deadline = Time.time + 12;
            while (Brain.Phase != 2 || Brain.IsTransitioning) { Check(Time.time < deadline, "Actual phase transition timeout."); yield return null; }
        }
        float settle = Time.time + .5f; while (Time.time < settle) yield return null;
        Check(Brain.StartPatternForReview(mixed ? "strong_spit" : "weak_spit"), "Actual spit rejected."); float until = Time.time + 25f;
        while (Brain.CurrentPatternId != "" || Brain.Composite.ActiveFlightCount > 0) { Check(Time.time < until, "Actual spit did not finish."); yield return null; }
        var actors = Adds(); Check(actors.Length >= 12 && (!mixed || actors.Select(a => a.Definition.EnemyId).Distinct().Count() >= 3), "Actual small/mixed roster missing.");
    }
    void Warp(Vector3 position) { var cc = player.CharacterController; bool on = cc.enabled; cc.enabled = false; player.transform.position = position; cc.enabled = on; player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms(); }
    void Pass(object row) { cases.Add(row); Write("RUNNING", null); }
    void Write(string status, string error) => File.WriteAllText(Path.Combine(output, "Play01/result.json"), JsonConvert.SerializeObject(new { status, error, stage = "D4", mode, caseId, utc = DateTime.UtcNow, cases,
        scope = "Actual portal, material summons, leases, pool and lifecycle; runtime root-reference failure labeled diagnostic", PlayerBuild = "FORBIDDEN", userManualFeel = "NOT_RUN", stoppedFromPlayerCallback = false }, Formatting.Indented));
}
