using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class OverburstBuildUtility
{
    [MenuItem("OVERBURST/Build/Windows Development")]
    public static void BuildWindowsDevelopment()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        DirectoryInfo workspace = Directory.GetParent(root)
            ?? throw new DirectoryNotFoundException("OVERBURST workspace root was not found.");
        string output = Path.Combine(workspace.FullName, "개인파일", "코덱스산출", "Builds", "Windows_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Build(output);
    }

    // Run in a fresh Editor process with -executeMethod OverburstBuildUtility.BuildWindowsBatch.
    // Supply -overburstBuildOutput followed by a new absolute output directory.
    public static void BuildWindowsBatch()
    {
        int exitCode = 1;
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-overburstBuildOutput");
            if (index < 0 || index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]))
                throw new ArgumentException("-overburstBuildOutput requires an absolute directory.");
            exitCode = Build(args[index + 1], args.Contains("-overburstRelease")) ? 0 : 1;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }

    private static bool Build(string outputDirectory, bool release = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Finish Play Mode or the current build first.");
        if (!Directory.Exists("Assets/ProjectOverburst"))
            throw new InvalidOperationException("OVERBURST project root was not found.");
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("Build output already exists: " + outputDirectory);

        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        // 부팅 순서(Persistent → Hideout)는 고정하고, 그 뒤의 지도 던전 등 콘텐츠 씬은 빌드 설정을 따른다.
        string[] requiredPrefix = { "PersistentScene", "HideoutScene" };
        if (scenes.Length < requiredPrefix.Length
            || !scenes.Take(requiredPrefix.Length).Select(Path.GetFileNameWithoutExtension).SequenceEqual(requiredPrefix)
            || scenes.Any(scene => !File.Exists(scene)))
            throw new InvalidOperationException("Build scenes must start with PersistentScene, HideoutScene and every enabled scene must exist.");

        // Supplier build callbacks restore the active scene; batch startup can have an unnamed scene.
        if (string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenes[0]);

        Directory.CreateDirectory(outputDirectory);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outputDirectory, "OVERBURST.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = release ? BuildOptions.DetailedBuildReport : BuildOptions.Development
        });
        var data = new
        {
            result = report.summary.result.ToString(),
            configuration = release ? "Release" : "Development",
            errors = report.summary.totalErrors,
            warnings = report.summary.totalWarnings,
            size = report.summary.totalSize,
            duration = report.summary.totalTime.ToString(),
            project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            path = report.summary.outputPath,
            scenes = scenes,
            messages = report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning)
                .Select(message => new { type = message.type.ToString(), content = message.content }).ToArray()
        };
        File.WriteAllText(Path.Combine(outputDirectory, "build-report.json"),
            Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented));
        Debug.Log("[OVERBURST Build] " + data.result + " errors=" + data.errors + " warnings=" + data.warnings);
        return report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0;
    }
}
