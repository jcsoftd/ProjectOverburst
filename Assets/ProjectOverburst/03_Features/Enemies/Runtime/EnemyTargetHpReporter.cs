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
            health.OnDamageResolved += HandleDamageResolved;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDamageResolved -= HandleDamageResolved;
        EnemyTargetHpHud.ForgetTarget(health);
    }

    private void HandleDamageResolved(CombatHealth source, DamageInfo info, float appliedDamage, bool lethal)
    {
        if (source == null || appliedDamage <= 0f)
            return;

        EnemyTargetHpHud.ReportPlayerDamage(source, info); // 대상 HUD
    }
}
