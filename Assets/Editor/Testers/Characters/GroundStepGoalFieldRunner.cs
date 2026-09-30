using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Local-only Play check; does not save the shared scene.
public static class GroundStepGoalFieldRunner
{
    private static readonly string[] GroundedIds =
    {
        "SpiderBrood_Rostrokarck", "VenomBrood_Kupolobrach_Tint_Orange",
        "PrimalHunt_Occisodonte", "CavernMutants_Ursacetus",
        "SpiderBrood_Scolokarck_Tint3", "SpiderBrood_Carcinoptera",
        "VenomBrood_Kupolojuve_Tint_Orange", "PrimalHunt_Dimaxillosaurus",
        "PrimalHunt_Venosaur_Tint_Brown", "CavernMutants_Gasterobrach",
        "CavernMutants_Gorhorrid", "DeathHarvest_DeathKnight"
    };
    private static readonly List<object> Rows = new List<object>();
    private static IEnumerator work;
    private static bool previousBackground;
    private static int previousFrameRate;
    private static int lastFrame;
    private static double deadline;
    private static string output;
    public static string Result { get; private set; } = "IDLE";

    public static void Start(string label)
    {
        if (!EditorApplication.isPlaying || work != null)
            throw new InvalidOperationException("Requires idle Play Mode");
        output = Path.GetFullPath("../개인파일/코덱스산출/MonsterFeel/20260924_GoalFEEL01/" + label);
        Directory.CreateDirectory(output);
        Rows.Clear();
        previousBackground = Application.runInBackground;
        previousFrameRate = Application.targetFrameRate;
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        Result = "RUNNING";
        deadline = EditorApplication.timeSinceStartup + 300;
        lastFrame = -1;
        work = Run();
        EditorApplication.update += Tick;
    }

