using System;
using System.Collections.Generic;
using System.IO;

namespace Overburst.Persistence
{
    public static class QuestAccountMigration
    {
        public static AccountSnapshot Upgrade(AccountSnapshot source)
        {
            if (source == null || source.schemaVersion != 2) throw new InvalidDataException("Expected schema 2 account.");
            var result = ItemSnapshotCodec.CopyValues(source);
            UpgradePersonal(result);
            return result;
        }

        private static void UpgradePersonal(AccountSnapshot state)
        {
            if (state.schemaVersion != 2) throw new InvalidDataException("Unsupported dormant character schema.");
            state.schemaVersion = EasySaveAccountStore.SchemaVersion;
            state.quests = state.quests ?? new List<QuestSnapshot>();
            foreach (var merchant in state.merchants)
            {
                if (merchant.reputationPolicyVersion > MerchantReputationPolicy.Version)
                    throw new InvalidDataException("Unsupported reputation policy.");
                if (merchant.reputationPolicyVersion == 0)
                    merchant.reputationTotalXp = MerchantReputationPolicy.Migrate(merchant.reputationLevel, merchant.reputationExperience);
                merchant.reputationPolicyVersion = MerchantReputationPolicy.Version;
                merchant.reputationLevel = merchant.reputationExperience = 0;
            }
            if (state.characters != null)
                foreach (var character in state.characters)
                    if (character.personal != null) UpgradePersonal(character.personal);
        }
    }
}
