#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.DebugTools;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>시각 재생만 소유하는 테스트 계정 세션. 재로딩·중단 뒤 일반 Play 반환을 끝까지 처리한다.</summary>
[InitializeOnLoad]
public sealed class VisualPlayEditorSession : IVisualPlayBackend
{
    const string Key = "Overburst.VisualPlay.Session.";
    const string BootScene = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    sealed class Plan
    {
        public string directory, phase, previousStartScene, token, categoryId, caseId;
        public bool background, fullContent, waitAfter, toolVerification;
        public float observeSeconds;
        public double deadline;
        public List<VisualPlayEntry> entries;
    }
    static readonly VisualPlayEditorSession instance;
    Plan plan;
    VisualPlayRunner runner;
    List<VisualPlayRecord> results = new List<VisualPlayRecord>();
    readonly Dictionary<string, int> previewCounts = new Dictionary<string, int>();
    public VisualPlayState State { get; private set; } = new VisualPlayState();
    public static VisualPlayEditorSession Instance => instance;
    public IReadOnlyList<VisualPlayRecord> Records => runner?.Records ?? results;
    public static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Tests/VisualPlay"));

    static VisualPlayEditorSession()
    {
        instance = new VisualPlayEditorSession();
        instance.plan = Read<Plan>("plan");
        if (instance.plan == null) instance.plan = Read<Plan>("deferredPlan");
        instance.State = Read<VisualPlayState>("lastState") ?? new VisualPlayState();
        instance.results = Read<List<VisualPlayRecord>>("lastResults") ?? new List<VisualPlayRecord>();
        if (instance.plan != null)
        {
            instance.State.Running = false;
            VisualPlayBridge.Category = Array.Find(VisualPlayCatalog.Categories, category => category.Id == instance.plan.categoryId);
            VisualPlayBridge.Selected = VisualPlayCatalog.Find(instance.plan.caseId);
            VisualPlayBridge.ObserveSeconds = instance.plan.observeSeconds;
            VisualPlayBridge.WaitAfterScenario = instance.plan.waitAfter;
            VisualPlayBridge.FullContent = instance.plan.fullContent;
            instance.State.Planned = instance.plan.entries.Count;
            instance.State.Phase = "테스트 계정 준비";
        }
        VisualPlayBridge.Backend = instance;
        EditorApplication.update += instance.Tick;
        EditorApplication.projectChanged += instance.previewCounts.Clear;
        EditorApplication.playModeStateChanged += instance.StateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += instance.BeforeReload;
        if (instance.plan != null) instance.State.Running = true;
    }

    static T Read<T>(string name) where T : class
    {
        try { string json = SessionState.GetString(Key + name, ""); return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<T>(json); }
        catch { return null; }
    }
    void SavePlan() => SessionState.SetString(Key + "plan", JsonConvert.SerializeObject(plan));
    void SaveResults()
    {
        SessionState.SetString(Key + "lastState", JsonConvert.SerializeObject(State));
        SessionState.SetString(Key + "lastResults", JsonConvert.SerializeObject(results));
        string output = plan?.directory ?? SessionState.GetString(Key + "lastDirectory", "");
        if (!string.IsNullOrEmpty(output))
            File.WriteAllText(Path.Combine(output, "Result.json"), JsonConvert.SerializeObject(new { state = State, records = results, automaticRegression = "NOT_RUN", developerAcceptance = plan?.toolVerification == true ? "NOT_RUN_TOOL_PROBE" : "USER_MARKS_ONLY" }, Formatting.Indented));
    }

    public string Availability(string caseId) => !Application.isEditor ? "Unity Editor 전용이에요"
        : VisualPlayScenarios.IsSupported(caseId) ? "자동 재생 연결 · 준비 중 장면 지원 여부를 확인해요" : VisualPlayScenarios.UnavailableReason(caseId);

    public string PlanSummary(string categoryId, string caseId, bool fullContent)
    {
        int Count(VisualPlayCase definition)
        {
            string key = definition.Id + "|" + fullContent;
            if (!previewCounts.TryGetValue(key, out int count)) previewCounts[key] = count = VisualPlayScenarios.Expand(definition, fullContent).Count();
            return count;
        }
        try
        {
            var category = VisualPlayCatalog.Categories.First(value => value.Id == categoryId);
            return $"{VisualPlayCatalog.Categories.Length}개 카테고리 · {VisualPlayCatalog.All.Length}개 장면 정의\n재생 예정: 전체 {VisualPlayCatalog.All.Sum(Count)} · 카테고리 {category.Cases.Sum(Count)} · 이 항목 {Count(VisualPlayCatalog.Find(caseId))}";
        }
        catch { return "콘텐츠 목록을 준비한 뒤 재생 예정 수를 표시해요"; }
    }

