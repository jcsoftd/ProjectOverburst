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

/// <summary>Two Hideout boots observe real practice-station circle attacks and readable radial geometry.</summary>
[InitializeOnLoad]
public static class GroundIndicatorPracticeVerifier
{
    const string Key="Overburst.GroundIndicatorPracticeVerifier.";
    const string DefaultOutput="../개인파일/코덱스산출/CombatVfx/20261004_GroundIndicatorReadabilityFix/Play";
    static HideoutParryPracticeStation station;
    static int lastSeenAttack, warningCount;
    static float startHp;
    static double nextStatus;
    static string Output=>SessionState.GetString(Key+"output","");
    static int Phase {get=>SessionState.GetInt(Key+"phase",0);set=>SessionState.SetInt(Key+"phase",value);}
    static int Cycle {get=>SessionState.GetInt(Key+"cycle",0);set=>SessionState.SetInt(Key+"cycle",value);}
    static double Deadline=>double.Parse(SessionState.GetString(Key+"deadline","0"),System.Globalization.CultureInfo.InvariantCulture);

    static GroundIndicatorPracticeVerifier()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=State;
        Application.logMessageReceived+=Log;
    }
    public static string Begin(string outputPath=null)
    {
        if(Phase!=0||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("Another isolated account is prepared.");
        if(IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))) throw new InvalidOperationException("Account return must be complete.");
        var boot=SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        if(!boot.isLoaded) throw new InvalidOperationException("Loaded PersistentScene required.");
        string destination=string.IsNullOrWhiteSpace(outputPath)?DefaultOutput:outputPath;
        Directory.CreateDirectory(destination);
        SessionState.SetString(Key+"output",Path.GetFullPath(destination));
        SessionState.SetString(Key+"before",BarbarianCampUrpVerifier.EditorSnapshot());
        SessionState.SetString(Key+"startScene",EditorSceneManager.playModeStartScene!=null?AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene):"");
        SessionState.SetString(Key+"realHash",RealHash());
        SessionState.SetString(Key+"errors","[]");
        SessionState.SetBool(Key+"refresh",true); AssetDatabase.DisallowAutoRefresh();
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(boot.path);
        Cycle=1; Phase=1; SetDeadline();
        try {IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output,"IsolatedAccount"));}
        catch {Phase=9;throw;}
        return "Two isolated practice-station gameplay boots scheduled.";
    }
    static void SetDeadline()=>SessionState.SetString(Key+"deadline",(EditorApplication.timeSinceStartup+150).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
    static void State(PlayModeStateChange state)
    {
        if(Phase==0) return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            station=null; lastSeenAttack=0; warningCount=0; startHp=0f;
            nextStatus=0; SetDeadline();
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
            if(EditorApplication.timeSinceStartup>Deadline) throw new TimeoutException("Practice-station Play timed out.");
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
            if(EditorApplication.timeSinceStartup>=nextStatus)
            {
                nextStatus=EditorApplication.timeSinceStartup+1;
                Write("progress_"+Cycle+".json",new{ready=AccountBootstrap.Ready,hideout=WorldSessionState.IsHideout,flow=flow!=null,switching=flow!=null&&flow.IsSwitching,player=PlayerContext.Instance?.CurrentActor!=null,time=Time.time,timeScale=Time.timeScale,frames=Time.frameCount,station=station!=null?station.name:"",enemy=station!=null&&station.ActiveActor!=null,startedAttacks=station!=null?station.StartedAttackCount:0,warningsObserved=warningCount,errors=JsonConvert.DeserializeObject(SessionState.GetString(Key+"errors","[]"))});
            }
            if(!AccountBootstrap.Ready||!WorldSessionState.IsHideout||flow==null||flow.IsSwitching||PlayerContext.Instance?.CurrentActor==null) return;
            var player=PlayerContext.Instance.CurrentActor;
            if(station==null)
            {
                var stations=Object.FindObjectsByType<HideoutParryPracticeStation>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
                station=stations.Single(s=>s.StrongAbility!=null && s.StrongAbility.HitAngle>=359.9f);
                foreach(var other in stations) if(other!=station) other.enabled=false;
                player.Health.SetMaxHp(1000000f,true); startHp=player.Health.CurrentHp;
            }
            var enemy=station.ActiveActor;
            if(enemy==null) return;
            var cc=player.GetComponent<CharacterController>(); bool ccEnabled=cc!=null && cc.enabled;
            if(ccEnabled)cc.enabled=false;
            player.transform.position=enemy.transform.position+enemy.transform.forward*.75f+Vector3.up*.05f;
            if(ccEnabled)cc.enabled=true;
            Physics.SyncTransforms();
            var warning=enemy.GetComponent<EnemyStrongAttackWarning>();
            var indicator=warning!=null ? warning.GetComponentsInChildren<ProceduralGroundIndicator>(true).FirstOrDefault(p=>p.Shape==GroundIndicatorShape.Circle && p.IsVisible) : null;
            if(indicator!=null && warning.IsVisible && station.StartedAttackCount>lastSeenAttack)
            {
                if(indicator.Surface==null || indicator.Border==null) throw new Exception("Practice circle layers missing.");
                foreach(var mesh in new[]{indicator.Surface.mesh,indicator.Border.mesh})
                {
                    var vertices=mesh.vertices; var uv=mesh.uv;
                    if(vertices.Length==0 || uv.Length!=vertices.Length || vertices.Any(v=>float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y))) throw new Exception("Invalid radial warning geometry.");
                    if(vertices.Any(v=>new Vector2(v.x,v.y).magnitude>indicator.OuterRadius+.001f)) throw new Exception("Circle warning escaped its numeric radius.");
                }
                lastSeenAttack=station.StartedAttackCount; warningCount++;
                Write("observation_"+Cycle+"_"+warningCount+".json",new{station=station.name,ability=station.StrongAbility.AbilityId,startedAttacks=lastSeenAttack,shape=indicator.Shape.ToString(),vertices=indicator.Surface.mesh.vertexCount,radius=indicator.OuterRadius,usesStandard=warning.UsesStandardIndicator,errors=JsonConvert.DeserializeObject(SessionState.GetString(Key+"errors","[]"))});
            }
            if(warningCount<3 || player.Health.CurrentHp>=startHp) return;
            if(!SessionState.GetBool(Key+"captureScheduled",false))
            {
                SessionState.SetBool(Key+"captureScheduled",true);
                player.StartCoroutine(Capture(Camera.main));
            }
        }
        catch(Exception e)
        {
            Write("play_failure.json",new {status="FAIL",error=e.ToString(),cycle=Cycle});
            Phase=9;
            if(EditorApplication.isPlaying&&OwnsAccount()) EditorApplication.ExitPlaymode();
        }
    }
    static System.Collections.IEnumerator Capture(Camera camera)
    {
        Texture2D texture=null;
        try
        {
            yield return new WaitForEndOfFrame();
            texture=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(Output,"play_"+Cycle+".png"),texture.EncodeToPNG());
            var player=PlayerContext.Instance.CurrentActor;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(GroundIndicatorBuilder.SourceGuid));
            var radial=source.GetComponentsInChildren<ParticleSystemRenderer>(true).Where(r=>r.name.Contains("fill_add_soft")||r.name.Contains("border_add_soft")).Select(r=>r.mesh).ToArray();
            if(radial.Length!=2 || radial.Any(m=>m==null||!m.isReadable)) throw new Exception("Nova source lost runtime readability.");
            var errors=JsonConvert.DeserializeObject<string[]>(SessionState.GetString(Key+"errors","[]"));
            Write("play_cycle_"+Cycle+".json",new{status=errors.Length==0?"PASS_SCOPED":"FAIL",station=station.name,ability=station.StrongAbility.AbilityId,startedAttacks=station.StartedAttackCount,warningsObserved=warningCount,actualPlayerDamage=startHp-player.Health.CurrentHp,sourceReadable=true,sourceMesh=radial[0].name,mainCamera=camera.name,time=Time.time,errors});
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
        string prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        if(prepared.Length>0&&!string.Equals(prepared,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))return;
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
