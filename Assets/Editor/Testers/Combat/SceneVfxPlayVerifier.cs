#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SceneVfxPlayVerifier
{
    const string Key = "Overburst.KAN9SceneVfx.";
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static string Output => SessionState.GetString(Key + "output", "");
    static string Account => Path.Combine(Output, "Account");
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static bool lostWork, reloadLocked, refreshLocked;
    static int frame = -1, readyFrame = -1;
    static SceneVfxPlayVerifier()
    {
        if (Phase != 0 && Phase != 4) { lostWork = Phase == 2; EditorApplication.update += Tick; }
        else if (Phase == 0 && File.Exists(QueuePath)) EditorApplication.update += TryQueuedStart;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }
    static string QueuePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Jira/20261005_KAN_Goal/vfx-auto-start.json"));
    static void TryQueuedStart()
    {
        if (Phase != 0 || !File.Exists(QueuePath)) { EditorApplication.update -= TryQueuedStart; return; }
        var plan = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(QueuePath));
        if ((string)plan["status"] != "QUEUED") { EditorApplication.update -= TryQueuedStart; return; }
        if (DateTime.UtcNow > plan["deadlineUtc"].ToObject<DateTime>().ToUniversalTime())
        { plan["status"] = "DEFERRED_TIMEOUT"; File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed) return;
        try
        {
            var result = Start((string)plan["output"]); plan["status"] = "STARTED"; plan["result"] = Newtonsoft.Json.Linq.JToken.FromObject(result);
            File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart;
        }
        catch (Exception error)
        {
            if (Phase != 0 || Directory.Exists((string)plan["output"]))
            { plan["status"] = "FAILED_START"; plan["error"] = error.ToString(); File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart; }
        }
    }
    public static object Start(string outputDirectory)
    {
        if (Phase != 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Healthy idle Editor required.");
        RequireNoForeign();
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))) throw new InvalidOperationException("Previous account return required.");
        string output = Path.GetFullPath(outputDirectory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Fresh owned output required.");
        var boot = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        if (boot == null) throw new InvalidOperationException("Product boot scene missing.");
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "before.json"), JsonConvert.SerializeObject(Scenes(), Formatting.Indented));
        SessionState.SetString(Key + "output", output); SessionState.SetString(Key + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "background", Application.runInBackground); SessionState.SetInt(Key + "cycle", 1);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 600).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetString(Key + "status", "RUNNING");
        SessionState.SetInt(Key + "pid", System.Diagnostics.Process.GetCurrentProcess().Id);
        SessionState.SetString(Key + "project", Application.dataPath);
        EditorSceneManager.playModeStartScene = boot; Application.runInBackground = true; Phase = 1;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch { SessionState.SetString(Key + "status", "FAIL_START"); BeginReturn(); throw; }
        return new { status = "STARTED", output, cycles = 2 };
    }
    public static object StartRequiredFixes(string outputDirectory)
    {
        var result = Start(outputDirectory);
        SessionState.SetBool(Key + "requiredFixes", true);
        return result;
    }
    static void RequireNoForeign()
    {
        foreach (string key in new[] { "Overburst.WeakAttackPlayerLoop.plan", "Overburst.CombatPerformance.folder", "Overburst.CombatPerformance.phase", "Overburst.VisualPlay.Session.plan", "Overburst.VisualPlay.Session.deferredPlan", "Overburst.PlayerFootstepProductVerifier.Pending", "Overburst.CombatFacingVerifier.output", "Overburst.CrustaspikanMaterialVerifier.plan", "Overburst.KANGoal.BuildOwner" })
            if (!string.IsNullOrEmpty(SessionState.GetString(key, ""))) throw new InvalidOperationException("Foreign owner: " + key);
        if (SessionState.GetInt("Overburst.KAN14GUI.Phase", 0) != 0 || SessionState.GetInt("Overburst.KAN5StopFailure.phase", 0) != 0) throw new InvalidOperationException("Other owned goal verification still active.");
    }
    static void Tick()
    {
        if (Phase == 0) { EditorApplication.update -= Tick; return; }
        if (Phase == 4) { EditorApplication.update -= Tick; return; }
        if (Phase == 3)
        {
            try { Return(); }
            catch (Exception error) { SuspendReturn("Return error: " + error.Message); }
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), CultureInfo.InvariantCulture)) throw new TimeoutException("Scene VFX test deadline.");
            if (!EditorApplication.isPlaying || IsolatedSavePlayGuard.ActiveDirectory != Account) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase == 1)
            {
                if (!AccountBootstrap.Ready || !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching) { readyFrame = -1; return; }
                if (readyFrame < 0) { readyFrame = Time.frameCount; return; }
                if (Time.frameCount - readyFrame < 10) return;
                EditorApplication.LockReloadAssemblies(); reloadLocked = true; AssetDatabase.DisallowAutoRefresh(); refreshLocked = true;
                string cycle = Path.Combine(Output, "Cycle" + SessionState.GetInt(Key + "cycle", 1));
                work.Push(VfxSceneOwnershipChecks.Run(cycle, Account));
                if (SessionState.GetBool(Key + "requiredFixes", false)) work.Push(RequiredJiraFixChecks.Run(cycle));
                Phase = 2;
            }
            if (lostWork) throw new InvalidOperationException("Scene VFX coroutine was lost during domain reload.");
            if (frame == Time.frameCount) return; frame = Time.frameCount;
            while (work.Count > 0)
            {
                var current = work.Peek();
                if (current.MoveNext()) { if (current.Current is IEnumerator nested) { work.Push(nested); continue; } return; }
                (work.Pop() as IDisposable)?.Dispose();
            }
            Finish("PASS_SCOPED", null);
        }
        catch (Exception error) { Finish("FAIL", error); }
    }
    static void Finish(string status, Exception error)
    {
        while (work.Count > 0) try { (work.Pop() as IDisposable)?.Dispose(); } catch { }
        SessionState.SetString(Key + "status", status);
        BeginReturn();
        if (refreshLocked) { AssetDatabase.AllowAutoRefresh(); refreshLocked = false; }
        if (reloadLocked) { EditorApplication.UnlockReloadAssemblies(); reloadLocked = false; }
        try { File.WriteAllText(Path.Combine(Output, "Cycle" + SessionState.GetInt(Key + "cycle", 1) + "-session.json"), JsonConvert.SerializeObject(new { status, error = error?.ToString() }, Formatting.Indented)); }
        catch (Exception writeError) { SessionState.SetString(Key + "status", "FAIL_RESULT_WRITE"); Debug.LogWarning("[KAN9 검사] 결과 저장 실패; 반환을 계속합니다: " + writeError.Message); }
        finally { if (EditorApplication.isPlaying && IsolatedSavePlayGuard.ActiveDirectory == Account) EditorApplication.ExitPlaymode(); }
    }
    static void BeginReturn()
    {
        Phase = 3;
        SessionState.SetString(Key + "returnDeadlineUtc", DateTime.UtcNow.AddSeconds(120).ToString("o", CultureInfo.InvariantCulture));
        SessionState.SetString(Key + "returnReason", "Waiting for owned Play to finish and idle Editor.");
    }
    public static void CancelReturn()
    {
        if (Phase != 3) throw new InvalidOperationException("Only the pending return can be cancelled.");
        SuspendReturn("Cancelled; restoration information retained. ResumeReturn after the foreign owner finishes.");
    }
    public static void ResumeReturn()
    {
        if (Phase != 4 || string.IsNullOrEmpty(Output)) throw new InvalidOperationException("No deferred owned return.");
        BeginReturn(); EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    static void SuspendReturn(string reason)
    {
        Phase = 4; EditorApplication.update -= Tick;
        SessionState.SetString(Key + "returnReason", reason);
        try
        {
            if (!string.IsNullOrEmpty(Output)) File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(new { status = "DEFERRED", productStatus = SessionState.GetString(Key + "status", ""), reason, resume = "SceneVfxPlayVerifier.ResumeReturn() after healthy idle Editor and no foreign owner", account = Account, originalStartScene = SessionState.GetString(Key + "start", ""), originalBackground = SessionState.GetBool(Key + "background", false), callbackRemoved = true, restorationRetained = true }, Formatting.Indented));
        }
        catch (Exception error) { Debug.LogWarning("[KAN9 반환] 결과 저장 실패; SessionState의 복원 정보는 유지합니다: " + error.Message); }
    }
    static void Return()
    {
        string deadline = SessionState.GetString(Key + "returnDeadlineUtc", "");
        if (string.IsNullOrEmpty(deadline)) { BeginReturn(); deadline = SessionState.GetString(Key + "returnDeadlineUtc", ""); }
        if (DateTime.UtcNow >= DateTime.Parse(deadline, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
        { SuspendReturn("Return deadline expired: " + SessionState.GetString(Key + "returnReason", "")); return; }
        SessionState.SetString(Key + "returnReason", "Editor is playing, compiling, importing or building.");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;
        if (SessionState.GetInt(Key + "pid", 0) != System.Diagnostics.Process.GetCurrentProcess().Id || SessionState.GetString(Key + "project", "") != Application.dataPath)
        { SuspendReturn("Editor PID/project does not match owned session."); return; }
        try { RequireNoForeign(); } catch (Exception error) { SessionState.SetString(Key + "returnReason", error.Message); return; }
        foreach (string value in new[] { Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.ActiveDirectory, SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") })
            if (!string.IsNullOrEmpty(value) && !string.Equals(Path.GetFullPath(value), Account, StringComparison.OrdinalIgnoreCase)) { SessionState.SetString(Key + "returnReason", "Foreign account path: " + value); return; }
        string currentStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        if (currentStart != "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity" && currentStart != SessionState.GetString(Key + "start", ""))
        { SessionState.SetString(Key + "returnReason", "Start scene now belongs to another owner."); return; }
        IsolatedSavePlayGuard.UseRealAccount();
        bool guard = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""));
        if (guard && SessionState.GetString(Key + "status", "") == "PASS_SCOPED" && SessionState.GetInt(Key + "cycle", 1) == 1)
        { SessionState.SetInt(Key + "cycle", 2); Phase = 1; readyFrame = -1; SessionState.EraseString(Key + "returnDeadlineUtc"); SessionState.EraseString(Key + "returnReason"); IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return; }
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "start", "")); Application.runInBackground = SessionState.GetBool(Key + "background", false);
        bool scenes = File.ReadAllText(Path.Combine(Output, "before.json")) == JsonConvert.SerializeObject(Scenes(), Formatting.Indented);
        bool realPlayAllowed = IsolatedSavePlayGuard.CanEnter(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), 0, EditorApplication.timeSinceStartup, IsolatedSavePlayGuard.RequiresAccountChoice);
        File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(new { status = guard && realPlayAllowed && scenes && !EditorUtility.scriptCompilationFailed ? "PASS_SCOPED" : "FAIL", productStatus = SessionState.GetString(Key + "status", ""), guard, realPlayAllowed, scenePreserved = scenes, startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), callbackRemoved = true, pendingCleared = true }, Formatting.Indented));
        foreach (string suffix in new[] { "output", "start", "deadline", "status", "returnDeadlineUtc", "returnReason", "project" }) SessionState.EraseString(Key + suffix);
        SessionState.EraseInt(Key + "pid");
        SessionState.EraseBool(Key + "background"); SessionState.EraseBool(Key + "requiredFixes"); SessionState.EraseInt(Key + "cycle"); Phase = 0; EditorApplication.update -= Tick;
    }
    static void BeforeReload() { if (Phase == 2) Finish("FAIL_RELOADED", new InvalidOperationException("Scene VFX verification interrupted by reload.")); }
    static object[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => (object)new { s.path, s.isDirty, s.rootCount }).ToArray();
}
#endif
