using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;

public sealed class WorldMinimapController : MonoBehaviour
{
    [SerializeField] private MinimapView view;
    [SerializeField] private float defaultZoomSize = 55f;
    [SerializeField] private float currentZoomSize = 55f;
    [SerializeField] private float minZoomSize = 20f;
    [SerializeField] private float maxZoomSize = 80f;
    [SerializeField] private float zoomStep = 10f;
    private static WorldMinimapController instance;
    private static readonly ProfilerMarker ProjectMarker = new ProfilerMarker("Minimap.Project");
    private readonly MinimapEnemySource source = new MinimapEnemySource();
    private readonly MinimapMarkerGraphic.Marker[] markers = new MinimapMarkerGraphic.Marker[MinimapEnemySource.Capacity];
    private PlayerContext playerContext;
    private Transform playerTarget;
    private bool requested, blocked, forceRefresh, warned, hasYaw;
    private int contentScene = int.MinValue;
    private Scene contentSceneInfo;
    private float nextCandidates, nextProjection, nextBinding, yaw;

    public static WorldMinimapController Instance
    {
        get
        {
            if (instance == null) instance = FindFirstObjectByType<WorldMinimapController>(FindObjectsInactive.Include);
            return instance;
        }
    }
    public MinimapView View => view;
    public float ZoomRadius => currentZoomSize;
    public bool IsVisible => requested && view != null && view.ViewRoot.activeSelf;
    public int ProjectionCount { get; private set; }
    public int CandidateCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Awake()
    {
        if (instance != null && instance != this) { enabled = false; return; }
        instance = this;
        minZoomSize = Mathf.Max(5f, minZoomSize);
        maxZoomSize = Mathf.Max(minZoomSize, maxZoomSize);
        currentZoomSize = Mathf.Clamp(defaultZoomSize, minZoomSize, maxZoomSize);
        view?.SetVisible(false);
    }

    private void OnEnable()
    {
        if (instance != null && instance != this) return;
        instance = this;
        if (view != null && view.IsReady)
        {
            view.ZoomInButton.onClick.AddListener(ZoomIn);
            view.ZoomOutButton.onClick.AddListener(ZoomOut);
        }
        GameplayInputBlocker.BlockStateChanged += HandleBlocked;
        RunFallGuard.TargetTeleported += HandleTeleported;
        HandleBlocked(GameplayInputBlocker.IsGameplayInputBlocked);
        nextBinding = 0;
    }

    private void OnDisable()
    {
        GameplayInputBlocker.BlockStateChanged -= HandleBlocked;
        RunFallGuard.TargetTeleported -= HandleTeleported;
        if (view != null && view.IsReady)
        {
            view.ZoomInButton.onClick.RemoveListener(ZoomIn);
            view.ZoomOutButton.onClick.RemoveListener(ZoomOut);
        }
        UnbindPlayer();
        source.Dispose();
        view?.SetVisible(false);
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    public static void ShowHubMinimap(Transform player) => Instance?.ShowForHub(player);
    public static void ShowHideoutMinimap(Transform player) => ShowHubMinimap(player);
    public void ShowForHideout(Transform player) => ShowForHub(player);

    public void ShowForHub(Transform player)
    {
        Scene scene = SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName);
        ShowForScene(player, scene.IsValid() && scene.isLoaded ? scene.handle : int.MinValue);
    }

