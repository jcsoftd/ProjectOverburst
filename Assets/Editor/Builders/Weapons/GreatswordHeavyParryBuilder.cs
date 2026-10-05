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
    public const string CounterSourcePath = "Assets/ThirdParty/03_애니메이션/Girl_GreatSword_AnimSet/Animation/Humanoid/Combo_01_4_inplace.fbx";
    public const string CounterClipPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/Clips/Greatsword_ParriedHeavy.anim";
    public const string CounterDefinitionPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordParriedHeavyAttack.asset";
    public const string WeaponDefinitionPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Definition/GreatswordDefinition.asset";
    public const float SourceStartSeconds = 0f, SourceEndSeconds = .266f;
    public const int ContactFrame = 4;

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
        float start = SourceStartSeconds, end = SourceEndSeconds;
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
            profile.heavyParryPlaybackSpeed = .5f;
            profile.heavyParryHeavyStartSeconds = .138f;
            profile.heavyParryEntryBlend = .04f;
            profile.heavyParryToAttackBlend = .1f;
            profile.heavyParryContactSeconds = ContactFrame / source.frameRate - start;
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(clip);
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.SaveAssetIfDirty(controller);
            BuildCounter();
            Debug.Log("[대검 패링] 원본 0~0.266초, 0.5배속 완주 후 Combo_01_4_inplace 0.138초 자세로 0.1초 보간. 약공3타 회전 두 판정 후 착지1회.");
        }
        finally { if (copy != null) UnityEngine.Object.DestroyImmediate(copy); }
    }

    private static void BuildCounter()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(WeaponDefinitionPath);
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(CounterSourcePath);
        if (weapon == null || weapon.heavyAttackDefinition == null || weapon.comboDefinition == null
            || source == null || !source.isHumanMotion)
            throw new InvalidOperationException("기존 대검·약공3타·Combo_01_4_inplace가 필요합니다.");
        var sourceCopy = UnityEngine.Object.Instantiate(source);
        var definitionCopy = UnityEngine.Object.Instantiate(weapon.heavyAttackDefinition);
        try
        {
            sourceCopy.name = "Combo_01_4_inplace_ParriedHeavy";
            var settings = AnimationUtility.GetAnimationClipSettings(sourceCopy);
            settings.loopTime = false; settings.loopBlend = false;
            settings.keepOriginalPositionXZ = true; settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(sourceCopy, settings);
            AnimationUtility.SetAnimationEvents(sourceCopy, Array.Empty<AnimationEvent>());
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CounterClipPath);
            if (clip == null) { clip = sourceCopy; AssetDatabase.CreateAsset(clip, CounterClipPath); sourceCopy = null; }
            else EditorUtility.CopySerialized(sourceCopy, clip);
            var weak = weapon.comboDefinition.GetStep(2);
            if (weak.attackPhases == null || weak.attackPhases.Length != 2)
                throw new InvalidOperationException("대검 약공3타는 두 원형 판정이어야 합니다.");
            var first = weak.attackPhases[0]; var second = weak.attackPhases[1];
            first.startNormalizedTime = (5f / 30f) / clip.length;
            first.endNormalizedTime = (12.5f / 30f) / clip.length;
            second.startNormalizedTime = (13f / 30f) / clip.length;
            second.endNormalizedTime = (23f / 30f) / clip.length;
            // The pattern, geometry, reaction and VFX are the existing weak third hit.
            // This clip's own timed windows drive its two rotations, independent of the weak clip's bake.
            first.progressSource = second.progressSource = AttackProgressSource.NormalizedTime;
            var slam = definitionCopy.attack.attackPhases[definitionCopy.SafeDischargePhaseIndex];
            slam.startNormalizedTime = (25f / 30f) / clip.length;
            slam.endNormalizedTime = (28f / 30f) / clip.length;
            var attack = definitionCopy.attack;
            attack.attackId = "Greatsword.Heavy.ParriedDoubleCircleSlam";
            attack.attackName = "패링 강화 강공: 원형 베기 두 번 후 내려찍기";
            attack.animationClip = clip; attack.animationSpeedMultiplier = 1f;
            attack.transitionDuration = .1f; attack.continuationStartNormalizedTime = .138f / clip.length;
            attack.playbackAcceleration = default;
            attack.visualHeightCurve = new AnimationCurve();
            attack.actionCancelStartNormalized = 1.05f / clip.length;
            attack.attackPhases = new[] { first, second, slam };
            attack.movementPhases = MeleeRuntime.CreateParryCounterMovementPhases(weapon.comboDefinition.steps[2], attack, 2, MeleeRuntime.ParryCounterMaximumTravel);
            attack.trailPhases = new[] {
                new AttackTrailPhaseData { startNormalizedTime = first.SafeStart, endNormalizedTime = first.SafeEnd },
                new AttackTrailPhaseData { startNormalizedTime = second.SafeStart, endNormalizedTime = second.SafeEnd },
                new AttackTrailPhaseData { startNormalizedTime = .8f / clip.length, endNormalizedTime = slam.SafeEnd }
            };
            if (!AttackPhaseValidator.TryValidate(attack.attackPhases, out string error)) throw new InvalidOperationException(error);
            definitionCopy.name = "GreatswordParriedHeavyAttack";
            definitionCopy.attack = attack; definitionCopy.dischargePhaseIndex = 2;
            var definition = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(CounterDefinitionPath);
            if (definition == null) { definition = definitionCopy; AssetDatabase.CreateAsset(definition, CounterDefinitionPath); definitionCopy = null; }
            else EditorUtility.CopySerialized(definitionCopy, definition);
            weapon.parriedHeavyAttackDefinition = definition;
            EditorUtility.SetDirty(clip); EditorUtility.SetDirty(definition); EditorUtility.SetDirty(weapon);
            AssetDatabase.SaveAssetIfDirty(clip); AssetDatabase.SaveAssetIfDirty(definition); AssetDatabase.SaveAssetIfDirty(weapon);
        }
        finally
        {
            if (sourceCopy != null) UnityEngine.Object.DestroyImmediate(sourceCopy);
            if (definitionCopy != null) UnityEngine.Object.DestroyImmediate(definitionCopy);
        }
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

    public static void ApplyCounterMovement()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("패링 강공 이동 적용은 유휴 편집 모드에서 실행하세요.");
        var weapon = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(WeaponDefinitionPath);
        if (weapon == null || weapon.heavyAttackDefinition == null || weapon.parriedHeavyAttackDefinition == null)
            throw new InvalidOperationException("일반·패링 강공 정의가 필요합니다.");
        var counter = weapon.parriedHeavyAttackDefinition;
        var step = counter.attack;
        step.movementPhases = MeleeRuntime.CreateParryCounterMovementPhases(weapon.comboDefinition.steps[2], step, counter.SafeDischargePhaseIndex, MeleeRuntime.ParryCounterMaximumTravel);
        counter.attack = step;
        EditorUtility.SetDirty(counter);
        AssetDatabase.SaveAssetIfDirty(counter);
    }

    private static Keyframe Boundary(AnimationCurve curve, float time, float shiftedTime)
    {
        const float epsilon = .00001f;
        float slope = (curve.Evaluate(time + epsilon) - curve.Evaluate(time - epsilon)) / (2f * epsilon);
        return new Keyframe(shiftedTime, curve.Evaluate(time), slope, slope);
    }
}
