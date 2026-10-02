using System;
using System.Collections;
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
public static class PlayerMaterialLightingVerifier
{
    const string Key = "Overburst.PlayerMaterialLightingVerifier.";
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static int Cycle { get => SessionState.GetInt(Key + "cycle", 0); set => SessionState.SetInt(Key + "cycle", value); }
    static string Output => SessionState.GetString(Key + "output", "");
    static List<string> Checks => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
    static List<string> Errors => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static Exception captureError;
    static PlayerActorRuntime Actor => PlayerContext.Instance?.CurrentActor;
    static bool Ready => AccountBootstrap.Ready && WorldSessionState.IsHideout && Actor != null &&
        PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching;

    static PlayerMaterialLightingVerifier()
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
        return "Two isolated map material lighting cycles started.";
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
                throw new TimeoutException("Material lighting verification timeout at phase " + Phase);
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
                BeginComparisons(SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName), 6);
                return;
            }
            if (Phase == 11)
            {
                if (captureError != null) throw captureError;
                return;
            }
            if (Phase == 6)
            {
                var flow = PersistentSceneFlow.Instance;
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
                BeginComparisons(scene, 7); return;
            }
            if (Phase == 7)
            {
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
        var materials = body.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
        Check(materials.Length > 0 && materials.All(PlayerMaterialLightingBuilder.IsConfigured),
            "All active/inactive body variants retain player material settings: " + scene.name);
        Actor.GetComponent<PlayerEquipment>().SynchronizeAccountLoadoutVisual();
        Check(body.SelectMany(r => r.sharedMaterials).Where(m => m != null).All(PlayerMaterialLightingBuilder.IsConfigured),
            "Account equipment synchronization preserves body materials: " + scene.name);
        if (WorldSessionState.IsHideout)
            Check(Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None)
                .SelectMany(m => m.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null).All(m => !AssetDatabase.GetAssetPath(m).StartsWith(PlayerMaterialLightingBuilder.MaterialRoot + "/", StringComparison.Ordinal)),
                "NPCs retain their own materials: " + scene.name);
        var lights = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
        Check(lights.Length > 0 && lights.All(l => (l.GetUniversalAdditionalLightData().renderingLayers & 1u) != 0),
            "Map lights include player default layer: " + scene.name);
        Check(Camera.main != null && Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing,
            "Game camera applies map post processing: " + scene.name);
        Check(Mathf.Approximately(Actor.GetComponentsInChildren<Light>(true).Single(l => l.name == "PlayerAmbientLight").intensity, .3f),
            "Existing player ambient light unchanged: " + scene.name);
    }


    static void BeginComparisons(Scene scene, int nextPhase)
    {
        if (Cycle != 1) { Phase = nextPhase; return; }
        Phase = 11; captureError = null; Deadline();
        var gameView = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
        Actor.StartCoroutine(CompareFrames(scene.name, nextPhase));
    }

    static IEnumerator CompareFrames(string scene, int nextPhase)
    {
        var renderers = Actor.GetComponentsInChildren<Renderer>(true);
        var slots = renderers.ToDictionary(r => r, r => r.sharedMaterials);
        var originals = slots.Values.SelectMany(x => x).Where(m => m != null && PlayerMaterialLightingBuilder.IsConfigured(m)).Distinct().ToArray();
        var map = new Dictionary<Material, Material>();
        var animators = Actor.GetComponentsInChildren<Animator>(true).ToDictionary(a => a, a => a.speed);
        var rotation = Actor.transform.rotation;
        var mapping = JsonConvert.DeserializeObject<List<MaterialPair>>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Output), "material_mapping.json")));
        try
        {
            foreach (var animator in animators.Keys) animator.speed = 0f;
            var facing = Camera.main.transform.position - Actor.transform.position;
            facing.y = 0;
            if (facing.sqrMagnitude > .001f) Actor.transform.rotation = Quaternion.LookRotation(facing);
            foreach (var material in originals)
            {
                var pair = mapping.Single(p => p.target == AssetDatabase.GetAssetPath(material));
                var source = AssetDatabase.LoadAssetAtPath<Material>(pair.source);
                map.Add(material, new Material(source) { name = source.name + "_VerificationOnly", hideFlags = HideFlags.HideAndDontSave });
            }
            // Original supplier constants, chosen modest correction, and stronger candidate.
            int count = scene == PersistentSceneFlow.HideoutSceneName ? 3 : 2;
            for (int variant = 0; variant < count; variant++)
            {
                string label = variant == 0 ? "Original" : variant == 1 ? "Applied" : "StrongCandidate";
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = slots[renderer].Select(m => m != null && map.ContainsKey(m) && variant != 1 ? map[m] : m).ToArray();
                if (variant == 2)
                    foreach (var material in map.Values) { material.SetFloat("_MonochromeLighting", 1f); material.SetFloat("_LightMinLimit", .2f); }
                for (int frame = 0; frame < 20; frame++) yield return null;
                yield return new WaitForEndOfFrame();
                try { CaptureFrame(scene + "_" + label); }
                catch (Exception error) { captureError = error; break; }
            }
        }
        finally
        {
            foreach (var pair in slots) if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            foreach (var pair in animators) if (pair.Key != null) pair.Key.speed = pair.Value;
            if (Actor != null) Actor.transform.rotation = rotation;
            foreach (var material in map.Values) Object.Destroy(material);
            if (captureError == null && Phase == 11) Phase = nextPhase;
        }
    }

    static void CaptureFrame(string label)
    {
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            Check(texture != null && texture.width > 0 && texture.height > 0, "Actual postprocessed Game View capture: " + label);
            File.WriteAllBytes(Path.Combine(Output, label + ".png"), texture.EncodeToPNG());
            var bounds = Actor.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).Select(r => r.bounds).ToArray();
            var position = Camera.main.WorldToScreenPoint(Actor.transform.position + Vector3.up);
            File.WriteAllText(Path.Combine(Output, label + ".json"), JsonConvert.SerializeObject(new {
                width=texture.width, height=texture.height,
                playerScreen=new {x=position.x, y=position.y, z=position.z},
                activeBodyRenderers=bounds.Length,
                cameras=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Select(c=>new {c.name,c.enabled}).ToArray()
            }, Formatting.Indented));
        }
        finally { if (texture != null) Object.Destroy(texture); }
    }
    sealed class MaterialPair { public string source { get; set; } public string target { get; set; } }

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
            if (Phase != 0 && Phase != 9) Finish("FAIL", "Verification Play was interrupted.");
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
