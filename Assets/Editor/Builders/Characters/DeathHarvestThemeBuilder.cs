using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using static MonsterThemeCombatBuilder;

// Local authoring tool. Supplier packages stay under ThirdParty and are never modified.
public static class DeathHarvestThemeBuilder
{
    public const string ThemeId = "DeathHarvest";
    private const string Base = "Assets/ThirdParty/01_비인간캐릭터/DeathHarvest";
    private const string Doll = "Assets/ThirdParty/03_애니메이션/KillerDollAnimations/Art/Animations";
    private const string Rake = Base + "/TheRake/Prefab/";
    private const string Bones = Base + "/SkeletonHumanoid/Skeleton Army/Prefabs/Skeleton/";
    private const string Knight = Base + "/DeathKnight2/Prefab/";
    private const string Reaper = Base + "/ReaperBoss/REAPER/PREFABS/";
    private static readonly Color Accent = new Color(.42f, .69f, .74f);
    private const float SmallVisualScale = .9f;
    private const float MediumVisualScale = .82f;
    private const float RunMultiplier = 1.45f;

    private sealed class Spec
    {
        public string key, label, source, library, idle, walk, run, back, hit, death, idleBreak, dodge;
        public string[] attacks;
        public EnemyThemeTier tier;
        public float weight;
        public Spec(string key, string label, string source, string library, EnemyThemeTier tier, float weight,
            string idle, string walk, string run, string back, string hit, string death, string idleBreak,
            string dodge, params string[] attacks)
        {
            this.key=key;this.label=label;this.source=source;this.library=library;this.tier=tier;
            this.weight=weight;this.idle=idle;this.walk=walk;this.run=run;this.back=back;
            this.hit=hit;this.death=death;this.idleBreak=idleBreak;this.dodge=dodge;
            this.attacks=attacks;
        }
    }

    private static readonly Spec[] Specs =
    {
        new Spec("RakeSkulker","창백한 숲갈퀴",Rake+"The Rake Skin 1.prefab","Doll",EnemyThemeTier.Small,3,
            "Idle01","WalkForwardFastDeadLegs","RunForward04","WalkBackFastDeadLegs","StaggerBack01","Death01","Idle07","DodgeForward",
            "AttackInPlace01","AttackForward01","AttackForwardHandStandKicks01","AttackInPlacePowerSpin"),
        new Spec("RakeStalker","뒤틀린 숲갈퀴",Rake+"The Rake Skin 2.prefab","Doll",EnemyThemeTier.Small,2,
            "Idle03","WalkForward03","RunForward02","WalkBack01","StaggerForward01","Death02","Idle06","DodgeLeft",
            "AttackInPlace02","AttackForward03","AttackForward06","AttackInPlaceHandStandSpinKick"),
        new Spec("BoneAsh","잿빛 해골",Bones+"SK_Skeleton_BlackBlood Variant.prefab","Doll",EnemyThemeTier.Small,3,
            "Idle02","WalkForwardDeadLegs","RunForward01","WalkBackDeadLegs","StaggerBack02","Death01","Idle05","DodgeRight",
            "AttackInPlace03","AttackForward02","AttackForward04"),
        new Spec("BoneMoss","이끼 해골",Bones+"SK_Skeleton_SandMoss Variant.prefab","Doll",EnemyThemeTier.Small,2,
            "Idle04","WalkForward01","RunForward03","WalkBack01","StaggerBack01","Death02","Idle07","DodgeForward",
            "AttackInPlace01","AttackForward05","AttackForwardHandStandKicks02"),
        new Spec("RakeBrute","거목 갈퀴",Rake+"The Rake Skin 2.prefab","Doll",EnemyThemeTier.Medium,3,
            "Idle03","WalkForward02","RunForward01","WalkBack01","StaggerBack02","Death01","Idle06","DodgeForward",
            "AttackInPlacePowerSpin","AttackForward03","AttackForwardHandStandKicks02","AttackInPlace03"),
        new Spec("BoneWarden","묘지 파수 해골",Bones+"SK_Skeleton_BlackMoss Variant.prefab","Doll",EnemyThemeTier.Medium,3,
            "Idle03","WalkForward04","RunForward04","WalkBack01","StaggerForward01","Death02","Idle04","DodgeForward",
            "AttackForward02","AttackInPlace02","AttackForwardHandStandKicks02","AttackInPlaceHandStandSpinKick"),
        new Spec("DeathKnight","사령 기사",Knight+"DarkKnight2_skin2.prefab","Knight",EnemyThemeTier.Medium,3,
            "fightidle","walk1","run","walkback","gethit1","death1","Idle2",null,
            "attack1","attack2","Attack5","attack8"),
        new Spec("Reaper","영혼 수확자",Reaper+"REAPER_PBR.prefab","Reaper",EnemyThemeTier.Elite,1,
            "idle","floatForward","floatForward","floatBackwards","getHit","death","idleNoScythe",null,
            "scytheAttack1","scytheAttack2","scytheAttack3","scythe3HitCombo","castSpellA")
    };

