using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class OverburstBgmVerifier
{
    private const string K = "Overburst.BgmVerifier.";
    private const string Destination = "../개인파일/코덱스산출/Audio/20261013_BgmApply/Evidence/Play";
    private static readonly List<string> checks = new List<string>();
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IEnumerator work;
    private static int frame = -1;
    private static double deadline;
    private static string output;
    private sealed class ClipRow { public string id, path, guid, action; }

    static OverburstBgmVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (!string.IsNullOrEmpty(SessionState.GetString(K + "output", "")))
            EditorApplication.update += Tick;
        if (!string.IsNullOrEmpty(SessionState.GetString(K + "return", "")))
            EditorApplication.update += Return;
    }

    public static void Native(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        checks.Clear(); output = Path.Combine(Path.GetFullPath(directory), "Evidence");
        var catalog = Resources.Load<OverburstBgmCatalog>(OverburstBgmCatalog.ResourcePath);
        Check(catalog != null, "Resources catalog loads");
        var rows = JsonConvert.DeserializeObject<ClipRow[]>(File.ReadAllText(Path.Combine(directory, "selected-import.json")));
        var approved = rows.ToDictionary(r => r.id, r => AssetDatabase.LoadAssetAtPath<AudioClip>(r.path));
        foreach (var row in rows)
        {
            Check(approved[row.id] && approved[row.id].length > 10, row.id + " native full clip");
            Check(AssetDatabase.AssetPathToGUID(row.path) == row.guid, row.id + " original GUID");
            if (row.action == "reuse") continue;
            var importer = (AudioImporter)AssetImporter.GetAtPath(row.path);
            Check(importer.defaultSampleSettings.loadType == AudioClipLoadType.Streaming && !importer.defaultSampleSettings.preloadAudioData, row.id + " streaming import");
        }
        Check(catalog.title == approved["S01"] && catalog.tutorial == approved["F05"] && catalog.town == approved["F35"], "approved single tracks");
        Check(catalog.outdoor.SequenceEqual(new[] { approved["F18"], approved["F15"], approved["S20"] }), "outdoor exact three tracks");
        Check(catalog.desert.SequenceEqual(new[] { approved["S17"], approved["F20"] }), "desert exact two tracks");
        Check(catalog.cave.SequenceEqual(new[] { approved["L05"], approved["F13"], approved["F14"], approved["F15"] }), "cave exact four tracks");
        foreach (var list in new[] { catalog.outdoor, catalog.desert, catalog.cave })
        {
            var seen = new HashSet<AudioClip>(); AudioClip previous = null;
            var rng = new System.Random(73130);
            for (int i = 0; i < 300; i++)
            {
                var next = OverburstBgmPlayer.Pick(list, previous, rng);
                if (!list.Contains(next) || next == previous) throw new InvalidOperationException("Random selection escaped approved list or repeated immediately.");
                seen.Add(next); previous = next;
            }
            Check(seen.Count == list.Length, "random coverage/no-repeat list size " + list.Length);
        }
        Check(OverburstBgmPlayer.Pick(null, null, new System.Random()) == null, "missing playlist is silent");
        Check(OverburstBgmPlayer.Pick(new AudioClip[] { null, catalog.title, catalog.title }, catalog.title, new System.Random()) == catalog.title, "single/duplicate/null entries remain safe");
        Write("native-result.json", new { success = true, checks, utc = DateTime.UtcNow });
    }

    public static void StartPlay()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))) throw new InvalidOperationException("Settings are owned by another session.");
        output = Path.GetFullPath(Destination); Directory.CreateDirectory(output);
        var save = Path.Combine(output, "isolated-save"); Directory.CreateDirectory(save);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var seed = NewAccountFactory.Create(registry); AccountCharacterCommands.EnsureLegacy(seed);
        new EasySaveAccountStore(save).Save(seed, seed.lastTransactionId);
        SessionState.SetString(K + "output", output); SessionState.SetString(K + "return", output);
        SessionState.SetString(K + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(K + "background", Application.runInBackground);
        SessionState.SetString(K + "scenes", Scenes());
        SessionState.SetInt(K + "input", UnityEngine.InputSystem.InputSystem.settings.GetInstanceID());
        SessionState.SetFloat(K + "deadline", (float)EditorApplication.timeSinceStartup + 180);
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", Path.Combine(output, "isolated-settings"));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(save); }
        catch (Exception error) { Finish(error); }
    }

    private static bool OwnPlay() => string.Equals(IsolatedSavePlayGuard.ActiveDirectory,
        Path.Combine(SessionState.GetString(K + "return", ""), "isolated-save"), StringComparison.OrdinalIgnoreCase);
    private static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty }; }));
    private static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && !string.IsNullOrEmpty(SessionState.GetString(K + "output", "")))
        { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
        if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(K + "return", "")))
        { SessionState.SetFloat(K + "returnDeadline", (float)EditorApplication.timeSinceStartup + 180); EditorApplication.update -= Return; EditorApplication.update += Return; }
    }
    private static void Tick()
    {
        output = SessionState.GetString(K + "output", "");
        if (string.IsNullOrEmpty(output)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (!OwnPlay()) throw new InvalidOperationException("Owned account changed; no other Play will be stopped.");
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(K + "deadline", 0)) throw new TimeoutException("BGM Play deadline");
            Application.runInBackground = true;
            if (work == null)
            {
                if (!TitleCharacterPanel.Instance || !OverburstBgmPlayer.Instance || OverburstBgmPlayer.Instance.Context != OverburstBgmContext.Title) return;
                checks.Clear(); deadline = EditorApplication.timeSinceStartup + 120; work = Verify();
            }
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("BGM verification deadline");
            if (frame == Time.frameCount) return; frame = Time.frameCount;
            if (!work.MoveNext()) Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    private static IEnumerator Verify()
    {
        var player = OverburstBgmPlayer.Instance; var source = player.Source; var catalog = player.Catalog;
        Check(UnityEngine.Object.FindObjectsByType<OverburstBgmPlayer>(FindObjectsSortMode.None).Length == 1, "single persistent music host");
        Check(source.clip == catalog.title && source.loop && source.spatialBlend == 0 && source.ignoreListenerPause, "title loop and 2D/menu setup");
        double end = EditorApplication.timeSinceStartup + 1;
        while (EditorApplication.timeSinceStartup < end) yield return null;
        float before = source.time; var title = TitleCharacterPanel.Instance;
        title.Show("characters"); end = EditorApplication.timeSinceStartup + .6;
        while (EditorApplication.timeSinceStartup < end) yield return null;
        Check(source.isPlaying && source.time > before && source.clip == catalog.title, "title to character screen keeps playback position");
        before = source.time; title.Show("classes"); end = EditorApplication.timeSinceStartup + .6;
        while (EditorApplication.timeSinceStartup < end) yield return null;
        Check(source.time > before && player.Context == OverburstBgmContext.Title, "class screen keeps title track");
        before = source.time;
        title.classNext.onClick.Invoke(); title.nameInput.text = "Bgm검증기사"; title.nameNext.onClick.Invoke();
        end = EditorApplication.timeSinceStartup + .7;
        while (EditorApplication.timeSinceStartup < end) yield return null;
        Check(Overburst.Appearance.AppearanceCustomizationPanel.IsOpen && Overburst.Appearance.AppearanceCustomizationPanel.Instance.IsCreation,
            "actual creation customization screen opens");
        Check(source.clip == catalog.title && source.time > before && player.Context == OverburstBgmContext.Title,
            "customization keeps the same title playback position");
        // Test the actual audio endpoint without changing accounts, map generation or world populations.
        player.enabled = false;
        player.SetContext(OverburstBgmContext.Town); before = source.time;
        end = EditorApplication.timeSinceStartup + .5;
        while (EditorApplication.timeSinceStartup < end) yield return null;
        player.SetContext(OverburstBgmContext.Town);
        Check(source.clip == catalog.town && source.loop && source.time > before, "same town context preserves day/night playback");
        player.SetContext(OverburstBgmContext.Tutorial);
        Check(source.clip == catalog.tutorial && source.loop, "tutorial selected track");
        foreach (var context in new[] { OverburstBgmContext.Outdoor, OverburstBgmContext.Desert, OverburstBgmContext.Cave })
        {
            player.SetContext(context); var prior = source.clip;
            end = EditorApplication.timeSinceStartup + .7;
            while (EditorApplication.timeSinceStartup < end) yield return null;
            Check(source.isPlaying && !source.loop && catalog.Playlist(context).Contains(prior), context + " approved random track plays");
            source.time = Mathf.Max(0, prior.length - .2f);
            end = EditorApplication.timeSinceStartup + 2;
            while (source.isPlaying && EditorApplication.timeSinceStartup < end) yield return null;
            Check(!source.isPlaying, context + " first track reaches real completion");
            // Keep automatic context polling out of this endpoint test, then execute its actual completion branch.
            typeof(OverburstBgmPlayer).GetField("nextContextCheck", Private).SetValue(player, float.MaxValue);
            typeof(OverburstBgmPlayer).GetMethod("Update", Private).Invoke(player, null);
            Check(source.clip != prior && catalog.Playlist(context).Contains(source.clip), context + " completion selects a different approved next track");
        }
        player.SetContext(OverburstBgmContext.None);
        Check(!source.isPlaying && source.clip == null, "excluded hideout context stops music");
        OverburstGameSettings.MasterVolume = .23f;
        Check(Mathf.Approximately(AudioListener.volume, .23f), "existing master volume still controls listener");
        Write("play-result.json", new { success = true, checks, utc = DateTime.UtcNow, scope = "actual title/character/class screens plus actual AudioSource playback/completion for selected contexts; full world entries NOT_RUN" });
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks.Add(message);
    }
    private static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    private static void Finish(Exception error)
    {
        if (error != null) Write("play-result.json", new { success = false, checks, error = error.ToString(), utc = DateTime.UtcNow });
        (work as IDisposable)?.Dispose(); work = null; EditorApplication.update -= Tick;
        SessionState.EraseString(K + "output");
        if (OwnPlay() && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        else if (!EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.update -= Return; EditorApplication.update += Return; }
    }
    private static void Return()
    {
        output = SessionState.GetString(K + "return", "");
        if (string.IsNullOrEmpty(output)) { EditorApplication.update -= Return; return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(K + "returnDeadline", float.MaxValue))
        {
            Write("return-result.json", new { success = false, deferred = true, reason = "Return wait expired; saved keys retained for next EditMode return." });
            EditorApplication.update -= Return; return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))) return;
        var start = SessionState.GetString(K + "startScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", null);
        Application.runInBackground = SessionState.GetBool(K + "background", false);
        IsolatedSavePlayGuard.UseRealAccount();
        bool success = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && Scenes() == SessionState.GetString(K + "scenes", "") && UnityEngine.InputSystem.InputSystem.settings.GetInstanceID() == SessionState.GetInt(K + "input", 0);
        Write("return-result.json", new { success, stopped = !EditorApplication.isPlayingOrWillChangePlaymode, guard = IsolatedSavePlayGuard.RequiresAccountChoice, scenes = Scenes(), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), utc = DateTime.UtcNow });
        foreach (var suffix in new[] { "return", "startScene", "scenes", "output" }) SessionState.EraseString(K + suffix);
        SessionState.EraseBool(K + "background"); SessionState.EraseInt(K + "input"); SessionState.EraseFloat(K + "deadline"); SessionState.EraseFloat(K + "returnDeadline");
        EditorApplication.update -= Tick;
        EditorApplication.update -= Return;
    }
}
