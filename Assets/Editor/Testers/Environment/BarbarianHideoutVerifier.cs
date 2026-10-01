using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BarbarianHideoutVerifier
{
    const string Key = "Overburst.BarbarianHideoutVerifier.";
    static string Output => SessionState.GetString(Key + "output", "");
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static int Cycle { get => SessionState.GetInt(Key + "cycle", 0); set => SessionState.SetInt(Key + "cycle", value); }
    static List<string> Checks => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
    static List<string> Errors => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static BarbarianHideoutVerifier()
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
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "checks", "[]");
        SessionState.SetString(Key + "errors", "[]");
        SessionState.SetString(Key + "realHash", RealHash());
        SessionState.SetString(Key + "active", SceneManager.GetActiveScene().path);
        SessionState.SetBool(Key + "dirty", SceneManager.GetActiveScene().isDirty);
        SessionState.SetString(Key + "status", "RUNNING");
        AssetDatabase.DisallowAutoRefresh();
        SessionState.SetBool(Key + "refreshOwned",true);
        Cycle = 1; Phase = 1; Deadline();
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount"));
        return "Two isolated Hideout cycles started.";
    }
    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        var checks = Checks; checks.Add("Cycle " + Cycle + ": " + message);
        SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(checks));
    }
    static void Deadline() => SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 180).ToString(System.Globalization.CultureInfo.InvariantCulture));
    static bool Ready => AccountBootstrap.Ready && WorldSessionState.IsHideout && PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching;
    static PlayerActorRuntime Actor => PlayerContext.Instance?.CurrentActor;
    static void Tick()
    {
        if (Phase == 0 || EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture))
                throw new TimeoutException("Hideout Play timeout at phase " + Phase);
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || Phase != 9) return;
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "Exit cleared isolated account path");
                if (Cycle == 1)
                {
                    Cycle = 2; Phase = 1; Deadline();
                    IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount"));
                }
                else
                {
                    Check(RealHash() == SessionState.GetString(Key + "realHash", ""), "Real account bytes preserved");
                    Check(Errors.Count == 0, "Runtime errors 0");
                    Finish("PASS", null);
                }
                return;
            }
            if (Errors.Count > 0) throw new InvalidOperationException("Runtime error detected; see captured errors.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase == 1)
            {
                // Dirty development scenes can contain an inactive scene-flow object.
                // Activation is confined to this Play session; no scene asset is saved.
                var flow = Object.FindFirstObjectByType<PersistentSceneFlow>(FindObjectsInactive.Include);
                if (flow != null && !flow.gameObject.activeInHierarchy) flow.gameObject.SetActive(true);
                if (!Ready || Actor == null) return;
                Check(Path.GetFullPath(AccountBootstrap.SaveDirectory) == Path.GetFullPath(Path.Combine(Output, "IsolatedAccount")), "Product boot uses isolated account");
                var scene = SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName);
                Check(scene.isLoaded && SceneManager.GetActiveScene() == scene, "Hideout loaded and active");
                var environment = scene.GetRootGameObjects().Single(r => r.name == "Barbarian Camp Environment");
                Check(environment.transform.Find("TD_Barbarian_Camp_Scene").GetComponentsInChildren<Renderer>(true).Length == 514, "Imported camp hierarchy loaded");
                var extensions = environment.transform.Find("Camp Extensions");
                Check(extensions == null && environment.transform.Find("Camp Perimeter") == null && environment.transform.Find("Camp Rest Area") == null, "Original camp composition loaded without expanded additions");
                Check(RenderSettings.fog, "Camp distance fog enabled");
                Check(environment.GetComponentsInChildren<ParticleSystem>(true).Length == 5, "Camp flame, smoke, embers, ground mist and dust loaded");
                Check(environment.GetComponentsInChildren<Light>(true).Any(l => l.type == LightType.Point && l.enabled), "Warm campfire light loaded");
                var spawn = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HubReturnPoint>(true)).Single(p => p.ReturnPointId == "Default");
                Check(Vector2.Distance(new Vector2(Actor.transform.position.x, Actor.transform.position.z), new Vector2(spawn.transform.position.x, spawn.transform.position.z)) < .6f, "Default spawn position applied");
                Check(Actor.transform.position.y > -.1f && Actor.transform.position.y < .6f, "Player grounded on camp floor");
                var fireCentre=environment.transform.Find("TD_Barbarian_Camp_Scene/Boiler").GetComponent<MeshRenderer>().bounds.center;
                var fireDistance=Actor.transform.position-fireCentre;fireDistance.y=0;
                Check(fireDistance.magnitude<=4.5f,"Player starts near campfire ("+fireDistance.magnitude.ToString("F2")+"m)");
                Check(Object.FindObjectsByType<MapDungeonPortal>(FindObjectsSortMode.None).Count(p => p.gameObject.scene == scene) == 1, "One map portal spawned");
                Check(Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Count(p => p.gameObject.scene == scene && p.transform.position.z < -5) > 10, "Configured pickups placed outside south entrance");
                Check(Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Count(p => p.gameObject.scene == scene && Vector3.Distance(p.transform.position, spawn.transform.position) < 5f) == 0, "Camp spawn is clear of pickup grid");
                var weaponMerchant = Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).Single(m => m.name == "WeaponMerchantObject");
                var animator = weaponMerchant.transform.Find("Merchant Visual").GetComponent<Animator>();
                if (!animator.isInitialized || animator.GetCurrentAnimatorStateInfo(0).normalizedTime <= 0) return;
                Check(animator.avatar.isHuman && animator.GetCurrentAnimatorClipInfo(0).Length == 1 && animator.GetCurrentAnimatorStateInfo(0).loop, "NPC-pack merchant idle is playing");
                var provision = Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).Single(m => m != weaponMerchant);
                var provisionAnimator = provision.transform.Find("Merchant Visual").GetComponent<Animator>();
                if (!provisionAnimator.isInitialized || provisionAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime <= 0) return;
                Check(provisionAnimator.avatar.isHuman && provisionAnimator.GetCurrentAnimatorStateInfo(0).loop, "Existing NPC-pack cook is playing provision-merchant idle");
                Check(weaponMerchant.transform.position.z>=6 && provision.transform.position.z>=6, "Both NPCs stay away from south foreground");
                var workshop = scene.GetRootGameObjects().Single(r => r.name == "Weapon Merchant Workshop");
                Check(workshop.transform.childCount == 14 && workshop.GetComponentsInChildren<MeshCollider>(true).Length >= 8, "Craftsman props and solid workshop obstacles loaded");
                Check(workshop.GetComponentsInChildren<ParticleSystem>(true).Single().isPlaying, "Forge flame is playing");
                TestMovement(environment, spawn.transform);
                SessionState.SetInt(Key + "interaction", 0);
                Phase = 2; Deadline();
                return;
            }
            if (Phase == 2)
            {
                var interactables = Services();
                int index = SessionState.GetInt(Key + "interaction", 0);
                if (index >= interactables.Length)
                {
                    var scene=SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName);
                    var pickups=Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Where(p=>p.gameObject.scene==scene).ToArray();
                    Check(pickups.Length>10&&pickups.All(p=>p.transform.position.z < -5),"All startup item pickups remain outside camp entrance");
                    var testBags=Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Where(p=>p.gameObject.scene==scene&&p.name.StartsWith("[TEMP] HideoutBag_",StringComparison.Ordinal)).ToArray();
                    Check(testBags.Length==7&&testBags.All(p=>p.transform.position.z<-5),"All seven temporary grade bags stay outside south entrance");
                    File.WriteAllText(Path.Combine(Output,"pickup_visual_inventory_"+Cycle+".json"),JsonConvert.SerializeObject(Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Where(p=>p.gameObject.scene==scene).Select(p=>new{name=p.name,position=p.transform.position.ToString(),vfx=p.GradeEffect!=null?p.GradeEffect.transform.position.ToString():null}),Formatting.Indented));
                    Capture();
                    PersistentSceneFlow.Instance.SwitchHubScene(PersistentSceneFlow.HideoutSceneName, "DungeonPortal");
                    Phase = 4; Deadline(); return;
                }
                var target = interactables[index];
                var p = Approach(target.InteractionTransform.position);
                ActorTeleportUtility.TeleportSafely(Actor.transform, p, Quaternion.identity);
                Phase = 3; return;
            }
            if (Phase == 3)
            {
                int index = SessionState.GetInt(Key + "interaction", 0);
                var target = Services()[index];
                Check(target.IsInteractionAvailable(Actor), "Service available: " + target.InteractionComponent.name);
                Check(target.TryInteract(Actor) == InteractionExecutionResult.Succeeded, "Service opens: " + target.InteractionComponent.name);
                if (target is MapDungeonPortal mapPortal) mapPortal.ClosePanel();
                foreach (var ui in Object.FindObjectsByType<StashUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ui.Close();
                foreach (var ui in Object.FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ui.Close();
                SessionState.SetInt(Key + "interaction", index + 1); Phase = 2; return;
            }
            if (Phase == 4)
            {
                if (!Ready) return;
                VerifyReturn("DungeonPortal");
                // Exercise loading from disk again through the product hub-return flow.
                SceneManager.SetActiveScene(SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName));
                SceneManager.UnloadSceneAsync(PersistentSceneFlow.HideoutSceneName);
                Phase = 5; Deadline(); return;
            }
            if (Phase == 5)
            {
                if (SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName).isLoaded) return;
                PersistentSceneFlow.Instance.SwitchHubScene(PersistentSceneFlow.HideoutSceneName, "Default");
                Phase = 6; Deadline(); return;
            }
            if (Phase == 6)
            {
                if (!Ready) return;
                VerifyReturn("Default");
                Check(Object.FindObjectsByType<MapDungeonPortal>(FindObjectsSortMode.None).Count() == 1, "Reload recreates exactly one portal");
                Check(Object.FindObjectsByType<StashInteractable>(FindObjectsSortMode.None).Count() == 1, "Reload retains stash");
                Check(Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).Count() == 2, "Reload retains both merchants");
                Check(Object.FindObjectsByType<HideoutParryPracticeStation>(FindObjectsSortMode.None).Count() == 2, "Reload retains both parry stations");
                Phase = 9; Deadline(); EditorApplication.ExitPlaymode();
            }
        }
        catch (Exception error)
        {
            Finish("FAIL", error.ToString());
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }
    static IInteractable[] Services() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
        .Where(c => c is StashInteractable || c is GeneralGoodsMerchantInteractable || c is MapDungeonPortal)
        .OrderBy(c => c.name).Cast<IInteractable>().ToArray();
    static Vector3 Approach(Vector3 target)
    {
        var env = SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName).GetRootGameObjects().Single(r => r.name == "Barbarian Camp Environment");
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI / 8;
            var p = new Vector3(target.x + Mathf.Sin(a) * 1.9f, .12f, target.z + Mathf.Cos(a) * 1.9f);
            var workshop = env.scene.GetRootGameObjects().Single(r => r.name == "Weapon Merchant Workshop");
            if (BarbarianHideoutBuilder.ClearCapsule(env, p) && BarbarianHideoutBuilder.ClearCapsule(workshop,p)) return p;
        }
        throw new InvalidOperationException("No clear service approach: " + target);
    }
    static void VerifyReturn(string id)
    {
        var point = Object.FindObjectsByType<HubReturnPoint>(FindObjectsSortMode.None).Single(p => p.ReturnPointId == id);
        var d = Actor.transform.position - point.transform.position; d.y = 0;
        Check(d.magnitude < .6f && Actor.transform.position.y > -.1f && Actor.transform.position.y < .6f, "Product return anchor grounded: " + id);
    }
    static void TestMovement(GameObject environment, Transform spawn)
    {
        Capture(true);
        Physics.SyncTransforms();
        var movement = Actor.GetComponent<PlayerMovement>();
        Check(movement != null && movement.Motor != null, "Product character motor ready");
        var start = Actor.transform.position;
        movement.Motor.MoveDirect(Vector3.forward * .3f);
        Check(Vector3.Distance(start, Actor.transform.position) > .15f, "Product motor moves on camp floor");
        ActorTeleportUtility.TeleportSafely(Actor.transform, spawn.position, spawn.rotation);
        var boiler = environment.GetComponentsInChildren<MeshCollider>(true).First(c => c.name == "Boiler");
        RaycastHit hit = default;
        bool found = false;
        foreach (float height in new[] { .5f, .9f, 1.3f, 1.6f })
        {
            for (int i = 0; i < 16 && !found; i++)
            {
                var direction = new Vector3(Mathf.Sin(i * Mathf.PI / 8), 0, Mathf.Cos(i * Mathf.PI / 8));
                var origin = boiler.bounds.center + direction * 3f; origin.y = height;
                found = boiler.Raycast(new Ray(origin, -direction), out hit, 6f);
            }
            if (found) break;
        }
        Check(found, "Cauldron has a solid side wall");
        var normal = hit.normal; normal.y = 0; normal.Normalize();
        var foot = hit.point + normal * 1.5f; foot.y = .12f;
        ActorTeleportUtility.TeleportSafely(Actor.transform, foot, Quaternion.identity);
        var before = Actor.transform.position;
        for (int i = 0; i < 30; i++) movement.Motor.MoveDirect(-normal * .1f);
        float travel = Vector3.Dot(Actor.transform.position - before, -normal);
        Check(travel < 1.8f, "Product motor is blocked by camp cauldron (travel " + travel.ToString("F2") + "m)");
        ActorTeleportUtility.TeleportSafely(Actor.transform, spawn.position, spawn.rotation);
    }
    static void Capture(bool atSpawn=false)
    {
        var camera = Camera.main;
        if (camera != null) SaveCamera(camera, Path.Combine(Output, "game_camera_" + Cycle + ".png"));
        if (camera != null && atSpawn) SaveCamera(camera,Path.Combine(Output,"game_camera_spawn_"+Cycle+".png"));
        var root = new GameObject("Hideout Verification Overview Camera");
        try
        {
            var overview = root.AddComponent<Camera>();
            overview.transform.position = new Vector3(0, 35, -20);
            overview.transform.LookAt(new Vector3(0, 0, 9));
            overview.orthographic = true; overview.orthographicSize = 21;
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData cameraData = overview.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            overview.clearFlags = CameraClearFlags.Skybox;
            SaveCamera(overview, Path.Combine(Output, "camp_overview_" + Cycle + ".png"));
            var merchant = Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).Single(m => m.name == "WeaponMerchantObject");
            overview.transform.position = merchant.transform.position + new Vector3(-4,8,-7);
            overview.transform.LookAt(merchant.transform.position + new Vector3(2,1,0));
            overview.orthographicSize = 5;
            SaveCamera(overview, Path.Combine(Output, "weapon_workshop_" + Cycle + ".png"));
        }
        finally { Object.DestroyImmediate(root); }
    }
    static void SaveCamera(Camera camera, string path)
    {
        var previous = camera.targetTexture;
        var active = RenderTexture.active;
        var target = RenderTexture.GetTemporary(1600, 1000, 24);
        var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture); }
    }
    static void State(PlayModeStateChange state)
    {
        if (Phase == 0 && !SessionState.GetBool(Key + "backgroundOwned", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + "background", Application.runInBackground); SessionState.SetBool(Key + "backgroundOwned", true);
            Application.runInBackground = true;
            EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + "reloadOwned",true);
        }
        if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key + "reloadOwned",false)) { EditorApplication.UnlockReloadAssemblies(); SessionState.EraseBool(Key + "reloadOwned"); }
        if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key + "backgroundOwned", false)) { Application.runInBackground = SessionState.GetBool(Key + "background", false); SessionState.EraseBool(Key + "backgroundOwned"); }
    }
    static void Log(string message, string trace, LogType type)
    {
        if (Phase == 0 || (type != LogType.Error && type != LogType.Assert && type != LogType.Exception)) return;
        var errors = Errors;
        if (errors.Count >= 50 || errors.Contains(message)) return;
        errors.Add(message); SessionState.SetString(Key + "errors", JsonConvert.SerializeObject(errors));
    }
    static string RealHash()
    {
        string root = Path.Combine(Application.persistentDataPath, "Account");
        if (!Directory.Exists(root)) return "ABSENT";
        using (var hash = SHA256.Create()) return string.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p)
            .Select(p => p.Substring(root.Length) + ":" + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(p)))));
    }
    static void Finish(string status, string error)
    {
        File.WriteAllText(Path.Combine(Output, "play_result.json"), JsonConvert.SerializeObject(new { status, environment=BarbarianHideoutBuilder.EnvironmentPath, checks = Checks, errors = Errors, error }, Formatting.Indented));
        SessionState.SetString(Key + "status", status); Phase = 0; SessionState.SetBool(Key + "restore", true);
    }
    static void Restore()
    {
        if (!SessionState.GetBool(Key + "restore", false) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        var scene = SceneManager.GetSceneByPath(SessionState.GetString(Key + "active", ""));
        if (scene.IsValid() && scene.isLoaded) SceneManager.SetActiveScene(scene);
        IsolatedSavePlayGuard.UseRealAccount();
        if (SessionState.GetBool(Key + "reloadOwned",false)) { EditorApplication.UnlockReloadAssemblies(); SessionState.EraseBool(Key + "reloadOwned"); }
        if (SessionState.GetBool(Key + "refreshOwned",false)) { AssetDatabase.AllowAutoRefresh(); SessionState.EraseBool(Key + "refreshOwned"); }
        SessionState.SetBool(Key + "restore", false);
    }

}
