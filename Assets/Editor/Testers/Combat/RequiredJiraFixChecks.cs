#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// 기존 격리 Play 실행기에서 실제 씬 전환과 제품 체력 계산을 검사한다.
public static class RequiredJiraFixChecks
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static IEnumerator Run(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<object>();
        void Check(bool pass, string label, object evidence = null)
        {
            checks.Add(new { pass, label, evidence });
            File.WriteAllText(Path.Combine(output, "required-progress.json"), JsonConvert.SerializeObject(checks, Formatting.Indented));
            if (!pass) throw new InvalidOperationException(label);
        }
        var ui = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
        var flow = PersistentSceneFlow.Instance;
        var input = PlayerInputFacade.Current;
        var settings = InputSystem.settings;
        string settingsJson = UnityEditor.EditorJsonUtility.ToJson(settings);
        InputSettings ownedSettings = null;
        Keyboard keyboard = null;
        InputDevice[] devices = input.RuntimeAsset.devices.HasValue ? input.RuntimeAsset.devices.Value.ToArray() : null;
        Key? held = null;
        int unloadAttempts = 0, transitionFrames = 0;
        string status = "ABORTED";
        void InputTick()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || keyboard == null) return;
            if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
            InputSystem.QueueStateEvent(keyboard, held.HasValue ? new KeyboardState(held.Value) : new KeyboardState());
        }
        void Unloaded(Scene scene)
        {
            if (!flow.IsSwitching) return;
            unloadAttempts++;
            ui.ToggleEquipment(); ui.equipmentButton.onClick.Invoke();
            Check(!ui.equipmentWindow.gameObject.activeSelf, "Unload callback cannot reopen equipment", scene.name);
        }
        IEnumerator Press(Key key)
        {
            held = key; for (int i = 0; i < 3; i++) yield return null;
            held = null; for (int i = 0; i < 3; i++) yield return null;
        }
        IEnumerator Switching()
        {
            float deadline = Time.realtimeSinceStartup + 120f;
            while (flow.IsSwitching && Time.realtimeSinceStartup < deadline)
            {
                held = transitionFrames % 6 < 3 ? Key.C : (Key?)null;
                ui.equipmentButton.onClick.Invoke();
                if (ui.equipmentWindow.gameObject.activeSelf) throw new InvalidOperationException("Equipment reopened during actual transition.");
                transitionFrames++; yield return null;
            }
            held = null; yield return null; yield return null;
            Check(!flow.IsSwitching && !ui.equipmentWindow.gameObject.activeSelf, "Transition completes with equipment closed");
        }
        try
        {
            Check(ui != null && flow != null && input != null && WorldSessionState.IsHideout, "Product UI, input and hideout ready");
            ownedSettings = UnityEngine.Object.Instantiate(settings); ownedSettings.hideFlags = HideFlags.HideAndDontSave;
            ownedSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            ownedSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = ownedSettings;
            keyboard = InputSystem.AddDevice<Keyboard>("RequiredFixOwnedKeyboard"); input.RuntimeAsset.devices = new InputDevice[] { keyboard };
            InputSystem.onBeforeUpdate += InputTick; SceneManager.sceneUnloaded += Unloaded;
            yield return Press(Key.C);
            Check(ui.equipmentWindow.gameObject.activeSelf, "Normal C key opens equipment through InputSystem");
            yield return Press(Key.Escape);
            Check(!ui.equipmentWindow.gameObject.activeSelf, "Normal Escape closes equipment");
            ui.equipmentButton.onClick.Invoke();
            Check(ui.equipmentWindow.gameObject.activeSelf, "Authored equipment button opens normally");
            var account = AccountGameplaySession.Current;
            var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
            var registry = (AccountContentRegistry)typeof(AccountGameplaySession).GetProperty("ContentRegistry", All).GetValue(account);
            var map = new MapInstanceState { mapContentId = registry.IdFor(definition), level = 1, grade = ItemGrade.Common, monsterThemeId = MapThemeCatalog.RollThemeId() };
            Check(flow.EnterDebugRun(DiamondDungeonWorld.SceneName, map), "Actual debug dungeon entry begins");
            yield return Switching();
            Check(WorldSessionState.Phase == WorldPhase.Run, "Actual dungeon is ready");
            HealthChecks(Check);
            yield return ResponsibilityChecks(Check);
            ui.equipmentButton.onClick.Invoke();
            Check(ui.equipmentWindow.gameObject.activeSelf, "Equipment can reopen after completed dungeon entry");
            var run = account.ReadRun();
            new AccountRunSession(account).Fail(run.runId);
            flow.ReturnToHub(RunSceneReturnContext.CreateHubTransfer(PersistentSceneFlow.DefaultHubSceneName, "Default"));
            yield return Switching();
            Check(WorldSessionState.IsHideout && unloadAttempts >= 2 && transitionFrames > 0, "Real dungeon/hideout unloads reject repeated C and button attempts", new { unloadAttempts, transitionFrames });
            yield return Press(Key.C);
            Check(ui.equipmentWindow.gameObject.activeSelf, "Normal C toggle works after return");
            ui.CloseEquipment();
            Check(!GameplayInputBlocker.IsGameplayInputBlocked, "Equipment close releases its blocker after return");
            status = "PASS_SCOPED";
        }
        finally
        {
            InputSystem.onBeforeUpdate -= InputTick; SceneManager.sceneUnloaded -= Unloaded;
            held = null; if (ui != null) ui.CloseEquipment();
            input.RuntimeAsset.devices = devices;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings = settings;
            bool restored = ReferenceEquals(InputSystem.settings, settings) && UnityEditor.EditorJsonUtility.ToJson(settings) == settingsJson && (keyboard == null || !keyboard.added);
            if (ownedSettings != null) UnityEngine.Object.DestroyImmediate(ownedSettings);
            File.WriteAllText(Path.Combine(output, "required-result.json"), JsonConvert.SerializeObject(new { status = restored ? status : "FAIL_INPUT_RETURN", checks, inputRestored = restored, realSceneTransition = true, healthScope = "Product methods on isolated temporary actor; gear/user feel not asserted" }, Formatting.Indented));
            if (!restored) throw new InvalidOperationException("Owned input restore failed.");
        }
    }
    // 실제 Play의 자동 생명주기와 기존 디버그 스폰/Update 경계를 검사한다.
    static IEnumerator ResponsibilityChecks(Action<bool, string, object> check)
    {
        if (DpsMeterDebugModule.AliveDummies != 0) throw new InvalidOperationException("Foreign DPS dummies must be preserved.");
        var random = UnityEngine.Random.state;
        var fixture = new GameObject("ResponsibilityBuffActor"); fixture.SetActive(false);
        var health = fixture.AddComponent<CombatHealth>();
        typeof(CombatHealth).GetField("showDamageNumbers", All).SetValue(health, false);
        var buffs = fixture.AddComponent<PlayerBuffController>();
        var source = new GameObject("ResponsibilityLifeStealActor");
        var sourceHealth = source.AddComponent<CombatHealth>();
        typeof(CombatHealth).GetField("showDamageNumbers", All).SetValue(sourceHealth, false);
        var melee = source.AddComponent<MeleeRuntime>(); melee.enabled = false;
        typeof(MeleeRuntime).GetField("activeStats", All).SetValue(melee, new WeaponFinalStats { lifeStealPercent = 50 });
        var module = typeof(DpsMeterDebugModule);
        var dummies = (List<EnemyActor>)module.GetField("dummies", All).GetValue(null);
        int notifications = 0;
        buffs.BuffsChanged += () => notifications++;
        var active = new List<BuffInstance>();
        void Apply()
        {
            buffs.ApplyBuff(new BuffDefinition { buffId = "responsibility-speed", duration = 60, tickInterval = 60, moveSpeedMultiplier = 1.3f });
            buffs.ApplySlowDebuff(60, .7f);
        }
        try
        {
            health.SetMaxHp(100, true); fixture.SetActive(true); yield return null;
            Apply(); check(buffs.GetActiveBuffs(active) == 2, "P2 automatic OnEnable supports buff and debuff", null);
            health.TakeDamage(new DamageInfo(10, Vector3.zero)); health.Heal(5);
            check(buffs.GetActiveBuffs(active) == 2, "P2 nonlethal damage and Heal retain effects in Play", null);
            int before = notifications; health.ResetHealth();
            check(buffs.GetActiveBuffs(active) == 0 && notifications == before + 1, "P2 actual Reset clears two effects once", null);
            Apply(); before = notifications; health.TakeDamage(new DamageInfo(200, Vector3.zero));
            check(health.IsDead && buffs.GetActiveBuffs(active) == 0 && notifications == before + 1, "P2 actual death clears effects once in Play", null);
            health.ResetHealth(); Apply(); before = notifications; fixture.SetActive(false);
            check(buffs.GetActiveBuffs(active) == 0 && notifications == before + 1, "P2 automatic OnDisable clears effects once", null);
            fixture.SetActive(true); yield return null; Apply(); before = notifications; health.ResetHealth();
            check(buffs.GetActiveBuffs(active) == 0 && notifications == before + 1, "P2 automatic re-enable reconnects once", null);
            before = notifications; health.ResetHealth();
            check(notifications == before && Mathf.Approximately(buffs.ActiveMoveSpeedMultiplier, 1), "P2 repeated empty Reset keeps baseline without notification", null);
            buffs.ApplyBuff(new BuffDefinition { buffId = "responsibility-expiry", duration = .2f, tickInterval = 60, moveSpeedMultiplier = 1.2f });
            float deadline = Time.realtimeSinceStartup + 5;
            while (buffs.GetActiveBuffs(active) != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            check(buffs.GetActiveBuffs(active) == 0, "P2 ordinary duration expiry still uses automatic Update", null);
            module.GetMethod("Spawn", All).Invoke(null, null);
            check(DpsMeterDebugModule.AliveDummies == 1, "V4 authored debug Spawn leases a real catalog dummy", null);
            var dummy = dummies.Single(); var dummyHealth = dummy.Health;
            check(dummyHealth.IsDeathFromDamagePrevented, "V4 authored Spawn installs owned death protection", null);
            dummyHealth.SetMaxHp(100, true); typeof(CombatHealth).GetField("currentHp", All).SetValue(dummyHealth, 5f);
            sourceHealth.SetMaxHp(100, true); typeof(CombatHealth).GetField("currentHp", All).SetValue(sourceHealth, 20f);
            var result = MeleeDamageResolver.Apply(new MeleeDamageRequest(dummyHealth, 100, default, 0, 0, 2, Vector3.zero, source, Vector3.zero, 0, true));
            float recorded = (float)DpsMeterDebugModule.TotalDamage;
            check(result.ActualDamage > 0 && Mathf.Approximately(result.ActualDamage, recorded) && Mathf.Approximately(5 - dummyHealth.CurrentHp, recorded), "V4 lethal actual damage matches DPS before automatic refill", new { result.ActualDamage, recorded, dummyHealth.CurrentHp });
            typeof(MeleeRuntime).GetMethod("ApplyOnHitEffects", All).Invoke(melee, new object[] { dummyHealth, result.ActualDamage, Vector3.zero });
            check(Mathf.Approximately(sourceHealth.CurrentHp, 20 + recorded * .5f), "V4 actual life steal reads damage before refill in Play", new { sourceHealth.CurrentHp, expected = 20 + recorded * .5f, parentHealthFound = melee.GetComponentInParent<CombatHealth>() != null });
            yield return null; yield return null;
            check(!dummyHealth.IsDead && Mathf.Approximately(dummyHealth.CurrentHp, dummyHealth.MaxHp), "V4 automatic runner Update refills surviving dummy", null);
            module.GetMethod("Despawn", All).Invoke(null, null);
            check(DpsMeterDebugModule.AliveDummies == 0 && !dummy.IsLeased && !dummyHealth.IsDeathFromDamagePrevented, "V4 authored Despawn releases lease and death protection", null);
            module.GetMethod("Spawn", All).Invoke(null, null);
            check(DpsMeterDebugModule.AliveDummies == 1 && dummies.Single().Health.IsDeathFromDamagePrevented, "V4 repeat authored Spawn reinstalls protection", null);
            module.GetMethod("Despawn", All).Invoke(null, null);
            check(DpsMeterDebugModule.AliveDummies == 0 && !dummyHealth.IsDeathFromDamagePrevented, "V4 repeat authored Despawn leaves no protected dummy", null);
        }
        finally
        {
            module.GetMethod("Despawn", All).Invoke(null, null);
            UnityEngine.Object.Destroy(fixture); UnityEngine.Object.Destroy(source);
            UnityEngine.Random.state = random;
        }
    }
    static void HealthChecks(Action<bool, string, object> check)
    {
        var buffs = MapRunBuffs.Current;
        if (buffs == null) throw new InvalidOperationException("Dungeon buff component missing.");
        var bonuses = (float[])typeof(MapRunBuffs).GetField("bonuses", All).GetValue(buffs);
        int index = (int)MapBuffKind.MaxHealth; float previous = bonuses[index];
        var fixture = new GameObject("RequiredFixHealthActor"); fixture.SetActive(false);
        var health = fixture.AddComponent<CombatHealth>();
        var progression = fixture.AddComponent<PlayerProgression>();
        typeof(PlayerProgression).GetField("health", All).SetValue(progression, health);
        typeof(PlayerProgression).GetField("<Level>k__BackingField", All).SetValue(progression, 1);
        bool Near(float a, float b) => Mathf.Abs(a - b) < .002f;
        void Expect(float max, string label) => check(Near(health.MaxHp, max), label, new { actual = health.MaxHp, expected = max });
        try
        {
            foreach (float multiplier in new[] { 1f, .902f })
            foreach (float card in new[] { 0f, .12f, .24f })
            {
                bonuses[index] = 0f; health.SetRunMapModifiers(1f, 1f);
                typeof(PlayerProgression).GetField("appliedHealthBonus", All).SetValue(progression, 0f);
                health.SetMaxHp(100f, true); health.SetRunMapModifiers(multiplier, 1f); bonuses[index] = card;
                for (int repeat = 0; repeat < 5; repeat++) progression.RefreshStats();
                Expect(100f * (1f + card) * multiplier, "Card and penalty remain stable across five stat refreshes");
                health.SetMaxHp(health.MaxHp, true);
                Expect(100f * (1f + card) * multiplier, "SetMaxHp(MaxHp,true) preserves effective maximum");
                check(Near(health.CurrentHp, health.MaxHp), "Refill retains current HP contract", null);
                health.SetRunMapModifiers(1f, 1f); bonuses[index] = 0f; progression.RefreshStats();
                Expect(100f, "Removing card and penalty restores base maximum");
            }
            health.SetMaxHp(100f, true); health.SetRunMapModifiers(.902f, .5f);
            typeof(CombatHealth).GetField("currentHp", All).SetValue(health, 40f);
            bonuses[index] = .12f; progression.RefreshStats();
            check(Near(health.CurrentHp, 45.412f), "Heal uses effective maximum increase and existing healing penalty", new { health.CurrentHp });
            bonuses[index] = .24f;
            typeof(PlayerProgression).GetMethod("RefreshGemStats", All).Invoke(progression, null);
            Expect(111.848f, "Non-healing gem refresh recalculates stable maximum");
            check(Near(health.CurrentHp, 45.412f), "Gem refresh does not heal", null);
        }
        finally { bonuses[index] = previous; UnityEngine.Object.DestroyImmediate(fixture); }
    }
}
#endif
