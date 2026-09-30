using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Local-only Play verifier. It never saves or reopens the shared scene.
[InitializeOnLoad]
public static class DeathHarvestPlayVerifier
{
    private const string Key = "DeathHarvestPlayVerifier";
    private const string Root = @"D:\JC Program\유니티\개인프로젝트\프로젝트 오버버스트\개인파일\코덱스산출\MonsterThemes\20260924_UndeadHorde\play";
    private static readonly List<string> passed = new List<string>();
    private static readonly List<string> failed = new List<string>();
    private static IEnumerator work;
    private static int lastFrame = -1;
    private static double deadline;
    private static bool stopping;
    private static bool previousBackground;
    private static int previousFrameRate;
    private static EnemyThemeDebugUI ui;
    private static PlayerInputFacade player;
    private static CombatHealth health;
    private static Vector3 center;
    private static DeathHarvestFrameRecorder recorder;

    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");

    static DeathHarvestPlayVerifier() => EditorApplication.playModeStateChanged += Changed;

    [MenuItem("OVERBURST/Enemies/Themes/Validate Death Harvest Play + Video")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start in Edit Mode.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != PersistentSceneFlow.PersistentSceneName)
            throw new InvalidOperationException("Requires the already open PersistentScene.");
        Directory.CreateDirectory(Root);
        passed.Clear(); failed.Clear();
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
            passed.Clear(); failed.Clear();
            work = Verify(); lastFrame = -1; stopping = false;
            deadline = EditorApplication.timeSinceStartup + 600;
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            stopping = true;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            Application.runInBackground = previousBackground;
            Application.targetFrameRate = previousFrameRate;
            (work as IDisposable)?.Dispose();
            work = null;
            if (LastResult == "RUNNING") SessionState.SetString(Key + ".result", "FAIL interrupted");
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            Debug.Log("[DeathHarvestPlay] " + LastResult);
        }
    }

    private static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            failed.Add("Unity " + message);
    }

    private static void Tick()
    {
        if (stopping || !EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "600s timeout");
            if (work.MoveNext()) return;
            Finish();
        }
        catch (Exception exception)
        {
            failed.Add(exception.ToString());
            Finish();
        }
    }

    private static void Finish()
    {
        stopping = true;
        EditorApplication.update -= Tick;
        try
        {
            if (recorder != null) UnityEngine.Object.Destroy(recorder.gameObject);
            if (ui != null)
            {
                ui.Clear();
                if (ui.InArena) ui.ToggleArena();
            }
        }
        catch (Exception exception) { failed.Add("cleanup " + exception.Message); }
        string result = (failed.Count == 0 ? "PASS" : "FAIL") + "\n"
            + string.Join("\n", passed.Select(s => "PASS " + s))
            + (failed.Count > 0 ? "\n" + string.Join("\n", failed.Select(s => "FAIL " + s)) : "")
            + "\nerrors=" + failed.Count;
        File.WriteAllText(Path.Combine(Root, "play_validation.txt"), result);
        SessionState.SetString(Key + ".result", result);
        EditorApplication.ExitPlaymode();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pass(string message)
    {
        passed.Add(message);
        Debug.Log("[DeathHarvestPlay] PASS " + message);
    }

    private static EnemyThemeEncounter DebugEncounter() =>
        UnityEngine.Object.FindObjectsByType<EnemyThemeEncounter>(FindObjectsSortMode.None)
            .FirstOrDefault(e => e.name == "Theme debug encounter");

    private static IEnumerator Verify()
    {
        float wait = Time.time + 50f;
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
        {
            Require(Time.time < wait, "Hideout load timeout");
            yield return null;
        }
        player = PlayerInputFacade.Current;
        Require(player != null, "Player missing");
        health = player.GetComponent<CombatHealth>();
        Require(health != null, "Player health missing");
        health.SetMaxHp(100000, true);
        ui = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);
        Require(ui != null, "Theme debug UI missing");
        ui.gameObject.SetActive(true);
        yield return null;
        Require(ui.tables != null && ui.tables.Length == 5, "Five theme buttons not assembled");
        var table = ui.tables[4];
        Require(table != null && table.ThemeId == "DeathHarvest" && table.Entries.Count == 8,
            "DeathHarvest table/8 definitions missing");
        Require(table.Validate(out string tableError), "Invalid table: " + tableError);
        Pass("five theme buttons; DeathHarvest table/8 entries");
        ui.ToggleArena();
        Require(ui.InArena, "Arena entry failed");
        center = player.transform.position;
        yield return null;
        var arena = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugArena>();
        var zones = arena.GetComponentsInChildren<EnemyThemeTriggerZone>();
        Require(zones.Length == 5 && zones.Count(z => z.GetComponent<EnemyThemeEncounter>().Table == table) == 1,
            "Fifth arena trigger/table missing");
        Pass("five arena zones; DeathHarvest trigger");

        foreach (EnemyThemeTrialMode mode in Enum.GetValues(typeof(EnemyThemeTrialMode)))
        {
            Require(ui.SetTrialMode(mode), "Set mode " + mode);
            var expected = EnemyThemeTrialPresets.Resolve(table, mode);
            if (mode == EnemyThemeTrialMode.Normal)
            {
                recorder = new GameObject("DeathHarvest gameplay recorder").AddComponent<DeathHarvestFrameRecorder>();
                recorder.Begin(Path.Combine(Root, "gameplay-frames"), 960, 540, 12, 10f);
            }
            Require(ui.Begin(4, false), "Begin " + mode);
            var encounter = DebugEncounter();
            Require(encounter != null, "Encounter " + mode);
            wait = Time.time + 25f;
            while (encounter.State != EnemyThemeEncounterState.Combat
                && encounter.State != EnemyThemeEncounterState.Failed)
            {
                Require(Time.time < wait, "Spawn timeout " + mode);
                yield return null;
            }
            Require(encounter.State != EnemyThemeEncounterState.Failed,
                "Spawn failed " + mode + ": " + encounter.LastMessage);
            Require(encounter.SpawnedCount == expected.Total && encounter.AliveCount == expected.Total,
                "Roster count " + mode + " actual=" + encounter.SpawnedCount + "/" + encounter.AliveCount);
            var actors = encounter.SnapshotActors();
            int elite = actors.Count(a => a.Definition.SquadParticipationMode == EnemySquadParticipationMode.Independent);
            Require(elite == expected.Elite, "Elite count " + mode + " actual=" + elite);
            Pass(mode + " roster " + expected.Small + "/" + expected.Medium + "/" + expected.Elite
                + " total=" + expected.Total);
            if (mode == EnemyThemeTrialMode.Normal)
            {
                var initial = actors.ToDictionary(a => a, a => a.transform.position);
                wait = Time.time + 10f;
                while (Time.time < wait || !recorder.Done) yield return null;
                Require(recorder.Error == null && recorder.FrameCount >= 60,
                    "Gameplay video frames: " + recorder.Error + " count=" + recorder.FrameCount);
                Require(actors.Any(a => a != null && Vector3.Distance(a.transform.position, initial[a]) > .25f),
                    "No normal roster movement");
                Pass("normal roster moved; gameplay frames=" + recorder.FrameCount);
                UnityEngine.Object.Destroy(recorder.gameObject); recorder = null;
            }
            else
            {
                wait = Time.time + 1.5f;
                while (Time.time < wait) yield return null;
            }
            ui.Clear();
            yield return null;
            Require(actors.All(a => a == null || !a.IsLeased), "Pool clear leaked " + mode);
        }

        var service = EnemySpawnService.Current;
        Require(service != null && service.RegisterAdditionalCatalog(table.Catalog, out _), "Catalog registration");
        foreach (var entry in table.Entries)
        {
            var definition = entry.definition;
            EnemyActor prior = null;
            uint previousLease = 0;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var actor = Spawn(definition, center + Vector3.forward * 7f);
                Require(actor.Health.CurrentHp == actor.Health.MaxHp, "HP reset " + definition.EnemyId);
                if (prior == actor) Require(actor.LeaseVersion > previousLease,
                    "Lease version " + definition.EnemyId);
                previousLease = actor.LeaseVersion;
                prior = actor;
                actor.RequestPoolRelease();
                Require(!actor.IsLeased && !actor.AbilityController.IsExecuting,
                    "Pool cleanup " + definition.EnemyId);
                yield return null;
            }
        }
        Pass("8 actors x 3 pool leases, HP/version/cancellation");

        int attackCount = 0;
        foreach (var entry in table.Entries)
        {
            var definition = entry.definition;
            var abilities = definition.AbilitySet;
            for (int i = 0; i < abilities.Count; i++)
            {
                var ability = abilities.GetAbility(i);
                Teleport(center);
                health.ResetHealth();
                float distance = ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile ? 4f
                    : ability.ExecutionMode == EnemyAbilityExecutionMode.Charge ? 3f
                    : Mathf.Min(1.1f, ability.Range * .75f);
                var actor = Spawn(definition, center - Vector3.forward * distance);
                actor.transform.rotation = Quaternion.identity;
                Physics.SyncTransforms();
                yield return null;
                var executor = actor.GetComponents<EnemyAbilityExecutor>().FirstOrDefault(e => e.Supports(ability));
                if (executor == null) failed.Add("executor missing " + definition.EnemyId + " " + i);
                else
                {
                    float hp = health.CurrentHp;
                    Vector3 start = actor.transform.position;
                    if (!executor.TryStart(ability, i, player.transform))
                        failed.Add("attack refused " + definition.EnemyId + " " + ability.AnimatorTrigger);
                    else
                    {
                        wait = Time.time + ability.AttackAnimationDuration + 1.1f;
                        while (Time.time < wait) yield return null;
                        if (health.CurrentHp >= hp)
                            failed.Add("no impact " + definition.EnemyId + " " + ability.AnimatorTrigger
                                + " mode=" + ability.ExecutionMode);
                        else attackCount++;
                        if (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge
                            && Vector3.Distance(start, actor.transform.position) <= .3f)
                            failed.Add("charge did not move " + definition.EnemyId + " " + i);
                    }
                }
                actor.RequestPoolRelease();
                yield return null;
            }
        }
        Pass("attack impacts " + attackCount + "/31");
    }

    private static EnemyActor Spawn(EnemyDefinition definition, Vector3 position)
    {
        var request = new EnemySpawnRequest(definition, position, Quaternion.identity,
            player.transform, null, player.transform, null, 1f, 1f, 77);
        Require(EnemySpawnService.Current.TrySpawn(request, out var actor), "Spawn " + definition.EnemyId);
        actor.AI.enabled = false;
        actor.Movement.StopMovement();
        return actor;
    }

    private static void Teleport(Vector3 position)
    {
        var controller = player.GetComponent<CharacterController>();
        bool enabled = controller != null && controller.enabled;
        if (enabled) controller.enabled = false;
        player.transform.SetPositionAndRotation(position, Quaternion.identity);
        if (enabled) controller.enabled = true;
        player.GetComponent<PlayerMovement>()?.ResetMotionAfterTeleport();
        Physics.SyncTransforms();
    }
}

