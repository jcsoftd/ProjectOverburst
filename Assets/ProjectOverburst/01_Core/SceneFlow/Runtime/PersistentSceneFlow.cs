using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PersistentSceneFlow : MonoBehaviour // 씬 전환 허브
{
    public const string PersistentSceneName = "PersistentScene"; // 상주 씬
    public const string HideoutSceneName = "HideoutScene"; // 전투 테스트 씬
    public const string DefaultHubSceneName = HideoutSceneName; // 기본 허브
    public const string DungeonRunSceneName = "DungeonRunScene"; // 절차형 던전 씬
    private const float DungeonRunCameraYaw = 135f; // 던전 기본 시점
    private const int PlayerReadyWaitFrames = 180; // 플레이어 준비 대기
    private const float HubGroundRayHeight = 30f; // 허브 지면 탐색 높이
    private const float HubGroundRayDistance = 100f; // 허브 지면 탐색 거리
    private const float HubGroundOffset = 0.12f; // 캐릭터 지면 여유

    private static PersistentSceneFlow instance; // 단일 인스턴스

    private string currentSubSceneName; // 현재 SubScene
    private bool isSwitching; // 전환 중
    private Coroutine switchRoutine; // 전환 루틴
    private LoadingScreenUI loadingScreen; // 로딩 UI
    private RunSceneReturnContext pendingDungeonFailureReturnContext; // 던전 생성 실패 복귀

    public static PersistentSceneFlow Instance
    {
        get { return instance; }
    }

    public string CurrentSubSceneName
    {
        get { return currentSubSceneName; }
    }

    public bool IsSwitching
    {
        get { return isSwitching; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapAfterSceneLoad()
    {
        if (SceneManager.GetActiveScene().name != PersistentSceneName) // Persistent 한정
            return;

        EnsureInstance();
    }

    public static PersistentSceneFlow EnsureInstance()
    {
        if (instance != null)
            return instance;

        PersistentSceneFlow existing = FindFirstObjectByType<PersistentSceneFlow>(); // 씬 배치 우선
        if (existing != null)
        {
            instance = existing;
            return existing;
        }

        GameObject controllerObject = new GameObject("PersistentSceneFlow"); // 대체 경로
        return controllerObject.AddComponent<PersistentSceneFlow>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject); // 중복 제거
            return;
        }

        instance = this; // singleton 등록
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name != PersistentSceneName) // Persistent 한정
            return;

        ResolveLoadingScreen(); // 로딩 UI
        if (loadingScreen != null)
            loadingScreen.ForceHide(); // 시작 숨김

        StartCoroutine(EnsureInitialSubScene()); // 초기 SubScene
    }

    public void EnterDungeon(DungeonRunEntryRequest request)
    {
        DungeonRunEntryRequest resolvedRequest = request
            ?? DungeonRunEntryRequest.CreateRandom(GetSourceSceneName());
        DungeonRunLaunchContextHolder.Set(resolvedRequest);

        string sourceSceneName =
            IsHubSceneName(resolvedRequest.SourceSceneName)
                ? resolvedRequest.SourceSceneName
                : DefaultHubSceneName;
        pendingDungeonFailureReturnContext =
            RunSceneReturnContext.CreateHubTransfer(
                sourceSceneName,
                resolvedRequest.ReturnPointId);
        StartSubSceneSwitch(DungeonRunSceneName, null);
    }

    public void ReturnToHub(RunSceneReturnContext context)
    {
        RunSceneReturnContext resolvedContext = context
            ?? RunSceneReturnContext.CreateHubTransfer(
                DefaultHubSceneName,
                "Default"); // 기본 복귀

        if (!IsHubSceneName(resolvedContext.TargetSceneName))
        {
            Debug.LogError("[SceneFlow] Unsupported hub scene: " + resolvedContext.TargetSceneName);
            return;
        }

        StartSubSceneSwitch(resolvedContext.TargetSceneName, resolvedContext);
    }

    public void SwitchHubScene(string targetSceneName, string returnPointId = "Default")
    {
        if (!IsHubSceneName(targetSceneName))
        {
            Debug.LogError("[SceneFlow] Unsupported hub scene: " + targetSceneName);
            return;
        }

        ReturnToHub(
            RunSceneReturnContext.CreateHubTransfer(
                targetSceneName,
                returnPointId));
    }

    private IEnumerator EnsureInitialSubScene()
    {
        currentSubSceneName = FindLoadedSubSceneName(); // 기존 로드 확인
        if (!string.IsNullOrEmpty(currentSubSceneName))
        {
            ActivateSubScene(currentSubSceneName); // 활성 씬
            if (IsHubSceneName(currentSubSceneName))
            {
                yield return PlacePlayerAtHubSpawn("Default"); // 플레이어 초기 배치
                SpawnConfiguredSceneItems(); // 최종 위치 기준 아이템 배치
                WorldMinimapController.ShowHubMinimap(FindPlayer()); // 허브 미니맵
            }

            yield break;
        }

        yield return LoadSubScene(DefaultHubSceneName);
        currentSubSceneName = DefaultHubSceneName; // 기본 허브
        ActivateSubScene(currentSubSceneName); // 활성 씬
        yield return PlacePlayerAtHubSpawn("Default"); // 플레이어 초기 배치
        SpawnConfiguredSceneItems(); // 최종 위치 기준 아이템 배치
        WorldMinimapController.ShowHubMinimap(FindPlayer()); // 허브 미니맵
    }

    private void StartSubSceneSwitch(
        string newSceneName,
        RunSceneReturnContext returnContext)
    {
        if (isSwitching)
        {
            Debug.LogWarning("[SceneFlow] SubScene switch already running. Requested=" + newSceneName);
            return;
        }

        switchRoutine = StartCoroutine(SwitchSubScene(newSceneName, returnContext)); // 전환 시작
    }

    private IEnumerator SwitchSubScene(
        string newSceneName,
        RunSceneReturnContext returnContext)
    {
        isSwitching = true; // 전환 잠금
        RunSceneReturnContext deferredRecoveryContext = null; // 생성 실패 후 재전환
        ClosePersistentUiForSceneSwitch(); // UI 정리
        ResolveLoadingScreen(); // 로딩 UI

        bool isReturnToHub = IsHubSceneName(newSceneName) && returnContext != null; // 허브 복귀 여부
        if (loadingScreen != null)
        {
            if (isReturnToHub)
                loadingScreen.Show("RETURNING", "현재 지역 연결 해제 중...");
            else if (IsRunSceneName(newSceneName))
                loadingScreen.Show("SIGNAL LINKING", "허브 연결 해제 중...");
            else
                loadingScreen.Show("LOADING", "씬 전환 준비 중...");

            loadingScreen.SetProgress(0.1f); // 시작 진행
        }

        string previousSceneName = string.IsNullOrEmpty(currentSubSceneName) // 이전 씬
            ? FindLoadedSubSceneName()
            : currentSubSceneName;

        if (IsRunSceneName(previousSceneName) || IsHubSceneName(newSceneName))
        {
            if (loadingScreen != null && isReturnToHub)
            {
                loadingScreen.SetStatus("현재 지역 연결 해제 중...");
                loadingScreen.SetProgress(0.25f); // 언로드 진행
            }

            RunWalkableContext.Clear(); // 런 grid 해제
            RuntimeUIDiagnostics.DestroyRunTransientUiRoots(); // 런 UI 정리
        }

        if (!string.IsNullOrEmpty(previousSceneName)
            && previousSceneName != newSceneName
            && IsSceneLoaded(previousSceneName))
        {
            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(previousSceneName);
            if (unloadOperation != null)
                yield return unloadOperation; // SubScene 언로드
        }

        if (IsRunSceneName(previousSceneName)
            && previousSceneName != newSceneName)
        {
            RunSceneReadinessRegistry.Clear(previousSceneName);
        }

        if (loadingScreen != null)
        {
            loadingScreen.SetStatus(newSceneName + " 로드 중...");
            loadingScreen.SetProgress(0.35f); // 로드 진행
        }

        if (!IsSceneLoaded(newSceneName))
            yield return LoadSubScene(newSceneName); // Additive 로드

        if (loadingScreen != null)
        {
            loadingScreen.SetStatus(
                newSceneName == DungeonRunSceneName
                    ? "던전 생성 중..."
                    : "허브 위치 정리 중...");
            loadingScreen.SetProgress(0.65f); // 생성 대기
        }

        currentSubSceneName = newSceneName; // 현재 SubScene
        ActivateSubScene(newSceneName); // 활성 씬

        if (IsHubSceneName(newSceneName) && returnContext != null)
        {
            bool returnComplete = false; // 복귀 완료
            RunSceneReturnProcessor.Begin(
                returnContext,
                () => returnComplete = true); // Player 복귀
            while (!returnComplete)
                yield return null;

            yield return PlacePlayerAtHubSpawn(returnContext.ReturnPointId); // 플레이어 복귀 배치
            SpawnConfiguredSceneItems(); // 최종 위치 기준 아이템 배치

            if (returnContext.IsExtractSuccess)
                MerchantStockRefreshService.HandleSuccessfulRunReturn(); // 상점 재고 갱신 판정

            if (loadingScreen != null)
                loadingScreen.SetProgress(1f); // 완료

            WorldMinimapController.ShowHubMinimap(FindPlayer()); // 허브 미니맵
        }
        else if (newSceneName == DungeonRunSceneName)
        {
            WorldMinimapController.Instance?.ForceHide(); // 던전 미니맵 구현 전 Hub 표시 차단
            float waitStartTime = Time.realtimeSinceStartup;
            while (!RunSceneReadinessRegistry.IsTerminal(newSceneName))
            {
                if (Time.realtimeSinceStartup - waitStartTime > 50f)
                {
                    RunSceneReadinessRegistry.MarkFailed(
                        newSceneName,
                        "DungeonRunScene ready wait timed out.");
                    break;
                }

                yield return null;
            }

            RunSceneReadinessState readiness =
                RunSceneReadinessRegistry.GetState(newSceneName);
            if (readiness == RunSceneReadinessState.Ready)
            {
                pendingDungeonFailureReturnContext = null;
                if (loadingScreen != null)
                    loadingScreen.SetProgress(1f);
            }
            else
            {
                string failureMessage =
                    RunSceneReadinessRegistry.GetMessage(newSceneName);
                Debug.LogError(
                    "[SceneFlow] DungeonRunScene generation failed. "
                    + failureMessage);
                DungeonRunLaunchContextHolder.Clear();
                deferredRecoveryContext =
                    pendingDungeonFailureReturnContext
                    ?? RunSceneReturnContext.CreateHubTransfer(
                        DefaultHubSceneName,
                        "Default");
                pendingDungeonFailureReturnContext = null;
            }
        }
        else if (IsHubSceneName(newSceneName))
        {
            yield return PlacePlayerAtHubSpawn("Default"); // 허브 직접 로드
            SpawnConfiguredSceneItems(); // 최종 위치 기준 아이템 배치
            WorldMinimapController.ShowHubMinimap(FindPlayer()); // 허브 미니맵
        }

        if (loadingScreen != null && deferredRecoveryContext == null)
            loadingScreen.Hide(); // 로딩 종료

        isSwitching = false; // 전환 해제
        switchRoutine = null; // 루틴 해제

        if (deferredRecoveryContext != null)
        {
            StartSubSceneSwitch(
                deferredRecoveryContext.TargetSceneName,
                deferredRecoveryContext);
        }
    }

    private void SpawnConfiguredSceneItems()
    {
        Scene scene = SceneManager.GetSceneByName(currentSubSceneName);
        ItemPickupSpawner.SpawnConfiguredPickupsInScene(scene);
    }

    private IEnumerator LoadSubScene(string sceneName)
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive); // 추가 씬
        if (loadOperation != null)
            yield return loadOperation;
    }

    private void ActivateSubScene(string sceneName)
    {
        Scene scene = SceneManager.GetSceneByName(sceneName); // 대상 씬
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        SceneManager.SetActiveScene(scene); // 활성 씬
        ApplyDefaultCameraYaw(sceneName); // 씬별 기본 시점
    }

    private static void ApplyDefaultCameraYaw(string sceneName)
    {
        QuarterViewCamera cameraController = QuarterViewCamera.ActiveInstance;
        if (cameraController == null)
            return;

        if (sceneName == DungeonRunSceneName)
            cameraController.SetYaw(DungeonRunCameraYaw); // 던전 진입 시 초기화
    }

    private string GetSourceSceneName()
    {
        if (!string.IsNullOrEmpty(currentSubSceneName))
            return currentSubSceneName;

        string loadedSubScene = FindLoadedSubSceneName(); // 로드 씬
        return string.IsNullOrEmpty(loadedSubScene) ? DefaultHubSceneName : loadedSubScene;
    }

    private static string FindLoadedSubSceneName()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (IsManagedSubSceneName(activeSceneName) && IsSceneLoaded(activeSceneName))
            return activeSceneName;

        if (IsSceneLoaded(DungeonRunSceneName))
            return DungeonRunSceneName;

        if (IsSceneLoaded(HideoutSceneName))
            return HideoutSceneName;

        return string.Empty;
    }

    public static bool IsHubSceneName(string sceneName)
    {
        return sceneName == HideoutSceneName;
    }

    public static bool IsRunSceneName(string sceneName)
    {
        return sceneName == DungeonRunSceneName;
    }

    private static bool IsManagedSubSceneName(string sceneName)
    {
        return IsRunSceneName(sceneName) || IsHubSceneName(sceneName);
    }

    private static bool IsSceneLoaded(string sceneName)
    {
        Scene scene = SceneManager.GetSceneByName(sceneName); // 대상 씬
        return scene.IsValid() && scene.isLoaded;
    }

    private static Transform FindPlayer()
    {
        PlayerActorRuntime actor = PlayerContext.GetOrCreate()?.CurrentActor;
        if (actor != null)
            return actor.transform;
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        return playerObject != null ? playerObject.transform : null;
    }

    private IEnumerator PlacePlayerAtHubSpawn(string returnPointId)
    {
        yield return null; // 로드된 콜라이더 반영

        HubReturnPoint returnPoint = FindHubReturnPoint(currentSubSceneName, returnPointId);
        if (returnPoint == null)
        {
            Debug.LogWarning(
                "[SceneFlow] HubReturnPoint was not found. Scene="
                + currentSubSceneName
                + ", Id="
                + returnPointId);
            yield break;
        }

        Vector3 spawnPosition = SnapHubPositionToGround(returnPoint);
        Quaternion spawnRotation = returnPoint.transform.rotation;
        int waitedFrames = 0;
        PlayerContext context = PlayerContext.GetOrCreate();
        while (context.CurrentActor == null && waitedFrames < PlayerReadyWaitFrames)
        {
            waitedFrames++;
            yield return null;
        }
        context.CurrentActorKit?.CancelCurrentActions(WeaponActionCancelReason.Recovery);

        Transform player = FindPlayer();
        if (player != null)
            ActorTeleportUtility.TeleportSafely(player, spawnPosition, spawnRotation);
        else
            Debug.LogWarning("[SceneFlow] Player was not ready for hub spawn placement.");
    }

    private static HubReturnPoint FindHubReturnPoint(string sceneName, string returnPointId)
    {
        HubReturnPoint[] points = FindObjectsByType<HubReturnPoint>(FindObjectsSortMode.None);
        HubReturnPoint fallback = null;

        for (int i = 0; i < points.Length; i++)
        {
            HubReturnPoint point = points[i];
            if (point == null || point.gameObject.scene.name != sceneName)
                continue;

            if (fallback == null)
                fallback = point;

            if (point.ReturnPointId == returnPointId)
                return point;
        }

        return fallback;
    }

    private static Vector3 SnapHubPositionToGround(HubReturnPoint returnPoint)
    {
        Vector3 position = returnPoint.transform.position;
        Vector3 rayOrigin = position + Vector3.up * HubGroundRayHeight;
        int groundLayer = LayerMask.NameToLayer("Ground");
        int groundMask = groundLayer >= 0 ? 1 << groundLayer : Physics.DefaultRaycastLayers;
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            HubGroundRayDistance,
            groundMask,
            QueryTriggerInteraction.Ignore);

        bool found = false;
        RaycastHit bestHit = default;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.gameObject.scene != returnPoint.gameObject.scene)
                continue;

            if (!found || hit.distance < bestHit.distance)
            {
                found = true;
                bestHit = hit;
            }
        }

        if (found)
            position.y = bestHit.point.y + HubGroundOffset;
        else
            Debug.LogWarning("[SceneFlow] Ground snap failed at hub return point: " + returnPoint.name);

        return position;
    }

    private void ResolveLoadingScreen()
    {
        if (loadingScreen == null)
            loadingScreen = FindFirstObjectByType<LoadingScreenUI>(FindObjectsInactive.Include); // 씬 UI
    }

    private static void ClosePersistentUiForSceneSwitch()
    {
        StashUI[] stashUis = FindObjectsByType<StashUI>(FindObjectsInactive.Include, FindObjectsSortMode.None); // 창고 UI
        for (int i = 0; i < stashUis.Length; i++)
        {
            if (stashUis[i] != null)
                stashUis[i].Close(); // 창고 닫기
        }

        InventoryUI[] inventoryUis = FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None); // 인벤 UI
        for (int i = 0; i < inventoryUis.Length; i++)
        {
            InventoryUI inventoryUi = inventoryUis[i];
            if (inventoryUi == null)
                continue;

            inventoryUi.InputToggleLocked = false; // Tab 복구
            inventoryUi.SetVisible(false); // 인벤 숨김
        }

        DragSlot.ClearDragState(); // 드래그 취소
        TooltipManager.RefreshActiveInstance(); // 툴팁 인스턴스
        if (TooltipManager.Instance != null)
            TooltipManager.Instance.HideTooltip(); // Tooltip 숨김

        GameplayInputBlocker.ClearAllForSceneReturn(); // 입력 복구
    }
}
