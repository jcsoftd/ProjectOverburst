using UnityEngine;

// A spit blob: a local cluster of liquid drops (the body) and world-space drips that fall off the
// flight path (the trail). At impact the body vanishes and the drips already in the air keep falling.
[DisallowMultipleComponent]
public sealed class EnemyBioProjectileVisual : MonoBehaviour
{
    private static readonly int MainColor = Shader.PropertyToID("_MainColor");
    private static readonly int SecondaryColor = Shader.PropertyToID("_SecondaryColor");
    private static readonly int SpecularColor = Shader.PropertyToID("_SpecularColor");
    private static readonly int SpecularValue = Shader.PropertyToID("_SpecularValue");

    [SerializeField] private ParticleSystem[] body = System.Array.Empty<ParticleSystem>();
    [SerializeField] private ParticleSystem[] trail = System.Array.Empty<ParticleSystem>();
    private ParticleSystemRenderer[] renderers;
    private MaterialPropertyBlock block;

    public void Configure(ParticleSystem[] bodySystems, ParticleSystem[] trailSystems)
    {
        body = bodySystems;
        trail = trailSystems;
    }

    public void Launch(BloodHitProfile tint, float scale)
    {
        transform.localScale = Vector3.one * Mathf.Max(.1f, scale);
        if (tint != null)
        {
            if (renderers == null) renderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
            if (block == null) block = new MaterialPropertyBlock();
            foreach (ParticleSystemRenderer renderer in renderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(MainColor, tint.mainColor);
                block.SetColor(SecondaryColor, tint.secondaryColor);
                block.SetColor(SpecularColor, tint.specularColor);
                block.SetFloat(SpecularValue, tint.specular);
                renderer.SetPropertyBlock(block);
            }
        }
        foreach (ParticleSystem system in body) if (system != null) { system.Clear(true); system.Play(true); }
        foreach (ParticleSystem system in trail) if (system != null) { system.Clear(true); system.Play(true); }
    }

    public void Stop()
    {
        foreach (ParticleSystem system in body)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (ParticleSystem system in trail)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
