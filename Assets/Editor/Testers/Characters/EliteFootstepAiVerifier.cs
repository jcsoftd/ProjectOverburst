using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EliteFootstepAiVerifier
{
    private const string Key = "EliteFootstepAiVerifier";
    private static readonly string[] Ids = {
        "SpiderBrood_Rostrokarck", "VenomBrood_Kupolobrach_Tint_Orange",
        "PrimalHunt_Occisodonte", "CavernMutants_Ursacetus"
    };
    private static IEnumerator work;
    private static bool stopping;
    private static int lastFrame;
    private static double deadline;
    private static bool previousBackground;
    private static int previousFrameRate;
    private static readonly System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();

    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");
    static EliteFootstepAiVerifier() => EditorApplication.playModeStateChanged += Changed;

    [MenuItem("OVERBURST/Enemies/Elites/Validate Footstep AI Chase")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Require(scene.name == PersistentSceneFlow.PersistentSceneName && !scene.isDirty,
            "Open saved PersistentScene");
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".result", "RUNNING");
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
            stopping = false;
            errors.Clear();
            work = Verify();
            lastFrame = -1;
            deadline = EditorApplication.timeSinceStartup + 160;
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
            if (LastResult == "RUNNING") SessionState.SetString(Key + ".result", "FAIL interrupted");
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            Debug.Log("[EliteFootstepAI] " + LastResult);
        }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
    }

    private static void Tick()
    {
        if (stopping) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
            if (work.MoveNext()) return;
            Require(errors.Count == 0, string.Join(" | ", errors));
            Finish("PASS 4 elites chased player with live AI, walked and emitted Feel; errors=0");
        }
        catch (Exception exception) { Finish("FAIL " + exception); }
    }

    private static void Finish(string result)
    {
        stopping = true;
        SessionState.SetString(Key + ".result", result);
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerator Verify()
    {
        EnemyActor current = null;
        EnemyThemeTrialHarness ui = null;
        EnemySpawnService spawn = null;
        try
        {
            float loadUntil = Time.realtimeSinceStartup + 45f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            {
                Require(Time.realtimeSinceStartup < loadUntil, "Hideout did not load");
                yield return null;
            }
            var player = PlayerInputFacade.Current;
            Require(player != null, "Player missing");
            ui = EnemyThemeTrialHarness.Current;
            Require(ui != null, "Theme debug UI missing");
            ui.ToggleArena();
            Require(ui.InArena, "Arena entry failed");
            Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service missing");
            var camera = QuarterViewCamera.ActiveInstance;
            Require(camera != null && camera.CurrentTarget == player.transform, "Player camera target missing");

            foreach (string id in Ids)
            {
                var table = ui.tables.First(t => t.Entries.Any(e => e.definition != null && e.definition.EnemyId == id));
                Require(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), "Catalog " + error);
                var definition = table.Entries.First(e => e.definition.EnemyId == id).definition;
                Vector3 location = player.transform.position + Vector3.forward * 10f;
                Require(Physics.Raycast(location + Vector3.up * 4f, Vector3.down, out var floor, 9f,
                    LayerMask.GetMask("Default", "Environment", "Ground"), QueryTriggerInteraction.Ignore),
                    "Arena floor " + id);
                location = floor.point + Vector3.up * .035f;
                var request = new EnemySpawnRequest(definition, location, Quaternion.identity,
                    player.transform, null, player.transform, null, 1f, 1f, 88);
                Require(spawn.TrySpawn(request, out current), "Spawn " + id);
                Require(current.AI != null && current.AI.enabled, "Live AI disabled " + id);
                current.AI.RequestAggro(player.transform);
                var emitter = current.GetComponent<EnemyEliteFootstepEmitter>();
                Require(emitter != null, "Footstep emitter " + id);
                var feel = UnityEngine.Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                int feelBefore = feel != null ? feel.PlayedCount : 0;
                int contactBefore = emitter.ContactCount;
                int cameraBefore = camera.GroundStepEmissionCount;
                Vector3 start = current.transform.position;
                float until = Time.realtimeSinceStartup + 10f;
                float closest = float.PositiveInfinity;
                while (Time.realtimeSinceStartup < until)
                {
                    Require(current.IsLeased && current.Health != null && !current.Health.IsDead,
                        "Enemy died before contact " + id);
                    Vector3 offset = current.transform.position - player.transform.position;
                    offset.y = 0f;
                    closest = Mathf.Min(closest, offset.magnitude);
                    yield return null;
                }
                feel = UnityEngine.Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                Require(emitter.ContactCount > contactBefore,
                    $"No AI walking contact {id}: nearest={closest:F2}, mode={current.Movement.LocomotionMode}, destination={current.Movement.HasDestination}");
                Require(feel != null && feel.PlayedCount > feelBefore, "No AI Feel " + id);
                Vector3 moved = current.transform.position - start;
                moved.y = 0f;
                Require(moved.magnitude > .15f, "Insufficient live movement " + id);
                Require(closest < 9.5f, $"No closer chase {id}: nearest={closest:F2}");
                Debug.Log($"[EliteFootstepAI] {id}: moved={moved.magnitude:F2}m nearest={closest:F2}m contacts={emitter.ContactCount-contactBefore} camera={camera.GroundStepEmissionCount-cameraBefore} feel={feel.PlayedCount-feelBefore} state={current.AI.CurrentDebugStateName}");
                spawn.Release(current);
                current = null;
                yield return null;
            }
        }
        finally
        {
            if (current != null && spawn != null) spawn.Release(current);
            if (ui != null && ui.InArena) { ui.Clear(); ui.ToggleArena(); }
        }
    }
}
