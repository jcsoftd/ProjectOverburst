using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerCurrencyAutoPickup : MonoBehaviour
{
    [SerializeField] private float pickupRadius = 2.5f;
    [SerializeField] private LayerMask pickupLayerMask = ~0;
    [SerializeField] private float scanInterval = 0.12f;

    private readonly Collider[] nearbyColliders = new Collider[24];
    private PlayerInventory inventory;
    private float nextScanTime;

    private void Awake()
    {
        ResolveInventory();
    }

    private void Update()
    {
        if (Time.time < nextScanTime)
            return;

        nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
        ScanNearbyCurrency();
    }

    private void ScanNearbyCurrency()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            Mathf.Max(0.1f, pickupRadius),
            nearbyColliders,
            pickupLayerMask,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = nearbyColliders[i];
            if (hit == null)
                continue;

            CurrencyWorldPickup pickup = hit.GetComponentInParent<CurrencyWorldPickup>();
            if (pickup != null)
                pickup.BeginMagnet(transform, inventory);
        }
    }

    private void ResolveInventory()
    {
        if (inventory == null)
            inventory = PlayerAccountInventoryService.FindSharedInventory();

        if (inventory == null)
            inventory = GetComponent<PlayerInventory>() ?? FindFirstObjectByType<PlayerInventory>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterSceneHook()
    {
        EnsurePlayerAutoPickup();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsurePlayerAutoPickup();
    }

    private static void EnsurePlayerAutoPickup()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null || playerObject.GetComponent<PlayerCurrencyAutoPickup>() != null)
            return;

        playerObject.AddComponent<PlayerCurrencyAutoPickup>();
    }
}
