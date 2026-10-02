using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>회피와 닷지 공격의 프로젝트 파생 자산만 생성하고 기존 판정 정의를 보존한다.</summary>
public static class PlayerEvadeBuilder
{
    public const string Root = "Assets/ProjectOverburst/03_Features/Player/Animations/Evade";
    public const string ProfilePath = Root + "/PlayerEvadeProfile.asset";
    public const string ControllerPath = "Assets/ProjectOverburst/03_Features/Player/Animations/AC_Player_Rigged.controller";
    public const string PlayerPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string DefinitionPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Definition/GreatswordDefinition.asset";
    public const string OpenerPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Combos/GreatswordDodgeOpener.asset";
    public const string AttackRoot = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/Clips";
    public const string SourceRoot = "Assets/ThirdParty/03_애니메이션/Sword_Animations_Pack/Animation/Humanoid/";
    public const int LightFirstFrame = 14, LightLastFrame = 94;
    public const int HeavyFirstFrame = 7, HeavyImpactFrame = 27, HeavyLastFrame = 83;

    [MenuItem("OVERBURST/Player/Build Evade and Dodge Attacks")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("회피 제작에는 유휴 편집 모드가 필요합니다.");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var definition = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(DefinitionPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        if (controller == null || definition == null || prefab == null || definition.comboDefinition == null
            || definition.heavyAttackDefinition == null || !definition.heavyAttackDefinition.IsConfigured)
            throw new InvalidOperationException("플레이어·대검 콤보·일반 강공 의존성이 필요합니다.");
        if (EditorUtility.IsDirty(controller) || EditorUtility.IsDirty(definition) || EditorUtility.IsDirty(prefab))
            throw new InvalidOperationException("기존 미저장 자산을 먼저 소유 작업에서 처리하세요.");
        string heavyBefore = EditorJsonUtility.ToJson(definition.heavyAttackDefinition);
        string comboBefore = EditorJsonUtility.ToJson(definition.comboDefinition);
        EnsureFolder(Root); EnsureFolder(AttackRoot);
        var profile = AssetDatabase.LoadAssetAtPath<PlayerEvadeProfile>(ProfilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<PlayerEvadeProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
        profile.explorationDodge = MakeClip(SourceRoot + "06_Dodge/01_Dodge/Dodge_F.anim", Root + "/Exploration_Dodge.anim", 0, 48);
        profile.explorationDodgeToRun = MakeClip(SourceRoot + "06_Dodge/05_Dodge_to_Run/Dodge_to_Run_F.anim", Root + "/Exploration_DodgeToRun.anim", 0, 40);
        var directions = new[] { "F", "FL", "FR", "L", "R" };
        var sources = new[] { "F", "F_L_45", "R_L_45", "L", "R" };
        var combat = new AnimationClip[5];
        for (int i = 0; i < combat.Length; i++)
            combat[i] = MakeClip(SourceRoot + "06_Dodge/02_Dodge_Combat/Dodge_Combat_" + sources[i] + ".anim",
                Root + "/Combat_Dodge_" + directions[i] + ".anim", 0, 48);
        profile.combatClips = new DirectionalAnimationSet8
        { forward = combat[0], forwardLeft = combat[1], forwardRight = combat[2], left = combat[3], right = combat[4] };
        var lightClip = MakeClip(SourceRoot + "02_Attack/12_Run_Attack/Run_Attack_01.anim",
            AttackRoot + "/Greatsword_DodgeAttack.anim", LightFirstFrame, LightLastFrame);
        var heavy = definition.heavyAttackDefinition;
        var heavyClip = MakeClip(SourceRoot + "02_Attack/12_Run_Attack/Run_Attack_02.anim",
            AttackRoot + "/Greatsword_DodgeHeavy.anim", HeavyFirstFrame, HeavyLastFrame,
            heavy.attack.animationClip.length, HeavyImpactFrame, heavy.attack.attackPhases[0].SafeStart);

        var opener = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(OpenerPath);
        if (opener == null) { opener = ScriptableObject.CreateInstance<MeleeComboDefinition>(); AssetDatabase.CreateAsset(opener, OpenerPath); }
        var working = ScriptableObject.CreateInstance<MeleeComboDefinition>();
        try
        {
            EditorJsonUtility.FromJsonOverwrite(comboBefore, working);
            var step = working.GetStep(0);
            step.attackId = "Greatsword.DodgeOpener.1"; step.attackName = "닷지 어택";
            step.animationClip = lightClip; step.transitionDuration = .08f;
            step.continuationStartNormalizedTime = 0f; step.playbackAcceleration = default;
            step.movementPhases = Array.Empty<AttackMovementPhaseData>();
            step.visualHeightCurve = new AnimationCurve();
            step.comboInputWindow = new ComboNormalizedWindow { startNormalizedTime = .86f, endNormalizedTime = .98f };
            step.actionCancelStartNormalized = .87f;
            var phase = step.attackPhases[0]; phase.startNormalizedTime = .50f; phase.endNormalizedTime = .86f;
            step.attackPhases = new[] { phase };
            step.trailPhases = new[] { new AttackTrailPhaseData { startNormalizedTime = .50f, endNormalizedTime = .86f } };
            working.steps = new[] { step };
            working.entryTransitionDuration = .08f;
            EditorUtility.CopySerialized(working, opener);
            EditorUtility.SetDirty(opener); AssetDatabase.SaveAssetIfDirty(opener);
        }
        finally { UnityEngine.Object.DestroyImmediate(working); }
        MeleeAttackVfxSlopeBakeUtility.BakeSelectedCombo(opener);
        if (!MeleeAttackVfxSlopeBakeUtility.ValidateCombo(opener, out string bakeError))
            throw new InvalidOperationException(bakeError);

        int layerIndex = Array.FindIndex(controller.layers, l => l.name == PlayerEvadeProfile.ExplorationLayer);
        if (layerIndex < 0) { controller.AddLayer(PlayerEvadeProfile.ExplorationLayer); layerIndex = controller.layers.Length - 1; }
        var layers = controller.layers; var layer = layers[layerIndex];
        layer.defaultWeight = 0f; layer.blendingMode = AnimatorLayerBlendingMode.Override; layer.iKPass = false;
        layers[layerIndex] = layer; controller.layers = layers;
        // 넉다운은 모든 회피/무기 전신 자세보다 마지막에 적용한다.
        var ordered = controller.layers.ToList();
        var knockdown = ordered.FirstOrDefault(l => l.name == "Player_Knockdown");
        if (knockdown != null) { ordered.Remove(knockdown); ordered.Add(knockdown); controller.layers = ordered.ToArray(); }
        var empty = layer.stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Evade_Empty")
            ?? layer.stateMachine.AddState("Evade_Empty");
        empty.writeDefaultValues = false; layer.stateMachine.defaultState = empty;
        Parameter(controller, PlayerEvadeProfile.ExplorationSpeed);
        State(layer.stateMachine, PlayerEvadeProfile.ExplorationStandState, profile.explorationDodge, PlayerEvadeProfile.ExplorationSpeed);
        State(layer.stateMachine, PlayerEvadeProfile.ExplorationRunState, profile.explorationDodgeToRun, PlayerEvadeProfile.ExplorationSpeed);
        var meleeLayer = controller.layers.First(l => l.name == "Combat_MeleeWeapon");
        for (int i = 0; i < combat.Length; i++) State(meleeLayer.stateMachine, PlayerEvadeProfile.CombatStatePrefix + directions[i], combat[i], "Melee_ActionSpeed");
        definition.dodgeAttackDefinition = opener; definition.dodgeHeavyAnimationClip = heavyClip;
        EditorUtility.SetDirty(profile); EditorUtility.SetDirty(definition); EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(profile); AssetDatabase.SaveAssetIfDirty(definition); AssetDatabase.SaveAssetIfDirty(controller);
        GameObject contents = null;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(PlayerPath);
            var field = new SerializedObject(contents.GetComponent<PlayerEvadeController>());
            field.FindProperty("evadeProfile").objectReferenceValue = profile;
            field.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
        }
        finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
        if (heavyBefore != EditorJsonUtility.ToJson(heavy) || comboBefore != EditorJsonUtility.ToJson(definition.comboDefinition))
            throw new InvalidOperationException("일반 콤보 또는 강공 정의가 변경되었습니다.");
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Combat/20261002_PlayerEvade/Validation"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "AssetBuild.json"), JsonConvert.SerializeObject(new
        {
            status = "PASS", profile = ProfilePath, opener = OpenerPath,
            lightSource = "Run_Attack_01", lightFrames = new[] { LightFirstFrame, LightLastFrame },
            heavySource = "Run_Attack_02", heavyFrames = new[] { HeavyFirstFrame, HeavyImpactFrame, HeavyLastFrame },
            lightLength = lightClip.length, heavyLength = heavyClip.length, normalHeavyLength = heavy.attack.animationClip.length,
            normalComboPreserved = true, normalHeavyPreserved = true,
            explorationDistance = profile.exploration.distance, explorationDuration = profile.exploration.duration,
            combatDistance = profile.combatDodge.distance, combatDuration = profile.combatDodge.duration,
            combatInvincible = profile.combatDodge.invincibleDuration, combatPerfect = profile.combatDodge.perfectWindow,
            clips = combat.Select(c => new { c.name, c.length, c.isHumanMotion }).ToArray()
        }, Formatting.Indented));
        Debug.Log("[PlayerEvade] 회피 3종과 닷지 약공/동일 강공 자산 연결 완료.");
    }

    static AnimationClip MakeClip(string sourcePath, string destination, int first, int last,
        float outputDuration = 0f, int impact = -1, float impactNormalized = 0f)
    {
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);
        if (source == null || !source.isHumanMotion) throw new InvalidOperationException(sourcePath);
        float start = first / source.frameRate, end = Mathf.Min(last / source.frameRate, source.length);
        float strike = impact / source.frameRate;
        float duration = outputDuration > 0f ? outputDuration : end - start;
        bool segmented = impact >= 0 && strike > start && strike < end && impactNormalized > 0f && impactNormalized < 1f;
        float Map(float t) => segmented
            ? (t <= strike ? (t - start) / (strike - start) * impactNormalized
                : impactNormalized + (t - strike) / (end - strike) * (1f - impactNormalized)) * duration
            : (t - start) / (end - start) * duration;
        float Scale(float t) => segmented
            ? duration * (t <= strike ? impactNormalized / (strike - start) : (1f - impactNormalized) / (end - strike))
            : duration / (end - start);
        var copy = UnityEngine.Object.Instantiate(source);
        try
        {
            copy.name = Path.GetFileNameWithoutExtension(destination);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                AnimationCurve value;
                if (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z")
                    value = AnimationCurve.Constant(0f, duration, curve.Evaluate(start));
                else
                {
                    var times = curve.keys.Where(k => k.time > start && k.time < end).Select(k => k.time).ToList();
                    times.Add(start); times.Add(end); if (segmented) times.Add(strike);
                    var keys = times.Distinct().OrderBy(t => t).Select(t =>
                    {
                        const float epsilon = .00001f;
                        float slope = (curve.Evaluate(t + epsilon) - curve.Evaluate(t - epsilon)) / (2f * epsilon) / Scale(t);
                        return new Keyframe(Mathf.Clamp(Map(t), 0f, duration), curve.Evaluate(t), slope, slope);
                    }).ToArray();
                    value = new AnimationCurve(keys);
                }
                value.preWrapMode = value.postWrapMode = WrapMode.ClampForever;
                AnimationUtility.SetEditorCurve(copy, binding, value);
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
                AnimationUtility.SetObjectReferenceCurve(copy, binding, AnimationUtility.GetObjectReferenceCurve(source, binding)
                    .Where(k => k.time >= start && k.time <= end).Select(k => new ObjectReferenceKeyframe { time = Map(k.time), value = k.value }).ToArray());
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.startTime = 0f; settings.stopTime = duration; settings.loopTime = settings.loopBlend = false;
            settings.keepOriginalPositionXZ = true; settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
            if (clip == null) { clip = copy; AssetDatabase.CreateAsset(clip, destination); copy = null; }
            else EditorUtility.CopySerialized(copy, clip);
            EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }
        finally { if (copy != null) UnityEngine.Object.DestroyImmediate(copy); }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static void Parameter(AnimatorController controller, string name)
    {
        if (!controller.parameters.Any(p => p.name == name)) controller.AddParameter(name, AnimatorControllerParameterType.Float);
    }
    static void State(AnimatorStateMachine machine, string name, AnimationClip clip, string speed)
    {
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
        state.motion = clip; state.writeDefaultValues = false; state.iKOnFeet = false;
        state.speed = 1f; state.speedParameter = speed; state.speedParameterActive = true;
        EditorUtility.SetDirty(state); EditorUtility.SetDirty(machine);
    }
}
