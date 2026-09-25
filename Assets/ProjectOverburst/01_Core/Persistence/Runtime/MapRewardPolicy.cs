using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Persistence
{
    public static class MapRewardPolicy
    {
        // Initial tuning only: +7 remains possible but much rarer than +1.
        private static readonly int[] DefaultUpgradeWeights = { 10000, 2000, 400, 80, 16, 3, 1 };

        public static int BossMapLevel(int dungeonLevel, float levelRoll, IReadOnlyList<int> upgradeWeights = null)
        {
            if (dungeonLevel < 1 || dungeonLevel > 100) throw new ArgumentOutOfRangeException(nameof(dungeonLevel));
            if (float.IsNaN(levelRoll) || float.IsInfinity(levelRoll) || levelRoll < 0f || levelRoll > 1f)
                throw new ArgumentOutOfRangeException(nameof(levelRoll));
            var weights = upgradeWeights ?? DefaultUpgradeWeights;
            if (weights.Count != 7) throw new ArgumentException("Seven upgrade weights are required.");
            long total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0) throw new ArgumentException("Every upgrade must remain possible.");
                total += weights[i];
            }
            double pick = Math.Min(levelRoll, 0.999999999d) * total;
            for (int i = 0; i < weights.Count; i++)
            {
                pick -= weights[i];
                if (pick < 0d) return Math.Min(100, dungeonLevel + i + 1);
            }
            return Math.Min(100, dungeonLevel + 7);
        }

        public static ItemData CreateBossMap(MapItemData definition, AccountContentRegistry registry,
            int dungeonLevel, float levelRoll, float gradeRoll, string runId,
            IReadOnlyList<MapOptionRoll> rolledOptions = null)
        {
            if (definition == null || registry == null || string.IsNullOrWhiteSpace(runId))
                throw new ArgumentException("Registered map content and run identity are required.");
            if (float.IsNaN(gradeRoll) || float.IsInfinity(gradeRoll) || gradeRoll < 0f || gradeRoll > 1f)
                throw new ArgumentOutOfRangeException(nameof(gradeRoll));
            string contentId = registry.IdFor(definition);
            int level = BossMapLevel(dungeonLevel, levelRoll);
            ItemGrade grade = ItemGradeAvailabilityPolicy.ResolveWeightedGrade(gradeRoll,
                ItemGradeAvailabilityPolicy.IsEnabled(ItemGrade.Cursed));
            var item = new ItemData(definition, level, grade) { originRunId = runId };
            item.mapState = new MapInstanceState { mapContentId = contentId, level = level, grade = grade };
            if (rolledOptions != null)
                foreach (var option in rolledOptions) item.mapState.options.Add(ItemSnapshotCodec.CopyValues(option));
            return item;
        }
    }
}
