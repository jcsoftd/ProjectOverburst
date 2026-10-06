using System;
using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Crustaspikan Combat Presentation")]
public sealed class CrustaspikanCombatPresentationProfile : ScriptableObject
{
    [Serializable]
    public sealed class GroundStrike
    {
        public string materialId;
        public int phase;
        public string[] contactBones = Array.Empty<string>();
        [Range(.5f, 3f)] public float strength = 1.5f;
    }
    public AudioClip[] footsteps = Array.Empty<AudioClip>();
    public AudioClip groundImpact;
    public AudioClip rumble;
    public GameObject dustPrefab;
    [Range(0f, 1f)] public float footstepVolume = .52f;
    [Range(.3f, 1.2f)] public float footstepPitch = .62f;
    [Range(0f, 1f)] public float impactVolume = .62f;
    [Range(0f, .5f)] public float footRumbleVolume = .12f;
    [Range(0f, .5f)] public float impactRumbleVolume = .25f;
    [Range(.2f, 2f)] public float rumbleSeconds = .8f;
    [Min(1f)] public float audibleDistance = 18f;
    [Range(0f, .05f)] public float footCameraAmplitude = .026f;
    [Range(0f, .05f)] public float strikeCameraAmplitude = .045f;
    public GroundStrike[] groundStrikes = Array.Empty<GroundStrike>();

    public GroundStrike Find(EnemyBossAttackMaterial material, int phase)
    {
        if (material == null) return null;
        for (int i = 0; i < groundStrikes.Length; i++)
            if (groundStrikes[i] != null && groundStrikes[i].materialId == material.materialId && groundStrikes[i].phase == phase)
                return groundStrikes[i];
        return null;
    }
}
