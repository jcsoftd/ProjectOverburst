using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;

// Local-only regression for the theme debug panel and its arena trigger pads.
[InitializeOnLoad]
public static class EnemyThemeDebugCountsPlayVerifier
{
    private const string Key = "EnemyThemeDebugCountsPlayVerifier";
    private static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    private static int lastFrame;
    private static double deadline;
    private static bool stopping;
    private static bool oldBackground;
    private static int oldFrameRate;
    private static readonly List<string> passed = new List<string>();
    private static readonly List<string> errors = new List<string>();
    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");

    static EnemyThemeDebugCountsPlayVerifier() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("OVERBURST/Enemies/Themes/Validate Debug Counts Play Mode")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play first");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Require(scene.name == PersistentSceneFlow.PersistentSceneName && !scene.isDirty,
            "Open saved PersistentScene first");
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".result", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            oldBackground = Application.runInBackground;
            oldFrameRate = Application.targetFrameRate;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            passed.Clear();
            errors.Clear();
            stopping = false;
            lastFrame = -1;
            deadline = EditorApplication.timeSinceStartup + 300;
            work.Clear();
            work.Push(Verify());
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            stopping = true;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            Application.runInBackground = oldBackground;
            Application.targetFrameRate = oldFrameRate;
            if (LastResult == "RUNNING") SessionState.SetString(Key + ".result", "FAIL interrupted");
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            Debug.Log("[EnemyThemeDebugCounts] " + LastResult);
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
            while (work.Count > 0)
            {
                var next = work.Peek();
                if (!next.MoveNext()) { work.Pop(); continue; }
                if (next.Current is IEnumerator nested) { work.Push(nested); continue; }
                return;
            }
            Require(errors.Count == 0, string.Join(" | ", errors));
            Finish("PASS");
        }
        catch (Exception exception)
        {
            Finish("FAIL " + exception);
        }
    }

    private static void Finish(string status)
    {
        stopping = true;
        string result = status + " · " + string.Join("; ", passed) + " · errors=" + errors.Count;
        SessionState.SetString(Key + ".result", result);
        string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
        string folder = Path.Combine(workspace, "개인파일", "코덱스산출", "MonsterDesign", "20260923_ThemeDebugCounts");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "play-results.json"),
            JsonConvert.SerializeObject(new { status, passed, errors }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerator Verify()
    {
        EnemyThemeDebugUI ui = null;
        try
        {
            float limit = Time.realtimeSinceStartup + 45f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            {
                Require(Time.realtimeSinceStartup < limit, "Hideout did not load");
                yield return null;
            }
            var player = PlayerInputFacade.Current;
            Require(player != null, "Player missing");
            ui = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);
            Require(ui != null && ui.tables.Length == 4, "Four-theme debug UI missing");
            ui.gameObject.SetActive(true);
            Require(ui.TrialMode == EnemyThemeTrialMode.Normal, "Normal must be the default debug mode");
            var modeBar = ui.transform.Find("Roster modes");
            Require(modeBar != null && modeBar.childCount == 4, "Mode selector missing or duplicated");
            ui.gameObject.SetActive(false);
            ui.gameObject.SetActive(true);
            modeBar = ui.transform.Find("Roster modes");
            Require(modeBar != null && modeBar.childCount == 4, "Mode selector duplicated after re-enable");
            var eliteButton = modeBar.Find("Mode 정예")?.GetComponent<UnityEngine.UI.Button>();
            var normalButton = modeBar.Find("Mode 일반")?.GetComponent<UnityEngine.UI.Button>();
            Require(eliteButton != null && normalButton != null, "Mode buttons missing after re-enable");
            eliteButton.onClick.Invoke();
            Require(ui.TrialMode == EnemyThemeTrialMode.Elite, "Elite button was not connected");
            normalButton.onClick.Invoke();
            Require(ui.TrialMode == EnemyThemeTrialMode.Normal, "Normal button was not connected");
            passed.Add("Mode buttons reconnect after panel re-enable");

            foreach (EnemyThemeTrialMode mode in Enum.GetValues(typeof(EnemyThemeTrialMode)))
            foreach (var table in ui.tables)
            {
                var counts = EnemyThemeTrialPresets.Resolve(table, mode);
                var tierByDefinition = table.Entries.ToDictionary(e => e.definition, e => e.tier);
                var roster = table.BuildRoster(counts.Small, counts.Medium, counts.Elite, 731);
                Require(roster.Count == counts.Total, "Roster total " + table.ThemeId + " / " + mode);
                Require(roster.Count(d => tierByDefinition[d] == EnemyThemeTier.Small) == counts.Small,
                    "Small count " + table.ThemeId + " / " + mode);
                Require(roster.Count(d => tierByDefinition[d] == EnemyThemeTier.Medium) == counts.Medium,
                    "Medium count " + table.ThemeId + " / " + mode);
                Require(roster.Count(d => tierByDefinition[d] == EnemyThemeTier.Elite) == counts.Elite,
                    "Elite count " + table.ThemeId + " / " + mode);
            }
            passed.Add("4 themes × 4 modes exact tier rosters");

            ui.ToggleArena();
            Require(ui.InArena, "Arena entry failed");
            var arena = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugArena>();
            Require(arena != null, "Arena instance missing");
            foreach (var zone in arena.GetComponentsInChildren<EnemyThemeEncounter>(true))
            {
                var expected = EnemyThemeTrialPresets.Resolve(zone.Table, EnemyThemeTrialMode.Normal);
                Require(zone.TrialRoster.Total == expected.Total, "Default pad count " + zone.Table.ThemeId);
                var label = zone.transform.Find("Zone label")?.GetComponent<TextMeshPro>();
                Require(label != null && label.text.Contains(expected.Total + "마리"), "Pad label " + zone.Table.ThemeId);
            }
            passed.Add("Arena pads and labels follow selected mode");

            for (int index = 0; index < ui.tables.Length; index++)
            {
                ui.Clear();
                Require(ui.SetTrialMode(EnemyThemeTrialMode.Normal), "Select normal mode");
                int expected = EnemyThemeTrialPresets.Resolve(ui.tables[index], ui.TrialMode).Total;
                Require(ui.spawnButtons[index].GetComponentInChildren<TextMeshProUGUI>(true).text == expected + "마리",
                    "Button label " + index);
                Require(ui.Begin(index, false), "Begin normal " + index);
                var encounter = FindDebugEncounter();
                Require(encounter != null, "Debug encounter missing");
                limit = Time.realtimeSinceStartup + 25f;
                while (encounter.State != EnemyThemeEncounterState.Combat
                    && encounter.State != EnemyThemeEncounterState.Failed)
                {
                    Require(Time.realtimeSinceStartup < limit, "Normal spawn timeout " + index);
                    yield return null;
                }
                Require(encounter.State != EnemyThemeEncounterState.Failed
                    && encounter.SpawnedCount == expected && encounter.AliveCount == expected,
                    "Normal spawn count " + ui.tables[index].ThemeId + ": " + encounter.LastMessage);
                Require(!ui.SetTrialMode(EnemyThemeTrialMode.Large), "Active trial accepted mode change");
                var actors = encounter.SnapshotActors();
                ui.Clear();
                yield return null;
                Require(actors.All(a => !a.IsLeased), "Clear leaked actors " + index);
                passed.Add(ui.tables[index].ThemeId + " normal=" + expected);
            }

            yield return VerifyOne(ui, EnemyThemeTrialMode.Elite, 1, 12);
            yield return VerifyOne(ui, EnemyThemeTrialMode.Large, 3, 40);
            yield return VerifyOne(ui, EnemyThemeTrialMode.Stress50, 0, 50);

            ui.Clear();
            Require(ui.SetTrialMode(EnemyThemeTrialMode.Normal), "Reset normal mode");
            var trigger = arena.GetComponentsInChildren<EnemyThemeTriggerZone>(true)
                .First(z => z.GetComponent<EnemyThemeEncounter>().Table.ThemeId == "CavernMutants");
            Require(trigger.TryActivate(player.transform), "Arena pad did not activate");
            var waves = trigger.GetComponent<EnemyThemeEncounter>();
            for (int wave = 1; wave <= 3; wave++)
            {
                int expected = wave * 12;
                limit = Time.realtimeSinceStartup + 25f;
                while (waves.SpawnedCount < expected && waves.State != EnemyThemeEncounterState.Failed)
                {
                    Require(Time.realtimeSinceStartup < limit, "Arena wave timeout " + wave);
                    yield return null;
                }
                Require(waves.State != EnemyThemeEncounterState.Failed && waves.SpawnedCount == expected,
                    "Arena wave count " + wave + ": " + waves.LastMessage);
                foreach (var actor in waves.SnapshotActors())
                    if (actor.IsLeased && !actor.Health.IsDead)
                        actor.Health.TakeDamage(new DamageInfo(actor.Health.MaxHp + 10000f,
                            actor.transform.position, player.gameObject, Vector3.forward));
            }
            limit = Time.realtimeSinceStartup + 15f;
            while (waves.State != EnemyThemeEncounterState.Completed)
            {
                Require(Time.realtimeSinceStartup < limit, "Arena wave completion timeout");
                yield return null;
            }
            passed.Add("Cavern normal arena waves 12 × 3 and clear");
            ui.Clear();
            ui.ToggleArena();
            Require(!ui.InArena, "Arena exit failed");
        }
        finally
        {
            if (ui != null)
            {
                ui.Clear();
                if (ui.InArena) ui.ToggleArena();
            }
        }
    }

    private static IEnumerator VerifyOne(EnemyThemeDebugUI ui, EnemyThemeTrialMode mode, int index, int expected)
    {
        Require(ui.SetTrialMode(mode), "Select " + mode);
        Require(ui.Begin(index, false), "Begin " + mode);
        var encounter = FindDebugEncounter();
        Require(encounter != null, "Debug encounter missing for " + mode);
        float limit = Time.realtimeSinceStartup + 30f;
        while (encounter.State != EnemyThemeEncounterState.Combat
            && encounter.State != EnemyThemeEncounterState.Failed)
        {
            Require(Time.realtimeSinceStartup < limit, mode + " spawn timeout");
            yield return null;
        }
        Require(encounter.State != EnemyThemeEncounterState.Failed
            && encounter.SpawnedCount == expected && encounter.AliveCount == expected,
            mode + " spawn count: " + encounter.LastMessage);
        passed.Add(mode + " " + ui.tables[index].ThemeId + "=" + expected);
        ui.Clear();
        yield return null;
    }

    private static EnemyThemeEncounter FindDebugEncounter() =>
        UnityEngine.Object.FindObjectsByType<EnemyThemeEncounter>(FindObjectsSortMode.None)
            .FirstOrDefault(e => e.name == "Theme debug encounter");
}
