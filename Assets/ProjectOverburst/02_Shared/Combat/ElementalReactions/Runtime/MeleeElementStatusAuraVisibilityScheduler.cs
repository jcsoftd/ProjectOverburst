using UnityEngine;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-9400)]
public sealed class MeleeElementStatusAuraVisibilityScheduler : MonoBehaviour
{
    public const int DefaultChecksPerFrame = 256;
    public const int DefaultMaxVisiblePresentations = 24;
    public const float DefaultEnterDistance = 42f;
    public const float DefaultExitDistance = 48f;
    public const float DefaultEnterViewportMargin = 0.03f;
    public const float DefaultExitViewportMargin = 0.10f;
    public const int DefaultBootPrewarm = 16;
    public const int DefaultMaxCreatesPerFrame = 4;
    private const float WarmTargetInterval = 0.5f;
    // First play of each effect (shader/material first use, particle buffers) cost 10-30 ms once per session.
    // At boot, play the three shown auras once far outside the world, seen only by a tiny off-screen camera.
    private const float WarmPlaySeconds = 0.6f;
    private const int WarmPlayMinFrames = 10;
    private static readonly Vector3 WarmPlayPosition = new Vector3(10000f, -10000f, 10000f);
    private static readonly MeleeElementStatusAuraType[] WarmPlayTypes =
    {
        MeleeElementStatusAuraType.Burning, MeleeElementStatusAuraType.Shocked, MeleeElementStatusAuraType.Corroded
    };
    private static readonly Unity.Profiling.ProfilerMarker WarmMarker =
        new Unity.Profiling.ProfilerMarker("Overburst.Cost.Aura.Warm");
    private static readonly System.Collections.Generic.List<CombatTarget> WarmCandidates =
        new System.Collections.Generic.List<CombatTarget>(128);

    private const int ControllerCapacity = 4096;
    private const int PresentationCapacity = 256;
    private const string PresentationResourcePath =
        "Combat/VFX/PF_VFX_MeleeElementStatusAura";
    private static MeleeElementStatusAuraVisibilityScheduler instance;

    [SerializeField, Min(1)] private int checksPerFrame = DefaultChecksPerFrame;
    [SerializeField, Range(1, PresentationCapacity)]
    private int maxVisiblePresentations = DefaultMaxVisiblePresentations;
    // Comparison-only prototype. Product defaults preserve the original effect for every visible owner.
    [SerializeField] private bool experimentalSimplifiedOverflow;
    [SerializeField, Min(0f)] private float enterDistance = DefaultEnterDistance;
    [SerializeField, Min(0f)] private float exitDistance = DefaultExitDistance;
    [SerializeField, Range(0f, 0.25f)] private float enterViewportMargin = DefaultEnterViewportMargin;
    [SerializeField, Range(0f, 0.25f)] private float exitViewportMargin = DefaultExitViewportMargin;
    // Warm pool: a presentation costs ~1.3 ms to instantiate, so 100 statuses landing at once used to
    // freeze one frame (154 ms). Build some at boot, top the pool up to the enemies on screen one per frame,
    // and cap how many a single frame may still instantiate when the pool is short.
    [SerializeField, Range(0, 64)] private int bootPrewarm = DefaultBootPrewarm;
    [SerializeField, Range(1, 16)] private int maxCreatesPerFrame = DefaultMaxCreatesPerFrame;

