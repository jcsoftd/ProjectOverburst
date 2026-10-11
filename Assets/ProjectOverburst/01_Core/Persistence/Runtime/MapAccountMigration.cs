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
            if (!maps && !bags) return state;
            // Existing transaction publishes only after a verified durable write.
            var transaction = new AccountTransactions(state, store, registry);
            transaction.Execute((bags ? "bag-quality-v2-" : "map-generation-v1-") + state.revision, state.revision,
                candidate =>
                {
                    if (maps) Upgrade(candidate);
                    if (bags) BagAccountMigration.UpgradeGoldOptions(candidate, registry);
                }, true);
            return transaction.Read();
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
