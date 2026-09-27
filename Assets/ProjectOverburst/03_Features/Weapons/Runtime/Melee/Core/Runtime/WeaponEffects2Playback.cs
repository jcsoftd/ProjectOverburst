using System;
using System.Collections.Generic;
using MeshEffects2;
using UnityEngine;
using UnityEngine.Rendering;

// Owns one complete supplier prefab. Explicit wake controls default to the authored values.
internal sealed class WeaponEffects2Playback : IDisposable
{
    // DemoScene places the effects at the guard (~-.16), with a blade ending at ~-1.31.
    // Emitter bounds are NOT blade bounds: some effects travel along the blade during their lifetime.
    internal const float AuthoredBladeLength = 1.15f;
    internal GameObject Root { get; }
    internal ParticleSystem[] Particles { get; }
    private readonly float[] timeRates, distanceRates;
    private readonly bool preview;
    private readonly List<VelocityClock> velocities = new List<VelocityClock>();
    private readonly List<GravityClock> gravities = new List<GravityClock>();
    private readonly List<LightClock> lights = new List<LightClock>();
    private ParticleSystem.Particle[] particleBuffer = Array.Empty<ParticleSystem.Particle>();
    private float elapsed;

    internal WeaponEffects2Playback(GameObject source, Transform parent, Vector3 position, Vector3 scale, bool editorPreview,
        float trailDensity = 1f, float trailSpread = 1f, float trailParticleSize = 1f, float trailParticleLifetime = 1f,
        Vector3? trailPosition = null, Vector3? trailScale = null)
    {
        preview = editorPreview;
        // Parent must be inactive so native Awake/OnEnable observe the final placement and scale.
        Root = UnityEngine.Object.Instantiate(source, parent, false);
        Root.transform.localPosition = position;
        Root.transform.localRotation = Quaternion.identity;
        Root.transform.localScale = scale;
        if (trailPosition.HasValue && trailScale.HasValue)
            ConfigureTrailPlacement(parent, trailPosition.Value, trailScale.Value);
        foreach (var scaler in Root.GetComponentsInChildren<ME2_ParticlesScale>(true))
        {
            // README: Effect1 uses this component, not Hierarchy particle scaling.
            scaler.Scale *= scale.x;
#if UNITY_EDITOR
            if (preview)
                typeof(ME2_ParticlesScale).GetMethod("Awake", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic).Invoke(scaler, null);
#endif
        }
        // The URP shadergraph patch samples the pipeline's opaque/depth textures.
        // The bundled legacy camera command buffer must not run against an SRP camera.
        if (GraphicsSettings.currentRenderPipeline != null)
            foreach (var distortion in Root.GetComponentsInChildren<ME2_CommandBufferDistortion>(true))
                distortion.enabled = false;
        if (preview)
        {
            foreach (var script in Root.GetComponentsInChildren<ME2_Velocity>(true))
                velocities.Add(new VelocityClock(script));
            foreach (var script in Root.GetComponentsInChildren<ME2_ParticleGravity>(true))
            {
                script.enabled = false; // The Editor uses the explicit preview clock below.
                gravities.Add(new GravityClock { script = script, particle = script.GetComponent<ParticleSystem>() });
            }
            foreach (var script in Root.GetComponentsInChildren<ME2_Light>(true))
                lights.Add(new LightClock { script = script, light = script.GetComponent<Light>(), intensity = script.GetComponent<Light>().intensity });
        }
        parent.gameObject.SetActive(true);
        Particles = Root.GetComponentsInChildren<ParticleSystem>(true);
        // Effect1's custom scaler uses the blade width for all referenced particles in Awake.
        // Correct only its world particles to their independently authored trail width.
        float trailWidthRatio = trailScale.HasValue ? trailScale.Value.x / Mathf.Max(.0001f, scale.x) : 1f;
        if (trailWidthRatio != 1f)
            foreach (var scaler in Root.GetComponentsInChildren<ME2_ParticlesScale>(true))
                if (scaler.AffectStartSize && scaler.ParticleSystems != null)
                    foreach (var p in scaler.ParticleSystems)
                        if (p != null && p.main.simulationSpace == ParticleSystemSimulationSpace.World)
                            ApplyParticleSize(p, trailWidthRatio);
        timeRates = new float[Particles.Length]; distanceRates = new float[Particles.Length];
        for (int i = 0; i < Particles.Length; i++)
        {
            var p = Particles[i];
            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            timeRates[i] = p.emission.rateOverTimeMultiplier;
            distanceRates[i] = p.emission.rateOverDistanceMultiplier;
            // World particles form the lingering wake; local particles remain the blade aura.
            if (p.main.simulationSpace != ParticleSystemSimulationSpace.World) continue;
            float density = Mathf.Clamp(trailDensity, .25f, 4f);
            timeRates[i] *= density;
            distanceRates[i] *= density;
            ApplySpread(p, Mathf.Clamp01(trailSpread));
            ApplyParticleSize(p, Mathf.Clamp(trailParticleSize, .1f, 5f));
            float lifetime = Mathf.Clamp(trailParticleLifetime, .1f, 5f);
            if (lifetime != 1f)
            {
                var main = p.main;
                main.startLifetime = ScaleCurve(main.startLifetime, lifetime);
            }
        }
    }

