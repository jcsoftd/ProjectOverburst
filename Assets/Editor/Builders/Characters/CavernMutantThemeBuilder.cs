using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using static MonsterThemeCombatBuilder;

public static class CavernMutantThemeBuilder
{
    public const string ThemeId="CavernMutants";
    private sealed class Spec
    {
        public int tier,theme=0,number=0; public string name; public string[] attacks;
        public Spec(string n,int t,params string[] a){name=n;tier=t;attacks=a;}
    }
    private static readonly Spec[] Specs={
        new Spec("Ceratoferox",0,"BiteAttack","ClawsAttackLeft","ClawsAttackRight"),
        new Spec("Cephalonops",0,"BiteAttackForward"),
        new Spec("Gasterobrach",1,"AttackLeft","AttackRight","AttackSmash"),
        new Spec("Limadon",1,"BiteAttack","FeelerConcentratedShot"),
        new Spec("Gorhorrid",1,"SmashAttackLeft","SmashAttackRight","TongueAttack"),
        new Spec("Ursacetus",2,"LeftHandAttack","RightHandAttack","2HandsSmashAttack")};
    private static readonly string[] Ids={ThemeId};
    private static readonly string[] Labels={"암굴 변이 군락"};
    private static readonly Color[] Colors={new Color(.42f,.76f,.65f)};

