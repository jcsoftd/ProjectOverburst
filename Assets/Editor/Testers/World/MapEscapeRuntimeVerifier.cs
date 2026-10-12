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

public static class MapEscapeRuntimeVerifier
{
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator Frames(int n) { while (n-- > 0) yield return null; }
    static IEnumerator Wait(Func<bool> condition, Action<bool, string> check, string label, float seconds = 30)
    {
        double until = EditorApplication.timeSinceStartup + seconds;
        while (!condition() && EditorApplication.timeSinceStartup < until) yield return null;
        check(condition(), label);
    }
    public static int SelectedSeed(int start)
    {
        var map = new MapInstanceState { terrain = MapTerrainKind.Cave, generationVersion = 1 };
        for (map.seed = start; map.seed < start + 1000; map.seed++) if (MapEmergencyExit.IsSelected(map)) return map.seed;
        throw new InvalidOperationException("No selected seed in fixture range.");
    }
    public static string RunNative(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Idle EditMode required.");
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        output = Path.GetFullPath(output);
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Use new owned evidence.");
        Directory.CreateDirectory(output); var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        try
        {
            foreach (var terrain in new[] { MapTerrainKind.Cave, MapTerrainKind.Desert })
            {
                var map = new MapInstanceState { terrain = terrain, generationVersion = 1, areaCount = 4 };
                int selected = 0; bool stable = true;
                for (int i = 1; i <= 10000; i++)
                {
                    map.seed = i; bool choice = MapEmergencyExit.IsSelected(map); if (choice) selected++;
                    var copied = ItemSnapshotCodec.CopyValues(map); copied.level = 100; copied.grade = ItemGrade.Legendary;
                    stable &= choice == MapEmergencyExit.IsSelected(copied) && choice == MapEmergencyExit.IsSelected(map);
                }
                Check(selected > 1800 && selected < 2200, terrain + ": deterministic population is near twenty percent");
                Check(stable, terrain + ": save copy, repeated entry, grade and level do not reroll selection");
                map.generationVersion = 0; Check(!MapEmergencyExit.IsSelected(map), terrain + ": legacy maps have no emergency exit");
            }
            Check(!MapEmergencyExit.IsSelected(null), "missing map has no exit");
            Check(MapEmergencyExit.ChannelSeconds == 3f, "confirmed three second duration");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "PASS", checks, fixtureSeed = SelectedSeed(48310) }, Formatting.Indented));
            return "PASS " + checks.Count;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = e.ToString() }, Formatting.Indented)); throw; }
    }
    public static IEnumerator Verify(RunWorldGate gate, int scenario, string output, Action<bool, string> check)
    {
        string label = gate.Map.terrain + " emergency case " + scenario;
        var exit = gate.EmergencyExit; var player = PlayerContext.Instance.CurrentActor;
        var flow = PersistentSceneFlow.Instance; var driver = flow.GetComponent<RunLifetimeDriver>();
        var account = AccountGameplaySession.Current; var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var tx = typeof(AccountGameplaySession).GetField("transactions", Fields).GetValue(account);
        var store = (EasySaveAccountStore)typeof(AccountTransactions).GetField("store", Fields).GetValue(tx);
        var menu = OverburstGameMenu.Instance;
        var budget = gate.SpawnBudget; bool budgetEnabled = budget.enabled, driverEnabled = driver.enabled;
        var encounter = gate.Completion.Boss; bool encounterEnabled = encounter && encounter.enabled;
        bool menuOwned = false;
        check(exit && MapEmergencyExit.IsSelected(gate.Map), label + ": selected map has its actual exit");
        check(!MapCombatGeometry.Contains(gate.Roster.Areas[exit.AreaIndex].Ground, gate.EntryPoint.position), label + ": exit excludes entry combat tile");
        check(gate.GetComponentsInChildren<MapEmergencyExit>(true).Length == 1, label + ": at most one exit");
        yield return Wait(() => WorldMinimapController.Instance?.Exploration != null, check, label + ": run exploration binding is ready");
        var map = WorldMinimapController.Instance.Exploration;
        if (!map.IsRevealed(exit.transform.position))
            check(!exit.Discovered && !Enumerable.Range(0, WorldMinimapController.Instance.Guides.Count).Any(i => WorldMinimapController.Instance.Guides.Get(i).Type == MinimapGuideSource.Kind.EmergencyExit), label + ": unrevealed exit has no minimap hint");
        void Confirm()
        {
            check(exit.TryInteract(player) == InteractionExecutionResult.Succeeded && menu.ModalOpen, label + ": actual interaction opens existing confirmation");
            menuOwned = true; menu.modalConfirm.onClick.Invoke(); menuOwned = false;
            check(exit.IsChanneling && !OverburstGameMenu.IsOpen, label + ": confirmation starts channel and restores game time");
        }
        try
        {
            budget.enabled = false;
            // Keep damage timing controlled, just like the finite enemies below. Boss intro/combat have their own Play verifier.
            if (encounter) encounter.enabled = false;
            check(gate.Map.terrain != MapTerrainKind.Desert || gate.BossArena.HasBoss, label + ": fixture does not auto-complete on small-only final tile entry");
            foreach (var field in budget.Fields) typeof(MapMonsterField).GetMethod("SetFiniteAwake", Fields).Invoke(field, new object[] { false });
            var enemies = new List<EnemyActor>(); foreach (var field in budget.Fields) field.CollectLivingActors(enemies);
            var damageSource = enemies.First(e => e);
            var position = exit.transform.position;
            var gateExit = typeof(RunWorldGate).GetProperty("EmergencyExit");
            exit.gameObject.SetActive(false); gateExit.SetValue(gate, null);
            MapEmergencyExit repeat = null;
            try
            {
                repeat = MapEmergencyExit.Prepare(gate, player);
                check(repeat && repeat.AreaIndex == exit.AreaIndex && (repeat.transform.position - position).sqrMagnitude < .00001f,
                    label + ": same stored map and ground reproduce exact exit after preparation retry");
            }
            finally
            {
                if (repeat) { repeat.gameObject.SetActive(false); UnityEngine.Object.Destroy(repeat.gameObject); }
                gateExit.SetValue(gate, exit); exit.gameObject.SetActive(true);
            }
            ActorTeleportUtility.TeleportSafely(player.transform, position + Vector3.up * .12f, Quaternion.identity);
            yield return Wait(() => exit.Discovered && exit.IsInteractionAvailable(player), check, label + ": actual exploration discovers exit");
            yield return Frames(5);
            check(Enumerable.Range(0, WorldMinimapController.Instance.Guides.Count).Any(i => WorldMinimapController.Instance.Guides.Get(i).Type == MinimapGuideSource.Kind.EmergencyExit), label + ": discovered exit has distinct hint kind");
            var interactionState = new { available = exit.IsInteractionAvailable(player), discovered = exit.Discovered,
                active = exit.isActiveAndEnabled, menuExists = menu != null, menuOpen = OverburstGameMenu.IsOpen,
                driverExists = driver != null, health = player.Health.CurrentHp, phase = account.ReadRun().phase.ToString(),
                introducing = gate.Completion.Boss && gate.Completion.Boss.IsIntroducing,
                hasBoss = gate.BossArena?.HasBoss, blocked = GameplayInputBlocker.IsGameplayInputBlocked,
                offset = new[] { player.transform.position.x - position.x, player.transform.position.y - position.y, player.transform.position.z - position.z } };
            var interactionResult = exit.TryInteract(player);
            File.WriteAllText(Path.Combine(output, "escape-interaction-" + gate.Map.terrain + "-" + scenario + ".json"),
                JsonConvert.SerializeObject(new { interactionState, result = interactionResult.ToString() }, Formatting.Indented));
            check(interactionResult == InteractionExecutionResult.Succeeded, label + ": opens warning before consuming opportunity");
            menuOwned = true;
            if (scenario == 0)
            {
                yield return Frames(8);
                string capture = Path.Combine(output, "escape-warning-" + gate.Map.terrain + ".png");
                ScreenCapture.CaptureScreenshot(capture); yield return Frames(3);
                check(menu.modalDescription.preferredHeight <= menu.modalDescription.rectTransform.rect.height + 1,
                    label + ": warning text fits existing confirmation layout");
            }
            menu.modalCancel.onClick.Invoke(); menuOwned = false;
            check(!exit.IsChanneling && account.ReadRun().phase == RunPhase.Active && !OverburstGameMenu.IsOpen, label + ": cancel preserves run and unpauses");
            Confirm(); yield return Frames(3);
            check(exit.TryInteract(player) == InteractionExecutionResult.ClosedSession && !exit.IsChanneling, label + ": interact again cancels channel");
            Confirm(); yield return Frames(3);
            player.Health.TakeDamage(new DamageInfo(2, player.transform.position, damageSource.gameObject, suppressDefaultHitVfx: true));
            check(!exit.IsChanneling && !exit.ExtractionPending && account.ReadRun().phase == RunPhase.Active, label + ": real damage cancels before settlement");
            yield return Frames(10);
            Confirm();
            ActorTeleportUtility.TeleportSafely(player.transform, gate.EntryPoint.position, Quaternion.identity);
            yield return Frames(2);
            check(!exit.IsChanneling, label + ": leaving range cancels channel");
            ActorTeleportUtility.TeleportSafely(player.transform, position + Vector3.up * .12f, Quaternion.identity);
            yield return Frames(3);
            Confirm(); menu.Open(); menuOwned = true; yield return Frames(2);
            check(!exit.IsChanneling, label + ": pause menu cancels channel");
            menu.Close(); menuOwned = false;
            yield return Frames(3);

            string id = account.ReadRun().runId; var loot = MapGenerationPolicy.CreateItem(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"), registry, 1, ItemGrade.Common, id);
            var captured = ItemSnapshotCodec.Capture(loot, registry);
            account.ExecuteState("escape-loot-" + id, state => AccountRunCommands.Acquire(state, id, captured));
            driver.enabled = false;
            Confirm(); float began = Time.time;
            yield return Wait(() => exit.ExtractionPending, check, label + ": real channel reaches ready state", 10);
            check(Time.time - began >= 2.9f && exit.ChannelProgress == 1 && account.ReadRun().phase == RunPhase.Active, label + ": no early extraction during three seconds");
            driver.CancelEmergencyExit(exit);
            check(driver.RequestEmergencyExit(exit), label + ": completed channel submits a valid request in the current frame");
            driver.Poll(DateTime.UtcNow.Ticks);
            check(account.ReadRun().phase == RunPhase.Active, label + ": requesting frame cannot commit success");
            if (scenario == 1)
            {
                player.Health.SetDamageDeathPrevention(flow, false);
                player.Health.TakeDamage(new DamageInfo(1000000, player.transform.position, damageSource.gameObject, suppressDefaultHitVfx: true));
                check(player.Health.IsDead && !exit.ExtractionPending, label + ": same-frame lethal damage cancels exit");
            }
            if (scenario == 2)
            {
                var reward = MapRewardPolicy.CreateBossMap(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"), registry, 1, .1f, .1f, id);
                reward.runtimeInstanceId = AccountRunCommands.CompletionRewardId(id);
                var rewardState = ItemSnapshotCodec.Capture(reward, registry);
                account.ExecuteState("complete-" + id, s => AccountRunCommands.CompleteMap(s, id, DateTime.UtcNow.Ticks, exit.AreaIndex, position, rewardState, gate.Map.terrain == MapTerrainKind.Desert));
                check(account.ReadRun().phase == RunPhase.Completed, label + ": completion checkpoint wins before extraction checkpoint");
            }
            if (scenario == 0) store.FaultInjector = point => { if (point == "before-write") throw new IOException("escape runtime fixture write failure"); };
            driver.enabled = driverEnabled;
            if (scenario == 0)
            {
                yield return Wait(() => driver.LastError != null, check, label + ": actual driver retains failed save request");
                check(account.ReadRun().phase == RunPhase.Active && exit.ExtractionPending, label + ": failed exit save does not publish success");
                store.FaultInjector = null;
                player.Health.TakeDamage(new DamageInfo(2, player.transform.position, damageSource.gameObject, suppressDefaultHitVfx: true));
                check(!exit.ExtractionPending && !exit.IsChanneling && driver.LastError == null && !GameplayInputBlocker.IsGameplayInputBlocked,
                    label + ": damage during save retry cancels both request and owned input blocks");
                yield return Frames(10);
                Confirm();
            }
            yield return Wait(() => WorldSessionState.IsHideout && !flow.IsSwitching, check, label + ": actual scene flow returns to hideout", 60);
            var saved = account.Read();
            check(saved.run.phase == (scenario == 1 ? RunPhase.Failed : RunPhase.Extracted), label + ": final state matches race outcome");
            check(saved.run.exitReason == (scenario == 0 ? RunExitReason.EmergencyEscape : scenario == 2 ? RunExitReason.Completed : RunExitReason.None), label + ": durable exit reason matches race outcome");
            check(saved.items.Any(i => i.instanceId == captured.instanceId && i.originRunId == null) == (scenario != 1), label + ": loot kept only on successful extraction");
            check(!exit && !UnityEngine.Object.FindFirstObjectByType<MapEmergencyExit>() && !GameplayInputBlocker.IsGameplayInputBlocked, label + ": exit and input ownership released on return");
        }
        finally
        {
            store.FaultInjector = null;
            if (menuOwned && menu) menu.Close();
            if (driver) driver.enabled = driverEnabled;
            if (encounter) encounter.enabled = encounterEnabled;
            if (budget)
            {
                budget.enabled = budgetEnabled;
                foreach (var field in budget.Fields) if (field) typeof(MapMonsterField).GetMethod("SetFiniteAwake", Fields).Invoke(field, new object[] { true });
            }
            if (exit) exit.CancelChannel();
        }
    }
}
