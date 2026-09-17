using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-840)]
public sealed class ElementalReactionSfxService : MonoBehaviour
{
    public const int InitialPoolSize = 8; // 평시 동시 반응음 선할당
    public const int MaximumPoolSize = 24; // 대규모 전투 음성 상한

    private sealed class Voice
    {
        public AudioSource Source;
        public double EndDspTime;
    }

    private static ElementalReactionSfxService instance;
    private static ElementalReactionSfxCatalog catalog;
    private static float globalVolume = 1f;

    private readonly Stack<Voice> available = new Stack<Voice>(InitialPoolSize);
    private readonly List<Voice> active = new List<Voice>(MaximumPoolSize);
    private readonly Dictionary<ElementalReactionSfxCueType, float> nextAllowedTime =
        new Dictionary<ElementalReactionSfxCueType, float>(8);
    private int createdVoiceCount;

    public static float GlobalVolume
    {
        get => globalVolume;
        set => globalVolume = Mathf.Clamp01(value);
    }

    public static void Configure(ElementalReactionSfxCatalog configuredCatalog)
    {
        catalog = configuredCatalog;
        EnsureInstance();
        instance?.nextAllowedTime.Clear();
    }

    private static void EnsureInstance()
    {
        if (instance != null || !Application.isPlaying)
            return;

        GameObject root = new GameObject(nameof(ElementalReactionSfxService));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<ElementalReactionSfxService>();
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

    private void OnEnable()
    {
        ElementalReactionEvents.ReactionStarted -= HandleReactionStarted;
        ElementalReactionEvents.ReactionStarted += HandleReactionStarted;
        ElementalReactionEvents.ReactionProcExecuted -= HandleReactionProcExecuted;
        ElementalReactionEvents.ReactionProcExecuted += HandleReactionProcExecuted;
        ElementalReactionEvents.ChainHopExecuted -= HandleChainHopExecuted;
        ElementalReactionEvents.ChainHopExecuted += HandleChainHopExecuted;
    }

    private void OnDisable()
    {
        ElementalReactionEvents.ReactionStarted -= HandleReactionStarted;
        ElementalReactionEvents.ReactionProcExecuted -= HandleReactionProcExecuted;
        ElementalReactionEvents.ChainHopExecuted -= HandleChainHopExecuted;
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

    private void HandleReactionStarted(ElementalReactionEvent reactionEvent)
    {
        switch (reactionEvent.ReactionType)
        {
            case ElementalReactionType.Vaporize:
                TryPlay(ElementalReactionSfxCueType.Vaporize, reactionEvent.Center);
                break;
            case ElementalReactionType.ThermalFracture:
                TryPlay(ElementalReactionSfxCueType.ThermalFracture, reactionEvent.Center);
                break;
            case ElementalReactionType.Freeze:
                TryPlay(ElementalReactionSfxCueType.Freeze, reactionEvent.Center);
                break;
        }
    }

    private void HandleReactionProcExecuted(ElementalReactionProcEvent procEvent)
    {
        switch (procEvent.ProcType)
        {
            case ElementalReactionProcType.Plasma:
                TryPlay(ElementalReactionSfxCueType.PlasmaExplosion, procEvent.Center);
                break;
            case ElementalReactionProcType.Shatter:
                TryPlay(ElementalReactionSfxCueType.Shatter, procEvent.Center);
                break;
            case ElementalReactionProcType.ColdCharge:
                TryPlay(ElementalReactionSfxCueType.ColdChargeExplosion, procEvent.Center);
                break;
        }
    }

    private void HandleChainHopExecuted(ElementalReactionChainHopEvent hopEvent)
    {
        if (hopEvent.HopIndex != 1)
            return; // 한 직접 타격의 첫 실제 전이에서만 1개 재생

        TryPlay(ElementalReactionSfxCueType.ChainTransition, hopEvent.Center);
    }

    private bool TryPlay(ElementalReactionSfxCueType cueType, Vector3 position)
    {
        if (catalog == null
            || !catalog.TryResolve(cueType, out ElementalReactionSfxCueSettings settings)
            || settings == null
            || !settings.TryPickClip(out AudioClip clip))
        {
            return false;
        }

        float now = Time.unscaledTime;
        if (settings.cooldown > 0f
            && nextAllowedTime.TryGetValue(cueType, out float allowedTime)
            && now < allowedTime)
        {
            return false;
        }

        Voice voice = AcquireVoice();
        if (voice?.Source == null)
            return false;

        AudioSource source = voice.Source;
        ConfigureSource(source, settings, clip, position);
        source.Play();
        float pitch = Mathf.Max(0.1f, Mathf.Abs(source.pitch));
        voice.EndDspTime = AudioSettings.dspTime + clip.length / pitch + 0.05d;
        active.Add(voice);
        if (settings.cooldown > 0f)
            nextAllowedTime[cueType] = now + settings.cooldown; // 성공한 재생만 제한
        return true;
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
        GameObject voiceObject = new GameObject("ReactionSfxVoice_" + createdVoiceCount);
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
        available.Push(voice);
    }

    private static void ConfigureSource(
        AudioSource source,
        ElementalReactionSfxCueSettings settings,
        AudioClip clip,
        Vector3 position)
    {
        ResetSource(source);
        source.transform.position = position;
        source.clip = clip;
        source.volume = Mathf.Clamp01(Mathf.Max(0f, settings.volume) * globalVolume);
        source.pitch = settings.ResolvePitch();
        source.spatialBlend = settings.spatial ? 1f : 0f;
        source.minDistance = Mathf.Max(0.1f, settings.minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.1f, settings.maxDistance);
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