    [MenuItem("OVERBURST/Enemies/Themes/Build Death Harvest")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
        Folder(Root);
        foreach(string part in new[]{"Actors","Definitions","Species","Grades","Animations","Abilities",
            "Movement","Behavior","Presets","Tables","Materials","Footfalls","Blood"})
            Folder(Root+"/"+part);
        Folder(Root+"/Materials/DeathHarvest");
        // 2026-10-01: 기존 액터는 보존하고 없는 액터만 만든다(MonsterThemeAuthoringPolicy). 템플릿은 새 액터를 만들 때만 필요하다.
        var template=AssetDatabase.LoadAssetAtPath<GameObject>(ProtofactorEnemyPilotBuilder.PrefabPaths[0]);
        var sourceDef=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(ProtofactorEnemyPilotBuilder.DefinitionPaths[0]);
        var touched=new List<Object>();
        var normal=MonsterThemeAuthoringPolicy.Grade("Grades/Normal","ThemeNormal","일반",EnemyGradeType.Normal,touched);
        var elite=MonsterThemeAuthoringPolicy.Grade("Grades/Elite","ThemeElite","정예",EnemyGradeType.Elite,touched);
        var variant=AssetDatabase.LoadAssetAtPath<EnemyVariantProfile>(ProtofactorEnemyPilotBuilder.DefaultVariantPath);
        var preset=MonsterThemeAuthoringPolicy.Preset("Presets/"+ThemeId,sourceDef,"Theme_"+ThemeId,"사령의 수확단",touched);
        var signal=MonsterThemeAuthoringPolicy.SignalMaterial("Materials/"+ThemeId,Accent,touched);
        var defs=new List<EnemyDefinition>();var created=new List<EnemyDefinition>();
        foreach(var spec in Specs)
        {
            string id=ThemeId+"_"+spec.key;
            var existing=MonsterThemeAuthoringPolicy.FindExisting(id);
            if(existing!=null){defs.Add(existing);continue;}
            MonsterThemeAuthoringPolicy.RequireTemplate(template,sourceDef,id);
            var built=BuildOne(spec,template,sourceDef,normal,elite,variant,preset,signal);
            defs.Add(built);created.Add(built);
        }
        var catalog=MonsterThemeAuthoringPolicy.LoadOrCreate<EnemyCatalog>("Catalog",out _);
        MonsterThemeAuthoringPolicy.MergeCatalog(catalog,defs,touched);
        var table=MonsterThemeAuthoringPolicy.LoadOrCreate<EnemyThemeTable>("Tables/"+ThemeId,out bool tableCreated);
        // spec.weight는 새로 붙는 항목의 기본 가중치다. 기존 항목의 등급·가중치는 테이블이 원본이다.
        var entries=Specs.Select((s,i)=>new EnemyThemeTable.Entry{definition=defs[i],tier=s.tier,weight=s.weight}).ToArray();
        MonsterThemeAuthoringPolicy.MergeTable(table,tableCreated,ThemeId,"사령의 수확단",catalog,Accent,entries,touched);
        if(!table.Validate(out string error)) throw new InvalidOperationException(error);
        MonsterThemeAuthoringPolicy.AppendPresetRoster(preset,
            entries.Where(e=>e.tier!=EnemyThemeTier.Elite && created.Contains(e.definition)).Select(e=>e.definition.ActorPrefab.gameObject),touched);
        MonsterThemeAuthoringPolicy.Save(touched);
        if(created.Count>0)AssetDatabase.SaveAssets();
        Debug.Log("[DeathHarvest] actors created="+created.Count+" preserved="+(defs.Count-created.Count)+"; existing tuning untouched.");
    }

