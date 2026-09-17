using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProtofactorEnemyAbilityPatternPlayModeVerifier
{
    private const string ActiveKey = "ProtofactorEnemyAbilityPatternPlayModeVerifier.Active";
    private const string BatchKey = "ProtofactorEnemyAbilityPatternPlayModeVerifier.Batch";
    private const string ExitCodeKey = "ProtofactorEnemyAbilityPatternPlayModeVerifier.ExitCode";
    private const string PreviousSceneKey = "ProtofactorEnemyAbilityPatternPlayModeVerifier.PreviousScene";

    private static int waitUntilFrame;
    private static GameObject fixtureRoot;
    private static EnemyAbilityController controller;
    private static ProtofactorAbilityPatternTestExecutor executor;

    static ProtofactorEnemyAbilityPatternPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Enemy Ability Patterns PlayMode")]
    public static void RunFromMenu()
    {
        Begin(false);
    }

    public static void RunOnceFromCommandLine()
    {
        Begin(true);
    }

    private static void Begin(bool batchMode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("PlayMode가 이미 실행 중이거나 전환 중입니다.");

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isDirty)
            throw new InvalidOperationException("저장되지 않은 현재 씬이 있어 빈 검증 씬으로 전환하지 않습니다.");

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        SessionState.SetString(
            PreviousSceneKey,
            activeScene.IsValid() ? activeScene.path : string.Empty);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            waitUntilFrame = Time.frameCount + 2;
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
            return;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= UpdateVerification;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        bool batchMode = SessionState.GetBool(BatchKey, false);
        string previousScene = SessionState.GetString(PreviousSceneKey, string.Empty);
        ClearSessionState();

        if (!batchMode
            && !string.IsNullOrWhiteSpace(previousScene)
            && System.IO.File.Exists(previousScene))
        {
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
        }

        if (batchMode)
            EditorApplication.Exit(exitCode);
        else if (exitCode == 0)
            Debug.Log("[ProtofactorEnemyAbilityPatternPlayModeVerifier] 검증을 완료했습니다.");
        else
            Debug.LogError("[ProtofactorEnemyAbilityPatternPlayModeVerifier] 검증에 실패했습니다.");
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying || Time.frameCount < waitUntilFrame)
            return;

        try
        {
            RunVerification();
            CompleteSuccessfully();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            FailAndExit();
        }
    }

    private static void RunVerification()
    {
        fixtureRoot = new GameObject("EnemyAbilityPatternFixture");
        CombatHealth health = fixtureRoot.AddComponent<CombatHealth>();
        health.SetMaxHp(100f, true);
        fixtureRoot.AddComponent<EnemyMeleeAttackController>();
        executor = fixtureRoot.AddComponent<ProtofactorAbilityPatternTestExecutor>();
        controller = fixtureRoot.AddComponent<EnemyAbilityController>();

        GameObject targetObject = new GameObject("AbilityTarget");
        targetObject.transform.SetParent(fixtureRoot.transform, false);
        targetObject.transform.position = Vector3.forward;
        Transform target = targetObject.transform;

        EnemyAbilityDefinition high = CreateAbility(
            "HighPriority",
            EnemyAbilityExecutionMode.DirectTarget,
            10f,
            10f,
            5);
        EnemyAbilityDefinition fallback = CreateAbility(
            "Fallback",
            EnemyAbilityExecutionMode.DirectTarget,
            10f,
            10f,
            0);
        EnemyAbilitySet cooldownSet = CreateSet("CooldownSet", high, fallback);
        controller.Configure(cooldownSet, 1f, 1f);

        Require(controller.TryStart(target), "최고 우선순위 Ability를 시작하지 못했습니다.");
        Require(executor.LastAbility == high, "최고 우선순위 Ability가 선택되지 않았습니다.");
        Require(!controller.IsCooldownReady(high), "시작한 Ability의 개별 쿨다운이 기록되지 않았습니다.");
        Require(controller.IsCooldownReady(fallback), "사용하지 않은 Ability 쿨다운까지 잠겼습니다.");

        Require(controller.TryStart(target), "쿨다운 중인 Ability를 건너뛰지 못했습니다.");
        Require(executor.LastAbility == fallback, "개별 쿨다운 뒤 Fallback Ability가 선택되지 않았습니다.");
        Require(!controller.TryStart(target), "모든 개별 쿨다운 중인데 Ability가 시작됐습니다.");

        controller.ResetForReuse();
        Require(controller.IsCooldownReady(high) && controller.IsCooldownReady(fallback), "풀 Reset에서 개별 쿨다운이 지워지지 않았습니다.");
        Require(controller.LastCommittedAbility == null, "풀 Reset에서 마지막 Ability가 지워지지 않았습니다.");

        EnemyAbilityDefinition emergency = CreateAbility(
            "Emergency",
            EnemyAbilityExecutionMode.DirectTarget,
            10f,
            0f,
            20);
        emergency.ConfigureUsePolicy(0f, 20, 0f, 0.5f);
        EnemyAbilityDefinition normal = CreateAbility(
            "Normal",
            EnemyAbilityExecutionMode.DirectTarget,
            10f,
            0f,
            0);
        EnemyAbilitySet healthSet = CreateSet("HealthSet", emergency, normal);
        controller.Configure(healthSet, 1f, 1f);
        Require(controller.TryStart(target), "Full HP 조건 Ability를 시작하지 못했습니다.");
        Require(executor.LastAbility == normal, "HP 100%에서 Emergency Ability가 선택됐습니다.");

        controller.Configure(healthSet, 1f, 1f);
        health.TakeDamage(new DamageInfo(60f, fixtureRoot.transform.position, targetObject));
        Require(health.NormalizedHp <= 0.5f, "HP 조건 Fixture를 만들지 못했습니다.");
        Require(controller.TryStart(target), "저체력 Emergency Ability를 시작하지 못했습니다.");
        Require(executor.LastAbility == emergency, "저체력 최고 우선순위 Ability가 선택되지 않았습니다.");

        EnemyAbilityDefinition close = CreateAbility(
            "Close",
            EnemyAbilityExecutionMode.DirectTarget,
            1.5f,
            0f,
            10);
        EnemyAbilityDefinition ranged = CreateAbility(
            "RangedBand",
            EnemyAbilityExecutionMode.DirectTarget,
            4f,
            0f,
            10);
        ranged.ConfigureUsePolicy(2f, 10);
        EnemyAbilitySet rangeSet = CreateSet("RangeSet", close, ranged);
        controller.Configure(rangeSet, 1f, 1f);
        target.position = Vector3.forward * 2.5f;
        Require(controller.TryStart(target), "거리 Band Ability를 시작하지 못했습니다.");
        Require(executor.LastAbility == ranged, "MinimumRange 조건이 적용되지 않았습니다.");

        EnemyAbilityDefinition alternateA = CreateAbility(
            "AlternateA",
            EnemyAbilityExecutionMode.DirectTarget,
            4f,
            0f,
            0);
        EnemyAbilityDefinition alternateB = CreateAbility(
            "AlternateB",
            EnemyAbilityExecutionMode.DirectTarget,
            4f,
            0f,
            0);
        EnemyAbilitySet alternateSet = CreateSet("AlternateSet", alternateA, alternateB);
        controller.Configure(alternateSet, 1f, 1f);
        Require(controller.TryStart(target), "첫 교대 Ability를 시작하지 못했습니다.");
        EnemyAbilityDefinition first = executor.LastAbility;
        Require(controller.TryStart(target), "두 번째 교대 Ability를 시작하지 못했습니다.");
        Require(executor.LastAbility != first, "동일 우선순위에서 같은 Ability가 연속 선택됐습니다.");

        EnemyAbilityDefinition unsupported = CreateAbility(
            "UnsupportedProjectile",
            EnemyAbilityExecutionMode.Projectile,
            10f,
            0f,
            100);
        controller.Configure(CreateSet("UnsupportedSet", unsupported), 1f, 1f);
        Require(!controller.TryStart(target), "지원 Executor가 없는 Ability가 실행됐습니다.");
        Require(controller.LastCommittedAbility == null, "실패한 실행이 Ability를 Commit했습니다.");
    }

    private static EnemyAbilityDefinition CreateAbility(
        string id,
        EnemyAbilityExecutionMode mode,
        float range,
        float cooldown,
        int priority)
    {
        EnemyAbilityDefinition ability =
            ScriptableObject.CreateInstance<EnemyAbilityDefinition>();
        ability.name = id;
        ability.Configure(
            id,
            "Attack1",
            10f,
            range,
            0.8f,
            120f,
            cooldown,
            0f,
            0.45f,
            0f,
            1f,
            true,
            mode,
            0.75f,
            false,
            0.1f);
        ability.ConfigureUsePolicy(0f, priority);
        return ability;
    }

    private static EnemyAbilitySet CreateSet(
        string id,
        params EnemyAbilityDefinition[] abilities)
    {
        EnemyAbilitySet set = ScriptableObject.CreateInstance<EnemyAbilitySet>();
        set.name = id;
        set.Configure(id, abilities);
        return set;
    }

    private static void CompleteSuccessfully()
    {
        Debug.Log(
            "[ProtofactorEnemyAbilityPatternPlayModeVerifier] PASS"
            + " executorRouting=1 perAbilityCooldown=1 cooldownReset=1"
            + " priority=1 selfHpCondition=1 distanceBand=1"
            + " avoidImmediateRepeat=1 unsupportedNoCommit=1");
        SessionState.SetInt(ExitCodeKey, 0);
        CleanupFixture();
        EditorApplication.update -= UpdateVerification;
        EditorApplication.ExitPlaymode();
    }

    private static void FailAndExit()
    {
        SessionState.SetInt(ExitCodeKey, 1);
        CleanupFixture();
        EditorApplication.update -= UpdateVerification;
        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();
    }

    private static void CleanupFixture()
    {
        if (fixtureRoot != null)
            UnityEngine.Object.Destroy(fixtureRoot);
        fixtureRoot = null;
        controller = null;
        executor = null;
    }

    private static void ClearSessionState()
    {
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(BatchKey);
        SessionState.EraseInt(ExitCodeKey);
        SessionState.EraseString(PreviousSceneKey);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

public sealed class ProtofactorAbilityPatternTestExecutor : EnemyAbilityExecutor
{
    public EnemyAbilityDefinition LastAbility { get; private set; }
    public override bool IsExecuting => false;

    public override bool Supports(EnemyAbilityDefinition ability)
    {
        return ability != null
            && ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget;
    }

    public override bool CanStart(
        EnemyAbilityDefinition ability,
        Transform target)
    {
        return Supports(ability) && target != null;
    }

    public override bool TryStart(
        EnemyAbilityDefinition ability,
        int abilityIndex,
        Transform target)
    {
        if (!CanStart(ability, target))
            return false;
        LastAbility = ability;
        return true;
    }

    public override float ResolveCooldown(float baseCooldown)
    {
        return Mathf.Max(0f, baseCooldown);
    }

    public override void Cancel()
    {
    }

    public override void ResetForReuse()
    {
        LastAbility = null;
    }
}
