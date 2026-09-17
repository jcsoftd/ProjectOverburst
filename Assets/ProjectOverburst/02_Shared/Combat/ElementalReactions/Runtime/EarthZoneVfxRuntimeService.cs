using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-9400)]
public sealed class EarthZoneVfxRuntimeService : MonoBehaviour
{
    private sealed class Pool
    {
        public readonly GameObject Prefab;
        public readonly Stack<GameObject> Available;
        public readonly Transform Parent;

        public Pool(GameObject prefab, int capacity, Transform parent)
        {
            Prefab = prefab;
            Parent = parent;
            Available = new Stack<GameObject>(capacity);
            for (int i = 0; i < capacity; i++)
            {
                GameObject item = Instantiate(prefab, parent);
                item.SetActive(false);
                Available.Push(item);
            }
        }

        public GameObject Acquire(Vector3 position)
        {
            if (Available.Count == 0)
                return null;
            GameObject item = Available.Pop();
            item.transform.SetPositionAndRotation(position, Quaternion.identity);
            item.SetActive(true);
            return item;
        }

        public void Release(GameObject item)
        {
            if (item == null)
                return;
            item.SetActive(false);
            item.transform.SetParent(Parent, false);
            Available.Push(item);
        }
    }

    private struct ActiveRecord
    {
        public EarthZoneVfxKind Kind;
        public GameObject Instance;
        public float ExpiresAt;
    }

    private static EarthZoneVfxRuntimeService instance;
    private readonly Pool[] pools = new Pool[7];
    private readonly Dictionary<int, ActiveRecord> active = new Dictionary<int, ActiveRecord>(128);
    private readonly List<int> expiredIds = new List<int>(32);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureCreated()
    {
        EnsureInstance();
    }

    public static void Show(int id, EarthZoneVfxKind kind, Vector3 position)
    {
        EarthZoneVfxRuntimeService service = EnsureInstance();
        service.HideInternal(id);
        Pool pool = service.pools[(int)kind];
        GameObject item = pool?.Acquire(position);
        if (item != null)
            service.active[id] = new ActiveRecord { Kind = kind, Instance = item, ExpiresAt = float.PositiveInfinity };
    }

    public static void Replace(int id, EarthZoneVfxKind kind, Vector3 position)
    {
        Show(id, kind, position);
    }

    public static void ShowOneShot(int id, EarthZoneVfxKind kind, Vector3 position, float lifetime)
    {
        Show(id, kind, position);
        if (instance != null && instance.active.TryGetValue(id, out ActiveRecord record))
        {
            record.ExpiresAt = Time.time + Mathf.Max(0.1f, lifetime);
            instance.active[id] = record;
        }
    }

    public static void Hide(int id)
    {
        if (instance != null)
            instance.HideInternal(id);
    }

    private static EarthZoneVfxRuntimeService EnsureInstance()
    {
        if (instance != null)
            return instance;
        GameObject owner = new GameObject(nameof(EarthZoneVfxRuntimeService));
        DontDestroyOnLoad(owner);
        instance = owner.AddComponent<EarthZoneVfxRuntimeService>();
        return instance;
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
        Initialize();
    }

    private void Initialize()
    {
        EarthZoneVfxCatalog catalog = Resources.Load<EarthZoneVfxCatalog>(EarthZoneVfxCatalog.ResourcePath);
        if (catalog == null)
            return;
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            EarthZoneVfxCatalogEntry entry = catalog.Entries[i];
            if (entry == null || entry.Prefab == null)
                continue;
            EarthZoneVfxAuthoring authoring = entry.Prefab.GetComponent<EarthZoneVfxAuthoring>();
            if (authoring == null || authoring.Kind != entry.Kind || pools[(int)entry.Kind] != null)
                continue;
            pools[(int)entry.Kind] = new Pool(entry.Prefab, authoring.PoolCapacity, transform);
        }
    }

    private void Update()
    {
        if (active.Count == 0)
            return;
        expiredIds.Clear();
        float now = Time.time;
        foreach (KeyValuePair<int, ActiveRecord> pair in active)
        {
            if (now >= pair.Value.ExpiresAt)
                expiredIds.Add(pair.Key);
        }
        for (int i = 0; i < expiredIds.Count; i++)
            HideInternal(expiredIds[i]);
    }

    private void HideInternal(int id)
    {
        if (!active.TryGetValue(id, out ActiveRecord record))
            return;
        active.Remove(id);
        pools[(int)record.Kind]?.Release(record.Instance);
    }
}
