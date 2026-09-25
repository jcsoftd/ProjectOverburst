using System;

namespace Overburst.Persistence
{
    public enum RunOutcome { None, Failed, Extracted }
    // Entry readiness is supplied by the world adapter; the map is consumed only by Activate.
    public sealed class AccountRunSession
    {
        private readonly AccountGameplaySession account;
        public AccountRunSession(AccountGameplaySession account)
        { this.account = account ?? throw new ArgumentNullException(nameof(account)); }

        public const int ExitDelaySeconds = 180;

        public RunOutcome ResolveOutcome(long utcTicks, bool playerDead, bool portalRequested = false, bool abandoned = false)
        {
            var run = account.ReadRun();
            if (run == null || run.phase == RunPhase.Failed || run.phase == RunPhase.Extracted) return RunOutcome.None;
            if (playerDead || abandoned)
                return Fail(run.runId) ? RunOutcome.Failed : RunOutcome.None;
            if (run.phase != RunPhase.BossCleared) return RunOutcome.None;
            bool expired = utcTicks >= run.bossClearedAtUtcTicks
                && utcTicks - run.bossClearedAtUtcTicks >= TimeSpan.TicksPerSecond * ExitDelaySeconds;
            return (portalRequested || expired) && Extract(run.runId) ? RunOutcome.Extracted : RunOutcome.None;
        }

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
            var run = account.ReadRun();
            if (run != null && run.runId == runId && run.phase == RunPhase.BossCleared) return false;
            return account.ExecuteState("boss-" + runId,
                state => AccountRunCommands.ClearBoss(state, runId, utcTicks));
        }

        public WorldItemPickup ClearBossWithMapReward(string runId, MapItemData mapDefinition,
            UnityEngine.Vector3 position, System.Collections.Generic.IReadOnlyList<MapOptionRoll> rolledOptions = null)
        {
            var run = account.ReadRun();
            if (run == null || run.runId != runId) throw new InvalidOperationException("Stale run identity.");
            if (run.phase == RunPhase.BossCleared) return null;
            if (run.phase != RunPhase.Active) throw new InvalidOperationException("Run is not active.");
            var reward = MapRewardPolicy.CreateBossMap(mapDefinition, account.ContentRegistry, run.map.level,
                UnityEngine.Random.value, UnityEngine.Random.value, runId, rolledOptions);
            var pickup = WorldItemDropFactory.CreateWorldPickup(reward, position, account.Owner.Inventory,
                PlayerContext.Instance?.CurrentActor?.transform);
            if (pickup == null) throw new InvalidOperationException("Boss map pickup could not be created.");
            pickup.gameObject.SetActive(false);
            bool committed = false;
            try
            {
                committed = ClearBoss(runId, DateTime.UtcNow.Ticks);
                if (!committed) return null;
                pickup.gameObject.SetActive(true);
                return pickup;
            }
            finally
            {
                if (!committed) UnityEngine.Object.Destroy(pickup.gameObject);
            }
        }

        public bool Fail(string runId)
        {
            var run = account.ReadRun();
            if (run != null && run.runId == runId && run.phase == RunPhase.Failed) return false;
            bool committed = account.ExecuteState("fail-" + runId,
                state => AccountRunCommands.Fail(state, runId));
            if (committed) WorldSessionState.SetPhase(WorldPhase.Settling);
            return committed;
        }

        public bool Extract(string runId)
        {
            var run = account.ReadRun();
            if (run != null && run.runId == runId && run.phase == RunPhase.Extracted) return false;
            bool committed = account.ExecuteState("extract-" + runId,
                state =>
                {
                    AccountRunCommands.Extract(state, runId);
                    AccountMerchantProjection.RefreshForSuccessfulRun(state, account.ContentRegistry);
                });
            if (committed) WorldSessionState.SetPhase(WorldPhase.Settling);
            return committed;
        }
    }
}
