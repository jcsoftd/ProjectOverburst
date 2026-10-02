using UnityEngine;

/// <summary>Separate weapon swings, ground impact, and organic received hits.</summary>
public sealed class CombatActionSfxService : MonoBehaviour
{
    private const string ResourceRoot = "Combat/SFX/CombatAction/";
    private const int VoiceLimit = 48; // 23:12 증폭 겹침(같은 클립 2번)으로 32 -> 48

    // 2026-09-30 청음 결정: 지면음 2·3단 = Earth_Explosion_1·2_M, 예고 핑 = Metallic Ring 긴 판, 회피·에너지 가득 신규.
    // 이 다섯 칸은 복사본 없이 CombatActionSfxCatalog가 ThirdParty 원본을 직접 참조한다.
    private static readonly string[] ClipNames =
    {
        "GreatswordLight01", "GreatswordLight02", "GreatswordLight03", "GreatswordLight04",
        "GreatswordHeavySwing", "GreatswordGround01", "GreatswordGround_EarthExplosion1", "GreatswordGround_EarthExplosion2",
        "OrganicHit01", "OrganicHit02", "OrganicHit03", "ParryClash_ImpactRinging", "ParryWindowPing_MetallicRingLong",
        "PlayerEvadeCloth", "ElementEnergyFull", "QuickSlotReady"
    };

    private static CombatActionSfxService instance;
    private readonly AudioClip[] clips = new AudioClip[ClipNames.Length];
    private readonly bool[] missingClipReported = new bool[ClipNames.Length];
    private readonly AudioSource[] voices = new AudioSource[VoiceLimit];
    private int voiceCount;
    private float nextWarningAt;
    private float nextEnemyReleaseAt;
    private float nextEnemyImpactAt;
    private static EnemyTelegraphVisualLibrary enemyVisuals;

    public static bool PlayParrySuccess(Vector3 position) => EnsureInstance()
        && instance.Play(11, position, .08f, .90f, 4f, 32f, 10);
    public static bool PlayStrongWarning(Vector3 position)
    {
        if (!EnsureInstance() || Time.unscaledTime < instance.nextWarningAt) return false;
        bool played = instance.Play(12, position, .40f, .68f, 3f, 20f, 45);
        if (played) instance.nextWarningAt = Time.unscaledTime + .09f;
        return played;
    }

    public static bool PlayEnemyStrongRelease(Vector3 position)
    {
        if (!EnsureInstance() || Time.unscaledTime < instance.nextEnemyReleaseAt) return false;
        if (enemyVisuals == null) enemyVisuals = Resources.Load<EnemyTelegraphVisualLibrary>(
            "Enemies/Balance/EnemyTelegraphVisualLibrary");
        AudioClip clip = enemyVisuals != null ? enemyVisuals.StrongRelease : null;
        if (clip == null) return false;
        bool played = instance.Play(clip, position, 1f, .72f, 3f, 24f, 65);
        if (played) instance.nextEnemyReleaseAt = Time.unscaledTime + .08f;
        return played;
    }

    public static bool PlayEnemyGroundImpact(Vector3 position)
    {
        if (!EnsureInstance() || Time.unscaledTime < instance.nextEnemyImpactAt) return false;
        if (enemyVisuals == null) enemyVisuals = Resources.Load<EnemyTelegraphVisualLibrary>(
            "Enemies/Balance/EnemyTelegraphVisualLibrary");
        AudioClip clip = enemyVisuals != null ? enemyVisuals.GroundImpactSound : null;
        if (clip == null) return false;
        bool played = instance.Play(clip, position, 1f, .60f, 4f, 30f, 55);
        if (played) instance.nextEnemyImpactAt = Time.unscaledTime + .08f;
        return played;
    }

    public static bool PlayGreatswordSwing(int comboIndex, bool heavy, Vector3 position)
    {
        if (!EnsureInstance()) return false;
        int clipIndex = heavy ? 4 : comboIndex;
        if (clipIndex < 0 || clipIndex > 4) return false;
        // 23:12 사용자: 다른 소리를 줄이지 않는다 — 약공 휘두름도 원래 0.9.
        return instance.Play(clipIndex, position, 0.6f, 0.9f, 4f, 28f, 100);
    }

