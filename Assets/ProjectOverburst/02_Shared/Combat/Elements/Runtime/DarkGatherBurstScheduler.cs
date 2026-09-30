using System.Collections.Generic;
using UnityEngine;

// 60D dark heavy: after the slam, gather enemies from a wider circle into the slam circle while the
// Demon_Runic_Explotion VFX rises, hold them, then burst the slam circle scaled by its total corrosion.
// Owned independently of the weapon action so a cancelled heavy still finishes.
public sealed class DarkGatherBurstScheduler : MonoBehaviour
{
    private struct Pulled
    {
        public CombatTarget Target;
        public EnemyMovement Motor;
        public ElementalStatusController Status;
        public int Life;
        public float Resistance;
    }

    private sealed class Cast
    {
        public GameObject Source;
        public CombatTeam Team;
        public Vector3 Center;
        public float Radius, GatherRadius, BurstDamage, VerticalTolerance, Clock;
        public float GatherStart, GatherEnd, BurstTime, PullSpeed;
        public float FormTime; // 공중 생성음 시각
        public bool Gathered, FormPlayed;
        public readonly List<Pulled> Targets = new List<Pulled>(48);
        public void Clear() { Source = null; Targets.Clear(); Gathered = false; FormPlayed = false; Clock = 0f; }
    }

    private static DarkGatherBurstScheduler instance;
    private readonly List<Cast> active = new List<Cast>(4);
    private readonly Stack<Cast> free = new Stack<Cast>(4);
    private readonly List<CombatTarget> candidates = new List<CombatTarget>(96);
    private readonly HashSet<int> visited = new HashSet<int>();
    private static readonly int BlockMask = 1 << 0; // Default: level geometry; enemies/player live on their own layers.

