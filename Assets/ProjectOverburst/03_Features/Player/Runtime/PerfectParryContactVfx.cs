using UnityEngine;

// One cue clock owns the initial strike, the readable hold, and the delayed sparks.
// The pool uses custom playback/completion, so menu pause also freezes un-emitted layers.
[DisallowMultipleComponent]
public sealed class PerfectParryContactVfx : MonoBehaviour, ITransientVfxPlayback, ITransientVfxCompletion
{
    public ParticleSystem flash, stroke, glow, sparks;
    public bool additional;
    private float age, delay, brightness, readable, total, sparkAt, size;
    private bool running, emitted, sparksEmitted;
    private static readonly float[] Angles = { -72f, -48f, -25f, -8f, 17f, 39f, 61f, 79f };
    public float Age => age;
    public bool SparksEmitted => sparksEmitted;
    public bool IsPlaybackAlive => running;
    public void Prepare(PerfectParryContactProfile profile, float startDelay)
    {
        delay = Mathf.Max(0, startDelay); brightness = OverburstGameSettings.HitEffectScale;
        readable = Mathf.Clamp(profile.readableSeconds, .15f, .4f);
        total = additional ? .28f : Mathf.Max(readable + .15f, profile.totalSeconds);
        sparkAt = additional ? 0f : Mathf.Clamp(profile.sparkDelay, 0f, .2f);
        size = profile.strokeSize;
    }
    public void RestartVfx()
    {
        StopAndClearVfx(); age = 0; emitted = sparksEmitted = false; running = true;
        if (delay <= 0) EmitContact();
    }
    public void StopAndClearVfx()
    {
        running = false;
        Clear(flash); Clear(stroke); Clear(glow); Clear(sparks);
    }
    private static void Clear(ParticleSystem ps)
    { if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); }
    private void EmitContact()
    {
        emitted = true;
        if (!additional)
        {
            // Two sharp blade glints share a single nucleus; the textured metal impact remains readable.
            Emit(flash, .12f, size * 1.18f, 24f * Mathf.Deg2Rad, new Color(1f, .97f, .84f, brightness), Vector3.zero);
            Emit(flash, .10f, size * .72f, 112f * Mathf.Deg2Rad, new Color(1f, .91f, .69f, brightness * .8f), Vector3.zero);
            Emit(stroke, readable + .1f, size, -18f * Mathf.Deg2Rad,
                new Color(1f, .88f, .58f, brightness), Vector3.zero);
            Emit(glow, readable + .1f, .48f, 0f, new Color(1f, .65f, .26f, brightness * .22f), Vector3.zero);
        }
    }
    private static void Emit(ParticleSystem ps, float lifetime, float scale, float rotation, Color color, Vector3 velocity)
    {
        if (ps == null) return;
        var p = new ParticleSystem.EmitParams { startLifetime = lifetime, startSize = scale,
            rotation = rotation * Mathf.Rad2Deg, startColor = color, velocity = velocity, position = ps.transform.position };
        ps.Emit(p, 1);
    }
    private void EmitSparks()
    {
        sparksEmitted = true;
        int count = additional ? 3 : 8;
        var camera = QuarterViewCamera.ActiveInstance != null ? QuarterViewCamera.ActiveInstance.GetComponent<Camera>() : Camera.main;
        Vector3 normal = camera != null ? camera.transform.forward : Vector3.forward;
        Vector3 fan = Vector3.ProjectOnPlane(transform.forward + Vector3.up * .30f, normal);
        if (fan.sqrMagnitude < .01f) fan = camera != null ? camera.transform.right : Vector3.right;
        fan.Normalize();
        for (int i = 0; i < count; i++)
        {
            float angle = Angles[additional ? i * 2 : i];
            Vector3 velocity = Quaternion.AngleAxis(angle, normal) * fan * (i < 3 ? 2.8f : 1.9f);
            float life = additional ? .23f : total - sparkAt - (i % 3) * .065f;
            Emit(sparks, life, additional ? .055f : i < 3 ? .10f : .052f,
                0f, new Color(1f, .84f, .48f, brightness * (additional ? .55f : 1f)), velocity);
        }
    }
    private void Update()
    {
        if (!running || Time.timeScale <= 0f || GameplayInputBlocker.IsGameplayInputBlocked) return;
        float dt = Time.unscaledDeltaTime;
        age += dt;
        if (!emitted && age >= delay) EmitContact();
        if (!sparksEmitted && age >= delay + sparkAt) EmitSparks();
        Simulate(flash, dt); Simulate(stroke, dt); Simulate(glow, dt); Simulate(sparks, dt);
        if (age >= delay + total) StopAndClearVfx();
    }
    // Keep Unity's automatic simulation stopped; advance particles exactly once on the cue clock.
    private static void Simulate(ParticleSystem ps, float dt)
    { if (ps != null) { ps.Simulate(dt, false, false, false); ps.Pause(false); } }
    private void OnDisable() { StopAndClearVfx(); }
}
