using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Overburst.EditorBalance.Analysis
{
    // 분석 도구 자체 검증(Editor, Play 없음): 계산 경로 점검·결정성·창 조작·보고서 내보내기.
    public static class CombatBalanceAnalysisVerifier
    {
        public static string LastStatus { get; private set; } = "NOT_RUN";
        public static string LastOutput { get; private set; }

        [MenuItem("OVERBURST/Balance/전투 분석 도구 자체 검증")]
        public static void RunFromMenu() => UnityEngine.Debug.Log(Run());

        [MenuItem("OVERBURST/Balance/전투 분석 기본 보고서 생성")]
        public static void ExportDefaultFromMenu() => UnityEngine.Debug.Log("보고서: " + ExportDefault());

        public static string ExportDefault()
        {
            var result = CombatBalanceAnalysisRunner.Run(new AnalysisConditions());
            return CombatBalanceAnalysisReport.Export(result, CombatBalanceAnalysisReport.LoadLatestMeasurement());
        }

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("자체 검증은 Play가 끝난 뒤 실행해야 합니다.");
            var checks = new List<string>();
            void Check(bool ok, string label, string detail = "") => checks.Add((ok ? "PASS " : "FAIL ") + label + (detail.Length > 0 ? " — " + detail : ""));
            string folder = Path.Combine(CombatBalanceAnalysisReport.OutputRoot, "Verification", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);
            try
            {
                var full = new AnalysisConditions();
                var watch = Stopwatch.StartNew();
                var result = CombatBalanceAnalysisRunner.Run(full);
                watch.Stop();
                int expected = CombatBalanceAnalysisRunner.ExpectedRows(full);
                Check(result.rows.Count == expected, "기본 조건 조합 수", $"{result.rows.Count}/{expected}, {watch.ElapsedMilliseconds}ms");
                Check(result.selfChecks.Count > 0 && result.selfChecks.All(s => s.StartsWith("PASS")), "계산 경로 자체 점검", string.Join(" | ", result.selfChecks.Where(s => !s.StartsWith("PASS"))));
                Check(result.rows.All(r => r.attack > 0f && r.enemyHealth > 0f && r.trace.Count >= 10), "모든 행에 공격·체력·계산 경로");
                Check(result.rows.Where(r => r.mode == CombatMode.Single).All(r => !float.IsNaN(r.chargeTime) || !OverburstElementRules.IsActive(r.element)), "단일 행 충전 시간 계산");
                Check(result.rows.All(r => r.normalSurvivable != 0 || r.strongSurvivable != 0), "생존 피격 수 계산");
                Check(result.findings.Any(f => f.id == "D01") && result.findings.All(f => !string.IsNullOrEmpty(f.condition) && !string.IsNullOrEmpty(f.cause)
                    && !string.IsNullOrEmpty(f.impact) && !string.IsNullOrEmpty(f.proposal)), "발견 4요소(재현 조건·원인·영향·수정 후보)", result.findings.Count + "건");
                var tooltipFinding = result.findings.FirstOrDefault(f => f.id == "D01");
                Check(tooltipFinding != null && tooltipFinding.severity == "정보", "무기 툴팁 DPS = 실제 약공 경로(±1%)", tooltipFinding?.impact);
                // 결정성: 같은 조건 두 번 → 같은 값
                var small = new AnalysisConditions { levelBuckets = new List<int> { 0, 5, 9 }, grades = new List<ItemGrade> { ItemGrade.Common, ItemGrade.Epic }, seedCount = 3 };
                var a = CombatBalanceAnalysisRunner.Run(small); var b = CombatBalanceAnalysisRunner.Run(small);
                bool same = a.rows.Count == b.rows.Count && a.rows.Zip(b.rows, (x, y) => x.key == y.key && Same(x.killTime, y.killTime) && Same(x.chargeTime, y.chargeTime)
                    && Same(x.attack, y.attack) && x.normalSurvivable == y.normalSurvivable).All(v => v);
                Check(same, "같은 시드 두 번 실행 결과 동일", a.rows.Count + "행");
                // 실패 주입(Codex 검토 10-01 P1-1): FAIL 체크·차단·오류·다른 지문이 전체 PASS로 가려지지 않는지.
                foreach (var line in InjectedStatusChecks(folder)) checks.Add(line);
                foreach (var line in IsolationAndFingerprintChecks(folder)) checks.Add(line);
                // 시간순 이벤트 모델(어둠 탄막·불 연쇄)
                foreach (var line in CombatBalanceAnalysisModel.EventModelSelfChecks()) checks.Add(line);
                // 창 조작: 조건 → 실행 → 문제 필터 → 선택·비교 → 내보내기
                var window = CombatBalanceAnalysisWindow.Open();
                try
                {
                    window.SetConditions(small);
                    window.RunAnalysis();
                    Check(window.Result != null && window.Result.rows.Count == a.rows.Count, "창에서 분석 실행", window.Result?.rows.Count + "행");
                    window.SetFilter("", true);
                    int flagged = window.Result.rows.Count(r => r.HasFlags);
                    Check(window.VisibleRowCount == flagged, "문제 조합 필터", $"{window.VisibleRowCount}/{flagged}");
                    window.SetFilter("불", false, "전체", "단일");
                    Check(window.VisibleRowCount == window.Result.rows.Count(r => r.mode == CombatMode.Single && r.Title.Contains("불")), "검색·전투 필터", window.VisibleRowCount.ToString());
                    var first = window.Result.rows.First(); var second = window.Result.rows.Skip(1).First();
                    window.SetBaseline(first); window.Select(second);
                    string exported = window.Export();
                    bool files = exported != null && new[] { "보고서.md", "rows.csv", "analysis.json" }.All(f => File.Exists(Path.Combine(exported, f)) && new FileInfo(Path.Combine(exported, f)).Length > 200);
                    Check(files, "보고서 내보내기(MD·CSV·JSON)", exported ?? "없음");
                    if (files)
                    {
                        string md = File.ReadAllText(Path.Combine(exported, "보고서.md"), Encoding.UTF8);
                        Check(md.Contains("재현 조건") && md.Contains("계산 모델 예상치 vs 실제 Play 측정치"), "보고서에 발견·예상/실측 절");
                        Check(!md.Contains(Directory.GetParent(Application.dataPath).FullName), "보고서에 로컬 절대 경로 없음");
                    }
                }
                finally { window.Close(); }
                // 기본 보고서 1부
                LastOutput = CombatBalanceAnalysisReport.Export(result, CombatBalanceAnalysisReport.LoadLatestMeasurement());
                Check(File.Exists(Path.Combine(LastOutput, "보고서.md")), "기본 조건 보고서", LastOutput);
            }
            catch (Exception e) { checks.Add("FAIL 예외 " + e); }
            LastStatus = checks.All(c => c.StartsWith("PASS")) ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(folder, "tool-tests.json"), CombatBalanceAnalysisReport.ToJson(new { status = LastStatus, checks, report = LastOutput }), new UTF8Encoding(false));
            return LastStatus + " " + folder + "\n" + string.Join("\n", checks);
        }

        static bool Same(float a, float b) => (float.IsNaN(a) && float.IsNaN(b)) || (float.IsInfinity(a) && float.IsInfinity(b)) || Mathf.Abs(a - b) < 1e-4f;

        static MeasurementScenario Scenario(string key, string status, params string[] checks)
        {
            var s = new MeasurementScenario { key = key, status = status };
            s.checks.AddRange(checks);
            return s;
        }

        static void WriteRun(string root, string name, MeasurementReport r, DateTime written)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "measurement.json");
            File.WriteAllText(path, CombatBalanceAnalysisReport.ToJson(r), new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(path, written);
        }

        // 실제 측정 폴더를 건드리지 않고 검증 폴더 안에 가짜 회차를 써서 병합까지 확인한다.
        static List<string> InjectedStatusChecks(string folder)
        {
            var list = new List<string>();
            void Check(bool ok, string label, string detail) => list.Add((ok ? "PASS " : "FAIL ") + label + " — " + detail);
            var keys = new List<string> { "k1", "k2" };
            var t0 = DateTime.UtcNow.AddMinutes(-10);

            var hidden = Scenario("k1", "PASS", "PASS 피해", "FAIL 군집 정리");
            Check(CombatBalanceMeasurementStatus.OfScenario(hidden) == "FAIL", "실패 주입: PASS로 기록됐지만 FAIL 체크가 있는 시나리오", CombatBalanceMeasurementStatus.OfScenario(hidden));
            var run = CombatBalanceMeasurementStatus.OfRun(new[] { hidden, Scenario("k2", null, "PASS 피해") }, new string[0], 2, keys);
            Check(run == "FAIL", "실패 주입: 체크 하나의 FAIL이 회차 FAIL로", run);
            run = CombatBalanceMeasurementStatus.OfRun(new[] { Scenario("k1", null, "PASS 피해"), Scenario("k2", "BLOCKED") }, new string[0], 2, keys);
            Check(run == "BLOCKED", "실패 주입: 차단 시나리오는 PASS가 아님", run);
            run = CombatBalanceMeasurementStatus.OfRun(new[] { Scenario("k1", null, "PASS 피해"), Scenario("k2", null, "PASS 피해") }, new[] { "예외" }, 2, keys);
            Check(run == "FAIL", "실패 주입: 오류가 있으면 FAIL", run);
            run = CombatBalanceMeasurementStatus.OfRun(new[] { Scenario("k1", "FAIL", "FAIL 예외 NullReferenceException") }, new string[0], 2, keys);
            Check(run == "FAIL", "실패 주입: 시나리오 예외", run);

            string root = Path.Combine(folder, "InjectedMeasurements");
            string Merge(string name, params (string run, MeasurementReport report, int minutes)[] runs)
            {
                string r = Path.Combine(root, name);
                foreach (var (dir, report, minutes) in runs) WriteRun(r, dir, report, t0.AddMinutes(minutes));
                return CombatBalanceAnalysisReport.LoadLatestMeasurement(r, keys)?.status ?? "없음";
            }
            MeasurementReport Report(string status, string fingerprint, params MeasurementScenario[] scenarios)
            {
                var r = new MeasurementReport { status = status, planVersion = "test", fingerprint = fingerprint };
                r.scenarios.AddRange(scenarios);
                return r;
            }
            string merged = Merge("hidden", ("a", Report("PASS", "A", Scenario("k1", "PASS", "PASS 피해", "FAIL 군집"), Scenario("k2", "PASS", "PASS 피해")), 0));
            Check(merged == "FAIL", "실패 주입 병합: 옛 기록의 숨은 FAIL", merged);
            merged = Merge("blocked", ("a", Report("PASS", "A", Scenario("k1", "PASS", "PASS 피해"), Scenario("k2", "BLOCKED")), 0));
            Check(merged == "BLOCKED", "실패 주입 병합: 차단", merged);
            var withError = Report("PASS", "A", Scenario("k1", "PASS", "PASS 피해"), Scenario("k2", "PASS", "PASS 피해"));
            withError.errors.Add("예외");
            merged = Merge("error", ("a", withError, 0));
            Check(merged == "FAIL", "실패 주입 병합: 오류", merged);
            merged = Merge("otherprint", ("a", Report("INTERRUPTED", "B", Scenario("k2", "PASS", "PASS 피해")), 0), ("b", Report("PASS", "A", Scenario("k1", "PASS", "PASS 피해")), 1));
            Check(merged.StartsWith("PARTIAL"), "지문이 다른 중단 회차는 병합하지 않음", merged);
            merged = Merge("sameprint", ("a", Report("INTERRUPTED", "A", Scenario("k2", "PASS", "PASS 피해")), 0), ("b", Report("PASS", "A", Scenario("k1", "PASS", "PASS 피해")), 1));
            Check(merged == "PASS", "지문이 같은 중단 회차는 이어 붙임", merged);
            var interrupted = Report("INTERRUPTED", "A", Scenario("k2", "PASS", "PASS 피해"));
            interrupted.interruptions.Add("측정 도중 Play가 외부에서 종료됨(완료 1개)");
            merged = Merge("interrupted", ("a", interrupted, 0), ("b", Report("PASS", "A", Scenario("k1", "PASS", "PASS 피해")), 1));
            Check(merged == "PASS", "중단 뒤 이어 완료: 중단 이력은 FAIL이 아님", merged);
            interrupted = Report("INTERRUPTED", "A", Scenario("k2", "PASS", "PASS 피해"));
            interrupted.errors.Add("측정 도중 Play가 외부에서 종료됨(완료 1개)");
            merged = Merge("legacy-interrupted", ("a", interrupted, 0), ("b", Report("PASS", "A", Scenario("k1", "PASS", "PASS 피해")), 1));
            Check(merged == "PASS", "옛 중단 기록의 정확한 표식을 중단 이력으로 변환", merged);
            interrupted.errors.Add("NullReferenceException 실제 오류");
            merged = Merge("interrupted-error", ("a", interrupted, 0), ("b", Report("PASS", "A", Scenario("k1", "PASS", "PASS 피해")), 1));
            Check(merged == "FAIL", "중단 회차의 실제 오류는 이어 완료해도 FAIL", merged);
            string Follow(float[] measured, float[] scheduled, bool dead) => CombatBalanceMeasurementStatus.OfFollowupTimes(measured, scheduled, dead, out _);
            Check(Follow(new[] { .64f }, new[] { .63f }, true) == "PASS", "후속 시간표: 사망까지 관측된 적중 대조", "0.64/0.63초");
            Check(Follow(new[] { .9f }, new[] { .63f }, true) == "FAIL", "후속 시간표: 사망해도 지연 오차는 FAIL", "0.9/0.63초");
            Check(Follow(new[] { .63f, .8f }, new[] { .63f }, true) == "FAIL", "후속 시간표: 추가 적중은 FAIL", "2/1회");
            Check(Follow(new float[0], new[] { .63f }, false) == "FAIL", "후속 시간표: 생존 대상의 누락은 FAIL", "0/1회");
            Check(Follow(new float[0], new[] { .63f }, true) == "NOT_RUN", "후속 시간표: 본타 사망이면 후속 시각 미검증", "NOT_RUN");
            Check(Follow(new[] { .63f }, new[] { .63f, .8f }, true) == "PASS", "후속 시간표: 사망 뒤 남은 적중은 앞부분만 대조", "1/2회");
            return list;
        }

        static List<string> IsolationAndFingerprintChecks(string folder)
        {
            var list = new List<string>();
            void Check(bool ok, string label) => list.Add((ok ? "PASS " : "FAIL ") + label);
            // 공유 프로세스에 남았던 테스트 값도 해제한다. 이전 값을 다시 넣지 않는다.
            try
            {
                Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", Path.Combine(folder, "OldTestAccount"));
                bool entered = false;
                CombatBalancePlayMeasurement.BeginIsolatedPlay(Path.Combine(folder, "NewTestAccount"), () => entered = true);
                CombatBalancePlayMeasurement.ClearIsolatedSaveDirectory();
                Check(entered && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY")), "격리 종료: 이전 테스트 계정 대신 빈 값으로 해제");
                bool failed = false;
                try { CombatBalancePlayMeasurement.BeginIsolatedPlay(Path.Combine(folder, "StartFailure"), () => throw new InvalidOperationException("시작 실패 주입")); }
                catch (InvalidOperationException) { failed = true; }
                Check(failed && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY")), "격리 Play 시작 실패도 저장 경로 해제");
            }
            finally { CombatBalancePlayMeasurement.ClearIsolatedSaveDirectory(); }

            string root = Path.Combine(folder, "FingerprintFixture");
            Directory.CreateDirectory(root);
            var files = new HashSet<string>();
            foreach (var f in new[] { "enemy.prefab", "motion.anim", "model.fbx", "movement.controller", "input.cs", "item.cs" })
            {
                File.WriteAllText(Path.Combine(root, f), "original", Encoding.UTF8);
                File.WriteAllText(Path.Combine(root, f + ".meta"), "guid: original", Encoding.UTF8);
                CombatBalancePlayMeasurement.AddFingerprintFile(files, f);
            }
            string Hash() => CombatBalancePlayMeasurement.HashFingerprintInputs(root, new[] { "plan" }, files);
            string original = Hash();
            Check(original == CombatBalancePlayMeasurement.HashFingerprintInputs(root, new[] { "plan" }, files.Reverse()), "지문: 파일 열거 순서와 무관하게 같은 결과");
            foreach (var f in new[] { "enemy.prefab", "motion.anim", "model.fbx", "movement.controller", "input.cs", "item.cs", "enemy.prefab.meta" })
            {
                string path = Path.Combine(root, f), old = File.ReadAllText(path);
                File.WriteAllText(path, "changed", Encoding.UTF8);
                Check(Hash() != original, "지문: " + f + " 변경을 탐지");
                File.WriteAllText(path, old, Encoding.UTF8);
            }
            File.Delete(Path.Combine(root, "enemy.prefab.meta"));
            Check(Hash() != original, "지문: 의존 파일 삭제도 탐지");
            return list;
        }
    }
}
