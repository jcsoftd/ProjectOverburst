using System;
using UnityEngine;

public enum ElementalReactionSfxCueType
{
    Vaporize,
    ThermalFracture,
    PlasmaExplosion,
    Freeze,
    Shatter,
    ChainTransition,
    ColdChargeExplosion
}

[Serializable]
public sealed class ElementalReactionSfxCueSettings
{
    [InspectorName("후보 AudioClip")]
    public AudioClip[] clips = Array.Empty<AudioClip>();
    [Range(0f, 2f), InspectorName("볼륨")]
    public float volume = 1f;
    [Range(0.1f, 3f), InspectorName("최소 피치")]
    public float minPitch = 1f;
    [Range(0.1f, 3f), InspectorName("최대 피치")]
    public float maxPitch = 1f;
    [InspectorName("3D 공간음")]
    public bool spatial = true;
    [Min(0.1f), InspectorName("최소 거리")]
    public float minDistance = 2f;
    [Min(0.2f), InspectorName("최대 거리")]
    public float maxDistance = 35f;
    [Min(0f), InspectorName("재생 쿨다운")]
    public float cooldown = 0.03f;

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

[CreateAssetMenu(
    fileName = "ElementalReactionSfxCatalog",
    menuName = "OVERBURST/Combat/Elemental Reaction SFX Catalog")]
public sealed class ElementalReactionSfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/SFX/ElementalReactionSfxCatalog";

    [Header("즉시 발생 반응")]
    [InspectorName("증기")]
    public ElementalReactionSfxCueSettings vaporize = new ElementalReactionSfxCueSettings();
    [InspectorName("균열 (선정 대기)")]
    public ElementalReactionSfxCueSettings thermalFracture = new ElementalReactionSfxCueSettings();
    [InspectorName("빙결 발생")]
    public ElementalReactionSfxCueSettings freeze = new ElementalReactionSfxCueSettings();

    [Header("후속 폭발")]
    [InspectorName("플라즈마 폭발")]
    public ElementalReactionSfxCueSettings plasmaExplosion = new ElementalReactionSfxCueSettings();
    [InspectorName("쇄빙")]
    public ElementalReactionSfxCueSettings shatter = new ElementalReactionSfxCueSettings();
    [InspectorName("냉전하 폭발")]
    public ElementalReactionSfxCueSettings coldChargeExplosion = new ElementalReactionSfxCueSettings();

    [Header("연쇄감전")]
    [InspectorName("전이 후보 (2·3 랜덤)")]
    public ElementalReactionSfxCueSettings chainTransition = new ElementalReactionSfxCueSettings();

    public bool TryResolve(
        ElementalReactionSfxCueType cueType,
        out ElementalReactionSfxCueSettings settings)
    {
        switch (cueType)
        {
            case ElementalReactionSfxCueType.Vaporize:
                settings = vaporize;
                break;
            case ElementalReactionSfxCueType.ThermalFracture:
                settings = thermalFracture;
                break;
            case ElementalReactionSfxCueType.PlasmaExplosion:
                settings = plasmaExplosion;
                break;
            case ElementalReactionSfxCueType.Freeze:
                settings = freeze;
                break;
            case ElementalReactionSfxCueType.Shatter:
                settings = shatter;
                break;
            case ElementalReactionSfxCueType.ChainTransition:
                settings = chainTransition;
                break;
            case ElementalReactionSfxCueType.ColdChargeExplosion:
                settings = coldChargeExplosion;
                break;
            default:
                settings = null;
                break;
        }

        return settings != null && settings.clips != null && settings.clips.Length > 0;
    }
}
