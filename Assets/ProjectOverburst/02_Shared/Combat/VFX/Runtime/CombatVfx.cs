using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class CombatVfx : MonoBehaviour // 전투 VFX 연결
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField] private Transform hitVfxAnchor;
    [SerializeField] private Transform deathVfxAnchor;

    private bool deathVfxSpawned;
    private GameObject authoredHitVfxPrefab;
    private GameObject authoredDeathVfxPrefab;
    private bool authoredPresentationCaptured;

    private void Awake()
    {
        CaptureAuthoredPresentation();
        if (health == null)
            health = GetComponent<CombatHealth>();
    }

    private void OnEnable()
    {
        deathVfxSpawned = false;

        if (health != null)
        {
            health.OnDamaged += HandleDamaged;
            health.OnDead += HandleDead;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.OnDamaged -= HandleDamaged;
            health.OnDead -= HandleDead;
        }
    }

    public void Configure(GameObject hitPrefab, GameObject deathPrefab)
    {
        CaptureAuthoredPresentation();
        hitVfxPrefab = hitPrefab;
        deathVfxPrefab = deathPrefab;
    }

    public void ResetForPool()
    {
        CaptureAuthoredPresentation();
        hitVfxPrefab = authoredHitVfxPrefab;
        deathVfxPrefab = authoredDeathVfxPrefab;
        deathVfxSpawned = false;
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (info.isDamageOverTime || !info.triggersOnHitEffects || info.suppressDefaultHitVfx)
            return; // 정식 원소 Hit와 임시 Hit 중복 차단

        Transform anchor = hitVfxAnchor != null ? hitVfxAnchor : transform;
        Vector3 localPosition = Vector3.zero;

        if (info.hitPoint.sqrMagnitude > 0.0001f)
            localPosition = anchor.InverseTransformPoint(info.hitPoint); // 피격점

        VfxPrefabFactory.SpawnFollowing(hitVfxPrefab, anchor, localPosition, false, true);
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (deathVfxSpawned)
            return; // 중복 방지

        deathVfxSpawned = true;
        if (TryGetComponent<EnemyDeathPresentation>(out var presentation) && presentation.isActiveAndEnabled)
            return; // Theme corpses use authored death + Feel landing, without the temporary burst.
        VfxPrefabFactory.Spawn(deathVfxPrefab, GetAnchorPosition(deathVfxAnchor), Quaternion.identity);
    }

    private Vector3 GetAnchorPosition(Transform anchor)
    {
        if (anchor != null)
            return anchor.position;

        return transform.position;
    }

    private void CaptureAuthoredPresentation()
    {
        if (authoredPresentationCaptured)
            return;

        authoredHitVfxPrefab = hitVfxPrefab;
        authoredDeathVfxPrefab = deathVfxPrefab;
        authoredPresentationCaptured = true;
    }
}
