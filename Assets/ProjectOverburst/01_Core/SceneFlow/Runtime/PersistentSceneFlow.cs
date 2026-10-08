using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PersistentSceneFlow : MonoBehaviour // 씬 전환 허브
{
    public const string PersistentSceneName = "PersistentScene"; // 상주 씬
    public const string HideoutSceneName = "HideoutScene"; // 전투 테스트 씬
    public const string MainSceneName = "MainScene";
    public const string DefaultHubSceneName = MainSceneName; // 기본 마을
    private const int PlayerReadyWaitFrames = 180; // 플레이어 준비 대기
    private const float HubGroundRayHeight = 30f; // 허브 지면 탐색 높이
    private const float HubGroundRayDistance = 100f; // 허브 지면 탐색 거리
    private const float HubGroundOffset = 0.12f; // 캐릭터 지면 여유

    private static PersistentSceneFlow instance; // 단일 인스턴스

    private string currentSubSceneName; // 현재 SubScene
    private bool isSwitching; // 전환 중
    private Coroutine switchRoutine; // 전환 루틴
    private LoadingScreenUI loadingScreen; // 로딩 UI
    private double presentationWaitSeconds;
    private const float PresentationWaitBudget = 10f;

    // Count only time spent in presentation preparation, not account/world/scene loading.
    private IEnumerator PrepareCommonPresentation(System.Func<bool> cancelled = null)
    {
        double began = Time.realtimeSinceStartupAsDouble;
        try
        {
            float remaining = Mathf.Max(0f, PresentationWaitBudget - (float)presentationWaitSeconds);
            yield return CombatImpactFeel.PrepareForGameplay(remaining, cancelled);

        }
        finally { presentationWaitSeconds += Time.realtimeSinceStartupAsDouble - began; }
    }

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
        if (IsHubSceneName(SceneManager.GetActiveScene().name))
        {
            WorldSessionState.SetContentScene(SceneManager.GetActiveScene());
            WorldSessionState.SetPhase(WorldPhase.Hideout);
        }
        if (SceneManager.GetActiveScene().name != PersistentSceneName)
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
        if (gameObject.scene.name != PersistentSceneName) // 직접 연 마을의 additive 부팅도 허용
            return;

        ResolveLoadingScreen(); // 로딩 UI
        loadingScreen?.Show("LOADING", "게임 준비 중...");

        StartCoroutine(BootAccountAndWorld());
    }

    private IEnumerator BootAccountAndWorld()
    {
        presentationWaitSeconds = 0;
        isSwitching = true;
        GameplayInputBlocker.Block(this);
        yield return null; // Persistent account/actor Start callbacks finish first.
        int waited = 0;
        while ((PlayerAccountInventoryService.Instance == null || PlayerProgression.Current == null
            || PlayerContext.Instance?.CurrentActor == null) && waited++ < PlayerReadyWaitFrames)
            yield return null;
        if (!Overburst.Persistence.AccountBootstrap.Initialize(PlayerAccountInventoryService.Instance))
        {
            ResolveLoadingScreen();
            loadingScreen?.Show("저장 데이터 오류", Overburst.Persistence.AccountBootstrap.Error);
            yield break;
        }
        if (GetComponent<Overburst.Persistence.RunLifetimeDriver>() == null)
            gameObject.AddComponent<Overburst.Persistence.RunLifetimeDriver>();
        loadingScreen?.SetStatus("전투 효과 준비 중...");
        yield return PrepareCommonPresentation();
        loadingScreen?.SetStatus("마을 준비 중...");
        loadingScreen?.SetProgress(0.35f);
        yield return EnsureInitialSubScene();
        GameplayInputBlocker.Unblock(this);
    }

    public string RunEntryError { get; private set; }
    private bool cancelRunEntry;

    public bool EnterRun(string sceneName, Overburst.Persistence.MapInstanceState map, string mapItemId = null)
        => BeginRun(sceneName, map, mapItemId, false);

    public bool EnterDebugRun(string sceneName, Overburst.Persistence.MapInstanceState map)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return BeginRun(sceneName, map, null, true);
#else
        return false;
