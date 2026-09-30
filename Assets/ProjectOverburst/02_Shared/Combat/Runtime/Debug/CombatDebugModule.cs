#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools;
using UnityEngine;

/// <summary>
/// <see cref="CombatDebugSettings"/>의 디버그 토글 5개를 디버그 창에 등록한다(90C 7.1~7.4).
/// 값은 계속 CombatDebugSettings가 소유한다. 옛 HUD 버튼은 4단계에서 지울 때까지 나란히 둔다.
/// </summary>
internal static class CombatDebugModule
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugRegistry.Section(DebugTabs.Player, "생존", 10)
            .Toggle("받는 피해 99.9% 감소",
                () => CombatDebugSettings.ReduceIncomingPlayerDamageBy99_9Percent,
                CombatDebugSettings.SetPlayerDamageReductionDebug)
            .WithId("player.survival.damageReduction")
            .Keywords("무적", "god", "피해")
            .Tip("플레이어가 받는 피해를 1000분의 1로 줄인다.");

        DebugRegistry.Section(DebugTabs.Combat, "표시", 10)
            .Toggle("공격 판정창",
                () => CombatDebugSettings.ShowAttackPatternDebug,
                CombatDebugSettings.SetAttackPatternDebug)
            .WithId("combat.display.attackPattern")
            .Keywords("hitbox", "판정")
            .Tip("플레이어 공격의 판정 범위를 그린다.");

        DebugSection visual = DebugRegistry.Section(DebugTabs.Enemies, "시각화", 30);
        visual.Toggle("AI 상태 글자",
                () => CombatDebugSettings.ShowEnemyAiStateDebug,
                CombatDebugSettings.SetEnemyAiStateDebug)
            .WithId("enemies.visual.aiState")
            .Keywords("ai", "상태")
            .Tip("적 체력 바 위에 AI 상태를 글자로 띄운다.");
        visual.Toggle("부대 형상",
                () => CombatDebugSettings.ShowEnemySquadGeometryDebug,
                CombatDebugSettings.SetEnemySquadGeometryDebug)
            .WithId("enemies.visual.squadGeometry")
            .Keywords("분대", "squad")
            .Tip("분대 추격 구역과 부대 배치를 그린다.");

        DebugRegistry.Section(DebugTabs.Spawn, "대량 스폰", 20)
            .Toggle("하이드아웃 몬스터 스폰",
                () => CombatDebugSettings.SpawnHideoutMonsters,
                CombatDebugSettings.SetHideoutMonsterSpawn)
            .WithId("spawn.mass.hideoutMonsters")
            .Keywords("spawn", "몬스터")
            .Tip("하이드아웃에 몬스터가 나오게 한다.");

        DebugPresets.RegisterBuiltIn("전투 테스트",
            ("player.survival.damageReduction", "1"),
            ("combat.display.attackPattern", "1"),
            ("enemies.visual.aiState", "1"));
    }
}
#endif
