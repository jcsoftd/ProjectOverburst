using System;

namespace Overburst.Persistence
{
    public static class MapAccountMigration
    {
        public static AccountSnapshot Load(EasySaveAccountStore store, AccountContentRegistry registry)
        {
            var state = store.Load();
            if (state == null) return null;
            bool maps = NeedsUpgrade(state), bags = BagAccountMigration.NeedsGoldUpgrade(state);
            bool testerSupport = NeedsTesterSupport(state);
            if (!maps && !bags && !testerSupport) return state;
            // Existing transaction publishes only after a verified durable write.
            var transaction = new AccountTransactions(state, store, registry);
            transaction.Execute((testerSupport ? "tester-support-v1-" : bags ? "bag-quality-v2-" : "map-generation-v1-") + state.revision, state.revision,
                candidate =>
                {
                    if (maps) Upgrade(candidate);
                    if (bags) BagAccountMigration.UpgradeGoldOptions(candidate, registry);
                    if (testerSupport) GrantTesterSupport(candidate, registry);
                }, true);
            return transaction.Read();
        }

        private static bool NeedsTesterSupport(AccountSnapshot state)
        {
            if (NewAccountFactory.CanGrantTesterSupport(state)) return true;
            if (state.characters != null)
                foreach (var character in state.characters)
                    if (character.personal != null && NeedsTesterSupport(character.personal)) return true;
            return false;
        }

        private static void GrantTesterSupport(AccountSnapshot state, AccountContentRegistry registry)
        {
            NewAccountFactory.GrantTesterSupport(state, registry);
            if (state.characters == null) return;
            foreach (var character in state.characters)
                if (character.personal != null)
                {
                    character.personal.nextAcquisitionOrder = Math.Max(state.nextAcquisitionOrder, character.personal.nextAcquisitionOrder);
                    GrantTesterSupport(character.personal, registry);
                    state.nextAcquisitionOrder = Math.Max(state.nextAcquisitionOrder, character.personal.nextAcquisitionOrder);
                }
        }
        public static bool NeedsUpgrade(AccountSnapshot state)
        {
            foreach (var item in state.items)
                if (item.map != null && item.map.generationVersion == 0) return true;
            if (state.run?.map != null && state.run.map.generationVersion == 0) return true;
            if (state.characters != null)
                foreach (var character in state.characters)
                    if (character.personal != null && NeedsUpgrade(character.personal)) return true;
            return false;
        }
        public static void Upgrade(AccountSnapshot state)
        {
            foreach (var item in state.items)
                if (item.map != null) MapGenerationPolicy.Initialize(item.map, item.instanceId);
            if (state.run?.map != null)
                MapGenerationPolicy.Initialize(state.run.map, string.IsNullOrEmpty(state.run.mapInstanceId) ? state.run.runId : state.run.mapInstanceId);
            if (state.characters != null)
                foreach (var character in state.characters)
                    if (character.personal != null) Upgrade(character.personal);
        }
    }
}