    private void ConfigureTrailPlacement(Transform container, Vector3 position, Vector3 scale)
    {
        // Keep emitters BELOW their original ancestors: supplier scripts discover renderers
        // with GetComponentsInChildren in OnEnable. Moving them to a sibling breaks that binding.
        var root = Root.transform;
        var world = new HashSet<Transform>();
        foreach (var p in Root.GetComponentsInChildren<ParticleSystem>(true))
            if (p.main.simulationSpace == ParticleSystemSimulationSpace.World) world.Add(p.transform);
        if (world.Count == 0) return;
        var frames = new Dictionary<Transform, Transform>();
        Transform CopyFrame(Transform original)
        {
            if (frames.TryGetValue(original, out var cached)) return cached;
            var path = new List<Transform>();
            for (var t = original; t != container; t = t.parent) path.Add(t);
            var frame = original;
            // Cancel the original frame exactly, including rotated nonuniform scale.
            // Two TRS nodes represent S^-1 R^-1 T^-1 without lossy matrix decomposition.
            foreach (var t in path)
            {
                var inverseScale = new GameObject("Trail inverse scale").transform;
                inverseScale.SetParent(frame, false);
                var s = t.localScale;
                inverseScale.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
                var inversePose = new GameObject("Trail inverse pose").transform;
                inversePose.SetParent(inverseScale, false);
                inversePose.localRotation = Quaternion.Inverse(t.localRotation);
                inversePose.localPosition = inversePose.localRotation * -t.localPosition;
                frame = inversePose;
            }
            var layout = new GameObject("Trail placement").transform;
            layout.SetParent(frame, false);
            layout.localPosition = position;
            layout.localScale = scale;
            frame = layout;
            // Reapply the authored frames below the package root, now in trail coordinates.
            for (int i = path.Count - 2; i >= 0; i--)
            {
                var t = path[i];
                var copy = new GameObject(t.name + " (trail frame)").transform;
                copy.SetParent(frame, false);
                copy.localPosition = t.localPosition;
                copy.localRotation = t.localRotation;
                copy.localScale = t.localScale;
                frame = copy;
            }
            frames.Add(original, frame);
            return frame;
        }
        var branches = new List<Transform>();
        foreach (var emitter in world)
        {
            bool nested = false;
            for (var ancestor = emitter.parent; ancestor != null && ancestor != root; ancestor = ancestor.parent)
                if (world.Contains(ancestor)) { nested = true; break; }
            if (!nested) branches.Add(emitter);
        }
        // External gravity anchors must use the same trail coordinate frame as their particles.
        foreach (var gravity in Root.GetComponentsInChildren<ME2_ParticleGravity>(true))
        {
            if (!world.Contains(gravity.transform) || gravity.Target == null) continue;
            bool movesWithBranch = false;
            foreach (var branch in branches)
                if (gravity.Target == branch || gravity.Target.IsChildOf(branch)) { movesWithBranch = true; break; }
            if (!movesWithBranch && gravity.Target.IsChildOf(root)) gravity.Target = CopyFrame(gravity.Target);
        }
        // Resolve parents before moving any branches, so ancestry always describes the original tree.
        var parents = new List<Transform>();
        foreach (var branch in branches) parents.Add(CopyFrame(branch.parent));
        for (int i = 0; i < branches.Count; i++) branches[i].SetParent(parents[i], false);
    }