    public DebugResult Start(VisualPlayScope scope, string categoryId, string caseId)
    {
        if (plan != null || State.Running || Read<Plan>("deferredPlan") != null) return DebugResult.Fail("진행 중인 재생과 계정 반환을 먼저 마쳐 주세요");
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPaused)
            return DebugResult.Fail("컴파일·임포트·Editor 정지가 끝난 뒤 시작해 주세요");
        if (!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            return DebugResult.Fail("다른 테스트 계정 세션이 사용 중이에요");
        IEnumerable<VisualPlayCase> definitions = VisualPlayCatalog.All;
        List<VisualPlayEntry> entries;
        if (scope == VisualPlayScope.Problems)
            entries = results.GroupBy(record => record.caseId + "|" + record.variant).Select(group => group.Last())
                .Where(record => record.review == VisualPlayReview.Problem)
                .Select(record => new VisualPlayEntry { caseId = record.caseId, variant = record.variant, label = record.label }).ToList();
        else
        {
            if (scope == VisualPlayScope.Category) definitions = definitions.Where(definition => VisualPlayCatalog.CategoryOf(definition.Id)?.Id == categoryId);
            if (scope == VisualPlayScope.Individual) definitions = definitions.Where(definition => definition.Id == caseId);
            entries = definitions.SelectMany(definition => VisualPlayScenarios.Expand(definition, VisualPlayBridge.FullContent)).ToList();
        }
        if (entries.Count == 0) return DebugResult.Fail("재생할 항목이 없어요");
        string directory = Path.Combine(OutputRoot, "Run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(directory);
        plan = new Plan { directory = directory, phase = "awaitingEdit", token = Guid.NewGuid().ToString("N"),
            previousStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), background = Application.runInBackground,
            fullContent = VisualPlayBridge.FullContent, waitAfter = VisualPlayBridge.WaitAfterScenario, observeSeconds = VisualPlayBridge.ObserveSeconds,
            categoryId = categoryId, caseId = caseId, entries = entries, deadline = EditorApplication.timeSinceStartup + 180 };
        State = new VisualPlayState { Running = true, Planned = entries.Count, Phase = "테스트 계정 준비", Current = "선택한 장면을 준비하고 있어요" };
        results.Clear(); SavePlan();
        SessionState.SetString(Key + "lastState", JsonConvert.SerializeObject(State));
        SessionState.SetString(Key + "lastResults", JsonConvert.SerializeObject(results));
        SessionState.SetString(Key + "lastDirectory", directory);
        File.WriteAllText(Path.Combine(directory, "Plan.json"), JsonConvert.SerializeObject(new { scope, categoryId, caseId, entries, fullContent = plan.fullContent }, Formatting.Indented));
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        return DebugResult.Ok("테스트 계정 Play에서 시각 재생을 시작해요");
    }

    public DebugResult Control(VisualPlayControl control)
    {
        if (runner != null) return runner.Control(control);
        if (plan != null && control == VisualPlayControl.Stop) { BeginReturn("중단"); return DebugResult.Ok("준비를 취소하고 일반 Play 상태로 반환해요"); }
        return DebugResult.Fail("현재 재생 중인 장면이 없어요");
    }

    // 도구 자체 검증은 같은 준비·재생·반환 경로에서 대표 장면만 골라 실행한다.
    // 디버그 창의 전체/카테고리/개별 범위와 사용자 확인 목록에는 별도 테스트를 추가하지 않는다.
    internal DebugResult StartVerification(IReadOnlyList<string> ids)
    {
        if (ids == null || ids.Count == 0 || ids.Any(id => VisualPlayCatalog.Find(id) == null))
            return DebugResult.Fail("등록된 검증 항목을 선택하세요");
        VisualPlayBridge.FullContent = false;
        VisualPlayBridge.ObserveSeconds = 1f;
        VisualPlayBridge.WaitAfterScenario = true;
        var result = Start(VisualPlayScope.All, "C01", ids[0]);
        if (!result.Success) return result;
        var all = plan.entries;
        plan.entries = ids.Select(id => all.First(entry => entry.caseId == id)).ToList();
        plan.toolVerification = true;
        State.Planned = plan.entries.Count;
        SessionState.SetString(Key + "lastState", JsonConvert.SerializeObject(State));
        SavePlan();
        File.WriteAllText(Path.Combine(plan.directory, "Plan.json"), JsonConvert.SerializeObject(new { scope = "TOOL_VERIFICATION", entries = plan.entries, fullContent = false }, Formatting.Indented));
        return result;
    }

    public DebugResult Review(VisualPlayReview review, string note)
    {
        if (runner != null) return runner.Review(review, note);
        VisualPlayRecord last = results.LastOrDefault();
        if (last == null) return DebugResult.Fail("확인할 재생 기록이 없어요");
        last.review = review; last.note = note;
        var latest = results.GroupBy(record => record.planIndex).Select(group => group.Last()).ToArray();
        State.Checked = latest.Count(record => record.review == VisualPlayReview.Checked);
        State.Problems = latest.Count(record => record.review == VisualPlayReview.Problem);
        State.Summary = $"재생 {State.Completed}/{State.Planned} · 실패 {State.Failed} · 미실행 {State.Skipped}\n화면 확인 {State.Checked} · 문제 표시 {State.Problems}";
        SaveResults(); return DebugResult.Ok("개발자 확인을 기록했어요");
    }

