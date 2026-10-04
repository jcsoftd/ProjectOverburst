using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>제품 입력과 실제 Animator로 접지음의 동기화·행동 차단·계정 반환을 검증한다.</summary>
[InitializeOnLoad]
public static partial class PlayerFootstepProductVerifier
{
    const string PendingKey = "Overburst.PlayerFootstepProductVerifier.Pending";
    const string DeadlineKey = "Overburst.PlayerFootstepProductVerifier.Deadline";
    const string ReturnKey = "Overburst.PlayerFootstepProductVerifier.ReturnToRealAccount";
    const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<object> checks = new List<object>(), samples = new List<object>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator work;
    static string output;
    static int frame, failures;
    static double deadline;
    static bool background, lightOnly, dashVisualOnly, paletteOnly, dashHeavyOnly;
    static bool reloadLocked, refreshLocked;
    static Keyboard keyboard;
    static Mouse mouse;
    static PlayerInputFacade input;
    static PlayerActorRuntime actor;
    static PlayerEvadeController evade;
    static MeleeRuntime melee;
    static PlayerMovement movement;
    static Animator animator;
    static PlayerEvadePoseProbe poseProbe;
    static InputDevice[] previousDevices;
    static InputSettings previousInputSettings, verifierInputSettings;
    static string previousInputSettingsJson;
    static GameObject blocker;
    static readonly List<UnityEngine.Object> fixtures = new List<UnityEngine.Object>();
    static Vector3 origin, startPosition, endPosition;
    static float startTime, endTime;
    static int starts, ends;
    static Vector3 forward;
    static Vector3 testFacing;
    static bool poseNextStart;
    static bool heldMove, heldShift, heldLeft, heldRight;
    static bool pointerAtComparison;
    static bool requestPause, requestResume, pauseRecorded, invalidateOnCompletion;
    static float frozenProgress;
    static Vector3 frozenPosition;
    static ItemData weaponItem;