    private static void ApplyParticleSize(ParticleSystem p, float amount)
    {
        if (amount == 1f) return;
        var main = p.main;
        if (main.startSize3D)
        {
            main.startSizeX = ScaleCurve(main.startSizeX, amount);
            main.startSizeY = ScaleCurve(main.startSizeY, amount);
            main.startSizeZ = ScaleCurve(main.startSizeZ, amount);
        }
        else main.startSize = ScaleCurve(main.startSize, amount);
        var trails = p.trails;
        // Size-aware trails already inherit the particle size; do not scale them twice.
        if (trails.enabled && !trails.sizeAffectsWidth)
            trails.widthOverTrail = ScaleCurve(trails.widthOverTrail, amount);
    }

    // Applied once to a fresh instance, never repeatedly to already scaled module values.
    private static void ApplySpread(ParticleSystem p, float amount)
    {
        if (amount == 1f) return;
        var main = p.main;
        main.startSpeed = ScaleCurve(main.startSpeed, amount);
        main.gravityModifier = ScaleCurve(main.gravityModifier, amount);
        var velocity = p.velocityOverLifetime;
        velocity.x = ScaleCurve(velocity.x, amount); velocity.y = ScaleCurve(velocity.y, amount); velocity.z = ScaleCurve(velocity.z, amount);
        velocity.orbitalX = ScaleCurve(velocity.orbitalX, amount);
        velocity.orbitalY = ScaleCurve(velocity.orbitalY, amount);
        velocity.orbitalZ = ScaleCurve(velocity.orbitalZ, amount);
        velocity.radial = ScaleCurve(velocity.radial, amount);
        var inherit = p.inheritVelocity; inherit.curve = ScaleCurve(inherit.curve, amount);
        var force = p.forceOverLifetime;
        force.x = ScaleCurve(force.x, amount); force.y = ScaleCurve(force.y, amount); force.z = ScaleCurve(force.z, amount);
        var noise = p.noise;
        noise.positionAmount = ScaleCurve(noise.positionAmount, amount);
        var external = p.externalForces; external.multiplier *= amount;
        var gravity = p.GetComponent<ME2_ParticleGravity>();
        if (gravity != null) gravity.Force *= amount;
    }

