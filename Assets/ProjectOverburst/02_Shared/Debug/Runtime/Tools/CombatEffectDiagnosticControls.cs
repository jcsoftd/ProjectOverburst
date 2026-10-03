#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace Overburst.DebugTools
{
    [Flags]
    public enum CombatDiagnosticEffect
    {
        None = 0, BloodSpray = 1, GroundDecals = 2, ElementHit = 4,
        StatusAura = 8, HitAudio = 16, ImpactFeel = 32, All = 63
    }

    public enum CombatEffectABCase
    {
        Baseline, NoBloodSpray, NoGroundDecals, NoElementHit,
        NoStatusAura, NoHitAudio, NoImpactFeel, NoHitPresentation
    }

    // Presentation suppression for a single diagnostic Play. No damage/status/energy gates.
    public static class CombatEffectDiagnosticControls
    {
        public const string PendingKey = "Overburst.CombatEffectAB.Pending";
        public const string ActiveKey = "Overburst.CombatEffectAB.Active";
        public const string PendingAccountKey = "Overburst.CombatEffectAB.PendingAccount";
        public const string ActiveAccountKey = "Overburst.CombatEffectAB.ActiveAccount";
        public const string LaunchPrefix = "--overburst-effect-ab=";
        static bool initialized;
        static CombatEffectABCase scenario;
        static CombatDiagnosticEffect disabled;
        static readonly long[] blockedChecks = new long[6];

        public static CombatDiagnosticEffect MaskFor(CombatEffectABCase value)
        {
            switch (value)
            {
                case CombatEffectABCase.Baseline: return CombatDiagnosticEffect.None;
                case CombatEffectABCase.NoBloodSpray: return CombatDiagnosticEffect.BloodSpray;
                case CombatEffectABCase.NoGroundDecals: return CombatDiagnosticEffect.GroundDecals;
                case CombatEffectABCase.NoElementHit: return CombatDiagnosticEffect.ElementHit;
                case CombatEffectABCase.NoStatusAura: return CombatDiagnosticEffect.StatusAura;
                case CombatEffectABCase.NoHitAudio: return CombatDiagnosticEffect.HitAudio;
                case CombatEffectABCase.NoImpactFeel: return CombatDiagnosticEffect.ImpactFeel;
                case CombatEffectABCase.NoHitPresentation: return CombatDiagnosticEffect.All;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        public static bool TryParse(string name, out CombatEffectABCase value)
        {
            return Enum.TryParse(name, false, out value) && Enum.IsDefined(typeof(CombatEffectABCase), value)
                && string.Equals(name, value.ToString(), StringComparison.Ordinal);
        }

        public static bool Allowed(CombatDiagnosticEffect effect)
        {
            if (!Application.isPlaying) return true;
            EnsureInitialized();
            bool allowed = (disabled & effect) == 0;
            if (!allowed)
                for (int i = 0; i < blockedChecks.Length; i++)
                    if (((int)disabled & (int)effect & (1 << i)) != 0) blockedChecks[i]++;
            return allowed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetToBaseline()
        {
            initialized = false;
            scenario = CombatEffectABCase.Baseline;
            disabled = CombatDiagnosticEffect.None;
            Array.Clear(blockedChecks, 0, blockedChecks.Length);
        }

        static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            string requested = null;
#if UNITY_EDITOR
            requested = UnityEditor.SessionState.GetString(ActiveKey, "");
            string expectedAccount = UnityEditor.SessionState.GetString(ActiveAccountKey, "");
            string actualAccount = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY") ?? "";
            if (!string.Equals(expectedAccount, actualAccount, StringComparison.OrdinalIgnoreCase)) requested = null;
#else
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(LaunchPrefix, StringComparison.Ordinal)) requested = arg.Substring(LaunchPrefix.Length);
#endif
            if (TryParse(requested, out var selected)) scenario = selected;
            disabled = MaskFor(scenario);
        }

        [Serializable]
        public sealed class SnapshotData
        {
            public string schema = "overburst-effect-ab-v1";
            public string scenario, disabledEffects;
            public int disabledMask;
            public long[] blockedChecks;
            public string counterOrigin = "Blocked policy checks, not effect instances, damage events or skipped milliseconds.";
            public string scope = "Six hit presentation groups only. Damage/status/energy/AI, weapon trails, HUD and damage numbers remain outside these gates. HitAudio covers explicit organic/element hit sounds; embedded Feel audio belongs to ImpactFeel.";
        }

        public static SnapshotData Snapshot()
        {
            if (Application.isPlaying) EnsureInitialized();
            return new SnapshotData { scenario = scenario.ToString(), disabledEffects = disabled.ToString(),
                disabledMask = (int)disabled, blockedChecks = (long[])blockedChecks.Clone() };
        }

        public static string ValidateCore()
        {
            // Each one-factor case must leave the other five groups enabled.
            var cases = (CombatEffectABCase[])Enum.GetValues(typeof(CombatEffectABCase));
            int[] expected = { 0, 1, 2, 4, 8, 16, 32, 63 };
            int checks = 0;
            for (int i = 0; i < cases.Length; i++)
            {
                if ((int)MaskFor(cases[i]) != expected[i]) throw new InvalidOperationException("Case contract changed: " + cases[i]);
                checks++;
            }
            foreach (string invalid in new[] { "", "0", "63", "NoArua", "NoBloodSpray,NoGroundDecals" })
            {
                if (TryParse(invalid, out _)) throw new InvalidOperationException("Invalid case accepted: " + invalid);
                checks++;
            }
            var before = Snapshot();
            if (Application.isPlaying) throw new InvalidOperationException("Core validation requires EditMode.");
            foreach (CombatDiagnosticEffect effect in new[] { CombatDiagnosticEffect.BloodSpray, CombatDiagnosticEffect.GroundDecals,
                CombatDiagnosticEffect.ElementHit, CombatDiagnosticEffect.StatusAura, CombatDiagnosticEffect.HitAudio, CombatDiagnosticEffect.ImpactFeel })
            {
                if (!Allowed(effect)) throw new InvalidOperationException("Suppression leaked into EditMode.");
                checks++;
            }
            if (JsonUtility.ToJson(before) != JsonUtility.ToJson(Snapshot())) throw new InvalidOperationException("Read-only verification changed state.");
            return "PASS " + checks + " · case masks, invalid input, EditMode default and read-only state";
        }
    }
}
#endif