    static PlayerFootstepProductVerifier()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))) EditorApplication.update += AutoBegin;
        if (!string.IsNullOrEmpty(SessionState.GetString(ReturnKey, "")))
        {
            EditorApplication.playModeStateChanged += RestoreAccountOnEditorReturn;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) ScheduleEditorAccountReturn();
        }
    }
    public static void StartIsolated(string directory, bool onlyDodgeLight = false, bool onlyDashVisual = false, bool onlyPalette = false, bool onlyDashHeavy = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 Editor가 필요합니다.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("이전 Play의 실제 계정 반환 후 시작하세요.");
        string target = PlayerFootstepNativeContactBuilder.ValidateOutput(directory); Directory.CreateDirectory(target);
        SessionState.SetBool(PendingKey + ".LightOnly", onlyDodgeLight);
        SessionState.SetBool(PendingKey + ".DashHeavyOnly", onlyDashHeavy);
        SessionState.SetBool(PendingKey + ".DashVisualOnly", onlyDashVisual);
        SessionState.SetBool(PendingKey + ".PaletteOnly", onlyPalette);
        SessionState.SetString(PendingKey, target);
        SessionState.SetString(DeadlineKey, (EditorApplication.timeSinceStartup + 120).ToString(System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.update -= AutoBegin; EditorApplication.update += AutoBegin;
        var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }).ToArray();
        string activeSceneBefore = SceneManager.GetActiveScene().path;
        string playStartSceneBefore = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        var persistent = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        if (persistent == null)
        {
            ClearPending(); SessionState.EraseBool(PendingKey + ".LightOnly");
            throw new InvalidOperationException("검증에 필요한 PersistentScene 자산이 없습니다.");
        }
        File.WriteAllText(Path.Combine(target, "Before.json"), JsonConvert.SerializeObject(new { scenes, activeSceneBefore, playStartSceneBefore }, Formatting.Indented));
        SessionState.SetString(ReturnKey + ".StartSceneBefore", playStartSceneBefore);
        EditorSceneManager.playModeStartScene = persistent;
        SessionState.SetString(ReturnKey, target);
        SessionState.SetBool(ReturnKey + ".BackgroundBefore", Application.runInBackground);
        Application.runInBackground = true;
        SessionState.SetString(ReturnKey + ".Deadline", (EditorApplication.timeSinceStartup + 900).ToString(System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.playModeStateChanged -= RestoreAccountOnEditorReturn;
        EditorApplication.playModeStateChanged += RestoreAccountOnEditorReturn;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(target, "IsolatedAccount")); }
        catch { ClearPending(); ScheduleEditorAccountReturn(); throw; }
    }
    static void RestoreAccountOnEditorReturn(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) ScheduleEditorAccountReturn();
    }
    static void ScheduleEditorAccountReturn()
    {
        // Run after every EnteredEditMode listener: the save guard first marks
        // the completed isolated run as blocked, then this owner returns it.
        EditorApplication.update -= RestoreEditorAccount;
        EditorApplication.update += RestoreEditorAccount;
    }
    static void RestoreEditorAccount()
    {
        string target = SessionState.GetString(ReturnKey, "");
        if (string.IsNullOrEmpty(target))
        {
            EditorApplication.update -= RestoreEditorAccount;
            EditorApplication.playModeStateChanged -= RestoreAccountOnEditorReturn;
            return;
        }
        double limit = double.Parse(SessionState.GetString(ReturnKey + ".Deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (EditorApplication.timeSinceStartup > limit)
        {
            EditorApplication.update -= RestoreEditorAccount;
            EditorApplication.playModeStateChanged -= RestoreAccountOnEditorReturn;
            File.WriteAllText(Path.Combine(target,"EditorAccountReturn.json"),"{\"status\":\"FAIL\",\"reason\":\"return timeout\"}");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        if (!string.IsNullOrEmpty(current)
            && !Path.GetFullPath(current).StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        foreach (string suffix in new[] { "active", "prepared" })
        {
            string other = SessionState.GetString("Overburst.IsolatedSavePlayGuard." + suffix, "");
            if (!string.IsNullOrEmpty(other) && !Path.GetFullPath(other).StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        }
        ClearPending(); SessionState.EraseBool(PendingKey + ".LightOnly");
        SessionState.EraseBool(PendingKey + ".DashVisualOnly");
        SessionState.EraseBool(PendingKey + ".PaletteOnly");
        SessionState.EraseBool(PendingKey + ".DashHeavyOnly");
        Application.runInBackground = SessionState.GetBool(ReturnKey + ".BackgroundBefore", Application.runInBackground);
        SessionState.EraseBool(ReturnKey + ".BackgroundBefore");
        bool previouslyBlocked = IsolatedSavePlayGuard.RequiresAccountChoice;
        IsolatedSavePlayGuard.UseRealAccount();
        bool restored = !IsolatedSavePlayGuard.RequiresAccountChoice
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable));
        if (AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity")
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(ReturnKey + ".StartSceneBefore", ""));
        SessionState.EraseString(ReturnKey + ".StartSceneBefore");
        File.WriteAllText(Path.Combine(target,"EditorAccountReturn.json"),JsonConvert.SerializeObject(new{status=restored?"PASS":"FAIL",previouslyBlocked,restored},Formatting.Indented));
        SessionState.EraseString(ReturnKey); SessionState.EraseString(ReturnKey + ".Deadline");
        EditorApplication.update -= RestoreEditorAccount;
        EditorApplication.playModeStateChanged -= RestoreAccountOnEditorReturn;
    }
    static void ClearPending()
    {
        EditorApplication.update -= AutoBegin; SessionState.EraseString(PendingKey); SessionState.EraseString(DeadlineKey);
    }
    static void AutoBegin()
    {
        string target = SessionState.GetString(PendingKey, "");
        if (string.IsNullOrEmpty(target)) { ClearPending(); return; }
        double limit = double.Parse(SessionState.GetString(DeadlineKey, "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (EditorApplication.timeSinceStartup > limit)
        {
            ClearPending(); File.WriteAllText(Path.Combine(target, "BootResult.json"), "{\"status\":\"FAIL\",\"reason\":\"boot timeout\"}");
            if (EditorApplication.isPlaying && IsolatedSavePlayGuard.ActiveDirectory.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) EditorApplication.ExitPlaymode();
            return;
        }
        if (EditorApplication.isPlaying && IsolatedSavePlayGuard.ActiveDirectory.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || !Overburst.Persistence.AccountBootstrap.Ready || PlayerContext.GetOrCreate()?.CurrentActor == null) return;
        ClearPending();
        try { Begin(target); }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(target, "BootResult.json"), JsonConvert.SerializeObject(new { status = "FAIL", error = e.ToString() }));
            if (work != null || stack.Count > 0) Finish("FAIL", false);
            if (EditorApplication.isPlaying && IsolatedSavePlayGuard.ActiveDirectory.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                EditorApplication.ExitPlaymode();
            else if (!EditorApplication.isPlayingOrWillChangePlaymode) ScheduleEditorAccountReturn();
        }
    }

    public static void Begin(string directory)
    {
        if (work != null || stack.Count > 0) throw new InvalidOperationException("검증 진행 중");
        output = PlayerFootstepNativeContactBuilder.ValidateOutput(directory);
        if (!EditorApplication.isPlaying || !Overburst.Persistence.AccountBootstrap.Ready || string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("부팅이 끝난 격리 Play가 필요합니다.");
        Directory.CreateDirectory(output); checks.Clear(); samples.Clear(); errors.Clear(); failures = 0; frame = -1;
        lightOnly = SessionState.GetBool(PendingKey + ".LightOnly", false); SessionState.EraseBool(PendingKey + ".LightOnly");
        dashVisualOnly = SessionState.GetBool(PendingKey + ".DashVisualOnly", false); SessionState.EraseBool(PendingKey + ".DashVisualOnly");
        paletteOnly = SessionState.GetBool(PendingKey + ".PaletteOnly", false); SessionState.EraseBool(PendingKey + ".PaletteOnly");
        dashHeavyOnly = SessionState.GetBool(PendingKey + ".DashHeavyOnly", false); SessionState.EraseBool(PendingKey + ".DashHeavyOnly");
        deadline = EditorApplication.timeSinceStartup + 600;
        background = SessionState.GetBool(ReturnKey + ".BackgroundBefore", Application.runInBackground); Application.runInBackground = true;
        EditorApplication.LockReloadAssemblies(); reloadLocked = true;
        AssetDatabase.DisallowAutoRefresh(); refreshLocked = true;
        work = Run(); EditorApplication.update += Tick; Application.logMessageReceived += Log;
        EditorApplication.playModeStateChanged += State; AssemblyReloadEvents.beforeAssemblyReload += Abort;
    }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void Check(bool ok, string label) { checks.Add(new { label, passed = ok }); if (!ok) { failures++; throw new InvalidOperationException(label); } }
    static void Progress(string phase) => File.WriteAllText(Path.Combine(output, "Progress.json"), JsonConvert.SerializeObject(new { status = "RUNNING", phase, count = checks.Count, samples }, Formatting.Indented));
    static void Log(string message, string trace, LogType kind) { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors.Add(message + "\n" + trace); }
    static void State(PlayModeStateChange value) { if (value == PlayModeStateChange.ExitingPlayMode) Finish("ABORTED", false); }
    static void Abort() => Finish("RELOADING", false);
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate(); if (!Application.isPlaying || frame == Time.frameCount) return; frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("회피 검증 시간 제한");
            if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (current.MoveNext()) { if (current.Current is IEnumerator nested) { stack.Push(nested); continue; } return; }
                (stack.Pop() as IDisposable)?.Dispose();
            }
            Finish(errors.Count == 0 ? "PASS" : "FAIL", true);
        }
        catch (Exception e) { errors.Add(e.ToString()); Finish("FAIL", true); }
    }
    static void Finish(string status, bool exit)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        EditorApplication.playModeStateChanged -= State; AssemblyReloadEvents.beforeAssemblyReload -= Abort;
        try
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = background;
            File.WriteAllText(Path.Combine(output, "PlayResult.json"), JsonConvert.SerializeObject(new { status, failures, checks, samples, errors }, Formatting.Indented));
        }
        finally
        {
            if (refreshLocked) { AssetDatabase.AllowAutoRefresh(); refreshLocked = false; }
            if (reloadLocked) { EditorApplication.UnlockReloadAssemblies(); reloadLocked = false; }
            if (exit && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }
    static IEnumerator Wait(float seconds) { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }
    static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    static IEnumerator WaitLocomotionLoops(int layer, string stateName, float loops)
    {
        int expected = Animator.StringToHash(stateName); float limit = Time.unscaledTime + 45f;
        while (Time.unscaledTime < limit && (animator.IsInTransition(layer)
            || animator.GetCurrentAnimatorStateInfo(layer).shortNameHash != expected)) yield return null;
        Check(animator.GetCurrentAnimatorStateInfo(layer).shortNameHash == expected, "이동 루프 상태 진입 " + stateName);
        float start = animator.GetCurrentAnimatorStateInfo(layer).normalizedTime;
        while (Time.unscaledTime < limit && animator.GetCurrentAnimatorStateInfo(layer).normalizedTime - start < loops) yield return null;
        Check(animator.GetCurrentAnimatorStateInfo(layer).normalizedTime - start >= loops, "실제 Animator 루프 진행 " + loops);
    }
    static void Send(bool move = false, bool shift = false, bool left = false, bool right = false)
    {
        heldMove = move; heldShift = shift; heldLeft = left; heldRight = right;
    }
    static void PoseStart()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || actor == null || keyboard == null || mouse == null) return;
        if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
        if (!mouse.enabled) InputSystem.EnableDevice(mouse);
        var keys = new List<Key>(); if (heldMove) keys.Add(Key.W); if (heldShift) keys.Add(Key.LeftShift);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
        Vector2 point = Camera.main != null ? (Vector2)Camera.main.WorldToScreenPoint(actor.transform.position + testFacing * 6f) : Vector2.zero;
        if (pointerAtComparison) point = new Vector2(Mathf.Max(8f, Screen.width - 620f) + 145f, Screen.height - 164f);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = (ushort)((heldLeft ? 1 : 0) | (heldRight ? 2 : 0)) });
        if (poseNextStart) actor.transform.rotation = Quaternion.LookRotation(testFacing);
        if (requestPause)
        {
            requestPause = false; OverburstGameMenu.Instance.Open();
            frozenProgress = evade.ActiveNormalizedTime; frozenPosition = actor.transform.position; pauseRecorded = true;
        }
        if (requestResume) { requestResume = false; OverburstGameMenu.Instance.Close(); }
    }
    static void Started(PlayerEvadeType type) { poseNextStart = false; starts++; startPosition = actor.transform.position; startTime = Time.unscaledTime; }
    static void Ended(PlayerEvadeType type)
    {
        ends++; endPosition = actor.transform.position; endTime = Time.unscaledTime;
        if (invalidateOnCompletion) input.CombatInputs.Invalidate();
    }
    static IEnumerator Reset(bool combat = true, float angle = 0)
    {
        Send(); melee.CancelCurrentAttackState(); evade.CancelForKnockdown(); actor.GetComponent<PlayerKnockdownController>()?.ResetReaction();
        actor.GetComponent<PlayerAnimation>()?.CancelWeaponRuntimeState();
        if (actor.Equipment.CurrentWeaponItem != weaponItem)
            Check(actor.Equipment.EquipWeaponItem(weaponItem), "시험 기준 무기 복원");
        input.CombatInputs.Invalidate();
        if (combat) PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        else PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System);
        testFacing = Quaternion.Euler(0, -angle, 0) * forward;
        ActorTeleportUtility.TeleportSafely(actor.transform, origin, Quaternion.LookRotation(testFacing));
        yield return Wait(.9f);
        if (combat) PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        else PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System);
        yield return Frames(2); starts = ends = 0;
        int combatLayer = animator.GetLayerIndex("Combat_MeleeWeapon");
        float readyLimit = Time.unscaledTime + 5f;
        while (Time.unscaledTime < readyLimit && (combat
            ? animator.GetLayerWeight(combatLayer) < .99f || animator.GetCurrentAnimatorStateInfo(combatLayer).shortNameHash != Animator.StringToHash("Melee_SwordIdle")
            : animator.GetLayerWeight(combatLayer) > .01f)) yield return null;
        Check(combat ? animator.GetLayerWeight(combatLayer) >= .99f && animator.GetCurrentAnimatorStateInfo(combatLayer).shortNameHash == Animator.StringToHash("Melee_SwordIdle")
            : animator.GetLayerWeight(combatLayer) <= .01f, "풋스텝 시험 전 안정된 이동 모드 " + combat);
    }
    static IEnumerator Run()
    {
        float bootLimit = Time.unscaledTime + 25f;
        while ((PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            && Time.unscaledTime < bootLimit) yield return null;
        Check(PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching
            && PersistentSceneFlow.Instance.CurrentSubSceneName == PersistentSceneFlow.HideoutSceneName,
            "풋스텝 검증 실제 Hideout 로딩 완료");
        actor = PlayerContext.GetOrCreate().CurrentActor; input = PlayerInputFacade.Current;
        evade = actor.GetComponent<PlayerEvadeController>(); melee = actor.GetComponent<MeleeRuntime>(); movement = actor.GetComponent<PlayerMovement>();
        animator = actor.GetComponentInChildren<Animator>(true); var ui = EnemyThemeTrialHarness.Current;
        var originalMode = animator.updateMode; float originalSpeed = animator.speed;
        try
        {
            previousInputSettings = InputSystem.settings;
            previousInputSettingsJson = EditorJsonUtility.ToJson(previousInputSettings);
            verifierInputSettings = UnityEngine.Object.Instantiate(previousInputSettings);
            verifierInputSettings.hideFlags = HideFlags.HideAndDontSave;
            verifierInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            verifierInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = verifierInputSettings;
            Check(evade != null && evade.Profile != null && ui != null, "제품 회피 프로필·시험 구역");
            actor.Health.SetMaxHp(1000000, true);
            weaponItem = new ItemData(AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath), 1, ItemGrade.Common, element: WeaponElement.Fire);
            Check(actor.Equipment.EquipWeaponItem(weaponItem), "실제 대검 장착"); melee.SetManualInputEnabled(true);
            if (!ui.InArena) ui.ToggleArena(); yield return Wait(1f);
            origin = actor.transform.position; forward = movement.ResolveMoveDirection(Vector2.up); forward.y = 0; forward.Normalize();
            previousDevices = input.RuntimeAsset.devices.HasValue ? input.RuntimeAsset.devices.Value.ToArray() : null;
            keyboard = InputSystem.AddDevice<Keyboard>("OwnedEvadeKeyboard"); mouse = InputSystem.AddDevice<Mouse>("OwnedEvadeMouse");
            input.RuntimeAsset.devices = new InputDevice[] { keyboard, mouse };
            testFacing = forward; InputSystem.onBeforeUpdate += PoseStart;
            blocker = new GameObject("OwnedEvadeInputBlocker");
            evade.OnEvadeStarted += Started; evade.OnEvadeEnded += Ended;
            poseProbe = blocker.AddComponent<PlayerEvadePoseProbe>(); poseProbe.animator = animator;
            yield return VerifyFootstepSync();
        }
        finally
        {
            InputSystem.onBeforeUpdate -= PoseStart; poseNextStart = false; heldMove = heldShift = heldLeft = heldRight = false;
            requestPause = requestResume = pauseRecorded = invalidateOnCompletion = false;
            pointerAtComparison = false;
            if (OverburstGameMenu.IsOpen) OverburstGameMenu.Instance?.Close();
            if (evade != null) { evade.OnEvadeStarted -= Started; evade.OnEvadeEnded -= Ended; evade.CancelForKnockdown(); evade.enabled = true; }
            if (input != null) { input.EnableGameplay(); input.RuntimeAsset.devices = previousDevices; }
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (previousInputSettings != null)
            {
                InputSystem.settings = previousInputSettings;
                bool restored = InputSystem.settings == previousInputSettings && EditorJsonUtility.ToJson(previousInputSettings) == previousInputSettingsJson;
                File.WriteAllText(Path.Combine(output,"InputSettingsRestored.json"),JsonConvert.SerializeObject(new{status=restored?"PASS":"FAIL",restored,originalAssetUnchanged=restored},Formatting.Indented));
                if(!restored)errors.Add("격리 입력 설정 원본 복원 실패");
            }
            if (verifierInputSettings != null) UnityEngine.Object.DestroyImmediate(verifierInputSettings);
            verifierInputSettings = previousInputSettings = null; previousInputSettingsJson = null;
            keyboard = null; mouse = null;
            poseProbe = null;
            if (blocker != null) { GameplayInputBlocker.Unblock(blocker); OverburstTimeEffectArbiter.ClearOwner(blocker); UnityEngine.Object.Destroy(blocker); }
            OverburstTimeEffectArbiter.SetPaused(false);
            foreach (var fixture in fixtures) if (fixture != null) UnityEngine.Object.Destroy(fixture); fixtures.Clear();
            melee?.CancelCurrentAttackState(); actor?.GetComponent<PlayerKnockdownController>()?.ResetReaction();
        }
    }
    static IEnumerator StartDodge(bool move, bool left = false, bool right = false)
    {
        heldShift = false; yield return Frames(2);
        Check(!input.EvadeHeld && !Field<bool>(input.CombatInputs, "evadeNeedsRelease"), "가상 Shift 해제 프레임 확인");
        poseNextStart = true;
        Send(move, true, left, right);
        float limit = Time.unscaledTime + .15f;
        do { yield return null; } while (!evade.IsEvading && Time.unscaledTime < limit);
        Check(evade.IsEvading, "실제 Shift 액션 회피 수락"
            + " input=" + input.CombatInputs.HasEvade + " shift=" + keyboard.leftShiftKey.isPressed
            + " held=" + input.EvadeHeld + " gameplay=" + input.IsGameplayEnabled
            + " release=" + Field<bool>(input.CombatInputs, "evadeNeedsRelease") + " focused=" + Application.isFocused
            + " blocked=" + GameplayInputBlocker.IsGameplayInputBlocked + " condition=" + actor.GetComponent<PlayerStateCoordinator>()?.CurrentCondition
            + " mode=" + PlayerCombatModeController.IsSharedCombatModeActive() + " can=" + evade.CanEvadeInCurrentMode
            + " clock=" + OverburstGameClock.UnscaledTime + " next=" + Field<float>(evade, "nextEvadeTime")
            + " locked=" + movement.IsMeleeAttackMoveLocked + " attacking=" + melee.IsAttackInProgress
            + " enabled=" + evade.isActiveAndEnabled); Send(move, false, left, right);
    }
}
public static partial class PlayerFootstepProductVerifier
{
    public static void StartFootstepIsolated(string directory)
    {
        PlayerFootstepNativeContactBuilder.RequireIdle();
        StartIsolated(directory);
    }

