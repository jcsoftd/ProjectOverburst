using System;
using UnityEditor;
using UnityEngine;

public static class MeleeSpecialAttackVfxSetupUtility
{
    private const float DefaultAttackVfxScale = 1f;
    private const float GreatswordGroundSlamForwardOffset = 2.5f;
    private const float GreatswordGroundSlamRadius = 2.3f;
    private const float GreatswordBaseRange = 3f;
    private const float GroundSlamImpactScaleMultiplier = 1.5f;
    private const string SourceFolder =
        "Assets/ThirdParty/06_VFX/Hovl Studio/Sword slash VFX/Prefabs";
    private const string SharedVfxFolder = "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX";
    private const string ThrustFolder = SharedVfxFolder + "/Thrust";
    private const string GroundSlamFolder = SharedVfxFolder + "/GroundSlam";

    private const string BasicSlashDefinitionPath =
        SharedVfxFolder + "/MeleeSlash/VFX_MeleeSlash_Horizontal.asset";
    private const string CircularSlashDefinitionPath =
        SharedVfxFolder + "/MeleeSlash/VFX_MeleeSlash_Circular.asset";
    private const string VerticalRisingDefinitionPath =
        SharedVfxFolder + "/MeleeSlash/VFX_MeleeSlash_VerticalRising.asset";
    private const string VerticalFallingDefinitionPath =
        SharedVfxFolder + "/MeleeSlash/VFX_MeleeSlash_VerticalFalling.asset";

    private const string PrickSourcePath = SourceFolder + "/Prick 2.prefab";
    private const string PrickPrefabPath = ThrustFolder + "/PF_VFX_Prick02.prefab";
    private const string PrickDefinitionPath = ThrustFolder + "/VFX_Prick02.asset";

    private const string SpikesSourcePath = SourceFolder + "/Spikes attack.prefab";
    private const string SpikesPrefabPath = GroundSlamFolder + "/PF_VFX_SpikesAttack.prefab";
    private const string SpikesDefinitionPath = GroundSlamFolder + "/VFX_SpikesAttack.asset";

    private const string GroundCrackSourcePath = SourceFolder + "/Sword Slash 5_1.prefab";
    private const string GroundCrackPrefabPath = GroundSlamFolder + "/PF_VFX_GroundSlamCrack.prefab";
    private const string GroundCrackDefinitionPath = GroundSlamFolder + "/VFX_GroundSlamCrack.asset";

    private const string OneHandSwordComboPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/Combos/OneHandSwordPrimaryCombo.asset";
    private const string GreatswordComboPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Combos/GreatswordComboSet01.asset";

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Melee Special Attack VFX")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        EnsureFolder(ThrustFolder);
        EnsureFolder(GroundSlamFolder);

        GameObject prickPrefab = EnsureOwnedPrefab(
            PrickSourcePath,
            PrickPrefabPath,
            "PF_VFX_Prick02");
        GameObject spikesPrefab = EnsureOwnedPrefab(
            SpikesSourcePath,
            SpikesPrefabPath,
            "PF_VFX_SpikesAttack");
        GameObject groundCrackPrefab = EnsureExtractedChildPrefab(
            GroundCrackSourcePath,
            "GroundCrack",
            GroundCrackPrefabPath,
            "PF_VFX_GroundSlamCrack");

        EnsureDefinition(
            PrickDefinitionPath,
            "VFX_Prick02",
            prickPrefab,
            new Vector3(0f, 1f, 0f),
            Vector3.zero);
        EnsureDefinition(
            SpikesDefinitionPath,
            "VFX_SpikesAttack",
            spikesPrefab,
            Vector3.zero,
            Vector3.zero);
        EnsureDefinition(
            GroundCrackDefinitionPath,
            "VFX_GroundSlamCrack",
            groundCrackPrefab,
            Vector3.zero,
            new Vector3(0f, 90f, 0f));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        MeleeSlashSetupUtility.RunFromCommandLine();

        ApplySpecialCues(
            OneHandSwordComboPath,
            applyCircularSlash: true,
            applyThrust: false,
            applyGroundSlam: false);
        ApplySpecialCues(
            GreatswordComboPath,
            applyCircularSlash: true,
            applyThrust: false,
            applyGroundSlam: true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate();
        Debug.Log("[MeleeSpecialAttackVfxSetup] Circular-slash and ground-slam VFX are ready.");
    }

    [MenuItem("OVERBURST/Codex/Setup/Combat/Separate Ground Slam Slash And Impact")]
    public static void ApplyGroundSlamSeparationFromMenu()
    {
        ApplyGroundSlamSeparationFromCommandLine();
    }

