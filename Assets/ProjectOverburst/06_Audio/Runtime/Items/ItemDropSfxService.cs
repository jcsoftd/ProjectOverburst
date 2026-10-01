using UnityEngine;

// 월드 아이템 드랍 소리 재생기. 카탈로그는 Resources에서 한 번만 읽는다.
public sealed class ItemDropSfxService : MonoBehaviour
{
    private const int VoiceLimit = 8;
    private const int CueSlots = 16; // 종류 7 + 등급 여유

    private static ItemDropSfxService instance;
    private static ItemDropSfxCatalog catalog;
    private static bool catalogLoaded;

    private readonly AudioSource[] voices = new AudioSource[VoiceLimit];
    private readonly float[] nextAllowedTime = new float[CueSlots];
    private int voiceCount;
    private int nextVoice;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        catalog = null;
        catalogLoaded = false;
    }

    // 아이템이 착지해 등급 효과가 나타날 때 전설·유물·신화면 등급음 1회.
    public static bool PlayReveal(ItemGrade grade, Vector3 position)
    {
        ItemDropSfxCatalog source = ResolveCatalog();
        AudioClip clip = source != null ? source.GetRevealClip(grade) : null;
        return clip != null && EnsureInstance()
            && instance.Play(clip, 8 + (int)grade % 8, position, source.revealVolume, source);
    }

    // 착지 순간 종류별 드랍음 1회.
    public static bool PlayLanded(BaseItemData data, Vector3 position)
    {
        ItemDropSfxCatalog source = ResolveCatalog();
        if (source == null || data == null) return false;
        ItemDropSfxKind kind = Classify(data);
        AudioClip clip = Pick(source.GetDropClips(kind));
        return clip != null && EnsureInstance()
            && instance.Play(clip, (int)kind, position, source.dropVolume, source);
    }

    public static ItemDropSfxKind Classify(BaseItemData data)
    {
        switch (data)
        {
            case WeaponItemData _: return ItemDropSfxKind.Weapon;
            case GearItemData gear:
                return gear.kind == GearKind.Earring || gear.kind == GearKind.Necklace
                    ? ItemDropSfxKind.Accessory : ItemDropSfxKind.Armor;
            case FlaskItemData _: return ItemDropSfxKind.Potion;
            case ConsumableItemData _: return ItemDropSfxKind.Potion;
            case MapItemData _: return ItemDropSfxKind.Map;
            case CurrencyItemData currency:
                return currency.currencyType == CurrencyType.MapFragment ? ItemDropSfxKind.Map : ItemDropSfxKind.Gold;
            default: return ItemDropSfxKind.Bag; // 가방·잡템·퀘스트·기타
        }
    }

    private static AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        int start = Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip clip = clips[(start + i) % clips.Length];
            if (clip != null) return clip;
        }
        return null;
    }

    private static ItemDropSfxCatalog ResolveCatalog()
    {
        if (!catalogLoaded)
        {
            catalog = Resources.Load<ItemDropSfxCatalog>(ItemDropSfxCatalog.ResourcePath);
            catalogLoaded = true;
        }
        return catalog;
    }

    private static bool EnsureInstance()
    {
        if (!Application.isPlaying) return false;
        if (instance != null) return true;
        var root = new GameObject(nameof(ItemDropSfxService));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<ItemDropSfxService>();
        return instance != null;
    }

    private bool Play(AudioClip clip, int cueSlot, Vector3 position, float volume, ItemDropSfxCatalog settings)
    {
        float now = Time.unscaledTime;
        cueSlot = Mathf.Clamp(cueSlot, 0, CueSlots - 1);
        if (now < nextAllowedTime[cueSlot]) return false;

        AudioSource source = null;
        for (int i = 0; i < voiceCount; i++)
            if (!voices[i].isPlaying) { source = voices[i]; break; }
        if (source == null)
        {
            if (voiceCount < VoiceLimit)
            {
                var voice = new GameObject("ItemDropSfxVoice_" + voiceCount);
                voice.transform.SetParent(transform, false);
                source = voice.AddComponent<AudioSource>();
                voices[voiceCount++] = source;
            }
            else
            {
                source = voices[nextVoice]; // 가장 오래된 순서로 돌려 쓴다
                nextVoice = (nextVoice + 1) % VoiceLimit;
                source.Stop();
            }
        }

        source.transform.position = position;
        source.playOnAwake = false;
        source.loop = false;
        source.clip = clip;
        source.pitch = 1f;
        source.volume = Mathf.Clamp01(volume);
        source.priority = 120;
        source.spatialBlend = settings.spatialBlend;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = settings.minDistance;
        source.maxDistance = Mathf.Max(settings.minDistance + 0.1f, settings.maxDistance);
        source.dopplerLevel = 0f;
        source.Play();
        nextAllowedTime[cueSlot] = now + settings.sameCueCooldown;
        return true;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