    static IEnumerator VerifyFootstepSync()
    {
        deadline = EditorApplication.timeSinceStartup + 600;
        var emitter = actor.GetComponent<FootstepEmitter>(); var resolver = actor.GetComponent<SurfaceResolver>();
        var motor = actor.GetComponent<OverburstCharacterMotor3D>(); var rig = animator.GetComponent<HumanoidFootContactRig>();
        var voice = Field<AudioSource>(emitter, "audioSource");
        var contacts = new List<object>(); var accepted = new List<PlayerFootstepContact>();
        var emitFrames = new HashSet<int>(); var phases = new List<object>();
        int duplicateFrames = 0, unsafeContacts = 0; string phase = "setup";
        float oldSpeed = animator.speed; int oldRate = Application.targetFrameRate, oldVsync = QualitySettings.vSyncCount;
        bool oldWalk = Field<bool>(movement, "isWalkMode");
        Vector3 oldOrigin = origin;
        Action<PlayerFootstepContact> onContact = c =>
        {
            accepted.Add(c);
            if (!emitFrames.Add(Time.frameCount)) duplicateFrames++;
            if (!motor.IsGrounded || motor.StandingOnEnemy || movement.IsEvading || movement.IsKnockedDown
                || melee.IsAttackInProgress || GameplayInputBlocker.IsGameplayInputBlocked || Time.timeScale <= 0) unsafeContacts++;
            Transform heel = c.Foot == 0 ? rig.LeftHeel : rig.RightHeel, toe = c.Foot == 0 ? rig.LeftToe : rig.RightToe;
            contacts.Add(new { phase, frame = Time.frameCount, scaledTime = Time.time, dt = Time.deltaTime, c.Foot,
                clip = c.Clip.name, c.StateHash, c.EventSeconds, c.NormalizedTime, c.EventWeight, c.SelectedWeight,
                soleHeight = Mathf.Min(heel.position.y, toe.position.y) - motor.GroundSurfaceHeight,
                probePosition = new[] { emitter.LastSamplePosition.x, emitter.LastSamplePosition.y, emitter.LastSamplePosition.z },
                profile = emitter.LastProfile.name, voicePlaying = voice != null && voice.isPlaying });
        };
        GameObject floor = null;
        try
        {
            Check(emitter != null && resolver != null && rig != null && animator.GetComponent<PlayerFootstepAnimationRelay>() != null, "실제 플레이어 풋스텝 연결");
            emitter.ContactAccepted += onContact; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "OwnedFootstepTestGround"; fixtures.Add(floor);
            floor.layer = 6; floor.transform.position = origin + Vector3.up * 9.75f; floor.transform.localScale = new Vector3(240, .5f, 240);
            floor.GetComponent<Renderer>().enabled = false;
            var surface = floor.AddComponent<SurfaceOverride>(); surface.Configure(resolver.Resolve());
            ActorTeleportUtility.TeleportSafely(actor.transform, origin + Vector3.up * 10.05f, Quaternion.LookRotation(forward));
            yield return Wait(.6f); origin = actor.transform.position;
            int initialProbes = resolver.SurfaceProbeCount, initialSteps = emitter.StepEmissionCount, initialLandings = emitter.LandingEmissionCount;
            foreach (bool walking in new[] { true, false })
            {
                phase = walking ? "ExplorationWalk20" : "ExplorationRun20"; yield return Reset(false);
                movement.GetType().GetField("isWalkMode", Private).SetValue(movement, walking);
                int start = accepted.Count; Send(true); yield return WaitLocomotionLoops(0, "NormalMove", 20); Send(); yield return Frames(2);
                var segment = accepted.Skip(start).ToArray();
                Check(segment.Length >= 36 && segment.Skip(3).All(c => c.Clip.name == (walking ? "Walk_Lfoot_JawFixed" : "Jog_Lfoot_JawFixed")), phase + " 20루프 접지 이벤트");
                Check(segment.Select(c => c.Foot).Distinct().Count() == 2, phase + " 좌우 접지 수신");
                phases.Add(new { phase, contacts = segment.Length, legacySteps = emitter.LegacyStepCount }); Progress(phase);
            }
            movement.GetType().GetField("isWalkMode", Private).SetValue(movement, false);
            foreach (float angle in new[] { 0f, 180f, -90f, 90f, -45f, 45f, -135f, 135f })
            {
                phase = "CombatDirection" + angle; yield return Reset(true, angle); int start = accepted.Count;
                Send(true); yield return WaitLocomotionLoops(animator.GetLayerIndex("Combat_MeleeWeapon"), "Melee_Locomotion", 10); Send(); yield return Frames(2);
                var segment = accepted.Skip(start).ToArray();
                phases.Add(new { phase, contacts = segment.Length, clips = segment.Select(c => c.Clip.name).Distinct().ToArray() }); Progress(phase);
                Check(segment.Length >= 16 && segment.All(c => c.Clip.name.StartsWith("Sword_Loop_") || c.Clip.name.StartsWith("Sword_Start_")), phase + " 10루프 이동 접지 count=" + segment.Length
                    + " clips=" + string.Join(",",segment.Select(c=>c.Clip.name).Distinct()));
            }
            foreach (float speed in new[] { .5f, 1f, 1.1f, 1.5f, 2f })
            {
                phase = "AnimatorSpeed" + speed; yield return Reset(true); animator.speed = speed; int start = accepted.Count;
                Send(true); yield return WaitLocomotionLoops(animator.GetLayerIndex("Combat_MeleeWeapon"), "Melee_Locomotion", 6); Send(); yield return Frames(2);
                Check(accepted.Count - start >= 10, phase + " 배속에 연동된 접지"); phases.Add(new { phase, contacts = accepted.Count - start });
            }
            animator.speed = oldSpeed;
            foreach (int rate in new[] { 30, 60, 120 })
            {
                phase = "TargetFPS" + rate; Application.targetFrameRate = rate; yield return Reset(true, 22.5f);
                int start = accepted.Count; Send(true); yield return Wait(3); Send(); yield return Frames(2);
                Check(accepted.Count - start >= 7, phase + " 혼합 방향 접지 수신"); phases.Add(new { phase, contacts = accepted.Count - start });
            }
            Application.targetFrameRate = 60;
            phase = "GuardMove";
            if (actor.Equipment.CanCurrentWeaponUseMeleeGuard)
            {
                yield return Reset(true); int guardStart = accepted.Count; Send(true, false, false, true);
                int guardLayer = animator.GetLayerIndex("Combat_MeleeWeapon_TransitionLower");
                yield return WaitLocomotionLoops(guardLayer, "Melee_TransitionLower_Locomotion", 3);
                Check(movement.IsMeleeGuarding && animator.GetLayerWeight(guardLayer) >= .99f, "실제 가드 입력과 하체 레이어");
                int guardHash = animator.GetCurrentAnimatorStateInfo(guardLayer).fullPathHash; Send(); yield return Frames(2);
                phases.Add(new { phase, contacts = accepted.Count - guardStart }); Progress(phase);
                Check(accepted.Count - guardStart >= 4 && accepted.Skip(guardStart).All(c => c.StateHash == guardHash), "가드 이동 하체 접지");
            }
            else
            {
                Check(actor.Equipment.CurrentWeaponAimMode == WeaponAimMode.None, "현재 대검은 가드 비활성 계약");
                phases.Add(new { phase, status = "NOT_APPLICABLE", reason = "Current weapon aim mode is None; right click is a heavy attack input." });
            }
            phase = "ModeTransition"; yield return Reset(false); int transitionStart = accepted.Count; Send(true); yield return Wait(1);
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); yield return Wait(1);
            PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System); yield return Wait(1); Send(); yield return Frames(2);
            int lowerHash = Animator.StringToHash(Field<string[]>(emitter, "animationContactStates").Single(s => s.EndsWith(".Melee_TransitionLower_Locomotion")));
            int lowerContacts = accepted.Skip(transitionStart).Count(c => c.StateHash == lowerHash);
            phases.Add(new { phase, contacts = accepted.Count - transitionStart, lowerContacts }); Progress(phase);
            Check(accepted.Count - transitionStart >= 4 && lowerContacts > 0, "모드 장착·해제 중 실제 하체 접지와 탐험/전투 연속 재생");
            phase = "Stop"; yield return Reset(); int quiet = emitter.StepEmissionCount; yield return Wait(.8f);
            Check(emitter.StepEmissionCount == quiet, "정지 중 이동 발소리 없음");
            phase = "BlockedByWall"; yield return Reset(true);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(wall); wall.name = "OwnedFootstepWall"; wall.layer = 6;
            wall.transform.position = actor.transform.position + forward * 1.2f + Vector3.up; wall.transform.localScale = new Vector3(5, 3, .5f);
            wall.transform.rotation = Quaternion.LookRotation(forward); wall.GetComponent<Renderer>().enabled = false;
            Send(true); yield return Wait(.9f); quiet = emitter.StepEmissionCount; yield return Wait(.8f);
            Check(emitter.StepEmissionCount == quiet, "벽에 막힌 실제 변위0에서 접지음 없음");
            Send(); fixtures.Remove(wall); UnityEngine.Object.Destroy(wall); yield return Frames(2);
            phase = "InputBlock"; Send(true); yield return Wait(.5f); GameplayInputBlocker.Block(blocker); yield return Frames(2);
            quiet = emitter.StepEmissionCount; yield return Wait(.7f); Check(emitter.StepEmissionCount == quiet, "UI 입력 차단 중 이동 발소리 없음");
            GameplayInputBlocker.Unblock(blocker); Send();
            phase = "Attack"; yield return Reset(); Send(true, false, true); yield return Wait(.2f);
            Check(melee.IsAttackInProgress, "공격 차단 시험 실제 공격 진입"); quiet = emitter.StepEmissionCount;
            yield return Wait(.25f); Check(emitter.StepEmissionCount == quiet, "새 공격 중 일반 풋스텝 차단"); Send(); melee.CancelCurrentAttackState();
            phase = "Evade"; yield return Reset(); yield return StartDodge(true); quiet = emitter.StepEmissionCount;
            while (evade.IsEvading) yield return null; Send(); Check(emitter.StepEmissionCount == quiet, "회피 중 일반 풋스텝 차단");
            phase = "ConditionBlock"; yield return Reset(); actor.GetComponent<PlayerStateCoordinator>().RequestStun(blocker); Send(true); quiet = emitter.StepEmissionCount;
            yield return Wait(.7f); Check(emitter.StepEmissionCount == quiet, "스턴 상태 접지 차단"); actor.GetComponent<PlayerStateCoordinator>().ReleaseStun(blocker); Send();
            phase = "Pause"; yield return Reset(); Send(true); yield return Wait(.4f); OverburstTimeEffectArbiter.SetPaused(true); quiet = emitter.StepEmissionCount;
            yield return Wait(.5f); Check(emitter.StepEmissionCount == quiet, "일시정지 중 접지 차단"); OverburstTimeEffectArbiter.SetPaused(false); Send();
            phase = "DisableRebind"; yield return Reset(); emitter.enabled = false; quiet = emitter.StepEmissionCount; Send(true); yield return Wait(.8f);
            Check(emitter.StepEmissionCount == quiet, "Emitter 비활성화 접지 차단"); emitter.enabled = true; emitter.BindAnimation(animator, rig);
            yield return Wait(1); Check(emitter.StepEmissionCount > quiet, "재활성화와 같은 Animator 재연결 복귀"); Send();
            phase = "AirLanding"; yield return Reset(false); quiet = emitter.LandingEmissionCount;
            ActorTeleportUtility.TeleportSafely(actor.transform, origin + Vector3.up * 3, actor.transform.rotation); Send(true); yield return Wait(1.5f); Send();
            Check(emitter.LandingEmissionCount == quiet + 1, "공중 이동 후 착지음 한 번");
            foreach (var profile in AssetDatabase.FindAssets("t:SurfaceProfile", new[] { "Assets/ProjectOverburst/06_Audio" }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<SurfaceProfile>))
            {
                phase = "Surface" + profile.name; yield return Reset(true); surface.Configure(profile); Send(true); yield return Wait(.9f); Send();
                Check(emitter.LastProfile == profile, phase + " 실제 발 샘플의 SurfaceOverride 선택");
            }
            Check(duplicateFrames == 0 && unsafeContacts == 0, "같은 프레임 중복·허용되지 않은 이동 접지 없음");
            Check(emitter.LegacyStepCount == 0, "지원 이동 클립의 거리 폴백 없음");
            Check(emitter.QueueOverflowCount == 0, "32개 이벤트 큐 용량 초과 없음");
            Check(resolver.SurfaceProbeCount - initialProbes == emitter.StepEmissionCount - initialSteps + emitter.LandingEmissionCount - initialLandings, "실제 발소리 한 번당 표면 Raycast 한 번");
            samples.Add(new { phases, accepted = accepted.Count, duplicateFrames, unsafeContacts, emitter.MaximumFrameCandidates,
                emitter.AnimationEventCount, emitter.RejectedEventCount, emitter.CatchUpDiscardCount, emitter.ElevatedContactDiscardCount, emitter.LegacyStepCount });
        }
        finally
        {
            if (emitter != null) { emitter.ContactAccepted -= onContact; emitter.enabled = true; }
            animator.speed = oldSpeed; Application.targetFrameRate = oldRate; QualitySettings.vSyncCount = oldVsync;
            movement.GetType().GetField("isWalkMode", Private).SetValue(movement, oldWalk);
            actor.GetComponent<PlayerStateCoordinator>().ReleaseStun(blocker); GameplayInputBlocker.Unblock(blocker); OverburstTimeEffectArbiter.SetPaused(false); Send();
            origin = oldOrigin;
            File.WriteAllText(Path.Combine(output, "FootstepContacts.json"), JsonConvert.SerializeObject(new { contacts, phases, duplicateFrames, unsafeContacts }, Formatting.Indented));
        }
    }
}
