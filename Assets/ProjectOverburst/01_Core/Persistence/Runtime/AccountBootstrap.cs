using System;
using System.IO;
using UnityEngine;

namespace Overburst.Persistence
{
    public static class AccountBootstrap
    {
        public static bool Attempted { get; private set; }
        public static bool Ready { get; private set; }
        public static string Error { get; private set; }
        public static string SaveDirectory { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Attempted = Ready = false;
            Error = SaveDirectory = null;
        }

        public static bool Initialize(PlayerAccountInventoryService account)
        {
            if (Attempted) return Ready;
            Attempted = true;
            try
            {
                if (account == null || account.Inventory == null || account.Stash == null
                    || PlayerProgression.Current == null || PlayerContext.Instance?.CurrentActor == null)
                    throw new InvalidOperationException("Persistent player/account services are not ready.");
                string testDirectory = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
                SaveDirectory = string.IsNullOrWhiteSpace(testDirectory)
                    ? Path.Combine(Application.persistentDataPath, "Account") : Path.GetFullPath(testDirectory);
                var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
                if (registry == null) throw new InvalidDataException("Account content registry is missing.");
                var store = new EasySaveAccountStore(SaveDirectory);
                var state = store.Load();
                if (state == null)
                {
                    // Initial capture imports the existing legacy progression once.
                    state = AccountGameplayProjection.Capture(new AccountSnapshot(), account, registry);
                    state.lastTransactionId = "new-account-" + Guid.NewGuid().ToString("N");
                    store.Save(state, state.lastTransactionId);
                }
                else
                {
                    AccountInvariants.Validate(state, registry);
                    var recovery = new AccountTransactions(state, store, registry);
                    if (state.run != null && state.run.phase != RunPhase.Extracted && state.run.phase != RunPhase.Failed)
                    {
                        recovery.Execute("recover-" + state.run.runId, state.revision,
                            candidate => AccountRunCommands.RecoverInterrupted(candidate));
                        state = recovery.Read();
                    }
                }
                AccountGameplayProjection.Restore(state, account, registry);
                new AccountGameplaySession(account, registry, store, state).Attach();
                Ready = true;
                return true;
            }
            catch (Exception error)
            {
                Error = "저장 데이터를 불러오지 못했습니다. 파일을 보존했습니다.\n" + error.Message;
                Debug.LogError("[AccountBootstrap] " + Error);
                return false;
            }
        }
    }
}
