using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

public static class MapCaveProgressionPlayVerifier
{
    static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
    static IEnumerator Wait(Func<bool> condition, Action<bool, string> check, string label)
    {
        double until = EditorApplication.timeSinceStartup + 35;
        while (!condition() && EditorApplication.timeSinceStartup < until) yield return null;
        check(condition(), label);
    }
    public static IEnumerator Verify(RunWorldGate gate, Action<bool, string> check)
    {
        yield return Frames(8);
        var roster = gate.Roster; var budget = gate.SpawnBudget; var player = PlayerContext.Instance.CurrentActor;
        var active = new List<(MapRunRoster.Member member, EnemyActor actor)>();
        foreach (var member in roster.Members)
            if (budget.Fields[member.AreaIndex].TryGetActor(member.Index, out var actor) && !actor.Health.IsDead) active.Add((member, actor));
        check(active.Count >= 3, "Progression has three live natural members in " + roster.Terrain);
        var survivor = active.First(a => roster.Terrain == MapTerrainKind.Desert || !budget.InView(a.actor.transform.position, .12f));
        var victims = active.Where(a => a.member != survivor.member).Take(2).ToArray();
        var owner = budget.Fields[survivor.member.AreaIndex]; var enemy = survivor.actor;
        float baseMax = enemy.Health.MaxHp, baseDamage = enemy.RuntimeStats.DamageMultiplier;
        var baseStats = enemy.RuntimeStats;
        var resolveDamage = typeof(EnemyMeleeAttackController).GetMethod("ResolveIncomingDamage", BindingFlags.Instance | BindingFlags.NonPublic);
        float MeleeDamage(EnemyActor actor) => (float)resolveDamage.Invoke(actor.Melee, new object[] { null, actor.GetComponent<EnemyRank>().Level });
        float baseMelee = MeleeDamage(enemy);
        check(roster.Kills == 0 && enemy.NaturalProgressMultiplier == 1f, "Natural actors start without progression bonus in " + roster.Terrain);
        enemy.Health.RestoreLivingHealth(baseMax * .4f);
        void Kill(EnemyActor victim) => victim.Health.TakeDamage(new DamageInfo(1000000, victim.transform.position, player.gameObject, suppressDefaultHitVfx: true));
        Kill(victims[0].actor);
        float Expected() => roster.Terrain == MapTerrainKind.Cave ? 1f + .15f * roster.Kills / roster.Target : 1f;
        check(roster.Kills == 1, "Only one real natural death advances progression in " + roster.Terrain);
        check(Mathf.Abs(enemy.Health.MaxHp / baseMax - Expected()) < .00001f && Mathf.Abs(enemy.Health.CurrentHp / enemy.Health.MaxHp - .4f) < .00001f,
            "Existing damaged actor gains max HP while retaining its health fraction in " + roster.Terrain);
        check(Mathf.Abs(enemy.RuntimeStats.DamageMultiplier / baseDamage - Expected()) < .00001f && Mathf.Abs(MeleeDamage(enemy) / baseMelee - Expected()) < .00001f,
            "Runtime ability damage and actual melee damage resolver use the same single factor in " + roster.Terrain);
        check(enemy.RuntimeStats.MoveSpeedMultiplier == baseStats.MoveSpeedMultiplier && enemy.RuntimeStats.AttackSpeedMultiplier == baseStats.AttackSpeedMultiplier
            && enemy.RuntimeStats.AttackInterval == baseStats.AttackInterval, "Progression leaves movement and attack timing unchanged in " + roster.Terrain);
        foreach (var field in budget.Fields)
        {
            var actors = new List<EnemyActor>(); field.CollectLivingActors(actors);
            check(actors.All(a => Mathf.Abs(a.NaturalProgressMultiplier - Expected()) < .00001f), "All active areas share progression: " + roster.Terrain + "/" + field.FiniteArea.Ground.Index);
        }
        if (roster.Terrain == MapTerrainKind.Desert)
        {
            check(roster.Kills == 1, "Desert remains unscaled after one real natural death");
            yield break;
        }
        var limit = typeof(MapSpawnBudget).GetField("<AliveLimit>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        int previousLimit = budget.AliveLimit, lease = survivor.member.Lease;
        try
        {
            limit.SetValue(budget, 0); EnemySpawnService.Current.Release(enemy);
            yield return Wait(() => survivor.member.State == MapRunRoster.Life.Pending, check, "Returned living ID remains pending without a kill in " + roster.Terrain);
            check(!enemy.IsLeased && enemy.NaturalProgressMultiplier == 1f, "Returned actor clears progression before another lease in " + roster.Terrain);
            Kill(victims[1].actor);
            check(roster.Kills == 2 && survivor.member.State == MapRunRoster.Life.Pending, "A further real death advances while the damaged member is dormant in " + roster.Terrain);
        }
        finally { limit.SetValue(budget, previousLimit); }
        yield return Wait(() => survivor.member.Lease > lease && owner.TryGetActor(survivor.member.Index, out enemy), check, "Dormant ID is leased again in " + roster.Terrain);
        check(Mathf.Abs(enemy.Health.MaxHp / baseMax - Expected()) < .00001f && Mathf.Abs(enemy.Health.CurrentHp / enemy.Health.MaxHp - .4f) < .00001f,
            "Recreated actor restores damaged health fraction at the current max HP in " + roster.Terrain);
        check(Mathf.Abs(enemy.RuntimeStats.DamageMultiplier / baseDamage - Expected()) < .00001f && Mathf.Abs(MeleeDamage(enemy) / baseMelee - Expected()) < .00001f,
            "Recreated actor gets the current damage factor once in " + roster.Terrain);
        check(roster.Kills == 2, "Streaming and progression adjustment add no deaths in " + roster.Terrain);
        if (roster.Terrain == MapTerrainKind.Cave)
        {
            // Four-room preparation may already create every initial member. Exercise an actual reserve wave instead.
            var late = roster.Members.First(m => !m.Initial && m.State == MapRunRoster.Life.Pending && m.Lease == 0 && m.AreaIndex != survivor.member.AreaIndex);
            var field = budget.Fields[late.AreaIndex];
            var uncreated = roster.Members.Where(m => m.AreaIndex == late.AreaIndex && m.State == MapRunRoster.Life.Pending && m.Lease == 0).Select(m => m.Index).ToHashSet();
            ActorTeleportUtility.TeleportSafely(player.transform, field.FiniteArea.Ground.Center + Vector3.up * .12f, Quaternion.identity);
            var clearing = new List<EnemyActor>(); field.CollectLivingActors(clearing);
            foreach (var actor in clearing) Kill(actor);
            int killsAfterClearing = roster.Kills;
            EnemyActor spawned = null;
            bool FindNewSpawn()
            {
                foreach (int id in uncreated)
                    if (field.TryGetActor(id, out var candidate)) { spawned = candidate; return true; }
                return false;
            }
            yield return Wait(FindNewSpawn, check, "Previously uncreated reserve joins through the normal reinforcement wave");
            check(spawned.NaturalProgressMultiplier == roster.NaturalProgressMultiplier && spawned.Health.CurrentHp == spawned.Health.MaxHp,
                "Late first spawn starts at full current max HP and current damage factor");
            check(roster.Kills == killsAfterClearing, "Reinforcement creation adds no deaths");
        }
    }
}
