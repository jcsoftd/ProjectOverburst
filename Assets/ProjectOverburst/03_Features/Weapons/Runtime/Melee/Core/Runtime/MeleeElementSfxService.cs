using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-850)]
public sealed class MeleeElementSfxService : MonoBehaviour
{
    public const int InitialPoolSize = 8;
    public const int MaximumPoolSize = 32;
    public const float SilentEnergy = 1f;
    public const float FullVolumeEnergy = 60f;

    public static float ResolveEnergyVolume(float energy)
    {
        if (float.IsNaN(energy)) return 0f;
        return Mathf.Clamp01((energy - SilentEnergy) / (FullVolumeEnergy - SilentEnergy));
    }

    private sealed class Voice
    {
        public AudioSource Source;
        public double EndDspTime;
        public int Key = -1; // 속성·큐 키 (maxVoices 계산용)
    }

    private static MeleeElementSfxService instance;
    private static MeleeElementSfxResolver resolver;
    private static float globalVolume = 1f;

    private readonly Stack<Voice> available = new Stack<Voice>(InitialPoolSize);
    private readonly List<Voice> active = new List<Voice>(MaximumPoolSize);
    private readonly Dictionary<int, float> nextAllowedTime = new Dictionary<int, float>(16);
    private int createdVoiceCount;

    public static float GlobalVolume
    {
        get => globalVolume;
        set => globalVolume = Mathf.Clamp01(value);
    }

    public static void Configure(MeleeElementSfxCatalog catalog)
    {
        resolver = catalog != null ? new MeleeElementSfxResolver(catalog) : null;
        configuredCatalog = catalog;
        EnsureInstance();
        instance?.nextAllowedTime.Clear();
    }

    private static MeleeElementSfxCatalog configuredCatalog;
    private const int UpperHeavyKeyBase = 1000; // 속성·큐 키(최대 9*5+4)와 겹치지 않게

    public static float DarkFormDelay => configuredCatalog != null && configuredCatalog.upperHeavy != null
        ? configuredCatalog.upperHeavy.darkFormDelay : 0.7f;
    public static float LightSparkleDelay => configuredCatalog != null && configuredCatalog.upperHeavy != null
        ? configuredCatalog.upperHeavy.lightSparkleDelay : 0.15f;

    // 60D 암흑·빛 강공 단계음 한 번.
    public static bool TryPlayUpperHeavy(UpperHeavySfxStage stage, Vector3 position, bool successfulParry = false, float energy = FullVolumeEnergy)
    {
        if (ResolveEnergyVolume(energy) <= 0f) return false;
        EnsureInstance();
        MeleeElementSfxCueSettings settings = configuredCatalog != null && configuredCatalog.upperHeavy != null
            ? configuredCatalog.upperHeavy.Get(stage) : null;
        bool played = instance != null && settings != null && settings.TryPickClip(out AudioClip clip)
            && instance.TryPlaySettings(settings, clip, UpperHeavyKeyBase + (int)stage, position, successfulParry, energy);
        if (played && (int)stage < UpperHeavyStageSlots)
        {
            upperHeavyPlays[(int)stage]++;
            upperHeavyLastTime[(int)stage] = Time.time;
        }
        return played;
    }

    // 검증용: 단계별 재생 횟수와 마지막 재생 시각(Time.time).
    private const int UpperHeavyStageSlots = 16;
    private static readonly int[] upperHeavyPlays = new int[UpperHeavyStageSlots];
    private static readonly float[] upperHeavyLastTime = new float[UpperHeavyStageSlots];
    public static int UpperHeavyPlayCount(UpperHeavySfxStage stage)
        => (int)stage < UpperHeavyStageSlots ? upperHeavyPlays[(int)stage] : 0;
    public static float UpperHeavyLastPlayTime(UpperHeavySfxStage stage)
        => (int)stage < UpperHeavyStageSlots ? upperHeavyLastTime[(int)stage] : -1f;
    public static void ResetUpperHeavyCounters()
    {
        System.Array.Clear(upperHeavyPlays, 0, upperHeavyPlays.Length);
        for (int i = 0; i < upperHeavyLastTime.Length; i++) upperHeavyLastTime[i] = -1f;
    }

    // 빛 강공 N타(0 내려치기 · 1 2타 · 2 마지막 타). 2연타는 내려치기 뒤 곧바로 마지막 타(2)다.
    // 2타·마지막 타에는 폭발과 공용 저음을 같은 순간 겹친다(2026-09-30 청음 결정).
    public static bool TryPlayLightHeavyHit(int hitIndex, Vector3 position, bool successfulParry = false, float energy = FullVolumeEnergy)
    {
        UpperHeavySfxStage stage = hitIndex <= 0 ? UpperHeavySfxStage.LightHit1
            : hitIndex == 1 ? UpperHeavySfxStage.LightHit2 : UpperHeavySfxStage.LightHit3;
        bool played = TryPlayUpperHeavy(stage, position, successfulParry && hitIndex <= 0, energy);
        if (hitIndex <= 0) return played;
        TryPlayUpperHeavy(hitIndex == 1 ? UpperHeavySfxStage.LightHit2Layer : UpperHeavySfxStage.LightHit3Layer, position, energy: energy);
        TryPlayUpperHeavy(UpperHeavySfxStage.HeavyLowBoom, position, energy: energy);
        return played;
    }

