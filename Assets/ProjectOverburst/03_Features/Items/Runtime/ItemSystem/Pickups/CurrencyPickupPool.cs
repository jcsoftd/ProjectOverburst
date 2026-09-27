using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;

// Shared presentation pool. Item identity and durable acquisition remain in CurrencyWorldPickup.
public static class CurrencyPickupPool
{
    public const int PrepareCount = 128;
    public const int IdleCapacity = 256;
    public const int MaxPreparePerFrame = 8;
    private static readonly Queue<CurrencyWorldPickup> Idle = new Queue<CurrencyWorldPickup>(IdleCapacity);
    private static GameObject prefab;
    private static Host host;
    private static bool quitting;
    public static int Created { get; private set; }
    public static int Misses { get; private set; }
    public static int Active { get; private set; }
    public static int Returns { get; private set; }
    public static int IdleCount => Idle.Count;
    private static readonly ProfilerMarker SpawnMarker = new ProfilerMarker("Overburst.Loot.CurrencyAcquire");
    private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("Overburst.Loot.CurrencyPrepare");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    { Idle.Clear(); prefab = null; host = null; quitting = false; Created = Misses = Active = Returns = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Prepare()
    {
        if (!Application.isPlaying || quitting) return;
        prefab = WorldItemDropFactory.GoldPickupPrefab;
        if (prefab != null) EnsureHost();
    }

    private static void EnsureHost()
    {
        if (host != null) return;
        var root = new GameObject("Currency pickup pool");
        Object.DontDestroyOnLoad(root);
        host = root.AddComponent<Host>();
        var idle = new GameObject("Idle currency");
        idle.transform.SetParent(root.transform, false);
        idle.SetActive(false);
        host.IdleRoot = idle.transform;
    }

    public static GameObject Acquire(GameObject source, Vector3 position)
    {
        if (!Application.isPlaying || quitting || source == null || source != WorldItemDropFactory.GoldPickupPrefab)
            return source != null ? Object.Instantiate(source) : null;
        using (SpawnMarker.Auto())
        {
            prefab = source;
            EnsureHost();
            CurrencyWorldPickup pickup = null;
            while (Idle.Count > 0 && pickup == null) pickup = Idle.Dequeue();
            if (pickup == null) { Misses++; pickup = Create(); }
            if (pickup == null) return null;
            pickup.gameObject.SetActive(false);
            pickup.transform.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(pickup.gameObject, SceneManager.GetActiveScene());
            pickup.transform.SetPositionAndRotation(position, prefab.transform.rotation);
            pickup.transform.localScale = prefab.transform.localScale;
            pickup.IsPoolLease = true;
            Active++;
            return pickup.gameObject;
        }
    }

    private static CurrencyWorldPickup Create()
    {
        var instance = Object.Instantiate(prefab, host.IdleRoot);
        instance.SetActive(false);
        var pickup = instance.GetComponent<CurrencyWorldPickup>();
        if (pickup == null) { Object.Destroy(instance); return null; }
        pickup.ResetForPool();
        Created++;
        return pickup;
    }

    public static bool TryReturn(CurrencyWorldPickup pickup)
    {
        if (pickup == null || !pickup.IsPoolLease) return false;
        pickup.IsPoolLease = false;
        Active = Mathf.Max(0, Active - 1);
        pickup.ResetForPool();
        pickup.gameObject.SetActive(false);
        if (quitting || host == null || Idle.Count >= IdleCapacity) Object.Destroy(pickup.gameObject);
        else { pickup.transform.SetParent(host.IdleRoot, false); Idle.Enqueue(pickup); Returns++; }
        return true;
    }

    internal static void LeaseDestroyed(CurrencyWorldPickup pickup)
    {
        if (!pickup.IsPoolLease) return;
        pickup.IsPoolLease = false;
        Active = Mathf.Max(0, Active - 1);
    }

    private sealed class Host : MonoBehaviour
    {
        public Transform IdleRoot;
        private void Update()
        {
            if (prefab == null || IdleRoot == null || Active + Idle.Count >= PrepareCount) return;
            using (PrepareMarker.Auto())
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < MaxPreparePerFrame && Active + Idle.Count < PrepareCount; i++)
                {
                    var pickup = Create();
                    if (pickup == null) break;
                    Idle.Enqueue(pickup);
                    if ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency >= 1d) break;
                }
            }
        }
        private void OnApplicationQuit() { quitting = true; }
        private void OnDestroy() { if (host == this) { host = null; Idle.Clear(); } }
    }
}
