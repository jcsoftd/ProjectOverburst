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
        string output = Path.Combine(root, "_개인파일", "Builds", "Windows_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
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
            exitCode = Build(args[index + 1]) ? 0 : 1;
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

    private static bool Build(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Finish Play Mode or the current build first.");
        if (!Directory.Exists("Assets/ProjectOverburst"))
            throw new InvalidOperationException("OVERBURST project root was not found.");
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("Build output already exists: " + outputDirectory);

        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        string[] expected = { "PersistentScene", "HideoutScene", "DungeonRunScene" };
        if (!scenes.Select(Path.GetFileNameWithoutExtension).SequenceEqual(expected) || scenes.Any(scene => !File.Exists(scene)))
            throw new InvalidOperationException("Expected PersistentScene, HideoutScene, DungeonRunScene in that order.");

        // Supplier build callbacks restore the active scene; batch startup can have an unnamed scene.
        if (string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenes[0]);

        Directory.CreateDirectory(outputDirectory);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outputDirectory, "OVERBURST.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        var data = new
        {
            result = report.summary.result.ToString(),
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
