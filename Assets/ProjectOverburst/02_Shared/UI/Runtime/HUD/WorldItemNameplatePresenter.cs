using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1050)]
[DisallowMultipleComponent]
public sealed class WorldItemNameplatePresenter : MonoBehaviour
{
    private const int AuthoredCapacity = 96;

    [Header("References")]
    [SerializeField] private WorldItemNameplateView view;
    [SerializeField] private WorldItemNameplateBridge bridge;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Material pickupHoverOutlineMaterial;

    [Header("Projection")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.85f, 0f);
    [SerializeField] private float hoverBoundsPaddingPixels = 8f;
    [SerializeField] private float hoverMinimumSizePixels = 24f;
    [SerializeField] private float safeMargin = 24f;
    [SerializeField] private float visibilityEnterSafeInset = 4f;
    [SerializeField] private float visibilityExitSafeOutset = 8f;
    [SerializeField] private float visibilityEnterOverlapPadding = 2f;

    [Header("Compact Row")]
    [SerializeField] private float rowHeight = 28f;
    [SerializeField] private float rowCenterSpacing = 30f;
    [SerializeField] private float minRowWidth = 56f;
    [SerializeField] private float maxRowWidth = 212f;
    [SerializeField] private float horizontalTextPadding = 8f;

    private readonly List<int> activeInstanceIds = new List<int>(AuthoredCapacity);
    private readonly List<WorldItemNameplateDisplayOption> displayOptions = new List<WorldItemNameplateDisplayOption>(AuthoredCapacity);
    private readonly List<WorldItemPickup> rendererCachePickups = new List<WorldItemPickup>(AuthoredCapacity);
    private readonly Dictionary<int, float> pickupDistances = new Dictionary<int, float>(AuthoredCapacity);
    private readonly List<WorldItemNameplateLayoutCandidate> layoutCandidates = new List<WorldItemNameplateLayoutCandidate>(AuthoredCapacity);
    private readonly List<WorldItemNameplatePlacement> placements = new List<WorldItemNameplatePlacement>(AuthoredCapacity);
    private readonly HashSet<int> screenVisiblePickupIds = new HashSet<int>();
    private readonly WorldItemPickupHoverHighlight pickupHoverHighlight = new WorldItemPickupHoverHighlight();
    private readonly WorldItemPickupHoverResolver pickupHoverResolver = new WorldItemPickupHoverResolver();
    private readonly WorldItemNameplateVisibilityHysteresis visibilityHysteresis = new WorldItemNameplateVisibilityHysteresis();

    private readonly WorldItemNameplateStableOrderRegistry stableOrderRegistry = new WorldItemNameplateStableOrderRegistry();
    private readonly WorldItemNameplateDisplaySet displaySet = new WorldItemNameplateDisplaySet();
    private PlayerPickupInteractor subscribedCore;
    private WorldItemNameplateBridge subscribedBridge;
    private WorldLootInteractionSnapshot snapshot;
    private WorldItemPickup rowHoverOverride;
    private int evaluatedActiveSetRevision = int.MinValue;
    private WorldLootInteractionMode evaluatedMode;
    private Rect evaluatedSafeRect;
    private bool hasEvaluatedDisplaySet;
    private int observedSelectedInstanceId;
    private int observedPendingInstanceId;

    private void Awake()
    {
        ResolveReferences();
        pickupHoverHighlight.SetOutlineMaterial(pickupHoverOutlineMaterial);
        BindBridgeEvents(bridge);
        view?.BindBridge(bridge);
    }

    private void OnEnable()
    {
        ResolveReferences();
        pickupHoverHighlight.SetOutlineMaterial(pickupHoverOutlineMaterial);
        BindBridgeEvents(bridge);
        view?.BindBridge(bridge);
        BindCore(PlayerPickupInteractor.ActiveCore);
    }

