using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>레벨별 자연 무기 후보·분포와 실제 사망/획득/저장/런 소유권을 격리 검증한다.</summary>
[InitializeOnLoad]
public static class NaturalLootVerifier
{
    const string Key = "Overburst.NaturalLootVerifier.";
    const string GuardKey = "Overburst.IsolatedSavePlayGuard.";
    static readonly List<string> checks = new List<string>();
    static bool executed;

    static NaturalLootVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    static void Require(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        checks.Add(label);
    }

    public static string VerifyNative(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Native loot checks require an idle Editor.");
        Directory.CreateDirectory(output);
        checks.Clear();
        var rows = new List<object>();
        Random.State random = Random.state;
        RunLootDebugOverride debug = CombatDebugSettings.RunLootOverride;
        var fixture = new GameObject("NaturalLootNativeFixture") { hideFlags = HideFlags.HideAndDontSave };
        string failure = null;
        try
        {
            var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var catalog = WeaponLevelCatalog.Current;
            Require(registry != null && catalog != null, "production registry and weapon catalog load");
            var all = new HashSet<WeaponItemData>();
            CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Always);
            Random.InitState(1007);
            for (int level = 1; level <= 100; level++)
            {
                var candidates = WeaponLootPolicy.DefinitionsForLevel(level);
                var expected = catalog.Candidates(level).Where(x => x.icon != null && x.weaponRootPrefab != null && x.worldPickupPrefab != null).Distinct().ToArray();
                Require(candidates.Length > 0 && candidates.Length == expected.Length && candidates.All(expected.Contains), "exact legal weapon candidates Lv." + level);
                foreach (var data in candidates)
                {
                    all.Add(data);
                    Require(!string.IsNullOrEmpty(registry.IdFor(data)), "registered natural weapon " + data.name);
                }
                foreach (EnemyGradeType rank in new[] { EnemyGradeType.Normal, EnemyGradeType.Elite, EnemyGradeType.Boss })
                {
                    var item = WeaponLootPolicy.Roll(rank, level);
                    Require(item != null && item.level == level && candidates.Contains((WeaponItemData)item.baseData), "forced roll has source level and legal definition " + rank + "/" + level);
                    int max = level >= 25 ? 6 : level >= 18 ? 5 : level >= 10 ? 4 : level >= 5 ? 3 : 2;
                    Require((int)item.grade <= max, "natural weapon obeys level grade cap " + rank + "/" + level);
                    var saved = ItemSnapshotCodec.Capture(item, registry);
                    var restored = ItemSnapshotCodec.Restore(saved, registry);
                    Require(restored.runtimeInstanceId == item.runtimeInstanceId && restored.level == level
                        && JsonConvert.SerializeObject(restored.weaponGradeStatRolls) == JsonConvert.SerializeObject(item.weaponGradeStatRolls), "weapon snapshot preserves identity and rolls " + rank + "/" + level);
                }
            }
            Require(all.Count == catalog.entries.Select(x => x.weapon).Where(x => x != null && WeaponContentPolicy.IsActiveWeapon(x)).Distinct().Count(), "all authored active weapons reachable through natural loot");
            Require(WeaponLootPolicy.DefinitionsForLevel(0).SequenceEqual(WeaponLootPolicy.DefinitionsForLevel(1))
                && WeaponLootPolicy.DefinitionsForLevel(101).SequenceEqual(WeaponLootPolicy.DefinitionsForLevel(100)), "weapon level clamps at 1 and 100");
            var rankFixture = fixture.AddComponent<EnemyRank>();
            const int trials = 10000;
            double[][] expectedRates = { new[] { .015, .03, .01, .01, .005 }, new[] { .08, .15, .05, .05, .02 }, new[] { .35, 1.0, .25, .10, .05 } };
            var names = new[] { "weapon", "gear", "gem", "flask", "bag" };
            var ranks = new[] { EnemyGradeType.Normal, EnemyGradeType.Elite, EnemyGradeType.Boss };
            CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Default);
            for (int r = 0; r < ranks.Length; r++)
            {
                typeof(EnemyRank).GetProperty(nameof(EnemyRank.GradeType)).SetValue(rankFixture, ranks[r]);
                var counts = new int[5];
                Random.InitState(1007 + r);
                for (int i = 0; i < trials; i++)
                {
                    if (WeaponLootPolicy.Roll(rankFixture, 25) != null) counts[0]++;
                    if (GearLootPolicy.Roll(rankFixture, 25) != null) counts[1]++;
                    if (ElementGemLootPolicy.Roll(rankFixture, 25) != null) counts[2]++;
                    if (FlaskLootPolicy.Roll(rankFixture, 25) != null) counts[3]++;
                    if (BagFarmingLoot.RollBag(rankFixture, 25, ItemGrade.Common, 0) != null) counts[4]++;
                }
                for (int kind = 0; kind < 5; kind++)
                {
                    double probability = expectedRates[r][kind];
                    double tolerance = 6 * Math.Sqrt(trials * probability * (1 - probability)) + 3;
                    Require(Math.Abs(counts[kind] - trials * probability) <= tolerance, "production drop distribution " + ranks[r] + "/" + names[kind]);
                    rows.Add(new { rank = ranks[r].ToString(), kind = names[kind], trials, count = counts[kind], expectedPercent = probability * 100, observedPercent = counts[kind] * 100.0 / trials });
                }
            }
            CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Never);
            Require(WeaponLootPolicy.Roll(rankFixture, 25) == null && GearLootPolicy.Roll(rankFixture, 25) == null
                && ElementGemLootPolicy.Roll(rankFixture, 25) == null && FlaskLootPolicy.Roll(rankFixture, 25) == null
                && BagFarmingLoot.RollBag(rankFixture, 25, ItemGrade.Common, 0) == null, "Never suppresses all five farming sources");
            for (int level = 1; level <= 24; level++)
            {
                var source = new ItemData(WeaponLootPolicy.DefinitionsForLevel(level)[0], level, ItemGrade.Common);
                var extra = BagFarmingLoot.Extra(source, rankFixture, level, ItemGrade.Common, 0);
                int max = level >= 18 ? 5 : level >= 10 ? 4 : level >= 5 ? 3 : 2;
                Require(extra != null && extra.baseData == source.baseData && extra.level == level && (int)extra.grade <= max,
                    "extra weapon respects source level and grade cap Lv." + level);
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            Object.DestroyImmediate(fixture);
            CombatDebugSettings.SetRunLootOverride(debug);
            Random.state = random;
            File.WriteAllText(Path.Combine(output, "Native.json"), JsonConvert.SerializeObject(new { status = failure == null ? "PASS" : "FAIL", checks, distributions = rows, failure }, Formatting.Indented));
        }
        return failure == null ? "PASS: " + checks.Count + " native checks" : "FAIL: " + failure;
    }

    public static void BeginPlay(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(SessionState.GetString(Key + "output", ""))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString(GuardKey + "prepared", "")))
            throw new InvalidOperationException("An idle, returned Editor is required for natural loot Play.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "sceneSetup", HideoutCatalogLayoutBuilder.EditorSnapshot());
        SessionState.SetString(Key + "account", Path.Combine(output, "IsolatedAccount"));
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 180);
        SessionState.SetBool(Key + "started", false);
        SessionState.SetBool(Key + "return", false);
        executed = false;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(SessionState.GetString(Key + "account", "")); }
        catch { SessionState.SetBool(Key + "return", true); throw; }
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (string.IsNullOrEmpty(SessionState.GetString(Key + "output", ""))) return;
        if (state == PlayModeStateChange.EnteredPlayMode) SessionState.SetBool(Key + "started", true);
        if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key + "return", true);
    }

    static void Tick()
    {
        string output = SessionState.GetString(Key + "output", "");
        if (string.IsNullOrEmpty(output)) return;
        if (SessionState.GetBool(Key + "return", false))
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string directory = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
            string prepared = SessionState.GetString(GuardKey + "prepared", "");
            if (!string.IsNullOrEmpty(prepared) || (!string.IsNullOrEmpty(directory) && directory != SessionState.GetString(Key + "account", ""))) return;
            string start = SessionState.GetString(Key + "start", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            IsolatedSavePlayGuard.UseRealAccount();
            bool preserved = HideoutCatalogLayoutBuilder.EditorSnapshot() == SessionState.GetString(Key + "sceneSetup", "");
            File.WriteAllText(Path.Combine(output, "Return.json"), JsonConvert.SerializeObject(new {
                status = preserved && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL", sceneSetupPreserved = preserved,
                blocked = IsolatedSavePlayGuard.RequiresAccountChoice, active = IsolatedSavePlayGuard.ActiveDirectory,
                prepared = SessionState.GetString(GuardKey + "prepared", ""), expires = SessionState.GetString(GuardKey + "expires", ""),
                env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)
            }, Formatting.Indented));
            foreach (string suffix in new[] { "output", "start", "sceneSetup", "account" }) SessionState.EraseString(Key + suffix);
            SessionState.EraseFloat(Key + "deadline"); SessionState.EraseBool(Key + "return"); SessionState.EraseBool(Key + "started");
            executed = false;
            return;
        }
        bool timedOut = EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0);
        if (!EditorApplication.isPlaying)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && (timedOut || SessionState.GetBool(Key + "started", false))) SessionState.SetBool(Key + "return", true);
            return;
        }
        if (executed) return;
        if (!timedOut && (!AccountBootstrap.Ready || PlayerContext.Instance?.CurrentActor == null
            || WorldSessionState.Phase != WorldPhase.Hideout || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching)) return;
        executed = true;
        checks.Clear();
        string failure = null;
        try
        {
            Require(AccountBootstrap.Ready && !timedOut, "isolated account and Hideout boot");
            Require(AccountBootstrap.SaveDirectory == SessionState.GetString(Key + "account", ""), "owned isolated account selected");
            VerifyPlay();
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(output, "Play.json"), JsonConvert.SerializeObject(new { status = failure == null ? "PASS" : "FAIL", checks, failure }, Formatting.Indented));
            SessionState.SetBool(Key + "return", true);
            EditorApplication.isPlaying = false;
        }
    }

    static void SetPhase(WorldPhase phase) => typeof(WorldSessionState).GetMethod("SetPhase", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { phase });

    static void VerifyPlay()
    {
        var actor = PlayerContext.Instance.CurrentActor;
        var inventory = actor.Inventory;
        var session = AccountGameplaySession.Current;
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        Random.State random = Random.state;
        var debug = CombatDebugSettings.RunLootOverride;
        var oldPickups = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(oldPickups);
        var ids = new HashSet<int>(oldPickups.Select(x => x.GetInstanceID()));
        var oldGold = new HashSet<int>(Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None).Select(x => x.GetInstanceID()));
        var enemy = new GameObject("NaturalLootBossFixture");
        enemy.transform.position = new Vector3(7000, 0, 7000);
        try
        {
            CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Default);
            Random.InitState(1007);
            Require(session.ExecuteState("natural-loot-capacity-" + Guid.NewGuid().ToString("N"), s => s.unlockedSlots = s.baseUnlockedSlots = s.inventoryCapacity), "isolated inventory capacity");
            var health = enemy.AddComponent<CombatHealth>();
            var rank = enemy.AddComponent<EnemyRank>();
            var dropper = enemy.AddComponent<EnemyLootDropper>();
            dropper.Configure(null, inventory, actor.transform, null);
            for (int pass = 0; pass < 2; pass++)
            {
                string run = "natural-weapon-" + Guid.NewGuid().ToString("N");
                var map = new MapInstanceState { level = 25, grade = ItemGrade.Common, options = new List<MapOptionRoll>() };
                Require(session.ExecuteState(run + "-entry", s => { AccountRunCommands.PrepareEntry(s, run, map, null, true); AccountRunCommands.Activate(s, run); }), "authoritative run entry " + pass);
                SetPhase(WorldPhase.Run);
                var context = new EncounterContext(run, 25, ItemGrade.Common);
                WorldItemPickup weapon = null;
                for (int kill = 0; kill < 40 && weapon == null; kill++)
                {
                    dropper.ResetForPool(); dropper.Configure(null, inventory, actor.transform, null); dropper.ConfigureEncounter(context);
                    rank.ConfigureTemporaryBoss(context); health.ResetHealth();
                    var before = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(before);
                    var old = new HashSet<int>(before.Select(x => x.GetInstanceID()));
                    health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, actor.gameObject, triggersOnHitEffects: false));
                    var after = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(after);
                    var created = after.Where(x => !old.Contains(x.GetInstanceID())).ToArray();
                    Require(created.Any(x => x.RuntimeItem?.baseData is GearItemData), "default boss death guarantees gear " + pass + "/" + kill);
                    Require(created.All(x => x.RuntimeItem.level == 25 && x.RuntimeItem.originRunId == run), "all natural loot stamped to map level and run " + pass + "/" + kill);
                    int count = after.Count;
                    health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, actor.gameObject, triggersOnHitEffects: false));
                    after.Clear(); WorldItemPickup.CopyActivePickups(after);
                    Require(after.Count == count, "repeated death does not grant twice " + pass + "/" + kill);
                    weapon = created.FirstOrDefault(x => x.RuntimeItem?.baseData is WeaponItemData);
                    foreach (var pickup in created) if (pickup != weapon) Object.DestroyImmediate(pickup.gameObject);
                }
                Require(weapon != null, "default probability produces natural weapon " + pass);
                var item = weapon.RuntimeItem;
                Require(WeaponLootPolicy.DefinitionsForLevel(25).Contains((WeaponItemData)item.baseData)
                    && !string.IsNullOrEmpty(weapon.DisplayName), "natural weapon uses legal definition and nameplate name " + pass);
                var snapshot = ItemSnapshotCodec.Capture(item, registry);
                Require(weapon.TryPickup(inventory), "actual natural weapon pickup " + pass);
                Require(actor.Equipment.EquipWeaponItem(item) && actor.Equipment.CurrentWeaponItem.runtimeInstanceId == item.runtimeInstanceId
                    && actor.Equipment.HasCurrentMeleeDefinition, "natural weapon equips with real combat definition " + pass);
                session.FlushPendingSave();
                var disk = new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();
                AccountInvariants.Validate(disk, registry);
                var persisted = disk.items.Single(x => x.instanceId == item.runtimeInstanceId);
                Require(persisted.level == 25 && persisted.originRunId == run && persisted.grade == snapshot.grade
                    && JsonConvert.SerializeObject(persisted.weaponRolls) == JsonConvert.SerializeObject(snapshot.weaponRolls), "disk restore preserves natural weapon identity and quality " + pass);
                if (pass == 1)
                    Require(session.ExecuteState(run + "-transfer", s => AccountRunCommands.Transfer(s, run, "natural-loot-transfer", item.runtimeInstanceId, registry)), "equipped natural weapon transferred to stash");
                Require(session.ExecuteState(run + "-fail", s => AccountRunCommands.Fail(s, run)), "run failure settles " + pass);
                Require(session.Read().items.Any(x => x.instanceId == item.runtimeInstanceId) == (pass == 1), "unprotected weapon lost and transferred weapon retained " + pass);
                AccountInvariants.Validate(session.Read(), registry);
                SetPhase(WorldPhase.Hideout);
            }
        }
        finally
        {
            var remaining = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(remaining);
            foreach (var pickup in remaining) if (pickup != null && !ids.Contains(pickup.GetInstanceID())) Object.DestroyImmediate(pickup.gameObject);
            foreach (var gold in Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None))
                if (!oldGold.Contains(gold.GetInstanceID())) Object.DestroyImmediate(gold.gameObject);
            Object.DestroyImmediate(enemy);
            SetPhase(WorldPhase.Hideout);
            CombatDebugSettings.SetRunLootOverride(debug);
            Random.state = random;
        }
    }
}
