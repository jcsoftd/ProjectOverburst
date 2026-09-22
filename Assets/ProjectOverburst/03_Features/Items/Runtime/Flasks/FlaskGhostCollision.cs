using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FlaskGhostCollision : MonoBehaviour
{
    private PlayerFlaskController flasks;
    private CharacterController body;
    private readonly List<EnemyRank> enemies = new List<EnemyRank>();
    private readonly HashSet<Collider> ignored = new HashSet<Collider>();
    private readonly List<Collider> expired = new List<Collider>();
    private float nextScan;
    private void Awake() { flasks = GetComponent<PlayerFlaskController>(); body = GetComponent<CharacterController>(); }
    private void FixedUpdate()
    {
        if (flasks == null || body == null || !body.enabled || !flasks.Effects.PassesEnemyBodies)
        { Restore(); return; }
        if (Time.time < nextScan) return;
        nextScan = Time.time + .1f;
        EnemyRank.CollectActive(enemies);
        foreach (var enemy in enemies)
        {
            if (enemy.GradeType != EnemyGradeType.Normal || (enemy.transform.position-transform.position).sqrMagnitude > 36f) continue;
            foreach (var collider in enemy.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || !collider.enabled || ignored.Contains(collider) || Physics.GetIgnoreCollision(body, collider)) continue;
                Physics.IgnoreCollision(body, collider, true);
                ignored.Add(collider);
            }
        }
        expired.Clear();
        foreach (var collider in ignored)
            if (collider == null || !collider.gameObject.activeInHierarchy || (collider.transform.position-transform.position).sqrMagnitude > 64f) expired.Add(collider);
        foreach (var collider in expired) { if (collider != null) Physics.IgnoreCollision(body, collider, false); ignored.Remove(collider); }
    }
    public void Restore()
    {
        foreach (var collider in ignored) if (collider != null && body != null) Physics.IgnoreCollision(body, collider, false);
        ignored.Clear();
    }
    private void OnDisable() => Restore();
}
