using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-9500)]
public sealed class ElementalReactionVfxRuntimeService : MonoBehaviour
{
    public const int StartBudget = 300;
    public const int ProcBudget = 300;
    public const int LinkBudget = 300;
    public const int EndBudget = 300;
    public const int LoopBudget = 300;
    public const float DefaultMaximumDistance = 45f;
    public const float ViewportMargin = 0.05f;

    private const int SlotTypeCount = 5;
    private const int ReactionTypeCount = 8;
    private const int SlotCount = ReactionTypeCount * SlotTypeCount;
    private const int LoopTrackingCapacity = 2048;
    private const int ChainTrackingCapacity = 64;
    private const float ChainTrackingTimeout = 1f;
    private const float MinimumLifetime = 0.1f;

    private static readonly int[] CategoryBudgets =
    {
        StartBudget,
        LoopBudget,
        ProcBudget,
        LinkBudget,
        EndBudget
    };

    private static ElementalReactionVfxRuntimeService instance;

    [SerializeField, Min(1f)] private float maximumDistance = DefaultMaximumDistance;
    [SerializeField, Range(0f, 0.2f)] private float viewportMargin = ViewportMargin;

    private readonly SlotRuntime[] slots = new SlotRuntime[SlotCount];
    private readonly int[] activeCategoryCounts = new int[SlotTypeCount];
    private readonly LoopRecord[] loopRecords = new LoopRecord[LoopTrackingCapacity];
    private readonly Dictionary<LoopRecordKey, int> loopRecordIndices =
        new Dictionary<LoopRecordKey, int>(LoopTrackingCapacity);
    private readonly int[] freeLoopIndices = new int[LoopTrackingCapacity];
    private readonly int[] activeLoopIndices = new int[LoopTrackingCapacity];
    private readonly ChainRecord[] chainRecords = new ChainRecord[ChainTrackingCapacity];
    private int freeLoopIndexCount;
    private int activeLoopIndexCount;
    private Camera cachedCamera;
    private Camera validationCamera;
    private int lastCameraResolveFrame = -1;
    private bool initialized;
    private bool subscribed;
    private int prewarmInstantiateCount;

#if UNITY_EDITOR
    private readonly int[] spawnCountsForValidation = new int[SlotCount];
#endif

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

