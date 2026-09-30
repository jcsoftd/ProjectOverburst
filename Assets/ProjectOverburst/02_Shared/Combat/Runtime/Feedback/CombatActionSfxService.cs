using UnityEngine;

/// <summary>Separate weapon swings, ground impact, and organic received hits.</summary>
public sealed class CombatActionSfxService : MonoBehaviour
{
    private const string ResourceRoot = "Combat/SFX/CombatAction/";
    private const int VoiceLimit = 32;

    // 2026-09-30 청음 결정: 지면음 2·3단 = Earth_Explosion_1·2_M, 예고 핑 = Metallic Ring 긴 판, 회피·에너지 가득 신규.
    // 이 다섯 칸은 복사본 없이 CombatActionSfxCatalog가 ThirdParty 원본을 직접 참조한다.
    private static readonly string[] ClipNames =
    {
        "GreatswordLight01", "GreatswordLight02", "GreatswordLight03", "GreatswordLight04",
        "GreatswordHeavySwing", "GreatswordGround01", "GreatswordGround_EarthExplosion1", "GreatswordGround_EarthExplosion2",
        "OrganicHit01", "OrganicHit02", "OrganicHit03", "ParryClash_ImpactRinging", "ParryWindowPing_MetallicRingLong",
        "PlayerEvadeCloth", "ElementEnergyFull"
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
        return instance.Play(clipIndex, position, 0.6f, 0.9f, 4f, 28f, 100);
    }

    // Energy is captured before the heavy discharge consumes it.
    // 2026-09-30: 지면강타 2단계로 축소 — 에너지 절반 미만 Earth_Explosion_1_M, 이상 Earth_Explosion_2_M.
    // GreatswordGround01 칸(5)은 인덱스 유지를 위해 남겨 두고 더는 재생하지 않는다.
    public const float GroundSecondTierEnergy = 0.5f;
    public static bool PlayGreatswordGround(float normalizedEnergy, Vector3 position)
    {
        if (!EnsureInstance()) return false;
        int tier = normalizedEnergy >= GroundSecondTierEnergy ? 1 : 0;
        return instance.Play(6 + tier, position, 0.85f, 1f, 5f, 42f, 75);
    }

    public static bool TryPlayOrganicHit(CombatHitFeedbackRequest request, Vector3 position)
    {
        if (request.Target == null
            || !request.Target.TryGetComponent<BloodHitTarget>(out var bloodTarget)
            || bloodTarget.Profile == null
            || !EnsureInstance())
            return false;

        if (bloodTarget.Profile.suppressBlood)
        {
            // 무혈 몬스터는 살점 대신 재질음(뼈 등). 프로필에 지정한 종만 재생한다.
            AudioClip material = bloodTarget.Profile.PickBloodlessHitClip();
            return material != null && instance.Play(material, position, 0.8f, 0.9f, 2f, 22f, 95);
        }

        int tier = request.IsCritical || request.IsLethal ? 2
            : request.ImpactShape == CombatImpactShape.Downward ? 1
            : (request.AttackSequenceId + request.PhaseIndex) & 1;
        return instance.Play(8 + tier, position, 0.8f, 0.95f, 2f, 22f, 95);
    }

    // B09: 회피 시작 1회.
    public static bool PlayPlayerEvade(Vector3 position) => EnsureInstance()
        && instance.Play(13, position, 0.5f, 0.75f, 3f, 24f, 110);

    // A24: 원소 에너지 게이지가 100%에 처음 닿은 순간 1회. 전투음 사이에서 튀지 않게 낮게 둔다.
    public static bool PlayElementEnergyFull(Vector3 position) => EnsureInstance()
        && instance.Play(14, position, 0f, 0.45f, 2f, 20f, 60);

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
