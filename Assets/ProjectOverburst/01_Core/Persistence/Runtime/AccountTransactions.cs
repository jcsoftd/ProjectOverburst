using System;
using System.Collections.Generic;
using System.IO;

namespace Overburst.Persistence
{
    public static class AccountInvariants
    {
        public static void Validate(AccountSnapshot state, AccountContentRegistry registry)
        {
            if (state == null || state.schemaVersion != 1 || state.revision < 0 || state.level < 1 || state.level > 100 || state.experience < 0)
                throw new InvalidDataException("Invalid account header/progression.");
            if (state.inventoryCapacity < 1 || state.unlockedSlots < 0 || state.unlockedSlots > state.inventoryCapacity || state.inventory.Count != state.inventoryCapacity)
                throw new InvalidDataException("Invalid inventory dimensions.");
            var items = new Dictionary<string, ItemSnapshot>(StringComparer.Ordinal);
            foreach (var item in state.items)
            {
                ItemSnapshotCodec.Validate(item, registry);
                if (!items.TryAdd(item.instanceId, item)) throw new InvalidDataException("Duplicate item identity: " + item.instanceId);
            }
            var owned = new HashSet<string>(StringComparer.Ordinal);
            Action<IEnumerable<string>> container = slots =>
            {
                if (slots == null) throw new InvalidDataException("Missing item container.");
                foreach (var id in slots)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!items.ContainsKey(id) || !owned.Add(id)) throw new InvalidDataException("Missing or multiply owned item: " + id);
                }
            };
            container(state.inventory); container(state.weapons); container(state.gear); container(state.bags);
            foreach (var tab in state.stashTabs)
            {
                if (tab.slots.Count != state.stashCapacity) throw new InvalidDataException("Invalid stash dimensions.");
                container(tab.slots);
            }
            var merchants = new HashSet<string>();
            foreach (var merchant in state.merchants)
            {
                registry.Resolve<MerchantDefinition>(merchant.contentId);
                if (!merchants.Add(merchant.contentId) || (merchant.stockInitialized ? merchant.capacity < 1 : merchant.capacity != 0 || merchant.currency.Count != 0) || merchant.stock.Count != merchant.capacity || merchant.reputationLevel < 0 || merchant.reputationExperience < 0)
                    throw new InvalidDataException("Invalid merchant state.");
                container(merchant.stock); container(merchant.currency);
            }
            if (owned.Count != items.Count) throw new InvalidDataException("Orphan account item.");
            var flaskIds = new HashSet<string>();
            foreach (var id in state.flasks)
                if (!string.IsNullOrEmpty(id) && (!state.inventory.Contains(id) || !flaskIds.Add(id) || !(registry.Resolve<BaseItemData>(items[id].contentId) is FlaskItemData)))
                    throw new InvalidDataException("Invalid equipped flask reference.");
            foreach (var item in state.items)
                if (!string.IsNullOrEmpty(item.originRunId) && (state.run == null || item.originRunId != state.run.runId || !IsRunning(state.run.phase)))
                    throw new InvalidDataException("Item refers to an inactive run.");
        }

        public static bool IsRunning(RunPhase phase) => phase == RunPhase.Active || phase == RunPhase.BossCleared;
    }

    // Only a candidate is edited. Disk commit precedes ownership replacement and notifications.
    public sealed class AccountTransactions
    {
        private AccountSnapshot current;
        private readonly EasySaveAccountStore store;
        private readonly AccountContentRegistry registry;
        private bool executing;
        public event Action<AccountSnapshot> Committed;
        public long Revision => current.revision;
        public AccountSnapshot Read() => ItemSnapshotCodec.CopyValues(current);
        public RunSnapshot ReadRun() => ItemSnapshotCodec.CopyValues(current.run);

        public AccountTransactions(AccountSnapshot initial, EasySaveAccountStore store, AccountContentRegistry registry)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            AccountInvariants.Validate(initial, registry);
            current = ItemSnapshotCodec.CopyValues(initial);
        }

        public bool Execute(string transactionId, long expectedRevision, Action<AccountSnapshot> mutation)
        {
            if (mutation == null) throw new ArgumentNullException(nameof(mutation));
            return ExecuteWithCandidate(transactionId, expectedRevision, () =>
            {
                var candidate = Read();
                mutation(candidate);
                return candidate;
            });
        }

        public bool ExecuteWithCandidate(string transactionId, long expectedRevision, Func<AccountSnapshot> createCandidate)
        {
            if (string.IsNullOrWhiteSpace(transactionId) || createCandidate == null) throw new ArgumentException("Transaction identity and mutation are required.");
            if (executing) throw new InvalidOperationException("Nested account transactions are not permitted.");
            if (current.lastTransactionId == transactionId) return false;
            if (expectedRevision != current.revision) throw new InvalidOperationException("Stale account revision.");
            executing = true;
            try
            {
                var candidate = createCandidate();
                AccountEquipmentPolicy.RecalculateCapacity(candidate, registry);
                candidate.revision = checked(current.revision + 1);
                candidate.lastTransactionId = transactionId;
                AccountInvariants.Validate(candidate, registry);
                store.Save(candidate, transactionId);
                current = candidate;
            }
            finally { executing = false; }
            // A listener cannot change the authoritative snapshot, or roll back a completed save.
            var listeners = Committed;
            if (listeners != null)
                foreach (Action<AccountSnapshot> listener in listeners.GetInvocationList())
                    try { listener(Read()); } catch (Exception error) { UnityEngine.Debug.LogException(error); }
            return true;
        }
    }

    public static class AccountEquipmentPolicy
    {
        public static void RecalculateCapacity(AccountSnapshot state, AccountContentRegistry registry)
        {
            int capacity = state.baseUnlockedSlots;
            foreach (var id in state.bags)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var item = state.items.Find(x => x.instanceId == id);
                if (item == null || !(registry.Resolve<BaseItemData>(item.contentId) is BagItemData bag))
                    throw new InvalidDataException("Invalid equipped bag.");
                capacity = checked(capacity + UnityEngine.Mathf.Max(0, bag.additionalSlots));
            }
            state.unlockedSlots = UnityEngine.Mathf.Clamp(capacity, 0, state.inventoryCapacity);
        }
    }
}
