using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Saved game bindings and preservation checks; actual Play is delegated to the
// existing real-player verifier so the account and scene return has one owner.
public static class MonsterBlenderParryR6Verifier
{
    static readonly string[] States={"Parry_Collapse","Stunned_Loop","Stun_Recover"};
    static readonly string[] Roles={"ParryCollapse","StunnedLoop","StunRecover"};
    static string Output(string relative)
    {
        string workspace=Directory.GetParent(Application.dataPath).Parent.FullName;
        string path=Path.GetFullPath(Path.Combine(workspace,relative));
        string allowed=Path.GetFullPath(Path.Combine(workspace,"개인파일/코덱스산출/Monsters"))+Path.DirectorySeparatorChar;
        if(!path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Private monster evidence folder required.");
        return path;
    }
    static JObject State(AnimatorState s) => new JObject{["name"]=s.name,["motion"]=s.motion==null?null:AssetDatabase.GetAssetPath(s.motion),["speed"]=s.speed,["speedParameterActive"]=s.speedParameterActive,
        ["transitions"]=new JArray(s.transitions.Select(t=>new JObject{["destination"]=t.destinationState?.name,["exit"]=t.exitTime,["duration"]=t.duration,["fixed"]=t.hasFixedDuration,["hasExitTime"]=t.hasExitTime}))};
    public static object CheckBindings(string privateOutputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();string output=Output(privateOutputRelative);
        var plan=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/apply-input.json")));
        var receipt=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/apply-result.json")));
        if((string)receipt["status"]!="PASS_NATIVE_APPLIED"||(int)receipt["boundActors"]!=plan["records"].Count())throw new InvalidDataException("Complete application receipt required.");
        var before=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/native-before.json")));
        var rows=new JArray();var failures=new JArray();int preserved=0;
        foreach(var p in (JObject)plan["preservedHashes"])
        {
            bool changed=(JArray)plan["records"]!=null&&plan["records"].Any(r=>(string)r["controller"]["path"]==p.Key||(string)r["profile"]["path"]==p.Key);
            if(changed)continue;
            string project=Directory.GetParent(Application.dataPath).FullName;
            string file=Path.Combine(p.Key.StartsWith("Assets/",StringComparison.Ordinal)?project:Directory.GetParent(project).FullName,p.Key);
            if(MonsterBlenderParryR6Builder.Hash(file)!=(string)p.Value)failures.Add("Preserved input changed: "+p.Key);else preserved++;
        }
        foreach(var record in plan["records"])
        {
            string id=(string)record["id"];var snapshot=before["rows"].Single(r=>(string)r["id"]==id);
            var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)record["definition"]["path"]);
            var actor=AssetDatabase.LoadAssetAtPath<GameObject>((string)record["actor"]["path"]).GetComponent<EnemyActor>();
            var profile=definition.AnimationProfile;var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>((string)record["controller"]["path"]);
            var states=controller.layers[0].stateMachine.states.Select(s=>s.state).ToArray();var clips=new[]{profile.ParryCollapse,profile.StunnedLoop,profile.StunRecover};
            var reasons=new JArray();
            if(actor.Animator.runtimeAnimatorController!=controller||profile.RuntimeController!=controller)reasons.Add("Actor/profile controller mismatch");
            if(actor.Animator.applyRootMotion||(bool)snapshot["isHuman"]!=actor.Animator.isHuman||AssetDatabase.GetAssetPath(actor.Animator.avatar)!=(string)snapshot["avatar"])reasons.Add("Native root motion/Avatar changed");
            if(AssetDatabase.AssetPathToGUID((string)record["controller"]["path"])!=(string)record["controller"]["guid"]||AssetDatabase.AssetPathToGUID((string)record["profile"]["path"])!=(string)record["profile"]["guid"])reasons.Add("Existing GUID changed");
            foreach(var old in snapshot["states"].Where(s=>!States.Contains((string)s["name"])))
            {var state=states.SingleOrDefault(s=>s.name==(string)old["name"]);if(state==null||!JToken.DeepEquals(old,JObject.Parse(State(state).ToString())))reasons.Add("Non-parry state changed: "+old["name"]);}
            var trios=new JArray();
            for(int i=0;i<Roles.Length;i++)
            {
                var clip=clips[i];var state=states.SingleOrDefault(s=>s.name==States[i]);
                bool pass=clip!=null&&state!=null&&state.motion==clip&&AssetDatabase.GetAssetPath(clip)==(string)record["assetPaths"][Roles[i]]
                    &&clip.isLooping==(i==1)&&Math.Abs(clip.frameRate-30)<.0001&&Math.Abs(clip.length-(float)record["durations"][Roles[i]])<.0001
                    &&Math.Abs(state.speed-(i==0?EnemyAnimationBridge.ParryCollapseSpeed:1))<.0001&&!state.speedParameterActive
                    &&clip.isHumanMotion==actor.Animator.isHuman&&AnimationUtility.GetAnimationEvents(clip).Length==0;
                if(!pass)reasons.Add("Saved role mismatch: "+Roles[i]);
                trios.Add(new JObject{["role"]=Roles[i],["pass"]=pass,["path"]=clip==null?null:AssetDatabase.GetAssetPath(clip),["seconds"]=clip==null?0:clip.length});
            }
            int missing=actor.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if(missing!=0)reasons.Add("Actor missing script");if(!definition.IsValid)reasons.Add("Saved definition invalid");
            foreach(var reason in reasons)failures.Add(id+": "+reason);
            rows.Add(new JObject{["id"]=id,["pass"]=reasons.Count==0,["missingScripts"]=missing,["nativeAvatarPreserved"]=true,["nonParryStatesPreserved"]=reasons.Count==0,["trio"]=trios,["reasons"]=reasons});
        }
        var result=new JObject{["status"]=failures.Count==0?"PASS_SAVED_NATIVE_BINDINGS":"FAIL",["actors"]=rows.Count,["clips"]=rows.Count*3,["preservedFiles"]=preserved,["rows"]=rows,["failures"]=failures,["runtimePlay"]= "NOT_RUN_BY_THIS_CHECK",["utc"]=DateTime.UtcNow};
        File.WriteAllText(Path.Combine(output,"data/native-bindings.json"),result.ToString());
        return new{status=(string)result["status"],actors=rows.Count,clips=rows.Count*3,preservedFiles=preserved,failures=failures.ToString()};
    }
    public static string StartPlayerParryTest(string privateOutputRelative,string take)
    {
        MonsterBlenderParryR6Builder.RequireIdle();string output=Output(privateOutputRelative);
        var check=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/native-bindings.json")));
        if((string)check["status"]!="PASS_SAVED_NATIVE_BINDINGS")throw new InvalidDataException("Saved native bindings must pass before Play.");
        if(string.IsNullOrEmpty(take)||take.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'))throw new ArgumentException("Simple evidence take name required.");
        var plan=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/apply-input.json")));
        return MonsterWeakAttackPlayerLoopVerifier.StartRealPlayerParryBatch(Path.Combine(output,take),plan["records"].Select(r=>(string)r["definition"]["path"]).ToArray());
    }
}
