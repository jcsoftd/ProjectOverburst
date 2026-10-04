using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
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
        var smallA=Payload("Ceratoferox",3,.7f);var smallB=Payload("Cephalonops",3,.7f);var medium=Payload("Gasterobrach",1,1f);var elite=Payload("Ursacetus",1,1.2f);
        var sweep=collection.attacks.First(x=>x.runtimeClip.name=="SpitterShot1");var line=collection.attacks.First(x=>x.runtimeClip.name=="SpitterShot2");
        sweep.strikes=new[]{Strike(sweep,33,57,GroundIndicatorShape.Sector),Strike(sweep,68,94,GroundIndicatorShape.Sector)};
        line.strikes=new[]{Strike(line,74,106,GroundIndicatorShape.Rectangle)};
        sweep.muzzleOffset=line.muzzleOffset=new Vector3(-.55f,.35f,0f);
        sweep.assemblyNotes="좌→우 / 우→좌 피해 분사와 암굴 소형2+중형1 토출. 기본 계수·속도는 재료 tuning, 토출 세부는 BCP_Crustaspikan. 시험값은 추후 수정.";
        line.assemblyNotes="직선 피해 분사와 암굴 소형3 토출. 기본 계수·속도는 재료 tuning, 토출 세부는 BCP_Crustaspikan. 시험값은 추후 수정.";
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
        set.authoringNotes="사용자 지시로 우선 적용한 시험 구성. 분사1:33–57F/68–94F, 소형2+중형1. 분사2:74–106F, 소형3. 바위·암굴 거수 투척은 교대, SetNextThrowPayload로 선택 가능. 종·수량·방출·기상·속도·피해는 후속 조절. 공급사 원본은 수정하지 않음.";
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
    static EnemyBossCompositePatternSet.Emission Emission(EnemyBossAttackMaterial material,int frame,int phase,EnemyBossCompositePatternSet.Payload payload,float distance,float yaw=0f)
        =>new EnemyBossCompositePatternSet.Emission{normalizedTime=frame/(material.runtimeClip.length*30f),phase=phase,payload=payload,landingDistance=distance,yawOffset=yaw,count=1};
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
    static GameObject Spray(BloodEffectsPackCatalog catalog,int index,string name)
    {
        var root=Object.Instantiate(catalog.sprays[index].prefab);root.SetActive(false);string path=Root+"/Composite/Visuals/PF_CrustaspikanBlood"+name+".prefab";
        try{
            root.name="PF_CrustaspikanBlood"+name;
            foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true)){
                ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.loop=true;main.duration=1f;main.startDelay=0f;main.playOnAwake=false;main.stopAction=ParticleSystemStopAction.None;
                main.startSpeed=22f;main.startLifetime=.8f;main.gravityModifier=.05f;main.maxParticles=64;main.simulationSpace=ParticleSystemSimulationSpace.World;
                main.scalingMode=ParticleSystemScalingMode.Hierarchy;ps.transform.localRotation=Quaternion.identity;var shape=ps.shape;shape.angle=3f;shape.radius=.15f;
                var emission=ps.emission;emission.rateOverTime=name=="Sweep"?30f:22f;emission.SetBursts(Array.Empty<ParticleSystem.Burst>());}
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)){
                var shared=renderer.sharedMaterials;
                for(int i=0;i<shared.Length;i++){if(shared[i]==null)continue;string materialPath=Root+"/Composite/Materials/M_BossBlood_"+shared[i].name.Replace('/','_')+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(material==null){material=new Material(shared[i]){name="M_BossBlood_"+shared[i].name};material.shader=catalog.sprayProfileShader;AssetDatabase.CreateAsset(material,materialPath);}shared[i]=material;}
                renderer.sharedMaterials=shared;}
            var controller=root.GetComponent<EnemyBossBloodSpray>();if(controller==null)root.AddComponent<EnemyBossBloodSpray>();
            return PrefabUtility.SaveAsPrefabAsset(root,path);
        }finally{Object.DestroyImmediate(root);}
    }
}
