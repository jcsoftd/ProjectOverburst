using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.Persistence;

namespace Overburst.Quests
{
    // Pure candidate mutations. Only AccountGameplaySession publishes or saves them.
    public static class AccountQuestCommands
    {
        public static bool IsReady(QuestSnapshot q) => q != null && q.state == SavedQuestState.Active
            && q.objectives.Count > 0 && q.objectives.All(o => o.current == o.required);

        public static QuestAvailability Availability(AccountSnapshot state, QuestDefinition definition)
        {
            var saved = state.quests.Find(q => q.questId == definition.id);
            if (saved != null) return saved.state == SavedQuestState.Completed ? QuestAvailability.Completed
                : IsReady(saved) ? QuestAvailability.ReadyToReport : QuestAvailability.Active;
            return state.level < definition.minimumLevel || (!string.IsNullOrEmpty(definition.prerequisiteId)
                && !state.quests.Exists(q => q.questId == definition.prerequisiteId && q.state == SavedQuestState.Completed))
                ? QuestAvailability.Locked : QuestAvailability.Available;
        }

        public static bool Accept(AccountSnapshot state, QuestDefinition definition, string merchantId, AccountContentRegistry registry)
        {
            if (!definition || merchantId != definition.giverMerchantId) throw new InvalidOperationException("이 상인의 의뢰가 아닙니다.");
            if (Availability(state, definition) != QuestAvailability.Available) return false;
            registry.Resolve<MerchantDefinition>(definition.giverMerchantId);
            registry.Resolve<MerchantDefinition>(definition.turnInMerchantId);
            var gold = definition.rewardGold == 0 ? null : registry.ResolvedEntries.Select(e => e.asset)
                .OfType<CurrencyItemData>().FirstOrDefault(c => c.currencyType == CurrencyType.Gold)
                ?? throw new InvalidDataException("Gold reward content is missing.");
            state.quests.Add(new QuestSnapshot
            {
                questId = definition.id, definitionVersion = definition.version, giverMerchantId = merchantId,
                turnInMerchantId = definition.turnInMerchantId, title = definition.title, description = definition.description,
                journalDescription = definition.journalDescription, readyDescription = definition.readyDescription,
                completedDescription = definition.completedDescription,
                acceptedRevision = checked(state.revision + 1),
                objectives = definition.objectives.Select(o => new QuestObjectiveSnapshot
                    { kind = o.kind, targetId = o.targetId, label = o.label, required = o.required }).ToList(),
                rewardGoldContentId = gold ? registry.IdFor(gold) : null,
                rewardGold = definition.rewardGold, rewardExperience = definition.rewardExperience,
                rewardReputation = definition.rewardReputation
            });
            if (string.IsNullOrEmpty(state.trackedQuestId)) state.trackedQuestId = definition.id;
            return true;
        }

        public static bool Track(AccountSnapshot state, string questId)
        {
            if (!string.IsNullOrEmpty(questId) && !state.quests.Exists(q => q.questId == questId && q.state == SavedQuestState.Active))
                throw new InvalidOperationException("진행 중인 퀘스트만 표시할 수 있습니다.");
            if (state.trackedQuestId == questId) return false;
            state.trackedQuestId = questId;
            return true;
        }

        public static bool Progress(AccountSnapshot state, QuestObjectiveKind kind, string targetId, int amount,
            string runId = null, long observedRevision = long.MaxValue)
        {
            if (amount < 1) return false;
            if ((kind == QuestObjectiveKind.KillEnemy || kind == QuestObjectiveKind.EnterRun || kind == QuestObjectiveKind.AcquireMap)
                && (state.run == null || state.run.runId != runId || !AccountInvariants.IsRunning(state.run.phase))) return false;
            bool changed = false;
            foreach (var q in state.quests)
            {
                if (q.state != SavedQuestState.Active || q.acceptedRevision > observedRevision) continue;
                for (int i = 0; i < q.objectives.Count; i++)
                {
                    var o = q.objectives[i];
                    if (o.kind != kind || o.targetId != targetId || o.current >= o.required) continue;
                    if (kind == QuestObjectiveKind.SelectMap || (kind == QuestObjectiveKind.EnterRun && targetId == "run.map"))
                    {
                        bool priorComplete = true;
                        for (int prior = 0; prior < i; prior++)
                            if (q.objectives[prior].current != q.objectives[prior].required) priorComplete = false;
                        if (!priorComplete) continue;
                    }
                    o.current = (int)Math.Min(o.required, (long)o.current + amount);
                    changed = true;
                }
            }
            return changed;
        }

        public static void CaptureMapAcquisitions(AccountSnapshot baseline, AccountSnapshot candidate)
        {
            if (candidate.run == null || !AccountInvariants.IsRunning(candidate.run.phase)) return;
            int acquired = 0;
            foreach (var item in candidate.items)
            {
                if (item.map == null || item.originRunId != candidate.run.runId || !candidate.inventory.Contains(item.instanceId)) continue;
                var previous = baseline.items.Find(x => x.instanceId == item.instanceId);
                acquired = checked(acquired + Math.Max(0, item.count - (previous?.count ?? 0)));
            }
            Progress(candidate, QuestObjectiveKind.AcquireMap, "map.any", acquired, candidate.run.runId, baseline.revision);
        }

