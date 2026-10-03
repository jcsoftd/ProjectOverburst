using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class DashHeavyAttackBuilder
{
    public const string ClipPath=PlayerEvadeBuilder.AttackRoot+"/Greatsword_DashHeavySpin.anim";
    public const string HeavyPath="Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordDashHeavyAttack.asset";
    public const string PatternPath="Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/AP_GreatswordDashHeavy_Forward.asset";
    public const string MaterialPath="Assets/ProjectOverburst/Resources/Combat/VFX/DashHeavyFocus.mat";
    public const string SourcePath=PlayerEvadeBuilder.SourceRoot+"02_Attack/04_Combo_Attack_04/Combo_Attack_04_04.anim";
    public const float HitSeconds=.50f, WaveEndSeconds=.80f;
    public const string GatherPath="Assets/ProjectOverburst/Resources/Combat/SFX/CombatAction/DashHeavyGather.wav";
    public const string ReleasePath="Assets/ThirdParty/11_사운드/Hack and Slash Sound Library/Audio/Slash/Sword Slash Metallic Ring Short 01.wav";

    [MenuItem("OVERBURST/Weapons/Build Dash Heavy Spin")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 Editor가 필요합니다.");
        var definition=AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(PlayerEvadeBuilder.DefinitionPath);
        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath);
        var catalog=AssetDatabase.LoadAssetAtPath<CombatActionSfxCatalog>("Assets/ProjectOverburst/Resources/Combat/SFX/CombatActionSfxCatalog.asset");
        var gather=AssetDatabase.LoadAssetAtPath<AudioClip>(GatherPath);
        var release=AssetDatabase.LoadAssetAtPath<AudioClip>(ReleasePath);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ProjectOverburst/Resources/Shaders/DashHeavyFocus.shader");
        if(definition==null||source==null||catalog==null||gather==null||release==null||shader==null)
            throw new InvalidOperationException("대시 강공 모션·소리·셰이더 의존성이 필요합니다.");
        string normalBefore=EditorJsonUtility.ToJson(definition.heavyAttackDefinition);
        string parryBefore=EditorJsonUtility.ToJson(definition.parriedHeavyAttackDefinition);
        foreach(string path in new[]{PlayerEvadeBuilder.DefinitionPath,ClipPath,HeavyPath,PatternPath,MaterialPath,AssetDatabase.GetAssetPath(catalog)})
        {
            var asset=AssetDatabase.LoadMainAssetAtPath(path);
            if(asset!=null&&EditorUtility.IsDirty(asset))throw new InvalidOperationException("미저장 자산 보존: "+path);
        }
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if(clip==null){clip=UnityEngine.Object.Instantiate(source);clip.name="Greatsword_DashHeavySpin";AssetDatabase.CreateAsset(clip,ClipPath);}
        else EditorUtility.CopySerialized(source,clip);
        clip.name="Greatsword_DashHeavySpin";
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            if(binding.propertyName=="RootT.x"||binding.propertyName=="RootT.z")
                AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0f,source.length,0f));
        AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
        EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);
        var pattern=LoadOrCreate<AttackPatternDefinition>(PatternPath);
        pattern.shape=AttackAreaShape.Rectangle;pattern.fillMode=AttackFillMode.LinearFill;
        pattern.direction=AttackFillDirection.NearToFar;pattern.verticalTolerance=3f;
        pattern.hitRevalidationTolerance=.4f;pattern.angleOffset=0f;pattern.progressCurve=AnimationCurve.Linear(0f,0f,1f,1f);
        EditorUtility.SetDirty(pattern);AssetDatabase.SaveAssetIfDirty(pattern);
        var heavy=LoadOrCreate<MeleeHeavyAttackDefinition>(HeavyPath);
        EditorUtility.CopySerialized(definition.heavyAttackDefinition,heavy);heavy.name="GreatswordDashHeavyAttack";
        var step=heavy.attack;
        step.attackId="Greatsword.DashHeavy.Spin";step.attackName="대시 회전 강공";step.animationClip=clip;
        step.animationSpeedMultiplier=1f;step.transitionDuration=.08f;
        step.continuationStartNormalizedTime=0f;step.playbackAcceleration=new MeleePlaybackAcceleration{dashHeavyFocus=true};
        step.movementPhases=Array.Empty<AttackMovementPhaseData>();step.visualHeightCurve=null;
        step.comboInputWindow=default;step.actionCancelStartNormalized=1f;
        step.trailPhases=new[]{new AttackTrailPhaseData{startNormalizedTime=.44f/clip.length,endNormalizedTime=.64f/clip.length}};
        var phase=definition.heavyAttackDefinition.attack.attackPhases[definition.heavyAttackDefinition.SafeDischargePhaseIndex];
        phase.attackPattern=pattern;phase.startNormalizedTime=HitSeconds/clip.length;phase.endNormalizedTime=WaveEndSeconds/clip.length;
        phase.geometry.rangeMultiplier=2.5f;phase.geometry.widthMultiplier=2f;
        phase.geometry.overrideForwardOffset=true;phase.geometry.forwardOffset=0f;
        phase.progressSource=AttackProgressSource.NormalizedTime;phase.basisFollowMode=AttackBasisFollowMode.Fixed;
        phase.vfxCues=Array.Empty<AttackVfxCueData>();phase.useBakedVfxSwingSlope=false;
        phase.vfxSwingSettings.orientation=AttackVfxSwingOrientation.Horizontal;
        step.attackPhases=new[]{phase};heavy.attack=step;heavy.dischargePhaseIndex=0;
        EditorUtility.SetDirty(heavy);AssetDatabase.SaveAssetIfDirty(heavy);
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,MaterialPath);}
        material.shader=shader;material.SetFloat("_Intensity",1.25f);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        definition.dashHeavyAttackDefinition=heavy;EditorUtility.SetDirty(definition);AssetDatabase.SaveAssetIfDirty(definition);
        var entries=catalog.entries.Where(e=>e!=null&&e.name!="DashHeavyGather"&&e.name!="DashHeavyRelease").ToList();
        entries.Add(new CombatActionSfxCatalog.Entry{name="DashHeavyGather",clip=gather});
        entries.Add(new CombatActionSfxCatalog.Entry{name="DashHeavyRelease",clip=release});catalog.entries=entries.ToArray();
        EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
        if(normalBefore!=EditorJsonUtility.ToJson(definition.heavyAttackDefinition)||parryBefore!=EditorJsonUtility.ToJson(definition.parriedHeavyAttackDefinition))
            throw new InvalidOperationException("일반·패링 강공 데이터가 변경됐습니다.");
    }
    private static T LoadOrCreate<T>(string path) where T:ScriptableObject
    {
        var asset=AssetDatabase.LoadAssetAtPath<T>(path);
        if(asset==null){asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);}
        return asset;
    }
}
