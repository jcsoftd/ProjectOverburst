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

    private bool pickedUp;
    private Transform magnetTarget;
    private Collider[] pickupColliders;
    private float nextPickupAttemptTime;
    private float spawnTime;
    private ItemData runtimeCurrencyItem;

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
            TryPickup(targetInventory);
    }

    public void Initialize(CurrencyItemData currencyData, int stackAmount, PlayerInventory inventory)
    {
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
        if (pickedUp || target == null)
            return;

        if (inventoryOverride != null)
            targetInventory = inventoryOverride;

        if (IsDirectPickupTarget(target))
        {
            TryPickup(targetInventory);
            return;
        }

        if (Time.time - spawnTime < Mathf.Max(0f, magnetActivationDelay))
            return;

        magnetTarget = target;
        SetCollidersTrigger(true);
    }

    public bool TryPickup(PlayerInventory inventoryOverride = null)
    {
        if (pickedUp || Time.time < nextPickupAttemptTime)
            return false;

        ResolveInventory(inventoryOverride);
        ResolveCurrencyData();
        if (targetInventory == null || currencyData == null)
            return false;

        ItemData item = runtimeCurrencyItem != null
            ? runtimeCurrencyItem
            : new ItemData(currencyData, 1, ItemGrade.Common, amount);

        if (!string.IsNullOrEmpty(item.originRunId))
        {
            var run = AccountGameplaySession.Current?.ReadRun();
            if (run == null || run.runId != item.originRunId
                || !AccountInvariants.IsRunning(run.phase))
                return false;
        }

        item.EnsureRuntimeState();
        item.EnsureAcquisitionOrder();

        pickedUp = targetInventory.AddItem(item);
        if (!pickedUp)
        {
            nextPickupAttemptTime = Time.time + 0.5f;
            ReleaseMagnet();
            return false;
        }

        ShowPickupText();
        runtimeCurrencyItem = null;
        Destroy(gameObject);
        return true;
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
            targetInventory = FindFirstObjectByType<PlayerInventory>();
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
        pickupColliders = GetComponentsInChildren<Collider>();
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