    private static IEnumerator Run()
    {
        var ui = Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);
        var player = PlayerInputFacade.Current;
        var camera = QuarterViewCamera.ActiveInstance;
        var runtime = Object.FindFirstObjectByType<EnemyFootfallRuntime>();
        var dust = Object.FindFirstObjectByType<EnemyFootDustVfx>();
        if (!ui || !player || !camera || !runtime || !dust)
            throw new InvalidOperationException("Missing arena, player, camera, footfall, or dust service");
        ui.gameObject.SetActive(true);
        float loadUntil = Time.time + 15f;
        while ((PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
               && Time.time < loadUntil) yield return null;
        if (PersistentSceneFlow.Instance == null
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            throw new InvalidOperationException("Hideout did not load");
        bool entered = !ui.InArena;
        if (entered) ui.ToggleArena();
        if (!ui.InArena) throw new InvalidOperationException("Arena did not open");
        EnemyActor actor = null;
        EnemySpawnService spawn = null;
        try
        {
            float ready = Time.time + .7f;
            while (Time.time < ready) yield return null;
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn))
                throw new InvalidOperationException("Spawn service missing");
            foreach (var table in ui.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
            foreach (string id in GroundedIds.Concat(new[]
                     { "SpiderBrood_RostrokarckLarvae", "DeathHarvest_Reaper" }))
            {
                var definition = ui.tables.SelectMany(t => t.Entries)
                    .First(e => e.definition != null && e.definition.EnemyId == id).definition;
                bool light = id == "SpiderBrood_RostrokarckLarvae";
                bool airborne = id == "DeathHarvest_Reaper";
                float spawnDistance = light || airborne ? 3f : 8f;
                Vector3 location = player.transform.position + Vector3.forward * spawnDistance;
                if (!Physics.Raycast(location + Vector3.up * 4f, Vector3.down, out RaycastHit floor,
                        9f, LayerMask.GetMask("Default", "Environment", "Ground"),
                        QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException("Arena floor missing: " + id);
                location = floor.point + Vector3.up * .035f;
                var request = new EnemySpawnRequest(definition, location, Quaternion.identity,
                    player.transform, null, player.transform, null, 1f, 1f, 91);
                int trackersBeforeSpawn = runtime.ActiveTrackers;
                if (!spawn.TrySpawn(request, out actor))
                    throw new InvalidOperationException("Spawn failed: " + id);
                var emitter = actor.GetComponent<EnemyEliteFootstepEmitter>();
                var profile = Resources.Load<EnemyFootfallProfile>("Enemies/Themes/Footfalls/" + id);
                EnemyGroundStepTier tier = profile != null ? profile.GroundStepTier : EnemyGroundStepTier.None;
                if (!light && !airborne && tier == EnemyGroundStepTier.None)
                    throw new InvalidOperationException("No impact tier: " + id);
                actor.AI.enabled = false;
                int contactBefore = emitter != null ? emitter.ContactCount : runtime.ContactCount;
                var feel = Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                int eliteBefore = feel != null ? feel.ElitePlayedCount : 0;
                int mediumBefore = feel != null ? feel.MediumPlayedCount : 0;
                int cameraBefore = camera.GroundStepEmissionCount;
                int dustBefore = dust.EmittedBursts;
                int trackersAfterSpawn = runtime.ActiveTrackers;
                Vector3 start = actor.transform.position;
                actor.Movement.SetDestination(player.transform.position + Vector3.back * 5f,
                    .1f, EnemyLocomotionMode.Run);
                float until = Time.time + (airborne ? 3f : 11f);
                float nearest = float.PositiveInfinity;
                while (Time.time < until)
                {
                    if (!actor.IsLeased || actor.Health == null || actor.Health.IsDead)
                        throw new InvalidOperationException("Actor released early: " + id);
                    Vector3 delta = actor.transform.position - player.transform.position;
                    delta.y = 0f;
                    nearest = Mathf.Min(nearest, delta.magnitude);
                    feel = Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                    int contacts = (emitter != null ? emitter.ContactCount : runtime.ContactCount) - contactBefore;
                    int tierPlayed = tier == EnemyGroundStepTier.Elite
                        ? feel != null ? feel.ElitePlayedCount - eliteBefore : 0
                        : feel != null ? feel.MediumPlayedCount - mediumBefore : 0;
                    if (airborne) { yield return null; continue; }
                    if (light ? contacts >= 2 && dust.EmittedBursts > dustBefore
                        : contacts >= 2 && tierPlayed > 0
                          && camera.GroundStepEmissionCount > cameraBefore)
                        break;
                    yield return null;
                }
                feel = Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                int contactDelta = (emitter != null ? emitter.ContactCount : runtime.ContactCount) - contactBefore;
                int eliteDelta = feel != null ? feel.ElitePlayedCount - eliteBefore : 0;
                int mediumDelta = feel != null ? feel.MediumPlayedCount - mediumBefore : 0;
                int cameraDelta = camera.GroundStepEmissionCount - cameraBefore;
                int dustDelta = dust.EmittedBursts - dustBefore;
                float moved = Vector3.ProjectOnPlane(actor.transform.position - start, Vector3.up).magnitude;
                if (airborne)
                {
                    if (emitter != null || profile != null
                        || trackersAfterSpawn != trackersBeforeSpawn
                        || eliteDelta != 0 || mediumDelta != 0 || cameraDelta != 0 || dustDelta != 0)
                        throw new InvalidOperationException("Airborne Reaper produced footfall: " + id);
                }
                else if (light)
                {
                    if (contactDelta < 2 || cameraDelta != 0 || eliteDelta != 0 || mediumDelta != 0)
                        throw new InvalidOperationException("Light actor had ground rumble: " + id);
                }
                else if (contactDelta < 2 || cameraDelta == 0
                         || (tier == EnemyGroundStepTier.Elite ? eliteDelta : mediumDelta) == 0
                         || moved < .15f)
                    throw new InvalidOperationException("No grounded contact/feedback: " + id
                        + " contacts=" + contactDelta + " camera=" + cameraDelta
                        + " audio=" + eliteDelta + "/" + mediumDelta
                        + " moved=" + moved.ToString("F2"));
                bool lifecycleChecked = id == "SpiderBrood_Rostrokarck"
                    || id == "SpiderBrood_Scolokarck_Tint3";
                if (lifecycleChecked)
                {
                    actor.Movement.StopMovement();
                    float settleStop = Time.time + .2f;
                    while (Time.time < settleStop) yield return null;
                    int stoppedContacts = emitter != null ? emitter.ContactCount : runtime.ContactCount;
                    int stoppedAudio = feel.PlayedCount;
                    int stoppedCamera = camera.GroundStepEmissionCount;
                    int stoppedDust = dust.EmittedBursts;
                    float paused = Time.time + .65f;
                    while (Time.time < paused) yield return null;
                    if ((emitter != null ? emitter.ContactCount : runtime.ContactCount) != stoppedContacts
                        || feel.PlayedCount != stoppedAudio
                        || camera.GroundStepEmissionCount != stoppedCamera
                        || dust.EmittedBursts != stoppedDust)
                        throw new InvalidOperationException("Stopped actor emitted a step: " + id);

                    actor.Movement.SetDestination(player.transform.position + Vector3.back * 5f,
                        .1f, EnemyLocomotionMode.Run);
                    float resumedUntil = Time.time + 5f;
                    while ((emitter != null ? emitter.ContactCount : runtime.ContactCount) <= stoppedContacts
                           && Time.time < resumedUntil) yield return null;
                    if ((emitter != null ? emitter.ContactCount : runtime.ContactCount) <= stoppedContacts)
                        throw new InvalidOperationException("Actor did not resume walking: " + id);
                    actor.Health.TakeDamage(new DamageInfo(actor.Health.CurrentHp + 100000f,
                        actor.transform.position, player.gameObject, triggersOnHitEffects: false));
                    if (!actor.Health.IsDead)
                        throw new InvalidOperationException("Could not kill actor: " + id);
                    float settleDeath = Time.time + .2f;
                    while (Time.time < settleDeath) yield return null;
                    int deadContacts = emitter != null ? emitter.ContactCount : runtime.ContactCount;
                    int deadAudio = feel.PlayedCount;
                    int deadCamera = camera.GroundStepEmissionCount;
                    int deadDust = dust.EmittedBursts;
                    float deadWait = Time.time + .65f;
                    while (Time.time < deadWait) yield return null;
                    if ((emitter != null ? emitter.ContactCount : runtime.ContactCount) != deadContacts
                        || feel.PlayedCount != deadAudio
                        || camera.GroundStepEmissionCount != deadCamera
                        || dust.EmittedBursts != deadDust)
                        throw new InvalidOperationException("Dead actor emitted a step: " + id);
                }
                Rows.Add(new { id, tier = tier.ToString(), contacts = contactDelta,
                    eliteAudio = eliteDelta, mediumAudio = mediumDelta, camera = cameraDelta,
                    dust = dustDelta, moved, nearest, lifecycleChecked });
                spawn.Release(actor);
                actor = null;
                float settle = Time.time + .1f;
                while (Time.time < settle) yield return null;
                int afterReleaseAudio = feel ? feel.PlayedCount : 0;
                int afterReleaseCamera = camera.GroundStepEmissionCount;
                int afterReleaseDust = dust.EmittedBursts;
                settle = Time.time + .35f;
                while (Time.time < settle) yield return null;
                if ((feel ? feel.PlayedCount : 0) != afterReleaseAudio
                    || camera.GroundStepEmissionCount != afterReleaseCamera
                    || dust.EmittedBursts != afterReleaseDust)
                    throw new InvalidOperationException("Released actor emitted a step: " + id);
            }
            File.WriteAllText(Path.Combine(output, "field-results.json"),
                JsonConvert.SerializeObject(Rows, Formatting.Indented));
            Result = "PASS grounded=12 light=0 airborne=0 lifecycle=2";
        }
        finally
        {
            if (actor != null && spawn != null) spawn.Release(actor);
            if (entered && ui.InArena) { ui.Clear(); ui.ToggleArena(); }
        }
    }

    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup >= deadline)
                throw new InvalidOperationException("Play ended or time limit");
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (work.MoveNext()) return;
            Finish(Result);
        }
        catch (Exception error) { Finish("FAIL " + error); }
    }

    private static void Finish(string status)
    {
        EditorApplication.update -= Tick;
        (work as IDisposable)?.Dispose();
        work = null;
        Application.runInBackground = previousBackground;
        Application.targetFrameRate = previousFrameRate;
        Result = status;
        if (!string.IsNullOrEmpty(output))
            File.WriteAllText(Path.Combine(output, "runner-result.txt"), status);
        Debug.Log("[GroundStepGoalFieldRunner] " + status);
    }
}
