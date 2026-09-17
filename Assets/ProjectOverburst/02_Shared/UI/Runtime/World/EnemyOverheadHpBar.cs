using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyOverheadHpBar : MonoBehaviour
{
    [SerializeField] private Transform hpBarAnchor;

    private CombatHealth health;
    private Collider targetCollider;
    private WorldUiOverlayService overlayService;
    private EnemyHpBarView activeView;
    private bool isElite;

    private void Awake()
    {
        health = GetComponent<CombatHealth>();
        targetCollider = GetComponent<Collider>();
        if (targetCollider == null)
            targetCollider = GetComponentInChildren<Collider>();

    }

    private void OnEnable()
    {
        isElite = TryGetComponent(out EnemyRank rank) && rank.Rank == EnemyRankType.Elite;
        AcquireView();
    }

    private void LateUpdate()
    {
        if (activeView == null)
            AcquireView();

        if (activeView == null || overlayService == null)
            return;

        bool visible = overlayService.TryProject(ResolveHeadWorldPosition(), out Vector2 anchoredPosition);
        activeView.SetProjectionVisible(visible);
        if (visible)
            activeView.RectTransform.anchoredPosition = anchoredPosition;
    }

    private void OnDisable()
    {
        ReleaseView();
    }

    private void OnDestroy()
    {
        ReleaseView();
    }

    private void AcquireView()
    {
        if (activeView != null || health == null)
            return;

        if (!WorldUiOverlayService.TryResolve(out overlayService))
            return;

        activeView = overlayService.AcquireHealthBar(health, isElite);
    }

    private void ReleaseView()
    {
        if (activeView == null)
            return;

        if (overlayService != null)
            overlayService.ReleaseHealthBar(activeView, isElite);
        else
            Destroy(activeView.gameObject);

        activeView = null;
    }

    private Vector3 ResolveHeadWorldPosition()
    {
        if (hpBarAnchor != null)
            return hpBarAnchor.position; // 신규 Actor는 제작된 전용 기준점을 우선 사용

        if (targetCollider != null)
            return new Vector3(targetCollider.bounds.center.x, targetCollider.bounds.max.y + 0.25f, targetCollider.bounds.center.z);

        return transform.position + Vector3.up * 1.5f;
    }

    public Transform HpBarAnchor => hpBarAnchor;

    public void ConfigureAnchor(Transform anchor)
    {
        hpBarAnchor = anchor;
    }
}
