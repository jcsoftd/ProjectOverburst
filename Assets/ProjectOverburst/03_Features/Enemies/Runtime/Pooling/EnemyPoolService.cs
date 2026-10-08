using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyPoolService : MonoBehaviour
{
    [SerializeField] private Transform inactivePoolRoot;
    [SerializeField, Min(0)] private int defaultPrewarmCount;

    private readonly Dictionary<EnemyActor, Queue<EnemyActor>> availableByPrefab
        = new Dictionary<EnemyActor, Queue<EnemyActor>>();
    private readonly Dictionary<EnemyActor, EnemyActor> leasedPrefabByActor
        = new Dictionary<EnemyActor, EnemyActor>();
    private readonly HashSet<EnemyActor> availableActors = new HashSet<EnemyActor>();
    private readonly HashSet<EnemyActor> returningActors = new HashSet<EnemyActor>();

    private readonly struct PendingReturn
    {
        public readonly EnemyActor prefab;
        public readonly uint lease;
        public readonly bool explicitReturn;
        public PendingReturn(EnemyActor prefab, uint lease, bool explicitReturn)
        { this.prefab = prefab; this.lease = lease; this.explicitReturn = explicitReturn; }
    }
    private readonly Dictionary<EnemyActor, PendingReturn> pendingPrefabByActor = new Dictionary<EnemyActor, PendingReturn>();
    public string LastReturnFailure { get; private set; }

    public Transform InactivePoolRoot => inactivePoolRoot;
    public int DefaultPrewarmCount => Mathf.Max(0, defaultPrewarmCount);
    public int LeasedCount => leasedPrefabByActor.Count;
    public int AvailableCount => availableActors.Count;
    public int CreatedCount { get; private set; }
    public int PendingReturnCount => pendingPrefabByActor.Count;
    public bool IsAuthoringValid => inactivePoolRoot != null
        && inactivePoolRoot != transform
        && !inactivePoolRoot.gameObject.activeSelf;

    public void Configure(Transform poolRoot, int prewarmCount)
    {
        inactivePoolRoot = poolRoot;
        defaultPrewarmCount = Mathf.Max(0, prewarmCount);
    }

    public bool Validate(out string message)
    {
        if (!IsAuthoringValid)
        {
            message = "EnemyPoolService에는 비활성 전용 PoolRoot가 연결되어야 합니다.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    public int Prewarm(EnemyDefinition definition, int count = -1)
    {
        if (!TryResolvePrefab(definition, out EnemyActor prefab))
            return 0;
        if (!Validate(out string message))
        {
            Debug.LogError($"[EnemyPoolService] {message}", this);
            return 0;
        }

        int requested = count >= 0 ? count : DefaultPrewarmCount;
        Queue<EnemyActor> queue = GetOrCreateQueue(prefab);
        int created = 0;
        while (queue.Count < requested)
        {
            EnemyActor actor = CreateInactive(prefab);
            if (actor == null)
                break;

            EnqueueAvailable(prefab, actor);
            created++;
        }

        return created;
    }

    internal EnemyActor Acquire(EnemyDefinition definition)
    {
        if (!TryResolvePrefab(definition, out EnemyActor prefab))
            return null;
        if (!Validate(out string message))
        {
            Debug.LogError($"[EnemyPoolService] {message}", this);
            return null;
        }

        Queue<EnemyActor> queue = GetOrCreateQueue(prefab);
        EnemyActor actor = null;
        while (queue.Count > 0 && actor == null)
        {
            actor = queue.Dequeue();
            if (actor != null)
                availableActors.Remove(actor);
        }

        if (actor == null)
            actor = CreateInactive(prefab);
        if (actor == null)
            return null;

        actor.AttachPool(this, prefab);
        leasedPrefabByActor[actor] = prefab;
        return actor;
    }

    public void Release(EnemyActor actor)
    {
        if (actor == null || returningActors.Contains(actor)) return;
        if (!TryGetOwnedPrefab(actor, out EnemyActor prefab))
        {
            if (!availableActors.Contains(actor)) Debug.LogWarning("[EnemyPoolService] 이 풀에서 대여하지 않은 Actor 반환 요청입니다.", actor);
            return;
        }
        if (inactivePoolRoot == null || !actor.gameObject.activeInHierarchy) { ReleaseDeferred(actor, actor.LeaseVersion); return; }
        leasedPrefabByActor.Remove(actor); pendingPrefabByActor.Remove(actor);
        if (!returningActors.Add(actor)) return;
        try
        {
            if (actor.gameObject.activeSelf) actor.gameObject.SetActive(false);
            ReturnToInactiveRoot(prefab, actor);
        }
        finally { returningActors.Remove(actor); }
    }

    // Disable/destroy callers request ownership only. Hierarchy changes wait for a safe update.
    public bool ReleaseDeferred(EnemyActor actor, uint expectedLeaseVersion)
    {
        if (actor == null || actor.LeaseVersion != expectedLeaseVersion || returningActors.Contains(actor)) return false;
        if (!TryGetOwnedPrefab(actor, out EnemyActor prefab)) return false;
        leasedPrefabByActor.Remove(actor);
        pendingPrefabByActor[actor] = new PendingReturn(prefab, expectedLeaseVersion, true);
        return true;
    }
    private bool TryGetOwnedPrefab(EnemyActor actor, out EnemyActor prefab)
    {
        if (leasedPrefabByActor.TryGetValue(actor, out prefab)) return true;
        if (pendingPrefabByActor.TryGetValue(actor, out PendingReturn pending) && actor.LeaseVersion == pending.lease)
        { prefab = pending.prefab; return prefab != null; }
        prefab = null; return false;
    }
    internal void NotifyActorDisabled(EnemyActor actor)
    {
        if (actor == null || returningActors.Contains(actor)) return;
        if (pendingPrefabByActor.TryGetValue(actor, out PendingReturn pending) && pending.lease == actor.LeaseVersion) return;
        if (!leasedPrefabByActor.TryGetValue(actor, out EnemyActor prefab)) return;
        leasedPrefabByActor.Remove(actor);
        pendingPrefabByActor[actor] = new PendingReturn(prefab, actor.LeaseVersion, false);
    }
    private void ReturnToInactiveRoot(EnemyActor prefab, EnemyActor actor)
    {
        actor.ResetForPool(); actor.transform.SetParent(inactivePoolRoot, false);
        actor.transform.localPosition = Vector3.zero; actor.transform.localRotation = Quaternion.identity; actor.transform.localScale = Vector3.one;
        EnqueueAvailable(prefab, actor);
    }
    private void LateUpdate()
    {
        if (pendingPrefabByActor.Count == 0) return;
        var snapshot = new List<KeyValuePair<EnemyActor, PendingReturn>>(pendingPrefabByActor);
        foreach (var entry in snapshot)
        {
            EnemyActor actor = entry.Key; PendingReturn pending = entry.Value;
            if (actor == null || pending.prefab == null || actor.LeaseVersion != pending.lease)
            { pendingPrefabByActor.Remove(actor); continue; }
            if (actor.gameObject.activeSelf && !pending.explicitReturn)
            { pendingPrefabByActor.Remove(actor); leasedPrefabByActor[actor] = pending.prefab; continue; }
            if (inactivePoolRoot == null)
            { LastReturnFailure = "살아 있는 Actor의 비활성 PoolRoot가 없어 반환을 보류했습니다."; continue; }
            if (!returningActors.Add(actor)) continue;
            pendingPrefabByActor.Remove(actor);
            try
            {
                if (actor.gameObject.activeSelf) actor.gameObject.SetActive(false);
                ReturnToInactiveRoot(pending.prefab, actor);
            }
            finally { returningActors.Remove(actor); }
        }
    }
    private void OnDestroy()
    {
        // Session teardown has no next LateUpdate; the engine owns object destruction.
        pendingPrefabByActor.Clear(); leasedPrefabByActor.Clear(); returningActors.Clear(); availableActors.Clear(); availableByPrefab.Clear();
    }

    private EnemyActor CreateInactive(EnemyActor prefab)
    {
        EnemyActor actor = Instantiate(prefab, inactivePoolRoot);
        if (actor == null)
            return null;

        if (actor.gameObject.activeInHierarchy)
        {
            Debug.LogError(
                "[EnemyPoolService] PoolRoot가 활성 상태라 안전한 비활성 생성 계약을 지킬 수 없습니다.",
                this);
            Destroy(actor.gameObject);
            return null;
        }

        CreatedCount++;
        actor.gameObject.SetActive(false);
        actor.transform.localScale = Vector3.one;
        actor.AttachPool(this, prefab);
        return actor;
    }

    private Queue<EnemyActor> GetOrCreateQueue(EnemyActor prefab)
    {
        if (!availableByPrefab.TryGetValue(prefab, out Queue<EnemyActor> queue))
        {
            queue = new Queue<EnemyActor>();
            availableByPrefab.Add(prefab, queue);
        }

        return queue;
    }

    private void EnqueueAvailable(EnemyActor prefab, EnemyActor actor)
    {
        if (actor == null || !availableActors.Add(actor))
            return;

        GetOrCreateQueue(prefab).Enqueue(actor);
    }

    private static bool TryResolvePrefab(EnemyDefinition definition, out EnemyActor prefab)
    {
        prefab = definition != null ? definition.ActorPrefab : null;
        return prefab != null;
    }
}
