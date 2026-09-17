using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SwordAnimationInPlaceAuditUtility
{
    private static readonly string[] ClipNames = { "slash1", "slash2", "slash3", "slash4", "slash6" };
    private const string ReportPath = "Logs/SwordAnimationInPlaceAudit.txt";

    [MenuItem("OVERBURST/User Tools/Audit/Sword Slash Animation InPlace")]
    public static void RunOnceFromCommandLine()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("Sword slash animation in-place audit");
        report.AppendLine("GeneratedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine();

        for (int i = 0; i < ClipNames.Length; i++)
        {
            AnimationClip clip = FindAnimationClipByName(ClipNames[i]);
            AppendClipReport(report, ClipNames[i], clip);
            report.AppendLine();
        }

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, report.ToString(), Encoding.UTF8);
        Debug.Log("[SwordAnimationInPlaceAudit] Wrote report: " + fullPath);
    }

    private static void AppendClipReport(StringBuilder report, string clipName, AnimationClip clip)
    {
        report.AppendLine("## " + clipName);
        if (clip == null)
        {
            report.AppendLine("Found=False");
            return;
        }

        string path = AssetDatabase.GetAssetPath(clip);
        AssetImporter importer = !string.IsNullOrEmpty(path) ? AssetImporter.GetAtPath(path) : null;
        ModelImporter modelImporter = importer as ModelImporter;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string assetShape = modelImporter != null
            ? "FBX sub asset / ModelImporter asset"
            : extension == ".anim"
                ? "independent .anim"
                : "other";

        report.AppendLine("ClipName=" + clip.name);
        report.AppendLine("AssetPath=" + path);
        report.AppendLine("AssetShape=" + assetShape);
        report.AppendLine("ImporterType=" + (importer != null ? importer.GetType().Name : "None"));
        report.AppendLine("ModelImporterAvailable=" + (modelImporter != null));
        report.AppendLine("AnimationType=" + (modelImporter != null ? modelImporter.animationType.ToString() : "N/A"));
        report.AppendLine("Length=" + clip.length.ToString("0.###"));
        report.AppendLine("LoopTime=" + AnimationUtility.GetAnimationClipSettings(clip).loopTime);

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        List<string> positionBindings = new List<string>();
        List<string> likelyForwardCurves = new List<string>();

        for (int i = 0; i < bindings.Length; i++)
        {
            EditorCurveBinding binding = bindings[i];
            if (!IsPositionRelated(binding))
                continue;

            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            string line = FormatBinding(binding, curve);
            positionBindings.Add(line);

            if (LooksLikeForwardMotion(binding, curve))
                likelyForwardCurves.Add(line);
        }

        report.AppendLine("PositionRelatedCurveCount=" + positionBindings.Count);
        report.AppendLine("PositionRelatedCurves:");
        if (positionBindings.Count == 0)
        {
            report.AppendLine("- None");
        }
        else
        {
            for (int i = 0; i < positionBindings.Count; i++)
                report.AppendLine("- " + positionBindings[i]);
        }

        report.AppendLine("LikelyForwardMotionCurves:");
        if (likelyForwardCurves.Count == 0)
        {
            report.AppendLine("- None");
        }
        else
        {
            for (int i = 0; i < likelyForwardCurves.Count; i++)
                report.AppendLine("- " + likelyForwardCurves[i]);
        }
    }

    private static bool IsPositionRelated(EditorCurveBinding binding)
    {
        return binding.propertyName.Contains("m_LocalPosition")
            || binding.propertyName.Contains("RootT")
            || binding.propertyName.Contains("MotionT")
            || binding.path.IndexOf("Hips", StringComparison.OrdinalIgnoreCase) >= 0
            || binding.path.IndexOf("Root", StringComparison.OrdinalIgnoreCase) >= 0
            || binding.path.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeForwardMotion(EditorCurveBinding binding, AnimationCurve curve)
    {
        if (curve == null || curve.length < 2)
            return false;

        bool possibleForwardAxis = binding.propertyName.EndsWith(".z", StringComparison.OrdinalIgnoreCase)
            || binding.propertyName.EndsWith(".x", StringComparison.OrdinalIgnoreCase)
            || binding.propertyName == "RootT.x"
            || binding.propertyName == "RootT.z"
            || binding.propertyName == "MotionT.x"
            || binding.propertyName == "MotionT.z";

        if (!possibleForwardAxis)
            return false;

        float first = curve.keys[0].value;
        float last = curve.keys[curve.length - 1].value;
        return Mathf.Abs(last - first) > 0.001f;
    }

    private static string FormatBinding(EditorCurveBinding binding, AnimationCurve curve)
    {
        int keyCount = curve != null ? curve.length : 0;
        float first = keyCount > 0 ? curve.keys[0].value : 0f;
        float last = keyCount > 0 ? curve.keys[keyCount - 1].value : 0f;
        float delta = last - first;
        return "path=\"" + binding.path
            + "\", property=\"" + binding.propertyName
            + "\", type=" + binding.type.Name
            + ", keys=" + keyCount
            + ", first=" + first.ToString("0.###")
            + ", last=" + last.ToString("0.###")
            + ", delta=" + delta.ToString("0.###");
    }

    private static AnimationClip FindAnimationClipByName(string clipName)
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip");

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AnimationClip found = FindAnimationClipAtPath(path, clipName, true);
            if (found != null)
                return found;
        }

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AnimationClip found = FindAnimationClipAtPath(path, clipName, false);
            if (found != null)
                return found;
        }

        return null;
    }

    private static AnimationClip FindAnimationClipAtPath(string path, string clipName, bool exactMatch)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null)
                continue;

            if (clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
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
