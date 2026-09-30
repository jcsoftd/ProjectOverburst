using UnityEngine;

/// <summary>
/// 2026-10-01 ESC 메뉴의 "누구 ESC인가" 판정. 프레임 맨 앞에서(다른 창들이 ESC로 스스로 닫기 전에)
/// 게임 입력을 막고 있는 창·전환이 있었는지 기록한다. 있었다면 그 프레임의 ESC는 그 창 몫이고 메뉴는 열리지 않는다.
/// 인벤토리·장비창·창고·상점·런 창·포탈·디버그 창·로딩·정산이 모두 GameplayInputBlocker를 건다.
/// </summary>
[DefaultExecutionOrder(-2000)]
public sealed class OverburstGameMenuGate : MonoBehaviour
{
    public static bool EscapeBelongsToMenu { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => EscapeBelongsToMenu = false;

    private void Update()
    {
        var flow = PersistentSceneFlow.Instance;
        EscapeBelongsToMenu = !OverburstGameMenu.IsOpen
            && !GameplayInputBlocker.IsGameplayInputBlocked
            && (flow == null || !flow.IsSwitching);
    }
}