    public static void ApplyGroundSlamSeparationFromCommandLine()
    {
        MeleeSlashSetupUtility.RunFromCommandLine();
    }

    [MenuItem("OVERBURST/Codex/Setup/Combat/Apply Greatsword Ground Slam Placement")]
    public static void ApplyGreatswordGroundSlamPlacementFromMenu()
    {
        ApplyGreatswordGroundSlamPlacementFromCommandLine();
    }

    public static void ApplyGreatswordGroundSlamPlacementFromCommandLine()
    {
        MeleeComboDefinition combo =
            AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(GreatswordComboPath);
        if (combo == null || combo.steps == null)
            throw new MissingReferenceException("Greatsword combo is missing.");

        bool found = false;
        for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
        {
            MeleeComboStepData step = combo.steps[stepIndex];
            if (step.attackPhases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
            {
                AttackPhaseData phase = step.attackPhases[phaseIndex];
                AttackPatternDefinition pattern = phase.attackPattern;
                if (pattern == null
                    || pattern.shape != AttackAreaShape.Circle
                    || pattern.fillMode != AttackFillMode.RadialExpand)
                {
                    continue;
                }

                AttackGeometryData geometry = phase.geometry;
                geometry.rangeMultiplier =
                    GreatswordGroundSlamRadius / GreatswordBaseRange;
                geometry.overrideForwardOffset = true;
                geometry.forwardOffset = GreatswordGroundSlamForwardOffset;
                phase.geometry = geometry;

                bool foundImpact = false;
                if (phase.vfxCues != null)
                {
                    for (int cueIndex = 0; cueIndex < phase.vfxCues.Length; cueIndex++)
                    {
                        AttackVfxCueData cue = phase.vfxCues[cueIndex];
                        if (cue.motionRole != AttackVfxMotionRole.GroundImpact
                            || cue.elementOverrideKey != "GroundSlamImpact")
                            continue;

                        cue.scaleMultiplier = GroundSlamImpactScaleMultiplier;
                        phase.vfxCues[cueIndex] = cue;
                        foundImpact = true;
                    }
                }

                if (!foundImpact)
                    throw new InvalidOperationException("Greatsword ground impact Cue is missing.");

                step.attackPhases[phaseIndex] = phase;
                found = true;
            }

            combo.steps[stepIndex] = step;
        }

        if (!found)
            throw new InvalidOperationException("Greatsword ground slam Phase is missing.");

        EditorUtility.SetDirty(combo);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateGreatswordGroundSlamPlacement(combo);
        Debug.Log("[MeleeSpecialAttackVfxSetup] Greatsword ground slam uses 2.5m offset, 2.3m radius, and 1.5x impact VFX.");
    }

    private static void ValidateGreatswordGroundSlamPlacement(MeleeComboDefinition combo)
    {
        for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
        {
            AttackPhaseData[] phases = combo.steps[stepIndex].attackPhases;
            if (phases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                AttackPatternDefinition pattern = phases[phaseIndex].attackPattern;
                if (pattern == null
                    || pattern.shape != AttackAreaShape.Circle
                    || pattern.fillMode != AttackFillMode.RadialExpand)
                {
                    continue;
                }

                AttackPhaseData phase = phases[phaseIndex];
                if (!phase.geometry.overrideForwardOffset
                    || Mathf.Abs(phase.geometry.forwardOffset - GreatswordGroundSlamForwardOffset) > 0.001f)
                {
                    throw new InvalidOperationException("Greatsword ground slam forward offset is invalid.");
                }

                float resolvedRadius = GreatswordBaseRange * phase.geometry.SafeRangeMultiplier;
                if (Mathf.Abs(resolvedRadius - GreatswordGroundSlamRadius) > 0.001f)
                    throw new InvalidOperationException("Greatsword ground slam radius is invalid.");

                for (int cueIndex = 0; cueIndex < phase.vfxCues.Length; cueIndex++)
                {
                    AttackVfxCueData cue = phase.vfxCues[cueIndex];
                    if (cue.motionRole == AttackVfxMotionRole.GroundImpact
                        && cue.elementOverrideKey == "GroundSlamImpact"
                        && Mathf.Abs(cue.scaleMultiplier - GroundSlamImpactScaleMultiplier) <= 0.001f)
                    {
                        return;
                    }
                }
            }
        }

        throw new InvalidOperationException("Greatsword ground impact scale is invalid.");
    }

    [MenuItem("OVERBURST/Codex/Setup/Combat/Reset Current Melee Attack VFX Base Scale")]
    public static void ResetCurrentDefinitionsToBaseScaleFromMenu()
    {
        ResetCurrentDefinitionsToBaseScaleFromCommandLine();
    }

    public static void ResetCurrentDefinitionsToBaseScaleFromCommandLine()
    {
        string[] definitionPaths =
        {
            BasicSlashDefinitionPath,
            CircularSlashDefinitionPath,
            VerticalRisingDefinitionPath,
            VerticalFallingDefinitionPath,
            PrickDefinitionPath,
            GroundCrackDefinitionPath,
            SpikesDefinitionPath
        };

        for (int i = 0; i < definitionPaths.Length; i++)
            SetDefinitionScale(definitionPaths[i], DefaultAttackVfxScale);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        for (int i = 0; i < definitionPaths.Length; i++)
            ValidateDefinitionScale(definitionPaths[i], DefaultAttackVfxScale);

        Debug.Log("[MeleeSpecialAttackVfxSetup] Current melee attack VFX base scale reset to 1.");
    }

    private static GameObject EnsureOwnedPrefab(
        string sourcePath,
        string targetPath,
        string rootName)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
        if (existing != null)
        {
            GameObject existingRoot = PrefabUtility.LoadPrefabContents(targetPath);
            try
            {
                existingRoot.name = rootName;
                NormalizeRootTransform(existingRoot.transform);
                DisableParticleLoops(existingRoot);
                PrefabUtility.SaveAsPrefabAsset(existingRoot, targetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(existingRoot);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
        }

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null)
            throw new MissingReferenceException("Source VFX prefab is missing: " + sourcePath);

        GameObject root = PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (root == null)
            root = UnityEngine.Object.Instantiate(source);

        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                PrefabUtility.UnpackPrefabInstance(
                    root,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }

            root.name = rootName;
            NormalizeRootTransform(root.transform);
            DisableParticleLoops(root);
            PrefabUtility.SaveAsPrefabAsset(root, targetPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
    }

    private static GameObject EnsureExtractedChildPrefab(
        string sourcePath,
        string childName,
        string targetPath,
        string rootName)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null)
            throw new MissingReferenceException("Source VFX prefab is missing: " + sourcePath);

        GameObject sourceInstance = PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (sourceInstance == null)
            sourceInstance = UnityEngine.Object.Instantiate(source);

        GameObject extracted = null;
        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(sourceInstance))
            {
                PrefabUtility.UnpackPrefabInstance(
                    sourceInstance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }

            Transform[] transforms = sourceInstance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != sourceInstance.transform && transforms[i].name == childName)
                {
                    extracted = transforms[i].gameObject;
                    break;
                }
            }

            if (extracted == null)
                throw new MissingReferenceException($"VFX child '{childName}' is missing: {sourcePath}");

            extracted.transform.SetParent(null, true);
            for (int childIndex = extracted.transform.childCount - 1; childIndex >= 0; childIndex--)
                UnityEngine.Object.DestroyImmediate(extracted.transform.GetChild(childIndex).gameObject);

            extracted.name = rootName;
            DisableParticleLoops(extracted);
            PrefabUtility.SaveAsPrefabAsset(extracted, targetPath);
        }
        finally
        {
            if (sourceInstance != null)
                UnityEngine.Object.DestroyImmediate(sourceInstance);
            if (extracted != null)
                UnityEngine.Object.DestroyImmediate(extracted);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
    }

    private static void EnsureDefinition(
        string path,
        string assetName,
        GameObject prefab,
        Vector3 localPositionOffset,
        Vector3 localEulerOffset)
    {
        MeleeAttackVfxDefinition definition =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(path);
        bool created = definition == null;
        if (created)
        {
            definition = ScriptableObject.CreateInstance<MeleeAttackVfxDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }

        definition.name = assetName;
        definition.neutralPrefab = prefab;
        definition.localPositionOffset = localPositionOffset;
        definition.localEulerOffset = localEulerOffset;
        if (created)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0)
                throw new InvalidOperationException("Ground layer is not configured.");

            definition.baseScale = Vector3.one * DefaultAttackVfxScale;
            definition.mirrorRightToLeft = false;
            definition.groundLayerMask = 1 << groundLayer;
            definition.groundProbeHeight = 2f;
            definition.groundProbeDistance = 6f;
            definition.groundSurfaceOffset = 0.02f;
            definition.lifetime = 1f;
            definition.poolCapacity = 12;
        }

        EditorUtility.SetDirty(definition);
    }

    private static void NormalizeRootTransform(Transform root)
    {
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
    }

    private static void SetDefinitionScale(string path, float scale)
    {
        MeleeAttackVfxDefinition definition =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(path);
        if (definition == null)
            throw new MissingReferenceException("Melee attack VFX definition is missing: " + path);

        definition.baseScale = Vector3.one * scale;
        EditorUtility.SetDirty(definition);
    }

    private static void ValidateDefinitionScale(string path, float expectedScale)
    {
        MeleeAttackVfxDefinition definition =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(path);
        Vector3 expected = Vector3.one * expectedScale;
        if (definition == null || (definition.baseScale - expected).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException("Unexpected melee attack VFX scale: " + path);
    }


    private static void ApplySpecialCues(
        string comboPath,
        bool applyCircularSlash,
        bool applyThrust,
        bool applyGroundSlam)
    {
        MeleeComboDefinition combo = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(comboPath);
        if (combo == null || combo.steps == null)
            throw new MissingReferenceException("Melee combo is missing: " + comboPath);

        bool changed = false;
        for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
        {
            MeleeComboStepData step = combo.steps[stepIndex];
            if (step.attackPhases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
            {
                AttackPhaseData phase = step.attackPhases[phaseIndex];
                AttackPatternDefinition pattern = phase.attackPattern;
                bool isThrust = pattern != null
                    && pattern.shape == AttackAreaShape.Rectangle
                    && pattern.fillMode == AttackFillMode.LinearFill;
                bool isCircularSlash = pattern != null
                    && pattern.shape == AttackAreaShape.Circle
                    && pattern.fillMode == AttackFillMode.AngularSweep;
                bool isGroundSlam = pattern != null
                    && pattern.shape == AttackAreaShape.Circle
                    && pattern.fillMode == AttackFillMode.RadialExpand;

                if ((!applyCircularSlash || !isCircularSlash)
                    && (!applyThrust || !isThrust)
                    && (!applyGroundSlam || !isGroundSlam))
                    continue;

                phase.vfxCues = MeleeAttackVfxCueTuningPreserver.Restore(
                    phase.vfxCues,
                    MeleeAttackVfxEditorDefaults.CreateDefaultCues(pattern));
                step.attackPhases[phaseIndex] = phase;
                changed = true;
            }

            combo.steps[stepIndex] = step;
        }

        if (!changed)
            throw new InvalidOperationException("Target attack phase was not found: " + comboPath);

        EditorUtility.SetDirty(combo);
    }

    private static void Validate()
    {
        ValidateOwnedPrefab(PrickPrefabPath);
        ValidateOwnedPrefab(GroundCrackPrefabPath);
        ValidateOwnedPrefab(SpikesPrefabPath);

        ValidateComboCue(
            OneHandSwordComboPath,
            AttackAreaShape.Circle,
            AttackFillMode.AngularSweep,
            1,
            AttackVfxPlacementMode.PatternOrigin);
        ValidateComboCue(
            GreatswordComboPath,
            AttackAreaShape.Circle,
            AttackFillMode.RadialExpand,
            3,
            AttackVfxPlacementMode.OwnerOrigin,
            AttackVfxPlacementMode.PatternGround,
            AttackVfxPlacementMode.PatternGround);
    }

    private static void ValidateOwnedPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new MissingReferenceException("Owned VFX prefab is missing: " + path);

        ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        if (systems.Length == 0)
            throw new MissingComponentException("Owned VFX has no ParticleSystem: " + path);

        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].main.loop)
                throw new InvalidOperationException("Looping ParticleSystem remains: " + path);
        }
    }

    private static void ValidateComboCue(
        string comboPath,
        AttackAreaShape shape,
        AttackFillMode fillMode,
        int expectedCount,
        params AttackVfxPlacementMode[] expectedPlacements)
    {
        MeleeComboDefinition combo = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(comboPath);
        for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
        {
            AttackPhaseData[] phases = combo.steps[stepIndex].attackPhases;
            if (phases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                AttackPatternDefinition pattern = phases[phaseIndex].attackPattern;
                if (pattern == null || pattern.shape != shape || pattern.fillMode != fillMode)
                    continue;

                AttackVfxCueData[] cues = phases[phaseIndex].vfxCues;
                if (cues == null || cues.Length != expectedCount)
                    throw new InvalidOperationException("Unexpected VFX cue count: " + comboPath);

                for (int i = 0; i < expectedPlacements.Length; i++)
                {
                    if (cues[i].definition == null || cues[i].placementMode != expectedPlacements[i])
                        throw new InvalidOperationException("Unexpected VFX cue placement: " + comboPath);
                }

                return;
            }
        }

        throw new InvalidOperationException("VFX target phase is missing: " + comboPath);
    }

    private static void DisableParticleLoops(GameObject root)
    {
        ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            main.loop = false;
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent))
            throw new InvalidOperationException("Invalid asset folder: " + path);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
