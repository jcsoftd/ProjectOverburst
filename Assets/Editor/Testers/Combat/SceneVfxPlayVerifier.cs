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
        if (Phase != 0) { lostWork = Phase == 2; EditorApplication.update += Tick; }
        else if (File.Exists(QueuePath)) EditorApplication.update += TryQueuedStart;
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
        EditorSceneManager.playModeStartScene = boot; Application.runInBackground = true; Phase = 1;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch { Phase = 3; throw; }
        return new { status = "STARTED", output, cycles = 2 };
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
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (Phase == 3) { Return(); return; }
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), CultureInfo.InvariantCulture)) throw new TimeoutException("Scene VFX test deadline.");
            if (!EditorApplication.isPlaying || IsolatedSavePlayGuard.ActiveDirectory != Account) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase == 1)
            {
                if (!AccountBootstrap.Ready || !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching) { readyFrame = -1; return; }
                if (readyFrame < 0) { readyFrame = Time.frameCount; return; }
                if (Time.frameCount - readyFrame < 10) return;
                EditorApplication.LockReloadAssemblies(); reloadLocked = true; AssetDatabase.DisallowAutoRefresh(); refreshLocked = true;
                work.Push(VfxSceneOwnershipChecks.Run(Path.Combine(Output, "Cycle" + SessionState.GetInt(Key + "cycle", 1)), Account)); Phase = 2;
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
        File.WriteAllText(Path.Combine(Output, "Cycle" + SessionState.GetInt(Key + "cycle", 1) + "-session.json"), JsonConvert.SerializeObject(new { status, error = error?.ToString() }, Formatting.Indented));
        Phase = 3;
        if (refreshLocked) { AssetDatabase.AllowAutoRefresh(); refreshLocked = false; }
        if (reloadLocked) { EditorApplication.UnlockReloadAssemblies(); reloadLocked = false; }
        if (EditorApplication.isPlaying && IsolatedSavePlayGuard.ActiveDirectory == Account) EditorApplication.ExitPlaymode();
    }
    static void Return()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { RequireNoForeign(); } catch { return; }
        foreach (string value in new[] { Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.ActiveDirectory, SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") }) if (!string.IsNullOrEmpty(value) && Path.GetFullPath(value) != Account) return;
        IsolatedSavePlayGuard.UseRealAccount();
        bool guard = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""));
        if (guard && SessionState.GetString(Key + "status", "") == "PASS_SCOPED" && SessionState.GetInt(Key + "cycle", 1) == 1)
        { SessionState.SetInt(Key + "cycle", 2); Phase = 1; readyFrame = -1; IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return; }
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "start", "")); Application.runInBackground = SessionState.GetBool(Key + "background", false);
        bool scenes = File.ReadAllText(Path.Combine(Output, "before.json")) == JsonConvert.SerializeObject(Scenes(), Formatting.Indented);
        File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(new { status = guard && scenes && SessionState.GetString(Key + "status", "") == "PASS_SCOPED" ? "PASS_SCOPED" : "FAIL", guard, scenePreserved = scenes, startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) }, Formatting.Indented));
        foreach (string suffix in new[] { "output", "start", "deadline", "status" }) SessionState.EraseString(Key + suffix);
        SessionState.EraseBool(Key + "background"); SessionState.EraseInt(Key + "cycle"); Phase = 0; EditorApplication.update -= Tick;
    }
    static void BeforeReload() { if (Phase == 2) Finish("FAIL_RELOADED", new InvalidOperationException("Scene VFX verification interrupted by reload.")); }
    static object[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => (object)new { s.path, s.isDirty, s.rootCount }).ToArray();
}
#endif
