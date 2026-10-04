using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class CrustaspikanMaterialBuilder
{
    public const string Root="Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanMaterials";
    public const string CollectionPath=Root+"/BMC_Crustaspikan.asset";
    const string Seed="Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/CavernMutants_Ursacetus.asset";
    static string Project=>Directory.GetParent(Application.dataPath).FullName;
    static readonly List<string> created=new List<string>();
    static string Hash(string path){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static void Idle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating
            ||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            ||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))
            throw new InvalidOperationException("Idle Editor and unoccupied account required.");
    }
    static JArray Scenes()=>new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Select(s=>new JObject{["path"]=s.path,["dirty"]=s.isDirty,["roots"]=s.rootCount}));
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
    }
    static T Asset<T>(string name,T seed=null) where T:ScriptableObject
    {
        string path=Root+"/"+name+".asset";Folder(Path.GetDirectoryName(path).Replace('\\','/'));
        var asset=seed==null?ScriptableObject.CreateInstance<T>():Object.Instantiate(seed);asset.name=Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset,path);created.Add(path);return asset;
    }
    static void Set(Object o,string field,Action<SerializedProperty> action)
    {var so=new SerializedObject(o);action(so.FindProperty(field)??throw new InvalidOperationException(o.name+" missing "+field));so.ApplyModifiedPropertiesWithoutUndo();}
    static void Ref(Object o,string field,Object v)=>Set(o,field,p=>p.objectReferenceValue=v);
    static void Save(Object o){EditorUtility.SetDirty(o);AssetDatabase.SaveAssetIfDirty(o);}
    static AnimationClip Clip(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
    static AnimationClip Runtime(AnimationClip source,string name,bool loop)
    {
        string path=Root+"/LicensedClips/"+name+".anim";Folder(Root+"/LicensedClips");
        var clip=Object.Instantiate(source);clip.name=name;
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=false;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
        // Supplier Shot2 binds the former body name. Rebind this owned copy to the actual renderer.
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            if(binding.path=="SK_CrustaspikanBody")
            {var curve=AnimationUtility.GetEditorCurve(clip,binding);AnimationUtility.SetEditorCurve(clip,binding,null);var target=binding;target.path="SK_CrustaspikanBodyWithWeakPoint";AnimationUtility.SetEditorCurve(clip,target,curve);}
        AssetDatabase.CreateAsset(clip,path);created.Add(path);return clip;
    }
    static AnimatorController Controller(Dictionary<string,AnimationClip> clips,JObject plan)
    {
        string path=Root+"/AC_Crustaspikan.controller";
        var controller=AnimatorController.CreateAnimatorControllerAtPath(path);created.Add(path);var sm=controller.layers[0].stateMachine;
        foreach(string name in new[]{"Locomotion","MoveAnimSpeed","AttackAnimSpeed"})controller.AddParameter(new AnimatorControllerParameter{name=name,type=AnimatorControllerParameterType.Float,defaultFloat=name=="Locomotion"?0:1});
        controller.AddParameter("HitX",AnimatorControllerParameterType.Float);controller.AddParameter("HitZ",AnimatorControllerParameterType.Float);
        var tree=new BlendTree{name="Locomotion",blendParameter="Locomotion",blendType=BlendTreeType.Simple1D,useAutomaticThresholds=false};AssetDatabase.AddObjectToAsset(tree,controller);
        tree.AddChild(clips["WalkBackwards"],-1);tree.AddChild(clips["IdleBreathe"],0);tree.AddChild(clips["WalkForward"],1);tree.AddChild(clips["WalkForward"],2);
        var idle=sm.AddState("Locomotion");idle.motion=tree;idle.speedParameter="MoveAnimSpeed";idle.speedParameterActive=true;sm.defaultState=idle;
        Action<string,string,AnimationClip,bool,bool> action=(trigger,name,clip,attack,terminal)=>
        {
            controller.AddParameter(trigger,AnimatorControllerParameterType.Trigger);var state=sm.AddState(name);state.motion=clip;
            if(attack){state.tag="Attack";state.speedParameter="AttackAnimSpeed";state.speedParameterActive=true;}
            var enter=sm.AddAnyStateTransition(state);enter.hasExitTime=false;enter.duration=.06f;enter.canTransitionToSelf=false;enter.AddCondition(AnimatorConditionMode.If,0,trigger);
            if(!terminal){var exit=state.AddTransition(idle);exit.hasExitTime=true;exit.exitTime=1f;exit.duration=.08f;}
        };
        int index=0;foreach(var row in plan["entries"])action("Attack"+(++index),"Attack_"+index,clips[(string)row["id"]],true,false);
        action("GotHit","Get_hit",clips["GetHitFront"],false,false);action("Death","Death",clips["Death"],false,true);action("IdleBreak","Idle_break",clips["IdleLookAround"],false,false);
        // Directly addressable native states for preparation, movement and presentation review.
        foreach(var pair in clips){var state=sm.AddState("Material_"+pair.Key);state.motion=pair.Value;}
        Save(controller);return controller;
    }
    static Material RenderMaterial(Material source,Dictionary<Material,Material> cache)
    {
        if(source==null)return null;if(cache.TryGetValue(source,out var existing))return existing;
        var shader=Shader.Find("Universal Render Pipeline/Lit")??throw new InvalidOperationException("URP Lit missing.");
        var material=new Material(shader){name=source.name+"_Crustaspikan"};
        foreach(var pair in new[]{new[]{"_BaseMap","_BaseMap"},new[]{"_MainTex","_BaseMap"},new[]{"_BumpMap","_BumpMap"}})
            if(source.HasProperty(pair[0])&&source.GetTexture(pair[0])!=null)material.SetTexture(pair[1],source.GetTexture(pair[0]));
        material.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white);
        material.SetFloat("_Smoothness",source.HasProperty("_Glossiness")?source.GetFloat("_Glossiness"):.3f);
        if(material.GetTexture("_BumpMap")!=null)material.EnableKeyword("_NORMALMAP");
        string path=Root+"/Render/"+material.name+".mat";Folder(Root+"/Render");AssetDatabase.CreateAsset(material,path);created.Add(path);cache.Add(source,material);return material;
    }
    static void Rock(EnemyBossMaterialCollection collection)
    {
        var primitive=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        try
        {
            var mesh=Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);mesh.name="CrustaspikanBoulder";
            var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]*=2f*(1f+.08f*Mathf.Sin(vertices[i].x*17f+vertices[i].y*13f+vertices[i].z*7f));mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=Root+"/Render/Boulder.asset";AssetDatabase.CreateAsset(mesh,path);created.Add(path);collection.boulderMesh=mesh;
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="CrustaspikanBoulder"};material.SetColor("_BaseColor",new Color(.28f,.24f,.2f));material.SetFloat("_Smoothness",.12f);
            path=Root+"/Render/Boulder.mat";AssetDatabase.CreateAsset(material,path);created.Add(path);collection.boulderMaterial=material;
        }
        finally{Object.DestroyImmediate(primitive);}
    }
    public static void BakeTelegraphProfiles(EnemyBossMaterialCollection collection)
    {
        var library=Resources.Load<EnemyTelegraphVisualLibrary>("Enemies/Balance/EnemyTelegraphVisualLibrary");
        if(library?.Nova==null)throw new InvalidOperationException("Approved nova source missing.");
        collection.radialFillProfile=ReadRadialProfile(library.Nova.GetComponentsInChildren<ParticleSystem>(true).First(p=>p.name.Contains("fill_add_soft")).GetComponent<ParticleSystemRenderer>().mesh);
        collection.radialBorderProfile=ReadRadialProfile(library.Nova.GetComponentsInChildren<ParticleSystem>(true).First(p=>p.name.Contains("border_add_soft")).GetComponent<ParticleSystemRenderer>().mesh);
        Save(collection);
    }
    static float[] ReadRadialProfile(Mesh source)
    {
        using(var meshes=MeshUtility.AcquireReadOnlyMeshData(source))
        using(var vertices=new NativeArray<Vector3>(source.vertexCount,Allocator.Temp))
        using(var uv=new NativeArray<Vector2>(source.vertexCount,Allocator.Temp))
        {
            meshes[0].GetVertices(vertices);meshes[0].GetUVs(0,uv);var profile=new float[11];
            for(int ring=0;ring<=10;ring++)
            {int found=-1;for(int i=0;i<uv.Length;i++)if(Mathf.Abs(uv[i].y-ring*.1f)<.0001f){found=i;break;}profile[ring]=found>=0?new Vector2(vertices[found].x,vertices[found].y).magnitude:ring*.1f;}
            return profile;
        }
    }
    public static string Apply(string planPath,string outputDirectory)
    {
        Idle();string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Project).FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        planPath=Path.GetFullPath(planPath);outputDirectory=Path.GetFullPath(outputDirectory);
        if(!planPath.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!outputDirectory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Private authoring and artifact paths required.");
        var plan=JObject.Parse(File.ReadAllText(planPath));
        if((string)plan["schema"]!="overburst.boss.material-authoring.v1"||plan["entries"].Count()!=16||plan["nativeMotionNames"].Count()!=37||plan["rootMotionExcluded"].Count()!=16)throw new ArgumentException("Complete non-RM native material plan required.");
        if(AssetDatabase.IsValidFolder(Root)||File.Exists(Path.Combine(Project,Root+".meta")))throw new InvalidOperationException("Target already exists; inspect it before rebuilding.");
        var seed=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Seed);
        if(seed?.IsValid!=true||!seed.ActorPrefab.IsAuthoringValid)throw new InvalidOperationException("Valid production actor seed required.");
        string vendorPath=AssetDatabase.FindAssets("Crustaspikan t:GameObject").Select(AssetDatabase.GUIDToAssetPath).Single(p=>p.EndsWith("/Prefab/Crustaspikan.prefab",StringComparison.Ordinal));
        string vendorRoot=vendorPath.Substring(0,vendorPath.LastIndexOf("/Prefab/",StringComparison.Ordinal));
        var sourceHashes=new JObject();foreach(string path in Directory.GetFiles(Path.Combine(Project,vendorRoot),"*",SearchOption.AllDirectories))sourceHashes[path.Substring(Project.Length+1).Replace('\\','/')]=Hash(path);
        var before=Scenes();created.Clear();Directory.CreateDirectory(outputDirectory);Scene preview=default;GameObject actorRoot=null;
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            Folder(Root);var originals=new Dictionary<string,AnimationClip>();var clips=new Dictionary<string,AnimationClip>();
            foreach(string name in plan["nativeMotionNames"].Values<string>())
            {var source=Clip(vendorRoot+"/FBX Files/Crustaspikan@"+name+".fbx");originals.Add(name,source);clips.Add(name,Runtime(source,name,name.StartsWith("Walk")||name=="IdleBreathe"));}
            var controller=Controller(clips,plan);var collection=Asset<EnemyBossMaterialCollection>("BMC_Crustaspikan");BakeTelegraphProfiles(collection);var abilities=new List<EnemyAbilityDefinition>();var materials=new List<EnemyBossAttackMaterial>();
            int index=0;foreach(var row in plan["entries"].OfType<JObject>())
            {
                string id=(string)row["id"];var delivery=(EnemyBossMaterialDelivery)Enum.Parse(typeof(EnemyBossMaterialDelivery),(string)row["delivery"]);
                var strikeRows=row["strikes"].ToArray();var ability=Asset<EnemyAbilityDefinition>("Abilities/"+id);
                ability.Configure("Crustaspikan_"+id,"Attack"+(++index),20f/strikeRows.Length,(float)row["range"],(float)strikeRows[0]["radius"],360f,.2f,0f,(float)strikeRows[0]["impact"],(float)row["seconds"],1f,false,
                    delivery==EnemyBossMaterialDelivery.Melee?EnemyAbilityExecutionMode.AreaSlam:EnemyAbilityExecutionMode.Projectile,3f,true,(float)row["seconds"]);
                ability.ConfigureAdditionalHits(strikeRows.Skip(1).Select(s=>(float)s["impact"]).ToArray());
                Set(ability,"telegraphedStrongAttack",p=>p.boolValue=delivery==EnemyBossMaterialDelivery.Melee);Set(ability,"parryable",p=>p.boolValue=delivery==EnemyBossMaterialDelivery.Melee);
                Set(ability,"minimumRecoveryTime",p=>p.floatValue=.3f);Save(ability);abilities.Add(ability);
                var material=Asset<EnemyBossAttackMaterial>("Attacks/"+id);material.materialId="Crustaspikan_"+id;material.displayName=(string)row["label"];material.ability=ability;
                material.originalClip=originals[id];material.runtimeClip=clips[id];material.delivery=delivery;
                material.strikes=strikeRows.Select(s=>new EnemyBossMaterialStrike{contactStart=(float)s["contactStart"],impact=(float)s["impact"],contactEnd=(float)s["contactEnd"],shape=(GroundIndicatorShape)Enum.Parse(typeof(GroundIndicatorShape),(string)s["shape"]),
                    localOrigin=new Vector3((float)s["localOrigin"][0],(float)s["localOrigin"][1],(float)s["localOrigin"][2]),yaw=(float)s["yaw"],radius=(float)s["radius"],innerRadius=(float)s["innerRadius"],angle=(float)s["angle"],width=(float)s["width"],length=(float)s["length"],minimumHeight=(float)s["minimumHeight"],maximumHeight=(float)s["maximumHeight"],timingBasis=(string)s["timingBasis"]}).ToArray();
                material.tracksTargetDuringWindup=(bool)row["tracksTargetDuringWindup"];
                material.muzzleBone=AssetDatabase.LoadAssetAtPath<GameObject>(vendorPath).GetComponentsInChildren<Transform>(true).Single(t=>t.name.Replace(" ","")=="Crustaspikan_Head").name;material.muzzleOffset=Vector3.zero;
                material.projectileRadius=delivery==EnemyBossMaterialDelivery.Boulder?.8f:(float)strikeRows[0]["radius"];material.assemblyNotes=(string)row["assemblyNotes"];
                if(!material.IsValid)throw new InvalidOperationException("Invalid material: "+id);Save(material);materials.Add(material);
            }
            collection.attacks=materials.ToArray();collection.motions=plan["nativeMotionNames"].Values<string>().Select(id=>new EnemyBossMaterialCollection.Motion{id=id,sourcePath=AssetDatabase.GetAssetPath(originals[id]),sourceGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(originals[id])),source=originals[id],runtime=clips[id],state="Material_"+id,preparation=id=="UnearthRock"||id.Contains("WithRock")})
                .Concat(plan["rootMotionExcluded"].Values<string>().Select(id=>new EnemyBossMaterialCollection.Motion{id=id,rootMotionVariant=true,sourcePath=vendorRoot+"/FBX Files/Crustaspikan@"+id+".fbx",sourceGuid=AssetDatabase.AssetPathToGUID(vendorRoot+"/FBX Files/Crustaspikan@"+id+".fbx")})).ToArray();
            var set=Asset<EnemyAbilitySet>("Abilities/AllAttacks");set.Configure("CrustaspikanMaterials",abilities.ToArray());Save(set);
            var animation=Asset<EnemyAnimationProfile>("AnimationProfile");animation.Configure("CrustaspikanMaterials",controller,clips["IdleBreathe"],clips["WalkForward"],clips["WalkForward"],materials.Select(m=>m.runtimeClip).ToArray(),clips["GetHitFront"],clips["Death"],clips.Values.ToArray(),collection.motions.Where(m=>m.rootMotionVariant).Select(m=>m.sourcePath).ToArray());Save(animation);
            var movement=Asset("Movement",seed.MovementProfile);movement.Configure("CrustaspikanMaterials",2f,90f,2f);movement.ConfigureAnimationReferenceSpeeds(2f,2f);movement.ConfigureBackpedalAnimationReferenceSpeed(1.5f);movement.ConfigureTurnAnimation(0f,0f);Save(movement);
            var behavior=Asset("Behavior",seed.BehaviorProfile);Set(behavior,"profileId",p=>p.stringValue="CrustaspikanMaterials");Set(behavior,"preferredApproachDistance",p=>p.floatValue=5f);Set(behavior,"playTauntOnAlert",p=>p.boolValue=false);Save(behavior);
            var grade=Asset<EnemyGradeProfile>("Grade");grade.Configure("CrustaspikanMaterialBoss","Boss material review",EnemyGradeType.Boss,1f,1f,1f,1f,1f);Save(grade);
            var variant=Asset<EnemyVariantProfile>("Variant");variant.Configure("CrustaspikanMaterials","Native body",Vector3.one,Color.white);variant.ConfigureRuntimeModifiers(Vector3.one,Vector3.one,1f,1f,1f,1f);Save(variant);
            var vendor=AssetDatabase.LoadAssetAtPath<GameObject>(vendorPath);var species=Asset("Species",seed.Species);species.Configure("CrustaspikanMaterials","Crustaspikan",vendor,animation,set);species.ConfigureRuntime(EnemyCombatRole.Vanguard,6000f,movement,behavior,1f);Save(species);
            var definition=Asset("Definition",seed);definition.ConfigureIdentity("CrustaspikanMaterials","Crustaspikan · 공격 재료");definition.ConfigureRuntime(animation,set,behavior,movement,seed.AiPreset,EnemySquadParticipationMode.Independent);
            var phase=Asset<EnemyBossPhaseDefinition>("ReviewPhase");phase.Configure("MaterialReview","공격 재료 검토",1f,set);Save(phase);
            var boss=Asset<EnemyBossDefinition>("ReviewBoss");boss.Configure("CrustaspikanMaterialReview","Crustaspikan",new[]{phase});Save(boss);
            var combat=Asset<EnemyBossCombatProfile>("ReviewCombat");combat.patterns=materials.Select(m=>new EnemyBossCombatProfile.Pattern{patternId=m.materialId,displayName=m.displayName,ability=m.ability,phaseMask=1}).ToArray();combat.parryGain=0f;combat.heavyHitGain=0f;combat.phaseBehaviors=new[]{behavior};Save(combat);
            preview=EditorSceneManager.NewPreviewScene();actorRoot=Object.Instantiate(seed.ActorPrefab.gameObject);SceneManager.MoveGameObjectToScene(actorRoot,preview);actorRoot.SetActive(false);actorRoot.name="PF_CrustaspikanMaterials";actorRoot.transform.localScale=Vector3.one;
            var actor=actorRoot.GetComponent<EnemyActor>();var visual=actor.VisualRoot;var oldAnimator=actor.Animator;var oldScale=visual.Find("Authored model scale");var oldAnimatedRoot=oldAnimator.transform;
            var scaled=new GameObject("Authored model scale").transform;scaled.SetParent(visual,false);scaled.localScale=Vector3.one*(float)plan["modelScale"];
            var model=(GameObject)PrefabUtility.InstantiatePrefab(vendor,preview);model.transform.SetParent(scaled,false);model.transform.localPosition=Vector3.up*((float)plan["modelYOffset"]/(float)plan["modelScale"]);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var missing in model.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("Missing Prefab")).ToArray())
            {
                var split=AssetDatabase.LoadAssetAtPath<GameObject>(vendorRoot+"/FBX Files/SM_CrustaspikanWeakPointSplit.fbx");if(split==null)throw new InvalidOperationException("Native weak point split missing.");
                var repaired=Object.Instantiate(split,missing.parent,false);repaired.name="SM_CrustaspikanWeakPointSplit";repaired.transform.localPosition=missing.localPosition;repaired.transform.localRotation=missing.localRotation;repaired.transform.localScale=missing.localScale;repaired.SetActive(false);Object.DestroyImmediate(missing.gameObject);
            }
            var renderCache=new Dictionary<Material,Material>();foreach(var r in model.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(m=>RenderMaterial(m,renderCache)).ToArray();Rock(collection);
            var animator=model.GetComponentInChildren<Animator>(true);animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.fireEvents=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach(var component in actorRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(component==null)throw new InvalidOperationException("Missing actor script.");var so=new SerializedObject(component);var p=so.GetIterator();
                while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference)
                {if(p.objectReferenceValue==oldAnimator)p.objectReferenceValue=animator;else if(p.objectReferenceValue==oldScale)p.objectReferenceValue=scaled;else if(p.objectReferenceValue==oldAnimatedRoot)p.objectReferenceValue=animator.transform;}
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            for(int i=visual.childCount-1;i>=0;i--)if(visual.GetChild(i)!=scaled)Object.DestroyImmediate(visual.GetChild(i).gameObject);
            foreach(var c in model.GetComponentsInChildren<Collider>(true))c.enabled=false;foreach(var rb in model.GetComponentsInChildren<Rigidbody>(true)){rb.isKinematic=true;rb.useGravity=false;}
            foreach(var t in actorRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("Enemy");
            var capsule=actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>();capsule.radius=1.8f;capsule.height=7.8f;capsule.center=Vector3.up*3.92f;
            var target=actor.GetComponent<CombatTarget>();target.ConfigureVolume(capsule.center,1.8f,7.8f);target.ConfigureHurtVolume(Vector3.up*4.52f,2.3f,9f);
            actor.Anchors.Find("AttackPoint").localPosition=new Vector3(0,1f,3f);actor.Anchors.Find("HitVfxPoint").localPosition=Vector3.up*3f;actor.Anchors.Find("HpBarAnchor").localPosition=Vector3.up*9.3f;
            Ref(actor,"definition",definition);actor.Identity.SetDefinition(definition);Ref(actor.Movement,"profile",movement);Ref(actor.AI,"behaviorProfile",behavior);Ref(actor.AI,"squadPursuitPreset",seed.AiPreset);Ref(actor.Melee,"abilitySet",set);Ref(actor.AbilityController,"abilitySet",set);
            Set(actor.Melee,"attackTriggers",p=>{p.arraySize=abilities.Count;for(int i=0;i<abilities.Count;i++)p.GetArrayElementAtIndex(i).stringValue=abilities[i].AnimatorTrigger;});
            var executor=actorRoot.AddComponent<EnemyBossMaterialExecutor>();executor.Configure(collection);Set(actor.AbilityController,"executors",p=>{p.arraySize=1;p.GetArrayElementAtIndex(0).objectReferenceValue=executor;});
            var director=actorRoot.AddComponent<EnemyBossCombatDirector>();director.Configure(combat);var phaseController=actorRoot.GetComponent<EnemyBossPhaseController>();phaseController.Configure(boss,actor,actor.Health,actor.AbilityController);Ref(actor,"bossPhaseController",phaseController);
            actorRoot.GetComponent<EnemyMovementReaction>().ConfigureVisualReactionRoot(scaled);actorRoot.GetComponent<EnemyVisualRootGuard>().Configure(animator.transform);Ref(actorRoot.GetComponent<EnemyDeathPresentation>(),"visualRoot",null);
            CombatImpactFeelBuilder.ConfigureActor(actor,definition);actorRoot.SetActive(true);
            string actorPath=Root+"/PF_CrustaspikanMaterials.prefab";var saved=PrefabUtility.SaveAsPrefabAsset(actorRoot,actorPath,out bool success);if(!success)throw new IOException("Prefab save failed.");created.Add(actorPath);
            definition.ConfigureComposition(species,grade,variant,saved.GetComponent<EnemyActor>());Save(definition);
            var catalog=Asset<EnemyCatalog>("Catalog");catalog.Configure(new[]{definition});Save(catalog);collection.actorDefinition=definition;collection.catalog=catalog;Save(collection);
            if(!definition.IsValid||!definition.ActorPrefab.IsAuthoringValid||!catalog.Validate(out var message))throw new InvalidOperationException("Saved actor contract invalid.");
            foreach(var pair in sourceHashes.Properties())if(Hash(Path.Combine(Project,pair.Name))!=(string)pair.Value)throw new InvalidOperationException("Supplier source changed: "+pair.Name);
            if(!JToken.DeepEquals(before,Scenes()))throw new InvalidOperationException("Open scene state changed.");
            var hashes=new JObject();foreach(string path in created){hashes[path]=Hash(Path.Combine(Project,path));if(File.Exists(Path.Combine(Project,path+".meta")))hashes[path+".meta"]=Hash(Path.Combine(Project,path+".meta"));}
            File.WriteAllText(Path.Combine(outputDirectory,"apply-result.json"),new JObject{["status"]="APPLIED",["attacks"]=16,["strikeCount"]=18,["playableMotions"]=37,["excludedRM"]=16,["sourcePreserved"]=true,["scenesBefore"]=before,["scenesAfter"]=Scenes(),["planSha256"]=Hash(planPath),["sourceHashes"]=sourceHashes,["assetHashes"]=hashes}.ToString());
            return CollectionPath;
        }
        catch(Exception error)
        {
            for(int i=created.Count-1;i>=0;i--)AssetDatabase.DeleteAsset(created[i]);
            File.WriteAllText(Path.Combine(outputDirectory,"failure.json"),new JObject{["status"]="FAILED_OWN_ASSETS_ROLLED_BACK",["error"]=error.ToString(),["created"]=JArray.FromObject(created)}.ToString());throw;
        }
        finally{if(actorRoot!=null)Object.DestroyImmediate(actorRoot);if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);AssetDatabase.AllowAutoRefresh();}
    }
}
