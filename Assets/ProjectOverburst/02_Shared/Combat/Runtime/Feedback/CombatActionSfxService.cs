using UnityEngine;

/// <summary>Separate weapon swings, ground impact, and organic received hits.</summary>
public sealed class CombatActionSfxService : MonoBehaviour
{
    private const string ResourceRoot = "Combat/SFX/CombatAction/";
    private const int VoiceLimit = 32;

    private static readonly string[] ClipNames =
    {
        "GreatswordLight01", "GreatswordLight02", "GreatswordLight03", "GreatswordLight04",
        "GreatswordHeavySwing", "GreatswordGround01", "GreatswordGround02", "GreatswordGround03",
        "OrganicHit01", "OrganicHit02", "OrganicHit03", "ParryClash_v3", "ParryWindowPing_v3"
    };

    private static CombatActionSfxService instance;
    private readonly AudioClip[] clips = new AudioClip[13];
    private readonly bool[] missingClipReported = new bool[13];
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
    public static bool PlayGreatswordGround(float normalizedEnergy, Vector3 position)
    {
        if (!EnsureInstance()) return false;
        int tier = normalizedEnergy >= 0.67f ? 2 : normalizedEnergy >= 0.34f ? 1 : 0;
        return instance.Play(5 + tier, position, 0.85f, 1f, 5f, 42f, 75);
    }

    public static bool TryPlayOrganicHit(CombatHitFeedbackRequest request, Vector3 position)
    {
        if (request.Target == null
            || !request.Target.TryGetComponent<BloodHitTarget>(out var bloodTarget)
            || bloodTarget.Profile == null || bloodTarget.Profile.suppressBlood
            || !EnsureInstance())
            return false;

        int tier = request.IsCritical || request.IsLethal ? 2
            : request.ImpactShape == CombatImpactShape.Downward ? 1
            : (request.AttackSequenceId + request.PhaseIndex) & 1;
        return instance.Play(8 + tier, position, 0.8f, 0.95f, 2f, 22f, 95);
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
            clip = Resources.Load<AudioClip>(ResourceRoot + ClipNames[index]);
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
