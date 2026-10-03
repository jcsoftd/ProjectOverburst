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
    public sealed class Target { public string role; public MeleeComboStepData step; public bool adaptEntry; }
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
    static float Smooth(float p){p=Mathf.Clamp01(p);return p*p*p*(10+p*(-15+6*p));}
    static bool Editable(EditorCurveBinding b) => b.type==typeof(Animator)&&b.path==""&&b.propertyName!="RootT.x"&&b.propertyName!="RootT.z";
    static void EnsureFolder(string path) { if(AssetDatabase.IsValidFolder(path))return;EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path)); }
    public static string Hash(string path){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    public static string Main(string output)=>Build(output,true);
    public static string Analyze(string output)=>Build(output,false);
    static string Build(string output,bool save)
    {
        if(save&&(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating))throw new InvalidOperationException("Save candidates only in idle EditMode.");
        Directory.CreateDirectory(output);
        var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
        var idleCurves=AnimationUtility.GetCurveBindings(idle).Where(b=>b.type==typeof(Animator)&&b.path=="").ToDictionary(b=>b.propertyName,b=>AnimationUtility.GetEditorCurve(idle,b));
        var records=new List<object>();
        if(save)EnsureFolder(Folder);
        foreach(var target in Targets())
        {
            var src=target.step.animationClip;string srcPath=AssetDatabase.GetAssetPath(src),path=Folder+"/GS_"+target.role+"_SwordIdle.anim";
            string sourceHash=Hash(srcPath);var originalBindings=AnimationUtility.GetCurveBindings(src);
            var sourceCurves=originalBindings.Where(b=>b.type==typeof(Animator)&&b.path=="").ToDictionary(b=>b.propertyName,b=>AnimationUtility.GetEditorCurve(src,b));
            var phases=(target.step.attackPhases??new AttackPhaseData[0]).Select(p=>new Vector2(p.SafeStart,p.SafeEnd)).Concat((target.step.trailPhases??new AttackTrailPhaseData[0]).Select(p=>new Vector2(p.SafeStart,p.SafeEnd))).ToArray();
            float first=phases.Min(p=>p.x)*src.length,last=phases.Max(p=>p.y)*src.length;
            float entryEnd=target.adaptEntry?Mathf.Min(first-.035f,Mathf.Min(.28f,src.length*.16f)):0;
            // Begin during the original recovery, before the feet complete their narrow-stance return.
            float recoveryStart=Mathf.Max(last+.045f,src.length*.56f);
            if(target.role=="DashHeavy")recoveryStart=Mathf.Max(recoveryStart,src.length*.70f);
            var copy=UnityEngine.Object.Instantiate(src);copy.name="GS_"+target.role+"_SwordIdle";
            int changed=0;float maxProtectedError=0;
            var editedBindings=new List<EditorCurveBinding>();var editedCurves=new List<AnimationCurve>();
            try
            {
                using(var quality=new SerializedObject(copy))
                {
                    var high=quality.FindProperty("m_UseHighQualityCurve");
                    if(high==null)throw new Exception("Editable Humanoid curve quality property missing.");
                    high.boolValue=true;quality.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach(var binding in originalBindings)
                {
                    string n=binding.propertyName;AnimationCurve reference;
                    if(!Editable(binding)||!idleCurves.TryGetValue(n,out reference))continue;
                    var old=sourceCurves[n];
                    var oldKeys=old.keys;
                    float begin=oldKeys.Where(k=>k.time>=recoveryStart).Select(k=>k.time).DefaultIfEmpty(src.length).Min();
                    float entry=entryEnd>0?oldKeys.Where(k=>k.time<=entryEnd).Select(k=>k.time).DefaultIfEmpty(0).Max():0;
                    if(begin>=src.length)begin=recoveryStart;
                    Func<float,float> value=t=>
                    {
                        float ew=entry>0?1-Smooth(t/entry):0;
                        float rw=Smooth((t-begin)/(src.length-begin));
                        if(ew==0&&rw==0)return old.Evaluate(t);
                        if(n.StartsWith("RootQ.")||n.StartsWith("LeftFootQ.")||n.StartsWith("RightFootQ."))
                        {
                            string prefix=n.Substring(0,n.LastIndexOf('.')+1);
                            Func<Dictionary<string,AnimationCurve>,float,Quaternion> q=(c,time)=>new Quaternion(c[prefix+"x"].Evaluate(time),c[prefix+"y"].Evaluate(time),c[prefix+"z"].Evaluate(time),c[prefix+"w"].Evaluate(time)).normalized;
                            var current=q(sourceCurves,t);var goal=q(idleCurves,0);
                            var a=goal*Quaternion.Inverse(q(sourceCurves,0));var z=goal*Quaternion.Inverse(q(sourceCurves,src.length));
                            var v=(Quaternion.Slerp(Quaternion.identity,z,rw)*Quaternion.Slerp(Quaternion.identity,a,ew)*current).normalized;
                            var raw=new Quaternion(sourceCurves[prefix+"x"].Evaluate(t),sourceCurves[prefix+"y"].Evaluate(t),sourceCurves[prefix+"z"].Evaluate(t),sourceCurves[prefix+"w"].Evaluate(t));
                            if(Quaternion.Dot(v,raw)<0)v=new Quaternion(-v.x,-v.y,-v.z,-v.w);
                            return n.EndsWith("x")?v.x:n.EndsWith("y")?v.y:n.EndsWith("z")?v.z:v.w;
                        }
                        return old.Evaluate(t)+(reference.Evaluate(0)-old.Evaluate(0))*ew+(reference.Evaluate(0)-old.Evaluate(src.length))*rw;
                    };
                    Func<float,float> slope=t=>{float a=Mathf.Max(0,t-.0001f),z=Mathf.Min(src.length,t+.0001f);return (value(z)-value(a))/(z-a);};
                    var times=new SortedSet<float>(oldKeys.Select(k=>k.time));times.Add(0);times.Add(src.length);times.Add(begin);
                    if(entry>0)times.Add(entry);
                    for(int i=1;i<Mathf.CeilToInt(src.length*120);i++){float t=i/120f;if(t<entry||t>begin)times.Add(t);}
                    var keys=new List<Keyframe>();
                    foreach(float t in times)
                    {
                        var keyIndex=Array.FindIndex(oldKeys,k=>Mathf.Abs(k.time-t)<.000001f);
                        bool untouched=(entry==0||t>=entry)&&t<=begin;
                        if(untouched&&keyIndex>=0){keys.Add(oldKeys[keyIndex]);continue;}
                        float tangent=slope(t);keys.Add(new Keyframe(t,value(t),tangent,tangent));
                    }
                    var curve=new AnimationCurve(keys.ToArray()){preWrapMode=old.preWrapMode,postWrapMode=old.postWrapMode};
                    editedBindings.Add(binding);editedCurves.Add(curve);changed++;
                    for(int i=0;i<=240;i++){float t=Mathf.Lerp(first,last,i/240f);maxProtectedError=Mathf.Max(maxProtectedError,Mathf.Abs(curve.Evaluate(t)-old.Evaluate(t)));}
                }
                // Rebuild Humanoid motion once, avoiding repeated per-curve native conversion.
                AnimationUtility.SetEditorCurves(copy,editedBindings.ToArray(),editedCurves.ToArray());
                if(maxProtectedError>.0001f)throw new Exception(target.role+" protected curve changed: "+maxProtectedError);
                if(save){var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if(existing==null){AssetDatabase.CreateAsset(copy,path);copy=null;}
                    else {if(!existing.name.StartsWith("GS_"+target.role+"_SwordIdle"))throw new Exception("Destination not owned: "+path);EditorUtility.CopySerialized(copy,existing);EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);}}
                var saved=save?AssetDatabase.LoadAssetAtPath<AnimationClip>(path):copy;
                if(Mathf.Abs(saved.length-src.length)>.000001f||Hash(srcPath)!=sourceHash)throw new Exception("Duration or original altered: "+target.role);
                records.Add(new {target.role,sourcePath=srcPath,sourceGuid=AssetDatabase.AssetPathToGUID(srcPath),sourceHash,path,guid=AssetDatabase.AssetPathToGUID(path),length=saved.length,entryEnd,recoveryStart,protectedStart=first,protectedEnd=last,changedCurves=changed,maxProtectedCurveError=maxProtectedError,sourcePreserved=true,entryPreserved=!target.adaptEntry});
            }
            finally{if(copy!=null)UnityEngine.Object.DestroyImmediate(copy);}
        }
        File.WriteAllText(Path.Combine(output,save?"copy-manifest.json":"curve-analysis.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status="PASS",saved=save,idlePath=IdlePath,records,definitionsChanged=false,guardExcluded=true,runtimeIkAdded=false,policy="120Hz recovery offsets; unchanged central strike curves, duration, events, root XZ and settings"},Newtonsoft.Json.Formatting.Indented));
        if(save)AssetDatabase.ExportPackage(new[]{Folder},Path.Combine(output,"SwordIdleAdapted_Attacks.unitypackage"),ExportPackageOptions.Recurse);
        return "PASS: "+records.Count+(save?" saved copies":" temporary curve candidates")+"; originals and gameplay definitions preserved.";
    }
}
