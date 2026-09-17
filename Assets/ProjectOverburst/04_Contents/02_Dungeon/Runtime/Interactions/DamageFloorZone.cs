using System.Collections.Generic;
using UnityEngine;

public class DamageFloorZone : MonoBehaviour
{
    [SerializeField] private float damagePerTick = 3f;
    [SerializeField] private float tickInterval = 1f;
    [SerializeField] private float slowDuration = 30f;
    [SerializeField] private float slowMoveSpeedMultiplier = 0.7f;
    [SerializeField] private GameObject tickVfxPrefab;
    [SerializeField] private AudioClip tickSound;

    private readonly Dictionary<CombatHealth, int> overlapCounts = new Dictionary<CombatHealth, int>();
    private readonly Dictionary<CombatHealth, float> nextTickTimes = new Dictionary<CombatHealth, float>();
    private readonly List<CombatHealth> targetsToRemove = new List<CombatHealth>();

    private void Update()
    {
        if (overlapCounts.Count == 0)
            return;

        float now = Time.time;
        targetsToRemove.Clear();

        foreach (KeyValuePair<CombatHealth, int> entry in overlapCounts)
        {
            CombatHealth health = entry.Key;
            if (health == null || health.IsDead || entry.Value <= 0)
            {
                targetsToRemove.Add(health);
                continue;
            }

            float nextTickTime = nextTickTimes.TryGetValue(health, out float storedTickTime) ? storedTickTime : now;
            if (now < nextTickTime)
                continue;

            DealTickDamage(health);
            nextTickTimes[health] = now + Mathf.Max(0.05f, tickInterval);
        }

        for (int i = 0; i < targetsToRemove.Count; i++)
            RemoveTarget(targetsToRemove[i]);
    }

    private void OnTriggerEnter(Collider other)
    {
        CombatHealth health = FindPlayerHealth(other);
        if (health == null)
            return;

        if (overlapCounts.TryGetValue(health, out int count))
        {
            overlapCounts[health] = count + 1;
            return;
        }

        overlapCounts.Add(health, 1);
        nextTickTimes[health] = Time.time + Mathf.Max(0.05f, tickInterval);
    }

    private void OnTriggerExit(Collider other)
    {
        CombatHealth health = FindPlayerHealth(other);
        if (health == null || !overlapCounts.TryGetValue(health, out int count))
            return;

        count--;
        if (count > 0)
        {
            overlapCounts[health] = count;
            return;
        }

        RemoveTarget(health);
    }

    private void DealTickDamage(CombatHealth health)
    {
        Vector3 direction = health.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            direction.Normalize();

        DamageInfo info = new DamageInfo(
            Mathf.Max(0f, damagePerTick),
            health.transform.position,
            gameObject,
            direction,
            0f,
            false,
            false,
            true);

        float hpBeforeDamage = health.CurrentHp;
        health.TakeDamage(info);
        if (hpBeforeDamage - health.CurrentHp > 0f)
        {
            ApplySlowDebuff(health);
            PlayerDamageFeedback.TriggerGlobal();
        }

        PlayTickFeedback(health.transform.position);
    }

    private void ApplySlowDebuff(CombatHealth health)
    {
        PlayerBuffController buffController = health.GetComponentInParent<PlayerBuffController>();
        if (buffController == null)
            buffController = health.gameObject.AddComponent<PlayerBuffController>();

        buffController.ApplySlowDebuff(slowDuration, slowMoveSpeedMultiplier);
    }

    private CombatHealth FindPlayerHealth(Collider other)
    {
        if (other == null)
            return null;

        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null && !IsPlayerTagged(other.transform))
            return null;

        CombatHealth health = other.GetComponentInParent<CombatHealth>();
        if (health != null)
            return health;

        return player != null ? player.GetComponentInChildren<CombatHealth>() : null;
    }

    private bool IsPlayerTagged(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (current.CompareTag("Player"))
                return true;

            current = current.parent;
        }

        return false;
    }

    private void PlayTickFeedback(Vector3 position)
    {
        if (tickVfxPrefab != null)
            Instantiate(tickVfxPrefab, position, Quaternion.identity);

        if (tickSound != null)
            AudioSource.PlayClipAtPoint(tickSound, position);
    }

    private void RemoveTarget(CombatHealth health)
    {
        if (ReferenceEquals(health, null))
            return;

        overlapCounts.Remove(health);
        nextTickTimes.Remove(health);
    }
}
