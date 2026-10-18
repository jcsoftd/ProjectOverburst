using System;
using System.Collections.Generic;
using System.Linq;

namespace Overburst.Persistence
{
    public static class AccountRunCommands
    {
        public static void PrepareEntry(AccountSnapshot state, string runId, MapInstanceState map, string mapItemId, bool debugEntry = false)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            debugEntry = false;
#endif
            if (state.run != null && (AccountInvariants.IsRunning(state.run.phase) || state.run.phase == RunPhase.EntryPending))
                throw new InvalidOperationException("A run is already pending or active.");
            if (string.IsNullOrWhiteSpace(runId) || map == null || map.level < 1 || map.level > 100
                || !Enum.IsDefined(typeof(ItemGrade), map.grade) || map.options == null)
                throw new ArgumentException("Invalid run/map.");
            if (state.run != null && state.run.runId == runId)
                throw new InvalidOperationException("A completed run identity cannot be reused.");
            if (!string.IsNullOrEmpty(mapItemId))
            {
                var item = state.items.SingleOrDefault(x => x.instanceId == mapItemId);
                if (item?.map == null || !state.inventory.Contains(mapItemId) || item.map.level != map.level || item.map.grade != map.grade || item.map.mapContentId != map.mapContentId)
                    throw new InvalidOperationException("The selected map is not available.");
                if ((item.map.generationVersion > 0 || map.generationVersion > 0)
                    && !ES3.Serialize(item.map).SequenceEqual(ES3.Serialize(map)))
                    throw new InvalidOperationException("The selected map properties have changed.");
                map = item.map;
            }
            else if (!debugEntry)
                throw new InvalidOperationException("A map item is required for this run.");
            MapGenerationPolicy.Validate(map);
            state.run = new RunSnapshot { runId = runId, phase = RunPhase.EntryPending,
                mapInstanceId = mapItemId, map = ItemSnapshotCodec.CopyValues(map) };
        }

        public static void Activate(AccountSnapshot state, string runId)
        {
            var run = Require(state, runId);
            if (run.phase != RunPhase.EntryPending) throw new InvalidOperationException("Run is not awaiting scene readiness.");
            if (!string.IsNullOrEmpty(run.mapInstanceId))
            {
                var item = state.items.Single(x => x.instanceId == run.mapInstanceId);
                if (!state.inventory.Contains(item.instanceId)) throw new InvalidOperationException("Reserved map has moved.");
                if (item.count < 1 || item.map == null || !ES3.Serialize(item.map).SequenceEqual(ES3.Serialize(run.map)))
                    throw new InvalidOperationException("Reserved map properties have changed.");
                if (--item.count == 0) Remove(state, new HashSet<string> { item.instanceId });
            }
            run.phase = RunPhase.Active;
            Overburst.Quests.AccountQuestCommands.Progress(state, Overburst.Quests.QuestObjectiveKind.EnterRun, "run.any", 1, runId);
            if (!string.IsNullOrEmpty(run.mapInstanceId))
                Overburst.Quests.AccountQuestCommands.Progress(state, Overburst.Quests.QuestObjectiveKind.EnterRun, "run.map", 1, runId);
        }

        public static void Acquire(AccountSnapshot state, string runId, ItemSnapshot item)
        {
            RequireActive(state, runId);
            if (HasOverflow(state)) throw new InvalidOperationException("Inventory is over capacity.");
            int slot = state.inventory.FindIndex(0, state.unlockedSlots, string.IsNullOrEmpty);
            if (slot < 0) throw new InvalidOperationException("Inventory is full.");
            var acquired = ItemSnapshotCodec.CopyValues(item);
            acquired.originRunId = runId;
            if (state.items.Any(x => x.instanceId == acquired.instanceId)) throw new InvalidOperationException("Item already acquired.");
            state.items.Add(acquired);
            state.inventory[slot] = acquired.instanceId;
            RecordCompletionPickup(state, acquired.instanceId);
        }

        public static void Transfer(AccountSnapshot state, string runId, string objectId, string itemId, int tab, int slot)
        {
            var run = RequireActive(state, runId);
            if (string.IsNullOrWhiteSpace(objectId) || run.transferredObjects.Contains(objectId)) throw new InvalidOperationException("Transfer object has already been used.");
            if (tab < 0 || tab >= state.stashTabs.Count || slot < 0 || slot >= state.stashCapacity || !string.IsNullOrEmpty(state.stashTabs[tab].slots[slot]))
                throw new InvalidOperationException("Stash destination is unavailable.");
            if (!PlayerContainers(state).Any(x => x.Contains(itemId))) throw new InvalidOperationException("Item is not owned by player.");
            var item = state.items.Single(x => x.instanceId == itemId);
            if (item.originRunId != runId) throw new InvalidOperationException("Item was not acquired in this run.");
            DetachForTransfer(state, item);
            state.stashTabs[tab].slots[slot] = itemId;
            run.transferredObjects.Add(objectId);
        }

