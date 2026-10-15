#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using Overburst.Persistence;
using UnityEngine;

namespace Overburst.DebugTools.Performance
{
    public sealed partial class CombatPerformanceRunner
    {
        public const string CspPlayerProtocol = "CSP-G00-PLAYER-1";
        public static bool CspPlayerRequested { get; } = !Application.isEditor
            && Array.IndexOf(Environment.GetCommandLineArgs(), "--overburst-csp-player") >= 0;
        public static bool CspPlayerAccountReady { get; private set; }
        static CombatPerformanceProfile cspPlayerProfile;
        static CspPlayerSession cspPlayerSession;
        static string cspPlayerOutput;

        [Serializable]
        public sealed class CspPlayerSession
        {
            public int schema = 1, qualitySchema = 3, pid, exitCode = -1;
            public string protocol = CspPlayerProtocol, status, reason, buildGuid, buildFingerprint;
            public string saveDirectory, settingsDirectory, actualSaveDirectory, startedUtc, endedUtc;
            public bool developmentBuild, traceReleased, runnerReleased;
        }

        public static void ValidateCspPlayerProfile(CombatPerformanceProfile profile)
        {
            if (profile == null) throw new ArgumentException("Player profile is missing");
            profile.Validate();
            if (!profile.csp || profile.mode != CombatPerformanceMode.Automated
                || profile.themes.Length != 1 || profile.enemyCounts.Length != 1
                || profile.elements.Length != 1 || profile.repetitions != 1
                || profile.layouts.Length != 1 || profile.layouts[0] != CombatPerformanceLayout.Dense)
                throw new ArgumentException("CSP Player requires one automated CSP recipe; repeat with a fresh process");
        }

        public static void ValidateCspPlayerIsolation(string output, string account, string settings)
        {
            string expected = Path.Combine(CombatPerformancePaths.RequireOutput(output), "IsolatedAccount");
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(settings)
                || !SamePlayerPath(CombatPerformancePaths.RequireOutput(account), expected)
                || !SamePlayerPath(CombatPerformancePaths.RequireOutput(settings), expected))
                throw new InvalidOperationException("CSP Player requires both save/settings directories to be this run's IsolatedAccount");
        }

        public static void RequireCspPlayerIsolation()
        {
            if (!CspPlayerRequested) return;
            string account = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
            ValidateCspPlayerIsolation(PlayerArgument(Environment.GetCommandLineArgs(), "--overburst-perf-output"),
                account, Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"));
            if (!CspPlayerAccountReady && Directory.Exists(account) && Directory.GetFileSystemEntries(account).Length != 0)
                throw new InvalidOperationException("Unprepared CSP Player cannot read or save an existing settings directory");
        }

        static bool SamePlayerPath(string left, string right) => !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right)
            && string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        static string PlayerArgument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Missing Player argument: " + key);
            return args[index + 1];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void PrepareCspPlayer()
        {
            if (!CspPlayerRequested) return;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                cspPlayerOutput = CombatPerformancePaths.RequireOutput(PlayerArgument(args, "--overburst-perf-output"));
                if (File.Exists(Path.Combine(cspPlayerOutput, "run.json")) || File.Exists(Path.Combine(cspPlayerOutput, "player-session.json")))
                    throw new IOException("CSP Player output was already used");
                string account = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
                string settings = Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY");
                ValidateCspPlayerIsolation(cspPlayerOutput, account, settings);
                string fingerprint = PlayerArgument(args, "--overburst-perf-build-hash");
                if (fingerprint.Length != 64 || Array.Exists(fingerprint.ToCharArray(), c => !Uri.IsHexDigit(c)))
                    throw new ArgumentException("Player build fingerprint must be SHA-256");
                cspPlayerSession = new CspPlayerSession { status = "PREPARING", buildGuid = Application.buildGUID,
                    buildFingerprint = fingerprint, developmentBuild = Debug.isDebugBuild,
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                    saveDirectory = Path.GetFullPath(account), settingsDirectory = Path.GetFullPath(settings), startedUtc = DateTime.UtcNow.ToString("O") };
                if (!Debug.isDebugBuild || Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("CSP requires a rendered Development Player");
                cspPlayerProfile = JsonUtility.FromJson<CombatPerformanceProfile>(File.ReadAllText(PlayerArgument(args, "--overburst-combat-perf")));
                ValidateCspPlayerProfile(cspPlayerProfile);
                if (AccountBootstrap.Attempted || (Directory.Exists(account) && Directory.GetFileSystemEntries(account).Length != 0))
                    throw new InvalidOperationException("CSP fixture must be prepared before account boot in a fresh directory");
                // Same fixture as the existing Editor CSP session; no real-account copy.
                var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
                var seed = NewAccountFactory.Create(registry);
                CheckCspPlayerContent(seed);
                seed.level = 10;
                AccountCharacterCommands.EnsureLegacy(seed);
                new EasySaveAccountStore(account).Save(seed, "CSP-G00-fixture");
                CspPlayerAccountReady = true;
                cspPlayerSession.status = "PREPARED";
                WriteCspPlayerSession();
            }
            catch (Exception error) { FailCspPlayer(error); }
        }

