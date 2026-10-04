using System;
using System.Collections.Generic;
using System.IO;
using Overburst.DebugTools.Performance;
using UnityEditor;
using UnityEngine;

public sealed class CombatPerformanceWindow : EditorWindow
{
    const string Preference = "Overburst.CombatPerformance.Profile";
    CombatPerformanceProfile profile;
    CombatPerformanceRun selected;
    CombatPerformanceComparison comparison;
    string selectedFolder, message = "", themes, counts;
    Vector2 scroll, resultScroll;
    readonly List<string> history = new List<string>();
    [MenuItem("OVERBURST/테스트/전투 성능")]
    public static void Open() => GetWindow<CombatPerformanceWindow>("전투 성능");
    void OnEnable()
    {
        try { profile = JsonUtility.FromJson<CombatPerformanceProfile>(EditorPrefs.GetString(Preference, "")); } catch { }
        if (profile == null) profile = new CombatPerformanceProfile();
        TextFields(); RefreshHistory();
    }
    void TextFields() { themes = string.Join(",", profile.themes); counts = string.Join(",", profile.enemyCounts); }
    void Preset(CombatPerformanceProfile value) { profile = value; TextFields(); Persist(); }
    void Persist() => EditorPrefs.SetString(Preference, JsonUtility.ToJson(profile));
    void OnGUI()
    {
        EditorGUILayout.LabelField("전투 성능", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(CombatPerformanceSession.Status);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        using (new EditorGUI.DisabledScope(CombatPerformanceSession.Busy || CombatPerformanceRunner.Current != null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("빠른 검사")) Preset(new CombatPerformanceProfile());
            if (GUILayout.Button("표준 검사")) Preset(CombatPerformanceProfile.Standard());
            if (GUILayout.Button("장시간 검사")) Preset(CombatPerformanceProfile.Soak());
            if (GUILayout.Button("수동 관찰")) Preset(new CombatPerformanceProfile { label = "Observation", mode = CombatPerformanceMode.Observation });
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            profile.label = EditorGUILayout.TextField("프로필 이름", profile.label);
            profile.mode = (CombatPerformanceMode)EditorGUILayout.EnumPopup("실행 방식", profile.mode);
            if (profile.mode == CombatPerformanceMode.Observation)
                profile.observationSeconds = EditorGUILayout.FloatField("관찰 시간(초)", profile.observationSeconds);
            else
            {
                themes = EditorGUILayout.TextField("테마 ID(쉼표 구분)", themes);
                counts = EditorGUILayout.TextField("적 수(쉼표 구분)", counts);
                profile.repetitions = EditorGUILayout.IntSlider("조건별 반복", profile.repetitions, 1, 10);
                profile.seed = EditorGUILayout.IntField("seed", profile.seed);
                profile.spawnPerFrame = EditorGUILayout.IntSlider("프레임당 스폰", profile.spawnPerFrame, 1, 100);
                var current = AssetDatabase.LoadAssetAtPath<WeaponItemData>(profile.weaponAssetPath);
                var weapon = (WeaponItemData)EditorGUILayout.ObjectField("검사 무기", current, typeof(WeaponItemData), false);
                if (weapon != current) profile.weaponAssetPath = weapon == null ? "" : AssetDatabase.GetAssetPath(weapon);
                profile.weaponResourcePath = EditorGUILayout.TextField("Player 무기 Resources 경로", profile.weaponResourcePath);
                EditorGUILayout.LabelField("배치", EditorStyles.boldLabel);
                bool dense = Array.IndexOf(profile.layouts, CombatPerformanceLayout.Dense) >= 0;
                bool spread = Array.IndexOf(profile.layouts, CombatPerformanceLayout.Spread) >= 0;
                dense = EditorGUILayout.Toggle("밀집", dense); spread = EditorGUILayout.Toggle("분산", spread);
                var layouts = new List<CombatPerformanceLayout>(); if (dense) layouts.Add(CombatPerformanceLayout.Dense); if (spread) layouts.Add(CombatPerformanceLayout.Spread); profile.layouts = layouts.ToArray();
                EditorGUILayout.LabelField("원소", EditorStyles.boldLabel);
                var elements = new List<WeaponElement>();
                foreach (var e in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
                    if (EditorGUILayout.Toggle(e.ToString(), Array.IndexOf(profile.elements, e) >= 0)) elements.Add(e);
                profile.elements = elements.ToArray();
                profile.baselineSeconds = EditorGUILayout.FloatField("기본 상태(초)", profile.baselineSeconds);
                profile.approachSeconds = EditorGUILayout.FloatField("AI 접근(초)", profile.approachSeconds);
                profile.warmupSeconds = EditorGUILayout.FloatField("예열(초)", profile.warmupSeconds);
                profile.measureSeconds = EditorGUILayout.FloatField("교전/원소 측정(초)", profile.measureSeconds);
                profile.tailSeconds = EditorGUILayout.FloatField("처치/획득 후 관찰(초)", profile.tailSeconds);
                profile.attackInterval = EditorGUILayout.FloatField("행동 요청 간격(초)", profile.attackInterval);
                profile.soakSeconds = EditorGUILayout.FloatField("장시간 반복 목표(초, 0=1순회)", profile.soakSeconds);
                profile.enemyHp = EditorGUILayout.FloatField("지속 교전용 적 HP", profile.enemyHp);
                profile.preparedElementStress = EditorGUILayout.Toggle("상태/에너지 준비 부하 시험", profile.preparedElementStress);
                profile.deathBurst = EditorGUILayout.Toggle("대량 처치·획득·저장 시험", profile.deathBurst);
                profile.uncapped = EditorGUILayout.Toggle("검사 중 VSync/FPS 제한 해제", profile.uncapped);
            }
            profile.regressionPercent = EditorGUILayout.FloatField("악화 상대 기준(%)", profile.regressionPercent);
            profile.regressionAbsoluteMs = EditorGUILayout.FloatField("악화 절대 기준(ms)", profile.regressionAbsoluteMs);
            profile.frameCapacity = EditorGUILayout.IntField("구간별 프레임 버퍼", profile.frameCapacity);
            profile.eventCapacity = EditorGUILayout.IntField("구간별 이벤트 버퍼", profile.eventCapacity);
            profile.segmentLimit = EditorGUILayout.IntField("최대 기록 구간", profile.segmentLimit);
            if (EditorGUI.EndChangeCheck())
                try { ApplyTextFields(); Persist(); message = ""; }
                catch (Exception error) { message = error.Message; }
            EditorGUILayout.HelpBox(profile.mode == CombatPerformanceMode.Automated
                ? "자동 실행은 새 격리 계정으로 PersistentScene을 시작합니다. 장착·실제 AI·공격·키보드 회피를 실행합니다. 상태/에너지 준비와 대량 사망은 별도 fixture 구간입니다. 정상 종료/중단 후 계정과 시작 씬을 반환합니다."
                : "현재 Play를 관찰합니다. 공격·씬·계정은 바꾸지 않습니다. 새 대상의 적중 이벤트는 다음 청크부터 기록될 수 있습니다. 수동 관찰 결과는 자동 기준 비교에 사용하지 않습니다.", MessageType.Info);
            bool canStart = profile.mode == CombatPerformanceMode.Observation ? EditorApplication.isPlaying : !EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(!canStart || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating))
                if (GUILayout.Button(profile.mode == CombatPerformanceMode.Observation ? "현재 Play 관찰 시작" : "격리 자동 검사 시작", GUILayout.Height(30)))
                    Attempt(() => { ApplyTextFields();
                        Persist(); message = "시작: " + CombatPerformanceSession.Start(profile); });
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("프로필 저장")) Attempt(() => { string path = EditorUtility.SaveFilePanel("프로필 저장", Path.Combine(CombatPerformancePaths.OutputRoot, "Profiles"), profile.label, "json");
                if (!string.IsNullOrEmpty(path)) { ApplyTextFields(); profile.Validate(); CombatPerformancePaths.SaveJson(path, profile); message = "저장: " + path; } });
            if (GUILayout.Button("프로필 불러오기")) Attempt(() => { string path = EditorUtility.OpenFilePanel("프로필", CombatPerformancePaths.OutputRoot, "json"); if (!string.IsNullOrEmpty(path)) {
                var value = JsonUtility.FromJson<CombatPerformanceProfile>(File.ReadAllText(path)); value.Validate(); Preset(value); } });
            EditorGUILayout.EndHorizontal();
        }
        using (new EditorGUI.DisabledScope(!CombatPerformanceSession.Busy && CombatPerformanceRunner.Current == null))
            if (GUILayout.Button("검사 중단·Editor 반환")) CombatPerformanceSession.Stop();
        if (CombatPerformanceSession.Busy && CombatPerformanceRunner.Current == null)
            if (GUILayout.Button("유휴 Editor에서 반환 재확인")) CombatPerformanceSession.RetryReturn();
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
        EditorGUILayout.Space(); EditorGUILayout.LabelField("기준과 결과", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("선택한 기준", string.IsNullOrEmpty(CombatPerformanceSession.Baseline) ? "없음" : Path.GetFileName(CombatPerformanceSession.Baseline));
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("목록 갱신")) RefreshHistory();
        if (GUILayout.Button("최근 결과")) Select(CombatPerformanceSession.LastFolder);
        if (GUILayout.Button("기준 해제")) CombatPerformanceSession.ClearBaseline();
        EditorGUILayout.EndHorizontal();
        foreach (string folder in history) if (GUILayout.Button(Path.GetFileName(folder), EditorStyles.miniButton)) Select(folder);
        if (selected != null)
        {
            EditorGUILayout.LabelField(selected.profile.label + " · " + selected.status + " · " + selected.origin);
            EditorGUILayout.LabelField(selected.cpu + " / " + selected.gpu);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("이 결과를 기준으로")) Attempt(() => { CombatPerformanceSession.SetBaseline(selectedFolder); message = "기준 설정 완료"; });
            if (GUILayout.Button("기준과 비교")) Attempt(() => { comparison = CombatPerformanceReport.SaveComparison(CombatPerformanceSession.Baseline, selectedFolder); message = comparison.status + ": " + comparison.reason; });
            if (GUILayout.Button("결과 폴더")) EditorUtility.RevealInFinder(selectedFolder);
            EditorGUILayout.EndHorizontal();
            resultScroll = EditorGUILayout.BeginScrollView(resultScroll, GUILayout.Height(240));
            EditorGUILayout.LabelField("구간 / P95·P99 ms / 실제 적중·처치 / 상태", EditorStyles.boldLabel);
            foreach (var s in selected.segments) EditorGUILayout.LabelField(s.scenario + " " + s.theme + " " + s.expectedEnemies + " " + s.element,
                (s.wall?.p95 ?? 0).ToString("F2") + " / " + (s.wall?.p99 ?? 0).ToString("F2") + " · " + s.enemyHits + "/" + s.kills + " · " + s.validity);
            EditorGUILayout.EndScrollView();
        }
        if (comparison != null)
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField("기준 비교 결과", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(comparison.status + ": " + comparison.reason, MessageType.Info);
            foreach (var row in comparison.rows)
                EditorGUILayout.LabelField(row.key, row.status + " · " + row.deltaMs.ToString("+0.00;-0.00;0.00") + "ms / " + row.deltaPercent.ToString("+0.0;-0.0;0.0") + "% · " + row.reason);
        }
        EditorGUILayout.EndScrollView();
    }
    void ApplyTextFields()
    {
        if (profile.mode == CombatPerformanceMode.Observation) return;
        var themeIds = Split(themes); var text = Split(counts); var numbers = new int[text.Length];
        for (int i = 0; i < numbers.Length; i++) numbers[i] = int.Parse(text[i]);
        profile.themes = themeIds; profile.enemyCounts = numbers;
    }
    static string[] Split(string value)
    {
        var result = new List<string>();
        foreach (var part in (value ?? "").Split(',')) { var trimmed = part.Trim(); if (trimmed.Length > 0) result.Add(trimmed); }
        return result.ToArray();
    }
    void Attempt(Action action) { try { action(); } catch (Exception error) { message = error.Message; } Repaint(); }
    void Select(string folder) => Attempt(() => { selected = CombatPerformanceReport.Read(folder); selectedFolder = folder; comparison = null;
        string path = Path.Combine(folder, "comparison.json"); if (File.Exists(path)) comparison = JsonUtility.FromJson<CombatPerformanceComparison>(File.ReadAllText(path)); });
    void RefreshHistory()
    {
        history.Clear();
        if (!Directory.Exists(CombatPerformancePaths.OutputRoot)) return;
        var folders = Directory.GetDirectories(CombatPerformancePaths.OutputRoot); Array.Sort(folders, StringComparer.Ordinal); Array.Reverse(folders);
        foreach (var folder in folders) if (history.Count < 12 && File.Exists(Path.Combine(folder, "run.json"))) history.Add(folder);
    }
}
