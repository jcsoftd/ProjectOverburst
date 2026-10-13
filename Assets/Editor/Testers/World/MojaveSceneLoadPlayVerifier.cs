using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Mojave;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Loads catalog scenes through Unity's runtime loader without beginning monster preparation or a run transaction.
[InitializeOnLoad]
public static class MojaveSceneLoadPlayVerifier
{
    const string Key = "Overburst.MojaveSceneLoadPlayVerifier.";
    static string output;
    static IEnumerator work;
    static bool finishing;
    static int frame;
    static MapInstanceState loadingMap;
    static Exception installError;
    static readonly List<string> checks = new List<string>();
    static readonly List<object> maps = new List<object>();
    static MojaveSceneLoadPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(Key + "pending", "") != "") EditorApplication.update += Begin;
        else if (SessionState.GetString(Key + "return", "") != "") EditorApplication.update += StopInterrupted;
        if (SessionState.GetString(Key + "return", "") != "") EditorApplication.update += Return;
    }
    static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }));
    static bool OwnPlay => IsolatedSavePlayGuard.ActiveDirectory == Path.Combine(SessionState.GetString(Key + "return", ""), "Account");
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static void Check(bool valid, string name)
    {
        if (!valid) throw new InvalidOperationException(name);
        checks.Add(name); Write("progress.json", new { checks = checks.Count, maps = maps.Count, last = name, utc = DateTime.UtcNow });
    }
    public static void Start(string directory, int firstIndex = 0, int mapCount = 30)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || IsolatedSavePlayGuard.ActiveDirectory != ""
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))
            throw new InvalidOperationException("Idle real-account Editor required.");
        if (firstIndex < 0 || mapCount <= 0 || firstIndex + mapCount > 30)
            throw new ArgumentOutOfRangeException(nameof(firstIndex));
        output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Application.dataPath + "/../../개인파일/코덱스산출") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
            throw new ArgumentException("New owned evidence folder required.");
        Directory.CreateDirectory(output);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var account = NewAccountFactory.Create(registry); AccountCharacterCommands.EnsureLegacy(account);
        new EasySaveAccountStore(Path.Combine(output, "Account")).Save(account, "scene-load-only-fixture");
        SessionState.SetString(Key + "pending", output); SessionState.SetString(Key + "return", output);
        SessionState.SetString(Key + "scenes", Scenes()); SessionState.SetString(Key + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetInt(Key + "firstIndex", firstIndex); SessionState.SetInt(Key + "mapCount", mapCount);
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 1200);
        Write("edit-before.json", new { scenes = Scenes(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), previewCount = EditorSceneManager.previewSceneCount, firstIndex, mapCount });
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", Path.Combine(output, "Settings"));
        Application.runInBackground = true;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update -= Begin; EditorApplication.update += Begin;
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "Account"));
    }
    static void Begin()
    {
        output = SessionState.GetString(Key + "pending", "");
        if (output == "") { EditorApplication.update -= Begin; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) { Finish(new Exception("Title boot timeout")); return; }
        if (!EditorApplication.isPlaying || !OwnPlay || !TitleCharacterPanel.Instance) return;
        EditorApplication.update -= Begin; SessionState.EraseString(Key + "pending");
        finishing = false; checks.Clear(); maps.Clear(); frame = -1; work = Verify();
        SceneManager.sceneLoaded += Installed;
        EditorApplication.update += Tick;
    }
    static void Installed(Scene scene, LoadSceneMode mode)
    {
        if (loadingMap == null || scene.path != MapTerrainCatalog.SceneFor(loadingMap)) return;
        try { DesertRunWorld.Install(scene, loadingMap); }
        catch (Exception error) { installError = error; }
    }
    static void Tick()
    {
        if (finishing) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (frame == Time.frameCount && EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "deadline", 0)) return;
        frame = Time.frameCount;
        try
        {
            if (!EditorApplication.isPlaying || !OwnPlay) throw new Exception("Owned Play ended unexpectedly");
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) throw new Exception("Scene-load verification timeout");
            if (!work.MoveNext()) Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Verify()
    {
        Check(OwnPlay && Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) == Path.Combine(output, "Account"),
            "Isolated Play Guard and save environment are active before character selection");
        Check(MapTerrainCatalog.DesertCandidates.Count == 30, "Runtime catalog contains all thirty replacement maps");
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        foreach (var candidate in MapTerrainCatalog.DesertCandidates.Skip(SessionState.GetInt(Key + "firstIndex", 0)).Take(SessionState.GetInt(Key + "mapCount", 30)))
        {
            var map = MapGenerationPolicy.CreateItem(definition, registry, 1, ItemGrade.Common).mapState;
            map.terrain = MapTerrainKind.Desert; map.desertCandidateId = candidate.id;
            map.areaCount = candidate.areaCount; map.width = candidate.width; map.depth = candidate.depth;
            string path = MapTerrainCatalog.SceneFor(map);
            Check(path == candidate.scene && Application.CanStreamedLevelBeLoaded(path), candidate.id + ": game destination is registered and loadable");
            loadingMap = map; installError = null;
            double began = EditorApplication.timeSinceStartup;
            var loading = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            Check(loading != null, candidate.id + ": runtime scene load started");
            while (!loading.isDone) yield return null;
            if (installError != null) throw installError;
            var scene = SceneManager.GetSceneByPath(path);
            Check(scene.IsValid() && scene.isLoaded, candidate.id + ": runtime scene loaded");
            VerifyLoaded(candidate, scene);
            double loadedSeconds = EditorApplication.timeSinceStartup - began;
            loadingMap = null;
            var unloading = SceneManager.UnloadSceneAsync(scene);
            Check(unloading != null, candidate.id + ": runtime unload started");
            while (!unloading.isDone) yield return null;
            Check(!SceneManager.GetSceneByPath(path).isLoaded, candidate.id + ": scene unloaded before the next map");
            maps.Add(new { candidate.id, path, candidate.areaCount, candidate.width, candidate.depth, loadedSeconds, monsterPreparation = "NOT_STARTED" });
            Write("loaded-maps.json", maps);
            // Verification's local asset references have returned before releasing the unloaded scene's assets.
            scene = default; loading = null; unloading = null;
            var releasing = Resources.UnloadUnusedAssets();
            while (!releasing.isDone) yield return null;
            GC.Collect();
            Check(releasing.isDone, candidate.id + ": unused scene asset release completed before continuing");
        }
    }
    static void VerifyLoaded(MapTerrainCatalog.DesertCandidate candidate, Scene scene)
    {
            var roots = scene.GetRootGameObjects();
            var worlds = roots.SelectMany(r => r.GetComponentsInChildren<MojaveWorld>(true)).ToArray();
            Check(worlds.Length == 1, candidate.id + ": one terrain world installed");
            var world = worlds[0]; world.EnsureLayout();
            Check(world.surface && world.surface.terrainData && world.generatedRoot && world.seed == candidate.seed
                && world.bakedSeed == candidate.seed && world.bakedLayoutVersion == MojaveLayout.Version,
                candidate.id + ": baked terrain and seed match the game catalog");
            Check(world.layout.places.Count == candidate.areaCount && world.layout.places.Count(p => p.role == MojaveTileRole.Entrance) == 1
                && world.layout.places.Count(p => p.role == MojaveTileRole.Boss) == 1, candidate.id + ": platform count and entrance/boss roles match");
            var minimap = DesertMinimapData.Load(candidate, world);
            Check(minimap && minimap.combatTiles.Length == candidate.areaCount, candidate.id + ": active minimap resolves against the loaded world");
            var gate = world.GetComponent<RunWorldGate>();
            Check(gate && gate.Context == null && gate.Roster == null && !gate.IsReady
                && roots.All(r => r.GetComponentsInChildren<EnemyActor>(true).Length == 0), candidate.id + ": monster preparation and spawning remain stopped");
            Check(roots.All(r => r.GetComponentsInChildren<MojaveExplorer>(true).All(c => !c.gameObject.activeInHierarchy)
                && r.GetComponentsInChildren<MojaveCamera>(true).All(c => !c.gameObject.activeInHierarchy)
                && r.GetComponentsInChildren<MojavePortal>(true).All(c => !c.gameObject.activeInHierarchy)), candidate.id + ": runtime installation disables authoring controls");
            Check(roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0),
                candidate.id + ": no missing scripts after runtime load");
    }
    static void Finish(Exception error)
    {
        if (finishing) return;
        finishing = true; EditorApplication.update -= Tick; EditorApplication.update -= Begin;
        SceneManager.sceneLoaded -= Installed; (work as IDisposable)?.Dispose(); work = null; loadingMap = null;
        output = SessionState.GetString(Key + "return", output); SessionState.EraseString(Key + "pending");
        Write("play-result.json", new { status = error == null ? "PASS_SCENE_LOADING_AND_CONNECTION" : "FAIL", checks, maps,
            firstIndex = SessionState.GetInt(Key + "firstIndex", 0), mapCount = SessionState.GetInt(Key + "mapCount", 30),
            error = error?.ToString(), scope = "Actual runtime additive scene loading, catalog destination, baked world, roles, minimap, unload and unused asset release for the selected catalog range. Monster preparation/spawn, run completion, bosses and Player are deferred by user." });
        if (EditorApplication.isPlaying && OwnPlay) EditorApplication.ExitPlaymode();
        EditorApplication.update -= Return; EditorApplication.update += Return;
    }
    static void StopInterrupted()
    {
        EditorApplication.update -= StopInterrupted;
        if (EditorApplication.isPlaying && OwnPlay) Finish(new Exception("Assembly reload interrupted owned scene-load Play"));
    }
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(Key + "return", "") != "")
        { EditorApplication.update -= Return; EditorApplication.update += Return; }
    }
    static void Return()
    {
        output = SessionState.GetString(Key + "return", "");
        if (output == "") { EditorApplication.update -= Return; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.ActiveDirectory != "") return;
        string start = SessionState.GetString(Key + "start", "");
        EditorSceneManager.playModeStartScene = start == "" ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        if (Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY") == Path.Combine(output, "Settings")) Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", null);
        Application.runInBackground = SessionState.GetBool(Key + "background", false); IsolatedSavePlayGuard.UseRealAccount();
        bool preserved = Scenes() == SessionState.GetString(Key + "scenes", "");
        Write("return-result.json", new { status = preserved && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL", preserved,
            scenes = Scenes(), blocked = IsolatedSavePlayGuard.RequiresAccountChoice, save = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) });
        foreach (string key in new[] { "return", "start", "scenes" }) SessionState.EraseString(Key + key);
        SessionState.EraseFloat(Key + "deadline"); SessionState.EraseBool(Key + "background"); EditorApplication.update -= Return;
        SessionState.EraseInt(Key + "firstIndex"); SessionState.EraseInt(Key + "mapCount");
    }
}
