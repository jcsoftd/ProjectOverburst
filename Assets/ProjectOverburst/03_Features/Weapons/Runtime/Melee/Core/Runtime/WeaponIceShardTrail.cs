using UnityEngine;

/// <summary>Leaves small shatter fragments along the moving blade while ice energy is present.</summary>
[DisallowMultipleComponent]
public sealed class WeaponIceShardTrail : MonoBehaviour
{
    [SerializeField] private ParticleSystem particles;
    [SerializeField, Min(0f)] private float particlesPerMeter = 48.3f;
    [SerializeField] private Vector2 smallSize = new Vector2(.045f, .090f);
    [SerializeField] private Vector2 largeSize = new Vector2(.100f, .135f);
    [SerializeField, Range(0f, 1f)] private float largeChance = .20f;
    [SerializeField] private Vector2 lifetime = new Vector2(.32f, .44f);
    private ParticleSystemRenderer particleRenderer;
    private System.Random random;
    private float energy, carry, sizeScale = 1f, lifetimeScale = 1f, densityScale = 1f, spreadScale = 1f;
    private bool manualClock, hasPrevious;
    private Vector3 previousBase, previousTip;
    private int emitted;

    public int ParticleCount => particles != null ? particles.particleCount : 0;

    public void Configure(MeleeWeaponElementFx.ElementTuning tuning, bool preview)
    {
        manualClock = preview;
        // Keep the existing shared trail controls relative to the approved appearance.
        sizeScale = Mathf.Max(0f, tuning.trailParticleSize / 5f * tuning.trailScale / .9f);
        lifetimeScale = Mathf.Max(0f, tuning.trailParticleLifetime / .34f);
        densityScale = Mathf.Max(0f, tuning.trailDensity / 2.03f);
        spreadScale = Mathf.Max(0f, tuning.trailSpread / .1f);
        if (particles == null) return;
        var main = particles.main;
        main.gravityModifier = .015f * spreadScale;
    }

    public void SetEnergy(float normalizedEnergy)
    {
        energy = Mathf.Clamp01(normalizedEnergy);
        if (particles == null) return;
        if (particleRenderer == null) particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        if (particleRenderer != null) particleRenderer.enabled = energy > 0f;
        if (energy <= 0f) { Clear(); return; }
        if (random == null) random = new System.Random(171);
        if (!particles.isPlaying)
        {
            particles.Play(false);
            if (manualClock) particles.Pause(false);
        }
    }

    // The weapon supplies its final blade pose; no transform searches or per-frame allocations.
    public void Sample(float seconds, Vector3 bladeBase, Vector3 bladeTip)
    {
        if (particles == null || energy <= 0f || seconds <= 0f) return;
        if (random == null) random = new System.Random(171);
        if (!particles.isPlaying) particles.Play(false);
        if (manualClock)
        {
            particles.Simulate(seconds, false, false, false);
            particles.Pause(false);
        }
        if (!hasPrevious)
        {
            previousBase = bladeBase; previousTip = bladeTip; hasPrevious = true;
            return;
        }
        Vector3 movement = bladeTip - previousTip;
        float distance = movement.magnitude;
        float bladeLength = Vector3.Distance(bladeBase, bladeTip);
        if (distance > Mathf.Max(1f, bladeLength * 2f))
        {
            Clear(); previousBase = bladeBase; previousTip = bladeTip; hasPrevious = true;
            return;
        }
        carry += distance * particlesPerMeter * densityScale * Mathf.Pow(energy, .65f);
        int requested = Mathf.FloorToInt(carry);
        carry -= requested;
        int count = Mathf.Min(requested, particles.main.maxParticles);
        float energySize = Mathf.Pow(energy, .35f) * sizeScale;
        for (int i = 0; i < count; i++)
        {
            float progress = (i + 1f) / Mathf.Max(1, count);
            float alongBlade = .18f + .82f * ((emitted * 37) % 97) / 96f;
            Vector3 from = Vector3.Lerp(previousBase, bladeBase, progress);
            Vector3 to = Vector3.Lerp(previousTip, bladeTip, progress);
            bool large = random.NextDouble() < largeChance;
            Vector2 sizeRange = large ? largeSize : smallSize;
            var parameters = new ParticleSystem.EmitParams
            {
                position = Vector3.Lerp(from, to, alongBlade),
                velocity = (movement.normalized * Mathf.Sin(emitted * 1.71f) * .045f + Vector3.down * .018f) * spreadScale,
                startSize = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)random.NextDouble()) * energySize,
                startLifetime = Mathf.Lerp(lifetime.x, lifetime.y, (float)random.NextDouble()) * lifetimeScale,
                rotation = emitted * 67 % 360
            };
            particles.Emit(parameters, 1);
            emitted++;
        }
        previousBase = bladeBase; previousTip = bladeTip;
    }

    public void Clear()
    {
        hasPrevious = false; carry = 0f; emitted = 0; random = null;
        if (particles != null) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDisable() => Clear();
}
