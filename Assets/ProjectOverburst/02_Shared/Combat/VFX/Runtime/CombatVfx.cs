using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class CombatVfx : MonoBehaviour // 전투 VFX 연결: 치명타 섬광·처치 혈흔
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private Transform hitVfxAnchor;

    private bool deathVfxSpawned;
    private CombatTarget combatTarget;
    private CombatTargetVfxPlacement vfxPlacement;

    private void Awake()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();
        combatTarget = GetComponent<CombatTarget>();
        vfxPlacement = GetComponent<CombatTargetVfxPlacement>();
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

    public void ResetForPool()
    {
        deathVfxSpawned = false;
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (info.isDamageOverTime || !info.triggersOnHitEffects)
            return;
        if (info.isCritical)
            PlayCritical(info); // 치명타 공용 섬광: 원소 타격 VFX와 별개로 한 번
    }

    private void PlayCritical(DamageInfo info)
    {
        Transform anchor = hitVfxAnchor != null ? hitVfxAnchor : transform;
        bool hasHitPoint = info.hitPoint.sqrMagnitude > 0.0001f;
        Vector3 contact = hasHitPoint ? info.hitPoint : anchor.position;
        float size = 1f;
        if (combatTarget != null && vfxPlacement != null)
        {
            if (!hasHitPoint) contact = combatTarget.CurrentHurtVolume.Center;
            contact = CombatTargetVfxPlacement.ResolveContact(combatTarget, contact, info.direction, out size);
        }
        CritHitVfxService.TryPlay(info.element, contact, size);
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (deathVfxSpawned)
            return; // 중복 방지

        deathVfxSpawned = true;
        BloodHitVfxService.RequestDeath(source, info); // 처치 혈흔 분출(혈흔 없는 몬스터는 내부에서 제외)
    }
}
