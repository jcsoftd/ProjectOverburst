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
public static class PlayerEvadeVerifier
{
    const string PendingKey = "Overburst.PlayerEvadeVerifier.Pending";
    const string DeadlineKey = "Overburst.PlayerEvadeVerifier.Deadline";
    const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<object> checks = new List<object>(), samples = new List<object>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator work;
    static string output;
    static int frame, failures;
    static double deadline;
    static bool background, lightOnly;
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
    static bool requestPause, requestResume, pauseRecorded, invalidateOnCompletion;
    static float frozenProgress;
    static Vector3 frozenPosition;
    static ItemData weaponItem;

    static PlayerEvadeVerifier()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))) EditorApplication.update += AutoBegin;
    }
    public static void StartIsolated(string directory, bool onlyDodgeLight = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 Editor가 필요합니다.");
        string target = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(target);
        SessionState.SetBool(PendingKey + ".LightOnly", onlyDodgeLight);
        SessionState.SetString(PendingKey, target);
        SessionState.SetString(DeadlineKey, (EditorApplication.timeSinceStartup + 120).ToString(System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.update -= AutoBegin; EditorApplication.update += AutoBegin;
        var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }).ToArray();
        File.WriteAllText(Path.Combine(target, "Before.json"), JsonConvert.SerializeObject(new { scenes }, Formatting.Indented));
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(target, "IsolatedAccount")); }
        catch { ClearPending(); throw; }
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
        deadline = EditorApplication.timeSinceStartup + 240;
        background = Application.runInBackground; Application.runInBackground = true;
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
        while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); (work as IDisposable)?.Dispose(); work = null;
        Application.runInBackground = background;
        File.WriteAllText(Path.Combine(output, "PlayResult.json"), JsonConvert.SerializeObject(new { status, failures, checks, samples, errors }, Formatting.Indented));
        if (exit && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
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
        input.CombatInputs.Invalidate();
        if (combat) PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        else PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System);
        testFacing = Quaternion.Euler(0, -angle, 0) * forward;
        ActorTeleportUtility.TeleportSafely(actor.transform, origin, Quaternion.LookRotation(testFacing));
        yield return Wait(.9f); starts = ends = 0;
    }
    static IEnumerator StartDodge(bool move, bool left = false, bool right = false)
    {
        poseNextStart = true;
        Send(move, true, left, right); yield return Frames(2);
        Check(evade.IsEvading, "실제 Shift 액션 회피 수락"); Send(move, false, left, right);
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
            if (lightOnly) { yield return VerifyDodgeLightOverlap(); yield return VerifyDodgeComboResume(); yield break; }
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
                Send(); yield return CompleteEvade("combat angle " + angle, 4, front ? .48f : .45f);
            }
            yield return Reset(); yield return StartDodge(false); Send(false, false, true); yield return Frames(2); Send();
            Check(input.CombatInputs.PendingDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "닷지 중 짧은 좌클릭 예약");
            yield return CompleteEvade("light queued", 4, .48f);
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light && Field<int>(melee, "comboStepIndex") == 0, "예약 약공 진입 1타");
            int action = Field<int>(melee, "activeActionId"); bool continued = false; float tapEnd = Time.unscaledTime + 2f;
            while (Time.unscaledTime < tapEnd) { continued |= melee.IsAttackInProgress && Field<int>(melee, "comboStepIndex") > 0; yield return null; }
            Check(!melee.IsAttackInProgress && !continued && Field<int>(melee, "nextActionId") == action + 1, "짧은 클릭 한 타만 실행");
            yield return Reset(); yield return StartDodge(true, true); yield return CompleteEvade("same frame held light", 4, .48f);
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "Shift·클릭 같은 프레임 수락");
            float continuationLimit = Time.unscaledTime + 3;
            while (melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light) { Check(Time.unscaledTime < continuationLimit, "일반 1타 연결 제한"); yield return null; }
            Check(melee.IsAttackInProgress && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None && Field<int>(melee, "comboStepIndex") == 0, "좌클릭 홀드 일반 1타 연결"); Send();
            yield return Reset(); melee.SetManualInputEnabled(false); Send(); yield return Frames(2); Send(false, false, true); yield return Frames(2);
            yield return StartDodge(false, true); melee.SetManualInputEnabled(true); yield return CompleteEvade("held before", 4, .48f);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "회피 전 유지한 좌클릭 전용 공격 수락"); Send();
            yield return Reset(); yield return StartDodge(false); yield return CompleteEvade("no click", 4, .48f);
            Send(false, false, true); yield return Frames(2);
            Check(melee.IsAttackInProgress && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None, "닷지 종료 후 클릭 일반 공격"); Send();

            yield return Reset();
            var energy = actor.GetComponent<OverburstElementEnergy>();
            if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
            FillEnergy(energy, 6000);
            Check(melee.TryStartHeavyAttack(forward) == WeaponActionResult.Accepted, "비교 일반 강공 시작");
            float heavyDuration = Field<float>(melee, "attackDuration"); var normalHeavyStep = Field<MeleeComboStepData>(melee, "activeAttackStep");
            normalHeavyStep.movementPhases = Array.Empty<AttackMovementPhaseData>(); normalHeavyStep.visualHeightCurve = null;
            string heavyStep = JsonUtility.ToJson(normalHeavyStep);
            float damageMultiplier = Field<float>(melee, "activeAttackDamageMultiplier");
            melee.CancelCurrentAttackState(); yield return Reset(); yield return StartDodge(false); Send(false, false, true, true); yield return Frames(2); Send();
            yield return CompleteEvade("both heavy priority", 4, .48f);
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy && melee.IsHeavyAttackInProgress, "좌우 경합 강공 한 번");
            Check(heavyStep == JsonUtility.ToJson(Field<MeleeComboStepData>(melee, "activeAttackStep"))
                && Mathf.Abs(heavyDuration - Field<float>(melee, "attackDuration")) < .0001f
                && Mathf.Approximately(damageMultiplier, Field<float>(melee, "activeAttackDamageMultiplier")), "이동/시각 점프만 제외한 동일 강공 판정·피드백·시간");
            Check(Field<AnimationClip>(melee, "activeAttackAnimationClip") == actor.Equipment.CurrentWeaponData.GetMeleeDefinition().dodgeHeavyAnimationClip, "강공 모션만 대체");
            Check(actor.GetComponent<PlayerParryController>().IsWindowOpen, "닷지 강공 기존 패링 창");
            action = Field<int>(melee, "activeActionId"); bool sawCommit = false; int commits = 0; float heavyLimit = Time.unscaledTime + 6;
            while (melee.IsAttackInProgress)
            {
                Check(Time.unscaledTime < heavyLimit, "닷지 강공 완료 제한");
                bool committed = Field<bool>(melee, "heavyDischargeCommitted"); if (committed && !sawCommit) commits++; sawCommit |= committed;
                Check(Field<int>(melee, "activeActionId") == action, "닷지 강공 행동 ID 유지"); yield return null;
            }
            Check(commits == 1 && input.CombatInputs.PendingDodgeFollowUp == PlayerDodgeFollowUpKind.None, "강공 착지·예약 소모 각각 한 번");
            Check(energy.Amount < .001f, "닷지 강공 기존 에너지 전량 소모");
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
            yield return Frames(2); yield return StartDodge(false); yield return CompleteEvade("slow clock", 4, .48f);
            OverburstTimeEffectArbiter.ClearOwner(blocker); yield return Wait(.2f);
            yield return Reset(); OverburstTimeEffectArbiter.Request(blocker, OverburstTimeEffectKind.HitStop, .01f, .5f);
            yield return Frames(2); yield return StartDodge(false); yield return CompleteEvade("hitstop clock", 4, .48f);
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
            Check(evade.LastEndWasCompleted && Mathf.Abs(resumedTravel.magnitude - 4f) < .12f && !melee.IsAttackInProgress,
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
            if (OverburstGameMenu.IsOpen) OverburstGameMenu.Instance?.Close();
            if (evade != null) { evade.OnEvadeStarted -= Started; evade.OnEvadeEnded -= Ended; evade.CancelForKnockdown(); evade.enabled = true; }
            if (input != null) { input.EnableGameplay(); input.RuntimeAsset.devices = previousDevices; }
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
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
            try
            {
                Vector3 endpoint = startPosition + evade.ActiveDirection * 4f;
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
                        Check(Mathf.Abs(Field<float>(melee,"activeAttackTransitionDuration")-.12f)<.0001f,"일반1타 연결0.12초 보간");
                        actualCombo1=true; Send();
                        bool distinctBlend=false;float blendBodyStep=0f, blendLimit=Time.time+.15f;
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
            Check(melee.IsDodgeLightWindupActive,"취소 전 준비 모션 "+cancel);Send();
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
            limit=Time.unscaledTime+3f;
            while(melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.Light)
            {Check(Time.unscaledTime<limit,"기존 다음 타 복귀 제한 "+interrupted);yield return null;}
            int expected=(interrupted+1)%4;
            Check(melee.IsAttackInProgress && Field<int>(melee,"comboStepIndex")==expected
                && Field<AnimationClip>(melee,"activeAttackAnimationClip")==actor.Equipment.CurrentWeaponData.GetMeleeDefinition().comboDefinition.GetStep(expected).animationClip,
                "콤보"+(interrupted+1)+"→닷지 어택→기존 다음"+(expected+1)+"타");
            samples.Add(new{label="held combo resume",interruptedHit=interrupted+1,resumedHit=expected+1});Send();yield return Wait(.15f);
        }
    }

    static IEnumerator VerifyDodgeAttackCorrection()
    {
        foreach (float angle in new[] { -90f, 45f, 90f })
        foreach (bool heavy in new[] { false, true })
        {
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
            Vector3 endpoint = startPosition + evade.ActiveDirection * 4f;
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
        Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy, "예약 닷지 강공 시작");
        float limit = Time.unscaledTime + 2;
        while (!melee.IsHeavyParryMotionActive && melee.IsAttackInProgress && Time.unscaledTime < limit) yield return null;
        Check(parry.SuccessCount == before + 1 && melee.IsHeavyParryMotionActive, "닷지 강공 실제 적 패링·모션 삽입");
        int id = Field<int>(melee, "activeActionId"); bool committed = false; bool resumed = false; bool variantResumed = false;
        limit = Time.unscaledTime + 8;
        while (melee.IsAttackInProgress)
        {
            Check(Time.unscaledTime < limit && Field<int>(melee, "activeActionId") == id, "패링 후 같은 강공 ID");
            if (!melee.IsHeavyParryMotionActive) { resumed = true; variantResumed |= Field<AnimationClip>(melee, "activeAttackAnimationClip") == actor.Equipment.CurrentWeaponData.GetMeleeDefinition().dodgeHeavyAnimationClip; }
            committed |= Field<bool>(melee, "heavyDischargeCommitted"); yield return null;
        }
        Check(resumed && committed && variantResumed,
            "패링 후 닷지 강공 모션으로 재개·착지");
        Check(Mathf.Abs(energy.Amount - energyBefore * .5f) < .01f, "닷지 강공 패링 기존 에너지 절반 환급");
        spawn.Release(enemy); leased.Remove(enemy); yield return Wait(1f);
    }
    static void FillEnergy(OverburstElementEnergy energy, int sequence)
    {
        energy.Clear(); var item = actor.Equipment.CurrentWeaponItem;
        for (int i = 0; i < 20; i++) energy.RecordConfirmedHit(item.runtimeInstanceId, item.ResolvedElement, sequence + i, 1);
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
