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

/// <summary>제품 입력 액션과 가상 키보드/마우스로 회피와 닷지 공격의 수명주기를 검증한다.</summary>
[InitializeOnLoad]
public static partial class PlayerEvadeVerifier
{
    const string PendingKey = "Overburst.PlayerEvadeVerifier.Pending";
    const string DeadlineKey = "Overburst.PlayerEvadeVerifier.Deadline";
    const string ReturnKey = "Overburst.PlayerEvadeVerifier.ReturnToRealAccount";
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
    static EnemySpawnService spawn;
    static readonly List<EnemyActor> leased = new List<EnemyActor>();
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

    static PlayerEvadeVerifier()
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
        string target = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(target);
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
        SessionState.SetString(ReturnKey + ".Deadline", (EditorApplication.timeSinceStartup + 480).ToString(System.Globalization.CultureInfo.InvariantCulture));
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
        catch (Exception e) { File.WriteAllText(Path.Combine(target, "BootResult.json"), JsonConvert.SerializeObject(new { status = "FAIL", error = e.ToString() })); }
    }

    public static void VerifyAssets(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("자산 검사에는 유휴 Editor가 필요합니다.");
        Directory.CreateDirectory(directory);
        var profile = AssetDatabase.LoadAssetAtPath<PlayerEvadeProfile>(PlayerEvadeBuilder.ProfilePath);
        var definition = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(PlayerEvadeBuilder.DefinitionPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerEvadeBuilder.ControllerPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerEvadeBuilder.PlayerPath);
        if (profile == null || definition == null || controller == null || prefab.GetComponent<PlayerEvadeController>().Profile != profile)
            throw new InvalidOperationException("회피 프로필·플레이어 연결 오류");
        int evadeIndex = Array.FindIndex(controller.layers, l => l.name == PlayerEvadeProfile.ExplorationLayer);
        int knockdownIndex = Array.FindIndex(controller.layers, l => l.name == "Player_Knockdown");
        if (evadeIndex < 0 || (knockdownIndex >= 0 && knockdownIndex <= evadeIndex)) throw new InvalidOperationException("넉다운 우선순위 오류");
        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) != 0) throw new InvalidOperationException("Missing Script");
        var sourcePaths = new[] { "06_Dodge/01_Dodge/Dodge_F.anim", "06_Dodge/05_Dodge_to_Run/Dodge_to_Run_F.anim",
            "06_Dodge/02_Dodge_Combat/Dodge_Combat_F.anim", "06_Dodge/02_Dodge_Combat/Dodge_Combat_F_L_45.anim",
            "06_Dodge/02_Dodge_Combat/Dodge_Combat_R_L_45.anim", "06_Dodge/02_Dodge_Combat/Dodge_Combat_L.anim",
            "06_Dodge/02_Dodge_Combat/Dodge_Combat_R.anim", "02_Attack/12_Run_Attack/Run_Attack_01.anim", "02_Attack/12_Run_Attack/Run_Attack_02.anim" };
        var clips = new[] { profile.explorationDodge, profile.explorationDodgeToRun, profile.combatClips.forward,
            profile.combatClips.forwardLeft, profile.combatClips.forwardRight, profile.combatClips.left, profile.combatClips.right,
            definition.dodgeAttackDefinition.GetStep(0).animationClip, definition.dodgeHeavyAnimationClip };
        var results = new List<object>();
        var scene = EditorSceneManager.NewPreviewScene(); var graph = default(PlayableGraph);
        try
        {
            var model = UnityEngine.Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(model, scene);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var rig = model.GetComponentInChildren<Animator>(true); rig.applyRootMotion = false;
            var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => rig.GetBoneTransform((HumanBodyBones)i)).Where(t => t != null).ToArray();
            for (int c = 0; c < clips.Length; c++)
            {
                var clip = clips[c]; var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerEvadeBuilder.SourceRoot + sourcePaths[c]);
                if (clip == null || !clip.isHumanMotion || AnimationUtility.GetAnimationClipSettings(clip).loopTime) throw new InvalidOperationException("클립 오류 " + c);
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName == "RootT.x" || b.propertyName == "RootT.z"))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve.keys.Any(k => Mathf.Abs(k.value - curve.keys[0].value) > .00001f)) throw new InvalidOperationException("이중 루트 이동 " + clip.name);
                }
                graph = PlayableGraph.Create("Owned evade pose comparison"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var a = AnimationClipPlayable.Create(graph, source); var b = AnimationClipPlayable.Create(graph, clip);
                a.SetApplyFootIK(false); b.SetApplyFootIK(false);
                var mixer = AnimationMixerPlayable.Create(graph, 2); graph.Connect(a, 0, mixer, 0); graph.Connect(b, 0, mixer, 1);
                AnimationPlayableOutput.Create(graph, "Compare", rig).SetSourcePlayable(mixer); graph.Play();
                float first = (c == 7 ? PlayerEvadeBuilder.LightFirstFrame : c == 8 ? PlayerEvadeBuilder.HeavyFirstFrame : 0) / source.frameRate;
                float last = (c == 1 ? 40 : c == 7 ? PlayerEvadeBuilder.LightLastFrame : c == 8 ? PlayerEvadeBuilder.HeavyLastFrame : 48) / source.frameRate;
                float maximumAngle = 0;
                foreach (float n in new[] { 0f, .137f, .36f, .5f, .713f, .999f })
                {
                    float strike = PlayerEvadeBuilder.HeavyImpactFrame / source.frameRate;
                    float impactN = definition.heavyAttackDefinition.attack.attackPhases[0].SafeStart;
                    float sourceTime = c == 8 ? (n <= impactN ? Mathf.Lerp(first, strike, n / impactN)
                        : Mathf.Lerp(strike, last, (n - impactN) / (1f - impactN))) : Mathf.Lerp(first, last, n);
                    a.SetTime(sourceTime); mixer.SetInputWeight(0, 1); mixer.SetInputWeight(1, 0); graph.Evaluate(0);
                    var rotations = bones.Select(t => t.localRotation).ToArray();
                    b.SetTime(clip.length * n); mixer.SetInputWeight(0, 0); mixer.SetInputWeight(1, 1); graph.Evaluate(0);
                    for (int i = 0; i < bones.Length; i++) maximumAngle = Mathf.Max(maximumAngle, Quaternion.Angle(rotations[i], bones[i].localRotation));
                }
                graph.Destroy(); graph = default;
                if (maximumAngle > .3f) throw new InvalidOperationException("원본 파생 포즈 불일치 " + clip.name + " " + maximumAngle);
                results.Add(new { clip.name, clip.length, clip.isHumanMotion, maximumAngle, guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)) });
            }
            if (Mathf.Abs(definition.dodgeHeavyAnimationClip.length - definition.heavyAttackDefinition.attack.animationClip.length) > .0001f)
                throw new InvalidOperationException("일반 강공과 닷지 강공 시간 불일치");
            if (!MeleeAttackVfxSlopeBakeUtility.ValidateCombo(definition.dodgeAttackDefinition, out string error)) throw new InvalidOperationException(error);
            File.WriteAllText(Path.Combine(directory, "AssetValidation.json"), JsonConvert.SerializeObject(new { status = "PASS", evadeIndex, knockdownIndex, results }, Formatting.Indented));
        }
        finally { if (graph.IsValid()) graph.Destroy(); EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static void Begin(string directory)
    {
        if (work != null || stack.Count > 0) throw new InvalidOperationException("검증 진행 중");
        output = IsolatedSavePlayGuard.ValidateDirectory(directory);
        if (!EditorApplication.isPlaying || !Overburst.Persistence.AccountBootstrap.Ready || string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("부팅이 끝난 격리 Play가 필요합니다.");
        Directory.CreateDirectory(output); checks.Clear(); samples.Clear(); errors.Clear(); failures = 0; frame = -1;
        lightOnly = SessionState.GetBool(PendingKey + ".LightOnly", false); SessionState.EraseBool(PendingKey + ".LightOnly");
        dashVisualOnly = SessionState.GetBool(PendingKey + ".DashVisualOnly", false); SessionState.EraseBool(PendingKey + ".DashVisualOnly");
        paletteOnly = SessionState.GetBool(PendingKey + ".PaletteOnly", false); SessionState.EraseBool(PendingKey + ".PaletteOnly");
        dashHeavyOnly = SessionState.GetBool(PendingKey + ".DashHeavyOnly", false); SessionState.EraseBool(PendingKey + ".DashHeavyOnly");
        deadline = EditorApplication.timeSinceStartup + 240;
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
    static IEnumerator VerifyDashVisualComparison()
    {
        var effect=actor.GetComponent<PlayerDashVfx>();
        Check(effect!=null,"정식 플레이어 대시 VFX 연결");
        int before=effect.CapturedAfterimageCount;
        yield return Reset(false);yield return StartDodge(false);Send();
        yield return CompleteEvade("탐험 대시",5f,.30f);
        Check(effect.CapturedAfterimageCount>before,"탐험 대시 실제 자세 잔상");
        yield return Wait(.6f);
        yield return Reset(true,180f);yield return StartDodge(true);Send();
        before=effect.CapturedAfterimageCount;
        yield return CompleteEvade("전투 구르기",4f,.45f);
        Check(effect.CapturedAfterimageCount==before,"구르기 대시 잔상 제외");
        var energy=actor.GetComponent<OverburstElementEnergy>();
        if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
        var cameraObject=new GameObject("Owned Dash Comparison Camera");fixtures.Add(cameraObject);
        var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        camera.CopyFrom(Camera.main);camera.enabled=false;camera.orthographic=true;camera.orthographicSize=3.1f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.18f);
        int uiLayer=LayerMask.NameToLayer("UI");if(uiLayer>=0)camera.cullingMask&=~(1<<uiLayer);
        var target=new RenderTexture(800,450,24,RenderTextureFormat.ARGB32);target.Create();
        var pixels=new Texture2D(800,450,TextureFormat.RGB24,false);
        var groundRenderers = new HashSet<Renderer>();
        foreach (var hit in Physics.RaycastAll(actor.transform.position + Vector3.up * 3f, Vector3.down, 8f))
            if (!hit.collider.transform.IsChildOf(actor.transform))
                foreach (var renderer in hit.collider.GetComponentsInParent<Renderer>()) groundRenderers.Add(renderer);
        var hiddenRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(renderer => !renderer.transform.IsChildOf(actor.transform)
                && !groundRenderers.Contains(renderer)
                && renderer.GetComponentInParent<OverburstFeelEmitter>() == null
                && !renderer.GetComponentsInParent<Transform>().Any(parent => parent.name == "Player Dash VFX"))
            .Select(renderer => (renderer, renderer.forceRenderingOff)).ToArray();
        var report=new List<object>();
        try
        {
            for(int color=0;color<PlayerDashVfx.ColorNames.Length;color++)foreach(bool heavy in new[]{false,true})
            {
                yield return Reset();effect.SetColorStyle(color);energy.Clear();yield return Frames(2);
                Vector3 center=actor.transform.position+(heavy?Vector3.zero:forward*2.5f)+Vector3.up*.8f;
                Vector3 right=Vector3.Cross(Vector3.up,forward);
                camera.transform.position=center-forward*7f+right*4f+Vector3.up*4.5f;
                camera.transform.LookAt(center);
                int snapshotBefore=effect.CapturedAfterimageCount,dustBefore=effect.EmittedDustCount;
                if(heavy){FillEnergy(energy,10000+color*100);Check(energy.Normalized>.999f,"가득 찬 에너지 강공 비교 준비");
                    Check(melee.TryStartHeavyAttack(forward)==WeaponActionResult.Accepted,"가득 찬 에너지 강공 비교 시작");}
                else{yield return StartDodge(false);Send();}
                string label=(heavy?"Heavy_":"Dash_")+color;
                string folder=Path.Combine(output,"Comparison",label);Directory.CreateDirectory(folder);
                var captureTimes=new List<float>();float start=Time.unscaledTime,nextCapture=start,limit=start+1.7f;
                int peakGhosts=0,frameIndex=0;
                float maximumFrameDuration = 0f;
                while(Time.unscaledTime<limit)
                {
                    peakGhosts=Mathf.Max(peakGhosts,effect.ActiveAfterimageCount);
                    maximumFrameDuration = Mathf.Max(maximumFrameDuration, Time.unscaledDeltaTime);
                    if(Time.unscaledTime>=nextCapture)
                    {
                        var request=new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=target};
                        try
                        {
                            foreach (var item in hiddenRenderers) if (item.renderer != null) item.renderer.forceRenderingOff = true;
                            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
                        }
                        finally
                        {
                            foreach (var item in hiddenRenderers) if (item.renderer != null) item.renderer.forceRenderingOff = item.forceRenderingOff;
                        }
                        var previous=RenderTexture.active;
                        try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,800,450),0,0);pixels.Apply();
                            File.WriteAllBytes(Path.Combine(folder,frameIndex.ToString("D3")+".png"),pixels.EncodeToPNG());}
                        finally{RenderTexture.active=previous;}
                        captureTimes.Add(Time.unscaledTime-start);frameIndex++;nextCapture=Time.unscaledTime+.05f;
                    }
                    yield return null;
                }
                Check(peakGhosts>0,"실제 자세 잔상 렌더 실행 "+label+" ghosts="+peakGhosts);
                if(!heavy){float actual=Vector3.Distance(new Vector3(startPosition.x,0,startPosition.z),new Vector3(endPosition.x,0,endPosition.z));
                    Check(ends==1&&Mathf.Abs(actual-5f)<.12f,"전투 대시5m "+color+" actual="+actual);
                    Check(Mathf.Abs(evade.Profile.combatDodge.duration-.48f)<.0001f
                        && Mathf.Abs(endTime-startTime-.48f)<maximumFrameDuration+.02f,"전투 대시0.48초 설정·캡처 프레임 내 종료 "+color);
                    Check(effect.EmittedDustCount>dustBefore,"접지한 대시 발밑 먼지 "+color);}
                report.Add(new{label,color,heavy,frames=frameIndex,captureTimes,peakGhosts,maximumFrameDuration,
                    snapshots=effect.CapturedAfterimageCount-snapshotBefore,dust=effect.EmittedDustCount-dustBefore,tint=new[]{effect.CurrentTint.r,effect.CurrentTint.g,effect.CurrentTint.b},accent=new[]{effect.CurrentAccent.r,effect.CurrentAccent.g,effect.CurrentAccent.b}});
                Progress("대시 잔상 비교 "+label);
            }
            effect.SetColorStyle(0);yield return Reset();energy.Clear();
            before=effect.CapturedAfterimageCount;
            Check(melee.TryStartHeavyAttack(forward)==WeaponActionResult.Accepted,"빈 에너지 강공 비교");yield return Wait(.3f);
            Check(effect.CapturedAfterimageCount==before,"에너지 가득 찬 강공에만 잔상");
            yield return Reset();yield return StartDodge(false);yield return Frames(10);effect.enabled=false;
            Check(effect.ActiveAfterimageCount==0,"비활성화 잔상 정리");effect.enabled=true;Send();yield return Wait(.7f);
            Check(effect.ActiveAfterimageCount==0,"잔상 종료 뒤 풀 비활성화");
            File.WriteAllText(Path.Combine(output,"Comparison.json"),JsonConvert.SerializeObject(new{status="PASS",report},Formatting.Indented));
            yield return VerifyDashPalette(effect);
        }
        finally{effect.SetColorStyle(0);target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(pixels);UnityEngine.Object.Destroy(cameraObject);}
    }
    static IEnumerator VerifyDashPalette(PlayerDashVfx effect)
    {
        var panel = PlayerDashVfxComparisonPanelBootstrap.Attach(effect);
        fixtures.Add(panel.gameObject);
        effect.SetColorStyle(0);
        Check(panel.SelectionCount == 7, "임시 단일 버튼7색상");
        for (int i = 1; i <= panel.SelectionCount; i++)
        {
            panel.Cycle(1);
            Check(panel.SelectionIndex == i % panel.SelectionCount, "단일 버튼 순환 " + i);
            yield return null;
        }
        panel.Cycle(-1); Check(panel.SelectionIndex == 6, "Shift 이전 색상 순환");
        panel.Cycle(1); Check(panel.SelectionIndex == 0, "기본 색상 복귀");
        var originalWeapon = weaponItem;
        var colors = new List<object>();
        var elementColors = new List<object>();
        try
        {
            for (int color = 0; color < PlayerDashVfx.ColorNames.Length; color++)
            {
                effect.SetColorStyle(color); yield return Reset(); yield return StartDodge(false); Send();
                Color tint = effect.CurrentTint;
                Color accent = effect.CurrentAccent;
                Check(effect.GroundDustTint == new Color(.53f,.48f,.39f,.18f), "모든 잔상 색상에서 갈색 먼지 " + color);
                if(color==1)Check(tint.r>.99f && tint.b>.9f && accent.r>.99f && accent.g>.9f && accent.b<.4f, "흰색 몸체와 밝은 노랑 가장자리");
                Check(tint.a > 0f && tint.a <= .23f, "색상 변경 밝기 제한 " + color);
                colors.Add(new { color, name = PlayerDashVfx.ColorNames[color], tint = new[] { tint.r, tint.g, tint.b, tint.a }, accent = new[]{accent.r,accent.g,accent.b,accent.a} });
                yield return CompleteEvade("색상 " + color, 5f, .48f, false);
            }
            Color[] expected = {new Color(1f,.3f,.1f),new Color(.42f,.87f,1f),new Color(.59f,.53f,1f),new Color(.42f,.13f,.25f),new Color(1f,.91f,.56f)}; int index = 0;
            foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
            {
                weaponItem = new ItemData(originalWeapon.baseData, 1, ItemGrade.Common, element: element);
                effect.SetColorStyle(6); yield return Reset(); yield return StartDodge(false); Send();
                var actual = effect.CurrentTint;
                var fixedColor = expected[index++];
                Check(Mathf.Abs(actual.r-fixedColor.r)<.001f && Mathf.Abs(actual.g-fixedColor.g)<.001f && Mathf.Abs(actual.b-fixedColor.b)<.001f,
                    "실제 장착 원소 자동색 " + element);
                elementColors.Add(new{element=element.ToString(),tint=new[]{actual.r,actual.g,actual.b,actual.a}});
                yield return CompleteEvade("원소 자동색 " + element, 5f, .48f, false);
            }
            weaponItem = originalWeapon; effect.SetColorStyle(0); yield return Reset();
            pointerAtComparison = true; Send(false, false, true); yield return Frames(3);
            Check(GameplayInputBlocker.IsGameplayInputBlocked && !melee.IsAttackInProgress, "임시 버튼 위 클릭 공격 차단");
            Send(); pointerAtComparison = false; yield return Frames(3);
            Check(!GameplayInputBlocker.IsGameplayInputBlocked, "버튼 밖 게임 입력 반환");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"ComparisonButton.png")); yield return Wait(.2f);
            File.WriteAllText(Path.Combine(output,"Palette.json"),JsonConvert.SerializeObject(new{status="PASS",colorsCount=panel.SelectionCount,colors,elementColors},Formatting.Indented));
        }
        finally
        {
            pointerAtComparison = false; weaponItem = originalWeapon; effect.SetColorStyle(0);
            fixtures.Remove(panel.gameObject); UnityEngine.Object.DestroyImmediate(panel.gameObject);
        }
    }
    static IEnumerator CompleteEvade(string label, float distance, float duration, bool checkPose = true)
    {
        float limit = Time.unscaledTime + 3, maxPoseError = 0;
        while (evade.IsEvading)
        {
            Check(Time.unscaledTime < limit, label + " 종료 제한");
            int layer = animator.GetLayerIndex(evade.ActiveType == PlayerEvadeType.ExplorationDodge ? PlayerEvadeProfile.ExplorationLayer : "Combat_MeleeWeapon");
            var state = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer) : animator.GetCurrentAnimatorStateInfo(layer);
            if (label == "hitstop clock" && evade.ActiveNormalizedTime > .6f)
                Check(!animator.IsInTransition(layer), "히트스톱에서도 닷지 진입 보간 완료");
            if (checkPose && evade.ActiveNormalizedTime > .20f && evade.ActiveNormalizedTime < .90f)
            {
                float error = Mathf.Abs(state.normalizedTime - evade.ActiveNormalizedTime); maxPoseError = Mathf.Max(maxPoseError, error);
                Check(error < .16f + Time.unscaledDeltaTime / duration, label + " Animator·이동 진행 일치 " + error
                    + " (native=" + state.normalizedTime + ",runtime=" + evade.ActiveNormalizedTime + ",hash=" + state.shortNameHash
                    + ",speed=" + state.speed + ",multiplier=" + state.speedMultiplier + ",animator=" + animator.speed
                    + ",clips=" + string.Join(",", animator.GetCurrentAnimatorClipInfo(layer).Select(c => c.clip.name)) + ")");
            }
            yield return null;
        }
        float actual = Vector3.Distance(new Vector3(startPosition.x, 0, startPosition.z), new Vector3(endPosition.x, 0, endPosition.z));
        Check(starts == 1 && ends == 1 && evade.LastEndWasCompleted, label + " 정상 종료 한 번");
        Check(Mathf.Abs(actual - distance) < .12f, label + " 이동 거리 " + actual);
        Check(Mathf.Abs(endTime - startTime - duration) < .09f, label + " 이동 시간 " + (endTime - startTime));
        samples.Add(new { label, actual, seconds = endTime - startTime, maxPoseError }); Progress(label);
    }

    static IEnumerator Run()
    {
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
            if (dashVisualOnly)
            {
                if (paletteOnly) yield return VerifyDashPalette(actor.GetComponent<PlayerDashVfx>());
                else yield return VerifyDashVisualComparison();
                yield break;
            }
            if (dashHeavyOnly) { yield return VerifyDashHeavy(); yield break; }
            if (lightOnly) { yield return VerifyDodgeLightOverlap(); yield return VerifyDodgeComboResume(); yield return VerifyDodgeLightRecovery(); yield break; }
            yield return Reset(false); yield return StartDodge(false);
            Check(evade.ActiveType == PlayerEvadeType.ExplorationDodge && !evade.IsInvincible && !evade.IsPerfectEvadeWindowActive, "탐험 닷지와 무적 없음");
            yield return CompleteEvade("exploration idle", 5, .30f); yield return Wait(.15f);
            Check(animator.GetLayerWeight(animator.GetLayerIndex(PlayerEvadeProfile.ExplorationLayer)) < .001f, "탐험 레이어 복귀");
            yield return Reset(false); Send(true); yield return Frames(2); yield return StartDodge(true);
            yield return CompleteEvade("exploration run", 5, .30f); yield return Wait(.10f);
            Check((movement.IsWalkMode ? !movement.IsRunning : movement.IsRunning) && movement.MoveInput.y > .9f,
                "닷지 후 현재 보행 연결 (running=" + movement.IsRunning + ",walk=" + movement.IsWalkMode + ",combat=" + movement.IsCombatMoveMode
                + ",mode=" + PlayerCombatModeController.IsSharedCombatModeActive() + ",runtimeMove=" + movement.MoveInput + ",authority=" + movement.ControlAuthority + ")"); Send();
            foreach (float angle in new[] { 0f, 45f, -45f, 90f, -90f, 91f, -91f, 180f })
            {
                yield return Reset(true, angle); Send(true); yield return Frames(1); yield return StartDodge(true);
                bool front = Mathf.Abs(angle) <= 90;
                Check(evade.ActiveType == (front ? PlayerEvadeType.CombatDodge : PlayerEvadeType.Roll), "시작 정면 180도 분기 " + angle + " (actual=" + evade.ActiveType + ")");
                Check(front ? !evade.IsPerfectEvadeWindowActive : evade.IsPerfectEvadeWindowActive, "퍼펙트 구르기 전용 " + angle);
                Send(); yield return CompleteEvade("combat angle " + angle, front ? evade.Profile.combatDodge.distance : 4f, front ? .48f : .45f);
            }
            yield return Reset(); yield return StartDodge(false); Send(false, false, true); yield return Frames(2); Send();
            Check(input.CombatInputs.PendingDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "닷지 중 짧은 좌클릭 예약");
            yield return CompleteEvade("light queued", evade.Profile.combatDodge.distance, .48f);
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light && Field<int>(melee, "comboStepIndex") == 0, "예약 약공 진입 1타");
            int action = Field<int>(melee, "activeActionId"); bool continued = false; float tapEnd = Time.unscaledTime + 2f;
            while (Time.unscaledTime < tapEnd) { continued |= melee.IsAttackInProgress && Field<int>(melee, "comboStepIndex") > 0; yield return null; }
            Check(!melee.IsAttackInProgress && !continued && Field<int>(melee, "nextActionId") == action + 1, "짧은 클릭 한 타만 실행");
            yield return Reset(); yield return StartDodge(true, true); yield return CompleteEvade("same frame held light", evade.Profile.combatDodge.distance, .48f);
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "Shift·클릭 같은 프레임 수락");
            float continuationLimit = Time.unscaledTime + 3;
            while (melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light) { Check(Time.unscaledTime < continuationLimit, "일반 1타 연결 제한"); yield return null; }
            Check(melee.IsAttackInProgress && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None && Field<int>(melee, "comboStepIndex") == 0, "좌클릭 홀드 일반 1타 연결"); Send();
            yield return Reset(); melee.SetManualInputEnabled(false); Send(); yield return Frames(2); Send(false, false, true); yield return Frames(2);
            yield return StartDodge(false, true); melee.SetManualInputEnabled(true); yield return CompleteEvade("held before", evade.Profile.combatDodge.distance, .48f);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "회피 전 유지한 좌클릭 전용 공격 수락"); Send();
            yield return Reset(); yield return StartDodge(false); yield return CompleteEvade("no click", evade.Profile.combatDodge.distance, .48f);
            Send(false, false, true); yield return Frames(2);
            Check(melee.IsAttackInProgress && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None, "닷지 종료 후 클릭 일반 공격"); Send();

            yield return VerifyDashHeavy();
            yield return VerifyDodgeAttackCorrection();
            yield return ParryFollowUp();

            foreach (string cancel in new[] { "knockdown", "ui", "weapon", "mode", "map", "disable", "focus" })
            {
                yield return Reset(); yield return StartDodge(false); Send(false, false, true); yield return Frames(2); Send();
                Check(input.CombatInputs.PendingDodgeFollowUp == PlayerDodgeFollowUpKind.Light, cancel + " 취소 전 예약");
                if (cancel == "knockdown") evade.CancelForKnockdown();
                else if (cancel == "ui") GameplayInputBlocker.Block(blocker);
                else if (cancel == "weapon") actor.Equipment.EquipWeaponItem(new ItemData(weaponItem.baseData, 1, ItemGrade.Common));
                else if (cancel == "mode") PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System);
                else if (cancel == "map") input.DisableGameplay();
                else if (cancel == "focus") typeof(PlayerInputFacade).GetMethod("OnApplicationFocus", Private).Invoke(input, new object[] { false });
                else evade.enabled = false;
                yield return Wait(.5f);
                Check(!melee.IsAttackInProgress && input.CombatInputs.PendingDodgeFollowUp == PlayerDodgeFollowUpKind.None, cancel + " 예약 폐기·공격 누출 없음");
                GameplayInputBlocker.Unblock(blocker); input.EnableGameplay(); evade.enabled = true;
            }
            yield return Reset(); invalidateOnCompletion = true;
            yield return StartDodge(false, true); Send(); while (evade.IsEvading) yield return null; yield return Frames(2);
            Check(!melee.IsAttackInProgress && input.CombatInputs.PendingDodgeFollowUp == PlayerDodgeFollowUpKind.None,
                "종료 이벤트 입력 무효화가 LateUpdate 전달도 취소"); invalidateOnCompletion = false;
            yield return Reset(); OverburstTimeEffectArbiter.Request(blocker, OverburstTimeEffectKind.PerfectEvade, .15f, .8f);
            yield return Frames(2); yield return StartDodge(false); yield return CompleteEvade("slow clock", evade.Profile.combatDodge.distance, .48f);
            OverburstTimeEffectArbiter.ClearOwner(blocker); yield return Wait(.2f);
            yield return Reset(); OverburstTimeEffectArbiter.Request(blocker, OverburstTimeEffectKind.HitStop, .01f, .5f);
            yield return Frames(2); yield return StartDodge(false); yield return CompleteEvade("hitstop clock", evade.Profile.combatDodge.distance, .48f);
            OverburstTimeEffectArbiter.ClearOwner(blocker); yield return Wait(.2f);
            yield return Reset(); yield return StartDodge(false); pauseRecorded = false; requestPause = true;
            while (!pauseRecorded) yield return null;
            yield return Wait(.2f);
            Vector3 drift = actor.transform.position - frozenPosition; drift.y = 0;
            Check(OverburstGameMenu.IsOpen && OverburstTimeEffectArbiter.IsPaused && evade.IsEvading
                && Mathf.Abs(evade.ActiveNormalizedTime - frozenProgress) < .001f && drift.magnitude < .001f,
                "실제 메뉴 멈춤 동안 회피 시간·수평 이동 정지 (open=" + OverburstGameMenu.IsOpen + ",paused=" + OverburstTimeEffectArbiter.IsPaused
                + ",evading=" + evade.IsEvading + ",progress=" + (evade.ActiveNormalizedTime - frozenProgress) + ",drift=" + drift.magnitude + ")");
            requestResume = true; while (requestResume) yield return null; yield return Wait(.5f);
            Vector3 resumedTravel = endPosition - startPosition; resumedTravel.y = 0;
            Check(evade.LastEndWasCompleted && Mathf.Abs(resumedTravel.magnitude - evade.Profile.combatDodge.distance) < .12f && !melee.IsAttackInProgress,
                "메뉴 닫기 후 남은 회피만 완료·공격 예약 없음");
            for (int i = 0; i < 8; i++)
            {
                yield return Reset(); yield return StartDodge(false, true); evade.CancelForKnockdown(); Send(); yield return Frames(2);
                Check(!evade.IsEvading && !melee.IsAttackInProgress && !evade.IsInvincible && input.CombatInputs.PendingDodgeFollowUp == PlayerDodgeFollowUpKind.None,
                    "반복 취소 복귀 " + i);
            }
            Check(animator.updateMode == originalMode && Mathf.Approximately(animator.speed, originalSpeed) && Mathf.Approximately(Time.timeScale, 1), "Animator·전역 시간 복원");
            Progress("Completed");
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
            foreach (var enemy in leased) if (enemy != null && enemy.IsLeased) spawn?.Release(enemy); leased.Clear();
            foreach (var fixture in fixtures) if (fixture != null) UnityEngine.Object.Destroy(fixture); fixtures.Clear();
            melee?.CancelCurrentAttackState(); actor?.GetComponent<PlayerKnockdownController>()?.ResetReaction();
        }
    }

    static IEnumerator VerifyDodgeLightOverlap()
    {
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var camera = QuarterViewCamera.ActiveInstance;
        Check(camera != null && camera.CurrentTarget == actor.transform, "실제 카메라 추적 대상");
        float lead = melee.DodgeLightWindupLead;
        Check(Mathf.Abs(evade.Profile.combatDodge.duration - .48f) < .0001f, "전투 닷지0.48초");
        foreach (float clickAt in new[] { 0f, .65f, .75f, .90f, -1f, -2f, -3f, -4f, -5f, -6f })
        {
            yield return Reset(true, clickAt == -3f ? 90 : clickAt == -4f ? -45 : 0);
            if(clickAt==-5f || clickAt==-6f)
            {
                OverburstTimeEffectArbiter.Request(blocker, clickAt==-5f ? OverburstTimeEffectKind.PerfectEvade : OverburstTimeEffectKind.HitStop,
                    clickAt==-5f ? .15f : .01f, .8f); yield return Frames(2);
            }
            bool heldBefore = clickAt == -1f, continueCombo = clickAt == -2f;
            if (heldBefore) { melee.SetManualInputEnabled(false); Send(); yield return Frames(2); Send(false, false, true); yield return Frames(2); }
            yield return StartDodge(clickAt == -3f || clickAt == -4f, clickAt <= 0f);
            if (heldBefore) melee.SetManualInputEnabled(true);
            var targets = new List<CombatHealth>(); var callbacks = new List<Action<CombatHealth, DamageInfo>>();
            var order = new List<string>(); var damageTimes = new List<float>(); var poseTimes = new List<float>();
            var damageFrames = new List<int>(); poseProbe.frames.Clear();
            poseProbe.actorRoot=actor.transform;poseProbe.bones=new[]{HumanBodyBones.Hips,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.Chest}.Select(b=>animator.GetBoneTransform(b)).ToArray();
            poseProbe.poseSamples.Clear();poseProbe.recordPoses=continueCombo;
            float started = Field<float>(evade, "evadeStartTime"), duration = evade.ActiveDuration;
            var trace = new List<object>();
            float previewAt = -1f, windowAt = -1f, maxBodyOffset = 0f, maxBodyStep = 0f, maxAttackDrift = 0f;
            float peakCameraLag=0f, arrivalCameraLag=0f;
            Vector3 previousHip = actor.transform.InverseTransformPoint(hips.position), attackOrigin = Vector3.zero;
            bool originCaptured = false, clicked = clickAt <= 0f, actualCombo1 = false;
            float requestedAt = Field<float>(input.CombatInputs, "dodgeLightRequestedAt");
            int swingBefore = Field<int>(melee, "dodgeLightSwingSequence");
            bool audibleVoiceInWindup = false;
            try
            {
                Vector3 endpoint = startPosition + evade.ActiveDirection * evade.Profile.combatDodge.distance;
                string[] names = { "left", "center", "right" }; float[] angles = { -60, 0, 60 };
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];var target = new GameObject("Owned light blend " + name); fixtures.Add(target);
                    target.transform.position = endpoint + Quaternion.AngleAxis(angles[i], Vector3.up) * evade.ActiveDirection * 2f;
                    var combat = target.AddComponent<CombatTarget>();target.GetComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
                    var health = target.GetComponent<CombatHealth>();health.SetMaxHp(1000000,true);
                    var data = new SerializedObject(combat);data.FindProperty("radius").floatValue = .05f;data.ApplyModifiedPropertiesWithoutUndo();
                    data = new SerializedObject(health);data.FindProperty("showDamageNumbers").boolValue=false;data.ApplyModifiedPropertiesWithoutUndo();
                    Action<CombatHealth,DamageInfo> callback=(h,d)=>{
                        if(melee.ActiveDodgeFollowUp!=PlayerDodgeFollowUpKind.Light)return;
                        order.Add(name);damageTimes.Add((Time.time-Field<float>(melee,"attackStartTime"))/Field<float>(melee,"attackDuration"));
                        damageFrames.Add(Time.frameCount);
                    };
                    health.OnDamaged+=callback;targets.Add(health);callbacks.Add(callback);
                }
                if (clicked && !continueCombo && !heldBefore) Send();
                float limit = Time.unscaledTime + 6f;
                while (Time.unscaledTime < limit)
                {
                    if (!clicked && evade.IsEvading && evade.ActiveNormalizedTime >= clickAt)
                    { Send(false,false,true); clicked=true; }
                    if (clicked && input.CombatInputs.PendingDodgeFollowUp==PlayerDodgeFollowUpKind.Light)
                    {
                        requestedAt=Field<float>(input.CombatInputs,"dodgeLightRequestedAt");
                        if(!continueCombo && !heldBefore) Send();
                    }
                    if (melee.IsDodgeLightWindupActive && previewAt < 0f)
                    { previewAt=Field<float>(melee,"dodgeLightWindupStart");if(heldBefore)Send();Check(evade.IsEvading && !melee.IsAttackInProgress,"준비 모션 중 닷지 이동 소유·피해 없음"); }
                    if (melee.IsDodgeLightWindupActive)
                        audibleVoiceInWindup |= UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                            .Any(v=>v.isPlaying && v.clip!=null && v.clip.name=="GreatswordLight01");
                    if (melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.Light)
                    {
                        if(!originCaptured){attackOrigin=actor.transform.position;originCaptured=true;}
                        Vector3 drift=actor.transform.position-attackOrigin;drift.y=0;maxAttackDrift=Mathf.Max(maxAttackDrift,drift.magnitude);
                        var executor=Field<AttackPhaseExecutor>(melee,"attackPhaseExecutor");
                        var phases=(IList)executor.GetType().GetField("phases",Private).GetValue(executor);
                        if(phases.Count>0 && (bool)phases[0].GetType().GetField("Started",BindingFlags.Public|BindingFlags.Instance).GetValue(phases[0]) && windowAt<0f)
                        {windowAt=OverburstGameClock.UnscaledTime;Check(!evade.IsEvading,"닷지 이동 중 피해 창 없음");}
                    }
                    Vector3 cameraLag = actor.transform.position + Field<Vector3>(camera,"targetOffset") - Field<Vector3>(camera,"focusPosition");cameraLag.y=0;
                    if(evade.IsEvading)peakCameraLag=Mathf.Max(peakCameraLag,cameraLag.magnitude);
                    else if(originCaptured)arrivalCameraLag=cameraLag.magnitude;
                    Vector3 hip=actor.transform.InverseTransformPoint(hips.position);
                    maxBodyOffset=Mathf.Max(maxBodyOffset,new Vector2(hip.x,hip.z).magnitude);
                    maxBodyStep=Mathf.Max(maxBodyStep,Vector3.Distance(hip,previousHip));previousHip=hip;
                    int layer=animator.GetLayerIndex("Combat_MeleeWeapon");var pose=animator.IsInTransition(layer)?animator.GetNextAnimatorStateInfo(layer):animator.GetCurrentAnimatorStateInfo(layer);
                    trace.Add(new{time=OverburstGameClock.UnscaledTime-started,evading=evade.IsEvading,evadeProgress=evade.ActiveNormalizedTime,
                        preview=melee.IsDodgeLightWindupActive,attacking=melee.IsAttackInProgress,kind=melee.ActiveDodgeFollowUp.ToString(),native=pose.normalizedTime,
                        cameraLag=cameraLag.magnitude,cameraFocus=new[]{Field<Vector3>(camera,"focusPosition").x,Field<Vector3>(camera,"focusPosition").y,Field<Vector3>(camera,"focusPosition").z},hips=new[]{hip.x,hip.y,hip.z},owner=new[]{actor.transform.position.x,actor.transform.position.y,actor.transform.position.z}});
                    if(continueCombo && originCaptured && melee.IsAttackInProgress && melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.None)
                    {
                        Check(Field<int>(melee,"comboStepIndex")==0 && Field<AnimationClip>(melee,"activeAttackAnimationClip")==actor.Equipment.CurrentWeaponData.GetMeleeDefinition().comboDefinition.GetStep(0).animationClip,"닷지 어택 뒤 실제 일반1타");
                        Check(Mathf.Abs(Field<float>(melee,"activeAttackTransitionDuration")-MeleeRuntime.DodgeLightComboBlendDuration)<.0001f,"일반1타 연결 전용 보간");
                        actualCombo1=true; Send();
                        bool distinctBlend=false;float blendBodyStep=0f, blendLimit=Time.time+MeleeRuntime.DodgeLightComboBlendDuration+.04f;
                        Vector3 lastHip=actor.transform.InverseTransformPoint(hips.position);
                        while(Time.time<blendLimit)
                        {
                            yield return null;
                            int blendLayer=animator.GetLayerIndex("Combat_MeleeWeapon");
                            if(animator.IsInTransition(blendLayer))
                                distinctBlend |= animator.GetCurrentAnimatorClipInfo(blendLayer).Any(c=>c.clip==actor.Equipment.CurrentWeaponData.GetMeleeDefinition().dodgeAttackDefinition.GetStep(0).animationClip)
                                    && animator.GetNextAnimatorClipInfo(blendLayer).Any(c=>c.clip==actor.Equipment.CurrentWeaponData.GetMeleeDefinition().comboDefinition.GetStep(0).animationClip);
                            Vector3 currentHip=actor.transform.InverseTransformPoint(hips.position);
                            blendBodyStep=Mathf.Max(blendBodyStep,Vector3.Distance(currentHip,lastHip));lastHip=currentHip;
                        }
                        Check(distinctBlend,"닷지 모션을 유지하면서 다른 상태의 일반1타 보간");
                        Check(blendBodyStep<.18f,"일반1타 보간 프레임 몸 위치 연속 "+blendBodyStep);
                        samples.Add(new{label="normal1 blend",distinctBlend,blendBodyStep});
                        poseProbe.recordPoses=false;File.WriteAllText(Path.Combine(output,"Normal1_Transition.json"),JsonConvert.SerializeObject(poseProbe.poseSamples,Formatting.Indented));break;
                    }
                    if(originCaptured && !melee.IsAttackInProgress)break;
                    yield return null;
                }
                foreach (int damageFrame in damageFrames)
                { Check(poseProbe.frames.TryGetValue(damageFrame, out float poseTime), "같은 렌더 프레임 타격 모션 기록"); poseTimes.Add(poseTime); }
                File.WriteAllText(Path.Combine(output,"Case_"+clickAt.ToString(System.Globalization.CultureInfo.InvariantCulture)+".json"),JsonConvert.SerializeObject(new{clickAt,started,duration,lead,requestedAt,previewAt,windowAt,maxAttackDrift,maxBodyOffset,maxBodyStep,peakCameraLag,arrivalCameraLag,order,damageTimes,poseTimes,trace},Formatting.Indented));
                float swingAt = Field<float>(melee,"dodgeLightSwingStartedAt");
                Check(Field<int>(melee,"dodgeLightSwingSequence")==swingBefore+1,"닷지 베기 휘두름 소리 한 번 "+clickAt);
                Check(audibleVoiceInWindup && Mathf.Abs(swingAt-previewAt)<.09f,"실제 오디오 음원 준비 모션 동기화 "+clickAt);
                samples.Add(new{label="dodge swing audio",clickAt,swingAt,previewAt,audibleVoiceInWindup});
                float scheduled=Mathf.Max(started+duration-lead,requestedAt);
                float expectedWindow=Mathf.Max(started+duration,scheduled+lead);
                Check(previewAt>=0f && Mathf.Abs(previewAt-scheduled)<.025f,"이른/늦은/유지 입력 시작 시각 "+clickAt+" actual="+(previewAt-started)+" expected="+(scheduled-started));
                Check(windowAt>=0f && Mathf.Abs(windowAt-expectedWindow)<.09f,"입력만큼 지연된 첫 판정 창 "+clickAt+" actual="+(windowAt-started)+" expected="+(expectedWindow-started));
                Check(order.SequenceEqual(new[]{"left","center","right"}),"좌→중앙→우 한 번씩 "+clickAt+" "+string.Join(",",order));
                Check(damageTimes.All(t=>t>=PlayerEvadeBuilder.LightHitStart-.03f && t<=PlayerEvadeBuilder.LightHitEnd+.15f),"실제 베기 구간 피해");
                Check(damageTimes.Zip(poseTimes,(runtime,pose)=>Mathf.Abs(runtime-pose)).All(d=>d<.03f),"Animator·실제 타격 시점 일치 runtime="+string.Join(",",damageTimes)+" native="+string.Join(",",poseTimes));
                Check(maxAttackDrift<.03f && maxBodyOffset<.30f,"공격 추가 이동·수평 몸 오프셋 제거 drift="+maxAttackDrift+" body="+maxBodyOffset);
                Check(!continueCombo || actualCombo1,"유지 입력 일반1타 연결");
                Check(peakCameraLag>.6f && peakCameraLag<1.85f && arrivalCameraLag<.15f,"닷지 중 카메라 지연·도착 추적 복귀 peak="+peakCameraLag+" remaining="+arrivalCameraLag);
                samples.Add(new{label="dodge light overlap",clickAt,lead,preview=previewAt-started,window=windowAt-started,expectedWindow=expectedWindow-started,
                    maxAttackDrift,maxBodyOffset,maxBodyStep,peakCameraLag,arrivalCameraLag,order,damageTimes,poseTimes,actualCombo1,trace});Progress("dodge light overlap "+clickAt);
            }
            finally
            {
                Send();melee.SetManualInputEnabled(true);OverburstTimeEffectArbiter.ClearOwner(blocker);
                for(int i=0;i<targets.Count;i++)if(targets[i]!=null){targets[i].OnDamaged-=callbacks[i];var go=targets[i].gameObject;fixtures.Remove(go);UnityEngine.Object.Destroy(go);}
            }
        }
        foreach(string cancel in new[]{"knockdown","ui","weapon","mode","disable","map","focus"})
        {
            yield return Reset();yield return StartDodge(false,true);
            while(evade.IsEvading && !melee.IsDodgeLightWindupActive)yield return null;
            Check(melee.IsDodgeLightWindupActive,"취소 전 준비 모션 "+cancel
                +" type="+evade.ActiveType+" pending="+input.CombatInputs.PendingDodgeFollowUp+" held="+input.AttackHeld);Send();
            if(cancel=="knockdown")evade.CancelForKnockdown();
            else if(cancel=="ui")GameplayInputBlocker.Block(blocker);
            else if(cancel=="weapon")actor.Equipment.EquipWeaponItem(new ItemData(weaponItem.baseData,1,ItemGrade.Common));
            else if(cancel=="mode")PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System);
            else if(cancel=="map")input.DisableGameplay();
            else if(cancel=="focus")typeof(PlayerInputFacade).GetMethod("OnApplicationFocus",Private).Invoke(input,new object[]{false});
            else evade.enabled=false;
            yield return Wait(.6f);Check(!melee.IsDodgeLightWindupActive && !melee.IsAttackInProgress,"준비 취소 후 공격 누출 없음 "+cancel);
            GameplayInputBlocker.Unblock(blocker);input.EnableGameplay();evade.enabled=true;
        }
        yield return Reset();yield return StartDodge(false,true);
        while(evade.IsEvading && !melee.IsDodgeLightWindupActive)yield return null;
        Check(melee.IsDodgeLightWindupActive,"실제 메뉴 열기 전 준비 모션");
        pauseRecorded=false;requestPause=true;while(!pauseRecorded)yield return null;
        yield return Wait(.2f);
        Vector3 pausedDrift=actor.transform.position-frozenPosition;pausedDrift.y=0;
        Check(OverburstGameMenu.IsOpen && evade.IsEvading && Mathf.Abs(evade.ActiveNormalizedTime-frozenProgress)<.001f && pausedDrift.magnitude<.001f,
            "공격 준비 중 실제 메뉴 일시정지·수평 이동 정지");
        Check(!melee.IsDodgeLightWindupActive,"메뉴 일시정지 준비 모션·입력 폐기");
        Send();requestResume=true;while(requestResume)yield return null;yield return Wait(.6f);
        Check(evade.LastEndWasCompleted && !melee.IsAttackInProgress && !melee.IsDodgeLightWindupActive,"메뉴 닫은 후 남은 닷지만 완료");
        yield return Reset();invalidateOnCompletion=true;yield return StartDodge(false,true);Send();
        while(evade.IsEvading)yield return null;yield return Frames(2);
        Check(!melee.IsAttackInProgress && !melee.IsDodgeLightWindupActive,"완료 이벤트 입력 무효화가 준비 모션 인계도 폐기");invalidateOnCompletion=false;
    }

    static IEnumerator VerifyDodgeComboResume()
    {
        for(int interrupted=0;interrupted<4;interrupted++)
        {
            yield return Reset();Send(false,false,true);
            float limit=Time.unscaledTime+8f;
            while(!(melee.IsAttackInProgress && melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.None
                && Field<int>(melee,"comboStepIndex")==interrupted
                && (Time.time-Field<float>(melee,"attackStartTime"))/Field<float>(melee,"attackDuration")>=.25f))
            {Check(Time.unscaledTime<limit,"실제 콤보 진행 도달 "+interrupted);yield return null;}
            yield return StartDodge(false,true);
            Check(input.AttackHeld,"좌클릭 해제 없는 닷지 입력 "+interrupted);
            while(evade.IsEvading)yield return null;
            Check(melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.Light,"콤보 중 닷지 어택 "+interrupted);
            float openerOrigin=Field<float>(melee,"attackStartTime"),openerDuration=Field<float>(melee,"attackDuration");
            poseProbe.poseSamples.Clear();poseProbe.recordPoses=true;
            limit=Time.unscaledTime+3f;
            while(melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.Light)
            {Check(Time.unscaledTime<limit,"기존 다음 타 복귀 제한 "+interrupted);yield return null;}
            int expected=(interrupted+1)%4;
            Check(melee.IsAttackInProgress && Field<int>(melee,"comboStepIndex")==expected
                && Field<AnimationClip>(melee,"activeAttackAnimationClip")==actor.Equipment.CurrentWeaponData.GetMeleeDefinition().comboDefinition.GetStep(expected).animationClip,
                "콤보"+(interrupted+1)+"→닷지 어택→기존 다음"+(expected+1)+"타");
            float resumedProgress=(Time.time-openerOrigin)/openerDuration;
            Check(resumedProgress>=PlayerEvadeBuilder.LightComboStart-.01f && resumedProgress<PlayerEvadeBuilder.LightComboStart+.06f,
                "모든 다음 타가 앞당긴48프레임에서 연결 "+(expected+1)+" progress="+resumedProgress);
            Check(Mathf.Abs(Field<float>(melee,"activeAttackTransitionDuration")-MeleeRuntime.DodgeLightComboBlendDuration)<.0001f,
                "모든 다음 타 전용0.16초 보간 "+(expected+1));
            Send();float firstMixed=-1f,lastMixed=-1f,maxBlendHipsStep=0f;
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);Vector3 previousHip=actor.transform.InverseTransformPoint(hips.position);
            int layer=animator.GetLayerIndex("Combat_MeleeWeapon");
            limit=Time.time+MeleeRuntime.DodgeLightComboBlendDuration+.04f;
            while(Time.time<limit)
            {
                bool mixing=animator.IsInTransition(layer)
                    && animator.GetCurrentAnimatorClipInfo(layer).Any(c=>c.clip.name=="Greatsword_DodgeAttack")
                    && animator.GetNextAnimatorClipInfo(layer).Any(c=>c.clip==Field<AnimationClip>(melee,"activeAttackAnimationClip"));
                Vector3 hip=actor.transform.InverseTransformPoint(hips.position);
                if(mixing){if(firstMixed<0f)firstMixed=Time.time;lastMixed=Time.time;maxBlendHipsStep=Mathf.Max(maxBlendHipsStep,Vector3.Distance(hip,previousHip));}
                previousHip=hip;yield return null;
            }
            poseProbe.recordPoses=false;
            Check(firstMixed>=0f && lastMixed-firstMixed>=MeleeRuntime.DodgeLightComboBlendDuration-.05f,
                "닷지와 다음 타 실제 클립 혼합 유지 "+(expected+1));
            Check(maxBlendHipsStep<.16f,"다음 타 연결 골반 순간 변경 제한 "+(expected+1)+" step="+maxBlendHipsStep);
            File.WriteAllText(Path.Combine(output,"ComboResume_"+(expected+1)+".json"),JsonConvert.SerializeObject(poseProbe.poseSamples,Formatting.Indented));
            samples.Add(new{label="held combo resume",interruptedHit=interrupted+1,resumedHit=expected+1,resumedProgress,openerDuration,
                blendDuration=MeleeRuntime.DodgeLightComboBlendDuration,mixedSpan=lastMixed-firstMixed,maxBlendHipsStep});
        }
    }

    static IEnumerator VerifyDodgeLightRecovery()
    {
        var definition=actor.Equipment.CurrentWeaponData.GetMeleeDefinition();
        var step=definition.dodgeAttackDefinition.GetStep(0);
        float boundary=step.actionCancelStartNormalized;
        Check(Mathf.Abs(boundary-(60f-7f)/(124f-7f))<.0001f,"원본1초 지점을 사본 진행률로 환산");
        Check(Mathf.Abs(step.animationClip.length-117f/60f)<.0001f,"원본 후반124프레임까지 사본 보존");
        int layer=animator.GetLayerIndex("Combat_MeleeWeapon");
        foreach(string mode in new[]{"stationary","earlyMove","lateMove"})
        {
            yield return Reset();yield return StartDodge(false,true);Send();
            while(evade.IsEvading)yield return null;
            Check(melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.Light,"회수 확인 닷지 공격 시작 "+mode);
            float clockOrigin=Field<float>(melee,"attackStartTime"), duration=Field<float>(melee,"attackDuration");
            Vector3 attackOrigin=actor.transform.position;
            poseProbe.poseSamples.Clear();poseProbe.recordPoses=true;
            bool requestedMove=false;float maximumProgress=0f,exitProgress=0f,maxDrift=0f;
            float timeout=Time.unscaledTime+5f;
            while(melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.Light)
            {
                Check(Time.unscaledTime<timeout,"닷지 후반 회수 종료 제한 "+mode);
                float progress=(Time.time-clockOrigin)/duration;maximumProgress=Mathf.Max(maximumProgress,progress);
                if(mode!="stationary" && !requestedMove && progress>=(mode=="earlyMove"?.25f:.70f))
                {Send(true);requestedMove=true;}
                if(progress<boundary-.02f)
                    Check(melee.IsAttackInProgress,"원본1초 이전 이동으로 공격 회수 생략 없음 "+mode);
                var drift=actor.transform.position-attackOrigin;drift.y=0;maxDrift=Mathf.Max(maxDrift,drift.magnitude);
                exitProgress=progress;yield return null;
            }
            float endedAt=Time.time;bool mixedLocomotion=false;float firstMixed=-1f,lastMixed=-1f;
            while(Time.time<endedAt+.35f)
            {
                var current=animator.GetCurrentAnimatorClipInfo(layer);
                var next=animator.GetNextAnimatorClipInfo(layer);
                bool mixing=animator.IsInTransition(layer)
                    && current.Any(c=>c.clip==step.animationClip) && next.Any(c=>c.clip!=step.animationClip);
                mixedLocomotion |= mixing;
                if(mixing){if(firstMixed<0f)firstMixed=Time.time;lastMixed=Time.time;}
                yield return null;
            }
            poseProbe.recordPoses=false;
            float mixedSpan=lastMixed-firstMixed;
            File.WriteAllText(Path.Combine(output,"Recovery_"+mode+".json"),JsonConvert.SerializeObject(new{mode,clockOrigin,duration,boundary,maximumProgress,exitProgress,maxDrift,mixedLocomotion,mixedSpan,poses=poseProbe.poseSamples},Formatting.Indented));
            Check(mixedLocomotion,"회수 자세에서 기본/이동 자세 실제 혼합 "+mode);
            Check(mixedSpan>=(mode=="stationary"?MeleeRuntime.DodgeLightFinishBlendDuration:MeleeRuntime.DodgeLightMovementBlendDuration)-.05f,
                "닷지 회수 보간 시간이 기본 이동 갱신에 단축되지 않음 "+mode+" span="+mixedSpan);
            if(mode=="stationary")
                Check(maximumProgress>.97f && maxDrift<.03f,"단독 닷지 공격 후반 끝까지 제자리 재생");
            else
            {
                var moved=actor.transform.position-attackOrigin;moved.y=0;
                Check(exitProgress>=boundary-.03f && exitProgress<.95f && moved.magnitude>.15f,
                    "원본1초 이후 실제 이동과 회수 보간 "+mode+" at="+exitProgress+" distance="+moved.magnitude);
            }
            samples.Add(new{label="dodge recovery",mode,duration,boundary,maximumProgress,exitProgress,maxDrift,mixedLocomotion});
            Send();Progress("dodge recovery "+mode);
        }
    }

    static IEnumerator VerifyDodgeAttackCorrection()
    {
        foreach (float angle in new[] { -90f, 45f, 90f })
        foreach (bool heavy in new[] { false })
        {
            // E heavy owns its remaining 5m carry and is covered by VerifyDashHeavy.
            yield return Reset(true, angle);
            yield return StartDodge(true, !heavy, heavy);
            Vector3 direction = evade.ActiveDirection;
            Send();
            while (evade.IsEvading) yield return null;
            yield return Frames(1);
            Check(melee.IsAttackInProgress && melee.ActiveDodgeFollowUp == (heavy ? PlayerDodgeFollowUpKind.Heavy : PlayerDodgeFollowUpKind.Light),
                "방향·인플레이스 시험 닷지 공격 시작 " + angle + "/" + heavy);
            Check(Vector3.Angle(Field<Vector3>(melee, "activeAttackDirection"), direction) < .1f
                && Vector3.Angle(actor.transform.forward, direction) < .1f, "조준 방향과 달라도 닷지 방향 공격 " + angle + "/" + heavy);
            var step = Field<MeleeComboStepData>(melee, "activeAttackStep");
            Check(step.movementPhases == null || step.movementPhases.Length == 0, "닷지 공격 authored 이동 없음 " + heavy);
            Vector3 position = actor.transform.position;
            float maxDrift = 0f, limit = Time.unscaledTime + 6f;
            while (melee.IsAttackInProgress)
            {
                Check(Time.unscaledTime < limit, "인플레이스 공격 완료 제한");
                Vector3 delta = actor.transform.position - position; delta.y = 0;
                maxDrift = Mathf.Max(maxDrift, delta.magnitude); yield return null;
            }
            Check(maxDrift < .03f, "닷지 뒤 공격 자체 수평 이동 없음 " + angle + "/" + heavy + " drift=" + maxDrift);
            samples.Add(new { label = "dodge attack in-place", angle, heavy, maxDrift, direction = direction.ToString("F4") });
        }
        yield return Reset();
        yield return StartDodge(false, true); Send();
        var targets = new List<CombatHealth>();
        var callbacks = new List<Action<CombatHealth, DamageInfo>>();
        var order = new List<string>(); var times = new List<float>(); var animationTimes = new List<float>();
        var damageFrames = new List<int>();poseProbe.frames.Clear();
        try
        {
            Vector3 endpoint = startPosition + evade.ActiveDirection * evade.Profile.combatDodge.distance;
            var names = new[] { "left", "center", "right" };
            var angles = new[] { -60f, 0f, 60f };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                var target = new GameObject("Owned dodge sweep " + name); fixtures.Add(target);
                target.transform.position = endpoint + Quaternion.AngleAxis(angles[i], Vector3.up) * evade.ActiveDirection * 2f;
                var combat = target.AddComponent<CombatTarget>(); target.GetComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
                var health = target.GetComponent<CombatHealth>(); health.SetMaxHp(1000000, true);
                var serialized = new SerializedObject(combat); serialized.FindProperty("radius").floatValue = .05f; serialized.ApplyModifiedPropertiesWithoutUndo();
                var data = new SerializedObject(health); data.FindProperty("showDamageNumbers").boolValue = false; data.ApplyModifiedPropertiesWithoutUndo();
                Action<CombatHealth, DamageInfo> callback = (h, damage) => {
                    order.Add(name); times.Add((Time.time - Field<float>(melee, "attackStartTime")) / Field<float>(melee, "attackDuration"));
                    damageFrames.Add(Time.frameCount);
                };
                health.OnDamaged += callback; targets.Add(health); callbacks.Add(callback);
            }
            while (evade.IsEvading) yield return null;
            yield return Frames(1);
            float limit = Time.unscaledTime + 3;
            while (melee.IsAttackInProgress) { Check(Time.unscaledTime < limit, "좌→우 실제 판정 완료 제한"); yield return null; }
            Check(order.SequenceEqual(new[] { "left", "center", "right" }), "실제 피해 좌→중앙→우 한 번씩 " + string.Join(",", order));
            Check(times.All(t => t >= PlayerEvadeBuilder.LightHitStart - .05f && t <= PlayerEvadeBuilder.LightHitEnd + .12f),
                "실제 피해가 원본16~25프레임 베기 구간 " + string.Join(",", times));
            foreach(int damageFrame in damageFrames)animationTimes.Add(poseProbe.frames[damageFrame]);
            Check(times.Zip(animationTimes, (runtime, pose) => Mathf.Abs(runtime - pose)).All(error => error < .15f),
                "실제 피해 시점의 Animator 베기 진행 일치 " + string.Join(",", animationTimes));
            samples.Add(new { label = "dodge left-to-right hit order", order, normalizedTimes = times, animationTimes });
        }
        finally
        {
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null) { targets[i].OnDamaged -= callbacks[i]; var target = targets[i].gameObject; fixtures.Remove(target); UnityEngine.Object.Destroy(target); }
        }
        yield return Reset(); yield return StartDodge(false, true);
        while (evade.IsEvading) yield return null;
        yield return Frames(1);
        float duration = Field<float>(melee, "attackDuration");
        Check(duration < .5f, "닷지 어택 긴 꼬리 절단·전체 동작0.5초 이내 " + duration);
        float hitEnd = Field<float>(melee, "attackStartTime") + duration * PlayerEvadeBuilder.LightHitEnd;
        float comboLimit = Time.time + 2f;
        while (melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light) { Check(Time.time < comboLimit, "닷지 어택 후속1타 제한"); yield return null; }
        float gap = Time.time - hitEnd;
        Check(melee.IsAttackInProgress && Field<int>(melee, "comboStepIndex") == 0 && gap < .15f,
            "베기 종료 후0.15초 이내 일반1타 연결 " + gap); Send();
        samples.Add(new { label = "dodge opener combo recovery", duration, gap });
    }

    static IEnumerator ParryFollowUp()
    {
        var ui = EnemyThemeTrialHarness.Current;
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "패링 스폰 서비스");
        foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "패링 적 카탈로그");
        var definition = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null && d.Grade.GradeType != EnemyGradeType.Boss
            && Enumerable.Range(0, d.AbilitySet.Count).Any(i => d.AbilitySet.GetAbility(i).IsParryable && d.AbilitySet.GetAbility(i).UsesPacedTimeline
                && d.AbilitySet.GetAbility(i).ExecutionMode == EnemyAbilityExecutionMode.MeleeArc));
        var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(i => definition.AbilitySet.GetAbility(i))
            .First(a => a.IsParryable && a.UsesPacedTimeline && a.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc);
        yield return Reset();
        var energy = actor.GetComponent<OverburstElementEnergy>(); FillEnergy(energy, 7000); float energyBefore = energy.Amount;
        var point = origin + forward * Mathf.Max(1.1f, ability.Range * .6f);
        Check(Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "패링 적 바닥");
        Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-forward), actor.transform), out var enemy), "패링 실제 적 생성");
        leased.Add(enemy); enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(100000, true);
        var fixture = ScriptableObject.CreateInstance<EnemyAbilitySet>(); fixtures.Add(fixture);
        var data = new SerializedObject(fixture); data.FindProperty("abilitySetId").stringValue = "evade-parry-owned-fixture";
        var entries = data.FindProperty("abilities"); entries.arraySize = 1; entries.GetArrayElementAtIndex(0).objectReferenceValue = ability; data.ApplyModifiedPropertiesWithoutUndo();
        enemy.AbilityController.Configure(fixture, 1f, 1f);
        Check(enemy.AbilityController.TryStart(actor.transform), "패링 적 강공 실행");
        float threatLimit = Time.unscaledTime + 4;
        while (!enemy.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()))
        { Check(Time.unscaledTime < threatLimit, "실제 적의 패링 위협 준비"); yield return null; }
        yield return StartDodge(false); Send(false, false, false, true); yield return Frames(2); Send();
        var parry = actor.GetComponent<PlayerParryController>(); int before = parry.SuccessCount;
        while (evade.IsEvading) yield return null;
        yield return Frames(1);
        Check(melee.IsHeavyAttackInProgress, "예약 대시 강공 또는 즉시 패링 강공 시작");
        float limit = Time.unscaledTime + 2;
        while (!melee.IsHeavyParryMotionActive && melee.IsAttackInProgress && Time.unscaledTime < limit) yield return null;
        Check(parry.SuccessCount == before + 1 && melee.IsHeavyParryMotionActive, "닷지 강공 실제 적 패링·모션 삽입");
        int id = Field<int>(melee, "activeActionId"); bool committed = false; bool resumed = false; bool variantResumed = false;
        limit = Time.unscaledTime + 8;
        while (melee.IsAttackInProgress)
        {
            Check(Time.unscaledTime < limit && Field<int>(melee, "activeActionId") == id, "패링 후 같은 강공 ID");
            if (!melee.IsHeavyParryMotionActive) { resumed = true; variantResumed |= Field<AnimationClip>(melee, "activeAttackAnimationClip") == actor.Equipment.CurrentWeaponData.GetMeleeDefinition().parriedHeavyAttackDefinition.attack.animationClip; }
            committed |= Field<bool>(melee, "heavyDischargeCommitted"); yield return null;
        }
        Check(resumed && committed && variantResumed,
            "패링 후 기존 강화 강공 모션으로 재개·착지");
        Check(Mathf.Abs(energy.Amount - energyBefore * .5f) < .01f, "닷지 강공 패링 기존 에너지 절반 환급");
        spawn.Release(enemy); leased.Remove(enemy); yield return Wait(1f);
    }
    static void FillEnergy(OverburstElementEnergy energy, int sequence)
    {
        energy.Clear(); var item = actor.Equipment.CurrentWeaponItem;
        for (int i = 0; i < 20; i++) energy.RecordConfirmedHit(item.runtimeInstanceId, actor.Equipment.ActiveElement, sequence + i, 1);
        Check(energy.Amount > 0, "실제 원소 에너지 충전");
    }
}

