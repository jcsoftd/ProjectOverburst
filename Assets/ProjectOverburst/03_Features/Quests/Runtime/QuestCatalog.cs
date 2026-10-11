using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Overburst.Quests
{
    public static class QuestCatalog
    {
        private static Dictionary<string, QuestDefinition> definitions;
        public static IReadOnlyCollection<QuestDefinition> All { get { EnsureLoaded(); return definitions.Values; } }
        public static QuestDefinition Find(string id)
        {
            EnsureLoaded();
            return id != null && definitions.TryGetValue(id, out var found) ? found : null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => definitions = null;
        private static void EnsureLoaded()
        {
            if (definitions != null) return;
            var values = Resources.LoadAll<QuestDefinition>("Quests");
            Validate(values);
            definitions = values.ToDictionary(q => q.id, StringComparer.Ordinal);
        }
        public static void Validate(IEnumerable<QuestDefinition> values)
        {
            var table = new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);
            foreach (var q in values)
            {
                if (!q || string.IsNullOrWhiteSpace(q.id) || !table.TryAdd(q.id, q)
                    || q.version < 1 || q.minimumLevel < 1 || q.minimumLevel > 100
                    || string.IsNullOrWhiteSpace(q.giverMerchantId) || string.IsNullOrWhiteSpace(q.turnInMerchantId)
                    || string.IsNullOrWhiteSpace(q.title) || string.IsNullOrWhiteSpace(q.description)
                    || q.rewardGold < 0 || q.rewardExperience < 0 || q.rewardReputation < 0
                    || q.objectives == null || q.objectives.Length == 0)
                    throw new InvalidDataException("Invalid or duplicate quest definition.");
                foreach (var o in q.objectives)
                    if (o == null || !Enum.IsDefined(typeof(QuestObjectiveKind), o.kind)
                        || string.IsNullOrWhiteSpace(o.targetId) || string.IsNullOrWhiteSpace(o.label) || o.required < 1
                        || (o.kind == QuestObjectiveKind.KillEnemy && o.targetId != "enemy.any-run")
                        || (o.kind == QuestObjectiveKind.EnterRun && o.targetId != "run.any" && o.targetId != "run.map")
                        || (o.kind == QuestObjectiveKind.AcquireMap && o.targetId != "map.any")
                        || (o.kind == QuestObjectiveKind.SelectMap && o.targetId != "portal.map"))
                        throw new InvalidDataException("Unsupported quest objective: " + q.id);
            }
            foreach (var q in table.Values)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { q.id };
                var next = q.prerequisiteId;
                while (!string.IsNullOrEmpty(next))
                {
                    if (!seen.Add(next) || !table.TryGetValue(next, out var prerequisite))
                        throw new InvalidDataException("Missing or cyclic quest prerequisite: " + q.id);
                    next = prerequisite.prerequisiteId;
                }
            }
        }
    }
}
