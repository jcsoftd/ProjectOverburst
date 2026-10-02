using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>공급사 원본과 파생 포즈, 실제 적 공격 패링과 같은 강공의 재개·취소를 검증한다.</summary>
public static class GreatswordHeavyParryVerifier
{
    const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator work;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<object> checks = new List<object>(), actions = new List<object>();
    static readonly List<string> errors = new List<string>();
    static string output;
    static int failures, frame;
    static double deadline;
    static bool background;
    static bool captureScreenshots;
    static bool ownsReloadLock, ownsRefreshLock;
    static PlayerActorRuntime captureOwner;
    static readonly List<Coroutine> captures = new List<Coroutine>();

    public static void VerifyAssets(string directory)
    {
        Directory.CreateDirectory(directory);
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(GreatswordHeavyParryBuilder.SourcePath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(GreatswordHeavyParryBuilder.ClipPath);
        var profile = AssetDatabase.LoadAssetAtPath<WeaponCombatAnimationProfile>(GreatswordHeavyParryBuilder.ProfilePath);
        if (source == null || clip == null || profile == null) throw new InvalidOperationException("자산이 없습니다.");
        if (!clip.isHumanMotion || AnimationUtility.GetAnimationClipSettings(clip).loopTime
            || Mathf.Abs(clip.length - (GreatswordHeavyParryBuilder.SourceEndSeconds - GreatswordHeavyParryBuilder.SourceStartSeconds)) > .0001f || profile.heavyParryClip != clip)
            throw new InvalidOperationException("파생 클립 연결·길이·Humanoid·반복 설정 오류");
        var counter = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(GreatswordHeavyParryBuilder.CounterDefinitionPath);
        var weapon = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(GreatswordHeavyParryBuilder.WeaponDefinitionPath);
        var weak = weapon.comboDefinition.GetStep(2);
        if (counter == null || !counter.IsConfigured || weapon.parriedHeavyAttackDefinition != counter
            || counter.attack.attackPhases.Length != 3 || counter.SafeDischargePhaseIndex != 2
            || AnimationUtility.GetAnimationClipSettings(counter.attack.animationClip).loopTime
            || !Mathf.Approximately(profile.heavyParryPlaybackSpeed, .5f)
            || !Mathf.Approximately(profile.heavyParryHeavyStartSeconds, .138f)
            || !Mathf.Approximately(profile.heavyParryToAttackBlend, .1f))
            throw new InvalidOperationException("패링 강화 강공·속도·시작 자세·보간 연결 오류");
        for (int i = 0; i < 2; i++)
        {
            var phase = counter.attack.attackPhases[i]; var template = weak.attackPhases[i];
            if (phase.attackPattern != template.attackPattern || phase.vfxCues[0].definition != template.vfxCues[0].definition
                || !Mathf.Approximately(phase.impact.damageMultiplier, template.impact.damageMultiplier)
                || phase.progressSource != AttackProgressSource.NormalizedTime)
                throw new InvalidOperationException("약공3타의 두 회전 범위·VFX·타격 설정 재사용 오류");
        }
        var scene = EditorSceneManager.NewPreviewScene();
        var graph = default(PlayableGraph);
        try
        {
            var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab"));
            SceneManager.MoveGameObjectToScene(model, scene);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var animator = model.GetComponentInChildren<Animator>(true);
            animator.applyRootMotion = false;
            var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => animator.GetBoneTransform((HumanBodyBones)i)).Where(t => t != null).ToArray();
            graph = PlayableGraph.Create("Owned parry pose comparison");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var a = AnimationClipPlayable.Create(graph, source);
            var b = AnimationClipPlayable.Create(graph, clip);
            a.SetApplyFootIK(false); b.SetApplyFootIK(false);
            var mixer = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(a, 0, mixer, 0); graph.Connect(b, 0, mixer, 1);
            var channel = AnimationPlayableOutput.Create(graph, "Compare", animator); channel.SetSourcePlayable(mixer); graph.Play();
            var samples = new List<object>();
            float maximumAngle = 0f, maximumPosition = 0f;
            foreach (float t in new[] { 0f, .0333333f, .0833333f, .13f, .20f, .266f })
            {
                a.SetTime(t + GreatswordHeavyParryBuilder.SourceStartSeconds);
                mixer.SetInputWeight(0, 1); mixer.SetInputWeight(1, 0); graph.Evaluate(0);
                var rotations = bones.Select(x => x.localRotation).ToArray();
                var positions = bones.Select(x => x.localPosition).ToArray();
                b.SetTime(t); mixer.SetInputWeight(0, 0); mixer.SetInputWeight(1, 1); graph.Evaluate(0);
                float angle = 0f, position = 0f;
                for (int i = 0; i < bones.Length; i++)
                { angle = Mathf.Max(angle, Quaternion.Angle(rotations[i], bones[i].localRotation)); position = Mathf.Max(position, Vector3.Distance(positions[i], bones[i].localPosition)); }
                maximumAngle = Mathf.Max(maximumAngle, angle); maximumPosition = Mathf.Max(maximumPosition, position);
                samples.Add(new { t, angle, position });
            }
            if (maximumAngle > .15f || maximumPosition > .002f) throw new InvalidOperationException("원본과 파생 포즈 불일치 " + maximumAngle + "deg / " + maximumPosition + "m");
            File.WriteAllText(Path.Combine(directory, "AssetValidation.json"), JsonConvert.SerializeObject(new
            { status = "PASS", clip.length, clip.frameRate, clip.isHumanMotion, maximumAngle, maximumPosition, samples,
                profile.heavyParryPlaybackSpeed, profile.heavyParryHeavyStartSeconds, profile.heavyParryToAttackBlend,
                counter = counter.name, counterClip = counter.attack.animationClip.name, phases = counter.attack.attackPhases.Length,
                counter.dischargePhaseIndex }, Formatting.Indented));
        }
        finally { if (graph.IsValid()) graph.Destroy(); EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static void Begin(string directory, bool screenshots = true)
    {
        if (work != null || stack.Count > 0) throw new InvalidOperationException("검증이 진행 중입니다.");
        output = IsolatedSavePlayGuard.ValidateDirectory(directory);
        if (!EditorApplication.isPlaying || !Overburst.Persistence.AccountBootstrap.Ready
            || string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("제품 부팅이 끝난 격리 Play가 필요합니다.");
        Directory.CreateDirectory(output);
        checks.Clear(); actions.Clear(); errors.Clear(); failures = 0; frame = -1;
        captures.Clear();
        captureScreenshots = screenshots;
        captureOwner = PlayerContext.GetOrCreate().CurrentActor;
        background = Application.runInBackground; Application.runInBackground = true;
        deadline = EditorApplication.timeSinceStartup + 240;
        work = Run();
        EditorApplication.LockReloadAssemblies(); ownsReloadLock = true;
        AssetDatabase.DisallowAutoRefresh(); ownsRefreshLock = true;
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += State;
        AssemblyReloadEvents.beforeAssemblyReload += Abort;
    }

    static void Check(bool passed, string label)
    { checks.Add(new { label, passed }); if (!passed) { failures++; throw new InvalidOperationException(label); } }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);
    static void Progress(string phase)
    {
        File.WriteAllText(Path.Combine(output, "Progress.json"), JsonConvert.SerializeObject(new
        { status = "RUNNING", phase, failures, checkCount = checks.Count, actions }, Formatting.Indented));
    }
    static void Log(string text, string trace, LogType kind)
    { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors.Add(text + "\n" + trace); }
    static void State(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("ABORTED", false); }
    static void Abort() => Finish("RELOADING", false);
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!Application.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("패링 검증 시간 제한");
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
        try
        {
        EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= State;
        Application.logMessageReceived -= Log; AssemblyReloadEvents.beforeAssemblyReload -= Abort;
        while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); (work as IDisposable)?.Dispose(); work = null;
        if (captureOwner != null) foreach (var capture in captures) if (capture != null) captureOwner.StopCoroutine(capture);
        captures.Clear(); captureOwner = null;
        Application.runInBackground = background;
        File.WriteAllText(Path.Combine(output, "PlayResult.json"), JsonConvert.SerializeObject(new { status, failures, checks, actions, errors }, Formatting.Indented));
        }
        finally
        {
            if (ownsRefreshLock) { ownsRefreshLock = false; AssetDatabase.AllowAutoRefresh(); }
            if (ownsReloadLock) { ownsReloadLock = false; EditorApplication.UnlockReloadAssemblies(); }
        }
        if (exit && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static IEnumerator Wait(float seconds) { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }
    static void Shot(string name)
    {
        if (captureScreenshots && captureOwner != null) captures.Add(captureOwner.StartCoroutine(CaptureAfterFrame(name)));
    }
    static IEnumerator CaptureAfterFrame(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D image = null;
        try { image = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG()); }
        finally { if (image != null) UnityEngine.Object.Destroy(image); }
    }

    static IEnumerator Run()
    {
        var actor = PlayerContext.GetOrCreate().CurrentActor;
        var player = PlayerInputFacade.Current;
        var melee = actor.GetComponent<MeleeRuntime>();
        var animator = actor.GetComponentInChildren<Animator>(true);
        var energy = actor.GetComponent<OverburstElementEnergy>();
        if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
        var ui = EnemyThemeTrialHarness.Current;
        var leases = new List<EnemyActor>(); var fixtures = new List<UnityEngine.Object>();
        var damageHooks = new List<(CombatHealth health, Action<CombatHealth, DamageInfo, float, bool> handler)>();
        EnemySpawnService spawn = null;
        var originalMode = animator.updateMode; float originalSpeed = animator.speed;
        try
        {
            Check(actor != null && melee != null && ui != null, "제품 플레이어와 시험 구역 참조");
            melee.CancelCurrentAttackState(); actor.Health.SetMaxHp(1000000f, true);
            Check(actor.Equipment.EquipWeaponItem(new ItemData(AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath), 1, ItemGrade.Common)), "실제 대검 장착");
            if (!ui.InArena) ui.ToggleArena();
            yield return Wait(.8f);
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "시험 구역 스폰 서비스");
            foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "적 카탈로그 등록");
            var definition = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null
                && d.Grade.GradeType != EnemyGradeType.Boss && Enumerable.Range(0, d.AbilitySet.Count)
                    .Any(i => d.AbilitySet.GetAbility(i).IsParryable && d.AbilitySet.GetAbility(i).UsesPacedTimeline && d.AbilitySet.GetAbility(i).ExecutionMode == EnemyAbilityExecutionMode.MeleeArc));
            var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(i => definition.AbilitySet.GetAbility(i))
                .First(a => a.IsParryable && a.UsesPacedTimeline && a.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc);
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee.SetManualInputEnabled(true); yield return Wait(1f);
            int layer = animator.GetLayerIndex("Combat_MeleeWeapon");
            Check(layer >= 0, "대검 전투 Animator 레이어");
            Progress("Setup complete");
            Check(Mathf.Approximately(ParryFeedbackService.ResolveTier(1, false).Slow, .25f)
                && Mathf.Approximately(ParryFeedbackService.ResolveTier(2, false).Slow, .29f)
                && Mathf.Approximately(ParryFeedbackService.ResolveTier(4, false).Slow, .35f), "슬로우 세 단계 +0.05초");

            EnemyActor Spawn(Vector3 direction, bool delayed = false, bool late = false)
            {
                var p = player.transform.position + direction * Mathf.Max(1.1f, ability.Range * .6f);
                Check(Physics.Raycast(p + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "적 위치 바닥");
                Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-direction), player.transform), out var enemy), "실제 적 생성");
                leases.Add(enemy); enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(100000f, true);
                enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (late) enemy.transform.position = player.transform.position + direction
                    * Mathf.Min(2.4f, EnemyAttackThreatGeometry.ResolveStartRange(enemy, ability) * .85f);
                var fixture = ScriptableObject.CreateInstance<EnemyAbilitySet>(); fixtures.Add(fixture);
                var data = new SerializedObject(fixture); data.FindProperty("abilitySetId").stringValue = "heavy-parry-owned-fixture";
                var abilities = data.FindProperty("abilities"); abilities.arraySize = 1; abilities.GetArrayElementAtIndex(0).objectReferenceValue = ability; data.ApplyModifiedPropertiesWithoutUndo();
                enemy.AbilityController.Configure(fixture, 1f, 1f);
                if (delayed || late)
                {
                    // 예약 간격 0.6초와 위협 선행 창 0.7초가 겹치는 실제 강공 두 개를 만든다.
                    // 제품 자산은 그대로 두고 두 번째 적의 소유 사본 준비 시간만 0.62초 늦춘다.
                    float speed = enemy.GetComponent<EnemyMeleeAttackController>().AbilityAnimationSpeed;
                    var shifted = UnityEngine.Object.Instantiate(ability); fixtures.Add(shifted);
                    var shiftedData = new SerializedObject(shifted);
                    if (delayed)
                    {
                        var preparation = shiftedData.FindProperty("preparationDuration");
                        preparation.floatValue = (Mathf.Max(.42f, preparation.floatValue / speed) + .62f) * speed;
                    }
                    if (late) shiftedData.FindProperty("requireTargetInRangeUntilHit").boolValue = false;
                    shiftedData.ApplyModifiedPropertiesWithoutUndo();
                    if (delayed) Check(Mathf.Abs(shifted.ResolveFirstImpactTime(speed) - ability.ResolveFirstImpactTime(speed) - .62f) < .001f,
                        "다수 검사 실제 강공 예약 간격 0.62초");
                    abilities.GetArrayElementAtIndex(0).objectReferenceValue = shifted; data.ApplyModifiedPropertiesWithoutUndo();
                    enemy.AbilityController.Configure(fixture, 1f, 1f);
                }
                return enemy;
            }
            void Release()
            { foreach (var enemy in leases) if (enemy != null && enemy.IsLeased) spawn.Release(enemy); leases.Clear(); }
            IEnumerator Threats(EnemyActor[] enemies)
            {
                foreach (var enemy in enemies) Check(enemy.AbilityController.TryStart(player.transform), "적의 실제 강공 시작");
                float limit = Time.unscaledTime + 4;
                while (!enemies.All(e => e.AbilityController.IsParryThreatTo(player.GetComponent<CombatTarget>())))
                { Check(Time.unscaledTime < limit, "실제 강공 피해 범위의 패링 후보"); yield return null; }
            }
            IEnumerator Complete(string label, bool expectParry, float savedProgress, Vector3 heldPosition)
            {
                bool sawParry = false, sawBridge = false, sawImpact = false, nativeTransitionObserved = false; int commits = 0, frameCount = 0;
                bool priorCommit = false; float maxHoldMovement = 0f, maxNormalizedError = 0f;
                float maxFrameDelta = 0f;
                float limit = Time.unscaledTime + 12;
                int action = Field<int>(melee, "activeActionId");
                float energyBefore = energy.Amount;
                while (melee.IsAttackInProgress)
                {
                    Check(Time.unscaledTime < limit, label + " 완료 시간 제한");
                    bool parryMotion = melee.IsHeavyParryMotionActive;
                    maxFrameDelta = Mathf.Max(maxFrameDelta, Time.unscaledDeltaTime);
                    bool committed = Field<bool>(melee, "heavyDischargeCommitted");
                    if (expectParry)
                    {
                        Check(Mathf.Abs(savedProgress * Field<AnimationClip>(melee, "activeAttackAnimationClip").length - .138f) < .0001f,
                            label + " 강화 강공 클립의 0.138초부터 시작");
                        Check(Field<MeleeHeavyAttackDefinition>(melee, "activeHeavyDefinition").SafeDischargePhaseIndex == 2,
                            label + " 마지막 내려찍기에서만 방출");
                        if (!committed) Check(Mathf.Approximately(energy.Amount, energyBefore), label + " 회전 두 타 전후에는 에너지 소비 없음");
                    }
                    var nativeState = animator.GetCurrentAnimatorStateInfo(layer);
                    // Animator.Play/CrossFade takes effect at the next native animation update.
                    if (frameCount > 0 && !parryMotion && !animator.IsInTransition(layer) && nativeState.IsName("Melee_Attack") && nativeState.normalizedTime <= 1f)
                    {
                        float runtimeProgress = (float)typeof(MeleeRuntime).GetMethod("GetAttackNormalizedTime", Fields).Invoke(melee, null);
                        float error = Mathf.Abs(runtimeProgress - nativeState.normalizedTime);
                        maxNormalizedError = Mathf.Max(maxNormalizedError, error);
                        Check(error <= Mathf.Max(.04f, Time.deltaTime / Field<float>(melee, "attackDuration") + .025f),
                            label + " 실제 Animator와 판정 진행률 일치 (frame=" + frameCount + ", native=" + nativeState.normalizedTime + ", runtime=" + runtimeProgress + ")");
                    }
                    if (parryMotion)
                    {
                        Check(!committed, label + " 패링/보간 중 착지 금지");
                        maxHoldMovement = Mathf.Max(maxHoldMovement, Vector3.Distance(heldPosition, player.transform.position));
                        Check(maxHoldMovement < .01f, label + " 패링/보간 중 전진 중지");
                        Check(Field<int>(melee, "activeActionId") == action, label + " 같은 행동 ID 유지");
                        Check(animator.updateMode == AnimatorUpdateMode.UnscaledTime, label + " 플레이어 패링 동작 시계");
                        var current = animator.GetCurrentAnimatorStateInfo(layer);
                        var next = animator.GetNextAnimatorStateInfo(layer);
                        if (Field<object>(melee, "heavyParryStage").ToString() == "Parry")
                        {
                            Check(Mathf.Approximately(animator.GetFloat("Melee_ParrySpeed"), .5f), label + " native 패링 반속 재생");
                            Check(Mathf.Abs(Field<float>(melee, "heavyParryDuration") - .532f) < .0001f,
                                label + " 패링 0.266초 전체를 0.532초에 재생");
                        }
                        sawParry |= current.IsName("Melee_HeavyParry") || next.IsName("Melee_HeavyParry");
                        bool transitioning = animator.IsInTransition(layer) && next.IsName("Melee_Attack");
                        nativeTransitionObserved |= transitioning;
                        // A frame longer than 0.1s can finish the native crossfade before the next observation.
                        // The runtime bridge must still hold that resulting heavy pose, with damage closed.
                        bool bridging = Field<object>(melee, "heavyParryStage").ToString() == "Bridge"
                            && (transitioning || current.IsName("Melee_Attack"));
                        if (!sawBridge && bridging) Shot(label + "_bridge");
                        sawBridge |= bridging;
                        if (bridging)
                        {
                            float nativeProgress = transitioning ? next.normalizedTime : current.normalizedTime;
                            Check(Mathf.Abs(nativeProgress - savedProgress) < .025f,
                                label + " 보간 중 강공 준비 자세 고정 (native=" + nativeProgress + ", saved=" + savedProgress + ")");
                        }
                        if (frameCount == 4) Shot(label + "_parry");
                    }
                    if (committed && !priorCommit) { commits++; Shot(label + "_impact"); }
                    sawImpact |= committed; priorCommit = committed; frameCount++;
                    yield return null;
                }
                Check(sawImpact && commits == 1, label + " 착지 확정 한 번");
                Check(expectParry ? sawParry && sawBridge : !sawParry && !sawBridge,
                    label + " 모션 분기 (parry=" + sawParry + ", bridge=" + sawBridge + ", maxFrameDelta=" + maxFrameDelta + ")");
                Check(animator.updateMode == originalMode && Mathf.Approximately(animator.speed, originalSpeed), label + " Animator 시간/속도 복원");
                actions.Add(new { label, action, sawParry, sawBridge, nativeTransitionObserved, commits, frameCount, savedProgress, maxHoldMovement, maxNormalizedError, maxFrameDelta });
                Progress(label + " completed");
            }

            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "일반 강공 입력");
            yield return Complete("ordinary", false, 0, player.transform.position); yield return Wait(.2f);
            foreach (string label in new[] { "immediate", "late", "multi" })
            {
                var enemies = label == "multi" ? new[] { Spawn(Vector3.forward), Spawn(Vector3.back, true) }
                    : new[] { Spawn(Vector3.forward, late: label == "late") };
                yield return null; yield return Threats(enemies);
                var parry = player.GetComponent<PlayerParryController>(); int successes = parry.SuccessCount;
                float hp = actor.Health.CurrentHp;
                if (label == "late")
                {
                    // Move the committed threats away for the first heavy frame, then restore their actual geometry before impact.
                    var positions = enemies.Select(e => e.transform.position).ToArray();
                    var rotations = enemies.Select(e => e.transform.rotation).ToArray();
                    foreach (var e in enemies) e.transform.position += Vector3.right * 20; Physics.SyncTransforms();
                    Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, label + " 강공 시작");
                    yield return Wait(.25f);
                    for (int i = 0; i < enemies.Length; i++) enemies[i].transform.SetPositionAndRotation(positions[i], rotations[i]); Physics.SyncTransforms();
                    yield return null;
                }
                else Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, label + " 강공 시작");
                Check(parry.SuccessCount == successes + 1 && melee.IsHeavyParryMotionActive,
                    label + " 실제 적 공격 패링 성공 (success=" + parry.SuccessCount + ", before=" + successes
                    + ", executing=" + enemies[0].AbilityController.IsExecuting + ", threat="
                    + enemies[0].AbilityController.IsParryThreatTo(player.GetComponent<CombatTarget>()) + ")");
                Check(enemies.All(e => !e.AbilityController.IsExecuting && e.GetComponent<EnemyMovementReaction>().IsParryStunned), label + " 공격 취소·기절 즉시");
                Check(actor.Health.CurrentHp == hp, label + " 패링 피해 방어");
                var position = player.transform.position;
                float progress = Field<float>(melee, "heavyParrySavedProgress");
                if (label == "late") Check(Field<float>(melee, "heavyParryMovementFloor") > .08f, "늦은 성공은 이미 전진한 강공 이동 기록");
                melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
                Check(Mathf.Approximately(progress, Field<float>(melee, "heavyParrySavedProgress")), label + " 중복 통지는 모션 재시작 금지");
                var hits = new List<(int enemy, int phase)>();
                foreach (var enemy in enemies)
                {
                    Action<CombatHealth, DamageInfo, float, bool> handler = (health, info, amount, lethal) =>
                    { if (info.source == actor.gameObject && amount > 0f) hits.Add((health.GetInstanceID(), info.sourceAttackPhaseIndex)); };
                    enemy.Health.OnDamageResolved += handler; damageHooks.Add((enemy.Health, handler));
                }
                yield return Complete(label, true, progress, position);
                foreach (var enemy in enemies)
                {
                    int id = enemy.Health.GetInstanceID();
                    Check(hits.Count(h => h.enemy == id && h.phase == 0) == 1, label + " 실제 적에게 첫 회전 판정 한 번");
                    Check(hits.Count(h => h.enemy == id && h.phase == 1) == 1, label + " 실제 적에게 두 번째 회전 판정 한 번");
                }
                checks.Add(new { label = label + " actual phase hits", passed = true, hits });
                Release(); yield return Wait(1.6f);
            }

            // 공용 대검의 실제 에너지 충전 경로로 5원소·충전량·빛 과충전 속도를 확인한다.
            var elementalWeapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath);
            foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
            foreach (float fraction in element == WeaponElement.Light ? new[] { 0f, .3f, 1f, 2f } : new[] { 0f, .3f, 1f })
            {
                var item = new ItemData(elementalWeapon, 1, ItemGrade.Common, element: element);
                Check(actor.Equipment.EquipWeaponItem(item), element + " 검증 무기 장착");
                energy.Clear();
                for (int i = 0; energy.Amount < energy.BaseMaximum * fraction - .001f && i < 100; i++)
                    Check(energy.RecordConfirmedHit(item.runtimeInstanceId, element, 70000 + i, 1f), element + " 실제 적중 충전");
                float amount = energy.Amount;
                Check(amount >= energy.BaseMaximum * fraction - .001f, element + " 충전 구간 준비");
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, element + " 강공 시작");
                melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
                var position = player.transform.position;
                yield return Complete(element + "_" + fraction.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), true, Field<float>(melee, "heavyParrySavedProgress"), position);
                Check(Mathf.Abs(energy.Amount - Mathf.Min(amount, energy.BaseMaximum) * .5f) < .01f, element + " 에너지 소비/패링 환급 한 번");
                yield return Wait(.25f);
            }

            foreach (string cancel in new[] { "request", "pause", "disable", "weapon-switch" })
            {
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, cancel + " 검증 강공");
                melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
                Check(melee.IsHeavyParryMotionActive, cancel + " 패링 단계 삽입");
                if (cancel == "pause")
                {
                    float before = Field<float>(melee, "heavyParryElapsed");
                    OverburstTimeEffectArbiter.SetPaused(true); yield return Wait(.15f);
                    Check(Mathf.Approximately(before, Field<float>(melee, "heavyParryElapsed")) && animator.speed == 0f, "메뉴 정지는 플레이어 모션/행동 모두 중지");
                    OverburstTimeEffectArbiter.SetPaused(false);
                }
                if (cancel == "disable") { melee.enabled = false; yield return null; melee.enabled = true; }
                else if (cancel == "weapon-switch")
                { actor.Equipment.EquipWeaponItem(new ItemData(AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath), 1, ItemGrade.Common)); yield return null; }
                else melee.CancelCurrentAttackState();
                Check(!melee.IsHeavyParryMotionActive && !melee.IsAttackInProgress, cancel + " 동작 정리");
                Check(animator.updateMode == originalMode && Mathf.Approximately(animator.speed, originalSpeed), cancel + " Animator 복원");
                yield return Wait(.25f);
            }
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "착지 후 통지 강공");
            while (!Field<bool>(melee, "heavyDischargeCommitted")) { Check(melee.IsAttackInProgress, "착지 후 검사 진입"); yield return null; }
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
            Check(!melee.IsHeavyParryMotionActive, "착지 후 패링은 두 번째 강공을 만들지 않음");
            while (melee.IsAttackInProgress) yield return null;
            Check(actor.GetComponent<PlayerLeftHandGrip>().CurrentWeight <= .001f, "강제 왼손 IK 복원 없음");

            yield return Wait(.5f);
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "정지/복귀 검증 강공");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
            OverburstTimeEffectArbiter.SetPaused(true); yield return Wait(.15f);
            OverburstTimeEffectArbiter.SetPaused(false);
            yield return Complete("pause-resume", true, Field<float>(melee, "heavyParrySavedProgress"), player.transform.position);
            yield return Wait(.5f);

            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "피격 중단 검증 강공");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
            float beforeHitHp = actor.Health.CurrentHp;
            actor.Health.TakeDamage(new DamageInfo(1000f, player.transform.position + Vector3.up,
                direction: Vector3.back, suppressDefaultHitVfx: true));
            Check(actor.Health.CurrentHp < beforeHitHp, "패링 모션 전체에 추가 무적 없음");
            Check(!melee.IsHeavyParryMotionActive && !melee.IsAttackInProgress, "실제 일반 피격은 패링 모션과 예약 강공 중단");
            Check(animator.updateMode == originalMode && Mathf.Approximately(animator.speed, originalSpeed), "실제 피격 Animator 시계 복원");
            yield return Wait(1f);

            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "사망 중단 검증 강공");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
            foreach (var arena in UnityEngine.Object.FindObjectsByType<EnemyThemeDebugArena>(FindObjectsSortMode.None))
                actor.Health.SetDamageDeathPrevention(arena, false);
            actor.Health.TakeDamage(new DamageInfo(1e12f, player.transform.position,
                direction: Vector3.back, suppressDefaultHitVfx: true));
            Check(actor.Health.IsDead && !melee.IsHeavyParryMotionActive && !melee.IsAttackInProgress, "실제 사망은 패링/강공 즉시 정리");
            Check(animator.updateMode == originalMode && Mathf.Approximately(animator.speed, originalSpeed), "사망 Animator 시계 복원");
            Progress("Cancellation, damage, death completed");
        }
        finally
        {
            foreach (var hook in damageHooks) if (hook.health != null) hook.health.OnDamageResolved -= hook.handler;
            OverburstTimeEffectArbiter.SetPaused(false);
            melee?.CancelCurrentAttackState();
            foreach (var enemy in leases) if (spawn != null && enemy != null && enemy.IsLeased) spawn.Release(enemy);
            foreach (var fixture in fixtures) if (fixture != null) UnityEngine.Object.Destroy(fixture);
        }
    }
}
