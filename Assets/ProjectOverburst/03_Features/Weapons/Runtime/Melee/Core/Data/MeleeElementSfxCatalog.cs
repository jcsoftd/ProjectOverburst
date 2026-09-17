using System;
using UnityEngine;

public enum MeleeElementSfxCueType
{
    Slash,
    Hit
}

[Serializable]
public sealed class MeleeElementSfxCueSettings
{
    public AudioClip[] clips = Array.Empty<AudioClip>();
    [Range(0f, 2f)] public float volume = 1f;
    [Range(0.1f, 3f)] public float minPitch = 1f;
    [Range(0.1f, 3f)] public float maxPitch = 1f;
    public bool spatial = true;
    [Min(0.1f)] public float minDistance = 2f;
    [Min(0.2f)] public float maxDistance = 40f;
    [Min(0f)] public float cooldown = 0.03f;

    public bool TryPickClip(out AudioClip clip)
    {
        clip = null;
        if (clips == null || clips.Length == 0)
            return false;

        int start = UnityEngine.Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip candidate = clips[(start + i) % clips.Length];
            if (candidate == null)
                continue;

            clip = candidate;
            return true;
        }

        return false;
    }

    public float ResolvePitch()
    {
        float low = Mathf.Clamp(Mathf.Min(minPitch, maxPitch), 0.1f, 3f);
        float high = Mathf.Clamp(Mathf.Max(minPitch, maxPitch), low, 3f);
        return Mathf.Approximately(low, high) ? low : UnityEngine.Random.Range(low, high);
    }
}

[Serializable]
public sealed class MeleeElementSfxEntry
{
    public WeaponElement element;
    public MeleeElementSfxCueSettings slash = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings hit = new MeleeElementSfxCueSettings();

    public MeleeElementSfxCueSettings GetSettings(MeleeElementSfxCueType cueType)
    {
        return cueType == MeleeElementSfxCueType.Slash ? slash : hit;
    }
}

[CreateAssetMenu(
    fileName = "MeleeElementSfxCatalog",
    menuName = "OVERBURST/Weapons/Melee Element SFX Catalog")]
public sealed class MeleeElementSfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/SFX/MeleeElementSfxCatalog";

    public MeleeElementSfxEntry[] entries = Array.Empty<MeleeElementSfxEntry>();

    public bool TryResolve(
        WeaponElement element,
        MeleeElementSfxCueType cueType,
        out MeleeElementSfxCueSettings settings)
    {
        settings = null;
        if (entries == null)
            return false;

        for (int i = 0; i < entries.Length; i++)
        {
            MeleeElementSfxEntry entry = entries[i];
            if (entry == null || entry.element != element)
                continue;

            settings = entry.GetSettings(cueType);
            return settings != null && settings.clips != null && settings.clips.Length > 0;
        }

        return false;
    }
}
