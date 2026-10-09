using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BreakablePlantExperimentVerifier
{
    const string Key = "Overburst.BreakablePlantExperiment.";
    static readonly List<object> checks = new List<object>();
    static IEnumerator routine;
    static bool subscribed;
    static int frame;
    static string Output => SessionState.GetString(Key + "output", "");
    static bool OwnPlay => EditorApplication.isPlaying && !string.IsNullOrEmpty(Output)
        && string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Path.Combine(Output,"Save"), StringComparison.OrdinalIgnoreCase);
    static BreakablePlantExperimentVerifier() { if (!string.IsNullOrEmpty(Output)) Subscribe(); }
    static void Subscribe()
    {
        if (subscribed) return;
        subscribed = true; EditorApplication.update += Pump;
        EditorApplication.playModeStateChanged += State;
    }
    static void Unsubscribe()
    {
        subscribed = false; EditorApplication.update -= Pump;
        EditorApplication.playModeStateChanged -= State;
    }
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(Output,name),JsonConvert.SerializeObject(value,Formatting.Indented));
    static void Check(bool pass, string name)
    {
        checks.Add(new {name,pass}); if (!pass) throw new InvalidOperationException(name);
    }

    public static void Run(string directory) => Begin(directory,false);
    public static void RunCandidates(string directory) => Begin(directory,true);
    static void Begin(string directory,bool candidates)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(Output))
            throw new InvalidOperationException("Idle EditMode is required.");
        Directory.CreateDirectory(directory); checks.Clear();
        SessionState.SetString(Key+"output",Path.GetFullPath(directory));
        SessionState.SetString(Key+"startScene", EditorSceneManager.playModeStartScene == null ? "" : AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key+"background",Application.runInBackground);
        SessionState.SetBool(Key+"candidates",candidates); SessionState.SetBool(Key+"started",false); SessionState.SetBool(Key+"return",false);
        SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+180);
        Subscribe();
        try
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
            Application.runInBackground = true;
            IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output,"Save"));
            Write("progress.json",new {status="BOOTING"});
        }
        catch (Exception e) { Finish("FAIL",e); }
    }
    static void Pump()
    {
        if (string.IsNullOrEmpty(Output)) {Unsubscribe();return;}
        if (SessionState.GetBool(Key+"return",false)) { Return();return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key+"deadline",0)) {Finish("FAIL",new TimeoutException());return;}
        if (!OwnPlay) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (!Overburst.Persistence.AccountBootstrap.Ready || PlayerContext.Instance?.CurrentActor == null
            || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.MainSceneName) return;
        if (!SessionState.GetBool(Key+"started",false))
        {
            SessionState.SetBool(Key+"started",true);
            routine = SessionState.GetBool(Key+"candidates",false) ? CandidateTrial() : Trial(); Write("progress.json",new {status="TESTING"});
        }
        if (routine == null || frame == Time.frameCount) return;
        frame=Time.frameCount;
        try { if (!routine.MoveNext()) Finish("PASS",null); }
        catch (Exception e) {Finish("FAIL",e);}
    }
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && !SessionState.GetBool(Key+"return",false) && !string.IsNullOrEmpty(Output))
            Finish("INTERRUPTED",null);
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+"return",false))
            SessionState.SetFloat(Key+"returnAfter",(float)EditorApplication.timeSinceStartup+.3f);
    }
    static void Finish(string status, Exception error)
    {
        try {(routine as IDisposable)?.Dispose(); routine=null;}
        finally
        {
            Write("play-result.json",new {status,checks,error=error?.ToString()});
            SessionState.SetBool(Key+"return",true);
            SessionState.SetFloat(Key+"returnAfter",(float)EditorApplication.timeSinceStartup+.3f);
            if (OwnPlay) EditorApplication.ExitPlaymode();
        }
    }
    static void Return()
    {
        if (OwnPlay) {EditorApplication.ExitPlaymode();return;}
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.timeSinceStartup < SessionState.GetFloat(Key+"returnAfter",0)) return;
        string expected = Path.Combine(Output,"Save");
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        string prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        if ((!string.IsNullOrEmpty(current) && current != expected) || (!string.IsNullOrEmpty(prepared) && prepared != expected))
        {Write("return.json",new {status="DEFERRED",reason="foreign account preparation"});return;}
        string oldStart=SessionState.GetString(Key+"startScene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(oldStart)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(oldStart);
        Application.runInBackground=SessionState.GetBool(Key+"background",false);
        IsolatedSavePlayGuard.UseRealAccount();
        Write("return.json",new {status=IsolatedSavePlayGuard.RequiresAccountChoice?"FAIL":"PASS",
            saveDirectory=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),IsolatedSavePlayGuard.ActiveDirectory,
            IsolatedSavePlayGuard.RequiresAccountChoice,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),
            expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires","")});
        foreach(string suffix in new[]{"output","startScene"}) SessionState.EraseString(Key+suffix);
        foreach(string suffix in new[]{"background","started","return","candidates"}) SessionState.EraseBool(Key+suffix);
        foreach(string suffix in new[]{"deadline","returnAfter"}) SessionState.EraseFloat(Key+suffix);
        Unsubscribe();
    }
    static IEnumerator Trial()
    {
        var actor=PlayerContext.Instance.CurrentActor;
        var wall=UnityEngine.Object.FindFirstObjectByType<BreakablePlantExperiment>();
        Check(wall != null && wall.name==BreakablePlantExperimentBuilder.InstanceName,"saved town wall loaded");
        var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"actual greatsword equipped in isolated account");
        typeof(PlayerEquipment).GetMethod("SetElementGem",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
            .Invoke(actor.Equipment,new object[]{null});
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        var melee=actor.PlayerKit.MeleeRuntime; melee.SetManualInputEnabled(true);
        Vector3 direction=wall.transform.forward;
        ActorTeleportUtility.TeleportSafely(actor.transform,wall.transform.position-direction*1.65f,Quaternion.LookRotation(direction));
        float wait=Time.time+.8f;while(Time.time<wait)yield return null;
        var camera=Camera.main;
        Check(camera != null,"game camera exists");
        var captureObject=new GameObject("Owned wall capture camera");
        var capture=captureObject.AddComponent<Camera>(); capture.CopyFrom(camera); capture.enabled=false;
        capture.transform.SetPositionAndRotation(wall.transform.position+new Vector3(4.8f,4.5f,-6.5f),Quaternion.identity);
        capture.transform.LookAt(wall.transform.position+Vector3.up*.6f);capture.fieldOfView=43;
        var rt=new RenderTexture(960,540,24);rt.Create();
        var tex=new Texture2D(960,540,TextureFormat.RGB24,false);
        void Capture(string name)
        {
            var old=RenderTexture.active;
            try {capture.targetTexture=rt;capture.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,960,540),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Output,name),tex.EncodeToPNG());}
            finally {capture.targetTexture=null;RenderTexture.active=old;}
        }
        try
        {
            Capture("before.png");
            float hp=actor.Health.CurrentHp,wallHp=wall.CurrentHp;
            float energy=actor.GetComponent<OverburstElementEnergy>()?.Amount??0;
            Check(wall.BlocksMovement && !wall.Broken && wall.FragmentCount==4,"intact tree has 4 inert fragments and collision");
            var request=new WeaponActionRequest(WeaponActionSource.PlayerInput,null, direction);
            Check(melee.TryStartAction(request,out _) == WeaponActionResult.Accepted,"real weak attack accepted");
            float until=Time.time+3;
            int screenshot=0;float nextFrame=0;
            while(Time.time<until)
            {
                if(Time.time>=nextFrame){Capture("frame-"+(screenshot++).ToString("000")+".png");nextFrame=Time.time+.05f;}
                if(wall.Broken&&wall.BreakCount==1&&screenshot>12)break;
                yield return null;
            }
            Check(wall.Broken&&wall.BreakCount==1&&!wall.BlocksMovement,"actual attack shatters wall once and removes blocking collision");
            Check(Mathf.Abs(actor.Health.CurrentHp-hp)<.001f && Mathf.Abs(wall.CurrentHp-wallHp)<.001f,"scenery hit does not emit actor HP damage or healing");
            Check(Mathf.Abs((actor.GetComponent<OverburstElementEnergy>()?.Amount??0)-energy)<.001f,"scenery hit does not charge elemental energy");
            Capture("broken.png");
            ((IDamageable)wall).TakeDamage(new DamageInfo(20,wall.transform.position,actor.gameObject,direction,0));
            Check(wall.BreakCount==1,"duplicate contact cannot break twice");
            until=Time.time+3.5f;
            while(Time.time<until){if(Time.time>=nextFrame){Capture("frame-"+(screenshot++).ToString("000")+".png");nextFrame=Time.time+.05f;}yield return null;}
            Check(wall.DebrisCleared,"debris settles then clears after lifetime");Capture("cleared.png");
            wall.ResetPlant();Check(!wall.Broken&&wall.BlocksMovement&&!wall.DebrisCleared,"reset restores same fragments and target");
            until=Time.time+.4f;while(Time.time<until)yield return null;
            Check(melee.TryStartHeavyAttack(direction)==WeaponActionResult.Accepted,"real uncharged heavy attack accepted");
            until=Time.time+4;while(Time.time<until&&!wall.Broken)yield return null;
            Check(wall.Broken&&wall.BreakCount==2,"uncharged heavy also shatters restored wall");
            wall.ResetPlant();
            ((IDamageable)wall).TakeDamage(new DamageInfo(20,wall.transform.position,actor.gameObject,direction,0,false,false,true));
            Check(!wall.Broken,"damage-over-time cannot break wall");
            Capture("reset.png");
        }
        finally {rt.Release();UnityEngine.Object.Destroy(tex);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(captureObject);}
    }
    static IEnumerator CandidateTrial()
    {
        var actor=PlayerContext.Instance.CurrentActor;
        var group=GameObject.Find(BreakableTownCandidatesBuilder.InstanceName);
        Check(group!=null,"saved plant candidate group loaded");
        var plants=group.GetComponentsInChildren<BreakablePlantExperiment>(true).OrderBy(p=>p.name).ToArray();
        Check(plants.Length==4,"four actual town model variants loaded");
        Check(GameObject.Find(BreakablePlantExperimentBuilder.InstanceName)!=null,"original tree experiment preserved");
        Check(GameObject.Find("__EXPERIMENT_AUTO_FRACTURE_CACTUS__")!=null,"independent cactus experiment preserved");
        var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"actual greatsword equipped");
        typeof(PlayerEquipment).GetMethod("SetElementGem",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(actor.Equipment,new object[]{null});
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        var melee=actor.PlayerKit.MeleeRuntime;melee.SetManualInputEnabled(true);
        var originalCamera=Camera.main;Check(originalCamera!=null,"product camera available");
        var cameraObject=new GameObject("Owned candidate capture camera");var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(originalCamera);camera.enabled=false;
        var rt=new RenderTexture(960,540,24);rt.Create();var tex=new Texture2D(960,540,TextureFormat.RGB24,false);
        UnityEngine.InputSystem.Keyboard keyboard=null;
        UnityEngine.InputSystem.InputSettings originalInput=null,ownedInput=null;
        void Capture(string name)
        {
            var previous=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,960,540),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Output,name),tex.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;}
        }
        void Frame(Vector3 point,float distance)
        {camera.transform.position=point+new Vector3(1,.8f,-1).normalized*distance;camera.transform.LookAt(point);camera.fieldOfView=44;}
        try
        {
            Frame(group.transform.position+Vector3.up*.45f,11);Capture("all-before.png");
            float hp=actor.Health.CurrentHp,energy=actor.GetComponent<OverburstElementEnergy>()?.Amount??0;
            for(int i=0;i<plants.Length;i++)
            {
                foreach(var p in plants)p.ResetPlant();
                var plant=plants[i];int initialBreaks=plant.BreakCount;string prefix="candidate-"+(i+1);Vector3 direction=group.transform.forward;
                Check(!plant.Broken&&plant.BlocksMovement&&plant.FragmentCount==4,plant.name+": intact four fragments");
                ActorTeleportUtility.TeleportSafely(actor.transform,plant.transform.position-direction*1.65f,Quaternion.LookRotation(-direction));
                float until=Time.time+.8f;while(Time.time<until)yield return null;
                Check(melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput,null,-direction),out _)==WeaponActionResult.Accepted,plant.name+": real miss attack accepted");
                until=Time.time+2;while(Time.time<until)yield return null;
                Check(!plant.Broken,plant.name+": attack facing away cannot break it");
                ActorTeleportUtility.TeleportSafely(actor.transform,plant.transform.position-direction*1.65f,Quaternion.LookRotation(direction));
                until=Time.time+.4f;while(Time.time<until)yield return null;
                Frame(plant.transform.position+Vector3.up*.5f,6);Capture(prefix+"-before.png");
                Check(melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput,null,direction),out _)==WeaponActionResult.Accepted,plant.name+": real weak attack accepted");
                until=Time.time+4;float nextFrame=0;int image=0;bool capturedBroken=false;
                while(Time.time<until)
                {
                    if(Time.time>=nextFrame){Capture(prefix+"-frame-"+(image++).ToString("000")+".png");nextFrame=Time.time+.08f;}
                    if(plant.Broken&&!capturedBroken){Capture(prefix+"-broken.png");capturedBroken=true;}
                    yield return null;
                }
                Check(plant.Broken&&plant.BreakCount==initialBreaks+1&&!plant.BlocksMovement,plant.name+": weak hit breaks once and clears blocking collision");
                Check(plant.DebrisCleared,plant.name+": debris clears after lifetime");Capture(prefix+"-cleared.png");
                Check(Mathf.Abs(actor.Health.CurrentHp-hp)<.001f&&Mathf.Abs((actor.GetComponent<OverburstElementEnergy>()?.Amount??0)-energy)<.001f,plant.name+": actor HP and elemental energy unchanged");
                plant.ResetPlant();Check(!plant.Broken&&plant.BlocksMovement,plant.name+": reusable reset");
                until=Time.time+.5f;while(Time.time<until)yield return null;
                Check(melee.TryStartHeavyAttack(direction)==WeaponActionResult.Accepted,plant.name+": real uncharged heavy attack accepted");
                until=Time.time+4;while(Time.time<until&&!plant.Broken)yield return null;
                Check(plant.Broken&&plant.BreakCount==initialBreaks+2,plant.name+": heavy breaks restored candidate");
                Write(prefix+"-heavy-targets.json",new{attacked=plant.name,brokenTargets=plants.Where(p=>p.Broken).Select(p=>p.name).ToArray()});
                until=Time.time+2;while(Time.time<until)yield return null;plant.ResetPlant();
            }
            foreach(var plant in plants)((IDamageable)plant).TakeDamage(new DamageInfo(20,plant.transform.position,actor.gameObject,group.transform.forward,0));
            Check(plants.All(p=>p.Broken),"prepare four broken plants for actual reset key input");
            originalInput=UnityEngine.InputSystem.InputSystem.settings;ownedInput=UnityEngine.Object.Instantiate(originalInput);ownedInput.hideFlags=HideFlags.DontSave;
            ownedInput.backgroundBehavior=UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            ownedInput.editorInputBehaviorInPlayMode=UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            UnityEngine.InputSystem.InputSystem.settings=ownedInput;
            keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("Owned plant reset test keyboard");
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.R));
            float resetUntil=Time.time+.5f;while(Time.time<resetUntil)yield return null;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());
            resetUntil=Time.time+.2f;while(Time.time<resetUntil)yield return null;
            Check(plants.All(p=>!p.Broken&&p.BlocksMovement),"R input restores all four plants");
            Frame(group.transform.position+Vector3.up*.45f,11);Capture("all-reset.png");
        }
        finally
        {
            if(keyboard!=null&&keyboard.added)UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            if(ownedInput!=null){if(UnityEngine.InputSystem.InputSystem.settings==ownedInput)UnityEngine.InputSystem.InputSystem.settings=originalInput;UnityEngine.Object.Destroy(ownedInput);}
            rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(tex);UnityEngine.Object.Destroy(cameraObject);
        }
    }

}
