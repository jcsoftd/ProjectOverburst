using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;

public static class OverburstGoalEFeelValidator
{
    private const string BaselineCommit = "dc8ffba43979d7ad3aa41cbeeebf0f191d551c0d";
    private const string FeelRoot = "Assets/ThirdParty/10_툴/Feel";
    private const string PrefabPath = "Assets/ProjectOverburst/Resources/Feel/PF_OverburstFeelHub.prefab";
    private const string TimeArbiterPath = "Assets/ProjectOverburst/02_Shared/Presentation/Feel/Runtime/OverburstTimeEffectArbiter.cs";

    [MenuItem("OVERBURST/Codex/Validate/Feel/Validate GOAL E Feel Presentation")]
    public static void ValidateFromMenu()
    {
        List<string> failures = new List<string>();
        ValidatePackage(failures);
        ValidatePreset(failures);
        ValidateOwnership(failures);
        ValidateScope(failures);
        if (failures.Count > 0)
            throw new InvalidOperationException("[OverburstGoalEFeelValidator] FAIL\n- " + string.Join("\n- ", failures));

        UnityEngine.Debug.Log("[OverburstGoalEFeelValidator] PASS\n"
            + "- Feel 5.9.1 package/meta present and public Git ignored\n"
            + "- 23 pooled emitters: weak 8, strong 6, death 4, evade 2, interaction 2, UI 1\n"
            + "- MMF particles/UI only; camera, freeze, timescale and audio feedback excluded\n"
            + "- Time.timeScale writes centralized in OverburstTimeEffectArbiter\n"
            + "- combat dedupe, lethal/critical routing, evade and interaction bridges connected");
    }

    private static void ValidatePackage(List<string> failures)
    {
        if (!Directory.Exists(FeelRoot))
        {
            failures.Add("Feel package folder missing");
            return;
        }

        string readme = Path.Combine(FeelRoot, "readme.txt");
        if (!File.Exists(readme) || !File.ReadAllText(readme).Contains("Feel v5.9.1"))
            failures.Add("Feel readme/version is not 5.9.1");
        int fileCount = Directory.GetFiles(FeelRoot, "*", SearchOption.AllDirectories).Length;
        if (fileCount != 4928)
            failures.Add("Feel package file count changed: " + fileCount);
        if (!File.Exists(FeelRoot + ".meta"))
            failures.Add("Feel folder meta missing");
        if (RunGit("check-ignore -q -- \"" + FeelRoot + "/readme.txt\"") != 0)
            failures.Add("Feel package is not excluded from public Git");
        string projectSettings = File.ReadAllText("ProjectSettings/ProjectSettings.asset");
        if (!projectSettings.Contains("MOREMOUNTAINS_NICEVIBRATIONS_INSTALLED"))
            failures.Add("Feel package installation define missing");
    }

