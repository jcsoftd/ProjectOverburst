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
        public string ProjectionError { get; private set; }
        public bool NeedsProjectionRecovery => ProjectionError != null;
        public static bool ShouldRoute => Current != null && !Current.editing && !Current.restoring;
        internal AccountContentRegistry ContentRegistry => registry;
        internal PlayerAccountInventoryService Owner => account;
        public long Revision => transactions.Revision;
        public long PersistedRevision => transactions.PersistedRevision;
        public bool HasPendingSave => transactions.HasPendingSave;
        public bool FlushPendingSave() => !editing && !restoring && transactions.FlushPendingSave();
        public AccountSnapshot Read() => transactions.Read();
        public bool CanAcquireFromRun(string runId) => transactions.CanAcquireFromRun(runId);
        public RunSnapshot ReadRun() => transactions.ReadRun();

        public AccountGameplaySession(PlayerAccountInventoryService account, AccountContentRegistry registry, EasySaveAccountStore store, AccountSnapshot initial, bool deferDiskWrites = false)
        {
            this.account = account;
            this.registry = registry;
            transactions = new AccountTransactions(initial, store, registry, deferDiskWrites);
        }

        public void Attach() => Current = this;
        public void Detach() { if (Current == this) { Current = null; GameplayInputBlocker.Unblock(account); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => Current = null;

        public bool Execute(Func<bool> operation) => Execute(operation, false);

        public static bool RequiresPickupCheckpoint(ItemGrade grade) =>
            grade >= ItemGrade.Legendary && ItemGradeAvailabilityPolicy.IsEnabled(grade);

        public static bool AcquireWorldItem(PlayerInventory inventory, ItemData item)
        {
            if (inventory == null || item == null) return false;
            if (Current == null) return inventory.AddItem(item);
            if (Current.editing || Current.restoring) return false; // A pickup owns its completion boundary.
            bool checkpoint = RequiresPickupCheckpoint(item.grade);
            if (checkpoint)
            {
                CurrencyPickupBatch.DrainPending();
                if (PlayerProgression.Current != null && !PlayerProgression.Current.FlushPendingExperience()) return false;
            }
            try { return Current.Execute(() => inventory.AddItem(item), false, checkpoint); }
            catch (System.IO.IOException error) { Debug.LogError("아이템 획득 저장에 실패했습니다: " + error.Message); return false; }
        }

        // Only CurrencyWorldPickup may use this restricted inventory-only mutation path.
        internal static bool RunCurrencyAcquisition(Func<bool> operation)
        {
            try { return Current != null ? Current.Execute(operation, true) : operation(); }
            catch (System.IO.IOException error) { Debug.LogError("재화 획득을 확정하지 못했습니다: " + error.Message); return false; }
        }

        private bool Execute(Func<bool> operation, bool currencyOnly, bool saveImmediately = false)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (restoring) throw new InvalidOperationException("Cannot edit while restoring account state.");
            if (editing) return operation();
            EnsureProjectionReady();
            var before = currencyOnly ? transactions.CurrentForCurrencyCapture : transactions.Read();
            bool committed = false;
            editing = true;
            notifications.Clear();
            originalStackCounts.Clear();
            rollbackActions.Clear();
            try
            {
                if (!operation()) return false;
                committed = transactions.ExecuteWithCandidate(Guid.NewGuid().ToString("N"), before.revision,
                    () => currencyOnly
                        ? AccountGameplayProjection.CaptureCurrencyInventory(before, account.Inventory, registry)
                        : AccountGameplayProjection.Capture(before, account, registry), saveImmediately);
                if (committed && !currencyOnly)
                {
                    try { AccountPlayerProjection.ApplyCommittedBindings(transactions.Read(), account.Inventory, registry); }
                    catch (Exception error) { RequireProjectionRecovery(error); RestoreAuthoritativeProjection(); }
                }
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
                        if (needsRestore) RestoreAuthoritativeProjection();
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

        public bool GrantExperience(PlayerProgression progression, int amount)
        {
            if (progression == null) throw new ArgumentNullException(nameof(progression));
            if (editing || restoring) throw new InvalidOperationException("A gameplay command is already running.");
            if (NeedsProjectionRecovery) return false;
            editing = true;
            notifications.Clear();
            try
            {
                bool committed = transactions.GrantExperience(Guid.NewGuid().ToString("N"),
                    transactions.Revision, amount, out int level, out int experience);
                if (committed) progression.ApplyCommittedExperience(level, experience);
                return committed;
            }
            catch (System.IO.IOException error)
            {
                // Runtime progression is unchanged when candidate validation fails.
                Debug.LogError("계정 경험치 확정을 보류했습니다: " + error.Message);
                return false;
            }
            finally
            {
                editing = false;
                var queued = notifications.ToArray(); notifications.Clear();
                foreach (var notification in queued)
                    try { notification(); } catch (Exception error) { Debug.LogException(error); }
            }
        }

        // Run state commands edit the same authoritative account, then project only after disk commit.
        public bool ExecuteState(string transactionId, Action<AccountSnapshot> mutation)
        {
            if (editing || restoring) throw new InvalidOperationException("A gameplay command is already running.");
            EnsureProjectionReady();
            editing = true;
            notifications.Clear();
            try
            {
                bool committed = transactions.Execute(transactionId, transactions.Revision, mutation, saveImmediately: true);
                if (!committed) return false;
                RestoreAuthoritativeProjection();
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

        private void EnsureProjectionReady()
        {
            if (NeedsProjectionRecovery)
                throw new System.IO.IOException("저장된 계정 상태의 화면 복구를 기다리는 중입니다.");
        }

        private void RequireProjectionRecovery(Exception error)
        {
            if (ProjectionError != error.Message)
                Debug.LogError("계정 화면 복원에 실패했습니다. 확정된 저장 상태에서 복구합니다: " + error.Message);
            ProjectionError = error.Message;
            GameplayInputBlocker.Block(account);
        }

        // A projection error must never turn a durable commit into a failed command.
        private bool RestoreAuthoritativeProjection()
        {
            restoring = true;
            try
            {
                AccountGameplayProjection.Restore(transactions.Read(), account, registry);
                ProjectionError = null;
                GameplayInputBlocker.Unblock(account);
                return true;
            }
            catch (Exception error) { RequireProjectionRecovery(error); return false; }
            finally { restoring = false; }
        }

        public bool TryRecoverProjection()
        {
            if (!NeedsProjectionRecovery) return true;
            if (editing || restoring) return false;
            return RestoreAuthoritativeProjection();
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
