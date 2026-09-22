using UnityEngine;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-9400)]
public sealed class MeleeElementStatusAuraVisibilityScheduler : MonoBehaviour
{
    public const int DefaultChecksPerFrame = 256;
    public const int DefaultMaxVisiblePresentations = 64;
    public const float DefaultEnterDistance = 42f;
    public const float DefaultExitDistance = 48f;
    public const float DefaultEnterViewportMargin = 0.03f;
    public const float DefaultExitViewportMargin = 0.10f;

    private const int ControllerCapacity = 4096;
    private const int PresentationCapacity = 256;
    private const string PresentationResourcePath =
        "Combat/VFX/PF_VFX_MeleeElementStatusAura";
    private static MeleeElementStatusAuraVisibilityScheduler instance;

    [SerializeField, Min(1)] private int checksPerFrame = DefaultChecksPerFrame;
    [SerializeField, Range(1, PresentationCapacity)]
    private int maxVisiblePresentations = DefaultMaxVisiblePresentations;
    [SerializeField, Min(0f)] private float enterDistance = DefaultEnterDistance;
    [SerializeField, Min(0f)] private float exitDistance = DefaultExitDistance;
    [SerializeField, Range(0f, 0.25f)] private float enterViewportMargin = DefaultEnterViewportMargin;
    [SerializeField, Range(0f, 0.25f)] private float exitViewportMargin = DefaultExitViewportMargin;

    private readonly MeleeElementStatusAuraController[] activeControllers =
        new MeleeElementStatusAuraController[ControllerCapacity];
    private readonly MeleeElementStatusAuraPresentation[] inactivePresentations =
        new MeleeElementStatusAuraPresentation[PresentationCapacity];
    private readonly MeleeElementStatusAuraPresentation[] pendingReturns =
        new MeleeElementStatusAuraPresentation[PresentationCapacity];
    private int pendingReturnCount;
    private int activeControllerCount;
    private int roundRobinCursor;
    private int inactivePresentationCount;
    private int createdPresentationCount;
    private int leasedPresentationCount;
    private Camera cachedCamera;
    private MeleeElementStatusAuraPresentation presentationPrefab;
    private bool presentationLoadFailed;

#if UNITY_EDITOR
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
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
        ClearRegistrations();
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
        Advance(ResolveCamera(), checksPerFrame);
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
            if (moved != null)
                moved.SetSchedulerIndex(index);
        }
        activeControllers[lastIndex] = null;
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
            controller.ApplyScheduledVisibility(visible);
            roundRobinCursor++;
        }
    }

    private MeleeElementStatusAuraPresentation LeasePresentation(
        MeleeElementStatusAuraController owner)
    {
        if (owner == null || leasedPresentationCount >= maxVisiblePresentations)
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
            if (createdPresentationCount >= maxVisiblePresentations)
                return null;
            MeleeElementStatusAuraPresentation prefab = ResolvePresentationPrefab();
            if (prefab == null)
                return null;
            leased = Instantiate(prefab, transform);
            leased.name = "ElementStatusAura_Pooled";
            createdPresentationCount++;
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
#endif
}