    private static EnemyDefinition BuildOne(Spec spec,GameObject template,EnemyDefinition sourceDef,
        EnemyGradeProfile normal,EnemyGradeProfile elite,EnemyVariantProfile variant,EnemyAiPreset preset,Material signal)
    {
        string id=ThemeId+"_"+spec.key;
        AnimationClip Clip(string name) => spec.library=="Doll"?DollClip(name):OwnClip(spec.library,name);
        var idle=Clip(spec.idle);var walk=Clip(spec.walk);var run=Clip(spec.run);
        var back=Clip(spec.back);var hit=Clip(spec.hit);var death=Clip(spec.death);
        if(spec.library=="Knight")
        {
            idle=LoopKnightClip(idle,"Idle");
            walk=LoopKnightClip(walk,"Walk");
            run=LoopKnightClip(run,"Run");
            back=LoopKnightClip(back,"Back");
        }
        var idleBreak=Clip(spec.idleBreak);
        var dodge=spec.dodge!=null?Clip(spec.dodge):null;
        var attacks=spec.attacks.Select(Clip).ToArray();
        var extras=new[]{back,idleBreak,dodge}.Where(c=>c!=null).Distinct().ToArray();
        var controller=Controller(id,idle,walk,run,hit,death,attacks,extras);
        PatchController(controller,back,idleBreak,dodge);
        var animation=Asset<EnemyAnimationProfile>("Animations/"+id);
        animation.Configure(id,controller,idle,walk,run,attacks,hit,death,extras,Array.Empty<string>());
        var display=CreateDisplay(spec,idle);
        try
        {
            float footprint=spec.tier==EnemyThemeTier.Small?1.35f:spec.tier==EnemyThemeTier.Medium?2.35f:4.0f;
            float height=spec.tier==EnemyThemeTier.Small?1.55f:spec.tier==EnemyThemeTier.Medium?2.45f:3.8f;
            float factor=spec.tier==EnemyThemeTier.Small?SmallVisualScale:
                spec.tier==EnemyThemeTier.Medium?MediumVisualScale:EliteTierSizeMultiplier;
            float scale=Mathf.Min(footprint/Mathf.Max(display.displaySize.x,display.displaySize.z),
                height/display.displaySize.y)*factor;
            Vector3 size=display.displaySize*scale;
            float radius=Mathf.Clamp(Mathf.Max(size.x,size.z)*.28f,.30f,1.25f);
            float bodyHeight=Mathf.Max(radius*2,size.y*.9f);
            var movement=Clone(sourceDef.MovementProfile,"Movement/"+id);
            movement.name=id;
            float speed=spec.tier==EnemyThemeTier.Small?1.85f:spec.tier==EnemyThemeTier.Medium?1.45f:1.2f;
            movement.Configure(id,speed,spec.tier==EnemyThemeTier.Elite?190:390,speed,RunMultiplier,2.2f);
            movement.ConfigureAnimationReferenceSpeeds(ReferenceSpeed(new[]{walk},walk,scale,speed),
                ReferenceSpeed(new[]{run},run,scale,speed*RunMultiplier));
            movement.ConfigureKnockbackReductionPercent(spec.tier==EnemyThemeTier.Elite?55:spec.tier==EnemyThemeTier.Medium?25:0);
            movement.ConfigureCrowdWeight(spec.tier==EnemyThemeTier.Elite?4:spec.tier==EnemyThemeTier.Medium?1.8f:1f);
            movement.ConfigureHitWeight(MonsterThemeWeightBuilder.Resolve(spec.tier));
            var behavior=Clone(sourceDef.BehaviorProfile,"Behavior/"+id);
            behavior.name=id;
            Set(behavior,"profileId",id);
            Set(behavior,"recoveryDuration",spec.tier==EnemyThemeTier.Small?.30f:spec.tier==EnemyThemeTier.Medium?.48f:.80f);
            Set(behavior,"attackTurnCooldown",spec.tier==EnemyThemeTier.Small?.8f:1.1f);
            Set(behavior,"preferredApproachDistance",radius+.85f);
            Set(behavior,"preferredMinDistance",radius+.45f);
            Set(behavior,"playTauntOnAlert",false);
            Set(behavior,"repositionStyle",(int)EnemyRepositionStyle.Backpedal);
            Set(behavior,"dodgeLungeChance",0f);
            var abilities=BuildAbilities(id,spec,attacks,radius);
            var abilitySet=Asset<EnemyAbilitySet>("Abilities/"+id+"_Set");
            abilitySet.Configure(id,abilities);
            var species=Asset<EnemySpeciesDefinition>("Species/"+id);
            species.Configure(id,spec.label,AssetDatabase.LoadAssetAtPath<GameObject>(spec.source),animation,abilitySet);
            species.ConfigureRuntime(spec.tier==EnemyThemeTier.Small?EnemyCombatRole.Swarm:
                    spec.tier==EnemyThemeTier.Elite?EnemyCombatRole.Bruiser:EnemyCombatRole.Vanguard,
                spec.tier==EnemyThemeTier.Small?42:spec.tier==EnemyThemeTier.Medium?135:400,
                movement,behavior,spec.tier==EnemyThemeTier.Small?1:spec.tier==EnemyThemeTier.Medium?3:12);
            var definition=Asset<EnemyDefinition>("Definitions/"+id);
            definition.ConfigureIdentity(id,spec.label);
            var participation=spec.tier==EnemyThemeTier.Elite?EnemySquadParticipationMode.Independent:
                EnemySquadParticipationMode.SquadMember;
            definition.ConfigureRuntime(animation,abilitySet,behavior,movement,preset,participation);
            var actor=BuildActor(template,display,id,definition,scale,size,radius,bodyHeight,factor,controller,
                abilitySet,movement,behavior,preset,participation,signal,Accent);
            definition.ConfigureComposition(species,spec.tier==EnemyThemeTier.Elite?elite:normal,variant,actor);
            MonsterMixedSquadBuilder.ApplyDefinition(definition,EnemyTacticalRole.MeleePressure);
            FinishActor(spec,id,definition,size,radius,bodyHeight);
            BuildFootfall(spec,id,walk,run);
            foreach(var o in new Object[]{animation,movement,behavior,abilitySet,species,definition})EditorUtility.SetDirty(o);
            foreach(var a in abilities)EditorUtility.SetDirty(a);
            return definition;
        }
        finally {Object.DestroyImmediate(display.gameObject);}
    }