public sealed class DeathHarvestFrameRecorder : MonoBehaviour
{
    public bool Done { get; private set; }
    public string Error { get; private set; }
    public int FrameCount { get; private set; }

    public void Begin(string folder, int width, int height, int fps, float duration) =>
        StartCoroutine(Record(folder, width, height, fps, duration));

    private IEnumerator Record(string folder, int width, int height, int fps, float duration)
    {
        RenderTexture texture = null;
        Texture2D pixels = null;
        Camera camera = null;
        try
        {
            Directory.CreateDirectory(folder);
            camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Main camera missing");
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            float start = Time.unscaledTime;
            float next = start;
            while (Time.unscaledTime - start < duration)
            {
                yield return new WaitForEndOfFrame();
                if (Time.unscaledTime < next) continue;
                next += 1f / fps;
                var oldCameraTarget = camera.targetTexture;
                var oldActive = RenderTexture.active;
                try
                {
                    camera.targetTexture = texture;
                    camera.Render();
                    RenderTexture.active = texture;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(folder, FrameCount.ToString("D4") + ".png"), pixels.EncodeToPNG());
                    FrameCount++;
                }
                finally
                {
                    camera.targetTexture = oldCameraTarget;
                    RenderTexture.active = oldActive;
                }
            }
        }
        finally
        {
            if (texture != null) Destroy(texture);
            if (pixels != null) Destroy(pixels);
            Done = true;
        }
    }
}
