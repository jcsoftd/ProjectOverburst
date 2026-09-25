using System;
using System.Collections.Generic;
using System.Linq;

namespace Overburst.Persistence
{
    public static class AccountRunCommands
    {
        public static void PrepareEntry(AccountSnapshot state, string runId, MapInstanceState map, string mapItemId)
        {
            if (state.run != null && (AccountInvariants.IsRunning(state.run.phase) || state.run.phase == RunPhase.EntryPending))
                throw new InvalidOperationException("A run is already pending or active.");
            if (string.IsNullOrWhiteSpace(runId) || map == null || map.level < 1 || map.level > 100)
                throw new ArgumentException("Invalid run/map.");
            if (map.level > 1)
            {
                var item = state.items.SingleOrDefault(x => x.instanceId == mapItemId);
                if (item?.map == null || !state.inventory.Contains(mapItemId) || item.map.level != map.level || item.map.grade != map.grade || item.map.mapContentId != map.mapContentId)
                    throw new InvalidOperationException("The selected map is not available.");
                map = item.map;
            }
            state.run = new RunSnapshot { runId = runId, phase = RunPhase.EntryPending, mapInstanceId = map.level == 1 ? null : mapItemId, map = ItemSnapshotCodec.CopyValues(map) };
        }

        public static void Activate(AccountSnapshot state, string runId)
        {
            var run = Require(state, runId);
            if (run.phase != RunPhase.EntryPending) throw new InvalidOperationException("Run is not awaiting scene readiness.");
            if (!string.IsNullOrEmpty(run.mapInstanceId))
            {
                var item = state.items.Single(x => x.instanceId == run.mapInstanceId);
                if (!state.inventory.Contains(item.instanceId)) throw new InvalidOperationException("Reserved map has moved.");
                if (--item.count == 0) Remove(state, new HashSet<string> { item.instanceId });
            }
            run.phase = RunPhase.Active;
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
        }

        public static void Transfer(AccountSnapshot state, string runId, string objectId, string itemId, int tab, int slot)
        {
            var run = RequireActive(state, runId);
            if (string.IsNullOrWhiteSpace(objectId) || run.transferredObjects.Contains(objectId)) throw new InvalidOperationException("Transfer object has already been used.");
            if (tab < 0 || tab >= state.stashTabs.Count || slot < 0 || slot >= state.stashCapacity || !string.IsNullOrEmpty(state.stashTabs[tab].slots[slot]))
                throw new InvalidOperationException("Stash destination is unavailable.");
            if (!PlayerContainers(state).Any(x => x.Contains(itemId))) throw new InvalidOperationException("Item is not owned by player.");
            var item = state.items.Single(x => x.instanceId == itemId);
            foreach (var container in PlayerContainers(state)) ClearReferences(container, new HashSet<string> { itemId });
            ClearReferences(state.flasks, new HashSet<string> { itemId });
            foreach (var quick in state.quickSlots) if (quick.flaskInstanceId == itemId) quick.flaskInstanceId = null;
            item.originRunId = null;
            state.stashTabs[tab].slots[slot] = itemId;
            run.transferredObjects.Add(objectId);
        }

        public static void ClearBoss(AccountSnapshot state, string runId, long utcTicks)
        {
            var run = RequireActive(state, runId);
            if (run.phase == RunPhase.BossCleared) return;
            run.phase = RunPhase.BossCleared;
            run.bossClearedAtUtcTicks = utcTicks;
            state.bossClearCount = checked(state.bossClearCount + 1);
        }

        public static void Extract(AccountSnapshot state, string runId)
        {
            var run = RequireActive(state, runId);
            if (run.phase != RunPhase.BossCleared) throw new InvalidOperationException("Boss must be cleared before extraction.");
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
            yield return state.inventory; yield return state.weapons; yield return state.gear; yield return state.bags;
        }
        private static void ClearReferences(List<string> slots, HashSet<string> removed)
        {
            for (int i = 0; i < slots.Count; i++) if (!string.IsNullOrEmpty(slots[i]) && removed.Contains(slots[i])) slots[i] = null;
        }
        private static void Remove(AccountSnapshot state, HashSet<string> removed)
        {
            foreach (var slots in PlayerContainers(state)) ClearReferences(slots, removed);
            foreach (var tab in state.stashTabs) ClearReferences(tab.slots, removed);
            ClearReferences(state.flasks, removed);
            foreach (var quick in state.quickSlots) if (removed.Contains(quick.flaskInstanceId ?? "")) quick.flaskInstanceId = null;
            state.items.RemoveAll(x => removed.Contains(x.instanceId));
        }
    }
}
