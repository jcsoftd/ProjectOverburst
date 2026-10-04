using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Two product boots verify the haze assets, the gameplay camera and safe account return.</summary>
[InitializeOnLoad]
public static class HideoutGroundHazeVerifier
{
    const string Key="Overburst.HideoutGroundHazeVerifier.";
    const string DefaultOutput="../개인파일/코덱스산출/Environment/20261004_HideoutAtmosphere";
    static string Output=>SessionState.GetString(Key+"output","");
    static int Phase {get=>SessionState.GetInt(Key+"phase",0);set=>SessionState.SetInt(Key+"phase",value);}
    static int Cycle {get=>SessionState.GetInt(Key+"cycle",0);set=>SessionState.SetInt(Key+"cycle",value);}
    static double Deadline=>double.Parse(SessionState.GetString(Key+"deadline","0"),System.Globalization.CultureInfo.InvariantCulture);

    static HideoutGroundHazeVerifier()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=State;
        Application.logMessageReceived+=Log;
    }
    public static string Begin()
    {
        if(Phase!=0||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("Another isolated account is prepared.");
        var boot=SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        if(!boot.isLoaded) throw new InvalidOperationException("Loaded PersistentScene required.");
        Directory.CreateDirectory(DefaultOutput);
        SessionState.SetString(Key+"output",Path.GetFullPath(DefaultOutput));
        SessionState.SetString(Key+"before",BarbarianCampUrpVerifier.EditorSnapshot());
        SessionState.SetString(Key+"startScene",EditorSceneManager.playModeStartScene!=null?AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene):"");
        SessionState.SetString(Key+"realHash",RealHash());
        SessionState.SetString(Key+"errors","[]");
        SessionState.SetBool(Key+"refresh",true); AssetDatabase.DisallowAutoRefresh();
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(boot.path);
        Cycle=1; Phase=1; SetDeadline();
        try {IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output,"IsolatedAccount"));}
        catch {Phase=9;throw;}
        return "Two isolated gameplay boots scheduled; all account changes confined to this verifier.";
    }
    static void SetDeadline()=>SessionState.SetString(Key+"deadline",(EditorApplication.timeSinceStartup+150).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
    static void State(PlayModeStateChange state)
    {
        if(Phase==0) return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key+"background",Application.runInBackground); SessionState.SetBool(Key+"backgroundOwned",true);
            Application.runInBackground=true;
            EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key+"reload",true);
        }
        if(state==PlayModeStateChange.ExitingPlayMode)
        {
            if(SessionState.GetBool(Key+"backgroundOwned",false)) {Application.runInBackground=SessionState.GetBool(Key+"background",false);SessionState.EraseBool(Key+"backgroundOwned");}
            if(SessionState.GetBool(Key+"reload",false)) {EditorApplication.UnlockReloadAssemblies();SessionState.EraseBool(Key+"reload");}
            if(Phase!=2&&Phase!=9) {Write("cancelled.json",new{status="FAIL",reason="Play ended before capture"}); Phase=9;}
        }
    }
    static void Tick()
    {
        if(Phase==0||EditorApplication.isCompiling||EditorApplication.isUpdating) return;
        try
        {
            if(Phase==9) {ReturnAccount();return;}
            if(EditorApplication.timeSinceStartup>Deadline) throw new TimeoutException("Ground haze Play timed out.");
            if(!EditorApplication.isPlaying)
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode||Phase!=2) return;
                // Run after all stop handlers, including the save guard, have completed.
                if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))) return;
                IsolatedSavePlayGuard.UseRealAccount();
                if(Cycle==1)
                {
                    Cycle=2;Phase=1;SetDeadline();
                    IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output,"IsolatedAccount"));
                }
                else {Phase=9;ReturnAccount();}
                return;
            }
            if(Path.GetFullPath(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"")!=Path.GetFullPath(Path.Combine(Output,"IsolatedAccount")))
                throw new InvalidOperationException("This verifier no longer owns the active account.");
            var flow=Object.FindFirstObjectByType<PersistentSceneFlow>(FindObjectsInactive.Include);
            if(flow!=null&&!flow.gameObject.activeInHierarchy) flow.gameObject.SetActive(true);
            EditorApplication.QueuePlayerLoopUpdate();
            if(!AccountBootstrap.Ready||!WorldSessionState.IsHideout||flow==null||flow.IsSwitching||PlayerContext.Instance?.CurrentActor==null) return;
            var scene=SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName);
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var haze=all.Where(t=>t.name=="Hideout Ground Haze").ToArray();
            if(haze.Length!=1||!haze[0].gameObject.activeInHierarchy) throw new Exception("One active haze volume required.");
            var renderer=haze[0].GetComponent<MeshRenderer>(); var mat=renderer.sharedMaterial;
            if(!mat.shader.isSupported||mat.GetTexture("_Noise")==null||mat.GetTexture("_GroundHeight")==null) throw new Exception("Runtime haze asset invalid.");
            if(all.Single(t=>t.name=="Camp Ground Mist").gameObject.activeSelf) throw new Exception("Legacy mist would be doubled.");
            if(haze[0].GetComponent<Collider>()!=null) throw new Exception("Haze must not affect collision.");
            if(all.Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)) throw new Exception("Hideout has missing scripts.");
            var camera=Camera.main;
            if(camera==null||!camera.GetUniversalAdditionalCameraData().requiresDepthTexture) throw new Exception("Gameplay camera depth unavailable.");
            // Give the product camera and hideout loading presentation several real frames to settle.
            int frames=SessionState.GetInt(Key+"frames",0)+1;SessionState.SetInt(Key+"frames",frames);
            if(frames<90) return;
            if(!SessionState.GetBool(Key+"captureScheduled",false))
            {
                SessionState.SetBool(Key+"captureScheduled",true);
                var actor=PlayerContext.Instance.CurrentActor;
                actor.StartCoroutine(Capture(camera,renderer));
            }
        }
        catch(Exception e)
        {
            Write("play_failure.json",new {status="FAIL",error=e.ToString(),cycle=Cycle});
            Phase=9;
            if(EditorApplication.isPlaying&&OwnsAccount()) EditorApplication.ExitPlaymode();
        }
    }
    static System.Collections.IEnumerator Capture(Camera camera,Renderer renderer)
    {
        Texture2D texture=null;
        try
        {
            yield return new WaitForEndOfFrame();
            texture=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(Output,"play_"+Cycle+".png"),texture.EncodeToPNG());
            Write("play_cycle_"+Cycle+".json",new {status="PASS_SCOPED",activeVolumes=1,shaderSupported=true,depthTexture=true,
                legacyMistDisabled=true,missingScripts=0,collisionComponents=0,mainCamera=camera.name,
                density=renderer.sharedMaterial.GetFloat("_Density"),time=Time.time,errors=JsonConvert.DeserializeObject(SessionState.GetString(Key+"errors","[]"))});
        }
        finally {if(texture!=null) Object.Destroy(texture);SessionState.EraseBool(Key+"captureScheduled");SessionState.EraseInt(Key+"frames");Phase=2;EditorApplication.ExitPlaymode();}
    }
    static bool OwnsAccount()=>string.Equals(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase);
    static void ReturnAccount()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating) return;
        string current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        if(current.Length>0&&!OwnsAccount()) return;
        string active=IsolatedSavePlayGuard.ActiveDirectory;
        if(active.Length>0&&!string.Equals(active,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase)) return;
        IsolatedSavePlayGuard.UseRealAccount();
        string start=SessionState.GetString(Key+"startScene","");
        EditorSceneManager.playModeStartScene=start.Length==0?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        if(SessionState.GetBool(Key+"reload",false)) {EditorApplication.UnlockReloadAssemblies();SessionState.EraseBool(Key+"reload");}
        if(SessionState.GetBool(Key+"refresh",false)) {AssetDatabase.AllowAutoRefresh();SessionState.EraseBool(Key+"refresh");}
        bool realPreserved=RealHash()==SessionState.GetString(Key+"realHash","");
        bool editorPreserved=BarbarianCampUrpVerifier.EditorSnapshot()==SessionState.GetString(Key+"before","");
        Write("account_return.json",new {status=realPreserved&&editorPreserved&&!IsolatedSavePlayGuard.RequiresAccountChoice?"PASS":"FAIL",
            realAccountBytesPreserved=realPreserved,editorSetupPreserved=editorPreserved,
            blocked=IsolatedSavePlayGuard.RequiresAccountChoice,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"",
            activeDirectory=IsolatedSavePlayGuard.ActiveDirectory,cycles=Cycle});
        Phase=0;
        foreach(string field in new[]{"output","before","startScene","realHash","deadline","errors"}) SessionState.EraseString(Key+field);
        SessionState.EraseInt(Key+"cycle");SessionState.EraseInt(Key+"phase");SessionState.EraseInt(Key+"frames");SessionState.EraseBool(Key+"captureScheduled");
    }
    static void Log(string message,string trace,LogType type)
    {
        if(Phase==0||Phase==9||(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)) return;
        var items=JsonConvert.DeserializeObject<System.Collections.Generic.List<string>>(SessionState.GetString(Key+"errors","[]"));
        if(items.Count<25&&!items.Contains(message)) {items.Add(message);SessionState.SetString(Key+"errors",JsonConvert.SerializeObject(items));}
    }
    static string RealHash()
    {
        string root=Path.Combine(Application.persistentDataPath,"Account"); if(!Directory.Exists(root)) return "ABSENT";
        using(var hash=SHA256.Create()) return string.Join("|",Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(p=>p)
            .Select(p=>p.Substring(root.Length)+":"+BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(p)))));
    }
    static void Write(string file,object result)=>File.WriteAllText(Path.Combine(Output,file),JsonConvert.SerializeObject(result,Formatting.Indented));
}