    private void OnDisable()
    {
        BindCore(null);
        BindBridgeEvents(null);
        rowHoverOverride = null;
        pickupHoverResolver.Clear();
        pickupHoverHighlight.Clear();
        visibilityHysteresis.Clear();
        view?.HideAll();
        hasEvaluatedDisplaySet = false;
        evaluatedActiveSetRevision = int.MinValue;
    }

    private void LateUpdate()
    {
        PlayerPickupInteractor activeCore = PlayerPickupInteractor.ActiveCore;
        if (activeCore != subscribedCore)
            BindCore(activeCore);

        if (subscribedCore != null)
            snapshot = subscribedCore.CurrentSnapshot; // 강조·거리·투영만 최신값 사용

        RenderSnapshot();
    }

    public static bool ShouldDisplay(
        WorldLootInteractionMode mode,
        bool isPersistentLabelTarget,
        bool isHovered)
    {
        if (mode == WorldLootInteractionMode.LootFocus)
            return true;

        if (mode == WorldLootInteractionMode.CombatAutoLegendary)
            return isPersistentLabelTarget || isHovered;

        return isHovered; // CombatForcedHidden은 hover 하나만 표시
    }

    public static bool IsPointerClickable(
        WorldLootInteractionMode mode,
        bool isInputBlocked,
        bool canPickup)
    {
        return mode == WorldLootInteractionMode.LootFocus
            && !isInputBlocked
            && canPickup; // 거리 밖도 50 자동 이동 요청 가능
    }

    public static bool ShouldSuppressAllPresentation(bool isInputBlocked)
    {
        return isInputBlocked;
    }

    private void RenderSnapshot()
    {
        if (view == null
            || view.RowsRoot == null
            || snapshot == null
            || snapshot.ActivePickups == null)
        {
            ClearPresentationState();
            return;
        }

        if (ShouldSuppressAllPresentation(snapshot.IsInputBlocked))
        {
            ClearPresentationState();
            return;
        }

        Camera camera = ResolveCamera();
        if (camera == null)
        {
            ClearPresentationState();
            return;
        }

        Rect safeRect = ResolveSafeRect(view.RowsRoot.rect, safeMargin);
        SynchronizeStableState(safeRect);
        SynchronizeRendererCache();
        pickupHoverResolver.CollectScreenVisibleInstanceIds(camera, screenVisiblePickupIds);
        BuildPickupDistances();
        WorldItemPickup worldHoveredPickup = ResolveWorldHoveredPickup(camera);
        ValidateRowHoverOverride();
        WorldItemPickup outlineTarget = rowHoverOverride != null ? rowHoverOverride : worldHoveredPickup;
        ApplyOutlineTarget(outlineTarget);
        int hoveredInstanceId = GetInstanceId(worldHoveredPickup);
        int emphasizedInstanceId = GetInstanceId(outlineTarget);
        BuildLayoutCandidates(camera, hoveredInstanceId, emphasizedInstanceId);

        WorldItemNameplateLayout.Build(
            layoutCandidates,
            Mathf.Min(AuthoredCapacity, view.AuthoredRowCount),
            safeRect,
            rowCenterSpacing,
            placements,
            visibilityHysteresis,
            visibilityEnterSafeInset,
            visibilityExitSafeOutset,
            visibilityEnterOverlapPadding);
        WorldItemNameplateVisibilityRegistry.Publish(
            placements,
            Mathf.Min(AuthoredCapacity, view.AuthoredRowCount));
        bool allowPointerInput = IsPointerClickable(snapshot.Mode, false, true);
        view.Render(placements, allowPointerInput);
    }

