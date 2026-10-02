using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PlayerLightingRollbackVerifier
{
    const string Key = "Overburst.PlayerLightingRollbackVerifier.";
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static int Cycle { get => SessionState.GetInt(Key + "cycle", 0); set => SessionState.SetInt(Key + "cycle", value); }
    static string Output => SessionState.GetString(Key + "output", "");
    static List<string> Checks => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
    static List<string> Errors => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static PlayerActorRuntime Actor => PlayerContext.Instance?.CurrentActor;
    static bool Ready => AccountBootstrap.Ready && WorldSessionState.IsHideout && Actor != null &&
        PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching;

    static PlayerLightingRollbackVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.update += Restore;
        EditorApplication.playModeStateChanged += State;
        Application.logMessageReceived += Log;
    }

    public static string Begin(string output)
    {
        if (Phase != 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Idle Editor required.");
        var boot = SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        if (!boot.IsValid() || !boot.isLoaded) throw new InvalidOperationException("Loaded PersistentScene required.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output); Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "checks", "[]"); SessionState.SetString(Key + "errors", "[]");
        SessionState.SetString(Key + "realHash", RealHash());
        SessionState.SetString(Key + "active", SceneManager.GetActiveScene().path);
        SessionState.SetString(Key + "status", "RUNNING");
        AssetDatabase.DisallowAutoRefresh(); SessionState.SetBool(Key + "refreshOwned", true);
        SceneManager.SetActiveScene(boot); Cycle = 1; Phase = 1; Deadline();
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        return "Two isolated map lighting rollback cycles started.";
    }

    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        var values = Checks; values.Add("Cycle " + Cycle + ": " + label);
        SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(values));
    }
    static void Deadline() => SessionState.SetString(Key + "deadline",
        (EditorApplication.timeSinceStartup + 120).ToString(System.Globalization.CultureInfo.InvariantCulture));

    static void Tick()
    {
        if (Phase == 0 || EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture))
                throw new TimeoutException("Rollback verification timeout at phase " + Phase);
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || Phase != 9) return;
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "Exit clears isolated account path");
                if (Cycle == 1) { Cycle = 2; Phase = 1; Deadline(); IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount")); }
                else { Check(RealHash() == SessionState.GetString(Key + "realHash", ""), "Real account bytes preserved"); Check(Errors.Count == 0, "Runtime errors 0"); Finish("PASS", null); }
                return;
            }
            if (Errors.Count > 0) throw new InvalidOperationException("Runtime error detected.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase == 1)
            {
                var flow = Object.FindFirstObjectByType<PersistentSceneFlow>(FindObjectsInactive.Include);
                if (flow != null && !flow.gameObject.activeInHierarchy) flow.gameObject.SetActive(true);
                if (!Ready) return;
                Check(Path.GetFullPath(AccountBootstrap.SaveDirectory) == Path.GetFullPath(Path.Combine(Output, "IsolatedAccount")), "Product boot uses isolated account");
                VerifyWorld(SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName));
                Check(RenderSettings.fog, "Camp environment fog retained");
                Check(Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).Length == 2 &&
                    Object.FindObjectsByType<StashInteractable>(FindObjectsSortMode.None).Length == 1, "Both merchants and stash retained");
                var account = AccountGameplaySession.Current;
                var registry = (AccountContentRegistry)typeof(AccountGameplaySession).GetProperty("ContentRegistry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(account);
                var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
                var map = new MapInstanceState {mapContentId = registry.IdFor(definition), level = 1, grade = ItemGrade.Common, monsterThemeId = "CavernMutants"};
                Check(flow.EnterDebugRun(DiamondDungeonWorld.SceneName, map), "Real dungeon entry accepted: " + flow.RunEntryError);
                Phase = 2; Deadline(); return;
            }
            if (Phase == 2)
            {
                if (PersistentSceneFlow.Instance.IsSwitching || WorldSessionState.Phase != WorldPhase.Run || Actor == null) return;
                var scene = SceneManager.GetSceneByName(DiamondDungeonWorld.SceneName);
                VerifyWorld(scene);
                var world = Object.FindFirstObjectByType<DiamondDungeonWorld>();
                foreach (var field in world.Fields) field.enabled = false;
                foreach (var item in world.EventDirector.Events) item.enabled = false;
                Actor.Health.SetMaxHp(1000000, true);
                var driver = Object.FindFirstObjectByType<RunLifetimeDriver>();
                Check(driver != null, "Product run return driver ready"); driver.RequestAbandon();
                Phase = 3; Deadline(); return;
            }
            if (Phase == 3)
            {
                if (!Ready) return;
                Check(!SceneManager.GetSceneByName(DiamondDungeonWorld.SceneName).isLoaded, "Dungeon unloads on return");
                VerifyWorld(SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName));
                SceneManager.SetActiveScene(SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName));
                SceneManager.UnloadSceneAsync(PersistentSceneFlow.HideoutSceneName); Phase = 4; Deadline(); return;
            }
            if (Phase == 4)
            {
                if (SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName).isLoaded) return;
                Check(Type.GetType("PlayerLightingScope, Assembly-CSharp") == null, "Map unload does not recreate lighting manager");
                PersistentSceneFlow.Instance.SwitchHubScene(PersistentSceneFlow.HideoutSceneName, "Default"); Phase = 5; Deadline(); return;
            }
            if (Phase == 5)
            {
                if (!Ready) return;
                VerifyWorld(SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName));
                Phase = 9; Deadline(); EditorApplication.ExitPlaymode();
            }
        }
        catch (Exception error) { Finish("FAIL", error.ToString()); if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); }
    }

    static void VerifyWorld(Scene scene)
    {
        Check(scene.isLoaded && SceneManager.GetActiveScene() == scene, "World loaded and active: " + scene.name);
        Check(Type.GetType("PlayerLightingScope, Assembly-CSharp") == null && Type.GetType("PlayerLightingRendererFeature, Assembly-CSharp") == null,
            "Lighting isolation runtime and pass absent: " + scene.name);
        var body = Actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check(body.Length > 0 && body.All(r => r.renderingLayerMask == 1 && r.lightProbeUsage != LightProbeUsage.CustomProvided),
            "Body variants use authored map light layer and probes: " + scene.name);
        var lights = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
        Check(lights.Length > 0 && lights.All(l => (l.GetUniversalAdditionalLightData().renderingLayers & 1u) != 0),
            "Map lights include player default layer: " + scene.name);
        Check(Camera.main != null && Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing,
            "Game camera applies map post processing: " + scene.name);
        Check(Mathf.Approximately(Actor.GetComponentsInChildren<Light>(true).Single(l => l.name == "PlayerAmbientLight").intensity, .3f),
            "Existing player ambient light unchanged: " + scene.name);
    }

    static string RealHash()
    {
        string root = Path.Combine(Application.persistentDataPath, "Account");
        if (!Directory.Exists(root)) return "ABSENT";
        using (var hash = SHA256.Create()) return string.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p)
            .Select(p => p.Substring(root.Length) + ":" + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(p)))));
    }
    static void Log(string message, string trace, LogType type)
    {
        if (Phase == 0 || (type != LogType.Error && type != LogType.Assert && type != LogType.Exception)) return;
        var values = Errors; if (values.Count < 30 && !values.Contains(message)) { values.Add(message); SessionState.SetString(Key + "errors", JsonConvert.SerializeObject(values)); }
    }
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && Phase != 0)
        {
            SessionState.SetBool(Key + "background", Application.runInBackground); SessionState.SetBool(Key + "backgroundOwned", true);
            Application.runInBackground = true; EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + "reloadOwned", true);
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            if (SessionState.GetBool(Key + "reloadOwned", false)) { EditorApplication.UnlockReloadAssemblies(); SessionState.EraseBool(Key + "reloadOwned"); }
            if (SessionState.GetBool(Key + "backgroundOwned", false)) { Application.runInBackground = SessionState.GetBool(Key + "background", false); SessionState.EraseBool(Key + "backgroundOwned"); }
        }
    }
    static void Finish(string status, string error)
    {
        File.WriteAllText(Path.Combine(Output, "play_result.json"), JsonConvert.SerializeObject(new {status, cycles = Cycle, checks = Checks, errors = Errors, error}, Formatting.Indented));
        SessionState.SetString(Key + "status", status); Phase = 0; SessionState.SetBool(Key + "restore", true);
    }
    static void Restore()
    {
        if (!SessionState.GetBool(Key + "restore", false) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        var scene = SceneManager.GetSceneByPath(SessionState.GetString(Key + "active", ""));
        if (scene.IsValid() && scene.isLoaded) SceneManager.SetActiveScene(scene);
        IsolatedSavePlayGuard.UseRealAccount();
        if (SessionState.GetBool(Key + "reloadOwned", false)) { EditorApplication.UnlockReloadAssemblies(); SessionState.EraseBool(Key + "reloadOwned"); }
        if (SessionState.GetBool(Key + "refreshOwned", false)) { AssetDatabase.AllowAutoRefresh(); SessionState.EraseBool(Key + "refreshOwned"); }
        SessionState.SetBool(Key + "restore", false);
    }
}
