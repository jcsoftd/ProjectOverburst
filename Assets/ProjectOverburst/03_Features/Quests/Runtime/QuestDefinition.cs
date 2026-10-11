using System;
using UnityEngine;

namespace Overburst.Quests
{
    public enum QuestObjectiveKind { KillEnemy, TalkNpc, EnterRun, AcquireMap, SelectMap }
    public enum QuestAvailability { Locked, Available, Active, ReadyToReport, Completed }

    [Serializable]
    public sealed class QuestObjectiveDefinition
    {
        public QuestObjectiveKind kind;
        public string targetId;
        public string label;
        public int required = 1;
    }

    [CreateAssetMenu(menuName = "OVERBURST/Quest")]
    public sealed class QuestDefinition : ScriptableObject
    {
        public string id;
        public int version = 1;
        public string giverMerchantId;
        public string turnInMerchantId;
        public string prerequisiteId;
        public int minimumLevel = 1;
        public int sortOrder;
        public string title;
        [TextArea(3, 12)] public string description;
        [TextArea(3, 12)] public string journalDescription;
        [TextArea(3, 12)] public string readyDescription;
        [TextArea(3, 12)] public string completedDescription;
        public QuestObjectiveDefinition[] objectives = Array.Empty<QuestObjectiveDefinition>();
        public int rewardGold;
        public int rewardExperience;
        public int rewardReputation;
    }
}
