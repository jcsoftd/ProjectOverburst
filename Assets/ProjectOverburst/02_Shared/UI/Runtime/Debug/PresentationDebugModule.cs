#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools;
using UnityEngine;

/// <summary>UI·연출 탭의 화면 도구(90C 7.7). 데미지 숫자는 3단계 모듈이 같은 탭에 더한다.</summary>
internal static class PresentationDebugModule
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugSection screen = DebugRegistry.Section(DebugTabs.Presentation, "화면", 30);
        screen.Buttons("초기화")
            .Add("게임 창 배치", ResetGameWindows)
            .Add("카메라 줌", ResetZoom)
            .WithId("presentation.screen.reset")
            .Tip("인벤토리·장비창·창고 위치(OverburstUIWorkshop.ResetLayout)와 쿼터뷰 카메라 줌을 기본으로 돌린다.");
    }

    private static DebugResult ResetGameWindows()
    {
        OverburstUIWorkshop workshop = Object.FindFirstObjectByType<OverburstUIWorkshop>(FindObjectsInactive.Include);
        if (workshop == null)
            return DebugResult.Fail("게임 창 관리자가 없어요");
        workshop.ResetLayout();
        return DebugResult.Ok();
    }

    private static DebugResult ResetZoom()
    {
        QuarterViewCamera camera = Object.FindFirstObjectByType<QuarterViewCamera>();
        if (camera == null)
            return DebugResult.Fail("쿼터뷰 카메라가 없어요");
        camera.ResetZoom();
        return DebugResult.Ok();
    }
}
#endif
