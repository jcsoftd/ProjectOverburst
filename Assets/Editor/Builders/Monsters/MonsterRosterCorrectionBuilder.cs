using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Applies reviewed equipment variants and removes forbidden motions from the active roster.</summary>
public static class MonsterRosterCorrectionBuilder
{
    const string Root="Assets/ProjectOverburst/Resources/Enemies/Themes/";
    static string Project=>Directory.GetParent(Application.dataPath).FullName;
    static void Idle()=>MonsterBlenderParryR6Builder.RequireIdle();
    static void Save(UnityEngine.Object asset){EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);}
    static AnimationClip Clip(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
    static void Backup(string directory,UnityEngine.Object asset)
    {
        if(EditorUtility.IsDirty(asset))throw new InvalidOperationException("Unsaved owned asset: "+asset.name);
        string path=AssetDatabase.GetAssetPath(asset);
        foreach(string suffix in new[]{"",".meta"}){string target=Path.Combine(directory,"Before",path+suffix);Directory.CreateDirectory(Path.GetDirectoryName(target));if(!File.Exists(target))File.Copy(Path.Combine(Project,path+suffix),target,false);}
    }
    static void NonLoop(AnimationClip clip){var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);}
    static bool Forbidden(UnityEngine.Object clip)=>clip!=null&&AssetDatabase.GetAssetPath(clip).IndexOf("KillerDoll",StringComparison.OrdinalIgnoreCase)>=0;
    static void ReplaceTree(BlendTree tree,AnimationClip hit,AnimationClip walk)
    {
        var children=tree.children;
        for(int i=0;i<children.Length;i++){
            if(children[i].motion is BlendTree nested)ReplaceTree(nested,hit,walk);
            else if(Forbidden(children[i].motion)){children[i].motion=tree.blendParameter=="Locomotion"?walk:hit;if(tree.blendParameter=="Locomotion"&&children[i].threshold<0)children[i].timeScale=-1;}
        }
        tree.children=children;EditorUtility.SetDirty(tree);
    }
    public static string ReplaceRakeForbiddenMotions(string directory)
    {
        Idle();Directory.CreateDirectory(directory);
        var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"Definitions/DeathHarvest_RakeBrute.asset");
        var profile=d.AnimationProfile;var controller=(AnimatorController)profile.RuntimeController;
        var indices=Enumerable.Range(0,d.AbilitySet.Count).Where(i=>!d.AbilitySet.GetAbility(i).IsTelegraphedStrongAttack).ToArray();
        var weak=indices.Select(d.AbilitySet.GetAbility).ToArray();var attacks=indices.Select(profile.GetAttackClip).ToArray();
        var removed=Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).Where(a=>a.IsTelegraphedStrongAttack).ToArray();
        var removedIds=removed.Select(a=>a.AbilityId).ToArray();
        if(weak.Length!=3||attacks.Any(c=>c==null||Forbidden(c)))throw new InvalidOperationException("Expected three approved zombie weak attacks");
        foreach(var asset in new UnityEngine.Object[]{profile,controller,d.AbilitySet,d.ActorPrefab.gameObject}.Concat(removed))Backup(directory,asset);
        var reactions=new AnimationClip[2];string[] sources={"Zombie_HitReact_Head.fbx","Zombie_Idle_Death.fbx"};
        for(int i=0;i<sources.Length;i++){
            string name="DeathHarvest_RakeBrute_"+Path.GetFileNameWithoutExtension(sources[i]);
            string path=Root+"Animations/Derived/V3/"+name+".anim";
            reactions[i]=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(reactions[i]==null){reactions[i]=UnityEngine.Object.Instantiate(Clip("Assets/ThirdParty/03_애니메이션/Zombie_Animations_Set/Animations/"+sources[i]));reactions[i].name=name;NonLoop(reactions[i]);AssetDatabase.CreateAsset(reactions[i],path);Save(reactions[i]);}
        }
        var sm=controller.layers[0].stateMachine;
        foreach(var item in sm.states){var state=item.state;
            if(new[]{"Attack_1","Attack_2","Attack_3","Attack_4"}.Contains(state.name)){
                foreach(var t in sm.anyStateTransitions.Where(t=>t.destinationState==state).ToArray())sm.RemoveAnyStateTransition(t);
                foreach(var t in sm.entryTransitions.Where(t=>t.destinationState==state).ToArray())sm.RemoveEntryTransition(t);
                foreach(var other in sm.states)foreach(var t in other.state.transitions.Where(t=>t.destinationState==state).ToArray())other.state.RemoveTransition(t);
                sm.RemoveState(state);continue;
            }
            if(state.motion is BlendTree tree)ReplaceTree(tree,reactions[0],profile.Walk);
            else if(Forbidden(state.motion))state.motion=state.name=="Death"?reactions[1]:state.name=="Idle_break"?profile.Idle:reactions[0];
            EditorUtility.SetDirty(state);
        }
        for(int i=controller.parameters.Length-1;i>=0;i--)if(new[]{"Attack1","Attack2","Attack3","Attack4"}.Contains(controller.parameters[i].name))controller.RemoveParameter(i);
        EditorUtility.SetDirty(sm);Save(controller);
        d.AbilitySet.Configure(d.AbilitySet.AbilitySetId,weak);Save(d.AbilitySet);
        profile.Configure(d.EnemyId,controller,profile.Idle,profile.Walk,profile.Run,attacks,reactions[0],reactions[1],new[]{profile.Idle,profile.Walk},Array.Empty<string>());Save(profile);
        var contents=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(d.ActorPrefab));
        try{var so=new SerializedObject(contents.GetComponent<EnemyActor>().Melee);var triggers=so.FindProperty("attackTriggers");triggers.arraySize=weak.Length;for(int i=0;i<weak.Length;i++)triggers.GetArrayElementAtIndex(i).stringValue=weak[i].AnimatorTrigger;so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(contents,AssetDatabase.GetAssetPath(d.ActorPrefab),out bool saved);if(!saved)throw new Exception("Rake prefab save failed");}finally{PrefabUtility.UnloadPrefabContents(contents);}
        if(controller.animationClips.Any(Forbidden)||!d.IsValid||!profile.IsValid||profile.AttackClipCount!=3)throw new Exception("Weak-only bindings failed validation");
        var retired=new JArray();
        var candidates=removed.Select(AssetDatabase.GetAssetPath).Concat(new[]{Root+"Animations/Derived/V3/DeathHarvest_RakeBrute_ZombieStrong.anim"}).Where(p=>AssetDatabase.LoadMainAssetAtPath(p)!=null).Distinct().ToArray();
        var dependents=new[]{AssetDatabase.GetAssetPath(d),AssetDatabase.GetAssetPath(d.ActorPrefab),AssetDatabase.GetAssetPath(profile),AssetDatabase.GetAssetPath(controller),AssetDatabase.GetAssetPath(d.AbilitySet)};
        foreach(string path in candidates){
            if(!path.StartsWith(Root,StringComparison.Ordinal)||AssetDatabase.GetDependencies(dependents,true).Contains(path))throw new Exception("Retired strong still referenced: "+path);
            Backup(directory,AssetDatabase.LoadMainAssetAtPath(path));
            if(!AssetDatabase.MoveAssetToTrash(path))throw new Exception("Recycle Bin move failed: "+path);
            if(File.Exists(Path.Combine(Project,path))||File.Exists(Path.Combine(Project,path+".meta")))throw new Exception("Retired asset still exists: "+path);
            retired.Add(new JObject{["absolutePath"]=Path.Combine(Project,path),["metaPath"]=Path.Combine(Project,path+".meta"),["movedToTrash"]=true});
        }
        var receipt=new JObject{["status"]="PASS_NATIVE_WEAK_ONLY",["enemyId"]=d.EnemyId,["weakAttacks"]=3,["strongAttacks"]=0,["removedStrongIds"]=new JArray(removedIds),["retiredAssets"]=retired,["forbiddenControllerClips"]=0,["approvedParryMotionsChanged"]=false,["audioChanged"]=false};
        File.WriteAllText(Path.Combine(directory,"rake-apply-result.json"),receipt.ToString());return (string)receipt["status"];
    }

    public static string ActivateEquipmentVariants(string directory)
    {
        Idle();var built=JObject.Parse(File.ReadAllText(Path.Combine(directory,"actor-build-job.json")));
        if((string)built["status"]!="PASS"||built["completed"].Count()!=4)throw new Exception("All four equipment variants must be saved");
        var rows=JObject.Parse(File.ReadAllText(Path.Combine(directory,"content-selection.json")))["rows"].OfType<JObject>().ToArray();
        var catalog=AssetDatabase.LoadAssetAtPath<EnemyCatalog>(Root+"Catalog.asset");var table=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(Root+"Tables/GraveHunt.asset");
        if(catalog.Count!=63||table.Entries.Count!=10)throw new Exception("Expected existing eight-theme roster");
        var defs=rows.Select(r=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"Definitions/"+r["enemyId"]+".asset")).ToArray();
        if(defs.Any(d=>d?.IsValid!=true))throw new Exception("Invalid saved variant");
        foreach(var asset in new UnityEngine.Object[]{catalog,table,defs[0].AiPreset})Backup(directory,asset);
        catalog.ConfigureApproved(Enumerable.Range(0,catalog.Count).Select(catalog.GetDefinition).Concat(defs).ToArray());Save(catalog);
        var entries=table.Entries.Concat(rows.Select((r,i)=>new EnemyThemeTable.Entry{definition=defs[i],tier=(EnemyThemeTier)Enum.Parse(typeof(EnemyThemeTier),(string)r["tier"],true),weight=1})).ToArray();
        table.ConfigureApproved(table.ThemeId,table.DisplayName,catalog,table.Accent,entries);Save(table);
        var preset=defs[0].AiPreset;preset.ConfigureIdentity("Theme_GraveHunt",table.DisplayName,entries.Where(e=>e.tier!=EnemyThemeTier.Elite).Select(e=>e.definition.ActorPrefab.gameObject).ToArray());Save(preset);
        if(!table.Validate(out string error)||catalog.Count!=67)throw new Exception(error);
        File.WriteAllText(Path.Combine(directory,"activation-result.json"),new JObject{["status"]="PASS_NATIVE_ACTIVATED_PLAY_PENDING",["catalogCount"]=catalog.Count,["themeCount"]=8,["addedSmall"]=3,["addedMedium"]=1,["themeActors"]=entries.Length,["audioChanged"]=false}.ToString());return "ACTIVATED_4_EQUIPMENT_VARIANTS";
    }
}
