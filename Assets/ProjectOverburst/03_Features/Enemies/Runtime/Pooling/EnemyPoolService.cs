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
    private readonly Dictionary<EnemyActor, EnemyActor> pendingPrefabByActor
        = new Dictionary<EnemyActor, EnemyActor>();

    public Transform InactivePoolRoot => inactivePoolRoot;
    public int DefaultPrewarmCount => Mathf.Max(0, defaultPrewarmCount);
    public int LeasedCount => leasedPrefabByActor.Count;
    public int AvailableCount => availableActors.Count;
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
        if (actor == null)
            return;

        if (!leasedPrefabByActor.TryGetValue(actor, out EnemyActor prefab))
        {
            if (availableActors.Contains(actor))
                return; // 사망 비활성으로 이미 반환된 경우

            Debug.LogWarning("[EnemyPoolService] 이 풀에서 대여하지 않은 Actor 반환 요청입니다.", actor);
            return;
        }

        leasedPrefabByActor.Remove(actor);
        if (!returningActors.Add(actor))
            return;

        try
        {
            if (actor.gameObject.activeSelf)
                actor.gameObject.SetActive(false);
            ReturnToInactiveRoot(prefab, actor);
        }
        finally
        {
            returningActors.Remove(actor);
        }
    }

    internal void NotifyActorDisabled(EnemyActor actor)
    {
        if (actor == null || returningActors.Contains(actor))
            return;
        if (!leasedPrefabByActor.TryGetValue(actor, out EnemyActor prefab))
            return;

        leasedPrefabByActor.Remove(actor);
        pendingPrefabByActor[actor] = prefab;
    }

    private void ReturnToInactiveRoot(EnemyActor prefab, EnemyActor actor)
    {
        actor.ResetForPool();
        actor.transform.SetParent(inactivePoolRoot, false);
        actor.transform.localPosition = Vector3.zero;
        actor.transform.localRotation = Quaternion.identity;
        actor.transform.localScale = Vector3.one;
        EnqueueAvailable(prefab, actor);
    }

    private void LateUpdate()
    {
        if (pendingPrefabByActor.Count == 0)
            return;

        List<KeyValuePair<EnemyActor, EnemyActor>> snapshot
            = new List<KeyValuePair<EnemyActor, EnemyActor>>(pendingPrefabByActor);
        pendingPrefabByActor.Clear();
        for (int i = 0; i < snapshot.Count; i++)
        {
            EnemyActor actor = snapshot[i].Key;
            EnemyActor prefab = snapshot[i].Value;
            if (actor == null || prefab == null || !returningActors.Add(actor))
                continue;

            try
            {
                if (actor.gameObject.activeSelf)
                {
                    leasedPrefabByActor[actor] = prefab;
                    continue;
                }

                ReturnToInactiveRoot(prefab, actor);
            }
            finally
            {
                returningActors.Remove(actor);
            }
        }
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