    public static int ActiveCount => instance != null ? instance.active.Count : 0;
    public static int LastPulledCount { get; private set; }
    public static int LastBurstTargetCount { get; private set; }
    public static int LastBurstStackSum { get; private set; }
    public static float LastBurstDamage { get; private set; }
    public static int BurstCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        LastPulledCount = LastBurstTargetCount = LastBurstStackSum = BurstCount = 0;
        LastBurstDamage = 0f;
    }

    public static void Submit(OverburstElementDischarge discharge, GameObject source, CombatTeam team,
        Vector3 center, float slamRadius, float verticalTolerance)
    {
        if (discharge == null || source == null || discharge.Element != WeaponElement.Dark
            || !(discharge.Energy > 0f) || slamRadius <= 0f) return;
        EnsureInstance();
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float speed = tuning.SafeDarkVfxPlaybackSpeed;
        Cast cast = instance.free.Count > 0 ? instance.free.Pop() : new Cast();
        cast.Clear();
        cast.Source = source;
        cast.Team = team;
        cast.Center = center;
        cast.Radius = slamRadius;
        cast.GatherRadius = slamRadius * tuning.SafeDarkGatherRadiusMultiplier
            * (1f + FlaskCombatModifiers.Bonus(source, FlaskEffect.DarkGatherRadius));
        cast.BurstDamage = discharge.FirstBlastDamage * (1f + FlaskCombatModifiers.Bonus(source, FlaskEffect.DarkBurstDamage));
        cast.VerticalTolerance = Mathf.Max(0f, verticalTolerance);
        cast.GatherStart = tuning.SafeDarkGatherStart / speed;
        cast.GatherEnd = tuning.SafeDarkGatherEnd / speed;
        cast.BurstTime = tuning.SafeDarkBurstTime / speed;
        cast.FormTime = MeleeElementSfxService.DarkFormDelay / speed;
        float travel = Mathf.Max(0f, cast.GatherRadius - tuning.SafeDarkGatherInnerRadius);
        cast.PullSpeed = travel / Mathf.Max(0.05f, cast.GatherEnd - cast.GatherStart);
        instance.active.Add(cast);
    }

    public static void ClearAll()
    {
        if (instance == null) return;
        for (int i = 0; i < instance.active.Count; i++) { instance.active[i].Clear(); instance.free.Push(instance.active[i]); }
        instance.active.Clear();
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        var root = new GameObject("DarkGatherBurstScheduler");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<DarkGatherBurstScheduler>();
    }

    private void Update()
    {
        if (active.Count == 0) return;
        float dt = Mathf.Max(0f, Time.deltaTime); // Scaled and unclamped: matches the pooled VFX particle clock.
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Cast cast = active[i];
            cast.Clock += dt;
            if (cast.Source == null) { Retire(i); continue; }
            if (!cast.Gathered && cast.Clock >= cast.GatherStart) Gather(cast, tuning);
            if (!cast.FormPlayed && cast.Clock >= cast.FormTime)
            {
                cast.FormPlayed = true;
                MeleeElementSfxService.TryPlayUpperHeavy(UpperHeavySfxStage.DarkForm, cast.Center); // 공중 생성
            }
            if (cast.Clock < cast.BurstTime) continue;
            Burst(cast, tuning);
            Retire(i);
        }
    }

    // The pull runs on the physics step. EnemyMovement consumes at most one 0.25 m area displacement per
    // FixedUpdate, so per-frame requests were clamped or merged and the pull slowed down with the frame rate.
    private void FixedUpdate()
    {
        if (active.Count == 0) return;
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float step = Mathf.Max(0f, Time.fixedDeltaTime);
        for (int i = 0; i < active.Count; i++)
        {
            Cast cast = active[i];
            if (!cast.Gathered || cast.Source == null || cast.Clock >= cast.BurstTime) continue;
            float speed = cast.Clock < cast.GatherEnd ? cast.PullSpeed
                : cast.PullSpeed * tuning.SafeDarkGatherHoldSpeedFraction;
            Pull(cast, speed * step, tuning.SafeDarkGatherInnerRadius);
        }
    }

    private void Retire(int index)
    {
        Cast cast = active[index];
        active.RemoveAt(index);
        cast.Clear();
        free.Push(cast);
    }

    private void Gather(Cast cast, OverburstElementTuning tuning)
    {
        using var costScope = ElementCombatCostMarkers.Dark_Gather_Pull.Auto();
        cast.Gathered = true;
        MeleeElementSfxService.TryPlayUpperHeavy(UpperHeavySfxStage.DarkPull, cast.Center); // 끌어당기기 시작
        cast.Targets.Clear();
        CombatTargetRegistry.CollectPotentialTargets(cast.Center, cast.GatherRadius, candidates);
        int limit = tuning.SafeDarkGatherMaxTargets;
        Vector3 eye = cast.Center + Vector3.up * 0.6f;
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (!UpperElementCombatUtility.IsValidEnemy(target, cast.Team)
                || !UpperElementCombatUtility.IsInRadius(target, cast.Center, cast.GatherRadius)
                || !UpperElementCombatUtility.IsInHeight(target, cast.Center, cast.VerticalTolerance)) continue;
            EnemyMovement motor = target.GetComponent<EnemyMovement>();
            float resistance = OverburstElementTuning.GradeMoveResistance(UpperElementCombatUtility.GradeOf(target));
            if (motor == null || resistance <= 0f) continue;
            Vector3 targetEye = target.WorldCenter;
            if (Physics.Linecast(eye, new Vector3(targetEye.x, eye.y, targetEye.z), BlockMask, QueryTriggerInteraction.Ignore)) continue;
            var status = target.GetComponent<ElementalStatusController>();
            var entry = new Pulled { Target = target, Motor = motor, Status = status,
                Life = status != null ? status.LifecycleVersion : 0, Resistance = resistance };
            if (cast.Targets.Count < limit) { cast.Targets.Add(entry); continue; }
            // Keep the nearest targets, as the previous pull did.
            int farthest = 0;
            float farthestDistance = -1f;
            for (int j = 0; j < cast.Targets.Count; j++)
            {
                float d = UpperElementCombatUtility.PlanarDistance(cast.Targets[j].Target.WorldCenter, cast.Center);
                if (d <= farthestDistance) continue;
                farthest = j;
                farthestDistance = d;
            }
            if (UpperElementCombatUtility.PlanarDistance(target.WorldCenter, cast.Center) < farthestDistance)
                cast.Targets[farthest] = entry;
        }
        candidates.Clear();
        LastPulledCount = cast.Targets.Count;
        ReleaseFromOlderCasts(cast);
    }

    // 60D 4.3: an enemy caught by a newer gather follows only that gather.
    private void ReleaseFromOlderCasts(Cast newest)
    {
        if (active.Count < 2 || newest.Targets.Count == 0) return;
        visited.Clear();
        for (int i = 0; i < newest.Targets.Count; i++)
            if (newest.Targets[i].Target != null) visited.Add(newest.Targets[i].Target.GetInstanceID());
        for (int c = 0; c < active.Count; c++)
        {
            Cast older = active[c];
            if (older == newest) continue;
            for (int i = older.Targets.Count - 1; i >= 0; i--)
            {
                CombatTarget target = older.Targets[i].Target;
                if (target != null && visited.Contains(target.GetInstanceID())) older.Targets.RemoveAt(i);
            }
        }
        visited.Clear();
    }

    private static void Pull(Cast cast, float step, float innerRadius)
    {
        if (step <= 0f) return;
        using var costScope = ElementCombatCostMarkers.Dark_Gather_Pull.Auto();
        for (int i = cast.Targets.Count - 1; i >= 0; i--)
        {
            Pulled pulled = cast.Targets[i];
            bool stale = pulled.Target == null || !pulled.Target.IsAlive || pulled.Motor == null
                || (pulled.Status != null && pulled.Status.LifecycleVersion != pulled.Life);
            if (stale) { cast.Targets.RemoveAt(i); continue; }
            Vector3 toCentre = cast.Center - pulled.Motor.transform.position;
            toCentre.y = 0f;
            float gap = toCentre.magnitude - innerRadius;
            if (gap <= 0f) continue;
            pulled.Motor.RequestAreaDisplacement(toCentre.normalized * Mathf.Min(gap, step * pulled.Resistance));
        }
    }

    private void Burst(Cast cast, OverburstElementTuning tuning)
    {
        using var costScope = ElementCombatCostMarkers.Dark_Gather_Burst.Auto();
        MeleeElementSfxService.TryPlayUpperHeavy(UpperHeavySfxStage.DarkBurst, cast.Center); // 폭발
        CombatTargetRegistry.CollectPotentialTargets(cast.Center, cast.Radius, candidates);
        visited.Clear();
        int stackSum = 0;
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            CombatTarget target = candidates[i];
            if (!UpperElementCombatUtility.IsValidEnemy(target, cast.Team)
                || !UpperElementCombatUtility.IsInRadius(target, cast.Center, cast.Radius)
                || !UpperElementCombatUtility.IsInHeight(target, cast.Center, cast.VerticalTolerance)
                || !visited.Add(target.DamageReceiver.GetInstanceID())) { candidates.RemoveAt(i); continue; }
            var status = target.GetComponent<ElementalStatusController>();
            if (status != null) stackSum += status.GetStackCount(WeaponElement.Dark);
        }
        int counted = Mathf.Min(stackSum, tuning.SafeDarkBurstStackCap);
        float damage = cast.BurstDamage * (tuning.SafeDarkBurstBaseFraction + tuning.SafeDarkBurstPerStack * counted);
        int hits = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (target == null || !target.IsAlive || target.DamageReceiver == null) continue;
            var status = target.GetComponent<ElementalStatusController>();
            status?.ConsumeForDischarge(WeaponElement.Dark, out _);
            Vector3 point = target.WorldCenter;
            Vector3 direction = point - cast.Center;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            UpperElementCombatUtility.DealDerivedDamage(target.DamageReceiver, damage, point, cast.Source, direction, WeaponElement.Dark);
            MeleeElementHitVfxService.TryPlay(WeaponElement.Dark, point);
            hits++;
            if (cast.Source == null) break; // A lethal reward may have torn down the source.
        }
        candidates.Clear();
        visited.Clear();
        LastBurstTargetCount = hits;
        LastBurstStackSum = stackSum;
        LastBurstDamage = damage;
        BurstCount++;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
