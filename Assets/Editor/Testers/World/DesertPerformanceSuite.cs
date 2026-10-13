using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.DebugTools.Performance;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class DesertPerformanceSuite
{
    const string Key = "Overburst.DesertPerformanceSuite.";
    sealed class Plan
    {
        public string output, active, status = "RUNNING", reason, accountHash, inputHash, sourceHash;
        public string[] ids, sceneHashes;
        public int index, pid;
        public bool cancelled;
        public double activeAt;
        public List<object> attempts = new List<object>();
    }
    static Plan plan;
    static DesertPerformanceSuite()
    {
        string path = SessionState.GetString(Key + "plan", "");
        if (File.Exists(path))
        {
            plan = JsonConvert.DeserializeObject<Plan>(File.ReadAllText(path));
            // The entry verifier owns reload cancellation. Never automatically restart interrupted Play.
            if (plan?.status != "RUNNING") plan = null;
        }
        EditorApplication.update += Tick;
    }
    public static bool Busy => plan != null;
    public static string LastFolder => EditorPrefs.GetString(Key + "last", "");
    public static string Status => plan == null ? "대기" : plan.status + " · " + plan.index + "/" + plan.ids.Length
        + " · " + (plan.index < plan.ids.Length ? plan.ids[plan.index] : "반환 확인");
    static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Performance/DesertTraversal"));
    static bool Idle => !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode
        && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer
        && !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))
        && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
        && string.IsNullOrEmpty(SessionState.GetString("Overburst.MapRunEntryPlayVerifier.return", ""));
    static void Save()
    {
        string path = Path.Combine(plan.output, "batch.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(plan, Formatting.Indented), new UTF8Encoding(false));
        SessionState.SetString(Key + "plan", path);
    }
    static string HashFiles(string directory, string pattern = "*")
    {
        if (!Directory.Exists(directory)) return "ABSENT";
        var b = new StringBuilder();
        using (var sha = SHA256.Create())
            foreach (string file in Directory.GetFiles(directory, pattern, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                b.Append(file.Substring(directory.Length)).Append(':').Append(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))));
        return CombatPerformancePaths.Hash(b.ToString());
    }
    static string RealAccountHash() => HashFiles(Path.Combine(Application.persistentDataPath, "Account"));
    static string InputHash() => CombatPerformancePaths.Hash(File.ReadAllText(Path.Combine(Application.dataPath, "ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions")));
    static string SourceHash() => HashFiles(Path.Combine(Application.dataPath, "ProjectOverburst"), "*.cs")
        + HashFiles(Path.Combine(Application.dataPath, "Editor/Testers/World"), "DesertPerformance*.cs");

    public static string Start(DesertPerformanceProfile profile)
    {
        if (Busy || !Idle || CombatPerformanceSession.Busy) throw new InvalidOperationException("기존 실행과 계정 반환을 마친 유휴 EditMode에서 시작하세요.");
        RequireCurrentCompilation();
        profile.Validate();
        MapTerrainCatalog.InvalidateCandidates();
        string[] ids = profile.candidateIds.Length == 0 ? Representatives() : profile.candidateIds.ToArray();
        if (ids.Select(id => MapTerrainCatalog.Resolve(id).areaCount).Distinct().Count() != ids.Length)
            throw new ArgumentException("규모별 한 맵만 선택하세요.");
        foreach (string id in ids)
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(MapTerrainCatalog.Resolve(id).scene)) throw new InvalidOperationException("검사 씬 누락: " + id);
        string output = CombatPerformancePaths.RequireOutput(Path.Combine(OutputRoot, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        Directory.CreateDirectory(output);
        DesertPerformanceDetails.ArchiveReference(output);
        File.WriteAllText(Path.Combine(output, "profile.json"), JsonConvert.SerializeObject(profile, Formatting.Indented));
        plan = new Plan { output = output, ids = ids, active = "", pid = System.Diagnostics.Process.GetCurrentProcess().Id,
            accountHash = RealAccountHash(), inputHash = InputHash(), sourceHash = SourceHash(),
            sceneHashes = ids.Select(id => AssetDatabase.GetAssetDependencyHash(MapTerrainCatalog.Resolve(id).scene).ToString()).ToArray() };
        EditorPrefs.SetString(Key + "last", output); Save(); return output;
    }
    public static void Stop()
    {
        if (plan == null) return;
        if (plan.status == "RETURN_PENDING") { plan.status = "RUNNING"; plan.activeAt = EditorApplication.timeSinceStartup; }
        plan.cancelled = true; Save();
        if (!string.IsNullOrEmpty(plan.active) && SessionState.GetString("Overburst.MapRunEntryPlayVerifier.return", "") == plan.active)
            MapRunEntryPlayVerifier.StopDesertPerformance(plan.active);
    }
    public static void Resume()
    {
        if (Busy || !Idle) throw new InvalidOperationException("유휴 EditMode에서 재개하세요.");
        RequireCurrentCompilation();
        string path = Path.Combine(LastFolder, "batch.json");
        var previous = JsonConvert.DeserializeObject<Plan>(File.ReadAllText(path));
        if (previous == null || previous.index >= previous.ids.Length || previous.pid != System.Diagnostics.Process.GetCurrentProcess().Id)
            throw new InvalidOperationException("같은 Editor의 미완료 배치만 재개할 수 있습니다. 새 세션은 새 검사를 시작하세요.");
        if (previous.accountHash != RealAccountHash() || previous.inputHash != InputHash() || previous.sourceHash != SourceHash())
            throw new InvalidOperationException("계정/입력/소스가 변경됐습니다. 조건이 다른 새 검사를 시작하세요.");
        plan = previous; plan.status = "RUNNING"; plan.reason = ""; plan.cancelled = false; plan.active = ""; Save();
    }
    static void Finish(string status, string reason)
    {
        plan.status = status; plan.reason = reason;
        try { Save(); WriteBatchSummary(plan); }
        finally { plan = null; SessionState.EraseString(Key + "plan"); }
    }
    static void Tick()
    {
        if (plan == null || plan.status != "RUNNING") return;
        try
        {
            if (plan.pid != System.Diagnostics.Process.GetCurrentProcess().Id) { Finish("INTERRUPTED", "Editor PID 변경"); return; }
            if (!string.IsNullOrEmpty(plan.active))
            {
                string returned = Path.Combine(plan.active, "return-result.json");
                if (!File.Exists(returned) || !Idle)
                {
                    if (EditorApplication.timeSinceStartup - plan.activeAt > 7440)
                    {
                        // Request exit only through the exact owned verifier; record failure after EditMode return.
                        if (SessionState.GetString("Overburst.MapRunEntryPlayVerifier.return", "") == plan.active)
                            MapRunEntryPlayVerifier.StopDesertPerformance(plan.active);
                        plan.status = "RETURN_PENDING"; plan.reason = "Play/Editor 반환 제한시간. 다음 맵은 시작하지 않습니다."; Save();
                    }
                    return;
                }
                var returnData = JObject.Parse(File.ReadAllText(returned));
                var play = JObject.Parse(File.ReadAllText(Path.Combine(plan.active, "play-result.json")));
                string mapPath = Path.Combine(plan.active, "map.json");
                var map = File.Exists(mapPath) ? JsonConvert.DeserializeObject<DesertPerformanceProbe.Result>(File.ReadAllText(mapPath)) : null;
                bool account = plan.accountHash == RealAccountHash(), input = plan.inputHash == InputHash();
                bool source = plan.sourceHash == SourceHash() && plan.sceneHashes[plan.index] == AssetDatabase.GetAssetDependencyHash(MapTerrainCatalog.Resolve(plan.ids[plan.index]).scene).ToString();
                bool guard = string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""));
                string probePath = Path.Combine(plan.active, "probe-return.json");
                var probeReturn = File.Exists(probePath) ? JObject.Parse(File.ReadAllText(probePath)) : null;
                bool ok = (string)returnData["status"] == "PASS" && (string)play["status"] == "PASS" && (string)probeReturn?["status"] == "PASS" && map != null
                    && (map.status == "COMPLETE" || map.status == "COMPLETE_LIMITED") && account && input && source && guard;
                var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh(); long before = process.PrivateMemorySize64;
                int desired = AssetDatabase.DesiredWorkerCount, standby = EditorUserSettings.standbyImportWorkerCount;
                try { EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect(); AssetDatabase.DesiredWorkerCount = 0; EditorUserSettings.standbyImportWorkerCount = 0; AssetDatabase.ForceToDesiredWorkerCount(); }
                finally { AssetDatabase.DesiredWorkerCount = desired; EditorUserSettings.standbyImportWorkerCount = standby; }
                process.Refresh();
                var evidence = new { status = ok ? "PASS" : "FAIL", account, input, source, guard, stopped = !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode,
                    desired, standby, workersRestored = desired == AssetDatabase.DesiredWorkerCount && standby == EditorUserSettings.standbyImportWorkerCount,
                    privateBytesBefore = before, privateBytesAfter = process.PrivateMemorySize64, output = plan.active };
                File.WriteAllText(Path.Combine(plan.active, "performance-return.json"), JsonConvert.SerializeObject(evidence, Formatting.Indented));
                plan.attempts.Add(new { id = plan.ids[plan.index], output = plan.active, ok, mapStatus = map?.status }); Save();
                if (plan.cancelled) { Finish("CANCELLED", "사용자 중단; 기존 결과 보존"); return; }
                if (!ok) { Finish("FAILED", "입장/이동/기록/반환 실패: " + plan.active); return; }
                plan.index++; plan.active = ""; Save();
            }
            if (plan.cancelled) { Finish("CANCELLED", "맵 시작 전 사용자 중단"); return; }
            if (plan.index == plan.ids.Length)
            {
                var profile = DesertPerformanceProfile.Read(Path.Combine(plan.output, "profile.json"));
                Finish(profile.maximumAreas > 0 || !FullRepresentativeCoverage(plan.ids) ? "COMPLETE_LIMITED" : "COMPLETE", "규모별 대표 맵 방문/기록 완료; 성능 합격은 별도 판정"); return;
            }
            if (!Idle) return; // An existing unrelated Play is never stopped or switched.
            if (plan.accountHash != RealAccountHash() || plan.inputHash != InputHash() || plan.sourceHash != SourceHash())
            { Finish("CONDITIONS_CHANGED", "실행 사이 계정/입력/소스 변경"); return; }
            var candidate = MapTerrainCatalog.Resolve(plan.ids[plan.index]);
            int index = MapTerrainCatalog.DesertCandidates.ToList().FindIndex(c => c.id == candidate.id);
            plan.active = Path.Combine(plan.output, plan.index.ToString("D2") + "_" + candidate.id + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            plan.activeAt = EditorApplication.timeSinceStartup; Save();
            MapRunEntryPlayVerifier.Start(plan.active, desertOnly: true, desertCandidateIndex: index, desertPerformanceProfile: Path.Combine(plan.output, "profile.json"));
        }
        catch (Exception error)
        {
            // If Play exists, the entry verifier retains its teardown and account return responsibility.
            if (!string.IsNullOrEmpty(plan.active) && SessionState.GetString("Overburst.MapRunEntryPlayVerifier.return", "") == plan.active)
            {
                plan.cancelled = true; plan.reason = error.ToString(); Save();
                MapRunEntryPlayVerifier.StopDesertPerformance(plan.active);
            }
            else Finish("FAILED", error.ToString());
        }
    }
    static void WriteBatchSummary(Plan value)
    {
        var b = new StringBuilder("# 사막 규모별 대표 맵 순회 검사\n\n");
        b.AppendLine(value.status + " · 완료 " + value.index + "/" + value.ids.Length + " 맵\n\n" + value.reason);
        b.AppendLine("\n| 맵 | 상태 | 방문/예정 플랫폼 | 입장 초 | 가장 느린 구간 P95 ms |\n|---|---|---:|---:|---:|");
        foreach (string id in value.ids)
        {
            var files = Directory.GetDirectories(value.output, "*_" + id + "_*").Select(d => Path.Combine(d, "map.json")).Where(File.Exists).OrderBy(f => f).ToArray();
            var maps = files.Select(f => JsonConvert.DeserializeObject<DesertPerformanceProbe.Result>(File.ReadAllText(f))).ToArray();
            var map = maps.LastOrDefault(m => m.status == "COMPLETE" || m.status == "COMPLETE_LIMITED") ?? maps.LastOrDefault();
            b.AppendLine("| " + id + " | " + (map?.status ?? "NOT_RUN") + " | " + (map == null ? "-" : map.visitedAreas + "/" + map.expectedAreas)
                + " | " + (map == null ? "-" : map.entrySeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                + " | " + (map == null ? "-" : map.segments.Select(s => s.performance.wall?.p95 ?? 0).DefaultIfEmpty().Max().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)) + " |");
        }
        b.AppendLine("\n각 시도의 map.json·summary.md·Segments·performance-return.json을 함께 확인한다. NOT_RUN/실패/제한 결과를 전체 통과로 합산하지 않는다. Player 빌드와 성능 합격은 이 검사에서 실행하지 않는다.");
        File.WriteAllText(Path.Combine(value.output, "summary.md"), b.ToString(), new UTF8Encoding(false));
    }
    public static string[] Representatives() => MapTerrainCatalog.DesertCandidates.GroupBy(c => c.areaCount).OrderBy(g => g.Key)
        .Select(g => g.OrderBy(c => c.id.StartsWith("SQ", StringComparison.Ordinal) ? 0 : 1).ThenBy(c => c.id, StringComparer.Ordinal).First().id).ToArray();
    public static bool FullRepresentativeCoverage(IEnumerable<string> ids)
        => ids.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(Representatives().OrderBy(id => id, StringComparer.Ordinal));
    static void RequireCurrentCompilation()
    {
        string directory = Path.Combine(Application.dataPath, "Editor/Testers/World");
        DateTime compiled = File.GetLastWriteTimeUtc(typeof(DesertPerformanceSuite).Assembly.Location);
        if (Directory.GetFiles(directory, "DesertPerformance*.cs").Concat(new[] { Path.Combine(directory, "MapRunEntryPlayVerifier.cs") })
            .Any(file => File.GetLastWriteTimeUtc(file) > compiled))
            throw new InvalidOperationException("수정한 검사 코드의 컴파일이 아직 완료되지 않았습니다. 컴파일과 도메인 재로딩 후 다시 시작하세요.");
        DateTime runtimeCompiled = File.GetLastWriteTimeUtc(typeof(MapSpawnBudget).Assembly.Location);
        foreach (string file in new[] { "ProjectOverburst/03_Features/MapDungeon/Runtime/MapSpawnBudget.Finite.cs",
            "ProjectOverburst/03_Features/Enemies/Runtime/AI/EnemySquadPursuitRuntimeService.cs",
            "ProjectOverburst/02_Shared/Debug/Runtime/Tools/CombatPerformance/CombatPerformanceRecorder.cs" })
            if (File.GetLastWriteTimeUtc(Path.Combine(Application.dataPath, file)) > runtimeCompiled)
                throw new InvalidOperationException("상세 계측 마커의 런타임 컴파일이 아직 완료되지 않았습니다.");
    }
}