        public static void Transfer(AccountSnapshot state, string runId, string objectId, string itemId, AccountContentRegistry registry)
        {
            var run = RequireActive(state, runId);
            if (string.IsNullOrWhiteSpace(objectId) || run.transferredObjects.Contains(objectId))
                throw new InvalidOperationException("Transfer object has already been used.");
            if (!PlayerContainers(state).Any(x => x.Contains(itemId)))
                throw new InvalidOperationException("Item is not carried by player.");
            var item = state.items.Single(x => x.instanceId == itemId);
            if (item.originRunId != runId)
                throw new InvalidOperationException("Item was not acquired in this run.");
            var runtime = ItemSnapshotCodec.Restore(item, registry);
            int maxStack = 1;
            if (runtime.baseData is ConsumableItemData consumable)
                maxStack = consumable.IsPermanentSingleItem ? 1 : UnityEngine.Mathf.Max(1, consumable.maxStack);
            else if (runtime.baseData is CurrencyItemData currency)
                maxStack = UnityEngine.Mathf.Max(1, currency.maxStack);
            else if (runtime.itemType == "Junk" || runtime.itemType == "QuestItem") maxStack = 99;
            var merges = new List<KeyValuePair<ItemSnapshot, int>>();
            int remaining = item.count;
            int emptyTab = -1, emptySlot = -1;
            for (int tab = 0; tab < state.stashTabs.Count; tab++)
                for (int slot = 0; slot < state.stashTabs[tab].slots.Count; slot++)
                {
                    string id = state.stashTabs[tab].slots[slot];
                    if (string.IsNullOrEmpty(id))
                    {
                        if (emptyTab < 0) { emptyTab = tab; emptySlot = slot; }
                        continue;
                    }
                    if (maxStack <= 1 || remaining <= 0) continue;
                    var target = state.items.Single(x => x.instanceId == id);
                    if (target.contentId != item.contentId || target.level != item.level || target.grade != item.grade
                        || !string.IsNullOrEmpty(target.originRunId)) continue;
                    int count = Math.Min(remaining, Math.Max(0, maxStack - target.count));
                    if (count == 0) continue;
                    merges.Add(new KeyValuePair<ItemSnapshot, int>(target, count));
                    remaining -= count;
                }
            if (remaining > 0 && (emptyTab < 0 || remaining > maxStack))
                throw new InvalidOperationException("Stash has no room for the entire selected stack.");
            // Planning above is read-only: a rejected transfer neither merges a partial stack nor spends the object.
            DetachForTransfer(state, item);
            foreach (var merge in merges) merge.Key.count += merge.Value;
            if (remaining == 0) state.items.Remove(item);
            else
            {
                item.count = remaining;
                state.stashTabs[emptyTab].slots[emptySlot] = itemId;
            }
            run.transferredObjects.Add(objectId);
        }

        public static void ClaimEventCard(AccountSnapshot state, string runId, string eventId, int experience)
        {
            var run = RequireActive(state, runId);
            if (string.IsNullOrWhiteSpace(eventId) || run.rewardedEncounters.Contains(eventId))
                throw new InvalidOperationException("Event card was already claimed.");
            if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
            if (experience > 0 && state.level < OverburstGrowthRules.MaximumLevel)
            {
                state.experience = checked(state.experience + experience);
                while (state.level < OverburstGrowthRules.MaximumLevel)
                {
                    int needed = OverburstGrowthRules.ExperienceToNext(state.level);
                    if (state.experience < needed) break;
                    state.experience -= needed;
                    state.level++;
                }
                if (state.level >= OverburstGrowthRules.MaximumLevel) state.experience = 0;
                state.skillTree = AccountSkillTree.GrantEligible(state.skillTree, state.level);
            }
            run.rewardedEncounters.Add(eventId);
        }

        private static void DetachForTransfer(AccountSnapshot state, ItemSnapshot item)
        {
            var removed = new HashSet<string> { item.instanceId };
            foreach (var container in PlayerContainers(state)) ClearReferences(container, removed);
            if (removed.Contains(state.elementalGemInstanceId ?? "")) state.elementalGemInstanceId = null;
            ClearReferences(state.flasks, removed);
            foreach (var quick in state.quickSlots)
            {
                if (quick.flaskInstanceId == item.instanceId) quick.flaskInstanceId = null;
            }
            if (item.flask != null) { item.flask.equippedSlot = -1; item.flask.cooldownRemaining = 0f; }
            item.originRunId = null;
        }

        public static void ClearBoss(AccountSnapshot state, string runId, long utcTicks)
        {
            var run = RequireActive(state, runId);
            if (AccountInvariants.IsCompleted(run.phase)) return;
            if (run.map.generationVersion > 0) throw new InvalidOperationException("Product maps require normal completion with a saved reward.");
            run.phase = RunPhase.BossCleared;
            run.bossClearedAtUtcTicks = utcTicks;
            state.bossClearCount = checked(state.bossClearCount + 1);
        }