    private void SynchronizeStableState(Rect safeRect)
    {
        bool activeSetChanged = snapshot.ActiveSetRevision != evaluatedActiveSetRevision;
        if (activeSetChanged || !hasEvaluatedDisplaySet)
        {
            activeInstanceIds.Clear();
            for (int i = 0; i < snapshot.ActivePickups.Count; i++)
            {
                WorldItemPickup pickup = snapshot.ActivePickups[i].Pickup;
                if (pickup == null || !snapshot.ActivePickups[i].CanPickup)
                    continue;

                activeInstanceIds.Add(pickup.GetInstanceID());
            }

            stableOrderRegistry.Synchronize(activeInstanceIds); // 50 ActiveSetRevision에서만 안정 순서 갱신
        }

        BuildDisplayOptions();
        bool modeChanged = !hasEvaluatedDisplaySet || snapshot.Mode != evaluatedMode;
        bool safeAreaChanged = !hasEvaluatedDisplaySet || !Approximately(evaluatedSafeRect, safeRect);
        if (activeSetChanged || modeChanged || safeAreaChanged || !hasEvaluatedDisplaySet)
        {
            displaySet.Rebuild(displayOptions, AuthoredCapacity);
            evaluatedActiveSetRevision = snapshot.ActiveSetRevision;
            evaluatedMode = snapshot.Mode;
            evaluatedSafeRect = safeRect;
            hasEvaluatedDisplaySet = true;
        }
        else
        {
            int pendingId = GetInstanceId(snapshot.PendingAutoMovePickup);
            if (pendingId != observedPendingInstanceId)
                displaySet.EnsureGuaranteed(displayOptions, pendingId, AuthoredCapacity, out _);

            int selectedId = GetInstanceId(snapshot.SelectedPickup);
            if (selectedId != observedSelectedInstanceId)
                displaySet.EnsureGuaranteed(displayOptions, selectedId, AuthoredCapacity, out _); // 최대 1개 교체
        }

        observedPendingInstanceId = GetInstanceId(snapshot.PendingAutoMovePickup);
        observedSelectedInstanceId = GetInstanceId(snapshot.SelectedPickup);
    }

    private void BuildDisplayOptions()
    {
        displayOptions.Clear();
        for (int i = 0; i < snapshot.ActivePickups.Count; i++)
        {
            WorldLootPickupSnapshot pickupSnapshot = snapshot.ActivePickups[i];
            WorldItemPickup pickup = pickupSnapshot.Pickup;
            int instanceId = GetInstanceId(pickup);
            if (instanceId == 0
                || !pickupSnapshot.CanPickup
                || !stableOrderRegistry.TryGetStableOrder(instanceId, out int stableOrder))
            {
                continue;
            }

            bool eligible = snapshot.Mode == WorldLootInteractionMode.LootFocus
                || snapshot.Mode == WorldLootInteractionMode.CombatAutoLegendary
                    && pickupSnapshot.IsPersistentLabelTarget;
            displayOptions.Add(new WorldItemNameplateDisplayOption(
                instanceId,
                stableOrder,
                pickup.Grade,
                eligible,
                pickup == snapshot.SelectedPickup,
                pickup == snapshot.PendingAutoMovePickup));
        }
    }

    private void BuildLayoutCandidates(Camera camera, int hoveredInstanceId, int emphasizedInstanceId)
    {
        layoutCandidates.Clear();
        for (int i = 0; i < snapshot.ActivePickups.Count; i++)
        {
            WorldLootPickupSnapshot pickupSnapshot = snapshot.ActivePickups[i];
            WorldItemPickup pickup = pickupSnapshot.Pickup;
            int instanceId = GetInstanceId(pickup);
            if (instanceId == 0
                || !pickupSnapshot.CanPickup
                || !screenVisiblePickupIds.Contains(instanceId)
                || !stableOrderRegistry.TryGetStableOrder(instanceId, out int stableOrder))
            {
                continue;
            }

            bool hovered = instanceId == hoveredInstanceId;
            bool stableVisible = displaySet.Contains(instanceId);
            bool temporaryHover = hovered
                && snapshot.Mode != WorldLootInteractionMode.LootFocus
                && !stableVisible;
            if (!stableVisible && !temporaryHover)
                continue;

            if (!TryProjectLocalPoint(camera, pickup.transform.position + worldOffset, out Vector2 anchorPosition))
                continue;

            string displayText = BuildDisplayText(pickup);
            Vector2 size = view.MeasureRowSize(
                displayText,
                minRowWidth,
                maxRowWidth,
                horizontalTextPadding,
                rowHeight);
            layoutCandidates.Add(new WorldItemNameplateLayoutCandidate(
                pickup,
                displayText,
                anchorPosition,
                size,
                pickupSnapshot.Distance,
                instanceId,
                stableOrder,
                pickup.Grade,
                pickup == snapshot.SelectedPickup,
                pickup == snapshot.PendingAutoMovePickup,
                temporaryHover,
                pickupSnapshot.IsWithinPickupRange,
                instanceId == emphasizedInstanceId));
        }
    }

