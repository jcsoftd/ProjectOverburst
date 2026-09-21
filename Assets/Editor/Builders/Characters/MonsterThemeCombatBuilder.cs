using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MonsterThemeCombatBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/Enemies/Themes";
    private sealed class Spec
    {
        public int number,tier,theme;
        public string[] attacks;
        public Spec(int n,int t,int themeIndex,params string[] actions) {number=n;tier=t;theme=themeIndex;attacks=actions;}
    }
    private static readonly Spec[] Specs = {
        new Spec(31,0,0,"Bite","JumpBite"),new Spec(17,0,0,"BiteAttack","ClawsAttack","JumpBiteAttack"),
        new Spec(34,1,0,"ClawsAttack","JumpClawsAttack","Spit"),new Spec(3,1,0,"BiteAttack","ClawsAttack","JumpBiteAttack"),
        new Spec(29,2,0,"ClawAttackLeft","ClawAttackRight","DoubleClawsAttack","2HitComboAttackClawAttack","SpittersAttack"),
        new Spec(38,0,1,"BiteForwardAttack","JumpBiteAttack"),new Spec(40,0,1,"BiteForwardAttack","JumpBiteAttack"),
        new Spec(1,1,1,"Bite","JumpBite","ShootProjectile"),new Spec(24,1,1,"DashSpikeAttack","JumpClawsAttack","ElectroShot"),
        new Spec(21,2,1,"ClawsAttackLeft","ClawsAttackRight","2HitComboClawsAttack","JumpClawsAttack","SpitProjectiles","SacksSprayGasAttack"),
        new Spec(2,0,2,"BiteForward","JumpBite"),new Spec(8,1,2,"LeftClawsAttack","RightClawsAttack","2HitComboClawsAttack","JumpClawsAttack"),
        new Spec(42,1,2,"BiteAttack","ClawsAttackLeft","ClawsAttackRight","2HitComboClawsAttack","Spit"),
        new Spec(27,2,2,"BiteForward","JumpBite")
    };
    private static readonly string[] Ids = {"SpiderBrood","VenomBrood","PrimalHunt"};
    private static readonly string[] Labels = {"거미 부화 군락","독낭 외골격 군락","원시 포식자 무리"};
    private static readonly Color[] Colors = {new Color(.8f,.45f,1),new Color(1,.6f,.12f),new Color(1,.3f,.15f)};

    [MenuItem("OVERBURST/Enemies/Themes/Build Combat Tables")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
        var gallery=Object.FindFirstObjectByType<MonsterShowcaseGallery>();
        if(gallery==null || gallery.actors.Length!=47)throw new InvalidOperationException("Open the saved MonsterVol2_Showcase scene first.");
        if(gallery.gameObject.scene.isDirty)throw new InvalidOperationException("Save scene changes before content generation.");
        Folder(Root);
        foreach(string f in new[]{"Actors","Definitions","Species","Grades","Animations","Abilities","Movement","Behavior","Presets","Tables","Materials"})Folder(Root+"/"+f);
        var template=AssetDatabase.LoadAssetAtPath<GameObject>(ProtofactorEnemyPilotBuilder.PrefabPaths[0]);
        var sourceDef=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(ProtofactorEnemyPilotBuilder.DefinitionPaths[0]);
        if(template==null || sourceDef==null)throw new InvalidOperationException("Existing actor baseline is missing.");
        var normal=Asset<EnemyGradeProfile>("Grades/Normal");normal.Configure("ThemeNormal","일반",EnemyGradeType.Normal,1,1,1,1);
        var elite=Asset<EnemyGradeProfile>("Grades/Elite");elite.Configure("ThemeElite","정예",EnemyGradeType.Elite,1,1,1,1);
        var variant=AssetDatabase.LoadAssetAtPath<EnemyVariantProfile>(ProtofactorEnemyPilotBuilder.DefaultVariantPath);
        var presets=new EnemyAiPreset[3];var signals=new Material[3];
        for(int i=0;i<3;i++)
        {
            presets[i]=Clone(sourceDef.AiPreset,"Presets/"+Ids[i]);presets[i].ConfigureIdentity("Theme_"+Ids[i],Labels[i],Array.Empty<GameObject>());
            signals[i]=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+Ids[i]+".mat");
            if(signals[i]==null){signals[i]=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(signals[i],Root+"/Materials/"+Ids[i]+".mat");}
            signals[i].SetColor("_BaseColor",Colors[i]);EditorUtility.SetDirty(signals[i]);
        }
        var definitions=new List<EnemyDefinition>();
        foreach(var spec in Specs)
        {
            var display=gallery.actors[spec.number-1];string id=Ids[spec.theme]+"_"+display.displayName;
            AnimationClip Clip(string name) => display.clips.FirstOrDefault(c=>string.Equals(c.name,name,StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(id+" missing clip "+name);
            var idle=Clip("Idle");var walk=display.clips.FirstOrDefault(c=>new[]{"WalkForward","CrawlForward"}.Any(n=>string.Equals(n,c.name,StringComparison.OrdinalIgnoreCase)));
            if(walk==null)throw new InvalidOperationException(id+" lacks ground locomotion");
            var run=display.clips.FirstOrDefault(c=>c.name=="Run")??walk;
            var hit=Clip("GetHitFront");var death=Clip("Death");
            var attacks=spec.attacks.Select(Clip).ToArray();
            var optional=display.clips.Where(c=>!c.name.EndsWith("_RM") && !c.name.StartsWith("Fly") && !c.name.StartsWith("Swim")
                && !attacks.Contains(c) && c!=idle && c!=walk && c!=run && c!=hit && c!=death).ToArray();
            var controller=Controller(id,idle,walk,run,hit,death,attacks,display.clips);
            var animation=Asset<EnemyAnimationProfile>("Animations/"+id);
            animation.Configure(id,controller,idle,walk,run,attacks,hit,death,optional,display.clips.Where(c=>c.name.EndsWith("_RM")).Select(AssetDatabase.GetAssetPath).Distinct().ToArray());
            float footprint=spec.tier==0?1.45f:spec.tier==1?2.6f:4.9f, height=spec.tier==0?1.15f:spec.tier==1?2.15f:3.6f;
            float scale=Mathf.Min(footprint/Mathf.Max(display.displaySize.x,display.displaySize.z),height/display.displaySize.y);
            Vector3 size=display.displaySize*scale;
            float radius=Mathf.Clamp(Mathf.Max(size.x,size.z)*.22f,.22f,1.1f);
            float bodyHeight=Mathf.Max(radius*2,size.y*.88f);
            var movement=Clone(sourceDef.MovementProfile,"Movement/"+id);
            float speed=spec.tier==0?2.3f:spec.tier==1?1.9f:1.5f;
            movement.Configure(id,speed,spec.tier==2?200:420,speed,1.6f,2.2f);
            float walkReference=ReferenceSpeed(display.clips,walk,scale,speed);
            float runReference=run==walk?walkReference:ReferenceSpeed(display.clips,run,scale,speed*1.6f);
            movement.ConfigureAnimationReferenceSpeeds(walkReference,runReference);
            movement.ConfigureKnockbackReductionPercent(spec.tier==2?55:spec.tier==1?20:0);
            movement.ConfigureCrowdWeight(spec.tier==2?4:spec.tier==1?1.8f:1);
            var behavior=Clone(sourceDef.BehaviorProfile,"Behavior/"+id);
            Set(behavior,"profileId",id);Set(behavior,"recoveryDuration",spec.tier==0?.28f:spec.tier==1?.45f:.75f);
            Set(behavior,"attackTurnCooldown",spec.tier==0?.8f:1.15f);Set(behavior,"dodgeLungeChance",0f);
            Set(behavior,"preferredApproachDistance",radius+.8f);Set(behavior,"preferredMinDistance",radius+.45f);
            Set(behavior,"playTauntOnAlert",spec.tier>0 && display.clips.Any(c=>c.name=="Roar"||c.name=="Taunt"));
            var abilities=new List<EnemyAbilityDefinition>();
            for(int a=0;a<attacks.Length;a++)
            {
                string name=attacks[a].name;
                bool projectile=name.Contains("Spit")||name.Contains("Shot")||name.Contains("Projectile");
                bool charge=name.Contains("Jump")||name.Contains("Dash");
                bool gas=name.Contains("SprayGas");bool combo=name.StartsWith("2Hit");
                var mode=projectile?EnemyAbilityExecutionMode.Projectile:charge?EnemyAbilityExecutionMode.Charge:gas?EnemyAbilityExecutionMode.AreaSlam:EnemyAbilityExecutionMode.MeleeArc;
                var ability=Asset<EnemyAbilityDefinition>("Abilities/"+id+"_"+name);
                float impact=combo?.3f:charge?.62f:projectile?.52f:.43f;
                float damage=spec.tier==0?5:spec.tier==1?10:18;
                float range=projectile?8:charge?4.8f:radius+1.25f;
                float duration=attacks[a].length;
                ability.Configure(id+"_"+name,"Attack"+(a+1),damage,range,radius+.95f,gas?360:125,
                    charge?5:projectile?4:spec.tier==2?2.8f:1.6f,duration*impact,impact,duration*.95f,
                    charge?.45f:projectile?.6f:1,false,mode,1.5f,true,duration);
                ability.ConfigureAdditionalHits(combo?new[]{.66f}:Array.Empty<float>());
                ability.ConfigureUsePolicy(projectile?2.8f:charge && spec.number!=24?1.8f:0,0);
                abilities.Add(ability);
            }
            var abilitySet=Asset<EnemyAbilitySet>("Abilities/"+id+"_Set");abilitySet.Configure(id,abilities.ToArray());
            var species=Asset<EnemySpeciesDefinition>("Species/"+id);
            species.Configure(id,display.displayName,AssetDatabase.LoadAssetAtPath<GameObject>(display.sourcePath),animation,abilitySet);
            species.ConfigureRuntime(spec.tier==0?EnemyCombatRole.Swarm:spec.tier==1?EnemyCombatRole.Vanguard:EnemyCombatRole.Bruiser,
                spec.tier==0?38:spec.tier==1?150:500,movement,behavior,spec.tier==0?1:spec.tier==1?3:12);
            var definition=Asset<EnemyDefinition>("Definitions/"+id);definition.ConfigureIdentity(id,display.displayName);
            var participation=spec.tier==2?EnemySquadParticipationMode.Independent:EnemySquadParticipationMode.SquadMember;
            definition.ConfigureRuntime(animation,abilitySet,behavior,movement,presets[spec.theme],participation);
            var prefab=BuildActor(template,display,id,definition,scale,size,radius,bodyHeight,controller,abilitySet,movement,behavior,presets[spec.theme],participation,signals[spec.theme],Colors[spec.theme]);
            definition.ConfigureComposition(species,spec.tier==2?elite:normal,variant,prefab);
            definitions.Add(definition);
            foreach(var asset in new Object[]{animation,movement,behavior,abilitySet,species,definition})EditorUtility.SetDirty(asset);
            foreach(var ability in abilities)EditorUtility.SetDirty(ability);
        }
        var catalog=Asset<EnemyCatalog>("Catalog");catalog.Configure(definitions.ToArray());EditorUtility.SetDirty(catalog);
        for(int i=0;i<3;i++)
        {
            int theme=i;var table=Asset<EnemyThemeTable>("Tables/"+Ids[i]);
            var roster=Specs.Select((s,index)=>new {s,index}).Where(x=>x.s.theme==theme)
                .Select(x=>new EnemyThemeTable.Entry{definition=definitions[x.index],tier=(EnemyThemeTier)x.s.tier,weight=1}).ToArray();
            table.Configure(Ids[i],Labels[i],catalog,Colors[i],roster);
            presets[i].ConfigureIdentity("Theme_"+Ids[i],Labels[i],roster.Where(e=>e.tier!=EnemyThemeTier.Elite).Select(e=>e.definition.ActorPrefab.gameObject).ToArray());
            EditorUtility.SetDirty(table);EditorUtility.SetDirty(presets[i]);
            if(!table.Validate(out string error))throw new InvalidOperationException(error);
        }
        EditorUtility.SetDirty(normal);EditorUtility.SetDirty(elite);AssetDatabase.SaveAssets();
        Debug.Log("[MonsterThemeCombat] Created 14 actors / 3 tables using existing actor and squad runtime.");
    }

    private static EnemyActor BuildActor(GameObject template,MonsterShowcaseActor display,string id,EnemyDefinition definition,float scale,Vector3 size,float radius,float height,
        RuntimeAnimatorController controller,EnemyAbilitySet abilities,EnemyMovementProfile movement,EnemyBehaviorProfile behavior,EnemyAiPreset preset,EnemySquadParticipationMode participation,Material signal,Color color)
    {
        var root=Object.Instantiate(template);root.name="PF_"+id;root.SetActive(false);
        try
        {
            var actor=root.GetComponent<EnemyActor>();var visual=actor.VisualRoot;var oldAnimator=actor.Animator;
            var scaled=new GameObject("Authored model scale").transform;scaled.SetParent(visual,false);scaled.localScale=Vector3.one*scale;
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(display.sourcePath),scaled);
            model.transform.localPosition=display.transform.GetChild(0).localPosition+Vector3.up*(.02f/scale);
            model.transform.localRotation=display.transform.GetChild(0).localRotation;
            model.transform.localScale=display.transform.GetChild(0).localScale;
            var animator=model.GetComponentInChildren<Animator>(true);animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.fireEvents=false;
            foreach(var c in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(c==null)continue;var so=new SerializedObject(c);var p=so.GetIterator();
                while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue==oldAnimator)p.objectReferenceValue=animator;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            for(int i=visual.childCount-1;i>=0;i--)if(visual.GetChild(i)!=scaled)Object.DestroyImmediate(visual.GetChild(i).gameObject);
            foreach(var c in model.GetComponentsInChildren<Collider>(true))c.enabled=false;
            foreach(var rb in model.GetComponentsInChildren<Rigidbody>(true)){rb.isKinematic=true;rb.useGravity=false;}
            foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("Enemy");
            var capsule=actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>();capsule.radius=radius;capsule.height=height;capsule.center=Vector3.up*(height*.5f+.02f);
            actor.Anchors.Find("AttackPoint").localPosition=new Vector3(0,Mathf.Clamp(height*.5f,.65f,1.5f),radius+.35f);
            actor.Anchors.Find("HitVfxPoint").localPosition=Vector3.up*height*.55f;
            actor.Anchors.Find("HpBarAnchor").localPosition=Vector3.up*(Mathf.Max(size.y,height)+.3f);
            actor.Identity.SetDefinition(definition);Set(actor,"definition",definition);
            Set(actor.Movement,"profile",movement);Set(actor.AI,"behaviorProfile",behavior);Set(actor.AI,"squadPursuitPreset",preset);Set(actor.AI,"squadParticipationMode",(int)participation);
            Set(actor.Melee,"abilitySet",abilities);Set(actor.AbilityController,"abilitySet",abilities);
            Set(actor.AnimationBridge,"hitReactionAnimationSpeedMultiplier",1.15f);
            var special=root.AddComponent<EnemyThemeSpecialExecutor>();special.Configure(signal,color);
            var slam=root.AddComponent<EnemyAreaSlamAbilityExecutor>();slam.Configure(actor.Melee);
            var soAbilities=new SerializedObject(actor.AbilityController);var executors=soAbilities.FindProperty("executors");
            var all=root.GetComponents<EnemyAbilityExecutor>();executors.arraySize=all.Length;for(int i=0;i<all.Length;i++)executors.GetArrayElementAtIndex(i).objectReferenceValue=all[i];soAbilities.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<EnemyVisualRootGuard>().Configure(animator.transform);
            root.SetActive(true);
            var saved=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Actors/PF_"+id+".prefab");return saved.GetComponent<EnemyActor>();
        }
        finally {Object.DestroyImmediate(root);}
    }
    private static AnimatorController Controller(string id,AnimationClip idle,AnimationClip walk,AnimationClip run,AnimationClip hit,AnimationClip death,AnimationClip[] attacks,AnimationClip[] all)
    {
        string path=Root+"/Animations/AC_"+id+".controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(path);
        else foreach(var sub in AssetDatabase.LoadAllAssetsAtPath(path))if(sub!=controller)Object.DestroyImmediate(sub,true);
        var machine=new AnimatorStateMachine{name="Base Layer"};AssetDatabase.AddObjectToAsset(machine,controller);
        controller.layers=new[]{new AnimatorControllerLayer{name="Base Layer",defaultWeight=1,stateMachine=machine}};
        controller.parameters=Array.Empty<AnimatorControllerParameter>();
        foreach(string name in new[]{"Locomotion","MoveAnimSpeed","AttackAnimSpeed"})controller.AddParameter(new AnimatorControllerParameter{name=name,type=AnimatorControllerParameterType.Float,defaultFloat=name=="Locomotion"?0:1});
        var tree=new BlendTree{name="Locomotion",blendParameter="Locomotion",blendType=BlendTreeType.Simple1D,useAutomaticThresholds=false};AssetDatabase.AddObjectToAsset(tree,controller);
        var back=all.FirstOrDefault(c=>c.name.Equals("WalkBackwards",StringComparison.OrdinalIgnoreCase)||c.name=="CrawlBackwards")??walk;
        tree.AddChild(back,-1);tree.AddChild(idle,0);tree.AddChild(walk,1);tree.AddChild(run,2);
        var locomotion=machine.AddState("Locomotion");locomotion.motion=tree;locomotion.speedParameter="MoveAnimSpeed";locomotion.speedParameterActive=true;machine.defaultState=locomotion;
        for(int i=0;i<attacks.Length;i++)Action(controller,machine,locomotion,"Attack"+(i+1),"Attack_"+(i+1),attacks[i],true);
        controller.AddParameter("HitX",AnimatorControllerParameterType.Float);controller.AddParameter(new AnimatorControllerParameter{name="HitZ",type=AnimatorControllerParameterType.Float,defaultFloat=1});
        var hitTree=new BlendTree{name="Directional hit",blendParameter="HitX",blendParameterY="HitZ",blendType=BlendTreeType.FreeformDirectional2D};AssetDatabase.AddObjectToAsset(hitTree,controller);
        hitTree.AddChild(hit,Vector2.up);
        hitTree.AddChild(all.FirstOrDefault(c=>c.name=="GetHitBack")??hit,Vector2.down);
        hitTree.AddChild(all.FirstOrDefault(c=>c.name=="GetHitLeft")??hit,Vector2.left);
        hitTree.AddChild(all.FirstOrDefault(c=>c.name=="GetHitRight")??hit,Vector2.right);
        Action(controller,machine,locomotion,"GotHit","Get_hit",hitTree,false);
        var taunt=all.FirstOrDefault(c=>c.name=="Roar"||c.name=="Taunt");if(taunt!=null)Action(controller,machine,locomotion,"Taunt","Taunt",taunt,false);
        var idleBreak=all.FirstOrDefault(c=>c.name=="IdleAngry")??idle;Action(controller,machine,locomotion,"IdleBreak","Idle_break",idleBreak,false);
        Action(controller,machine,locomotion,"Death","Death",death,false,true);EditorUtility.SetDirty(controller);return controller;
    }
    private static void Action(AnimatorController controller,AnimatorStateMachine machine,AnimatorState locomotion,string trigger,string name,Motion clip,bool attack,bool terminal=false)
    {
        controller.AddParameter(trigger,AnimatorControllerParameterType.Trigger);var state=machine.AddState(name);state.motion=clip;
        if(attack){state.speedParameter="AttackAnimSpeed";state.speedParameterActive=true;}
        var enter=machine.AddAnyStateTransition(state);enter.hasExitTime=false;enter.duration=.06f;enter.canTransitionToSelf=false;enter.AddCondition(AnimatorConditionMode.If,0,trigger);
        if(!terminal){var exit=state.AddTransition(locomotion);exit.hasExitTime=true;exit.exitTime=.98f;exit.duration=.08f;}
    }
    private static float ReferenceSpeed(AnimationClip[] clips,AnimationClip clip,float scale,float fallback)
    {var rm=clips.FirstOrDefault(c=>c.name==clip.name+"_RM");float value=rm!=null?rm.averageSpeed.magnitude*scale:0;return value>.15f?value:fallback;}
    private static T Asset<T>(string relative) where T:ScriptableObject
    {string path=Root+"/"+relative+".asset";var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset==null){asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);}return asset;}
    private static T Clone<T>(T source,string relative) where T:ScriptableObject
    {var target=Asset<T>(relative);EditorUtility.CopySerialized(source,target);return target;}
    private static void Folder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
    private static void Set(Object obj,string property,object value)
    {
        var so=new SerializedObject(obj);var p=so.FindProperty(property)??throw new InvalidOperationException(obj.name+" missing "+property);
        if(value is Object reference)p.objectReferenceValue=reference;else if(value is string text)p.stringValue=text;else if(value is bool boolean)p.boolValue=boolean;else if(value is int integer)p.intValue=integer;else if(value is float number)p.floatValue=number;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
