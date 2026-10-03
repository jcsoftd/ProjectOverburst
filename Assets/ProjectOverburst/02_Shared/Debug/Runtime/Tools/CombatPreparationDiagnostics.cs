#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace Overburst.DebugTools
{
    // Preparation-only controls for owned isolated validation. Effects remain fully enabled.
    public static class CombatPreparationDiagnostics
    {
        public const string SkipKey = "Overburst.CombatPreparationValidation.Skip";
        public const string AccountKey = "Overburst.CombatPreparationValidation.Account";
        static bool initialized;
        static int skipped;
        static readonly StageData[] stages = { new StageData(), new StageData(), new StageData() };

        [Serializable]
        public sealed class StageData
        {
            public string status = "NotStarted";
            public int prepareCalls, completed, created, unpreparedUses;
            public double prepareMilliseconds;
            public int workUnits;
            public double workMilliseconds, longestWorkMilliseconds;
        }

        [Serializable]
        public sealed class Data
        {
            public int skippedMask;
            public StageData[] stages;
            public string scope = "Preparation only; synchronous work and loading elapsed wait are separate. Stage2 work includes shared maintenance. No effect suppression or damage/input ownership.";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            initialized = false; skipped = 0;
            for (int i = 0; i < stages.Length; i++) stages[i] = new StageData();
        }

        static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
#if UNITY_EDITOR
            string actual = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
            string expected = UnityEditor.SessionState.GetString(AccountKey, "");
            if (!string.IsNullOrEmpty(actual) && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                skipped = UnityEditor.SessionState.GetInt(SkipKey, 0) & 7;
#endif
        }

        public static bool Skip(int stage)
        {
            EnsureInitialized();
            bool result = (skipped & (1 << stage)) != 0;
            if (result) stages[stage].status = "SkippedByValidation";
            return result;
        }

        public static void Begin(int stage) { stages[stage].prepareCalls++; stages[stage].status = "Preparing"; }
        public static void End(int stage, string status, int completed, double milliseconds)
        {
            stages[stage].status = status; stages[stage].completed = completed;
            stages[stage].prepareMilliseconds += milliseconds;
        }
        // Atomic synchronous work only; never spans a yield. Elapsed loading wait remains separate.
        public static void Work(int stage, long began)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            stages[stage].workUnits++;
            stages[stage].workMilliseconds += ms;
            stages[stage].longestWorkMilliseconds = Math.Max(stages[stage].longestWorkMilliseconds, ms);
        }
        public static void Created(int stage) => stages[stage].created++;
        public static void UnpreparedUse(int stage) => stages[stage].unpreparedUses++;
        public static Data Snapshot()
        {
            EnsureInitialized();
            var copy = new StageData[stages.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                var s = stages[i];
                copy[i] = new StageData { status = s.status, prepareCalls = s.prepareCalls, completed = s.completed,
                    created = s.created, unpreparedUses = s.unpreparedUses, prepareMilliseconds = s.prepareMilliseconds,
                    workUnits = s.workUnits, workMilliseconds = s.workMilliseconds, longestWorkMilliseconds = s.longestWorkMilliseconds };
            }
            return new Data { skippedMask = skipped, stages = copy };
        }
    }
}
#endif
