using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class EnemyController : MonoBehaviour
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private bool disableCollidersOnDeath = true;
    [SerializeField] private bool deactivateOnDeath = true;
    [SerializeField] private float deactivateDelay = 1.25f;

    public bool IsDead { get; private set; }
    public bool OwnsDeathLifetime => deactivateOnDeath;
    private Rigidbody body;
    private bool deathPhysicsHeld;
    private bool aliveKinematic;
    private bool aliveGravity;

    private void Awake()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();
    }

    private void OnEnable()
    {
        RestoreDeathPhysics();
        IsDead = health != null && health.IsDead;
        if (health != null)
            health.OnDead += HandleDead;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDead -= HandleDead;
        RestoreDeathPhysics();
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (IsDead)
            return;

        IsDead = true;
        // Removing the collision body must never leave a gravity-driven corpse.
        body = GetComponent<Rigidbody>();
        if (body != null)
        {
            aliveKinematic = body.isKinematic;
            aliveGravity = body.useGravity;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
            deathPhysicsHeld = true;
        }
        if (GetComponent<EnemyAIController>() == null)
        {
            EnemyMovement mover = GetComponent<EnemyMovement>();
            if (mover != null)
                mover.StopForDeath();
        }

        if (disableCollidersOnDeath)
            DisableColliders();
        if (deactivateOnDeath)
            StartCoroutine(DeactivateAfterDelay());
    }

    private void DisableColliders()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    private void RestoreDeathPhysics()
    {
        if (!deathPhysicsHeld || body == null) return;
        body.isKinematic = aliveKinematic;
        body.useGravity = aliveGravity;
        deathPhysicsHeld = false;
    }

    private IEnumerator DeactivateAfterDelay()
    {
        float resolvedDelay = ResolveDeactivateDelay();
        if (resolvedDelay > 0f)
            yield return new WaitForSeconds(resolvedDelay);

        var corpse = GetComponent<EnemyCorpseFade>();
        if (corpse != null) yield return corpse.Fade();

        EnemyActor actor = GetComponent<EnemyActor>();
        if (actor == null || !actor.RequestPoolRelease())
            gameObject.SetActive(false);
    }

    private float ResolveDeactivateDelay()
    {
        float resolvedDelay = Mathf.Max(0f, deactivateDelay);
        EnemyActor actor = GetComponent<EnemyActor>();
        EnemyAnimationProfile profile = actor != null
            && actor.Definition != null
            ? actor.Definition.AnimationProfile
            : null;
        AnimationClip deathClip = profile != null ? profile.Death : null;
        if (deathClip != null)
            resolvedDelay = Mathf.Max(resolvedDelay, deathClip.length + 0.4f);

        return resolvedDelay;
    }
}
