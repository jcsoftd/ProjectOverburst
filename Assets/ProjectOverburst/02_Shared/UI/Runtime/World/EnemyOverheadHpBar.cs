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
    private EnemyRank rank;
    private EnemyIdentity identity;
    private EnemyMovementReaction movementReaction;
    private EnemyHpBarTier activeTier;

    private void Awake()
    {
        health = GetComponent<CombatHealth>();
        targetCollider = GetComponent<Collider>();
        if (targetCollider == null)
            targetCollider = GetComponentInChildren<Collider>();
        rank = GetComponent<EnemyRank>();
        identity = GetComponent<EnemyIdentity>();
        movementReaction = GetComponent<EnemyMovementReaction>();
    }

    private void OnEnable()
    {
        activeTier = ResolveTier();
        AcquireView();
    }

    private void LateUpdate()
    {
        EnemyHpBarTier currentTier = ResolveTier();
        if (activeView != null && currentTier != activeTier)
        {
            ReleaseView();
            activeTier = currentTier;
        }

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

        activeTier = ResolveTier();
        activeView = overlayService.AcquireHealthBar(health, activeTier);
    }

    private void ReleaseView()
    {
        if (activeView == null)
            return;

        if (overlayService != null)
            overlayService.ReleaseHealthBar(activeView, activeTier);
        else
            Destroy(activeView.gameObject);

        activeView = null;
    }

    private EnemyHpBarTier ResolveTier()
    {
        if (rank != null && rank.Rank == EnemyRankType.Elite)
            return EnemyHpBarTier.Elite;
        if (identity != null && identity.GradeType != EnemyGradeType.Normal)
            return EnemyHpBarTier.Elite;

        EnemyHitWeightProfile profile = movementReaction != null ? movementReaction.HitWeightProfile : null;
        if (profile != null)
        {
            if (profile.Weight == EnemyHitWeight.Light) return EnemyHpBarTier.Small;
        }
        return EnemyHpBarTier.Medium;
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
