using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerLevelUpVfx : MonoBehaviour
{
    private const string PrefabResourcePath = "VFX/PF_PlayerLevelUpVfx";
    private const float EffectLifetimeSeconds = 5f;

    private PlayerProgression progression;
    private PlayerContext context;
    private GameObject prefab;
    private GameObject activeEffect;
    private Coroutine releaseRoutine;
    private bool warnedMissingPrefab;

    public void Bind(PlayerProgression nextProgression, PlayerContext nextContext)
    {
        if (progression != null)
            progression.LeveledUp -= Play;
        progression = nextProgression;
        context = nextContext;
        if (progression != null)
            progression.LeveledUp += Play;
    }

    private void OnDisable()
    {
        if (progression != null)
            progression.LeveledUp -= Play;
        progression = null;
        context = null;
        if (releaseRoutine != null)
            StopCoroutine(releaseRoutine);
        releaseRoutine = null;
        if (activeEffect != null)
            Destroy(activeEffect);
        activeEffect = null;
    }

    private void Play(int previousLevel, int currentLevel)
    {
        PlayerActorRuntime actor = context != null ? context.CurrentActor : null;
        if (actor == null || !actor.isActiveAndEnabled)
            return;

        if (prefab == null)
            prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            if (!warnedMissingPrefab)
            {
                Debug.LogWarning("[PlayerLevelUpVfx] Missing prefab: " + PrefabResourcePath);
                warnedMissingPrefab = true;
            }
            return;
        }

        if (releaseRoutine != null)
            StopCoroutine(releaseRoutine);
        if (activeEffect != null)
            Destroy(activeEffect);

        activeEffect = Instantiate(prefab, actor.transform);
        activeEffect.name = "PlayerLevelUpVfx";
        activeEffect.transform.localPosition = Vector3.zero;
        activeEffect.transform.localRotation = Quaternion.identity;
        activeEffect.transform.localScale = prefab.transform.localScale;
        ParticleSystem rootParticle = activeEffect.GetComponent<ParticleSystem>();
        if (rootParticle != null)
        {
            rootParticle.Clear(true);
            rootParticle.Play(true);
        }
        releaseRoutine = StartCoroutine(ReleaseAfterDelay(activeEffect));
    }

    private IEnumerator ReleaseAfterDelay(GameObject effect)
    {
        yield return new WaitForSeconds(EffectLifetimeSeconds);
        if (effect != null)
            Destroy(effect);
        if (activeEffect == effect)
        {
            activeEffect = null;
            releaseRoutine = null;
        }
    }
}