    private void SynchronizeRendererCache()
    {
        if (pickupHoverResolver.SynchronizedRevision == snapshot.ActiveSetRevision)
            return;

        rendererCachePickups.Clear();
        for (int i = 0; i < snapshot.ActivePickups.Count; i++)
        {
            WorldLootPickupSnapshot pickupSnapshot = snapshot.ActivePickups[i];
            WorldItemPickup pickup = pickupSnapshot.Pickup;
            if (pickup == null || !pickupSnapshot.CanPickup)
                continue;

            rendererCachePickups.Add(pickup);
        }

        pickupHoverResolver.Synchronize(snapshot.ActiveSetRevision, rendererCachePickups);
    }

    private void BuildPickupDistances()
    {
        pickupDistances.Clear();
        for (int i = 0; i < snapshot.ActivePickups.Count; i++)
        {
            WorldLootPickupSnapshot pickupSnapshot = snapshot.ActivePickups[i];
            int instanceId = GetInstanceId(pickupSnapshot.Pickup);
            if (instanceId != 0 && pickupSnapshot.CanPickup)
                pickupDistances[instanceId] = pickupSnapshot.Distance;
        }
    }

    private bool TryProjectLocalPoint(Camera camera, Vector3 worldPosition, out Vector2 localPoint)
    {
        if (!TryProjectScreenPoint(camera, worldPosition, out Vector2 screenPoint))
        {
            localPoint = default;
            return false;
        }

        Canvas canvas = view.RowsRoot.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera != null ? canvas.worldCamera : camera
            : null;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            view.RowsRoot,
            screenPoint,
            eventCamera,
            out localPoint);
    }

    private WorldItemPickup ResolveWorldHoveredPickup(Camera camera)
    {
        // GOAL A2: Mouse 직접 읽기 대신 UI Point 포인터 위치를 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null)
            return null;

        return pickupHoverResolver.Resolve(
            camera,
            facade.PointerPosition,
            hoverBoundsPaddingPixels,
            hoverMinimumSizePixels,
            pickupDistances);
    }

    private void ApplyOutlineTarget(WorldItemPickup target)
    {
        if (target == null || !pickupHoverResolver.TryGetRenderers(target, out Renderer[] renderers))
        {
            pickupHoverHighlight.Clear();
            return;
        }

        pickupHoverHighlight.SetTarget(target, renderers);
        pickupHoverHighlight.RefreshSourceState();
    }

    private void ValidateRowHoverOverride()
    {
        if (rowHoverOverride == null)
            return;

        if (snapshot.Mode != WorldLootInteractionMode.LootFocus
            || snapshot.IsInputBlocked
            || !pickupHoverResolver.Contains(rowHoverOverride))
        {
            rowHoverOverride = null;
        }
    }

    private void BindBridgeEvents(WorldItemNameplateBridge target)
    {
        if (subscribedBridge != null)
        {
            subscribedBridge.PointerHoverEntered -= HandleRowPointerEnter;
            subscribedBridge.PointerHoverExited -= HandleRowPointerExit;
        }

        subscribedBridge = target;
        if (subscribedBridge != null)
        {
            subscribedBridge.PointerHoverEntered += HandleRowPointerEnter;
            subscribedBridge.PointerHoverExited += HandleRowPointerExit;
        }
    }

    private void HandleRowPointerEnter(WorldItemPickup pickup)
    {
        if (snapshot != null
            && snapshot.Mode == WorldLootInteractionMode.LootFocus
            && !snapshot.IsInputBlocked
            && pickupHoverResolver.Contains(pickup))
        {
            rowHoverOverride = pickup;
        }
    }

    private void HandleRowPointerExit(WorldItemPickup pickup)
    {
        if (rowHoverOverride == pickup)
            rowHoverOverride = null;
    }

    private void ClearPresentationState()
    {
        rowHoverOverride = null;
        placements.Clear();
        layoutCandidates.Clear();
        pickupHoverHighlight.Clear();
        pickupHoverResolver.Clear();
        visibilityHysteresis.Clear();
        view?.HideAll();
    }

    private void BindCore(PlayerPickupInteractor core)
    {
        WorldItemNameplateVisibilityRegistry.Clear();
        if (subscribedCore != null)
            subscribedCore.SnapshotChanged -= HandleSnapshotChanged;

        subscribedCore = core;
        stableOrderRegistry.Clear();
        displaySet.Clear();
        rowHoverOverride = null;
        pickupHoverResolver.Clear();
        rendererCachePickups.Clear();
        pickupDistances.Clear();
        pickupHoverHighlight.Clear();
        snapshot = subscribedCore != null ? subscribedCore.CurrentSnapshot : null;
        if (subscribedCore != null)
            subscribedCore.SnapshotChanged += HandleSnapshotChanged;

        hasEvaluatedDisplaySet = false;
        evaluatedActiveSetRevision = int.MinValue;
    }

    private void HandleSnapshotChanged(WorldLootInteractionSnapshot value)
    {
        snapshot = value;
    }

    private Camera ResolveCamera()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        return targetCamera;
    }

    private void ResolveReferences()
    {
        if (view == null)
            view = GetComponent<WorldItemNameplateView>();
        if (bridge == null)
            bridge = GetComponent<WorldItemNameplateBridge>();
    }

    private WorldItemPickup FindPickup(int instanceId)
    {
        for (int i = 0; i < snapshot.ActivePickups.Count; i++)
        {
            WorldItemPickup pickup = snapshot.ActivePickups[i].Pickup;
            if (GetInstanceId(pickup) == instanceId)
                return pickup;
        }

        return null;
    }

    private static bool TryProjectScreenPoint(Camera camera, Vector3 worldPosition, out Vector2 screenPoint)
    {
        Vector3 screen = camera.WorldToScreenPoint(worldPosition);
        if (screen.z <= 0f
            || float.IsNaN(screen.x)
            || float.IsNaN(screen.y)
            || float.IsInfinity(screen.x)
            || float.IsInfinity(screen.y))
        {
            screenPoint = default;
            return false;
        }

        screenPoint = new Vector2(screen.x, screen.y);
        return true;
    }

    private static string BuildDisplayText(WorldItemPickup pickup)
    {
        if (pickup == null)
            return string.Empty;

        return pickup.StackCount > 1
            ? pickup.DisplayName + " ×" + pickup.StackCount
            : pickup.DisplayName;
    }

    private static int GetInstanceId(WorldItemPickup pickup)
    {
        return pickup != null ? pickup.GetInstanceID() : 0;
    }

    private static Rect ResolveSafeRect(Rect rootRect, float margin)
    {
        float resolvedMargin = Mathf.Max(0f, margin);
        return Rect.MinMaxRect(
            rootRect.xMin + resolvedMargin,
            rootRect.yMin + resolvedMargin,
            rootRect.xMax - resolvedMargin,
            rootRect.yMax - resolvedMargin);
    }

    private static bool Approximately(Rect left, Rect right)
    {
        return Mathf.Approximately(left.xMin, right.xMin)
            && Mathf.Approximately(left.xMax, right.xMax)
            && Mathf.Approximately(left.yMin, right.yMin)
            && Mathf.Approximately(left.yMax, right.yMax);
    }
}
