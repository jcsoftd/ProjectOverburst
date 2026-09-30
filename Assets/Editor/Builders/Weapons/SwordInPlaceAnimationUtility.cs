using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SwordInPlaceAnimationUtility
{
    private const string SwordAssetPath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const string InPlaceFolderPath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/Animation/Clips/InPlace";
    private const string ReportPath = "Logs/SwordInPlaceAnimationSetup.txt";
    private static readonly string[] ComboClipNames = { "slash1", "slash2", "slash4", "slash3" };
    private static readonly string[] SourceClipNames = { "slash1", "slash2", "slash3", "slash4" };

    [MenuItem("OVERBURST/Codex/Setup/Weapons/Create Sword InPlace Animations")]
    public static void RunOnceFromCommandLine()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("Sword in-place animation setup");
        report.AppendLine("GeneratedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine();

        EnsureFolder(InPlaceFolderPath);

        Dictionary<string, AnimationClip> inPlaceClips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < SourceClipNames.Length; i++)
        {
            string clipName = SourceClipNames[i];
            AnimationClip source = FindAnimationClipByName(clipName, false);
            AnimationClip inPlace = CreateOrUpdateInPlaceClip(clipName, source, report);
            if (inPlace != null)
                inPlaceClips[clipName] = inPlace;
        }

        UpdateSwordAssetReferences(inPlaceClips, report);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, report.ToString(), Encoding.UTF8);
        Debug.Log("[SwordInPlaceAnimation] Wrote report: " + fullPath);
    }

    public static AnimationClip FindSwordComboClip(string clipName)
    {
        AnimationClip inPlace = AssetDatabase.LoadAssetAtPath<AnimationClip>(GetInPlaceClipPath(clipName));
        if (inPlace != null)
            return inPlace;

        return FindAnimationClipByName(clipName, false);
    }

    private static AnimationClip CreateOrUpdateInPlaceClip(string clipName, AnimationClip source, StringBuilder report)
    {
        report.AppendLine("## " + clipName);
        if (source == null)
        {
            report.AppendLine("Source=Missing");
            report.AppendLine();
            return null;
        }

        string sourcePath = AssetDatabase.GetAssetPath(source);
        string targetPath = GetInPlaceClipPath(clipName);
        AnimationClip target = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetPath);

        if (target == null)
        {
            target = UnityEngine.Object.Instantiate(source);
            target.name = clipName + "_InPlace";
            AssetDatabase.CreateAsset(target, targetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
            target.name = clipName + "_InPlace";
        }

        FlattenRootCurve(target, "RootT.x", report);
        FlattenRootCurve(target, "RootT.z", report);
        PreserveLoopOff(target);
        EditorUtility.SetDirty(target);

        report.AppendLine("SourcePath=" + sourcePath);
        report.AppendLine("TargetPath=" + targetPath);
        report.AppendLine("TargetLength=" + target.length.ToString("0.###"));
        report.AppendLine("LoopTime=" + AnimationUtility.GetAnimationClipSettings(target).loopTime);
        report.AppendLine();
        return target;
    }

    private static void FlattenRootCurve(AnimationClip clip, string propertyName, StringBuilder report)
    {
        EditorCurveBinding binding = new EditorCurveBinding
        {
            path = string.Empty,
            propertyName = propertyName,
            type = typeof(Animator)
        };

        AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(clip, binding);
        if (sourceCurve == null || sourceCurve.length == 0)
        {
            report.AppendLine(propertyName + "=Missing");
            return;
        }

        AnimationCurve flattened = new AnimationCurve();
        float startValue = sourceCurve.keys[0].value;
        float originalEndValue = sourceCurve.keys[sourceCurve.length - 1].value;

        for (int i = 0; i < sourceCurve.length; i++)
        {
            Keyframe sourceKey = sourceCurve.keys[i];
            Keyframe key = new Keyframe(sourceKey.time, startValue)
            {
                inTangent = 0f,
                outTangent = 0f,
                inWeight = sourceKey.inWeight,
                outWeight = sourceKey.outWeight,
                weightedMode = sourceKey.weightedMode
            };
            flattened.AddKey(key);
        }

        flattened.preWrapMode = sourceCurve.preWrapMode;
        flattened.postWrapMode = sourceCurve.postWrapMode;
        AnimationUtility.SetEditorCurve(clip, binding, flattened);
        report.AppendLine(propertyName + "=Flattened, keys=" + sourceCurve.length
            + ", start=" + startValue.ToString("0.###")
            + ", originalEnd=" + originalEndValue.ToString("0.###")
            + ", originalDelta=" + (originalEndValue - startValue).ToString("0.###"));
    }

    private static void PreserveLoopOff(AnimationClip clip)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
    }

    private static void UpdateSwordAssetReferences(Dictionary<string, AnimationClip> inPlaceClips, StringBuilder report)
    {
        WeaponItemData sword = AssetDatabase.LoadAssetAtPath<WeaponItemData>(SwordAssetPath);
        if (sword == null)
            throw new MissingReferenceException("[SwordInPlaceAnimation] Sword.asset not found.");

        report.AppendLine("## Sword.asset references");

        for (int comboIndex = 0; comboIndex < ComboClipNames.Length; comboIndex++)
        {
            string clipName = ComboClipNames[comboIndex];
            if (!inPlaceClips.TryGetValue(clipName, out AnimationClip inPlaceClip))
            {
                report.AppendLine(clipName + "=MissingInPlaceClip");
                continue;
            }

            bool assigned = false;
            MeleeComboDefinition comboDefinition = sword.GetMeleeComboDefinition();
            if (comboDefinition != null && comboDefinition.steps != null)
            {
                for (int stepIndex = 0; stepIndex < comboDefinition.steps.Length; stepIndex++)
                {
                    if (!string.Equals(comboDefinition.steps[stepIndex].attackName, clipName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    comboDefinition.steps[stepIndex].animationClip = inPlaceClip;
                    assigned = true;
                    report.AppendLine(clipName + "=" + AssetDatabase.GetAssetPath(inPlaceClip));
                    break;
                }
            }

            if (!assigned)
                report.AppendLine(clipName + "=StepMissing");
        }

        MeleeComboDefinition activeComboDefinition = sword.GetMeleeComboDefinition();
        if (activeComboDefinition != null)
            EditorUtility.SetDirty(activeComboDefinition);
        EditorUtility.SetDirty(sword);
        report.AppendLine();
    }

    private static void EnsureFolder(string folderPath)
    {
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

    private static string GetInPlaceClipPath(string clipName)
    {
        return InPlaceFolderPath + "/" + clipName + "_InPlace.anim";
    }

    private static AnimationClip FindAnimationClipByName(string clipName, bool includeInPlace)
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip");

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AnimationClip found = FindAnimationClipAtPath(path, clipName, true, includeInPlace);
            if (found != null)
                return found;
        }

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AnimationClip found = FindAnimationClipAtPath(path, clipName, false, includeInPlace);
            if (found != null)
                return found;
        }

        return null;
    }

    private static AnimationClip FindAnimationClipAtPath(string path, string clipName, bool exactMatch, bool includeInPlace)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null)
                continue;

            if (clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!includeInPlace && clip.name.EndsWith("_InPlace", StringComparison.OrdinalIgnoreCase))
                continue;

            if (exactMatch && string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                return clip;

            if (!exactMatch && NormalizeClipName(clip.name).Contains(NormalizeClipName(clipName)))
                return clip;
        }

        return null;
    }

    private static string NormalizeClipName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }
}
