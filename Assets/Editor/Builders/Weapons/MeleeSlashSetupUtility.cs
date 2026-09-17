using System;
using UnityEditor;
using UnityEngine;

public static class MeleeSlashSetupUtility
{
    private const float PrefabVisualScale = 0.5f;
    private const float GreatswordSlashCueScale = 1.05f;
    private const string SourcePrefabPath =
        "Assets/ThirdParty/06_VFX/UniqueVFXUltra/UniqueSwordSlashesVol_1/Prefabs/vfx_SwordSlash03.prefab";
    private const string TargetFolder =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/MeleeSlash";
    private const string BasePrefabPath =
        TargetFolder + "/PF_VFX_MeleeSlash_Base.prefab";
    private const string HorizontalPrefabPath =
        TargetFolder + "/PF_VFX_MeleeSlash_Horizontal.prefab";
    private const string CircularPrefabPath =
        TargetFolder + "/PF_VFX_MeleeSlash_Circular.prefab";
    private const string HorizontalDefinitionPath =
        TargetFolder + "/VFX_MeleeSlash_Horizontal.asset";
    private const string CircularDefinitionPath =
        TargetFolder + "/VFX_MeleeSlash_Circular.asset";
    private const string VerticalRisingDefinitionPath =
        TargetFolder + "/VFX_MeleeSlash_VerticalRising.asset";
    private const string VerticalFallingDefinitionPath =
        TargetFolder + "/VFX_MeleeSlash_VerticalFalling.asset";

    private static readonly Vector3 SlashHeightOffset = new Vector3(0f, 1f, 0f);
    private static readonly Vector3 CircularRotation = Vector3.zero;
    private static readonly Quaternion HorizontalVisualRotation = Quaternion.Euler(0f, 90f, 0f);
    private static readonly Vector3 VerticalRisingRotation = new Vector3(0f, 0f, 90f);
    private static readonly Vector3 VerticalFallingRotation = new Vector3(0f, 0f, -90f);
    private static readonly Quaternion CircularCrossRotation = Quaternion.Euler(0f, 90f, 0f);

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Melee Slash")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        EnsureFolder(TargetFolder);
        GameObject basePrefab = LoadRequiredPrefab(BasePrefabPath);
        GameObject horizontalPrefab = LoadRequiredPrefab(HorizontalPrefabPath);
        GameObject circularPrefab = LoadRequiredPrefab(CircularPrefabPath);

        MeleeAttackVfxDefinition horizontal = EnsureDefinition(
            HorizontalDefinitionPath,
            "VFX_MeleeSlash_Horizontal",
            horizontalPrefab,
            Vector3.zero,
            mirrorRightToLeft: true,
            horizontalMirrorScaleAxis: AttackVfxScaleMirrorAxis.X);
        MeleeAttackVfxDefinition circular = EnsureDefinition(
            CircularDefinitionPath,
            "VFX_MeleeSlash_Circular",
            circularPrefab,
            CircularRotation,
            mirrorRightToLeft: true,
            horizontalMirrorScaleAxis: AttackVfxScaleMirrorAxis.X);
        MeleeAttackVfxDefinition verticalRising = EnsureDefinition(
            VerticalRisingDefinitionPath,
            "VFX_MeleeSlash_VerticalRising",
            basePrefab,
            VerticalRisingRotation,
            mirrorRightToLeft: false,
            horizontalMirrorScaleAxis: AttackVfxScaleMirrorAxis.X);
        MeleeAttackVfxDefinition verticalFalling = EnsureDefinition(
            VerticalFallingDefinitionPath,
            "VFX_MeleeSlash_VerticalFalling",
            basePrefab,
            VerticalFallingRotation,
            mirrorRightToLeft: false,
            horizontalMirrorScaleAxis: AttackVfxScaleMirrorAxis.X);

