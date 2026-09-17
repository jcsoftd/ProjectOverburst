using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OverburstFeelEmitter : MonoBehaviour
{
    [SerializeField] private OverburstFeelCue cue;
    [SerializeField] private MMF_Player player;
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private Image uiImage;
    private bool initialized;

    public OverburstFeelCue Cue => cue;
    public MMF_Player Player => player;
    public ParticleSystem ParticleSystem => particleSystem;
    public Image UiImage => uiImage;

    public void Configure(
        OverburstFeelCue configuredCue,
        MMF_Player configuredPlayer,
        ParticleSystem configuredParticleSystem,
        Image configuredUiImage)
    {
        cue = configuredCue;
        player = configuredPlayer;
        particleSystem = configuredParticleSystem;
        uiImage = configuredUiImage;
    }

    public void Play(Vector3 worldPosition, float intensity)
    {
        if (player == null)
            return;

        StopAndReset();
        player.Initialization(true);
        initialized = true;
        player.PlayFeedbacks(worldPosition, Mathf.Max(0.01f, intensity));
    }

    public void StopAndReset()
    {
        if (player != null && initialized)
        {
            player.StopFeedbacks();
            player.RestoreInitialValues();
            player.ResetFeedbacks();
        }

        if (particleSystem != null)
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (uiImage != null)
            uiImage.color = Color.clear;
        initialized = false;
    }
}
