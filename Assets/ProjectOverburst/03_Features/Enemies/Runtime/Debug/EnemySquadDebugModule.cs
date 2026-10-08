#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Overburst.DebugTools;
using UnityEngine;

/// <summary>
/// 3단계(90C 8.1·8.3): 옛 분대 오버레이를 실시간 값·분포 막대로 나누고, 적 테마 시험을 공통 부품으로 다시 그린다.
/// 실시간 값 ID는 DebugPrefs.DefaultPins와 같아서 처음 실행하면 옛 오버레이와 같은 값이 핀 오버레이에 뜬다.
/// </summary>
internal static class EnemySquadDebugModule
{
    private static readonly Color PursuitColor = new Color(0.33f, 0.55f, 0.95f);
    private static readonly Color RushColor = new Color(0.9f, 0.36f, 0.33f);
    private static readonly Color NearColor = new Color(0.95f, 0.62f, 0.26f);
    private static readonly Color ReserveColor = new Color(0.45f, 0.78f, 0.9f);
    private static readonly Color RemnantColor = new Color(0.55f, 0.58f, 0.64f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugSection live = DebugRegistry.Section(DebugTabs.Enemies, "실시간 집계", 10, "핀을 누르면 창을 닫아도 보여요");
        live.Readout("총 몬스터", () => EnemyAIController.AliveEnemyCount.ToString())
            .WithId("enemies.live.total")
            .Pinnable();
        live.Readout("어그로 몬스터", () => EnemyAIController.AggroEnemyCount.ToString())
            .WithId("enemies.live.aggro")
            .Pinnable();
        live.Readout("세션 사망 · 이탈 기준", () =>
                $"{EnemyAIController.SessionMonsterDeathCount} · {EnemyAIController.CurrentCombatLoseTargetRange:0}m")
            .WithId("enemies.live.deaths")
            .Pinnable();

        DebugSection squad = DebugRegistry.Section(DebugTabs.Enemies, "부대 시스템", 20);
        squad.Readout("상태", SquadStateText)
            .WithId("enemies.squad.state")
            .Pinnable();
        squad.Bar("역할 분포", SquadRoles)
            .WithId("enemies.squad.roles")
            .Pinnable()
            .VisibleWhen(() => EnemySquadPursuitRuntimeService.GetRuntimeStats().IsActive);

        RegisterThemeTrial();
    }

    /// <summary>옛 HUD 분대 오버레이(2026-10-01 삭제)와 같은 문구.</summary>
    private static string SquadStateText()
    {
        EnemySquadPursuitRuntimeStats stats = EnemySquadPursuitRuntimeService.GetRuntimeStats();
        return stats.IsActive
            ? $"켜짐 · 구역 {stats.ActiveEncounterCount} · 부대 {stats.SquadCount}"
            : $"대기 {stats.CombatEligibleAgentCount}/{stats.ActivationCount} · 등록 {stats.RegisteredAgentCount}";
    }

    private static DebugBarSegment[] SquadRoles()
    {
        EnemySquadPursuitRuntimeStats stats = EnemySquadPursuitRuntimeService.GetRuntimeStats();
        return new[]
        {
            new DebugBarSegment("추격", stats.PursuitCount, PursuitColor),
            new DebugBarSegment("러쉬", stats.RushCount, RushColor),
            new DebugBarSegment("근접", stats.NearCombatCount, NearColor),
            new DebugBarSegment("예비", stats.ReserveCount, ReserveColor),
            new DebugBarSegment("잔존", stats.RemnantCount, RemnantColor),
        };
    }

    private static void RegisterThemeTrial()
    {
        DebugSection s = DebugRegistry.Section(DebugTabs.Spawn, "적 테마 시험", 10,
            $"게임 편성 {EnemyThemeTrialService.Entries.Count}테마 · 최신 몬스터");
        s.Choice("규모", () => EnemyThemeTrialService.Mode, value => EnemyThemeTrialService.SetMode(value),
                EnemyThemeTrialPresets.Label)
            .WithId("spawn.theme.mode")
            .NoPreset()
            .EnabledWhen(() => !EnemyThemeTrialService.Busy, "진행 중인 시험을 먼저 정리하세요");
        foreach (EnemyThemeTrialEntry entry in EnemyThemeTrialService.Entries)
        {
            EnemyThemeTrialEntry current = entry;
            s.Buttons(current.ShortName)
                .Add(() => $"{EnemyThemeTrialService.CountOf(current)}마리", () => EnemyThemeTrialService.Begin(current, false))
                .Add(() => $"{EnemyThemeTrialService.CountOf(current)}×3", () => EnemyThemeTrialService.Begin(current, true))
                .WithId("spawn.theme." + current.Id)
                .EnabledWhen(() => EnemyThemeTrialService.HasPlayer && !EnemyThemeTrialService.Busy,
                    "진행 중인 시험을 먼저 정리하세요")
                .Tip(current.Table.DisplayName + $" · {current.Table.Entries.Count}종 · 왼쪽 1회, 오른쪽 3회 공세");
        }
        s.Progress("진행", () => EnemyThemeTrialService.Progress)
            .WithId("spawn.theme.progress")
            .Pinnable();
        s.Buttons("시험")
            .Add("정리 / 공세 중지", EnemyThemeTrialService.Clear)
            .Add(() => EnemyThemeTrialService.InArena ? "하이드아웃으로 돌아가기" : "독립 시험장 입장", EnemyThemeTrialService.ToggleArena)
            .WithId("spawn.theme.actions")
            .EnabledWhen(() => EnemyThemeTrialService.HasPlayer, "플레이어가 있는 씬에서만")
            .Tip("시험장은 하이드아웃에서만. 들어가면 하이드아웃 몬스터 스폰을 끄고 최소 체력 1로 보호하며, 나오면 되돌린다.");
    }
}
#endif
