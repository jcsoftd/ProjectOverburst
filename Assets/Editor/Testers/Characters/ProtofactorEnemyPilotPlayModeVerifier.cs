using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProtofactorEnemyPilotPlayModeVerifier
{
    private const string ActiveKey = "ProtofactorEnemyPilotPlayModeVerifier.Active";
    private const string BatchKey = "ProtofactorEnemyPilotPlayModeVerifier.Batch";
    private const string ExitCodeKey = "ProtofactorEnemyPilotPlayModeVerifier.ExitCode";
    private const string PreviousSceneKey = "ProtofactorEnemyPilotPlayModeVerifier.PreviousScene";
    private const string CatalogResourcePath =
        "Enemies/Protofactor/Catalogs/EC_ProtofactorPilot";
    private const string CeratoferoxId = "Ceratoferox_Normal";
    private const string RapaxId = "Rapax_Normal";
    private const int ReuseCountPerDefinition = 20;

    private enum VerifyStep
    {
        None,
        Setup,
        CheckInitial,
        CheckAggro,
        WaitForAttackDamage,
        WaitForRangeMiss,
        CheckRelease,
        WaitForReleasedLateHit,
        RunPoolReuse,
        CheckCrossDefinitionIsolation
    }

    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float timeoutAt;
    private static GameObject fixtureRoot;
    private static GameObject encounterOwner;
    private static Transform encounterAnchor;
    private static Transform target;
    private static CombatHealth targetHealth;
    private static CombatHealth bystanderHealth;
    private static EnemySpawnService spawnService;
    private static EnemyPoolService poolService;
    private static EnemyDefinition ceratoferoxDefinition;
    private static EnemyDefinition rapaxDefinition;
    private static EnemyActor ceratoferox;
    private static EnemyActor rapax;
    private static int baselineSquadCount;
    private static int baselineCrowdCount;
    private static float targetHpBeforeAttack;
    private static float bystanderHpBeforeAttack;
    private static float rangeMissHpBeforeAttack;
    private static float releasedLateHitHpBeforeAttack;
    private static int targetDamageEventCount;
    private static int bystanderDamageEventCount;
    private static EnemyActor releasedAttackActor;
    private static bool previousRunInBackground;

    static ProtofactorEnemyPilotPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Protofactor Enemy Pilot PlayMode")]
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
        {
            throw new InvalidOperationException(
                "현재 씬에 저장되지 않은 변경이 있어 빈 검증 씬으로 전환하지 않았습니다.");
        }

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
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            step = VerifyStep.Setup;
            WaitFrames(2);
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Application.runInBackground = previousRunInBackground;
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
            Debug.Log("[ProtofactorEnemyPilotPlayModeVerifier] 검증을 완료했습니다.");
        else
            Debug.LogError("[ProtofactorEnemyPilotPlayModeVerifier] 검증에 실패했습니다.");
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying || Time.frameCount < waitUntilFrame)
            return;

        try
        {
            switch (step)
            {
                case VerifyStep.Setup:
                    SetupFixture();
                    step = VerifyStep.CheckInitial;
                    WaitFrames(2);
                    break;

                case VerifyStep.CheckInitial:
                    VerifyInitialActors();
                    ceratoferox.AI.RequestAggro(target);
                    rapax.AI.RequestAggro(target);
                    Require(
                        ceratoferox.AI.CurrentStateName == "Chase",
                        "Ceratoferox가 RequestAggro 직후 Chase로 전환되지 않았습니다.");
                    Require(
                        rapax.AI.CurrentStateName == "Chase",
                        "Rapax가 RequestAggro 직후 Chase로 전환되지 않았습니다.");
                    step = VerifyStep.CheckAggro;
                    WaitFrames(1);
                    break;

                case VerifyStep.CheckAggro:
                    Require(
                        ceratoferox.AI.IsAggroActive
                        && rapax.AI.IsAggroActive,
                        "신규 2종의 어그로 상태가 유지되지 않았습니다.");
                    PrepareAttackDamageCheck();
                    step = VerifyStep.WaitForAttackDamage;
                    waitUntilFrame = Time.frameCount + 1;
                    timeoutAt = Time.realtimeSinceStartup + 4f;
                    break;

                case VerifyStep.WaitForAttackDamage:
                    if (targetHealth.CurrentHp < targetHpBeforeAttack)
                    {
                        VerifyDirectTargetDamage();
                        PrepareRangeMissCheck();
                        step = VerifyStep.WaitForRangeMiss;
                        waitUntilFrame = Time.frameCount + 1;
                        timeoutAt = Time.realtimeSinceStartup + 4f;
                        break;
                    }

                    if (Time.realtimeSinceStartup >= timeoutAt)
                    {
                        throw new InvalidOperationException(
                            "Ceratoferox DirectTarget 능력이 4초 안에 선택 대상에게 피해를 주지 못했습니다.");
                    }
                    break;

                case VerifyStep.WaitForRangeMiss:
                    if (!rapax.AbilityController.IsExecuting)
                    {
                        Require(
                            Mathf.Approximately(targetHealth.CurrentHp, rangeMissHpBeforeAttack),
                            "Rapax DirectTarget이 타격 전 사거리를 이탈한 대상에게 피해를 주었습니다.");
                        ReleasePilotActors();
                        step = VerifyStep.CheckRelease;
                        WaitFrames(2);
                        break;
                    }

                    if (Time.realtimeSinceStartup >= timeoutAt)
                    {
                        throw new InvalidOperationException(
                            "Rapax 사거리 이탈 DirectTarget 실행이 4초 안에 종료되지 않았습니다.");
                    }
                    break;

                case VerifyStep.CheckRelease:
                    VerifyReleasedState();
                    PrepareReleasedLateHitCheck();
                    step = VerifyStep.WaitForReleasedLateHit;
                    waitUntilFrame = Time.frameCount + 1;
                    timeoutAt = Time.realtimeSinceStartup + 2.5f;
                    break;

                case VerifyStep.WaitForReleasedLateHit:
                    if (Time.realtimeSinceStartup < timeoutAt)
                        break;

                    Require(
                        Mathf.Approximately(
                            targetHealth.CurrentHp,
                            releasedLateHitHpBeforeAttack),
                        "풀 Release 뒤 취소된 DirectTarget 공격이 늦게 피해를 적용했습니다.");
                    VerifyInactive(releasedAttackActor, CeratoferoxId + " LateHit");
                    step = VerifyStep.RunPoolReuse;
                    WaitFrames(1);
                    break;

                case VerifyStep.RunPoolReuse:
                    VerifyRepeatedReuse(ceratoferoxDefinition);
                    VerifyRepeatedReuse(rapaxDefinition);
                    step = VerifyStep.CheckCrossDefinitionIsolation;
                    WaitFrames(1);
                    break;

                case VerifyStep.CheckCrossDefinitionIsolation:
                    VerifyCrossDefinitionIsolation();
                    CompleteSuccessfully();
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            FailAndExit();
        }
    }

    private static void SetupFixture()
    {
        EnemyCatalog catalog = Resources.Load<EnemyCatalog>(CatalogResourcePath);
        Require(catalog != null, "Protofactor Pilot EnemyCatalog을 Resources에서 찾지 못했습니다.");
        Require(catalog.Validate(out string catalogMessage), catalogMessage);
        Require(
            catalog.TryGet(CeratoferoxId, out ceratoferoxDefinition),
            "Catalog에 Ceratoferox_Normal이 없습니다.");
        Require(
            catalog.TryGet(RapaxId, out rapaxDefinition),
            "Catalog에 Rapax_Normal이 없습니다.");

        baselineSquadCount =
            EnemySquadPursuitRuntimeService.GetRuntimeStats().RegisteredAgentCount;
        baselineCrowdCount = EnemyCrowdService.RegisteredCount;

        fixtureRoot = new GameObject("ProtofactorEnemyPilotPlayModeFixture");
        GameObject inactivePool = new GameObject("InactivePool");
        inactivePool.transform.SetParent(fixtureRoot.transform, false);
        inactivePool.SetActive(false);

        GameObject serviceObject = new GameObject("EnemySpawnService");
        serviceObject.transform.SetParent(fixtureRoot.transform, false);
        poolService = serviceObject.AddComponent<EnemyPoolService>();
        poolService.Configure(inactivePool.transform, 0);
        spawnService = serviceObject.AddComponent<EnemySpawnService>();
        spawnService.Configure(catalog, poolService);
        Require(spawnService.Validate(out string serviceMessage), serviceMessage);

        encounterOwner = new GameObject("Encounter");
        encounterOwner.transform.SetParent(fixtureRoot.transform, false);
        GameObject anchorObject = new GameObject("EncounterAnchor");
        anchorObject.transform.SetParent(encounterOwner.transform, false);
        encounterAnchor = anchorObject.transform;

        GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        targetObject.name = "PlayerTarget";
        targetObject.transform.SetParent(fixtureRoot.transform, false);
        targetObject.transform.position = new Vector3(0f, 0f, 1.25f);
        int playerLayer = LayerMask.NameToLayer("Player");
        Require(playerLayer >= 0, "Player Layer가 없어 근접 공격 판정을 검증할 수 없습니다.");
        targetObject.layer = playerLayer;
        targetHealth = targetObject.AddComponent<CombatHealth>();
        DisableDamageNumbers(targetHealth);
        targetHealth.SetMaxHp(5000f, true);
        CombatTarget combatTarget =
            CombatTarget.EnsureConfigured(targetObject, CombatTeam.PlayerParty);
        Require(combatTarget != null && combatTarget.IsAlive, "Player CombatTarget 조립에 실패했습니다.");
        targetHealth.OnDamaged += HandleTargetDamaged;
        target = targetObject.transform;
        encounterAnchor.position = target.position;

        GameObject bystanderObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bystanderObject.name = "PlayerBystander";
        bystanderObject.transform.SetParent(fixtureRoot.transform, false);
        bystanderObject.transform.position = new Vector3(0.4f, 0f, 1.25f);
        bystanderObject.layer = playerLayer;
        bystanderHealth = bystanderObject.AddComponent<CombatHealth>();
        DisableDamageNumbers(bystanderHealth);
        bystanderHealth.SetMaxHp(5000f, true);
        CombatTarget bystanderTarget =
            CombatTarget.EnsureConfigured(bystanderObject, CombatTeam.PlayerParty);
        Require(
            bystanderTarget != null && bystanderTarget.IsAlive,
            "Bystander CombatTarget 조립에 실패했습니다.");
        bystanderHealth.OnDamaged += HandleBystanderDamaged;

        ceratoferox = Spawn(ceratoferoxDefinition, Vector3.zero, null);
        rapax = Spawn(rapaxDefinition, new Vector3(8f, 0f, 0f), null);
    }

    private static EnemyActor Spawn(
        EnemyDefinition definition,
        Vector3 position,
        Transform spawnTarget)
    {
        EnemySpawnRequest request = new EnemySpawnRequest(
            definition,
            position,
            Quaternion.identity,
            spawnTarget,
            encounterOwner,
            encounterAnchor);
        Require(
            spawnService.TrySpawn(request, out EnemyActor actor),
            definition.EnemyId + " 스폰에 실패했습니다.");
        return actor;
    }

    private static void VerifyInitialActors()
    {
        VerifyActor(ceratoferox, ceratoferoxDefinition);
        VerifyActor(rapax, rapaxDefinition);
        VerifyAllPilotAbilitiesUseDirectTarget();
        Require(
            ceratoferox.AI.CurrentStateName == "Roam",
            "Ceratoferox 초기 상태가 Roam이 아닙니다: " + ceratoferox.AI.CurrentStateName);
        Require(
            rapax.AI.CurrentStateName == "Roam",
            "Rapax 초기 상태가 Roam이 아닙니다: " + rapax.AI.CurrentStateName);
        Require(
            ceratoferox.AI.SquadPursuitPreset == ceratoferoxDefinition.SquadPursuitPreset
            && rapax.AI.SquadPursuitPreset == rapaxDefinition.SquadPursuitPreset,
            "Definition의 부대 추격 Preset이 AI에 주입되지 않았습니다.");

        EnemySquadPursuitRuntimeStats stats =
            EnemySquadPursuitRuntimeService.GetRuntimeStats();
        Require(
            stats.RegisteredAgentCount == baselineSquadCount + 2,
            "부대 추격 등록 수 불일치: " + stats.RegisteredAgentCount);
        Require(
            EnemyCrowdService.RegisteredCount == baselineCrowdCount + 2,
            "군집 Agent 등록 수 불일치: " + EnemyCrowdService.RegisteredCount);
    }

    private static void VerifyActor(EnemyActor actor, EnemyDefinition expected)
    {
        Require(actor != null && actor.gameObject.activeInHierarchy, expected.EnemyId + " Actor가 비활성입니다.");
        Require(actor.transform.localScale == Vector3.one, expected.EnemyId + " ActorRoot Scale이 1이 아닙니다.");
        Require(actor.Definition == expected, expected.EnemyId + " Definition 연결이 다릅니다.");
        Require(actor.Identity != null && actor.Identity.Definition == expected, expected.EnemyId + " Identity가 다릅니다.");
        Require(actor.Validate(out string message), message);
        Require(actor.Health != null && !actor.Health.IsDead, expected.EnemyId + " Health가 유효하지 않습니다.");
        Require(actor.Movement != null && actor.Movement.enabled, expected.EnemyId + " Movement가 비활성입니다.");
        Require(actor.AI != null && actor.AI.enabled, expected.EnemyId + " AI가 비활성입니다.");
        Require(
            actor.AbilityController != null
            && actor.AbilityController.enabled
            && actor.AbilityController.AbilitySet == expected.AbilitySet,
            expected.EnemyId + " AbilityController 연결이 유효하지 않습니다.");
        Require(actor.Melee != null && actor.Melee.enabled, expected.EnemyId + " Melee가 비활성입니다.");
        Require(actor.Animator != null && actor.Animator.enabled, expected.EnemyId + " Animator가 비활성입니다.");
        Require(!actor.Animator.applyRootMotion, expected.EnemyId + " Animator Root Motion이 켜져 있습니다.");
        Require(
            actor.Animator.runtimeAnimatorController == expected.AnimationProfile.RuntimeController,
            expected.EnemyId + " Animator Controller가 AnimationProfile과 다릅니다.");
        AnimationClip[] clips = actor.Animator.runtimeAnimatorController.animationClips;
        Require(clips != null && clips.Length > 0, expected.EnemyId + " Animator Clip이 없습니다.");
        for (int i = 0; i < clips.Length; i++)
        {
            string path = clips[i] != null ? AssetDatabase.GetAssetPath(clips[i]) : string.Empty;
            Require(
                !System.IO.Path.GetFileNameWithoutExtension(path)
                    .EndsWith("_RM", StringComparison.OrdinalIgnoreCase),
                expected.EnemyId + " Animator에 Root Motion Clip이 연결되었습니다: " + path);
        }
    }

    private static void PrepareAttackDamageCheck()
    {
        ceratoferox.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        target.position = new Vector3(0f, 0f, 1.25f);
        bystanderHealth.transform.position = new Vector3(0.4f, 0f, 1.25f);
        targetHealth.ResetHealth();
        bystanderHealth.ResetHealth();
        targetDamageEventCount = 0;
        bystanderDamageEventCount = 0;
        targetHpBeforeAttack = targetHealth.CurrentHp;
        bystanderHpBeforeAttack = bystanderHealth.CurrentHp;
        ceratoferox.AI.SetTarget(target);
        ceratoferox.AI.RequestAggro(target);
    }

    private static void VerifyDirectTargetDamage()
    {
        ceratoferox.AI.CancelAttack();
        ceratoferox.AI.SetTarget(null);
        Require(
            targetDamageEventCount == 1,
            "Ceratoferox DirectTarget 한 번 실행의 선택 대상 피해 횟수가 1이 아닙니다: "
            + targetDamageEventCount);
        Require(
            Mathf.Approximately(bystanderHealth.CurrentHp, bystanderHpBeforeAttack)
            && bystanderDamageEventCount == 0,
            "선택 대상 옆 Bystander가 DirectTarget 공격에 함께 맞았습니다.");
    }

    private static void PrepareRangeMissCheck()
    {
        ceratoferox.AI.enabled = false;
        rapax.AI.enabled = false;
        rapax.Movement.CancelActionLock();
        rapax.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        target.position = new Vector3(0f, 0f, 1.25f);
        targetHealth.ResetHealth();
        targetDamageEventCount = 0;
        rangeMissHpBeforeAttack = targetHealth.CurrentHp;
        Require(
            rapax.AbilityController.TryStart(target),
            "Rapax 사거리 이탈 DirectTarget 검증 공격을 시작하지 못했습니다.");
        target.position = new Vector3(0f, 0f, 20f);
    }

    private static void PrepareReleasedLateHitCheck()
    {
        target.position = new Vector3(0f, 0f, 1.25f);
        targetHealth.ResetHealth();
        targetDamageEventCount = 0;
        releasedLateHitHpBeforeAttack = targetHealth.CurrentHp;
        releasedAttackActor = Spawn(ceratoferoxDefinition, Vector3.zero, null);
        releasedAttackActor.AI.enabled = false;
        Require(
            releasedAttackActor.AbilityController.TryStart(target),
            "Release 후 late-hit 검증용 DirectTarget 공격을 시작하지 못했습니다.");
        spawnService.Release(releasedAttackActor);
        VerifyInactive(releasedAttackActor, CeratoferoxId + " ImmediateRelease");
    }

    private static void VerifyAllPilotAbilitiesUseDirectTarget()
    {
        int verifiedCount = 0;
        VerifyDirectAbilitySet(ceratoferoxDefinition.AbilitySet, CeratoferoxId, ref verifiedCount);
        VerifyDirectAbilitySet(rapaxDefinition.AbilitySet, RapaxId, ref verifiedCount);
        Require(
            verifiedCount == 6,
            "Pilot Ability 6개 계약과 실제 개수가 다릅니다: " + verifiedCount);
    }

    private static void VerifyDirectAbilitySet(
        EnemyAbilitySet abilitySet,
        string ownerId,
        ref int verifiedCount)
    {
        Require(abilitySet != null && abilitySet.IsValid, ownerId + " AbilitySet이 유효하지 않습니다.");
        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition ability = abilitySet.GetAbility(i);
            Require(ability != null, ownerId + " Ability[" + i + "]가 없습니다.");
            Require(
                ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget,
                ownerId + " Ability가 DirectTarget이 아닙니다: " + ability.AbilityId);
            Require(
                ability.RequireTargetInRangeUntilHit,
                ownerId + " DirectTarget의 타격 시점 사거리 Gate가 꺼져 있습니다: "
                + ability.AbilityId);
            verifiedCount++;
        }
    }

    private static void ReleasePilotActors()
    {
        spawnService.Release(ceratoferox);
        spawnService.Release(rapax);
    }

    private static void VerifyReleasedState()
    {
        VerifyInactive(ceratoferox, CeratoferoxId);
        VerifyInactive(rapax, RapaxId);
        Require(
            EnemySquadPursuitRuntimeService.GetRuntimeStats().RegisteredAgentCount
                == baselineSquadCount,
            "Release 뒤 부대 추격 Agent가 남았습니다.");
        Require(
            EnemyCrowdService.RegisteredCount == baselineCrowdCount,
            "Release 뒤 군집 Agent가 남았습니다.");
    }

    private static void VerifyRepeatedReuse(EnemyDefinition definition)
    {
        EnemyActor expectedInstance = null;
        for (int i = 0; i < ReuseCountPerDefinition; i++)
        {
            bool injectTransientState = (i & 1) == 0;
            EnemyActor actor = Spawn(
                definition,
                new Vector3(30f + i, 0f, 0f),
                injectTransientState ? target : null);
            if (expectedInstance == null)
                expectedInstance = actor;
            else
                Require(
                    ReferenceEquals(expectedInstance, actor),
                    definition.EnemyId + " 풀이 같은 인스턴스를 재사용하지 않았습니다.");

            Require(actor.Definition == definition, definition.EnemyId + " 재사용 Definition 오염");
            Require(actor.Identity.Definition == definition, definition.EnemyId + " 재사용 Identity 오염");
            Require(actor.AI.CurrentStateName == "Roam", definition.EnemyId + " 재사용 State 누수");
            Require(!actor.AbilityController.IsExecuting, definition.EnemyId + " 재사용 Attack 누수");
            Require(
                actor.AI.Target == (injectTransientState ? target : null),
                definition.EnemyId + " 재사용 Target 초기화 불일치");
            Require(
                Mathf.Approximately(actor.Health.CurrentHp, actor.Health.MaxHp),
                definition.EnemyId + " 재사용 HP가 완전히 복구되지 않았습니다.");

            actor.Health.TakeDamage(
                new DamageInfo(1f, actor.transform.position, target.gameObject));
            Require(
                actor.Health.CurrentHp < actor.Health.MaxHp,
                definition.EnemyId + " 풀 재사용 전 HP 오염 Fixture 적용 실패");
            if (injectTransientState)
                actor.AbilityController.TryStart(target);

            spawnService.Release(actor);
            VerifyInactive(actor, definition.EnemyId);
        }
    }

    private static void VerifyCrossDefinitionIsolation()
    {
        EnemyActor cerato = Spawn(ceratoferoxDefinition, new Vector3(40f, 0f, 0f), null);
        EnemyActor rapaxActor = Spawn(rapaxDefinition, new Vector3(44f, 0f, 0f), null);
        Require(!ReferenceEquals(cerato, rapaxActor), "서로 다른 Definition이 같은 풀 인스턴스를 공유했습니다.");
        Require(cerato.Definition == ceratoferoxDefinition, "Ceratoferox 풀 Key가 Rapax에 오염됐습니다.");
        Require(rapaxActor.Definition == rapaxDefinition, "Rapax 풀 Key가 Ceratoferox에 오염됐습니다.");
        Require(
            cerato.name.Contains("Ceratoferox", StringComparison.OrdinalIgnoreCase),
            "Ceratoferox Definition이 다른 Actor Prefab을 대여했습니다.");
        Require(
            rapaxActor.name.Contains("Rapax", StringComparison.OrdinalIgnoreCase),
            "Rapax Definition이 다른 Actor Prefab을 대여했습니다.");
        spawnService.Release(cerato);
        spawnService.Release(rapaxActor);
        Require(poolService.LeasedCount == 0, "완료 시점에 반환되지 않은 Actor가 있습니다.");
        Require(poolService.AvailableCount >= 2, "2종 풀의 사용 가능 인스턴스가 분리 보관되지 않았습니다.");
    }

    private static void VerifyInactive(EnemyActor actor, string label)
    {
        Require(actor != null && !actor.gameObject.activeSelf, label + " Actor가 Release 뒤 활성입니다.");
        Require(!actor.IsLeased, label + " Actor의 Lease 표시가 남았습니다.");
        Require(actor.Definition == null, label + " Actor의 Definition이 Release 뒤 남았습니다.");
        Require(actor.Identity != null && actor.Identity.Definition == null, label + " Identity가 Release 뒤 남았습니다.");
        Require(actor.AI.Target == null, label + " AI Target이 Release 뒤 남았습니다.");
        Require(!actor.AbilityController.IsExecuting, label + " 공격 Coroutine이 Release 뒤 남았습니다.");
    }

    private static void CompleteSuccessfully()
    {
        Debug.Log(
            "[ProtofactorEnemyPilotPlayModeVerifier] PASS"
            + " definitions=2 directAbilities=6 spawn=2 nonRM=1 roamToChase=2"
            + " selectedTargetDamageOnce=1 bystanderUnaffected=1 rangeMiss=1 lateHitAfterRelease=0"
            + " reusePerDefinition=" + ReuseCountPerDefinition
            + " poolIsolation=1 squadUnregister=1 crowdUnregister=1");
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
        if (targetHealth != null)
            targetHealth.OnDamaged -= HandleTargetDamaged;
        if (bystanderHealth != null)
            bystanderHealth.OnDamaged -= HandleBystanderDamaged;
        if (fixtureRoot != null)
            UnityEngine.Object.Destroy(fixtureRoot);
        fixtureRoot = null;
        ceratoferox = null;
        rapax = null;
        spawnService = null;
        poolService = null;
        target = null;
        targetHealth = null;
        bystanderHealth = null;
        releasedAttackActor = null;
        encounterOwner = null;
        encounterAnchor = null;
    }

    private static void HandleTargetDamaged(CombatHealth source, DamageInfo info)
    {
        targetDamageEventCount++;
    }

    private static void HandleBystanderDamaged(CombatHealth source, DamageInfo info)
    {
        bystanderDamageEventCount++;
    }

    private static void DisableDamageNumbers(CombatHealth health)
    {
        SerializedObject serializedHealth = new SerializedObject(health);
        SerializedProperty property = serializedHealth.FindProperty("showDamageNumbers");
        if (property == null)
            return;

        property.boolValue = false;
        serializedHealth.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WaitFrames(int frames)
    {
        waitUntilFrame = Time.frameCount + Mathf.Max(1, frames);
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