        // Runs before the measured recipe, only for explicitly requested CSP Players.
        static void CheckCspPlayerContent(AccountSnapshot seed)
        {
            int gemCount = 0;
            foreach (var item in seed.items)
                if (item.instanceId == seed.elementalGemInstanceId && item.contentId == NewAccountFactory.StarterElementGemId
                    && item.level == 1 && item.grade == ItemGrade.Common && item.count == 1) gemCount++;
            if (gemCount != 1) throw new InvalidOperationException("Starter fire gem is not equipped at level 1");
            var available = new System.Collections.Generic.List<string>();
            var sizes = new System.Collections.Generic.HashSet<int>();
            foreach (var row in MapTerrainCatalog.DesertCandidates)
                if (Application.CanStreamedLevelBeLoaded(row.scene)) { available.Add(row.id); sizes.Add(row.areaCount); }
            if (!sizes.SetEquals(new[] { 4, 6, 9, 12, 15 }))
                throw new InvalidOperationException("Player is missing a desert size group");
            var generated = new System.Collections.Generic.HashSet<string>();
            int deserts = 0;
            for (int i = 0; i < 2048; i++)
            {
                var map = new MapInstanceState();
                MapGenerationPolicy.Initialize(map, "CSP-Player-content-" + i);
                if (!Application.CanStreamedLevelBeLoaded(MapTerrainCatalog.SceneFor(map)))
                    throw new InvalidOperationException("New map selected a scene absent from this Player");
                if (map.terrain == MapTerrainKind.Desert) { deserts++; generated.Add(map.desertCandidateId); }
            }
            if (!generated.SetEquals(available)) throw new InvalidOperationException("Map samples did not cover the available desert candidates");
            System.IO.File.WriteAllText(Path.Combine(cspPlayerOutput, "player-content-checks.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "PASS", starterFireGemLevel = 1,
                    sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings,
                    catalogCandidates = MapTerrainCatalog.DesertCandidates.Count, availableDeserts = available,
                    generatedDeserts = generated, samples = 2048, desertSamples = deserts }, Newtonsoft.Json.Formatting.Indented));
        }

        static void StartCspPlayer()
        {
            if (!CspPlayerAccountReady) { Application.Quit(2); return; }
            try
            {
                string fingerprint = CombatPerformancePaths.Hash(cspPlayerSession.buildGuid + "|" + CspPlayerProtocol + "|" + cspPlayerSession.buildFingerprint);
                var runner = StartRun(cspPlayerProfile, cspPlayerOutput, revision: "Player/" + cspPlayerSession.buildGuid, fingerprint: fingerprint);
                runner.Completed += CompleteCspPlayer;
                cspPlayerSession.status = "RUNNING";
                WriteCspPlayerSession();
            }
            catch (Exception error) { FailCspPlayer(error); }
        }

        static void CompleteCspPlayer(CombatPerformanceRun run)
        {
            try
            {
                cspPlayerSession.actualSaveDirectory = AccountBootstrap.SaveDirectory;
                cspPlayerSession.traceReleased = CombatSpawnQualityTrace.Sink == null;
                cspPlayerSession.runnerReleased = Current == null;
                bool success = run.status == "COMPLETE" && run.logErrors == 0 && run.segments.Count > 0
                    && SamePlayerPath(cspPlayerSession.actualSaveDirectory, cspPlayerSession.saveDirectory)
                    && cspPlayerSession.traceReleased && cspPlayerSession.runnerReleased;
                cspPlayerSession.status = success ? "COMPLETE" : "FAILED";
                cspPlayerSession.reason = run.reason;
                cspPlayerSession.endedUtc = DateTime.UtcNow.ToString("O");
                cspPlayerSession.exitCode = success ? 0 : 2;
                WriteCspPlayerSession();
                Application.Quit(cspPlayerSession.exitCode);
            }
            catch (Exception error) { FailCspPlayer(error); }
        }

        static void WriteCspPlayerSession() => CombatPerformancePaths.SaveJson(Path.Combine(cspPlayerOutput, "player-session.json"), cspPlayerSession);
        static void FailCspPlayer(Exception error)
        {
            CspPlayerAccountReady = false;
            Debug.LogException(error);
            if (cspPlayerSession != null)
            {
                cspPlayerSession.status = "FAILED"; cspPlayerSession.reason = error.ToString();
                cspPlayerSession.endedUtc = DateTime.UtcNow.ToString("O"); cspPlayerSession.exitCode = 2;
                try { WriteCspPlayerSession(); } catch (Exception writeError) { Debug.LogException(writeError); }
            }
            Application.Quit(2);
        }
    }
}
#endif