    void Tick()
    {
        VisualPlayBridge.Backend = this;
        if (plan == null)
        {
            var deferred = Read<Plan>("deferredPlan");
            if (deferred == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) return;
            plan = deferred; plan.phase = "returning"; plan.deadline = EditorApplication.timeSinceStartup + 120;
            State.Running = true; SavePlan();
        }
        try
        {
            if (plan.phase == "awaitingEdit")
            {
                if (EditorApplication.timeSinceStartup > plan.deadline) { BeginReturn("준비 시간 초과"); return; }
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))) { BeginReturn("다른 계정 준비 중"); return; }
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScene);
                if (scene == null) throw new InvalidOperationException("시작 씬을 찾지 못했어요");
                EditorSceneManager.playModeStartScene = scene;
                Application.runInBackground = true;
                plan.phase = "booting"; plan.deadline = EditorApplication.timeSinceStartup + 180; SavePlan();
                IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(plan.directory, "Account"));
                return;
            }
            if (plan.phase == "booting")
            {
                if (EditorApplication.timeSinceStartup > plan.deadline) { BeginReturn("부팅 시간 초과"); return; }
                if (!EditorApplication.isPlaying || !AccountBootstrap.Ready || PlayerContext.Instance?.CurrentActor == null) return;
                if (!OwnsCurrentPlay()) { BeginReturn("테스트 계정이 일치하지 않아요"); return; }
                if (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) return;
                VisualPlayBridge.FullContent = plan.fullContent;
                DebugTime.SetSpeed(1f); DebugHub.Close();
                plan.phase = "running"; SavePlan();
                runner = new VisualPlayRunner(plan.entries, State, records => { results = records.ToList(); BeginReturn(State.Phase); });
                return;
            }
            if (plan.phase == "running")
            {
                if (runner == null) { BeginReturn("재로딩으로 재생 중단"); return; }
                if (!OwnsCurrentPlay()) { BeginReturn("Play 세션 변경으로 중단"); return; }
                EditorApplication.QueuePlayerLoopUpdate(); runner.Tick(); return;
            }
            if (plan.phase == "returning") ReturnToNormal();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[시각 확인] " + exception.Message);
            BeginReturn("실행 오류: " + exception.Message);
        }
    }

    bool OwnsCurrentPlay()
    {
        if (plan == null || !EditorApplication.isPlaying) return false;
        string expected = Path.GetFullPath(Path.Combine(plan.directory, "Account"));
        return SameDirectory(IsolatedSavePlayGuard.ActiveDirectory, expected) && SameDirectory(AccountBootstrap.SaveDirectory, expected);
    }
    static bool SameDirectory(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
    }

    void BeginReturn(string reason)
    {
        if (plan == null) return;
        if (runner != null)
        {
            results = runner.Records.ToList();
            try { runner.Dispose(); } catch (Exception error) { Debug.LogWarning("[시각 확인] 정리 오류: " + error.Message); }
            finally { runner = null; }
        }
        State.Phase = reason; State.Detail = "일반 Play 상태로 반환하고 있어요";
        plan.phase = "returning"; plan.deadline = EditorApplication.timeSinceStartup + 120;
        SaveResults(); SavePlan();
        if (OwnsCurrentPlay() || (EditorApplication.isPlaying && SameDirectory(IsolatedSavePlayGuard.ActiveDirectory, Path.Combine(plan.directory, "Account")))) EditorApplication.ExitPlaymode();
    }

    void ReturnToNormal()
    {
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        string ours = Path.Combine(plan.directory, "Account");
        bool occupied = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || (!string.IsNullOrEmpty(current) && !SameDirectory(current, ours)) || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory);
        if (occupied)
        {
            State.Detail = "다른 테스트 준비가 끝난 뒤 반환해요";
            if (EditorApplication.timeSinceStartup > plan.deadline)
            {
                State.Running = false; State.Detail = "다른 테스트 계정 준비 때문에 반환을 보류했어요";
                SaveResults(); SessionState.SetString(Key + "deferredPlan", JsonConvert.SerializeObject(plan));
                SessionState.EraseString(Key + "plan"); plan = null;
            }
            return;
        }
        IsolatedSavePlayGuard.UseRealAccount();
        if (AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == BootScene)
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(plan.previousStartScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStartScene);
        Application.runInBackground = plan.background;
        State.Running = false; State.Waiting = State.Paused = false; State.Detail = "일반 Play로 돌아갈 준비가 됐어요";
        SaveResults(); SessionState.EraseString(Key + "plan"); SessionState.EraseString(Key + "deferredPlan"); plan = null;
    }

    void StateChanged(PlayModeStateChange state)
    {
        if (plan == null) return;
        if (state == PlayModeStateChange.ExitingPlayMode && plan.phase == "running") BeginReturn("Play 종료로 중단");
        if (state == PlayModeStateChange.EnteredEditMode && plan.phase == "booting") BeginReturn("부팅이 취소됐어요");
        // ReturnToNormal runs on a later Editor update, after every EnteredEditMode handler.
    }

    void BeforeReload()
    {
        if (plan == null || plan.phase != "running") return;
        BeginReturn("스크립트 재로딩으로 중단");
    }
}
#endif