    private static EnemyAbilityDefinition[] BuildAbilities(string id,Spec spec,AnimationClip[] attacks,float radius)
    {
        var result=new List<EnemyAbilityDefinition>();
        for(int i=0;i<attacks.Length;i++)
        {
            var clip=attacks[i];
            bool spell=spec.library=="Reaper"&&clip.name.IndexOf("castSpell",StringComparison.OrdinalIgnoreCase)>=0;
            bool spin=clip.name.IndexOf("Spin",StringComparison.OrdinalIgnoreCase)>=0;
            bool combo=clip.name.IndexOf("Combo",StringComparison.OrdinalIgnoreCase)>=0;
            var mode=spell?EnemyAbilityExecutionMode.Projectile:spin?EnemyAbilityExecutionMode.AreaSlam:
                EnemyAbilityExecutionMode.MeleeArc;
            var ability=Asset<EnemyAbilityDefinition>("Abilities/"+id+"_"+i+"_"+clip.name);
            float damage=spec.tier==EnemyThemeTier.Small?5f:spec.tier==EnemyThemeTier.Medium?11f:19f;
            float impact=spin?.56f:spell?.52f:.44f;
            float range=spell?8f:spin?radius+1.8f:
                radius+(spec.tier==EnemyThemeTier.Elite?2f:1.3f);
            ability.Configure(id+"_"+i+"_"+clip.name,"Attack"+(i+1),damage,range,
                spin?radius+1.45f:radius+.9f,spin?360f:spell?70f:130f,
                spell?3.8f:spec.tier==EnemyThemeTier.Elite?2.8f:1.7f,
                clip.length*impact,impact,clip.length*.96f,spell?.6f:1f,
                false,mode,1.5f,true,clip.length);
            ability.ConfigureAdditionalHits(combo?new[]{.66f,.82f}:Array.Empty<float>());
            ability.ConfigureUsePolicy(spell?2.8f:0f,0);
            EditorUtility.SetDirty(ability);
            result.Add(ability);
        }
        return result.ToArray();
    }

