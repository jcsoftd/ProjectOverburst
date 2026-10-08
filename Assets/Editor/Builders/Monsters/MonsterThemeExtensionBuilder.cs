using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

// Reuses the production V3 actor builder. Source poses remain the authority for contacts and sizing.
public static class MonsterThemeExtensionBuilder
{
    static Queue<string> pending;
    static string jobDirectory;
    static JArray completed;
    const string Root="Assets/ProjectOverburst/Resources/Enemies/Themes/";
    static string Project=>Directory.GetParent(Application.dataPath).FullName;
    static string Hash(string path){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static void Idle(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating
        ||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))throw new Exception("Idle unoccupied Editor required");}
    static AnimationClip Clip(string path,string name=null)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>name==null?!c.name.StartsWith("__preview__"):c.name==name);
    static void Save(UnityEngine.Object obj){EditorUtility.SetDirty(obj);AssetDatabase.SaveAssetIfDirty(obj);}
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
    static JArray V(Vector3 p)=>new JArray(p.x,p.y,p.z);
    static float PlayerApproachReach(Vector3 a,Vector3 b,float radius)
    {
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var body=player.GetComponentsInChildren<CapsuleCollider>(true).Single(c=>c.enabled&&!c.isTrigger);
        if(body.direction!=1||player.transform.lossyScale!=Vector3.one)throw new Exception("Saved player capsule basis changed");
        var center=player.transform.InverseTransformPoint(body.transform.TransformPoint(body.center));var scale=body.transform.lossyScale;
        float targetRadius=body.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
        float half=Mathf.Max(0,body.height*Mathf.Abs(scale.y)*.5f-targetRadius),reach=0;
        for(int i=0;i<=16;i++){var p=Vector3.Lerp(a,b,i/16f);float dy=Mathf.Max(center.y-half-p.y,0,p.y-center.y-half);
            float squared=(radius+targetRadius)*(radius+targetRadius)-(p.x-center.x)*(p.x-center.x)-dy*dy;
            if(squared>=0)reach=Mathf.Max(reach,p.z-center.z+Mathf.Sqrt(squared));}
        return reach;
    }
    public static string Prepare(string directory)
    {
        Idle();string receipt=Path.Combine(directory,"native-prepare-job.json");
        File.WriteAllText(receipt,new JObject{["status"]="RUNNING",["phase"]="read visual selections"}.ToString());
        var selection=JObject.Parse(File.ReadAllText(Path.Combine(directory,"content-selection.json")));
        var inventory=JObject.Parse(File.ReadAllText(Path.Combine(directory,"native-source-inventory.json")));
        var rows=selection["rows"].OfType<JObject>().ToArray();
        if(rows.Length!=7||rows.Any(r=>(string)r["timingStatus"]!="VISUAL_REVIEWED"))throw new Exception("Seven visually reviewed source selections required");
        string presetPath=Root+"Presets/GraveHunt.asset";
        var preset=AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(presetPath);
        if(preset==null){preset=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(Root+"Presets/DeathHarvest.asset"));preset.name="GraveHunt";preset.ConfigureIdentity("Theme_GraveHunt",(string)selection["themeName"],Array.Empty<GameObject>());AssetDatabase.CreateAsset(preset,presetPath);Save(preset);}
        var cards=new JObject();var batches=new JArray();
        var manifest=new JObject{["schema"]="overburst.theme-extension.native-review.v1",["inputs"]=new JArray(),["cards"]=cards};
        string manifestPath=Path.Combine(directory,"native-content-review.json");
        foreach(var row in rows)
        {
            File.WriteAllText(receipt,new JObject{["status"]="RUNNING",["phase"]="author contacts",["model"]=row["id"]}.ToString());
            string cid=(string)row["id"],id=(string)row["enemyId"],tier=(string)row["tier"],key=cid+":"+tier;
            var source=inventory["models"].OfType<JObject>().Single(m=>(string)m["id"]==cid);
            var all=inventory["clips"].OfType<JObject>().Where(c=>((string)c["path"]).IndexOf(cid.Replace("-",""),StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            Func<string,JObject> lookup=name=>all.Single(c=>(string)c["name"]==name);
            Func<string,string> path=name=>(string)lookup(name)["path"];
            var scene=EditorSceneManager.NewPreviewScene();GameObject model=null;PlayableGraph graph=default;
            try {
                model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>((string)source["path"]));SceneManager.MoveGameObjectToScene(model,scene);
                if(row["enabledRenderers"] is JArray enabled)foreach(var r in model.GetComponentsInChildren<Renderer>(true))r.enabled=enabled.Values<string>().Contains(r.name);
                var animator=model.GetComponentInChildren<Animator>(true);animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                graph=PlayableGraph.Create("Theme source authoring");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var output=AnimationPlayableOutput.Create(graph,"Pose",animator);
                Action<AnimationClip,float> pose=(clip,time)=>{var p=AnimationClipPlayable.Create(graph,clip);p.SetTime(time);output.SetSourcePlayable(p);graph.Play();graph.Evaluate(0);p.Destroy();};
                pose(Clip(path((string)row["idle"])),0);
                var bounds=UpcomingMonsterThemeReviewSizing.GeometryBounds(model);
                float scale=row["targetHeight"]!=null?(float)row["targetHeight"]/bounds.size.y:(float)row["targetPlanarSpan"]/Mathf.Max(bounds.size.x,bounds.size.z);
                if(row["minimumVisibleHeight"]!=null)scale=Mathf.Max(scale,(float)row["minimumVisibleHeight"]/bounds.size.y);
                model.transform.localScale=Vector3.one*scale;
                pose(Clip(path((string)row["idle"])),0);bounds=UpcomingMonsterThemeReviewSizing.GeometryBounds(model);
                float floor=.02f-bounds.min.y;model.transform.position=Vector3.up*floor;
                var entries=new JArray();var weak=new JArray();var strong=new JArray();
                foreach(var attack in row["weak"].OfType<JObject>().Concat(row["strong"] is JObject s?new[]{s}:Array.Empty<JObject>()))
                {
                    bool heavy=attack==row["strong"];string name=(string)attack["clip"];var metadata=lookup(name);var clip=Clip((string)metadata["path"],name);
                    var hitFrames=attack["hitFrames"].Values<int>().ToArray();var times=new JArray(hitFrames.Select(f=>(double)f/clip.frameRate/clip.length));
                    string selectionKey="GraveHunt/"+id+"/"+name;var phases=new JArray();var windows=new JArray();float reach=0;
                    string suffix=(string)attack["contactBoneSuffix"];var transforms=animator.GetComponentsInChildren<Transform>(true);
                    var bone=transforms.SingleOrDefault(t=>string.Equals(t.name,suffix,StringComparison.OrdinalIgnoreCase))
                        ??transforms.Where(t=>!t.name.StartsWith("ik_",StringComparison.OrdinalIgnoreCase)).Single(t=>t.name.EndsWith(suffix,StringComparison.OrdinalIgnoreCase));
                    foreach(int hit in hitFrames){int lo=hit-2,hi=hit+2;var frames=new JArray();windows.Add(new JArray(lo/clip.frameRate/clip.length,hi/clip.frameRate/clip.length));
                        for(int f=lo;f<=hi;f++){pose(clip,f/clip.frameRate);Vector3 b=bone.position;
                            if(attack["contactRenderer"]!=null)b=model.GetComponentsInChildren<Renderer>(true).Single(r=>r.name==(string)attack["contactRenderer"]).bounds.center;
                            float radius=(float?)attack["contactRadius"]??(tier=="small"?.19f:tier=="medium"?.25f:.3f);
                            Vector3 a=b-Vector3.forward*radius*.5f;b+=Vector3.forward*radius;
                            if(attack["pairedContactBoneSuffix"]!=null){a=bone.position;b=transforms.Single(t=>t.name==(string)attack["pairedContactBoneSuffix"]).position;}
                            if(attack["contactRenderer"]!=null){var renderer=model.GetComponentsInChildren<Renderer>(true).Single(r=>r.name==(string)attack["contactRenderer"]);
                                var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;var local=mesh.bounds;var extent=local.extents;int axis=extent.x>extent.y?(extent.x>extent.z?0:2):(extent.y>extent.z?1:2);
                                var direction=axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward;var worldScale=renderer.transform.lossyScale;
                                float factor=Mathf.Abs(worldScale[axis]);radius=Mathf.Max(Mathf.Abs(worldScale[(axis+1)%3])*extent[(axis+1)%3],Mathf.Abs(worldScale[(axis+2)%3])*extent[(axis+2)%3]);
                                float half=Mathf.Max(0,extent[axis]-radius/factor);a=renderer.transform.TransformPoint(local.center-direction*half);b=renderer.transform.TransformPoint(local.center+direction*half);}
                            reach=Mathf.Max(reach,PlayerApproachReach(a,b,radius));
                            frames.Add(new JObject{["normalizedTime"]=f/clip.frameRate/clip.length,["capsules"]=new JArray(new JObject{["a"]=V(a),["b"]=V(b),["radius"]=radius})});}
                        phases.Add(new JObject{["frames"]=frames});}
                    var entry=new JObject{["role"]=heavy?"strong":"weak",["actualClip"]=name,["sourcePath"]=metadata["path"],["sourceGuid"]=metadata["guid"],["sourceLocalId"]=metadata["localId"],
                        ["selectionKey"]=selectionKey,["cardKey"]=key,["reviewStatus"]="NATIVE_CONTENT_REVIEW",["sourceSha256"]=Hash(Path.Combine(Project,(string)metadata["path"])),
                        ["runtimeClipPath"]=metadata["path"],["nativeFps"]=clip.frameRate,["nativeAuthoringComplete"]=true,["hitNormalizedTimes"]=times,
                        ["motionPolicy"]=(string)attack["policy"]??"Stationary",["sourceTrimSeconds"]=new JArray(0,clip.length),["stationaryStartRange"]=Mathf.Max(.1f,reach-.012f),
                        ["maxAdvanceDistance"]=(string)attack["policy"]=="ShortAdvance"?.3f:0,["advanceWindow"]=new JArray(.08,.40),
                        ["advanceCurve"]=new JArray(new JObject{["time"]=0,["value"]=0,["inTangent"]=1,["outTangent"]=1},new JObject{["time"]=1,["value"]=1,["inTangent"]=1,["outTangent"]=1}),
                        ["poseRootBonePath"]=AnimationUtility.CalculateTransformPath(animator.transform.GetChild(0),animator.transform),["contactWindowsNormalized"]=windows,
                        ["contactGeometry"]=new JObject{["coordinateBasis"]="FinalEffectiveGameGeometry",["phases"]=phases}};
                    if(heavy){entry["preparationNormalized"]=(int)attack["preparationFrame"]/clip.frameRate/clip.length;
                        entry["cueNormalized"]=(int)attack["cueFrame"]/clip.frameRate/clip.length;entry["contactBonePath"]=AnimationUtility.CalculateTransformPath(bone,animator.transform);
                        pose(clip,(int)attack["cueFrame"]/clip.frameRate);entry["cuePositionActorMeters"]=V(bone.position);}
                    entries.Add(entry);(heavy?strong:weak).Add(new JObject{["key"]=selectionKey,["clip"]=name,["sourcePath"]=metadata["path"],["selectedCount"]=hitFrames.Length,["motionPolicy"]=entry["motionPolicy"]});
                }
                cards[key]=new JObject{["name"]=row["name"],["status"]="native-reviewed",["inRoster"]=true,["isBoss"]=false,["weak"]=weak,["strong"]=strong};
                var materials=new JObject();foreach(var mat in model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct())
                    materials[AssetDatabase.GetAssetPath(mat)]=new JObject{["targetPath"]="Assets/ProjectOverburst/05_Art/Materials/Monsters/GraveHunt/"+mat.name+".mat",["convertFromSource"]=true};
                var seed=tier=="small"?"CavernMutants_Cephalonops":tier=="medium"?"SpiderBrood_Cavecrawler":"CavernMutants_Ursacetus";
                var batch=new JObject{["schema"]="overburst.theme-extension.actor.v1",["activateInGame"]=false,["applyAudio"]=false,["approvedPath"]=manifestPath,["cardKey"]=key,["enemyId"]=id,["grade"]=tier,
                    ["seedDefinition"]=Root+"Definitions/"+seed+".asset",["sourcePrefab"]=source["path"],["aiPresetPath"]=presetPath,["idlePath"]=path((string)row["idle"]),["movePath"]=path((string)row["move"]),["runPath"]=path((string)row["run"]),
                    ["extras"]=new JObject{["CrawlBackwards"]=path((string)row["move"]),["CrawlForward_RM"]=path((string)row["move"]+"_RM"),["Run_RM"]=path((string)row["run"]+"_RM"),["CrawlBackwards_RM"]=path((string)row["move"]+"_RM"),["GetHit1"]=path((string)row["hit"]),["Death"]=path((string)row["death"])},
                    ["reverseBackwardAnimation"]=true,["entries"]=entries,["nativeReactionFallback"]=true,["materialOverrides"]=materials,["modelScale"]=V(Vector3.one*scale),["modelYOffset"]=floor,["size"]=V(bounds.size),
                    ["bodyRadius"]=(float?)row["bodyRadius"]??Mathf.Clamp(Mathf.Min(bounds.size.x,bounds.size.z)*.3f,.25f,1f),["bodyHeight"]=Mathf.Max(.65f,bounds.size.y),["hurtRadius"]=Mathf.Clamp(Mathf.Min(bounds.size.x,bounds.size.z)*.36f,.3f,1.2f),["hurtHeight"]=Mathf.Max(.65f,bounds.size.y),["enabledRenderers"]=row["enabledRenderers"]};
                if(strong.Count>0){var e=entries.OfType<JObject>().Single(e=>(string)e["role"]=="strong");batch["strongHitNormalizedTimes"]=e["hitNormalizedTimes"];batch["strongRange"]=e["stationaryStartRange"];}
                var files=new JObject();foreach(var p in entries.Select(e=>(string)e["sourcePath"]).Concat(new[]{(string)source["path"],path((string)row["idle"]),path((string)row["move"]),path((string)row["run"])}).Distinct())foreach(string suffix in new[]{"",".meta"})files[p+suffix]=Hash(Path.Combine(Project,p+suffix));
                batch["expectedSourceFiles"]=files;
                string failurePath=Path.Combine(directory,"Actors",id,"failure.json");
                if(File.Exists(failurePath)){
                    var interrupted=JObject.Parse(File.ReadAllText(failurePath));var resume=new JObject();
                    foreach(string asset in interrupted["created"].Values<string>())foreach(string suffix in new[]{"",".meta"})resume[asset+suffix]=Hash(Path.Combine(Project,asset+suffix));
                    batch["resumeAssetHashes"]=resume;
                }
                batches.Add(batch);
            }catch(Exception error){File.WriteAllText(receipt,new JObject{["status"]="FAIL",["model"]=row["id"],["error"]=error.ToString()}.ToString());throw;}
            finally{if(graph.IsValid())graph.Destroy();if(model!=null)UnityEngine.Object.DestroyImmediate(model);EditorSceneManager.ClosePreviewScene(scene);}
        }
        File.WriteAllText(manifestPath,manifest.ToString());foreach(var batch in batches.OfType<JObject>()){batch["approvedSha256"]=Hash(manifestPath);File.WriteAllText(Path.Combine(directory,(string)batch["enemyId"]+"-batch.json"),batch.ToString());}
        File.WriteAllText(receipt,new JObject{["status"]="PASS",["prepared"]=batches.Count}.ToString());
        return "PREPARED_NATIVE_REVIEW_BATCHES_7";
    }
    public static string QueueActors(string directory)
    {
        Idle();if(pending!=null)throw new Exception("Owned actor batch already running");
        var paths=Directory.GetFiles(directory,"GraveHunt_*-batch.json");if(paths.Length!=7)throw new Exception("Seven prepared batches required");
        pending=new Queue<string>(paths.OrderBy(x=>x));jobDirectory=directory;completed=new JArray();WriteJob("QUEUED");EditorApplication.update+=Tick;
        return "QUEUED_7_PRODUCTION_CORE_ACTORS";
    }
    static void WriteJob(string status,string error=null)=>File.WriteAllText(Path.Combine(jobDirectory,"actor-build-job.json"),new JObject{
        ["status"]=status,["remaining"]=pending?.Count??0,["completed"]=completed,["error"]=error}.ToString());
    static void Tick()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))return;
        try{
            if(pending.Count==0){WriteJob("PASS");EditorApplication.update-=Tick;pending=null;return;}
            string path=pending.Dequeue(),id=(string)JObject.Parse(File.ReadAllText(path))["enemyId"];WriteJob("RUNNING");
            string output=Path.Combine(jobDirectory,"Actors",id);Directory.CreateDirectory(output);
            MonsterV3NewActorBuilder.Apply(path,output);completed.Add(id);WriteJob("RUNNING");
        }catch(Exception error){WriteJob("FAIL",error.ToString());EditorApplication.update-=Tick;pending=null;Debug.LogException(error);}
    }

    public static string Activate(string directory)
    {
        Idle();
        var built=JObject.Parse(File.ReadAllText(Path.Combine(directory,"actor-build-job.json")));
        if((string)built["status"]!="PASS" || built["completed"].Count()!=7)throw new Exception("Seven saved actors required");
        var catalog=AssetDatabase.LoadAssetAtPath<EnemyCatalog>(Root+"Catalog.asset");
        var oldTable=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(Root+"Tables/DeathHarvest.asset");
        var additions=JObject.Parse(File.ReadAllText(Path.Combine(directory,"content-selection.json")))["rows"].OfType<JObject>()
            .Select(r=>new EnemyThemeTable.Entry{definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"Definitions/"+(string)r["enemyId"]+".asset"),
                tier=(EnemyThemeTier)Enum.Parse(typeof(EnemyThemeTier),(string)r["tier"],true),weight=1f}).ToArray();
        var movedIds=new HashSet<string>{"V3_Ghoul","DeathHarvest_RakeBrute","DeathHarvest_Reaper"};
        var moved=oldTable.Entries.Where(e=>movedIds.Contains(e.definition.EnemyId)).ToArray();
        string tablePath=Root+"Tables/GraveHunt.asset",materialPath=Root+"Materials/GraveHunt.mat";
        if(catalog.Count!=56 || moved.Length!=3 || additions.Any(e=>e.definition?.IsValid!=true)
            || AssetDatabase.LoadMainAssetAtPath(tablePath)!=null)throw new Exception("Unchanged 56-actor baseline and fresh GraveHunt table required");
        var oldEntries=oldTable.Entries.Where(e=>!movedIds.Contains(e.definition.EnemyId)).ToArray();
        var entries=additions.Concat(moved).ToArray();
        var oldPreset=AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(Root+"Presets/DeathHarvest.asset");
        var preset=AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(Root+"Presets/GraveHunt.asset");
        string name=(string)JObject.Parse(File.ReadAllText(Path.Combine(directory,"content-selection.json")))["themeName"];
        var touched=new UnityEngine.Object[]{catalog,oldTable,oldPreset,preset}.Concat(entries.Select(e=>(UnityEngine.Object)e.definition)).Distinct().ToArray();
        foreach(var obj in touched){if(EditorUtility.IsDirty(obj))throw new Exception("Unsaved owned asset: "+obj.name);
            string p=AssetDatabase.GetAssetPath(obj);foreach(string suffix in new[]{"",".meta"}){
                string backup=Path.Combine(directory,"Activation/Before",p+suffix);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(Path.Combine(Project,p+suffix),backup,false);}}
        var before=new JObject();foreach(var obj in touched)before[AssetDatabase.GetAssetPath(obj)]=JObject.Parse(EditorJsonUtility.ToJson(obj));
        Directory.CreateDirectory(Path.Combine(directory,"Activation"));File.WriteAllText(Path.Combine(directory,"Activation/native-before.json"),before.ToString());
        catalog.ConfigureApproved(Enumerable.Range(0,catalog.Count).Select(catalog.GetDefinition).Concat(additions.Select(e=>e.definition)).ToArray());Save(catalog);
        oldPreset.ConfigureIdentity("Theme_DeathHarvest",oldTable.DisplayName,oldEntries.Where(e=>e.tier!=EnemyThemeTier.Elite).Select(e=>e.definition.ActorPrefab.gameObject).ToArray());Save(oldPreset);
        preset.ConfigureIdentity("Theme_GraveHunt",name,entries.Where(e=>e.tier!=EnemyThemeTier.Elite).Select(e=>e.definition.ActorPrefab.gameObject).ToArray());Save(preset);
        foreach(var e in entries){var so=new SerializedObject(e.definition);so.FindProperty("aiPreset").objectReferenceValue=preset;
            so.FindProperty("squadParticipationMode").enumValueIndex=(int)(e.tier==EnemyThemeTier.Elite?EnemySquadParticipationMode.Independent:EnemySquadParticipationMode.SquadMember);
            so.ApplyModifiedPropertiesWithoutUndo();Save(e.definition);}
        oldTable.ConfigureApproved(oldTable.ThemeId,oldTable.DisplayName,catalog,oldTable.Accent,oldEntries);Save(oldTable);
        var table=ScriptableObject.CreateInstance<EnemyThemeTable>();table.name="GraveHunt";
        table.ConfigureApproved("GraveHunt",name,catalog,new Color(.64f,.46f,.28f,1),entries);AssetDatabase.CreateAsset(table,tablePath);Save(table);
        if(AssetDatabase.LoadMainAssetAtPath(materialPath)==null){var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.SetColor("_BaseColor",table.Accent);AssetDatabase.CreateAsset(mat,materialPath);Save(mat);}
        var tables=AssetDatabase.FindAssets("t:EnemyThemeTable",new[]{Root+"Tables"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<EnemyThemeTable>).ToArray();
        foreach(var t in tables)if(!t.Validate(out string reason))throw new Exception(t.name+": "+reason);
        if(tables.Length!=8 || tables.Sum(t=>t.Entries.Count)!=63 || tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct().Count()!=63)throw new Exception("Eight themes / 63 unique actors required");
        var result=new JObject{["status"]="PASS_NATIVE_ACTIVATION",["catalogCount"]=catalog.Count,["themes"]=new JArray(tables.OrderBy(t=>t.ThemeId).Select(t=>new JObject{
            ["id"]=t.ThemeId,["name"]=t.DisplayName,["small"]=t.Entries.Count(e=>e.tier==EnemyThemeTier.Small),["medium"]=t.Entries.Count(e=>e.tier==EnemyThemeTier.Medium),["elite"]=t.Entries.Count(e=>e.tier==EnemyThemeTier.Elite),
            ["entries"]=new JArray(t.Entries.Select(e=>new JObject{["id"]=e.definition.EnemyId,["name"]=e.definition.DisplayName,["tier"]=e.tier.ToString().ToLowerInvariant(),["path"]=AssetDatabase.GetAssetPath(e.definition)}))})),
            ["newAudioApplied"]=false,["approvedNewParryMotions"]=0};
        File.WriteAllText(Path.Combine(directory,"Activation/apply-result.json"),result.ToString());return "PASS_NATIVE_ACTIVATION_8_THEMES_63_ACTORS";
    }

    public static string ReplaceRakeMotions(string directory)
    {
        Idle();directory=Path.Combine(directory,"RakeReplacement");Directory.CreateDirectory(directory);
        var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"Definitions/DeathHarvest_RakeBrute.asset");
        var weak=Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).Where(a=>!a.IsTelegraphedStrongAttack).ToArray();
        if(weak.Length!=3 || d.ActorPrefab.GetComponentInChildren<Animator>(true).avatar?.isHuman!=true)throw new Exception("Three existing Humanoid Rake attacks required");
        string sourceRoot="Assets/ThirdParty/03_애니메이션/Zombie_Pro_Mocap/Animation/In_Place/";
        var originals=Enumerable.Range(1,3).Select(i=>Clip(sourceRoot+"Zombie_HyperAttack_"+i+"_SHORT_Loop_IPC.fbx")).ToArray();
        var idles=new[]{1,3}.Select(i=>Clip(sourceRoot+"Zombie_HyperAttack_"+i+"_SHORT_Idle_Loop_IPC.fbx")).ToArray();
        var walk=Clip(sourceRoot+"Zombie_Walk_F_2_Loop_IPC.fbx");
        var controller=(AnimatorController)d.AnimationProfile.RuntimeController;
        var assets=new UnityEngine.Object[]{d.AnimationProfile,controller,d.MovementProfile,d.ActorPrefab}.Concat(weak).Concat(weak.Select(a=>(UnityEngine.Object)a.WeakAttackExecution)).ToArray();
        foreach(var obj in assets){string path=AssetDatabase.GetAssetPath(obj);if(EditorUtility.IsDirty(obj))throw new Exception("Unsaved owned asset "+path);
            foreach(string suffix in new[]{"",".meta"}){string backup=Path.Combine(directory,"Before",path+suffix);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(Path.Combine(Project,path+suffix),backup,false);}}
        var oldClips=weak.Select(a=>a.WeakAttackExecution.RuntimeClip).ToArray();
        var scene=EditorSceneManager.NewPreviewScene();var actor=UnityEngine.Object.Instantiate(d.ActorPrefab.gameObject);SceneManager.MoveGameObjectToScene(actor,scene);
        var animator=actor.GetComponentInChildren<Animator>(true);animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
        var graph=PlayableGraph.Create("Rake native replacement contacts");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var output=AnimationPlayableOutput.Create(graph,"Pose",animator);
        var times=new[]{new[]{.60f,1.55f},new[]{.833333f,2.1f,2.9f},new[]{.333333f,.966667f}};
        var receipts=new JArray();
        try{
            for(int i=0;i<3;i++){
                var source=originals[i];string derivedPath=Root+"Animations/Derived/V3/DeathHarvest_RakeBrute_Hyper"+(i+1)+".anim";
                if(AssetDatabase.LoadMainAssetAtPath(derivedPath)!=null)throw new Exception("Derived clip already exists; inspect receipt before rerunning");
                var clip=UnityEngine.Object.Instantiate(source);clip.name=source.name;var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);AssetDatabase.CreateAsset(clip,derivedPath);Save(clip);
                var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);output.SetSourcePlayable(playable);graph.Play();
                Action<float> pose=t=>{playable.SetTime(t);graph.Evaluate(0);};
                var frames=times[i].Select(t=>Mathf.RoundToInt(t*source.frameRate)).ToArray();var normalized=frames.Select(f=>f/source.frameRate/clip.length).ToArray();
                var windows=new Vector2[frames.Length];var geometry=new EnemyWeakAttackContactFrame[frames.Length][];float reach=0;
                for(int h=0;h<frames.Length;h++){
                    pose(frames[h]/source.frameRate);
                    var hands=new[]{HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
                    int chosen=actor.transform.InverseTransformPoint(animator.GetBoneTransform(hands[0]).position).z>actor.transform.InverseTransformPoint(animator.GetBoneTransform(hands[1]).position).z?0:1;
                    var hand=animator.GetBoneTransform(hands[chosen]);var finger=animator.GetBoneTransform(chosen==0?HumanBodyBones.LeftIndexDistal:HumanBodyBones.RightIndexDistal)??hand;
                    windows[h]=new Vector2((frames[h]-2)/source.frameRate/clip.length,(frames[h]+2)/source.frameRate/clip.length);
                    geometry[h]=new EnemyWeakAttackContactFrame[5];
                    for(int j=0;j<5;j++){float t=(frames[h]-2+j)/source.frameRate;pose(t);var a=actor.transform.InverseTransformPoint(hand.position);var b=actor.transform.InverseTransformPoint(finger.position);
                        b+=(b-a).normalized*.12f;float radius=.23f;reach=Mathf.Max(reach,new Vector2(b.x,b.z).magnitude+radius);
                        geometry[h][j]=new EnemyWeakAttackContactFrame(t/clip.length,new EnemyWeakAttackContactCapsule(a,b,radius));}
                }
                var ability=weak[i];var profile=ability.WeakAttackExecution;
                var so=new SerializedObject(ability);so.FindProperty("hitNormalizedTime").floatValue=normalized[0];so.FindProperty("hitDelay").floatValue=frames[0]/source.frameRate;
                so.FindProperty("attackAnimationDuration").floatValue=clip.length;so.FindProperty("attackLockDuration").floatValue=clip.length+.20f;
                so.FindProperty("range").floatValue=reach+.35f;so.FindProperty("cooldown").floatValue=Mathf.Max(ability.Cooldown,clip.length+.20f);so.ApplyModifiedPropertiesWithoutUndo();ability.ConfigureAdditionalHits(normalized.Skip(1).ToArray());
                profile.Configure("GraveHunt/Rake/Hyper"+(i+1),source,clip,new Vector2(0,source.length),EnemyWeakAttackMotionPolicy.Stationary,reach+.35f,0,Vector2.zero,null,"",windows,geometry);
                ability.ConfigureWeakAttackExecution(profile);Save(profile);Save(ability);
                foreach(var state in controller.layers[0].stateMachine.states)if(state.state.motion==oldClips[i]){state.state.motion=clip;EditorUtility.SetDirty(state.state);}
                var animation=new SerializedObject(d.AnimationProfile);var attackClips=animation.FindProperty("attackClips");for(int c=0;c<attackClips.arraySize;c++)if(attackClips.GetArrayElementAtIndex(c).objectReferenceValue==oldClips[i])attackClips.GetArrayElementAtIndex(c).objectReferenceValue=clip;animation.ApplyModifiedPropertiesWithoutUndo();
                receipts.Add(new JObject{["ability"]=AssetDatabase.GetAssetPath(ability),["id"]=ability.AbilityId,["clip"]=source.name,["sourcePath"]=AssetDatabase.GetAssetPath(source),["runtimeClipPath"]=derivedPath,["fps"]=source.frameRate,["hitFrames"]=new JArray(frames),["hitSeconds"]=new JArray(frames.Select(f=>f/source.frameRate)),["nativeContactGeometryAuthored"]=true});playable.Destroy();
            }
            var p=AnimationClipPlayable.Create(graph,walk);p.SetApplyFootIK(false);output.SetSourcePlayable(p);graph.Play();var foot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var positions=new List<Vector3>();for(float t=0;t<walk.length;t+=1/60f){p.SetTime(t);graph.Evaluate(0);positions.Add(actor.transform.InverseTransformPoint(foot.position));}
            float low=positions.Min(v=>v.y);var stance=new List<float>();for(int n=1;n<positions.Count;n++){float velocity=(positions[n-1].z-positions[n].z)*60; if(positions[n].y<low+.08f&&velocity>.05f)stance.Add(velocity);}
            if(stance.Count<5)throw new Exception("Native foot-stance speed could not be measured");stance.Sort();float reference=stance[stance.Count/2];d.MovementProfile.ConfigureAnimationReferenceSpeeds(reference,reference);d.MovementProfile.ConfigureBackpedalAnimationReferenceSpeed(reference);Save(d.MovementProfile);p.Destroy();
            var sm=controller.layers[0].stateMachine;var loco=(BlendTree)sm.states.Single(s=>s.state.name=="Locomotion").state.motion;var oldIdle=d.AnimationProfile.Idle;
            Action<BlendTree> replace=null;replace=tree=>{var children=tree.children;for(int j=0;j<children.Length;j++){if(children[j].motion is BlendTree nested)replace(nested);else if(children[j].motion is AnimationClip)children[j].motion=children[j].motion==oldIdle?idles[0]:walk;}tree.children=children;EditorUtility.SetDirty(tree);};replace(loco);
            for(int i=0;i<2;i++){var state=sm.states.Single(s=>s.state.name=="IdleVariant_"+i).state;state.motion=idles[i];EditorUtility.SetDirty(state);}
            var animationProfile=new SerializedObject(d.AnimationProfile);animationProfile.FindProperty("idle").objectReferenceValue=idles[0];animationProfile.FindProperty("walk").objectReferenceValue=walk;animationProfile.FindProperty("run").objectReferenceValue=walk;animationProfile.ApplyModifiedPropertiesWithoutUndo();Save(d.AnimationProfile);Save(controller);
            string actorPath=AssetDatabase.GetAssetPath(d.ActorPrefab);var root=PrefabUtility.LoadPrefabContents(actorPath);
            try{root.GetComponent<EnemyLocomotionVariantSelector>().Configure(root.GetComponentInChildren<Animator>(true),new[]{"IdleVariant_0","IdleVariant_1"},new[]{1f,1f},new[]{1f});PrefabUtility.SaveAsPrefabAsset(root,actorPath,out bool saved);if(!saved)throw new Exception("Rake prefab save failed");}finally{PrefabUtility.UnloadPrefabContents(root);}
            File.WriteAllText(Path.Combine(directory,"apply-result.json"),new JObject{["status"]="PASS_NATIVE_RAKE_REPLACEMENT",["entries"]=receipts,["idles"]=new JArray(idles.Select(c=>c.name)),["walk"]=walk.name,["walkStanceReferenceSpeed"]=reference,["strongChanged"]=false,["approvedParryMotionsChanged"]=false,["audioChanged"]=false}.ToString());
            return "PASS_NATIVE_RAKE_REPLACEMENT";
        }finally{graph.Destroy();UnityEngine.Object.DestroyImmediate(actor);EditorSceneManager.ClosePreviewScene(scene);}
    }

    public static string ApplyReviewedWeakCorrections(string directory)
    {
        Idle();string output=Path.Combine(directory,"ContactCorrection");Directory.CreateDirectory(output);var results=new JArray();
        foreach(string path in Directory.GetFiles(directory,"GraveHunt_*-batch.json")){
            var batch=JObject.Parse(File.ReadAllText(path));var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"Definitions/"+(string)batch["enemyId"]+".asset");
            foreach(var row in batch["entries"].OfType<JObject>().Where(r=>(string)r["role"]=="weak")){
                var ability=Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).Single(a=>a.WeakAttackExecution?.OriginalClip?.name==(string)row["actualClip"]);
                foreach(var asset in new UnityEngine.Object[]{ability,ability.WeakAttackExecution}){string assetPath=AssetDatabase.GetAssetPath(asset);if(EditorUtility.IsDirty(asset))throw new Exception("Unsaved owned asset");
                    foreach(string suffix in new[]{"",".meta"}){string backup=Path.Combine(output,"Before",assetPath+suffix);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(Path.Combine(Project,assetPath+suffix),backup,false);}}
                var times=row["hitNormalizedTimes"].Values<float>().ToArray();var so=new SerializedObject(ability);so.FindProperty("hitNormalizedTime").floatValue=times[0];
                so.FindProperty("hitDelay").floatValue=times[0]*ability.AttackAnimationDuration;so.FindProperty("range").floatValue=(float)row["stationaryStartRange"];so.ApplyModifiedPropertiesWithoutUndo();ability.ConfigureAdditionalHits(times.Skip(1).ToArray());
                MonsterWeakAttackExecutionWriter.UpdateContentExtension(row,ability);
                results.Add(new JObject{["id"]=ability.AbilityId,["path"]=AssetDatabase.GetAssetPath(ability),["range"]=ability.Range,["hitFrames"]=new JArray(times.Select(t=>Mathf.RoundToInt(t*ability.AttackAnimationDuration*(float)row["nativeFps"])))});
            }
            if(d.EnemyId=="GraveHunt_DarknessSpider"){
                string actorPath=AssetDatabase.GetAssetPath(d.ActorPrefab);foreach(string suffix in new[]{"",".meta"}){string backup=Path.Combine(output,"Before",actorPath+suffix);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(Path.Combine(Project,actorPath+suffix),backup,false);}
                var actor=PrefabUtility.LoadPrefabContents(actorPath);try{var component=actor.GetComponent<EnemyActor>();var body=component.CollisionRoot.GetComponentInChildren<CapsuleCollider>();body.radius=(float)batch["bodyRadius"];actor.GetComponent<CombatTarget>().ConfigureVolume(body.center,body.radius,body.height);
                    PrefabUtility.SaveAsPrefabAsset(actor,actorPath,out bool saved);if(!saved)throw new Exception("Spider physical body save failed");}finally{PrefabUtility.UnloadPrefabContents(actor);}
            }
        }
        File.WriteAllText(Path.Combine(output,"apply-result.json"),new JObject{["status"]="PASS_NATIVE_CONTACT_CORRECTION_PENDING_PLAY",["entries"]=results,["strongChanged"]=false,["sourceChanged"]=false,["attackSpeedChanged"]=false}.ToString());return "NATIVE_WEAK_CORRECTIONS_"+results.Count;
    }

    public static string ApplyFinalWeakContactReview(string directory)
    {
        Idle();string folder=Path.Combine(directory,"FinalWeakContactReview");Directory.CreateDirectory(folder);var rows=new JArray();
        var ids=JObject.Parse(File.ReadAllText(Path.Combine(directory,"content-selection.json")))["rows"].Select(r=>(string)r["enemyId"]).Concat(new[]{"DeathHarvest_RakeBrute"});
        foreach(string id in ids){var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"Definitions/"+id+".asset");
            var weak=Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).Where(a=>!a.IsTelegraphedStrongAttack).ToArray();
            var scene=EditorSceneManager.NewPreviewScene();var actor=UnityEngine.Object.Instantiate(d.ActorPrefab.gameObject);SceneManager.MoveGameObjectToScene(actor,scene);
            foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            var animator=actor.GetComponentInChildren<Animator>(true);animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var graph=PlayableGraph.Create("Final native weak contact review");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var output=AnimationPlayableOutput.Create(graph,"Pose",animator);
            try{for(int index=0;index<weak.Length;index++){var a=weak[index];var p=a.WeakAttackExecution;var c=p.RuntimeClip;
                foreach(var asset in new UnityEngine.Object[]{a,p}){string path=AssetDatabase.GetAssetPath(asset);if(EditorUtility.IsDirty(asset))throw new Exception("Unsaved owned attack");foreach(string suffix in new[]{"",".meta"}){string backup=Path.Combine(folder,"Before",path+suffix);Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup))File.Copy(Path.Combine(Project,path+suffix),backup,false);}}
                var times=Enumerable.Range(0,a.HitCount).Select(a.GetHitNormalizedTime).ToArray();var windows=Enumerable.Range(0,p.ContactWindowCount).Select(h=>{p.TryGetContactWindow(h,out var w);return w;}).ToArray();
                var field=typeof(EnemyWeakAttackContactGeometry).GetField("frames",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                var frames=p.CopyContactGeometry().Select(g=>((EnemyWeakAttackContactFrame[])field.GetValue(g)).ToArray()).ToArray();
                if(id=="DeathHarvest_RakeBrute"){
                    int number=int.Parse(p.SelectionKey.Substring(p.SelectionKey.Length-1));var hits=number==1?new[]{19,45}:number==2?new[]{25,64,88}:new[]{11};
                    times=hits.Select(f=>f/c.frameRate/c.length).ToArray();windows=new Vector2[hits.Length];frames=new EnemyWeakAttackContactFrame[hits.Length][];
                    var play=AnimationClipPlayable.Create(graph,c);play.SetApplyFootIK(false);output.SetSourcePlayable(play);graph.Play();
                    for(int h=0;h<hits.Length;h++){
                        play.SetTime(0);graph.Evaluate(.00001f);play.SetTime(hits[h]/c.frameRate);graph.Evaluate(0);var hands=new[]{HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
                        Func<int,float> handReach=side=>{var start=actor.transform.InverseTransformPoint(animator.GetBoneTransform(hands[side]).position);
                            var tip=animator.GetBoneTransform(side==0?HumanBodyBones.LeftIndexDistal:HumanBodyBones.RightIndexDistal)??animator.GetBoneTransform(hands[side]);var end=actor.transform.InverseTransformPoint(tip.position);end+=(end-start).normalized*.12f;
                            return PlayerApproachReach(start,end,.23f);};
                        int chosen=handReach(0)>handReach(1)?0:1;
                        var hand=animator.GetBoneTransform(hands[chosen]);var finger=animator.GetBoneTransform(chosen==0?HumanBodyBones.LeftIndexDistal:HumanBodyBones.RightIndexDistal)??hand;
                        windows[h]=new Vector2((hits[h]-2)/c.frameRate/c.length,(hits[h]+2)/c.frameRate/c.length);frames[h]=new EnemyWeakAttackContactFrame[5];
                        for(int j=0;j<5;j++){float time=(hits[h]-2+j)/c.frameRate;play.SetTime(time);graph.Evaluate(0);var start=actor.transform.InverseTransformPoint(hand.position);var end=actor.transform.InverseTransformPoint(finger.position);end+=(end-start).normalized*.12f;
                            frames[h][j]=new EnemyWeakAttackContactFrame(time/c.length,new EnemyWeakAttackContactCapsule(start,end,.23f));}
                    }play.Destroy();
                }
                float reach=float.MaxValue;
                for(int h=0;h<times.Length;h++){var geometry=EnemyWeakAttackContactGeometry.Create(frames[h],windows[h]);float phase=0;for(int shape=0;shape<geometry.CapsuleCount;shape++){geometry.TryEvaluateCapsule(shape,times[h],out var capsule);phase=Mathf.Max(phase,PlayerApproachReach(capsule.A,capsule.B,capsule.Radius));}
                    if(phase<=.1f)throw new Exception("Authored impact cannot contact saved player: "+a.AbilityId+" phase "+h);reach=Mathf.Min(reach,phase);}
                float startRange=Mathf.Max(.1f,reach-.035f);var serialized=new SerializedObject(p);var progress=serialized.FindProperty("advanceProgress").animationCurveValue;
                var advanceWindow=p.AdvanceWindow;if(p.UsesAdvance)advanceWindow.y=Mathf.Min(advanceWindow.y,times[0]-1/c.frameRate/c.length);
                var ability=new SerializedObject(a);ability.FindProperty("hitNormalizedTime").floatValue=times[0];ability.FindProperty("hitDelay").floatValue=times[0]*c.length;
                ability.FindProperty("range").floatValue=startRange;ability.ApplyModifiedPropertiesWithoutUndo();a.ConfigureAdditionalHits(times.Skip(1).ToArray());
                p.Configure(p.SelectionKey,p.OriginalClip,c,p.SourceTrimSeconds,p.MotionPolicy,startRange,p.MaxAdvanceDistance,advanceWindow,progress,p.PoseRootBonePath,windows,frames);
                a.ConfigureWeakAttackExecution(p);Save(p);Save(a);rows.Add(new JObject{["id"]=a.AbilityId,["hits"]=times.Length,["hitFrames"]=new JArray(times.Select(t=>Mathf.RoundToInt(t*c.length*c.frameRate))),["range"]=startRange,["advanceEnd"]=advanceWindow.y});
            }}finally{graph.Destroy();UnityEngine.Object.DestroyImmediate(actor);EditorSceneManager.ClosePreviewScene(scene);}
        }
        File.WriteAllText(Path.Combine(folder,"apply-result.json"),new JObject{["status"]="NATIVE_IMPACT_POSE_CONTACTS_PENDING_PLAY",["entries"]=rows,["strongChanged"]=false,["attackSpeedChanged"]=false,["audioChanged"]=false}.ToString());return "NATIVE_FINAL_WEAK_CONTACTS_"+rows.Count;
    }

    public static string PrepareSmallTimingReview(string directory)
    {
        Idle();directory=Path.Combine(directory,"SmallTiming");Directory.CreateDirectory(directory);var rows=new JArray();var tasks=new JArray();
        foreach(var table in AssetDatabase.FindAssets("t:EnemyThemeTable",new[]{Root+"Tables"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<EnemyThemeTable>))foreach(var entry in table.Entries.Where(e=>e.tier==EnemyThemeTier.Small)){
            var d=entry.definition;for(int i=0;i<d.AbilitySet.Count;i++){var a=d.AbilitySet.GetAbility(i);if(a.IsTelegraphedStrongAttack)throw new Exception("Small must not have strong attack");var profile=a.WeakAttackExecution;
                if(profile?.RuntimeClip==null)throw new Exception("Missing small runtime clip: "+a.AbilityId);var clip=profile.RuntimeClip;
                rows.Add(new JObject{["enemyId"]=d.EnemyId,["name"]=d.DisplayName,["theme"]=table.ThemeId,["abilityId"]=a.AbilityId,["abilityPath"]=AssetDatabase.GetAssetPath(a),["profilePath"]=AssetDatabase.GetAssetPath(profile),["model"]=AssetDatabase.GetAssetPath(d.ActorPrefab),
                    ["clip"]=clip.name,["animation"]=AssetDatabase.GetAssetPath(clip),["seconds"]=clip.length,["fps"]=clip.frameRate,["policy"]=profile.MotionPolicy.ToString(),["hitFrames"]=new JArray(Enumerable.Range(0,a.HitCount).Select(h=>a.GetHitNormalizedTime(h)*clip.length*clip.frameRate)),
                    ["hitTimes"]=new JArray(Enumerable.Range(0,a.HitCount).Select(h=>a.GetHitNormalizedTime(h)*clip.length)),["releaseCount"]=a.ReleaseCount,["projectilesPerRelease"]=a.ProjectilesPerRelease,["nativeContactGeometry"]=profile.HasContactGeometry,
                    ["animationEvents"]=new JArray(AnimationUtility.GetAnimationEvents(clip).Select(e=>new JObject{["function"]=e.functionName,["time"]=e.time,["frame"]=e.time*clip.frameRate}))});
                tasks.Add(new JObject{["id"]=d.EnemyId,["kind"]=a.AbilityId,["model"]=AssetDatabase.GetAssetPath(d.ActorPrefab),["animation"]=AssetDatabase.GetAssetPath(clip),["clip"]=clip.name,["fps"]=Mathf.RoundToInt(clip.frameRate)});
            }}
        if(rows.Select(r=>(string)r["enemyId"]).Distinct().Count()!=24)throw new Exception("All 24 small actors required");
        File.WriteAllText(Path.Combine(directory,"native-small-timing.json"),new JObject{["status"]="NATIVE_INVENTORY_VISUAL_PENDING",["actors"]=24,["rows"]=rows}.ToString());
        File.WriteAllText(Path.Combine(directory,"motion-capture-plan.json"),new JObject{["tasks"]=tasks}.ToString());return "PREPARED_SMALL_TIMING_"+rows.Count;
    }
}
