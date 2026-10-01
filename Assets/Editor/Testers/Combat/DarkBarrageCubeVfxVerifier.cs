using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class DarkBarrageCubeVfxVerifier
{
    private const string Key = "DarkBarrageCubeVfxVerifier";
    private static readonly List<string> checks = new List<string>(), errors = new List<string>();
    private static IEnumerator work;
    private static int frame;
    private static double deadline;
    private static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static DarkBarrageCubeVfxVerifier() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Idle Editor required");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene")
            throw new InvalidOperationException("PersistentScene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".status", "RUNNING");
        SessionState.SetBool(Key, true);
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
    }

    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); frame = -1;
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 120;
            work = Verify();
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            SessionState.SetBool(Key, false);
        }
    }

    private static void Log(string text, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text); }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("VFX verification timed out");
            if (!work.MoveNext()) Finish();
        }
        catch (Exception exception) { checks.Add("FAIL " + exception); Finish(); }
    }
    private static void Finish()
    {
        string status = errors.Count == 0 && checks.All(c => c.StartsWith("PASS ")) ? "PASS" : "FAIL";
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new {status, checks, errors}, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }
    private static void Check(bool pass, string description)
    { checks.Add((pass ? "PASS " : "FAIL ") + description); if (!pass) throw new InvalidOperationException(description); }

    private static Transform[] Views(string name) => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(t => t.name == name || t.name == name + "(Clone)").ToArray();
    private static bool Visible(Transform root) => root.gameObject.activeInHierarchy
        && root.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.particleCount > 0 && p.GetComponent<ParticleSystemRenderer>()?.enabled == true);

    private static IEnumerator Verify()
    {
        EnemySpawnService spawn = null;
        EnemyThemeTrialHarness arena = null;
        var leased = new List<EnemyActor>();
        MeleeRuntime melee = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current;
            var actor = PlayerContext.GetOrCreate().CurrentActor;
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase), "Isolated account");
            var heavy = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(DarkBarrageCubeVfxBuilder.HeavyPath);
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(DarkBarrageCubeVfxBuilder.ProjectilePath);
            var hit = AssetDatabase.LoadAssetAtPath<GameObject>(DarkBarrageCubeVfxBuilder.HitPath);
            var landing = AssetDatabase.LoadAssetAtPath<GameObject>(DarkBarrageCubeVfxBuilder.LandingPath);
            var landingDefinition = AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(DarkBarrageCubeVfxBuilder.ShockwaveDefinitionPath);
            Check(landing != null && landingDefinition.ResolvePrefab(WeaponElement.Dark) == landing,
                "Dark landing uses crimson prefab");
            Check(Enum.GetValues(typeof(WeaponElement)).Cast<WeaponElement>().Where(e => e != WeaponElement.Dark)
                .All(e => landingDefinition.ResolvePrefab(e) == landingDefinition.neutralPrefab), "Other elements retain neutral landing");
            Check(heavy.elementVfx.darkBarrageProjectile == body && heavy.elementVfx.darkBarrageHit == hit, "Heavy uses both Cube03 game prefabs");
            Check(!DarkBarrageScheduler.UsesTemporaryProjectile(heavy.elementVfx), "Temporary projectile replaced");
            Check(Mathf.Approximately(body.transform.localScale.x, .28f) && Mathf.Approximately(hit.transform.localScale.x, .4f),
                "Projectile 20 percent smaller, hit scale unchanged");
            Check(body.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 && body.GetComponentsInChildren<Collider>(true).Length == 0
                && body.GetComponentsInChildren<Rigidbody>(true).Length == 0, "Supplier movement and physics removed");
            Check(body.GetComponentsInChildren<TrailRenderer>(true).Length == 2, "Cube03 carries both authored trails");
            Check(hit.GetComponentsInChildren<ParticleSystem>(true).All(p => !p.main.loop && p.main.stopAction == ParticleSystemStopAction.None), "Hit completes naturally without self destruction");
            arena = EnemyThemeTrialHarness.Current;
            if (!arena.InArena) arena.ToggleArena();
            yield return null;
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service available");
            foreach (var table in arena.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), "Catalog " + error);
            var enemyDefinition = arena.tables.SelectMany(t => t.Entries).Select(e => e.definition)
                .First(d => d != null && d.EnemyId.Contains("Ceratoferox"));
            actor.Health.SetMaxHp(1000000, true);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Dark)), "Equip dark greatsword");
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee = player.GetComponent<MeleeRuntime>(); melee.SetManualInputEnabled(true);
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            float ready = Time.time + 1f; while (Time.time < ready) yield return null;
            // Keep the combat capture clear of the Hideout's temporary pickup display.
            var controller = player.GetComponent<CharacterController>();
            bool controllerEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            player.transform.position += Vector3.right * 20f;
            player.GetComponent<PlayerMovement>()?.ResetMotionAfterTeleport();
            if (controller != null) controller.enabled = controllerEnabled;
            ready = Time.time + .5f; while (Time.time < ready) yield return null;
            Vector3 origin = player.transform.position;
            for (int i = 0; i < 2; i++)
            {
                Check(spawn.TrySpawn(new EnemySpawnRequest(enemyDefinition, origin + new Vector3(i == 0 ? -2f : 2f, 0f, 6f),
                    Quaternion.LookRotation(Vector3.back), player.transform), out var enemy), "Spawn target " + i);
                leased.Add(enemy); enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(1000000, true);
            }
            yield return null;
            int sequence = 73000;
            var firstViews = new HashSet<int>();
            long firstHitCreated = 0;
            long firstLandingCreated = 0;
            for (int round = 0; round < 2; round++)
            {
                foreach (var enemy in leased)
                {
                    var status = enemy.GetComponent<ElementalStatusController>(); status.ClearAllStatuses();
                    for (int n = 0; n < 2; n++) status.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark, 10f,
                        player.gameObject, energy.WeaponInstanceId, true, false, enemy.transform.position, Vector3.forward));
                    Check(status.GetStackCount(WeaponElement.Dark) == 2, "Round " + round + " corrosion applied");
                }
                energy.Clear(); for (int n = 0; n < 10; n++) energy.RecordConfirmedHit(energy.WeaponInstanceId, energy.Element, ++sequence, 1);
                Check(Mathf.Approximately(energy.Amount, 100f), "Round " + round + " full energy");
                int casts = DarkBarrageScheduler.CastCount, hits = DarkBarrageScheduler.TotalHits;
                long requests = TransientVfxPool.GetStatistics(hit).Requests;
                long landingRequests = TransientVfxPool.GetStatistics(landing).Requests;
                float hp = leased.Sum(e => e.Health.CurrentHp);
                var accepted = WeaponActionResult.RejectedNotReady; float startDeadline = Time.time + 3f;
                while (Time.time < startDeadline)
                {
                    PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
                    accepted = melee.TryStartHeavyAttack(Vector3.forward);
                    if (accepted == WeaponActionResult.Accepted) break;
                    yield return null;
                }
                Check(accepted == WeaponActionResult.Accepted, "Round " + round + " actual heavy accepted");
                bool bodyVisible = false, hitVisible = false, landingVisible = false, bodyShot = false, hitShot = false, landingShot = false;
                float landingCaptureAt = float.PositiveInfinity;
                float end = Time.time + 12f;
                while (Time.time < end)
                {
                    bool liveBody = Views(body.name).Any(Visible), liveHit = Views(hit.name).Any(Visible);
                    bodyVisible |= liveBody; hitVisible |= liveHit;
                    bool liveLanding = Views(landing.name).Any(Visible);
                    landingVisible |= liveLanding;
                    if (liveLanding && float.IsPositiveInfinity(landingCaptureAt)) landingCaptureAt = Time.time + .12f;
                    if (round == 0 && liveLanding && !landingShot && Time.time >= landingCaptureAt)
                    { ScreenCapture.CaptureScreenshot(Path.Combine(Output, "landing.png")); landingShot = true; }
                    if (round == 0 && liveBody && !bodyShot) { ScreenCapture.CaptureScreenshot(Path.Combine(Output, "projectile.png")); bodyShot = true; }
                    if (round == 0 && liveHit && !hitShot) { ScreenCapture.CaptureScreenshot(Path.Combine(Output, "hit.png")); hitShot = true; }
                    if (DarkBarrageScheduler.CastCount > casts && DarkBarrageScheduler.ActiveCount == 0) break;
                    yield return null;
                }
                Check(DarkBarrageScheduler.CastCount == casts + 1 && DarkBarrageScheduler.ActiveCount == 0, "Round " + round + " barrage finished");
                Check(bodyVisible && hitVisible, "Round " + round + " visible projectile and hit particles");
                Check(landingVisible && TransientVfxPool.GetStatistics(landing).Requests == landingRequests + 1,
                    "Round " + round + " one visible crimson landing wave");
                Check(Views(landing.name).SelectMany(t => t.GetComponentsInChildren<Renderer>(true))
                    .All(r => r.sharedMaterial.GetColor("_Colour").r > r.sharedMaterial.GetColor("_Colour").g * 10f),
                    "Round " + round + " landing crimson survives playback");
                Check(DarkBarrageScheduler.LastShotCount == 6 && DarkBarrageScheduler.TotalHits == hits + 6, "Round " + round + " 4 normal and 2 finisher hits");
                Check(leased.Sum(e => e.Health.CurrentHp) < hp && Mathf.Approximately(energy.Amount, 0f), "Round " + round + " damage lands without energy recharge");
                Check(TransientVfxPool.GetStatistics(hit).Requests == requests + 6, "Round " + round + " matching hit VFX requests");
                float settle = Time.time + TransientVfxPool.ResolveLifetime(hit, 0f) + 1f;
                while (Time.time < settle && (Views(body.name).Any(t => t.gameObject.activeInHierarchy)
                    || TransientVfxPool.GetStatistics(hit).Active > 0)) yield return null;
                Check(!Views(body.name).Any(t => t.gameObject.activeInHierarchy) && TransientVfxPool.GetStatistics(hit).Active == 0, "Round " + round + " all VFX returned to pools");
                Check(TransientVfxPool.GetStatistics(landing).Active == 0, "Round " + round + " landing returned to pool");
                var ids = new HashSet<int>(Views(body.name).Select(t => t.GetInstanceID()));
                if (round == 0) { firstViews = ids; firstHitCreated = TransientVfxPool.GetStatistics(hit).Created; firstLandingCreated = TransientVfxPool.GetStatistics(landing).Created; }
                else Check(firstViews.SetEquals(ids) && firstHitCreated == TransientVfxPool.GetStatistics(hit).Created
                    && firstLandingCreated == TransientVfxPool.GetStatistics(landing).Created, "Second cast reuses projectile, hit and landing instances");
            }
        }
        finally
        {
            if (melee != null) melee.SetManualInputEnabled(false);
            if (spawn != null) foreach (var enemy in leased) if (enemy != null && enemy.IsLeased) spawn.Release(enemy);
            if (arena != null && arena.InArena) arena.ToggleArena();
        }
    }
}
