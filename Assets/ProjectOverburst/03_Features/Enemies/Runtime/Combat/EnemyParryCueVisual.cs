using UnityEngine;

// Shared visual configuration; no global registration, feedbacks, audio or pooling.
public static class EnemyParryCueVisual
{
    public static void Configure(ParticleSystem particles, Material material)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = .70f;
        main.startLifetime = .70f;
        main.startSpeed = 0f;
        main.startSize = 3.2f;
        main.startColor = new Color(4f, 3.2f, 1.7f, 1f);
        main.maxParticles = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        var shape = particles.shape;
        shape.enabled = false;
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var fadeGradient = new Gradient();
        fadeGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, .80f),
                new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, .80f), new GradientAlphaKey(0f, 1f) });
        fade.color = fadeGradient;
        var pulse = particles.sizeOverLifetime;
        pulse.enabled = true;
        pulse.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1.1f), new Keyframe(.08f, 1.45f),
            new Keyframe(.25f, .85f), new Keyframe(.8f, .6f),
            new Keyframe(1f, .35f)));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
