using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProtofactorEnemyRosterPlayModeVerifier
{
    private const string ActiveKey = "ProtofactorEnemyRosterPlayModeVerifier.Active";
    private const string BatchKey = "ProtofactorEnemyRosterPlayModeVerifier.Batch";
    private const string ExitCodeKey = "ProtofactorEnemyRosterPlayModeVerifier.ExitCode";
    private const string PreviousSceneKey = "ProtofactorEnemyRosterPlayModeVerifier.PreviousScene";
    private const string CatalogResourcePath = "Enemies/Protofactor/Catalogs/EC_ProtofactorPilot";
    private const int ReuseCyclesPerSpecies = 20;

    private static readonly string[] ExpectedDefinitionIds =
    {
        "Ceratoferox_Normal",
        "Ceratoferox_Elite",
        "Rapax_Normal",
        "Rapax_Elite",
        "Gobbler_Normal",
        "Gobbler_Elite"
    };

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private static int waitUntilFrame;
    private static GameObject fixtureRoot;
    private static EnemySpawnService spawnService;
    private static EnemyPoolService poolService;

    static ProtofactorEnemyRosterPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Protofactor Enemy Roster PlayMode")]
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
            Debug.Log("[ProtofactorEnemyRosterPlayModeVerifier] 검증을 완료했습니다.");
        else
            Debug.LogError("[ProtofactorEnemyRosterPlayModeVerifier] 검증에 실패했습니다.");
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
        EnemyCatalog catalog = Resources.Load<EnemyCatalog>(CatalogResourcePath);
        Require(catalog != null, "Protofactor EnemyCatalog을 찾지 못했습니다.");
        Require(catalog.Validate(out string catalogMessage), catalogMessage);
        Require(catalog.Count == 7, "Catalog Definition 수가 7이 아닙니다.");
        Require(
            catalog.TryGet("Ursacetus_Boss", out EnemyDefinition boss)
            && boss != null
            && boss.Grade != null
            && boss.Grade.GradeType == EnemyGradeType.Boss,
            "Ursacetus Boss Definition이 Catalog에 없습니다.");

        Dictionary<string, EnemyDefinition> definitions =
            new Dictionary<string, EnemyDefinition>(StringComparer.Ordinal);
        for (int i = 0; i < ExpectedDefinitionIds.Length; i++)
        {
            string id = ExpectedDefinitionIds[i];
            Require(catalog.TryGet(id, out EnemyDefinition definition), id + " Definition이 없습니다.");
            Require(definition != null && definition.IsValid, id + " Definition이 유효하지 않습니다.");
            definitions.Add(id, definition);
        }

        fixtureRoot = new GameObject("ProtofactorEnemyRosterFixture");
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

        Transform hideoutParent = CreateContextObject("HideoutContext").transform;
        Transform dungeonParent = CreateContextObject("DungeonContext").transform;
        Transform hideoutTarget = CreateTarget("HideoutTarget", new Vector3(0f, 0f, 1.5f));
        Transform dungeonTarget = CreateTarget("DungeonTarget", new Vector3(12f, 0f, 1.5f));
        GameObject hideoutOwner = CreateContextObject("HideoutEncounter");
        GameObject dungeonOwner = CreateContextObject("DungeonEncounter");

        VerifySpeciesPair(
            definitions["Ceratoferox_Normal"],
            definitions["Ceratoferox_Elite"],
            hideoutParent,
            dungeonParent,
            hideoutTarget,
            dungeonTarget,
            hideoutOwner,
            dungeonOwner);
        VerifySpeciesPair(
            definitions["Rapax_Normal"],
            definitions["Rapax_Elite"],
            hideoutParent,
            dungeonParent,
            hideoutTarget,
            dungeonTarget,
            hideoutOwner,
            dungeonOwner);
        VerifySpeciesPair(
            definitions["Gobbler_Normal"],
            definitions["Gobbler_Elite"],
            hideoutParent,
            dungeonParent,
            hideoutTarget,
            dungeonTarget,
            hideoutOwner,
            dungeonOwner);

        Require(poolService.LeasedCount == 0, "검증 종료 시 Lease가 남았습니다.");
        Require(poolService.AvailableCount == 3, "종별 Actor 인스턴스가 정확히 3개 재사용되지 않았습니다.");
    }

    private static void VerifySpeciesPair(
        EnemyDefinition normal,
        EnemyDefinition elite,
        Transform hideoutParent,
        Transform dungeonParent,
        Transform hideoutTarget,
        Transform dungeonTarget,
        GameObject hideoutOwner,
        GameObject dungeonOwner)
    {
        Require(normal.Species == elite.Species, normal.EnemyId + " Species 공유가 깨졌습니다.");
        Require(normal.ActorPrefab == elite.ActorPrefab, normal.EnemyId + " ActorPrefab 공유가 깨졌습니다.");

        EnemyActor firstActor = null;
        for (int cycle = 0; cycle < ReuseCyclesPerSpecies; cycle++)
        {
            bool useElite = (cycle & 1) == 1;
            EnemyDefinition definition = useElite ? elite : normal;
            Transform parent = useElite ? dungeonParent : hideoutParent;
            Transform target = useElite ? dungeonTarget : hideoutTarget;
            GameObject owner = useElite ? dungeonOwner : hideoutOwner;
            Vector3 position = new Vector3(cycle * 0.1f, 0f, 0f);
            EnemySpawnRequest request = new EnemySpawnRequest(
                definition,
                position,
                Quaternion.identity,
                target,
                owner,
                target,
                parent,
                1f,
                1f,
                1000 + cycle);

            Require(spawnService.TrySpawn(request, out EnemyActor actor), definition.EnemyId + " 스폰 실패.");
            if (firstActor == null)
                firstActor = actor;
            else
                Require(ReferenceEquals(firstActor, actor), definition.EnemyId + " Actor가 풀에서 재사용되지 않았습니다.");

            VerifyContext(actor, parent, target, owner);
            VerifyGradeAndPresentation(actor, definition, useElite);
            spawnService.Release(actor);
            Require(!actor.gameObject.activeSelf && !actor.IsLeased, definition.EnemyId + " 반환 실패.");
            Require(actor.Definition == null, definition.EnemyId + " 반환 뒤 Definition이 남았습니다.");
            Require(actor.VisualRoot.localScale == Vector3.one, definition.EnemyId + " VisualScale 초기화 실패.");
            Require(actor.CollisionRoot.localScale == Vector3.one, definition.EnemyId + " CollisionScale 초기화 실패.");
            Require(actor.Anchors.localScale == Vector3.one, definition.EnemyId + " AnchorScale 초기화 실패.");
            VerifyTintCleared(actor);
        }
    }

    private static void VerifyContext(
        EnemyActor actor,
        Transform expectedParent,
        Transform expectedTarget,
        GameObject expectedOwner)
    {
        Require(actor.transform.parent == expectedParent, "Spawn Parent Context가 바뀌지 않았습니다.");
        Require(actor.AI.Target == expectedTarget, "Target Context가 바뀌지 않았습니다.");
        Require(actor.AI.SquadEncounterOwner == expectedOwner, "Encounter Owner Context가 바뀌지 않았습니다.");
        Require(actor.AI.SquadEncounterAnchor == expectedTarget, "Encounter Anchor Context가 바뀌지 않았습니다.");
    }

    private static void VerifyGradeAndPresentation(
        EnemyActor actor,
        EnemyDefinition definition,
        bool elite)
    {
        EnemyRank rank = actor.GetComponent<EnemyRank>();
        Require(rank != null, definition.EnemyId + " EnemyRank가 없습니다.");
        Require(
            rank.GradeType == (elite ? EnemyGradeType.Elite : EnemyGradeType.Normal),
            definition.EnemyId + " GradeType 어댑터가 다릅니다.");
        Require(
            rank.Rank == (elite ? EnemyRankType.Elite : EnemyRankType.Normal),
            definition.EnemyId + " legacy HP바 Rank 어댑터가 다릅니다.");

        Vector3 expectedScale = elite ? Vector3.one * 1.15f : Vector3.one;
        Require(actor.VisualRoot.localScale == expectedScale, definition.EnemyId + " VisualScale이 다릅니다.");
        Require(actor.CollisionRoot.localScale == expectedScale, definition.EnemyId + " CollisionScale이 다릅니다.");
        Require(actor.Anchors.localScale == expectedScale, definition.EnemyId + " AnchorScale이 다릅니다.");
        Require(
            Mathf.Approximately(actor.Health.MaxHp, normalisedBaseHealth(definition)),
            definition.EnemyId + " 임시 1.0 능력치 계약이 깨졌습니다.");

        EnemyAbilitySet abilitySet = definition.AbilitySet;
        Require(abilitySet != null && abilitySet.IsValid, definition.EnemyId + " AbilitySet이 유효하지 않습니다.");
        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition ability = abilitySet.GetAbility(i);
            Require(
                ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget
                && ability.RequireLineOfSight,
                definition.EnemyId + " DirectTarget/LOS 계약이 깨졌습니다.");
        }

        VerifyTintApplied(actor, definition.ResolveRuntimeStats().Tint);
    }

    private static float normalisedBaseHealth(EnemyDefinition definition)
    {
        return definition.Species != null ? definition.Species.BaseMaxHealth : 100f;
    }

    private static void VerifyTintApplied(EnemyActor actor, Color tint)
    {
        Renderer[] renderers = actor.VisualRoot.GetComponentsInChildren<Renderer>(true);
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        int checkedSlots = 0;
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer targetRenderer = renderers[rendererIndex];
            Material[] materials = targetRenderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                    continue;

                int colorId;
                if (material.HasProperty(BaseColorId))
                    colorId = BaseColorId;
                else if (material.HasProperty(ColorId))
                    colorId = ColorId;
                else
                    continue;

                targetRenderer.GetPropertyBlock(block, materialIndex);
                Color expected = material.GetColor(colorId) * tint;
                Require(
                    Approximately(block.GetColor(colorId), expected),
                    actor.Definition.EnemyId + " material slot tint가 다릅니다: " + materialIndex);
                block.Clear();
                checkedSlots++;
            }
        }

        Require(checkedSlots > 0, actor.Definition.EnemyId + " tint 검증 가능한 material slot이 없습니다.");
    }

    private static void VerifyTintCleared(EnemyActor actor)
    {
        Renderer[] renderers = actor.VisualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            Require(!renderers[i].HasPropertyBlock(), "풀 반환 뒤 tint PropertyBlock이 남았습니다.");
    }

    private static bool Approximately(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) < 0.002f
            && Mathf.Abs(left.g - right.g) < 0.002f
            && Mathf.Abs(left.b - right.b) < 0.002f
            && Mathf.Abs(left.a - right.a) < 0.002f;
    }

    private static GameObject CreateContextObject(string name)
    {
        GameObject value = new GameObject(name);
        value.transform.SetParent(fixtureRoot.transform, false);
        return value;
    }

    private static Transform CreateTarget(string name, Vector3 position)
    {
        GameObject value = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        value.name = name;
        value.transform.SetParent(fixtureRoot.transform, false);
        value.transform.position = position;
        int playerLayer = LayerMask.NameToLayer("Player");
        Require(playerLayer >= 0, "Player Layer가 없습니다.");
        value.layer = playerLayer;
        CombatHealth health = value.AddComponent<CombatHealth>();
        health.SetMaxHp(10000f, true);
        CombatTarget.EnsureConfigured(value, CombatTeam.PlayerParty);
        return value.transform;
    }

    private static void CompleteSuccessfully()
    {
        Debug.Log(
            "[ProtofactorEnemyRosterPlayModeVerifier] PASS definitions=6 species=3"
            + " normalEliteReuse=60 contextSwitch=60 gradeAdapter=60"
            + " visualCollisionAnchorScale=60 tintAllSlots=60"
            + " placeholderStats1x=60 directTargetLos=1 poolInstances=3");
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
        spawnService = null;
        poolService = null;
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
