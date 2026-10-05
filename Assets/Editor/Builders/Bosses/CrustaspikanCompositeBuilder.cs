using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class CrustaspikanCompositeBuilder
{
    public const string Root="Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanMaterials";
    public const string PatternPath=Root+"/Composite/BCP_Crustaspikan.asset";
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
    static T Asset<T>(string path) where T:ScriptableObject
    {var value=AssetDatabase.LoadAssetAtPath<T>(path);if(value==null){value=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(value,path);}return value;}
    public static string Build(string output)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating,"Idle Editor required.");
        Require(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"))&&string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")),"Unoccupied account required.");
        var allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        Require(Path.GetFullPath(output).StartsWith(allowed,StringComparison.OrdinalIgnoreCase),"Private output required.");Directory.CreateDirectory(output);
        string backup=Path.Combine(output,"Before");Directory.CreateDirectory(backup);
        var collection=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(Root+"/BMC_Crustaspikan.asset");Require(collection!=null,"Boss collection missing.");
        string actorPath=AssetDatabase.GetAssetPath(collection.actorDefinition.ActorPrefab);
        foreach(var path in new[]{Root+"/Attacks/SpitterShot1.asset",Root+"/Abilities/SpitterShot1.asset",Root+"/Attacks/SpitterShot2.asset",actorPath})
        {File.Copy(path,Path.Combine(backup,Path.GetFileName(Path.GetDirectoryName(path))+"_"+Path.GetFileName(path)),true);File.Copy(path+".meta",Path.Combine(backup,Path.GetFileName(Path.GetDirectoryName(path))+"_"+Path.GetFileName(path)+".meta"),true);}
        Folder(Root+"/Composite");Folder(Root+"/Composite/Visuals");Folder(Root+"/Composite/Materials");
        var blood=Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.ResourcePath);Require(blood?.sprays?.Length>=7,"Blood Effects Pack flow sources missing.");
        var smallA=Payload("Ceratoferox",12,.7f);var smallB=Payload("Cephalonops",12,.7f);var medium=Payload("Gasterobrach",1,1f);var elite=Payload("Ursacetus",1,1.2f);
        var sweep=collection.attacks.First(x=>x.runtimeClip.name=="SpitterShot1");var line=collection.attacks.First(x=>x.runtimeClip.name=="SpitterShot2");
        sweep.strikes=new[]{Strike(sweep,33,57,GroundIndicatorShape.Sector),Strike(sweep,68,94,GroundIndicatorShape.Sector)};
        line.strikes=new[]{Strike(line,74,106,GroundIndicatorShape.Rectangle)};
        sweep.muzzleOffset=line.muzzleOffset=new Vector3(-.55f,.35f,0f);
        sweep.assemblyNotes="좌→우 / 우→좌 5겹 피해 분사와 암굴 소형16+중형1 토출. 기본 계수·속도는 재료 tuning, 토출 세부는 BCP_Crustaspikan. 시험값은 추후 수정.";
        line.assemblyNotes="3겹 직선 피해 분사와 암굴 소형12 토출. 기본 계수·속도는 재료 tuning, 토출 세부는 BCP_Crustaspikan. 시험값은 추후 수정.";
        var ability=new SerializedObject(sweep.ability);ability.FindProperty("hitNormalizedTime").floatValue=sweep.strikes[0].impact;
        var extra=ability.FindProperty("additionalHitNormalizedTimes");extra.arraySize=1;extra.GetArrayElementAtIndex(0).floatValue=sweep.strikes[1].impact;
        ability.FindProperty("damage").floatValue=10f;ability.ApplyModifiedPropertiesWithoutUndo();
        if(sweep.tuning!=null && sweep.tuning.parries!=null && sweep.tuning.parries.Length>0 && sweep.tuning.parries.Length!=2)
            Array.Resize(ref sweep.tuning.parries,2);
        EditorUtility.SetDirty(sweep);EditorUtility.SetDirty(line);AssetDatabase.SaveAssetIfDirty(sweep.ability);AssetDatabase.SaveAssetIfDirty(sweep);AssetDatabase.SaveAssetIfDirty(line);
        var set=Asset<EnemyBossCompositePatternSet>(PatternPath);
        set.summonCatalog=Asset<EnemyCatalog>(Root+"/Composite/EC_CrustaspikanPayloads.asset");
        set.summonCatalog.Configure(new[]{smallA.definition,smallB.definition,medium.definition,elite.definition});
        EditorUtility.SetDirty(set.summonCatalog);AssetDatabase.SaveAssetIfDirty(set.summonCatalog);
        set.elite=elite;set.throwMaterial=collection.attacks.First(x=>x.runtimeClip.name=="ThrowRock");set.throwPayload=EnemyBossThrowPayload.Alternate;
        set.spitPatterns=new[]{
            new EnemyBossCompositePatternSet.Pattern{material=sweep,bloodSpray=Spray(blood,5,"Sweep"),sprayScale=1f,beamRadius=.75f,
                firstSweepStartYaw=-45f,firstSweepEndYaw=45f,reverseSecondSweep=true,
                emissions=new[]{Emission(sweep,36,0,smallA,8f),Emission(sweep,49,0,smallB,10f),Emission(sweep,80,1,medium,11f)}},
            new EnemyBossCompositePatternSet.Pattern{material=line,bloodSpray=Spray(blood,6,"Line"),sprayScale=.9f,beamRadius=.65f,
                firstSweepStartYaw=0f,firstSweepEndYaw=0f,
                emissions=new[]{Emission(line,77,0,smallA,8f,-10f),Emission(line,88,0,smallB,10f),Emission(line,98,0,smallA,9f,10f)}}
        };
        ConfigureDenseEmissions(set);ConfigureVomit(set);
        EditorUtility.SetDirty(set);AssetDatabase.SaveAssetIfDirty(set);Require(set.IsValid,"Composite authoring invalid.");
        var prefab=PrefabUtility.LoadPrefabContents(actorPath);
        try{
            var composite=prefab.GetComponent<EnemyBossCompositePatternExecutor>();if(composite==null)composite=prefab.AddComponent<EnemyBossCompositePatternExecutor>();composite.Configure(set);
            var controller=new SerializedObject(prefab.GetComponent<EnemyAbilityController>());var list=controller.FindProperty("executors");
            var previous=Enumerable.Range(0,list.arraySize).Select(i=>list.GetArrayElementAtIndex(i).objectReferenceValue).Where(x=>x!=null&&x!=composite).ToList();
            if(previous.Count==0)previous.Add(prefab.GetComponent<EnemyBossMaterialExecutor>());
            list.arraySize=previous.Count+1;list.GetArrayElementAtIndex(0).objectReferenceValue=composite;
            for(int i=0;i<previous.Count;i++)list.GetArrayElementAtIndex(i+1).objectReferenceValue=previous[i];controller.ApplyModifiedPropertiesWithoutUndo();
            Require(PrefabUtility.SaveAsPrefabAsset(prefab,actorPath)!=null,"Boss prefab save failed.");
        }finally{PrefabUtility.UnloadPrefabContents(prefab);}
        var result=new JObject{["status"]="APPLIED_NATIVE",["patternsValid"]=set.IsValid,["patternAsset"]=PatternPath,["prefab"]=actorPath,
            ["sweep"]=new JArray(sweep.strikes.Select(s=>new JObject{["startFrame"]=Mathf.RoundToInt(s.contactStart*sweep.runtimeClip.length*30f),["endFrame"]=Mathf.RoundToInt(s.contactEnd*sweep.runtimeClip.length*30f)})),
            ["line"]=new JArray(line.strikes.Select(s=>new JObject{["startFrame"]=Mathf.RoundToInt(s.contactStart*line.runtimeClip.length*30f),["endFrame"]=Mathf.RoundToInt(s.contactEnd*line.runtimeClip.length*30f)})),
            ["bloodSource"]=new JArray(blood.sprays[5].label,blood.sprays[6].label),["summons"]=new JArray(smallA.definition.DisplayName,smallB.definition.DisplayName,medium.definition.DisplayName,elite.definition.DisplayName),["play"]="NOT_RUN"};
        File.WriteAllText(Path.Combine(output,"apply-result.json"),result.ToString());return result.ToString();
    }
    static EnemyBossCompositePatternSet.Emission Emission(EnemyBossAttackMaterial material,int frame,int phase,EnemyBossCompositePatternSet.Payload payload,float distance,float yaw=0f,int count=1)
        =>new EnemyBossCompositePatternSet.Emission{normalizedTime=frame/(material.runtimeClip.length*30f),phase=phase,payload=payload,landingDistance=distance,yawOffset=yaw,count=count,scatter=1.25f};
    static void ConfigureDenseEmissions(EnemyBossCompositePatternSet set)
    {
        var sweep=set.spitPatterns.Single(p=>p.material.runtimeClip.name=="SpitterShot1");
        var line=set.spitPatterns.Single(p=>p.material.runtimeClip.name=="SpitterShot2");
        var payloads=set.spitPatterns.SelectMany(p=>p.emissions).Select(e=>e.payload).ToArray();
        var smallA=payloads.First(p=>p.definition.EnemyId=="CavernMutants_Ceratoferox");
        var smallB=payloads.First(p=>p.definition.EnemyId=="CavernMutants_Cephalonops");
        var medium=payloads.First(p=>p.definition.EnemyId=="CavernMutants_Gasterobrach");
        smallA.maximumAlive=smallB.maximumAlive=12;medium.maximumAlive=1;
        foreach(var payload in new[]{smallA,smallB,medium,set.elite}){payload.trajectory=EnemyBossPayloadTrajectory.Ballistic;payload.gravity=32f;}
        smallA.arcHeight=smallB.arcHeight=.22f;smallA.airPitch=smallB.airPitch=30f;
        medium.arcHeight=.3f;medium.airPitch=35f;set.elite.arcHeight=.55f;set.elite.airPitch=50f;set.elite.wakeSeconds=1.5f;
        sweep.emissions=new[]{
            Emission(sweep.material,36,0,smallA,8f,count:2),Emission(sweep.material,42,0,smallB,11f,count:2),
            Emission(sweep.material,49,0,smallA,13f,count:2),Emission(sweep.material,55,0,smallB,9f,count:2),
            Emission(sweep.material,72,1,smallA,11f,count:2),Emission(sweep.material,78,1,smallB,13f,count:2),
            Emission(sweep.material,80,1,medium,11f),Emission(sweep.material,84,1,smallA,9f,count:2),
            Emission(sweep.material,90,1,smallB,12f,count:2)};
        line.emissions=new[]{Emission(line.material,77,0,smallA,8f,-8f,3),Emission(line.material,84,0,smallB,11f,5f,3),
            Emission(line.material,91,0,smallA,14f,-5f,3),Emission(line.material,98,0,smallB,10f,8f,3)};
        set.authoringNotes="대량 분출 시험 구성. 분사1:5겹 혈흔,33–57F/68–94F,소형16+중형1(8묶음+중형). 분사2:3겹 혈흔,74–106F,소형12(3마리씩4묶음). 예약: 소형 각 종12,중형1,정예1、방 상한26。 토출/정예:Ballistic,중력32,최고점 상승 소형0.22/중형0.3/정예0.55m. 비행 시간은 출발·착지 높이와 중력으로 산출하며 flightSeconds는 TimedArc용. 속도에 따라 공중 기울기·착지 전 정렬,정예 기상1.5초. 피해/분사 판정창은 기존 값. 바위는 기존 TimedArc. 시험값 조절 가능.";
    }
    // Density changes preserve the boss prefab, models, motions, combat tuning, and payload visuals.
    public static string UpdateDensity(string output)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating,"Idle Editor required.");
        Require(string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)&&!IsolatedSavePlayGuard.RequiresAccountChoice
            &&string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"))
            &&string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")),"Unoccupied account required.");
        string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        Require(Path.GetFullPath(output).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&!File.Exists(Path.Combine(output,"apply-result.json")),"Fresh private output required.");
        var set=AssetDatabase.LoadAssetAtPath<EnemyBossCompositePatternSet>(PatternPath);Require(set!=null&&set.IsValid,"Existing patterns missing.");
        var encounter=AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>("Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan.asset");
        Require(encounter!=null&&encounter.composites==set&&!EditorUtility.IsDirty(encounter)&&!EditorUtility.IsDirty(set),"Saved matching encounter settings required.");
        Directory.CreateDirectory(output);string backup=Path.Combine(output,"Before");Directory.CreateDirectory(backup);
        foreach(var path in new[]{PatternPath,AssetDatabase.GetAssetPath(encounter)}.Concat(set.spitPatterns.SelectMany(p=>new[]{AssetDatabase.GetAssetPath(p.material),AssetDatabase.GetAssetPath(p.bloodSpray)})))
        {string name=Path.GetFileName(path);File.Copy(path,Path.Combine(backup,name),false);File.Copy(path+".meta",Path.Combine(backup,name+".meta"),false);}
        var blood=Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.ResourcePath);Require(blood?.sprays?.Length>=7,"Blood flow sources missing.");
        ConfigureDenseEmissions(set);ConfigureVomit(set);
        foreach(var pattern in set.spitPatterns){bool sweep=pattern.material.runtimeClip.name=="SpitterShot1";
            pattern.bloodSpray=Spray(blood,sweep?5:6,sweep?"Sweep":"Line");
            pattern.material.assemblyNotes=sweep?"5겹 왕복 피해 분사 + 암굴 소형16/중형1. 방출은 BCP_Crustaspikan에서 조절.":"3겹 직선 피해 분사 + 암굴 소형12. 방출은 BCP_Crustaspikan에서 조절.";
            EditorUtility.SetDirty(pattern.material);AssetDatabase.SaveAssetIfDirty(pattern.material);}
        encounter.maximumAdds=26;EditorUtility.SetDirty(encounter);AssetDatabase.SaveAssetIfDirty(encounter);
        EditorUtility.SetDirty(set);AssetDatabase.SaveAssetIfDirty(set);Require(set.IsValid&&encounter.Validate(out _),"Density configuration invalid.");
        var result=new JObject{["status"]="APPLIED_NATIVE",["maximumAdds"]=encounter.maximumAdds,
            ["patterns"]=new JArray(set.spitPatterns.Select(p=>new JObject{["clip"]=p.material.runtimeClip.name,
                ["layers"]=p.bloodSpray.transform.childCount,["particleSystems"]=p.bloodSpray.GetComponentsInChildren<ParticleSystem>(true).Length,
                ["monsters"]=p.emissions.Sum(e=>e.count),["waves"]=p.emissions.Length})),["play"]="NOT_RUN"};
        File.WriteAllText(Path.Combine(output,"apply-result.json"),result.ToString());return result.ToString();
    }
    static EnemyBossMaterialStrike Strike(EnemyBossAttackMaterial material,int start,int end,GroundIndicatorShape shape)
        =>new EnemyBossMaterialStrike{contactStart=start/(material.runtimeClip.length*30f),impact=start/(material.runtimeClip.length*30f),contactEnd=end/(material.runtimeClip.length*30f),
            shape=shape,radius=24f,width=1.5f,length=24f,angle=shape==GroundIndicatorShape.Sector?90f:360f,minimumHeight=0f,maximumHeight=10f,
            timingBasis="Native 30fps mouth-open motion; provisional composite window "+start+"–"+end+"F. One damage per physical target per sweep/line phase."};
    static EnemyBossCompositePatternSet.Payload Payload(string id,int limit,float wake)
    {
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/CavernMutants_"+id+".asset");Require(definition?.IsValid==true,"Invalid summon: "+id);
        string path=Root+"/Composite/Visuals/PF_Payload_"+id+".prefab";GameObject root=null;
        float height=1f;
        try{
            root=new GameObject("PF_Payload_"+id);root.SetActive(false);var source=definition.ActorPrefab;
            var model=Object.Instantiate(source.VisualRoot.gameObject,root.transform,false);model.name="Visual";model.transform.localScale=definition.ResolveRuntimeStats().VisualScale;
            foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(component);
            foreach(var collider in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
            foreach(var body in model.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(body);
            foreach(var animator in model.GetComponentsInChildren<Animator>(true)){animator.applyRootMotion=false;animator.fireEvents=false;animator.runtimeAnimatorController=null;animator.enabled=false;
                definition.AnimationProfile.Idle.SampleAnimation(animator.gameObject,0f);}
            Bounds bounds=default;bool found=false;
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)){
                var baked=new Mesh();try{renderer.BakeMesh(baked);var b=baked.bounds;var world=new Bounds(renderer.transform.TransformPoint(b.center),Vector3.Scale(b.size,renderer.transform.lossyScale));if(!found){bounds=world;found=true;}else bounds.Encapsulate(world);}finally{Object.DestroyImmediate(baked);}}
            Require(found,"Payload mesh missing: "+id);height=bounds.center.y-root.transform.position.y;model.transform.position-=bounds.center-root.transform.position;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))if(renderer is SkinnedMeshRenderer skin)skin.updateWhenOffscreen=true;
            Require(PrefabUtility.SaveAsPrefabAsset(root,path)!=null,"Payload save failed.");
        }finally{if(root!=null)Object.DestroyImmediate(root);}
        return new EnemyBossCompositePatternSet.Payload{definition=definition,flightVisual=AssetDatabase.LoadAssetAtPath<GameObject>(path),landingCenterHeight=Mathf.Max(.1f,height),wakeSeconds=wake,maximumAlive=limit};
    }
    static void ConfigureVomit(EnemyBossCompositePatternSet set)
    {
        set.eliteImpactKnockdown=true;
        foreach(var pattern in set.spitPatterns){
            bool sweep=pattern.material.runtimeClip.name=="SpitterShot1";
            pattern.sprayHalfAngle=sweep?24f:20f;pattern.spraySpeed=22f;pattern.sprayGravity=18f;
            pattern.beamRadius=sweep?.95f:.85f;
            foreach(var strike in pattern.material.strikes){strike.shape=GroundIndicatorShape.Sector;strike.radius=24f;strike.angle=sweep?140f:44f;}
            foreach(var emission in pattern.emissions){emission.scatter=3.2f;emission.yawScatter=sweep?18f:16f;
                emission.originScatter=.45f;emission.apexScatter=.35f;emission.launchJitterSeconds=.14f;}
            pattern.material.assemblyNotes=sweep
                ?"입에서 퍼지고 아래로 처지는 왕복 토사물 분사. 소형16+중형1을 불규칙한 위치·간격·중력 궤적으로 토출. 지면 예고140°·24m, 실제 이동 부채꼴 접촉은 타격창당1회."
                :"정면을 향해 퍼지는 토사물 분사. 소형12를 불규칙한 위치·간격·중력 궤적으로 토출. 지면 예고44°·24m, 실제 부채꼴 접촉은 타격창당1회.";
        }
        set.authoringNotes="왕복17 / 정면12 대량 토출. 액체와 몬스터는 중력 곡선을 사용한다. 방출 사건별 로컬 시드로 출발·시점·착지·기울기를 분산한다. 정예 착지 피해는 실제 HP 손실 뒤 기존 플레이어 넉다운·기상으로 연결한다. 수치는 조정용 시험값.";
    }
    public static string UpdateVomit(string output)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating,"Idle Editor required.");
        Require(!IsolatedSavePlayGuard.RequiresAccountChoice&&string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            &&string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            &&string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")),"Unoccupied account required.");
        string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        Require(Path.GetFullPath(output).StartsWith(allowed,StringComparison.OrdinalIgnoreCase),"Private output required.");Directory.CreateDirectory(output);
        var set=AssetDatabase.LoadAssetAtPath<EnemyBossCompositePatternSet>(PatternPath);Require(set!=null&&!EditorUtility.IsDirty(set),"Saved, unoccupied composite set required.");
        foreach(var pattern in set.spitPatterns)Require(!EditorUtility.IsDirty(pattern.material),"Attack material is being edited.");
        var blood=Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.ResourcePath);Require(blood?.sprays?.Length>=7,"Blood flow sources missing.");
        ConfigureVomit(set);
        foreach(var pattern in set.spitPatterns){bool sweep=pattern.material.runtimeClip.name=="SpitterShot1";
            pattern.bloodSpray=Spray(blood,sweep?5:6,sweep?"Sweep":"Line");EditorUtility.SetDirty(pattern.material);AssetDatabase.SaveAssetIfDirty(pattern.material);}
        Require(set.IsValid,"Vomit composite invalid.");EditorUtility.SetDirty(set);AssetDatabase.SaveAssetIfDirty(set);
        var result=new JObject{["status"]="APPLIED_NATIVE",["patternsValid"]=set.IsValid,["eliteImpactKnockdown"]=set.eliteImpactKnockdown,
            ["patterns"]=new JArray(set.spitPatterns.Select(p=>new JObject{["clip"]=p.material.runtimeClip.name,["fanHalfAngle"]=p.sprayHalfAngle,
                ["gravity"]=p.sprayGravity,["speed"]=p.spraySpeed,["count"]=p.emissions.Sum(e=>e.count),
                ["shape"]=p.material.strikes[0].shape.ToString(),["warningAngle"]=p.material.strikes[0].angle}))};
        File.WriteAllText(Path.Combine(output,"apply-result.json"),result.ToString());return result.ToString();
    }
    static GameObject Spray(BloodEffectsPackCatalog catalog,int index,string name)
    {
        var root=new GameObject("PF_CrustaspikanBlood"+name);root.SetActive(false);string path=Root+"/Composite/Visuals/PF_CrustaspikanBlood"+name+".prefab";
        try{
            int layers=name=="Sweep"?5:3;
            for(int i=0;i<layers;i++){
                var layer=Object.Instantiate(catalog.sprays[index].prefab,root.transform,false);layer.name="VomitLayer_"+(i+1);
                foreach(var nestedController in layer.GetComponentsInChildren<EnemyBossBloodSpray>(true))Object.DestroyImmediate(nestedController);
                layer.transform.localScale=Vector3.one;layer.transform.localPosition=Vector3.zero;
                layer.transform.localRotation=Quaternion.Euler(0f,Mathf.Lerp(-17.3f,17.3f,i/(float)(layers-1)),0f);layer.SetActive(true);
                var systems=layer.GetComponentsInChildren<ParticleSystem>(true);
                for(int j=0;j<systems.Length;j++){
                    var ps=systems[j];ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.transform.SetParent(layer.transform,false);ps.transform.localPosition=Vector3.zero;ps.transform.localRotation=Quaternion.identity;ps.transform.localScale=Vector3.one;
                    var main=ps.main;main.loop=true;main.duration=.62f;main.startDelay=0f;main.playOnAwake=false;main.stopAction=ParticleSystemStopAction.None;
                    main.startSpeed=new ParticleSystem.MinMaxCurve(18f,24f);main.startLifetime=new ParticleSystem.MinMaxCurve(.5f,1.05f);main.gravityModifier=0f;
                    main.maxParticles=96;main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Shape;
                    main.startSize3D=true;bool clump=j%3==0;
                    main.startSizeX=new ParticleSystem.MinMaxCurve(clump?.65f:.14f,clump?1.8f:.45f);
                    main.startSizeY=new ParticleSystem.MinMaxCurve(clump?.9f:.2f,clump?1.9f:.65f);main.startSizeZ=1f;
                    main.startColor=Color.white;
                    var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();
                    gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                        new[]{new GradientAlphaKey(1f,0),new GradientAlphaKey(.9f,.75f),new GradientAlphaKey(0f,1)});color.color=gradient;
                    main.startRotation=new ParticleSystem.MinMaxCurve(-Mathf.PI,Mathf.PI);
                    var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=7.6f;shape.radius=.28f;shape.scale=new Vector3(1f,.32f,1f);
                    var emission=ps.emission;emission.enabled=true;
                    var pulse=new AnimationCurve(new Keyframe(0f,.3f),new Keyframe(.1f,1f),new Keyframe(.22f,.35f),new Keyframe(.4f,.9f),new Keyframe(.6f,.18f),new Keyframe(.82f,.8f),new Keyframe(1f,.3f));
                    emission.rateOverTime=new ParticleSystem.MinMaxCurve(clump?60f:44f,pulse);
                    emission.SetBursts(new[]{new ParticleSystem.Burst(0f,(short)3,(short)7),new ParticleSystem.Burst(.18f,(short)2,(short)6),new ParticleSystem.Burst(.4f,(short)3,(short)8)});
                    var velocity=ps.velocityOverLifetime;velocity.enabled=false;var limit=ps.limitVelocityOverLifetime;limit.enabled=false;
                    var noise=ps.noise;noise.enabled=true;noise.strength=.22f;noise.frequency=.65f;noise.scrollSpeed=.25f;noise.damping=true;
                    var force=ps.forceOverLifetime;force.enabled=true;force.space=ParticleSystemSimulationSpace.World;force.x=force.z=0f;force.y=-18f;
                    var collision=ps.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;collision.mode=ParticleSystemCollisionMode.Collision3D;
                    collision.collidesWith=~((1<<LayerMask.NameToLayer("Enemy"))|(1<<LayerMask.NameToLayer("Player"))|(1<<LayerMask.NameToLayer("Ignore Raycast")));
                    collision.quality=ParticleSystemCollisionQuality.Medium;collision.bounce=0f;collision.dampen=1f;collision.lifetimeLoss=1f;collision.radiusScale=.12f;collision.sendCollisionMessages=false;
                    var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=clump?ParticleSystemRenderMode.Billboard:ParticleSystemRenderMode.Stretch;renderer.alignment=ParticleSystemRenderSpace.View;
                    renderer.velocityScale=.025f;renderer.lengthScale=1.35f;renderer.cameraVelocityScale=0f;
                }
            }
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)){
                var shared=renderer.sharedMaterials;
                for(int i=0;i<shared.Length;i++){if(shared[i]==null)continue;string materialPath=Root+"/Composite/Materials/M_BossBlood_"+shared[i].name.Replace('/','_')+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(material==null){material=new Material(shared[i]){name="M_BossBlood_"+shared[i].name};material.shader=catalog.sprayProfileShader;AssetDatabase.CreateAsset(material,materialPath);}shared[i]=material;}
                renderer.sharedMaterials=shared;}
            root.AddComponent<EnemyBossBloodSpray>();return PrefabUtility.SaveAsPrefabAsset(root,path);
        }finally{Object.DestroyImmediate(root);}
    }
}
