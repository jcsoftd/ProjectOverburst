using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EliteFootstepPlayVerifier
{
    private const string Key = "EliteFootstepPlayVerifier";
    private static readonly string[] Ids = {
        "SpiderBrood_Rostrokarck", "VenomBrood_Kupolobrach_Tint_Orange",
        "PrimalHunt_Occisodonte", "CavernMutants_Ursacetus"
    };
    private static IEnumerator work;
    private static int lastFrame;
    private static double deadline;
    private static bool stopping;
    private static bool previousBackground;
    private static int previousFrameRate;
    private static readonly System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();

    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");
    static EliteFootstepPlayVerifier() => EditorApplication.playModeStateChanged += Changed;

    [MenuItem("OVERBURST/Enemies/Elites/Validate Footstep Play")]
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
            deadline = EditorApplication.timeSinceStartup + 180;
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
            Debug.Log("[EliteFootstepPlay] " + LastResult);
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
            Finish("PASS 4 elites walked with synchronized contacts, Korean HUD names, Feel and camera; errors=0");
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
                    player.transform, null, player.transform, null, 1f, 1f, 77);
                Require(spawn.TrySpawn(request, out current), "Spawn " + id);
                var rank = current.GetComponent<EnemyRank>();
                Require(rank != null && rank.DisplayName == definition.DisplayName
                    && rank.DisplayName == EnemyDisplayNames.Resolve(id, id), "Korean rank name " + id);
                var emitter = current.GetComponent<EnemyEliteFootstepEmitter>();
                Require(emitter != null && emitter.Profile != null && emitter.Profile.IsValid,
                    "Footstep emitter " + id);
                current.AI.enabled = false;
                current.Movement.SetDestination(player.transform.position + Vector3.back * 5f,
                    .1f, EnemyLocomotionMode.Run);
                int contactBefore = emitter.ContactCount;
                int cameraBefore = camera.GroundStepEmissionCount;
                float until = Time.realtimeSinceStartup + 12f;
                while (emitter.ContactCount - contactBefore < 2
                    || camera.GroundStepEmissionCount <= cameraBefore)
                {
                    Require(Time.realtimeSinceStartup < until,
                        $"No walking contact/camera pulse {id}: contact={emitter.ContactCount-contactBefore}, camera={camera.GroundStepEmissionCount-cameraBefore}, mode={current.Movement.LocomotionMode}, destination={current.Movement.HasDestination}");
                    yield return null;
                }
                var feel = UnityEngine.Object.FindFirstObjectByType<EnemyEliteFootstepFeel>();
                Require(feel != null && feel.PlayedCount > 0 && feel.Capacity == 3, "Feel pool " + id);
                Debug.Log($"[EliteFootstepPlay] {id}: contacts={emitter.ContactCount-contactBefore} camera={camera.GroundStepEmissionCount-cameraBefore} feel={feel.PlayedCount}");
                spawn.Release(current);
                current = null;
                yield return null;
            }

            int accepted = camera.GroundStepEmissionCount;
            camera.RequestCombatImpact(CombatCameraRequestKind.AttackHit, Vector3.forward,
                Vector3.zero, false, .3f, .04f, 0f, .8f, .05f, .1f, 1f, .05f, 0f);
            camera.QueueGroundStep(.015f);
            yield return null;
            yield return null;
            Require(camera.GroundStepEmissionCount == accepted, "Footstep overrode combat impact");
        }
        finally
        {
            if (current != null && spawn != null) spawn.Release(current);
            if (ui != null && ui.InArena) { ui.Clear(); ui.ToggleArena(); }
        }
    }
}