    // Energy is captured before the heavy discharge consumes it.
    // 2026-09-30: 지면강타 2단계로 축소 — 에너지 절반 미만 Earth_Explosion_1_M, 이상 Earth_Explosion_2_M.
    // GreatswordGround01 칸(5)은 인덱스 유지를 위해 남겨 두고 더는 재생하지 않는다.
    public const float GroundSecondTierEnergy = 0.5f;
    public static bool PlayGreatswordGround(float normalizedEnergy, Vector3 position, bool successfulParry = false)
    {
        if (!EnsureInstance()) return false;
        int tier = normalizedEnergy >= GroundSecondTierEnergy ? 1 : 0;
        bool played = instance.Play(6 + tier, position, 0.85f, 1f, 5f, 42f, 75);
        if (played && successfulParry)
            instance.Play(6 + tier, position, 0.85f, 0.5f, 5f, 42f, 76);
        return played;
    }

    // 2026-09-30 17:56 구조: 몬스터 피격 = 공용 피격음(모든 몬스터, Stab 01~03 랜덤) + 종류별 추가음.
    // 22:57 묶음 적용: 추가음은 몬스터 프리팹의 MonsterHitSfxTarget 묶음에서 세트 하나(타격마다 번갈아)를 겹쳐 낸다.
    // OrganicHit 살점음·BloodHitProfile 재질음은 더 쓰지 않는다(묶음이 대신한다). 묶음이 없으면 공용 피격음만.
    // 볼륨은 청음 체크리스트 비율 — 23:12부터 줄이지 않고 올리는 방식: 공용·원소·휘두름은 원래 크기, 묶음 층은
    // 50% -> 1.0, 70% -> 1.2, 100% -> 1.6 (공용 피격음도 x1.6). 1을 넘는 몫은 같은 클립을 한 번 더 겹쳐 증폭한다.
    private static readonly string[] MonsterHitCommonNames = { "MonsterHitCommon01", "MonsterHitCommon02", "MonsterHitCommon03" };
    private const float MonsterHitCommonVolume = 1f;
    private const float MonsterHitCommonBoost = 1.6f; // 공용 피격음 = 체크리스트 100% (23:38 사용자: 2배 -> 1.6배)
    private readonly AudioClip[] monsterHitCommonClips = new AudioClip[MonsterHitCommonNames.Length];
    private bool monsterHitCommonMissingReported;
    private int lastMonsterHitCommon = -1;

    public static bool TryPlayOrganicHit(CombatHitFeedbackRequest request, Vector3 position)
    {
        if (request.Target == null
            || !request.Target.TryGetComponent<BloodHitTarget>(out var bloodTarget)
            || bloodTarget.Profile == null
            || !EnsureInstance())
            return false;

        bool played = instance.PlayMonsterHitCommon(position);
        if (!request.Target.TryGetComponent<MonsterHitSfxTarget>(out var sfxTarget) || sfxTarget.Bundle == null)
            return played;

        MonsterHitSfxBundle bundle = sfxTarget.Bundle;
        if ((request.IsCritical || request.IsLethal) && bundle.criticalBundle != null) bundle = bundle.criticalBundle;
        MonsterHitSfxBundle.Set set = bundle.PickSet();
        if (set == null || set.layers == null) return played;
        foreach (var layer in set.layers)
            if (layer != null && layer.clip != null
                && instance.PlayBoosted(layer.clip, position, layer.volume))
                played = true;
        return played;
    }

    // 1을 넘는 볼륨: 같은 클립을 같은 순간 한 번 더 겹쳐 낸다(AudioSource 볼륨 상한 1 우회, 최대 2배).
    private bool PlayBoosted(AudioClip clip, Vector3 position, float volume)
    {
        bool played = Play(clip, position, 0.8f, Mathf.Min(1f, volume), 2f, 22f, 95);
        if (played && volume > 1.01f) Play(clip, position, 0.8f, Mathf.Min(1f, volume - 1f), 2f, 22f, 96);
        return played;
    }

    // 공용 피격음 하나를 랜덤으로(바로 앞과 같은 소리는 피함).
    private bool PlayMonsterHitCommon(Vector3 position)
    {
        int count = MonsterHitCommonNames.Length;
        int pick = Random.Range(0, count);
        if (count > 1 && pick == lastMonsterHitCommon) pick = (pick + 1 + Random.Range(0, count - 1)) % count;
        if (monsterHitCommonClips[pick] == null) monsterHitCommonClips[pick] = ResolveNamedClip(MonsterHitCommonNames[pick]);
        AudioClip clip = monsterHitCommonClips[pick];
        if (clip == null)
        {
            if (!monsterHitCommonMissingReported)
            {
                Debug.LogError("[CombatActionSfxService] Missing clip: " + MonsterHitCommonNames[pick]);
                monsterHitCommonMissingReported = true;
            }
            return false;
        }
        lastMonsterHitCommon = pick;
        return PlayBoosted(clip, position, MonsterHitCommonVolume * MonsterHitCommonBoost);
    }