        GameObject serviceObject = new GameObject("ElementalReactionVfxRuntimeService");
        Object.DontDestroyOnLoad(serviceObject);
        instance = serviceObject.AddComponent<ElementalReactionVfxRuntimeService>();
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
        Initialize(Resources.Load<ElementalReactionVfxCatalog>(ElementalReactionVfxCatalog.ResourcePath));
    }

    private void OnEnable()
    {
        SubscribeEvents();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
        ReturnAllActive();
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
        ReturnAllActive();
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        float now = Time.time;
        UpdateChainTimeouts(now);
        UpdateLoopRecords();
        UpdateActiveInstances(now);
    }

    private void Initialize(ElementalReactionVfxCatalog catalog)
    {
        ClearPool();
        initialized = true;
        if (catalog == null || !catalog.RuntimeConsumerConnected)
            return;

        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            ElementalReactionVfxCatalogEntry entry = catalog.Entries[i];
            if (entry == null || entry.Prefab == null || !HasPlayableContent(entry.Prefab))
                continue; // 빈 wrapper는 풀·예산을 만들지 않음

            ElementalReactionVfxAuthoring authoring =
                entry.Prefab.GetComponent<ElementalReactionVfxAuthoring>();
            if (authoring == null
                || authoring.ReactionType != entry.ReactionType
                || authoring.SlotType != entry.SlotType)
            {
                continue;
            }

            int slotIndex = ResolveSlotIndex(entry.ReactionType, entry.SlotType);
            if (slotIndex < 0 || slots[slotIndex] != null)
                continue;

            int capacity = Mathf.Min(
                Mathf.Max(1, authoring.PoolCapacity),
                CategoryBudgets[(int)entry.SlotType]);
            slots[slotIndex] = new SlotRuntime(entry.Prefab, authoring, capacity, transform, this);
        }
    }

    private void SubscribeEvents()
    {
        if (subscribed)
            return;

        ElementalReactionEvents.ReactionStarted += HandleReactionStarted;
        ElementalReactionEvents.ReactionProcExecuted += HandleReactionProcExecuted;
        ElementalReactionEvents.ChainHopExecuted += HandleChainHopExecuted;
        ElementalReactionEvents.ChainCompleted += HandleChainCompleted;
        ElementalReactionStateEvents.StateChanged += HandleReactionStateChanged;
        ElementalReactionStateEvents.StateRemoved += HandleReactionStateRemoved;
        ElementalReactionStateEvents.StatesCleared += HandleReactionStatesCleared;
        subscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!subscribed)
            return;

        ElementalReactionEvents.ReactionStarted -= HandleReactionStarted;
        ElementalReactionEvents.ReactionProcExecuted -= HandleReactionProcExecuted;
        ElementalReactionEvents.ChainHopExecuted -= HandleChainHopExecuted;
        ElementalReactionEvents.ChainCompleted -= HandleChainCompleted;
        ElementalReactionStateEvents.StateChanged -= HandleReactionStateChanged;
        ElementalReactionStateEvents.StateRemoved -= HandleReactionStateRemoved;
        ElementalReactionStateEvents.StatesCleared -= HandleReactionStatesCleared;
        subscribed = false;
    }

    private void HandleReactionStarted(ElementalReactionEvent reactionEvent)
    {
        TryPlay(
            reactionEvent.ReactionType,
            ElementalReactionVfxSlotType.Start,
            new SpawnRequest(
                reactionEvent.TriggerTarget,
                null,
                reactionEvent.Center,
                reactionEvent.Center,
                reactionEvent.Center,
                ResolveReactionRadius(reactionEvent.ReactionType)));

        if (reactionEvent.ReactionType == ElementalReactionType.ChainElectricity)
            BeginChainTracking(reactionEvent);
    }

    private void HandleReactionProcExecuted(ElementalReactionProcEvent procEvent)
    {
        ElementalReactionType reactionType;
        float radius;
        switch (procEvent.ProcType)
        {
            case ElementalReactionProcType.Shatter:
                reactionType = ElementalReactionType.Shatter;
                radius = 0f;
                break;
            case ElementalReactionProcType.Plasma:
                reactionType = ElementalReactionType.Plasma;
                radius = ElementalReactionRules.PlasmaRadius;
                break;
            case ElementalReactionProcType.ColdCharge:
                reactionType = ElementalReactionType.ColdCharge;
                radius = ElementalReactionRules.ColdChargeRadius;
                break;
            default:
                return;
        }

        TryPlay(
            reactionType,
            ElementalReactionVfxSlotType.Proc,
            new SpawnRequest(
                procEvent.TriggerTarget,
                null,
                procEvent.Center,
                procEvent.Center,
                procEvent.Center,
                radius));
    }

    private void HandleChainHopExecuted(ElementalReactionChainHopEvent hopEvent)
    {
        if (hopEvent.HopIndex < 0 || hopEvent.HopIndex >= ElementalReactionRules.ChainMaximumTargetCount)
            return;

        int recordIndex = FindChainRecord(hopEvent.SequenceId);
        if (recordIndex < 0)
            return;

        ref ChainRecord record = ref chainRecords[recordIndex];
        if (hopEvent.HopIndex <= record.LastHopIndex)
            return;

        if (hopEvent.HopIndex > 0)
        {
            TryPlay(
                ElementalReactionType.ChainElectricity,
                ElementalReactionVfxSlotType.Link,
                new SpawnRequest(
                    hopEvent.Target,
                    record.PreviousTarget,
                    hopEvent.Center,
                    record.PreviousCenter,
                    hopEvent.Center,
                    0f));
        }

        TryPlay(
            ElementalReactionType.ChainElectricity,
            ElementalReactionVfxSlotType.Proc,
            new SpawnRequest(
                hopEvent.Target,
                null,
                hopEvent.Center,
                hopEvent.Center,
                hopEvent.Center,
                0f));
        record.PreviousTarget = hopEvent.Target;
        record.PreviousCenter = hopEvent.Center;
        record.LastHopIndex = hopEvent.HopIndex;
        record.ExpiresAt = Time.time + ChainTrackingTimeout;
    }

    private void HandleChainCompleted(ElementalReactionChainCompletedEvent completedEvent)
    {
        int index = FindChainRecord(completedEvent.SequenceId);
        if (index >= 0)
            chainRecords[index].Clear();
    }

    private void HandleReactionStateChanged(
        IElementalReactionStateOwner owner,
        ElementalReactionStateSnapshot snapshot,
        ElementalReactionStateChangeReason reason)
    {
        if (!HasLoopSlot(snapshot.ReactionType) || owner == null)
            return;

        LoopRecordKey key = new LoopRecordKey(owner, snapshot.ReactionType);
        if (loopRecordIndices.ContainsKey(key))
            return; // Refreshed는 같은 Loop instance를 유지

        CombatTarget target = ResolveOwnerTarget(owner);
        if (target == null)
            return;

        if (!TryRentLoopRecord(key, out int recordIndex))
            return;

        ref LoopRecord record = ref loopRecords[recordIndex];
        record.Owner = owner;
        record.OwnerObject = owner as Object;
        record.Target = target;
        record.ReactionType = snapshot.ReactionType;
        record.LastPosition = target.WorldCenter;
        TryAcquireLoop(recordIndex);
    }

    private void HandleReactionStateRemoved(
        IElementalReactionStateOwner owner,
        ElementalReactionType reactionType,
        ElementalReactionStateRemoveReason reason)
    {
        if (!TryGetLoopRecordIndex(owner, reactionType, out int index))
            return;

        ref LoopRecord record = ref loopRecords[index];
        CombatTarget target = record.Target;
        Vector3 endPosition = target != null ? target.WorldCenter : record.LastPosition;
        ReleaseLoopRecord(index);

        if (reactionType == ElementalReactionType.ThermalFracture
            || reactionType == ElementalReactionType.Freeze)
        {
            TryPlay(
                reactionType,
                ElementalReactionVfxSlotType.End,
                new SpawnRequest(target, null, endPosition, endPosition, endPosition, 0f));
        }
    }

    private void HandleReactionStatesCleared(
        IElementalReactionStateOwner owner,
        ElementalStatusClearReason reason)
    {
        ClearOwnerLoopRecord(owner, ElementalReactionType.ThermalFracture);
        ClearOwnerLoopRecord(owner, ElementalReactionType.Plasma);
        ClearOwnerLoopRecord(owner, ElementalReactionType.Freeze);
        ClearOwnerLoopRecord(owner, ElementalReactionType.ColdCharge);
    }

    private void BeginChainTracking(ElementalReactionEvent reactionEvent)
    {
        int freeIndex = FindFreeChainRecord();
        if (freeIndex < 0)
            return;

        chainRecords[freeIndex] = new ChainRecord(
            reactionEvent.SequenceId,
            reactionEvent.TriggerTarget,
            reactionEvent.Center,
            Time.time + ChainTrackingTimeout);
    }

    private bool TryPlay(
        ElementalReactionType reactionType,
        ElementalReactionVfxSlotType slotType,
        SpawnRequest request)
    {
        int slotIndex = ResolveSlotIndex(reactionType, slotType);
        if (slotIndex < 0)
            return false;

        SlotRuntime slot = slots[slotIndex];
        if (slot == null || !TryResolvePose(slot, request, out PoseData pose) || !IsVisible(pose.Position))
            return false; // 범위 밖이면 풀·예산 점유 전 종료

        int categoryIndex = (int)slotType;
        if (activeCategoryCounts[categoryIndex] >= CategoryBudgets[categoryIndex])
            return false;

        PooledInstance pooledInstance = slot.Acquire();
        if (pooledInstance == null)
            return false; // strict pool 고갈 시 표현만 생략

        activeCategoryCounts[categoryIndex]++;
        pooledInstance.Activate(slot, request, pose, Time.time);
#if UNITY_EDITOR
        spawnCountsForValidation[slotIndex]++;
#endif
        return true;
    }

    private void TryAcquireLoop(int recordIndex)
    {
        ref LoopRecord record = ref loopRecords[recordIndex];
        if (record.Instance != null || record.Target == null)
            return;

        int slotIndex = ResolveSlotIndex(record.ReactionType, ElementalReactionVfxSlotType.Loop);
        SlotRuntime slot = slotIndex >= 0 ? slots[slotIndex] : null;
        SpawnRequest request = new SpawnRequest(
            record.Target,
            null,
            record.Target.WorldCenter,
            record.Target.WorldCenter,
            record.Target.WorldCenter,
            ResolveReactionRadius(record.ReactionType));
        if (slot == null || !TryResolvePose(slot, request, out PoseData pose) || !IsVisible(pose.Position))
            return;
        if (activeCategoryCounts[(int)ElementalReactionVfxSlotType.Loop] >= LoopBudget)
            return;

        PooledInstance pooledInstance = slot.Acquire();
        if (pooledInstance == null)
            return;

        activeCategoryCounts[(int)ElementalReactionVfxSlotType.Loop]++;
        pooledInstance.Activate(slot, request, pose, Time.time);
        pooledInstance.BindLoopRecord(recordIndex);
        record.Instance = pooledInstance;
#if UNITY_EDITOR
        spawnCountsForValidation[slotIndex]++;
#endif
    }

    private void ReleaseLoopInstance(ref LoopRecord record)
    {
        if (record.Instance == null)
            return;

        ReleaseInstance(record.Instance);
        record.Instance = null;
    }

    private void UpdateLoopRecords()
    {
        int activePosition = 0;
        while (activePosition < activeLoopIndexCount)
        {
            int recordIndex = activeLoopIndices[activePosition];
            ref LoopRecord record = ref loopRecords[recordIndex];

            if (record.OwnerObject == null
                || record.Target == null
                || !record.Target.isActiveAndEnabled)
            {
                ReleaseLoopRecord(recordIndex);
                continue;
            }

            record.LastPosition = record.Target.WorldCenter;
            bool visible = IsVisible(record.LastPosition);
            if (!visible)
                ReleaseLoopInstance(ref record);
            else if (record.Instance == null)
                TryAcquireLoop(recordIndex); // 다시 가까워지면 상태 Loop 재대여

            activePosition++;
        }
    }

    private void UpdateActiveInstances(float now)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            SlotRuntime slot = slots[i];
            if (slot == null)
                continue;

            PooledInstance[] instances = slot.Instances;
            for (int j = 0; j < instances.Length; j++)
            {
                PooledInstance pooledInstance = instances[j];
                if (!pooledInstance.Active)
                    continue;

                if (pooledInstance.FollowTarget)
                {
                    if (!TryResolvePose(slot, pooledInstance.Request, out PoseData pose))
                    {
                        ReleaseInstance(pooledInstance);
                        continue;
                    }

                    pooledInstance.ApplyPose(pose);
                }

                if (slot.SlotType != ElementalReactionVfxSlotType.Loop
                    && pooledInstance.ShouldReturn(now))
                {
                    ReleaseInstance(pooledInstance);
                }
            }
        }
    }

    private void UpdateChainTimeouts(float now)
    {
        for (int i = 0; i < chainRecords.Length; i++)
        {
            if (chainRecords[i].Active && now >= chainRecords[i].ExpiresAt)
                chainRecords[i].Clear();
        }
    }

    private void ReleaseInstance(PooledInstance pooledInstance)
    {
        if (pooledInstance == null || !pooledInstance.Active)
            return;

        ElementalReactionVfxSlotType slotType = pooledInstance.Slot.SlotType;
        int loopRecordIndex = pooledInstance.LoopRecordIndex;
        int categoryIndex = (int)slotType;
        activeCategoryCounts[categoryIndex] = Mathf.Max(0, activeCategoryCounts[categoryIndex] - 1);
        pooledInstance.Release();

        if (slotType == ElementalReactionVfxSlotType.Loop
            && loopRecordIndex >= 0
            && loopRecordIndex < LoopTrackingCapacity
            && loopRecords[loopRecordIndex].Active
            && loopRecords[loopRecordIndex].Instance == pooledInstance)
        {
            loopRecords[loopRecordIndex].Instance = null;
        }
    }

    private void ReturnAllActive()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            SlotRuntime slot = slots[i];
            if (slot == null)
                continue;

            for (int j = 0; j < slot.Instances.Length; j++)
                ReleaseInstance(slot.Instances[j]);
        }

        ResetLoopTracking();
        for (int i = 0; i < chainRecords.Length; i++)
            chainRecords[i].Clear();
        Array.Clear(activeCategoryCounts, 0, activeCategoryCounts.Length);
    }

    private void ClearPool()
    {
        ReturnAllActive();
        for (int i = 0; i < slots.Length; i++)
        {
            SlotRuntime slot = slots[i];
            if (slot == null)
                continue;

            slot.DestroyAll();
            slots[i] = null;
        }

        prewarmInstantiateCount = 0;
#if UNITY_EDITOR
        Array.Clear(spawnCountsForValidation, 0, spawnCountsForValidation.Length);
#endif
    }

    private bool TryResolvePose(SlotRuntime slot, SpawnRequest request, out PoseData pose)
    {
        CombatTarget target = request.Target;
        Vector3 position;
        Quaternion rotation = slot.PrefabRotation;
        Vector3 scaleMultiplier = Vector3.one;

        switch (slot.SpawnBasis)
        {
            case ElementalReactionVfxSpawnBasis.HitPoint:
            case ElementalReactionVfxSpawnBasis.WorldPosition:
                position = request.WorldPosition;
                break;
            case ElementalReactionVfxSpawnBasis.TargetCenter:
                position = target != null ? target.WorldCenter : request.WorldPosition;
                break;
            case ElementalReactionVfxSpawnBasis.TargetGround:
                position = target != null ? target.CurrentVolume.Center : request.WorldPosition;
                if (target != null)
                    position.y = target.transform.position.y;
                break;
            case ElementalReactionVfxSpawnBasis.TargetVolume:
                position = target != null ? target.CurrentVolume.Center : request.WorldPosition;
                break;
            case ElementalReactionVfxSpawnBasis.SourceToTarget:
            {
                Vector3 from = request.SourceTarget != null
                    ? request.SourceTarget.WorldCenter
                    : request.SourcePosition;
                Vector3 to = target != null ? target.WorldCenter : request.TargetPosition;
                Vector3 direction = to - from;
                float distance = direction.magnitude;
                position = (from + to) * 0.5f;
                if (distance > 0.0001f)
                    rotation = Quaternion.LookRotation(direction / distance, Vector3.up) * slot.PrefabRotation;
                scaleMultiplier.z = Mathf.Max(0.0001f, distance);
                break;
            }
            default:
                position = request.WorldPosition;
                break;
        }

        if (slot.ScaleMode == ElementalReactionVfxScaleMode.ReactionRadius
            && slot.AuthoredRadius > 0f)
        {
            float factor = Mathf.Max(0f, request.ReactionRadius) / slot.AuthoredRadius;
            scaleMultiplier = new Vector3(factor, factor, factor);
        }
        else if (slot.ScaleMode == ElementalReactionVfxScaleMode.TargetVolume
            && target != null
            && slot.AuthoredRadius > 0f)
        {
            CombatTargetVolume volume = target.CurrentVolume;
            scaleMultiplier = new Vector3(
                volume.Radius / slot.AuthoredRadius,
                volume.HalfHeight / slot.AuthoredRadius,
                volume.Radius / slot.AuthoredRadius);
        }

        position += rotation * slot.LocalOffset;
        pose = new PoseData(
            position,
            rotation,
            Vector3.Scale(slot.PrefabScale, scaleMultiplier));
        return true;
    }

    private bool IsVisible(Vector3 position)
    {
        Camera camera = ResolveCamera();
        if (camera == null)
            return false;

        Vector3 delta = position - camera.transform.position;
        if (delta.sqrMagnitude > maximumDistance * maximumDistance)
            return false;

        Vector3 viewport = camera.WorldToViewportPoint(position);
        return viewport.z > 0f
            && viewport.x >= -viewportMargin
            && viewport.x <= 1f + viewportMargin
            && viewport.y >= -viewportMargin
            && viewport.y <= 1f + viewportMargin;
    }

    private Camera ResolveCamera()
    {
        if (validationCamera != null)
            return validationCamera;
        if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
            return cachedCamera;
        if (lastCameraResolveFrame == Time.frameCount)
            return null;

        lastCameraResolveFrame = Time.frameCount;
        cachedCamera = Camera.main;
        return cachedCamera;
    }

    private static CombatTarget ResolveOwnerTarget(IElementalReactionStateOwner owner)
    {
        Component component = owner as Component;
        return component != null ? component.GetComponent<CombatTarget>() : null;
    }

    private bool TryGetLoopRecordIndex(
        IElementalReactionStateOwner owner,
        ElementalReactionType reactionType,
        out int recordIndex)
    {
        if (owner == null)
        {
            recordIndex = -1;
            return false;
        }

        return loopRecordIndices.TryGetValue(
            new LoopRecordKey(owner, reactionType), out recordIndex);
    }

    private bool TryRentLoopRecord(LoopRecordKey key, out int recordIndex)
    {
        if (freeLoopIndexCount <= 0 || loopRecordIndices.ContainsKey(key))
        {
            recordIndex = -1;
            return false;
        }

        recordIndex = freeLoopIndices[--freeLoopIndexCount];
        ref LoopRecord record = ref loopRecords[recordIndex];
        record.Active = true;
        record.ActiveListPosition = activeLoopIndexCount;
        activeLoopIndices[activeLoopIndexCount++] = recordIndex;
        loopRecordIndices.Add(key, recordIndex);
        return true;
    }

    private void ReleaseLoopRecord(int recordIndex)
    {
        if (recordIndex < 0 || recordIndex >= LoopTrackingCapacity)
            return;

        ref LoopRecord record = ref loopRecords[recordIndex];
        if (!record.Active)
            return;

        loopRecordIndices.Remove(new LoopRecordKey(record.Owner, record.ReactionType));
        ReleaseLoopInstance(ref record);

        int activePosition = record.ActiveListPosition;
        int lastPosition = --activeLoopIndexCount;
        if (activePosition != lastPosition)
        {
            int movedRecordIndex = activeLoopIndices[lastPosition];
            activeLoopIndices[activePosition] = movedRecordIndex;
            loopRecords[movedRecordIndex].ActiveListPosition = activePosition;
        }
        activeLoopIndices[lastPosition] = 0;

        record.Clear();
        freeLoopIndices[freeLoopIndexCount++] = recordIndex;
    }

    private void ClearOwnerLoopRecord(
        IElementalReactionStateOwner owner,
        ElementalReactionType reactionType)
    {
        if (!TryGetLoopRecordIndex(owner, reactionType, out int recordIndex))
            return;

        ref LoopRecord record = ref loopRecords[recordIndex];
        CombatTarget target = record.Target;
        Vector3 endPosition = target != null ? target.WorldCenter : record.LastPosition;
        ReleaseLoopRecord(recordIndex);

        if (reactionType == ElementalReactionType.ThermalFracture
            || reactionType == ElementalReactionType.Freeze)
        {
            TryPlay(
                reactionType,
                ElementalReactionVfxSlotType.End,
                new SpawnRequest(target, null, endPosition, endPosition, endPosition, 0f));
        }
    }

    private void ResetLoopTracking()
    {
        loopRecordIndices.Clear();
        activeLoopIndexCount = 0;
        freeLoopIndexCount = LoopTrackingCapacity;
        for (int i = 0; i < LoopTrackingCapacity; i++)
        {
            loopRecords[i].Clear();
            activeLoopIndices[i] = 0;
            freeLoopIndices[i] = LoopTrackingCapacity - 1 - i;
        }
    }

    private int FindChainRecord(long sequenceId)
    {
        for (int i = 0; i < chainRecords.Length; i++)
        {
            if (chainRecords[i].Active && chainRecords[i].SequenceId == sequenceId)
                return i;
        }

        return -1;
    }

    private int FindFreeChainRecord()
    {
        for (int i = 0; i < chainRecords.Length; i++)
        {
            if (!chainRecords[i].Active)
                return i;
        }

        return -1;
    }

    private static bool HasLoopSlot(ElementalReactionType reactionType)
    {
        return reactionType == ElementalReactionType.ThermalFracture
            || reactionType == ElementalReactionType.Plasma
            || reactionType == ElementalReactionType.Freeze
            || reactionType == ElementalReactionType.ColdCharge;
    }

    private static float ResolveReactionRadius(ElementalReactionType reactionType)
    {
        switch (reactionType)
        {
            case ElementalReactionType.Vaporize: return ElementalReactionRules.VaporizeRadius;
            case ElementalReactionType.Plasma: return ElementalReactionRules.PlasmaRadius;
            case ElementalReactionType.ChainElectricity: return ElementalReactionRules.ChainRadius;
            case ElementalReactionType.ColdCharge: return ElementalReactionRules.ColdChargeRadius;
            default: return 0f;
        }
    }

    private static int ResolveSlotIndex(
        ElementalReactionType reactionType,
        ElementalReactionVfxSlotType slotType)
    {
        int reactionIndex = (int)reactionType;
        int slotIndex = (int)slotType;
        if (reactionIndex < 0
            || reactionIndex >= ReactionTypeCount
            || slotIndex < 0
            || slotIndex >= SlotTypeCount)
        {
            return -1;
        }

        return reactionIndex * SlotTypeCount + slotIndex;
    }

    private static bool HasPlayableContent(GameObject wrapperPrefab)
    {
        if (wrapperPrefab == null)
            return false;

        Transform content = wrapperPrefab.transform.Find("VFX_CONTENT");
        if (content == null)
            return false;
        if (content.childCount > 0)
            return true;

        Component[] components = content.GetComponents<Component>();
        return components.Length > 1;
    }

    private sealed class SlotRuntime
    {
        public readonly ElementalReactionType ReactionType;
        public readonly ElementalReactionVfxSlotType SlotType;
        public readonly ElementalReactionVfxSpawnBasis SpawnBasis;
        public readonly ElementalReactionVfxScaleMode ScaleMode;
        public readonly bool FollowTarget;
        public readonly float AuthoredRadius;
        public readonly Vector3 LocalOffset;
        public readonly float Lifetime;
        public readonly bool NaturalCompletion;
        public readonly Quaternion PrefabRotation;
        public readonly Vector3 PrefabScale;
        public readonly PooledInstance[] Instances;

        public SlotRuntime(
            GameObject prefab,
            ElementalReactionVfxAuthoring authoring,
            int capacity,
            Transform poolRoot,
            ElementalReactionVfxRuntimeService service)
        {
            ReactionType = authoring.ReactionType;
            SlotType = authoring.SlotType;
            SpawnBasis = authoring.SpawnBasis;
            ScaleMode = authoring.ScaleMode;
            FollowTarget = authoring.FollowTarget;
            AuthoredRadius = authoring.AuthoredRadius;
            LocalOffset = authoring.LocalOffset;
            Lifetime = TransientVfxPool.ResolveLifetime(prefab, authoring.Lifetime);
            NaturalCompletion = authoring.NaturalCompletion;
            PrefabRotation = prefab.transform.rotation;
            PrefabScale = prefab.transform.localScale;
            Instances = new PooledInstance[capacity];
            for (int i = 0; i < capacity; i++)
            {
                GameObject clone = Instantiate(prefab, poolRoot, false);
                clone.SetActive(false);
                clone.name = prefab.name;
                Instances[i] = new PooledInstance(clone);
                service.prewarmInstantiateCount++;
            }
        }

        public PooledInstance Acquire()
        {
            for (int i = 0; i < Instances.Length; i++)
            {
                if (!Instances[i].Active)
                    return Instances[i];
            }

            return null;
        }

        public void DestroyAll()
        {
            for (int i = 0; i < Instances.Length; i++)
            {
                GameObject gameObject = Instances[i]?.GameObject;
                if (gameObject == null)
                    continue;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(gameObject);
                else
#endif
                    Destroy(gameObject);
            }
        }
    }

    private sealed class PooledInstance
    {
        private readonly ParticleSystem[] particles;
        private readonly ITransientVfxPlayback customPlayback;
        private readonly ITransientVfxCompletion customCompletion;
        private float earliestReturnTime;
        private float safetyReturnTime;

        public readonly GameObject GameObject;
        public readonly Transform Transform;
        public bool Active { get; private set; }
        public bool FollowTarget { get; private set; }
        public SlotRuntime Slot { get; private set; }
        public SpawnRequest Request { get; private set; }
        public int LoopRecordIndex { get; private set; } = -1;

        public PooledInstance(GameObject gameObject)
        {
            GameObject = gameObject;
            Transform = gameObject.transform;
            particles = gameObject.GetComponentsInChildren<ParticleSystem>(true);
            MonoBehaviour[] behaviours = gameObject.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (customPlayback == null && behaviours[i] is ITransientVfxPlayback playback)
                    customPlayback = playback;
                if (customCompletion == null && behaviours[i] is ITransientVfxCompletion completion)
                    customCompletion = completion;
            }
        }

        public void Activate(SlotRuntime slot, SpawnRequest request, PoseData pose, float now)
        {
            Slot = slot;
            Request = request;
            FollowTarget = slot.FollowTarget;
            GameObject.SetActive(false);
            ApplyPose(pose);
            GameObject.SetActive(true);
            RestartPlayback();
            earliestReturnTime = now + MinimumLifetime;
            safetyReturnTime = now + Mathf.Max(MinimumLifetime, slot.Lifetime);
            Active = true;
        }

        public void ApplyPose(PoseData pose)
        {
            Transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            Transform.localScale = pose.Scale;
        }

        public void BindLoopRecord(int recordIndex)
        {
            LoopRecordIndex = recordIndex;
        }

        public bool ShouldReturn(float now)
        {
            if (now >= safetyReturnTime)
                return true;
            if (!Slot.NaturalCompletion || now < earliestReturnTime)
                return false;
            if (customCompletion != null)
                return !customCompletion.IsPlaybackAlive;

            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null && particles[i].IsAlive(false))
                    return false;
            }

            return true;
        }

        public void Release()
        {
            StopPlayback();
            GameObject.SetActive(false);
            Active = false;
            FollowTarget = false;
            Slot = null;
            Request = default;
            LoopRecordIndex = -1;
        }

        private void RestartPlayback()
        {
            if (customPlayback != null)
            {
                customPlayback.RestartVfx();
                return;
            }

            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem particle = particles[i];
                if (particle == null)
                    continue;
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(false);
            }
        }

        private void StopPlayback()
        {
            if (customPlayback != null)
            {
                customPlayback.StopAndClearVfx();
                return;
            }

            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                    particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private readonly struct SpawnRequest
    {
        public readonly CombatTarget Target;
        public readonly CombatTarget SourceTarget;
        public readonly Vector3 WorldPosition;
        public readonly Vector3 SourcePosition;
        public readonly Vector3 TargetPosition;
        public readonly float ReactionRadius;

        public SpawnRequest(
            CombatTarget target,
            CombatTarget sourceTarget,
            Vector3 worldPosition,
            Vector3 sourcePosition,
            Vector3 targetPosition,
            float reactionRadius)
        {
            Target = target;
            SourceTarget = sourceTarget;
            WorldPosition = worldPosition;
            SourcePosition = sourcePosition;
            TargetPosition = targetPosition;
            ReactionRadius = reactionRadius;
        }
    }

    private readonly struct PoseData
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;

        public PoseData(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
    }

    private struct LoopRecord
    {
        public bool Active;
        public int ActiveListPosition;
        public IElementalReactionStateOwner Owner;
        public Object OwnerObject;
        public CombatTarget Target;
        public ElementalReactionType ReactionType;
        public Vector3 LastPosition;
        public PooledInstance Instance;

        public void Clear()
        {
            Active = false;
            ActiveListPosition = -1;
            Owner = null;
            OwnerObject = null;
            Target = null;
            ReactionType = ElementalReactionType.None;
            LastPosition = default;
            Instance = null;
        }
    }

    private readonly struct LoopRecordKey : IEquatable<LoopRecordKey>
    {
        private readonly IElementalReactionStateOwner owner;
        private readonly ElementalReactionType reactionType;

        public LoopRecordKey(
            IElementalReactionStateOwner configuredOwner,
            ElementalReactionType configuredReactionType)
        {
            owner = configuredOwner;
            reactionType = configuredReactionType;
        }

        public bool Equals(LoopRecordKey other)
        {
            return ReferenceEquals(owner, other.owner) && reactionType == other.reactionType;
        }

        public override bool Equals(object value)
        {
            return value is LoopRecordKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (RuntimeHelpers.GetHashCode(owner) * 397) ^ (int)reactionType;
        }
    }

    private struct ChainRecord
    {
        public bool Active;
        public long SequenceId;
        public int LastHopIndex;
        public CombatTarget PreviousTarget;
        public Vector3 PreviousCenter;
        public float ExpiresAt;

        public ChainRecord(
            long sequenceId,
            CombatTarget previousTarget,
            Vector3 previousCenter,
            float expiresAt)
        {
            Active = true;
            SequenceId = sequenceId;
            LastHopIndex = -1;
            PreviousTarget = previousTarget;
            PreviousCenter = previousCenter;
            ExpiresAt = expiresAt;
        }

        public void Clear()
        {
            Active = false;
            SequenceId = 0;
            LastHopIndex = -1;
            PreviousTarget = null;
            PreviousCenter = default;
            ExpiresAt = 0f;
        }
    }

#if UNITY_EDITOR
    public int PrewarmInstantiateCountForValidation => prewarmInstantiateCount;
    public int RuntimeInstantiateCountForValidation => 0;
    public int ActiveTotalForValidation
    {
        get
        {
            int total = 0;
            for (int i = 0; i < activeCategoryCounts.Length; i++)
                total += activeCategoryCounts[i];
            return total;
        }
    }
    public int TrackedSequenceCountForValidation
    {
        get
        {
            int total = 0;
            for (int i = 0; i < chainRecords.Length; i++)
                total += chainRecords[i].Active ? 1 : 0;
            return total;
        }
    }

    public void InitializeForValidation(ElementalReactionVfxCatalog catalog, Camera camera)
    {
        validationCamera = camera;
        maximumDistance = DefaultMaximumDistance;
        viewportMargin = ViewportMargin;
        Initialize(catalog);
        SubscribeEvents();
    }

    public int GetActiveCategoryCountForValidation(ElementalReactionVfxSlotType slotType)
    {
        return activeCategoryCounts[(int)slotType];
    }

    public int GetSpawnCountForValidation(
        ElementalReactionType reactionType,
        ElementalReactionVfxSlotType slotType)
    {
        int index = ResolveSlotIndex(reactionType, slotType);
        return index >= 0 ? spawnCountsForValidation[index] : 0;
    }

    public int GetLoopInstanceIdForValidation(
        IElementalReactionStateOwner owner,
        ElementalReactionType reactionType)
    {
        bool found = TryGetLoopRecordIndex(owner, reactionType, out int index);
        PooledInstance pooledInstance = found ? loopRecords[index].Instance : null;
        return pooledInstance?.GameObject != null ? pooledInstance.GameObject.GetInstanceID() : 0;
    }

    public int GetLoopRecordIndexForValidation(
        IElementalReactionStateOwner owner,
        ElementalReactionType reactionType)
    {
        return TryGetLoopRecordIndex(owner, reactionType, out int index) ? index : -1;
    }

    public int TrackedLoopKeyCountForValidation => loopRecordIndices.Count;
    public int ActiveLoopIndexCountForValidation => activeLoopIndexCount;
    public int FreeLoopIndexCountForValidation => freeLoopIndexCount;
    public int LoopLinearProbeCountForValidation => 0;

    public void TickForValidation()
    {
        UpdateLoopRecords();
        UpdateActiveInstances(Time.time);
        UpdateChainTimeouts(Time.time);
    }

    public bool TryPlayForValidation(
        ElementalReactionType reactionType,
        ElementalReactionVfxSlotType slotType,
        CombatTarget target,
        Vector3 worldPosition,
        Vector3 sourcePosition,
        Vector3 targetPosition,
        float reactionRadius)
    {
        return TryPlay(
            reactionType,
            slotType,
            new SpawnRequest(target, null, worldPosition, sourcePosition, targetPosition, reactionRadius));
    }

    public void HandleStartForValidation(
        long sequenceId,
        ElementalReactionType reactionType,
        CombatTarget target,
        Vector3 center)
    {
        HandleReactionStarted(new ElementalReactionEvent(
            sequenceId, reactionType, target, center, default));
    }

    public void HandleProcForValidation(
        long sequenceId,
        ElementalReactionProcType procType,
        CombatTarget target,
        Vector3 center)
    {
        HandleReactionProcExecuted(new ElementalReactionProcEvent(
            sequenceId, procType, target, center, default, default, 1));
    }

    public void HandleChainHopForValidation(
        long sequenceId,
        int hopIndex,
        CombatTarget target,
        Vector3 center)
    {
        HandleChainHopExecuted(new ElementalReactionChainHopEvent(
            sequenceId, hopIndex, target, center, 1f, 1f));
    }

    public void HandleChainCompletedForValidation(long sequenceId, int hopCount)
    {
        HandleChainCompleted(new ElementalReactionChainCompletedEvent(sequenceId, hopCount));
    }

    public void HandleStateChangedForValidation(
        IElementalReactionStateOwner owner,
        ElementalReactionType reactionType,
        ElementalReactionStateChangeReason reason)
    {
        HandleReactionStateChanged(
            owner,
            new ElementalReactionStateSnapshot(
                reactionType, true, 6f, 1f, default),
            reason);
    }

    public void HandleStateRemovedForValidation(
        IElementalReactionStateOwner owner,
        ElementalReactionType reactionType,
        ElementalReactionStateRemoveReason reason)
    {
        HandleReactionStateRemoved(owner, reactionType, reason);
    }

    public void HandleStatesClearedForValidation(
        IElementalReactionStateOwner owner,
        ElementalStatusClearReason reason)
    {
        HandleReactionStatesCleared(owner, reason);
    }

    public static bool HasPlayableContentForValidation(GameObject wrapperPrefab)
    {
        return HasPlayableContent(wrapperPrefab);
    }
#endif
}
