using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Combat/Melee/One Hand Sword Shockwave Playback")]
public sealed class SwordShockwavePlayback : MonoBehaviour, ITransientVfxPlayback
{
    [SerializeField, Min(0.01f)] private float expansionDuration = 0.28f;
    [SerializeField, Min(0.01f)] private float startScale = 0.72f;
    [SerializeField, Min(0.01f)] private float endScale = 1.16f;
    [SerializeField] private OneHandSwordDistortionStyle comparisonStyle;

    private ParticleSystem[] particles;
    private MeshRenderer[] waveRenderers;
    private MaterialPropertyBlock waveProperties;
    private static readonly int WaveProgress = Shader.PropertyToID("_WaveProgress");
    private static readonly int WaveOpacity = Shader.PropertyToID("_WaveOpacity");
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
        if (comparisonStyle != null && comparisonStyle.version == SwordDistortionVersion.BladeTrail)
        {
            StopAndClearVfx();
            return;
        }
        ApplyWaveProgress(0f, true);
        SwordDistortionSurfaces.Set(this, true);
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
        SwordDistortionSurfaces.Set(this, false);
        expanding = false;
        CacheParticles();
        ApplyWaveProgress(1f, false);
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
        ApplyWaveProgress(progress, progress < 1f);
        if (progress >= 1f)
        {
            expanding = false;
            SwordDistortionSurfaces.Set(this, false);
        }
    }

    private void OnDisable() => SwordDistortionSurfaces.Set(this, false);

    private void ApplyScale(float progress)
    {
        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        transform.localScale = resolvedScale * Mathf.Lerp(startScale, endScale, eased);
    }

    private void CacheParticles()
    {
        if (particles == null)
            particles = GetComponentsInChildren<ParticleSystem>(true);
        if (waveRenderers == null)
            waveRenderers = GetComponentsInChildren<MeshRenderer>(true);
    }

    private void ApplyWaveProgress(float progress, bool visible)
    {
        if (waveRenderers == null || waveRenderers.Length == 0)
            return;
        if (waveProperties == null)
            waveProperties = new MaterialPropertyBlock();
        waveProperties.SetFloat(WaveProgress, progress);
        waveProperties.SetFloat(WaveOpacity, visible ? 1f : 0f);
        foreach (MeshRenderer renderer in waveRenderers)
        {
            if (renderer == null) continue;
            renderer.SetPropertyBlock(waveProperties);
            renderer.enabled = visible;
        }
    }
}