    private readonly MeleeElementStatusAuraController[] activeControllers =
        new MeleeElementStatusAuraController[ControllerCapacity];
    private readonly MeleeElementStatusAuraPresentation[] inactivePresentations =
        new MeleeElementStatusAuraPresentation[PresentationCapacity];
    private readonly MeleeElementStatusAuraPresentation[] pendingReturns =
        new MeleeElementStatusAuraPresentation[PresentationCapacity];
    private int pendingReturnCount;
    private int activeControllerCount;
    private int roundRobinCursor;
    private struct VisibleCandidate { public MeleeElementStatusAuraController Owner; public float Distance; }
    private sealed class CandidateComparer : System.Collections.Generic.IComparer<VisibleCandidate>
    { public int Compare(VisibleCandidate a, VisibleCandidate b) => a.Distance.CompareTo(b.Distance); }
    private static readonly CandidateComparer PriorityComparer = new CandidateComparer();
    private readonly bool[] visibility = new bool[ControllerCapacity];
    private readonly VisibleCandidate[] visibleCandidates = new VisibleCandidate[ControllerCapacity];
    private readonly ElementStatusBillboards billboards = new ElementStatusBillboards();
    public int LowCostVisibleCount => billboards.LastCount;
    private int inactivePresentationCount;
    private int createdPresentationCount;
    private int leasedPresentationCount;
    private Camera cachedCamera;
    private MeleeElementStatusAuraPresentation presentationPrefab;
    private bool presentationLoadFailed;
    private int warmTarget;
    private float nextWarmTargetTime;
    private int createFrame = -1;
    private int createdThisFrame;
    private bool warmPoolEnabled = true;
    private MeleeElementStatusAuraPresentation warmPlayPresentation;
    private GameObject warmPlayCamera;
    private RenderTexture warmPlayTarget;
    private float warmPlayUntil;
    private int warmPlayEndFrame;
    private bool warmPlayDone;

#if UNITY_EDITOR
    public bool WarmPlayDoneForValidation => warmPlayDone;
    public int WarmTargetForValidation => warmTarget;
    public bool WarmPoolEnabledForValidation { get => warmPoolEnabled; set => warmPoolEnabled = value; }
    public int LastCheckedCountForValidation { get; private set; }
    public int TotalCheckedCountForValidation { get; private set; }
    public int CreatedPresentationCountForValidation => createdPresentationCount;
    public int LeasedPresentationCountForValidation => leasedPresentationCount;
#endif

    public static int RegisteredControllerCount => instance != null
        ? instance.activeControllerCount
        : 0;
    public static int LeasedPresentationCount => instance != null
        ? instance.leasedPresentationCount
        : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    // Boot is a loading moment: pay for the first presentations (and the prefab load) here, not on the first hit.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void PrewarmAtBoot()
    {
        EnsureRuntimeService();
        if (instance == null)
            return;
        for (int i = 0; i < instance.bootPrewarm; i++)
            if (!instance.CreatePooledPresentation())
                break;
        instance.BeginWarmPlay();
    }

    private void BeginWarmPlay()
    {
        if (warmPlayDone || warmPlayPresentation != null)
            return;
        if (inactivePresentationCount == 0 && !CreatePooledPresentation())
        {
            warmPlayDone = true;
            return;
        }
        warmPlayPresentation = inactivePresentations[--inactivePresentationCount];
        inactivePresentations[inactivePresentationCount] = null;

        warmPlayTarget = new RenderTexture(128, 128, 16) { name = "ElementStatusAura_WarmTarget" };
        warmPlayCamera = new GameObject("ElementStatusAura_WarmCamera");
        warmPlayCamera.transform.SetParent(transform, false);
        warmPlayCamera.transform.position = WarmPlayPosition + new Vector3(0f, 1.5f, -4f);
        warmPlayCamera.transform.LookAt(WarmPlayPosition + Vector3.up);
        Camera camera = warmPlayCamera.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 20f;
        camera.depth = -100f;
        camera.targetTexture = warmPlayTarget;

        Transform warmTransform = warmPlayPresentation.transform;
        warmTransform.position = WarmPlayPosition;
        warmTransform.rotation = Quaternion.identity;
        warmTransform.localScale = Vector3.one;
        warmPlayPresentation.gameObject.SetActive(true);
        warmPlayPresentation.ClearAllAuras();
        // Ice uses a tint, not the chilled aura, so only the three auras the game shows are warmed.
        for (int i = 0; i < WarmPlayTypes.Length; i++)
        {
            warmPlayPresentation.SetStackCount(WarmPlayTypes[i], 5);
            warmPlayPresentation.SetAuraActive(WarmPlayTypes[i], true, true);
        }
        // Boot frames are long and few, and a rate-driven effect (FireAura) may not have emitted yet, so nothing
        // would be drawn and its materials would still load on the first real hit. Fast-forward every system.
        foreach (ParticleSystem particle in warmPlayPresentation.GetComponentsInChildren<ParticleSystem>(false))
        {
            particle.Simulate(.3f, false, false, false);
            particle.Play(false);
        }
        warmPlayEndFrame = Time.frameCount + WarmPlayMinFrames;
        warmPlayUntil = Time.unscaledTime + WarmPlaySeconds;
    }