    // 암흑 강공 폭발: 폭발음 + 겹침 폭발 + 공용 저음.
    public static bool TryPlayDarkBurst(Vector3 position, float energy = FullVolumeEnergy)
    {
        bool played = TryPlayUpperHeavy(UpperHeavySfxStage.DarkBurst, position, energy: energy);
        TryPlayUpperHeavy(UpperHeavySfxStage.DarkBurstLayer, position, energy: energy);
        TryPlayUpperHeavy(UpperHeavySfxStage.HeavyLowBoom, position, energy: energy);
        return played;
    }

    // 불·어둠·빛은 전용 착지 큐를 사용한다. 기본 지면음은 원소와 별개로 항상 재생한다.
    public static bool UsesUpperSlamCue(OverburstElementDischarge discharge)
    {
        return discharge != null && (discharge.Element == WeaponElement.Fire
            || discharge.Element == WeaponElement.Light
            || (discharge.Element == WeaponElement.Dark && discharge.Energy > 0f));
    }

    // 내려치는 순간 한 번. 재생했으면 true. 빛은 3연타·2연타 모두 차오름 + 1타(내려치기) 소리로 시작하고,
    // 뒤따르는 타는 LightTripleImpactScheduler가 2타(3연타만)·마지막 타 순서로 낸다.
    public static bool TryPlayUpperSlam(OverburstElementDischarge discharge, int lightSlamHitIndex, Vector3 position, bool successfulParry = false)
    {
        if (!UsesUpperSlamCue(discharge)) return false;
        if (discharge.Element != WeaponElement.Light)
            return TryPlayHeavyImpact(discharge.Element, position, successfulParry, discharge.Energy);
        TryPlayUpperHeavy(UpperHeavySfxStage.LightBuildUp, position, energy: discharge.Energy);
        return TryPlayLightHeavyHit(0, position, successfulParry, discharge.Energy);
    }

    public static bool TryPlaySlash(WeaponElement element, Vector3 position, float energy = FullVolumeEnergy)
    {
        return TryPlay(element, MeleeElementSfxCueType.Slash, position, energy: energy);
    }

    public static bool TryPlayHit(WeaponElement element, Vector3 position, bool critical = false, float energy = FullVolumeEnergy)
    {
        // 2026-09-30 17:56 결정: 무속성 무기는 무기 타격음이 없다(치명타 포함). 몬스터 공용 피격음이 모든 적에게 난다.
        if (element == WeaponElement.None) return false;
        // 치명타 큐가 비어 있거나 재생되지 않으면 일반 타격음으로 대체한다.
        return (critical && TryPlay(element, MeleeElementSfxCueType.CriticalHit, position, energy: energy))
            || TryPlay(element, MeleeElementSfxCueType.Hit, position, energy: energy);
    }

    public static bool TryPlayHeavyImpact(WeaponElement element, Vector3 position, bool successfulParry = false, float energy = FullVolumeEnergy)
    {
        return TryPlay(element, MeleeElementSfxCueType.HeavyImpact, position, successfulParry, energy);
    }

    // 강공 방출 후속 한 번: 번개 홉 도착점, 화염 전파 폭발점, 얼음 쇄빙 대상 위치.
    public static bool TryPlayFollowUp(WeaponElement element, Vector3 position, float energy = FullVolumeEnergy)
    {
        return TryPlay(element, MeleeElementSfxCueType.FollowUp, position, energy: energy);
    }

    public static bool IsSlashCueKey(string key)
    {
        return key == MeleeElementAttackVfxCatalog.BasicSlashKey
            || key == MeleeElementAttackVfxCatalog.CircularSlashKey
            || key == MeleeElementAttackVfxCatalog.GroundSlamSlashKey;
    }

    private static bool TryPlay(
        WeaponElement element,
        MeleeElementSfxCueType cueType,
        Vector3 position,
        bool successfulParry = false,
        float energy = FullVolumeEnergy)
    {
        if (ResolveEnergyVolume(energy) <= 0f) return false;
        EnsureInstance();
        return instance != null && instance.TryPlayInternal(element, cueType, position, successfulParry, energy);
    }

    private static void EnsureInstance()
    {
        if (instance != null || !Application.isPlaying)
            return;

        GameObject root = new GameObject(nameof(MeleeElementSfxService));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<MeleeElementSfxService>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        for (int i = 0; i < InitialPoolSize; i++)
            available.Push(CreateVoice());
    }

