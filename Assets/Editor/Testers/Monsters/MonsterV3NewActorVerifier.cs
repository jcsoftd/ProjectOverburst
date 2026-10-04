using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
public static class MonsterV3NewActorVerifier
{
    static string Hash(string file){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant();}
    public static object Run(string batchPath,string receiptPath,string output)
    {
        // Reads persistent assets only. No preview objects, gameplay objects or asset writes.
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Stable asset database required.");
        var batch=JObject.Parse(File.ReadAllText(batchPath));var receipt=JObject.Parse(File.ReadAllText(receiptPath));
        string project=Directory.GetParent(Application.dataPath).FullName;
        foreach(var p in ((JObject)receipt["assetHashes"]).Properties())if(Hash(Path.Combine(project,p.Name))!=(string)p.Value)throw new InvalidOperationException("Created asset changed: "+p.Name);
        foreach(var p in ((JObject)batch["expectedSourceFiles"]).Properties())if(Hash(Path.Combine(project,p.Name))!=(string)p.Value)throw new InvalidOperationException("Source changed: "+p.Name);
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)receipt["definitionPath"]);var actor=definition.ActorPrefab;
        bool valid=definition.IsValid&&actor.IsAuthoringValid&&actor.Definition==definition&&actor.Identity.Definition==definition
            &&actor.Movement.Profile==definition.MovementProfile&&actor.Animator.runtimeAnimatorController==definition.AnimationProfile.RuntimeController;
        var missing=new JArray(actor.GetComponentsInChildren<Transform>(true).Where(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0).Select(t=>t.name));
        var attacks=new JArray();var rows=batch["entries"].OfType<JObject>().ToArray();
        for(int i=0;i<definition.AbilitySet.Count;i++)
        {
            var ability=definition.AbilitySet.GetAbility(i);var row=rows.Single(r=>ability.AbilityId==definition.EnemyId+"_"+r["actualClip"]);
            var clip=definition.AnimationProfile.GetAttackClip(i);
            bool strong=(string)row["role"]=="strong";bool pass=ability.IsValid&&ability.HitCount==(strong?((batch["strongHitNormalizedTimes"] as JArray)?.Count??1):(int)row["selectedHitCount"])&&ability.IsTelegraphedStrongAttack==strong
                &&clip!=null&&clip.name==(string)row["actualClip"]&&AssetDatabase.GetAssetPath(clip)==(string)row["sourcePath"];
            if(strong)pass&=ability.IsParryable&&ability.WeakAttackExecution==null;
            else pass&=ability.WeakAttackExecution!=null&&ability.WeakAttackExecution.SelectionKey==(string)row["selectionKey"]&&ability.WeakAttackExecution.ValidateAuthoring(out _);
            attacks.Add(new JObject{["ability"]=ability.AbilityId,["strong"]=strong,["selection"]=row["selectionKey"],["hits"]=ability.HitCount,["pass"]=pass});
        }
        var animation=definition.AnimationProfile;var parry=new[]{animation.ParryCollapse,animation.StunnedLoop,animation.StunRecover};
        var source=AssetDatabase.LoadAssetAtPath<GameObject>((string)batch["sourcePrefab"]);
        var sourceRoot=source.GetComponentInChildren<Animator>(true).transform;
        var rigPaths=new HashSet<string>(sourceRoot.GetComponentsInChildren<Transform>(true).Select(t=>AnimationUtility.CalculateTransformPath(t,sourceRoot)));
        var weightedPaths=new HashSet<string>();
        foreach(var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            using(var weights=skin.sharedMesh.GetAllBoneWeights())foreach(var weight in weights)
                if(weight.weight>0)weightedPaths.Add(AnimationUtility.CalculateTransformPath(skin.bones[weight.boneIndex],sourceRoot));
        }
        bool allOriginalTransformsPresent=rigPaths.All(p=>p==""||actor.Animator.transform.Find(p)!=null);
        bool allWeightedBonesPresent=weightedPaths.All(p=>p==""||actor.Animator.transform.Find(p)!=null);
        var bindingIssues=new JArray();var unusedExporterHelpers=new JArray();
        foreach(var clip in new[]{animation.Idle,animation.Walk,animation.Run,animation.Hit,animation.Death}.Concat(parry.Where(c=>c!=null)).Concat(rows.Select(r=>
            AssetDatabase.LoadAllAssetsAtPath((string)r["sourcePath"]).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__")))).Distinct())
        {
            if(clip==null){bindingIssues.Add("Missing required clip");continue;}
            string clipSource=AssetDatabase.GetAssetPath(clip);
            if(clip==animation.Idle)clipSource=(string)batch["idlePath"];
            else if(clip==animation.Walk)clipSource=(string)batch["movePath"];
            else if(clip==animation.Run)clipSource=(string)batch["runPath"]??(string)batch["movePath"];
            var exportModel=AssetDatabase.LoadAssetAtPath<GameObject>(clipSource);
            foreach(var p in AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Transform)&&b.path!=""&&actor.Animator.transform.Find(b.path)==null).Select(b=>b.path).Distinct())
            {
                string node=p.Substring(p.LastIndexOf('/')+1);
                var exportNode=exportModel!=null?exportModel.transform.Find(p):null;
                bool duplicateTerminal=Regex.IsMatch(node,@"_\d+ \d+$")&&exportNode!=null&&exportNode.childCount==0
                    &&rigPaths.Contains(p.Substring(0,p.LastIndexOf('/')+1)+Regex.Replace(node,@" \d+$",""));
                bool helperName=Regex.IsMatch(node,@"^(nub( \d+)?|.*_nub|Dummy\d+|IK Chain\d+)$")||duplicateTerminal;
                bool originalDescendant=rigPaths.Any(x=>x==p||x.StartsWith(p+"/",StringComparison.Ordinal));
                bool weightedDescendant=weightedPaths.Any(x=>x==p||x.StartsWith(p+"/",StringComparison.Ordinal));
                bool unusedHelper=helperName&&exportModel!=null&&exportModel.transform.Find(p)!=null&&!originalDescendant&&!weightedDescendant;
                if(unusedHelper)unusedExporterHelpers.Add(new JObject{["clip"]=clip.name,["path"]=p,["sourceExportTransformExists"]=true,
                    ["originalRigDescendant"]=false,["weightedBoneDescendant"]=false,["duplicateTerminalLeaf"]=duplicateTerminal});
                else bindingIssues.Add(clip.name+"/"+p);
            }
        }
        bool rig=actor.Animator.avatar==source.GetComponentInChildren<Animator>(true).avatar&&!actor.Animator.applyRootMotion
            &&actor.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s=>s.sharedMesh).SequenceEqual(source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s=>s.sharedMesh))
            &&actor.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>s.sharedMaterials).SequenceEqual(source.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>s.sharedMaterials).Select(m=>
                batch["materialOverrides"]?[AssetDatabase.GetAssetPath(m)] is JObject mapping?AssetDatabase.LoadAssetAtPath<Material>((string)mapping["targetPath"]):m));
        bool common=actor.GetComponent<BloodHitTarget>()?.Profile!=null&&actor.GetComponent<MonsterHitSfxTarget>()?.Bundle==null;
        bool locomotionLoops=animation.Idle.isLooping&&animation.Walk.isLooping&&animation.Run.isLooping;
        bool requiresParry=rows.Any(r=>(string)r["role"]=="strong");
        bool parryValid=requiresParry?parry.All(c=>c!=null)&&parry[1].isLooping&&!parry[0].isLooping&&!parry[2].isLooping
            :(string)batch["grade"]=="small"&&parry.All(c=>c==null);
        bool passAll=valid&&locomotionLoops&&missing.Count==0&&attacks.Count==rows.Length&&attacks.All(a=>(bool)a["pass"])&&bindingIssues.Count==0&&rig&&common&&allOriginalTransformsPresent&&allWeightedBonesPresent
            &&parryValid;
        var result=new JObject{["status"]=passAll?"PASS_SAVED_NEW_ACTOR_BINDINGS":"FAIL",["validCoreReferences"]=valid,["missingScripts"]=missing,
            ["locomotionLoops"]=locomotionLoops,["attacks"]=attacks,["missingAnimationBindings"]=bindingIssues,["unusedExporterHelpers"]=unusedExporterHelpers,
            ["allOriginalTransformsPresent"]=allOriginalTransformsPresent,["allWeightedBonesPresent"]=allWeightedBonesPresent,
            ["originalAvatarMeshesPreserved"]=rig,["commonHitConnectedNewBundleEmpty"]=common,
            ["actorRootScale"]=new JArray(actor.transform.localScale.x,actor.transform.localScale.y,actor.transform.localScale.z),
            ["requiresParry"]=requiresParry,["parryClips"]=new JArray(parry.Where(c=>c!=null).Select(AssetDatabase.GetAssetPath)),["gameplayVerified"]=false,["themeActivation"]=false};
        File.WriteAllText(output,result.ToString());if(!passAll)throw new InvalidOperationException("Saved actor bindings failed; inspect result.");return result;
    }
}
