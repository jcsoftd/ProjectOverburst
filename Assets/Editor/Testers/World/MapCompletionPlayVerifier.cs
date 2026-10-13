using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class MapCompletionPlayVerifier
{
    static IEnumerator Frames(int n) { while (n-- > 0) yield return null; }
    static IEnumerator Wait(Func<bool> condition, Action<bool, string> check, string message, float seconds = 30)
    {
        double until = EditorApplication.timeSinceStartup + seconds;
        while (!condition() && EditorApplication.timeSinceStartup < until) yield return null;
        check(condition(), message);
    }
    static EnemyActor Living(RunWorldGate gate) => gate.Roster.Members.Where(m => m.State == MapRunRoster.Life.Active)
        .Select(m => gate.SpawnBudget.Fields[m.AreaIndex].TryGetActor(m.Index, out var actor) ? actor : null).FirstOrDefault(a => a);
    public static IEnumerator ConfirmDeathReturn(RunLifetimeDriver driver, Action<bool, string> check, string label)
    {
        yield return Wait(() => driver.DeathScreen && driver.DeathScreen.IsOpen && driver.DeathSettled
            && driver.DeathScreen.returnButton.interactable, check, label + ": death settles and manual return is available");
        driver.DeathScreen.returnButton.onClick.Invoke();
        yield return Wait(() => WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching,
            check, label + ": confirmed death return reaches hub");
    }
    public static IEnumerator Verify(RunWorldGate gate, int scenario, string output, Action<bool, string> check)
    {
        var flow = PersistentSceneFlow.Instance; var driver = flow.GetComponent<RunLifetimeDriver>();
        var player = PlayerContext.Instance.CurrentActor; var roster = gate.Roster; var completion = gate.Completion;
        var account = AccountGameplaySession.Current; var inventory = PlayerAccountInventoryService.SharedInventory;
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var transactions = typeof(AccountGameplaySession).GetField("transactions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(account);
        var store = (EasySaveAccountStore)typeof(AccountTransactions).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(transactions);
        string label = "Completion case " + scenario, id = account.ReadRun().runId;
        long bosses = account.Read().bossClearCount;
        check(completion && !completion.Portal && !completion.Reward, label + ": no early completion objects");
        try
        {
            double until = EditorApplication.timeSinceStartup + 160, movedAt = 0; int destination = 0;
            while (roster.Kills < roster.Target - 1 && EditorApplication.timeSinceStartup < until)
            {
                foreach (var member in roster.Members)
                {
                    if (roster.Kills >= roster.Target - 1) break;
                    if (member.State == MapRunRoster.Life.Active && gate.SpawnBudget.Fields[member.AreaIndex].TryGetActor(member.Index, out var enemy))
                        enemy.Health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, player.gameObject, suppressDefaultHitVfx: true));
                }
                if (EditorApplication.timeSinceStartup >= movedAt)
                {
                    var area = roster.Areas[destination++ % roster.Areas.Count];
                    ActorTeleportUtility.TeleportSafely(player.transform, area.Ground.Center + Vector3.up * .12f, Quaternion.identity);
                    movedAt = EditorApplication.timeSinceStartup + 2;
                }
                yield return Frames(4);
            }
            check(roster.Kills == roster.Target - 1 && account.ReadRun().phase == RunPhase.Active && !completion.Portal,
                label + ": target minus one cannot clear");
            var ui = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
            var regionDetails = (UnityEngine.UI.Text)typeof(OverburstGameUI).GetField("regionDetails", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
            check(regionDetails.text.Contains($"{roster.Target - 1:N0} / {roster.Target:N0}"), label + ": HUD shows the target count, not the hidden full roster");
            yield return Wait(() => Living(gate), check, label + ": last target actor available");
            var last = Living(gate); int otherArea = (roster.Members.First(m => m.State == MapRunRoster.Life.Active).AreaIndex + 1) % roster.Areas.Count;
            var point = roster.Areas[otherArea].Ground.Center;
            ActorTeleportUtility.TeleportSafely(player.transform, point + Vector3.up * .12f, Quaternion.identity);
            yield return Frames(3);
            last = Living(gate);
            check(last, label + ": live target retained after relocation");
            if (scenario == 1)
            {
                var cave = gate.GetComponent<Overburst.Caves.CaveWorld>();
                var passage = cave.passages.First(p => p.a == otherArea || p.b == otherArea);
                var bridge = passage.points[passage.points.Length / 2];
                ActorTeleportUtility.TeleportSafely(player.transform, bridge + Vector3.up * .12f, Quaternion.identity);
            }
            if (scenario == 2) ActorTeleportUtility.TeleportSafely(player.transform, point + Vector3.down * 8, Quaternion.identity);
            last.Health.TakeDamage(new DamageInfo(1000000, last.transform.position, player.gameObject, suppressDefaultHitVfx: true));
            check(roster.Kills == roster.Target && completion.CompletionArea == otherArea, label + ": target freezes player's current or last valid platform");
            if (scenario == 3)
            {
                player.Health.SetDamageDeathPrevention(flow, false);
                player.Health.TakeDamage(new DamageInfo(1000000, player.transform.position, last.gameObject, suppressDefaultHitVfx: true));
                yield return ConfirmDeathReturn(driver, check, label + ": simultaneous final kill and player death return");
                var failed = account.ReadRun();
                check(failed.phase == RunPhase.Failed && failed.completionReward == null && account.Read().bossClearCount == bosses,
                    label + ": same-frame death prevents completion reward and success");
                yield break;
            }
            if (scenario == 0)
            {
                store.FaultInjector = stage => { if (stage == "before-write") throw new IOException("G04 fixture completion failure"); };
                yield return Wait(() => completion.LastError != null, check, label + ": completion write failure observed");
                check(account.ReadRun().phase == RunPhase.Active && (!completion.Portal || !completion.Portal.gameObject.activeInHierarchy)
                    && (!completion.Reward || !completion.Reward.gameObject.activeInHierarchy), label + ": uncommitted floor objects stay inactive");
                store.FaultInjector = null;
            }
            yield return Wait(() => account.ReadRun().phase == RunPhase.Completed && completion.Portal && completion.Portal.gameObject.activeInHierarchy
                && completion.Reward && completion.Reward.gameObject.activeInHierarchy, check, label + ": completion and floor reward recover");
            var run = account.ReadRun(); string rewardId = run.completionReward.instanceId;
            check(run.completionArea == otherArea && account.Read().bossClearCount == bosses && !run.completionRewardClaimed,
                label + ": cave completion records no boss and no automatic pickup");
            yield return Frames(3);
            check(regionDetails.text.Contains("탐험 완료") && !regionDetails.text.Contains("180"), label + ": existing HUD shows completion without a countdown");
            var path = new NavMeshPath(); var areaGround = roster.Areas[otherArea].Ground;
            check(MapCombatGeometry.Contains(areaGround, completion.Portal.transform.position)
                && NavMesh.CalculatePath(areaGround.Center, completion.Portal.transform.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete,
                label + ": portal belongs to the completion platform and is walkable");
            driver.Poll(run.completedAtUtcTicks + TimeSpan.TicksPerDay);
            check(account.ReadRun().phase == RunPhase.Completed && WorldSessionState.Phase == WorldPhase.Run, label + ": future clock beyond 180 seconds never auto-extracts");
            check(gate.Context.CanGrantRewards && roster.Pending + roster.Active == roster.Total - roster.Target, label + ": finite remainder remains reward eligible");
            ActorTeleportUtility.TeleportSafely(player.transform, point + Vector3.up * .12f, Quaternion.identity);
            if (scenario == 0)
            {
                string snapshot = JsonConvert.SerializeObject(run.completionReward);
                UnityEngine.Object.Destroy(completion.Portal.gameObject); UnityEngine.Object.Destroy(completion.Reward.gameObject);
                yield return Frames(3);
                yield return Wait(() => completion.Portal && completion.Reward && completion.Portal.gameObject.activeInHierarchy, check, label + ": missing floor objects recover in same run");
                check(JsonConvert.SerializeObject(account.ReadRun().completionReward) == snapshot && completion.Reward.RuntimeItem.runtimeInstanceId == rewardId,
                    label + ": floor recovery preserves exact item rolls and ID");
                var fillers = new List<string>();
                var definition = registry.Resolve<MapItemData>(run.map.mapContentId);
                account.ExecuteState("fill-" + id, state =>
                {
                    while (state.inventory.Take(state.unlockedSlots).Any(string.IsNullOrEmpty))
                    {
                        var item = ItemSnapshotCodec.Capture(MapGenerationPolicy.CreateItem(definition, registry, 1, ItemGrade.Common, id), registry);
                        AccountRunCommands.Acquire(state, id, item); fillers.Add(item.instanceId);
                    }
                });
                check(!completion.Reward.TryPickup(inventory) && completion.Reward && !account.ReadRun().completionRewardClaimed, label + ": full inventory leaves reward on floor");
                account.ExecuteState("free-" + id, state => { string itemId = fillers[0]; state.inventory[state.inventory.IndexOf(itemId)] = null; state.items.RemoveAll(x => x.instanceId == itemId); });
                store.FaultInjector = stage => { if (stage == "before-write") throw new IOException("G04 fixture pickup failure"); };
                check(!completion.Reward.TryPickup(inventory) && !account.ReadRun().completionRewardClaimed && completion.Reward, label + ": pickup write failure retains floor and unclaimed state");
                store.FaultInjector = null;
            }
            if (scenario != 1)
            {
                check(completion.Reward.TryPickup(inventory), label + ": completion map picked up");
                yield return Frames(4);
                check(account.ReadRun().completionRewardClaimed && account.Read().items.Any(x => x.instanceId == rewardId && x.originRunId == id), label + ": pickup is owned but remains at risk until extraction");
                yield return Frames(20);
                check(!completion.Reward, label + ": claimed reward cannot respawn");
                if (scenario == 0)
                {
                    var owned = inventory.Items.Single(x => x?.runtimeInstanceId == rewardId);
                    check(!AccountGameplaySession.AcquireWorldItem(inventory, owned), label + ": an already owned reward cannot be acquired twice");
                    check(inventory.RemoveItem(owned), label + ": owned reward can leave the carried inventory");
                    var dropped = WorldItemDropFactory.CreateWorldPickup(owned, point, inventory, player.transform);
                    dropped.PlaceAuthored(point, gate.gameObject.scene);
                    check(dropped.TryPickup(inventory) && account.ReadRun().completionRewardClaimed,
                        label + ": dropped completion map is reacquired with the same permanent claim flag");
                    yield return Frames(3);
                }
            }
            var portalPosition = completion.Portal.transform.position;
            File.WriteAllText(Path.Combine(output, "completion-" + scenario + ".json"), JsonConvert.SerializeObject(new
            { id, run.completionArea, run.completedAtUtcTicks, run.completionReward, roster.Total, roster.Target, roster.Kills, roster.Active, roster.Pending,
                portal = new[] { portalPosition.x, portalPosition.y, portalPosition.z }, rewardId, bosses }, Formatting.Indented));
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "completion-" + scenario + ".png"));
            yield return Frames(3);
            if (scenario == 0)
            {
                var clearedMembers = roster.Members.Where(m => roster.Areas[m.AreaIndex].Cleared).ToArray();
                var leases = clearedMembers.Select(m => m.Lease).ToArray();
                int kills = roster.Kills;
                yield return Frames(20);
                check(clearedMembers.Length > 0 && roster.CaveTargetReached
                    && roster.Kills == kills && clearedMembers.Select(m => m.Lease).SequenceEqual(leases)
                    && roster.Kills + roster.Active + roster.Pending == roster.Total,
                    label + ": 95-percent cleared areas preserve remaining IDs without spawning or restoring them");
                var remaining = Living(gate);
                if (remaining)
                {
                    int xp = account.Read().experience; int level = account.Read().level;
                    remaining.Health.TakeDamage(new DamageInfo(1000000, remaining.transform.position, player.gameObject, suppressDefaultHitVfx: true));
                    PlayerProgression.Current?.FlushPendingExperience();
                    check(roster.Kills == kills + 1 && (account.Read().experience > xp || account.Read().level > level) && account.ReadRun().phase == RunPhase.Completed,
                        label + ": an existing post-clear survivor awards XP without re-completion");
                }
                else check(roster.Pending + roster.Active == roster.Total - roster.Kills,
                    label + ": unspawned remainder is retained without forcing a post-clear respawn");
                store.FaultInjector = stage => { if (stage == "before-write") throw new IOException("G04 fixture extraction failure"); };
                for (int i = 0; i < 3; i++) check(driver.RequestPortalExit(), label + ": repeated return request " + i);
                yield return Wait(() => driver.LastError != null, check, label + ": extraction write failure observed");
                check(WorldSessionState.Phase == WorldPhase.Run && account.ReadRun().phase == RunPhase.Completed, label + ": scene retained until extraction save succeeds");
                store.FaultInjector = null;
            }
            else
            {
                if (scenario == 2) check(driver.RequestPortalExit(), label + ": return requested before same-frame fatal damage");
                player.Health.SetDamageDeathPrevention(flow, false);
                player.Health.TakeDamage(new DamageInfo(1000000, player.transform.position, null, suppressDefaultHitVfx: true));
                yield return ConfirmDeathReturn(driver, check, label + ": post-completion death");
            }
            yield return Wait(() => WorldSessionState.IsHideout && !flow.IsSwitching, check, label + ": settled run returns to hub");
            run = account.ReadRun();
            if (scenario == 0) check(run.phase == RunPhase.Extracted && account.Read().items.Any(x => x.instanceId == rewardId && string.IsNullOrEmpty(x.originRunId)), label + ": successful manual extraction preserves reward");
            else check(run.phase == RunPhase.Failed && account.Read().items.All(x => x.instanceId != rewardId), label + ": death loses unextracted reward and overrides return");
            check(account.Read().bossClearCount == bosses, label + ": cave never increments boss clear count");
        }
        finally { store.FaultInjector = null; }
    }
}
