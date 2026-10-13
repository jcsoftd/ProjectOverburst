using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Overburst.DebugTools.Performance;

public static class DesertPerformanceReport
{
    static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static void WriteMap(string output, DesertPerformanceProbe.Result result)
    {
        var b = new StringBuilder("# 사막 이동 성능 기록\n\n");
        b.AppendLine(result.candidate + " · " + result.status + " · " + result.visitedAreas + "/" + result.expectedAreas + " 플랫폼");
        b.AppendLine("\n입장 " + F(result.entrySeconds) + "초 · 준비 " + F(result.preparationSeconds) + "초 · 자연 명단 " + result.total + " · 도로 " + result.roadMembers);
        b.AppendLine("\n측정 조건: " + result.routeContract);
        b.AppendLine("\n아래는 구간 전체 통계다. 최종 3회 집계의 주요 통계는 전투영역 안 travel/dwell 프레임만 사용하며 positions.csv의 combat_area/input_blocked로 다시 계산한다.");
        if (!string.IsNullOrEmpty(result.reason)) b.AppendLine("\n실패/중단: " + result.reason);
        b.AppendLine("\n| 구간 | 표본 | 평균 FPS | P95 ms | P99 ms | 최대 ms | >50ms | GC0/1/2 | 기록기 P95 ms | 신뢰 |\n|---|---:|---:|---:|---:|---:|---:|---|---:|---|");
        foreach (var segment in result.segments.OrderByDescending(s => s.performance.wall?.p95 ?? 0))
        {
            var p = segment.performance; var wall = p.wall;
            b.AppendLine("| " + segment.label + " | " + p.frames + " | " + (wall != null && wall.mean > 0 ? F(1000 / wall.mean) : "N/A")
                + " | " + F(wall?.p95 ?? 0) + " | " + F(wall?.p99 ?? 0) + " | " + F(wall?.maximum ?? 0) + " | " + p.hitches50
                + " | " + p.gc0 + "/" + p.gc1 + "/" + p.gc2 + " | " + F(p.observer?.p95 ?? 0) + " | " + p.validity + " |");
        }
        b.AppendLine("\n## 목표 프레임 예산과 부하\n\n");
        b.AppendLine("1% low는 가장 느린 ceil(전체 표본×1%) 프레임 시간의 평균을 FPS로 환산한다. 목표 FPS는 분석 예산이며 FPS 제한 설정과 별도다.");
        b.AppendLine("\n| 구간 | 목표 FPS | 예산 초과 % | 1% low FPS | 33/50/100ms 초과 | 최장 예산 초과 연속 프레임 | Unity 메모리 변화 bytes | 좌표 누락 |\n|---|---:|---:|---:|---|---:|---:|---:|");
        foreach (var segment in result.segments.Where(s => s.analysis != null))
        {
            var a = segment.analysis;
            b.AppendLine("| " + segment.label + " | " + a.targetFps + " | " + F(a.overBudgetPercent) + " | " + F(a.onePercentLowFps)
                + " | " + a.over33 + "/" + a.over50 + "/" + a.over100 + " | " + a.longestOverBudgetStreak + " | " + F(a.memoryDeltaBytes) + " | " + a.missingPositions + " |");
        }
        b.AppendLine("\n## 가장 느린 프레임의 위치와 당시 값\n\n");
        b.AppendLine("위치/스폰 값은 같은 ID의 초기 콜백 관측이다. 완료된 Profiler 표본의 지연 때문에 동시 관측만으로 원인을 확정하지 않는다. 전체 숫자는 각 구간 worst-frames.csv/analysis.json에 보존한다.");
        b.AppendLine("\n| 구간 | frame | wall ms | 기록기 ms | 좌표 x/y/z | 활성/대기 | 풀 신규 생성 누계 | 주요 시간 마커 ms |\n|---|---:|---:|---:|---|---|---:|---|");
        foreach (var pair in result.segments.Where(s => s.analysis != null).SelectMany(s => s.analysis.worstFrames.Select(f => new { segment = s, frame = f }))
            .OrderByDescending(x => x.frame.wallMs).Take(30))
        {
            var f = pair.frame; string[] w = string.IsNullOrEmpty(f.workloadRow) ? null : DesertPerformanceDetails.ParseCsv(f.workloadRow);
            var top = f.metrics.Where(m => m.Key.EndsWith("[ms]", StringComparison.Ordinal) && !m.Key.Contains("Wait")
                && !m.Key.Contains("/Main Thread [") && !m.Key.Contains("/PlayerLoop [") && !m.Key.Contains("/Render Thread ["))
                .OrderByDescending(m => m.Value).Take(4);
            b.AppendLine("| " + pair.segment.label + " | " + f.frame + " | " + F(f.wallMs) + " | " + F(f.observerMs)
                + " | " + (w == null ? "N/A" : w[1] + "/" + w[2] + "/" + w[3])
                + " | " + (w == null ? "N/A" : w[6] + "/" + w[7]) + " | " + (w == null || w.Length < 18 ? "N/A" : w[17])
                + " | " + string.Join("; ", top.Select(m => m.Key + "=" + F(m.Value))) + " |");
        }
        b.AppendLine("\n## 입장 단계 기록\n\n");
        b.AppendLine("30프레임 간격으로 관측한 준비 단계 전환이다. 아래 구간 길이는 관측 사이 시간이며 전용 함수 실행시간과 다르다.");
        b.AppendLine("\n| 관측 frame | 입장 후 초 | 단계 | 다음 관측까지 초 |\n|---:|---:|---|---:|");
        for (int i = 0; i < result.entryStages.Count; i++)
        {
            var stage = JObject.FromObject(result.entryStages[i]); double at = (double)stage["seconds"];
            double next = i + 1 < result.entryStages.Count ? (double)JObject.FromObject(result.entryStages[i + 1])["seconds"] : result.entrySeconds;
            b.AppendLine("| " + stage["frame"] + " | " + F(at) + " | " + stage["stage"] + " | " + F(Math.Max(0, next - at)) + " |");
        }
        b.AppendLine("\n## 우선 조사할 후보\n");
        foreach (var segment in result.segments.Where(s => s.performance.validity == "VALID").OrderByDescending(s => s.performance.wall?.p95 ?? 0).Take(10))
        {
            var p = segment.performance;
            var metrics = (p.metrics ?? Array.Empty<CombatPerformanceMetricResult>()).Where(m => m.stats.samples > 0).ToArray();
            var top = metrics.Where(m => m.metric.nanoseconds && !m.metric.name.Contains("Wait") && m.metric.name != "Main Thread"
                && m.metric.name != "Render Thread" && m.metric.name != "PlayerLoop").OrderByDescending(m => m.stats.p95).Take(5);
            b.AppendLine("- " + segment.label + ": " + string.Join(", ", top.Select(m => m.metric.name + " P95 " + F(m.stats.p95) + "ms")));
            var gc = metrics.FirstOrDefault(m => m.metric.name == "GC Allocated In Frame");
            if (gc != null && gc.stats.p95 > 0) b.AppendLine("  GC 할당 P95 " + F(gc.stats.p95) + "bytes. 해당 구간의 frames.csv에서 스파이크와 같은 프레임인지 대조.");
        }
        WriteAuditLinks(b, result);
        b.AppendLine("\n## 구간별 전체 마커 통계\n\n");
        foreach (var segment in result.segments)
        {
            var p = segment.performance;
            b.AppendLine("\n### " + segment.label + "\n");
            b.AppendLine("부하 시작: `" + DesertPerformanceProbe.Serialize(segment.workloadStart).Replace("\n", " ").Replace("\r", "") + "`\n");
            b.AppendLine("부하 종료: `" + DesertPerformanceProbe.Serialize(segment.workloadEnd).Replace("\n", " ").Replace("\r", "") + "`\n");
            b.AppendLine("지연 CPU: " + Timing(p.delayedCpu) + "; 지연 GPU: " + Timing(p.delayedGpu) + ". 샘플은 특정 위치/행과 직접 대응하지 않는다.\n");
            if (segment.analysis != null && segment.analysis.missingMetrics.Length > 0)
                b.AppendLine("미수집 요청 마커: **N/A** — " + string.Join(", ", segment.analysis.missingMetrics) + "\n");
            b.AppendLine("| 마커 | 단위 | 표본 | 평균 | P95 | P99 | 최대 |\n|---|---|---:|---:|---:|---:|---:|");
            foreach (var metric in (p.metrics ?? Array.Empty<CombatPerformanceMetricResult>()).OrderBy(m => m.metric.category).ThenBy(m => m.metric.name))
            {
                var s = metric.stats; bool available = s.samples > 0;
                b.AppendLine("| " + metric.metric.category + "/" + metric.metric.name + " | " + (metric.metric.nanoseconds ? "ms" : metric.metric.unit)
                    + " | " + s.samples + " | " + (available ? F(s.mean) : "N/A") + " | " + (available ? F(s.p95) : "N/A")
                    + " | " + (available ? F(s.p99) : "N/A") + " | " + (available ? F(s.maximum) : "N/A") + " |");
            }
            if (segment.analysis != null)
            {
                b.AppendLine("\n누적 Profiler 샘플 수: 시간 마커는 실행 횟수의 단서다. 카운터 마커는 메서드 호출 횟수가 아니며 동일 의미로 해석하지 않는다. 0과 미수집을 구분한다.\n");
                b.AppendLine("| 마커 샘플 | 누계 |\n|---|---:|");
                foreach (var count in segment.analysis.markerSampleTotals.OrderBy(c => c.Key)) b.AppendLine("| " + count.Key + " | " + F(count.Value) + " |");
            }
        }
        b.AppendLine("\n## 측정 해석\n\n");
        b.AppendLine("- 완료는 예정된 방문과 기록 완료이며 성능 합격이 아니다. 제한 실행·오류·누락·정체·버퍼 포화를 전체 통과로 올리지 않는다.");
        b.AppendLine("- Wall은 Editor 업데이트에서 관측한 완료 프레임 간격. Editor/UI/OS 대기와 기록기 비용이 포함된다. FPS=1000/평균 프레임 시간. 프레임별 FPS의 평균은 사용하지 않는다.");
        b.AppendLine("- CPU/렌더 스레드의 대기와 작업을 구분한다. 중첩/다중 스레드 마커는 합산하지 않는다. 상위 마커는 조사 후보이며 인과 확정이 아니다.");
        b.AppendLine("- FrameTiming CPU/GPU는 지연된 별도 timings.csv 채널. 표본 0은 N/A이며 0ms가 아니다. 특정 위치/스파이크 프레임에 GPU 표본을 직접 대응하지 않는다.");
        b.AppendLine("- positions.csv의 frame을 frames.csv와 대조해 위치/입력/자연 활성 수를 찾는다. 로딩·이동·컷신·도착 관찰을 별도 구간으로 기록한다.");
        b.AppendLine("- 플랫폼 census는 표본 시점의 프러스텀 후보·큰 메시·재질/LOD/그림자/파티클/충돌체 조사다. 실제 드로콜과 오버드로는 해당 카운터/Profiler로 추가 확인한다.");
        b.AppendLine("- 고정 시드·테마·레벨, 실제 충돌 이동, 자연 AI/스폰을 유지한다. 플레이어 피해 면역으로 생존을 보장한다. 전투 적중/사망/증원 소진 검증은 기존 전투 성능 도구 범위다.");
        b.AppendLine("- 재방문은 같은 실행의 웜 상태이며 첫 방문도 OS/셰이더 캐시 콜드 상태를 보장하지 않는다. uncapped/VSync/해상도/품질/Profiler 상태를 환경과 함께 확인한다.");
        b.AppendLine("- Player 빌드, Player FPS 합격, 장시간 전투, 직접 체감은 NOT_RUN. 공식 API: [ProfilerRecorder](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html), [FrameTimingManager](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FrameTimingManager.html).");
        File.WriteAllText(Path.Combine(output, "summary.md"), b.ToString(), new UTF8Encoding(false));
    }
    static string Timing(CombatPerformanceStats s) => s == null || s.samples == 0 ? "N/A (0 samples)" : s.samples + " samples / P95 " + F(s.p95) + "ms / P99 " + F(s.p99) + "ms";
    static void WriteAuditLinks(StringBuilder b, DesertPerformanceProbe.Result result)
    {
        b.AppendLine("\n## 전투·몬스터 AI 조사와 연결할 근거\n\n");
        b.AppendLine("실행 루트 Reference/최적화조사.md와 reference.json에 조사 원문·SHA·33개 후보를 보존한다. 아래 측정은 개선 성공이나 인과 확정이 아니다.\n");
        b.AppendLine("| 후보 ID | 조사 대상 | 현재 연결 근거 | 추가 확인 |\n|---|---|---|---|");
        foreach (var row in new[] {
            ("S04", "논리 명단 전수 검사", "OVERBURST.MapSpawnFinite.Reconcile", "total/active 비율·규모별 P95와 함께 비교"),
            ("S05", "구역 우선순위 정렬", "OVERBURST.MapSpawnFinite.Priority", "spawned=0인 프레임에도 비용이 남는지 raw frame 대조"),
            ("S06/S07", "주변 수·경계 복구·증원", "OVERBURST.MapSpawnFinite.Refresh", "합계 구간. 개별 순회·NavMesh 비용은 추가 분리 필요"),
            ("S08", "스폰 예산의 누락 비용", "OVERBURST.MapSpawnFinite.Total", "Total은 TickFinite 범위. SetFiniteAwake와 로딩 전체는 포함하지 않음; Batch와 중첩 합산 금지"),
            ("A03", "전역 부대 전략 갱신", "Overburst.AI.SquadUpdate", "전역 마커이며 개별 AI의 skippedUpdates와 구분"),
            ("A01/A04", "타깃·원거리 위치 탐색", "Overburst.AI.TargetPlan", "상위 마커. 컴포넌트 조회/물리 preview의 개별 비용은 미분리"),
            ("A06", "판단 외 이동·애니메이션", "Animators.Update", "actors_*.json의 판단 주기/틱·movementEnabled/culling과 비교; 엔진 마커는 전역") })
        {
            var metrics = result.segments.SelectMany(s => s.performance.metrics ?? Array.Empty<CombatPerformanceMetricResult>()).Where(m => m.metric.name == row.Item3 && m.stats.samples > 0).ToArray();
            string evidence = metrics.Length == 0 ? "N/A — 미수집" : "기록됨 · 구간 P95 최대 " + F(metrics.Max(m => m.stats.p95)) + "ms";
            var counts = result.segments.Where(s => s.analysis != null).SelectMany(s => s.analysis.markerSampleTotals)
                .Where(c => c.Key.Contains("/" + row.Item3 + " samples [count]")).ToArray();
            evidence += counts.Length > 0 ? " · 누적 실행 샘플 " + F(counts.Sum(c => c.Value)) : " · 실행 샘플 수 N/A";
            b.AppendLine("| " + row.Item1 + " | " + row.Item2 + " | " + row.Item3 + ": " + evidence + " | " + row.Item4 + " |");
        }
        b.AppendLine("| L01–L04 | 입장 후보/정렬/물리/NavMesh | entryStages·입장 raw frame | 단계 관측값. 각 함수별 비용·호출 수는 아직 미분리 |");
        b.AppendLine("| L05/P01–P03 | 활성 상한·풀 첫 생성/재사용/반환 | positions.csv의 alive_limit/pool_*·구간 부하 시작/종료 | 신규 생성과 스파이크 동시 관측. VFX Pool 마커를 Enemy Pool 비용으로 해석하지 않음 |");
        b.AppendLine("| A05/C06 | Scene 가시성·몬스터 광원 | editorCameras·actors_*.json의 frustum/rendererVisible/pointLights/shadowLights | Scene/그림자 영향 후보와 활성 광원 수. GPU 인과 판정은 A/B 필요 |");
        b.AppendLine("| S01/S02/A07/C08/C09 | 야외 전용 또는 동굴 전용 후보 | 이번 사막 순회에서는 NOT_THIS_MAP | 정식 해당 맵 검사로 확인 |");
        b.AppendLine("| C01–C05/C07/C10 | 공격·대량 처치·드랍·명찰·첫 효과 | 이번 순회에서는 CONTROLLED_COMBAT_NOT_RUN | 기존 전투 성능 도구의 지정 교전/처치/획득 구간과 별도 대조 |");
    }
}
