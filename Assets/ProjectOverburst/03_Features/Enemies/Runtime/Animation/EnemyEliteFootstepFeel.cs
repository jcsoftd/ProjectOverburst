using System;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Three reusable Feel slots for walking elites, separate from corpse impact slots.</summary>
public sealed class EnemyEliteFootstepFeel : MonoBehaviour
{
    [Serializable]
    public sealed class Slot
    {
        public MMF_Player player;
        public ParticleSystem dust;
        public AudioSource audio;
        [NonSerialized] public float availableAt;
    }

    [SerializeField] private Slot[] slots = Array.Empty<Slot>();
    private static EnemyEliteFootstepFeel instance;

    public int PlayedCount { get; private set; }
    public int DroppedCount { get; private set; }
    public int Capacity => slots != null ? slots.Length : 0;
    public void Configure(Slot[] value) => slots = value ?? Array.Empty<Slot>();

    public static bool Play(Vector3 point, float distance)
    {
        if (!Application.isPlaying || distance > 12f) return false;
        if (instance == null)
        {
            var prefab = Resources.Load<EnemyEliteFootstepFeel>("Feel/PF_EnemyEliteFootstepFeel");
            if (prefab == null) return false;
            instance = Instantiate(prefab);
        }
        return instance.PlayInternal(point, distance);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        // Dust is emitted by the three shared ParticleSystems. Keep these Feel slots for audio.
        foreach (Slot slot in slots)
        {
            if (slot?.player?.FeedbacksList == null) continue;
            foreach (MMF_Feedback feedback in slot.player.FeedbacksList)
                if (feedback is MMF_Particles) feedback.Active = false;
        }
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private bool PlayInternal(Vector3 point, float distance)
    {
        Slot selected = null;
        float now = Time.unscaledTime;
        foreach (Slot slot in slots)
        {
            if (slot?.player == null || slot.dust == null || slot.availableAt > now) continue;
            selected = slot;
            break;
        }
        if (selected == null) { DroppedCount++; return false; }

        selected.player.StopFeedbacks();
        selected.dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (selected.audio != null) selected.audio.Stop();
        selected.player.transform.SetPositionAndRotation(point, Quaternion.identity);
        float nearness = 1f - Mathf.Clamp01(distance / 12f);
        selected.player.Initialization(true);
        selected.player.PlayFeedbacks(point, Mathf.Lerp(0.45f, 1f, nearness));
        selected.availableAt = now + 0.3f;
        PlayedCount++;
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
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this) instance = null;
    }
}
