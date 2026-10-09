using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AutoFractureCactusTrialVerifier
{
    const string Key = "Overburst.AutoFractureCactusTrialVerifier.";
    static string Output => SessionState.GetString(Key + "output", "");
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static IEnumerator routine;
    static int frame;
    static InputSettings originalInput, ownedInput;
    static bool OwnPlay => EditorApplication.isPlaying && Output != ""
        && IsolatedSavePlayGuard.ActiveDirectory == Path.Combine(Output, "Save");

    static AutoFractureCactusTrialVerifier() { if (Output != "") Subscribe(); }
    static void Subscribe() { EditorApplication.update -= Pump; EditorApplication.update += Pump; }
    static void Write(string file, object data) => File.WriteAllText(Path.Combine(Output, file), JsonConvert.SerializeObject(data, Formatting.Indented));
    static void Check(bool pass, string description) { if (!pass) throw new InvalidOperationException(description); checks.Add(description); }

    public static void Run(string directory)
    {
        RunInternal(directory, false);
    }

    public static void RunTownProps(string directory)
    {
        RunInternal(directory, true);
    }

    static void RunInternal(string directory, bool townProps)
    {
        AutoFractureCactusTrialBuilder.RequireIdle();
        if (Output != "" || IsolatedSavePlayGuard.RequiresAccountChoice || IsolatedSavePlayGuard.ActiveDirectory != ""
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") != "") throw new InvalidOperationException("Another account or verifier owns this Editor.");
        directory = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(directory)) throw new ArgumentException("A fresh artifact directory is required.");
        Directory.CreateDirectory(directory); checks.Clear(); errors.Clear();
        SessionState.SetString(Key + "output", directory);
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetInt(Key + "pid", System.Diagnostics.Process.GetCurrentProcess().Id);
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 240);
        SessionState.SetBool(Key + "started", false); SessionState.SetBool(Key + "return", false); Subscribe();
        SessionState.SetBool(Key + "townProps", townProps);
        try
        {
            Application.runInBackground = true;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AutoFractureCactusTrialBuilder.TownPath);
            Write("progress.json", new { status = "BOOTING" });
            IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "Save"));
        }
        catch (Exception error) { Finish("FAIL", error); }
    }

    public static void Cancel() { if (Output != "") Finish("CANCELLED", new OperationCanceledException()); }

    static void Pump()
    {
        if (Output == "") { EditorApplication.update -= Pump; return; }
        if (SessionState.GetBool(Key + "return", false)) { Return(); return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) { Finish("FAIL", new TimeoutException("Cactus probe timed out.")); return; }
        if (!OwnPlay)
        {
            if (SessionState.GetBool(Key + "started", false) && !EditorApplication.isPlayingOrWillChangePlaymode) Finish("INTERRUPTED", new OperationCanceledException("Play ended during cactus verification."));
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();
        if (!Overburst.Persistence.AccountBootstrap.Ready || PlayerContext.Instance?.CurrentActor == null
            || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.MainSceneName) return;
        if (!SessionState.GetBool(Key + "started", false))
        {
            SessionState.SetBool(Key + "started", true);
            originalInput = InputSystem.settings; ownedInput = Object.Instantiate(originalInput); ownedInput.hideFlags = HideFlags.DontSave;
            ownedInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            ownedInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = ownedInput;
            Application.logMessageReceived += Log; routine = SessionState.GetBool(Key + "townProps", false) ? TownPropsTrial() : Trial(); Write("progress.json", new { status = "TESTING" });
        }
        if (routine == null) { Finish("FAIL", new InvalidOperationException("Domain reload interrupted the cactus probe.")); return; }
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try { if (!routine.MoveNext()) Finish("PASS", null); }
        catch (Exception error) { Finish("FAIL", error); }
    }

    static IEnumerator Trial()
    {
        var actor = PlayerContext.Instance.CurrentActor;
        var plant = Object.FindObjectsByType<AutoFractureCactusTrial>(FindObjectsSortMode.None).SingleOrDefault(x => x.name == AutoFractureCactusTrialBuilder.InstanceName);
        Check(plant != null && plant.name == AutoFractureCactusTrialBuilder.InstanceName, "Saved cactus trial loads in actual MainScene boot");
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Existing greatsword equips in isolated account");
        typeof(PlayerEquipment).GetMethod("SetElementGem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(actor.Equipment, new object[] { null });
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        var melee = actor.PlayerKit.MeleeRuntime; melee.SetManualInputEnabled(true);
        Vector3 direction = plant.transform.forward;
        ActorTeleportUtility.TeleportSafely(actor.transform, plant.transform.position - direction * 7f, Quaternion.LookRotation(direction));
        float until = Time.time + .8f; while (Time.time < until) yield return null;
        Check(plant.BlocksMovement && !plant.Broken, "Intact cactus blocks movement");
        var request = new WeaponActionRequest(WeaponActionSource.PlayerInput, null, direction);
        Check(melee.TryStartAction(request, out _) == WeaponActionResult.Accepted, "Real out-of-range attack starts");
        until = Time.time + 2f; while (Time.time < until) yield return null;
        Check(!plant.Broken, "Out-of-range attack leaves cactus intact");
        ActorTeleportUtility.TeleportSafely(actor.transform, plant.transform.position - direction * 1.65f, Quaternion.LookRotation(direction));
        until = Time.time + .7f; while (Time.time < until) yield return null;

        var cameraRoot = new GameObject("Owned cactus evidence camera");
        var camera = cameraRoot.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
        var data = cameraRoot.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.renderShadows = true;
        camera.transform.position = plant.transform.position + new Vector3(3.2f, 2.8f, -4.7f);
        camera.transform.LookAt(plant.transform.position + Vector3.up * .9f); camera.fieldOfView = 43;
        var rt = new RenderTexture(960, 540, 24); rt.Create();
        var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        int index = 0; float nextShot = 0;
        void Capture(string name)
        {
            var prior = RenderTexture.active;
            try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0,0,960,540),0,0); tex.Apply(); File.WriteAllBytes(Path.Combine(Output,name),tex.EncodeToPNG()); }
            finally { camera.targetTexture = null; RenderTexture.active = prior; }
        }
        void Frame() { if (Time.time < nextShot) return; Capture("frame-" + index++.ToString("000") + ".png"); nextShot = Time.time + .08f; }
        try
        {
            float hp = plant.CurrentHp, actorHp = actor.Health.CurrentHp;
            int healthEvents = 0; plant.OnDamaged += (_, _) => healthEvents++;
            Capture("before.png");
            Check(melee.TryStartAction(request, out _) == WeaponActionResult.Accepted, "Actual close-range weak attack starts");
            until = Time.time + 2f; while (Time.time < until) { Frame(); yield return null; }
            Check(plant.Broken && plant.BreakCount == 1 && !plant.BlocksMovement, "Actual attack breaks cactus exactly once and releases blocking collider");
            Check(healthEvents == 0 && plant.CurrentHp == hp && actor.Health.CurrentHp == actorHp, "Scenery adapter emits no actor HP damage or on-hit HP change");
            Check(plant.GetComponentsInChildren<Rigidbody>().Length == plant.FragmentCount && plant.GetComponentsInChildren<Rigidbody>().All(x => !x.isKinematic && x.useGravity), "Automatically generated fragments run as physical bodies");
            Capture("broken.png");
            ((IDamageable)plant).TakeDamage(new DamageInfo(20, plant.transform.position, actor.gameObject, direction, 0));
            Check(plant.BreakCount == 1, "Duplicate contact cannot break the same cactus twice");
            until = Time.time + 2f; while (Time.time < until) { Frame(); yield return null; }
            Check(plant.DebrisCleared && plant.GetComponentsInChildren<Rigidbody>().Length == 0, "Three-second debris lifetime deactivates all fragment bodies"); Capture("cleared.png");
            plant.ResetPlant();
            Check(!plant.Broken && plant.BlocksMovement && plant.GetComponent<CombatTarget>().enabled, "Reset reuses fragments and restores contact target");
            until = Time.time + .5f; while (Time.time < until) yield return null;
            ActorTeleportUtility.TeleportSafely(actor.transform, plant.transform.position - direction * 1.65f, Quaternion.LookRotation(direction));
            until = Time.time + .4f; while (Time.time < until) yield return null;
            Check(melee.TryStartHeavyAttack(direction) == WeaponActionResult.Accepted, "Actual uncharged heavy attack starts");
            until = Time.time + 3f; while (Time.time < until && !plant.Broken) yield return null;
            Check(plant.Broken && plant.BreakCount == 2, "Actual uncharged heavy breaks reset cactus");
            plant.ResetPlant();
            ((IDamageable)plant).TakeDamage(new DamageInfo(20, plant.transform.position, actor.gameObject, direction, 0, false, false, true));
            Check(!plant.Broken, "Damage-over-time does not break scenery");
            var keyboard = InputSystem.AddDevice<Keyboard>("OwnedFractureProbeKeyboard");
            try
            {
                ((IDamageable)plant).TakeDamage(new DamageInfo(20, plant.transform.position, actor.gameObject, direction, 0));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                for (int i = 0; i < 4; i++) yield return null;
                Check(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input is available before reset key probe");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.F8));
                var trace = new List<object>();
                for (int i = 0; i < 8; i++) { yield return null; trace.Add(new { frame = Time.frameCount, current = Keyboard.current?.name, pressed = keyboard.f8Key.isPressed, edge = keyboard.f8Key.wasPressedThisFrame, blocked = GameplayInputBlocker.IsGameplayInputBlocked, plant.Broken }); }
                Write("reset-input.json", trace);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Check(!plant.Broken && plant.BlocksMovement, "F8 input restores this independent cactus trial");
            }
            finally { InputSystem.RemoveDevice(keyboard); }
            Capture("reset.png");
            Check(errors.Count == 0, "No new runtime errors during trial");
        }
        finally { rt.Release(); Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(cameraRoot); }
    }

    static IEnumerator TownPropsTrial()
    {
        var group = GameObject.Find(AutoFractureTownPropTrialBuilder.GroupName);
        Check(group != null, "Saved town prop group loads through real MainScene boot");
        var plants = group.GetComponentsInChildren<AutoFractureCactusTrial>();
        Check(plants.Length == 3 && plants.All(x => x.FragmentCount == 12), "Three actual town models each contain twelve automatic fragments");
        var actor = PlayerContext.Instance.CurrentActor;
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Existing greatsword equips in isolated account");
        typeof(PlayerEquipment).GetMethod("SetElementGem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(actor.Equipment, new object[] { null });
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        var melee = actor.PlayerKit.MeleeRuntime; melee.SetManualInputEnabled(true);
        var cameraRoot = new GameObject("Owned town prop evidence camera"); var camera = cameraRoot.AddComponent<Camera>();
        camera.CopyFrom(Camera.main); camera.enabled = false; camera.fieldOfView = 42;
        var cameraData = cameraRoot.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); cameraData.renderPostProcessing = true; cameraData.renderShadows = true;
        var rt = new RenderTexture(960,540,24); rt.Create(); var tex = new Texture2D(960,540,TextureFormat.RGB24,false);
        void Capture(string name)
        {
            var prior = RenderTexture.active;
            try { camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,960,540),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Output,name),tex.EncodeToPNG()); }
            finally { camera.targetTexture=null;RenderTexture.active=prior; }
        }
        void Aim(Vector3 position, bool overview)
        {
            camera.transform.position = position + (overview ? new Vector3(2.5f,5f,6.5f) : new Vector3(2.1f,2.2f,3.8f));
            camera.transform.LookAt(position + Vector3.up * (overview ? .35f : .45f));
        }
        try
        {
            Aim(plants[1].transform.position, true); Capture("overview-before.png");
            for (int index = 0; index < plants.Length; index++)
            {
                foreach (var p in plants) p.ResetPlant(); var plant=plants[index]; string id=AutoFractureTownPropTrialBuilder.Names[index];
                Vector3 direction=plant.transform.forward; var request=new WeaponActionRequest(WeaponActionSource.PlayerInput,null,direction);
                ActorTeleportUtility.TeleportSafely(actor.transform,plant.transform.position-direction*7f,Quaternion.LookRotation(direction));
                float until=Time.time+.6f;while(Time.time<until)yield return null;
                Check(melee.TryStartAction(request,out _)==WeaponActionResult.Accepted,id+": out-of-range weak attack starts");
                until=Time.time+2f;while(Time.time<until)yield return null;
                Check(!plant.Broken && plant.BlocksMovement,id+": distant attack leaves original visual and collider intact");
                ActorTeleportUtility.TeleportSafely(actor.transform,plant.transform.position-direction*1.5f,Quaternion.LookRotation(direction));
                until=Time.time+.7f;while(Time.time<until)yield return null;
                Aim(plant.transform.position,false);Capture(id+"-before.png");
                float hp=plant.CurrentHp,actorHp=actor.Health.CurrentHp;int breaks=plant.BreakCount;
                Check(melee.TryStartAction(request,out _)==WeaponActionResult.Accepted,id+": actual close weak attack starts");
                int shot=0;float next=0;until=Time.time+2f;
                while(Time.time<until){if(Time.time>=next){Capture(id+"-frame-"+shot++.ToString("000")+".png");next=Time.time+.08f;}yield return null;}
                Check(plant.Broken && plant.BreakCount==breaks+1 && !plant.BlocksMovement,id+": real attack breaks scenery once and removes movement blocking");
                var bodies=plant.GetComponentsInChildren<Rigidbody>();
                Check(bodies.Length==12 && bodies.All(x=>!x.isKinematic && x.useGravity),id+": twelve automatic pieces have gravity and active rigidbodies");
                Check(plant.CurrentHp==hp && actor.Health.CurrentHp==actorHp,id+": attack gives no scenery HP loss or player HP side effect");
                ((IDamageable)plant).TakeDamage(new DamageInfo(20,plant.transform.position,actor.gameObject,direction,0));
                Check(plant.BreakCount==breaks+1,id+": repeat contact cannot duplicate fracture");
                until=Time.time+2f;while(Time.time<until)yield return null;
                Check(plant.DebrisCleared && plant.GetComponentsInChildren<Rigidbody>().Length==0,id+": debris deactivates after three seconds");
                plant.ResetPlant();Check(!plant.Broken && plant.BlocksMovement && plant.GetComponent<CombatTarget>().enabled,id+": reset restores original model and target");
                Capture(id+"-reset.png");
                ActorTeleportUtility.TeleportSafely(actor.transform,plant.transform.position-direction*1.5f,Quaternion.LookRotation(direction));
                until=Time.time+.6f;while(Time.time<until)yield return null;
                Check(melee.TryStartHeavyAttack(direction)==WeaponActionResult.Accepted,id+": uncharged heavy starts");
                until=Time.time+3f;while(Time.time<until && !plant.Broken)yield return null;
                Check(plant.Broken && plant.BreakCount==breaks+2,id+": actual uncharged heavy breaks restored model");
                until=Time.time+1f;while(Time.time<until)yield return null;
                Write("progress.json",new{status="TESTING",completed=id,checks=checks.Count});
            }
            var all=Object.FindObjectsByType<AutoFractureCactusTrial>(FindObjectsSortMode.None);
            foreach(var plant in all){plant.ResetPlant();((IDamageable)plant).TakeDamage(new DamageInfo(20,plant.transform.position,actor.gameObject,plant.transform.forward,0));}
            Check(all.Length==4 && all.All(x=>x.Broken),"Independent cactus and all three prop adapters are breakable");
            var keyboard=InputSystem.AddDevice<Keyboard>("OwnedTownPropProbeKeyboard");
            try
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());for(int i=0;i<4;i++)yield return null;
                Check(!GameplayInputBlocker.IsGameplayInputBlocked,"Gameplay input is available for global trial reset");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.F8));for(int i=0;i<8;i++)yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                Check(all.All(x=>!x.Broken && x.BlocksMovement),"F8 restores only the four automatic fracture examples together");
            }
            finally{InputSystem.RemoveDevice(keyboard);}
            Aim(plants[1].transform.position,true);Capture("overview-reset.png");
            Check(errors.Count==0,"No new runtime errors during three-prop trial");
        }
        finally{rt.Release();Object.Destroy(rt);Object.Destroy(tex);Object.Destroy(cameraRoot);}
    }

    static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void Finish(string status, Exception error)
    {
        try { (routine as IDisposable)?.Dispose(); routine = null; }
        finally
        {
            Application.logMessageReceived -= Log;
            if (originalInput != null) InputSystem.settings = originalInput;
            if (ownedInput != null) Object.DestroyImmediate(ownedInput);
            originalInput = null; ownedInput = null;
            Write("play-result.json", new { status, error = error?.ToString(), checks, errors });
            SessionState.SetBool(Key + "return", true); SessionState.SetFloat(Key + "returnAfter", (float)EditorApplication.timeSinceStartup + .5f);
            if (OwnPlay) EditorApplication.ExitPlaymode();
        }
    }

    static void Return()
    {
        if (OwnPlay) { EditorApplication.ExitPlaymode(); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "returnAfter", 0)) return;
        if (System.Diagnostics.Process.GetCurrentProcess().Id != SessionState.GetInt(Key + "pid", 0)) throw new InvalidOperationException("Editor owner changed.");
        string expected = Path.Combine(Output, "Save");
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if ((current != "" && current != expected) || (prepared != "" && prepared != expected)) { Write("return.json", new { status = "DEFERRED", reason = "Foreign account preparation" }); return; }
        string start = SessionState.GetString(Key + "startScene", "");
        EditorSceneManager.playModeStartScene = start == "" ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        Application.runInBackground = SessionState.GetBool(Key + "background", false); IsolatedSavePlayGuard.UseRealAccount();
        Write("return.json", new { status = IsolatedSavePlayGuard.RequiresAccountChoice ? "FAIL" : "PASS", IsolatedSavePlayGuard.RequiresAccountChoice,
            environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.ActiveDirectory,
            prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""),
            compilationFailed = EditorUtility.scriptCompilationFailed, startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) });
        foreach (var suffix in new[] { "output", "startScene" }) SessionState.EraseString(Key + suffix);
        foreach (var suffix in new[] { "background", "started", "return", "townProps" }) SessionState.EraseBool(Key + suffix);
        foreach (var suffix in new[] { "deadline", "returnAfter" }) SessionState.EraseFloat(Key + suffix);
        SessionState.EraseInt(Key + "pid"); EditorApplication.update -= Pump;
    }
}
