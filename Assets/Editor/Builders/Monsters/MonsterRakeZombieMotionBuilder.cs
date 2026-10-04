using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Connects the approved idle/movement variants. Rank and attack choices are independent.
public static class MonsterRakeZombieMotionBuilder
{
    const string DefinitionPath = "Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/DeathHarvest_RakeBrute.asset";
    static string Hash(string path)
    { using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
    static void Idle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating
            ||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            ||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))
            throw new InvalidOperationException("Idle unoccupied Editor required.");
    }
    public static string Apply(string planPath,string outputDirectory)
    {
        Idle();var plan=JObject.Parse(File.ReadAllText(planPath));
        if(Hash((string)plan["approvedPath"])!=(string)plan["approvedSha256"])throw new InvalidOperationException("Approved input changed.");
        string project=Directory.GetParent(Application.dataPath).FullName;
        string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(project).FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        outputDirectory=Path.GetFullPath(outputDirectory);
        if(!outputDirectory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Private output directory required.");
        Directory.CreateDirectory(outputDirectory);
        var paths=((JObject)plan["expectedAssets"]).Properties().Select(p=>p.Name).ToArray();
        foreach(var pair in ((JObject)plan["expectedAssets"]).Properties())
        {
            if(Hash(Path.Combine(project,pair.Name))!=(string)pair.Value)throw new InvalidOperationException("Owned asset changed: "+pair.Name);
            string backup=Path.Combine(outputDirectory,"Before",pair.Name);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(Path.Combine(project,pair.Name),backup,false);
        }
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath);
        if(definition?.IsValid!=true)throw new InvalidOperationException("Valid Rake definition required.");
        var controller=definition.AnimationProfile.RuntimeController as AnimatorController;
        if(controller==null||definition.ActorPrefab.GetComponentInChildren<Animator>(true)?.avatar?.isHuman!=true)
            throw new InvalidOperationException("Owned controller and Humanoid Avatar required.");
        var card=JObject.Parse(File.ReadAllText((string)plan["approvedPath"]))["cards"]["runtime:DeathHarvest_RakeBrute"];
        AnimationClip Clip(JToken row)
        {
            var clip=AssetDatabase.LoadAllAssetsAtPath((string)row["sourcePath"]).OfType<AnimationClip>().Single(c=>c.name==(string)row["clip"]);
            if(!clip.isHumanMotion||!AnimationUtility.GetAnimationClipSettings(clip).loopTime)throw new InvalidOperationException("Looping Humanoid source required: "+clip.name);
            return clip;
        }
        var idles=card["idle"].Select(Clip).ToArray();var moves=card["move"].Select(Clip).ToArray();
        if(idles.Length!=3||moves.Length!=2)throw new InvalidOperationException("Approved three idles and two moves required.");
        var sm=controller.layers[0].stateMachine;var loco=sm.states.Single(s=>s.state.name=="Locomotion").state;
        if(!(loco.motion is BlendTree locomotion))throw new InvalidOperationException("Native locomotion blend tree required.");
        string actorPath=AssetDatabase.GetAssetPath(definition.ActorPrefab);
        var native=paths.Where(p=>!p.EndsWith(".meta",StringComparison.Ordinal)&&!p.EndsWith(".prefab",StringComparison.Ordinal))
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath).Where(o=>o!=null).Distinct().ToDictionary(o=>o,o=>EditorJsonUtility.ToJson(o));
        if(native.Keys.Any(EditorUtility.IsDirty))throw new InvalidOperationException("Unsaved owned asset.");
        var newObjects=new List<UnityEngine.Object>();
        try
        {
            if(sm.states.Any(s=>s.state.name.StartsWith("IdleVariant_",StringComparison.Ordinal)))throw new InvalidOperationException("Variants already exist; inspect saved receipt before rerunning.");
            if(!controller.parameters.Any(p=>p.name=="MoveVariant"))controller.AddParameter("MoveVariant",AnimatorControllerParameterType.Float);
            var walkTree=new BlendTree{name="Approved zombie walk variants",blendType=BlendTreeType.Simple1D,blendParameter="MoveVariant",useAutomaticThresholds=false};
            AssetDatabase.AddObjectToAsset(walkTree,controller);newObjects.Add(walkTree);
            walkTree.children=moves.Select((clip,i)=>new ChildMotion{motion=clip,threshold=i,timeScale=1f}).ToArray();
            AnimationClip oldIdle=definition.AnimationProfile.Idle;
            void ReplaceMovement(BlendTree tree)
            {
                var children=tree.children;
                for(int i=0;i<children.Length;i++)
                {
                    if(children[i].motion is BlendTree nested)ReplaceMovement(nested);
                    else children[i].motion=children[i].motion==oldIdle?idles[0]:walkTree;
                }
                tree.children=children;EditorUtility.SetDirty(tree);
            }
            ReplaceMovement(locomotion);
            var names=new string[idles.Length];
            for(int i=0;i<idles.Length;i++)
            {
                names[i]="IdleVariant_"+i;
                var state=sm.AddState(names[i]);newObjects.Add(state);state.motion=idles[i];state.writeDefaultValues=loco.writeDefaultValues;
                state.speedParameter=loco.speedParameter;state.speedParameterActive=loco.speedParameterActive;
                var move=state.AddTransition(loco);newObjects.Add(move);move.hasExitTime=false;move.duration=.10f;
                move.AddCondition(AnimatorConditionMode.Greater,.08f,"Locomotion");
                var back=state.AddTransition(loco);newObjects.Add(back);back.hasExitTime=false;back.duration=.10f;
                back.AddCondition(AnimatorConditionMode.Less,-.08f,"Locomotion");
                EditorUtility.SetDirty(state);
            }
            var profile=new SerializedObject(definition.AnimationProfile);
            profile.FindProperty("idle").objectReferenceValue=idles[0];profile.FindProperty("walk").objectReferenceValue=moves[0];profile.FindProperty("run").objectReferenceValue=moves[0];
            var optional=profile.FindProperty("optional");
            foreach(var clip in idles.Skip(1).Concat(moves.Skip(1)))
                if(!Enumerable.Range(0,optional.arraySize).Any(i=>optional.GetArrayElementAtIndex(i).objectReferenceValue==clip))
                {int index=optional.arraySize;optional.InsertArrayElementAtIndex(index);optional.GetArrayElementAtIndex(index).objectReferenceValue=clip;}
            profile.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(sm);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);AssetDatabase.SaveAssetIfDirty(definition.AnimationProfile);
            var root=PrefabUtility.LoadPrefabContents(actorPath);
            try
            {
                var selector=root.GetComponent<EnemyLocomotionVariantSelector>()??root.AddComponent<EnemyLocomotionVariantSelector>();
                selector.Configure(root.GetComponentInChildren<Animator>(true),names,card["idle"].Select(r=>(float)r["weight"]).ToArray(),card["move"].Select(r=>(float)r["weight"]).ToArray());
                PrefabUtility.SaveAsPrefabAsset(root,actorPath,out bool saved);if(!saved)throw new IOException("Rake prefab save failed.");
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            var result=new JObject{["status"]="APPLIED_IDLE_MOVE_PENDING_PLAY",["idleStates"]=new JArray(names),["idleClips"]=new JArray(idles.Select(c=>c.name)),["moveClips"]=new JArray(moves.Select(c=>c.name)),["rankChanged"]=false,["attacksChanged"]=false,["audioChanged"]=false};
            File.WriteAllText(Path.Combine(outputDirectory,"apply-result.json"),result.ToString());return result.ToString();
        }
        catch(Exception error)
        {
            foreach(var pair in native)if(pair.Key!=null)EditorJsonUtility.FromJsonOverwrite(pair.Value,pair.Key);
            for(int i=newObjects.Count-1;i>=0;i--)if(newObjects[i]!=null)UnityEngine.Object.DestroyImmediate(newObjects[i],true);
            foreach(string path in paths){File.Copy(Path.Combine(outputDirectory,"Before",path),Path.Combine(project,path),true);if(!path.EndsWith(".meta",StringComparison.Ordinal))AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);}
            File.WriteAllText(Path.Combine(outputDirectory,"apply-result.json"),new JObject{["status"]="FAILED_OWNED_BACKUPS_RESTORED",["error"]=error.ToString()}.ToString());throw;
        }
    }
}
