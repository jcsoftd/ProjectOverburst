using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 재로딩 뒤에도 소유 빌드 예약을 이어가되, 공유 검증/계정이 사용 중이면 실행하지 않는다.
[InitializeOnLoad]
public static class KanRecoveryPlayerBuild
{
    [Serializable] sealed class Plan { public string status, output, error, deadlineUtc; public double quietUntil; }
    static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Jira/20261005_KAN_Goal"));
    static string PlanPath => Path.Combine(Root, "build-queue.json");
    static Plan plan;
    static readonly string[] ForeignKeys = { "Overburst.CombatPerformance.folder", "Overburst.CombatPerformance.phase",
        "Overburst.VisualPlay.Session.plan", "Overburst.VisualPlay.Session.deferredPlan",
        "Overburst.PlayerFootstepProductVerifier.Pending", "Overburst.CombatFacingVerifier.output",
        "Overburst.CrustaspikanMaterialVerifier.plan" };

    static KanRecoveryPlayerBuild()
    {
        if (!File.Exists(PlanPath)) return;
        try { plan = JsonConvert.DeserializeObject<Plan>(File.ReadAllText(PlanPath)); }
        catch { return; }
        if (plan == null || plan.status != "QUEUED") return;
        plan.quietUntil = 0;
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }

    static void Persist() => File.WriteAllText(PlanPath, JsonConvert.SerializeObject(plan, Formatting.Indented));
    static void BeforeReload() { if (plan != null && plan.status == "QUEUED") { plan.quietUntil = 0; Persist(); } Detach(); }
    static void Detach() { EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload; }
    static string BusyReason()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "shared Play";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "compile/import";
        if (EditorUtility.scriptCompilationFailed) return "compilation errors";
        if (BuildPipeline.isBuildingPlayer) return "another build";
        foreach (string key in ForeignKeys) if (!string.IsNullOrEmpty(SessionState.GetString(key, ""))) return key;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            || IsolatedSavePlayGuard.RequiresAccountChoice) return "account return pending";
        return null;
    }

    static void Tick()
    {
        if (plan == null || plan.status != "QUEUED") { Detach(); return; }
        if (!DateTime.TryParse(plan.deadlineUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var deadline)
            || DateTime.UtcNow > deadline.ToUniversalTime())
        { plan.status = "DEFERRED"; plan.error = "Owned queue deadline expired: " + BusyReason(); Persist(); Detach(); return; }
        string busy = BusyReason();
        if (busy != null) { plan.quietUntil = 0; return; }
        if (plan.quietUntil == 0) { plan.quietUntil = EditorApplication.timeSinceStartup + 5; return; }
        if (EditorApplication.timeSinceStartup < plan.quietUntil) return;
        string expected = Path.GetFullPath(plan.output);
        if (Path.GetDirectoryName(expected) != Root || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(expected), "^Build[0-9]{2}$") || Directory.Exists(expected))
        { plan.status = "FAIL"; plan.error = "Build output changed/already exists; inspect artifacts."; Persist(); Detach(); return; }
        plan.status = "BUILDING"; plan.error = null; Persist(); Detach();
        SessionState.SetString("Overburst.KANGoal.BuildOwner", expected);
        try
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length < 3 || !scenes[0].EndsWith("/PersistentScene.unity") || !scenes[1].EndsWith("/HideoutScene.unity") || scenes.Any(s => !File.Exists(s)))
                throw new InvalidOperationException("Real Persistent/Hideout/dungeon build scenes required.");
            var before = Scenes();
            Directory.CreateDirectory(expected);
            File.WriteAllText(Path.Combine(expected, "build-start.json"), JsonConvert.SerializeObject(new
            { utc = DateTime.UtcNow, pid = System.Diagnostics.Process.GetCurrentProcess().Id, scenes, before, unity = Application.unityVersion }, Formatting.Indented));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = Path.Combine(expected, "OVERBURST.exe"), target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.DetailedBuildReport,
                extraScriptingDefines = new[] { "OVERBURST_SAVE_RECOVERY_QA" }
            });
            var after = Scenes();
            bool preserved = JsonConvert.SerializeObject(before) == JsonConvert.SerializeObject(after);
            var result = new { status = report.summary.result.ToString(), errors = report.summary.totalErrors,
                warnings = report.summary.totalWarnings, duration = report.summary.totalTime.TotalSeconds,
                report.summary.outputPath, unity = Application.unityVersion, scenes, before, after, sceneStatePreserved = preserved,
                definitions = new[] { "DEVELOPMENT_BUILD", "OVERBURST_SAVE_RECOVERY_QA" },
                messages = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                    .Select(m => m.content).ToArray() };
            File.WriteAllText(Path.Combine(expected, "build-report.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0) throw new InvalidOperationException("Player build failed.");
            if (!preserved) throw new InvalidOperationException("User dirty scene state changed during build.");
            plan.status = "PASS";
        }
        catch (Exception error)
        {
            plan.status = "FAIL"; plan.error = error.ToString();
            if (Directory.Exists(expected)) File.WriteAllText(Path.Combine(expected, "build-error.txt"), error.ToString());
        }
        finally { SessionState.EraseString("Overburst.KANGoal.BuildOwner"); Persist(); }
    }

    static object[] Scenes() => Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i))
        .Select(s => (object)new { s.path, s.isDirty, s.rootCount }).ToArray();
}