    private void UpdateWarmPlay()
    {
        if (warmPlayPresentation != null && Time.unscaledTime >= warmPlayUntil && Time.frameCount >= warmPlayEndFrame)
            EndWarmPlay();
    }

    private void EndWarmPlay()
    {
        if (warmPlayPresentation != null)
        {
            warmPlayPresentation.ClearAllAuras();
            warmPlayPresentation.gameObject.SetActive(false);
            CacheReturnedPresentation(warmPlayPresentation);
            warmPlayPresentation = null;
        }
        if (warmPlayCamera != null)
            DestroyObject(warmPlayCamera);
        warmPlayCamera = null;
        if (warmPlayTarget != null)
        {
            warmPlayTarget.Release();
            if (Application.isPlaying) Destroy(warmPlayTarget);
            else DestroyImmediate(warmPlayTarget);
        }
        warmPlayTarget = null;
        warmPlayDone = true;
    }

    private static void EnsureRuntimeService()
    {
        if (instance != null)
            return;
        GameObject serviceObject = new GameObject("MeleeElementStatusAuraVisibilityScheduler");
        Object.DontDestroyOnLoad(serviceObject);
        instance = serviceObject.AddComponent<MeleeElementStatusAuraVisibilityScheduler>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        NormalizeSettings();
    }

    private void OnDisable()
    {
        ClearRegistrations();
    }

    private void OnDestroy()
    {
        billboards.Dispose();
        ClearRegistrations();
        EndWarmPlay();
        DestroyPooledPresentations();
        if (instance == this)
            instance = null;
    }

    private void OnValidate()
    {
        NormalizeSettings();
    }

    private void Update()
    {
        Camera camera = ResolveCamera();
        Advance(camera, checksPerFrame);
        UpdateWarmPlay();
        MaintainWarmPool(camera);
    }

    // Keep enough presentations for every enemy on screen; at most one is built per frame, and none
    // in a frame that already had to instantiate for a lease.
    private void MaintainWarmPool(Camera camera)
    {
        if (!warmPoolEnabled)
            return;
        if (Time.unscaledTime >= nextWarmTargetTime)
        {
            nextWarmTargetTime = Time.unscaledTime + WarmTargetInterval;
            warmTarget = Mathf.Min(inactivePresentations.Length,
                Mathf.Max(bootPrewarm, CountVisibleEnemies(camera)));
        }
        if (createdPresentationCount < warmTarget && CreatesThisFrame() == 0)
            CreatePooledPresentation();
    }

    private int CountVisibleEnemies(Camera camera)
    {
        if (camera == null)
            return 0;
        CombatTargetRegistry.CollectTeamTargets(CombatTeam.Enemy, WarmCandidates);
        int count = 0;
        // Exit margins (wider) so enemies about to walk into view are counted too.
        for (int i = 0; i < WarmCandidates.Count; i++)
            if (ResolveVisibility(camera, WarmCandidates[i].WorldCenter, true))
                count++;
        WarmCandidates.Clear();
        return count;
    }

    private int CreatesThisFrame()
    {
        if (createFrame != Time.frameCount)
        {
            createFrame = Time.frameCount;
            createdThisFrame = 0;
        }
        return createdThisFrame;
    }

    private bool CreatePooledPresentation()
    {
        if (inactivePresentationCount >= inactivePresentations.Length)
            return false;
        MeleeElementStatusAuraPresentation created = InstantiatePresentation();
        if (created == null)
            return false;
        // Built active so Awake caches its modules now; the lease path then only reactivates it.
        created.ClearAllAuras();
        created.gameObject.SetActive(false);
        inactivePresentations[inactivePresentationCount++] = created;
        return true;
    }

    private MeleeElementStatusAuraPresentation InstantiatePresentation()
    {
        MeleeElementStatusAuraPresentation prefab = ResolvePresentationPrefab();
        if (prefab == null)
            return null;
        using (WarmMarker.Auto())
        {
            MeleeElementStatusAuraPresentation created = Instantiate(prefab, transform);
            created.name = "ElementStatusAura_Pooled";
            createdPresentationCount++;
            CreatesThisFrame();
            createdThisFrame++;
            return created;
        }
    }

    internal static bool Register(MeleeElementStatusAuraController controller)
    {
        if (controller == null || !controller.HasLogicalAura)
            return false;
        if (instance == null)
        {
            if (!Application.isPlaying)
                return false;
            EnsureRuntimeService();
        }
        return instance != null && instance.RegisterInternal(controller);
    }