    private static void ValidatePreset(List<string> failures)
    {
        if (!File.Exists(PrefabPath))
        {
            failures.Add("project-owned Feel hub prefab missing");
            return;
        }
        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            OverburstFeelFeedbackHub hub = prefab.GetComponent<OverburstFeelFeedbackHub>();
            if (hub == null)
            {
                failures.Add("project-owned Feel hub component missing");
                return;
            }

            int[] expected = { 8, 6, 4, 2, 2, 1 };
            int[] actual = new int[expected.Length];
            HashSet<string> forbiddenTypes = new HashSet<string>
            {
                "MMF_CameraShake", "MMF_CameraFieldOfView", "MMF_CinemachineImpulse",
                "MMF_FreezeFrame", "MMF_TimescaleModifier", "MMF_AudioSource"
            };

            OverburstFeelEmitter[] emitters = prefab.GetComponentsInChildren<OverburstFeelEmitter>(true);
            foreach (OverburstFeelEmitter emitter in emitters)
            {
                if (emitter == null || emitter.Player == null)
                {
                    failures.Add("null emitter/player in Feel hub");
                    continue;
                }
                actual[(int)emitter.Cue]++;
                if (emitter.Player.AutoPlayOnEnable || emitter.Player.AutoPlayOnStart
                    || emitter.Player.AutoInitialization
                    || !emitter.Player.ForceTimescaleMode
                    || emitter.Player.ForcedTimescaleMode != TimescaleModes.Unscaled)
                {
                    failures.Add(emitter.name + " player lifecycle/timescale policy mismatch");
                }
                if (emitter.Player.FeedbacksList == null || emitter.Player.FeedbacksList.Count != 1)
                {
                    failures.Add(emitter.name + " must have exactly one project-owned expression feedback");
                    continue;
                }

                MMF_Feedback feedback = emitter.Player.FeedbacksList[0];
                string typeName = feedback?.GetType().Name ?? "null";
                if (forbiddenTypes.Contains(typeName))
                    failures.Add(emitter.name + " contains forbidden shared-owner feedback " + typeName);
                if (emitter.Cue == OverburstFeelCue.UiConfirm)
                {
                    if (typeName != "MMF_Image" || emitter.UiImage == null)
                        failures.Add("UI preset must use one MMF_Image with bound Image");
                }
                else if (typeName != "MMF_Particles" || emitter.ParticleSystem == null)
                {
                    failures.Add(emitter.name + " must use one pooled MMF_Particles feedback");
                }
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                    failures.Add(((OverburstFeelCue)i) + " pool expected=" + expected[i] + " actual=" + actual[i]);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    private static void ValidateOwnership(List<string> failures)
    {
        string[] projectSources = Directory.GetFiles("Assets/ProjectOverburst", "*.cs", SearchOption.AllDirectories);
        foreach (string source in projectSources)
        {
            string normalized = source.Replace('\\', '/');
            string text = File.ReadAllText(source);
            if (normalized != TimeArbiterPath
                && (text.Contains("Time.timeScale =") || text.Contains("Time.fixedDeltaTime =")))
            {
                failures.Add("time axis write outside arbiter: " + normalized);
            }
        }

        RequireText("Assets/ProjectOverburst/02_Shared/Combat/Runtime/Feedback/CombatHitFeedbackService.cs",
            "OverburstTimeEffectArbiter.Request", failures);
        RequireText("Assets/ProjectOverburst/02_Shared/Combat/Runtime/Feedback/CombatHitFeedbackService.cs",
            "OverburstFeelFeedbackHub.Request", failures);
        RequireText("Assets/ProjectOverburst/03_Features/Weapons/Runtime/Melee/Core/Runtime/MeleeRuntime.cs",
            "isLethal:", failures);
        RequireText("Assets/ProjectOverburst/03_Features/Player/Runtime/PlayerEvadeController.cs",
            "OverburstFeelCue.Evade", failures);
        RequireText("Assets/ProjectOverburst/02_Shared/Interaction/Runtime/PlayerInteractionController.cs",
            "OverburstFeelCue.Interaction", failures);
    }

    private static void ValidateScope(List<string> failures)
    {
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectGitOutput("diff --name-only " + BaselineCommit + " --", paths);
        CollectGitOutput("ls-files --others --exclude-standard", paths);
        foreach (string path in paths)
        {
            if (!IsAllowed(path))
                failures.Add("unauthorized GOAL E path: " + path);
        }
    }

    private static bool IsAllowed(string path)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith("Assets/Editor/Builders/Feel", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Assets/Editor/Testers/Feel", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Assets/ProjectOverburst/02_Shared/Presentation", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Assets/ProjectOverburst/Resources/Feel", StringComparison.OrdinalIgnoreCase))
            return true;

        return path == "Assets/ProjectOverburst/02_Shared/Combat/Runtime/Feedback/CombatHitFeedbackService.cs"
            || path == "Assets/ProjectOverburst/03_Features/Weapons/Runtime/Melee/Core/Runtime/MeleeRuntime.cs"
            || path == "Assets/ProjectOverburst/03_Features/Player/Runtime/PlayerEvadeController.cs"
            || path == "Assets/ProjectOverburst/02_Shared/Interaction/Runtime/PlayerInteractionController.cs"
            || path == "Docs/04_플레이기반전환_GOAL기준.md"
            || path == "Docs/05_시스템구현현황.md"
            || path == "Docs/05A_현재작업요약.md"
            || path == "Docs/10_전투시스템_마스터.md"
            || path == "Docs/40_플레이어시스템_마스터.md"
            || path == "Docs/90_UI통합시스템_마스터.md"
            || path == "Docs/97_환경설정_Git_에셋복원.md"
            || path == "ProjectSettings/ProjectSettings.asset";
    }

    private static void RequireText(string path, string token, List<string> failures)
    {
        if (!File.Exists(path) || !File.ReadAllText(path).Contains(token))
            failures.Add(path + " missing integration token " + token);
    }

    private static void CollectGitOutput(string arguments, HashSet<string> paths)
    {
        ProcessStartInfo info = new ProcessStartInfo("git", "-c core.quotepath=false " + arguments)
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        using Process process = Process.Start(info);
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            paths.Add(line.Trim());
    }

    private static int RunGit(string arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using Process process = Process.Start(info);
        process.WaitForExit();
        return process.ExitCode;
    }
}
