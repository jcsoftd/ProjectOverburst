using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Creates independent candidates. Gameplay definitions and supplier clips are never changed.
public static class SwordIdleAttackCopyBuilder
{
    public static System.Threading.Tasks.Task<string> WhenIdle(string output)
    {
        var task=new System.Threading.Tasks.TaskCompletionSource<string>();double deadline=EditorApplication.timeSinceStartup+180,stable=0;
        EditorApplication.CallbackFunction update=null;AssemblyReloadEvents.AssemblyReloadCallback reload=null;
        Action<Exception> finish=e=>{EditorApplication.update-=update;AssemblyReloadEvents.beforeAssemblyReload-=reload;if(e!=null)task.TrySetException(e);};
        reload=()=>finish(new InvalidOperationException("Reload interrupted candidate save queue."));
        update=()=>{try{if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating){stable=0;if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Shared Editor stayed busy; no candidates saved.");return;}if(stable==0){stable=EditorApplication.timeSinceStartup;return;}if(EditorApplication.timeSinceStartup-stable<2)return;string result=Main(output);finish(null);task.TrySetResult(result);}catch(Exception e){finish(e);}};
        EditorApplication.update+=update;AssemblyReloadEvents.beforeAssemblyReload+=reload;return task.Task;
    }
    public const string Folder = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/SwordIdleAdapted";
    public const string Definition = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Definition/GreatswordDefinition.asset";
    public const string IdlePath = "Assets/ThirdParty/03_애니메이션/Sword_Animations_Pack/Animation/Humanoid/01_Idle/Idle_Combat.anim";
    public sealed class Target { public string role; public MeleeComboStepData step; public bool adaptEntry=true; }
    public static List<Target> Targets()
    {
        var d=AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(Definition);
        var result=new List<Target>();
        for(int i=0;i<d.comboDefinition.steps.Length;i++)result.Add(new Target{role="Combo"+(i+1),step=d.comboDefinition.steps[i],adaptEntry=true});
        result.Add(new Target{role="Heavy",step=d.heavyAttackDefinition.attack,adaptEntry=true});
        result.Add(new Target{role="ParriedHeavy",step=d.parriedHeavyAttackDefinition.attack});
        if(d.dodgeAttackDefinition!=null)for(int i=0;i<d.dodgeAttackDefinition.steps.Length;i++)result.Add(new Target{role="DodgeLight"+(i+1),step=d.dodgeAttackDefinition.steps[i]});
        if(d.dashHeavyAttackDefinition!=null)result.Add(new Target{role="DashHeavy",step=d.dashHeavyAttackDefinition.attack});
        return result;
    }

    static bool Busy()=>EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating;
    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace(Path.DirectorySeparatorChar,'/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    public static string Hash(string path)
    {
        using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
    }
    public static string Main(string output)=>Build(output,true);
    public static string Analyze(string output)=>Build(output,false);
    static string Build(string output,bool save)
    {
        if(Busy())throw new InvalidOperationException("Build candidates only in idle EditMode.");
        Directory.CreateDirectory(output);
        var targets=Targets();if(targets.Count!=8||targets.Select(t=>t.role).Distinct().Count()!=8)throw new Exception("Expected the owned eight greatsword attacks.");
        var sourceHashes=targets.Select(t=>Hash(AssetDatabase.GetAssetPath(t.step.animationClip))).ToArray();string definitionHash=Hash(Definition),idleHash=Hash(IdlePath);
        string cache=SwordIdleNativeAttackAuthoring.Build(output);
        var objects=UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(cache);var clips=objects.OfType<AnimationClip>().ToArray();
        var records=new List<object>();
        try
        {
            if(clips.Length!=17)throw new Exception("Native candidate count mismatch.");
            if(save)EnsureFolder(Folder);
            // Validate every destination before writing any candidate.
            foreach(var t in targets)
            {
                string p=Folder+"/GS_"+t.role+"_SwordIdle.anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if(existing!=null&&existing.name!="GS_"+t.role+"_SwordIdle")throw new Exception("Destination is not owned: "+p);
            }
            for(int i=0;i<targets.Count;i++)
            {
                var t=targets[i];var source=t.step.animationClip;var candidate=clips[2+i*2];string path=Folder+"/GS_"+t.role+"_SwordIdle.anim";
                candidate.name="GS_"+t.role+"_SwordIdle";
                bool events=Newtonsoft.Json.JsonConvert.SerializeObject(AnimationUtility.GetAnimationEvents(source))==Newtonsoft.Json.JsonConvert.SerializeObject(AnimationUtility.GetAnimationEvents(candidate));
                if(!events||Mathf.Abs(candidate.length-source.length)>.000001f||candidate.frameRate!=source.frameRate)throw new Exception("Attack timing/event mismatch: "+t.role);
                string beforeGuid=AssetDatabase.AssetPathToGUID(path);var saved=candidate;
                if(save)
                {
                    var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if(existing==null){saved=UnityEngine.Object.Instantiate(candidate);AssetDatabase.CreateAsset(saved,path);}
                    else{saved=existing;EditorUtility.CopySerialized(candidate,saved);EditorUtility.SetDirty(saved);AssetDatabase.SaveAssetIfDirty(saved);}
                    if(beforeGuid!=""&&beforeGuid!=AssetDatabase.AssetPathToGUID(path))throw new Exception("Candidate GUID changed: "+t.role);
                }
                using(var a=new SerializedObject(source))using(var b=new SerializedObject(saved))
                {
                    if(a.FindProperty("m_UseHighQualityCurve").boolValue!=b.FindProperty("m_UseHighQualityCurve").boolValue)throw new Exception("Source curve quality mode changed: "+t.role);
                }
                if(Hash(AssetDatabase.GetAssetPath(source))!=sourceHashes[i])throw new Exception("Source changed during authoring: "+t.role);
                records.Add(new{t.role,path,guid=AssetDatabase.AssetPathToGUID(path),beforeGuid,length=saved.length,sourceHash=sourceHashes[i],sourcePreserved=true,eventsPreserved=events,frameRatePreserved=true,runtimeIk=0,algorithm="native forward Humanoid capture, coherent rotation path, authored Idle boundaries and body clearance"});
            }
            if(Hash(Definition)!=definitionHash||Hash(IdlePath)!=idleHash)throw new Exception("Definition/Idle changed during authoring.");
            File.WriteAllText(Path.Combine(output,"copy-manifest.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status="BUILT: stored native verification required",saved=save,records,definitionsChanged=false,guardExcluded=true,runtimeIkAdded=false,airborneMotionPreserved=true,bodyRootY="clearance authored for P09 and serialized visualHeightCurve; terrain requires separate live verification"},Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(output,"recommended-transitions.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{appliedToGameplay=false,blendSeconds=.12f,settledIdleStartNormalized=0f,rules=new[]{new{outgoing="Combo1",settledThreshold=.90f},new{outgoing="Combo2",settledThreshold=.825f},new{outgoing="Combo3",settledThreshold=.90f}},otherwise="preserve serialized continuation offsets",runtimeFootIk=0},Newtonsoft.Json.Formatting.Indented));
            if(save)AssetDatabase.ExportPackage(new[]{Folder},Path.Combine(output,"SwordIdleAdapted_Attacks.unitypackage"),ExportPackageOptions.Recurse);
            return "BUILT: eight independent candidates; originals, events and product bindings preserved. Stored native verification required.";
        }
        finally{foreach(var o in objects)if(o!=null)UnityEngine.Object.DestroyImmediate(o);}
    }
}
