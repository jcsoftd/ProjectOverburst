using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CaveFallProtectionVerifier
{
    const string Key = "Overburst.CaveFallVerifier.";
    static readonly Vector3 Origin = new Vector3(2000, 80, 2000);
    static readonly List<string> checks = new List<string>();
    static IEnumerator scenario;
    static GameObject fixture;
    static double deadline;
    static bool background;
    static string output;

    static CaveFallProtectionVerifier()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "output", "")))
            EditorApplication.update += Tick;
    }
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
    static float XZ(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));

    public static object VerifyNative(string directory)
    {
        CavePlatformMapBuilder.Guard(); output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        var world = Object.FindFirstObjectByType<CaveWorld>();
        if (!world || !world.authoredLayout) throw new InvalidOperationException("Open an authored cave map first.");
        checks.Clear(); CaveFallProtection.Register(world);
        int routes = 0, edges = 0;
        try
        {
            foreach (var passage in world.passages)
                foreach (bool reverse in new[] { false, true })
                {
                    var points = reverse ? passage.points.Reverse().ToArray() : passage.points;
                    var guard = new CaveFallProtection.Movement(); var at = points[0];
                    foreach (var target in points)
                    {
                        at = guard.Resolve(at, target, .4f);
                        if (!guard.Active || XZ(at, target) > .3f)
                            throw new InvalidOperationException($"Blocked connector {passage.a}-{passage.b}, reverse={reverse}, error={XZ(at, target)}");
                    }
                    routes++;
                }
            foreach (var court in world.courts)
                for (int i = 0; i < 16; i++)
                {
                    var guard = new CaveFallProtection.Movement();
                    var d = new Vector3(Mathf.Cos(i * Mathf.PI / 8), 0, Mathf.Sin(i * Mathf.PI / 8));
                    var edge = guard.Resolve(court.center, court.center + d * 100, .4f);
                    if (!guard.Active || !world.Ground(edge, out _, 40)) throw new InvalidOperationException("Unsupported edge: " + court.name);
                    edges++;
                }
            Fixtures();
            var steady = new CaveFallProtection.Movement(); var start = world.courts[0].center;
            steady.Resolve(start, start, .4f);
            long allocations = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) steady.Resolve(start, start, .4f);
            watch.Stop(); allocations = GC.GetAllocatedBytesForCurrentThread() - allocations;
            Require(allocations < 256, "Steady boundary queries do not allocate per query");
            var result = new { status = "PASS", scene = world.gameObject.scene.path, routes, edges, steady1000Milliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocations, checks = checks.ToArray() };
            Write("native.json", result); return result;
        }
        finally { CaveFallProtection.Unregister(world); if (fixture) Object.DestroyImmediate(fixture); fixture = null; }
    }

    static CaveWorld MakeFixture()
    {
        fixture = new GameObject("Cave fall protection verification");
        var world = fixture.AddComponent<CaveWorld>();
        world.generatedRoot = new GameObject("Decks").transform; world.generatedRoot.SetParent(fixture.transform);
        Deck(world, "Start platform", Vector3.zero, new Vector3(8, 1, 8));
        Deck(world, "Bridge", new Vector3(7, 0, 0), new Vector3(6, 1, 1.6f));
        Deck(world, "Far platform", new Vector3(14, 0, 0), new Vector3(8, 1, 8));
        Deck(world, "Disconnected platform", new Vector3(0, 0, 12), new Vector3(8, 1, 8));
        for (int i = 0; i < 3; i++) Deck(world, "Stair " + i, new Vector3(19 + i * 2, i + 1, 0), new Vector3(2, 1, 2));
        Deck(world, "High platform", new Vector3(28, 3, 0), new Vector3(8, 1, 8));
        var scenery = Deck(world, "Lower scenery is not traversable", new Vector3(0, -12, 0), new Vector3(100, 1, 100));
        Object.DestroyImmediate(scenery.GetComponent<CaveWalkSurface>());
        CavePlatformBoundaryVerifier.AddFixtureBoundaries(world);
        Physics.SyncTransforms(); CaveFallProtection.Register(world);
        return world;
    }
    static GameObject Deck(CaveWorld world, string name, Vector3 top, Vector3 size)
    {
        var go = new GameObject(name); go.transform.SetParent(world.generatedRoot);
        go.transform.position = Origin + top - Vector3.up * size.y * .5f;
        go.AddComponent<BoxCollider>().size = size; go.AddComponent<CaveWalkSurface>(); return go;
    }
    static void Fixtures()
    {
        var world = MakeFixture();
        var guard = new CaveFallProtection.Movement();
        var edge = guard.Resolve(Origin, Origin + Vector3.forward * 12, .4f);
        Require(guard.Active && edge.z < Origin.z + 4 && edge.z > Origin.z + 3.5f, "Continuous sweep blocks a dash across a gap to another valid platform");
        var jumped = guard.Resolve(edge, edge + Vector3.up * 4 + Vector3.forward * 20, .4f);
        Require(jumped.z < Origin.z + 4 && jumped.y >= Origin.y + 3.9f, "Jump height survives while horizontal void traversal is blocked");
        var pushed = guard.Resolve(jumped + Vector3.forward * 12, jumped + Vector3.forward * 12 - Vector3.up * 20, .4f);
        Require(pushed.z < Origin.z + 4 && pushed.y >= Origin.y, "External displacement and below-floor recovery retain the last deck");
        guard = default;
        var across = guard.Resolve(Origin, Origin + Vector3.right * 14, .4f);
        Require(XZ(across, Origin + Vector3.right * 14) < .01f, "Bridge stays open");
        var upper = guard.Resolve(across, Origin + new Vector3(28, 3, 0), .4f);
        Require(XZ(upper, Origin + Vector3.right * 28) < .01f && Mathf.Abs(upper.y - Origin.y - 3) < .01f, "Three stair modules retain their elevation");
        var back = guard.Resolve(upper, Origin, .4f);
        Require(XZ(back, Origin) < .01f, "Descending stairs and return bridge stay open");
        Require(CaveFallProtection.TryDropOrigin(Origin + new Vector3(0, .4f, 4.4f), out var drop) && drop.z < Origin.z + 3.9f, "Offset loot origin outside rim moves back onto deck");
        Require(CaveFallProtection.TryDropLanding(Origin, Origin + Vector3.forward * 12, .05f, out var land) && land.z < Origin.z + 4 && land.y > Origin.y, "Item arc endpoint cannot cross a void or use scenery");
        Object.DestroyImmediate(fixture); fixture = null;
        guard = default;
        Require(guard.Resolve(Origin, Origin + Vector3.forward * 100, .4f) == Origin + Vector3.forward * 100, "Unloaded cave releases the constraint");
    }

    public static void StartPlay(string directory)
    {
        CavePlatformMapBuilder.Guard();
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Editor must be idle with a saved scene.");
        output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 100);
        SessionState.SetBool(Key + "finished", false);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneManager.GetActiveScene().path);
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "Account"));
    }
    static void Tick()
    {
        output = SessionState.GetString(Key + "output", "");
        if (string.IsNullOrEmpty(output)) { EditorApplication.update -= Tick; return; }
        deadline = SessionState.GetFloat(Key + "deadline", 0);
        if (SessionState.GetBool(Key + "finished", false))
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            { if (EditorApplication.timeSinceStartup > deadline + 120) EditorApplication.update -= Tick; return; }
            var ownAccount = Path.Combine(output, "Account");
            string active = IsolatedSavePlayGuard.ActiveDirectory;
            string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
            if ((!string.IsNullOrEmpty(active) && active != ownAccount) || (!string.IsNullOrEmpty(current) && current != ownAccount))
            { EditorApplication.update -= Tick; return; }
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "startScene", ""));
            IsolatedSavePlayGuard.UseRealAccount();
            Write("return.json", new { status = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) ? "PASS" : "FAIL", scene = SceneManager.GetActiveScene().path, dirty = SceneManager.GetActiveScene().isDirty });
            SessionState.EraseString(Key + "output"); SessionState.EraseString(Key + "startScene"); SessionState.EraseFloat(Key + "deadline"); SessionState.EraseBool(Key + "finished");
            EditorApplication.update -= Tick; return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Cave fall Play verification");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (scenario == null) { checks.Clear(); background = Application.runInBackground; Application.runInBackground = true; scenario = Play(); }
            EditorApplication.QueuePlayerLoopUpdate();
            if (scenario.MoveNext()) return;
            Finish(null);
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static void Finish(string error)
    {
        Write("play.json", new { status = error == null ? "PASS" : "FAIL", error, checks = checks.ToArray() });
        if (fixture) Object.DestroyImmediate(fixture); fixture = null; scenario = null;
        Application.runInBackground = background;
        SessionState.SetBool(Key + "finished", true);
        EditorApplication.ExitPlaymode();
    }

    static IEnumerator Play()
    {
        MakeFixture();
        var player = new GameObject("Player motor test"); player.transform.SetParent(fixture.transform); player.transform.position = Origin;
        var cc = player.AddComponent<CharacterController>(); cc.center = Vector3.up; cc.height = 2; cc.radius = .4f;
        var motor = player.AddComponent<OverburstCharacterMotor3D>();
        motor.Configure(new CharacterMotorSettings { skinWidthRadiusRatio = .1f, slopeLimit = 45, stepOffset = .3f, groundLayer = ~0, gravity = -25, groundStickVelocity = -2, maxFallSpeed = 60, groundProbeStartOffset = .35f, groundSnapDistance = .3f, groundCheckRadius = .3f, maxExternalSpeed = 30 });
        Physics.SyncTransforms(); motor.MoveDirect(Vector3.forward * 20);
        Require(player.transform.position.z < Origin.z + 4 && player.transform.position.z > Origin.z + 3.4f, "Player MoveDirect / attack displacement stops at rim");
        motor.BeginEvadeMotion(); motor.MoveEvade(Vector3.forward * 8); motor.EndEvadeMotion();
        Require(player.transform.position.z < Origin.z + 4, "Player evade stays on platform");
        motor.MoveDirect(Vector3.up * 3 + Vector3.forward * 5);
        Require(player.transform.position.y > Origin.y + 2.8f && player.transform.position.z < Origin.z + 4, "Player airborne movement stays over platform");
        motor.MoveDirect(Vector3.down * 10);
        Require(player.transform.position.y >= Origin.y - .05f, "Player vertical recovery keeps feet on platform");
        cc.enabled = false; player.transform.position = Origin; cc.enabled = true; motor.ResetMotion(); Physics.SyncTransforms();
        motor.MoveDirect(Vector3.right * 14);
        Require(XZ(player.transform.position, Origin + Vector3.right * 14) < .2f, "Player motor crosses the connecting bridge");
        cc.enabled = false; player.transform.position = Origin + Vector3.forward * 3.5f; cc.enabled = true; motor.ResetMotion();
        player.SetActive(false);

        var enemy = new GameObject("Enemy motor test"); enemy.transform.SetParent(fixture.transform); enemy.transform.position = Origin;
        var cap = enemy.AddComponent<CapsuleCollider>(); cap.center = Vector3.up; cap.height = 2; cap.radius = .4f;
        var body = enemy.AddComponent<Rigidbody>(); body.constraints = RigidbodyConstraints.FreezeRotation; body.useGravity = false;
        var em = enemy.AddComponent<EnemyMotor>(); Physics.SyncTransforms();
        em.MoveToPosition(Origin + Vector3.forward * 20);
        yield return null; yield return null;
        Require(body.position.z < Origin.z + 4 && body.position.z > Origin.z + 3.4f, "Dynamic enemy movement / knockback is constrained");
        body.linearVelocity = Vector3.forward * 300;
        float end = Time.time + .15f; while (Time.time < end) yield return null;
        Require(body.position.z < Origin.z + 4 && body.position.y >= Origin.y - .05f, "Physics impulse cannot push enemy off deck");
        body.isKinematic = true; em.MoveToPosition(Origin + Vector3.forward * 20, 180, Vector3.forward);
        Require(enemy.transform.position.z < Origin.z + 4, "Kinematic enemy movement uses the same boundary");
        enemy.SetActive(false);

        var random = UnityEngine.Random.state;
        try
        {
            for (int i = 0; i < 32; i++)
            {
                var item = new GameObject("Drop " + i); item.transform.SetParent(fixture.transform);
                item.transform.position = Origin + new Vector3(0, .4f, 4.2f);
                var presentation = item.AddComponent<WorldPickupPresentation>();
                typeof(WorldPickupPresentation).GetField("scatterRadius", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(presentation, 2f);
                var motion = item.AddComponent<WorldItemDropMotion>(); int landed = 0; motion.Landed += () => landed++;
                motion.Begin(); motion.SettleImmediately(); motion.SettleImmediately();
                Require(item.transform.position.z < Origin.z + 4 && item.transform.position.y >= Origin.y && landed == 1, "Drop stays on deck and emits one landing event " + i);
                Object.DestroyImmediate(item);
            }
        }
        finally { UnityEngine.Random.state = random; }
        var gold = CurrencyItemRegistry.Get(CurrencyType.Gold);
        Require(gold != null, "Actual currency data exists");
        for (int i = 0; i < 2; i++)
        {
            var currency = WorldItemDropFactory.CreateCurrencyWorldPickup(gold, 3, Origin + new Vector3(0, .4f, 4.2f), null);
            Require(currency != null, "Currency factory creates a real pooled pickup");
            currency.GetComponent<WorldItemDropMotion>().SettleImmediately();
            Require(currency.transform.position.z < Origin.z + 4 && currency.transform.position.y >= Origin.y, "Pooled currency lands on the deck");
            if (!CurrencyPickupPool.TryReturn(currency)) Object.DestroyImmediate(currency.gameObject);
        }
        var invRequest = new InventoryWorldDropRequest { FallbackTransform = player.transform, ForwardDistance = 20, SpawnHeight = .2f };
        var dropPoint = (Vector3)typeof(InventoryWorldDrop).GetMethod("GetWorldDropPosition", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { invRequest });
        Require(dropPoint.z < Origin.z + 4 && dropPoint.z > Origin.z + 3.4f && Mathf.Abs(dropPoint.x - Origin.x) < .1f, "Inventory forward drop cannot spawn beyond rim");
        var lootOwner = new GameObject("Tall loot source"); lootOwner.transform.SetParent(fixture.transform); lootOwner.transform.position = Origin;
        var loot = lootOwner.AddComponent<EnemyLootDropper>();
        var tallDrop = (Vector3)typeof(EnemyLootDropper).GetMethod("ConstrainCaveDrop", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(loot, new object[] { Origin + new Vector3(0, 8, 9) });
        Require(tallDrop.z < Origin.z + 4 && tallDrop.y >= Origin.y + 8, "Tall monster loot uses the source deck before scattering");
        var guardObject = new GameObject("Run fall guard"); guardObject.transform.SetParent(fixture.transform); guardObject.transform.position = Origin;
        var runGuard = guardObject.AddComponent<RunFallGuard>();
        runGuard.Configure(new RunWalkableArea(new bool[1, 1], 1, 1, 0, 0, 1), RunFallGuardMode.TeleportToRespawn, Vector3.zero, Origin.y + 10);
        yield return null;
        Require(XZ(guardObject.transform.position, Origin) < .1f && guardObject.transform.position.y >= Origin.y, "Cave boundary takes priority over legacy run fall plane");
    }
}
