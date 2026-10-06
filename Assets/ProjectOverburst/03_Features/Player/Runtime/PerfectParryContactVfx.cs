using UnityEngine;

// One cue clock owns the small strike glints and two readable metal-fragment batches.
// The pool uses custom playback/completion, so menu pause also freezes un-emitted layers.
[DisallowMultipleComponent]
public sealed class PerfectParryContactVfx : MonoBehaviour, ITransientVfxPlayback, ITransientVfxCompletion
{
    public ParticleSystem flash, stroke, glow, sparks;
    public bool additional;
    private float age, delay, brightness, readable, total, sparkAt, size;
    private bool running, emitted, sparksEmitted;
    private static readonly float[] ContactAngles = { -50f, -24f, -5f, 16f, 36f, 58f, 155f };
    private static readonly float[] ContactSpeeds = { 4.3f, 2.7f, 3.6f, 4.8f, 3.1f, 2.2f, 1.6f };
    private static readonly float[] ContactWidths = { .20f, .25f, .17f, .15f, .19f, .16f, .13f };
    private static readonly float[] ContactLengths = { .62f, .46f, .70f, .48f, .58f, .36f, .26f };
    private static readonly float[] TailAngles = { -64f, -37f, -13f, 26f, 68f };
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
            // A small angular glint marks the contact; moving fragments carry the readable tail.
            EmitGlint(.095f, size * .65f, .20f, 24f, new Color(1f, .97f, .87f, brightness));
            EmitGlint(.075f, size * .38f, .15f, 111f, new Color(1f, .93f, .76f, brightness * .8f));
            EmitFragments(stroke, false);
            // Keep the serialized halo slot for existing prefabs, without emitting a round light mass.
        }
    }
    private void EmitGlint(float lifetime, float length, float thickness, float degrees, Color color)
    {
        if (flash == null) return;
        var particle = new ParticleSystem.EmitParams { startLifetime = lifetime,
            startSize3D = new Vector3(length, length * thickness, 1f), rotation = degrees,
            startColor = color, velocity = Vector3.zero, position = flash.transform.position };
        flash.Emit(particle, 1);
    }
    private void EmitSparks()
    {
        sparksEmitted = true;
        EmitFragments(sparks, true);
    }
    private void EmitFragments(ParticleSystem ps, bool tail)
    {
        int count = additional ? 3 : tail ? 5 : 7;
        var camera = QuarterViewCamera.ActiveInstance != null ? QuarterViewCamera.ActiveInstance.GetComponent<Camera>() : Camera.main;
        Vector3 normal = camera != null ? camera.transform.forward : Vector3.forward;
        Vector3 fan = Vector3.ProjectOnPlane(transform.forward + Vector3.up * .18f, normal);
        if (fan.sqrMagnitude < .01f) fan = camera != null ? camera.transform.right : Vector3.right;
        fan.Normalize();
        for (int i = 0; i < count; i++)
        {
            float angle = tail ? TailAngles[additional ? i * 2 : i] : ContactAngles[i];
            float speed = additional ? 1.9f : tail ? 1.7f + (i % 3) * .45f : ContactSpeeds[i];
            Vector3 velocity = Quaternion.AngleAxis(angle, normal) * fan * speed;
            float life = additional ? .23f : tail ? Mathf.Min(total - sparkAt, .35f + i * .04f)
                : Mathf.Min(total, readable + .22f + (i % 3) * .05f);
            float width = additional ? .12f : tail ? .14f + (i % 2) * .025f : ContactWidths[i];
            float length = additional ? .26f : tail ? .30f + (i % 3) * .07f : ContactLengths[i];
            Color color = tail ? new Color(1f, .88f, .61f, brightness * (additional ? .55f : .9f))
                : new Color(1f, .95f, .80f, brightness);
            if (ps == null) continue;
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            Vector3 up = camera != null ? camera.transform.up : Vector3.up;
            float degrees = -Mathf.Atan2(Vector3.Dot(velocity, up), Vector3.Dot(velocity, right)) * Mathf.Rad2Deg;
            var particle = new ParticleSystem.EmitParams { startLifetime = life,
                startSize3D = new Vector3(length, width, 1f), rotation = degrees,
                startColor = color, velocity = velocity, position = ps.transform.position };
            ps.Emit(particle, 1);
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
