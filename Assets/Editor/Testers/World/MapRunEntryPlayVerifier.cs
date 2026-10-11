using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MapRunEntryPlayVerifier
{
    const string Key = "Overburst.MapRunEntryPlayVerifier.";
    static string output;
    static double deadline;
    static int lastFrame;
    static string lastStage;
    static double lastStageAt;
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static readonly List<string> checks = new List<string>();
    static readonly List<object> observations = new List<object>();
    static MapRunEntryPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(Key + "pending", "") != "") EditorApplication.update += Begin;
        if (SessionState.GetString(Key + "return", "") != "") EditorApplication.update += Return;
    }
    static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }));
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        checks.Add(name); Write("progress.json", new { checks = checks.Count, last = name, utc = DateTime.UtcNow });
    }
    public static void Start(string directory, bool desertOnly = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.ActiveDirectory != "" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))
            throw new InvalidOperationException("Use an idle real-account Editor.");
        output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Use a new owned evidence folder.");
        Directory.CreateDirectory(output);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var state = NewAccountFactory.Create(registry); AccountCharacterCommands.EnsureLegacy(state);
        for (int i = 0; i < 3; i++)
        {
            var item = MapGenerationPolicy.CreateItem(definition, registry, 1, ItemGrade.Common);
            var map = item.mapState; map.seed = 48310;
            if (i < 2) { map.terrain = MapTerrainKind.Cave; map.areaCount = i == 0 ? 4 : 20; map.width = map.depth = 0; map.desertCandidateId = null; }
            else
            {
                var row = MapTerrainCatalog.DesertCandidates[0]; map.terrain = MapTerrainKind.Desert;
                map.desertCandidateId = row.id; map.areaCount = row.areaCount; map.width = row.width; map.depth = row.depth;
            }
            var value = ItemSnapshotCodec.Capture(item, registry); value.acquisitionOrder = state.nextAcquisitionOrder++;
            state.items.Add(value); state.inventory[i] = value.instanceId;
        }
        new EasySaveAccountStore(Path.Combine(output, "Account")).Save(state, "map-entry-fixture");
        Write("fixture.json", state.items.Where(i => i.map != null));
        SessionState.SetString(Key + "pending", output); SessionState.SetString(Key + "return", output);
        SessionState.SetBool(Key + "desertOnly", desertOnly);
        SessionState.SetString(Key + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "scenes", Scenes()); SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 600);
        Write("edit-before.json", new { scenes = Scenes(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), previewCount = EditorSceneManager.previewSceneCount });
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", Path.Combine(output, "Settings"));
        Application.runInBackground = true;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update -= Begin; EditorApplication.update += Begin;
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "Account"));
    }
    static bool OwnPlay => IsolatedSavePlayGuard.ActiveDirectory == Path.Combine(SessionState.GetString(Key + "return", ""), "Account");
    static void Begin()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        output = SessionState.GetString(Key + "pending", "");
        if (output == "") { EditorApplication.update -= Begin; return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) { Finish(new Exception("Boot timeout")); return; }
        if (!EditorApplication.isPlaying || !OwnPlay || !TitleCharacterPanel.Instance) return;
        EditorApplication.update -= Begin; SessionState.EraseString(Key + "pending");
        checks.Clear(); observations.Clear(); work.Clear(); Application.runInBackground = true;
        deadline = EditorApplication.timeSinceStartup + 600; lastFrame = -1; work.Push(Verify()); EditorApplication.update += Tick;
    }
    static IEnumerator Frames(int n) { while (n-- > 0) yield return null; }
    static IEnumerator Wait(Func<bool> ready, string label, float seconds = 100)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!ready() && EditorApplication.timeSinceStartup < end) yield return null;
        Check(ready(), label);
    }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        var generator = UnityEngine.Object.FindFirstObjectByType<Overburst.Caves.CaveRuntimeGenerator>();
        string stage = generator ? generator.Stage : null;
        if (stage != lastStage || (stage != null && EditorApplication.timeSinceStartup - lastStageAt >= 5))
        {
            lastStage = stage; lastStageAt = EditorApplication.timeSinceStartup;
            File.AppendAllText(Path.Combine(output, "stages.jsonl"), JsonConvert.SerializeObject(new { utc = DateTime.UtcNow,
                stage, courts = generator ? generator.GetComponent<Overburst.Caves.CaveWorld>().courts.Count : 0, frame = Time.frameCount }) + "\n");
        }
        if (Time.frameCount == lastFrame && EditorApplication.timeSinceStartup < deadline) return;
        lastFrame = Time.frameCount;
        try
        {
            if (!EditorApplication.isPlaying || !OwnPlay) throw new Exception("Owned Play ended unexpectedly");
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Entry verification timeout");
            while (work.Count > 0)
            {
                var current = work.Peek();
                if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); work.Pop(); continue; }
                if (current.Current is IEnumerator nested) { work.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception e) { Finish(e); }
    }
    static IEnumerator Verify()
    {
        yield return Frames(6); TitleCharacterPanel.Instance.start.onClick.Invoke(); yield return Frames(3);
        TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(() => AccountBootstrap.Ready && WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, "Isolated title and MainScene boot");
        var account = AccountGameplaySession.Current; var flow = PersistentSceneFlow.Instance;
        Check(AccountBootstrap.SaveDirectory == Path.Combine(output, "Account"), "Account is isolated");
        var maps = account.Read().items.Where(i => i.map != null).ToArray();
        var cave4 = maps.Single(i => i.map.terrain == MapTerrainKind.Cave && i.map.areaCount == 4);
        var cave20 = maps.Single(i => i.map.terrain == MapTerrainKind.Cave && i.map.areaCount == 20);
        var desert = maps.Single(i => i.map.terrain == MapTerrainKind.Desert);
        long revision = account.Revision;
        Check(!flow.EnterRun(DiamondDungeonWorld.SceneName, cave4.map, cave4.instanceId), "Wrong destination is rejected before preparation");
        Check(account.Revision == revision && account.Read().items.Any(i => i.instanceId == cave4.instanceId), "Wrong destination preserves map and account revision");
        Check(flow.EnterMapRun(cave20.map, cave20.instanceId), "Begin cancellation case"); flow.CancelRunEntry();
        yield return Wait(() => !flow.IsSwitching && WorldSessionState.IsHideout, "Cancelled preparation returns to hub");
        Check(account.Read().items.Any(i => i.instanceId == cave20.instanceId), "Cancelled map remains owned");
        var selected = SessionState.GetBool(Key + "desertOnly", false) ? new[] { desert } : new[] { cave4, desert, cave20 };
        foreach (var item in selected)
        {
            string label = MapGenerationPolicy.Describe(item.map); double began = EditorApplication.timeSinceStartup;
            Check(flow.EnterMapRun(item.map, item.instanceId), label + ": entry starts");
            yield return Wait(() => !flow.IsSwitching, label + ": transition terminates", 180);
            observations.Add(new { label, seconds = EditorApplication.timeSinceStartup - began, error = flow.RunEntryError, phase = WorldSessionState.Phase.ToString() });
            Write("observations.json", observations);
            if (flow.RunEntryError != null)
                Check(account.Read().items.Any(i => i.instanceId == item.instanceId), label + ": failed preparation preserves map");
            Check(flow.RunEntryError == null && WorldSessionState.Phase == WorldPhase.Run, label + ": entry succeeded: " + flow.RunEntryError);
            Check(account.ReadRun().phase == RunPhase.Active && !account.Read().items.Any(i => i.instanceId == item.instanceId), label + ": map consumed once after preparation");
            var scene = WorldSessionState.ContentScene;
            Check(scene.name == MapTerrainCatalog.SceneNameFor(item.map), label + ": destination matches stored map");
            var gate = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RunWorldGate>()).Single();
            Check(gate.IsReady && gate.EntryPoint && Vector3.Distance(PlayerContext.Instance.CurrentActor.transform.position, gate.EntryPoint.position) < 2, label + ": player placed at prepared entry");
            if (item.map.terrain == MapTerrainKind.Cave)
            {
                var cave = gate.GetComponent<Overburst.Caves.CaveWorld>();
                Check(cave.courts.Count == item.map.areaCount && cave.seed == item.map.seed, label + ": count and seed match saved map");
                Check(!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Overburst.Caves.CaveDungeonPortal>()).Any(), label + ": no premature abandon portal");
            }
            else
            {
                var world = gate.GetComponent<Overburst.Mojave.DesertRunWorld>();
                Check(world && world.AreaCenters.Count == item.map.areaCount, label + ": all areas reachable");
                Check(world.ConservativeObstacleCount > 0, label + ": unreadable rock meshes retain blocking navigation footprints");
                Check(!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Overburst.Mojave.MojaveExplorer>(true)).Any(c => c.isActiveAndEnabled), label + ": authoring explorer disabled");
                Check(!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Overburst.Mojave.MojaveCamera>(true)).Any(c => c.isActiveAndEnabled), label + ": authoring camera disabled");
            }
            yield return Frames(12);
            flow.GetComponent<RunLifetimeDriver>().RequestAbandon();
            yield return Wait(() => !flow.IsSwitching && WorldSessionState.IsHideout, label + ": abandon returns to hub");
            Check(account.ReadRun().phase == RunPhase.Failed, label + ": existing abandon settlement maintained");
        }
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick; EditorApplication.update -= Begin;
        while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
        SessionState.EraseString(Key + "pending"); output = SessionState.GetString(Key + "return", output);
        Write("play-result.json", new { status = error == null ? "PASS" : "FAIL", checks, observations, error = error?.ToString(), scope = "Runtime scene/entry/persistence; combat and pointer UI not covered" });
        if (EditorApplication.isPlaying && OwnPlay) EditorApplication.ExitPlaymode();
        EditorApplication.update -= Return; EditorApplication.update += Return;
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
        string scene = SessionState.GetString(Key + "start", "");
        EditorSceneManager.playModeStartScene = scene == "" ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        if (Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY") == Path.Combine(output, "Settings")) Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", null);
        Application.runInBackground = SessionState.GetBool(Key + "background", false); IsolatedSavePlayGuard.UseRealAccount();
        bool preserved = Scenes() == SessionState.GetString(Key + "scenes", "");
        Write("return-result.json", new { status = preserved && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL", scenes = Scenes(), preserved, blocked = IsolatedSavePlayGuard.RequiresAccountChoice, save = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) });
        foreach (var key in new[] { "return", "start", "scenes" }) SessionState.EraseString(Key + key);
        SessionState.EraseFloat(Key + "deadline"); SessionState.EraseBool(Key + "background"); EditorApplication.update -= Return;
        SessionState.EraseBool(Key + "desertOnly");
    }
}