#endif
    }

    private bool BeginRun(string sceneName, Overburst.Persistence.MapInstanceState map, string mapItemId, bool debugEntry)
    {
        var account = Overburst.Persistence.AccountGameplaySession.Current;
        if (isSwitching || !WorldSessionState.IsHideout || account == null
            || string.IsNullOrWhiteSpace(sceneName) || sceneName == PersistentSceneName || IsHubSceneName(sceneName)) return false;
        if (!IsSceneLoaded(sceneName) && !Application.CanStreamedLevelBeLoaded(sceneName))
        { RunEntryError = "입장할 월드 씬을 찾을 수 없습니다."; return false; }
        string runId = System.Guid.NewGuid().ToString("N");
        try
        {
            if (!new Overburst.Persistence.AccountRunSession(account).Prepare(runId, map, mapItemId, debugEntry)) return false;
        }
        catch (System.Exception error) { RunEntryError = error.Message; return false; }
        RunEntryError = null; cancelRunEntry = false; isSwitching = true;
        ClosePersistentUiForSceneSwitch();
        GameplayInputBlocker.Block(this);
        switchRoutine = StartCoroutine(EnterPreparedRun(sceneName, runId));
        return true;
    }

    public void CancelRunEntry()
    {
        if (isSwitching && Overburst.Persistence.AccountGameplaySession.Current?.ReadRun()?.phase
            == Overburst.Persistence.RunPhase.EntryPending) cancelRunEntry = true;
    }

    private IEnumerator EnterPreparedRun(string sceneName, string runId)
    {
        presentationWaitSeconds = 0;
        var account = Overburst.Persistence.AccountGameplaySession.Current;
        var run = new Overburst.Persistence.AccountRunSession(account);
        string previous = currentSubSceneName;
        ResolveLoadingScreen();
        loadingScreen?.Show("ENTERING", "월드 준비 중...");
        AsyncOperation loading = null;
        if (!IsSceneLoaded(sceneName))
        {
            try { loading = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive); }
            catch (System.Exception error) { RunEntryError = error.Message; }
            if (loading != null) yield return loading;
        }
        RunWorldGate gate = null;
        if (RunEntryError == null)
        {
            foreach (var candidate in FindObjectsByType<RunWorldGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.gameObject.scene.name == sceneName)
                {
                    if (gate != null) { RunEntryError = "월드 준비 게이트가 중복되었습니다."; break; }
                    gate = candidate;
                }
            if (gate == null) RunEntryError = "월드 준비 게이트를 찾을 수 없습니다.";
            if (RunEntryError == null)
                try { gate.BeginPreparation(account.ReadRun()); }
                catch (System.Exception error) { RunEntryError = error.Message; }
        }
        float deadline = Time.realtimeSinceStartup + 60f;
        while (RunEntryError == null && !cancelRunEntry && gate != null && !gate.IsReady && gate.Error == null
            && Time.realtimeSinceStartup < deadline) yield return null;
        if (RunEntryError == null)
        {
            if (cancelRunEntry) RunEntryError = "입장을 취소했습니다.";
            else if (gate == null || !gate.IsReady || gate.EntryPoint == null)
                RunEntryError = gate != null && gate.Error != null ? gate.Error : "월드 준비 시간이 초과됐습니다.";
            else if (FindPlayer() == null) RunEntryError = "플레이어가 준비되지 않았습니다.";
        }
        Transform entryPlayer = null;
        Vector3 entryPosition = Vector3.zero;
        Quaternion entryRotation = Quaternion.identity;
        RunFallGuard entryGuard = null;
        bool addedEntryGuard = false;
        if (RunEntryError == null)
        {
            yield return PrepareCommonPresentation(() => cancelRunEntry);
            if (cancelRunEntry) RunEntryError = "입장을 취소했습니다.";
        }
        if (RunEntryError == null)
        {
            try
            {
                entryPlayer = FindPlayer();
                entryPosition = gate.EntryPoint.position;
                entryRotation = gate.EntryPoint.rotation;
                if (sceneName == DiamondDungeonWorld.SceneName)
                {
                    if (entryPlayer == null || RunWalkableContext.Current == null
                        || !RunWalkableContext.Current.IsWalkable(entryPosition))
                        throw new System.InvalidOperationException("플레이어 낙하 복귀 지점을 준비하지 못했습니다.");
                    entryGuard = entryPlayer.GetComponent<RunFallGuard>();
                    if (entryGuard == null)
                    {
                        entryGuard = entryPlayer.gameObject.AddComponent<RunFallGuard>();
                        addedEntryGuard = true;
                    }
                    bool configured = RunWalkableContext.TryConfigureExistingFallGuard(
                        entryGuard, RunFallGuardMode.TeleportToRespawn, entryPosition);
                    entryGuard.enabled = false;
                    if (!configured)
                        throw new System.InvalidOperationException("플레이어 낙하 복귀 영역을 준비하지 못했습니다.");
                }
                if (!run.Activate(runId)) RunEntryError = "지도 입장을 확정하지 못했습니다.";
            }
            catch (System.Exception error) { RunEntryError = error.Message; }
        }
        if (RunEntryError != null)
        {
            if (entryGuard != null)
            {
                entryGuard.Configure(null, RunFallGuardMode.TeleportToRespawn, Vector3.zero);
                if (addedEntryGuard) Destroy(entryGuard);
            }
            // Do not return control while the pending entry cannot be durably cancelled.
            bool cancelled = false;
            while (!cancelled)
            {
                try { run.Fail(runId); cancelled = true; }
                catch (System.Exception error) { loadingScreen?.SetStatus("입장 취소 저장 재시도: " + error.Message); }
                if (!cancelled) yield return new WaitForSecondsRealtime(1f);
            }
            if (IsSceneLoaded(sceneName)) yield return SceneManager.UnloadSceneAsync(sceneName);
            ActivateSubScene(previous);
            WorldSessionState.SetPhase(WorldPhase.Hideout);
            loadingScreen?.Hide();
            isSwitching = false; switchRoutine = null;
            GameplayInputBlocker.Unblock(this);
            yield break;
        }
        currentSubSceneName = sceneName;
        ActivateSubScene(sceneName);
        PlayerContext.Instance?.CurrentActorHealth?.SetRunMapModifiers(
            1f - MapOptionPolicy.Value(gate.Map, MapOptionPolicy.PlayerHealth),
            1f - MapOptionPolicy.Value(gate.Map, MapOptionPolicy.PlayerHealing));
        PlayerContext.Instance?.CurrentActorKit?.CancelCurrentActions(WeaponActionCancelReason.Recovery);
        ActorTeleportUtility.TeleportSafely(entryPlayer, entryPosition, entryRotation);
        if (entryGuard != null) entryGuard.enabled = true;
        if (!string.IsNullOrEmpty(previous) && previous != sceneName && IsSceneLoaded(previous))
            yield return SceneManager.UnloadSceneAsync(previous);
        loadingScreen?.Hide();
        isSwitching = false; switchRoutine = null;
        GameplayInputBlocker.Unblock(this);
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
        // Initial load also includes the deferred hub spawn placement. Publishing
        // "ready" before that teleport let arena entry be overwritten next frame.
        isSwitching = true;
        WorldSessionState.SetPhase(WorldPhase.Loading);
        try { yield return EnsureInitialSubSceneCore(); }
        finally
        {
            loadingScreen?.Hide();
            isSwitching = false;
        }
    }

    private IEnumerator EnsureInitialSubSceneCore()
    {
        currentSubSceneName = FindLoadedSubSceneName(); // 기존 로드 확인
        if (!string.IsNullOrEmpty(currentSubSceneName))
        {
            ActivateSubScene(currentSubSceneName); // 활성 씬
            if (IsHubSceneName(currentSubSceneName))
            {
                yield return PlacePlayerAtHubSpawn("Default"); // 플레이어 초기 배치
                SpawnConfiguredSceneItems(); // 최종 위치 기준 아이템 배치
                yield return PrepareCommonPresentation();
                WorldSessionState.SetPhase(WorldPhase.Hideout);
                WorldMinimapController.ShowHubMinimap(FindPlayer()); // 허브 미니맵
            }

            yield break;
        }

        yield return LoadSubScene(DefaultHubSceneName);
        currentSubSceneName = DefaultHubSceneName; // 기본 허브
        ActivateSubScene(currentSubSceneName); // 활성 씬
        yield return PlacePlayerAtHubSpawn("Default"); // 플레이어 초기 배치
        SpawnConfiguredSceneItems(); // 최종 위치 기준 아이템 배치
        yield return PrepareCommonPresentation();
        WorldSessionState.SetPhase(WorldPhase.Hideout);
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
        presentationWaitSeconds = 0;
        if (IsHubSceneName(newSceneName))
        {
            var playerGuard = FindPlayer()?.GetComponent<RunFallGuard>();
            if (playerGuard != null) playerGuard.enabled = false;
        }
        WorldSessionState.SetPhase(WorldPhase.Loading);
        isSwitching = true; // 전환 잠금
        ClosePersistentUiForSceneSwitch(); // UI 정리
        GameplayInputBlocker.Block(this);
        ResolveLoadingScreen(); // 로딩 UI

        string previousSceneName = string.IsNullOrEmpty(currentSubSceneName)
            ? FindLoadedSubSceneName()
            : currentSubSceneName;
        bool isReturnToHub = IsHubSceneName(newSceneName) && returnContext != null; // 허브 복귀 여부
        bool isDungeonReturn = isReturnToHub && !string.IsNullOrEmpty(previousSceneName)
            && !IsHubSceneName(previousSceneName);
        if (loadingScreen != null)
        {
            if (isReturnToHub)
                loadingScreen.Show("RETURNING", "현재 지역 연결 해제 중...",
                    isDungeonReturn ? LoadingScreenArtworkContext.ReturnToHideout : LoadingScreenArtworkContext.Random);
            else
                loadingScreen.Show("LOADING", "씬 전환 준비 중...");

            loadingScreen.SetProgress(0.1f); // 시작 진행
        }

        if (IsHubSceneName(newSceneName))
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

        if (loadingScreen != null)
        {
            loadingScreen.SetStatus(newSceneName + " 로드 중...");
            loadingScreen.SetProgress(0.35f); // 로드 진행
        }

        if (!IsSceneLoaded(newSceneName))
            yield return LoadSubScene(newSceneName); // Additive 로드

        if (loadingScreen != null)
        {
            loadingScreen.SetStatus("허브 위치 정리 중...");
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

            if (loadingScreen != null)
                loadingScreen.SetProgress(1f); // 완료

            WorldSessionState.SetPhase(WorldPhase.Hideout);
            WorldMinimapController.ShowHubMinimap(FindPlayer()); // 허브 미니맵
        }
        else if (IsHubSceneName(newSceneName))
        {
            yield return PlacePlayerAtHubSpawn("Default"); // 허브 직접 로드
            SpawnConfiguredSceneItems(); // 최종 위치 기준 아이템 배치
            WorldSessionState.SetPhase(WorldPhase.Hideout);
            WorldMinimapController.ShowHubMinimap(FindPlayer()); // 허브 미니맵
        }

        yield return PrepareCommonPresentation();
        if (loadingScreen != null)
            loadingScreen.Hide(); // 로딩 종료

        isSwitching = false; // 전환 해제
        switchRoutine = null; // 루틴 해제
        GameplayInputBlocker.Unblock(this);

    }

    private void SpawnConfiguredSceneItems()
    {
        Scene scene = SceneManager.GetSceneByName(currentSubSceneName);
        ItemPickupSpawner.SpawnConfiguredPickupsInScene(scene);
        MapDungeonPortal.SpawnInHideout(scene);
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
        WorldSessionState.SetContentScene(scene);
        ApplyDefaultCameraYaw(sceneName); // 씬별 기본 시점
    }

    private static void ApplyDefaultCameraYaw(string sceneName)
    {
        QuarterViewCamera cameraController = QuarterViewCamera.ActiveInstance;
        if (cameraController == null)
            return;

        Scene scene = SceneManager.GetSceneByName(sceneName);
        MainTownSceneSettings settings = null;
        if (scene.IsValid() && scene.isLoaded)
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                settings = root.GetComponentInChildren<MainTownSceneSettings>();
                if (settings != null) break;
            }
        cameraController.SetYaw(settings != null ? settings.CameraYaw : QuarterViewCamera.DefaultYaw);
    }

    private static string FindLoadedSubSceneName()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (IsManagedSubSceneName(activeSceneName) && IsSceneLoaded(activeSceneName))
            return activeSceneName;

        if (IsSceneLoaded(MainSceneName))
            return MainSceneName;

        if (IsSceneLoaded(HideoutSceneName))
            return HideoutSceneName;

        return string.Empty;
    }

    public static bool IsHubSceneName(string sceneName)
    {
        return sceneName == MainSceneName || sceneName == HideoutSceneName;
    }

    private static bool IsManagedSubSceneName(string sceneName)
    {
        return IsHubSceneName(sceneName);
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

    private static void CloseOwnedUi(System.Action close)
    {
        try { close(); } catch (System.Exception error) { Debug.LogException(error); }
    }

    private static void ClosePersistentUiForSceneSwitch()
    {
        foreach (ShopUI ui in FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (ui != null) CloseOwnedUi(ui.Close);
        foreach (OverburstRunUi ui in FindObjectsByType<OverburstRunUi>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (ui != null) CloseOwnedUi(ui.Close);
        foreach (OverburstGameUI ui in FindObjectsByType<OverburstGameUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (ui != null) CloseOwnedUi(ui.CloseEquipment);
        if (OverburstGameMenu.Instance != null) CloseOwnedUi(OverburstGameMenu.Instance.Close);
        if (OverburstSkillTreeUI.Instance != null) CloseOwnedUi(OverburstSkillTreeUI.Instance.Close);
        if (OverburstHudMenu.Instance != null) CloseOwnedUi(OverburstHudMenu.Instance.CloseImmediate);
        StashUI[] stashUis = FindObjectsByType<StashUI>(FindObjectsInactive.Include, FindObjectsSortMode.None); // 창고 UI
        for (int i = 0; i < stashUis.Length; i++)
        {
            if (stashUis[i] != null)
                CloseOwnedUi(stashUis[i].Close); // 자기 요청 반환
        }

        InventoryUI[] inventoryUis = FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None); // 인벤 UI
        for (int i = 0; i < inventoryUis.Length; i++)
        {
            InventoryUI inventoryUi = inventoryUis[i];
            if (inventoryUi == null)
                continue;


            CloseOwnedUi(() => inventoryUi.SetVisible(false)); // 인벤 숨김
        }

        DragSlot.ClearDragState(); // 드래그 취소
        TooltipManager.RefreshActiveInstance(); // 툴팁 인스턴스
        if (TooltipManager.Instance != null)
            TooltipManager.Instance.HideTooltip(); // Tooltip 숨김


    }
}
