using System;
using UnityEngine;

public enum MeleeElementSfxCueType
{
    Slash,
    Hit,
    HeavyImpact,
    CriticalHit, // 비어 있으면 Hit로 대체
    FollowUp // 강공 방출 후속: 번개 홉·화염 전파·얼음 쇄빙(대상별)
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
    [Tooltip("같은 속성·같은 큐의 동시 재생 수 제한. 0이면 제한 없음(풀 한도만).")]
    [Min(0)] public int maxVoices;

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

public enum UpperHeavySfxStage
{
    DarkPull, // 흡인 시작
    DarkForm, // 공중에 폭발물 생성
    DarkBurst, // 폭발(피해)
    LightHit1,
    LightHit2,
    LightHit3,
    LightBuildUp, // 3연타 첫 타부터 3타까지 차오름
    LightSparkle // 마지막 타 뒤 반짝임
}

// 60D 상위 원소 강공 단계음. 암흑 내려치기는 Dark.heavyImpact를 그대로 쓴다.
[Serializable]
public sealed class UpperHeavySfxSettings
{
    [Header("암흑 — 내려치기(0s)는 Dark.heavyImpact")]
    public MeleeElementSfxCueSettings darkPull = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings darkForm = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings darkBurst = new MeleeElementSfxCueSettings();
    [Tooltip("내려치기 뒤 공중 생성음 시각(VFX 시간, 재생 속도로 나눔).")]
    [Min(0f)] public float darkFormDelay = 0.7f;

    [Header("빛 — 3연타 0/0.8/1.6s, 2연타는 2·3타만")]
    public MeleeElementSfxCueSettings lightHit1 = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings lightHit2 = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings lightHit3 = new MeleeElementSfxCueSettings();
    [Tooltip("3연타일 때만 첫 타와 함께 시작.")]
    public MeleeElementSfxCueSettings lightBuildUp = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings lightSparkle = new MeleeElementSfxCueSettings();
    [Tooltip("마지막 타 뒤 반짝임 시각(초).")]
    [Min(0f)] public float lightSparkleDelay = 0.15f;

    public MeleeElementSfxCueSettings Get(UpperHeavySfxStage stage)
    {
        switch (stage)
        {
            case UpperHeavySfxStage.DarkPull: return darkPull;
            case UpperHeavySfxStage.DarkForm: return darkForm;
            case UpperHeavySfxStage.DarkBurst: return darkBurst;
            case UpperHeavySfxStage.LightHit1: return lightHit1;
            case UpperHeavySfxStage.LightHit2: return lightHit2;
            case UpperHeavySfxStage.LightHit3: return lightHit3;
            case UpperHeavySfxStage.LightBuildUp: return lightBuildUp;
            case UpperHeavySfxStage.LightSparkle: return lightSparkle;
            default: return null;
        }
    }
}

[Serializable]
public sealed class MeleeElementSfxEntry
{
    public WeaponElement element;
    public MeleeElementSfxCueSettings slash = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings hit = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings heavyImpact = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings criticalHit = new MeleeElementSfxCueSettings();
    public MeleeElementSfxCueSettings followUp = new MeleeElementSfxCueSettings();

    public MeleeElementSfxCueSettings GetSettings(MeleeElementSfxCueType cueType)
    {
        switch (cueType)
        {
            case MeleeElementSfxCueType.Slash: return slash;
            case MeleeElementSfxCueType.Hit: return hit;
            case MeleeElementSfxCueType.HeavyImpact: return heavyImpact;
            case MeleeElementSfxCueType.CriticalHit: return criticalHit;
            case MeleeElementSfxCueType.FollowUp: return followUp;
            default: return null;
        }
    }
}

[CreateAssetMenu(
    fileName = "MeleeElementSfxCatalog",
    menuName = "OVERBURST/Weapons/Melee Element SFX Catalog")]
public sealed class MeleeElementSfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/SFX/MeleeElementSfxCatalog";

    public MeleeElementSfxEntry[] entries = Array.Empty<MeleeElementSfxEntry>();
    public UpperHeavySfxSettings upperHeavy = new UpperHeavySfxSettings();

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
