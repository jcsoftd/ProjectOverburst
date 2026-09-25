using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Combat/Melee/One Hand Sword Shockwave Playback")]
public sealed class SwordShockwavePlayback : MonoBehaviour, ITransientVfxPlayback
{
    [SerializeField, Min(0.01f)] private float expansionDuration = 0.28f;
    [SerializeField, Min(0.01f)] private float startScale = 0.72f;
    [SerializeField, Min(0.01f)] private float endScale = 1.16f;

    private ParticleSystem[] particles;
    private Vector3 resolvedScale;
    private float elapsed;
    private bool expanding;

    public void RestartVfx()
    {
        resolvedScale = transform.localScale;
        elapsed = 0f;
        expanding = true;
        ApplyScale(0f);

        CacheParticles();
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null || !particles[i].gameObject.activeInHierarchy)
                continue;
            particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles[i].Play(false);
        }
    }

    public void StopAndClearVfx()
    {
        expanding = false;
        CacheParticles();
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] != null)
                particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void Update()
    {
        if (!expanding)
            return;

        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / expansionDuration);
        ApplyScale(progress);
        if (progress >= 1f)
            expanding = false;
    }

    private void ApplyScale(float progress)
    {
        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        transform.localScale = resolvedScale * Mathf.Lerp(startScale, endScale, eased);
    }

    private void CacheParticles()
    {
        if (particles == null)
            particles = GetComponentsInChildren<ParticleSystem>(true);
    }
}
