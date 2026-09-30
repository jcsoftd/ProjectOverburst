using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class OverburstElementLiveVerifier
{
    private static IEnumerator routine;
    private static GameObject root;
    private static PlayerActorRuntime actor;
    private static PlayerInputFacade input;
    private static MeleeRuntime melee;
    private static Mouse mouse;
    private static ItemData previousWeapon;
    private static int frame;
    private static double deadline;
    private static bool background;
    private static InputSettings originalSettings, fixtureSettings;
    private static MouseState queuedMouse;
    private static void DriveInput()
    {
        if (mouse != null && InputState.currentUpdateType == InputUpdateType.Dynamic)
            InputSystem.QueueStateEvent(mouse, queuedMouse);
    }
    private static readonly List<string> checks = new List<string>();
    private static readonly List<string> errors = new List<string>();
    public static string LastResult { get; private set; } = "NOT_RUN";
    public static string Begin()
    {
        if (!Application.isPlaying || routine != null) throw new InvalidOperationException("Requires idle Play Mode");
        LastResult = "RUNNING"; checks.Clear(); errors.Clear();
        background = Application.runInBackground; Application.runInBackground = true;
        originalSettings = InputSystem.settings;
        fixtureSettings = UnityEngine.Object.Instantiate(originalSettings);
        fixtureSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        fixtureSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings = fixtureSettings;
        queuedMouse = new MouseState();
        InputSystem.onBeforeUpdate += DriveInput;
        deadline = EditorApplication.timeSinceStartup + 100; frame = -1;
        Application.logMessageReceived += Log;
        routine = Verify(); EditorApplication.update += Tick;
        return LastResult;
    }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new Exception("timeout/interrupted");
            if (!routine.MoveNext()) Finish(true, null);
        }
        catch (Exception ex) { Finish(false, ex.ToString()); }
    }
    private static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static IEnumerator Verify()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName) yield return null;
        actor = PlayerContext.GetOrCreate().CurrentActor;
        Require(actor != null, "no player");
        input = actor.GetComponent<PlayerInputFacade>(); melee = actor.GetComponent<MeleeRuntime>();
        Require(input != null && melee != null, "missing input or melee");
        previousWeapon = actor.Equipment.CurrentWeaponItem;
        mouse = InputSystem.AddDevice<Mouse>("ElementVerifyMouse");
        input.RuntimeAsset.devices = new InputDevice[] { mouse };
        root = new GameObject("ElementLiveVerificationTargets");
        var targets = new List<CombatHealth>();
        for (int i = 0; i < 12; i++)
        {
            GameObject target = new GameObject("ElementTarget" + i); target.transform.SetParent(root.transform);
            CombatHealth health = target.AddComponent<CombatHealth>(); health.SetMaxHp(100000f, true);
            target.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
            target.AddComponent<CombatTarget>(); target.AddComponent<ElementalStatusController>();
            targets.Add(health);
        }
        var sword = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset");
        Require(sword != null, "missing sword");
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        while (!actor.Movement.IsGrounded) yield return null;
        // One representative real combo; other elements use the fast deterministic contract checks.
        for (int elementIndex = 0; elementIndex < 1; elementIndex++)
        {
            WeaponElement element = OverburstElementRules.At(elementIndex);
            melee.CancelCurrentAttackState();
            var weapon = new ItemData(sword, 1, ItemGrade.Common, 1, element);
            Require(actor.Equipment.EquipWeaponItem(weapon), "equip " + element);
            // Equipment switching intentionally requires a released button before accepting another attack.
            queuedMouse = new MouseState();
            yield return null;
            yield return null;
            OverburstElementEnergy energy = actor.GetComponent<OverburstElementEnergy>();
            if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
            energy.Clear();
            foreach (CombatHealth target in targets) target.ResetHealth();
            HashSet<string> comboSteps = new HashSet<string>();
            float end = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < end && (comboSteps.Count < 3 || energy.Amount < 30f))
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    float angle = Mathf.PI * 2f * i / targets.Count;
                    targets[i].transform.position = actor.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.1f;
                    CombatTargetRegistry.NotifySpatialChanged(targets[i].transform);
                }
                queuedMouse = new MouseState { position = new Vector2(Screen.width * .65f, Screen.height * .5f) }.WithButton(MouseButton.Left);
                if (melee.IsAttackInProgress)
                {
                    var step = (MeleeComboStepData)typeof(MeleeRuntime).GetField("activeAttackStep", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(melee);
                    if (!string.IsNullOrEmpty(step.attackId)) comboSteps.Add(step.attackId);
                }
                yield return null;
            }
            queuedMouse = new MouseState(); yield return null;
            melee.CancelCurrentAttackState();
            input.CombatInputs.ClearAttack();
            Require(comboSteps.Count >= 3, "did not execute existing three combo steps: " + element);
            Require(energy.Element == element && energy.Amount >= 30f, "real hits failed energy: " + element);
            CombatHealth prepared = null;
            foreach (CombatHealth target in targets)
                if (target.CurrentHp < target.MaxHp && target.GetComponent<ElementalStatusController>().HasStatus(element)) { prepared = target; break; }
            Require(prepared != null, "no real damage/status " + element);
            float before = energy.Amount;
            // This explicitly tests the future-heavy contract, not a playable heavy attack.
            Require(energy.TryCommitDischarge(30f, out OverburstElementDischarge discharge), "commit " + element);
            Require(discharge.TryResolveConfirmedHit(prepared, 1f, out OverburstDischargeResult result), "resolve " + element);
            Require(result.BonusDamage > 0f && energy.Amount == 0f, "discharge result " + element);
            discharge.End();
            checks.Add(element + ": real LMB 3-step combo, energy=" + before + ", bonus contract=" + result.BonusDamage + ", state consumed=" + result.ConsumedStacks);
        }
        queuedMouse = new MouseState();
        for (int i = 0; i < 12; i++) targets[i].gameObject.SetActive(false);
        var currentEnergy = actor.GetComponent<OverburstElementEnergy>(); currentEnergy.Clear();
        float missEnd = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < missEnd)
        {
            queuedMouse = new MouseState { position = new Vector2(Screen.width * .65f, Screen.height * .5f) }.WithButton(MouseButton.Left);
            yield return null;
        }
        Require(currentEnergy.Amount == 0f, "miss charged energy (or unrelated scene target in range)");
        checks.Add("miss does not charge");
        Require(errors.Count == 0, string.Join("\n", errors));
    }
    private static void Finish(bool success, string failure)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        routine = null;
        InputSystem.onBeforeUpdate -= DriveInput;
        if (originalSettings != null) InputSystem.settings = originalSettings;
        if (fixtureSettings != null) UnityEngine.Object.DestroyImmediate(fixtureSettings);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (input != null && input.RuntimeAsset != null) input.RuntimeAsset.devices = null;
        if (melee != null) melee.CancelCurrentAttackState();
        if (actor != null)
        {
            if (previousWeapon != null) actor.Equipment.EquipWeaponItem(previousWeapon);
            else actor.Equipment.ClearCurrentWeapon();
            actor.GetComponent<OverburstElementEnergy>()?.Clear();
        }
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
        Application.runInBackground = background;
        LastResult = (success ? "PASS\n" : "FAIL\n" + failure + "\n") + string.Join("\n", checks);
    }
}
