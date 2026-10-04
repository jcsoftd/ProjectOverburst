#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools.Performance;
using UnityEngine;

namespace Overburst.DebugTools
{
    internal static class CombatPerformanceDebugModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            var section = DebugRegistry.Section(DebugTabs.SystemTab, "전투 성능 검사", 25, "현재 Play 관찰 · 시작하면 패널을 닫아요 · F1으로 다시 열기");
            section.Buttons("현재 전투 측정")
                .Add("2분", () => CombatPerformancePanel.Start(120))
                .Add("5분", () => CombatPerformancePanel.Start(300))
                .Add("직접 종료", () => CombatPerformancePanel.Start(CombatPerformancePanel.ManualLimitSeconds))
                .WithId("system.combatPerf.start").NoPreset()
                .EnabledWhen(() => CombatPerformancePanel.CanStart, "플레이어 준비·다른 측정과 시각 재생 종료 후 시작해요")
                .Keywords("performance", "벤치마크", "끊김")
                .Tip("직접 플레이하는 전투의 프레임·GC·메모리·전투 이벤트를 기록해요. 직접 종료는 최대 8시간이며, 30초마다 저장해요. 시작 후 패널을 닫아 관찰 비용을 줄여요.");
            section.Readout("진행", () => CombatPerformancePanel.Status).Lines(2)
                .WithId("system.combatPerf.status").Pinnable();
            section.Button("종료·저장", CombatPerformancePanel.Finish)
                .WithId("system.combatPerf.finish")
                .EnabledWhen(() => CombatPerformancePanel.Observing, "현재 전투 수동 측정 중에 사용할 수 있어요")
                .Tip("진행 중인 구간까지 저장하고 측정만 종료해요. 현재 Play는 계속할 수 있어요.");
            section.Readout("최근 결과", () => CombatPerformancePanel.Summary).Lines(4)
                .WithId("system.combatPerf.result")
                .Tip("p95·p99는 최근 측정 구간 값이에요. 전체 끊김·GC와 시작~종료 메모리 변화를 함께 표시해요. 실전 관찰은 동일 조건의 성능 비교에서 제외돼요.");
            section.Button("결과 폴더 열기", CombatPerformancePanel.RevealResult)
                .WithId("system.combatPerf.reveal")
                .EnabledWhen(() => CombatPerformancePanel.HasResult, "측정 종료 후 저장된 결과를 열 수 있어요");
            section.Button("반복 검사 창 열기", CombatPerformancePanel.OpenWindow)
                .WithId("system.combatPerf.window")
                .VisibleWhen(() => Application.isEditor)
                .EnabledWhen(() => CombatPerformancePanel.Backend != null, "Unity Editor 연결이 필요해요")
                .Tip("격리 전투의 조건·반복 횟수·기준 결과를 설정하는 창을 열어요. 자동 반복 검사는 EditMode에서 시작해요.");
        }
    }
}
#endif
