using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WorldMinimapController : MonoBehaviour
{
    private const float MapPadding = 8f;
    private const float UpdateInterval = 0.08f;

    [Header("UI References")]
    [SerializeField] private GameObject minimapRoot;
    [SerializeField] private RectTransform rotatingRoot;
    [SerializeField] private RawImage minimapImage;
    [SerializeField] private RawImage fogOverlay;
    [SerializeField] private RectTransform playerMarker;
    [SerializeField] private Image playerMarkerImage;
    [SerializeField] private RectTransform facingCone;
    [SerializeField] private Image facingConeImage;
    [SerializeField] private Button zoomInButton;
    [SerializeField] private Button zoomOutButton;
    [SerializeField] private TextMeshProUGUI zoomValueText;

    [Header("Display")]
    [SerializeField] private int interactiveSortingOrder = 45;
    [SerializeField] private int inventoryOpenSortingOrder = -10;
    [SerializeField] private float minimapSize = 240f;
    [SerializeField] private float playerMarkerSize = 10f;
    [SerializeField] private float facingConeSize = 44f;

    [Header("Zoom")]
    [SerializeField] private float defaultZoomSize = 55f;
    [SerializeField] private float currentZoomSize = 55f;
    [SerializeField] private float minZoomSize = 20f;
    [SerializeField] private float maxZoomSize = 80f;
    [SerializeField] private float zoomStep = 10f;

    [Header("Markers")]
    [SerializeField] private float normalEnemyMarkerSize = 4.6f;
    [SerializeField] private float eliteEnemyMarkerSize = 6.2f;
    [SerializeField] private int maxEnemyMarkers = 160;

    private static WorldMinimapController instance;
    private static Sprite circleSprite;

    private readonly List<Image> enemyMarkerPool = new List<Image>(160);
    private readonly List<EnemyRank> enemyBuffer = new List<EnemyRank>(160);

    private RectTransform contentRoot;
    private RectTransform topologyRoot;
    private RectTransform markerRoot;
    private Transform playerTarget;
    private Camera playerViewCamera;
    private Canvas minimapCanvas;
    private GraphicRaycaster minimapRaycaster;
    private InventoryUI inventoryUI;
    private StashUI stashUI;
    private float nextUpdateTime;
    private bool warnedMissingReferences;

    public static WorldMinimapController Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<WorldMinimapController>(
                    FindObjectsInactive.Include);
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
        EnsureLightweightView();
        ApplyUiDefaults();
    }

    private void OnEnable()
    {
        EnsureLightweightView();
        RegisterZoomButtons();
        RunFallGuard.TargetTeleported -= HandleTargetTeleported;
        RunFallGuard.TargetTeleported += HandleTargetTeleported; // 낙하 복구 연결
    }

    private void OnDisable()
    {
        RunFallGuard.TargetTeleported -= HandleTargetTeleported;
        UnregisterZoomButtons();
    }

    private void Start()
    {
        ShowForHub(FindPlayer());
    }

    private void LateUpdate()
    {
        UpdateInteractionLayer();
        if (Time.unscaledTime < nextUpdateTime || !HasRequiredReferences())
            return;

        nextUpdateTime = Time.unscaledTime + UpdateInterval;
        Transform resolvedPlayer = FindPlayer();
        if (resolvedPlayer != null)
            playerTarget = resolvedPlayer;

        if (playerTarget == null)
            return;

        RefreshTrackedView();
    }

    private void RefreshTrackedView()
    {
        playerViewCamera = playerViewCamera != null ? playerViewCamera : Camera.main;
        UpdateContentRotation();
        UpdatePlayerMarker();
        UpdateEnemyMarkers();
    }

    private void HandleTargetTeleported(Transform teleportedTarget)
    {
        Transform resolvedPlayer = FindPlayer(); // 현재 리더 또는 Player
        if (teleportedTarget == null || (teleportedTarget != playerTarget && teleportedTarget != resolvedPlayer))
            return;

        playerTarget = resolvedPlayer != null ? resolvedPlayer : teleportedTarget;
        nextUpdateTime = 0f; // 주기 대기 제거
        if (HasRequiredReferences())
            RefreshTrackedView();
    }

    public static void ShowHubMinimap(Transform player)
    {
        if (Instance != null)
            Instance.ShowForHub(player);
    }

    public static void ShowHideoutMinimap(Transform player)
    {
        ShowHubMinimap(player); // 기존 호출 호환
    }

    public void ShowForHub(Transform player)
    {
        if (!HasRequiredReferences())
            return;

        playerTarget = ResolvePlayerActor(player);
        ClearTopology();
        SetRootVisible(true);
        UpdatePlayerMarker();
        UpdateEnemyMarkers();
    }

    public void ShowForHideout(Transform player)
    {
        ShowForHub(player); // 기존 호출 호환
    }

    public void ForceHide()
    {
        SetRootVisible(false);
    }

    private void EnsureLightweightView()
    {
        if (rotatingRoot == null)
            return;

        Transform existing = rotatingRoot.Find("LightweightMapContent");
        if (existing == null)
        {
            GameObject rootObject = new GameObject("LightweightMapContent", typeof(RectTransform));
            rootObject.transform.SetParent(rotatingRoot, false);
            existing = rootObject.transform;
        }

        contentRoot = existing as RectTransform;
        Stretch(contentRoot);
        topologyRoot = EnsureRectChild(contentRoot, "Topology");
        markerRoot = EnsureRectChild(contentRoot, "Markers");
        Stretch(topologyRoot);
        Stretch(markerRoot);

        if (minimapImage != null)
        {
            minimapImage.texture = null;
            minimapImage.enabled = false;
        }

        if (fogOverlay != null)
        {
            fogOverlay.texture = null;
            fogOverlay.enabled = false;
        }
    }

    private void ApplyUiDefaults()
    {
        minZoomSize = Mathf.Max(5f, minZoomSize);
        maxZoomSize = Mathf.Max(minZoomSize, maxZoomSize);
        currentZoomSize = Mathf.Clamp(currentZoomSize <= 0f ? defaultZoomSize : currentZoomSize, minZoomSize, maxZoomSize);

        if (minimapRoot != null)
        {
            minimapCanvas = minimapRoot.GetComponent<Canvas>();
            minimapRaycaster = minimapRoot.GetComponent<GraphicRaycaster>();
        }

        if (playerMarker != null)
            playerMarker.sizeDelta = Vector2.one * playerMarkerSize;
        if (facingCone != null)
            facingCone.sizeDelta = Vector2.one * facingConeSize;

        UpdateZoomValueText();
    }

    private void ClearTopology()
    {
        if (topologyRoot == null)
            return;

        for (int i = topologyRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = topologyRoot.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }
    }

    private void UpdateEnemyMarkers()
    {
        if (markerRoot == null || playerTarget == null)
            return;

        EnemyRank.CollectActive(enemyBuffer);
        int used = 0;
        int count = Mathf.Min(enemyBuffer.Count, maxEnemyMarkers);
        for (int i = 0; i < count; i++)
        {
            EnemyRank enemy = enemyBuffer[i];
            if (enemy == null)
                continue;

            Vector2 flat = new Vector2(enemy.transform.position.x - playerTarget.position.x, enemy.transform.position.z - playerTarget.position.z);
            if (flat.sqrMagnitude > currentZoomSize * currentZoomSize)
                continue;

            Image marker = GetEnemyMarker(used++);
            bool elite = enemy.Rank == EnemyRankType.Elite;
            marker.rectTransform.anchoredPosition = ProjectRelative(flat);
            marker.rectTransform.sizeDelta = Vector2.one * (elite ? eliteEnemyMarkerSize : normalEnemyMarkerSize);
            marker.color = elite ? new Color(1f, 0.42f, 0.12f, 1f) : new Color(1f, 0.22f, 0.2f, 0.95f);
            marker.gameObject.SetActive(true);
        }

        for (int i = used; i < enemyMarkerPool.Count; i++)
            enemyMarkerPool[i].gameObject.SetActive(false);
    }

    private Image GetEnemyMarker(int index)
    {
        while (enemyMarkerPool.Count <= index)
        {
            Image marker = CreateImage("EnemyMarker", markerRoot, Color.red);
            marker.sprite = GetCircleSprite();
            enemyMarkerPool.Add(marker);
        }
        return enemyMarkerPool[index];
    }

    private void UpdateContentRotation()
    {
        if (contentRoot == null || playerViewCamera == null)
            return;
        contentRoot.localRotation = Quaternion.Euler(0f, 0f, playerViewCamera.transform.eulerAngles.y);
    }

    private void UpdatePlayerMarker()
    {
        if (playerMarker != null)
            playerMarker.anchoredPosition = Vector2.zero;
        if (facingCone != null)
        {
            facingCone.anchoredPosition = Vector2.zero;
            float cameraYaw = playerViewCamera != null ? playerViewCamera.transform.eulerAngles.y : 0f;
            float playerYaw = playerTarget != null ? playerTarget.eulerAngles.y : 0f;
            facingCone.localRotation = Quaternion.Euler(0f, 0f, cameraYaw - playerYaw);
        }
    }

    private Vector2 ProjectRelative(Vector2 flat)
    {
        return flat / Mathf.Max(1f, currentZoomSize) * MapPixelRadius;
    }

    private float MapPixelRadius => Mathf.Max(1f, minimapSize * 0.5f - MapPadding);

    private void ZoomIn()
    {
        SetZoom(currentZoomSize - zoomStep);
    }

    private void ZoomOut()
    {
        SetZoom(currentZoomSize + zoomStep);
    }

    private void SetZoom(float value)
    {
        currentZoomSize = Mathf.Clamp(value, minZoomSize, maxZoomSize);
        UpdateZoomValueText();
        UpdateEnemyMarkers();
    }

    private void UpdateZoomValueText()
    {
        if (zoomValueText == null)
            return;
        float ratio = Mathf.Clamp(defaultZoomSize / Mathf.Max(1f, currentZoomSize), 0.1f, 9.9f);
        zoomValueText.text = $"x{ratio:0.0}";
    }

    private void RegisterZoomButtons()
    {
        if (zoomInButton != null)
        {
            zoomInButton.onClick.RemoveListener(ZoomIn);
            zoomInButton.onClick.AddListener(ZoomIn);
        }
        if (zoomOutButton != null)
        {
            zoomOutButton.onClick.RemoveListener(ZoomOut);
            zoomOutButton.onClick.AddListener(ZoomOut);
        }
    }

    private void UnregisterZoomButtons()
    {
        if (zoomInButton != null)
            zoomInButton.onClick.RemoveListener(ZoomIn);
        if (zoomOutButton != null)
            zoomOutButton.onClick.RemoveListener(ZoomOut);
    }

    private void UpdateInteractionLayer()
    {
        if (minimapCanvas == null)
            return;

        if (inventoryUI == null)
            inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (stashUI == null)
            stashUI = FindFirstObjectByType<StashUI>(FindObjectsInactive.Include);

        bool blocked = (inventoryUI != null && inventoryUI.IsVisible) || (stashUI != null && stashUI.IsOpen);
        minimapCanvas.overrideSorting = true;
        minimapCanvas.sortingOrder = blocked ? inventoryOpenSortingOrder : interactiveSortingOrder;
        if (minimapRaycaster != null)
            minimapRaycaster.enabled = !blocked;
    }

    private bool HasRequiredReferences()
    {
        bool valid = minimapRoot != null && rotatingRoot != null && contentRoot != null && markerRoot != null;
        if (!valid && !warnedMissingReferences)
        {
            warnedMissingReferences = true;
            Debug.LogWarning(
                "[WorldMinimap] Lightweight minimap references are missing. "
                + "Re-run the HUD objectizer.");
        }
        return valid;
    }

    private void SetRootVisible(bool visible)
    {
        if (minimapRoot != null && minimapRoot.activeSelf != visible)
            minimapRoot.SetActive(visible);
    }

    private static Transform FindPlayer()
    {
        Transform leader = ResolvePlayerActor(null);
        if (leader != null)
            return leader;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            return player.transform;

        PlayerMovement movement = FindFirstObjectByType<PlayerMovement>();
        if (movement != null)
            return movement.transform;

        PlayerInventory inventory = FindFirstObjectByType<PlayerInventory>();
        return inventory != null && inventory.GetComponentInParent<PlayerMovement>() != null
            ? inventory.GetComponentInParent<PlayerMovement>().transform
            : null; // 계정 서비스 오인 방지
    }

    private static Transform ResolvePlayerActor(Transform fallback)
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        PlayerActorRuntime leader = context != null ? context.CurrentActor : null;
        return leader != null ? leader.transform : fallback;
    }

    private static RectTransform EnsureRectChild(Transform parent, string objectName)
    {
        Transform child = parent.Find(objectName);
        if (child == null)
        {
            GameObject childObject = new GameObject(objectName, typeof(RectTransform));
            childObject.transform.SetParent(parent, false);
            child = childObject.transform;
        }
        return child as RectTransform;
    }

    private static Image CreateImage(string objectName, RectTransform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        if (rect == null)
            return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null)
            return circleSprite;

        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "LightweightMinimapCircleTexture",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Vector2 center = Vector2.one * ((size - 1) * 0.5f);
        float radius = size * 0.47f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), center) <= radius ? Color.white : Color.clear);
        }
        texture.Apply(false, true);
        circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        circleSprite.name = "LightweightMinimapCircleSprite";
        circleSprite.hideFlags = HideFlags.HideAndDontSave;
        return circleSprite;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
