using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class OneHandSwordCombatAnimatorSetupUtility
{
    private const string ControllerPath = "Assets/ProjectOverburst/03_Features/Player/Animations/AC_Player_Rigged.controller";
    private const string PlayerPrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    private const string SwordAssetPath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const string ProfileFolder = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/Animation";
    private const string ProfilePath = ProfileFolder + "/OneHandSwordCombatAnimationProfile.asset";
    private const string WarriorInPlaceFolder = "Assets/ThirdParty/03_애니메이션/Frank_Slash_Pack/Assets/Animations/Frank_SlashPack_Warrior/FBX_Animation/In_Place";
    private const string GeneratedAnimationFolder = "Assets/ProjectOverburst/03_Features/Player/Animations/Generated";
    private const string CombatEquipExitClipPath = GeneratedAnimationFolder + "/Frank_RPG_Warrior_Equip_Reverse.anim";
    private const string RollClipPath = "Assets/ProjectOverburst/03_Features/Player/Animations/Roll/InPlace/RM_Roll_front_InPlace.anim";
    private const string PrimaryComboFolder = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/Animation/Clips/PrimaryCombo";
    private const string AttackStateName = "Melee_Attack";
    private const string MaskFolder = "Assets/ProjectOverburst/03_Features/Player/Animations/Masks";
    private const string TransitionLowerMaskPath = MaskFolder + "/AM_Combat_MeleeWeapon_TransitionLower.mask";
    private const string ReportPath = "Logs/OneHandSwordCombatAnimatorSetup.txt";
    private const string PendingSetupFlagPath = "Temp/RunOneHandSwordCombatAnimatorSetup.flag";
    private const string PendingWalkLocomotionFlagPath = "Temp/ApplyOneHandSwordWalkLocomotion.flag";
    private const string PendingGuardImportFlagPath = "Temp/RepairOneHandSwordGuardAimRelativeImports.flag";

    private const string BaseLayerName = "Base Layer";
    private const string MagicLayerName = "UpperBody_Magic";
    private const string FullBodyAimLayerName = "FullBody_Aim";
    private const string CombatLayerName = "Combat_MeleeWeapon";
    private const string TransitionLowerLayerName = "Combat_MeleeWeapon_TransitionLower";
    private const string TransitionLowerEmptyStateName = "Melee_TransitionLower_Empty";
    private const string TransitionLowerLocomotionStateName = "Melee_TransitionLower_Locomotion";
    private const string MoveXParameter = "MoveX";
    private const string MoveYParameter = "MoveY";
    private const string ActionSpeedParameter = "Melee_ActionSpeed";
    private const float EquipSpeedMultiplier = 1.3f;
    private const float UnequipSpeedMultiplier = 1.5f;
    private const float UnequipStartOffsetSeconds = 0.1f;
    private const float CombatMoveSpeed = 3f;
    private const float CombatGuardMoveSpeed = 1.8f;

    private static readonly string[] LoopingInPlaceClipNames =
    {
        "Frank_RPG_Warrior_Idle",
        "Frank_RPG_Warrior_Guard",
        "Frank_RPG_Warrior_8Way_Walk_F",
        "Frank_RPG_Warrior_8Way_Walk_B",
        "Frank_RPG_Warrior_8Way_Walk_L",
        "Frank_RPG_Warrior_8Way_Walk_R",
        "Frank_RPG_Warrior_8Way_Walk_FL",
        "Frank_RPG_Warrior_8Way_Walk_FR",
        "Frank_RPG_Warrior_8Way_Walk_BL",
        "Frank_RPG_Warrior_8Way_Walk_BR",
        "Frank_RPG_Warrior_4Way_Guard_Walk_F",
        "Frank_RPG_Warrior_4Way_Guard_Walk_B",
        "Frank_RPG_Warrior_4Way_Guard_Walk_L",
        "Frank_RPG_Warrior_4Way_Guard_Walk_R"
    };

    private static readonly string[] PrimaryComboClipNames =
    {
        "Frank_RPG_Warrior_Combo01_1_InPlace",
        "Frank_RPG_Warrior_Combo01_2_InPlace",
        "Frank_RPG_Warrior_Attack04_InPlace",
        "Frank_RPG_Warrior_Combo05_3_InPlace"
    };

    private static readonly string[] AimRelativeWalkClipNames =
    {
        "Frank_RPG_Warrior_8Way_Walk_F",
        "Frank_RPG_Warrior_8Way_Walk_B",
        "Frank_RPG_Warrior_8Way_Walk_L",
        "Frank_RPG_Warrior_8Way_Walk_R",
        "Frank_RPG_Warrior_8Way_Walk_FL",
        "Frank_RPG_Warrior_8Way_Walk_FR",
        "Frank_RPG_Warrior_8Way_Walk_BL",
        "Frank_RPG_Warrior_8Way_Walk_BR"
    };

    private static readonly string[] AimRelativeGuardClipNames =
    {
        "Frank_RPG_Warrior_Guard",
        "Frank_RPG_Warrior_4Way_Guard_Walk_F",
        "Frank_RPG_Warrior_4Way_Guard_Walk_B",
        "Frank_RPG_Warrior_4Way_Guard_Walk_L",
        "Frank_RPG_Warrior_4Way_Guard_Walk_R"
    };

    private static readonly string[] AimRelativeGuardReactionClipNames =
    {
        "Frank_RPG_Warrior_Block",
        "Frank_RPG_Warrior_4Way_Block"
    };

    [MenuItem("OVERBURST/Codex/Setup/Player/Setup OneHandSword Combat Animator")]
    public static void RunOnceFromCommandLine()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("OneHandSword combat animator setup");
        report.AppendLine("GeneratedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine();

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            throw new MissingReferenceException("[OneHandSwordCombatAnimator] Missing controller: " + ControllerPath);

        EnsureFolder(ProfileFolder);
        EnsureFolder(GeneratedAnimationFolder);
        EnsureFolder(MaskFolder);
        WeaponCombatAnimationProfile profile = EnsureProfile(report);
        AvatarMask transitionLowerMask = EnsureTransitionLowerBodyMask(report);
        EnsureLoopingClips(report);
        ConfigureAnimator(controller, transitionLowerMask, report);
        ValidateAttackStates(controller, report);
        ConfigurePlayerPrefab(profile, report);
        ConfigureSwordAsset(profile, report);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        WriteReport(report);
    }

    [MenuItem("OVERBURST/Codex/Setup/Player/Apply OneHandSword Walk Locomotion")]
    public static void ApplyWalkLocomotionFromCommandLine()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("OneHandSword walk locomotion update");
        report.AppendLine("GeneratedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine();

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            throw new MissingReferenceException("[OneHandSwordCombatAnimator] Missing controller: " + ControllerPath);

        EnsureLoopingClips(report);
        ConfigureExistingLocomotionTrees(controller, report);
        ConfigureCombatMovementSpeeds(report);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        WriteReport(report);
    }

    [MenuItem("OVERBURST/Codex/Setup/Combat/Repair OneHandSword Guard Aim Relative Imports")]
    public static void RepairGuardAimRelativeImportsFromCommandLine()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("OneHandSword guard and block aim-relative import repair");
        report.AppendLine("GeneratedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine();

        for (int i = 0; i < AimRelativeGuardClipNames.Length; i++)
        {
            string clipName = AimRelativeGuardClipNames[i];
            EnsureClipLoops(WarriorInPlaceFolder + "/" + clipName + ".FBX", clipName, report);
            EnsureAimRelativeLocomotionImport(clipName, report);
        }

        for (int i = 0; i < AimRelativeGuardReactionClipNames.Length; i++)
            EnsureAimRelativeLocomotionImport(AimRelativeGuardReactionClipNames[i], report);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        WriteReport(report);
        Debug.Log("[OneHandSwordCombatAnimator] Guard aim-relative import repair PASS.");
    }

    [InitializeOnLoadMethod]
    private static void RunPendingSetupAfterScriptReload()
    {
        if (Application.isBatchMode)
            return;

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), PendingSetupFlagPath);
        if (!File.Exists(fullPath))
            return;

        File.Delete(fullPath);
        try
        {
            RunOnceFromCommandLine();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [InitializeOnLoadMethod]
    private static void RunPendingWalkLocomotionAfterScriptReload()
    {
        if (Application.isBatchMode)
            return;

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), PendingWalkLocomotionFlagPath);
        if (!File.Exists(fullPath))
            return;

        File.Delete(fullPath);
        try
        {
            ApplyWalkLocomotionFromCommandLine();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [InitializeOnLoadMethod]
    private static void RunPendingGuardImportAfterScriptReload()
    {
        if (Application.isBatchMode)
            return;

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), PendingGuardImportFlagPath);
        if (!File.Exists(fullPath))
            return;

        File.Delete(fullPath);
        try
        {
            RepairGuardAimRelativeImportsFromCommandLine();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static WeaponCombatAnimationProfile EnsureProfile(StringBuilder report)
    {
        WeaponCombatAnimationProfile profile = AssetDatabase.LoadAssetAtPath<WeaponCombatAnimationProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<WeaponCombatAnimationProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            report.AppendLine("ProfileCreated=" + ProfilePath);
        }
        else
        {
            report.AppendLine("ProfileExists=" + ProfilePath);
        }

        SerializedObject serialized = new SerializedObject(profile);
        SetEnum(serialized, "combatStyle", (int)WeaponCombatStyle.MeleeWeapon, report);
        SetString(serialized, "animatorLayerName", CombatLayerName, report);
        SetString(serialized, "transitionLowerLayerName", TransitionLowerLayerName, report);
        SetString(serialized, "emptyStateName", "Melee_Empty", report);
        SetString(serialized, "equipStateName", "Melee_Equip", report);
        SetString(serialized, "locomotionStateName", "Melee_Locomotion", report);
        SetString(serialized, "guardLocomotionStateName", "Melee_GuardLocomotion", report);
        SetString(serialized, "unequipStateName", "Melee_Unequip", report);
        SetString(serialized, "blockStateName", "Melee_Block", report);
        SetString(serialized, "movingBlockStateName", "Melee_MovingBlock", report);
        SetString(serialized, "hitStateNamePrefix", "Melee_Hit", report);
        SetString(serialized, "jumpStateName", "Melee_Jump", report);
        SetString(serialized, "rollStateName", "Melee_Roll", report);
        SetString(serialized, "attackStateName", AttackStateName, report);
        SetString(serialized, "transitionLowerEmptyStateName", TransitionLowerEmptyStateName, report);
        SetString(serialized, "transitionLowerLocomotionStateName", TransitionLowerLocomotionStateName, report);
        SetString(serialized, "actionSpeedParameterName", ActionSpeedParameter, report);
        AssignDriverClip(serialized, "equipClip", "Frank_RPG_Warrior_Equip", report);
        SetObject(serialized, "unequipClip", EnsureCombatEquipExitClip(report), report);
        AssignDriverClip(serialized, "combatIdleClip", "Frank_RPG_Warrior_Idle", report);
        AssignDirectionalClipSet(serialized, "locomotion", AimRelativeWalkClipNames, report);
        AssignDriverClip(serialized, "guardIdleClip", "Frank_RPG_Warrior_Guard", report);
        AssignDirectionalClipSet(serialized, "guardLocomotion", new[]
        {
            AimRelativeGuardClipNames[1],
            AimRelativeGuardClipNames[2],
            AimRelativeGuardClipNames[3],
            AimRelativeGuardClipNames[4]
        }, report);
        SetBool(serialized, "useTransitionLowerBodyWhileGuarding", false, report);
        AssignDriverClip(serialized, "blockClip", "Frank_RPG_Warrior_Block", report);
        AssignDriverClip(serialized, "movingBlockClip", "Frank_RPG_Warrior_4Way_Block", report);
        AssignClipArray(serialized, "hitClips", new[]
        {
            "Frank_RPG_Warrior_Hit01",
            "Frank_RPG_Warrior_Hit02",
            "Frank_RPG_Warrior_Hit03"
        }, report);
        AssignDriverClip(serialized, "jumpClip", "Frank_RPG_Warrior_Jump_ZeroHeight", report);
        SetObject(serialized, "rollClip", LoadRollClip(report), report);
        SetFloat(serialized, "layerFadeInDuration", 0.08f, report);
        SetFloat(serialized, "layerFadeOutDuration", 0.12f, report);
        SetFloat(serialized, "equipToLocomotionTransitionDuration", 0.12f, report);
        SetFloat(serialized, "locomotionToGuardTransitionDuration", 0.08f, report);
        SetFloat(serialized, "guardLocomotionEnterTransitionDuration", 0.16f, report);
        SetFloat(serialized, "guardLocomotionExitTransitionDuration", 0.12f, report);
        SetFloat(serialized, "guardLocomotionStartOffsetSeconds", 0.05f, report);
        SetFloat(serialized, "actionTransitionDuration", 0.06f, report);
        SetFloat(serialized, "movingTransitionEndBlendDuration", 0.18f, report);
        SetFloat(serialized, "unequipWeaponBackLeadTime", 0.3f, report);
        SetFloat(serialized, "legacyActionSuppressionFadeDuration", 0.04f, report);
        SetFloat(serialized, "transitionLowerLayerFadeDuration", 0.05f, report);
        SetFloat(serialized, "transitionLowerMoveInputThreshold", 0.05f, report);
        SetFloat(serialized, "equipAnimationSpeedMultiplier", EquipSpeedMultiplier, report);
        SetFloat(serialized, "unequipAnimationSpeedMultiplier", UnequipSpeedMultiplier, report);
        SetFloat(serialized, "unequipAnimationStartOffsetSeconds", UnequipStartOffsetSeconds, report);
        SetObject(serialized, "attackTemplateClip", LoadPrimaryComboClip(0), report);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static AvatarMask EnsureTransitionLowerBodyMask(StringBuilder report)
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(TransitionLowerMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, TransitionLowerMaskPath);
            report.AppendLine("TransitionLowerMaskCreated=" + TransitionLowerMaskPath);
        }
        else
        {
            report.AppendLine("TransitionLowerMaskExists=" + TransitionLowerMaskPath);
        }

        mask.name = "AM_Combat_MeleeWeapon_TransitionLower";
        mask.transformCount = 0;
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, true);
        EditorUtility.SetDirty(mask);
        report.AppendLine("TransitionLowerMaskConfigured=Body+Legs+FootIK");
        return mask;
    }

    private static void ConfigureAnimator(AnimatorController controller, AvatarMask transitionLowerMask, StringBuilder report)
    {
        EnsureParameter(controller, "IsCombatMode", AnimatorControllerParameterType.Bool, report);
        EnsureParameter(controller, "IsGuarding", AnimatorControllerParameterType.Bool, report);
        EnsureParameter(controller, ActionSpeedParameter, AnimatorControllerParameterType.Float, report);
        RemoveParameterIfExists(controller, "OHS_EquipTrigger", report);
        RemoveParameterIfExists(controller, "OHS_UnequipTrigger", report);
        RemoveParameterIfExists(controller, "OHS_HitTrigger", report);
        RemoveParameterIfExists(controller, "OHS_BlockTrigger", report);
        RemoveParameterIfExists(controller, "OHS_JumpTrigger", report);
        RemoveParameterIfExists(controller, "OHS_HitIndex", report);
        RemoveParameterIfExists(controller, "OHS_ActionSpeed", report);

        RemoveLayerIfExists(controller, MagicLayerName, report);
        RemoveLayerIfExists(controller, FullBodyAimLayerName, report);
        RemoveLayerIfExists(controller, "Combat_OneHandSword", report);
        RemoveLayerIfExists(controller, "Combat_OneHandSword_TransitionLower", report);

        AnimatorControllerLayer layer = EnsureLayer(controller, CombatLayerName, report);
        AnimatorStateMachine stateMachine = layer.stateMachine;

        AnimatorState empty = EnsureState(stateMachine, "Melee_Empty", new Vector3(80f, 80f, 0f), report);
        empty.motion = null;
        stateMachine.defaultState = empty;

        AnimatorState equip = EnsureClipState(stateMachine, "Melee_Equip", "Frank_RPG_Warrior_Equip", new Vector3(340f, 80f, 0f), report);
        SetStateSpeed(equip, EquipSpeedMultiplier, report);
        AnimatorState locomotion = EnsureState(stateMachine, "Melee_Locomotion", new Vector3(620f, 80f, 0f), report);
        ConfigureLocomotionTree(controller, locomotion, report);
        AnimatorState guard = EnsureState(stateMachine, "Melee_GuardLocomotion", new Vector3(620f, 260f, 0f), report);
        ConfigureGuardTree(controller, guard, report);
        AnimatorState unequip = EnsureState(stateMachine, "Melee_Unequip", new Vector3(340f, 260f, 0f), report);
        unequip.motion = EnsureCombatEquipExitClip(report);
        ConfigureStateDefaults(unequip);
        SetStateSpeed(unequip, UnequipSpeedMultiplier, report);
        AnimatorState block = EnsureClipState(stateMachine, "Melee_Block", "Frank_RPG_Warrior_Block", new Vector3(880f, 80f, 0f), report);
        AnimatorState movingBlock = EnsureClipState(stateMachine, "Melee_MovingBlock", "Frank_RPG_Warrior_4Way_Block", new Vector3(880f, 260f, 0f), report);
        AnimatorState roll = EnsureAssetClipState(stateMachine, "Melee_Roll", LoadRollClip(report), new Vector3(880f, 420f, 0f), report);
        ConfigureActionSpeedState(roll, report);
        AnimatorState jump = EnsureClipState(stateMachine, "Melee_Jump", "Frank_RPG_Warrior_Jump_ZeroHeight", new Vector3(1140f, 80f, 0f), report);
        AnimatorState hit01 = EnsureClipState(stateMachine, "Melee_Hit01", "Frank_RPG_Warrior_Hit01", new Vector3(1140f, 260f, 0f), report);
        AnimatorState hit02 = EnsureClipState(stateMachine, "Melee_Hit02", "Frank_RPG_Warrior_Hit02", new Vector3(1140f, 420f, 0f), report);
        AnimatorState hit03 = EnsureClipState(stateMachine, "Melee_Hit03", "Frank_RPG_Warrior_Hit03", new Vector3(1140f, 580f, 0f), report);
        AnimatorState attack = EnsureAssetClipState(stateMachine, AttackStateName, LoadPrimaryComboClip(0), new Vector3(1400f, 80f, 0f), report);
        ConfigureActionSpeedState(attack, report);
        RemoveLegacyMeleeStates(stateMachine, report);

        EnsureExitTransition(equip, locomotion, 0.05f, report);
        EnsureExitTransition(block, guard, 0.04f, report);
        EnsureExitTransition(movingBlock, guard, 0.04f, report);
        EnsureExitTransition(roll, locomotion, 0.06f, report);
        EnsureExitTransition(jump, locomotion, 0.06f, report);
        EnsureExitTransition(hit01, locomotion, 0.06f, report);
        EnsureExitTransition(hit02, locomotion, 0.06f, report);
        EnsureExitTransition(hit03, locomotion, 0.06f, report);

        EnsureTransitionLowerLayer(controller, transitionLowerMask, report);
        RemoveBaseCombatStates(controller, report);
        RemoveLegacyAnimatorSubAssets(report);
    }

    private static AnimatorControllerLayer EnsureLayer(AnimatorController controller, string layerName, StringBuilder report)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name == layerName)
            {
                layers[i].defaultWeight = 0f;
                layers[i].blendingMode = AnimatorLayerBlendingMode.Override;
                layers[i].avatarMask = null;
                layers[i].iKPass = true;
                controller.layers = layers;
                report.AppendLine("LayerExists=" + layerName);
                return layers[i];
            }
        }

        AnimatorStateMachine stateMachine = new AnimatorStateMachine
        {
            name = layerName + "_StateMachine",
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(stateMachine, controller);

        AnimatorControllerLayer layer = new AnimatorControllerLayer
        {
            name = layerName,
            defaultWeight = 0f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = null,
            iKPass = true,
            stateMachine = stateMachine
        };

        Array.Resize(ref layers, layers.Length + 1);
        layers[layers.Length - 1] = layer;
        controller.layers = layers;
        report.AppendLine("LayerCreated=" + layerName);
        return layer;
    }

    private static void ValidateAttackStates(AnimatorController controller, StringBuilder report)
    {
        AnimatorControllerLayer layer = FindLayer(controller, CombatLayerName);
        if (layer == null || layer.stateMachine == null)
            throw new MissingReferenceException("[OneHandSwordCombatAnimator] Combat layer is missing.");

        AnimatorState state = FindState(layer.stateMachine, AttackStateName);
        AnimationClip expectedClip = LoadPrimaryComboClip(0);
        if (state == null || state.motion != expectedClip)
        {
            throw new InvalidOperationException(
                "[OneHandSwordCombatAnimator] Attack state wiring is invalid: " + AttackStateName);
        }

        if (!state.speedParameterActive || state.speedParameter != ActionSpeedParameter)
        {
            throw new InvalidOperationException(
                "[OneHandSwordCombatAnimator] Attack speed parameter is invalid: " + AttackStateName);
        }

        report.AppendLine("AttackStateValidated=" + AttackStateName + ", Template=" + expectedClip.name);
    }

    private static void RemoveLegacyMeleeStates(AnimatorStateMachine stateMachine, StringBuilder report)
    {
        ChildAnimatorState[] states = stateMachine.states;
        for (int i = states.Length - 1; i >= 0; i--)
        {
            AnimatorState state = states[i].state;
            if (state == null || !state.name.StartsWith("OHS_", StringComparison.Ordinal))
                continue;

            string legacyName = state.name;
            stateMachine.RemoveState(state);
            report.AppendLine("LegacyMeleeStateRemoved=" + legacyName);
        }
    }

    private static void EnsureTransitionLowerLayer(AnimatorController controller, AvatarMask transitionLowerMask, StringBuilder report)
    {
        AnimatorControllerLayer layer = EnsureLayer(controller, TransitionLowerLayerName, report);
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name != TransitionLowerLayerName)
                continue;

            layers[i].defaultWeight = 0f;
            layers[i].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[i].avatarMask = transitionLowerMask;
            layers[i].iKPass = true;
            controller.layers = layers;
            layer = layers[i];
            report.AppendLine("TransitionLowerLayerMask=" + (transitionLowerMask != null ? transitionLowerMask.name : "null"));
            break;
        }

        AnimatorStateMachine stateMachine = layer.stateMachine;
        AnimatorState empty = EnsureState(stateMachine, TransitionLowerEmptyStateName, new Vector3(80f, 460f, 0f), report);
        empty.motion = null;
        stateMachine.defaultState = empty;

        AnimatorState locomotion = EnsureState(stateMachine, TransitionLowerLocomotionStateName, new Vector3(360f, 460f, 0f), report);
        ConfigureTransitionLowerLocomotionTree(controller, locomotion, report);
    }

    private static void RemoveLayerIfExists(AnimatorController controller, string layerName, StringBuilder report)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name != layerName)
                continue;

            AnimatorStateMachine stateMachine = layers[i].stateMachine;
            controller.RemoveLayer(i);
            if (stateMachine != null)
                UnityEngine.Object.DestroyImmediate(stateMachine, true);

            report.AppendLine("LayerRemoved=" + layerName);
            return;
        }

        report.AppendLine("LayerAlreadyMissing=" + layerName);
    }

    private static void RemoveBaseCombatStates(AnimatorController controller, StringBuilder report)
    {
        AnimatorControllerLayer baseLayer = controller.layers.Length > 0 ? controller.layers[0] : null;
        if (baseLayer == null || baseLayer.stateMachine == null)
            return;

        RemoveStateByName(baseLayer.stateMachine, "CombatLocomotion", report);
        RemoveStateByName(baseLayer.stateMachine, "CombatGuardLocomotion", report);
    }

    private static void RemoveStateByName(AnimatorStateMachine stateMachine, string stateName, StringBuilder report)
    {
        AnimatorState state = FindState(stateMachine, stateName);
        if (state == null)
        {
            report.AppendLine("BaseStateAlreadyMissing=" + stateName);
            return;
        }

        RemoveTransitionsTargeting(stateMachine, state);
        if (state.motion is BlendTree tree)
            UnityEngine.Object.DestroyImmediate(tree, true);

        stateMachine.RemoveState(state);
        report.AppendLine("BaseStateRemoved=" + stateName);
    }

    private static void RemoveTransitionsTargeting(AnimatorStateMachine stateMachine, AnimatorState targetState)
    {
        ChildAnimatorState[] states = stateMachine.states;
        for (int i = 0; i < states.Length; i++)
        {
            AnimatorState state = states[i].state;
            if (state == null)
                continue;

            AnimatorStateTransition[] transitions = state.transitions;
            for (int j = transitions.Length - 1; j >= 0; j--)
            {
                if (transitions[j] != null && transitions[j].destinationState == targetState)
                    state.RemoveTransition(transitions[j]);
            }
        }
    }

    private static AnimatorState EnsureClipState(AnimatorStateMachine stateMachine, string stateName, string clipName, Vector3 position, StringBuilder report)
    {
        AnimatorState state = EnsureState(stateMachine, stateName, position, report);
        state.motion = LoadWarriorClip(clipName);
        ConfigureStateDefaults(state);
        report.AppendLine(stateName + "/Clip=" + clipName);
        return state;
    }

    private static AnimatorState EnsureAssetClipState(AnimatorStateMachine stateMachine, string stateName, AnimationClip clip, Vector3 position, StringBuilder report)
    {
        AnimatorState state = EnsureState(stateMachine, stateName, position, report);
        state.motion = clip;
        ConfigureStateDefaults(state);
        report.AppendLine(stateName + "/Clip=" + (clip != null ? AssetDatabase.GetAssetPath(clip) : "null"));
        return state;
    }

    private static AnimatorState EnsureState(AnimatorStateMachine stateMachine, string stateName, Vector3 position, StringBuilder report)
    {
        AnimatorState existing = FindState(stateMachine, stateName);
        if (existing != null)
        {
            ConfigureStateDefaults(existing);
            report.AppendLine("StateExists=" + stateName);
            return existing;
        }

        AnimatorState state = stateMachine.AddState(stateName, position);
        ConfigureStateDefaults(state);
        report.AppendLine("StateCreated=" + stateName);
        return state;
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        ChildAnimatorState[] states = stateMachine.states;
        for (int i = 0; i < states.Length; i++)
        {
            AnimatorState state = states[i].state;
            if (state != null && state.name == stateName)
                return state;
        }

        return null;
    }

    private static void ConfigureStateDefaults(AnimatorState state)
    {
        state.speed = 1f;
        state.iKOnFeet = true;
        state.writeDefaultValues = true;
    }

    private static void SetStateSpeed(AnimatorState state, float speed, StringBuilder report)
    {
        if (state == null)
            return;

        state.speed = Mathf.Max(0.01f, speed);
        report.AppendLine("StateSpeed/" + state.name + "=" + state.speed.ToString("0.###"));
    }

    private static void ConfigureActionSpeedState(AnimatorState state, StringBuilder report)
    {
        if (state == null)
            return;

        state.speed = 1f;
        state.speedParameterActive = true;
        state.speedParameter = ActionSpeedParameter;
        report.AppendLine("StateSpeedParameter/" + state.name + "=" + ActionSpeedParameter);
    }

    private static void ConfigureLocomotionTree(AnimatorController controller, AnimatorState state, StringBuilder report)
    {
        ConfigureWalk8WayTree(controller, state, "Melee_Locomotion_Tree", report);
    }

    private static void ConfigureTransitionLowerLocomotionTree(AnimatorController controller, AnimatorState state, StringBuilder report)
    {
        ConfigureWalk8WayTree(controller, state, "Melee_TransitionLower_Locomotion_Tree", report);
    }

    private static void ConfigureWalk8WayTree(AnimatorController controller, AnimatorState state, string treeName, StringBuilder report)
    {
        BlendTree tree = EnsureBlendTree(controller, state, treeName);
        ConfigureDirectionalTree(tree);
        tree.children = Array.Empty<ChildMotion>();

        AddChild(tree, "Frank_RPG_Warrior_Idle", 0f, 0f, report);
        AddChild(tree, "Frank_RPG_Warrior_8Way_Walk_F", 0f, 1f, report);
        AddChild(tree, "Frank_RPG_Warrior_8Way_Walk_B", 0f, -1f, report);
        AddChild(tree, "Frank_RPG_Warrior_8Way_Walk_L", -1f, 0f, report);
        AddChild(tree, "Frank_RPG_Warrior_8Way_Walk_R", 1f, 0f, report);
        AddChild(tree, "Frank_RPG_Warrior_8Way_Walk_FL", -0.707f, 0.707f, report);
        AddChild(tree, "Frank_RPG_Warrior_8Way_Walk_FR", 0.707f, 0.707f, report);
        AddChild(tree, "Frank_RPG_Warrior_8Way_Walk_BL", -0.707f, -0.707f, report);
        AddChild(tree, "Frank_RPG_Warrior_8Way_Walk_BR", 0.707f, -0.707f, report);
        EditorUtility.SetDirty(tree);
    }

    private static void ConfigureExistingLocomotionTrees(AnimatorController controller, StringBuilder report)
    {
        AnimatorControllerLayer combatLayer = FindLayer(controller, CombatLayerName);
        AnimatorControllerLayer transitionLayer = FindLayer(controller, TransitionLowerLayerName);
        if (combatLayer == null || transitionLayer == null)
            throw new MissingReferenceException("[OneHandSwordCombatAnimator] Required combat layers are missing.");

        AnimatorState locomotion = FindState(combatLayer.stateMachine, "Melee_Locomotion");
        AnimatorState transitionLocomotion = FindState(transitionLayer.stateMachine, TransitionLowerLocomotionStateName);
        if (locomotion == null || transitionLocomotion == null)
            throw new MissingReferenceException("[OneHandSwordCombatAnimator] Required locomotion states are missing.");

        ConfigureLocomotionTree(controller, locomotion, report);
        ConfigureTransitionLowerLocomotionTree(controller, transitionLocomotion, report);
    }

    private static AnimatorControllerLayer FindLayer(AnimatorController controller, string layerName)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name == layerName)
                return layers[i];
        }

        return null;
    }

    private static void ConfigureGuardTree(AnimatorController controller, AnimatorState state, StringBuilder report)
    {
        BlendTree tree = EnsureBlendTree(controller, state, "Melee_GuardLocomotion_Tree");
        ConfigureDirectionalTree(tree);
        tree.children = Array.Empty<ChildMotion>();

        AddChild(tree, "Frank_RPG_Warrior_Guard", 0f, 0f, report);
        AddChild(tree, "Frank_RPG_Warrior_4Way_Guard_Walk_F", 0f, 1f, report);
        AddChild(tree, "Frank_RPG_Warrior_4Way_Guard_Walk_B", 0f, -1f, report);
        AddChild(tree, "Frank_RPG_Warrior_4Way_Guard_Walk_L", -1f, 0f, report);
        AddChild(tree, "Frank_RPG_Warrior_4Way_Guard_Walk_R", 1f, 0f, report);
        EditorUtility.SetDirty(tree);
    }

    private static BlendTree EnsureBlendTree(AnimatorController controller, AnimatorState state, string treeName)
    {
        if (state.motion is BlendTree existing)
        {
            existing.name = treeName;
            return existing;
        }

        if (state.motion is BlendTree oldTree)
            UnityEngine.Object.DestroyImmediate(oldTree, true);

        BlendTree tree = new BlendTree
        {
            name = treeName,
            hideFlags = HideFlags.HideInHierarchy
        };

        AssetDatabase.AddObjectToAsset(tree, controller);
        state.motion = tree;
        return tree;
    }

    private static void ConfigureDirectionalTree(BlendTree tree)
    {
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = MoveXParameter;
        tree.blendParameterY = MoveYParameter;
        tree.useAutomaticThresholds = false;
    }

    private static void AddChild(BlendTree tree, string clipName, float x, float y, StringBuilder report)
    {
        AnimationClip clip = LoadWarriorClip(clipName);
        if (clip == null)
        {
            report.AppendLine("MissingClip=" + clipName);
            return;
        }

        tree.AddChild(clip, new Vector2(x, y));
        report.AppendLine(tree.name + "/" + clipName + "=" + AssetDatabase.GetAssetPath(clip));
    }

    private static void EnsureExitTransition(AnimatorState from, AnimatorState to, float duration, StringBuilder report)
    {
        if (from == null || to == null)
            return;

        AnimatorStateTransition[] transitions = from.transitions;
        for (int i = 0; i < transitions.Length; i++)
        {
            if (transitions[i] != null && transitions[i].destinationState == to)
            {
                ConfigureExitTransition(transitions[i], duration);
                report.AppendLine("TransitionExists=" + from.name + "->" + to.name);
                return;
            }
        }

        AnimatorStateTransition transition = from.AddTransition(to);
        ConfigureExitTransition(transition, duration);
        report.AppendLine("TransitionCreated=" + from.name + "->" + to.name);
    }

    private static void ConfigureExitTransition(AnimatorStateTransition transition, float duration)
    {
        transition.hasExitTime = true;
        transition.exitTime = 0.95f;
        transition.hasFixedDuration = true;
        transition.duration = Mathf.Max(0f, duration);
        transition.offset = 0f;
        transition.interruptionSource = TransitionInterruptionSource.None;
        transition.canTransitionToSelf = false;
    }

    private static void ConfigurePlayerPrefab(WeaponCombatAnimationProfile profile, StringBuilder report)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            WeaponCombatAnimatorRouter router = root.GetComponent<WeaponCombatAnimatorRouter>();
            if (router == null)
            {
                router = root.AddComponent<WeaponCombatAnimatorRouter>();
                report.AppendLine("PrefabComponentAdded=WeaponCombatAnimatorRouter");
            }

            MeleeWeaponCombatAnimatorDriver driver = root.GetComponent<MeleeWeaponCombatAnimatorDriver>();
            if (driver == null)
            {
                driver = root.AddComponent<MeleeWeaponCombatAnimatorDriver>();
                report.AppendLine("PrefabComponentAdded=MeleeWeaponCombatAnimatorDriver");
            }

            Animator animator = root.GetComponentInChildren<Animator>(true);
            PlayerMovement movement = root.GetComponent<PlayerMovement>();
            PlayerEquipment equipment = root.GetComponent<PlayerEquipment>();
            PlayerAnimation animation = root.GetComponent<PlayerAnimation>();
            PlayerEvadeController evadeController = root.GetComponent<PlayerEvadeController>();

            ConfigureCombatMovementSpeeds(movement, report);
            ConfigureBakedAttackRootMotion(animator, report);

            SerializedObject routerSerialized = new SerializedObject(router);
            SetObject(routerSerialized, "targetAnimator", animator, report);
            SetObject(routerSerialized, "playerMovement", movement, report);
            SetObject(routerSerialized, "playerEquipment", equipment, report);
            SetObject(routerSerialized, "meleeWeaponDriver", driver, report);
            routerSerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject driverSerialized = new SerializedObject(driver);
            SetObject(driverSerialized, "targetAnimator", animator, report);
            SetObject(driverSerialized, "playerMovement", movement, report);
            SetObject(driverSerialized, "playerEquipment", equipment, report);
            SetObject(driverSerialized, "fallbackProfile", profile, report);
            SetString(driverSerialized, "layerName", CombatLayerName, report);
            SetString(driverSerialized, "transitionLowerLayerName", TransitionLowerLayerName, report);
            SetString(driverSerialized, "emptyStateName", "Melee_Empty", report);
            SetString(driverSerialized, "equipStateName", "Melee_Equip", report);
            SetString(driverSerialized, "locomotionStateName", "Melee_Locomotion", report);
            SetString(driverSerialized, "guardLocomotionStateName", "Melee_GuardLocomotion", report);
            SetString(driverSerialized, "unequipStateName", "Melee_Unequip", report);
            SetString(driverSerialized, "blockStateName", "Melee_Block", report);
            SetString(driverSerialized, "movingBlockStateName", "Melee_MovingBlock", report);
            SetString(driverSerialized, "hitStateNamePrefix", "Melee_Hit", report);
            SetString(driverSerialized, "jumpStateName", "Melee_Jump", report);
            SetString(driverSerialized, "rollStateName", "Melee_Roll", report);
            SetString(driverSerialized, "attackStateName", AttackStateName, report);
            SetString(driverSerialized, "transitionLowerEmptyStateName", TransitionLowerEmptyStateName, report);
            SetString(driverSerialized, "transitionLowerLocomotionStateName", TransitionLowerLocomotionStateName, report);
            SetString(driverSerialized, "actionSpeedParameterName", ActionSpeedParameter, report);
            SetFloat(driverSerialized, "transitionLowerLayerFadeDuration", 0.05f, report);
            SetFloat(driverSerialized, "transitionLowerMoveInputThreshold", 0.05f, report);
            SetFloat(driverSerialized, "equipAnimationSpeedMultiplier", EquipSpeedMultiplier, report);
            SetFloat(driverSerialized, "unequipAnimationSpeedMultiplier", UnequipSpeedMultiplier, report);
            SetFloat(driverSerialized, "unequipAnimationStartOffsetSeconds", UnequipStartOffsetSeconds, report);
            driverSerialized.ApplyModifiedPropertiesWithoutUndo();

            if (animation != null)
            {
                SerializedObject animationSerialized = new SerializedObject(animation);
                SetBool(animationSerialized, "useWeaponCombatAnimatorRouter", true, report);
                SetBool(animationSerialized, "useLegacyCombatBaseLayerStates", false, report);
                SetBool(animationSerialized, "useLegacyWeaponAimLayers", false, report);
                SetObject(animationSerialized, "weaponCombatAnimatorRouter", router, report);
                animationSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (evadeController != null)
            {
                SerializedObject evadeSerialized = new SerializedObject(evadeController);
                SetObject(evadeSerialized, "rollAnimationClip", LoadRollClip(report), report);
                evadeSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            report.AppendLine("PlayerPrefabSaved=" + PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureCombatMovementSpeeds(StringBuilder report)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            ConfigureCombatMovementSpeeds(root.GetComponent<PlayerMovement>(), report);
            ConfigureBakedAttackRootMotion(root.GetComponentInChildren<Animator>(true), report);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureCombatMovementSpeeds(PlayerMovement movement, StringBuilder report)
    {
        if (movement == null)
            throw new MissingComponentException("[OneHandSwordCombatAnimator] PlayerMovement is missing from " + PlayerPrefabPath);

        SerializedObject serialized = new SerializedObject(movement);
        SetFloat(serialized, "meleeCombatMoveSpeed", CombatMoveSpeed, report);
        SetFloat(serialized, "meleeCombatGuardMoveSpeed", CombatGuardMoveSpeed, report);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(movement);
    }

    private static void ConfigureBakedAttackRootMotion(Animator animator, StringBuilder report)
    {
        if (animator == null)
            throw new MissingComponentException("[OneHandSwordCombatAnimator] Animator is missing from " + PlayerPrefabPath);

        animator.applyRootMotion = false;
        EditorUtility.SetDirty(animator);
        report.AppendLine("AnimatorRootMotion=Disabled, Rotation=BakedIntoPose");
    }

    private static void ConfigureSwordAsset(WeaponCombatAnimationProfile profile, StringBuilder report)
    {
        WeaponItemData sword = AssetDatabase.LoadAssetAtPath<WeaponItemData>(SwordAssetPath);
        if (sword == null)
        {
            report.AppendLine("SwordAssetMissing=" + SwordAssetPath);
            return;
        }

        MeleeWeaponDefinition meleeDefinition = sword.GetMeleeDefinition();
        if (meleeDefinition == null)
            throw new MissingReferenceException("Sword melee definition is missing.");

        meleeDefinition.combatStyle = WeaponCombatStyle.MeleeWeapon;
        meleeDefinition.animationProfile = profile;
        meleeDefinition.aim.poseBodyMode = WeaponAimPoseBodyMode.None;
        meleeDefinition.aim.upperBodyChannel = WeaponUpperBodyAimChannel.None;
        meleeDefinition.aim.usesUpperBodyPose = false;
        EditorUtility.SetDirty(meleeDefinition);
        EditorUtility.SetDirty(sword);
        report.AppendLine("SwordAssetConfigured=" + SwordAssetPath);
    }

    private static void AssignDriverClip(SerializedObject serialized, string propertyName, string clipName, StringBuilder report)
    {
        SetObject(serialized, propertyName, LoadWarriorClip(clipName), report);
    }

    private static void AssignDirectionalClipSet(SerializedObject serialized, string propertyName, string[] clipNames, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        string[] fieldNames = clipNames.Length == 8
            ? new[] { "forward", "backward", "left", "right", "forwardLeft", "forwardRight", "backwardLeft", "backwardRight" }
            : new[] { "forward", "backward", "left", "right" };
        if (property == null || clipNames.Length != fieldNames.Length)
        {
            report.AppendLine("DirectionalPropertyInvalid=" + propertyName);
            return;
        }

        for (int i = 0; i < clipNames.Length; i++)
            property.FindPropertyRelative(fieldNames[i]).objectReferenceValue = LoadWarriorClip(clipNames[i]);

        report.AppendLine("DirectionalPropertyConfigured=" + propertyName);
    }

    private static void AssignClipArray(SerializedObject serialized, string propertyName, string[] clipNames, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || !property.isArray)
        {
            report.AppendLine("ArrayPropertyMissing=" + propertyName);
            return;
        }

        property.arraySize = clipNames.Length;
        for (int i = 0; i < clipNames.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = LoadWarriorClip(clipNames[i]);

        report.AppendLine("ArrayPropertyConfigured=" + propertyName);
    }

    private static AnimationClip LoadPrimaryComboClip(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= PrimaryComboClipNames.Length)
            return null;

        string clipName = PrimaryComboClipNames[stepIndex];
        string path = PrimaryComboFolder + "/" + clipName + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
            throw new MissingReferenceException("[OneHandSwordCombatAnimator] Primary combo clip missing: " + path);

        return clip;
    }

    private static void EnsureLoopingClips(StringBuilder report)
    {
        for (int i = 0; i < LoopingInPlaceClipNames.Length; i++)
        {
            string clipName = LoopingInPlaceClipNames[i];
            EnsureClipLoops(WarriorInPlaceFolder + "/" + clipName + ".FBX", clipName, report);
        }

        for (int i = 0; i < AimRelativeWalkClipNames.Length; i++)
            EnsureAimRelativeLocomotionImport(AimRelativeWalkClipNames[i], report);

        for (int i = 0; i < AimRelativeGuardClipNames.Length; i++)
            EnsureAimRelativeLocomotionImport(AimRelativeGuardClipNames[i], report);

        for (int i = 0; i < AimRelativeGuardReactionClipNames.Length; i++)
            EnsureAimRelativeLocomotionImport(AimRelativeGuardReactionClipNames[i], report);
    }

    private static void EnsureAimRelativeLocomotionImport(string clipName, StringBuilder report)
    {
        string path = WarriorInPlaceFolder + "/" + clipName + ".FBX";
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            report.AppendLine("AimRelativeImportMissing=" + path);
            return;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        bool found = false;
        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            if (!string.Equals(clips[i].name, clipName, StringComparison.OrdinalIgnoreCase))
                continue;

            found = true;
            if (!clips[i].lockRootRotation)
            {
                clips[i].lockRootRotation = true;
                changed = true;
            }

            if (!clips[i].keepOriginalOrientation)
            {
                clips[i].keepOriginalOrientation = true;
                changed = true;
            }

            if (!clips[i].lockRootPositionXZ)
            {
                clips[i].lockRootPositionXZ = true;
                changed = true;
            }

            if (!clips[i].keepOriginalPositionXZ)
            {
                clips[i].keepOriginalPositionXZ = true;
                changed = true;
            }
            break;
        }

        if (!found)
        {
            report.AppendLine("AimRelativeClipMissing=" + clipName);
            return;
        }

        if (!changed)
        {
            report.AppendLine("AimRelativeImportAlreadyOk=" + clipName);
            return;
        }

        importer.clipAnimations = clips;
        EditorUtility.SetDirty(importer);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        report.AppendLine("AimRelativeImportUpdated=" + clipName);
    }

    private static void EnsureClipLoops(string path, string clipName, StringBuilder report)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            report.AppendLine("LoopImportMissing=" + path);
            return;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            if (!string.Equals(clips[i].name, clipName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!clips[i].loopTime)
            {
                clips[i].loopTime = true;
                changed = true;
            }

            if (!clips[i].loopPose)
            {
                clips[i].loopPose = true;
                changed = true;
            }

            if (clips[i].wrapMode != WrapMode.Loop)
            {
                clips[i].wrapMode = WrapMode.Loop;
                changed = true;
            }

            break;
        }

        if (!changed)
        {
            report.AppendLine("LoopImportAlreadyOk=" + clipName);
            return;
        }

        importer.clipAnimations = clips;
        EditorUtility.SetDirty(importer);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        report.AppendLine("LoopImportUpdated=" + clipName);
    }

    private static AnimationClip EnsureCombatEquipExitClip(StringBuilder report)
    {
        AnimationClip sourceClip = LoadWarriorClip("Frank_RPG_Warrior_Equip");
        if (sourceClip == null)
        {
            report.AppendLine("CombatEquipExitSourceMissing=Frank_RPG_Warrior_Equip");
            return null;
        }

        AnimationClip reversedClip = CreateReversedClip(sourceClip, "Frank_RPG_Warrior_Equip_Reverse");
        AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CombatEquipExitClipPath);
        if (existingClip == null)
        {
            AssetDatabase.CreateAsset(reversedClip, CombatEquipExitClipPath);
            existingClip = reversedClip;
            report.AppendLine("CombatEquipExitClipCreated=" + CombatEquipExitClipPath);
        }
        else
        {
            EditorUtility.CopySerialized(reversedClip, existingClip);
            existingClip.name = "Frank_RPG_Warrior_Equip_Reverse";
            EditorUtility.SetDirty(existingClip);
            report.AppendLine("CombatEquipExitClipUpdated=" + CombatEquipExitClipPath);
        }

        return existingClip;
    }

    private static AnimationClip CreateReversedClip(AnimationClip sourceClip, string clipName)
    {
        AnimationClip reversedClip = new AnimationClip
        {
            name = clipName,
            frameRate = sourceClip.frameRate,
            legacy = sourceClip.legacy,
            wrapMode = sourceClip.wrapMode
        };

        float length = Mathf.Max(0f, sourceClip.length);
        EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(sourceClip);
        for (int i = 0; i < curveBindings.Length; i++)
        {
            AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(sourceClip, curveBindings[i]);
            AnimationUtility.SetEditorCurve(reversedClip, curveBindings[i], ReverseCurve(sourceCurve, length));
        }

        EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(sourceClip);
        for (int i = 0; i < objectBindings.Length; i++)
        {
            UnityEditor.ObjectReferenceKeyframe[] sourceFrames = AnimationUtility.GetObjectReferenceCurve(sourceClip, objectBindings[i]);
            AnimationUtility.SetObjectReferenceCurve(reversedClip, objectBindings[i], ReverseObjectReferenceFrames(sourceFrames, length));
        }

        AnimationEvent[] sourceEvents = AnimationUtility.GetAnimationEvents(sourceClip);
        AnimationUtility.SetAnimationEvents(reversedClip, ReverseEvents(sourceEvents, length));
        AnimationUtility.SetAnimationClipSettings(reversedClip, AnimationUtility.GetAnimationClipSettings(sourceClip));
        reversedClip.EnsureQuaternionContinuity();
        return reversedClip;
    }

    private static AnimationCurve ReverseCurve(AnimationCurve sourceCurve, float length)
    {
        if (sourceCurve == null)
            return null;

        Keyframe[] sourceKeys = sourceCurve.keys;
        Keyframe[] reversedKeys = new Keyframe[sourceKeys.Length];
        for (int i = 0; i < sourceKeys.Length; i++)
        {
            Keyframe sourceKey = sourceKeys[i];
            Keyframe reversedKey = new Keyframe(
                Mathf.Max(0f, length - sourceKey.time),
                sourceKey.value,
                -sourceKey.outTangent,
                -sourceKey.inTangent,
                sourceKey.outWeight,
                sourceKey.inWeight)
            {
                weightedMode = sourceKey.weightedMode
            };

            reversedKeys[sourceKeys.Length - 1 - i] = reversedKey;
        }

        return new AnimationCurve(reversedKeys)
        {
            preWrapMode = sourceCurve.postWrapMode,
            postWrapMode = sourceCurve.preWrapMode
        };
    }

    private static UnityEditor.ObjectReferenceKeyframe[] ReverseObjectReferenceFrames(UnityEditor.ObjectReferenceKeyframe[] sourceFrames, float length)
    {
        if (sourceFrames == null)
            return Array.Empty<UnityEditor.ObjectReferenceKeyframe>();

        UnityEditor.ObjectReferenceKeyframe[] reversedFrames = new UnityEditor.ObjectReferenceKeyframe[sourceFrames.Length];
        for (int i = 0; i < sourceFrames.Length; i++)
        {
            UnityEditor.ObjectReferenceKeyframe frame = sourceFrames[i];
            frame.time = Mathf.Max(0f, length - frame.time);
            reversedFrames[sourceFrames.Length - 1 - i] = frame;
        }

        return reversedFrames;
    }

    private static AnimationEvent[] ReverseEvents(AnimationEvent[] sourceEvents, float length)
    {
        if (sourceEvents == null)
            return Array.Empty<AnimationEvent>();

        AnimationEvent[] reversedEvents = new AnimationEvent[sourceEvents.Length];
        for (int i = 0; i < sourceEvents.Length; i++)
        {
            AnimationEvent sourceEvent = sourceEvents[i];
            reversedEvents[sourceEvents.Length - 1 - i] = new AnimationEvent
            {
                time = Mathf.Max(0f, length - sourceEvent.time),
                functionName = sourceEvent.functionName,
                stringParameter = sourceEvent.stringParameter,
                floatParameter = sourceEvent.floatParameter,
                intParameter = sourceEvent.intParameter,
                objectReferenceParameter = sourceEvent.objectReferenceParameter,
                messageOptions = sourceEvent.messageOptions
            };
        }

        return reversedEvents;
    }

    private static AnimationClip LoadWarriorClip(string clipName)
    {
        string path = WarriorInPlaceFolder + "/" + clipName + ".FBX";
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip != null && string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                return clip;
        }

        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    private static AnimationClip LoadRollClip(StringBuilder report)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RollClipPath);
        report.AppendLine((clip != null ? "RollClip=" : "RollClipMissing=") + RollClipPath);
        return clip;
    }

    private static void EnsureParameter(AnimatorController controller, string parameterName, AnimatorControllerParameterType parameterType, StringBuilder report)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == parameterName)
            {
                report.AppendLine("ParameterExists=" + parameterName);
                return;
            }
        }

        controller.AddParameter(parameterName, parameterType);
        report.AppendLine("ParameterCreated=" + parameterName);
    }

    private static void RemoveParameterIfExists(AnimatorController controller, string parameterName, StringBuilder report)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name != parameterName)
                continue;

            controller.RemoveParameter(i);
            report.AppendLine("LegacyParameterRemoved=" + parameterName);
            return;
        }
    }

    private static void RemoveLegacyAnimatorSubAssets(StringBuilder report)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ControllerPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is not BlendTree tree
                || !tree.name.StartsWith("OHS_", StringComparison.Ordinal))
            {
                continue;
            }

            string legacyName = tree.name;
            UnityEngine.Object.DestroyImmediate(tree, true);
            report.AppendLine("LegacyBlendTreeRemoved=" + legacyName);
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static void SetString(SerializedObject serialized, string propertyName, string value, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            report.AppendLine("StringPropertyMissing=" + propertyName);
            return;
        }

        property.stringValue = value;
        report.AppendLine("StringProperty/" + propertyName + "=" + value);
    }

    private static void SetFloat(SerializedObject serialized, string propertyName, float value, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            report.AppendLine("FloatPropertyMissing=" + propertyName);
            return;
        }

        property.floatValue = value;
        report.AppendLine("FloatProperty/" + propertyName + "=" + value.ToString("0.###"));
    }

    private static void SetBool(SerializedObject serialized, string propertyName, bool value, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            report.AppendLine("BoolPropertyMissing=" + propertyName);
            return;
        }

        property.boolValue = value;
        report.AppendLine("BoolProperty/" + propertyName + "=" + value);
    }

    private static void SetEnum(SerializedObject serialized, string propertyName, int value, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            report.AppendLine("EnumPropertyMissing=" + propertyName);
            return;
        }

        property.intValue = value;
        report.AppendLine("EnumProperty/" + propertyName + "=" + value);
    }

    private static void SetObject(SerializedObject serialized, string propertyName, UnityEngine.Object value, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            report.AppendLine("ObjectPropertyMissing=" + propertyName);
            return;
        }

        property.objectReferenceValue = value;
        report.AppendLine("ObjectProperty/" + propertyName + "=" + (value != null ? value.name : "null"));
    }

    private static void WriteReport(StringBuilder report)
    {
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, report.ToString(), Encoding.UTF8);
        Debug.Log("[OneHandSwordCombatAnimator] Wrote report: " + fullPath);
    }
}