    internal static void Unregister(MeleeElementStatusAuraController controller)
    {
        if (instance != null)
            instance.UnregisterInternal(controller);
        else if (controller != null)
            controller.SetSchedulerIndex(-1);
    }

    internal static MeleeElementStatusAuraPresentation TryLeasePresentation(
        MeleeElementStatusAuraController owner)
    {
        return instance != null ? instance.LeasePresentation(owner) : null;
    }

    internal static void ReleasePresentation(MeleeElementStatusAuraPresentation presentation)
    {
        if (instance != null)
            instance.ReturnPresentation(presentation);
        else if (presentation != null)
            DestroyObject(presentation.gameObject);
    }

    private bool RegisterInternal(MeleeElementStatusAuraController controller)
    {
        int existingIndex = controller.SchedulerIndex;
        if (existingIndex >= 0
            && existingIndex < activeControllerCount
            && activeControllers[existingIndex] == controller)
            return true;

        if (activeControllerCount >= activeControllers.Length)
        {
            controller.ReleasePresentation();
            controller.SetSchedulerIndex(-1);
            return true;
        }

        int index = activeControllerCount++;
        activeControllers[index] = controller;
        controller.SetSchedulerIndex(index);
        controller.ReleasePresentation();
        return true;
    }

    private void UnregisterInternal(MeleeElementStatusAuraController controller)
    {
        if (controller == null)
            return;
        controller.ReleasePresentation();
        int index = controller.SchedulerIndex;
        if (index < 0
            || index >= activeControllerCount
            || activeControllers[index] != controller)
        {
            controller.SetSchedulerIndex(-1);
            return;
        }
        RemoveAt(index);
    }

    private void RemoveAt(int index)
    {
        int lastIndex = --activeControllerCount;
        MeleeElementStatusAuraController removed = activeControllers[index];
        if (index != lastIndex)
        {
            MeleeElementStatusAuraController moved = activeControllers[lastIndex];
            activeControllers[index] = moved;
            visibility[index] = visibility[lastIndex];
            if (moved != null)
                moved.SetSchedulerIndex(index);
        }
        activeControllers[lastIndex] = null;
        visibility[lastIndex] = false;
        if (removed != null)
            removed.SetSchedulerIndex(-1);
        if (activeControllerCount == 0 || roundRobinCursor >= activeControllerCount)
            roundRobinCursor = 0;
    }

    private void Advance(Camera camera, int budget)
    {
        FlushPendingReturns();
        int checkCount = Mathf.Min(Mathf.Max(0, budget), activeControllerCount);
#if UNITY_EDITOR
        LastCheckedCountForValidation = 0;
#endif
        for (int checkedCount = 0; checkedCount < checkCount && activeControllerCount > 0; checkedCount++)
        {
            if (roundRobinCursor >= activeControllerCount)
                roundRobinCursor = 0;
            MeleeElementStatusAuraController controller = activeControllers[roundRobinCursor];
#if UNITY_EDITOR
            LastCheckedCountForValidation++;
            TotalCheckedCountForValidation++;
#endif
            if (controller == null || !controller.isActiveAndEnabled || !controller.HasLogicalAura)
            {
                if (controller != null)
                    controller.ReleasePresentation();
                RemoveAt(roundRobinCursor);
                continue;
            }
            bool visible = ResolveVisibility(
                camera, controller.VisibilityPosition, controller.IsPresentationVisible);
            visibility[roundRobinCursor] = visible;
            roundRobinCursor++;
        }
        if (!experimentalSimplifiedOverflow)
        {
            billboards.Begin();
            for (int i = 0; i < activeControllerCount; i++)
                if (activeControllers[i] != null)
                    activeControllers[i].ApplyScheduledVisibility(visibility[i]);
            return;
        }
        int visibleCount = 0;
        Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
        for (int i = 0; i < activeControllerCount; i++)
        {
            var owner = activeControllers[i];
            if (owner == null) continue;
            if (!visibility[i]) { owner.ReleasePresentation(); continue; }
            visibleCandidates[visibleCount++] = new VisibleCandidate { Owner = owner,
                Distance = (owner.VisibilityPosition - cameraPosition).sqrMagnitude };
        }
        System.Array.Sort(visibleCandidates, 0, visibleCount, PriorityComparer);
        // Return distant leases before renting closer ones, so the budget is actually reusable.
        for (int i = maxVisiblePresentations; i < visibleCount; i++) visibleCandidates[i].Owner.ReleasePresentation();
        billboards.Begin();
        Quaternion rotation = camera != null ? camera.transform.rotation : Quaternion.identity;
        for (int i = 0; i < visibleCount; i++)
        {
            var owner = visibleCandidates[i].Owner;
            if (i < maxVisiblePresentations) owner.ApplyScheduledVisibility(true);
            if (!owner.IsPresentationVisible)
                billboards.Add(owner.VisibilityPosition, rotation,
                    owner.IsAuraActive(MeleeElementStatusAuraType.Burning), owner.IsAuraActive(MeleeElementStatusAuraType.Shocked));
            visibleCandidates[i] = default;
        }
        billboards.Draw(camera);
    }

