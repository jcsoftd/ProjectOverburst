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
            perf.Buttons("측정")
                .Add("10초", () => DebugPerfRecorder.Start(10f))
                .Add("30초", () => DebugPerfRecorder.Start(30f))
                .WithId("system.perf.record")
                .EnabledWhen(() => !DebugPerfRecorder.Running, "측정 중이에요")
                .Tip("평균·1% 느린 프레임·최악·GC 횟수·몬스터 수. 에디터에서는 CSV를 개인파일/코덱스산출/Perf에 쓴다.");
            perf.Readout("측정 결과", () => DebugPerfRecorder.Progress)
                .Lines(2)
                .WithId("system.perf.result");

            DebugSection save = DebugRegistry.Section(DebugTabs.SystemTab, "저장", 35, "디버그 창 설정은 PlayerPrefs, 계정은 저장 폴더");
            save.Readout("계정", () => $"{DebugAccount.Label} · {DebugAccount.FolderName}")
                .WithId("system.save.account")
                .Tip("OVERBURST_SAVE_DIRECTORY가 있으면 격리 계정이다. 실제 계정에서는 계정에 남는 버튼이 한 번 더 묻는다.");

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
            window.Button("개발자 메모장 열기/닫기", ToggleNotepad)
                .WithId("system.window.notepad")
                .Keywords("memo", "메모");
        }

        private static DebugResult ToggleNotepad()
        {
            DeveloperNotepadUI notepad = Object.FindFirstObjectByType<DeveloperNotepadUI>(FindObjectsInactive.Include);
            if (notepad == null || !notepad.isActiveAndEnabled)
                return DebugResult.Fail("이 씬에 메모장이 없어요");
            notepad.Toggle();
            return DebugResult.Ok(notepad.IsWindowOpen ? "열림" : "닫힘");
        }
    }
}
#endif
