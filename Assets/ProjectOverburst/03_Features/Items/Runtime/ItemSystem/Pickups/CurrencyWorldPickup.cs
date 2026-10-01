using UnityEngine;
using Overburst.Persistence;

public class CurrencyWorldPickup : MonoBehaviour
{
    private const string DefaultCurrencyName = "재화";
    private const float MinimumMagnetSpeed = 12f;

    [SerializeField] private CurrencyItemData currencyData;
    [SerializeField] private CurrencyType currencyType;
    [SerializeField] private int amount = 1;
    [SerializeField] private PlayerInventory targetInventory;
    [SerializeField] private float magnetSpeed = MinimumMagnetSpeed;
    [SerializeField] private float pickupDistance = 0.5f;
    [SerializeField] private float directPickupDistance = 0.6f;
    [SerializeField] private float magnetActivationDelay = 1.5f;
    [SerializeField] private Transform vfxAnchor;

    private static readonly Unity.Profiling.ProfilerMarker CollectMarker = new Unity.Profiling.ProfilerMarker("Overburst.Currency.Collect");
    private static readonly Unity.Profiling.ProfilerMarker FeedbackMarker = new Unity.Profiling.ProfilerMarker("Overburst.Currency.Feedback");
    private static readonly Unity.Profiling.ProfilerMarker ReturnMarker = new Unity.Profiling.ProfilerMarker("Overburst.Currency.Return");
    public CurrencyType CurrencyKind => currencyData != null ? currencyData.currencyType : currencyType;
    private bool pickedUp;
    private bool pickupQueued;
    private uint leaseVersion;
    private Transform magnetTarget;
    private Collider[] pickupColliders;
    private float nextPickupAttemptTime;
    private float spawnTime;
    private ItemData runtimeCurrencyItem;

    internal bool IsPoolLease { get; set; }
    private void OnDestroy() { CurrencyPickupPool.LeaseDestroyed(this); }

    internal void ResetForPool()
    {
        pickedUp = false;
        pickupQueued = false;
        magnetTarget = null;
        nextPickupAttemptTime = 0f;
        runtimeCurrencyItem = null;
        targetInventory = null;
        currencyData = null;
        amount = 1;
        var motion = GetComponent<WorldItemDropMotion>();
        if (motion != null) motion.ResetForPool();
        var guard = GetComponent<RunFallGuard>();
        if (guard != null) guard.Configure(null, RunFallGuardMode.ClampToLastSafePosition, Vector3.zero, 0f);
    }

    private void BeginLease()
    {
        unchecked { leaseVersion++; }
        pickupQueued = false;
        pickedUp = false;
        magnetTarget = null;
        nextPickupAttemptTime = 0f;
        spawnTime = Time.time;
    }

    public CurrencyType CurrencyType => currencyType;
    public int Amount => amount;
    public Transform VfxAnchor => vfxAnchor != null ? vfxAnchor : transform;

    private void Awake()
    {
        spawnTime = Time.time;
        CacheColliders();
        RemoveLegacyRigidbody();
    }

