using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class DungeonPhase1To5RegressionValidator
{
    private const string LogPath =
        "Logs/DungeonPhase1To5Regression.log";
    private const string SourceTileRoot =
        "Assets/ThirdParty/04_환경맵/Multistory Dungeons 2/"
        + "DunGen Presets/Top-Down Tiles";
    private const string SourceDemoRoot =
        "Assets/ThirdParty/04_환경맵/Multistory Dungeons 2/"
        + "DunGen Presets/Demo";
    private const string DunGenCodeRoot =
        "Assets/ThirdParty/10_툴/DunGen/Code";

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Validate Phases 1 to 5")]
    public static void ValidateFromMenu()
    {
        Debug.Log(ValidateOrThrow());
    }

    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = ValidateOrThrow();
            File.WriteAllText(LogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(LogPath, exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string ValidateOrThrow()
    {
        ThirdPartyFingerprint before =
            CaptureThirdPartyFingerprint();
        StringBuilder report = new();
        report.AppendLine(
            "[DungeonPhase1To5RegressionValidator] PASS");
        report.AppendLine();
        report.AppendLine(DungeonContentValidator.ValidateOrThrow());
        report.AppendLine();
        report.AppendLine(
            DungeonGenerationRegressionValidator.ValidateOrThrow());
        report.AppendLine();
        report.AppendLine(
            DungeonTraversalRegressionValidator.ValidateOrThrow());
        report.AppendLine();
        report.AppendLine(
            DungeonPortalAuthoringBuilder.ValidateOrThrow());
        report.AppendLine();
        report.AppendLine(DungeonRunSceneValidator.ValidateOrThrow());
        report.AppendLine();
        report.AppendLine(
            DungeonTilePerformanceAuthoringBuilder.ValidateOrThrow());

        ThirdPartyFingerprint after =
            CaptureThirdPartyFingerprint();
        Require(
            before.FileCount == after.FileCount
            && before.Hash == after.Hash,
            "검증 중 ThirdParty 관련 소스가 변경됨");
        report.AppendLine();
        report.AppendLine("[ThirdPartyMutationGuard] PASS");
        report.AppendLine($"FileCount={after.FileCount}");
        report.AppendLine($"Fingerprint={after.Hash}");
        report.AppendLine("ChangedFileCount=0");
        report.AppendLine();
        report.AppendLine("[PhaseGate]");
        report.AppendLine("Phase1Content=PASS");
        report.AppendLine("Phase2Generation=PASS");
        report.AppendLine("Phase3Traversal=PASS");
        report.AppendLine("Phase4PortalAuthoring=PASS");
        report.AppendLine("Phase5VisibilityAuthoring=PASS");
        report.AppendLine("Phase6Executed=0");
        return report.ToString().TrimEnd();
    }

    private static ThirdPartyFingerprint
        CaptureThirdPartyFingerprint()
    {
        List<string> paths = new();
        IReadOnlyList<string> tileNames =
            DungeonContentAuthoringBuilder.GetAllSourceTileNames();
        for (int i = 0; i < tileNames.Count; i++)
        {
            paths.Add(
                $"{SourceTileRoot}/{tileNames[i]}.prefab");
            paths.Add(
                $"{SourceTileRoot}/{tileNames[i]}.prefab.meta");
        }

        string[] demoFiles =
        {
            "TD Demo Dungeon Flow.asset",
            "TD Demo Dungeon Flow.asset.meta",
            "TD Demo Dungeon Archetype.asset",
            "TD Demo Dungeon Archetype.asset.meta",
            "TD Demo Tileset.asset",
            "TD Demo Tileset.asset.meta",
            "TD Demo Exits Tileset.asset",
            "TD Demo Exits Tileset.asset.meta"
        };
        for (int i = 0; i < demoFiles.Length; i++)
            paths.Add($"{SourceDemoRoot}/{demoFiles[i]}");

        string codePhysicalRoot =
            Path.GetFullPath(DunGenCodeRoot);
        string[] codeFiles = Directory.GetFiles(
            codePhysicalRoot,
            "*",
            SearchOption.AllDirectories);
        for (int i = 0; i < codeFiles.Length; i++)
        {
            string extension =
                Path.GetExtension(codeFiles[i]);
            if (!extension.Equals(
                    ".cs",
                    StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(
                    ".meta",
                    StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(
                    ".asmdef",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            paths.Add(
                Path.GetRelativePath(
                        Directory.GetCurrentDirectory(),
                        codeFiles[i])
                    .Replace('\\', '/'));
        }

        paths.Sort(StringComparer.Ordinal);
        using SHA256 fileHasher = SHA256.Create();
        using MemoryStream aggregateInput = new();
        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            Require(File.Exists(path),
                "ThirdParty 지문 대상 누락: " + path);
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            aggregateInput.Write(
                pathBytes,
                0,
                pathBytes.Length);
            aggregateInput.WriteByte(0);
            byte[] fileHash =
                fileHasher.ComputeHash(File.ReadAllBytes(path));
            aggregateInput.Write(
                fileHash,
                0,
                fileHash.Length);
        }

        using SHA256 aggregate = SHA256.Create();
        byte[] hash =
            aggregate.ComputeHash(aggregateInput.ToArray());
        return new ThirdPartyFingerprint(
            paths.Count,
            BitConverter.ToString(hash).Replace("-", string.Empty));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private readonly struct ThirdPartyFingerprint
    {
        public ThirdPartyFingerprint(int fileCount, string hash)
        {
            FileCount = fileCount;
            Hash = hash;
        }

        public int FileCount { get; }
        public string Hash { get; }
    }
}