/// <summary>Animator evaluation 이후 실제 타격 프레임의 자세를 읽는다.</summary>
[DefaultExecutionOrder(10000)]
public sealed class PlayerEvadePoseProbe : MonoBehaviour
{
    public Animator animator;
    public readonly Dictionary<int,float> frames = new Dictionary<int,float>();
    public bool recordPoses;
    public Transform actorRoot;
    public Transform[] bones;
    public readonly List<object> poseSamples=new List<object>();
    void LateUpdate()
    {
        if (animator == null) return;
        int layer = animator.GetLayerIndex("Combat_MeleeWeapon");
        if (layer < 0) return;
        var pose = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer) : animator.GetCurrentAnimatorStateInfo(layer);
        var current = animator.GetCurrentAnimatorStateInfo(layer);
        var next = animator.GetNextAnimatorStateInfo(layer);
        // Damage can occur on the frame that starts the next combo. Compare its outgoing dodge pose.
        frames[Time.frameCount] = animator.IsInTransition(layer) && next.IsName(PlayerEvadeProfile.DodgeLightState)
            ? next.normalizedTime : current.IsName(PlayerEvadeProfile.DodgeLightState) ? current.normalizedTime : pose.normalizedTime;
        if(recordPoses && actorRoot!=null && bones!=null)
            poseSamples.Add(new{frame=Time.frameCount,time=Time.time,unscaled=Time.unscaledTime,transition=animator.IsInTransition(layer),
                current=animator.GetCurrentAnimatorClipInfo(layer).Select(c=>new{name=c.clip.name,c.weight}).ToArray(),
                next=animator.GetNextAnimatorClipInfo(layer).Select(c=>new{name=c.clip.name,c.weight}).ToArray(),
                positions=bones.Select(b=>{var v=actorRoot.InverseTransformPoint(b.position);return new[]{v.x,v.y,v.z};}).ToArray()});
    }
}
