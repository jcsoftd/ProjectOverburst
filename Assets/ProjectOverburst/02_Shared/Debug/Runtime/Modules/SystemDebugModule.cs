#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overburst.DebugTools
{
    /// <summary>시스템 탭: 시간(배속·일시정지·한 프레임), 성능, 로그, 창 관리(90C 7.8).</summary>
    internal static class SystemDebugModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            DebugSection time = DebugRegistry.Section(DebugTabs.SystemTab, "시간", 10, "1배속 미만에서는 히트스톱이 나오지 않아요");
            time.Choice("배속", () => DebugTime.Speed, DebugTime.SetSpeed,
                    speed => speed.ToString("0.##", CultureInfo.InvariantCulture) + "x", DebugTime.Speeds)
                .WithId("system.time.speed")
                .NoPreset()
                .Keywords("timescale", "슬로우", "속도")
                .Tip("히트스톱·슬로우 중에 바꾸면 끝난 뒤 적용된다. 느린 배속은 동작 관찰용이고, 타격감은 1배속에서 본다.");
            time.Buttons("정지")
                .Add(() => DebugTime.Paused ? "재개" : "일시정지", DebugTime.TogglePause)
                .WithId("system.time.pause")
                .Hotkey(Key.F6)
                .Keywords("pause");
            time.Buttons("한 프레임")
                .Add("진행", DebugTime.Step)
                .WithId("system.time.step")
                .Hotkey(Key.F7)
                .Keywords("step", "frame")
                .Tip("멈춘 상태에서 배속 1로 한 프레임만 진행한다.");
            time.Readout("상태", () => DebugTime.StatusText)
                .WithId("system.time.status")
                .Pinnable();

            DebugSection perf = DebugRegistry.Section(DebugTabs.SystemTab, "성능", 20, "0.5초 평균");
            perf.Readout("프레임", () => DebugPerf.FrameText)
                .WithId("system.perf.frame")
                .Pinnable()
                .Keywords("fps");
            perf.Readout("메모리", () => DebugPerf.MemoryText)
                .WithId("system.perf.memory")
                .Pinnable();
            perf.Readout("몬스터 수", () => EnemyAIController.AliveEnemyCount + "마리")
                .WithId("system.perf.monsters")
                .Pinnable();

            DebugSection log = DebugRegistry.Section(DebugTabs.SystemTab, "로그", 30, "오류가 나면 탭 이름에 빨간 표시");
            log.Readout("오류 · 경고", () => DebugLogCapture.ErrorCount + " · " + DebugLogCapture.WarningCount)
                .WithId("system.log.counts")
                .Pinnable();
            log.Readout("최근", () => DebugLogCapture.FormatRecent(12, DebugUi.Style))
                .Lines(12)
                .WithId("system.log.recent");
            log.Button("로그 지우기", DebugLogCapture.Clear)
                .WithId("system.log.clear");

            DebugSection window = DebugRegistry.Section(DebugTabs.SystemTab, "창", 40);
            window.Button("창 위치·크기 초기화", DebugHub.ResetLayout)
                .WithId("system.window.reset")
                .Tip("창이 화면 밖으로 나갔거나 너무 작아졌을 때 쓴다.");
            window.Button("핀 모두 해제", DebugPrefs.ClearPins)
                .WithId("system.window.clearPins");
        }
    }
}
#endif
