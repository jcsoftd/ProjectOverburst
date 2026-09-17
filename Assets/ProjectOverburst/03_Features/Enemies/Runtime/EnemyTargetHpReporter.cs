using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyTargetHpReporter : MonoBehaviour
{
    private CombatHealth health;

    private void Awake()
    {
        health = GetComponent<CombatHealth>();
    }

    private void OnEnable()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();

        if (health != null)
            health.OnDamaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDamaged -= HandleDamaged;
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (source == null || source.IsDead)
            return;

        EnemyTargetHpHud.ReportPlayerDamage(source, info); // 대상 HUD
    }
}
