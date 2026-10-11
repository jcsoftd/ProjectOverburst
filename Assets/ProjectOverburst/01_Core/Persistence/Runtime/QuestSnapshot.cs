using System;
using System.Collections.Generic;
using Overburst.Quests;

namespace Overburst.Persistence
{
    public enum SavedQuestState { Active, Completed }
    [Serializable]
    public sealed class QuestSnapshot
    {
        public string questId;
        public int definitionVersion;
        public string giverMerchantId;
        public string turnInMerchantId;
        public SavedQuestState state;
        public string title;
        public string description;
        public string journalDescription;
        public string readyDescription;
        public string completedDescription;
        public List<QuestObjectiveSnapshot> objectives = new List<QuestObjectiveSnapshot>();
        public string rewardGoldContentId;
        public int rewardGold;
        public int rewardExperience;
        public int rewardReputation;
        public long acceptedRevision;
        public long completedRevision;
    }
    [Serializable]
    public sealed class QuestObjectiveSnapshot
    {
        public QuestObjectiveKind kind;
        public string targetId;
        public string label;
        public int required;
        public int current;
    }
}
