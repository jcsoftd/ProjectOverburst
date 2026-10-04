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

/// <summary>이동 보조옵션의 추첨·저장·실제 장착과 상태별 이동 계산 회귀.</summary>
[InitializeOnLoad]
public static class GearMovementSpeedVerifier
{
    const string Key = "Overburst.GearMovementSpeedVerifier.";
    static readonly List<string> checks = new List<string>();
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static bool executed;
    static GearMovementSpeedVerifier() { EditorApplication.update += Tick; }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks.Add(label);
    }
    static void Near(float actual, float expected, string label)
        => Check(Mathf.Abs(actual - expected) < .0001f, label);
    static string Output => SessionState.GetString(Key + "Output", "");

    public static string VerifyData()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        checks.Clear();
        var rng = UnityEngine.Random.state;
        var definition = ScriptableObject.CreateInstance<GearItemData>();
        var registry = ScriptableObject.CreateInstance<AccountContentRegistry>();
        try
        {
            registry.SetAuthoringEntries(new List<AccountContentEntry> { new AccountContentEntry { id = "test.gear", asset = definition } });
            Check((int)GearStat.CriticalDamage == 10 && (int)GearStat.ExplorationMoveSpeed == 11 && (int)GearStat.CombatMoveSpeed == 12,
                "Saved stat numbers retained");
            int exploration = 0, combat = 0, both = 0;
            foreach (GearKind kind in Enum.GetValues(typeof(GearKind)))
            {
                definition.kind = kind;
                for (int grade = 0; grade <= 7; grade++) for (int seed = 0; seed < 128; seed++)
                {
                    var rows = GearQuality.Roll(definition, (ItemGrade)grade, seed);
                    Check(GearQuality.IsValid(definition, (ItemGrade)grade, rows), "Valid quality " + kind + "/" + grade + "/" + seed);
                    bool hasExploration = rows.Any(r => r.stat == GearStat.ExplorationMoveSpeed);
                    bool hasCombat = rows.Any(r => r.stat == GearStat.CombatMoveSpeed);
                    if (hasExploration) exploration++;
                    if (hasCombat) combat++;
                    if (hasExploration && hasCombat) both++;
                }
            }
            Check(exploration > 0 && combat > 0 && both > 0, "Both options roll independently and may coexist");
            definition.kind = GearKind.Helmet;
            var item = Fixture(definition);
            foreach (var row in item.gearRolls.Skip(1).Take(2))
            {
                Near(GearQuality.BaseValue(item, row), .5f, "Movement baseline " + row.stat);
                Near(GearQuality.Value(item, row), .5f, "Unstarred movement " + row.stat);
            }
            item.gearRolls[1].stars.Add(WeaponGradeStarType.Yellow);
            Near(GearQuality.Value(item, item.gearRolls[1]), 1f, "Movement star weight");
            item.gearRolls[1].stars.Clear();
            var totals = GearStatTotals.FromItems(new[] { item, item });
            Near(totals.ExplorationMoveSpeed, 1f, "Exploration rows add across gear");
            Near(totals.CombatMoveSpeed, 1f, "Combat rows add across gear");
            Near(totals.MovementSpeedMultiplier(false), 1.01f, "Exploration selects own total");
            totals.CombatMoveSpeed = 5f;
            Near(totals.MovementSpeedMultiplier(false), 1.01f, "Combat cannot leak into exploration");
            Near(totals.MovementSpeedMultiplier(true), 1.05f, "Combat selects own total");
            string before = JsonConvert.SerializeObject(item.gearRolls);
            var restored = ItemSnapshotCodec.Restore(ItemSnapshotCodec.Capture(item, registry), registry);
            Check(restored.runtimeInstanceId == item.runtimeInstanceId && JsonConvert.SerializeObject(restored.gearRolls) == before,
                "ES3 snapshot restores identities and both option rows without reroll");
            item.gearRolls[1].stat = GearStat.Armor; item.gearRolls[2].stat = GearStat.AttackSpeed;
            before = JsonConvert.SerializeObject(item.gearRolls);
            item.EnsureRuntimeState();
            Check(JsonConvert.SerializeObject(item.gearRolls) == before, "Existing gear rows stay unchanged");
            Check(SimpleItemTooltipBuilder.GearStatLabel(GearStat.ExplorationMoveSpeed) == "탐험 이동속도"
                && SimpleItemTooltipBuilder.GearStatLabel(GearStat.CombatMoveSpeed) == "전투 이동속도", "Distinct tooltip labels");
            Check(rng.Equals(UnityEngine.Random.state), "Data verification leaves global random state unchanged");
            return JsonConvert.SerializeObject(new { status = "PASS", checks = checks.Count, exploration, combat, both });
        }
        finally { UnityEngine.Random.state = rng; UnityEngine.Object.DestroyImmediate(registry); UnityEngine.Object.DestroyImmediate(definition); }
    }

    static ItemData Fixture(GearItemData definition)
    {
        var item = new ItemData(definition, 30, ItemGrade.Common);
        item.gearRolls = new List<GearStatRoll> {
            new GearStatRoll { stat = definition.MainStat },
            new GearStatRoll { stat = GearStat.ExplorationMoveSpeed },
            new GearStatRoll { stat = GearStat.CombatMoveSpeed },
            new GearStatRoll { stat = GearStat.Attack }
        };
        return item;
    }

    public static void Begin(string output, bool reentry = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Output) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))
            throw new InvalidOperationException("Idle Editor with no prepared account required.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "Output", output);
        SessionState.SetString(Key + "Start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "Background", Application.runInBackground);
        SessionState.SetBool(Key + "Reentry", reentry);
        SessionState.SetFloat(Key + "Deadline", (float)EditorApplication.timeSinceStartup + 180);
        SessionState.SetBool(Key + "Return", false);
        executed = false;
        try
        {
            Application.runInBackground = true;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
            IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        }
        catch { SessionState.SetBool(Key + "Return", true); throw; }
    }

    static void Tick()
    {
        if (string.IsNullOrEmpty(Output)) return;
        bool timedOut = EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "Deadline", 0);
        if (SessionState.GetBool(Key + "Return", false))
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
            string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
            if (!string.IsNullOrEmpty(env) && !env.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(prepared) && !prepared.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (timedOut) { Write("Return", "DEFERRED", "Another account owns the Editor"); Clear(); }
                return;
            }
            string output = Output;
            string stage = SessionState.GetBool(Key + "Reentry", false) ? "Reentry" : "First";
            try
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "Start", ""));
                Application.runInBackground = SessionState.GetBool(Key + "Background", Application.runInBackground);
                IsolatedSavePlayGuard.UseRealAccount();
                Check(!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
                    && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory), "Real account and normal Play returned");
                Write(stage + "Return", "PASS", null);
            }
            catch (Exception error) { Write(stage + "Return", "FAIL", error.ToString()); }
            finally { Clear(); }
            return;
        }
        if (!EditorApplication.isPlaying)
        {
            if (timedOut && !EditorApplication.isPlayingOrWillChangePlaymode)
            { Write("Play", "FAIL", "Play boot timed out or was cancelled"); SessionState.SetBool(Key + "Return", true); }
            return;
        }
        if (executed) return;
        if (!timedOut && (PlayerContext.Instance?.CurrentActorMovement == null || !AccountBootstrap.Ready
            || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching)) return;
        executed = true; checks.Clear();
        string name = SessionState.GetBool(Key + "Reentry", false) ? "Reentry" : "First";
        try { VerifyPlayer(); Write(name, "PASS", null); }
        catch (Exception error) { Write(name, "FAIL", error.ToString()); }
        finally { SessionState.SetBool(Key + "Return", true); EditorApplication.isPlaying = false; }
    }

    static void Write(string name, string status, string error)
        => File.WriteAllText(Path.Combine(Output, name + ".json"), JsonConvert.SerializeObject(new { status, checks, error }, Formatting.Indented));
    static void Clear()
    {
        foreach (string key in new[] { "Output", "Start" }) SessionState.EraseString(Key + key);
        foreach (string key in new[] { "Background", "Reentry", "Return" }) SessionState.EraseBool(Key + key);
        SessionState.EraseFloat(Key + "Deadline"); executed = false; checks.Clear();
    }

    static float InvokeSpeed(PlayerMovement movement, string method)
        => (float)typeof(PlayerMovement).GetMethod(method, Private).Invoke(movement, null);
    static void VerifyPlayer()
    {
        Check(AccountBootstrap.Ready && AccountBootstrap.SaveDirectory.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Own isolated account");
        var movement = PlayerContext.Instance.CurrentActorMovement;
        var equipment = PlayerContext.Instance.CurrentActorEquipment;
        Check(movement && equipment, "Actual player movement and equipment ready");
        if (SessionState.GetBool(Key + "Reentry", false))
        {
            string expected = File.ReadAllText(Path.Combine(Output, "SavedRows.json"));
            Check(JsonConvert.SerializeObject(equipment.GetGearSlotItem(0).gearRolls) == expected, "Saved movement options restored on fresh Play");
            Near(GearStatTotals.From(equipment).ExplorationMoveSpeed, .5f, "Exploration bonus after reentry");
            Near(GearStatTotals.From(equipment).CombatMoveSpeed, .5f, "Combat bonus after reentry");
        }
        for (int i = 0; i < 6; i++) if (equipment.GetGearSlotItem(i) != null)
            Check(equipment.ClearGearSlot(i, out _), "Clear isolated gear slot " + i);
        var definitions = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath).Entries;
        var definition = definitions.Select(e => e.asset).OfType<GearItemData>().First(d => d.kind == GearKind.Helmet);
        var gear = Fixture(definition);
        var weapon = new ItemData(AssetDatabase.LoadAssetAtPath<WeaponItemData>(
            "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset"), 1, ItemGrade.Common);
        Check(equipment.EquipWeaponItem(weapon), "Equip actual greatsword");
        movement.CancelLootAutoMove();
        typeof(PlayerMovement).GetField("moveDirection", Private).SetValue(movement, movement.transform.forward);
        PlayerCombatModeController.ExitSharedCombatMode(PlayerCombatModeReason.System);
        float explorationBase = InvokeSpeed(movement, "GetTargetMoveSpeed");
        Check(equipment.EquipGearItemToSlot(gear, 0, out _), "Equip movement fixture through account transaction");
        Near(InvokeSpeed(movement, "GetTargetMoveSpeed"), explorationBase * 1.005f, "Exploration applies only exploration bonus once");
        Near(movement.RunMoveSpeed, ((float)typeof(PlayerMovement).GetField("runSpeed", Private).GetValue(movement)) * 1.005f, "Exploration Run base includes bonus once");
        var intent = (ActorMovementIntent)typeof(PlayerMovement).GetMethod("CreatePlayerMovementIntent", Private).Invoke(movement, null);
        Near(movement.BaseMoveSpeed * intent.SpeedMultiplier, explorationBase * 1.005f, "Player intent and locomotion base do not double apply");
        PlayerCombatModeController.EnterSharedCombatMode(PlayerCombatModeReason.System);
        Check(movement.IsCombatWalkLocomotionMode, "Greatsword combat movement active");
        float combatWithGear = InvokeSpeed(movement, "GetTargetMoveSpeed");
        Check(equipment.ClearGearSlot(0, out _), "Unequip movement fixture");
        float combatBase = InvokeSpeed(movement, "GetTargetMoveSpeed");
        Near(combatWithGear, combatBase * 1.005f, "Combat applies only combat bonus once");
        Check(equipment.EquipGearItemToSlot(gear, 0, out _), "Reequip fixture");
        Near(InvokeSpeed(movement, "GetTargetMoveSpeed"), combatWithGear, "Reequip restores bonus");
        gear.gearRolls[1].stat = GearStat.Armor;
        Near(InvokeSpeed(movement, "GetTargetMoveSpeed"), combatWithGear, "Exploration option does not change combat speed");
        PlayerCombatModeController.ExitSharedCombatMode(PlayerCombatModeReason.System);
        Near(InvokeSpeed(movement, "GetTargetMoveSpeed"), explorationBase, "Combat option does not change exploration speed");
        gear.gearRolls[1].stat = GearStat.ExplorationMoveSpeed;
        gear.gearRolls[2].stat = GearStat.AttackSpeed;
        PlayerCombatModeController.EnterSharedCombatMode(PlayerCombatModeReason.System);
        Near(InvokeSpeed(movement, "GetTargetMoveSpeed"), combatBase, "Exploration option alone leaves combat speed unchanged");
        gear.gearRolls[2].stat = GearStat.CombatMoveSpeed;
        Check(equipment.EquipGearItemToSlot(gear, 0, out _), "Commit final valid fixture rows");
        Check(AccountGameplaySession.Current.FlushPendingSave(), "Save final isolated loadout");
        File.WriteAllText(Path.Combine(Output, "SavedRows.json"), JsonConvert.SerializeObject(gear.gearRolls));
    }
}
