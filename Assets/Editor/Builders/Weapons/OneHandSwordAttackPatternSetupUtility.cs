using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Owns creation and formal wiring of the reusable melee attack pattern assets.
public static class OneHandSwordAttackPatternSetupUtility
{
    private const float OneHandSwordBaseRange = 2.6f;
    private const float LegacyVfxReferenceRange = 2.75f;
    private const string PatternFolder = "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/AttackPatterns";
    private const string SwordAssetPath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const string PlayerActorPrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    private const string PendingFlagPath = "Temp/ApplyMeleeAttackPatternSetup.flag";
    private const string ReportPath = "Logs/MeleeAttackPatternSetup.txt";

    private static readonly PatternSpec[] DefaultPatternSpecs =
    {
        new PatternSpec(
            "AP_Sector_RightToLeft",
            AttackAreaShape.Sector,
            AttackFillMode.AngularSweep,
            AttackFillDirection.RightToLeft,
            0.55f,
            0f,
            1f),
        new PatternSpec(
            "AP_Sector_LeftToRight",
            AttackAreaShape.Sector,
            AttackFillMode.AngularSweep,
            AttackFillDirection.LeftToRight,
            0.55f,
            0f,
            1f),
        new PatternSpec(
            "AP_Circle_LeftToRight",
            AttackAreaShape.Circle,
            AttackFillMode.AngularSweep,
            AttackFillDirection.LeftToRight,
            0f,
            -90f,
            1f),
        new PatternSpec(
            "AP_Circle_RightToLeft",
            AttackAreaShape.Circle,
            AttackFillMode.AngularSweep,
            AttackFillDirection.RightToLeft,
            0f,
            90f,
            1f),
        new PatternSpec(
            "AP_Rectangle_NearToFar",
            AttackAreaShape.Rectangle,
            AttackFillMode.LinearFill,
            AttackFillDirection.NearToFar,
            0.45f,
            0f,
            1.1f),
        new PatternSpec(
            "AP_Circle_RadialExpand",
            AttackAreaShape.Circle,
            AttackFillMode.RadialExpand,
            AttackFillDirection.NearToFar,
            0f,
            0f,
            1f),
        new PatternSpec(
            "AP_Sector_RadialExpand",
            AttackAreaShape.Sector,
            AttackFillMode.RadialExpand,
            AttackFillDirection.NearToFar,
            0.55f,
            0f,
            1f)
    };

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Modular Melee Attack Patterns")]
    public static void ApplySetup()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("Modular melee attack pattern setup");
        report.AppendLine("GeneratedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        Dictionary<string, AttackPatternDefinition> patterns = EnsureDefaultPatterns(report);
        WeaponItemData sword = AssetDatabase.LoadAssetAtPath<WeaponItemData>(SwordAssetPath);
        if (sword == null)
            throw new MissingReferenceException("Sword.asset not found: " + SwordAssetPath);

        ApplySwordAttackPhases(sword, patterns, report, false);
        ValidatePlayerDebugRenderer(report);
        AppendEvaluatorSmokeChecks(patterns, report);

        EditorUtility.SetDirty(sword);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        WriteReport(report);
        Debug.Log("[MeleeAttackPatternSetup] Setup complete. Report: " + ReportPath);
    }

    public static void RunOnceFromCommandLine()
    {
        ApplySetup();
    }

    public static void ApplySwordAttackPhases(WeaponItemData sword, bool overwriteExisting = false)
    {
        if (sword == null)
            return;

        Dictionary<string, AttackPatternDefinition> patterns = EnsureDefaultPatterns(null);
        ApplySwordAttackPhases(sword, patterns, null, overwriteExisting);
    }

    public static Dictionary<string, AttackPatternDefinition> EnsureDefaultPatterns()
    {
        return EnsureDefaultPatterns(null);
    }