    private static AnimationClip DollClip(string name)
    {
        string full="KillerDoll_"+name;
        string path=Doll+"/"+full+".FBX";
        var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c=>c.name==full);
        if(clip==null||!clip.isHumanMotion)throw new InvalidOperationException("Missing Humanoid clip: "+path);
        return clip;
    }

    private static AnimationClip OwnClip(string library,string name)
    {
        string folder=library=="Knight"?Base+"/DeathKnight2/Animations":Base+"/ReaperBoss/REAPER/FBX FILES";
        var clips=AssetDatabase.FindAssets("t:Model",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath)
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>()
            .Where(c=>!c.name.StartsWith("__preview__")).ToArray();
        var clip=clips.FirstOrDefault(c=>string.Equals(c.name,name,StringComparison.OrdinalIgnoreCase));
        if(clip==null)
            clip=clips.FirstOrDefault(c=>AssetDatabase.GetAssetPath(c).IndexOf("@"+name+".",StringComparison.OrdinalIgnoreCase)>=0);
        if(clip==null)throw new InvalidOperationException(library+" missing "+name+"; found "+string.Join(", ",clips.Select(c=>c.name)));
        return clip;
    }

    // The supplier's Generic locomotion clips do not loop. In a blend tree they
    // stop at the final pose after one cycle, so the actor slides while moving.
    // Keep the supplier FBX untouched and author project-owned looping copies.
    private static AnimationClip LoopKnightClip(AnimationClip source,string role)
    {
        string path=Root+"/Animations/DeathHarvest_DeathKnight_Loop"+role+".anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null)
        {
            clip=Object.Instantiate(source);
            clip.hideFlags=HideFlags.None;
            AssetDatabase.CreateAsset(clip,path);
        }
        else EditorUtility.CopySerialized(source,clip);
        clip.name="DeathHarvest_DeathKnight_Loop"+role;
        clip.hideFlags=HideFlags.None;
        clip.wrapMode=WrapMode.Loop;
        var settings=AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime=true;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    [MenuItem("OVERBURST/Enemies/Themes/Repair Death Harvest Knight Animation")]
    public static void RepairKnightLocomotion()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit Mode required.");
        const string id="DeathHarvest_DeathKnight";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/Animations/AC_"+id+".controller");
        var profile=AssetDatabase.LoadAssetAtPath<EnemyAnimationProfile>(Root+"/Animations/"+id+".asset");
        if(controller==null||profile==null)throw new InvalidOperationException("Knight animation assets are missing.");
        var idle=LoopKnightClip(OwnClip("Knight","fightidle"),"Idle");
        var walk=LoopKnightClip(OwnClip("Knight","walk1"),"Walk");
        var run=LoopKnightClip(OwnClip("Knight","run"),"Run");
        var back=LoopKnightClip(OwnClip("Knight","walkback"),"Back");
        var machine=controller.layers[0].stateMachine;
        var state=machine.states.Select(s=>s.state).First(s=>s.name=="Locomotion");
        var tree=(BlendTree)state.motion;
        var children=tree.children;
        for(int i=0;i<children.Length;i++)
        {
            if(Mathf.Approximately(children[i].threshold,-1f))children[i].motion=back;
            else if(Mathf.Approximately(children[i].threshold,0f))children[i].motion=idle;
            else if(Mathf.Approximately(children[i].threshold,1f))children[i].motion=walk;
            else if(Mathf.Approximately(children[i].threshold,2f))children[i].motion=run;
        }
        tree.children=children;
        var attacks=Enumerable.Range(0,profile.AttackClipCount).Select(profile.GetAttackClip).ToArray();
        var optional=Enumerable.Range(0,profile.OptionalClipCount).Select(profile.GetOptionalClip).ToArray();
        if(optional.Length>0)optional[0]=back;
        var excluded=Enumerable.Range(0,profile.ExcludedRootMotionClipCount)
            .Select(profile.GetExcludedRootMotionClipPath).ToArray();
        profile.Configure(id,controller,idle,walk,run,attacks,profile.Hit,profile.Death,optional,excluded);
        EditorUtility.SetDirty(tree);
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[DeathHarvest] Knight idle, walk, run, and backpedal now loop.");
    }

    private static void PatchController(AnimatorController controller,AnimationClip back,AnimationClip idleBreak,AnimationClip dodge)
    {
        var machine=controller.layers[0].stateMachine;
        var states=machine.states.Select(s=>s.state).ToArray();
        var loco=states.First(s=>s.name=="Locomotion");
        var tree=(BlendTree)loco.motion;
        var children=tree.children;
        int backIndex=Array.FindIndex(children,c=>Mathf.Approximately(c.threshold,-1f));
        if(backIndex>=0){children[backIndex].motion=back;tree.children=children;}
        var idleState=states.FirstOrDefault(s=>s.name=="Idle_break");
        if(idleState!=null)idleState.motion=idleBreak;
        if(dodge!=null)
        {
            if(!controller.parameters.Any(p=>p.name=="Dodge"))controller.AddParameter("Dodge",AnimatorControllerParameterType.Trigger);
            var state=states.FirstOrDefault(s=>s.name=="Dodge")??machine.AddState("Dodge");
            state.motion=dodge;
            var enter=machine.AddAnyStateTransition(state);enter.hasExitTime=false;enter.duration=.06f;
            enter.canTransitionToSelf=false;enter.AddCondition(AnimatorConditionMode.If,0,"Dodge");
            var exit=state.AddTransition(loco);exit.hasExitTime=true;exit.exitTime=.96f;exit.duration=.06f;
        }
        EditorUtility.SetDirty(tree);EditorUtility.SetDirty(controller);
    }

    private static MonsterShowcaseActor CreateDisplay(Spec spec,AnimationClip idle)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(spec.source);
        if(source==null)throw new InvalidOperationException("Missing prefab: "+spec.source);
        var root=new GameObject("Author "+spec.key);root.SetActive(false);
        var display=root.AddComponent<MonsterShowcaseActor>();
        display.enabled=false;display.displayName=spec.key;display.sourcePath=spec.source;
        var model=(GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);
        display.animator=model.GetComponentInChildren<Animator>(true);
        if(display.animator==null||display.animator.avatar==null||!display.animator.avatar.isValid)
            throw new InvalidOperationException("Invalid Avatar: "+spec.source);
        if(spec.library=="Doll"&&!display.animator.avatar.isHuman)
            throw new InvalidOperationException("Killer Doll requires Humanoid Avatar: "+spec.source);
        idle.SampleAnimation(display.animator.gameObject,0f);
        Bounds bounds=default;bool found=false;
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(!renderer.enabled)continue;
            var mesh=new Mesh();renderer.BakeMesh(mesh);
            foreach(var vertex in mesh.vertices)
            {
                Vector3 point=renderer.transform.TransformPoint(vertex);
                if(!found){bounds=new Bounds(point,Vector3.zero);found=true;}else bounds.Encapsulate(point);
            }
            Object.DestroyImmediate(mesh);
        }
        if(!found||bounds.size.y<.01f)throw new InvalidOperationException("No mesh bounds: "+spec.source);
        model.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        display.displaySize=bounds.size;
        return display;
    }

    private static void FinishActor(Spec spec,string id,EnemyDefinition definition,Vector3 size,float radius,float height)
    {
        string path=AssetDatabase.GetAssetPath(definition.ActorPrefab);
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var actor=root.GetComponent<EnemyActor>();
            var target=root.GetComponent<CombatTarget>();
            if(root.GetComponent<EnemyHitResponseCoordinator>()==null)
                root.AddComponent<EnemyHitResponseCoordinator>();
            if(target!=null)
            {
                var placement=root.GetComponent<CombatTargetVfxPlacement>()??
                    root.AddComponent<CombatTargetVfxPlacement>();
                Set(placement,"useAuthoredVolume",true);
                SetVector(placement,"localBodyCenter",new Vector3(0,height*.53f,0));
                Set(placement,"bodyRadius",radius);
                Set(placement,"bodyHeight",height);
                Set(placement,"useAuthoredHitVolume",true);
                SetVector(placement,"localHitCenter",new Vector3(0,height*.58f,0));
                Set(placement,"hitRadius",radius*.9f);
                Set(placement,"hitHeight",height*.85f);
                var blood=root.GetComponent<BloodHitTarget>()??root.AddComponent<BloodHitTarget>();
                Set(blood,"profile",BloodProfile(spec));
            }
            foreach(var renderer in actor.VisualRoot.GetComponentsInChildren<Renderer>(true))
            {
                var mats=renderer.sharedMaterials;
                for(int i=0;i<mats.Length;i++)mats[i]=UrpMaterial(mats[i]);
                renderer.sharedMaterials=mats;
            }
            CombatImpactFeelBuilder.ConfigureActor(actor,definition);
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    private static BloodHitProfile BloodProfile(Spec spec)
    {
        string key=spec.library=="Reaper"?"Spectral":spec.library=="Knight"?"Ash":
            spec.source.Contains("Skeleton")?"Bone":"Rake";
        string path=Root+"/Blood/"+key+".asset";
        var profile=AssetDatabase.LoadAssetAtPath<BloodHitProfile>(path);
        if(profile==null){profile=ScriptableObject.CreateInstance<BloodHitProfile>();AssetDatabase.CreateAsset(profile,path);}
        Color color=key=="Spectral"?new Color(.16f,.45f,.58f):key=="Ash"?new Color(.29f,.22f,.29f):
            key=="Bone"?new Color(.34f,.31f,.26f):new Color(.43f,.18f,.18f);
        profile.mainColor=color;
        profile.secondaryColor=color*.55f;profile.specularColor=color*1.25f;
        profile.specular=key=="Spectral"?.35f:.12f;
        profile.size=spec.tier==EnemyThemeTier.Elite?6.2f:spec.tier==EnemyThemeTier.Medium?5.0f:4.1f;
        EditorUtility.SetDirty(profile);return profile;
    }

    private static void SetVector(Object target,string property,Vector3 value)
    {
        var so=new SerializedObject(target);
        var field=so.FindProperty(property)??throw new InvalidOperationException(target.name+" missing "+property);
        field.vector3Value=value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Material UrpMaterial(Material source)
    {
        if(source==null||source.shader==null)return source;
        if(source.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal))return source;
        string key=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
        if(string.IsNullOrEmpty(key))key=source.name.Replace("/","_");
        string path=Root+"/Materials/DeathHarvest/"+key+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material!=null)return material;
        material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=source.name+"_URP"};
        if(source.HasProperty("_MainTex"))material.SetTexture("_BaseMap",source.GetTexture("_MainTex"));
        else if(source.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",source.GetTexture("_BaseMap"));
        if(source.HasProperty("_Color"))material.SetColor("_BaseColor",source.GetColor("_Color"));
        else if(source.HasProperty("_BaseColor"))material.SetColor("_BaseColor",source.GetColor("_BaseColor"));
        foreach(var pair in new[]{new[]{"_BumpMap","_BumpMap"},new[]{"_MetallicGlossMap","_MetallicGlossMap"},
            new[]{"_OcclusionMap","_OcclusionMap"},new[]{"_EmissionMap","_EmissionMap"}})
            if(source.HasProperty(pair[0]))material.SetTexture(pair[1],source.GetTexture(pair[0]));
        if(source.HasProperty("_Glossiness"))material.SetFloat("_Smoothness",source.GetFloat("_Glossiness"));
        if(source.HasProperty("_Metallic"))material.SetFloat("_Metallic",source.GetFloat("_Metallic"));
        if(material.GetTexture("_BumpMap")!=null)material.EnableKeyword("_NORMALMAP");
        if(material.GetTexture("_MetallicGlossMap")!=null)material.EnableKeyword("_METALLICSPECGLOSSMAP");
        if(source.name.IndexOf("cloth",StringComparison.OrdinalIgnoreCase)>=0
            ||source.name.IndexOf("glass",StringComparison.OrdinalIgnoreCase)>=0)
            material.SetFloat("_Cull",0);
        AssetDatabase.CreateAsset(material,path);
        return material;
    }

    private static void BuildFootfall(Spec spec,string id,AnimationClip walk,AnimationClip run)
    {
        if(spec.tier==EnemyThemeTier.Elite)return; // Reaper floats.
        var foot=Asset<EnemyFootfallProfile>("Footfalls/"+id);
        foot.ConfigureDetailed(id,walk,run,spec.tier==EnemyThemeTier.Small?EnemyHitWeight.Light:EnemyHitWeight.Standard,
            new[]{new EnemyFootfallContact(.12f,new Vector3(-.21f,0,.17f)),
                new EnemyFootfallContact(.62f,new Vector3(.21f,0,.17f))},
            new[]{new EnemyFootfallContact(.10f,new Vector3(-.25f,0,.22f)),
                new EnemyFootfallContact(.58f,new Vector3(.25f,0,.22f))});
        EditorUtility.SetDirty(foot);
    }
}
