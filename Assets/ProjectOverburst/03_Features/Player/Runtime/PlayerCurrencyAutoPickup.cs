using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerCurrencyAutoPickup : MonoBehaviour
{
    [SerializeField] private float pickupRadius = 2.5f;
    [SerializeField] private LayerMask pickupLayerMask = ~0;
    [SerializeField] private float scanInterval = 0.12f;

    private Collider[] nearbyColliders = new Collider[64];
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
        float baseRadius = Mathf.Max(0.1f, pickupRadius);
        float goldRadius = baseRadius * (1f + BagQuality.EquippedBonus(BagStat.GoldMagnetRadius) * .01f);
        int hitCount;
        // Grow the retained buffer when saturated, so dense drop piles cannot starve pickups.
        do
        {
            hitCount = Physics.OverlapSphereNonAlloc(transform.position, goldRadius, nearbyColliders,
                pickupLayerMask, QueryTriggerInteraction.Collide);
            if (hitCount < nearbyColliders.Length) break;
            System.Array.Resize(ref nearbyColliders, checked(nearbyColliders.Length * 2));
        } while (true);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = nearbyColliders[i];
            if (hit == null)
                continue;

            CurrencyWorldPickup pickup = hit.GetComponentInParent<CurrencyWorldPickup>();
            if (pickup != null && (pickup.CurrencyKind == CurrencyType.Gold
                || (pickup.transform.position - transform.position).sqrMagnitude <= baseRadius * baseRadius))
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
