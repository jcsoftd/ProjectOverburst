using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MoreMountains.Feedbacks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Local-only actual multi-actor and camera-zoom Play check. Never saves a scene.
public static class GroundStepGoalCrowdZoomRunner
{
    private static readonly string[] Ids =
    {
        "SpiderBrood_Rostrokarck", "VenomBrood_Kupolobrach_Tint_Orange",
        "PrimalHunt_Occisodonte", "SpiderBrood_Scolokarck_Tint3",
        "PrimalHunt_Dimaxillosaurus"
    };
    private static IEnumerator work;
    private static int lastFrame;
    private static double deadline;
    private static string output;
    private static bool previousBackground;
    public static string Result { get; private set; } = "IDLE";

    public static void Start(string label)
    {
        if (!EditorApplication.isPlaying || work != null)
            throw new InvalidOperationException("Requires idle Play Mode");
        output = Path.GetFullPath("../개인파일/코덱스산출/MonsterFeel/20260924_GoalFEEL01/" + label);
        Directory.CreateDirectory(output);
        previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        deadline = EditorApplication.timeSinceStartup + 90;
        lastFrame = -1;
        Result = "RUNNING";
        work = Run();
        EditorApplication.update += Tick;
    }

    private static IEnumerator Run()
    {
        var ui = EnemyThemeTrialHarness.Current;
        var player = PlayerInputFacade.Current;
        var camera = QuarterViewCamera.ActiveInstance;
        var runtime = Object.FindFirstObjectByType<EnemyFootfallRuntime>();
        var dust = Object.FindFirstObjectByType<EnemyFootDustVfx>();
        if (!ui || !player || !camera || !runtime || !dust)
            throw new InvalidOperationException("Missing arena/player/camera/footfall service");
        float readyUntil = Time.time + 15f;
        while ((PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
               && Time.time < readyUntil) yield return null;
        if (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching)
            throw new InvalidOperationException("Hideout not ready");

        bool entered = !ui.InArena;
        var actors = new List<EnemyActor>();
        EnemySpawnService spawn = null;
        try
        {
            if (entered) ui.ToggleArena();
            if (!ui.InArena) throw new InvalidOperationException("Arena did not open");
            float arenaReady = Time.time + .6f;
            while (Time.time < arenaReady) yield return null;
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn))
                throw new InvalidOperationException("Spawn service missing");
            foreach (var table in ui.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);

            var zoomField = typeof(QuarterViewCamera).GetField("targetDistance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var lastStepField = typeof(QuarterViewCamera).GetField("lastGroundStepAmplitude",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (zoomField == null || lastStepField == null) throw new InvalidOperationException("Camera probe field missing");
            var zoomRows = new List<object>();
            foreach (float zoom in new[] { 6f, 28f })
            {
                zoomField.SetValue(camera, zoom);
                float zoomUntil = Time.unscaledTime + 2f;
                while (Mathf.Abs(camera.CurrentDistance - zoom) > .12f
                       && Time.unscaledTime < zoomUntil) yield return null;
                if (Mathf.Abs(camera.CurrentDistance - zoom) > .12f)
                    throw new InvalidOperationException("Camera did not reach zoom " + zoom);
                float settle = Time.unscaledTime + .42f;
                while (Time.unscaledTime < settle) yield return null;
                int beforeCamera = camera.GroundStepEmissionCount;
                if (!EnemyEliteFootstepFeel.Play(player.transform.position + Vector3.forward * 2f,
                        2f, EnemyGroundStepTier.Elite))
                    throw new InvalidOperationException("Zoom audio failed at " + zoom);
                var feel = Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                var slots = (EnemyEliteFootstepFeel.Slot[])typeof(EnemyEliteFootstepFeel)
                    .GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feel);
                var slot = slots.First(s => s.currentTier == EnemyGroundStepTier.Elite
                    && s.availableAt > Time.unscaledTime);
                float volume = slot.audioFeedback.MaxVolume;
                camera.QueueGroundStep(EnemyGroundStepTuning.CameraAmplitude(EnemyGroundStepTier.Elite, 2f), .14f);
                yield return null;
                float amplitude = (float)lastStepField.GetValue(camera);
                if (camera.GroundStepEmissionCount != beforeCamera + 1)
                    throw new InvalidOperationException("Zoom camera failed at " + zoom);
                zoomRows.Add(new { zoom = camera.CurrentDistance, volume, amplitude });
            }
            var near = zoomRows[0];
            var far = zoomRows[1];
            float nearVolume = (float)near.GetType().GetProperty("volume").GetValue(near);
            float farVolume = (float)far.GetType().GetProperty("volume").GetValue(far);
            float nearAmplitude = (float)near.GetType().GetProperty("amplitude").GetValue(near);
            float farAmplitude = (float)far.GetType().GetProperty("amplitude").GetValue(far);
            if (Mathf.Abs(nearVolume - farVolume) > .0001f
                || Mathf.Abs(nearAmplitude - farAmplitude) > .0001f)
                throw new InvalidOperationException("Footstep strength changed with camera zoom");
            camera.ResetZoom();
            float next = Time.unscaledTime + .5f;
            while (Time.unscaledTime < next) yield return null;

            var emitters = new List<EnemyEliteFootstepEmitter>();
            var firstContacts = new List<int>();
            for (int i = 0; i < Ids.Length; i++)
            {
                string id = Ids[i];
                var definition = ui.tables.SelectMany(t => t.Entries)
                    .First(e => e.definition != null && e.definition.EnemyId == id).definition;
                Vector3 location = player.transform.position + Vector3.forward * (6f + i * .2f)
                    + Vector3.right * ((i - 2) * 2.2f);
                if (!Physics.Raycast(location + Vector3.up * 4f, Vector3.down, out RaycastHit floor,
                        9f, LayerMask.GetMask("Default", "Environment", "Ground"),
                        QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException("Arena floor missing: " + id);
                location = floor.point + Vector3.up * .035f;
                if (!spawn.TrySpawn(new EnemySpawnRequest(definition, location, Quaternion.identity,
                        player.transform, null, player.transform, null, 1f, 1f, 96), out var actor))
                    throw new InvalidOperationException("Spawn failed: " + id);
                actors.Add(actor);
                actor.AI.enabled = false;
                var emitter = actor.GetComponent<EnemyEliteFootstepEmitter>();
                if (i < 3 && emitter == null)
                    throw new InvalidOperationException("Elite emitter missing: " + id);
                emitters.Add(emitter);
                firstContacts.Add(emitter ? emitter.ContactCount : 0);
            }
            var footFeel = Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
            int firstMediumContact = runtime.ContactCount;
            int firstEliteAudio = footFeel.ElitePlayedCount;
            int firstMediumAudio = footFeel.MediumPlayedCount;
            int firstCamera = camera.GroundStepEmissionCount;
            int firstDust = dust.EmittedBursts;
            int firstDropped = footFeel.DroppedCount;
            for (int i = 0; i < actors.Count; i++)
                actors[i].Movement.SetDestination(player.transform.position + Vector3.back * 3f
                    + Vector3.right * ((i - 2) * 1.1f), .1f, EnemyLocomotionMode.Run);
            float crowdUntil = Time.time + 9f;
            while (Time.time < crowdUntil)
            {
                if (actors.Any(a => !a.IsLeased || a.Health == null || a.Health.IsDead))
                    throw new InvalidOperationException("Crowd actor released early");
                if (Enumerable.Range(0, 3).All(i => emitters[i].ContactCount - firstContacts[i] >= 2)
                    && runtime.ContactCount - firstMediumContact >= 3
                    && footFeel.ElitePlayedCount - firstEliteAudio >= 3
                    && footFeel.MediumPlayedCount - firstMediumAudio >= 1
                    && camera.GroundStepEmissionCount > firstCamera) break;
                yield return null;
            }
            int[] eliteContacts = Enumerable.Range(0, 3)
                .Select(i => emitters[i].ContactCount - firstContacts[i]).ToArray();
            int mediumContacts = runtime.ContactCount - firstMediumContact;
            int eliteAudio = footFeel.ElitePlayedCount - firstEliteAudio;
            int mediumAudio = footFeel.MediumPlayedCount - firstMediumAudio;
            int cameraSteps = camera.GroundStepEmissionCount - firstCamera;
            int dustSteps = dust.EmittedBursts - firstDust;
            if (eliteContacts.Any(c => c < 2) || mediumContacts < 3
                || eliteAudio < 3 || mediumAudio < 1 || cameraSteps < 1
                || footFeel.Capacity != 3 || dustSteps < 3)
                throw new InvalidOperationException("Crowd missing contacts/feedback: "
                    + JsonConvert.SerializeObject(new { eliteContacts, mediumContacts,
                        eliteAudio, mediumAudio, cameraSteps, dustSteps }));
            foreach (var actor in actors) spawn.Release(actor);
            actors.Clear();
            next = Time.time + .1f;
            while (Time.time < next) yield return null;
            int afterAudio = footFeel.PlayedCount;
            int afterCamera = camera.GroundStepEmissionCount;
            next = Time.time + .35f;
            while (Time.time < next) yield return null;
            if (footFeel.PlayedCount != afterAudio || camera.GroundStepEmissionCount != afterCamera)
                throw new InvalidOperationException("Crowd feedback continued after release");
            File.WriteAllText(Path.Combine(output, "crowd-zoom-results.json"),
                JsonConvert.SerializeObject(new { zoomRows, eliteContacts, mediumContacts,
                    eliteAudio, mediumAudio, cameraSteps, dustSteps,
                    droppedAudio = footFeel.DroppedCount - firstDropped,
                    audioCapacity = footFeel.Capacity }, Formatting.Indented));
            Result = "PASS 6m/28m zoom, 3 elite + 2 medium walking, 3-slot audio, release";
        }
        finally
        {
            if (spawn != null)
                foreach (var actor in actors) if (actor) spawn.Release(actor);
            camera.ResetZoom();
            if (entered && ui.InArena) { ui.Clear(); ui.ToggleArena(); }
        }
    }

    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup >= deadline)
                throw new InvalidOperationException("Play ended or time limit");
            if (lastFrame == Time.frameCount) return;
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
        Result = status;
        File.WriteAllText(Path.Combine(output, "runner-result.txt"), status);
        Debug.Log("[GroundStepGoalCrowdZoomRunner] " + status);
    }
}
