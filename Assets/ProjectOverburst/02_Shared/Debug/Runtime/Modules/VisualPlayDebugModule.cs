#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>시각 확인 탭: 선택·재생, 관찰 설정, 현재 진행, 사용자 확인의 고정 UI.</summary>
    internal static class VisualPlayDebugModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            var select = DebugRegistry.Section(DebugTabs.VisualPlay, "선택과 재생", 20, "게임 화면과 소리를 보는 개발자 확인용 · 테스트 계정 사용");
            select.Picker("카테고리", () => VisualPlayCatalog.Categories, item => $"{item.Title} · {item.Cases.Length}",
                () => VisualPlayBridge.Category, value => VisualPlayBridge.Category = value)
                .WithId("visual.select.category").NoPreset().EnabledWhen(() => !VisualPlayBridge.Running, "재생을 마친 뒤 선택할 수 있어요");
            select.Picker("개별 항목", () => VisualPlayBridge.CurrentCases, item => item.Title,
                () => VisualPlayBridge.Selected, value => VisualPlayBridge.Selected = value)
                .WithId("visual.select.case").NoPreset().EnabledWhen(() => !VisualPlayBridge.Running, "재생을 마친 뒤 선택할 수 있어요");
            select.Readout("볼 내용", () => VisualPlayBridge.Selected.Observe).Lines(3).WithId("visual.select.observe");
            select.Readout("준비 상태", () => VisualPlayBridge.ReadyText).WithId("visual.select.ready");
            select.Readout("목록 범위", () => VisualPlayBridge.RangeText).Lines(2).WithId("visual.select.range");
            select.Buttons("재생")
                .Add("전체", () => VisualPlayBridge.Start(VisualPlayScope.All))
                .Add("카테고리", () => VisualPlayBridge.Start(VisualPlayScope.Category))
                .Add("이 항목", () => VisualPlayBridge.Start(VisualPlayScope.Individual))
                .WithId("visual.run.scope").EnabledWhen(() => VisualPlayBridge.CanStart, "Unity Editor에서 진행 중인 재생이 없을 때 시작해요")
                .Tip("선택한 범위를 같은 시나리오로 재생해요. 실제 계정 Play라면 테스트 계정으로 다시 시작해요.");

            var settings = DebugRegistry.Section(DebugTabs.VisualPlay, "관찰 설정", 30, "동작은 정상 1배속 · 연결 동작 사이에는 지연을 넣지 않아요");
            settings.Choice("진행 방식", () => VisualPlayBridge.WaitAfterScenario, value => VisualPlayBridge.WaitAfterScenario = value,
                value => value ? "항목마다 멈춤" : "연속 재생")
                .WithId("visual.settings.wait").NoPreset();
            settings.Number("관찰 시간(초)", () => VisualPlayBridge.ObserveSeconds, value => VisualPlayBridge.ObserveSeconds = value, 1f, 20f, 1f, "0")
                .WithId("visual.settings.seconds").NoPreset().Tip("동작이 끝난 뒤 기다리는 시간이에요. 지속 효과는 실제 종료까지 보여줘요.");
            settings.Toggle("콘텐츠 전수", () => VisualPlayBridge.FullContent, value => VisualPlayBridge.FullContent = value)
                .WithId("visual.settings.content").NoPreset().EnabledWhen(() => !VisualPlayBridge.Running, "재생 시작 때 콘텐츠 범위를 고정해요")
                .Tip("외형·몬스터·무기/원소 전수 항목에서 정식 목록을 순환해요. 끄면 대표 콘텐츠를 재생해요.");

            var progress = DebugRegistry.Section(DebugTabs.VisualPlay, "현재 재생", 10);
            progress.Readout("현재", () => VisualPlayBridge.State.Current).Lines(2).WithId("visual.progress.current").Pinnable();
            progress.Readout("관찰 지점", () => VisualPlayBridge.State.Observe).Lines(3).WithId("visual.progress.observe");
            progress.Readout("단계", () => VisualPlayBridge.State.Phase + " · " + VisualPlayBridge.State.Detail).Lines(2).WithId("visual.progress.phase").Pinnable();
            progress.Progress("진행", () => new DebugProgress(VisualPlayBridge.State.Completed, VisualPlayBridge.State.Planned,
                $"{VisualPlayBridge.State.Completed}/{VisualPlayBridge.State.Planned} · 실패 {VisualPlayBridge.State.Failed} · 미실행 {VisualPlayBridge.State.Skipped}"))
                .WithId("visual.progress.count").Pinnable();
            progress.Buttons("조작")
                .Add(() => VisualPlayBridge.State.Paused || VisualPlayBridge.State.Waiting ? "계속" : "항목 뒤 멈춤", () => VisualPlayBridge.Control(VisualPlayControl.Pause))
                .Add("다시 보기", () => VisualPlayBridge.Control(VisualPlayControl.Replay))
                .Add("다음", () => VisualPlayBridge.Control(VisualPlayControl.Next))
                .WithId("visual.progress.controls").EnabledWhen(() => VisualPlayBridge.Running, "재생 중에 사용할 수 있어요");
            progress.Button("재생 중단", () => VisualPlayBridge.Control(VisualPlayControl.Stop))
                .WithId("visual.progress.stop").EnabledWhen(() => VisualPlayBridge.Running, "재생 중에 사용할 수 있어요");

            var result = DebugRegistry.Section(DebugTabs.VisualPlay, "개발자 확인", 40, "재생 완료와 화면 확인은 따로 기록해요");
            result.Text("메모", () => VisualPlayBridge.Note, value => VisualPlayBridge.Note = value).WithId("visual.result.note").NoPreset();
            result.Buttons("표시")
                .Add("확인함", () => VisualPlayBridge.Review(VisualPlayReview.Checked))
                .Add("문제 있음", () => VisualPlayBridge.Review(VisualPlayReview.Problem))
                .WithId("visual.result.review");
            result.Readout("이번 결과", () => VisualPlayBridge.State.Summary).Lines(3).WithId("visual.result.summary");
            result.Button("문제 표시한 항목 다시 보기", () => VisualPlayBridge.Start(VisualPlayScope.Problems))
                .WithId("visual.result.replayProblems").EnabledWhen(() => VisualPlayBridge.CanStart && VisualPlayBridge.State.Problems > 0, "문제 표시한 재생 기록이 필요해요");
            foreach (var item in select.Items) item.VisibleWhen(() => !VisualPlayBridge.Running);
            foreach (var item in progress.Items) item.VisibleWhen(() => VisualPlayBridge.Running);
            foreach (var item in settings.Items) item.VisibleWhen(() => !VisualPlayBridge.Running);
        }
    }
}
#endif
