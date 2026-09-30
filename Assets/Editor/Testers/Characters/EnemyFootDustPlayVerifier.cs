using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EnemyFootDustPlayVerifier
{
    private const string Key = "EnemyFootDustPlayVerifier";
    private static readonly string[] Representatives =
    {
        "VenomBrood_Venodonte_Tint1", // Light
        "PrimalHunt_Dimaxillosaurus", // Standard
        "CavernMutants_Ursacetus" // Heavy, existing elite audio/camera
    };
    private static readonly List<string> lines = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static IEnumerator work;
    private static bool stopping;
    private static int lastFrame;
    private static double deadline;
    private static bool previousBackground;
    private static int previousFrameRate;

    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");
    public static string Phase => SessionState.GetString(Key + ".phase", "IDLE");

    static EnemyFootDustPlayVerifier() => EditorApplication.playModeStateChanged += Changed;

    [MenuItem("OVERBURST/Enemies/Validate Shared Foot Dust Play")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Editor is already playing");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Require(scene.name == PersistentSceneFlow.PersistentSceneName && !scene.isDirty,
            "Open the saved PersistentScene");
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key + ".continue", false);
        SessionState.SetString(Key + ".result", "RUNNING");
        SessionState.SetString(Key + ".phase", "STARTING");
        EditorApplication.EnterPlaymode();
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            previousBackground = Application.runInBackground;
            previousFrameRate = Application.targetFrameRate;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            lines.Clear();
            errors.Clear();
            stopping = false;
            work = Verify();
            lastFrame = -1;
            deadline = EditorApplication.timeSinceStartup + 210;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            stopping = true;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            (work as IDisposable)?.Dispose();
            work = null;
            Application.runInBackground = previousBackground;
            Application.targetFrameRate = previousFrameRate;
            if (LastResult == "RUNNING") SaveResult("FAIL interrupted");
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            SessionState.SetString(Key + ".phase", "DONE");
            Debug.Log("[EnemyFootDustPlay] " + LastResult);
        }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
    }

    private static void Tick()
    {
        if (stopping || !EditorApplication.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Timed out at " + Phase);
            if (work.MoveNext()) return;
            Require(errors.Count == 0, "Unity errors: " + string.Join(" | ", errors));
            Finish("PASS representatives, shared particles, pool reuse, crowd, budget");
        }
        catch (Exception exception) { Finish("FAIL " + exception); }
    }

    private static void Finish(string result)
    {
        stopping = true;
        SaveResult(result);
        EditorApplication.update -= Tick;
        (work as IDisposable)?.Dispose();
        work = null;
        EditorApplication.ExitPlaymode();
    }

    private static void SaveResult(string result)
    {
        SessionState.SetString(Key + ".result", result);
        lines.Add("result=" + result);
        string folder = OutputFolder();
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "play-verification.txt"), lines);
    }

    private static string OutputFolder()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(project, "..", "개인파일", "코덱스산출",
            "MonsterFootDust", "20260923_111748"));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerator Verify()
    {
        EnemyThemeTrialHarness ui = null;
        EnemySpawnService spawn = null;
        var active = new List<EnemyActor>(64);
        try
        {
            SessionState.SetString(Key + ".phase", "LOADING");
            float until = Time.realtimeSinceStartup + 45f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            {
                Require(Time.realtimeSinceStartup < until, "Hideout did not load");
                yield return null;
            }

            var player = PlayerInputFacade.Current;
            Require(player != null, "Player missing");
            ui = EnemyThemeTrialHarness.Current;
            Require(ui != null, "Theme debug UI missing");
            ui.ToggleArena();
            Require(ui.InArena, "Arena entry failed");
            Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn),
                "Spawn service missing");
            var dust = UnityEngine.Object.FindFirstObjectByType<EnemyFootDustVfx>();
            var runtime = UnityEngine.Object.FindFirstObjectByType<EnemyFootfallRuntime>();
            Require(dust != null && runtime != null, "Shared footstep services missing");
            Require(dust.GetComponentsInChildren<ParticleSystem>(true).Length == 3,
                "Expected exactly three shared particle systems");
            lines.Add("particleSystems=3");
            int baseTrackers = runtime.ActiveTrackers;

            var definitions = ui.tables.SelectMany(t => t.Entries)
                .Where(e => e.definition != null)
                .GroupBy(e => e.definition.EnemyId)
                .ToDictionary(group => group.Key, group => group.First().definition);
            foreach (var table in ui.tables)
                Require(spawn.RegisterAdditionalCatalog(table.Catalog, out string error),
                    "Catalog: " + error);

            for (int i = 0; i < Representatives.Length; i++)
            {
                string id = Representatives[i];
                SessionState.SetString(Key + ".phase", "REPRESENTATIVE " + id);
                EnemyActor actor = Spawn(spawn, definitions[id], player.transform,
                    player.transform.position + Vector3.forward * 9f);
                active.Add(actor);
                bool elite = actor.GetComponent<EnemyEliteFootstepEmitter>() != null;
                int trackers = runtime.ActiveTrackers;
                Require(elite ? trackers == baseTrackers : trackers == baseTrackers + 1,
                    "Incorrect tracker registration " + id + " count=" + trackers);
                actor.AI.enabled = false;
                actor.Movement.SetDestination(player.transform.position + Vector3.back * 5f,
                    .1f, EnemyLocomotionMode.Run);
                int beforeDust = dust.EmittedBursts;
                int beforeContact = elite ? actor.GetComponent<EnemyEliteFootstepEmitter>().ContactCount
                    : runtime.ContactCount;
                float start = Time.realtimeSinceStartup;
                while (dust.EmittedBursts == beforeDust ||
                    (elite ? actor.GetComponent<EnemyEliteFootstepEmitter>().ContactCount
                        : runtime.ContactCount) == beforeContact)
                {
                    Require(Time.realtimeSinceStartup - start < 14f,
                        $"No foot dust for {id}, mode={actor.Movement.LocomotionMode}, " +
                        $"destination={actor.Movement.HasDestination}, contacts={runtime.ContactCount}, " +
                        $"groundMisses={dust.GroundMisses}, visibilityDrops={dust.VisibilityDrops}");
                    yield return null;
                }
                lines.Add($"representative={id} elite={elite} emitted={dust.EmittedBursts-beforeDust} " +
                    $"contact={(elite ? actor.GetComponent<EnemyEliteFootstepEmitter>().ContactCount : runtime.ContactCount)-beforeContact}");
                if (elite)
                {
                    var feel = UnityEngine.Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                    Require(feel != null && feel.PlayedCount > 0, "Elite audio Feel missing");
                    Require(feel.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0),
                        "Duplicate elite dust particles");
                }
                spawn.Release(actor);
                active.Remove(actor);
                yield return null;
                Require(runtime.ActiveTrackers == baseTrackers, "Pooled tracker remained " + id);
            }

            // Arrange examples in camera space so their feet are visible together.
            string[] visualIds = { Representatives[0], Representatives[1], Representatives[2] };
            int hiddenPickups = 0;
            foreach (WorldItemPickup pickup in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(
                FindObjectsSortMode.None))
            {
                if (pickup == null || (pickup.transform.position - player.transform.position).sqrMagnitude > 30f * 30f)
                    continue;
                pickup.gameObject.SetActive(false);
                hiddenPickups++;
            }
            lines.Add("hiddenNearbyTestPickups=" + hiddenPickups);
            Camera gameCamera = QuarterViewCamera.ActiveInstance.GetComponent<Camera>();
            Require(gameCamera != null, "Game camera missing");
            int mutedParticles = 0;
            ParticleSystem[] footDustSystems = dust.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particle in UnityEngine.Object.FindObjectsByType<ParticleSystem>(
                FindObjectsSortMode.None))
            {
                if (particle == null || footDustSystems.Contains(particle)) continue;
                var emission = particle.emission;
                emission.enabled = false;
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                mutedParticles++;
            }
            lines.Add("mutedUnrelatedTestParticles=" + mutedParticles);
            Vector3 cameraForward = Vector3.ProjectOnPlane(gameCamera.transform.forward,
                Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(gameCamera.transform.right,
                Vector3.up).normalized;
            Require(cameraForward.sqrMagnitude > .5f && cameraRight.sqrMagnitude > .5f,
                "Camera ground vectors missing");
            for (int i = 0; i < visualIds.Length; i++)
            {
                float side = (i - 1) * 2.4f;
                EnemyActor actor = Spawn(spawn, definitions[visualIds[i]], player.transform,
                    player.transform.position + cameraForward * 2.5f + cameraRight * side);
                active.Add(actor);
                actor.AI.enabled = false;
                actor.Movement.SetDestination(player.transform.position +
                    cameraForward * 2f + cameraRight * side, .1f, EnemyLocomotionMode.Run);
            }
            SessionState.SetString(Key + ".phase", "VISUAL");
            float visualStart = Time.realtimeSinceStartup;
            float nextTurn = visualStart + 2f;
            bool farDestination = true;
            bool captured = false;
            int visualStartBursts = dust.EmittedBursts;
            int visualPeakParticles = 0;
            bool sampledParticles = false;
            var particleBuffer = new ParticleSystem.Particle[160];
            float visualPeakAlpha = 0f;
            var captureRoot = new GameObject("EnemyFootDustTestCapture");
            var frameCapture = captureRoot.AddComponent<EnemyFootDustFrameCapture>();
            var tierCaptures = new EnemyFootDustFrameCapture[3];
            var tierCaptureStarted = new bool[3];
            for (int tier = 0; tier < tierCaptures.Length; tier++)
                tierCaptures[tier] = captureRoot.AddComponent<EnemyFootDustFrameCapture>();
            while (Time.realtimeSinceStartup - visualStart < 8f)
            {
                int visibleParticles = footDustSystems.Sum(system => system.particleCount);
                visualPeakParticles = Mathf.Max(visualPeakParticles, visibleParticles);
                float maxAlpha = 0f;
                for (int tier = 0; tier < footDustSystems.Length; tier++)
                {
                    ParticleSystem system = footDustSystems[tier];
                    int particleCount = system.GetParticles(particleBuffer);
                    float tierAlpha = 0f;
                    for (int particleIndex = 0; particleIndex < particleCount; particleIndex++)
                        tierAlpha = Mathf.Max(tierAlpha,
                            particleBuffer[particleIndex].GetCurrentColor(system).a / 255f);
                    maxAlpha = Mathf.Max(maxAlpha, tierAlpha);
                    if (!tierCaptureStarted[tier]
                        && Time.realtimeSinceStartup - visualStart >= 1.5f && tierAlpha >= .35f)
                    {
                        tierCaptureStarted[tier] = true;
                        tierCaptures[tier].Begin(Path.Combine(OutputFolder(),
                            "foot-dust-" + new[] { "light", "standard", "heavy" }[tier] + ".png"));
                    }
                }
                visualPeakAlpha = Mathf.Max(visualPeakAlpha, maxAlpha);
                if (!sampledParticles && Time.realtimeSinceStartup - visualStart >= 2f
                    && visibleParticles >= 3)
                {
                    sampledParticles = true;
                    foreach (ParticleSystem system in footDustSystems)
                    {
                        var particles = new ParticleSystem.Particle[160];
                        int particleCount = system.GetParticles(particles);
                        if (particleCount == 0) continue;
                        ParticleSystem.Particle particle = particles[0];
                        var renderer = system.GetComponent<ParticleSystemRenderer>();
                        lines.Add($"visualParticle {system.name} n={particleCount} " +
                            $"pos={particle.position} viewport={gameCamera.WorldToViewportPoint(particle.position)} " +
                            $"size={particle.GetCurrentSize(system):F2} color={particle.GetCurrentColor(system)} " +
                            $"rendererVisible={renderer.isVisible} bounds={renderer.bounds}");
                    }
                }
                if (!captured && Time.realtimeSinceStartup - visualStart >= 2f
                    && dust.EmittedBursts > visualStartBursts && visibleParticles >= 3
                    && maxAlpha >= .35f)
                {
                    captured = true;
                    foreach (ParticleSystem particle in UnityEngine.Object.FindObjectsByType<ParticleSystem>(
                        FindObjectsSortMode.None))
                    {
                        if (particle == null || footDustSystems.Contains(particle)) continue;
                        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    lines.Add($"screenshotParticleAlpha={maxAlpha:F2}");
                    frameCapture.Begin(Path.Combine(OutputFolder(), "foot-dust-gameplay.png"));
                }
                if (Time.realtimeSinceStartup >= nextTurn)
                {
                    for (int i = 0; i < active.Count; i++)
                        active[i].Movement.SetDestination(player.transform.position +
                            cameraForward * (farDestination ? 6f : 2f)
                            + cameraRight * ((i - 1) * 2.4f),
                            .1f, EnemyLocomotionMode.Run);
                    farDestination = !farDestination;
                    nextTurn += 2f;
                }
                yield return null;
            }
            lines.Add($"visual emitted={dust.EmittedBursts - visualStartBursts} " +
                $"peakParticles={visualPeakParticles} peakAlpha={visualPeakAlpha:F2}");
            lines.Add("screenshot=" + (frameCapture.Done ? "foot-dust-gameplay.png" :
                "FAIL " + frameCapture.Error));
            Require(frameCapture.Done, "Visual frame was not captured: " + frameCapture.Error);
            for (int tier = 0; tier < tierCaptures.Length; tier++)
            {
                string tierName = new[] { "light", "standard", "heavy" }[tier];
                lines.Add($"tierScreenshot {tierName} started={tierCaptureStarted[tier]} " +
                    $"done={tierCaptures[tier].Done} error={tierCaptures[tier].Error}");
                Require(tierCaptures[tier].Done, "Missing " + tierName + " visual frame");
            }
            UnityEngine.Object.Destroy(captureRoot);
            foreach (EnemyActor actor in active) spawn.Release(actor);
            active.Clear();
            yield return null;
            Require(runtime.ActiveTrackers == baseTrackers,
                "Trackers remained after visual examples");

            SessionState.SetString(Key + ".phase", "CROWD");
            int spawned = 0;
            for (int i = 0; i < 50; i++)
            {
                string id = i % 5 == 0 ? Representatives[1] : Representatives[0];
                int column = i % 10;
                int row = i / 10;
                Vector3 point = player.transform.position +
                    new Vector3((column - 4.5f) * 1.6f, 0f, 8f + row * 1.7f);
                EnemyActor actor = Spawn(spawn, definitions[id], player.transform, point);
                active.Add(actor);
                actor.AI.enabled = false;
                actor.Movement.SetDestination(player.transform.position +
                    new Vector3((column - 4.5f) * 1.6f, 0f, -4f), .1f,
                    EnemyLocomotionMode.Run);
                spawned++;
                if (i % 5 == 4) yield return null;
            }
            Require(runtime.ActiveTrackers == baseTrackers + spawned,
                "Crowd tracker count " + runtime.ActiveTrackers);
            int crowdBefore = dust.EmittedBursts;
            int contactBeforeCrowd = runtime.ContactCount;
            int maxParticles = 0;
            ParticleSystem[] sharedSystems = dust.GetComponentsInChildren<ParticleSystem>(true);
            float crowdStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - crowdStart < 5f)
            {
                int particles = 0;
                foreach (ParticleSystem system in sharedSystems)
                    particles += system.particleCount;
                maxParticles = Mathf.Max(maxParticles, particles);
                yield return null;
            }
            lines.Add($"crowd spawned={spawned} contacts={runtime.ContactCount-contactBeforeCrowd} " +
                $"emitted={dust.EmittedBursts-crowdBefore} maxParticles={maxParticles} " +
                $"budgetDrops={dust.BudgetDrops} groundMisses={dust.GroundMisses}");
            Require(runtime.ContactCount > contactBeforeCrowd && dust.EmittedBursts > crowdBefore,
                "Crowd did not make foot dust");
            foreach (EnemyActor actor in active) spawn.Release(actor);
            active.Clear();
            yield return null;
            Require(runtime.ActiveTrackers == baseTrackers,
                "Crowd trackers remained after pooling");

            // Synchronously flood one frame to prove the global per-frame budget.
            SessionState.SetString(Key + ".phase", "BUDGET");
            Vector3 testPoint = player.transform.position + Vector3.forward * 3f;
            Require(Physics.Raycast(testPoint + Vector3.up * 4f, Vector3.down,
                out RaycastHit floor, 9f, LayerMask.GetMask("Default", "Environment", "Ground"),
                QueryTriggerInteraction.Ignore), "Budget test floor missing");
            int budgetBefore = dust.BudgetDrops;
            for (int i = 0; i < 40; i++)
                EnemyFootDustVfx.TryEmit(floor.point, floor.normal, Vector3.forward,
                    EnemyHitWeight.Light, 3f, floor.collider);
            Require(dust.BudgetDrops > budgetBefore, "Per-frame burst budget did not cap");
            lines.Add("budgetFloodDrops=" + (dust.BudgetDrops - budgetBefore));
            lines.Add("unityErrors=" + errors.Count);
        }
        finally
        {
            if (spawn != null)
                foreach (EnemyActor actor in active) if (actor != null) spawn.Release(actor);
            if (ui != null && ui.InArena)
            {
                ui.Clear();
                ui.ToggleArena();
            }
        }
    }

    private static EnemyActor Spawn(EnemySpawnService spawn, EnemyDefinition definition,
        Transform player, Vector3 location)
    {
        Require(Physics.Raycast(location + Vector3.up * 4f, Vector3.down,
            out RaycastHit floor, 9f, LayerMask.GetMask("Default", "Environment", "Ground"),
            QueryTriggerInteraction.Ignore), "Arena floor " + definition.EnemyId);
        var request = new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f,
            Quaternion.identity, player, null, player, null, 1f, 1f, 91);
        Require(spawn.TrySpawn(request, out EnemyActor actor), "Spawn " + definition.EnemyId);
        return actor;
    }

}

public sealed class EnemyFootDustFrameCapture : MonoBehaviour
{
    public bool Done { get; private set; }
    public string Error { get; private set; } = "not started";

    public void Begin(string path) => StartCoroutine(Capture(path));

    private IEnumerator Capture(string path)
    {
        yield return new WaitForEndOfFrame();
        try
        {
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (frame == null) throw new InvalidOperationException("Screen capture returned null");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, frame.EncodeToPNG());
            UnityEngine.Object.Destroy(frame);
            Done = true;
            Error = string.Empty;
        }
        catch (Exception exception) { Error = exception.Message; }
    }
}
