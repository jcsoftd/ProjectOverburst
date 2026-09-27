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
    private float intensity = 1f, playbackSpeed = 1f;
    private float[] authoredSimulationSpeeds;
    private Renderer[] intensityRenderers;
    private static readonly int Distortion = Shader.PropertyToID("_Distortion");

    public void Configure(float intensityMultiplier, float speedMultiplier)
    {
        CacheParticles();
        intensity = Mathf.Clamp(intensityMultiplier, .05f, 4f);
        playbackSpeed = Mathf.Clamp(speedMultiplier, .1f, 4f);
        for (int i = 0; i < particles.Length; i++)
        {
            var main = particles[i].main;
            main.simulationSpeed = authoredSimulationSpeeds[i] * playbackSpeed;
        }
        var block = new MaterialPropertyBlock();
        foreach (var renderer in intensityRenderers)
        {
            var material = renderer.sharedMaterial;
            if (material == null || !material.HasProperty(Distortion)) continue;
            renderer.GetPropertyBlock(block);
            block.SetFloat(Distortion, material.GetFloat(Distortion) * intensity);
            renderer.SetPropertyBlock(block);
        }
    }

    public static float ResolveCueLifetime(GameObject prefab, float lifetime, float speed)
    {
        return prefab != null && prefab.GetComponent<SwordShockwavePlayback>() != null
            ? TransientVfxPool.ResolveLifetime(prefab, lifetime) / Mathf.Clamp(speed, .1f, 4f) : lifetime;
    }

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

    private void Update() => Advance(Time.deltaTime);

    public void Advance(float deltaTime)
    {
        if (!expanding)
            return;

        elapsed += Mathf.Max(0f, deltaTime) * playbackSpeed;
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
        {
            particles = GetComponentsInChildren<ParticleSystem>(true);
            authoredSimulationSpeeds = new float[particles.Length];
            for (int i = 0; i < particles.Length; i++) authoredSimulationSpeeds[i] = particles[i].main.simulationSpeed;
        }
        if (waveRenderers == null)
            waveRenderers = GetComponentsInChildren<MeshRenderer>(true);
        if (intensityRenderers == null) intensityRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ApplyWaveProgress(float progress, bool visible)
    {
        if (waveRenderers == null || waveRenderers.Length == 0)
            return;
        if (waveProperties == null)
            waveProperties = new MaterialPropertyBlock();
        waveProperties.SetFloat(WaveProgress, progress);
        waveProperties.SetFloat(WaveOpacity, visible ? intensity : 0f);
        foreach (MeshRenderer renderer in waveRenderers)
        {
            if (renderer == null) continue;
            renderer.SetPropertyBlock(waveProperties);
            renderer.enabled = visible;
        }
    }
}
