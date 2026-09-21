using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// Drives real Input Actions and runtime Update in Persistent -> Hideout.
// Timing gates are adjusted only on the live actor; no assets/scenes are saved.
[InitializeOnLoad]
public static class OverburstGoalGPlayModeVerifier
{
    private const string Key = "OverburstGoalGPlayModeVerifier.Active";
    private static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    private static readonly List<string> passed = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static PlayerInputFacade input;
    private static PlayerActorRuntime actor;
    private static MeleeRuntime melee;
    private static PlayerEvadeController evade;
    private static Keyboard keyboard;
    private static Mouse mouse;
    private static Gamepad pad;
    private static int lastFrame;
    private static double deadline;
    private static bool previousBackground;
    private static int previousFrameRate;
    private static float previousTimeScale;
    private static int evadeStarts;
    public static string LastResult => SessionState.GetString(Key + ".Result", "NOT_RUN");

    static OverburstGoalGPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("OVERBURST/Codex/Validate/Input/Validate GOAL G Play Mode")]
    public static void RunFromMenu()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        var scene = EditorSceneManager.GetActiveScene();
        Require(scene.name == PersistentSceneFlow.PersistentSceneName && !scene.isDirty,
            "Requires a clean PersistentScene");
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".Result", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            previousBackground = Application.runInBackground;
            previousFrameRate = Application.targetFrameRate;
            previousTimeScale = Time.timeScale;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            passed.Clear(); errors.Clear(); routines.Clear();
            keyboard = InputSystem.AddDevice<Keyboard>("GoalGKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("GoalGMouse");
            pad = InputSystem.AddDevice<Gamepad>("GoalGGamepad");
            routines.Push(Verify());
            lastFrame = -1;
            deadline = EditorApplication.timeSinceStartup + 180;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (LastResult == "RUNNING") SessionState.SetString(Key + ".Result", "FAIL interrupted");
            if (evade != null) evade.OnEvadeStarted -= OnEvade;
            if (input != null && input.RuntimeAsset != null) input.RuntimeAsset.devices = null;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            Application.runInBackground = previousBackground;
            Application.targetFrameRate = previousFrameRate;
            Time.timeScale = previousTimeScale;
        }
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            Debug.Log("[OverburstGoalGPlayModeVerifier] " + LastResult);
        }
    }

    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout after " + string.Join(",", passed));
            while (routines.Count > 0)
            {
                IEnumerator current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
            Require(errors.Count == 0, "Runtime errors: " + string.Join(" | ", errors));
            Finish("PASS " + string.Join(", ", passed) + "; errors=0");
        }
        catch (Exception exception) { Finish("FAIL " + exception.Message + "; passed=" + string.Join(",", passed)); }
    }

    private static void Finish(string result)
    {
        SessionState.SetString(Key + ".Result", result);
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    private static void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
    }

    private static void OnEvade(PlayerEvadeType type) => evadeStarts++;

    private static IEnumerator Verify()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            yield return null;
        actor = PlayerContext.GetOrCreate().CurrentActor;
        Require(actor != null, "Actor unavailable");
        input = actor.GetComponent<PlayerInputFacade>();
        melee = actor.GetComponent<MeleeRuntime>();
        evade = actor.GetComponent<PlayerEvadeController>();
        Require(input != null && input.CombatInputs != null && melee != null && evade != null, "Missing runtime");
        input.RuntimeAsset.devices = new InputDevice[] { keyboard, mouse, pad };
        var sword = AssetDatabase.LoadAssetAtPath<WeaponItemData>(
            "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset");
        Require(actor.Equipment.EquipWeaponItem(new ItemData(sword, 1, ItemGrade.Common)), "Equip failed");
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        evadeStarts = 0;
        evade.OnEvadeStarted += OnEvade;
        yield return Neutral();
        while (!actor.Movement.IsGrounded) yield return null;

        // Short press released before the combo window opens must execute once.
        yield return StartAndCloseWindow();
        int sequence = Get<int>(melee, "nextHitFeedbackSequenceId");
        yield return TapAttack();
        Require(input.CombatInputs.HasAttack && !input.AttackHeld, "Short attack was not buffered");
        OpenWindow();
        yield return null;
        Require(Get<int>(melee, "nextHitFeedbackSequenceId") == sequence + 1, "Buffered combo did not execute once");
        Require(!input.CombatInputs.HasAttack, "Accepted attack not consumed");
        yield return Seconds(0.35f);
        Require(Get<int>(melee, "nextHitFeedbackSequenceId") == sequence + 1, "Tap replayed twice");
        passed.Add("released-tap/consume-once");

        // Expiry uses real elapsed time even during slow motion.
        yield return StartAndCloseWindow();
        sequence = Get<int>(melee, "nextHitFeedbackSequenceId");
        yield return TapAttack();
        Time.timeScale = 0.1f;
        yield return Seconds(input.AttackBufferDuration + 0.06f);
        Require(!input.CombatInputs.HasAttack, "Unscaled expiry failed");
        OpenWindow(); Time.timeScale = previousTimeScale;
        yield return null;
        Require(Get<int>(melee, "nextHitFeedbackSequenceId") == sequence, "Expired attack fired");
        passed.Add("expiry/slow-motion");

        // Repeat presses replace a single slot, never build a queue.
        yield return StartAndCloseWindow();
        sequence = Get<int>(melee, "nextHitFeedbackSequenceId");
        yield return TapAttack(); yield return TapAttack();
        OpenWindow(); yield return null;
        yield return Seconds(0.3f);
        Require(Get<int>(melee, "nextHitFeedbackSequenceId") == sequence + 1, "Repeated taps created a queue");
        passed.Add("bounded-repeat");

        // Existing hold-to-chain semantics must remain usable.
        yield return ResetCombat();
        sequence = Get<int>(melee, "nextHitFeedbackSequenceId");
        MouseAttack(true);
        yield return Until(() => Get<int>(melee, "nextHitFeedbackSequenceId") >= sequence + 3, 5f);
        MouseAttack(false); yield return null;
        passed.Add("held-combo");

        // Same-frame evade wins without briefly accepting a melee attack.
        yield return ResetCombat();
        sequence = Get<int>(melee, "nextHitFeedbackSequenceId");
        int evades = evadeStarts;
        MouseAttack(true); KeyEvade(true);
        yield return null;
        MouseAttack(false); KeyEvade(false);
        yield return null;
        Require(evadeStarts == evades + 1 && Get<int>(melee, "nextHitFeedbackSequenceId") == sequence,
            "Same-frame evade priority failed");
        Require(!input.CombatInputs.HasAttack && !input.CombatInputs.HasEvade, "Evade left queued inputs");
        passed.Add("evade-priority");

        // Cooldown keeps a released Shift until permitted; cost paid at execution.
        yield return ResetCombat();
        Set(evade, "nextEvadeTime", Time.unscaledTime + 5f);
        evades = evadeStarts;
        float stamina = actor.GetComponent<PlayerStaminaController>().CurrentStamina;
        KeyEvade(true); yield return null; KeyEvade(false); yield return null;
        Require(input.CombatInputs.HasEvade && evadeStarts == evades, "Evade cooldown buffering failed");
        Require(actor.GetComponent<PlayerStaminaController>().CurrentStamina == stamina, "Rejected evade charged stamina");
        Set(evade, "nextEvadeTime", 0f); yield return null;
        Require(evadeStarts == evades + 1, "Released evade did not execute");
        yield return Seconds(0.55f);
        Require(evadeStarts == evades + 1, "Evade replayed");
        passed.Add("evade-cooldown/cost/once");

        yield return ResetCombat();
        Set(evade, "nextEvadeTime", Time.unscaledTime + 5f);
        evades = evadeStarts;
        KeyEvade(true); yield return null; KeyEvade(false); yield return null;
        yield return Seconds(input.EvadeBufferDuration + 0.05f);
        Set(evade, "nextEvadeTime", 0f); yield return null;
        Require(evadeStarts == evades && !input.CombatInputs.HasEvade, "Expired evade fired");
        passed.Add("evade-expiry");

        // Every boundary must drop a pending tap, even if it enters/exits in one frame.
        for (int boundary = 0; boundary < 9; boundary++)
        {
            yield return StartAndCloseWindow();
            yield return TapAttack();
            Require(input.CombatInputs.HasAttack, "Boundary fixture missing input " + boundary
                + " dt=" + Time.unscaledDeltaTime + " held=" + input.AttackHeld
                + " condition=" + actor.GetComponent<PlayerStateCoordinator>().CurrentCondition);
            switch (boundary)
            {
                case 0: GameplayInputBlocker.Block(actor); GameplayInputBlocker.Unblock(actor); break;
                case 1: input.DisableGameplay(); input.EnableGameplay(); break;
                case 2:
                    actor.GetComponent<PlayerStateCoordinator>().RequestCondition(actor, PlayerConditionState.Stunned);
                    actor.GetComponent<PlayerStateCoordinator>().ReleaseCondition(actor); break;
                case 3: melee.enabled = false; melee.enabled = true; break;
                case 4: input.enabled = false; input.enabled = true; break;
                case 5: actor.Equipment.EquipWeaponItem(new ItemData(sword, 1, ItemGrade.Common)); break;
                case 6: input.SendMessage("OnApplicationFocus", false); break;
                case 7:
                    Scene old = SceneManager.GetActiveScene();
                    Scene temp = SceneManager.CreateScene("GoalG_TransientBoundary");
                    SceneManager.SetActiveScene(temp); SceneManager.SetActiveScene(old);
                    SceneManager.UnloadSceneAsync(temp); break;
                case 8: input.GameplayMap.Disable(); input.GameplayMap.Enable(); break;
            }
            Require(!input.CombatInputs.HasAttack, "Boundary retained input " + boundary);
            yield return null;
        }
        passed.Add("UI/map/stun/component/facade/equipment/focus/scene-clear");

        yield return StartAndCloseWindow();
        yield return TapAttack();
        actor.Health.TakeDamage(new DamageInfo { damage = 1f, isDamageOverTime = true });
        Require(!input.CombatInputs.HasAttack, "Hit retained input");
        passed.Add("damage-clear");

        yield return ResetCombat();
        // Gamepad uses the same buffer through the actual configured Attack action.
        yield return StartAndCloseWindow();
        sequence = Get<int>(melee, "nextHitFeedbackSequenceId");
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.West));
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
        Require(input.CombatInputs.HasAttack, "Gamepad tap not buffered");
        OpenWindow(); yield return null;
        Require(Get<int>(melee, "nextHitFeedbackSequenceId") == sequence + 1, "Gamepad buffered attack failed");
        passed.Add("gamepad-buffer");
        yield return ResetCombat();

        yield return StartAndCloseWindow();
        yield return TapAttack();
        Require(input.CombatInputs.HasAttack, "Death fixture missing input");
        actor.Health.TakeDamage(new DamageInfo { damage = actor.Health.MaxHp * 10f, isDamageOverTime = true });
        Require(actor.Health.IsDead && !input.CombatInputs.HasAttack, "Death retained input");
        actor.Health.ResetHealth();
        sequence = Get<int>(melee, "nextHitFeedbackSequenceId");
        yield return Seconds(0.2f);
        Require(!input.CombatInputs.HasAttack && Get<int>(melee, "nextHitFeedbackSequenceId") == sequence,
            "Reset replayed pre-death input");
        passed.Add("death/reset-clear");

        // Keeping a button down while the facade re-enables must not revive a stale hold.
        yield return ResetCombat();
        MouseAttack(true); yield return null;
        melee.CancelCurrentAttackState();
        input.enabled = false; input.enabled = true;
        input.RuntimeAsset.devices = new InputDevice[] { keyboard, mouse, pad };
        sequence = Get<int>(melee, "nextHitFeedbackSequenceId");
        yield return Seconds(0.2f);
        Require(Get<int>(melee, "nextHitFeedbackSequenceId") == sequence, "Re-enable revived held attack");
        yield return Neutral();
        MouseAttack(true); yield return null;
        Require(Get<int>(melee, "nextHitFeedbackSequenceId") == sequence + 1, "Release gate never recovered");
        passed.Add("re-enable/release-gate");
        yield return ResetCombat();
    }

    private static IEnumerator StartAndCloseWindow()
    {
        yield return ResetCombat();
        MouseAttack(true); yield return null; MouseAttack(false); yield return null;
        Require(melee.IsAttackInProgress, "Attack fixture did not start");
        Set(melee, "attackDuration", 10f);
        Set(melee, "attackStartTime", Time.time);
        var step = Get<MeleeComboStepData>(melee, "activeAttackStep");
        step.comboInputWindow = new ComboNormalizedWindow { startNormalizedTime = 0.8f, endNormalizedTime = 1f };
        Set(melee, "activeAttackStep", step);
    }

    private static void OpenWindow()
    {
        var step = Get<MeleeComboStepData>(melee, "activeAttackStep");
        step.comboInputWindow = new ComboNormalizedWindow { startNormalizedTime = 0f, endNormalizedTime = 1f };
        Set(melee, "activeAttackStep", step);
    }

    private static IEnumerator ResetCombat()
    {
        yield return Neutral();
        yield return Until(() => !evade.IsEvading, 3f);
        melee.CancelCurrentAttackState();
        actor.GetComponent<PlayerAnimation>().CancelWeaponRuntimeState();
        actor.Movement.CancelWeaponActionLocks();
        Set(evade, "nextEvadeTime", 0f);
        actor.GetComponent<PlayerStaminaController>().RestoreFull();
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        yield return null;
    }

    private static IEnumerator Neutral()
    {
        MouseAttack(false); KeyEvade(false);
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null; yield return null;
    }

    private static IEnumerator TapAttack()
    {
        MouseAttack(true); yield return null;
        MouseAttack(false); yield return null;
    }

    private static void MouseAttack(bool down)
    {
        var value = new MouseState { position = new Vector2(Screen.width * 0.75f, Screen.height * 0.5f) };
        if (down) value = value.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(mouse, value);
    }

    private static void KeyEvade(bool down) => InputSystem.QueueStateEvent(keyboard,
        down ? new KeyboardState(UnityEngine.InputSystem.Key.LeftShift) : new KeyboardState());

    private static IEnumerator Seconds(float duration)
    {
        float end = Time.realtimeSinceStartup + duration;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    private static IEnumerator Until(Func<bool> condition, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!condition()) { Require(Time.realtimeSinceStartup < end, "Condition timeout"); yield return null; }
    }

    private static T Get<T>(object target, string field) => (T)Field(target, field).GetValue(target);
    private static void Set(object target, string field, object value) => Field(target, field).SetValue(target, value);
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing field " + name);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
