#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Overburst.DebugTools;
using UnityEngine;

/// <summary>
/// 적·AI 탭의 제어(90C 7.3)와 스폰 탭의 대량 스폰(7.4). 분대 집계·테마 시험은 3단계 모듈이 같은 탭에 더한다.
/// 적 처치는 평소 피해 경로(CombatHealth.TakeDamage)를 타므로 경험치·드롭·사망 집계가 똑같이 생긴다.
/// </summary>
internal static class EnemyDebugModule
{
    internal enum KillRange { Near, All }

    private const float NearRadius = 20f;
    private static readonly List<EnemyRank> ranks = new List<EnemyRank>(256);
    private static KillRange range = KillRange.Near;
    private static bool skipBoss = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        ranks.Clear();
        range = KillRange.Near;
        skipBoss = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugSection control = DebugRegistry.Section(DebugTabs.Enemies, "제어", 40, "경험치·드롭이 평소처럼 생겨요");
        control.Choice("범위", () => range, value => range = value, value => value == KillRange.Near ? $"반경 {NearRadius:0}m" : "전체")
            .WithId("enemies.control.range")
            .NoPreset();
        control.Toggle("보스 제외", () => skipBoss, value => skipBoss = value)
            .WithId("enemies.control.skipBoss")
            .NoPreset();
        control.Button(() => $"적 모두 처치  ·  {CountTargets()}마리", KillAll)
            .WithId("enemies.control.killAll")
            .EnabledWhen(() => DebugTeleport.HasPlayer, "플레이어가 있는 씬에서만")
            .Keywords("kill", "처치", "clear");

        DebugSection spawn = DebugRegistry.Section(DebugTabs.Spawn, "대량 스폰", 20);
        spawn.Buttons("한 무리 소환")
            .Add("원형 30", () => Spawn(EnemyMassSpawnDebugPattern.Circle, 30))
            .Add("원형 100", () => Spawn(EnemyMassSpawnDebugPattern.Circle, 100))
            .Add("뭉침 100", () => Spawn(EnemyMassSpawnDebugPattern.Clustered, 100))
            .WithId("spawn.mass.spawn")
            .EnabledWhen(() => DebugTeleport.HasPlayer, "플레이어가 있는 씬에서만")
            .Keywords("spawn", "소환", "몬스터")
            .Tip("옛 HUD '대량 스폰' 버튼과 같은 EnemyMassSpawnDebugService다. 한 프레임에 20마리씩 나눠 만든다.");
    }

    private static DebugResult Spawn(EnemyMassSpawnDebugPattern pattern, int count)
    {
        return EnemyMassSpawnDebugService.RequestSpawn(pattern, count)
            ? DebugResult.Ok($"{count}마리 요청")
            : DebugResult.Fail("플레이어를 찾지 못했어요");
    }

    private static int CountTargets()
    {
        int count = 0;
        CollectTargets(null, ref count);
        return count;
    }

    private static DebugResult KillAll()
    {
        var targets = new List<CombatHealth>(64);
        int count = 0;
        CollectTargets(targets, ref count);
        if (targets.Count == 0)
            return DebugResult.Ok("대상 없음");
        int killed = 0;
        foreach (CombatHealth health in targets)
        {
            if (health == null || health.IsDead)
                continue;
            // 방어력 감쇠를 넘도록 넉넉히. source를 비워 플레이어 원소 에너지는 쌓이지 않는다.
            health.TakeDamage(new DamageInfo(health.CurrentHp * 50f + 100000f, health.transform.position,
                triggersOnHitEffects: false));
            if (health.IsDead)
                killed++;
        }
        return DebugResult.Ok($"{killed}/{targets.Count}마리 처치");
    }

    private static void CollectTargets(List<CombatHealth> output, ref int count)
    {
        Transform player = DebugTeleport.Player;
        EnemyRank.CollectActive(ranks);
        for (int i = 0; i < ranks.Count; i++)
        {
            EnemyRank rank = ranks[i];
            if (rank == null || (skipBoss && rank.GradeType == EnemyGradeType.Boss))
                continue;
            if (range == KillRange.Near && player != null
                && (rank.transform.position - player.position).sqrMagnitude > NearRadius * NearRadius)
                continue;
            CombatHealth health = rank.GetComponent<CombatHealth>();
            if (health == null)
                health = rank.GetComponentInChildren<CombatHealth>();
            if (health == null || health.IsDead)
                continue;
            count++;
            output?.Add(health);
        }
    }
}
#endif
