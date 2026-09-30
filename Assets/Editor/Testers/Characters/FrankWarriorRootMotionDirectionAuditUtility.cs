using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class FrankWarriorRootMotionDirectionAuditUtility
{
    private const string RootMotionFolder =
        "Assets/ThirdParty/03_애니메이션/Frank_Slash_Pack/Assets/Animations/Frank_SlashPack_Warrior/FBX_Animation/Root_Motion_8Way";
    private const string PendingFlagPath = "Temp/AuditFrankWarriorRootMotionDirections.flag";
    private const string ReportPath = "Logs/FrankWarriorRootMotionDirectionAudit.txt";

    private static readonly string[] DirectionSuffixes =
    {
        "F", "B", "L", "R", "FL", "FR", "BL", "BR"
    };

    [MenuItem("OVERBURST/Codex/Analyze/Animation/Audit Frank Warrior Root Motion Directions")]
    public static void RunOnceFromCommandLine()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("Frank Warrior Root Motion 8-way direction audit");
        report.AppendLine("GeneratedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine("CoordinateBasis=Unity local X/Z, project forward=+Z");
        report.AppendLine();

        for (int i = 0; i < DirectionSuffixes.Length; i++)
            AppendDirectionAudit(DirectionSuffixes[i], report);

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, report.ToString(), Encoding.UTF8);
        Debug.Log("[FrankWarriorRootMotionDirectionAudit] Wrote report: " + fullPath);
    }

    [InitializeOnLoadMethod]
    private static void RunPendingAuditAfterScriptReload()
    {
        if (Application.isBatchMode)
            return;

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), PendingFlagPath);
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

    private static void AppendDirectionAudit(string suffix, StringBuilder report)
    {
        string path = RootMotionFolder + "/Frank_RPG_Warrior@Velocity_8Way_Walk_" + suffix + ".FBX";
        AnimationClip clip = LoadMainClip(path);
        if (clip == null)
        {
            report.AppendLine(suffix + ": MissingClip=" + path);
            return;
        }

        Vector3 averageSpeed = clip.averageSpeed;
        Vector2 planarSpeed = new Vector2(averageSpeed.x, averageSpeed.z);
        string classifiedDirection = planarSpeed.sqrMagnitude > 0.000001f
            ? ClassifyDirection(planarSpeed.normalized)
            : "None";

        report.AppendLine(suffix + ":");
        report.AppendLine("  Asset=" + path);
        report.AppendLine("  Clip=" + clip.name);
        report.AppendLine("  AverageSpeed=" + FormatVector(averageSpeed));
        report.AppendLine("  NormalizedXZ=" + FormatVector2(planarSpeed.sqrMagnitude > 0.000001f ? planarSpeed.normalized : Vector2.zero));
        report.AppendLine("  ProjectDirection=" + classifiedDirection);
    }

    private static AnimationClip LoadMainClip(string path)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        AnimationClip fallback = null;
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null || clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(clip.name, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))
                return clip;

            if (fallback == null)
                fallback = clip;
        }

        return fallback;
    }

    private static string ClassifyDirection(Vector2 direction)
    {
        float angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
        int octant = Mathf.RoundToInt(angle / 45f);
        switch (octant)
        {
            case 0: return "F";
            case 1: return "FR";
            case 2: return "R";
            case 3: return "BR";
            case 4:
            case -4: return "B";
            case -3: return "BL";
            case -2: return "L";
            case -1: return "FL";
            default: return "Unknown";
        }
    }

    private static string FormatVector(Vector3 value)
    {
        return "(" + value.x.ToString("0.###") + ", " + value.y.ToString("0.###") + ", " + value.z.ToString("0.###") + ")";
    }

    private static string FormatVector2(Vector2 value)
    {
        return "(" + value.x.ToString("0.###") + ", " + value.y.ToString("0.###") + ")";
    }
}
