using System;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum CombatImpactSurface { Flesh, Shell, Venom, Ground }
public enum CombatImpactShape { Sweep, Downward, Thrust }

/// <summary>Bounded Feel players for local contact details and corpse landing, never damage or time ownership.</summary>
public sealed class CombatImpactFeel : MonoBehaviour
{
    [Serializable]
    public sealed class Slot
    {
        public CombatImpactSurface surface;
        public MMF_Player player;
        public ParticleSystem particles;
        public ParticleSystem criticalFlash;
        public AudioSource audio;
        [NonSerialized] public float availableAt;
    }

    [SerializeField] private Slot[] slots = Array.Empty<Slot>();
    private static CombatImpactFeel instance;
    private float nextLandingAt;
    public int PlayedCount { get; private set; }
    public int DroppedCount { get; private set; }
    public int Capacity => slots.Length;
    public void Configure(Slot[] value) => slots = value;

    public static bool Play(CombatImpactSurface surface, CombatImpactShape shape, Vector3 point,
        Vector3 direction, bool critical = false, float intensity = 1f)
    {
        if (!Application.isPlaying) return false;
        if (instance == null)
        {
            var prefab = Resources.Load<CombatImpactFeel>("Feel/PF_CombatImpactFeel");
            if (prefab == null) return false;
            instance = Instantiate(prefab);
        }
        return instance.PlayInternal(surface, shape, point, direction, critical, intensity);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private bool PlayInternal(CombatImpactSurface surface, CombatImpactShape shape, Vector3 point,
        Vector3 direction, bool critical, float intensity)
    {
        // Simultaneous heavy corpses share one audible landing accent.
        if (surface == CombatImpactSurface.Ground && Time.unscaledTime < nextLandingAt) return false;
        foreach (var slot in slots)
        {
            if (slot.surface != surface || slot.availableAt > Time.unscaledTime) continue;
            slot.player.StopFeedbacks();
            slot.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (slot.audio != null) slot.audio.Stop();
            direction.y = 0;
            if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
            Vector3 emissionDirection = surface == CombatImpactSurface.Ground ? Vector3.up
                : shape == CombatImpactShape.Downward ? (direction.normalized + Vector3.down * .7f).normalized
                : direction.normalized;
            slot.player.transform.SetPositionAndRotation(point, Quaternion.LookRotation(emissionDirection));
            var particleShape = slot.particles.shape;
            particleShape.angle = surface == CombatImpactSurface.Ground ? 78 : shape == CombatImpactShape.Thrust ? 12 : 34;
            var main = slot.particles.main;
            main.startSizeMultiplier = surface == CombatImpactSurface.Ground ? .22f : critical ? .085f : .055f;
            if (slot.criticalFlash != null)
            {
                var flash = slot.criticalFlash.main;
                flash.startRotation = shape == CombatImpactShape.Downward ? Mathf.PI * .5f : 0;
                foreach (var feedback in slot.player.FeedbacksList)
                    if (feedback is MMF_Particles effect && effect.BoundParticleSystem == slot.criticalFlash)
                        effect.Active = critical;
            }
            slot.player.Initialization(true);
            slot.player.PlayFeedbacks(point, Mathf.Clamp(intensity, .5f, 1.3f));
            slot.availableAt = Time.unscaledTime + (surface == CombatImpactSurface.Ground ? .7f : .3f);
            if (surface == CombatImpactSurface.Ground) nextLandingAt = Time.unscaledTime + .18f;
            PlayedCount++;
            return true;
        }
        DroppedCount++; // Keep active particles intact instead of restarting a visible slot.
        return false;
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) => StopAll();
    private void StopAll()
    {
        foreach (var slot in slots)
        {
            slot.player.StopFeedbacks();
            slot.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (slot.audio != null) slot.audio.Stop();
            slot.availableAt = 0;
        }
        nextLandingAt = 0;
    }
    private void OnDisable() => StopAll();
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this) instance = null;
    }
}
