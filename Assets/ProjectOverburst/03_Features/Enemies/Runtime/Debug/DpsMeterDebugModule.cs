#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Overburst.DebugTools;
using UnityEngine;

/// <summary>
/// DPS 측정기(사용자 요청 2026-10-01): 허수아비 1마리·10마리를 불러 실제로 들어간 피해(CombatHealth.OnDamageResolved의 실피해)를 잰다.
/// 허수아비는 테마 몬스터를 스폰 서비스로 불러 AI를 끄고 체력을 크게 잡은 것이라, 몬스터 방어력·등급 보정이 실전과 같게 적용된다.
/// 시간은 실제 초(unscaled)로 잰다. 히트스톱으로 느려진 만큼 DPS도 낮게 나온다.
/// </summary>
public static class DpsMeterDebugModule
{
    internal enum DummyTier { Small, Medium, Elite }

    private const float DummyHp = 10000000f;
    private const float IdleFreezeSeconds = 3f;
    private static readonly string[] TableIds = { "SpiderBrood", "VenomBrood", "PrimalHunt", "CavernMutants", "DeathHarvest" };

    private static readonly List<EnemyActor> dummies = new List<EnemyActor>();
    private static readonly Queue<(float time, float damage)> window = new Queue<(float time, float damage)>();
    private static readonly HashSet<CombatHealth> hitTargets = new HashSet<CombatHealth>();
    private static EnemySpawnService spawnService;
    private static int dummyCount = 1;
    private static DummyTier tier = DummyTier.Medium;
    private static float firstHit = -1f;
    private static float lastHit = -1f;
    private static double total;
    private static int hits;
    private static int crits;
    private static float recent;
    private static float peak;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        dummies.Clear();
        spawnService = null;
        dummyCount = 1;
        tier = DummyTier.Medium;
        ResetMeasure();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugSection s = DebugRegistry.Section(DebugTabs.Combat, "DPS 측정", 30, "허수아비는 공격하지 않고 죽지 않아요");
        s.Choice("허수아비 수", () => dummyCount, value => dummyCount = value, value => value + "마리", new[] { 1, 10 })
            .WithId("combat.dps.count")
            .NoPreset();
        s.Choice("허수아비 등급", () => tier, value => tier = value, TierLabel)
            .WithId("combat.dps.tier")
            .NoPreset()
            .Tip("테마 표의 소형·중형·정예 몬스터를 쓴다. 등급별 방어력 보정이 실전처럼 적용된다.");
        s.Buttons("허수아비")
            .Add(() => $"{dummyCount}마리 소환", Spawn)
            .Add("치우기", Despawn)
            .WithId("combat.dps.spawn")
            .EnabledWhen(() => DebugTeleport.HasPlayer, "플레이어가 있는 씬에서만")
            .Keywords("dps", "허수아비", "dummy", "딜");
        s.Button("측정 초기화", () =>
            {
                ResetMeasure();
                return DebugResult.Ok();
            })
            .WithId("combat.dps.reset");
        s.Readout("평균 DPS", AverageText)
            .WithId("combat.dps.value")
            .Pinnable()
            .Keywords("dps");
        s.Readout("최근 1초 · 최고", RecentText)
            .WithId("combat.dps.recent")
            .Pinnable();
        s.Readout("누적", TotalText)
            .Lines(2)
            .WithId("combat.dps.total")
            .Pinnable();
    }

    public static int AliveDummies
    {
        get
        {
            int count = 0;
            foreach (EnemyActor dummy in dummies)
            {
                if (dummy != null && dummy.IsLeased)
                    count++;
            }
            return count;
        }
    }

    public static double TotalDamage => total;
    public static int Hits => hits;

    private static DebugResult Spawn()
    {
        Transform player = DebugTeleport.Player;
        if (player == null)
            return DebugResult.Fail("플레이어가 없어요");
        Despawn();
        if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player, out spawnService))
            return DebugResult.Fail("스폰 서비스를 찾지 못했어요");
        EnemyDefinition definition = PickDefinition(out EnemyThemeTable table);
        if (definition == null)
            return DebugResult.Fail(TierLabel(tier) + " 몬스터 정의가 없어요");
        spawnService.RegisterAdditionalCatalog(table.Catalog, out _);

        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;
        forward.Normalize();
        Vector3 center = player.position + forward * (dummyCount == 1 ? 4f : 6f);
        for (int i = 0; i < dummyCount; i++)
        {
            Vector3 at = center;
            if (dummyCount > 1)
            {
                // 광역기가 한 번에 닿도록 반지름 2.2m 원에 붙여 세운다.
                float angle = i * Mathf.PI * 2f / dummyCount;
                at += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.2f;
            }
            Vector3 look = player.position - at;
            look.y = 0f;
            if (!spawnService.TrySpawn(new EnemySpawnRequest(definition, at, Quaternion.LookRotation(look), player), out EnemyActor actor))
                continue;
            if (actor.AI != null)
                actor.AI.enabled = false;
            actor.Movement?.StopMovement();
            actor.Health.SetMaxHp(DummyHp, true);
            actor.Health.OnDamageResolved += Record;
            dummies.Add(actor);
        }
        ResetMeasure();
        EnsureRunner();
        return dummies.Count == dummyCount
            ? DebugResult.Ok($"{definition.DisplayName} {dummies.Count}마리")
            : DebugResult.Fail($"{dummies.Count}/{dummyCount}마리만 소환했어요(자리 부족 등)");
    }

    private static DebugResult Despawn()
    {
        int released = 0;
        foreach (EnemyActor dummy in dummies)
        {
            if (dummy == null)
                continue;
            if (dummy.Health != null)
                dummy.Health.OnDamageResolved -= Record;
            if (dummy.IsLeased && spawnService != null)
            {
                spawnService.Release(dummy);
                released++;
            }
        }
        dummies.Clear();
        return DebugResult.Ok($"{released}마리 치움");
    }

    private static EnemyDefinition PickDefinition(out EnemyThemeTable table)
    {
        var wanted = tier == DummyTier.Small ? EnemyThemeTier.Small
            : tier == DummyTier.Medium ? EnemyThemeTier.Medium
            : EnemyThemeTier.Elite;
        foreach (string id in TableIds)
        {
            table = Resources.Load<EnemyThemeTable>("Enemies/Themes/Tables/" + id);
            if (table == null)
                continue;
            foreach (EnemyThemeTable.Entry entry in table.Entries)
            {
                if (entry.definition != null && entry.tier == wanted)
                    return entry.definition;
            }
        }
        table = null;
        return null;
    }

    private static void Record(CombatHealth health, DamageInfo info, float actual, bool died)
    {
        if (actual <= 0f)
            return;
        float now = Time.unscaledTime;
        if (firstHit < 0f)
            firstHit = now;
        lastHit = now;
        total += actual;
        hits++;
        if (info.isCritical)
            crits++;
        window.Enqueue((now, actual));
        hitTargets.Add(health);
        // 죽지 않게 체력을 되채운다(최대 체력의 20% 아래면).
        if (health.CurrentHp < health.MaxHp * 0.2f)
            health.ResetHealth();
    }

    internal static void Tick()
    {
        float now = Time.unscaledTime;
        while (window.Count > 0 && now - window.Peek().time > 1f)
            window.Dequeue();
        float sum = 0f;
        foreach ((float _, float damage) in window)
            sum += damage;
        recent = sum;
        if (recent > peak)
            peak = recent;
    }

    private static float Elapsed
    {
        get
        {
            if (firstHit < 0f)
                return 0f;
            float end = Time.unscaledTime - lastHit > IdleFreezeSeconds ? lastHit : Time.unscaledTime;
            return Mathf.Max(0.001f, end - firstHit);
        }
    }

    private static string AverageText()
    {
        if (hits == 0)
            return AliveDummies > 0 ? "공격하면 측정 시작" : "허수아비를 소환하세요";
        return ((float)(total / Mathf.Max(0.001f, Elapsed))).ToString("N0");
    }

    private static string RecentText() => hits == 0 ? "-" : $"{recent:N0} · 최고 {peak:N0}";

    private static string TotalText()
    {
        if (hits == 0)
            return "-";
        string idle = Time.unscaledTime - lastHit > IdleFreezeSeconds ? " (멈춤)" : string.Empty;
        return $"{total:N0} · {hits}타 · 치명 {crits * 100f / hits:0}% · {Elapsed:0.0}초{idle}\n맞은 대상 {hitTargets.Count}/{Mathf.Max(1, AliveDummies)}";
    }

    private static void ResetMeasure()
    {
        window.Clear();
        hitTargets.Clear();
        firstHit = -1f;
        lastHit = -1f;
        total = 0;
        hits = 0;
        crits = 0;
        recent = 0f;
        peak = 0f;
    }

    private static string TierLabel(DummyTier value)
        => value == DummyTier.Small ? "소형" : value == DummyTier.Medium ? "중형" : "정예";

    private static void EnsureRunner()
    {
        GameObject host = DebugHub.Host;
        if (host != null && !host.TryGetComponent(out DpsMeterRunner _))
            host.AddComponent<DpsMeterRunner>();
    }

    internal static void Shutdown() => Despawn();
}

/// <summary>디버그 창 Host에 붙어 최근 1초 DPS를 갱신한다.</summary>
public sealed class DpsMeterRunner : MonoBehaviour
{
    private void Update() => DpsMeterDebugModule.Tick();
    private void OnDestroy() => DpsMeterDebugModule.Shutdown();
}
#endif
