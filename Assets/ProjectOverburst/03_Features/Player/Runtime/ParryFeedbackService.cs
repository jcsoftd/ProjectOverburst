using System.Collections.Generic;
using UnityEngine;

// 패링 성공 한 번(같은 프레임의 다수 패링 포함)의 공용 피드백.
// 무지개 플레어를 패링 중심에 한 번 재생하고 주변 소형 몬스터를 밀어내고 경직시킨다.
// 주 패링음(팅)은 CombatActionSfxService 소유를 유지하고, 여기서는 겹침·저음 레이어만 낸다.
public sealed class ParryFeedbackService : MonoBehaviour
{
    public struct Tier
    {
        public float HitStop, Slow, KnockbackRadius, CameraAmplitude, Zoom;
        public float SlowScale, SlowRecover, ZoomIn, ZoomOut, CameraDuration, FlashScale;
        public ParryGrade Grade;
    }

    private static readonly Tier[] Grades =
    {
        new Tier { Grade = ParryGrade.Incomplete, HitStop = .04f, Slow = .12f, SlowScale = .65f, SlowRecover = .15f,
            CameraAmplitude = .04f, CameraDuration = .10f, Zoom = .02f, ZoomIn = .04f, ZoomOut = .18f, FlashScale = 1f },
        new Tier { Grade = ParryGrade.Normal, HitStop = .05f, Slow = .15f, SlowScale = .5f, SlowRecover = .20f,
            CameraAmplitude = .06f, CameraDuration = .12f, Zoom = .04f, ZoomIn = .045f, ZoomOut = .25f, FlashScale = 1f },
        new Tier { Grade = ParryGrade.Perfect, HitStop = .09f, Slow = .30f, SlowScale = .15f, SlowRecover = .35f,
            KnockbackRadius = 2.5f, CameraAmplitude = .12f, CameraDuration = .20f, Zoom = .09f, ZoomIn = .06f, ZoomOut = .40f, FlashScale = 1f },
    };

    private const int FlashPoolCapacity = 8;
    private const int KnockbackCap = 24;
    private const float KnockbackNear = 2.2f, KnockbackFar = 1.2f, KnockbackStagger = .45f;
    private const string SfxRoot = "Combat/SFX/CombatAction/";
    private const int VoiceCount = 8;

