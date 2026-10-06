using System.Collections.Generic;
using UnityEngine;

// The actor's measured foot contacts and the executor's actual strike releases drive presentation only.
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyActor), typeof(EnemyEliteFootstepEmitter))]
public sealed class CrustaspikanCombatPresentation : MonoBehaviour
{
    private const int SlotCount = 4;
    [SerializeField] private CrustaspikanCombatPresentationProfile profile;
    private EnemyActor actor;
    private EnemyEliteFootstepEmitter footsteps;
    private EnemyBossMaterialExecutor executor;
    private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private readonly Slot[] slots = new Slot[SlotCount];
    private int cursor, soundCursor;
    private uint lease;
    private bool subscribed;
    private sealed class Slot
    { public GameObject root; public ParticleSystem dust; public AudioSource attack, tail; public float tailStarted; }

    public int FootContactCount { get; private set; }
    public int GroundStrikeCount { get; private set; }
    public int DustBurstCount { get; private set; }
    public int AudioEmissionCount { get; private set; }
    public Vector3 LastGroundContact { get; private set; }
    public CrustaspikanCombatPresentationProfile Profile => profile;
    public int SlotCapacity => SlotCount;

    public void Configure(CrustaspikanCombatPresentationProfile settings) => profile = settings;
    private void Awake() => Resolve();
    private void Resolve()
    {
        if (actor == null) actor = GetComponent<EnemyActor>();
        if (footsteps == null) footsteps = GetComponent<EnemyEliteFootstepEmitter>();
        if (executor == null) executor = GetComponent<EnemyBossMaterialExecutor>();
        if (bones.Count == 0 && actor != null && actor.Animator != null)
            foreach (var bone in actor.Animator.GetComponentsInChildren<Transform>(true)) bones[bone.name] = bone;
    }
    private void OnEnable()
    {
        Resolve();
        if (subscribed) return;
        if (footsteps != null) footsteps.GroundContact += Foot;
        if (executor != null) executor.StrikeReleased += Strike;
        subscribed = true;
    }
    private void OnDisable()
    {
        if (footsteps != null) footsteps.GroundContact -= Foot;
        if (executor != null) executor.StrikeReleased -= Strike;
        subscribed = false; StopSlots();
    }
    private void Update()
    {
        if (actor != null && lease != actor.LeaseVersion) { StopSlots(); lease = actor.LeaseVersion; }
        if (actor == null || !actor.IsLeased || actor.Health == null || actor.Health.IsDead) { StopSlots(); return; }
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || !slot.tail.isPlaying) continue;
            float elapsed = Time.unscaledTime - slot.tailStarted;
            if (elapsed >= profile.rumbleSeconds) slot.tail.Stop();
            else slot.tail.volume *= Mathf.Exp(-4f * Time.unscaledDeltaTime / Mathf.Max(.1f, profile.rumbleSeconds));
        }
    }
    private bool Ready => profile != null && actor != null && actor.IsLeased && actor.Health != null && !actor.Health.IsDead;
    private void Foot(Vector3 point, float distance, Collider ground)
    {
        if (!Ready) return;
        FootContactCount++;
        Play(point, distance, 1f, ground, true);
    }
    private void Strike(EnemyBossAttackMaterial material, int phase)
    {
        if (!Ready || material.delivery != EnemyBossMaterialDelivery.Melee) return;
        var effect = profile.Find(material, phase);
        if (effect == null) return; // Side swipes never produce a fake ground slam.
        int count = Mathf.Max(1, effect.contactBones.Length);
        float distance = PlayerDistance();
        bool audio = true;
        for (int i = 0; i < count; i++)
        {
            Vector3 contact = material.strikes[phase].Origin(transform);
            if (i < effect.contactBones.Length && bones.TryGetValue(effect.contactBones[i], out var bone) && bone != null)
                contact = bone.position;
            if (!TryGround(contact, out var ground)) continue;
            GroundStrikeCount++;
            Play(ground.point, distance, effect.strength, ground.collider, false, audio);
            audio = false; // Two hands: two dust contacts, one impact/rumble/camera request.
        }
    }
    private float PlayerDistance()
    {
        var camera = QuarterViewCamera.ActiveInstance;
        if (camera == null || camera.CurrentTarget == null) return float.PositiveInfinity;
        var delta = camera.CurrentTarget.position - transform.position; delta.y = 0f; return delta.magnitude;
    }
    private bool TryGround(Vector3 contact, out RaycastHit ground)
    {
        ground = default;
        int count = Physics.RaycastNonAlloc(contact + Vector3.up * 2f, Vector3.down, hits, 6f,
            LayerMask.GetMask("Default", "Ground", "Environment"), QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<EnemyActor>() != null
                || hit.collider.GetComponentInParent<PlayerActorRuntime>() != null || hit.distance >= closest) continue;
            ground = hit; closest = hit.distance;
        }
        return !float.IsPositiveInfinity(closest) && Mathf.Abs(ground.point.y - transform.position.y) < 1.5f;
    }
    private void Play(Vector3 point, float distance, float strength, Collider ground, bool walking, bool withAudio = true)
    {
        if (distance >= Mathf.Max(profile.audibleDistance, 22f)) return;
        var slot = Acquire(); if (slot == null) return;
        slot.root.transform.position = point + Vector3.up * .035f;
        LastGroundContact = point;
        var surface = ground != null ? ground.GetComponentInParent<SurfaceOverride>() : null;
        string surfaceId = surface != null && surface.Profile != null ? surface.Profile.SurfaceId : ground != null ? ground.sharedMaterial?.name : null;
        bool water = !string.IsNullOrEmpty(surfaceId) && surfaceId.IndexOf("Water", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (!water && slot.dust != null && distance < 22f)
        {
            slot.dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = slot.dust.main;
            main.startSizeMultiplier = walking ? 1.55f : 1.8f + strength * .65f;
            main.startSpeedMultiplier = walking ? .75f : 1f + strength * .25f;
            main.startLifetimeMultiplier = walking ? .7f : .85f;
            slot.dust.Play(true); DustBurstCount++;
        }
        if (!withAudio || distance >= profile.audibleDistance) return;
        float attenuation = Mathf.SmoothStep(0f, 1f, 1f - distance / profile.audibleDistance);
        AudioClip clip = walking && profile.footsteps.Length > 0 ? profile.footsteps[soundCursor++ % profile.footsteps.Length] : profile.groundImpact;
        if (clip != null)
        {
            slot.attack.clip = clip; slot.attack.volume = attenuation * (walking ? profile.footstepVolume : profile.impactVolume);
            slot.attack.pitch = walking ? profile.footstepPitch + ((soundCursor & 1) == 0 ? -.025f : .025f) : .82f;
            slot.attack.Play(); AudioEmissionCount++;
        }
        if (profile.rumble != null)
        {
            slot.tail.clip = profile.rumble; slot.tail.pitch = 1f;
            slot.tail.volume = attenuation * (walking ? profile.footRumbleVolume : profile.impactRumbleVolume);
            slot.tailStarted = Time.unscaledTime; slot.tail.Play(); AudioEmissionCount++;
        }
        if (distance < 11f)
        {
            float nearby = Mathf.SmoothStep(0f, 1f, 1f - distance / 11f);
            QuarterViewCamera.ActiveInstance?.QueueGroundStep(nearby * (walking ? profile.footCameraAmplitude : profile.strikeCameraAmplitude), walking ? .12f : .16f);
        }
    }
    private Slot Acquire()
    {
        int index = cursor++ % SlotCount;
        var slot = slots[index];
        if (slot == null)
        {
            var root = new GameObject("Crustaspikan ground contact " + index); root.transform.SetParent(transform, false);
            slot = new Slot { root = root, attack = root.AddComponent<AudioSource>(), tail = root.AddComponent<AudioSource>() };
            ConfigureAudio(slot.attack, 90); ConfigureAudio(slot.tail, 140);
            if (profile.dustPrefab != null)
            {
                var dust = Instantiate(profile.dustPrefab, root.transform);
                dust.transform.localPosition = Vector3.zero; dust.transform.localScale = Vector3.one;
                slot.dust = dust.GetComponent<ParticleSystem>();
            }
            slots[index] = slot;
        }
        slot.attack.Stop(); slot.tail.Stop();
        return slot;
    }
    private static void ConfigureAudio(AudioSource audio, int priority)
    {
        audio.playOnAwake = false; audio.loop = false; audio.spatialBlend = 0f; audio.dopplerLevel = 0f;
        audio.priority = priority; // Player-relative attenuation is applied once, independent of camera zoom.
    }
    private void StopSlots()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i]; if (slot == null) continue;
            slot.attack.Stop(); slot.tail.Stop(); slot.dust?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
