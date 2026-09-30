using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 패링 성공 한 번(같은 프레임의 다수 패링 포함)의 공용 피드백.
// 파동은 패링 중심에서만 내고, 그 파동이 주변 소형 몬스터를 밀어내고 경직시킨다.
// 주 패링음(팅)은 CombatActionSfxService 소유를 유지하고, 여기서는 겹침·저음 레이어만 낸다.
public sealed class ParryFeedbackService : MonoBehaviour
{
    public struct Tier
    {
        public float HitStop, Slow, WaveScale, WaveIntensity, KnockbackRadius, CameraAmplitude, Zoom;
        public bool SecondWave;
    }

    // 0: 1마리, 1: 2~3마리, 2: 4마리 이상. 강공 패링이 섞이면 한 단계 올린다.
    // 2026-10-01 사용자 조정: 카메라 흔들림 2배(.045/.06/.075 -> .09/.12/.15).
    // 2차 조정: Slow = 히트스톱 뒤 .15배로 버티는 실제 시간(그 뒤 .35초 복귀는 PlayerParryController),
    // Zoom = 패링 순간 화면 확대 비율(7/9/11%). 확대는 Slow가 끝날 때까지 유지하고 .4초 동안 돌아온다.
    private static readonly Tier[] Tiers =
    {
        new Tier { HitStop = .07f, Slow = .20f, WaveScale = .50f, WaveIntensity = .8f, KnockbackRadius = 2.0f, CameraAmplitude = .09f, Zoom = .07f },
        new Tier { HitStop = .08f, Slow = .24f, WaveScale = .65f, WaveIntensity = 1.0f, KnockbackRadius = 2.5f, CameraAmplitude = .12f, Zoom = .09f },
        new Tier { HitStop = .10f, Slow = .30f, WaveScale = .80f, WaveIntensity = 1.2f, KnockbackRadius = 3.0f, CameraAmplitude = .15f, Zoom = .11f, SecondWave = true },
    };

    private const float ShockwaveBaseScale = 3.35f;      // DF_GRS_CircleShockwave baseScale
    private const float ShockwaveAuthoredLifetime = .6818182f;
    private const float ShockwaveSpeed = 1.4f;
    private const float ShockwaveHeight = .85f;          // DF_GRS_CircleShockwave localPositionOffset.y
    private const int ShockwavePoolCapacity = 8;
    private const int KnockbackCap = 24;
    private const float KnockbackNear = 2.2f, KnockbackFar = 1.2f, KnockbackStagger = .45f;
    private const string SfxRoot = "Combat/SFX/CombatAction/";
    private const int VoiceCount = 4;
    private const float ZoomIn = .06f, ZoomOut = .4f;

    private static ParryFeedbackService instance;
    private static EnemyTelegraphVisualLibrary library;
    private static readonly List<EnemyRank> ActiveScratch = new List<EnemyRank>(160);
    private static readonly List<KeyValuePair<float, EnemyActor>> Candidates = new List<KeyValuePair<float, EnemyActor>>(64);
    private readonly AudioSource[] voices = new AudioSource[VoiceCount];
    private int nextVoice;
    private AudioClip tingClip, thumpClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; library = null; }

    public static Tier ResolveTier(int parriedCount, bool anyStrong)
    {
        int index = parriedCount >= 4 ? 2 : parriedCount >= 2 ? 1 : 0;
        if (anyStrong) index = Mathf.Min(index + 1, Tiers.Length - 1);
        return Tiers[index];
    }

    // chainIndex: 같은 패링 창에서 몇 번째 패링인지(0부터). 음높이를 한 단계씩 올린다.
    public static void Play(Vector3 center, Vector3 playerPosition, Tier tier, int parriedCount,
        int chainIndex, ICollection<EnemyActor> parried)
    {
        if (!Application.isPlaying || !EnsureInstance()) return;
        instance.SpawnWave(center, tier.WaveScale, tier.WaveIntensity);
        if (tier.SecondWave) instance.StartCoroutine(instance.SecondWave(center, tier.WaveScale));
        KnockbackSmall(center, tier.KnockbackRadius, parried);
        instance.PlayLayers(center, parriedCount, chainIndex);
        RequestCamera(center - playerPosition, tier.CameraAmplitude);
        QuarterViewCamera.ActiveInstance?.RequestZoomPunch(tier.Zoom, ZoomIn, tier.HitStop + tier.Slow, ZoomOut);
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

    private void SpawnWave(Vector3 center, float scaleMultiplier, float intensity)
    {
        if (library == null)
            library = Resources.Load<EnemyTelegraphVisualLibrary>("Enemies/Balance/EnemyTelegraphVisualLibrary");
        GameObject prefab = library != null ? library.ParryShockwave : null;
        if (prefab == null) return;
        Vector3 position = ProbeGround(center) + Vector3.up * ShockwaveHeight;
        float scale = ShockwaveBaseScale * scaleMultiplier;
        float lifetime = SwordShockwavePlayback.ResolveCueLifetime(prefab, ShockwaveAuthoredLifetime, ShockwaveSpeed);
        TransientVfxPool.Spawn(prefab, position, Quaternion.identity, lifetime, ShockwavePoolCapacity, null,
            wave =>
            {
                wave.transform.localScale = Vector3.one * scale;
                wave.GetComponent<SwordShockwavePlayback>()?.Configure(intensity, ShockwaveSpeed);
            });
    }

    private IEnumerator SecondWave(Vector3 center, float scaleMultiplier)
    {
        yield return new WaitForSecondsRealtime(.08f);
        SpawnWave(center, scaleMultiplier, .8f);
    }

    private static Vector3 ProbeGround(Vector3 origin)
    {
        int mask = Physics.DefaultRaycastLayers;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0) mask &= ~(1 << enemyLayer);
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);
        return Physics.Raycast(origin + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 5f, mask,
            QueryTriggerInteraction.Ignore) ? hit.point : new Vector3(origin.x, origin.y - 1f, origin.z);
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

    private void PlayLayers(Vector3 position, int parriedCount, int chainIndex)
    {
        // 여러 마리이거나 창 안 연속 패링이면 팅을 한 겹 더, 음높이를 한 단계씩 올려 겹친다.
        if (tingClip != null && (parriedCount >= 2 || chainIndex > 0))
            PlayVoice(tingClip, position, .6f, Mathf.Min(1.18f, 1f + .06f * Mathf.Max(1, chainIndex + (parriedCount >= 2 ? 1 : 0))));
        if (thumpClip != null)
            PlayVoice(thumpClip, position, parriedCount >= 4 ? .7f : .45f, .85f);
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

    private static void RequestCamera(Vector3 direction, float amplitude)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < .0001f) direction = Vector3.forward;
        QuarterViewCamera.ActiveInstance?.RequestCombatImpact(
            CombatCameraRequestKind.AttackHit, direction.normalized, Vector3.zero, false,
            .2f, amplitude, amplitude * 4f, .75f, .06f, .2f, // 2026-10-01: 길이 .12->.2초, 기울기 1.5->4배로 더 티 나게
            2.5f, amplitude * 1.8f, 4f);
    }
}
