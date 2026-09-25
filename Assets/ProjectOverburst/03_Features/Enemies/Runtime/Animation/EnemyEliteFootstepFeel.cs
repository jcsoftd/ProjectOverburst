using System;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Three reusable audio Feel slots for heavy walking contacts, separate from corpse impacts.</summary>
public sealed class EnemyEliteFootstepFeel : MonoBehaviour
{
    [Serializable]
    public sealed class Slot
    {
        public MMF_Player player;
        public ParticleSystem dust;
        public AudioSource audio;
        [NonSerialized] public float availableAt;
        [NonSerialized] public EnemyGroundStepTier currentTier;
        [NonSerialized] public MMF_AudioSource audioFeedback;
    }

    [SerializeField] private Slot[] slots = Array.Empty<Slot>();
    private static EnemyEliteFootstepFeel instance;

    public int PlayedCount { get; private set; }
    public int DroppedCount { get; private set; }
    public int PreemptedMediumCount { get; private set; }
    public int ElitePlayedCount { get; private set; }
    public int MediumPlayedCount { get; private set; }
    public int Capacity => slots != null ? slots.Length : 0;
    public void Configure(Slot[] value) => slots = value ?? Array.Empty<Slot>();

    public static bool Play(Vector3 point, float distance)
        => Play(point, distance, EnemyGroundStepTier.Elite);

    public static bool Play(Vector3 point, float distance, EnemyGroundStepTier tier)
    {
        if (!Application.isPlaying || tier == EnemyGroundStepTier.None
            || distance >= EnemyGroundStepTuning.AudibleDistance(tier)) return false;
        if (instance == null)
        {
            var prefab = Resources.Load<EnemyEliteFootstepFeel>("Feel/PF_EnemyEliteFootstepFeel");
            if (prefab == null) return false;
            instance = Instantiate(prefab);
        }
        return instance.PlayInternal(point, distance, tier);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        // Dust is emitted by the shared world systems. These slots provide player-relative audio.
        foreach (Slot slot in slots)
        {
            if (slot?.player?.FeedbacksList == null) continue;
            foreach (MMF_Feedback feedback in slot.player.FeedbacksList)
            {
                if (feedback is MMF_Particles) feedback.Active = false;
                if (feedback is MMF_AudioSource audioFeedback) slot.audioFeedback = audioFeedback;
            }
            if (slot.audio == null) continue;
            slot.audio.spatialBlend = 0f;
            slot.audio.dopplerLevel = 0f;
        }
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private bool PlayInternal(Vector3 point, float distance, EnemyGroundStepTier tier)
    {
        Slot selected = null;
        Slot weaker = null;
        float now = Time.unscaledTime;
        foreach (Slot slot in slots)
        {
            if (slot?.player == null || slot.audio == null) continue;
            if (slot.availableAt <= now) { selected = slot; break; }
            if (tier == EnemyGroundStepTier.Elite
                && slot.currentTier == EnemyGroundStepTier.Medium
                && (weaker == null || slot.availableAt < weaker.availableAt)) weaker = slot;
        }
        if (selected == null && weaker != null)
        {
            selected = weaker;
            PreemptedMediumCount++;
        }
        if (selected == null) { DroppedCount++; return false; }

        selected.player.StopFeedbacks();
        if (selected.dust != null)
            selected.dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (selected.audio != null) selected.audio.Stop();
        selected.player.transform.SetPositionAndRotation(point, Quaternion.identity);
        float volume = EnemyGroundStepTuning.AudioVolume(tier, distance);
        if (selected.audioFeedback != null)
        {
            selected.audioFeedback.MinVolume = volume * .88f;
            selected.audioFeedback.MaxVolume = volume;
        }
        selected.player.Initialization(true);
        selected.player.PlayFeedbacks(point, 1f);
        selected.availableAt = now + 0.3f;
        selected.currentTier = tier;
        PlayedCount++;
        if (tier == EnemyGroundStepTier.Elite) ElitePlayedCount++;
        else MediumPlayedCount++;
        return true;
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) => StopAll();
    private void OnDisable() => StopAll();
    private void StopAll()
    {
        foreach (Slot slot in slots)
        {
            if (slot == null) continue;
            slot.player?.StopFeedbacks();
            if (slot.dust != null) slot.dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (slot.audio != null) slot.audio.Stop();
            slot.availableAt = 0f;
            slot.currentTier = EnemyGroundStepTier.None;
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this) instance = null;
    }
}
