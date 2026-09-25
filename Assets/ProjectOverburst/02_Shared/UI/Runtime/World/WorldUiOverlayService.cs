using System.Collections.Generic;
using UnityEngine;

public enum EnemyHpBarTier { Small, Medium, Elite }

[DisallowMultipleComponent]
public sealed class WorldUiOverlayService : MonoBehaviour
{
    [Header("Roots")]
    [SerializeField] private RectTransform healthBarRoot;
    [SerializeField] private RectTransform damagePopupRoot;

    [Header("Health Bar Prefabs")]
    [SerializeField] private EnemyHpBarView normalHealthBarPrefab;
    [SerializeField] private EnemyHpBarView mediumHealthBarPrefab;
    [SerializeField] private EnemyHpBarView eliteHealthBarPrefab;
    [Min(0)]
    [SerializeField] private int normalHealthBarPrewarm = 16;
    [Min(0)]
    [SerializeField] private int mediumHealthBarPrewarm = 8;
    [Min(0)]
    [SerializeField] private int eliteHealthBarPrewarm = 8;

    private static WorldUiOverlayService instance;

    private readonly Queue<EnemyHpBarView> normalHealthBarPool = new Queue<EnemyHpBarView>();
    private readonly Queue<EnemyHpBarView> mediumHealthBarPool = new Queue<EnemyHpBarView>();
    private readonly Queue<EnemyHpBarView> eliteHealthBarPool = new Queue<EnemyHpBarView>();
    private Camera targetCamera;

    public RectTransform DamagePopupRoot => damagePopupRoot;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError("[WorldUiOverlayService] 중복 서비스가 발견됐습니다.", this);
            enabled = false;
            return;
        }

        instance = this;
        targetCamera = Camera.main;
        PrewarmHealthBars();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static bool TryResolve(out WorldUiOverlayService service)
    {
        if (instance == null)
            instance = FindFirstObjectByType<WorldUiOverlayService>(FindObjectsInactive.Include);

        service = instance;
        return service != null && service.isActiveAndEnabled;
    }

    public EnemyHpBarView AcquireHealthBar(CombatHealth health, EnemyHpBarTier tier)
    {
        Queue<EnemyHpBarView> pool = PoolFor(tier);
        EnemyHpBarView prefab = PrefabFor(tier);
        EnemyHpBarView view = DequeueValid(pool);
        if (view == null)
            view = CreateHealthBar(prefab);

        if (view == null)
            return null;

        view.transform.SetParent(healthBarRoot, false);
        view.gameObject.SetActive(true);
        view.Bind(health);
        view.SetProjectionVisible(false);
        return view;
    }

    public void ReleaseHealthBar(EnemyHpBarView view, EnemyHpBarTier tier)
    {
        if (view == null)
            return;

        view.Unbind();
        view.gameObject.SetActive(false);
        view.transform.SetParent(healthBarRoot, false);
        PoolFor(tier).Enqueue(view);
    }

    public EnemyHpBarView AcquireHealthBar(CombatHealth health, bool isElite) =>
        AcquireHealthBar(health, isElite ? EnemyHpBarTier.Elite : EnemyHpBarTier.Small);

    public void ReleaseHealthBar(EnemyHpBarView view, bool isElite) =>
        ReleaseHealthBar(view, isElite ? EnemyHpBarTier.Elite : EnemyHpBarTier.Small);

    public bool TryProject(Vector3 worldPosition, out Vector2 anchoredPosition)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        return WorldUiScreenProjection.TryProject(healthBarRoot, targetCamera, worldPosition, out anchoredPosition);
    }

    private void PrewarmHealthBars()
    {
        Prewarm(normalHealthBarPrefab, normalHealthBarPool, normalHealthBarPrewarm);
        if (mediumHealthBarPrefab != null)
            Prewarm(mediumHealthBarPrefab, mediumHealthBarPool, mediumHealthBarPrewarm);
        Prewarm(eliteHealthBarPrefab, eliteHealthBarPool, eliteHealthBarPrewarm);
    }

    private Queue<EnemyHpBarView> PoolFor(EnemyHpBarTier tier)
    {
        switch (tier)
        {
            case EnemyHpBarTier.Medium: return mediumHealthBarPool;
            case EnemyHpBarTier.Elite: return eliteHealthBarPool;
            default: return normalHealthBarPool;
        }
    }

    private EnemyHpBarView PrefabFor(EnemyHpBarTier tier)
    {
        switch (tier)
        {
            case EnemyHpBarTier.Medium: return mediumHealthBarPrefab != null ? mediumHealthBarPrefab : normalHealthBarPrefab;
            case EnemyHpBarTier.Elite: return eliteHealthBarPrefab;
            default: return normalHealthBarPrefab;
        }
    }

    private void Prewarm(EnemyHpBarView prefab, Queue<EnemyHpBarView> pool, int count)
    {
        for (int i = 0; i < count; i++)
        {
            EnemyHpBarView view = CreateHealthBar(prefab);
            if (view == null)
                break;

            view.gameObject.SetActive(false);
            pool.Enqueue(view);
        }
    }

    private EnemyHpBarView CreateHealthBar(EnemyHpBarView prefab)
    {
        if (prefab == null || healthBarRoot == null)
        {
            Debug.LogError("[WorldUiOverlayService] 체력바 프리팹 또는 루트가 연결되지 않았습니다.", this);
            return null;
        }

        return Instantiate(prefab, healthBarRoot, false);
    }

    private static EnemyHpBarView DequeueValid(Queue<EnemyHpBarView> pool)
    {
        while (pool.Count > 0)
        {
            EnemyHpBarView view = pool.Dequeue();
            if (view != null)
                return view;
        }

        return null;
    }
}
