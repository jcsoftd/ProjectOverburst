using System.Collections.Generic;
using UnityEngine;

// 60D light heavy follow-up hits. Owns them independently of the weapon action so a cancelled heavy still finishes.
// Timing uses scaled time without a per-frame clamp so the hits land on the same frames as the pooled VFX particles.
public sealed class LightTripleImpactScheduler : MonoBehaviour
{
    private struct Pending
    {
        public GameObject Source;
        public CombatTeam Team;
        public Vector3 Center;
        public float Radius, Damage, VerticalTolerance, Due, SfxEnergy;
        public int HitIndex;
        public bool SparkleOnly; // 소리만: 마지막 타 뒤 반짝임
    }

    private static LightTripleImpactScheduler instance;
    private readonly List<Pending> pending = new List<Pending>(8);
    private readonly List<CombatTarget> candidates = new List<CombatTarget>(64);
    private readonly HashSet<int> visited = new HashSet<int>();
    private float clock;

    public static int PendingCount => instance != null ? instance.pending.Count : 0;
    public static int DispatchedHitCount { get; private set; }
    public static int LastDispatchTargetCount { get; private set; }
    public static int LastDispatchHitIndex { get; private set; } = -1;
    public static float LastDispatchDamage { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        DispatchedHitCount = 0;
        LastDispatchTargetCount = 0;
        LastDispatchHitIndex = -1;
        LastDispatchDamage = 0f;
    }

    public static void Submit(GameObject source, CombatTeam team, Vector3 center, float radius, float damage,
        float verticalTolerance, float delay, int hitIndex, float sfxEnergy = MeleeElementSfxService.FullVolumeEnergy)
    {
        if (source == null || radius <= 0f || !(damage > 0f)) return;
        EnsureInstance();
        instance.pending.Add(new Pending
        {
            Source = source, Team = team, Center = center, Radius = radius, Damage = damage,
            VerticalTolerance = Mathf.Max(0f, verticalTolerance), Due = instance.clock + Mathf.Max(0f, delay),
            HitIndex = hitIndex, SfxEnergy = sfxEnergy
        });
    }

    // Seconds after the slam at which VFX hit N lands, given the variant's shift and playback speed.
    public static float ResolveDelay(int hitIndex, int slamHitIndex)
    {
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float shifted = tuning.LightTripleSourceHitTime(hitIndex) - tuning.LightTripleSourceHitTime(slamHitIndex);
        return Mathf.Max(0f, shifted) / tuning.SafeLightTripleVfxPlaybackSpeed;
    }

    public static void ClearAll()
    {
        if (instance != null) instance.pending.Clear();
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        var root = new GameObject("LightTripleImpactScheduler");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<LightTripleImpactScheduler>();
    }

    private void Update()
    {
        if (pending.Count == 0) return;
        clock += Mathf.Max(0f, Time.deltaTime);
        for (int i = 0; i < pending.Count;)
        {
            Pending item = pending[i];
            if (item.Due > clock + 0.000001f) { i++; continue; }
            pending.RemoveAt(i);
            Dispatch(item);
        }
    }

    private void Dispatch(Pending item)
    {
        if (item.SparkleOnly)
        {
            MeleeElementSfxService.TryPlayUpperHeavy(UpperHeavySfxStage.LightSparkle, item.Center, energy: item.SfxEnergy);
            return;
        }
        // N타 소리는 VFX와 같은 프레임에 한 번. 마지막 타(2) 뒤에는 반짝임을 예약한다.
        MeleeElementSfxService.TryPlayLightHeavyHit(item.HitIndex, item.Center, energy: item.SfxEnergy);
        if (item.HitIndex >= 2)
            pending.Add(new Pending { Center = item.Center, Due = clock + MeleeElementSfxService.LightSparkleDelay, SparkleOnly = true, SfxEnergy = item.SfxEnergy });
        // 2타·마지막 타: 충격파 + 카메라 흔들림(마지막 타가 더 세게). 내려치기(1타)는 MeleeHeavyDischargeExecutor가 낸다.
        UpperHeavyImpactFeedback.PlayShockwave(item.Center, item.Radius);
        if (item.Source != null)
            UpperHeavyImpactFeedback.RequestCamera(item.Source.transform.position, item.Center, item.HitIndex >= 2 ? 0.75f : 0.5f);
        using var costScope = ElementCombatCostMarkers.Light_TripleImpact_Dispatch.Auto();
        if (item.Source == null) return;
        CombatTargetRegistry.CollectPotentialTargets(item.Center, item.Radius, candidates);
        visited.Clear();
        int hits = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (!UpperElementCombatUtility.IsValidEnemy(target, item.Team)
                || !UpperElementCombatUtility.IsInRadius(target, item.Center, item.Radius)
                || !UpperElementCombatUtility.IsInHeight(target, item.Center, item.VerticalTolerance)) continue;
            CombatHealth health = target.DamageReceiver;
            if (!visited.Add(health.GetInstanceID())) continue;
            Vector3 point = target.WorldCenter;
            Vector3 direction = point - item.Center;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            UpperElementCombatUtility.DealDerivedDamage(health, item.Damage, point, item.Source, direction, WeaponElement.Light);
            MeleeElementHitVfxService.TryPlay(WeaponElement.Light, point);
            hits++;
            if (item.Source == null) break; // A lethal reward may have torn down the source.
        }
        candidates.Clear();
        visited.Clear();
        DispatchedHitCount++;
        LastDispatchTargetCount = hits;
        LastDispatchHitIndex = item.HitIndex;
        LastDispatchDamage = item.Damage;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