    private void Update()
    {
        if (pickedUp || magnetTarget == null)
            return;

        Vector3 targetPosition = GetMagnetTargetPosition();
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, GetEffectiveMagnetSpeed() * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPosition) <= Mathf.Max(0.05f, pickupDistance))
            RequestPickup(targetInventory);
    }

    public void Initialize(CurrencyItemData currencyData, int stackAmount, PlayerInventory inventory)
    {
        BeginLease();
        runtimeCurrencyItem = null;
        if (currencyData != null)
        {
            this.currencyData = currencyData;
            currencyType = currencyData.currencyType;
        }

        amount = Mathf.Max(1, stackAmount);
        targetInventory = inventory;
        spawnTime = Time.time;
        name = BuildName(currencyData);
        CacheColliders();
        RemoveLegacyRigidbody();
    }

    public void Initialize(ItemData currencyItem, PlayerInventory inventory)
    {
        BeginLease();
        runtimeCurrencyItem = currencyItem;
        targetInventory = inventory;

        if (currencyItem != null)
        {
            amount = Mathf.Max(1, currencyItem.stackCount);
            currencyData = currencyItem.baseData as CurrencyItemData;
            if (currencyData != null)
                currencyType = currencyData.currencyType;
        }

        spawnTime = Time.time;
        name = BuildName(currencyData);
        CacheColliders();
        RemoveLegacyRigidbody();
    }

    public void BeginMagnet(Transform target, PlayerInventory inventoryOverride = null)
    {
        if (!isActiveAndEnabled || pickedUp || target == null)
            return;

        if (inventoryOverride != null)
            targetInventory = inventoryOverride;

        if (IsDirectPickupTarget(target))
        {
            RequestPickup(targetInventory);
            return;
        }

        if (Time.time - spawnTime < Mathf.Max(0f, magnetActivationDelay))
            return;

        magnetTarget = target;
        SetCollidersTrigger(true);
    }

    // Magnet arrivals are coalesced into one inventory transaction per frame.
    public void RequestPickup(PlayerInventory inventoryOverride = null)
    {
        if (pickupQueued || !CanAttemptPickup()) return;
        if (inventoryOverride != null) targetInventory = inventoryOverride;
        pickupQueued = true;
        CurrencyPickupBatch.Enqueue(this, leaseVersion);
    }

    public bool TryPickup(PlayerInventory inventoryOverride = null)
    {
        if (pickupQueued || AccountGameplaySession.Current?.IsEditing == true
            || !TryPrepareItem(inventoryOverride, out var item)) return false;
        bool acquired;
        using (CollectMarker.Auto())
            acquired = AccountGameplaySession.RunCurrencyAcquisition(() => targetInventory.AddItem(item));
        CompleteAcquisition(acquired);
        return acquired;
    }

    internal bool TryAddQueued(uint version)
    {
        if (version != leaseVersion || !pickupQueued || !TryPrepareItem(null, out var item)) return false;
        return targetInventory.AddItem(item);
    }

    internal void CompleteQueued(uint version, bool acquired)
    {
        if (version != leaseVersion || !pickupQueued) return;
        pickupQueued = false;
        if (!isActiveAndEnabled) return;
        CompleteAcquisition(acquired);
    }

    private bool CanAttemptPickup() => isActiveAndEnabled && !pickedUp && Time.time >= nextPickupAttemptTime;

    private bool TryPrepareItem(PlayerInventory inventoryOverride, out ItemData item)
    {
        item = null;
        if (!CanAttemptPickup()) return false;
        ResolveInventory(inventoryOverride);
        ResolveCurrencyData();
        if (targetInventory == null || currencyData == null) return false;
        item = runtimeCurrencyItem ?? (runtimeCurrencyItem = new ItemData(currencyData, 1, ItemGrade.Common, amount));
        if (!string.IsNullOrEmpty(item.originRunId)
            && AccountGameplaySession.Current?.CanAcquireFromRun(item.originRunId) != true) return false;
        item.EnsureRuntimeState();
        item.EnsureAcquisitionOrder();
        return true;
    }

    private void CompleteAcquisition(bool acquired)
    {
        if (!acquired)
        {
            nextPickupAttemptTime = Time.time + 0.5f;
            ReleaseMagnet();
            return;
        }
        pickedUp = true;
        using (FeedbackMarker.Auto()) ShowPickupText();
        runtimeCurrencyItem = null;
        using (ReturnMarker.Auto())
            if (!CurrencyPickupPool.TryReturn(this)) Destroy(gameObject);
    }

    private Vector3 GetMagnetTargetPosition()
    {
        return magnetTarget.position + Vector3.up * 0.85f;
    }

    private float GetEffectiveMagnetSpeed()
    {
        return Mathf.Max(MinimumMagnetSpeed, magnetSpeed);
    }

    private bool IsDirectPickupTarget(Transform target)
    {
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= Mathf.Max(0.05f, directPickupDistance);
    }

    private void ReleaseMagnet()
    {
        magnetTarget = null;
        SetCollidersTrigger(true);
    }

    private void ShowPickupText()
    {
        Vector3 position = ResolveFeedbackPosition();
        string itemName = currencyData != null && !string.IsNullOrWhiteSpace(currencyData.itemName) ? currencyData.itemName : DefaultCurrencyName;
        DamageNumberSpawner.SpawnCurrencyPickup(position, itemName, amount, currencyData != null ? currencyData.color : Color.white);
    }

    private Vector3 ResolveFeedbackPosition()
    {
        if (magnetTarget != null)
            return magnetTarget.position;

        if (targetInventory != null)
            return targetInventory.transform.position;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        return playerObject != null ? playerObject.transform.position : transform.position;
    }

    private void ResolveInventory(PlayerInventory inventoryOverride)
    {
        if (inventoryOverride != null)
            targetInventory = inventoryOverride;

        if (targetInventory == null)
            targetInventory = PlayerAccountInventoryService.FindSharedInventory();
    }

    private void ResolveCurrencyData()
    {
        if (currencyData == null)
            currencyData = CurrencyItemRegistry.Get(currencyType);
    }

    private string BuildName(CurrencyItemData currencyData)
    {
        string itemName = currencyData != null && !string.IsNullOrEmpty(currencyData.itemName) ? currencyData.itemName : currencyType.ToString();
        return "CurrencyPickup_" + itemName + "_" + amount;
    }

    private void CacheColliders()
    {
        if (pickupColliders == null) pickupColliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < pickupColliders.Length; i++)
        {
            if (pickupColliders[i] != null)
                pickupColliders[i].isTrigger = true;
        }
    }

    private void SetCollidersTrigger(bool isTrigger)
    {
        if (pickupColliders == null)
            CacheColliders();

        for (int i = 0; i < pickupColliders.Length; i++)
        {
            Collider pickupCollider = pickupColliders[i];
            if (pickupCollider == null)
                continue;

            pickupCollider.isTrigger = true;
        }
    }

    private void RemoveLegacyRigidbody()
    {
        Rigidbody legacyRigidbody = GetComponent<Rigidbody>();
        if (legacyRigidbody == null)
            return;

        legacyRigidbody.linearVelocity = Vector3.zero;
        legacyRigidbody.angularVelocity = Vector3.zero;
        legacyRigidbody.useGravity = false;
        legacyRigidbody.isKinematic = true;
        legacyRigidbody.detectCollisions = false;
        Destroy(legacyRigidbody);
    }
}