        ApplyActiveCueDefinitions(horizontal, circular, verticalRising, verticalFalling);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate(basePrefab, horizontalPrefab, circularPrefab, horizontal, circular, verticalRising, verticalFalling);
        Debug.Log("[MeleeSlashSetup] Authored slash prefabs were preserved; references and mirror policy are active.");
    }

    private static GameObject LoadRequiredPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new MissingReferenceException("Required authored slash prefab is missing: " + path);
        return prefab;
    }

    private static GameObject EnsureBasePrefab()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (source == null)
            throw new MissingReferenceException("Sword Slash 03 source prefab is missing.");

        GameObject root = new GameObject("PF_VFX_MeleeSlash_Base");
        GameObject visual = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject;
        if (visual == null)
            visual = UnityEngine.Object.Instantiate(source, root.transform);

        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(visual))
            {
                PrefabUtility.UnpackPrefabInstance(
                    visual,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }

            NormalizeRoot(root.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * PrefabVisualScale;
            DisableLoops(root);
            PrefabUtility.SaveAsPrefabAsset(root, BasePrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
    }

    private static GameObject EnsureCircularCompositePrefab(GameObject horizontalPrefab)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(CircularPrefabPath) == null
            ? new GameObject("PF_VFX_MeleeSlash_Circular")
            : PrefabUtility.LoadPrefabContents(CircularPrefabPath);
        bool loadedContents = AssetDatabase.LoadAssetAtPath<GameObject>(CircularPrefabPath) != null;

        try
        {
            root.name = "PF_VFX_MeleeSlash_Circular";
            NormalizeRoot(root.transform);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            CreateNestedSlash(horizontalPrefab, root.transform, "Slash_A", Quaternion.identity);
            CreateNestedSlash(horizontalPrefab, root.transform, "Slash_B", CircularCrossRotation);
            PrefabUtility.SaveAsPrefabAsset(root, CircularPrefabPath);
        }
        finally
        {
            if (loadedContents)
                PrefabUtility.UnloadPrefabContents(root);
            else
                UnityEngine.Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(CircularPrefabPath);
    }

    private static GameObject EnsureHorizontalPrefab(GameObject basePrefab)
    {
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(HorizontalPrefabPath) == null
            ? new GameObject("PF_VFX_MeleeSlash_Horizontal")
            : PrefabUtility.LoadPrefabContents(HorizontalPrefabPath);
        bool loadedContents = AssetDatabase.LoadAssetAtPath<GameObject>(HorizontalPrefabPath) != null;

        try
        {
            root.name = "PF_VFX_MeleeSlash_Horizontal";
            NormalizeRoot(root.transform);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            CreateNestedSlash(basePrefab, root.transform, "Slash", HorizontalVisualRotation);
            PrefabUtility.SaveAsPrefabAsset(root, HorizontalPrefabPath);
        }
        finally
        {
            if (loadedContents)
                PrefabUtility.UnloadPrefabContents(root);
            else
                UnityEngine.Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(HorizontalPrefabPath);
    }

    private static void CreateNestedSlash(
        GameObject basePrefab,
        Transform parent,
        string name,
        Quaternion localRotation)
    {
        GameObject child = PrefabUtility.InstantiatePrefab(basePrefab, parent) as GameObject;
        if (child == null)
            throw new InvalidOperationException("Failed to create nested Melee Slash instance.");

        child.name = name;
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = localRotation;
        child.transform.localScale = Vector3.one;
    }

    private static MeleeAttackVfxDefinition EnsureDefinition(
        string path,
        string assetName,
        GameObject prefab,
        Vector3 localEulerOffset,
        bool mirrorRightToLeft,
        AttackVfxScaleMirrorAxis horizontalMirrorScaleAxis)
    {
        MeleeAttackVfxDefinition definition =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<MeleeAttackVfxDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
            throw new InvalidOperationException("Ground layer is not configured.");

        definition.name = assetName;
        definition.neutralPrefab = prefab;
        definition.baseScale = Vector3.one;
        definition.localPositionOffset = SlashHeightOffset;
        definition.localEulerOffset = localEulerOffset;
        definition.mirrorRightToLeft = mirrorRightToLeft;
        definition.horizontalMirrorScaleAxis = horizontalMirrorScaleAxis;
        definition.groundLayerMask = 1 << groundLayer;
        definition.groundProbeHeight = 2f;
        definition.groundProbeDistance = 6f;
        definition.groundSurfaceOffset = 0.02f;
        definition.lifetime = 1f;
        definition.poolCapacity = 12;
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static void ApplyActiveCueDefinitions(
        MeleeAttackVfxDefinition horizontal,
        MeleeAttackVfxDefinition circular,
        MeleeAttackVfxDefinition verticalRising,
        MeleeAttackVfxDefinition verticalFalling)
    {
        string[] comboGuids = AssetDatabase.FindAssets(
            "t:MeleeComboDefinition",
            new[] { "Assets/ProjectOverburst/03_Features/Weapons" });
        for (int assetIndex = 0; assetIndex < comboGuids.Length; assetIndex++)
        {
            string path = AssetDatabase.GUIDToAssetPath(comboGuids[assetIndex]);
            MeleeComboDefinition combo = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(path);
            bool isGreatswordCombo = path.StartsWith(
                "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/",
                StringComparison.Ordinal);
            bool changed = false;

            for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
            {
                MeleeComboStepData step = combo.steps[stepIndex];
                if (step.attackPhases == null)
                    continue;

                for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
                {
                    AttackPhaseData phase = step.attackPhases[phaseIndex];
                    bool phaseUsesUnifiedSlash = false;
                    if (phase.vfxCues == null)
                        continue;

                    for (int cueIndex = 0; cueIndex < phase.vfxCues.Length; cueIndex++)
                    {
                        AttackVfxCueData cue = phase.vfxCues[cueIndex];
                        switch (cue.motionRole)
                        {
                            case AttackVfxMotionRole.HorizontalSweep:
                                cue.definition = horizontal;
                                cue.mirrorAxis = AttackVfxMirrorAxis.None;
                                break;
                            case AttackVfxMotionRole.HorizontalCircular:
                                cue.definition = circular;
                                cue.mirrorAxis = AttackVfxMirrorAxis.None;
                                break;
                            case AttackVfxMotionRole.VerticalRising:
                                cue.definition = verticalRising;
                                cue.mirrorAxis = AttackVfxMirrorAxis.None;
                                break;
                            case AttackVfxMotionRole.VerticalFalling:
                                cue.definition = verticalFalling;
                                cue.mirrorAxis = AttackVfxMirrorAxis.None;
                                break;
                            default:
                                continue;
                        }

                        cue.autoSwingSlope = true;
                        cue.swingSlopeOffsetDegrees = 0f;
                        cue.localEulerOffset = Vector3.zero;
                        if (isGreatswordCombo)
                            cue.scaleMultiplier = GreatswordSlashCueScale;
                        phase.vfxCues[cueIndex] = cue;
                        phaseUsesUnifiedSlash = true;
                        changed = true;
                    }

                    if (phaseUsesUnifiedSlash)
                        phase.useBakedVfxSwingSlope = true;
                    step.attackPhases[phaseIndex] = phase;
                }

                combo.steps[stepIndex] = step;
            }

            if (changed)
                EditorUtility.SetDirty(combo);
        }
    }

    private static void Validate(
        GameObject basePrefab,
        GameObject horizontalPrefab,
        GameObject circularPrefab,
        MeleeAttackVfxDefinition horizontal,
        MeleeAttackVfxDefinition circular,
        MeleeAttackVfxDefinition verticalRising,
        MeleeAttackVfxDefinition verticalFalling)
    {
        ValidatePrefab(basePrefab, expectedSystemMultiple: 1);
        ValidatePrefab(horizontalPrefab, expectedSystemMultiple: 1);
        ValidatePrefab(circularPrefab, expectedSystemMultiple: 2);
        ValidateDefinition(horizontal, horizontalPrefab, Vector3.zero, true, AttackVfxScaleMirrorAxis.X);
        ValidateDefinition(circular, circularPrefab, CircularRotation, true, AttackVfxScaleMirrorAxis.X);
        ValidateDefinition(verticalRising, basePrefab, VerticalRisingRotation, false, AttackVfxScaleMirrorAxis.X);
        ValidateDefinition(verticalFalling, basePrefab, VerticalFallingRotation, false, AttackVfxScaleMirrorAxis.X);

        int horizontalCount = 0;
        int circularCount = 0;
        int risingCount = 0;
        int fallingCount = 0;
        string[] comboGuids = AssetDatabase.FindAssets(
            "t:MeleeComboDefinition",
            new[] { "Assets/ProjectOverburst/03_Features/Weapons" });
        for (int assetIndex = 0; assetIndex < comboGuids.Length; assetIndex++)
        {
            MeleeComboDefinition combo = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(
                AssetDatabase.GUIDToAssetPath(comboGuids[assetIndex]));
            for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
            {
                AttackPhaseData[] phases = combo.steps[stepIndex].attackPhases;
                if (phases == null)
                    continue;
                for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
                {
                    AttackVfxCueData[] cues = phases[phaseIndex].vfxCues;
                    if (cues == null)
                        continue;
                    for (int cueIndex = 0; cueIndex < cues.Length; cueIndex++)
                    {
                        AttackVfxCueData cue = cues[cueIndex];
                        switch (cue.motionRole)
                        {
                            case AttackVfxMotionRole.HorizontalSweep:
                                ValidateCue(cue, horizontal, AttackVfxMirrorAxis.None);
                                horizontalCount++;
                                break;
                            case AttackVfxMotionRole.HorizontalCircular:
                                ValidateCue(cue, circular, AttackVfxMirrorAxis.None);
                                circularCount++;
                                break;
                            case AttackVfxMotionRole.VerticalRising:
                                ValidateCue(cue, verticalRising, AttackVfxMirrorAxis.None);
                                risingCount++;
                                break;
                            case AttackVfxMotionRole.VerticalFalling:
                                ValidateCue(cue, verticalFalling, AttackVfxMirrorAxis.None);
                                fallingCount++;
                                break;
                        }
                    }
                }
            }
        }

        if (horizontalCount != 4 || circularCount != 1 || risingCount != 0 || fallingCount != 1)
        {
            throw new InvalidOperationException(
                $"Unexpected Unified Slash cue counts: H={horizontalCount}, C={circularCount}, R={risingCount}, F={fallingCount}.");
        }
    }

    private static void ValidatePrefab(GameObject prefab, int expectedSystemMultiple)
    {
        if (prefab == null)
            throw new MissingReferenceException("Unified Slash prefab is missing.");
        ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        if (systems.Length == 0 || systems.Length % expectedSystemMultiple != 0)
            throw new InvalidOperationException("Unexpected Unified Slash ParticleSystem count.");
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].main.loop)
                throw new InvalidOperationException("Unified Slash still has a looping ParticleSystem.");
        }
    }

    private static void ValidateCircularComposite(GameObject circularPrefab, GameObject basePrefab)
    {
        if (circularPrefab.transform.childCount != 2)
            throw new InvalidOperationException("Circular Unified Slash must contain exactly two nested slashes.");

        Transform slashA = circularPrefab.transform.Find("Slash_A");
        Transform slashB = circularPrefab.transform.Find("Slash_B");
        if (slashA == null || slashB == null)
            throw new MissingReferenceException("Circular Unified Slash children are missing.");
        if (Quaternion.Angle(slashA.localRotation, Quaternion.identity) > 0.01f
            || Quaternion.Angle(slashB.localRotation, CircularCrossRotation) > 0.01f)
        {
            throw new InvalidOperationException("Circular Unified Slash child rotations are invalid.");
        }

        int baseSystemCount = basePrefab.GetComponentsInChildren<ParticleSystem>(true).Length;
        int circularSystemCount = circularPrefab.GetComponentsInChildren<ParticleSystem>(true).Length;
        if (circularSystemCount != baseSystemCount * 2)
            throw new InvalidOperationException("Circular Unified Slash does not contain two complete Base effects.");
    }

    private static void ValidateHorizontalPrefab(GameObject horizontalPrefab, GameObject basePrefab)
    {
        if (horizontalPrefab.transform.childCount != 1)
            throw new InvalidOperationException("Horizontal Melee Slash must contain one nested Base slash.");

        Transform slash = horizontalPrefab.transform.Find("Slash");
        if (slash == null
            || Quaternion.Angle(slash.localRotation, HorizontalVisualRotation) > 0.01f
            || (slash.localPosition - Vector3.zero).sqrMagnitude > 0.000001f
            || (slash.localScale - Vector3.one).sqrMagnitude > 0.000001f)
        {
            throw new InvalidOperationException("Horizontal Melee Slash orientation is invalid.");
        }

        int baseSystemCount = basePrefab.GetComponentsInChildren<ParticleSystem>(true).Length;
        int horizontalSystemCount = horizontalPrefab.GetComponentsInChildren<ParticleSystem>(true).Length;
        if (horizontalSystemCount != baseSystemCount)
            throw new InvalidOperationException("Horizontal Melee Slash does not contain one complete Base effect.");
    }

    private static void ValidateBaseVisualScale(GameObject basePrefab)
    {
        Transform visual = basePrefab.transform.Find("Visual");
        Vector3 expectedScale = Vector3.one * PrefabVisualScale;
        if (visual == null
            || basePrefab.transform.childCount != 1
            || (visual.localScale - expectedScale).sqrMagnitude > 0.000001f
            || (basePrefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
        {
            throw new InvalidOperationException("Melee Slash prefab visual scale is invalid.");
        }
    }

    private static void ValidateDefinition(
        MeleeAttackVfxDefinition definition,
        GameObject prefab,
        Vector3 expectedEuler,
        bool expectedMirror,
        AttackVfxScaleMirrorAxis expectedMirrorScaleAxis)
    {
        if (definition == null
            || definition.neutralPrefab != prefab
            || (definition.baseScale - Vector3.one).sqrMagnitude > 0.000001f
            || (definition.localPositionOffset - SlashHeightOffset).sqrMagnitude > 0.000001f
            || (definition.localEulerOffset - expectedEuler).sqrMagnitude > 0.000001f
            || definition.mirrorRightToLeft != expectedMirror
            || definition.horizontalMirrorScaleAxis != expectedMirrorScaleAxis
            || Mathf.Abs(definition.lifetime - 1f) > 0.001f
            || definition.poolCapacity != 12)
        {
            throw new InvalidOperationException("Unified Slash definition is invalid: " + definition?.name);
        }
    }

    private static void ValidateCue(
        AttackVfxCueData cue,
        MeleeAttackVfxDefinition definition,
        AttackVfxMirrorAxis mirrorAxis)
    {
        if (cue.definition != definition
            || cue.mirrorAxis != mirrorAxis
            || !cue.autoSwingSlope
            || Mathf.Abs(cue.swingSlopeOffsetDegrees) > 0.001f
            || cue.localEulerOffset.sqrMagnitude > 0.000001f)
        {
            throw new InvalidOperationException("Melee Slash cue policy is invalid: " + cue.motionRole);
        }
    }

    private static void NormalizeRoot(Transform root)
    {
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
    }

    private static void DisableLoops(GameObject root)
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
