using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Applies completed attack rows to existing combat actors without rebuilding their models or SFX.
// Inputs and recovery artifacts are supplied by the caller outside Assets.
public static class MonsterV3ExistingActorBatchBuilder
{
    public const int Revision = 6;
    const string Root = "Assets/ProjectOverburst/Resources/Enemies/Themes/";
    static string Project => Directory.GetParent(Application.dataPath).FullName;
    static string Workspace => Directory.GetParent(Project).FullName;
    static string Hash(string file)
    { using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant(); }
    static void Idle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Idle Editor with unoccupied account required.");
    }
    static string Output(string path)
    {
        path=Path.GetFullPath(path);
        string allowed=Path.GetFullPath(Path.Combine(Workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Private artifact directory required.");
        Directory.CreateDirectory(path);return path;
    }
    static AnimationClip Original(JObject row)
        => AssetDatabase.LoadAllAssetsAtPath((string)row["sourcePath"]).OfType<AnimationClip>()
            .Single(c=>AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c,out string g,out long id)
                && g==(string)row["sourceGuid"] && id==(long)row["sourceLocalId"]);
    static AnimationClip Runtime(JObject row)
        => (string)row["runtimeClipPath"]==(string)row["sourcePath"]?Original(row):AssetDatabase.LoadAssetAtPath<AnimationClip>((string)row["runtimeClipPath"]);
    static AnimatorState State(AnimatorController controller,string trigger)
    {return controller.layers[0].stateMachine.states.SingleOrDefault(s=>s.state.name=="Attack_"+trigger.Substring("Attack".Length)).state;}
    static EnemyAbilityDefinition BoundAbility(EnemyDefinition definition,AnimatorController controller,JObject row)
    {
        var clip=Runtime(row);
        for(int i=0;i<definition.AbilitySet.Count;i++)
        {
            var ability=definition.AbilitySet.GetAbility(i);
            if(ability!=null && State(controller,ability.AnimatorTrigger)?.motion==clip)return ability;
        }
        string path=Root+"Abilities/"+definition.EnemyId+"_"+(string)row["actualClip"]+".asset";
        return AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(path);
    }
    static void Set(UnityEngine.Object asset,string field,Action<SerializedProperty> write)
    {
        var so=new SerializedObject(asset);var p=so.FindProperty(field)??throw new InvalidOperationException("Missing property "+field);
        write(p);so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Save(UnityEngine.Object asset)
    {if(EditorUtility.IsDirty(asset))AssetDatabase.SaveAssetIfDirty(asset);}
    static void ModifyAbility(EnemyAbilityDefinition ability,JObject row,string trigger)
    {
        // A newly cloned ability can still reference its seed's old contact windows.
        // Replace the link only after the new timing has been authored and validated.
        ability.ConfigureWeakAttackExecution(null);
        float time=(float)row["hitNormalizedTimes"][0];
        Set(ability,"animatorTrigger",p=>p.stringValue=trigger);
        var mode=(string)row["visualType"]=="ranged"?EnemyAbilityExecutionMode.Projectile:EnemyAbilityExecutionMode.MeleeArc;
        Set(ability,"executionMode",p=>p.enumValueIndex=(int)mode);
        if(mode==EnemyAbilityExecutionMode.Projectile)
            Set(ability,"range",p=>p.floatValue=(float)row["stationaryStartRange"]);
        if(mode==EnemyAbilityExecutionMode.MeleeArc && (string)row["motionPolicy"]=="VisualJump")
            Set(ability,"minimumRange",p=>p.floatValue=0f);
        Set(ability,"hitNormalizedTime",p=>p.floatValue=time);
        Set(ability,"hitDelay",p=>p.floatValue=time*Runtime(row).length);
        Set(ability,"attackAnimationDuration",p=>p.floatValue=Runtime(row).length);
        // Preserve existing pacing, cooldown, weight, priority and total pattern damage.
        int perRelease=(int?)row["projectilesPerRelease"]??1;
        ability.ConfigureProjectileGrouping(perRelease);
        var additionalTimes=row["hitNormalizedTimes"].Where((v,i)=>i%perRelease==0).Skip(1).Select(v=>(float)v).ToArray();
        try { ability.ConfigureAdditionalHits(additionalTimes); }
        catch(ArgumentException error)
        { throw new ArgumentException("Invalid release sequence for "+row["actualClip"]+": first="+ability.HitNormalizedTime
            +", perRelease="+perRelease+", additional="+string.Join(",",additionalTimes),error); }
        if((string)row["rangedUsePolicy"]=="BlockedApproachFallback")
        {
            Set(ability,"cooldown",p=>p.floatValue=14f);
            Set(ability,"priority",p=>p.intValue=-10);
            Set(ability,"preparationDuration",p=>p.floatValue=.4f);
            Set(ability,"releaseDuration",p=>p.floatValue=Runtime(row).length*.12f);
            Set(ability,"minimumRecoveryTime",p=>p.floatValue=.35f);
            Set(ability,"referencePatternDamagePercent",p=>p.floatValue=(float)row["damageBudget"]["referencePatternDamagePercent"]);
        }
        EditorUtility.SetDirty(ability);
    }
    static AnimatorState AddAttack(AnimatorController ac,string trigger,AnimationClip clip)
    {
        var sm=ac.layers[0].stateMachine;
        var loco=sm.states.Single(s=>s.state.name=="Locomotion").state;
        var reference=sm.states.First(s=>s.state.name.StartsWith("Attack_",StringComparison.Ordinal)).state;
        if(!ac.parameters.Any(p=>p.name==trigger))ac.AddParameter(trigger,AnimatorControllerParameterType.Trigger);
        var state=sm.AddState("Attack_"+trigger.Substring("Attack".Length));state.motion=clip;
        state.writeDefaultValues=reference.writeDefaultValues;state.speedParameter="AttackAnimSpeed";state.speedParameterActive=true;
        var enter=sm.AddAnyStateTransition(state);enter.hasExitTime=false;enter.duration=.06f;enter.canTransitionToSelf=false;
        enter.AddCondition(AnimatorConditionMode.If,0,trigger);
        var exit=state.AddTransition(loco);exit.hasExitTime=true;exit.exitTime=.98f;exit.duration=.08f;
        EditorUtility.SetDirty(ac);return state;
    }

    public static string Apply(string batchPath,string outputDirectory)
    {
        Idle();outputDirectory=Output(outputDirectory);
        var batch=JObject.Parse(File.ReadAllText(batchPath));
        if((string)batch["schema"]!="overburst.v3.existing-actor-batch.v1" || (bool?)batch["applyAudio"]!=false)
            throw new ArgumentException("Unexpected existing actor batch.");
        string approvedPath=(string)batch["approvedPath"],approvedSha=(string)batch["approvedSha256"];
        if(Hash(approvedPath)!=approvedSha)throw new InvalidOperationException("V3 input changed.");
        var approved=JObject.Parse(File.ReadAllText(approvedPath));
        foreach(var input in approved["inputs"])
            if(Hash((string)input["path"])!=(string)input["sha256"])throw new InvalidOperationException("Selection input changed.");
        var rows=batch["entries"].OfType<JObject>().ToArray();
        var ids=batch["definitionIds"] is JArray definitionIds ? definitionIds.Values<string>().ToArray()
            : batch["models"].Values<string>().Select(n=>"CavernMutants_"+n).ToArray();
        var defs=ids.ToDictionary(id=>id,id=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"Definitions/"+id+".asset"));
        var originalFiles=new Dictionary<string,string>();var newAssets=new HashSet<string>();
        var abilityTargets=new Dictionary<string,string>();
        var assetPaths=new HashSet<string>();
        foreach(var property in ((JObject)batch["expectedAssets"]).Properties())
        {
            string file=Path.Combine(Project,property.Name);
            if(Hash(file)!=(string)property.Value)throw new InvalidOperationException("Owned asset changed since preparation: "+property.Name);
            string backup=Path.Combine(outputDirectory,"Before",property.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(file,backup,true);originalFiles[file]=backup;
            if(!property.Name.EndsWith(".meta",StringComparison.Ordinal))assetPaths.Add(property.Name);
        }
        foreach(string path in assetPaths)
        {
            foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if(asset!=null && EditorUtility.IsDirty(asset))throw new InvalidOperationException("Unsaved owned asset: "+path);
        }
        foreach(var pair in defs)
        {
            var d=pair.Value;var card=approved["cards"]?["runtime:"+pair.Key];
            if(d==null || !d.IsValid || card==null || (string)card["status"]!="confirmed" || (bool?)card["inRoster"]!=true)
                throw new InvalidOperationException("Existing confirmed actor required: "+pair.Key);
            if(d.ActorPrefab.GetComponentsInChildren<Transform>(true).Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))
                throw new InvalidOperationException("Missing script: "+pair.Key);
            var selected=card["weak"].Values<JObject>().Select(r=>(string)r["key"]).OrderBy(k=>k);
            if(!selected.SequenceEqual(rows.Where(r=>(string)r["cardKey"]=="runtime:"+pair.Key).Select(r=>(string)r["selectionKey"]).OrderBy(k=>k)))
                throw new InvalidOperationException("Incomplete selected weak attack batch: "+pair.Key);
        }
        foreach(var row in rows)
        {
            if((bool?)row["nativeAuthoringComplete"]!=true || Hash(Path.Combine(Project,(string)row["sourcePath"]))!=(string)row["sourceSha256"]
                || Original(row)==null || Runtime(row)==null)throw new InvalidOperationException("Completed native row required.");
            string id=((string)row["cardKey"]).Substring("runtime:".Length);
            var controller=defs[id].ActorPrefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController as AnimatorController;
            var bound=BoundAbility(defs[id],controller,row);
            string abilityPath=bound!=null?AssetDatabase.GetAssetPath(bound):Root+"Abilities/"+id+"_"+(string)row["actualClip"]+".asset";
            string profilePath=MonsterWeakAttackExecutionWriter.Root+"/"+(bound!=null?bound.AbilityId:id+"_"+(string)row["actualClip"])+".asset";
            abilityTargets[(string)row["selectionKey"]]=abilityPath;
            foreach(string path in new[]{abilityPath,profilePath})
                if(AssetDatabase.LoadMainAssetAtPath(path)==null)newAssets.Add(path);
                else if(!assetPaths.Contains(path))throw new InvalidOperationException("Existing target absent from ownership manifest: "+path);
        }
        var applied=new JArray();
        var nativeBefore=assetPaths.Where(p=>!p.EndsWith(".prefab",StringComparison.Ordinal))
            .SelectMany(p=>AssetDatabase.LoadAllAssetsAtPath(p)).Where(o=>o!=null)
            .Distinct().ToDictionary(o=>o,o=>EditorJsonUtility.ToJson(o));
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            foreach(var pair in defs)
            {
                Idle();string id=pair.Key;var def=pair.Value;
                var animator=def.ActorPrefab.GetComponentInChildren<Animator>(true);var ac=animator.runtimeAnimatorController as AnimatorController;
                if(ac==null)throw new InvalidOperationException("Native owned controller required.");
                var selectedRows=rows.Where(r=>(string)r["cardKey"]=="runtime:"+id).ToArray();
                var picked=new List<EnemyAbilityDefinition>();
                for(int i=0;i<def.AbilitySet.Count;i++)
                {
                    var ability=def.AbilitySet.GetAbility(i);var state=State(ac,ability.AnimatorTrigger);
                    bool weak=selectedRows.Any(r=>state.motion==Runtime(r));
                    bool strong=approved["cards"]["runtime:"+id]["strong"].Any(r=>(string)r["clip"]==state.motion.name);
                    if(weak || strong)picked.Add(ability);
                }
                foreach(var row in selectedRows)
                {
                    string path=abilityTargets[(string)row["selectionKey"]];
                    var ability=AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(path);
                    string trigger;
                    if(ability==null)
                    {
                        var seed=picked.FirstOrDefault(a=>!a.IsTelegraphedStrongAttack)
                            ?? Enumerable.Range(0,def.AbilitySet.Count).Select(def.AbilitySet.GetAbility).First(a=>!a.IsTelegraphedStrongAttack);
                        ability=UnityEngine.Object.Instantiate(seed);
                        var pending=ac.layers[0].stateMachine.states.FirstOrDefault(s=>s.state.name.StartsWith("Attack_",StringComparison.Ordinal)
                            && s.state.motion==Runtime(row)).state;
                        int next=ac.parameters.Where(p=>p.name.StartsWith("Attack",StringComparison.Ordinal))
                            .Select(p=>int.TryParse(p.name.Substring(6),out int n)?n:0).DefaultIfEmpty(0).Max()+1;
                        trigger=pending!=null?"Attack"+pending.name.Substring("Attack_".Length):"Attack"+next;
                        Set(ability,"abilityId",p=>p.stringValue=id+"_"+(string)row["actualClip"]);
                        ability.name=id+"_"+(string)row["actualClip"];
                        AssetDatabase.CreateAsset(ability,path);picked.Add(ability);
                        if(pending==null)AddAttack(ac,trigger,Runtime(row));
                    }
                    else{trigger=ability.AnimatorTrigger;if(!picked.Contains(ability))picked.Add(ability);}
                    ModifyAbility(ability,row,trigger);
                    MonsterWeakAttackExecutionWriter.Apply(approvedPath,approvedSha,(string)row["cardKey"],(string)row["selectionKey"],row,ability,Original(row),Runtime(row));
                    var state=State(ac,trigger);
                    if(state.motion!=Runtime(row)){state.motion=Runtime(row);EditorUtility.SetDirty(state);EditorUtility.SetDirty(ac);}
                    Save(ability);
                    applied.Add(new JObject{["cardKey"]=row["cardKey"].DeepClone(),["selectionKey"]=row["selectionKey"].DeepClone(),
                        ["abilityPath"]=path,["profilePath"]=AssetDatabase.GetAssetPath(ability.WeakAttackExecution),
                        ["trigger"]=trigger,["hits"]=ability.HitCount,["stationaryRange"]=ability.WeakAttackExecution.StationaryStartRange});
                }
                def.AbilitySet.Configure(id,picked.ToArray());EditorUtility.SetDirty(def.AbilitySet);Save(def.AbilitySet);
                Set(def.AnimationProfile,"attackClips",p=>{p.arraySize=picked.Count;for(int i=0;i<picked.Count;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=State(ac,picked[i].AnimatorTrigger).motion;});
                Save(def.AnimationProfile);Save(ac);
                string actorPath=AssetDatabase.GetAssetPath(def.ActorPrefab);var root=PrefabUtility.LoadPrefabContents(actorPath);
                try
                {
                    var melee=root.GetComponent<EnemyMeleeAttackController>();var movement=root.GetComponent<EnemyMovement>();
                    if(melee==null || movement==null)throw new InvalidOperationException("Actor movement/executor missing.");
                    var driver=root.GetComponent<EnemyWeakAttackMotionDriver>()??root.AddComponent<EnemyWeakAttackMotionDriver>();
                    driver.Configure(melee,movement);
                    var ranged=selectedRows.Where(r=>(string)r["visualType"]=="ranged").ToArray();
                    if(ranged.Length>0)
                    {
                        var special=root.GetComponent<EnemyThemeSpecialExecutor>();
                        if(special==null)
                        {
                            special=root.AddComponent<EnemyThemeSpecialExecutor>();
                            var executors=root.GetComponents<EnemyAbilityExecutor>();
                            Set(root.GetComponent<EnemyAbilityController>(),"executors",p=>{
                                p.arraySize=executors.Length;for(int i=0;i<executors.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=executors[i];});
                        }
                        var tuning=new SerializedObject(special);var muzzles=tuning.FindProperty("muzzleOverrides");
                        foreach(var row in ranged)
                        {
                            var ability=AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(abilityTargets[(string)row["selectionKey"]]);
                            string boneName=(string)row["muzzleBone"];
                            var socket=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==boneName);
                            int index=0;while(index<muzzles.arraySize&&muzzles.GetArrayElementAtIndex(index).FindPropertyRelative("ability").objectReferenceValue!=ability)index++;
                            if(index==muzzles.arraySize)muzzles.InsertArrayElementAtIndex(index);
                            var entry=muzzles.GetArrayElementAtIndex(index);
                            entry.FindPropertyRelative("ability").objectReferenceValue=ability;
                            entry.FindPropertyRelative("socket").objectReferenceValue=socket;
                            entry.FindPropertyRelative("localOffset").vector3Value=Vector3.zero;
                        }
                        tuning.ApplyModifiedPropertiesWithoutUndo();
                        if(ranged.Any(r=>(string)r["rangedUsePolicy"]=="BlockedApproachFallback"))
                        {
                            var fallback=root.GetComponent<EnemyBlockedApproachProjectileExecutor>()??root.AddComponent<EnemyBlockedApproachProjectileExecutor>();
                            fallback.Configure(special);
                            var executors=root.GetComponents<EnemyAbilityExecutor>().Where(e=>e!=special&&e!=fallback).Prepend(fallback).ToArray();
                            Set(root.GetComponent<EnemyAbilityController>(),"executors",p=>{
                                p.arraySize=executors.Length;for(int i=0;i<executors.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=executors[i];});
                        }
                    }
                    Set(melee,"attackTriggers",p=>{p.arraySize=picked.Count;for(int i=0;i<picked.Count;i++)p.GetArrayElementAtIndex(i).stringValue=picked[i].AnimatorTrigger;});
                    PrefabUtility.SaveAsPrefabAsset(root,actorPath,out bool success);
                    if(!success)throw new IOException("Actor save failed: "+actorPath);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            var result=new JObject{["status"]="APPLIED_PENDING_SAVED_PROFILE_PLAY",["actors"]=defs.Count,["profiles"]=applied.Count,["entries"]=applied,["audioApplied"]=false};
            File.WriteAllText(Path.Combine(outputDirectory,"apply-result.json"),result.ToString());return result.ToString();
        }
        catch(Exception error)
        {
            // Restore the serialized native objects as well as disk bytes. Dirty controller
            // subassets otherwise survive an import and can overwrite the restored file later.
            foreach(var item in nativeBefore)
                if(item.Key!=null){EditorJsonUtility.FromJsonOverwrite(item.Value,item.Key);EditorUtility.SetDirty(item.Key);}
            foreach(string path in assetPaths.Where(p=>p.EndsWith(".controller",StringComparison.Ordinal)))
                foreach(var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                    if(obj!=null && !nativeBefore.ContainsKey(obj))UnityEngine.Object.DestroyImmediate(obj,true);
            foreach(string path in assetPaths.Where(p=>!p.EndsWith(".prefab",StringComparison.Ordinal)))
                Save(AssetDatabase.LoadMainAssetAtPath(path));
            // Restore only this batch's exact original bytes and delete only assets created by this call.
            foreach(string path in newAssets)if(AssetDatabase.LoadMainAssetAtPath(path)!=null)AssetDatabase.DeleteAsset(path);
            foreach(var item in originalFiles)File.Copy(item.Value,item.Key,true);
            foreach(string path in assetPaths)AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            File.WriteAllText(Path.Combine(outputDirectory,"apply-result.json"),new JObject{["status"]="FAILED_OWNED_BACKUPS_RESTORED",["error"]=error.ToString()}.ToString());
            throw;
        }
        finally{AssetDatabase.AllowAutoRefresh();}
    }
}
