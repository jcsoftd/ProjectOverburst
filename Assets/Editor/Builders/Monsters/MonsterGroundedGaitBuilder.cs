using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;

// The four approved slow giants use their grounded gait at the existing run speed.
// Supplier clips, actor scale/grounding, combat/parry, and common sound assets remain unchanged.
public static class MonsterGroundedGaitBuilder
{
    static readonly string[] Ids={"V3_Anglerox","V3_Hideoplast","V3_Deinodonte","V3_Perderos"};
    static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/Monsters/20261006_SevenThemeImprovement/GOAL_01/GaitApply"));
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static string Disk(string path)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));
    static readonly JArray backups=new JArray();
    static void Backup(Object asset)
    {
        string path=AssetDatabase.GetAssetPath(asset);if(backups.Any(x=>(string)x["path"]==path))return;
        string destination=Path.Combine(Output,"Before",path);Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(Disk(path),destination,false);File.Copy(Disk(path)+".meta",destination+".meta",false);
        backups.Add(new JObject{["path"]=path,["beforeSha256"]=Hash(Disk(path)),["guid"]=AssetDatabase.AssetPathToGUID(path),["metaSha256"]=Hash(Disk(path)+".meta")});
        File.WriteAllText(Path.Combine(Output,"backups.json"),backups.ToString());
    }
    public static object Apply()
    {
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
        try{return ApplyCore();}
        catch(Exception ex)
        {
            Undo.RevertAllDownToGroup(group);
            foreach(JObject b in backups){var asset=AssetDatabase.LoadMainAssetAtPath((string)b["path"]);if(asset!=null){EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);}}
            if(Directory.Exists(Output))File.WriteAllText(Path.Combine(Output,"failure.json"),new JObject{["error"]=ex.ToString(),["nativeUndoRestored"]=new JArray(backups.Select(b=>new JObject{["path"]=b["path"],["bytesMatchBefore"]=Hash(Disk((string)b["path"]))==(string)b["beforeSha256"]}))}.ToString());
            throw;
        }
    }
    static object ApplyCore()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer
            ||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.SevenThemeFollowupFlow.plan","")))throw new InvalidOperationException("Idle unreserved shared Editor required.");
        if(File.Exists(Path.Combine(Output,"applied.json"))||File.Exists(Path.Combine(Output,"backups.json")))throw new InvalidOperationException("Fresh owned output required; no blind repeat.");
        Directory.CreateDirectory(Output);backups.Clear();var records=new JArray();
        var catalog=AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");
        foreach(var id in Ids)
        {
            if(!catalog.TryGet(id,out var d)||!d.IsValid)throw new InvalidOperationException("Invalid saved actor "+id);
            var profile=d.AnimationProfile;var movement=d.MovementProfile;var controller=profile.RuntimeController as AnimatorController;
            if(controller==null)throw new InvalidOperationException("Native controller required "+id);
            var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Locomotion").state;
            var tree=state.motion as BlendTree;if(tree==null)throw new InvalidOperationException("Native locomotion tree required "+id);
            var children=tree.children;int index=Array.FindIndex(children,c=>c.threshold>0 && c.motion==profile.Run);
            if(index<0||children.Count(c=>c.threshold>0 && c.motion==profile.Run)!=1||profile.Run==profile.Walk)
                throw new InvalidOperationException("Distinct current run role required "+id);
            float speed=movement.MoveSpeed,runMultiplier=movement.RunSpeedMultiplier,walkReference=movement.AnimationReferenceSpeed,originalRunReference=movement.RunAnimationReferenceSpeed;
            AnimationClip original=profile.Run;string supplierPath=AssetDatabase.GetAssetPath(original),supplierHash=Hash(Disk(supplierPath));
            string actorPath=AssetDatabase.GetAssetPath(d.ActorPrefab),actorHash=Hash(Disk(actorPath));
            string profileBefore=EditorJsonUtility.ToJson(profile);
            Backup(controller);Backup(profile);Backup(movement);
            Undo.RecordObjects(new Object[]{controller,tree,profile,movement},"네 거대 몬스터 지상 보행 연결");
            var child=children[index];child.motion=profile.Walk;children[index]=child;tree.children=children;
            var serialized=new SerializedObject(profile);serialized.FindProperty("run").objectReferenceValue=profile.Walk;serialized.ApplyModifiedPropertiesWithoutUndo();
            movement.ConfigureAnimationReferenceSpeeds(walkReference,walkReference);
            EditorUtility.SetDirty(tree);EditorUtility.SetDirty(controller);EditorUtility.SetDirty(profile);EditorUtility.SetDirty(movement);
            AssetDatabase.SaveAssetIfDirty(controller);AssetDatabase.SaveAssetIfDirty(profile);AssetDatabase.SaveAssetIfDirty(movement);
            var feet=AssetDatabase.FindAssets("t:EnemyFootfallProfile").Select(g=>AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p=>p!=null && p.EnemyId==id && p.RunClip==original).ToArray();
            foreach(var foot in feet)
            {
                Backup(foot);Undo.RecordObject(foot,"지상 보행 발 접촉 연결");
                if(foot.HasDetailedContacts)
                {
                    var contacts=Enumerable.Range(0,foot.GetContactCount(false)).Select(i=>foot.GetContact(false,i)).ToArray();
                    foot.ConfigureDetailed(foot.EnemyId,foot.LocomotionClip,profile.Walk,foot.VisualWeight,contacts,contacts);
                }
                else{var so=new SerializedObject(foot);so.FindProperty("runClip").objectReferenceValue=profile.Walk;so.ApplyModifiedPropertiesWithoutUndo();}
                EditorUtility.SetDirty(foot);AssetDatabase.SaveAssetIfDirty(foot);
            }
            if(movement.MoveSpeed!=speed||movement.RunSpeedMultiplier!=runMultiplier||movement.AnimationReferenceSpeed!=walkReference
                ||movement.RunAnimationReferenceSpeed!=walkReference||profile.Run!=profile.Walk||children[index].motion!=profile.Run
                ||Hash(Disk(supplierPath))!=supplierHash||Hash(Disk(actorPath))!=actorHash||!d.IsValid)throw new InvalidOperationException("Gait preservation contract failed "+id);
            foreach(JObject b in backups){string path=(string)b["path"];if(AssetDatabase.AssetPathToGUID(path)!=(string)b["guid"]||Hash(Disk(path)+".meta")!=(string)b["metaSha256"])throw new InvalidOperationException("GUID/meta changed "+path);}
            records.Add(new JObject{["id"]=id,["originalRun"]=supplierPath,["chosenRun"]=AssetDatabase.GetAssetPath(profile.Run),["chosenClip"]=profile.Run.name,
                ["oldCycleSeconds"]=original.length/((speed*runMultiplier*d.ResolveRuntimeStats().MoveSpeedMultiplier)/originalRunReference),
                ["newCycleSeconds"]=profile.Run.length*walkReference/(speed*runMultiplier*d.ResolveRuntimeStats().MoveSpeedMultiplier),
                ["physicalMoveSpeed"]=speed,["physicalRunMultiplier"]=runMultiplier,["runReference"]=movement.RunAnimationReferenceSpeed,
                ["actorPrefabUnchanged"]=true,["supplierClipUnchanged"]=true,["footfallProfilesUpdated"]=feet.Length,["profileBefore"]=profileBefore});
            File.WriteAllText(Path.Combine(Output,"progress.json"),records.ToString());
        }
        foreach(JObject b in backups)b["afterSha256"]=Hash(Disk((string)b["path"]));
        var result=new JObject{["status"]="APPLIED_NATIVE",["actors"]=records,["assets"]=backups,["gameplaySpeedChanged"]=false,["soundWrites"]=0,["approvedAnimationWrites"]=0,["runtimeValidation"]="PENDING"};
        File.WriteAllText(Path.Combine(Output,"applied.json"),result.ToString());return new{status="APPLIED_NATIVE",actors=records.Count,assets=backups.Count};
    }
}