    [MenuItem("OVERBURST/Enemies/Themes/Build Cavern Mutants")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
        // 2026-10-01: 기존 액터는 보존하고 없는 액터만 만든다(MonsterThemeAuthoringPolicy). 새 액터에 필요한 템플릿(MonsterThemeTemplate)이
        // 없으면 폴더·등급·재질·프리셋을 만들기 전에 멈춘다.
        var template=MonsterThemeTemplate.LoadActor();
        var sourceDef=MonsterThemeTemplate.LoadSource();
        MonsterThemeAuthoringPolicy.RequireTemplateBeforeWrites(Specs.Select(s=>Ids[s.theme]+"_"+s.name),template,sourceDef);
        Folder(Root);
        foreach(string f in new[]{"Actors","Definitions","Species","Grades","Animations","Abilities","Movement","Behavior","Presets","Tables","Materials"})Folder(Root+"/"+f);
        var touched=new List<Object>();
        var normal=MonsterThemeAuthoringPolicy.Grade("Grades/Normal","ThemeNormal","일반",EnemyGradeType.Normal,touched);
        var elite=MonsterThemeAuthoringPolicy.Grade("Grades/Elite","ThemeElite","정예",EnemyGradeType.Elite,touched);
        var variant=MonsterThemeTemplate.LoadDefaultVariant();
        var presets=new EnemyAiPreset[1];var signals=new Material[1];
        for(int i=0;i<1;i++)
        {
            presets[i]=MonsterThemeAuthoringPolicy.Preset("Presets/"+Ids[i],sourceDef,"Theme_"+Ids[i],Labels[i],touched);
            signals[i]=MonsterThemeAuthoringPolicy.SignalMaterial("Materials/"+Ids[i],Colors[i],touched);
        }
        var definitions=new List<EnemyDefinition>();var created=new List<EnemyDefinition>();
        foreach(var spec in Specs)
        {
            string id=Ids[spec.theme]+"_"+spec.name;
            var existing=MonsterThemeAuthoringPolicy.FindExisting(id);
            if(existing!=null){definitions.Add(existing);continue;}
            MonsterThemeAuthoringPolicy.RequireTemplate(template,sourceDef,id);
            var display=CreateDisplay(spec.name);
            try {
            AnimationClip Clip(string name) => display.clips.FirstOrDefault(c=>string.Equals(c.name,name,StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(id+" missing clip "+name);
            var idle=display.clips.First(c=>c.name=="Idle"||c.name=="IdleBreathe");var walk=display.clips.FirstOrDefault(c=>new[]{"WalkForward","CrawlForward","Walk"}.Any(n=>string.Equals(n,c.name,StringComparison.OrdinalIgnoreCase)));
            if(walk==null)throw new InvalidOperationException(id+" lacks ground locomotion");
            var run=display.clips.FirstOrDefault(c=>(c.name=="Run"||c.name=="RunForward"))??walk;
            var hit=Clip("GetHitFront");var death=Clip("Death");
            var attacks=spec.attacks.Select(Clip).ToArray();
            var optional=display.clips.Where(c=>!c.name.EndsWith("_RM") && !c.name.StartsWith("Fly") && !c.name.StartsWith("Swim")
                && !attacks.Contains(c) && c!=idle && c!=walk && c!=run && c!=hit && c!=death).ToArray();
            var controller=Controller(id,idle,walk,run,hit,death,attacks,display.clips);
            var animation=Asset<EnemyAnimationProfile>("Animations/"+id);
            animation.Configure(id,controller,idle,walk,run,attacks,hit,death,optional,display.clips.Where(c=>c.name.EndsWith("_RM")).Select(AssetDatabase.GetAssetPath).Distinct().ToArray());
            float footprint=spec.tier==0?1.45f:spec.tier==1?2.6f:4.9f, height=spec.tier==0?1.15f:spec.tier==1?2.15f:3.6f;
            float sizeFactor=spec.tier==0?SmallTierSizeMultiplier:spec.tier==2?EliteTierSizeMultiplier:1f;
            float scale=Mathf.Min(footprint/Mathf.Max(display.displaySize.x,display.displaySize.z),height/display.displaySize.y)*sizeFactor;
            Vector3 size=display.displaySize*scale;
            float radius=Mathf.Clamp(Mathf.Max(size.x,size.z)/sizeFactor*.22f,.22f,1.1f)*sizeFactor;
            float bodyHeight=Mathf.Max(radius*2,size.y*.88f);
            var movement=Clone(sourceDef.MovementProfile,"Movement/"+id);
            float speed=(spec.tier==0?2.3f:spec.tier==1?1.9f:1.5f)*Mathf.Min(1f,sizeFactor);
            movement.Configure(id,speed,spec.tier==2?200:420,speed,1.6f,2.2f);
            float walkReference=ReferenceSpeed(display.clips,walk,scale,speed);
            float runReference=run==walk?walkReference:ReferenceSpeed(display.clips,run,scale,speed*1.6f);
            movement.ConfigureAnimationReferenceSpeeds(walkReference,runReference);
            movement.ConfigureKnockbackReductionPercent(spec.tier==2?55:spec.tier==1?20:0);
            movement.ConfigureCrowdWeight(spec.tier==2?4:spec.tier==1?1.8f:1);
            movement.ConfigureHitWeight(MonsterThemeWeightBuilder.Resolve(spec.tier==0?EnemyThemeTier.Small:spec.tier==2?EnemyThemeTier.Elite:EnemyThemeTier.Medium));
            var behavior=Clone(sourceDef.BehaviorProfile,"Behavior/"+id);
            Set(behavior,"profileId",id);Set(behavior,"recoveryDuration",spec.tier==0?.28f:spec.tier==1?.45f:.75f);
            Set(behavior,"attackTurnCooldown",spec.tier==0?.8f:1.15f);Set(behavior,"dodgeLungeChance",0f);
            Set(behavior,"preferredApproachDistance",radius+.8f*sizeFactor);Set(behavior,"preferredMinDistance",radius+.45f*sizeFactor);
            Set(behavior,"playTauntOnAlert",spec.tier>0 && display.clips.Any(c=>c.name=="Roar"||c.name=="Roar1"||c.name=="Taunt"));
            var abilities=new List<EnemyAbilityDefinition>();
            for(int a=0;a<attacks.Length;a++)
            {
                string name=attacks[a].name;
                bool projectile=name.Contains("Spit")||name.Contains("Shot")||name.Contains("Projectile");
                bool charge=name.Contains("Jump")||name.Contains("Dash");
                bool gas=name=="2HandsSmashAttack";bool combo=name.StartsWith("2Hit");
                var mode=projectile?EnemyAbilityExecutionMode.Projectile:charge?EnemyAbilityExecutionMode.Charge:gas?EnemyAbilityExecutionMode.AreaSlam:EnemyAbilityExecutionMode.MeleeArc;
                var ability=Asset<EnemyAbilityDefinition>("Abilities/"+id+"_"+name);
                float impact=combo?.3f:charge?.62f:projectile?.52f:.43f;
                float damage=spec.tier==0?5:spec.tier==1?10:18;
                float range=projectile?8:name=="TongueAttack"?3.6f:radius+1.25f*sizeFactor;
                float duration=attacks[a].length;
                ability.Configure(id+"_"+name,"Attack"+(a+1),damage,range,projectile?radius/sizeFactor+.95f:name=="TongueAttack"?3.6f:radius+.95f*sizeFactor,gas?360:name=="TongueAttack"?45:125,
                    charge?5:projectile?4:spec.tier==2?2.8f:1.6f,duration*impact,impact,duration*.95f,
                    charge?.45f:projectile?.6f:1,false,mode,1.5f,true,duration);
                ability.ConfigureAdditionalHits(combo?new[]{.66f}:Array.Empty<float>());
                ability.ConfigureUsePolicy(projectile?2.8f:charge && spec.number!=24?1.8f:0,0);
                abilities.Add(ability);
            }
            var abilitySet=Asset<EnemyAbilitySet>("Abilities/"+id+"_Set");abilitySet.Configure(id,abilities.ToArray());
            var species=Asset<EnemySpeciesDefinition>("Species/"+id);
            string displayName = EnemyDisplayNames.Resolve(id, display.displayName);
            species.Configure(id,displayName,AssetDatabase.LoadAssetAtPath<GameObject>(display.sourcePath),animation,abilitySet);
            species.ConfigureRuntime(spec.tier==0?EnemyCombatRole.Swarm:spec.name=="Limadon"?EnemyCombatRole.Ranged:spec.tier==1?EnemyCombatRole.Vanguard:EnemyCombatRole.Bruiser,
                spec.tier==0?38:spec.tier==1?110:330,movement,behavior,spec.tier==0?1:spec.tier==1?3:12);
            var definition=Asset<EnemyDefinition>("Definitions/"+id);definition.ConfigureIdentity(id,displayName);
            var participation=spec.tier==2?EnemySquadParticipationMode.Independent:EnemySquadParticipationMode.SquadMember;
            definition.ConfigureRuntime(animation,abilitySet,behavior,movement,presets[spec.theme],participation);
            var prefab=BuildActor(template,display,id,definition,scale,size,radius,bodyHeight,sizeFactor,controller,abilitySet,movement,behavior,presets[spec.theme],participation,signals[spec.theme],Colors[spec.theme]);
            definition.ConfigureComposition(species,spec.tier==2?elite:normal,variant,prefab);
            MonsterMixedSquadBuilder.ApplyDefinition(definition,spec.name=="Limadon"?EnemyTacticalRole.RangedHold:EnemyTacticalRole.MeleePressure);
            definitions.Add(definition);created.Add(definition);
            foreach(var asset in new Object[]{animation,movement,behavior,abilitySet,species,definition})EditorUtility.SetDirty(asset);
            foreach(var ability in abilities)EditorUtility.SetDirty(ability);
            if(spec.name=="Limadon") {Set(behavior,"preferredMinDistance",2.8f);Set(behavior,"preferredApproachDistance",4.5f);}
            } finally {Object.DestroyImmediate(display.gameObject);}
        }
        var catalog=MonsterThemeAuthoringPolicy.LoadOrCreate<EnemyCatalog>("Catalog",out _);
        MonsterThemeAuthoringPolicy.MergeCatalog(catalog,definitions,touched);
        for(int i=0;i<1;i++)
        {
            int theme=i;var table=MonsterThemeAuthoringPolicy.LoadOrCreate<EnemyThemeTable>("Tables/"+Ids[i],out bool tableCreated);
            // 기본 가중치는 이 빌더가 새로 붙이는 항목에만 쓴다. 기존 항목의 등급·가중치는 테이블이 원본이다.
            var generated=Specs.Select((s,index)=>new {s,index}).Where(x=>x.s.theme==theme)
                .Select(x=>new EnemyThemeTable.Entry{definition=definitions[x.index],tier=(EnemyThemeTier)x.s.tier,weight=new[]{3f,2f,4f,2f,3f,1f}[x.index]}).ToArray();
            MonsterThemeAuthoringPolicy.MergeTable(table,tableCreated,Ids[i],Labels[i],catalog,Colors[i],generated,touched);
            MonsterThemeAuthoringPolicy.AppendPresetRoster(presets[i],generated.Where(e=>e.tier!=EnemyThemeTier.Elite && created.Contains(e.definition)).Select(e=>e.definition.ActorPrefab.gameObject),touched);
            if(!table.Validate(out string error))throw new InvalidOperationException(error);
        }
        MonsterThemeAuthoringPolicy.Save(touched);
        if(created.Count>0)
        {
            AssetDatabase.SaveAssets();
            MonsterThemeLocomotionBuilder.ApplyCreated(created.Select(d=>d.EnemyId).ToArray());
            FinalizeAuthoredContent(created);
        }
        Debug.Log("[MonsterThemeCombat] Cavern actors created="+created.Count+" preserved="+(definitions.Count-created.Count)+"; existing tuning untouched.");
    }

    // 2026-10-01: 이번 실행에서 새로 만든 동굴 액터에만 제작 기본 타격 시점·혀 공격 반경·연출을 적용한다.
    // 기존 액터의 hitNormalizedTime·hitRadius는 공격 타이밍 조정(09-30) 이후 값이 원본이라 건드리지 않는다.
    // hitDelay는 파생값이라 항상 공격 길이 × 첫 타격 시점으로 맞춘다. 공격은 배열 순서가 아니라 공격 이름으로 찾는다.
    public static void FinalizeAuthoredContent(IReadOnlyCollection<EnemyDefinition> createdDefinitions)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
        float[][] contacts={new[]{.36f,.44f,.43f},new[]{.33f},new[]{.35f,.36f,.48f},new[]{.43f,.50f},new[]{.36f,.44f,.30f},new[]{.38f,.46f,.49f}};
        var touched=new List<Object>();
        for(int i=0;i<Specs.Length;i++)
        {
            string id=ThemeId+"_"+Specs[i].name;
            var d=createdDefinitions?.FirstOrDefault(c=>c!=null && c.EnemyId==id);
            if(d==null)continue;
            for(int a=0;a<d.AbilitySet.Count;a++)
            {
                var ability=d.AbilitySet.GetAbility(a);
                string attack=ability.AbilityId.StartsWith(id+"_",StringComparison.Ordinal)?ability.AbilityId.Substring(id.Length+1):null;
                int k=attack!=null?Array.IndexOf(Specs[i].attacks,attack):-1;
                if(k<0)throw new InvalidOperationException("Authored contact has no matching attack: "+ability.AbilityId);
                Set(ability,"hitNormalizedTime",contacts[i][k]);Set(ability,"hitDelay",ability.AttackAnimationDuration*contacts[i][k]);
                if(ability.AbilityId.EndsWith("_TongueAttack"))Set(ability,"hitRadius",2.5f);
                EditorUtility.SetDirty(ability);touched.Add(ability);
            }
            var path=AssetDatabase.GetAssetPath(d.ActorPrefab);var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor=root.GetComponent<EnemyActor>();
                if(Specs[i].name=="Gorhorrid")foreach(var renderer in actor.VisualRoot.GetComponentsInChildren<Renderer>(true))
                    if(renderer.name.Contains("_WeakPoint"))renderer.enabled=false;
                CombatImpactFeelBuilder.ConfigureActor(actor,d);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        MonsterThemeAuthoringPolicy.Save(touched);
    }

    private static MonsterShowcaseActor CreateDisplay(string name)
    {
        const string vendor="Assets/ThirdParty/01_비인간캐릭터/";
        string source=name=="Cephalonops"?vendor+"Protofactor 1/Monster Full Pack Vol 2/Monster Pack Vol 8/Cephalonops/Prefab/Cephalonops.prefab"
            :name=="Limadon"?vendor+"Protofactor 1/Monster Full Pack Vol 2/Monster Pack Vol 12/Limadon/Prefab/Limadon.prefab"
            :name=="Gorhorrid"?vendor+"Protofactor 1/Monster Full Pack Vol 2/Monster Pack Vol 7/Gorhorrid/Prefabs/Gorhorrid.prefab"
            :vendor+"Protofactor/Sci Fi/Sci Fi Characters Mega Pack Vol 2/Sci Fi Creatures Vol 2/"+name+"/Prefab/"+name+".prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(source)==null)throw new InvalidOperationException(source);
        string folder=System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(source)).Replace('\\','/');
        var root=new GameObject("Author "+name);root.SetActive(false);
        var display=root.AddComponent<MonsterShowcaseActor>();display.enabled=false;display.displayName=name;display.sourcePath=source;
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(source),root.transform);
        display.animator=model.GetComponentInChildren<Animator>(true);
        display.clips=AssetDatabase.FindAssets("t:Model",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath)
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
        var idle=display.clips.First(c=>c.name=="Idle"||c.name=="IdleBreathe");
        idle.SampleAnimation(display.animator.gameObject,0);
        Bounds bounds=default;bool found=false;
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(!renderer.enabled)continue;var mesh=new Mesh();renderer.BakeMesh(mesh);
            foreach(var vertex in mesh.vertices) {var p=renderer.transform.TransformPoint(vertex);if(!found){bounds=new Bounds(p,Vector3.zero);found=true;}else bounds.Encapsulate(p);}
            Object.DestroyImmediate(mesh);
        }
        if(!found || bounds.size.y<.01f)throw new InvalidOperationException("No physical model bounds: "+name);
        model.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        display.displaySize=bounds.size;
        return display;
    }
}
