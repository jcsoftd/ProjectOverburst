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
    }
}