    // Explicit scene scope also supports the monster test arena without depending on the active persistent scene.
    public void ShowForScene(Transform player, int sceneHandle)
    {
        source.Dispose();
        contentScene = sceneHandle;
        contentSceneInfo = default;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene candidate = SceneManager.GetSceneAt(i);
            if (candidate.handle == sceneHandle) { contentSceneInfo = candidate; break; }
        }
        playerTarget = player;
        requested = true;
        hasYaw = false;
        forceRefresh = true;
        nextBinding = 0;
        view?.SetVisible(false);
        TryBindPlayer();
    }

    public void ForceHide()
    {
        requested = false;
        source.Dispose();
        UnbindPlayer();
        playerTarget = null;
        contentScene = int.MinValue;
        view?.SetVisible(false);
    }

    private void LateUpdate()
    {
        if (!requested) return;
        if (view == null || !view.IsReady)
        {
            if (!warned) { warned = true; Debug.LogWarning("[WorldMinimap] Run OverburstMinimapMigration before using the minimap.", this); }
            return;
        }
        float now = Time.unscaledTime;
        if (now >= nextBinding)
        {
            nextBinding = now + 0.25f;
            TryBindPlayer();
        }
        if (playerTarget == null || !playerTarget.gameObject.activeInHierarchy
            || contentScene == int.MinValue || !contentSceneInfo.isLoaded)
        { view.SetVisible(false); return; }
        QuarterViewCamera camera = QuarterViewCamera.ActiveInstance;
        if (camera != null)
        {
            float nextYaw = camera.CurrentYaw;
            if (!hasYaw || Mathf.Abs(Mathf.DeltaAngle(yaw, nextYaw)) > 0.01f) forceRefresh = true;
            yaw = nextYaw;
            hasYaw = true;
        }
        if (!hasYaw) return;
        view.SetVisible(true);
        view.UpdateSafeArea();
        view.SetFacing(yaw - playerTarget.eulerAngles.y);
        view.SetZoom(currentZoomSize, defaultZoomSize, minZoomSize, maxZoomSize);
        bool dirty = source.IsDirty || forceRefresh;
        if (dirty || now >= nextCandidates)
        {
            source.Select(playerTarget.position, currentZoomSize, contentScene);
            CandidateCount++;
            nextCandidates = now + 0.1f;
            dirty = true;
        }
        if (dirty || now >= nextProjection)
        {
            ProjectMarkers();
            nextProjection = now + 1f / 30f;
            forceRefresh = false;
        }
    }

    private void ProjectMarkers()
    {
        using (ProjectMarker.Auto())
        {
            float radians = yaw * Mathf.Deg2Rad, cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            float scale = view.Radius / currentZoomSize;
            Vector3 origin = playerTarget.position;
            int count = 0;
            for (int i = 0; i < source.Count; i++)
            {
                if (!source.TryGet(i, origin, currentZoomSize * currentZoomSize, out Vector3 position, out EnemyGradeType grade, out int id)) continue;
                markers[count++] = new MinimapMarkerGraphic.Marker
                {
                    Position = MinimapProjection.ProjectBasis(position, origin, cos, sin, scale), Grade = grade, Id = id,
                    Size = grade == EnemyGradeType.Normal ? 4.6f : grade == EnemyGradeType.Boss ? 8f : 6.2f,
                    Color = grade == EnemyGradeType.Normal ? new Color32(255, 74, 65, 245)
                        : grade == EnemyGradeType.Boss ? new Color32(255, 211, 105, 255) : new Color32(255, 139, 48, 255)
                };
            }
            view.Markers.SetMarkers(markers, count);
            ProjectionCount++;
        }
    }

    private void TryBindPlayer()
    {
        PlayerContext context = PlayerContext.Instance;
        if (context == playerContext)
        {
            // CurrentActor resolves late actors; only use its fallback search while waiting.
            if (context != null && (playerTarget == null || !playerTarget.gameObject.activeInHierarchy))
                HandlePlayerChanged(context.CurrentActor);
            return;
        }
        UnbindPlayer();
        playerContext = context;
        if (context == null) return;
        context.CurrentActorChanged += HandlePlayerChanged;
        HandlePlayerChanged(context.CurrentActor);
    }

    private void UnbindPlayer()
    {
        if (playerContext != null) playerContext.CurrentActorChanged -= HandlePlayerChanged;
        playerContext = null;
    }

    private void HandlePlayerChanged(PlayerActorRuntime actor)
    {
        playerTarget = actor != null ? actor.transform : null;
        view?.Markers?.Clear();
        forceRefresh = true;
    }
    private void HandleTeleported(Transform target) { if (target == playerTarget) forceRefresh = true; }
    private void HandleBlocked(bool value)
    {
        blocked = value;
        view?.SetBlocked(value);
        if (view != null && view.IsReady) view.SetZoom(currentZoomSize, defaultZoomSize, minZoomSize, maxZoomSize);
    }
    private void ZoomIn() { if (!blocked) SetZoom(currentZoomSize - zoomStep); }
    private void ZoomOut() { if (!blocked) SetZoom(currentZoomSize + zoomStep); }
    public void SetZoom(float radius)
    {
        currentZoomSize = Mathf.Clamp(radius, minZoomSize, maxZoomSize);
        forceRefresh = true;
        if (view != null && view.IsReady) view.SetZoom(currentZoomSize, defaultZoomSize, minZoomSize, maxZoomSize);
    }
}