        public static void CompleteMap(AccountSnapshot state, string runId, long utcTicks,
            int area, UnityEngine.Vector3 anchor, ItemSnapshot reward, bool bossDefeated = true)
        {
            var run = RequireActive(state, runId);
            if (run.phase == RunPhase.Completed) return;
            if (run.phase != RunPhase.Active || run.map.generationVersion <= 0
                || area < 0 || area >= run.map.areaCount || utcTicks <= 0 || utcTicks > DateTime.MaxValue.Ticks
                || !Finite(anchor.x) || !Finite(anchor.y) || !Finite(anchor.z)
                || reward?.map == null || reward.count != 1 || reward.originRunId != runId
                || reward.instanceId != CompletionRewardId(runId))
                throw new InvalidOperationException("Invalid map completion or reward.");
            run.completedAtUtcTicks = utcTicks;
            run.completionArea = area; run.completionAnchor = anchor;
            run.completionReward = ItemSnapshotCodec.CopyValues(reward);
            run.completionRewardClaimed = false;
            run.phase = RunPhase.Completed;
            if (bossDefeated && run.map.terrain == MapTerrainKind.Desert) state.bossClearCount = checked(state.bossClearCount + 1);
        }

        public static string CompletionRewardId(string runId) => runId + "/completion-map";
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // The claim flag is committed in the same transaction as inventory acquisition.
        public static void RecordCompletionPickup(AccountSnapshot state, string itemId)
        {
            var run = state.run;
            if (run?.completionReward == null || run.completionReward.instanceId != itemId) return;
            if (run.phase != RunPhase.Completed
                || !state.items.Any(x => x.instanceId == itemId && x.originRunId == run.runId))
                throw new InvalidOperationException("Completion reward is unavailable.");
            // Dropping and picking up the same owned item does not create another completion reward.
            run.completionRewardClaimed = true;
        }

        public static void Extract(AccountSnapshot state, string runId, bool emergencyEscape = false)
        {
            var run = RequireActive(state, runId);
            bool completed = AccountInvariants.IsCompleted(run.phase);
            if (!completed && (!emergencyEscape || run.map.generationVersion <= 0))
                throw new InvalidOperationException("Run must be completed or use a map emergency exit.");
            // A durable completion wins over a simultaneous emergency-exit request.
            run.exitReason = completed ? RunExitReason.Completed : RunExitReason.EmergencyEscape;
            foreach (var item in state.items) if (item.originRunId == runId) item.originRunId = null;
            run.phase = RunPhase.Extracted;
        }

        public static void Fail(AccountSnapshot state, string runId)
        {
            var run = Require(state, runId);
            if (run.phase == RunPhase.Failed) return;
            if (run.phase == RunPhase.Extracted) throw new InvalidOperationException("An extracted run cannot fail.");
            Remove(state, new HashSet<string>(state.items.Where(x => x.originRunId == runId).Select(x => x.instanceId)));
            run.phase = RunPhase.Failed;
            // Capacity is recalculated from remaining bags by the account equipment policy.
            // Existing inventory entries beyond that capacity are deliberately retained.
        }

        public static bool RecoverInterrupted(AccountSnapshot state)
        {
            if (state.run == null || state.run.phase == RunPhase.Extracted || state.run.phase == RunPhase.Failed) return false;
            Fail(state, state.run.runId);
            return true;
        }

        public static bool HasOverflow(AccountSnapshot state) => state.inventory.Skip(state.unlockedSlots).Any(x => !string.IsNullOrEmpty(x));
        private static RunSnapshot Require(AccountSnapshot state, string id)
        {
            if (state.run == null || state.run.runId != id) throw new InvalidOperationException("Stale run identity.");
            return state.run;
        }
        private static RunSnapshot RequireActive(AccountSnapshot state, string id)
        {
            var run = Require(state, id);
            if (!AccountInvariants.IsRunning(run.phase)) throw new InvalidOperationException("Run is not active.");
            return run;
        }
        private static IEnumerable<List<string>> PlayerContainers(AccountSnapshot state)
        {
            yield return state.inventory; yield return state.weapons; yield return state.gear; yield return state.bags; yield return state.flasks;
            yield return new List<string> { state.elementalGemInstanceId };
        }
        private static void ClearReferences(List<string> slots, HashSet<string> removed)
        {
            for (int i = 0; i < slots.Count; i++) if (!string.IsNullOrEmpty(slots[i]) && removed.Contains(slots[i])) slots[i] = null;
        }
        private static void Remove(AccountSnapshot state, HashSet<string> removed)
        {
            foreach (var slots in PlayerContainers(state)) ClearReferences(slots, removed);
            if (removed.Contains(state.elementalGemInstanceId ?? "")) state.elementalGemInstanceId = null;
            foreach (var tab in state.stashTabs) ClearReferences(tab.slots, removed);
            ClearReferences(state.flasks, removed);
            foreach (var quick in state.quickSlots) if (removed.Contains(quick.flaskInstanceId ?? "")) quick.flaskInstanceId = null;
            state.items.RemoveAll(x => removed.Contains(x.instanceId));
            AccountPlayerProjection.NormalizeBindings(state);
        }
    }
}