    private void Update()
    {
        double now = AudioSettings.dspTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Voice voice = active[i];
            if (now < voice.EndDspTime && voice.Source != null && voice.Source.isPlaying)
                continue;

            active.RemoveAt(i);
            ReleaseVoice(voice);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private bool TryPlayInternal(
        WeaponElement element,
        MeleeElementSfxCueType cueType,
        Vector3 position,
        bool successfulParry,
        float energy)
    {
        if (resolver == null
            || !resolver.TryResolve(element, cueType, out MeleeElementSfxCueSettings settings)
            || settings == null
            || !settings.TryPickClip(out AudioClip clip))
        {
            return false;
        }

        int cooldownKey = ((int)element * CueTypeCount) + (int)cueType;
        return TryPlaySettings(settings, clip, cooldownKey, position, successfulParry, energy);
    }

    private bool TryPlaySettings(MeleeElementSfxCueSettings settings, AudioClip clip, int cooldownKey, Vector3 position, bool successfulParry, float energy)
    {
        float now = Time.unscaledTime;
        if (nextAllowedTime.TryGetValue(cooldownKey, out float allowedTime) && now < allowedTime)
            return false;
        if (settings.maxVoices > 0 && CountActive(cooldownKey) >= settings.maxVoices)
            return false;

        Voice voice = AcquireVoice();
        if (voice == null || voice.Source == null)
            return false;

        AudioSource source = voice.Source;
        ConfigureSource(source, settings, clip, position, energy);
        source.Play();
        float pitch = Mathf.Max(0.1f, Mathf.Abs(source.pitch));
        voice.EndDspTime = AudioSettings.dspTime + clip.length / pitch + 0.05d;
        voice.Key = cooldownKey;
        active.Add(voice);
        if (successfulParry)
        {
            Voice extra = AcquireVoice();
            if (extra?.Source != null)
            {
                ConfigureSource(extra.Source, settings, clip, position, energy, source.pitch);
                extra.Source.volume = source.volume * 0.5f;
                extra.Source.Play();
                extra.EndDspTime = voice.EndDspTime;
                // The extra layer belongs to this cue, without consuming another cue admission.
                extra.Key = -1;
                active.Add(extra);
            }
        }
        nextAllowedTime[cooldownKey] = now + Mathf.Max(0f, settings.cooldown); // 실제 재생 뒤 소비
        return true;
    }

    private const int CueTypeCount = 5;

    private int CountActive(int key)
    {
        int count = 0;
        for (int i = 0; i < active.Count; i++)
            if (active[i].Key == key)
                count++;
        return count;
    }

    private Voice AcquireVoice()
    {
        Voice voice = available.Count > 0 ? available.Pop() : null;
        if (voice == null && createdVoiceCount < MaximumPoolSize)
            voice = CreateVoice();
        if (voice?.Source != null)
            voice.Source.gameObject.SetActive(true);
        return voice;
    }

    private Voice CreateVoice()
    {
        GameObject voiceObject = new GameObject("MeleeSfxVoice_" + createdVoiceCount);
        voiceObject.transform.SetParent(transform, false);
        AudioSource source = voiceObject.AddComponent<AudioSource>();
        ResetSource(source);
        voiceObject.SetActive(false);
        createdVoiceCount++;
        return new Voice { Source = source };
    }

    private void ReleaseVoice(Voice voice)
    {
        if (voice?.Source == null)
            return;

        ResetSource(voice.Source);
        voice.Source.gameObject.SetActive(false);
        voice.EndDspTime = 0d;
        voice.Key = -1;
        available.Push(voice);
    }

    private static void ConfigureSource(
        AudioSource source,
        MeleeElementSfxCueSettings settings,
        AudioClip clip,
        Vector3 position,
        float energy,
        float? pitchOverride = null)
    {
        ResetSource(source);
        source.transform.position = position;
        source.clip = clip;
        source.volume = ResolveOutputVolume(settings.volume) * ResolveEnergyVolume(energy);
        source.pitch = pitchOverride ?? settings.ResolvePitch();
        source.spatialBlend = settings.spatial ? 1f : 0f;
        source.minDistance = Mathf.Max(0.1f, settings.minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.1f, settings.maxDistance);
    }

    private static float ResolveOutputVolume(float localVolume)
    {
        return Mathf.Clamp01(Mathf.Max(0f, localVolume) * globalVolume); // 중앙 옵션 연결 지점
    }

    private static void ResetSource(AudioSource source)
    {
        source.Stop();
        source.clip = null;
        source.outputAudioMixerGroup = null;
        source.playOnAwake = false;
        source.loop = false;
        source.mute = false;
        source.bypassEffects = false;
        source.bypassListenerEffects = false;
        source.bypassReverbZones = false;
        source.priority = 128;
        source.volume = 1f;
        source.pitch = 1f;
        source.panStereo = 0f;
        source.spatialBlend = 0f;
        source.reverbZoneMix = 1f;
        source.dopplerLevel = 0f;
        source.spread = 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 1f;
        source.maxDistance = 500f;
        source.ignoreListenerVolume = false;
        source.ignoreListenerPause = false;
        source.transform.localPosition = Vector3.zero;
        source.transform.localRotation = Quaternion.identity;
        source.transform.localScale = Vector3.one;
    }
}
