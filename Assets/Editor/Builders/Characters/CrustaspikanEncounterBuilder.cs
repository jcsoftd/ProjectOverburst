using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CrustaspikanEncounterBuilder
{
    public const string AssetPath="Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan.asset";
    // 비어 있는 등장 사운드만 연결한다. 이미 저작된 전투 수치와 사운드 선택은 보존한다.
    public static string ConnectEntranceSound()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 EditMode에서 등장 사운드를 연결하세요.");
        var settings=AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(AssetPath);
        if(settings==null || EditorUtility.IsDirty(settings))throw new InvalidOperationException("전투 설정의 미저장 변경을 먼저 소유 작업에서 마쳐야 합니다.");
        var serialized=new SerializedObject(settings);serialized.Update();
        var sounds=new[] {
            new[]{"roarClip","Dragon Kit Sound FX/Dragon_RoarBig_1.wav"},
            new[]{"inhaleClip","Dragon Kit Sound FX/Dragon_Inhale_1.wav"},
            new[]{"rumbleClip","Pro Sound Collection/Cinematic Sounds/cinematic_deep_bass_rumble_01.wav"},
            new[]{"breachClip","Pro Sound Collection/Impacts_Smashable/rock_earthquake_impact_01.wav"},
            new[]{"impactClip","Pro Sound Collection/Impacts_Smashable/rock_impact_heavy_slam_01.wav"}
        };
        foreach(var entry in sounds)
        {
            var sound=serialized.FindProperty("entrance."+entry[0]);
            if(sound==null)throw new InvalidOperationException("등장 사운드 설정이 컴파일되지 않았습니다.");
            if(sound.objectReferenceValue!=null)continue;
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ThirdParty/11_사운드/"+entry[1]);
            if(clip==null)throw new InvalidOperationException("기존 등장 효과음을 찾지 못했습니다: "+entry[1]);
            sound.objectReferenceValue=clip;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(settings);
        return "Existing rumble, earth rupture, heavy slam, inhale and roar linked to entrance only";
    }
    [MenuItem("OVERBURST/Builders/Crustaspikan/Build First Encounter Settings")]
    public static void BuildMenu()=>Debug.Log(Build());
    public static string Build()
    {
        var existing=AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(AssetPath);
        if(existing!=null){
            if(existing.composites==null){existing.composites=AssetDatabase.LoadAssetAtPath<EnemyBossCompositePatternSet>("Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanMaterials/Composite/BCP_Crustaspikan.asset");EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);}
            if(!existing.Validate(out string r))throw new InvalidOperationException(r);return "Existing encounter settings preserved: "+AssetPath;
        }
        EnsureFolder("Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanEncounter");
        var s=ScriptableObject.CreateInstance<CrustaspikanEncounterSettings>();
        s.materials=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>("Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanMaterials/BMC_Crustaspikan.asset");
        s.composites=AssetDatabase.LoadAssetAtPath<EnemyBossCompositePatternSet>("Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanMaterials/Composite/BCP_Crustaspikan.asset");
        s.adds=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>("Assets/ProjectOverburst/Resources/Enemies/Themes/Tables/CavernMutants.asset");
        var rules=new List<CrustaspikanEncounterSettings.MaterialRule>();
        foreach(var m in s.materials.attacks)
        {
            string clip=m.runtimeClip.name;float speed=1.1f,damage=.9f;bool parry=true;
            if(clip=="LeftHandAttack" || clip=="RightHandAttack"){speed=1.2f;damage=.45f;}
            if(clip.Contains("Smash")){speed=.95f;damage=1.3f;}
            if(clip.Contains("FootStomp")){speed=1.05f;damage=.8f;parry=false;}
            if(clip=="2HandsSmashAttack"){speed=.85f;damage=1.4f;parry=false;}
            if(clip.Contains("Spitter") || clip=="ThrowRock"){speed=1f;damage=clip=="SpitterShot2"?.5f:.75f;parry=false;}
            rules.Add(new CrustaspikanEncounterSettings.MaterialRule{clip=clip,speed=speed,damage=damage,finalHitParry=parry});
        }
        s.materialRules=rules.ToArray();
        var patterns=new List<CrustaspikanEncounterSettings.Pattern>{
            P("left_light","왼손 약공","light",10,5,0,10.5f,A("LeftHandAttack")),
            P("right_light","오른손 약공","light",10,5,0,11f,A("RightHandAttack")),
            P("combo","2연타 · 마지막 패링","combo",12,8,2,11.5f,A("2HitComboAttack")),
            P("advance_combo","접근 후 2연타","pressure",8,12,7,17,M(new Vector3(0,0,2.5f),1.1f),A("2HitComboAttackForward")),
            P("left_smash","왼손 강타","heavy",8,12,6,14.5f,A("LeftHandSmashAttack")),
            P("right_smash","오른손 강타","heavy",8,12,4,12.5f,A("RightHandSmashAttack")),
            P("left_stomp","왼발 · 회피","stomp",6,10,0,6.5f,A("LeftFootStompAttack")),
            P("right_stomp","오른발 · 회피","stomp",6,10,0,7f,A("RightFootStompAttack")),
            P("rear_left","회전 왼발 · 후방 견제","rear",7,14,0,7f,A("Turn90LeftFootStompAttack")),
            P("rear_right","회전 오른발 · 후방 견제","rear",7,14,0,7f,A("Turn90RightFootStompAttack")),
            P("turn_hand_left","회전 왼손 견제","turn",5,12,2,11.5f,A("Turn90LeftHandAttack")),
            P("turn_hand_right","회전 오른손 견제","turn",5,12,2,11.5f,A("Turn90RightHandAttack")),
            P("donut","양손 강타 · 내측/외측 회피","area",5,18,3.5f,14f,A("2HandsSmashAttack")),
            P("weak_spit","약한 분사 · 소형 방출","summon",6,20,6,24,A("SpitterShot2")),
            P("strong_spit","강한 분사 · 소형/중형 방출","summon",4,28,6,24,A("SpitterShot1")),
            P("rock_throw","바위 발굴 → 투척","ranged",7,14,8,24,N("UnearthRock"),A("ThrowRock")),
            P("elite_throw","정예 발굴 → 투척 → 교전","summon",4,32,6,24,
                new CrustaspikanEncounterSettings.Step{kind=CrustaspikanStepKind.LiftElite},
                new CrustaspikanEncounterSettings.Step{kind=CrustaspikanStepKind.ThrowElite}),
            P("retreat_counter","후퇴 유도 → 지연 반격","counter",6,16,2,8,
                M(new Vector3(0,0,-2),.8f),new CrustaspikanEncounterSettings.Step{kind=CrustaspikanStepKind.Wait,seconds=.45f},A("RightHandAttack"))
        };
        patterns.Find(p=>p.id=="elite_throw").phaseMask=2;
        patterns.Find(p=>p.id=="strong_spit").phaseMask=2;
        patterns.Find(p=>p.id=="retreat_counter").phaseMask=2;
        patterns.Find(p=>p.id=="rear_left").rearOnly=true;patterns.Find(p=>p.id=="rear_right").rearOnly=true;
        s.patterns=patterns.ToArray();
        if(!s.Validate(out string reason))throw new InvalidOperationException(reason);
        AssetDatabase.CreateAsset(s,AssetPath);AssetDatabase.SaveAssetIfDirty(s);
        return "Created "+AssetPath+" · 18 patterns / 16 material rules / grid circular arena runtime bootstrap";
    }
    private static CrustaspikanEncounterSettings.Step A(string clip)=>new CrustaspikanEncounterSettings.Step{kind=CrustaspikanStepKind.Attack,materialOrMotion=clip};
    private static CrustaspikanEncounterSettings.Step N(string motion)=>new CrustaspikanEncounterSettings.Step{kind=CrustaspikanStepKind.Motion,materialOrMotion=motion};
    private static CrustaspikanEncounterSettings.Step M(Vector3 delta,float seconds)=>new CrustaspikanEncounterSettings.Step{kind=CrustaspikanStepKind.Move,localDisplacement=delta,seconds=seconds};
    private static CrustaspikanEncounterSettings.Pattern P(string id,string label,string family,float weight,float cooldown,float min,float max,params CrustaspikanEncounterSettings.Step[] steps)
        =>new CrustaspikanEncounterSettings.Pattern{id=id,label=label,family=family,weight=weight,cooldown=cooldown,minimumDistance=min,maximumDistance=max,steps=steps};
    private static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        int slash=path.LastIndexOf('/');string parent=path.Substring(0,slash);EnsureFolder(parent);AssetDatabase.CreateFolder(parent,path.Substring(slash+1));
    }
}
