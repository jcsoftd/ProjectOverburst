using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Overburst.DebugTools.Performance;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CombatPerformanceSession
{
    const string Key = "Overburst.CombatPerformance.";
    const string Scene = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    public static string ActiveFolder => SessionState.GetString(Key + "folder", "");
    public static bool Busy => !string.IsNullOrEmpty(ActiveFolder);
    public static string LastFolder => EditorPrefs.GetString(Key + "last", "");
    public static string Status => CombatPerformanceRunner.Current != null ? CombatPerformanceRunner.Current.Progress
        : Busy ? "Editor 전환/반환 중" : "대기";
    [Serializable] sealed class SceneState { public string path; public bool dirty, loaded, active; public int roots; }
    [Serializable] sealed class Before
    {
        public int pid; public string startScene, project, accountHash, inputSettings;
        public bool playOptionsEnabled; public EnterPlayModeOptions playOptions;
        public SceneState[] scenes;
    }
    [Serializable] sealed class Returned
    {
        public string status, reason, utc, environment, active, prepared, expires;
        public bool choice, stopped, compiling, updating, accountPreserved, scenePreserved, startScenePreserved, inputPreserved;
        public int pid; public SceneState[] scenes;
        public long privateBytesBefore, privateBytesAfter, workingSetBefore, workingSetAfter;
        public int desiredWorkers, standbyWorkers;
    }
    static CombatPerformanceSession()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        EditorApplication.update += Tick;
    }
    static SceneState[] Scenes()
    {
        var states = new SceneState[SceneManager.sceneCount];
        for (int i = 0; i < states.Length; i++) { var s = SceneManager.GetSceneAt(i); states[i] = new SceneState { path = s.path, dirty = s.isDirty, loaded = s.isLoaded, active = s == SceneManager.GetActiveScene(), roots = s.rootCount }; }
        return states;
    }
    public static string Start(CombatPerformanceProfile profile)
    {
        profile.Validate();
        if (profile.mode == CombatPerformanceMode.Observation) return StartObservation(profile);
        if (Busy || BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("유휴 EditMode이고 일반 계정 반환이 완료된 상태에서 실행하세요.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene) == null || AssetDatabase.LoadAssetAtPath<WeaponItemData>(profile.weaponAssetPath) == null)
            throw new InvalidOperationException("제품 시작 씬 또는 검사 무기가 없습니다.");
        foreach (string id in profile.themes) if (MapThemeCatalog.Resolve(id) == null) throw new InvalidOperationException("테마 누락: " + id);
        string folder = NewFolder();
        var before = new Before { pid = System.Diagnostics.Process.GetCurrentProcess().Id, project = Application.dataPath,
            startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), scenes = Scenes(), accountHash = AccountHash(),
            inputSettings = InputSettingsHash(), playOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled, playOptions = EditorSettings.enterPlayModeOptions };
        CombatPerformancePaths.SaveJson(Path.Combine(folder, "editor-before.json"), before);
        CombatPerformancePaths.SaveJson(Path.Combine(folder, "profile.json"), profile);
        string fingerprint = ContentFingerprint(profile);
        SessionState.SetString(Key + "folder", folder); SessionState.SetString(Key + "phase", "starting");
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetString(Key + "fingerprint", fingerprint);
        SessionState.SetString(Key + "baseline", EditorPrefs.GetString(Key + "baseline", ""));
        EditorPrefs.SetString(Key + "last", folder);
        try
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene);
            IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(folder, "IsolatedAccount"));
        }
        catch
        {
            SessionState.SetString(Key + "phase", "returning");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture));
            throw;
        }
        return folder;
    }
    static string NewFolder()
    {
        string folder = Path.Combine(CombatPerformancePaths.OutputRoot, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        CombatPerformancePaths.RequireOutput(folder); Directory.CreateDirectory(folder); return folder;
    }
    static string StartObservation(CombatPerformanceProfile profile)
    {
        if (Busy || !EditorApplication.isPlaying || CombatPerformanceRunner.Current != null) throw new InvalidOperationException("기존 Play에서 실행 중인 성능 검사가 없어야 합니다.");
        string folder = NewFolder();
        CombatPerformanceRunner.StartRun(profile, folder, revision: Revision(), fingerprint: ContentFingerprint(profile));
        EditorPrefs.SetString(Key + "last", folder); return folder;
    }
    static string Revision()
    {
        string git = Path.GetFullPath(Path.Combine(Application.dataPath, "../.git/HEAD"));
        if (!File.Exists(git)) return "UNKNOWN";
        string head = File.ReadAllText(git).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return head;
        string reference = Path.Combine(Path.GetDirectoryName(git), head.Substring(5));
        return File.Exists(reference) ? File.ReadAllText(reference).Trim() : head.Substring(5);
    }
    public static string ContentFingerprint(CombatPerformanceProfile profile)
    {
        var b = new StringBuilder();
        string sourceRoot = Path.Combine(Application.dataPath, "ProjectOverburst");
        var files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories); Array.Sort(files, StringComparer.Ordinal);
        using (var sha = SHA256.Create()) foreach (string file in files)
        { b.Append(file.Substring(Application.dataPath.Length)).Append(':').Append(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)))); }
        b.Append(AssetDatabase.GetAssetDependencyHash(Scene)); b.Append(AssetDatabase.GetAssetDependencyHash(profile.weaponAssetPath));
        foreach (string theme in profile.themes)
        { var table = MapThemeCatalog.Resolve(theme); if (table != null) b.Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(table))); }
        return CombatPerformancePaths.Hash(b.ToString());
    }
    static string AccountHash()
    {
        string path = Path.Combine(Application.persistentDataPath, "Account");
        if (!Directory.Exists(path)) return "ABSENT";
        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories); Array.Sort(files, StringComparer.Ordinal);
        var b = new StringBuilder();
        using (var sha = SHA256.Create()) foreach (var file in files) b.Append(file.Substring(path.Length)).Append(':').Append(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))));
        return CombatPerformancePaths.Hash(b.ToString());
    }
    static string InputSettingsHash()
    {
        string file = Path.Combine(Application.dataPath, "ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions");
        return File.Exists(file) ? CombatPerformancePaths.Hash(File.ReadAllText(file)) : "ABSENT";
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (!Busy) return;
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetString(Key + "phase", "") == "starting")
        {
            string folder = ActiveFolder;
            try
            {
                if (!Same(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), Path.Combine(folder, "IsolatedAccount"))
                    || !Same(IsolatedSavePlayGuard.ActiveDirectory, Path.Combine(folder, "IsolatedAccount"))) throw new InvalidOperationException("본인 격리 Play 소유권 불일치");
                var profile = JsonUtility.FromJson<CombatPerformanceProfile>(File.ReadAllText(Path.Combine(folder, "profile.json")));
                SessionState.SetString(Key + "phase", "running");
                var runner = CombatPerformanceRunner.StartRun(profile, folder, AssetDatabase.LoadAssetAtPath<WeaponItemData>(profile.weaponAssetPath), Revision(), SessionState.GetString(Key + "fingerprint", ""));
                runner.Completed += Completed;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(folder, "start-error.txt"), error.ToString());
                SessionState.SetString(Key + "phase", "returning");
                SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture));
                if (Same(IsolatedSavePlayGuard.ActiveDirectory, Path.Combine(folder, "IsolatedAccount"))) EditorApplication.ExitPlaymode();
            }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetString(Key + "phase", "returning");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture));
            // All EnteredEditMode listeners, including IsolatedSavePlayGuard, finish before the next update.
        }
    }
    static void Completed(CombatPerformanceRun run)
    {
        if (Busy && Same(IsolatedSavePlayGuard.ActiveDirectory, Path.Combine(ActiveFolder, "IsolatedAccount")))
        {
            SessionState.SetString(Key + "phase", "returning");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture));
            EditorApplication.ExitPlaymode();
        }
    }
    static void BeforeReload()
    {
        if (!Busy || CombatPerformanceRunner.Current == null) return;
        CombatPerformanceRunner.Current.Stop();
    }
    public static void Stop()
    {
        if (CombatPerformanceRunner.Current != null) CombatPerformanceRunner.Current.Stop();
        else if (Busy)
        {
            SessionState.SetString(Key + "phase", "returning");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture));
            string own = Path.Combine(ActiveFolder, "IsolatedAccount");
            if (EditorApplication.isPlayingOrWillChangePlaymode && (Same(IsolatedSavePlayGuard.ActiveDirectory, own)
                || Same(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), own))) EditorApplication.isPlaying = false;
        }
    }
    static bool Same(string a, string b)
    { try { return !string.IsNullOrEmpty(a) && string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    static void Tick()
    {
        if (!Busy) return;
        string phase = SessionState.GetString(Key + "phase", "");
        if (phase == "running" && EditorApplication.isPlaying && CombatPerformanceRunner.Current == null)
        { SessionState.SetString(Key + "phase", "returning"); SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture)); Stop(); return; }
        double.TryParse(SessionState.GetString(Key + "deadline", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double deadline);
        if (phase == "starting" && EditorApplication.timeSinceStartup > deadline)
        { Stop(); return; }
        if (phase != "returning" || BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string own = Path.Combine(ActiveFolder, "IsolatedAccount");
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if ((!string.IsNullOrEmpty(env) && !Same(env, own)) || (!string.IsNullOrEmpty(prepared) && !Same(prepared, own)) || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))
        {
            if (EditorApplication.timeSinceStartup > deadline)
            { File.WriteAllText(Path.Combine(ActiveFolder, "return-deferred.txt"), "다른 격리 실행이 계정을 사용 중입니다. 유휴 상태에서 본인 반환을 다시 실행하세요."); SessionState.SetString(Key + "phase", "deferred"); }
            return;
        }
        ReturnEditor();
    }
    public static void RetryReturn()
    { if (Busy) { SessionState.SetString(Key + "phase", "returning"); SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 120).ToString("R", CultureInfo.InvariantCulture)); } }
    static void ReturnEditor()
    {
        string folder = ActiveFolder;
        var before = JsonUtility.FromJson<Before>(File.ReadAllText(Path.Combine(folder, "editor-before.json")));
        var returned = new Returned { status = "FAIL", utc = DateTime.UtcNow.ToString("O"), pid = System.Diagnostics.Process.GetCurrentProcess().Id };
        try
        {
            if (before.pid != returned.pid || before.project != Application.dataPath) throw new InvalidOperationException("Editor 프로세스 또는 프로젝트가 달라졌습니다.");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(before.startScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(before.startScene);
            IsolatedSavePlayGuard.UseRealAccount();
            returned.environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
            returned.active = IsolatedSavePlayGuard.ActiveDirectory; returned.prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
            returned.expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""); returned.choice = IsolatedSavePlayGuard.RequiresAccountChoice;
            returned.stopped = !EditorApplication.isPlayingOrWillChangePlaymode; returned.compiling = EditorApplication.isCompiling; returned.updating = EditorApplication.isUpdating;
            returned.accountPreserved = before.accountHash == AccountHash(); returned.inputPreserved = before.inputSettings == InputSettingsHash();
            returned.scenes = Scenes(); returned.scenePreserved = JsonUtility.ToJson(new SceneContainer { scenes = before.scenes }) == JsonUtility.ToJson(new SceneContainer { scenes = returned.scenes });
            returned.startScenePreserved = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == before.startScene;
            if (!returned.choice && returned.stopped && !returned.compiling && !returned.updating && returned.accountPreserved && returned.inputPreserved && returned.scenePreserved && returned.startScenePreserved
                && string.IsNullOrEmpty(returned.environment) && string.IsNullOrEmpty(returned.active) && string.IsNullOrEmpty(returned.prepared) && string.IsNullOrEmpty(returned.expires)
                && before.playOptionsEnabled == EditorSettings.enterPlayModeOptionsEnabled && before.playOptions == EditorSettings.enterPlayModeOptions)
            {
                returned.status = "PASS";
                // Run once after owned Play has stopped. The sample stream is already closed.
                var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh();
                returned.privateBytesBefore = process.PrivateMemorySize64; returned.workingSetBefore = process.WorkingSet64;
                int desired = AssetDatabase.DesiredWorkerCount, standby = EditorUserSettings.standbyImportWorkerCount;
                returned.desiredWorkers = desired; returned.standbyWorkers = standby;
                try { EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect(); AssetDatabase.DesiredWorkerCount = 0; EditorUserSettings.standbyImportWorkerCount = 0; AssetDatabase.ForceToDesiredWorkerCount(); }
                finally { AssetDatabase.DesiredWorkerCount = desired; EditorUserSettings.standbyImportWorkerCount = standby; }
                process.Refresh(); returned.privateBytesAfter = process.PrivateMemorySize64; returned.workingSetAfter = process.WorkingSet64;
            }
            else returned.reason = "계정/씬/입력/시작 씬 또는 Editor 반환 조건 불일치";
            string runPath = Path.Combine(folder, "run.json");
            if (File.Exists(runPath))
            {
                var run = CombatPerformanceReport.Read(folder);
                if (run.status == "RUNNING") { run.status = "INTERRUPTED"; run.reason = "러너 종료 기록 없이 EditMode로 돌아왔습니다."; }
                if (run.contentFingerprint != ContentFingerprint(run.profile)) { run.status = "SOURCE_CHANGED"; run.reason += " 실행 중 코드/자산이 변경됐습니다."; }
                CombatPerformancePaths.SaveJson(runPath, run); CombatPerformanceReport.Write(run);
            }
        }
        catch (Exception error) { returned.status = "FAIL"; returned.reason = error.ToString(); }
        CombatPerformancePaths.SaveJson(Path.Combine(folder, "editor-return.json"), returned);
        string baseline = SessionState.GetString(Key + "baseline", "");
        if (returned.status == "PASS" && !string.IsNullOrEmpty(baseline) && File.Exists(Path.Combine(folder, "run.json")))
            try { CombatPerformanceReport.SaveComparison(baseline, folder); }
            catch (Exception error) { File.WriteAllText(Path.Combine(folder, "comparison-error.txt"), error.ToString()); }
        if (returned.status == "PASS")
        { foreach (string suffix in new[] { "folder", "phase", "deadline", "fingerprint", "baseline" }) SessionState.EraseString(Key + suffix); }
        else SessionState.SetString(Key + "phase", "deferred");
    }
    [Serializable] sealed class SceneContainer { public SceneState[] scenes; }
    public static void SetBaseline(string folder)
    {
        var run = CombatPerformanceReport.Read(folder);
        if (run.status != "COMPLETE" || run.logErrors != 0 || run.profile.mode != CombatPerformanceMode.Automated) throw new InvalidOperationException("오류 없는 자동 실행 완료 회차를 기준으로 선택하세요.");
        if (run.origin == "Editor" && (!File.Exists(Path.Combine(folder, "editor-return.json")) || JsonUtility.FromJson<Returned>(File.ReadAllText(Path.Combine(folder, "editor-return.json")))?.status != "PASS"))
            throw new InvalidOperationException("Editor 반환 PASS가 필요합니다.");
        EditorPrefs.SetString(Key + "baseline", folder);
    }
    public static string Baseline => EditorPrefs.GetString(Key + "baseline", "");
    public static void ClearBaseline() => EditorPrefs.DeleteKey(Key + "baseline");
}
