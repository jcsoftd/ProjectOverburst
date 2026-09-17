using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProtofactorEnemyGroundingPlayModeVerifier
{
    private const string ActiveKey = "ProtofactorEnemyGroundingPlayModeVerifier.Active";
    private const string BatchKey = "ProtofactorEnemyGroundingPlayModeVerifier.Batch";
    private const string ExitCodeKey = "ProtofactorEnemyGroundingPlayModeVerifier.ExitCode";
    private const string PreviousSceneKey = "ProtofactorEnemyGroundingPlayModeVerifier.PreviousScene";
    private const string CatalogResourcePath = "Enemies/Protofactor/Catalogs/EC_ProtofactorPilot";
    private const float MaximumVisualGroundError = 0.035f;
    private const float MaximumColliderGroundError = 0.015f;
    private const float SettlePhysicsSeconds = 3f;

    private static readonly List<EnemyActor> Actors = new List<EnemyActor>();

    private static GameObject fixtureRoot;
    private static EnemySpawnService spawnService;
    private static float verifyAtFixedTime;

    static ProtofactorEnemyGroundingPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Protofactor Enemy Grounding PlayMode")]
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
            throw new InvalidOperationException("현재 씬에 저장되지 않은 변경이 있어 검증 씬으로 전환하지 않습니다.");

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
            try
            {
                SetupFixture();
                verifyAtFixedTime = Time.fixedTime + SettlePhysicsSeconds;
                EditorApplication.update -= UpdateVerification;
                EditorApplication.update += UpdateVerification;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                FailAndExit();
            }

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
    }

    private static void SetupFixture()
    {
        EnemyCatalog catalog = Resources.Load<EnemyCatalog>(CatalogResourcePath);
        Require(catalog != null, "Protofactor EnemyCatalog을 찾지 못했습니다.");
        Require(catalog.Validate(out string catalogMessage), catalogMessage);
        Require(catalog.Count == 7, "접지 검증 대상 Definition 수가 7개가 아닙니다.");

        fixtureRoot = new GameObject("ProtofactorEnemyGroundingFixture");

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Ground";
        floor.transform.SetParent(fixtureRoot.transform, false);
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(110f, 1f, 16f);

        GameObject inactivePool = new GameObject("InactivePool");
        inactivePool.transform.SetParent(fixtureRoot.transform, false);
        inactivePool.SetActive(false);

        GameObject services = new GameObject("EnemySpawnService");
        services.transform.SetParent(fixtureRoot.transform, false);
        EnemyPoolService poolService = services.AddComponent<EnemyPoolService>();
        poolService.Configure(inactivePool.transform, 0);
        spawnService = services.AddComponent<EnemySpawnService>();
        spawnService.Configure(catalog, poolService);
        Require(spawnService.Validate(out string serviceMessage), serviceMessage);

        Actors.Clear();
        for (int i = 0; i < catalog.Count; i++)
        {
            EnemyDefinition definition = catalog.GetDefinition(i);
            Require(definition != null, "접지 검증 Definition이 비어 있습니다: " + i);

            Vector3 spawnPosition = new Vector3(-42f + i * 14f, 0.35f, 0f);
            EnemySpawnRequest request = new EnemySpawnRequest(
                definition,
                spawnPosition,
                Quaternion.identity,
                null,
                null,
                null,
                fixtureRoot.transform,
                1f,
                1f,
                9000 + i);

            Require(
                spawnService.TrySpawn(request, out EnemyActor actor),
                definition.EnemyId + " 접지 검증 스폰에 실패했습니다.");
            actor.AI.enabled = false;
            actor.Movement.StopMovement();
            Actors.Add(actor);
        }
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying || Time.fixedTime < verifyAtFixedTime)
            return;

        try
        {
            Physics.SyncTransforms();
            float maximumVisualError = 0f;
            float maximumColliderError = 0f;

            for (int i = 0; i < Actors.Count; i++)
            {
                EnemyActor actor = Actors[i];
                Require(actor != null && actor.gameObject.activeInHierarchy, "접지 검증 Actor가 유실되었습니다.");

                float visualBottom = CalculateBakedWorldBottom(actor.VisualRoot);
                float colliderBottom = CalculateColliderWorldBottom(actor.CollisionRoot);
                float visualError = Mathf.Abs(visualBottom);
                float colliderError = Mathf.Abs(colliderBottom);
                Rigidbody body = actor.GetComponent<Rigidbody>();
                maximumVisualError = Mathf.Max(maximumVisualError, visualError);
                maximumColliderError = Mathf.Max(maximumColliderError, colliderError);

                Debug.Log(
                    $"[ProtofactorEnemyGroundingPlayModeVerifier] {actor.Definition.EnemyId}"
                    + $" visualBottom={visualBottom:F4} colliderBottom={colliderBottom:F4}"
                    + $" rootY={actor.transform.position.y:F4}"
                    + $" velocityY={(body != null ? body.linearVelocity.y : float.NaN):F4}"
                    + $" sleeping={(body != null && body.IsSleeping())}");

                Require(
                    visualError <= MaximumVisualGroundError,
                    actor.Definition.EnemyId + $" 실제 메시가 바닥에서 벗어났습니다: {visualBottom:F4}m");
                Require(
                    colliderError <= MaximumColliderGroundError,
                    actor.Definition.EnemyId + $" Collider가 바닥에서 벗어났습니다: {colliderBottom:F4}m");
            }

            Debug.Log(
                "[ProtofactorEnemyGroundingPlayModeVerifier] PASS"
                + $" definitions={Actors.Count}"
                + $" maxVisualGroundError={maximumVisualError:F4}"
                + $" maxColliderGroundError={maximumColliderError:F4}"
                + $" settlePhysicsSeconds={SettlePhysicsSeconds:F1}");
            SessionState.SetInt(ExitCodeKey, 0);
            CleanupFixture();
            EditorApplication.update -= UpdateVerification;
            EditorApplication.ExitPlaymode();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            FailAndExit();
        }
    }

    private static float CalculateBakedWorldBottom(Transform visualRoot)
    {
        Require(visualRoot != null, "VisualRoot가 없습니다.");
        float minimumY = float.PositiveInfinity;
        SkinnedMeshRenderer[] skinned =
            visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        for (int rendererIndex = 0; rendererIndex < skinned.Length; rendererIndex++)
        {
            SkinnedMeshRenderer renderer = skinned[rendererIndex];
            if (renderer.sharedMesh == null)
                continue;

            Mesh baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, false);
                Vector3[] vertices = baked.vertices;
                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    float worldY = renderer.transform.TransformPoint(vertices[vertexIndex]).y;
                    minimumY = Mathf.Min(minimumY, worldY);
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(baked);
            }
        }

        MeshFilter[] filters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
        {
            MeshFilter filter = filters[filterIndex];
            if (filter.sharedMesh == null)
                continue;

            Vector3[] vertices = filter.sharedMesh.vertices;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                float worldY = filter.transform.TransformPoint(vertices[vertexIndex]).y;
                minimumY = Mathf.Min(minimumY, worldY);
            }
        }

        Require(!float.IsPositiveInfinity(minimumY), "실제 메시 정점을 찾지 못했습니다.");
        return minimumY;
    }

    private static float CalculateColliderWorldBottom(Transform collisionRoot)
    {
        Require(collisionRoot != null, "CollisionRoot가 없습니다.");
        Collider[] colliders = collisionRoot.GetComponentsInChildren<Collider>(true);
        Require(colliders.Length > 0, "접지 검증 Collider가 없습니다.");

        float minimumY = float.PositiveInfinity;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i].enabled && !colliders[i].isTrigger)
                minimumY = Mathf.Min(minimumY, colliders[i].bounds.min.y);
        }

        Require(!float.IsPositiveInfinity(minimumY), "활성 Body Collider가 없습니다.");
        return minimumY;
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
        Actors.Clear();
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
