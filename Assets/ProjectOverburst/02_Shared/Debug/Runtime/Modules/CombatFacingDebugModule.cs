#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Overburst.DebugTools
{
    internal static class CombatFacingDebugModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            DebugRegistry.Section(DebugTabs.Player, "방향 표시 시안", 85)
                .Toggle("방향 표시 VFX", () => PlayerCombatFacingVfx.DebugVisible, PlayerCombatFacingVfx.SetDebugVisible)
                .WithId("player.presentation.combatFacingVfx")
                .NoPreset()
                .Keywords("방향", "facing", "vfx", "촬영")
                .Tip("기본 꺼짐. 켰을 때만 전투 중 은백 방향 표시가 나온다. 끄면 즉시 사라지고 새 Play에서는 꺼짐으로 시작한다.");
        }
    }
}
#endif
