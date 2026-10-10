using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Caves;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CaveMainPortalVerifier
{
    const string Key = "Overburst.CavePortalVerifier.";
    static string output;
    static IEnumerator scenario;
    static bool background;
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static CaveMainPortalVerifier()
    { if (SessionState.GetString(Key + "output", "") != "") EditorApplication.update += Tick; }
    static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    public static void StartPlay(string directory)
    {
        CavePlatformMapBuilder.Guard();
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the open scene first.");
        output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 360);
        SessionState.SetBool(Key + "finished", false);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        if (!EditorSceneManager.playModeStartScene) throw new InvalidOperationException("PersistentScene not found.");
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "Account"));
    }
    static void Log(string condition, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition + "\n" + trace); }
    static void Tick()
    {
        output = SessionState.GetString(Key + "output", "");
        if (output == "") { EditorApplication.update -= Tick; return; }
        if (SessionState.GetBool(Key + "finished", false))
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var active = IsolatedSavePlayGuard.ActiveDirectory; var env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
            var own = Path.Combine(output, "Account");
            if ((!string.IsNullOrEmpty(active) && active != own) || (!string.IsNullOrEmpty(env) && env != own)) return;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "startScene", ""));
            IsolatedSavePlayGuard.UseRealAccount();
            Write("return.json", new { status = IsolatedSavePlayGuard.RequiresAccountChoice ? "FAIL" : "PASS", scene = SceneManager.GetActiveScene().path, dirty = SceneManager.GetActiveScene().isDirty });
            SessionState.EraseString(Key + "output"); SessionState.EraseString(Key + "startScene"); SessionState.EraseFloat(Key + "deadline"); SessionState.EraseBool(Key + "finished");
            EditorApplication.update -= Tick; return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) throw new TimeoutException("Cave portal flow timed out.");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (scenario == null)
            { checks.Clear(); errors.Clear(); Application.logMessageReceived += Log; background = Application.runInBackground; Application.runInBackground = true; scenario = Play(); }
            EditorApplication.QueuePlayerLoopUpdate();
            if (!scenario.MoveNext()) Finish(null);
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static void Finish(string error)
    {
        Application.logMessageReceived -= Log;
        Write("play.json", new { status = error == null && errors.Count == 0 ? "PASS" : "FAIL", error, checks, errors });
        scenario = null; Application.runInBackground = background;
        SessionState.SetBool(Key + "finished", true); EditorApplication.ExitPlaymode();
    }
    static IEnumerator Play()
    {
        while (!WorldSessionState.IsHideout || !PersistentSceneFlow.Instance || PersistentSceneFlow.Instance.IsSwitching || !PlayerContext.Instance?.CurrentActor) yield return null;
        Require(WorldSessionState.ContentScene.name == "MainScene", "Product boot reaches MainScene");
        Require(Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath).Resolve<MapItemData>("map.caves").dungeonThemeId == "Caves", "Cave map has its own registered content ID");
        int previousSeed = 0, visit = 0;
        foreach (int count in new[] { 9, 12, 15, 9 })
        {
            var portal = Object.FindObjectsByType<CaveDungeonPortal>(FindObjectsSortMode.None).Single(p => !p.returnToTown);
            Require(portal.destinations.Select(d => d.platforms).SequenceEqual(new[] { 9, 12, 15 }), "Count choices 9 / 12 / 15");
            var actor = PlayerContext.Instance.CurrentActor;
            ActorTeleportUtility.TeleportSafely(actor.transform, portal.transform.position + Vector3.right * 1.3f, Quaternion.identity);
            Require(portal.TryInteract(actor) == InteractionExecutionResult.Succeeded, "Town interaction opens count selector " + count);
            yield return null;
            var panel = Object.FindFirstObjectByType<CavePortalPanel>(); Require(panel, "Count panel exists");
            portal.SelectIndex(0); panel.previous.onClick.Invoke(); Require(portal.SelectedCount == 9, "Lower count clamp");
            panel.next.onClick.Invoke(); Require(portal.SelectedCount == 12, "Next button selects 12");
            panel.next.onClick.Invoke(); panel.next.onClick.Invoke(); Require(portal.SelectedCount == 15, "Upper count clamp");
            while (portal.SelectedCount > count) panel.previous.onClick.Invoke();
            if (count == 9)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "portal-panel.png"));
                for (int f = 0; f < 12; f++) yield return null;
                panel.close.onClick.Invoke(); yield return null;
                Require(!Object.FindFirstObjectByType<CavePortalPanel>(), "Close button removes selector");
                Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Close restores gameplay input");
                portal.TryInteract(actor); panel = Object.FindFirstObjectByType<CavePortalPanel>();
            }
            var started = Time.realtimeSinceStartupAsDouble;
            panel.enter.onClick.Invoke();
            while (PersistentSceneFlow.Instance.IsSwitching) yield return null;
            Require(string.IsNullOrEmpty(PersistentSceneFlow.Instance.RunEntryError), "No entry error " + count);
            Require(WorldSessionState.ContentScene.name == "Caves_Run_" + count && WorldSessionState.Phase == WorldPhase.Run, "Selected cave entered " + count);
            var world = Object.FindFirstObjectByType<CaveWorld>();
            Require(world && world.authoredLayout && world.courts.Count == count, "Actual platform count " + count);
            Require(world.GetComponent<CaveRuntimeGenerator>()?.IsReady == true, "Generated during this entry " + count);
            Require(world.seed != previousSeed, "Every entry uses its own run seed"); previousSeed = world.seed;
            var boundary = Newtonsoft.Json.Linq.JObject.FromObject(CavePlatformBoundaryVerifier.VerifyWorld(world));
            Write("boundary-" + (++visit) + ".json", boundary);
            Require((string)boundary["status"] == "PASS", "Live platform gates and rims pass " + count + ": " + boundary["failures"]);
            Require(!EditorUtility.IsPersistent(world.GetComponentInChildren<Terrain>().terrainData), "Terrain generated in memory");
            Require(!EditorUtility.IsPersistent(world.GetComponent<Unity.AI.Navigation.NavMeshSurface>().navMeshData), "Navigation generated in memory");
            var nav = new UnityEngine.AI.NavMeshPath();
            var disconnected = world.courts.Where(c => !UnityEngine.AI.NavMesh.CalculatePath(world.courts[0].center, c.center, UnityEngine.AI.NavMesh.AllAreas, nav)
                || nav.status != UnityEngine.AI.NavMeshPathStatus.PathComplete).Select(c => c.name).ToArray();
            bool PathOK(Vector3 a,Vector3 b) => UnityEngine.AI.NavMesh.CalculatePath(a,b,UnityEngine.AI.NavMesh.AllAreas,nav) && nav.status==UnityEngine.AI.NavMeshPathStatus.PathComplete;
            Write("navigation-"+visit+".json",new{seed=world.seed,disconnected,links=world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>().Select(c=>{
                var link=c.GetComponent<Unity.AI.Navigation.NavMeshLink>(); var a=link.transform.TransformPoint(link.startPoint); var b=link.transform.TransformPoint(link.endPoint);
                var ca=world.courts.First(x=>x.tile.GetComponent<CavePlatformBoundary>()==c.boundaryA); var cb=world.courts.First(x=>x.tile.GetComponent<CavePlatformBoundary>()==c.boundaryB);
                return new{name=c.name,from=ca.name,to=cb.name,aToStart=PathOK(ca.center,a),endToB=PathOK(b,cb.center),span=PathOK(a,b),a=a.ToString(),b=b.ToString()};}).ToArray()});
            Require(disconnected.Length == 0, "All platform centers have a navigation path: " + string.Join(", ", disconnected));
            var ground = world.GetComponentInChildren<Terrain>();
            var lower = world.transform.Find(CaveRuntimeDetails.RootName);
            Require(lower && lower.Find("Webs anchored in crevices").childCount > 0, "Lower webs generated");
            Require(lower.Find("Egg nests at cliff feet").childCount > 0, "Lower egg nests generated");
            foreach (var web in lower.Find("Webs anchored in crevices").GetComponentsInChildren<Renderer>())
                Require(web.bounds.max.y <= new CaveRuntimeDetails(world.GetComponent<CaveRuntimeGenerator>().assets, world.GetComponent<CaveRuntimeGenerator>()).WebCeiling(world, web.bounds) + .02f, "Added web below platform surface");
            Require(!Object.FindFirstObjectByType<CaveExplorer>() && !Object.FindFirstObjectByType<CaveCamera>(), "Preview actor and camera disabled");
            Require(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.enabled) == 1, "One active audio listener");
            Require(world.Ground(actor.transform.position, out var hit) && Mathf.Abs(actor.transform.position.y - hit.point.y) < 2, "Real player stands on entry platform " + count);
            Require(Overburst.Persistence.AccountGameplaySession.Current.ReadRun().phase == RunPhase.Active, "Account run activated " + count);
            Write("entry-" + visit + ".json", new { count, seed=world.seed, generationMilliseconds=world.generationMilliseconds, seconds = Time.realtimeSinceStartupAsDouble - started, player = actor.transform.position.ToString(), scene = world.gameObject.scene.path });
            for (int f = 0; f < 30; f++) yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "cave-" + count + ".png"));
            for (int f = 0; f < 12; f++) yield return null;
            var exit = Object.FindObjectsByType<CaveDungeonPortal>(FindObjectsSortMode.None).Single(p => p.returnToTown);
            ActorTeleportUtility.TeleportSafely(actor.transform, exit.transform.position, Quaternion.identity);
            Require(exit.TryInteract(actor) == InteractionExecutionResult.StartedTransition, "Cave return interaction " + count);
            while (!WorldSessionState.IsHideout || PersistentSceneFlow.Instance.IsSwitching) yield return null;
            Require(WorldSessionState.ContentScene.name == "MainScene" && !Object.FindFirstObjectByType<CaveWorld>(), "Return unloads cave and reaches town " + count);
            Require(!Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).name.StartsWith("Cave layout probes") || SceneManager.GetSceneAt(i).name.StartsWith("Cave dressing probes")), "Generation probe scenes released");
            Require(Overburst.Persistence.AccountGameplaySession.Current.ReadRun().phase == RunPhase.Failed, "Exploration exit settles run without a boss-clear reward " + count);
            Require(!GameplayInputBlocker.IsGameplayInputBlocked, "Return restores gameplay input " + count);
        }
    }
}