    private MeleeElementStatusAuraPresentation LeasePresentation(
        MeleeElementStatusAuraController owner)
    {
        int leaseLimit = experimentalSimplifiedOverflow ? maxVisiblePresentations : ControllerCapacity;
        if (owner == null || leasedPresentationCount >= leaseLimit)
            return null;

        MeleeElementStatusAuraPresentation leased = null;
        while (inactivePresentationCount > 0 && leased == null)
        {
            int index = --inactivePresentationCount;
            leased = inactivePresentations[index];
            inactivePresentations[index] = null;
        }

        if (leased == null)
        {
            // Pool short (a burst before the warm pool caught up): build a few this frame; the other owners
            // retry on the next scheduler pass and show their aura a few frames later instead of freezing one.
            if (createdPresentationCount >= leaseLimit || CreatesThisFrame() >= maxCreatesPerFrame)
                return null;
            leased = InstantiatePresentation();
            if (leased == null)
                return null;
        }

        leasedPresentationCount++;
        Transform leasedTransform = leased.transform;
        leasedTransform.SetParent(owner.transform, false);
        leasedTransform.localPosition = Vector3.zero;
        leasedTransform.localRotation = Quaternion.identity;
        leasedTransform.localScale = Vector3.one;
        if (!leased.gameObject.activeSelf)
            leased.gameObject.SetActive(true);
        leased.ClearAllAuras();
        leased.ConfigureTarget(owner.GetComponent<CombatTarget>());
        return leased;
    }

    private void ReturnPresentation(MeleeElementStatusAuraPresentation presentation)
    {
        if (presentation == null)
            return;
        presentation.ClearAllAuras();
        bool deferParentChange = !presentation.gameObject.activeInHierarchy || !isActiveAndEnabled;
        presentation.gameObject.SetActive(false);
        leasedPresentationCount = Mathf.Max(0, leasedPresentationCount - 1);
        // A parent's OnDisable cannot reparent its children. Retire the lease now,
        // but only make it available for reuse after a safe scheduler update.
        if (deferParentChange)
        {
            if (pendingReturnCount < pendingReturns.Length)
                pendingReturns[pendingReturnCount++] = presentation;
            else
            {
                createdPresentationCount = Mathf.Max(0, createdPresentationCount - 1);
                DestroyObject(presentation.gameObject);
            }
            return;
        }
        CacheReturnedPresentation(presentation);
    }

    private void FlushPendingReturns()
    {
        while (pendingReturnCount > 0)
        {
            int index = --pendingReturnCount;
            MeleeElementStatusAuraPresentation returned = pendingReturns[index];
            pendingReturns[index] = null;
            // Scene/owner destruction may have destroyed the still-parented child.
            if (returned == null)
                createdPresentationCount = Mathf.Max(0, createdPresentationCount - 1);
            else
                CacheReturnedPresentation(returned);
        }
    }

    private void CacheReturnedPresentation(MeleeElementStatusAuraPresentation presentation)
    {
        presentation.transform.SetParent(transform, false);
        if (inactivePresentationCount < inactivePresentations.Length)
            inactivePresentations[inactivePresentationCount++] = presentation;
        else
        {
            createdPresentationCount = Mathf.Max(0, createdPresentationCount - 1);
            DestroyObject(presentation.gameObject);
        }
    }