    // B09: 회피 시작 1회.
    public static bool PlayPlayerEvade(Vector3 position) => EnsureInstance()
        && instance.Play(13, position, 0.5f, 0.75f, 3f, 24f, 110);

    // A24: 원소 에너지 게이지가 100%에 처음 닿은 순간 1회. 전투음 사이에서 튀지 않게 낮게 둔다.
    public static bool PlayElementEnergyFull(Vector3 position) => EnsureInstance()
        && instance.Play(14, position, 0f, 0.45f, 2f, 20f, 60);

    // 2026-09-30: 물약·퀵슬롯 쿨다운이 끝나 다시 쓸 수 있게 된 순간 1회(2D). 여러 칸이 같은 순간 풀려도 한 번만 낸다.
    // 음원은 임시(InfinityPBR Generic_Buff_1_M), 청음 후 교체.
    private float nextQuickSlotReadyAt;
    public static bool PlayQuickSlotReady()
    {
        if (!EnsureInstance() || Time.unscaledTime < instance.nextQuickSlotReadyAt) return false;
        bool played = instance.Play(15, instance.transform.position, 0f, 0.35f, 2f, 20f, 90);
        if (played) instance.nextQuickSlotReadyAt = Time.unscaledTime + .12f;
        return played;
    }

    private static CombatActionSfxCatalog directClips;
    private static bool directClipsLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDirectClips()
    {
        directClips = null;
        directClipsLoaded = false;
    }

    // 공급사 원본을 직접 가리키는 카탈로그가 먼저, 없으면 기존 Resources 클립. 다른 전투 소리도 같은 이름으로 쓸 수 있다.
    public static AudioClip ResolveNamedClip(string clipName)
    {
        if (!directClipsLoaded)
        {
            directClips = Resources.Load<CombatActionSfxCatalog>(CombatActionSfxCatalog.ResourcePath);
            directClipsLoaded = true;
        }
        AudioClip direct = directClips != null ? directClips.Find(clipName) : null;
        return direct != null ? direct : Resources.Load<AudioClip>(ResourceRoot + clipName);
    }

    private static bool EnsureInstance()
    {
        if (!Application.isPlaying) return false;
        if (instance != null) return true;
        var root = new GameObject(nameof(CombatActionSfxService));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<CombatActionSfxService>();
        return instance != null;
    }

    private bool Play(int index, Vector3 position, float spatialBlend, float volume,
        float minDistance, float maxDistance, int priority)
    {
        AudioClip clip = clips[index];
        if (clip == null)
        {
            clip = ResolveNamedClip(ClipNames[index]);
            if (clip == null)
            {
                if (!missingClipReported[index])
                {
                    Debug.LogError("[CombatActionSfxService] Missing clip: " + ClipNames[index]);
                    missingClipReported[index] = true;
                }
                return false;
            }
            clips[index] = clip;
        }

        return Play(clip, position, spatialBlend, volume, minDistance, maxDistance, priority);
    }

    private bool Play(AudioClip clip, Vector3 position, float spatialBlend, float volume,
        float minDistance, float maxDistance, int priority)
    {
        AudioSource source = null;
        for (int i = 0; i < voiceCount; i++)
            if (!voices[i].isPlaying) { source = voices[i]; break; }
        if (source == null)
        {
            if (voiceCount < VoiceLimit)
            {
                var voice = new GameObject("CombatSfxVoice_" + voiceCount);
                voice.transform.SetParent(transform, false);
                source = voice.AddComponent<AudioSource>();
                voices[voiceCount++] = source;
            }
            else
            {
                // A successful parry must cut through a crowded mix. Lower-priority
                // effects may be replaced; ordinary sounds may never evict the clash.
                int replaceIndex = -1;
                int lowestImportance = priority;
                for (int i = 0; i < voiceCount; i++)
                {
                    if (voices[i].priority <= lowestImportance) continue;
                    replaceIndex = i;
                    lowestImportance = voices[i].priority;
                }
                if (replaceIndex < 0) return false;
                source = voices[replaceIndex];
                source.Stop();
            }
        }

        source.transform.position = position;
        source.playOnAwake = false;
        source.loop = false;
        source.clip = clip;
        source.pitch = 1f;
        source.volume = volume;
        source.priority = priority;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.Play();
        return true;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