        public static bool TurnIn(AccountSnapshot state, string questId, string merchantId, AccountContentRegistry registry)
        {
            var q = state.quests.Find(x => x.questId == questId);
            if (!IsReady(q)) return false;
            if (q.turnInMerchantId != merchantId) throw new InvalidOperationException("보고할 상인을 확인해 주세요.");
            registry.Resolve<MerchantDefinition>(merchantId);
            // Gold capacity is planned before any reward or completion is changed.
            GrantGold(state, q.rewardGoldContentId, q.rewardGold, registry);
            GrantExperience(state, q.rewardExperience);
            var merchant = state.merchants.Find(m => m.contentId == merchantId);
            if (merchant == null)
            {
                merchant = new MerchantSnapshot { contentId = merchantId, reputationPolicyVersion = MerchantReputationPolicy.Version };
                state.merchants.Add(merchant);
            }
            merchant.reputationTotalXp = MerchantReputationPolicy.Clamp((int)Math.Min(int.MaxValue,
                (long)merchant.reputationTotalXp + q.rewardReputation));
            q.state = SavedQuestState.Completed;
            q.completedRevision = checked(state.revision + 1);
            if (state.trackedQuestId == q.questId) state.trackedQuestId = null;
            return true;
        }

        private static void GrantGold(AccountSnapshot state, string contentId, int amount, AccountContentRegistry registry)
        {
            if (amount == 0) return;
            var gold = registry.Resolve<CurrencyItemData>(contentId);
            if (gold.currencyType != CurrencyType.Gold) throw new InvalidDataException("Invalid quest gold contract.");
            int maxStack = Math.Max(1, gold.maxStack), remaining = amount;
            var merges = new List<KeyValuePair<ItemSnapshot, int>>();
            var empty = new List<int>();
            for (int i = 0; i < state.unlockedSlots; i++)
            {
                var id = state.inventory[i];
                if (string.IsNullOrEmpty(id)) { empty.Add(i); continue; }
                var item = state.items.Single(x => x.instanceId == id);
                if (item.contentId != contentId || !string.IsNullOrEmpty(item.originRunId)) continue;
                int added = Math.Min(remaining, Math.Max(0, maxStack - item.count));
                if (added > 0) { merges.Add(new KeyValuePair<ItemSnapshot, int>(item, added)); remaining -= added; }
            }
            int slotsNeeded = (int)(((long)remaining + maxStack - 1) / maxStack);
            if (slotsNeeded > empty.Count) throw new InvalidOperationException("보상을 받을 인벤토리 공간이 부족합니다.");
            foreach (var pair in merges) pair.Key.count = checked(pair.Key.count + pair.Value);
            for (int i = 0; remaining > 0; i++)
            {
                int added = Math.Min(remaining, maxStack);
                var item = new ItemSnapshot
                {
                    contentId = contentId, instanceId = Guid.NewGuid().ToString("N"),
                    balanceVersion = OverburstCombatBalance.ItemBalanceVersion, acquisitionOrder = state.nextAcquisitionOrder++,
                    level = 1, grade = ItemGrade.Common, count = added
                };
                state.items.Add(item); state.inventory[empty[i]] = item.instanceId; remaining -= added;
            }
        }

        private static void GrantExperience(AccountSnapshot state, int amount)
        {
            if (amount <= 0 || state.level >= OverburstGrowthRules.MaximumLevel) return;
            state.experience = checked(state.experience + amount);
            while (state.level < OverburstGrowthRules.MaximumLevel && state.experience >= OverburstGrowthRules.ExperienceToNext(state.level))
            { state.experience -= OverburstGrowthRules.ExperienceToNext(state.level); state.level++; }
            if (state.level >= OverburstGrowthRules.MaximumLevel) state.experience = 0;
            state.skillTree = AccountSkillTree.GrantEligible(state.skillTree, state.level);
        }

        public static void Validate(AccountSnapshot state, AccountContentRegistry registry)
        {
            if (state.quests == null) throw new InvalidDataException("Missing saved quests.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var q in state.quests)
            {
                if (q == null || string.IsNullOrWhiteSpace(q.questId) || !ids.Add(q.questId) || q.definitionVersion < 1
                    || !Enum.IsDefined(typeof(SavedQuestState), q.state) || q.acceptedRevision < 1 || q.acceptedRevision > state.revision
                    || q.rewardGold < 0 || q.rewardExperience < 0 || q.rewardReputation < 0
                    || string.IsNullOrWhiteSpace(q.title) || q.description == null || q.objectives == null || q.objectives.Count == 0
                    || (q.state == SavedQuestState.Completed ? q.completedRevision < q.acceptedRevision || q.completedRevision > state.revision : q.completedRevision != 0))
                    throw new InvalidDataException("Invalid saved quest contract.");
                registry.Resolve<MerchantDefinition>(q.giverMerchantId); registry.Resolve<MerchantDefinition>(q.turnInMerchantId);
                if (q.rewardGold > 0 && registry.Resolve<CurrencyItemData>(q.rewardGoldContentId).currencyType != CurrencyType.Gold)
                    throw new InvalidDataException("Invalid saved quest reward.");
                foreach (var o in q.objectives)
                    if (o == null || !Enum.IsDefined(typeof(QuestObjectiveKind), o.kind) || string.IsNullOrWhiteSpace(o.targetId)
                        || string.IsNullOrWhiteSpace(o.label) || o.required < 1 || o.current < 0 || o.current > o.required
                        || (q.state == SavedQuestState.Completed && o.current != o.required))
                        throw new InvalidDataException("Invalid saved quest objective.");
            }
            if (!string.IsNullOrEmpty(state.trackedQuestId) && !state.quests.Exists(q => q.questId == state.trackedQuestId && q.state == SavedQuestState.Active))
                throw new InvalidDataException("Invalid tracked quest.");
        }
    }
}