    private MeleeElementStatusAuraPresentation ResolvePresentationPrefab()
    {
        if (presentationPrefab != null)
            return presentationPrefab;
        if (presentationLoadFailed)
            return null;
        presentationPrefab = Resources.Load<MeleeElementStatusAuraPresentation>(
            PresentationResourcePath);
        if (presentationPrefab == null)
        {
            presentationLoadFailed = true;
            Debug.LogError(
                "[ElementStatusAura] 공용 presentation prefab 누락: Resources/"
                + PresentationResourcePath);
        }
        return presentationPrefab;
    }

    private bool ResolveVisibility(Camera camera, Vector3 position, bool currentlyVisible)
    {
        if (camera == null)
            return false;
        Vector3 delta = position - camera.transform.position;
        float distanceLimit = currentlyVisible ? exitDistance : enterDistance;
        if (delta.sqrMagnitude > distanceLimit * distanceLimit)
            return false;
        Vector3 viewport = camera.WorldToViewportPoint(position);
        if (viewport.z <= 0f)
            return false;
        float margin = currentlyVisible ? exitViewportMargin : enterViewportMargin;
        return viewport.x >= -margin
            && viewport.x <= 1f + margin
            && viewport.y >= -margin
            && viewport.y <= 1f + margin;
    }

    private Camera ResolveCamera()
    {
        if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
            return cachedCamera;
        cachedCamera = Camera.main;
        return cachedCamera;
    }

    private void ClearRegistrations()
    {
        for (int i = 0; i < activeControllerCount; i++)
        {
            MeleeElementStatusAuraController controller = activeControllers[i];
            activeControllers[i] = null;
            if (controller == null)
                continue;
            controller.ReleasePresentation();
            controller.SetSchedulerIndex(-1);
        }
        activeControllerCount = 0;
        roundRobinCursor = 0;
        cachedCamera = null;
    }

    private void DestroyPooledPresentations()
    {
        for (int i = 0; i < pendingReturnCount; i++)
        {
            if (pendingReturns[i] != null)
                DestroyObject(pendingReturns[i].gameObject);
            pendingReturns[i] = null;
        }
        pendingReturnCount = 0;
        for (int i = 0; i < inactivePresentationCount; i++)
        {
            if (inactivePresentations[i] != null)
                DestroyObject(inactivePresentations[i].gameObject);
            inactivePresentations[i] = null;
        }
        inactivePresentationCount = 0;
        createdPresentationCount = leasedPresentationCount;
    }

    private static void DestroyObject(GameObject target)
    {
        if (target == null)
            return;
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private void NormalizeSettings()
    {
        checksPerFrame = Mathf.Max(1, checksPerFrame);
        maxVisiblePresentations = Mathf.Clamp(
            maxVisiblePresentations, 1, PresentationCapacity);
        enterDistance = Mathf.Max(0f, enterDistance);
        exitDistance = Mathf.Max(enterDistance, exitDistance);
        enterViewportMargin = Mathf.Clamp(enterViewportMargin, 0f, 0.25f);
        exitViewportMargin = Mathf.Clamp(
            exitViewportMargin, enterViewportMargin, 0.25f);
        bootPrewarm = Mathf.Clamp(bootPrewarm, 0, 64);
        maxCreatesPerFrame = Mathf.Clamp(maxCreatesPerFrame, 1, 16);
    }

#if UNITY_EDITOR
    public void InitializeForValidation(
        MeleeElementStatusAuraPresentation validationPrefab = null,
        int validationMaxVisiblePresentations = DefaultMaxVisiblePresentations)
    {
        if (instance != null && instance != this)
            instance.ClearRegistrations();
        instance = this;
        ClearRegistrations();
        DestroyPooledPresentations();
        checksPerFrame = DefaultChecksPerFrame;
        maxVisiblePresentations = Mathf.Clamp(
            validationMaxVisiblePresentations, 1, PresentationCapacity);
        enterDistance = DefaultEnterDistance;
        exitDistance = DefaultExitDistance;
        enterViewportMargin = DefaultEnterViewportMargin;
        exitViewportMargin = DefaultExitViewportMargin;
        presentationPrefab = validationPrefab;
        presentationLoadFailed = false;
        TotalCheckedCountForValidation = 0;
    }

    public void AdvanceForValidation(Camera camera, int budget = DefaultChecksPerFrame)
    {
        Advance(camera, budget);
    }

    // Empties the idle pool so a probe can measure the short-pool (capped) path.
    public void DrainPoolForValidation()
    {
        FlushPendingReturns();
        DestroyPooledPresentations();
    }
#endif
}
