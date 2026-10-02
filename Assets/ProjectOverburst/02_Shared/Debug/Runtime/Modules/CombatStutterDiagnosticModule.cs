#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Overburst.DebugTools
{
    internal static class CombatStutterDiagnosticModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            DebugSection section = DebugRegistry.Section(DebugTabs.SystemTab, "전투 끊김 진단", 25,
                "직접 전투를 관찰 · 최대120초 · 종료 뒤 결과 저장");
            section.Buttons("기록 시작")
                .Add("관찰 기록", () => CombatStutterCapture.Start())
                .Add("Profiler 포함", () => CombatStutterCapture.Start(binaryProfiler: true))
                .WithId("system.stutter.start")
                .EnabledWhen(() => !CombatStutterCapture.Running, "이미 기록 중이에요")
                .Tip("적중/치명타·혈흔 변형·피격음·GC·저장·렌더 대기를 기록한다. Profiler 포함은 추가 측정 비용과 raw 파일 기록이 있다.");
            section.Button("기록 종료·저장", CombatStutterCapture.Stop)
                .WithId("system.stutter.stop")
                .EnabledWhen(() => CombatStutterCapture.Running, "기록 중이 아니에요");
            section.Readout("상태", () => CombatStutterCapture.Status).WithId("system.stutter.status").Lines(2);
            section.Readout("결과 폴더", () => CombatStutterCapture.LastFolder ?? "아직 결과 없음")
                .WithId("system.stutter.output").Lines(2);
        }
    }
}
#endif