    private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float amount)
    {
        if (curve.mode == ParticleSystemCurveMode.Constant || curve.mode == ParticleSystemCurveMode.TwoConstants)
        { curve.constantMin *= amount; curve.constantMax *= amount; }
        else curve.curveMultiplier *= amount;
        return curve;
    }

    internal void SetEnergy(float strength)
    {
        for (int i = 0; i < Particles.Length; i++)
        {
            var emission = Particles[i].emission;
            emission.rateOverTimeMultiplier = timeRates[i] * strength;
            emission.rateOverDistanceMultiplier = distanceRates[i] * strength;
        }
    }

    internal void Play(bool clear)
    {
        if (clear) Clear();
        foreach (var p in Particles)
        {
            if (!p.gameObject.activeInHierarchy) continue;
            if (preview) { p.useAutoRandomSeed = false; p.randomSeed = 17; }
            p.Play(false);
        }
    }
    internal void PlayContinuously()
    {
        Play(true);
        // Fill only the blade-bound layers once. World-space wakes still start at the
        // weapon's actual position, while local effects no longer grow in from empty.
        foreach (var p in Particles)
        {
            if (!p.gameObject.activeInHierarchy || !p.main.loop ||
                p.main.simulationSpace != ParticleSystemSimulationSpace.Local ||
                p.emission.rateOverTimeMultiplier <= 0f) continue;
            float warmup = Mathf.Min(3f, p.main.startDelay.constantMax +
                Mathf.Min(1.5f, p.main.startLifetime.constantMax));
            p.Simulate(warmup, false, true, true);
            p.Play(false);
        }
    }
    internal void Clear()
    {
        foreach (var p in Particles) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        elapsed = 0;
        foreach (var velocity in velocities) velocity.Reset();
    }
    internal int Count { get { int n = 0; foreach (var p in Particles) n += p.particleCount; return n; } }

    internal void Advance(float dt)
    {
        if (!preview || dt <= 0) return;
        elapsed += dt;
        // Same equations and serialized parameters as ME2_Velocity/ME2_ParticleGravity/ME2_Light.
        // Only Time.deltaTime/Time.time are replaced by the editor's supplied clock.
        foreach (var clock in velocities) clock.Advance(dt);
        foreach (var clock in lights)
        {
            float duration = Mathf.Max(.0001f, clock.script.Duration);
            float t = clock.script.Loop ? elapsed % duration : Mathf.Min(elapsed, duration);
            clock.light.intensity = clock.script.IntensityOverTime.Evaluate(t / duration) * clock.intensity;
        }
        foreach (var p in Particles)
        {
            if (!p.gameObject.activeInHierarchy) continue;
            p.Simulate(dt, false, false, false);
            p.Pause(false);
        }
        foreach (var clock in gravities)
        {
            if (clock.script.Target == null) continue;
            int capacity = clock.particle.main.maxParticles;
            if (particleBuffer.Length < capacity) particleBuffer = new ParticleSystem.Particle[capacity];
            int count = clock.particle.GetParticles(particleBuffer);
            Vector3 target = clock.script.Target.position;
            for (int i = 0; i < count; i++)
            {
                Vector3 delta = target - particleBuffer[i].position;
                float force = Mathf.SmoothStep(clock.script.ForceByDistanceRemap.x,
                    clock.script.ForceByDistanceRemap.y, delta.magnitude);
                particleBuffer[i].velocity += delta.normalized * (dt * clock.script.Force * force);
            }
            clock.particle.SetParticles(particleBuffer, count);
        }
    }
    internal void Sample(float seconds)
    {
        Play(true);
        for (float remaining = Mathf.Max(0, seconds); remaining > .00001f;)
        { float dt = Mathf.Min(1f / 60f, remaining); Advance(dt); remaining -= dt; }
    }
    public void Dispose()
    {
        if (Root == null) return;
        Root.SetActive(false);
        var container = Root.transform.parent.gameObject;
        if (Application.isPlaying) UnityEngine.Object.Destroy(container);
        else UnityEngine.Object.DestroyImmediate(container);
    }
    private sealed class VelocityClock
    {
        private readonly ME2_Velocity script;
        private readonly Renderer[] renderers;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Vector3 last;
        private float value;
        internal VelocityClock(ME2_Velocity script)
        { this.script = script; renderers = script.GetComponentsInChildren<Renderer>(true); Reset(); }
        internal void Reset() { last = script.transform.position; value = 0; }
        internal void Advance(float dt)
        {
            var position = script.transform.position;
            value += 1f - Mathf.Exp(-(last - position).sqrMagnitude * script.VelocityMultiplier);
            value = Mathf.Clamp(value - script.Decay * dt, .000000001f, 1f);
            last = position;
            foreach (var renderer in renderers)
            { renderer.GetPropertyBlock(block); block.SetFloat("_Velocity", value); renderer.SetPropertyBlock(block); }
        }
    }
    private sealed class GravityClock { internal ME2_ParticleGravity script; internal ParticleSystem particle; }
    private sealed class LightClock { internal ME2_Light script; internal Light light; internal float intensity; }
}
