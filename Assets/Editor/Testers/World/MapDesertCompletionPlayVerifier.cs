using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Exercises the real generated desert, existing cinematic and saved completion transaction.
public static class MapDesertCompletionPlayVerifier
{
    static IEnumerator Frames(int n) { while (n-- > 0) yield return null; }
    static IEnumerator Wait(Func<bool> ready, Action<bool, string> check, string label, float seconds = 30)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!ready() && EditorApplication.timeSinceStartup < end) yield return null;
        check(ready(), label);
    }
    static void Hit(CombatHealth health, GameObject source, float damage = 1000000)
        => health.TakeDamage(new DamageInfo(damage, health.transform.position, source, suppressDefaultHitVfx: true));
    static float Distance(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return float.PositiveInfinity;
        float length = 0; var corners = path.corners;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return length;
    }
    public static IEnumerator Verify(RunWorldGate gate, int scenario, string output, Action<bool, string> check)
    {
        string label = "Desert boss case " + scenario;
        var flow = PersistentSceneFlow.Instance; var player = PlayerContext.Instance.CurrentActor;
        var driver = flow.GetComponent<RunLifetimeDriver>(); var account = AccountGameplaySession.Current;
        var completion = gate.Completion; var arena = gate.BossArena; var encounter = completion.Boss;
        var roster = gate.Roster; long bosses = account.Read().bossClearCount;
        string runId = account.ReadRun().runId;
        check(arena != null && (int)MapBossArena.TileSize(arena.Ground) == roster.Areas.Min(a => (int)MapBossArena.TileSize(a.Ground)),
            label + ": selected authored tile follows large then medium then small priority");
        check(!MapCombatGeometry.Contains(arena.Ground, player.transform.position), label + ": entry remains outside the completion platform");
        if (!arena.HasBoss)
        {
            check(!encounter && !completion.Portal && !completion.Reward, label + ": small-only desert has no boss or early completion");
            yield return Frames(10);
            check(account.ReadRun().phase == RunPhase.Active, label + ": small-only run stays active before platform entry");
            ActorTeleportUtility.TeleportSafely(player.transform, arena.PortalPoint + Vector3.up * .12f, Quaternion.identity);
            yield return Wait(() => completion.IsCompleted && completion.Portal && completion.Reward, check, label + ": entering small final platform creates guaranteed map and return portal");
            check(account.Read().bossClearCount == bosses && completion.CompletionArea == arena.Ground.Index
                && !UnityEngine.Object.FindObjectsByType<CrustaspikanEncounter>(FindObjectsSortMode.None).Any(),
                label + ": no boss actor, intro or boss-kill increment on small-only clear");
            check(completion.Reward.TryPickup(PlayerAccountInventoryService.SharedInventory), label + ": small-only guaranteed map pickup");
            string rewardId = account.ReadRun().completionReward.instanceId;
            completion.Poll(); yield return Frames(3);
            check(account.ReadRun().completionReward.instanceId == rewardId && account.Read().bossClearCount == bosses, label + ": repeated poll does not duplicate small-only reward");
            check(driver.RequestPortalExit(), label + ": small-only manual return accepted");
            yield return Wait(() => WorldSessionState.IsHideout && !flow.IsSwitching, check, label + ": small-only manual extraction reaches hub");
            check(account.ReadRun().phase == RunPhase.Extracted && account.Read().items.Any(i => i.instanceId == rewardId && string.IsNullOrEmpty(i.originRunId)), label + ": small-only reward extraction persisted");
            yield break;
        }
        GameObject blocker = null;
        try
        {
        check(arena != null && encounter && encounter.Brain == null && !encounter.MapEngaged && !encounter.IsIntroducing,
            label + ": boss actor and cinematic absent before room entry");
        check(!UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Any(a => a.IsLeased && a.gameObject.scene == gate.gameObject.scene && a.Definition.Grade.GradeType == EnemyGradeType.Boss),
            label + ": no active boss hidden elsewhere in scene");
        check(roster.Areas[arena.Ground.Index].Count == 0 && gate.SpawnBudget.Fields.Count == roster.Areas.Count,
            label + ": reserved boss room has no natural population and retains field indexing");
        check(roster.Areas.Where(a => MapBossArena.TileSize(a.Ground) == MapBossArena.TileSize(arena.Ground))
            .All(a => Distance(gate.EntryPoint.position, a.Ground.Center) <= arena.WalkDistance + .1f),
            label + ": room is farthest by actual shortest walk within the preferred tile size");
        check(arena.DodgePoints.Count >= 4 && MapCombatGeometry.CanPlace(arena.Ground, arena.Settings.materials.actorDefinition, arena.BossPoint)
            && float.IsFinite(Distance(arena.BossPoint, arena.PortalPoint)) && float.IsFinite(Distance(arena.BossPoint, arena.RewardPoint)),
            label + ": actual body, evasion points, reward and portal are reachable");
        check(!completion.Portal && !completion.Reward, label + ": no early completion reward");
        yield return Frames(10);
        check(encounter.Brain == null, label + ": boss does not spawn while player remains outside");
        blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Owned intro camera blocker fixture";
        UnityEngine.Object.DestroyImmediate(blocker.GetComponent<Collider>());
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(blocker, gate.gameObject.scene);
        blocker.transform.position = arena.BossPoint + Vector3.up * 8; blocker.transform.localScale = new Vector3(20, 20, 20);
        ActorTeleportUtility.TeleportSafely(player.transform, arena.DodgePoints[0] + Vector3.up * .12f, Quaternion.identity);
        yield return Wait(() => encounter.IsIntroducing, check, label + ": room entry starts existing entrance cinematic");
        var brain = encounter.Brain; var actor = brain.Actor; uint lease = actor.LeaseVersion;
        var cinematic = encounter.EntranceCinematic;
        yield return Wait(() => blocker.GetComponent<Renderer>().forceRenderingOff, check, label + ": live cinematic render callback hides foreground blocker");
        check(cinematic.HiddenOccluderCount > 0 && blocker.activeInHierarchy, label + ": intro hides renderer without deactivating scenery");
        check(encounter.MapEngaged && cinematic.ShotCamera && cinematic.HiddenCanvasCount > 0 && cinematic.VisualOffset.y < 0
            && GameplayInputBlocker.IsGameplayInputBlocked, label + ": boss emerges from underground with cinematic camera and input block");
        player.Health.SetDamageDeathPrevention(flow, false);
        float hp = player.Health.CurrentHp, bossHp = actor.Health.CurrentHp;
        Hit(player.Health, actor.gameObject); Hit(actor.Health, player.gameObject);
        check(player.Health.CurrentHp == hp && actor.Health.CurrentHp == bossHp && player.Health.IsDamageImmune && actor.Health.IsDamageImmune,
            label + ": input-locked cinematic protects both actors from damage");
        if (scenario == 2)
        {
            cinematic.Cancel();
            check(!blocker.GetComponent<Renderer>().forceRenderingOff && cinematic.HiddenOccluderCount == 0, label + ": cancellation restores foreground rendering");
            UnityEngine.Object.Destroy(blocker); blocker = null;
            check(!player.Health.IsDamageImmune && !actor.Health.IsDamageImmune && !GameplayInputBlocker.IsGameplayInputBlocked,
                label + ": cancellation releases protection and input immediately");
            driver.RequestAbandon();
            yield return Wait(() => WorldSessionState.IsHideout && !flow.IsSwitching, check, label + ": cancelled cinematic run returns to hub");
            check(account.ReadRun().phase == RunPhase.Failed && account.Read().bossClearCount == bosses && !player.Health.IsDamageImmune
                && !UnityEngine.Object.FindObjectsByType<CrustaspikanEntranceCinematic>(FindObjectsSortMode.None).Any(),
                label + ": teardown leaves no director, protection or clear reward");
            yield break;
        }
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "desert-entrance-" + scenario + ".png"));
        if (scenario == 1) cinematic.Skip();
        else
        {
            yield return Wait(() => cinematic.RoarStarted || !encounter.IsIntroducing, check, label + ": roar shot available", 35);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "desert-roar.png")); yield return Frames(2);
        }
        yield return Wait(() => !encounter.IsIntroducing, check, label + ": cinematic finishes or skips", 45);
        check(!blocker.GetComponent<Renderer>().forceRenderingOff && cinematic.HiddenOccluderCount == 0, label + ": finish or skip restores foreground rendering");
        UnityEngine.Object.Destroy(blocker); blocker = null;
        if (scenario == 0) yield return VerifyLighting(gate, output, check);
        check(cinematic.VisualRestored && !player.Health.IsDamageImmune && !actor.Health.IsDamageImmune
            && !GameplayInputBlocker.IsGameplayInputBlocked && encounter.BossHud, label + ": camera flow, visuals, HUD, input and damage restored");
        if (scenario == 0) check(cinematic.ArrivalStarted && cinematic.ImpactStarted && cinematic.RoarStarted && !cinematic.WasSkipped,
            label + ": authored emergence, impact and roar played to completion");
        else if (scenario == 1) check(cinematic.WasSkipped, label + ": skip uses the same restoration path");
        var rank = actor.GetComponent<EnemyRank>(); var definition = actor.Definition;
        float expectedHp = definition.ReferenceHealthCoefficient > 0
            ? CombatBalanceFormulas.EnemyReferenceHealth(definition.ReferenceHealthCoefficient, gate.Map.level) * 1.5f
            : definition.ResolveRuntimeStats().MaxHealth * 1.5f * OverburstGrowthRules.EnemyHealthFactor(gate.Map.level);
        check(rank.Level == gate.Map.level && rank.Encounter == gate.Context && Mathf.Abs(actor.Health.MaxHp - expectedHp) < 1f,
            label + ": boss preserves map level and health affix without trial HP override");
        player.Health.SetDamageDeathPrevention(flow, true);
        Hit(player.Health, actor.gameObject, 1);
        check(player.Health.CurrentHp < hp, label + ": normal incoming damage resumes after cinematic");
        ActorTeleportUtility.TeleportSafely(player.transform, gate.EntryPoint.position, Quaternion.identity);
        yield return Frames(2);
        ActorTeleportUtility.TeleportSafely(player.transform, arena.DodgePoints[0] + Vector3.up * .12f, Quaternion.identity);
        yield return Frames(3);
        check(encounter.Brain == brain && actor.LeaseVersion == lease && encounter.EntranceCinematic == cinematic && !encounter.IsIntroducing,
            label + ": reentering room does not recreate boss or replay intro");
        if (scenario == 0)
        {
            yield return Wait(() => brain.PatternCount > 0, check, label + ": production boss starts its real combat patterns", 35);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "desert-combat.png")); yield return Frames(2);
            var landed = new List<EnemyActor>(); Action<EnemyActor> collect = add => landed.Add(add);
            brain.Composite.MonsterLanded += collect;
            try
            {
                var pattern = encounter.Settings.patterns.First(p => p.steps.Any(s => s.materialOrMotion == "SpitterShot1" || s.materialOrMotion == "SpitterShot2"));
                check(brain.StartPatternForReview(pattern.id), label + ": real summon pattern starts for reward inspection");
                yield return Wait(() => landed.Any(a => a && a.IsLeased && !a.Health.IsDead), check, label + ": authored boss summon lands", 35);
                var add = landed.First(a => a && a.IsLeased && !a.Health.IsDead); var addRank = add.GetComponent<EnemyRank>();
                check(addRank.Level == gate.Map.level && !addRank.Encounter.CanGrantRewards && !addRank.Encounter.RewardsAllowed,
                    label + ": summon inherits level but has no reward permission");
                PlayerProgression.Current.FlushPendingExperience();
                int xp = account.Read().experience, level = account.Read().level, kills = roster.Kills;
                int drops = UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length;
                Hit(add.Health, player.gameObject); PlayerProgression.Current.FlushPendingExperience();
                yield return Frames(3);
                check(account.Read().experience == xp && account.Read().level == level && roster.Kills == kills
                    && UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length == drops,
                    label + ": killed summon grants no XP, natural kill count or floor items");
            }
            finally { brain.Composite.MonsterLanded -= collect; }
        }
        check(roster.Kills == 0, label + ": boss objective does not require natural monster kills");
        Hit(actor.Health, player.gameObject);
        check(encounter.Defeated && completion.CompletionArea == arena.Ground.Index, label + ": actual boss death requests room completion");
        if (scenario == 1)
        {
            player.Health.SetDamageDeathPrevention(flow, false); Hit(player.Health, actor.gameObject);
            yield return Wait(() => WorldSessionState.IsHideout && !flow.IsSwitching, check, label + ": same-frame boss kill and player death settle");
            check(account.ReadRun().phase == RunPhase.Failed && account.ReadRun().completionReward == null && account.Read().bossClearCount == bosses,
                label + ": simultaneous death wins over uncommitted boss completion");
            yield break;
        }
        yield return Wait(() => account.ReadRun().phase == RunPhase.Completed && completion.Portal && completion.Reward,
            check, label + ": boss clear creates one guaranteed map and manual return portal");
        check(account.Read().bossClearCount == bosses + 1 && roster.Kills == 0 && roster.Pending + roster.Active == roster.Total
            && Vector3.Distance(completion.Portal.transform.position, arena.PortalPoint) < .1f && encounter.AliveAdds == 0,
            label + ": boss clear counted once, summons removed, natural population retained, reserved portal used");
        string rewardId = account.ReadRun().completionReward.instanceId;
        check(completion.Reward.TryPickup(PlayerAccountInventoryService.SharedInventory), label + ": guaranteed map picked up");
        yield return Frames(3);
        driver.Poll(account.ReadRun().completedAtUtcTicks + TimeSpan.TicksPerDay);
        check(account.ReadRun().phase == RunPhase.Completed && account.Read().bossClearCount == bosses + 1,
            label + ": no timed extraction or repeated clear increment");
        File.WriteAllText(Path.Combine(output, "desert-completion.json"), JsonConvert.SerializeObject(new
        { runId, level = rank.Level, hp = actor.Health.MaxHp, arena = arena.Ground.Index, arena.WalkDistance, roster.Total, roster.Kills,
            patterns = brain.PatternCount, summons = encounter.TotalAddsSpawned, rewardId, phase = account.ReadRun().phase.ToString(), completionArea = account.ReadRun().completionArea, clearedAt = account.ReadRun().completedAtUtcTicks }, Formatting.Indented));
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "desert-completion.png")); yield return Frames(3);
        check(driver.RequestPortalExit(), label + ": manual return accepted");
        yield return Wait(() => WorldSessionState.IsHideout && !flow.IsSwitching, check, label + ": manual extraction returns to hub");
        check(account.ReadRun().phase == RunPhase.Extracted && account.Read().items.Any(i => i.instanceId == rewardId && string.IsNullOrEmpty(i.originRunId))
            && account.Read().bossClearCount == bosses + 1 && !player.Health.IsDamageImmune && !GameplayInputBlocker.IsGameplayInputBlocked,
            label + ": reward retained and presentation protection fully released after extraction");
        }
        finally { if (blocker) UnityEngine.Object.Destroy(blocker); }
    }
    static IEnumerator VerifyLighting(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var world = gate.GetComponent<Overburst.Mojave.DesertRunWorld>();
        var sun = gate.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>())
            .Where(l => l.enabled && l.type == LightType.Directional).OrderByDescending(l => l.intensity).First();
        var expected = Quaternion.Euler(0, QuarterViewCamera.ActiveInstance.CurrentYaw, 0) * new Vector3(-.65f, 1.15f, -1f).normalized;
        check(Vector3.Dot(-sun.transform.forward, expected) > .999f && sun.shadowStrength <= .6801f, "Desert sun comes from camera-side lower left and keeps softer shadows");
        float scale = Time.timeScale, intensity = sun.intensity; Color color = sun.color; Quaternion adjusted = sun.transform.rotation;
        try
        {
            Time.timeScale = 0; yield return Frames(4);
            world.enabled = false; yield return Frames(3);
            check(Quaternion.Angle(sun.transform.rotation, adjusted) > 1f, "Disabling run lighting restores original authored sun");
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "desert-light-before.png")); yield return Frames(4);
            world.enabled = true; yield return Frames(4);
            check(Quaternion.Angle(sun.transform.rotation, adjusted) < .1f && sun.intensity == intensity && sun.color == color,
                "Re-enabling restores camera-relative direction without changing sun intensity or color");
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "desert-light-after.png")); yield return Frames(4);
        }
        finally { world.enabled = true; Time.timeScale = scale; }
    }
}
