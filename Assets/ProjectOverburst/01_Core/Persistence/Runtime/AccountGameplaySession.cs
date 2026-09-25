using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Overburst.Persistence
{
    public sealed class AccountGameplaySession
    {
        public static AccountGameplaySession Current { get; private set; }
        private readonly PlayerAccountInventoryService account;
        private readonly AccountContentRegistry registry;
        private readonly AccountTransactions transactions;
        private readonly List<Action> notifications = new List<Action>();
        private readonly Dictionary<ItemData, int> originalStackCounts = new Dictionary<ItemData, int>();
        private readonly List<Action> rollbackActions = new List<Action>();
        private bool editing;
        private bool restoring;
        public bool IsEditing => editing;
        public static bool ShouldRoute => Current != null && !Current.editing && !Current.restoring;
        public long Revision => transactions.Revision;
        public AccountSnapshot Read() => transactions.Read();

        public AccountGameplaySession(PlayerAccountInventoryService account, AccountContentRegistry registry, EasySaveAccountStore store, AccountSnapshot initial)
        {
            this.account = account;
            this.registry = registry;
            transactions = new AccountTransactions(initial, store, registry);
        }

        public void Attach() => Current = this;
        public void Detach() { if (Current == this) Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => Current = null;

        public bool Execute(Func<bool> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (restoring) throw new InvalidOperationException("Cannot edit while restoring account state.");
            if (editing) return operation();
            var before = transactions.Read();
            bool committed = false;
            editing = true;
            notifications.Clear();
            originalStackCounts.Clear();
            rollbackActions.Clear();
            try
            {
                if (!operation()) return false;
                committed = transactions.ExecuteWithCandidate(Guid.NewGuid().ToString("N"), before.revision,
                    () => AccountGameplayProjection.Capture(before, account, registry));
                return committed;
            }
            finally
            {
                try
                {
                    if (!committed)
                    {
                        for (int i = rollbackActions.Count - 1; i >= 0; i--)
                            try { rollbackActions[i](); } catch (Exception error) { Debug.LogException(error); }
                        foreach (var pair in originalStackCounts) pair.Key.stackCount = pair.Value;
                        notifications.Clear();
                        bool needsRestore;
                        try { needsRestore = !ES3.Serialize(before).SequenceEqual(ES3.Serialize(AccountGameplayProjection.Capture(before, account, registry))); }
                        catch { needsRestore = true; }
                        if (needsRestore)
                        {
                            restoring = true;
                            try { AccountGameplayProjection.Restore(before, account, registry); }
                            finally { restoring = false; }
                        }
                    }
                }
                finally
                {
                    editing = false;
                    originalStackCounts.Clear();
                    rollbackActions.Clear();
                    var queued = notifications.ToArray(); notifications.Clear();
                    foreach (var notification in queued)
                        try { notification(); } catch (Exception error) { Debug.LogException(error); }
                }
            }
        }

        // Run state commands edit the same authoritative account, then project only after disk commit.
        public bool ExecuteState(string transactionId, Action<AccountSnapshot> mutation)
        {
            if (editing || restoring) throw new InvalidOperationException("A gameplay command is already running.");
            editing = true;
            notifications.Clear();
            try
            {
                bool committed = transactions.Execute(transactionId, transactions.Revision, mutation);
                if (!committed) return false;
                restoring = true;
                try { AccountGameplayProjection.Restore(transactions.Read(), account, registry); }
                finally { restoring = false; }
                return true;
            }
            finally
            {
                editing = false;
                var queued = notifications.ToArray(); notifications.Clear();
                foreach (var notification in queued)
                    try { notification(); } catch (Exception error) { Debug.LogException(error); }
            }
        }

        public static bool Run(Func<bool> operation)
        {
            try { return Current != null ? Current.Execute(operation) : operation(); }
            catch (System.IO.IOException error) { Debug.LogError("계정 저장에 실패해 변경을 취소했습니다: " + error.Message); return false; }
        }

        public static void TrackStack(ItemData item)
        {
            if (item != null && Current != null && Current.editing && !Current.originalStackCounts.ContainsKey(item))
                Current.originalStackCounts.Add(item, item.stackCount);
        }

        public static void OnRollback(Action action)
        {
            if (Current != null && Current.editing) Current.rollbackActions.Add(action);
        }

        public static void Notify(Action notification)
        {
            if (Current != null && Current.editing)
            {
                if (!Current.notifications.Contains(notification)) Current.notifications.Add(notification);
                return;
            }
            notification();
        }
    }
}
