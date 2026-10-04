#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Overburst.DebugTools.Performance
{
    public interface ICombatPerformancePanelBackend
    {
        bool Busy { get; }
        string LastFolder { get; }
        CombatPerformanceRun ReadLastRun();
        string StartObservation(CombatPerformanceProfile profile);
        DebugResult OpenWindow();
        DebugResult Reveal(string folder);
    }

    /// <summary>F1 패널과 기존 전투 성능 기록기를 연결한다. Editor 기능은 별도 어셈블리에서 제공한다.</summary>
    public static class CombatPerformancePanel
    {
        public static ICombatPerformancePanelBackend Backend { get; set; }
        public const int ManualLimitSeconds = 8 * 60 * 60;
        static CombatPerformanceRun summaryRun;
        static string summary = "아직 전투 성능을 측정하지 않았어요";

        public static bool Running => CombatPerformanceRunner.Current != null;
        public static bool Observing => Running && CombatPerformanceRunner.Current.Run.profile.mode == CombatPerformanceMode.Observation;
        public static bool CanStart => Application.isPlaying && !Running && !DebugPerfRecorder.Running
            && !VisualPlayBridge.Running && !(Backend?.Busy ?? false) && PlayerContext.GetOrCreate().CurrentActor != null;
        public static string LastFolder => CombatPerformanceRunner.LastRun?.output ?? Backend?.LastFolder ?? "";
        public static bool HasResult => !Running && !(Backend?.Busy ?? false) && !string.IsNullOrEmpty(LastFolder);
        public static string Status
        {
            get
            {
                var runner = CombatPerformanceRunner.Current;
                if (runner == null) return Backend?.Busy == true ? "반복 검사 전환·반환 중" : "대기 · 현재 Play의 전투를 기록해요";
                if (!Observing) return "자동 반복 검사 · " + runner.Progress;
                double seconds = runner.ElapsedSeconds;
                return string.Format(CultureInfo.InvariantCulture, "현재 전투 측정 · {0:0} / {1:0}초 · 저장 {2}구간",
                    seconds, runner.Run.profile.observationSeconds, runner.Run.segments.Count);
            }
        }

        public static DebugResult Start(int seconds)
        {
            if (!CanStart) return DebugResult.Fail("플레이어가 준비되고 진행 중인 측정·시각 재생이 없어야 해요");
            if (seconds < 1 || seconds > ManualLimitSeconds) return DebugResult.Fail("측정 시간은 1초~8시간이에요");
            try
            {
                var profile = new CombatPerformanceProfile { label = "DebugPanelObservation", mode = CombatPerformanceMode.Observation,
                    observationSeconds = seconds, preparedElementStress = false, deathBurst = false };
                if (Backend != null) Backend.StartObservation(profile);
                else
                {
                    string folder = Path.Combine(CombatPerformancePaths.OutputRoot, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                        + "_Panel_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    CombatPerformanceRunner.StartRun(profile, folder);
                }
                DebugHub.Close();
                return DebugResult.Ok("전투 성능 측정 시작 · F1을 열어 종료·저장할 수 있어요");
            }
            catch (Exception error) { return DebugResult.Fail("측정을 시작하지 못했어요: " + error.Message); }
        }

        public static DebugResult Finish()
        {
            if (!Observing) return DebugResult.Fail("현재 전투의 수동 측정 중에 사용할 수 있어요");
            var runner = CombatPerformanceRunner.Current;
            runner.CompleteObservation();
            return runner.Run.status == "COMPLETE" ? DebugResult.Ok("측정 종료 · 결과 저장 완료 · Play를 계속할 수 있어요")
                : DebugResult.Fail("결과 저장 상태: " + runner.Run.status + " · " + runner.Run.reason);
        }

        public static string Summary
        {
            get
            {
                // Read persisted results only while idle. Reuse the formatted text across UI refreshes.
                var run = CombatPerformanceRunner.LastRun ?? (!Running && Backend?.Busy != true ? Backend?.ReadLastRun() : null);
                if (run == null || ReferenceEquals(run, summaryRun)) return summary;
                summaryRun = run;
                var text = new StringBuilder();
                int frames = 0, gc = 0, hitches = 0;
                double seconds = 0;
                foreach (var part in run.segments) { frames += part.frames; gc += part.gc0; hitches += part.hitches50; seconds += part.seconds; }
                string state = run.status == "COMPLETE" ? "완료" : run.status == "CANCELLED" ? "중단" : run.status == "INTERRUPTED" ? "Play 종료" : run.status;
                text.AppendFormat(CultureInfo.InvariantCulture, "{0} · {1:0}초 · {2}구간 · {3}프레임", state, seconds, run.segments.Count, frames);
                if (run.segments.Count > 0)
                {
                    var first = run.segments[0]; var last = run.segments[run.segments.Count - 1];
                    if (last.wall != null && last.wall.samples > 0)
                        text.AppendFormat(CultureInfo.InvariantCulture, "\n최근 구간 평균 {0:0.0}ms · p95 {1:0.0} · p99 {2:0.0}", last.wall.mean, last.wall.p95, last.wall.p99);
                    else text.Append("\n최근 구간 프레임 표본 없음");
                    text.AppendFormat(CultureInfo.InvariantCulture, "\n전체 50ms 초과 {0}회 · 측정 GC(0) {1}회 · 메모리 {2:+0.0;-0.0;0.0}MB",
                        hitches, gc, (last.memoryEndBytes - first.memoryStartBytes) / 1048576d);
                    if (last.validity != "VALID") text.Append("\n최근 구간: " + last.validity);
                }
                else text.Append("\n저장된 측정 구간이 없어요");
                summary = text.ToString(); return summary;
            }
        }

        public static DebugResult OpenWindow() => Backend?.OpenWindow() ?? DebugResult.Fail("반복 검사 창은 Unity Editor에서 열 수 있어요");
        public static DebugResult RevealResult()
        {
            if (!HasResult) return DebugResult.Fail("저장된 결과가 필요해요");
            try
            {
                string folder = CombatPerformancePaths.RequireOutput(LastFolder);
                if (!Directory.Exists(folder)) return DebugResult.Fail("결과 폴더를 찾지 못했어요");
                if (Backend != null) return Backend.Reveal(folder);
                Application.OpenURL(new Uri(folder + Path.DirectorySeparatorChar).AbsoluteUri);
                return DebugResult.Ok("결과 폴더 열림");
            }
            catch (Exception error) { return DebugResult.Fail(error.Message); }
        }
    }
}
#endif
