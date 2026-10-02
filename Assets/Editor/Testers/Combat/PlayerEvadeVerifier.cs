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
    static bool background;
    static Keyboard keyboard;
    static Mouse mouse;
    static PlayerInputFacade input;
    static PlayerActorRuntime actor;
    static PlayerEvadeController evade;
    static MeleeRuntime melee;
    static PlayerMovement movement;
    static Animator animator;
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
    public static void StartIsolated(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 Editor가 필요합니다.");
        string target = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(target);
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
                Send(); yield return CompleteEvade("combat angle " + angle, 4, front ? .32f : .45f);
            }
            yield return Reset(); yield return StartDodge(false); Send(false, false, true); yield return Frames(2); Send();
            Check(input.CombatInputs.PendingDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "닷지 중 짧은 좌클릭 예약");
            yield return CompleteEvade("light queued", 4, .32f);
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light && Field<int>(melee, "comboStepIndex") == 0, "예약 약공 진입 1타");
            int action = Field<int>(melee, "activeActionId"); bool continued = false; float tapEnd = Time.unscaledTime + 2f;
            while (Time.unscaledTime < tapEnd) { continued |= melee.IsAttackInProgress && Field<int>(melee, "comboStepIndex") > 0; yield return null; }
            Check(!melee.IsAttackInProgress && !continued && Field<int>(melee, "nextActionId") == action + 1, "짧은 클릭 한 타만 실행");
            yield return Reset(); yield return StartDodge(true, true); yield return CompleteEvade("same frame held light", 4, .32f);
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "Shift·클릭 같은 프레임 수락");
            float continuationLimit = Time.unscaledTime + 3;
            while (Field<int>(melee, "comboStepIndex") < 1) { Check(Time.unscaledTime < continuationLimit, "일반 2타 연결 제한"); yield return null; }
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None && Field<int>(melee, "comboStepIndex") == 1, "좌클릭 홀드 일반 2타 연결"); Send();
            yield return Reset(); melee.SetManualInputEnabled(false); Send(false, false, true); yield return Frames(2);
            yield return StartDodge(false, true); melee.SetManualInputEnabled(true); yield return CompleteEvade("held before", 4, .32f);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None, "회피 전 누르고 있던 클릭 전용 공격 아님"); Send();
            yield return Reset(); yield return StartDodge(false); yield return CompleteEvade("no click", 4, .32f);
            Send(false, false, true); yield return Frames(2);
            Check(melee.IsAttackInProgress && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None, "닷지 종료 후 클릭 일반 공격"); Send();

            yield return Reset();
            var energy = actor.GetComponent<OverburstElementEnergy>();
            if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
            FillEnergy(energy, 6000);
            Check(melee.TryStartHeavyAttack(forward) == WeaponActionResult.Accepted, "비교 일반 강공 시작");
            float heavyDuration = Field<float>(melee, "attackDuration"); string heavyStep = JsonUtility.ToJson(Field<MeleeComboStepData>(melee, "activeAttackStep"));
            float damageMultiplier = Field<float>(melee, "activeAttackDamageMultiplier");
            melee.CancelCurrentAttackState(); yield return Reset(); yield return StartDodge(false); Send(false, false, true, true); yield return Frames(2); Send();
            yield return CompleteEvade("both heavy priority", 4, .32f);
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy && melee.IsHeavyAttackInProgress, "좌우 경합 강공 한 번");
            Check(heavyStep == JsonUtility.ToJson(Field<MeleeComboStepData>(melee, "activeAttackStep"))
                && Mathf.Abs(heavyDuration - Field<float>(melee, "attackDuration")) < .0001f
                && Mathf.Approximately(damageMultiplier, Field<float>(melee, "activeAttackDamageMultiplier")), "동일 강공 전체 판정·피드백·이동 정의와 시간");
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
            yield return Frames(2); yield return StartDodge(false); yield return CompleteEvade("slow clock", 4, .32f);
            OverburstTimeEffectArbiter.ClearOwner(blocker); yield return Wait(.2f);
            yield return Reset(); OverburstTimeEffectArbiter.Request(blocker, OverburstTimeEffectKind.HitStop, .01f, .5f);
            yield return Frames(2); yield return StartDodge(false); yield return CompleteEvade("hitstop clock", 4, .32f);
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
            if (blocker != null) { GameplayInputBlocker.Unblock(blocker); OverburstTimeEffectArbiter.ClearOwner(blocker); UnityEngine.Object.Destroy(blocker); }
            OverburstTimeEffectArbiter.SetPaused(false);
            foreach (var enemy in leased) if (enemy != null && enemy.IsLeased) spawn?.Release(enemy); leased.Clear();
            foreach (var fixture in fixtures) if (fixture != null) UnityEngine.Object.Destroy(fixture); fixtures.Clear();
            melee?.CancelCurrentAttackState(); actor?.GetComponent<PlayerKnockdownController>()?.ResetReaction();
        }
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
