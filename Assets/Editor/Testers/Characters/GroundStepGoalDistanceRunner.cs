using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Local-only distance, audio-priority, and camera arbitration probe.
public static class GroundStepGoalDistanceRunner
{
    private static readonly List<object> Rows = new List<object>();
    private static IEnumerator work;
    private static bool previousBackground;
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
        Application.runInBackground = true;
        Result = "RUNNING";
        lastFrame = -1;
        deadline = EditorApplication.timeSinceStartup + 90;
        work = Run();
        EditorApplication.update += Tick;
    }

    private static IEnumerator Run()
    {
        float readyUntil = Time.time + 15f;
        while ((PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PlayerInputFacade.Current == null || QuarterViewCamera.ActiveInstance == null)
               && Time.time < readyUntil) yield return null;
        var player = PlayerInputFacade.Current;
        var camera = QuarterViewCamera.ActiveInstance;
        if (!player || !camera || camera.CurrentTarget != player.transform)
            throw new InvalidOperationException("Player camera missing");
        foreach (EnemyGroundStepTier tier in new[]
                 { EnemyGroundStepTier.Elite, EnemyGroundStepTier.Medium, EnemyGroundStepTier.None })
        {
            foreach (float distance in new[] { 2f, 4f, 8f, 12f })
            {
                float settle = Time.unscaledTime + .42f;
                while (Time.unscaledTime < settle) yield return null;
                var feel = Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                int beforeAudio = feel ? feel.PlayedCount : 0;
                int beforeCamera = camera.GroundStepEmissionCount;
                float amplitude = EnemyGroundStepTuning.CameraAmplitude(tier, distance);
                float expectedVolume = EnemyGroundStepTuning.AudioVolume(tier, distance);
                bool played = EnemyEliteFootstepFeel.Play(
                    player.transform.position + Vector3.forward * distance, distance, tier);
                if (amplitude > 0f)
                    camera.QueueGroundStep(amplitude,
                        tier == EnemyGroundStepTier.Elite ? .14f : .1f);
                feel = Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                bool shouldPlay = expectedVolume > 0f;
                if (played != shouldPlay || (feel ? feel.PlayedCount : 0) != beforeAudio + (shouldPlay ? 1 : 0))
                    throw new InvalidOperationException("Wrong audio gate: " + tier + " " + distance);
                float actualVolume = 0f;
                if (shouldPlay)
                {
                    var slots = (EnemyEliteFootstepFeel.Slot[])typeof(EnemyEliteFootstepFeel)
                        .GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(feel);
                    var slot = slots.FirstOrDefault(s => s.currentTier == tier
                        && s.availableAt > Time.unscaledTime);
                    if (slot == null || slot.audioFeedback == null || slot.audio.spatialBlend != 0f)
                        throw new InvalidOperationException("Player-relative audio not configured: " + tier);
                    actualVolume = slot.audioFeedback.MaxVolume;
                    if (Mathf.Abs(actualVolume - expectedVolume) > .001f)
                        throw new InvalidOperationException("Wrong audio volume: " + tier + " " + distance);
                }
                yield return null;
                int cameraDelta = camera.GroundStepEmissionCount - beforeCamera;
                if (cameraDelta != (amplitude > 0f ? 1 : 0))
                    throw new InvalidOperationException("Wrong camera gate: " + tier + " " + distance);
                Rows.Add(new { tier = tier.ToString(), distance,
                    audio = played, expectedVolume, actualVolume, amplitude, camera = cameraDelta });
            }
        }

        float waitUntil = Time.unscaledTime + .42f;
        while (Time.unscaledTime < waitUntil) yield return null;
        var pool = Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
        int preemptBefore = pool.PreemptedMediumCount;
        int eliteBefore = pool.ElitePlayedCount;
        Vector3 contact = player.transform.position + Vector3.forward * 2f;
        for (int i = 0; i < 3; i++)
            if (!EnemyEliteFootstepFeel.Play(contact, 2f, EnemyGroundStepTier.Medium))
                throw new InvalidOperationException("Could not fill medium audio slots");
        if (!EnemyEliteFootstepFeel.Play(contact, 2f, EnemyGroundStepTier.Elite)
            || pool.PreemptedMediumCount != preemptBefore + 1
            || pool.ElitePlayedCount != eliteBefore + 1)
            throw new InvalidOperationException("Elite audio did not preempt a medium");

        waitUntil = Time.unscaledTime + .42f;
        while (Time.unscaledTime < waitUntil) yield return null;
        int beforeStrongest = camera.GroundStepEmissionCount;
        camera.QueueGroundStep(.0125f, .1f);
        camera.QueueGroundStep(.0425f, .14f);
        yield return null;
        float last = (float)typeof(QuarterViewCamera)
            .GetField("lastGroundStepAmplitude", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(camera);
        float duration = (float)typeof(QuarterViewCamera)
            .GetField("impactDuration", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(camera);
        if (camera.GroundStepEmissionCount != beforeStrongest + 1
            || Mathf.Abs(last - .0425f * camera.GroundStepCameraStrength) > .001f
            || Mathf.Abs(duration - .14f) > .001f)
            throw new InvalidOperationException("Strongest ground step was not selected");

        waitUntil = Time.unscaledTime + .42f;
        while (Time.unscaledTime < waitUntil) yield return null;
        int beforeCombat = camera.GroundStepEmissionCount;
        camera.RequestCombatImpact(CombatCameraRequestKind.AttackHit, Vector3.forward,
            Vector3.zero, false, .3f, .04f, 0f, .8f, .05f, .1f, 1f, .05f, 0f);
        camera.QueueGroundStep(.05f, .14f);
        yield return null;
        if (camera.GroundStepEmissionCount != beforeCombat)
            throw new InvalidOperationException("Footstep overrode combat impact");

        File.WriteAllText(Path.Combine(output, "distance-results.json"),
            JsonConvert.SerializeObject(new { distances = Rows, elitePreemptsMedium = true,
                strongestOnly = true, combatWins = true }, Formatting.Indented));
        Result = "PASS 12 distances, audio priority, strongest camera, combat priority";
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
        Debug.Log("[GroundStepGoalDistanceRunner] " + status);
    }
}
