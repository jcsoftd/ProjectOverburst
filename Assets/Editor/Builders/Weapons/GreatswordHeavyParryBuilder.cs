using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>받아치기와 반동만 분리하고 기존 대검 공격과 독립된 Animator 슬롯으로 연결한다.</summary>
public static class GreatswordHeavyParryBuilder
{
    public const string SourcePath = "Assets/ThirdParty/03_애니메이션/Sword_Animations_Pack/Animation/Humanoid/02_Attack/13_Parry_Counter_Attack/Parry_Counter_Attack.anim";
    public const string ClipPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/Clips/Greatsword_HeavyParry.anim";
    public const string ProfilePath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/GreatswordCombatAnimationProfile.asset";
    public const string ControllerPath = "Assets/ProjectOverburst/03_Features/Player/Animations/AC_Player_Rigged.controller";
    public const int FirstFrame = 2, ContactFrame = 4, LastFrame = 17;

    [MenuItem("OVERBURST/Weapons/Build Greatsword Heavy Parry")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("편집 모드에서 실행하세요.");
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath);
        var profile = AssetDatabase.LoadAssetAtPath<WeaponCombatAnimationProfile>(ProfilePath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (source == null || !source.isHumanMotion || profile == null || controller == null)
            throw new InvalidOperationException("패링 원본·대검 프로필·플레이어 Animator가 필요합니다.");
        float start = FirstFrame / source.frameRate, end = LastFrame / source.frameRate;
        var copy = UnityEngine.Object.Instantiate(source);
        try
        {
            copy.name = "Greatsword_HeavyParry";
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
                AnimationUtility.SetEditorCurve(copy, binding, Trim(AnimationUtility.GetEditorCurve(source, binding), start, end));
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
                AnimationUtility.SetObjectReferenceCurve(copy, binding, AnimationUtility.GetObjectReferenceCurve(source, binding)
                    .Where(k => k.time >= start && k.time <= end)
                    .Select(k => new ObjectReferenceKeyframe { time = k.time - start, value = k.value }).ToArray());
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.startTime = 0f;
            settings.stopTime = end - start;
            settings.loopTime = false;
            settings.loopBlend = false;
            settings.keepOriginalPositionXZ = true;
            settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null) { clip = copy; AssetDatabase.CreateAsset(clip, ClipPath); copy = null; }
            else EditorUtility.CopySerialized(copy, clip);
            EditorUtility.SetDirty(clip);
            var machine = controller.layers.First(l => l.name == profile.animatorLayerName).stateMachine;
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == profile.heavyParryStateName)
                ?? machine.AddState(profile.heavyParryStateName, new Vector3(700, 350));
            state.motion = clip;
            state.writeDefaultValues = false;
            state.iKOnFeet = false;
            state.speed = 1f;
            state.speedParameter = profile.heavyParrySpeedParameterName;
            state.speedParameterActive = true;
            if (!controller.parameters.Any(p => p.name == profile.heavyParrySpeedParameterName))
                controller.AddParameter(new AnimatorControllerParameter { name = profile.heavyParrySpeedParameterName,
                    type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            profile.heavyParryClip = clip;
            profile.heavyParryEntryBlend = .04f;
            profile.heavyParryToAttackBlend = .12f;
            profile.heavyParryContactSeconds = (ContactFrame - FirstFrame) / source.frameRate;
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(clip);
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.SaveAssetIfDirty(controller);
            Debug.Log("[대검 패링] 원본 2~17프레임(60FPS, 0.25초), 반격 제외, 독립 상태 연결 완료.");
        }
        finally { if (copy != null) UnityEngine.Object.DestroyImmediate(copy); }
    }

    private static AnimationCurve Trim(AnimationCurve source, float start, float end)
    {
        var keys = new List<Keyframe>();
        foreach (var original in source.keys)
        {
            if (original.time < start - .000001f || original.time > end + .000001f) continue;
            var key = original;
            key.time = Mathf.Clamp(key.time - start, 0f, end - start);
            keys.Add(key);
        }
        if (keys.Count == 0 || keys[0].time > .000001f) keys.Insert(0, Boundary(source, start, 0f));
        if (keys[keys.Count - 1].time < end - start - .000001f) keys.Add(Boundary(source, end, end - start));
        return new AnimationCurve(keys.ToArray()) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
    }

    private static Keyframe Boundary(AnimationCurve curve, float time, float shiftedTime)
    {
        const float epsilon = .00001f;
        float slope = (curve.Evaluate(time + epsilon) - curve.Evaluate(time - epsilon)) / (2f * epsilon);
        return new Keyframe(shiftedTime, curve.Evaluate(time), slope, slope);
    }
}
