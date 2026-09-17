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

    private void Awake()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();
    }

    private void OnEnable()
    {
        IsDead = health != null && health.IsDead;
        if (health != null)
            health.OnDead += HandleDead;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDead -= HandleDead;
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (IsDead)
            return;

        IsDead = true;
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

    private IEnumerator DeactivateAfterDelay()
    {
        float resolvedDelay = ResolveDeactivateDelay();
        if (resolvedDelay > 0f)
            yield return new WaitForSeconds(resolvedDelay);

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
            resolvedDelay = Mathf.Max(resolvedDelay, deathClip.length + 0.05f);

        return resolvedDelay;
    }
}