public sealed class DesertPerformanceWindow : EditorWindow
{
    DesertPerformanceProfile profile = new DesertPerformanceProfile();
    string candidate = "", error;
    [MenuItem("OVERBURST/검증/사막 규모별 이동 성능 검사")]
    public static void Open() => GetWindow<DesertPerformanceWindow>("사막 성능 검사");
    void OnInspectorUpdate() { if (DesertPerformanceSuite.Busy) Repaint(); }
    void OnGUI()
    {
        EditorGUILayout.HelpBox("규모별 한 맵(4/6/9/12/15), 총 5개 대표 맵의 전투영역 중심과 내부 지점을 실제 입력으로 방문합니다. 플레이어는 몬스터 몸을 통과하며, 맵별 Play 종료/설정·계정 반환 후 다음 맵으로 이동합니다.", MessageType.Info);
        EditorGUILayout.LabelField(DesertPerformanceSuite.Status);
        using (new EditorGUI.DisabledScope(DesertPerformanceSuite.Busy))
        {
            candidate = EditorGUILayout.TextField("맵 ID (빈 값=규모별 1개)", candidate);
            profile.theme = EditorGUILayout.TextField("몬스터 테마", profile.theme);
            profile.level = EditorGUILayout.IntField("지도 레벨", profile.level);
            profile.repetitions = EditorGUILayout.IntField("플랫폼 순회 횟수", profile.repetitions);
            profile.pointsPerArea = EditorGUILayout.IntSlider("플랫폼당 지점", profile.pointsPerArea, 1, 9);
            profile.dwellSeconds = EditorGUILayout.FloatField("지점별 관찰 초", profile.dwellSeconds);
            profile.targetFps = EditorGUILayout.IntField("분석 목표 FPS", profile.targetFps);
            profile.uncapped = EditorGUILayout.Toggle("VSync/FPS 제한 해제", profile.uncapped);
            profile.screenshots = EditorGUILayout.Toggle("측정 밖 플랫폼 캡처", profile.screenshots);
            if (GUILayout.Button("선택 범위 전체 순회 시작")) Execute(() => { profile.maximumAreas = 0; profile.candidateIds = string.IsNullOrWhiteSpace(candidate) ? Array.Empty<string>() : new[] { candidate.Trim() }; DesertPerformanceSuite.Start(profile); });
            if (GUILayout.Button("도구 확인: 첫 맵/플랫폼 1개/지점 2개")) Execute(() => DesertPerformanceSuite.Start(new DesertPerformanceProfile {
                candidateIds = new[] { string.IsNullOrWhiteSpace(candidate) ? MapTerrainCatalog.DesertCandidates[0].id : candidate.Trim() },
                repetitions = 1, pointsPerArea = 2, maximumAreas = 1, warmupSeconds = 1, dwellSeconds = 2 }));
            if (GUILayout.Button("미완료 맵부터 재개 (기존 결과 보존)")) Execute(DesertPerformanceSuite.Resume);
        }
        using (new EditorGUI.DisabledScope(!DesertPerformanceSuite.Busy))
            if (GUILayout.Button("중단하고 Editor 반환")) Execute(DesertPerformanceSuite.Stop);
        if (!string.IsNullOrEmpty(DesertPerformanceSuite.LastFolder) && GUILayout.Button("최근 결과 폴더 열기")) EditorUtility.RevealInFinder(DesertPerformanceSuite.LastFolder);
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
    }
    void Execute(Action action) { try { action(); error = null; } catch (Exception e) { error = e.Message; } }
}
