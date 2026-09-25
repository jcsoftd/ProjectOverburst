using System;

namespace Overburst.Persistence
{
    // Entry readiness is supplied by the world adapter; the map is consumed only by Activate.
    public sealed class AccountRunSession
    {
        private readonly AccountGameplaySession account;
        public AccountRunSession(AccountGameplaySession account)
        { this.account = account ?? throw new ArgumentNullException(nameof(account)); }

        public bool Prepare(string runId, MapInstanceState map, string mapItemId = null)
        {
            bool committed = account.ExecuteState("prepare-" + runId,
                state => AccountRunCommands.PrepareEntry(state, runId, map, mapItemId));
            if (committed) WorldSessionState.SetPhase(WorldPhase.Loading);
            return committed;
        }

        public bool Activate(string runId)
        {
            bool committed = account.ExecuteState("activate-" + runId,
                state => AccountRunCommands.Activate(state, runId));
            if (committed) WorldSessionState.SetPhase(WorldPhase.Run);
            return committed;
        }

        public bool Transfer(string runId, string objectId, string itemId, int tab, int slot)
            => account.ExecuteState("transfer-" + runId + "-" + objectId,
                state => AccountRunCommands.Transfer(state, runId, objectId, itemId, tab, slot));

        public bool Transfer(string runId, string objectId, string itemId)
            => account.ExecuteState("transfer-" + runId + "-" + objectId,
                state => AccountRunCommands.Transfer(state, runId, objectId, itemId, account.ContentRegistry));

        public bool ClearBoss(string runId, long utcTicks)
        {
            var run = account.Read().run;
            if (run != null && run.runId == runId && run.phase == RunPhase.BossCleared) return false;
            return account.ExecuteState("boss-" + runId,
                state => AccountRunCommands.ClearBoss(state, runId, utcTicks));
        }

        public bool Fail(string runId)
        {
            var run = account.Read().run;
            if (run != null && run.runId == runId && run.phase == RunPhase.Failed) return false;
            bool committed = account.ExecuteState("fail-" + runId,
                state => AccountRunCommands.Fail(state, runId));
            if (committed) WorldSessionState.SetPhase(WorldPhase.Settling);
            return committed;
        }

        public bool Extract(string runId)
        {
            var run = account.Read().run;
            if (run != null && run.runId == runId && run.phase == RunPhase.Extracted) return false;
            bool committed = account.ExecuteState("extract-" + runId,
                state => AccountRunCommands.Extract(state, runId));
            if (committed) WorldSessionState.SetPhase(WorldPhase.Settling);
            return committed;
        }
    }
}
