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
    public int PreemptedCount { get; private set; }
    public int Capacity => slots.Length;
    public void Configure(Slot[] value) => slots = value;

    public static bool Play(CombatImpactSurface surface, CombatImpactShape shape, Vector3 point,
        Vector3 direction, bool critical = false, float intensity = 1f, bool lethal = false)
    {
        if (!Application.isPlaying) return false;
        if (instance == null)
        {
            var prefab = Resources.Load<CombatImpactFeel>("Feel/PF_CombatImpactFeel");
            if (prefab == null) return false;
            instance = Instantiate(prefab);
        }
        return instance.PlayInternal(surface, shape, point, direction, critical, intensity, lethal);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private bool PlayInternal(CombatImpactSurface surface, CombatImpactShape shape, Vector3 point,
        Vector3 direction, bool critical, float intensity, bool lethal)
    {
        // Simultaneous heavy corpses share one audible landing accent.
        if (surface == CombatImpactSurface.Ground && Time.unscaledTime < nextLandingAt) return false;
        Slot selected = null;
        foreach (var slot in slots)
        {
            if (slot.surface != surface || slot.player == null || slot.particles == null) continue;
            if (slot.availableAt <= Time.unscaledTime) { selected = slot; break; }
            // A kill must remain readable even if every ordinary contact slot is busy.
            if (lethal && (selected == null || slot.availableAt < selected.availableAt)) selected = slot;
        }
        if (selected == null)
        {
            DroppedCount++;
            return false;
        }
        if (selected.availableAt > Time.unscaledTime) PreemptedCount++;
        var selectedSlot = selected;
        selectedSlot.player.StopFeedbacks();
        selectedSlot.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (selectedSlot.audio != null) selectedSlot.audio.Stop();
        direction.y = 0;
        if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
        Vector3 emissionDirection = surface == CombatImpactSurface.Ground ? Vector3.up
            : shape == CombatImpactShape.Downward ? (direction.normalized + Vector3.down * .7f).normalized
            : direction.normalized;
        selectedSlot.player.transform.SetPositionAndRotation(point, Quaternion.LookRotation(emissionDirection));
        var particleShape = selectedSlot.particles.shape;
        particleShape.angle = surface == CombatImpactSurface.Ground ? 78 : shape == CombatImpactShape.Thrust ? 12 : 34;
        var main = selectedSlot.particles.main;
        main.startSizeMultiplier = surface == CombatImpactSurface.Ground ? .22f : lethal ? .10f : critical ? .085f : .055f;
        if (selectedSlot.criticalFlash != null)
        {
            var flash = selectedSlot.criticalFlash.main;
            flash.startRotation = shape == CombatImpactShape.Downward ? Mathf.PI * .5f : 0;
            foreach (var feedback in selectedSlot.player.FeedbacksList)
                if (feedback is MMF_Particles effect && effect.BoundParticleSystem == selectedSlot.criticalFlash)
                    effect.Active = critical || lethal;
        }
        selectedSlot.player.Initialization(true);
        selectedSlot.player.PlayFeedbacks(point, Mathf.Clamp(lethal ? intensity * 1.12f : intensity, .5f, 1.3f));
        selectedSlot.availableAt = Time.unscaledTime + (surface == CombatImpactSurface.Ground ? .7f : .3f);
        if (surface == CombatImpactSurface.Ground) nextLandingAt = Time.unscaledTime + .18f;
        PlayedCount++;
        return true;
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