    private static ParryFeedbackService instance;
    private static EnemyTelegraphVisualLibrary library;
    private static readonly List<EnemyRank> ActiveScratch = new List<EnemyRank>(160);
    private static readonly List<KeyValuePair<float, EnemyActor>> Candidates = new List<KeyValuePair<float, EnemyActor>>(64);
    private readonly AudioSource[] voices = new AudioSource[VoiceCount];
    private int nextVoice;
    private AudioClip tingClip, thumpClip;
    public static int LastAdditionalTingCount { get; private set; }
    public static int LastPerfectLayerCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; library = null; LastAdditionalTingCount = 0; LastPerfectLayerCount = 0; }

    public static Tier ResolveTier(ParryGrade grade) => Grades[Mathf.Clamp((int)grade, 0, Grades.Length - 1)];

    // Older presentation tools retain their historical count-based preview; gameplay uses the grade overload.
    public static Tier ResolveTier(int parriedCount, bool anyStrong)
    {
        int index = parriedCount >= 4 ? 2 : parriedCount >= 2 ? 1 : 0;
        if (anyStrong) index = Mathf.Min(index + 1, 2);
        Tier tier = ResolveTier(ParryGrade.Perfect);
        tier.HitStop = index == 0 ? .07f : index == 1 ? .08f : .10f;
        tier.Slow = index == 0 ? .25f : index == 1 ? .29f : .35f;
        return tier;
    }

    public static void Play(Vector3 center, Vector3 playerPosition, Tier tier, int parriedCount,
        int chainIndex, ICollection<EnemyActor> parried, PerfectParryContactPresenter perfectContact = null)
    {
        if (!Application.isPlaying || !EnsureInstance()) return;
        // Keep the complete original bundle, then add the perfect-only contact layer.
        instance.SpawnFlash(center, tier.FlashScale);
        if (tier.KnockbackRadius > 0f) KnockbackSmall(center, tier.KnockbackRadius, parried);
        instance.PlayLayers(center, tier.Grade, parriedCount);
        RequestCamera(center - playerPosition, tier.CameraAmplitude, tier.CameraDuration);
        QuarterViewCamera.ActiveInstance?.RequestZoomPunch(tier.Zoom, tier.ZoomIn, tier.HitStop + tier.Slow, tier.ZoomOut);
        LastPerfectLayerCount = 0;
        if (tier.Grade == ParryGrade.Perfect && perfectContact != null && perfectContact.CanPresent)
        {
            perfectContact.QueuePresentation(center);
            var profile = perfectContact.Profile; Vector3 contact = perfectContact.ResolvePosition();
            instance.PlayVoice(profile.impact, contact, profile.impactVolume, 1f);
            instance.PlayVoice(profile.ring, contact, profile.ringVolume, 1f);
            instance.PlayVoice(profile.low, contact, profile.lowVolume, 1f);
            LastPerfectLayerCount = 3;
        }
    }

    // Additional attacks in the same action get a local contact without replaying world/camera/audio feedback.
    public static void PlayContact(Vector3 center, ParryGrade grade)
    {
        if (Application.isPlaying && EnsureInstance()) instance.SpawnFlash(center, ResolveTier(grade).FlashScale * .5f);
    }

    private static bool EnsureInstance()
    {
        if (instance != null) return true;
        var host = new GameObject(nameof(ParryFeedbackService));
        DontDestroyOnLoad(host);
        instance = host.AddComponent<ParryFeedbackService>();
        return instance != null;
    }

    private void Awake()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = .08f;
            source.minDistance = 4f;
            source.maxDistance = 32f;
            source.priority = 12;
            voices[i] = source;
        }
        tingClip = CombatActionSfxService.ResolveNamedClip("ParryClash_ImpactRinging"); // ThirdParty 원본 직접 참조(CombatActionSfxCatalog)
        thumpClip = Resources.Load<AudioClip>(SfxRoot + "GreatswordGround01"); // 임시 저음 레이어. 음원 확정 전
    }

    private void SpawnFlash(Vector3 center, float scale)
    {
        if (library == null)
            library = Resources.Load<EnemyTelegraphVisualLibrary>("Enemies/Balance/EnemyTelegraphVisualLibrary");
        GameObject prefab = library != null ? library.ParrySuccess : null;
        if (prefab == null) return;
        TransientVfxPool.Spawn(prefab, center, Quaternion.identity, 0f, FlashPoolCapacity,
            prepareBeforeActivation: value => value.transform.localScale = prefab.transform.localScale * scale,
            returnMode: TransientVfxReturnMode.NaturalParticleCompletion, useUnscaledTime: true);
    }

    // 소형(일반 등급 + 가벼운 체급)만 밀어낸다. 피해는 주지 않는다.
    private static void KnockbackSmall(Vector3 center, float radius, ICollection<EnemyActor> exclude)
    {
        ActiveScratch.Clear();
        Candidates.Clear();
        EnemyRank.CollectActive(ActiveScratch);
        float radiusSqr = radius * radius;
        for (int i = 0; i < ActiveScratch.Count; i++)
        {
            EnemyRank rank = ActiveScratch[i];
            if (rank == null || rank.Rank != EnemyRankType.Normal || rank.GradeType != EnemyGradeType.Normal) continue;
            Vector3 delta = rank.transform.position - center; delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            if (sqr > radiusSqr) continue;
            EnemyActor enemy = rank.GetComponent<EnemyActor>();
            if (enemy == null || !enemy.IsLeased || enemy.Health == null || enemy.Health.IsDead
                || (exclude != null && exclude.Contains(enemy))) continue;
            EnemyMovementReaction reaction = enemy.GetComponent<EnemyMovementReaction>();
            if (reaction == null || reaction.HitWeightProfile != null
                && reaction.HitWeightProfile.Weight != EnemyHitWeight.Light) continue;
            Candidates.Add(new KeyValuePair<float, EnemyActor>(sqr, enemy));
        }
        if (Candidates.Count > KnockbackCap) Candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
        int count = Mathf.Min(Candidates.Count, KnockbackCap);
        for (int i = 0; i < count; i++)
        {
            EnemyActor enemy = Candidates[i].Value;
            float distance = Mathf.Sqrt(Candidates[i].Key);
            Vector3 direction = enemy.transform.position - center; direction.y = 0f;
            if (direction.sqrMagnitude < .0001f) direction = enemy.transform.forward * -1f;
            EnemyMovementReaction reaction = enemy.GetComponent<EnemyMovementReaction>();
            enemy.AbilityController?.Cancel();
            reaction.ApplyKnockbackDistance(direction,
                Mathf.Lerp(KnockbackNear, KnockbackFar, Mathf.Clamp01(distance / Mathf.Max(.01f, radius))), false);
            reaction.ExtendKnockbackReaction(KnockbackStagger, false);
            enemy.AnimationBridge?.PlayHit();
            enemy.GetComponent<HitFlashFeedback>()?.FlashOnce();
        }
        ActiveScratch.Clear();
        Candidates.Clear();
    }

    private void PlayLayers(Vector3 position, ParryGrade grade, int parriedCount)
    {
        LastAdditionalTingCount = 0;
        // The main .9 ting is played by CombatActionSfxService once per player action.
        if (grade != ParryGrade.Incomplete && tingClip != null)
        {
            PlayVoice(tingClip, position, .6f, parriedCount >= 2 ? 1.06f : 1f);
            LastAdditionalTingCount++;
            if (grade == ParryGrade.Perfect)
            {
                PlayVoice(tingClip, position, .5f, 1f);
                LastAdditionalTingCount++;
            }
        }
        if (grade != ParryGrade.Incomplete && thumpClip != null)
            PlayVoice(thumpClip, position, grade == ParryGrade.Perfect ? .6f : .45f, .85f);
    }

    private void PlayVoice(AudioClip clip, Vector3 position, float volume, float pitch)
    {
        AudioSource source = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        source.transform.position = position;
        source.Stop();
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.Play();
    }

    private static void RequestCamera(Vector3 direction, float amplitude, float duration)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < .0001f) direction = Vector3.forward;
        QuarterViewCamera.ActiveInstance?.RequestCombatImpact(
            CombatCameraRequestKind.AttackHit, direction.normalized, Vector3.zero, false,
            duration, amplitude, amplitude * 4f, .75f, .06f, .2f,
            2.5f, amplitude * 1.8f, 4f);
    }
}
