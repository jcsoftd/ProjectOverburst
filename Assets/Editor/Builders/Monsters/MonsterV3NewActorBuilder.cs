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
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Creates an isolated review-ready combat actor from the production actor core.
// Catalog/theme activation is a separate step after gameplay and presentation checks.
public static class MonsterV3NewActorBuilder
{
    const string Root="Assets/ProjectOverburst/Resources/Enemies/Themes/";
    static string Project=>Directory.GetParent(Application.dataPath).FullName;
    static string Hash(string path){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static void Idle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating
            ||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            ||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))
            throw new InvalidOperationException("Idle Editor and unoccupied account required.");
    }
    static void Set(Object obj,string field,Action<SerializedProperty> write)
    {var so=new SerializedObject(obj);write(so.FindProperty(field)??throw new InvalidOperationException(obj.name+" missing "+field));so.ApplyModifiedPropertiesWithoutUndo();}
    static void Ref(Object obj,string field,Object value)=>Set(obj,field,p=>p.objectReferenceValue=value);
    static void Text(Object obj,string field,string value)=>Set(obj,field,p=>p.stringValue=value);
    static void Number(Object obj,string field,float value)=>Set(obj,field,p=>p.floatValue=value);
    static AnimationClip Clip(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
    static AnimationClip Original(JObject row)=>AssetDatabase.LoadAllAssetsAtPath((string)row["sourcePath"]).OfType<AnimationClip>().Single(c=>
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c,out string g,out long id)&&g==(string)row["sourceGuid"]&&id==(long)row["sourceLocalId"]);
    static float RootSpeed(AnimationClip clip,float scale,bool requireTravel=true)
    {
        var bindings=AnimationUtility.GetCurveBindings(clip);
        var rootTranslation=bindings.Where(b=>b.propertyName=="RootT.z").ToArray();
        float speed=0;
        if(rootTranslation.Length==1)
        {
            var curve=AnimationUtility.GetEditorCurve(clip,rootTranslation[0]);
            speed=Mathf.Abs(curve.Evaluate(clip.length)-curve.Evaluate(0))/clip.length*scale;
        }
        else
        {
            // Generic exports can retain translation on the actual top-level Transform.
            // Only a root-level curve is eligible; animated limbs cannot calibrate gait speed.
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(clip));
            foreach(var group in bindings.Where(b=>b.type==typeof(Transform)&&b.propertyName.StartsWith("m_LocalPosition.")
                &&b.path.Count(c=>c=='/')==0).GroupBy(b=>b.path).OrderBy(g=>g.Key.Length))
            {
                Vector3 travel=Vector3.zero;
                foreach(var binding in group)
                {
                    var curve=AnimationUtility.GetEditorCurve(clip,binding);float delta=curve.Evaluate(clip.length)-curve.Evaluate(0);
                    if(binding.propertyName=="m_LocalPosition.x")travel.x=delta;
                    else if(binding.propertyName=="m_LocalPosition.z")travel.z=delta;
                }
                var node=string.IsNullOrEmpty(group.Key)?model.transform:model.transform.Find(group.Key);
                if(node==null)continue;
                if(node.parent!=null)travel=node.parent.TransformVector(travel);
                speed=new Vector2(travel.x,travel.z).magnitude/clip.length*scale;
                if(speed>.01f)break;
            }
        }
        if(requireTravel&&speed<=.01f)throw new InvalidOperationException("Selected native locomotion has no calibrated root travel: "+clip.name);
        return speed;
    }
    static Vector3 Vector(JToken a)=>new Vector3((float)a[0],(float)a[1],(float)a[2]);
    static JArray Scenes()=>new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Select(s=>
        new JObject{["path"]=s.path,["dirty"]=s.isDirty,["roots"]=s.rootCount}));
    static void Folder(string path,List<string> folders)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash),folders);
        AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));folders.Add(path);
    }
    static T Create<T>(string path,List<string> created,List<string> folders,T seed=null) where T:ScriptableObject
    {
        Folder(Path.GetDirectoryName(path).Replace('\\','/'),folders);
        var value=seed==null?ScriptableObject.CreateInstance<T>():Object.Instantiate(seed);
        value.name=Path.GetFileNameWithoutExtension(path);AssetDatabase.CreateAsset(value,path);created.Add(path);return value;
    }
    static void Save(Object asset){EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);}
    static Material RenderMaterial(Material source,JObject batch,List<string> created,List<string> folders)
    {
        var mapping=batch["materialOverrides"]?[AssetDatabase.GetAssetPath(source)] as JObject;
        if(mapping==null)return source;
        string path=(string)mapping["targetPath"];
        if(!path.StartsWith("Assets/ProjectOverburst/05_Art/Materials/",StringComparison.Ordinal))throw new ArgumentException("Owned render material path required.");
        var existing=AssetDatabase.LoadAssetAtPath<Material>(path);if(existing!=null)return existing;
        var template=AssetDatabase.LoadAssetAtPath<Material>((string)mapping["templatePath"]);
        if(template==null||template.shader==null||template.shader.name!="Universal Render Pipeline/Lit")throw new InvalidOperationException("Approved URP render material unavailable.");
        Folder(Path.GetDirectoryName(path).Replace('\\','/'),folders);
        var material=Object.Instantiate(template);material.name=Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(material,path);created.Add(path);return material;
    }
    static AnimationClip LoopClip(AnimationClip source,string path,List<string> created,List<string> folders)
    {
        if(source.isLooping)return source;
        Folder(Path.GetDirectoryName(path).Replace('\\','/'),folders);
        var loop=Object.Instantiate(source);loop.name=Path.GetFileNameWithoutExtension(path);
        var settings=AnimationUtility.GetAnimationClipSettings(loop);settings.loopTime=true;
        AnimationUtility.SetAnimationClipSettings(loop,settings);
        AssetDatabase.CreateAsset(loop,path);created.Add(path);return loop;
    }
    static AnimatorState Action(AnimatorController controller,AnimatorStateMachine sm,AnimatorState idle,string trigger,string name,AnimationClip clip,bool attack,bool terminal=false)
    {
        controller.AddParameter(trigger,AnimatorControllerParameterType.Trigger);
        var state=sm.AddState(name);state.motion=clip;
        if(attack){state.speedParameter="AttackAnimSpeed";state.speedParameterActive=true;}
        var enter=sm.AddAnyStateTransition(state);enter.hasExitTime=false;enter.duration=.06f;enter.canTransitionToSelf=false;enter.AddCondition(AnimatorConditionMode.If,0,trigger);
        if(!terminal){var exit=state.AddTransition(idle);exit.hasExitTime=true;exit.exitTime=1;exit.duration=.08f;}
        return state;
    }
    static AnimatorController Controller(string path,AnimationClip idle,AnimationClip walk,AnimationClip run,AnimationClip back,bool reverseBack,AnimationClip hit,AnimationClip death,AnimationClip[] attacks,
        AnimationClip[] parry,List<string> created,List<string> folders)
    {
        Folder(Path.GetDirectoryName(path).Replace('\\','/'),folders);
        var controller=AnimatorController.CreateAnimatorControllerAtPath(path);created.Add(path);var sm=controller.layers[0].stateMachine;
        foreach(string name in new[]{"Locomotion","MoveAnimSpeed","AttackAnimSpeed"})controller.AddParameter(new AnimatorControllerParameter{
            name=name,type=AnimatorControllerParameterType.Float,defaultFloat=name=="Locomotion"?0:1});
        var tree=new BlendTree{name="Locomotion",blendParameter="Locomotion",blendType=BlendTreeType.Simple1D,useAutomaticThresholds=false};AssetDatabase.AddObjectToAsset(tree,controller);
        tree.AddChild(back,-1);tree.AddChild(idle,0);tree.AddChild(walk,1);tree.AddChild(run,2);
        if(reverseBack){var children=tree.children;var child=children[0];child.timeScale=-1;children[0]=child;tree.children=children;}
        var loco=sm.AddState("Locomotion");loco.motion=tree;loco.speedParameter="MoveAnimSpeed";loco.speedParameterActive=true;sm.defaultState=loco;
        for(int i=0;i<attacks.Length;i++)Action(controller,sm,loco,"Attack"+(i+1),"Attack_"+(i+1),attacks[i],true);
        controller.AddParameter("HitX",AnimatorControllerParameterType.Float);controller.AddParameter("HitZ",AnimatorControllerParameterType.Float);
        Action(controller,sm,loco,"GotHit","Get_hit",hit,false);Action(controller,sm,loco,"Death","Death",death,false,true);
        Action(controller,sm,loco,"IdleBreak","Idle_break",idle,false);
        if(parry.Length==3)
        {
        var collapse=sm.AddState(EnemyAnimationBridge.ParryCollapseStateName);collapse.motion=parry[0];collapse.speed=EnemyAnimationBridge.ParryCollapseSpeed;
        var stunned=sm.AddState(EnemyAnimationBridge.StunnedLoopStateName);stunned.motion=parry[1];
        var recover=sm.AddState(EnemyAnimationBridge.StunRecoverStateName);recover.motion=parry[2];
        var toLoop=collapse.AddTransition(stunned);toLoop.hasExitTime=true;toLoop.exitTime=1;toLoop.duration=.08f;
        var toIdle=recover.AddTransition(loco);toIdle.hasExitTime=true;toIdle.exitTime=1;toIdle.duration=.08f;
        }
        Save(controller);return controller;
    }
    public static string Apply(string batchPath,string output)
    {
        Idle();output=Path.GetFullPath(output);
        string workspace=Directory.GetParent(Project).FullName;
        string allowed=Path.GetFullPath(Path.Combine(workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Private artifact path required.");
        Directory.CreateDirectory(output);var batch=JObject.Parse(File.ReadAllText(batchPath));
        if((string)batch["schema"]!="overburst.v3.new-actor-batch.v1"||(bool?)batch["activateInGame"]!=false||(bool?)batch["applyAudio"]!=false)
            throw new ArgumentException("New review actor batch required; game activation and audio writes are excluded.");
        string approvedPath=(string)batch["approvedPath"],approvedSha=(string)batch["approvedSha256"],key=(string)batch["cardKey"],id=(string)batch["enemyId"];
        if(Hash(approvedPath)!=approvedSha)throw new InvalidOperationException("Approved V3 changed.");
        var approved=JObject.Parse(File.ReadAllText(approvedPath));
        foreach(var i in approved["inputs"])if(Hash((string)i["path"])!=(string)i["sha256"])throw new InvalidOperationException("Selection input changed.");
        var card=approved["cards"]?[key] as JObject;
        if(card==null||(string)card["status"]!="confirmed"||(bool?)card["inRoster"]!=true||(bool?)card["isBoss"]==true
            ||id.Any(c=>!(char.IsLetterOrDigit(c)||c=='_'||c=='-')))throw new ArgumentException("Confirmed regular actor required.");
        var sourceFiles=(JObject)batch["expectedSourceFiles"];
        foreach(var p in sourceFiles.Properties())
        {
            if(Hash(Path.Combine(Project,p.Name))!=(string)p.Value)throw new InvalidOperationException("Source changed: "+p.Name);
            if(!p.Name.EndsWith(".meta",StringComparison.Ordinal))foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(p.Name))
                if(asset!=null&&EditorUtility.IsDirty(asset))throw new InvalidOperationException("Unsaved source asset: "+p.Name);
        }
        string receipt=Path.Combine(output,"apply-result.json");
        if(File.Exists(receipt))
        {
            var prior=JObject.Parse(File.ReadAllText(receipt));
            if((string)prior["status"]!="APPLIED_REVIEW_ACTOR"||(string)prior["batchSha256"]!=Hash(batchPath))throw new InvalidOperationException("Inspect prior outcome before retry.");
            foreach(var p in ((JObject)prior["assetHashes"]).Properties())if(Hash(Path.Combine(Project,p.Name))!=(string)p.Value)throw new InvalidOperationException("Created actor changed; preserved: "+p.Name);
            return "UNCHANGED_VERIFIED_PRIOR_RESULT";
        }
        var rows=batch["entries"].OfType<JObject>().ToArray();
        if(!card["weak"].Values<JObject>().Select(r=>(string)r["key"]).OrderBy(x=>x).SequenceEqual(rows.Where(r=>(string)r["role"]=="weak").Select(r=>(string)r["selectionKey"]).OrderBy(x=>x)))
            throw new InvalidOperationException("Incomplete approved weak selection.");
        var strong=rows.SingleOrDefault(r=>(string)r["role"]=="strong");
        bool weakOnlySmall=strong==null&&(string)batch["grade"]=="small"&&key.EndsWith(":small",StringComparison.Ordinal)&&card["strong"].Count()==0;
        if(strong==null&&!weakOnlySmall)throw new InvalidOperationException("Only confirmed small actors may omit strong/parry motions.");
        if(!weakOnlySmall&&(card["strong"].Count()!=1||(string)card["strong"][0]["key"]!=(string)strong["selectionKey"]
            ||(string)strong["parryMotionReceipt"]?["status"]!="APPROVED_IMPORT_VERIFIED_ACTOR_BIND_PENDING"))throw new InvalidOperationException("Confirmed strong and approved three motions required.");
        var seed=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)batch["seedDefinition"]);
        if(seed==null||!seed.IsValid||!seed.ActorPrefab.IsAuthoringValid||seed.ResolveRuntimeStats().VisualScale!=Vector3.one)
            throw new InvalidOperationException("Valid production actor core with unit grade/variant scale required.");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>((string)batch["sourcePrefab"]);
        var preset=AssetDatabase.LoadAssetAtPath<EnemyAiPreset>((string)batch["aiPresetPath"]);
        if(preset==null)throw new InvalidOperationException("Approved theme pursuit preset missing.");
        if(prefab==null||prefab.GetComponentsInChildren<Transform>(true).Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))throw new InvalidOperationException("Source model missing scripts.");
        var paths=new List<string>{Root+"Definitions/"+id+".asset",Root+"Species/"+id+".asset",Root+"Animations/"+id+".asset",
            Root+"Animations/AC_"+id+".controller",Root+"Movement/"+id+".asset",Root+"Behavior/"+id+".asset",Root+"Abilities/"+id+"_Set.asset",Root+"Actors/PF_"+id+".prefab"};
        foreach(var row in rows)
        {
            Original(row);paths.Add(Root+"Abilities/"+id+"_"+row["actualClip"]+".asset");
            if((string)row["role"]=="weak")paths.Add(MonsterWeakAttackExecutionWriter.Root+"/"+id+"_"+row["actualClip"]+".asset");
        }
        if(batch["materialOverrides"] is JObject materialMappings)foreach(var mapping in materialMappings.Properties())paths.Add((string)mapping.Value["targetPath"]);
        var locomotionSources=new[]{Clip((string)batch["idlePath"]),Clip((string)batch["movePath"]),
            Clip((string)batch["runPath"]??(string)batch["movePath"]),Clip((string)batch["extras"]["CrawlBackwards"])};
        var locomotionRoles=new[]{"Idle","Walk","Run","Back"};
        for(int i=0;i<locomotionSources.Length;i++)if(!locomotionSources[i].isLooping)paths.Add(Root+"Animations/"+id+"_"+locomotionRoles[i]+"_Loop.anim");
        foreach(string path in paths)if(File.Exists(Path.Combine(Project,path))||File.Exists(Path.Combine(Project,path+".meta"))||AssetDatabase.LoadMainAssetAtPath(path)!=null)
            throw new InvalidOperationException("Creation target already exists; preserved: "+path);
        var parry=weakOnlySmall?Array.Empty<AnimationClip>():new[]{"ParryCollapse","StunnedLoop","StunRecover"}.Select(role=>
        {
            var binding=strong["parryMotionReceipt"]["runtimeBindings"][role];var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>((string)binding["assetPath"]);
            if(clip==null||!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip,out string guid,out long localId)||guid!=(string)binding["guid"]||localId!=(long)binding["localId"]
                ||Mathf.Abs(clip.frameRate-30)>.001f||clip.isLooping!=(role=="StunnedLoop"))throw new InvalidOperationException("Approved runtime binding changed: "+role);
            return clip;
        }).ToArray();
        var created=new List<string>();var folders=new List<string>();var before=Scenes();Scene scene=default;GameObject actorRoot=null;
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            var looping=locomotionSources.Select((source,i)=>LoopClip(source,Root+"Animations/"+id+"_"+locomotionRoles[i]+"_Loop.anim",created,folders)).ToArray();
            var idle=looping[0];var walk=looping[1];var run=looping[2];var back=looping[3];
            var hit=Clip((string)batch["extras"]["GetHit1"]);var death=Clip((string)batch["extras"]["Death"]);var attacks=rows.Select(Original).ToArray();
            var controller=Controller(Root+"Animations/AC_"+id+".controller",idle,walk,run,back,(bool?)batch["reverseBackwardAnimation"]==true,hit,death,attacks,parry,created,folders);
            var animation=Create<EnemyAnimationProfile>(Root+"Animations/"+id+".asset",created,folders);
            animation.Configure(id,controller,idle,walk,run,attacks,hit,death,new[]{back},new[]{(string)batch["extras"]["CrawlForward_RM"]});
            if(parry.Length==3){Ref(animation,"parryCollapse",parry[0]);Ref(animation,"stunnedLoop",parry[1]);Ref(animation,"stunRecover",parry[2]);}
            Save(animation);
            var abilities=new List<EnemyAbilityDefinition>();
            foreach(var row in rows)
            {
                bool isStrong=(string)row["role"]=="strong";var source=Enumerable.Range(0,seed.AbilitySet.Count).Select(seed.AbilitySet.GetAbility).First(a=>a.IsTelegraphedStrongAttack==isStrong);
                var ability=Create(Root+"Abilities/"+id+"_"+row["actualClip"]+".asset",created,folders,source);ability.ConfigureWeakAttackExecution(null);
                Text(ability,"abilityId",id+"_"+row["actualClip"]);Text(ability,"animatorTrigger","Attack"+(abilities.Count+1));
                var hitTimes=isStrong?(batch["strongHitNormalizedTimes"] as JArray??new JArray((int)batch["strongImpactFrame"]/Original(row).frameRate/Original(row).length)):(JArray)row["hitNormalizedTimes"];
                float time=(float)hitTimes[0];
                Number(ability,"hitNormalizedTime",time);Number(ability,"hitDelay",time*Original(row).length);Number(ability,"attackAnimationDuration",Original(row).length);
                Number(ability,"attackLockDuration",Original(row).length);ability.ConfigureAdditionalHits(hitTimes.Skip(1).Select(t=>(float)t).ToArray());
                if(isStrong){Number(ability,"range",(float)batch["strongRange"]);Number(ability,"hitRadius",(float)batch["strongRange"]);}
                else
                {
                    if((string)row["executionMode"]=="Projectile"||(string)row["executionMode"]=="Zone")
                    {
                        var mode=(EnemyAbilityExecutionMode)Enum.Parse(typeof(EnemyAbilityExecutionMode),(string)row["executionMode"]);
                        Set(ability,"executionMode",p=>p.enumValueIndex=(int)mode);
                        Number(ability,"range",(float)row["stationaryStartRange"]);
                    }
                    MonsterWeakAttackExecutionWriter.Apply(approvedPath,approvedSha,key,(string)row["selectionKey"],row,ability,Original(row),Original(row));
                    created.Add(AssetDatabase.GetAssetPath(ability.WeakAttackExecution));
                }
                Save(ability);abilities.Add(ability);
            }
            var set=Create<EnemyAbilitySet>(Root+"Abilities/"+id+"_Set.asset",created,folders);set.Configure(id,abilities.ToArray());Save(set);
            var movement=Create(Root+"Movement/"+id+".asset",created,folders,seed.MovementProfile);Text(movement,"profileId",id);
            // A cloned movement preset must not wait for turn states absent from this controller.
            if(!new[]{"FacingTurnLeft","FacingTurnRight"}.All(name=>controller.layers[0].stateMachine.states.Any(s=>s.state.name==name)))
                movement.ConfigureTurnAnimation(0,0);
            bool nativeInPlace=(string)batch["locomotionReferencePolicy"]=="NativeInPlaceAtGradeSpeed";
            float modelScale=Vector(batch["modelScale"]).x;
            if(nativeInPlace&&locomotionSources.Skip(1).Any(c=>RootSpeed(c,modelScale,false)>.05f))
                throw new InvalidOperationException("In-place policy requires native locomotion with no horizontal root travel.");
            float rmSpeed=nativeInPlace?movement.MoveSpeed:RootSpeed(Clip((string)batch["extras"]["CrawlForward_RM"]),modelScale);
            float runSpeed=nativeInPlace?movement.MoveSpeed*movement.RunSpeedMultiplier:
                batch["extras"]["Run_RM"]!=null?RootSpeed(Clip((string)batch["extras"]["Run_RM"]),modelScale):rmSpeed;
            movement.ConfigureAnimationReferenceSpeeds(rmSpeed,runSpeed);
            movement.ConfigureBackpedalAnimationReferenceSpeed(nativeInPlace?movement.MoveSpeed:
                RootSpeed(Clip((string)batch["extras"]["CrawlBackwards_RM"]),modelScale));Save(movement);
            var behavior=Create(Root+"Behavior/"+id+".asset",created,folders,seed.BehaviorProfile);Text(behavior,"profileId",id);
            Set(behavior,"playTauntOnAlert",p=>p.boolValue=false);Number(behavior,"preferredMinDistance",(float)batch["bodyRadius"]+.45f);
            Number(behavior,"preferredApproachDistance",(float)batch["bodyRadius"]+.8f);Save(behavior);
            var species=Create(Root+"Species/"+id+".asset",created,folders,seed.Species);species.Configure(id,(string)card["name"],prefab,animation,set);
            species.ConfigureRuntime(seed.Species.CombatRole,seed.Species.BaseMaxHealth,movement,behavior,seed.Species.ThreatCost);Save(species);
            var definition=Create(Root+"Definitions/"+id+".asset",created,folders,seed);definition.ConfigureIdentity(id,(string)card["name"]);
            definition.ConfigureRuntime(animation,set,behavior,movement,preset,seed.SquadParticipationMode);Save(definition);
            scene=EditorSceneManager.NewPreviewScene();actorRoot=Object.Instantiate(seed.ActorPrefab.gameObject);SceneManager.MoveGameObjectToScene(actorRoot,scene);actorRoot.SetActive(false);
            actorRoot.name="PF_"+id;actorRoot.transform.localScale=Vector3.one;var actor=actorRoot.GetComponent<EnemyActor>();var visual=actor.VisualRoot;
            var oldAnimator=actor.Animator;var oldScale=visual.Find("Authored model scale");var oldAnimatedRoot=oldAnimator.transform;
            var scaled=new GameObject("Authored model scale").transform;scaled.SetParent(visual,false);scaled.localScale=Vector(batch["modelScale"]);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);model.transform.SetParent(scaled,false);
            model.transform.localPosition=new Vector3(0,(float)batch["modelYOffset"]/scaled.localScale.x,0);model.transform.localRotation=Quaternion.identity;model.transform.localScale=Vector3.one;
            if((string)batch["weaponMode"]=="TwoHanded")
            {
                foreach(var t in model.GetComponentsInChildren<Transform>(true))
                {
                    if(t.name=="SM_2HandedSword")t.gameObject.SetActive(true);
                    if(t.name=="SM_Sword"||t.name=="SM_Shield")t.gameObject.SetActive(false);
                }
            }
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>RenderMaterial(m,batch,created,folders)).ToArray();
            var animator=model.GetComponentInChildren<Animator>(true);if(animator==null)throw new InvalidOperationException("Native Animator missing.");
            animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.fireEvents=false;
            foreach(var c in actorRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(c==null)throw new InvalidOperationException("Actor missing script.");
                var so=new SerializedObject(c);var p=so.GetIterator();
                while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference)
                {
                    if(p.objectReferenceValue==oldAnimator)p.objectReferenceValue=animator;
                    else if(p.objectReferenceValue==oldScale)p.objectReferenceValue=scaled;
                    else if(p.objectReferenceValue==oldAnimatedRoot)p.objectReferenceValue=animator.transform;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            for(int i=visual.childCount-1;i>=0;i--)if(visual.GetChild(i)!=scaled)Object.DestroyImmediate(visual.GetChild(i).gameObject);
            foreach(var c in model.GetComponentsInChildren<Collider>(true))c.enabled=false;
            foreach(var rb in model.GetComponentsInChildren<Rigidbody>(true)){rb.isKinematic=true;rb.useGravity=false;}
            foreach(var t in actorRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("Enemy");
            float radius=(float)batch["bodyRadius"],height=(float)batch["bodyHeight"];var capsule=actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>();
            capsule.radius=radius;capsule.height=height;capsule.center=new Vector3(0,height*.5f+.02f,0);
            var target=actorRoot.GetComponent<CombatTarget>();target.ConfigureVolume(capsule.center,radius,height);
            target.ConfigureHurtVolume(new Vector3(0,(float)batch["hurtHeight"]*.5f+.02f,0),(float)batch["hurtRadius"],(float)batch["hurtHeight"]);
            actor.Anchors.Find("AttackPoint").localPosition=new Vector3(0,.8f,.8f);
            actor.Anchors.Find("HitVfxPoint").localPosition=new Vector3(0,.9f,0);
            actor.Anchors.Find("HpBarAnchor").localPosition=Vector3.up*(Vector(batch["size"]).y+.3f);
            Ref(actor,"definition",definition);actor.Identity.SetDefinition(definition);Ref(actor.Movement,"profile",movement);
            Ref(actor.AI,"behaviorProfile",behavior);Ref(actor.AI,"squadPursuitPreset",preset);
            Ref(actor.Melee,"abilitySet",set);Ref(actor.AbilityController,"abilitySet",set);
            Set(actor.Melee,"attackTriggers",p=>{p.arraySize=abilities.Count;for(int i=0;i<abilities.Count;i++)p.GetArrayElementAtIndex(i).stringValue=abilities[i].AnimatorTrigger;});
            var sfx=actorRoot.GetComponent<MonsterHitSfxTarget>();if(sfx!=null)Ref(sfx,"bundle",null);
            if(actorRoot.GetComponent<BloodHitTarget>()?.Profile==null)throw new InvalidOperationException("Common blood/hit sound target profile missing.");
            actorRoot.GetComponent<EnemyMovementReaction>().ConfigureVisualReactionRoot(scaled);
            actorRoot.GetComponent<EnemyVisualRootGuard>().Configure(animator.transform);
            var weakDriver=actorRoot.GetComponent<EnemyWeakAttackMotionDriver>()??actorRoot.AddComponent<EnemyWeakAttackMotionDriver>();
            weakDriver.Configure(actor.Melee,actor.Movement);
            if(rows.Any(r=>(string)r["executionMode"]=="Projectile"))
            {
                var projectile=actorRoot.GetComponent<EnemyThemeSpecialExecutor>()??actorRoot.AddComponent<EnemyThemeSpecialExecutor>();
                Set(projectile,"muzzleOverrides",p=>
                {
                    var shots=rows.Select((r,i)=>new{row=r,index=i}).Where(x=>(string)x.row["executionMode"]=="Projectile").ToArray();
                    p.arraySize=shots.Length;
                    for(int i=0;i<shots.Length;i++)
                    {
                        var item=p.GetArrayElementAtIndex(i);var shot=shots[i];
                        var mouth=animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name==(string)shot.row["muzzleBone"]);
                        item.FindPropertyRelative("ability").objectReferenceValue=abilities[shot.index];
                        item.FindPropertyRelative("socket").objectReferenceValue=mouth;
                        item.FindPropertyRelative("localOffset").vector3Value=Vector3.zero;
                    }
                });
                Set(actor.AbilityController,"executors",p=>
                {
                    var executors=actorRoot.GetComponents<EnemyAbilityExecutor>();p.arraySize=executors.Length;
                    for(int i=0;i<executors.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=executors[i];
                });
            }
            var channelRow=rows.SingleOrDefault(r=>(string)r["executionMode"]=="Zone");
            if(channelRow!=null)
            {
                if((string)channelRow["sourceCountKind"]!="continuous"||(int?)channelRow["runtimeHitCount"]!=3)
                    throw new InvalidOperationException("A confirmed continuous attack with three budgeted pulses is required.");
                var channel=actorRoot.GetComponent<EnemyChannelAbilityExecutor>()??actorRoot.AddComponent<EnemyChannelAbilityExecutor>();
                Ref(channel,"channelAbility",abilities[Array.IndexOf(rows,channelRow)]);
                Ref(channel,"muzzle",animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name==(string)channelRow["muzzleBone"]));
                Number(channel,"castRadius",.07f);
                Set(actor.AbilityController,"executors",p=>
                {
                    var executors=actorRoot.GetComponents<EnemyAbilityExecutor>();p.arraySize=executors.Length;
                    for(int i=0;i<executors.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=executors[i];
                });
            }
            Ref(actorRoot.GetComponent<EnemyDeathPresentation>(),"visualRoot",null);
            CombatImpactFeelBuilder.ConfigureActor(actor,definition);actorRoot.SetActive(true);
            string actorPath=Root+"Actors/PF_"+id+".prefab";Folder(Root+"Actors",folders);
            var saved=PrefabUtility.SaveAsPrefabAsset(actorRoot,actorPath,out bool success);if(!success)throw new IOException("Actor prefab save failed.");created.Add(actorPath);
            definition.ConfigureComposition(species,seed.Grade,seed.Variant,saved.GetComponent<EnemyActor>());Save(definition);
            if(!definition.IsValid||!definition.ActorPrefab.IsAuthoringValid)throw new InvalidOperationException("Saved production actor references invalid.");
            foreach(var p in sourceFiles.Properties())if(Hash(Path.Combine(Project,p.Name))!=(string)p.Value)throw new InvalidOperationException("Source preservation failed: "+p.Name);
            var hashes=new JObject();foreach(string path in created)foreach(string suffix in new[]{"",".meta"})hashes[path+suffix]=Hash(Path.Combine(Project,path+suffix));
            var result=new JObject{["status"]="APPLIED_REVIEW_ACTOR",["batchSha256"]=Hash(batchPath),["cardKey"]=key,["enemyId"]=id,
                ["definitionPath"]=AssetDatabase.GetAssetPath(definition),["actorPath"]=actorPath,["weakCount"]=rows.Count(r=>(string)r["role"]=="weak"),["strongCount"]=strong==null?0:1,["approvedParryCount"]=parry.Length,
                ["assetHashes"]=hashes,["sourceFilesPreserved"]=sourceFiles.Count,["mainCatalogChanged"]=false,["themeTablesChanged"]=false,
                ["fullGameRosterApplied"]=false,["newAudioApplied"]=false,["footfallsPending"]=batch["footfallsPending"],["gameplayVerified"]=false,
                ["scenesPreserved"]=JToken.DeepEquals(before,Scenes()),["tierDefaultsFrom"]=seed.EnemyId};
            if(!(bool)result["scenesPreserved"])throw new InvalidOperationException("User scenes changed.");
            File.WriteAllText(receipt,result.ToString());return result.ToString();
        }
        catch(Exception error)
        {
            // Only assets created by this call are removed. Source models/core assets are never written.
            for(int i=created.Count-1;i>=0;i--)if(AssetDatabase.LoadMainAssetAtPath(created[i])!=null)AssetDatabase.DeleteAsset(created[i]);
            for(int i=folders.Count-1;i>=0;i--)if(Directory.Exists(folders[i])&&Directory.GetFileSystemEntries(folders[i]).Length==0)AssetDatabase.DeleteAsset(folders[i]);
            File.WriteAllText(Path.Combine(output,"failure.json"),new JObject{["status"]="FAILED_CREATED_ASSETS_ROLLED_BACK",["error"]=error.ToString(),["created"]=JArray.FromObject(created)}.ToString());throw;
        }
        finally
        {if(actorRoot!=null)Object.DestroyImmediate(actorRoot);if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);AssetDatabase.AllowAutoRefresh();}
    }
}
