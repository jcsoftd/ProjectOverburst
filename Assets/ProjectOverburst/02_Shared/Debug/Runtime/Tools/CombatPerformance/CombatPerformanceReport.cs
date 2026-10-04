#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Overburst.DebugTools.Performance
{
    public static class CombatPerformanceReport
    {
        [Serializable] sealed class ReturnStatus { public string status; }
        static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        static string Cell(string value) => (value ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        public static CombatPerformanceRun Read(string directory)
        {
            string path = Path.Combine(directory, "run.json");
            if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new IOException("결과 파일이 너무 큽니다.");
            var run = UnityEngine.JsonUtility.FromJson<CombatPerformanceRun>(File.ReadAllText(path));
            if (run == null || run.schema != 1 || run.profile == null || run.segments == null) throw new InvalidDataException("지원하지 않는 결과입니다.");
            return run;
        }
        public static void Write(CombatPerformanceRun run)
        {
            var b = new StringBuilder();
            b.AppendLine("# OVERBURST 전투 성능 결과").AppendLine();
            b.AppendLine("- 회차: " + Cell(run.id)).AppendLine("- 실행: " + Cell(run.status) + " / " + Cell(run.origin));
            b.AppendLine("- 프로필: " + Cell(run.profile.label) + " / " + Cell(run.weapon));
            b.AppendLine("- 환경: " + Cell(run.cpu) + " / " + Cell(run.gpu) + " / " + run.width + "×" + run.height + " / " + Cell(run.graphicsApi));
            b.AppendLine("- 코드: " + Cell(run.sourceRevision) + " / 콘텐츠: " + Cell(run.contentFingerprint));
            b.AppendLine("- 신규 오류/예외 로그: " + run.logErrors).AppendLine();
            if (!string.IsNullOrEmpty(run.reason)) b.AppendLine("실행 사유: " + Cell(run.reason)).AppendLine();
            b.AppendLine("| 구간 | 반복/주기 | 적 시작→종료 | wall 평균/P95/P99/최대 ms | 50ms 끊김 | CPU/GPU 지연 평균 ms | 공격 수락/요청 | 적중/처치 | 혈흔 표시/요청 | 풀 새 생성 | 측정 상태 |");
            b.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (var s in run.segments)
            {
                b.AppendLine("| " + Cell(s.key) + " | " + (s.repetition + 1) + "/" + s.cycle + " | " + s.aliveAtStart + "→" + s.aliveAtEnd
                    + " | " + N(s.wall?.mean ?? 0) + "/" + N(s.wall?.p95 ?? 0) + "/" + N(s.wall?.p99 ?? 0) + "/" + N(s.wall?.maximum ?? 0)
                    + " | " + s.hitches50 + " | " + Available(s.delayedCpu) + "/" + Available(s.delayedGpu)
                    + " | " + s.accepted + "/" + s.requests + " | " + s.enemyHits + "/" + s.kills + " | " + s.bloodPlayed + "/" + s.bloodRequests
                    + " | " + s.poolCreated + " | " + Cell(s.validity) + " |");
            }
            b.AppendLine().AppendLine("| 구간 | 측정 초 | GC 0/1/2 | Unity 메모리 시작→끝 MiB | 관찰 비용 P95 ms | 풀 반환 대기 | 회피 완료/시작 | 획득/요청 | 50ms 끊김/분 |");
            b.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var s in run.segments)
                b.AppendLine("| " + Cell(s.key) + " | " + N(s.seconds) + " | " + s.gc0 + "/" + s.gc1 + "/" + s.gc2
                    + " | " + N(s.memoryStartBytes / 1048576) + "→" + N(s.memoryEndBytes / 1048576) + " | " + N(s.observer?.p95 ?? 0)
                    + " | " + s.poolPendingEnd + " | " + s.evadeCompleted + "/" + s.evades + " | " + s.pickupsCollected + "/" + s.pickupsRequested + " | " + N(s.hitch50PerMinute) + " |");
            b.AppendLine().AppendLine("시간은 프레임 ms 기준입니다. P95/P99는 nearest-rank 분위수이며 반복 비교는 회차별 P95의 중앙값을 사용합니다.");
            b.AppendLine("버퍼 포화·관찰기 부하·실제 적중 없음은 성능 비교에서 제외합니다. 3회 미만 비교는 후보 결과입니다.");
            b.AppendLine("첫 사용은 이 실행의 첫 사용이며 새 Player 프로세스·OS 캐시·셰이더 캐시의 초기 상태를 보장하지 않습니다.");
            b.AppendLine("원소 상태/에너지와 대량 사망의 직접 주입 구간은 실제 플레이 구간과 독립적으로 해석합니다. 회피는 현재 키보드 바인딩을 통과하는 가상 입력입니다.");
            b.AppendLine("timings.csv는 지연된 CPU/GPU 측정 채널입니다. 전투 이벤트와 같은 프레임이라고 해석하지 않습니다. 기록되지 않은 값은 UNAVAILABLE입니다.");
            b.AppendLine("Profiler marker 시간은 중첩되거나 여러 스레드의 합일 수 있어 전체 프레임 시간으로 합산하지 않습니다.");
            b.AppendLine("수동 관찰은 각 30초 청크 시작에 대상 참조를 확보합니다. 청크 도중 새로 생긴 대상의 적중 이벤트는 누락될 수 있습니다.");
            b.AppendLine("메모리 값은 Unity allocated memory이며 운영체제 Private Memory 또는 GPU 메모리가 아닙니다. post-release 값은 풀/캐시가 따뜻해진 뒤 반복 추세로 판단합니다.");
            b.AppendLine("실행 COMPLETE와 성능 목표 통과는 별개입니다. 일반 Play 반환은 editor-return.json의 PASS로 확인합니다.");
            File.WriteAllText(Path.Combine(CombatPerformancePaths.RequireOutput(run.output), "report.md"), b.ToString(), new UTF8Encoding(false));
        }
        static string Available(CombatPerformanceStats s) => s != null && s.samples > 0 ? N(s.mean) : "UNAVAILABLE";
        static Dictionary<string, List<CombatPerformanceSegment>> Groups(CombatPerformanceRun run)
        {
            var groups = new Dictionary<string, List<CombatPerformanceSegment>>(StringComparer.Ordinal);
            foreach (var s in run.segments)
            { if (!groups.TryGetValue(s.key, out var list)) { list = new List<CombatPerformanceSegment>(); groups.Add(s.key, list); } list.Add(s); }
            return groups;
        }
        public static CombatPerformanceComparison Compare(CombatPerformanceRun baseline, CombatPerformanceRun current)
        {
            var result = new CombatPerformanceComparison { baseline = baseline.id, current = current.id, status = "COMPARABLE", reason = "" };
            if (baseline.schema != current.schema || baseline.status != "COMPLETE" || current.status != "COMPLETE" || baseline.logErrors != 0 || current.logErrors != 0
                || string.IsNullOrEmpty(baseline.workloadSignature) || baseline.workloadSignature != current.workloadSignature)
            { result.status = "INCOMPARABLE"; result.reason = "완료/오류/프로필/환경/무기 조건이 일치하지 않습니다."; return result; }
            if (current.profile.mode == CombatPerformanceMode.Observation)
            { result.status = "INCOMPARABLE"; result.reason = "수동 관찰은 동일 전투 부하를 보장하지 않습니다. 원자료로 검토하세요."; return result; }
            var before = Groups(baseline); var after = Groups(current);
            var keys = new SortedSet<string>(before.Keys, StringComparer.Ordinal); keys.UnionWith(after.Keys);
            foreach (string key in keys)
            {
                var row = new CombatPerformanceComparisonRow { key = key, status = "INCOMPARABLE" };
                result.rows.Add(row);
                if (!before.TryGetValue(key, out var a) || !after.TryGetValue(key, out var b)) { row.reason = "한쪽 구간이 없습니다."; continue; }
                row.baselineRuns = a.Count; row.currentRuns = b.Count;
                if (!Eligible(a) || !Eligible(b)) { row.reason = "측정 무효/불완전 구간이 포함됐습니다."; continue; }
                if (a[0].rosterSignature != b[0].rosterSignature) { row.reason = "몬스터 구성 불일치"; continue; }
                if (!Close(Median(a, s => s.requests), Median(b, s => s.requests), .1)
                    || !Close(Median(a, s => s.accepted), Median(b, s => s.accepted), .1)
                    || !Close(Median(a, s => s.enemyHits), Median(b, s => s.enemyHits), .1)
                    || !Close(Median(a, s => s.kills), Median(b, s => s.kills), .05)
                    || !Close(Median(a, s => s.bloodPlayed), Median(b, s => s.bloodPlayed), .1)
                    || !Close(Median(a, s => s.pickupsCollected), Median(b, s => s.pickupsCollected), .1)
                    || !Close(Median(a, s => s.aliveAtEnd), Median(b, s => s.aliveAtEnd), .05)
                    || !Close(Median(a, s => s.evades), Median(b, s => s.evades), .1)
                    || Median(a, s => s.inFlightAtEnd) != Median(b, s => s.inFlightAtEnd))
                { row.reason = "실제 공격/적중/처치/표시/획득/생존/회피 부하 차이가 허용 범위를 넘었습니다."; continue; }
                row.beforeMedianP95 = Median(a, s => s.wall.p95); row.afterMedianP95 = Median(b, s => s.wall.p95);
                row.deltaMs = row.afterMedianP95 - row.beforeMedianP95;
                row.deltaPercent = row.beforeMedianP95 > 0 ? row.deltaMs * 100 / row.beforeMedianP95 : 0;
                double threshold = Math.Max(current.profile.regressionAbsoluteMs, row.beforeMedianP95 * current.profile.regressionPercent / 100);
                row.status = row.deltaMs > threshold ? "REGRESSION" : row.deltaMs < -threshold ? "IMPROVED" : "WITHIN_THRESHOLD";
                if (a.Count < 3 || b.Count < 3) { row.status = "CANDIDATE_" + row.status; row.reason = "각 3회 미만인 후보 비교"; }
            }
            int eligible = 0, regressions = 0, improvements = 0;
            foreach (var row in result.rows)
            {
                if (row.status == "INCOMPARABLE") continue;
                eligible++; if (row.status.EndsWith("REGRESSION", StringComparison.Ordinal)) regressions++;
                if (row.status.EndsWith("IMPROVED", StringComparison.Ordinal)) improvements++;
            }
            if (eligible == 0) result.status = "INCOMPARABLE";
            result.reason = "비교 가능 " + eligible + " / " + result.rows.Count + "구간 · 악화 " + regressions + " · 개선 " + improvements + " (후보 포함)";
            return result;
        }
        static bool Eligible(List<CombatPerformanceSegment> segments)
        { foreach (var s in segments) if (s.validity != "VALID" || s.wall == null || s.wall.samples < 2) return false; return true; }
        static double Median(List<CombatPerformanceSegment> values, Func<CombatPerformanceSegment, double> select)
        { var array = new double[values.Count]; for (int i = 0; i < values.Count; i++) array[i] = select(values[i]); return CombatPerformanceStats.Calculate(array, array.Length).median; }
        static bool Close(double a, double b, double tolerance) => Math.Abs(a - b) <= Math.Max(1, Math.Max(a, b) * tolerance);
        public static CombatPerformanceComparison SaveComparison(string baselineDirectory, string currentDirectory)
        {
            var before = Read(baselineDirectory); var after = Read(currentDirectory);
            var result = Compare(before, after);
            bool Returned(CombatPerformanceRun run, string folder)
            {
                if (run.origin != "Editor") return true;
                string path = Path.Combine(folder, "editor-return.json");
                return File.Exists(path) && UnityEngine.JsonUtility.FromJson<ReturnStatus>(File.ReadAllText(path))?.status == "PASS";
            }
            if (!Returned(before, baselineDirectory) || !Returned(after, currentDirectory))
            { result.status = "INCOMPARABLE"; result.reason = "Editor 일반 Play 반환 PASS가 필요합니다."; result.rows.Clear(); }
            CombatPerformancePaths.SaveJson(Path.Combine(currentDirectory, "comparison.json"), result);
            var b = new StringBuilder("# 전투 성능 기준 비교\n\n");
            b.AppendLine(result.status + ": " + result.reason).AppendLine();
            b.AppendLine("| 구간 | 기준/현재 반복 | 기준 P95 ms | 현재 P95 ms | 차이 ms/% | 판정 | 사유 |").AppendLine("|---|---:|---:|---:|---:|---|---|");
            foreach (var row in result.rows) b.AppendLine("| " + Cell(row.key) + " | " + row.baselineRuns + "/" + row.currentRuns + " | " + N(row.beforeMedianP95)
                + " | " + N(row.afterMedianP95) + " | " + N(row.deltaMs) + "/" + N(row.deltaPercent) + " | " + row.status + " | " + Cell(row.reason) + " |");
            File.WriteAllText(Path.Combine(CombatPerformancePaths.RequireOutput(currentDirectory), "comparison.md"), b.ToString(), new UTF8Encoding(false));
            return result;
        }
    }
}
#endif