    [InitializeOnLoadMethod]
    private static void RunPendingSetupAfterScriptReload()
    {
        if (Application.isBatchMode)
            return;

        string fullFlagPath = Path.Combine(Directory.GetCurrentDirectory(), PendingFlagPath);
        if (!File.Exists(fullFlagPath))
            return;

        File.Delete(fullFlagPath);
        try
        {
            ApplySetup();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static Dictionary<string, AttackPatternDefinition> EnsureDefaultPatterns(StringBuilder report)
    {
        EnsureAssetFolder(PatternFolder);
        Dictionary<string, AttackPatternDefinition> patterns =
            new Dictionary<string, AttackPatternDefinition>(StringComparer.Ordinal);

        for (int i = 0; i < DefaultPatternSpecs.Length; i++)
        {
            PatternSpec spec = DefaultPatternSpecs[i];
            string assetPath = PatternFolder + "/" + spec.Name + ".asset";
            AttackPatternDefinition pattern = AssetDatabase.LoadAssetAtPath<AttackPatternDefinition>(assetPath);
            bool created = pattern == null;
            if (created)
            {
                pattern = ScriptableObject.CreateInstance<AttackPatternDefinition>();
                spec.ApplyTo(pattern);
                AssetDatabase.CreateAsset(pattern, assetPath);
                EditorUtility.SetDirty(pattern);
            }

            patterns.Add(spec.Name, pattern);
            report?.AppendLine("Pattern=" + spec.Name + ", Status=" + (created ? "Created" : "Preserved"));
        }

        return patterns;
    }

    private static void ApplySwordAttackPhases(
        WeaponItemData sword,
        IReadOnlyDictionary<string, AttackPatternDefinition> patterns,
        StringBuilder report,
        bool overwriteExisting)
    {
        MeleeComboDefinition comboDefinition = sword.GetMeleeComboDefinition();
        MeleeComboStepData[] steps = comboDefinition != null ? comboDefinition.steps : null;
        if (steps == null || steps.Length == 0)
            throw new InvalidOperationException("Sword combo steps are empty.");

        string[] comboPatternNames =
        {
            "AP_Sector_RightToLeft",
            "AP_Sector_LeftToRight",
            "AP_Circle_LeftToRight",
            "AP_Rectangle_NearToFar"
        };

        for (int i = 0; i < steps.Length; i++)
        {
            MeleeComboStepData step = steps[i];
            if (string.IsNullOrWhiteSpace(step.attackId))
                step.attackId = ResolveSwordComboAttackId(i);

            if (!overwriteExisting && step.attackPhases != null && step.attackPhases.Length > 0)
            {
                steps[i] = step;
                report?.AppendLine(
                    "SwordStep=" + (i + 1)
                    + ", AttackId=" + step.attackId
                    + ", Phase=Preserved");
                continue;
            }

            string patternName = comboPatternNames[i % comboPatternNames.Length];
            AttackPatternDefinition pattern = patterns[patternName];
            ResolveInitialPhaseWindow(out float startTime, out float endTime);

            step.attackPhases = new[]
            {
                new AttackPhaseData
                {
                    attackPattern = pattern,
                    startNormalizedTime = startTime,
                    endNormalizedTime = endTime,
                    geometry = CreateSwordComboGeometry(i),
                    progressSource = AttackProgressSourcePolicy.ResolveDefault(pattern),
                    basisFollowMode = AttackBasisFollowMode.Fixed,
                    vfxCues = MeleeAttackVfxEditorDefaults.CreateDefaultCues(pattern),
                    impact = new AttackImpactData
                    {
                        damageMultiplier = 1f,
                        knockbackMultiplier = 1f,
                        overrideTargetReaction = false,
                        triggersOnHitEffects = true
                    }
                }
            };
            steps[i] = step;

            report?.AppendLine(
                "SwordStep=" + (i + 1)
                + ", Name=" + step.attackName
                + ", Pattern=" + patternName
                + ", Phase=" + startTime.ToString("0.###") + "-" + endTime.ToString("0.###"));
        }

        EditorUtility.SetDirty(comboDefinition);

        EditorUtility.SetDirty(sword);
    }

    public static string ResolveSwordComboAttackId(int stepIndex)
    {
        return "Sword.Combo05." + (Mathf.Max(0, stepIndex) + 1);
    }

    private static AttackGeometryData CreateSwordComboGeometry(int stepIndex)
    {
        float rangeMultiplier = stepIndex >= 2 ? 1.1f : 1f;
        return new AttackGeometryData
        {
            rangeMultiplier = rangeMultiplier,
            angleMultiplier = stepIndex == 0 || stepIndex == 1 ? 1.4f : 1f,
            widthMultiplier = 1f,
            vfxScaleMultiplier = OneHandSwordBaseRange
                * rangeMultiplier
                / LegacyVfxReferenceRange,
            overrideForwardOffset = true,
            forwardOffset = stepIndex == 3 ? 0.45f : (stepIndex < 2 ? 0.55f : 0f)
        };
    }

    private static void ResolveInitialPhaseWindow(
        out float startNormalizedTime,
        out float endNormalizedTime)
    {
        startNormalizedTime = 0.23f;
        endNormalizedTime = 0.47f;
    }

    private static void ValidatePlayerDebugRenderer(StringBuilder report)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerActorPrefabPath);
        if (prefabRoot == null)
            throw new MissingReferenceException("Player actor prefab not found: " + PlayerActorPrefabPath);

        try
        {
            MeleeRuntime meleeRuntime = prefabRoot.GetComponentInChildren<MeleeRuntime>(true);
            if (meleeRuntime == null)
                throw new MissingComponentException("MeleeRuntime not found in player actor prefab.");

            AttackPatternDebugRenderer renderer = meleeRuntime.GetComponent<AttackPatternDebugRenderer>();
            if (renderer == null)
                throw new MissingComponentException("AttackPatternDebugRenderer not found in player actor prefab.");

            report?.AppendLine("PlayerDebugRenderer=Present");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void AppendEvaluatorSmokeChecks(
        IReadOnlyDictionary<string, AttackPatternDefinition> patterns,
        StringBuilder report)
    {
        AttackPatternBasis basis = new AttackPatternBasis(Vector3.zero, Vector3.forward);
        AppendOrderCheck(
            "SectorRightToLeft",
            patterns["AP_Sector_RightToLeft"].Resolve(2.75f, 100f, 1f, 0.55f),
            basis,
            new Vector3(0.3f, 0f, 1.2f),
            new Vector3(-0.3f, 0f, 1.2f),
            report);
        AppendOrderCheck(
            "SectorLeftToRight",
            patterns["AP_Sector_LeftToRight"].Resolve(2.75f, 100f, 1f, 0.55f),
            basis,
            new Vector3(-0.3f, 0f, 1.2f),
            new Vector3(0.3f, 0f, 1.2f),
            report);
        AppendOrderCheck(
            "CircleLeftToRight",
            patterns["AP_Circle_LeftToRight"].Resolve(2.75f, 100f, 1f, 0f),
            basis,
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            report);
        AppendOrderCheck(
            "CircleRightToLeft",
            patterns["AP_Circle_RightToLeft"].Resolve(2.75f, 100f, 1f, 0f),
            basis,
            new Vector3(1f, 0f, 0f),
            new Vector3(-1f, 0f, 0f),
            report);
        AppendOrderCheck(
            "RectangleNearToFar",
            patterns["AP_Rectangle_NearToFar"].Resolve(2.75f, 100f, 1.1f, 0.45f),
            basis,
            new Vector3(0f, 0f, 0.75f),
            new Vector3(0f, 0f, 2.25f),
            report);
        AppendOrderCheck(
            "SectorRadialExpand",
            patterns["AP_Sector_RadialExpand"].Resolve(2.75f, 100f, 1f, 0.55f),
            basis,
            new Vector3(0f, 0f, 0.9f),
            new Vector3(0f, 0f, 2.2f),
            report);
        AppendOrderCheck(
            "CircleRadialExpand",
            patterns["AP_Circle_RadialExpand"].Resolve(2.75f, 100f, 1f, 0f),
            basis,
            new Vector3(0f, 0f, 0.75f),
            new Vector3(0f, 0f, 2.25f),
            report);
    }

    private static void AppendOrderCheck(
        string checkName,
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        Vector3 firstPoint,
        Vector3 secondPoint,
        StringBuilder report)
    {
        bool firstValid = AttackPatternEvaluator.TryEvaluate(pattern, basis, firstPoint, out float firstProgress);
        bool secondValid = AttackPatternEvaluator.TryEvaluate(pattern, basis, secondPoint, out float secondProgress);
        bool passed = firstValid && secondValid && firstProgress < secondProgress;
        report.AppendLine(
            "SmokeCheck=" + checkName
            + ", Result=" + (passed ? "Pass" : "Fail")
            + ", First=" + firstProgress.ToString("0.###")
            + ", Second=" + secondProgress.ToString("0.###"));
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        string[] parts = assetFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static void WriteReport(StringBuilder report)
    {
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, report.ToString(), Encoding.UTF8);
    }

    private readonly struct PatternSpec
    {
        public readonly string Name;
        private readonly AttackAreaShape shape;
        private readonly AttackFillMode fillMode;
        private readonly AttackFillDirection direction;
        private readonly float forwardOffset;
        private readonly float angleOffset;
        private readonly float width;

        public PatternSpec(
            string name,
            AttackAreaShape shape,
            AttackFillMode fillMode,
            AttackFillDirection direction,
            float forwardOffset,
            float angleOffset,
            float width)
        {
            Name = name;
            this.shape = shape;
            this.fillMode = fillMode;
            this.direction = direction;
            this.forwardOffset = forwardOffset;
            this.angleOffset = angleOffset;
            this.width = width;
        }

        public void ApplyTo(AttackPatternDefinition pattern)
        {
            pattern.shape = shape;
            pattern.fillMode = fillMode;
            pattern.direction = direction;
            pattern.angleOffset = angleOffset;
            pattern.verticalTolerance = 3f;
            pattern.hitRevalidationTolerance = 0.4f;
            pattern.progressCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }
}
