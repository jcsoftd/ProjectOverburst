using System.Collections.Generic;
using UnityEngine;

// 60D 4 (2026-10-02): confirmed slam hits consume corrosion immediately. Their stack sum becomes one
// shared ammo count, fired in small volleys at shuffled enemies visible when the slam was committed.
// Flight speed and curves are unchanged. The barrage outlives the weapon action; derived hits only add
// the existing monster hit sound and never charge energy or apply corrosion.
public sealed class DarkBarrageScheduler : MonoBehaviour
{
    private struct Entry
    {
        public CombatTarget Target;
        public CombatHealth Health;
        public ElementalStatusController Status;
        public int Life;
        public int Hits;
    }

    private enum ShotState : byte { Waiting, Rising, Hovering, Homing, Done }

    private struct Shot
    {
        public int Entry;
        public int Volley;
        public bool Finisher;
        public ShotState State;
        public float RiseAt, ReleaseAt; // cast clock
        public float PhaseStart, PhaseDuration, ReleasedAt; // Time.time while homing
        public float BobPhase;
        public Vector3 From, Hover, Apex, Control, Control2Offset, Position;
        public GameObject Projectile, Trail;
    }

    private sealed class Cast
    {
        public GameObject Source;
        public CombatTeam Team;
        public Vector3 Center;
        public float SearchRadius, VerticalTolerance, ShotDamage, Interval, RiseTime, Clock;
        public int Remaining, StartFrame;
        public int Id, StackSum, ShotsPerVolley, DeckCursor, LastPicked = -1;
        public int CurrentVolley = -1;
        public bool StopLaunching, FinisherAnnounced;
        public MeleeHeavyElementVfxSet Vfx;
        public readonly List<Entry> Entries = new List<Entry>(40);
        public readonly List<Shot> Shots = new List<Shot>(240);
        public readonly List<int> Deck = new List<int>(128);
        public readonly HashSet<int> Donors = new HashSet<int>();
        public readonly HashSet<int> VolleyTargets = new HashSet<int>();
        public void Clear()
        {
            Source = null; Entries.Clear(); Shots.Clear(); Deck.Clear(); Donors.Clear(); VolleyTargets.Clear(); Clock = 0f; Remaining = 0;
            StackSum = DeckCursor = 0; LastPicked = -1;
            CurrentVolley = -1;
            StopLaunching = FinisherAnnounced = false; Vfx = default;
        }
    }

    private struct Lingering { public GameObject Instance; public GameObject Prefab; public float ReleaseAt; }

    private static DarkBarrageScheduler instance;
    private static readonly int BlockMask = 1 << 0; // Default: level geometry; enemies/player live on their own layers.
    private const float TrailLinger = 0.6f;
    private const float HitVfxWindow = 0.5f;
    private const float BobHeight = 0.08f;

    private readonly List<Cast> active = new List<Cast>(4);
    private readonly Stack<Cast> free = new Stack<Cast>(4);
    private readonly Dictionary<int, Cast> pending = new Dictionary<int, Cast>();
    private readonly Plane[] screenPlanes = new Plane[6];
    private int nextCastId;
    private GameObject feedbackSource;
    private Vector3 feedbackPoint, feedbackDirection;
    private bool damageConfirmed;
    private readonly List<CombatTarget> candidates = new List<CombatTarget>(96);
    private readonly HashSet<int> visited = new HashSet<int>();
    private readonly Dictionary<GameObject, Stack<GameObject>> viewPool = new Dictionary<GameObject, Stack<GameObject>>();
    private readonly Dictionary<GameObject, ParticleSystem[]> viewParticles = new Dictionary<GameObject, ParticleSystem[]>();
    private readonly Dictionary<GameObject, TrailRenderer[]> viewTrails = new Dictionary<GameObject, TrailRenderer[]>();
    private readonly Dictionary<GameObject, GameObject> viewPrefab = new Dictionary<GameObject, GameObject>();
    private readonly List<Lingering> lingering = new List<Lingering>(64);
    private readonly Queue<float> hitVfxTimes = new Queue<float>(64);
    private Transform viewRoot;
    private int liveProjectileViews;
    private float lastLaunchSfx = -1f, lastHitSfx = -1f, lastFinisherHitSfx = -1f;

    public static int ActiveCount => instance != null ? instance.active.Count : 0;
    public static int CastCount { get; private set; }
    public static int LastTargetCount { get; private set; }
    public static int LastShotCount { get; private set; }
    public static int LastStackSum { get; private set; }
    public static bool LastFinisher { get; private set; }
    public static float LastInterval { get; private set; }
    public static float LastSearchRadius { get; private set; }
    public static Vector3 LastCenter { get; private set; }
    public static float LastShotDamage { get; private set; }
    public static int TotalLaunched { get; private set; }
    public static int TotalHits { get; private set; }
    public static int TotalRetargets { get; private set; }
    public static int TotalCommonHitSfx { get; private set; }
    public static int LastDonorCount { get; private set; }
    public static int LastVolleySize { get; private set; }
    public static int TotalFizzles { get; private set; }
    public static int MaxLaunchedInOneFrame { get; private set; }
    public static float LastFirstLaunchTime { get; private set; } = -1f;
    public static float LastFinalLaunchTime { get; private set; } = -1f;
    public static float LastFinalHitTime { get; private set; } = -1f;
    // Instance ids of the target of every shot of the latest barrage, in release order (validation).
    public static readonly List<int> LastLaunchTargets = new List<int>(240);
    public static readonly List<float> LastLaunchTimes = new List<float>(240);
#if UNITY_EDITOR
    public static bool DebugDraw = true; // Scene view lines while the projectile VFX slots are still empty.
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        CastCount = LastTargetCount = LastShotCount = LastStackSum = 0;
        TotalLaunched = TotalHits = TotalRetargets = TotalFizzles = MaxLaunchedInOneFrame = 0;
        TotalCommonHitSfx = LastDonorCount = LastVolleySize = 0;
        LastFinisher = false;
        LastInterval = LastSearchRadius = LastShotDamage = 0f;
        LastCenter = Vector3.zero;
        LastFirstLaunchTime = LastFinalLaunchTime = LastFinalHitTime = -1f;
        LastLaunchTargets.Clear();
        LastLaunchTimes.Clear();
    }

    // Freeze the screen targets before direct damage can kill/pool them. Ammo is supplied only by confirmed hits.
    public static int PrepareSlam(OverburstElementDischarge discharge, GameObject source, CombatTeam team,
        Vector3 center, float slamRadius, float verticalTolerance, MeleeHeavyElementVfxSet vfx)
    {
        if (discharge == null || source == null || discharge.Element != WeaponElement.Dark
            || !(discharge.Energy > 0f) || slamRadius <= 0f) return 0;
        EnsureInstance();
        return instance.Prepare(discharge, source, team, center, verticalTolerance, vfx);
    }

    public static void ConfirmSlamHit(int id, int donorId, int stacks, ElementalStatusController status, int life, bool lethal)
    {
        if (instance == null || !instance.pending.TryGetValue(id, out Cast cast) || stacks <= 0
            || !cast.Donors.Add(donorId)) return;
        // Lethal direct hits may already have cleared the status/pool lifecycle. The pre-hit snapshot still counts.
        if (!lethal && status != null && status.LifecycleVersion == life)
            stacks = status.ConsumeForDischarge(WeaponElement.Dark, out _);
        cast.StackSum += Mathf.Max(0, stacks);
    }

    public static void CompleteSlam(int id)
    {
        if (instance != null && instance.pending.TryGetValue(id, out Cast cast))
        {
            instance.pending.Remove(id);
            instance.Begin(cast);
        }
    }

    public static void ClearAll()
    {
        if (instance == null) return;
        for (int i = instance.active.Count - 1; i >= 0; i--) instance.Retire(i);
        foreach (Cast cast in instance.pending.Values) { cast.Clear(); instance.free.Push(cast); }
        instance.pending.Clear();
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        var root = new GameObject("DarkBarrageScheduler");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<DarkBarrageScheduler>();
        instance.viewRoot = new GameObject("Views").transform;
        instance.viewRoot.SetParent(root.transform, false);
    }

    private int Prepare(OverburstElementDischarge discharge, GameObject source, CombatTeam team,
        Vector3 center, float verticalTolerance, MeleeHeavyElementVfxSet vfx)
    {
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        Cast cast = free.Count > 0 ? free.Pop() : new Cast();
        cast.Clear();
        cast.Source = source;
        cast.Team = team;
        cast.Center = center;
        cast.Vfx = vfx;
        cast.StartFrame = Time.frameCount;
        cast.VerticalTolerance = Mathf.Max(0f, verticalTolerance);
        float damageBonus = 1f + FlaskCombatModifiers.Bonus(source, FlaskEffect.DarkBurstDamage);
        cast.ShotDamage = discharge.FirstBlastDamage * tuning.SafeDarkBarrageShotDamage * damageBonus;
        cast.Interval = tuning.SafeDarkBarrageFireInterval;
        cast.ShotsPerVolley = tuning.SafeDarkBarrageShotsPerVolley;
        cast.RiseTime = tuning.SafeDarkBarrageRiseTime;
        CollectScreenTargets(cast);
        cast.Id = ++nextCastId;
        pending.Add(cast.Id, cast);
        return cast.Id;
    }

    private void Begin(Cast cast)
    {
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        BuildShots(cast);
        PlanShots(cast, tuning);
        cast.Remaining = cast.Shots.Count;
        LastTargetCount = cast.Entries.Count;
        LastShotCount = cast.Shots.Count;
        LastStackSum = cast.StackSum;
        LastDonorCount = cast.Donors.Count;
        LastVolleySize = cast.ShotsPerVolley;
        LastFinisher = false;
        LastInterval = cast.Interval;
        LastSearchRadius = cast.SearchRadius;
        LastCenter = cast.Center;
        LastShotDamage = cast.ShotDamage;
        LastFirstLaunchTime = LastFinalLaunchTime = LastFinalHitTime = -1f;
        LastLaunchTargets.Clear();
        LastLaunchTimes.Clear();
        if (cast.Shots.Count == 0) { cast.Clear(); free.Push(cast); return; }
        int launching = 0;
        for (int i = 0; i < active.Count; i++) if (!active[i].StopLaunching) launching++;
        for (int i = 0; i < active.Count && launching >= tuning.SafeDarkBarrageMaxConcurrent; i++)
        {
            if (active[i].StopLaunching) continue;
            active[i].StopLaunching = true;
            launching--;
        }
        CastCount++;
        active.Add(cast);
    }

    private void CollectScreenTargets(Cast cast)
    {
        using var costScope = ElementCombatCostMarkers.Dark_Barrage_Collect.Auto();
        Camera camera = Camera.main;
        if (camera == null) return;
        GeometryUtility.CalculateFrustumPlanes(camera, screenPlanes);
        CombatTargetRegistry.CollectTeamTargets(cast.Team == CombatTeam.Enemy ? CombatTeam.PlayerParty : CombatTeam.Enemy, candidates);
        visited.Clear();
        Vector3 eye = cast.Center + Vector3.up * 0.6f;
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (!UpperElementCombatUtility.IsValidEnemy(target, cast.Team)
                || !visited.Add(target.DamageReceiver.GetInstanceID())) continue;
            CombatTargetVolume volume = target.CurrentHurtVolume;
            var bounds = new Bounds(volume.Center, new Vector3(volume.Radius * 2f, volume.HalfHeight * 2f, volume.Radius * 2f));
            if (!GeometryUtility.TestPlanesAABB(screenPlanes, bounds)) continue;
            var status = target.GetComponent<ElementalStatusController>();
            Vector3 targetEye = target.WorldCenter;
            if (Physics.Linecast(eye, new Vector3(targetEye.x, eye.y, targetEye.z), BlockMask, QueryTriggerInteraction.Ignore)) continue;
            cast.Entries.Add(new Entry { Target = target, Health = target.DamageReceiver, Status = status,
                Life = status != null ? status.LifecycleVersion : 0 });
            cast.SearchRadius = Mathf.Max(cast.SearchRadius, Vector3.Distance(cast.Center, volume.Center));
        }
        candidates.Clear();
        visited.Clear();
    }

    private static void BuildShots(Cast cast)
    {
        for (int i = 0; i < cast.StackSum; i++) cast.Shots.Add(new Shot { Entry = -1 });
    }

    // Every shot rises into the same hover disc; small volleys leave at a fixed interval.
    private static void PlanShots(Cast cast, OverburstElementTuning tuning)
    {
        float stagger = tuning.SafeDarkBarrageRiseStagger;
        float firstRelease = cast.RiseTime + stagger;
        float radius = tuning.SafeDarkBarrageHoverRadius;
        Vector3 origin = cast.Center + Vector3.up * 0.5f;
        for (int i = 0; i < cast.Shots.Count; i++)
        {
            Shot shot = cast.Shots[i];
            Vector2 disc = Random.insideUnitCircle * radius;
            float height = Random.Range(tuning.SafeDarkBarrageRiseHeightMin, tuning.SafeDarkBarrageRiseHeightMax);
            shot.State = ShotState.Waiting;
            shot.From = shot.Position = origin;
            shot.Hover = cast.Center + new Vector3(disc.x, height, disc.y);
            shot.RiseAt = Random.Range(0f, stagger);
            shot.ReleaseAt = firstRelease + (i / cast.ShotsPerVolley) * cast.Interval;
            shot.Volley = i / cast.ShotsPerVolley;
            shot.BobPhase = Random.value * Mathf.PI * 2f;
            cast.Shots[i] = shot;
        }
    }

    private void Update()
    {
        if (active.Count == 0 && lingering.Count == 0) return;
        using var costScope = ElementCombatCostMarkers.Dark_Barrage_Tick.Auto();
        float dt = Mathf.Max(0f, Time.deltaTime);
        float now = Time.time;
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        int launchBudget = tuning.SafeDarkBarrageMaxLaunchPerFrame;
        int launchedThisFrame = 0;
        bool hitThisFrame = false, finisherHitThisFrame = false;
        Vector3 lastHitPoint = Vector3.zero;
        for (int c = active.Count - 1; c >= 0; c--)
        {
            Cast cast = active[c];
            if (cast.Source == null) { Retire(c); continue; }
            // The submit frame's delta belongs to the wind-up, not to the barrage.
            if (Time.frameCount != cast.StartFrame) cast.Clock += dt;
            for (int i = 0; i < cast.Shots.Count; i++)
            {
                Shot shot = cast.Shots[i];
                if (shot.State == ShotState.Done) continue;
                Vector3 previous = shot.Position;
                if (shot.State != ShotState.Homing)
                {
                    if (cast.StopLaunching) { Finish(cast, ref shot, now); cast.Shots[i] = shot; continue; }
                    AdvanceInAir(cast, ref shot, tuning);
                    if (shot.State != ShotState.Waiting && cast.Clock >= shot.ReleaseAt && launchBudget > 0)
                    {
                        launchBudget--;
                        launchedThisFrame++;
                        if (!Release(cast, ref shot, now, tuning)) { Finish(cast, ref shot, now); cast.Shots[i] = shot; continue; }
                    }
                    MoveView(ref shot, previous);
                    cast.Shots[i] = shot;
                    continue;
                }
                if (Home(cast, ref shot, now, tuning))
                {
                    if (Arrive(cast, ref shot, now, tuning))
                    {
                        hitThisFrame |= !shot.Finisher;
                        finisherHitThisFrame |= shot.Finisher;
                        lastHitPoint = shot.Position;
                    }
                    Finish(cast, ref shot, now);
                }
                else MoveView(ref shot, previous);
                cast.Shots[i] = shot;
            }
            if (cast.Remaining <= 0) Retire(c);
        }
        if (launchedThisFrame > MaxLaunchedInOneFrame) MaxLaunchedInOneFrame = launchedThisFrame;
        float sfxGap = tuning.SafeDarkBarrageSfxMinInterval;
        if (hitThisFrame && now - lastHitSfx >= sfxGap)
        {
            lastHitSfx = now;
            MeleeElementSfxService.TryPlayUpperHeavy(UpperHeavySfxStage.DarkBarrageHit, lastHitPoint);
        }
        if (finisherHitThisFrame && now - lastFinisherHitSfx >= sfxGap)
        {
            lastFinisherHitSfx = now;
            MeleeElementSfxService.TryPlayUpperHeavy(UpperHeavySfxStage.DarkBarrageFinisherHit, lastHitPoint);
        }
        ReleaseLingering(now);
    }

    private void Finish(Cast cast, ref Shot shot, float now)
    {
        HideProjectile(ref shot, now);
        shot.State = ShotState.Done;
        cast.Remaining--;
    }

    // Waiting -> rising to the hover disc -> hovering with a slight bob until its release time.
    private void AdvanceInAir(Cast cast, ref Shot shot, OverburstElementTuning tuning)
    {
        if (shot.State == ShotState.Waiting)
        {
            if (cast.Clock < shot.RiseAt) return;
            shot.State = ShotState.Rising;
            ShowProjectile(cast, ref shot, tuning);
        }
        if (shot.State == ShotState.Rising)
        {
            float t = Mathf.Clamp01((cast.Clock - shot.RiseAt) / Mathf.Max(0.0001f, cast.RiseTime));
            float eased = 1f - (1f - t) * (1f - t);
            shot.Position = Vector3.LerpUnclamped(shot.From, shot.Hover, eased);
            if (t >= 1f) shot.State = ShotState.Hovering;
            return;
        }
        shot.Position = shot.Hover + Vector3.up * (Mathf.Sin(cast.Clock * 6f + shot.BobPhase) * BobHeight);
    }

    // Leaves the hover toward its target. False when no enemy is left to aim at.
    private bool Release(Cast cast, ref Shot shot, float now, OverburstElementTuning tuning)
    {
        if (cast.CurrentVolley != shot.Volley) { cast.CurrentVolley = shot.Volley; cast.VolleyTargets.Clear(); }
        if (!PickTarget(cast, ref shot)) { TotalFizzles++; return false; }
        if (shot.Projectile == null && shot.Trail == null) ShowProjectile(cast, ref shot, tuning);
        Vector3 target = TargetPoint(cast, shot);
        Vector3 toTarget = target - shot.Position;
        // A short upward kick before diving keeps each shot readable as it peels off the cloud.
        Vector3 heading = toTarget.sqrMagnitude > 0.0001f ? (toTarget.normalized + Vector3.up * 0.6f).normalized : Vector3.up;
        BeginHoming(ref shot, shot.Position, heading, target, now, tuning);
        shot.State = ShotState.Homing;
        shot.ReleasedAt = now;
        TotalLaunched++;
        CombatTarget launchTarget = cast.Entries[shot.Entry].Target;
        LastLaunchTargets.Add(launchTarget != null ? launchTarget.GetInstanceID() : 0);
        LastLaunchTimes.Add(now);
        if (LastFirstLaunchTime < 0f) LastFirstLaunchTime = now;
        LastFinalLaunchTime = now;
        if (shot.Finisher && !cast.FinisherAnnounced)
        {
            cast.FinisherAnnounced = true;
            MeleeElementSfxService.TryPlayUpperHeavy(UpperHeavySfxStage.DarkBarrageFinisherLaunch, shot.Position);
            if (cast.Source != null) UpperHeavyImpactFeedback.RequestCamera(cast.Source.transform.position, cast.Center, 1f);
        }
        else if (now - lastLaunchSfx >= tuning.SafeDarkBarrageSfxMinInterval)
        {
            lastLaunchSfx = now;
            MeleeElementSfxService.TryPlayUpperHeavy(UpperHeavySfxStage.DarkBarrageLaunch, shot.Position);
        }
        return true;
    }

    // Returns true when the shot reaches its target this frame.
    private bool Home(Cast cast, ref Shot shot, float now, OverburstElementTuning tuning)
    {
        if (!IsValid(cast.Entries[shot.Entry]))
        {
            Vector3 heading = shot.Position - shot.Apex;
            if (!Retarget(cast, ref shot)) return true;
            BeginHoming(ref shot, shot.Position, heading.sqrMagnitude > 0.000001f ? heading.normalized : Vector3.up,
                TargetPoint(cast, shot), now, tuning);
        }
        float u = Mathf.Clamp01((now - shot.PhaseStart) / Mathf.Max(0.0001f, shot.PhaseDuration));
        Vector3 end = TargetPoint(cast, shot);
        // Cubic curve: the second control rides with the moving target so every shot still lands.
        Vector3 p2 = end + shot.Control2Offset;
        float a = 1f - u;
        shot.Position = a * a * a * shot.Apex + 3f * a * a * u * shot.Control + 3f * a * u * u * p2 + u * u * u * end;
        return u >= 1f || now - shot.ReleasedAt >= tuning.SafeDarkBarrageMaxLifetime;
    }

    // 2026-10-01 user request: shots swoop on random curves instead of flying straight. Two random controls
    // bend the path sideways and up/down by up to darkBarrageCurveAmount x distance (clamped 0.8..3 m).
    private static void BeginHoming(ref Shot shot, Vector3 from, Vector3 heading, Vector3 target, float now,
        OverburstElementTuning tuning)
    {
        Vector3 toTarget = target - from;
        float distance = toTarget.magnitude;
        Vector3 direction = distance > 0.001f ? toTarget / distance : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;
        float bend = Mathf.Clamp(distance * tuning.SafeDarkBarrageCurveAmount, 0.8f, 3f);
        Vector3 Swerve() => side * (Random.Range(-1f, 1f) * bend) + Vector3.up * (Random.Range(-0.3f, 1f) * bend * 0.6f);
        shot.Apex = from;
        shot.Control = from + heading * Mathf.Clamp(distance * 0.3f, 0.5f, 2f) + Swerve();
        shot.Control2Offset = -direction * (distance * 0.3f) + Swerve();
        shot.PhaseStart = now;
        // The bent path is longer than the straight line.
        shot.PhaseDuration = Mathf.Clamp(distance * 1.35f / tuning.SafeDarkBarrageSpeed,
            tuning.SafeDarkBarrageFlightMin, tuning.SafeDarkBarrageFlightMax);
    }

    private static Vector3 TargetPoint(Cast cast, Shot shot)
    {
        CombatTarget target = cast.Entries[shot.Entry].Target;
        return target != null ? target.WorldCenter : shot.Position;
    }

    private static bool IsValid(Entry entry)
    {
        return entry.Target != null && entry.Target.isActiveAndEnabled && entry.Target.IsAlive
            && entry.Health != null && !entry.Health.IsDead
            && (entry.Status == null || entry.Status.LifecycleVersion == entry.Life);
    }

    // Shuffle each pass so a volley spreads over different living enemies before repeating one.
    private bool PickTarget(Cast cast, ref Shot shot, bool distributeVolley = true)
    {
        if (distributeVolley)
        {
            bool hasUnpicked = false;
            for (int i = 0; i < cast.Entries.Count; i++)
                if (IsValid(cast.Entries[i]) && !cast.VolleyTargets.Contains(i)) { hasUnpicked = true; break; }
            if (!hasUnpicked) cast.VolleyTargets.Clear();
        }
        while (true)
        {
            if (cast.DeckCursor >= cast.Deck.Count)
            {
                cast.Deck.Clear(); cast.DeckCursor = 0;
                for (int i = 0; i < cast.Entries.Count; i++) if (IsValid(cast.Entries[i])) cast.Deck.Add(i);
                if (cast.Deck.Count == 0) return false;
                for (int i = cast.Deck.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (cast.Deck[i], cast.Deck[j]) = (cast.Deck[j], cast.Deck[i]);
                }
                if (cast.Deck.Count > 1 && cast.Deck[0] == cast.LastPicked)
                    (cast.Deck[0], cast.Deck[1]) = (cast.Deck[1], cast.Deck[0]);
            }
            int pick = cast.Deck[cast.DeckCursor++];
            if (!IsValid(cast.Entries[pick]) || (distributeVolley && cast.VolleyTargets.Contains(pick))) continue;
            cast.LastPicked = shot.Entry = pick;
            if (distributeVolley) cast.VolleyTargets.Add(pick);
            return true;
        }
    }

    private bool Retarget(Cast cast, ref Shot shot)
    {
        if (!PickTarget(cast, ref shot, false)) { TotalFizzles++; return false; }
        TotalRetargets++;
        return true;
    }

    private bool Arrive(Cast cast, ref Shot shot, float now, OverburstElementTuning tuning)
    {
        using var costScope = ElementCombatCostMarkers.Dark_Barrage_Hit.Auto();
        Entry entry = cast.Entries[shot.Entry];
        if (!IsValid(entry)) return false; // Fizzle already counted by Retarget.
        Vector3 point = entry.Target.WorldCenter;
        shot.Position = point;
        entry.Hits++;
        cast.Entries[shot.Entry] = entry;
        Vector3 direction = point - cast.Center;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        feedbackSource = cast.Source; feedbackPoint = point; feedbackDirection = direction; damageConfirmed = false;
        entry.Health.OnDamageResolved += OnBarrageDamageResolved;
        try { UpperElementCombatUtility.DealDerivedDamage(entry.Health, cast.ShotDamage, point, cast.Source, direction, WeaponElement.Dark); }
        finally { if (entry.Health != null) entry.Health.OnDamageResolved -= OnBarrageDamageResolved; feedbackSource = null; }
        if (!damageConfirmed) return false;
        SpawnHitVfx(cast, point, shot.Finisher, now, tuning);
        TotalHits++;
        LastFinalHitTime = now;
        return true;
    }

    private void OnBarrageDamageResolved(CombatHealth health, DamageInfo info, float actualDamage, bool lethal)
    {
        if (!(actualDamage > 0f) || info.source != feedbackSource || info.triggersOnHitEffects
            || info.element != WeaponElement.Dark || (info.playerAttackKind & PlayerAttackKind.Heavy) != 0) return;
        damageConfirmed = true;
        var request = new CombatHitFeedbackRequest(feedbackSource, 0, null, false, WeaponElement.Dark,
            feedbackPoint, false, worldDirection: feedbackDirection, isLethal: lethal, target: health);
        if (CombatActionSfxService.TryPlayOrganicHit(request, feedbackPoint)) TotalCommonHitSfx++;
    }

    private void Retire(int index)
    {
        Cast cast = active[index];
        active.RemoveAt(index);
        float now = Time.time;
        for (int i = 0; i < cast.Shots.Count; i++)
        {
            Shot shot = cast.Shots[i];
            HideProjectile(ref shot, now);
        }
        cast.Clear();
        free.Push(cast);
    }

    private void SpawnHitVfx(Cast cast, Vector3 point, bool finisher, float now, OverburstElementTuning tuning)
    {
        GameObject prefab = cast.Vfx.darkBarrageHit;
        if (prefab == null) return;
        while (hitVfxTimes.Count > 0 && now - hitVfxTimes.Peek() > HitVfxWindow) hitVfxTimes.Dequeue();
        int cap = tuning.SafeDarkBarrageHitVfxCap;
        if (hitVfxTimes.Count >= cap) return; // Damage still lands; only the burst is skipped.
        hitVfxTimes.Enqueue(now);
        float scale = finisher ? tuning.SafeDarkBarrageFinisherHitScale : 1f;
        TransientVfxPool.Spawn(prefab, point, Quaternion.identity, 0f, cap,
            prepareBeforeActivation: spawned => spawned.transform.localScale = prefab.transform.localScale * scale,
            returnMode: TransientVfxReturnMode.NaturalParticleCompletion);
    }

    // Projectile and trail views: the authored prefabs move as they are, from a dedicated pool.
    // Until the user assigns them, a code-built temporary orb + trail stands in (no asset is created).
    private void ShowProjectile(Cast cast, ref Shot shot, OverburstElementTuning tuning)
    {
        if (liveProjectileViews >= tuning.SafeDarkBarrageProjectileVfxCap) return;
        float scale = shot.Finisher ? tuning.SafeDarkBarrageFinisherProjectileScale : 1f;
        GameObject body = cast.Vfx.darkBarrageProjectile, trail = cast.Vfx.darkBarrageTrail;
        if (body == null && trail == null) body = TemporaryProjectile();
        shot.Projectile = AcquireView(body, shot.Position, scale);
        shot.Trail = AcquireView(trail, shot.Position, scale);
        if (shot.Projectile != null || shot.Trail != null) liveProjectileViews++;
    }

    private GameObject temporaryProjectile;
    public static bool UsesTemporaryProjectile(MeleeHeavyElementVfxSet vfx) => vfx.darkBarrageProjectile == null && vfx.darkBarrageTrail == null;

    // Temporary stand-in: dark crimson orb with a fading black-red trail. Kept inactive as a clone template.
    private GameObject TemporaryProjectile()
    {
        if (temporaryProjectile != null) return temporaryProjectile;
        var root = new GameObject("TEMP_DarkBarrageProjectile");
        root.SetActive(false);
        root.transform.SetParent(viewRoot, false);
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(orb.GetComponent<Collider>()); // clones are made this frame; a deferred Destroy would copy it
        orb.transform.SetParent(root.transform, false);
        orb.transform.localScale = Vector3.one * 0.3f;
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var orbMaterial = new Material(unlit);
        Color core = new Color(0.55f, 0.03f, 0.1f, 1f);
        if (orbMaterial.HasProperty("_BaseColor")) orbMaterial.SetColor("_BaseColor", core);
        if (orbMaterial.HasProperty("_Color")) orbMaterial.SetColor("_Color", core);
        var orbRenderer = orb.GetComponent<MeshRenderer>();
        orbRenderer.sharedMaterial = orbMaterial;
        orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var trail = root.AddComponent<TrailRenderer>();
        trail.time = 0.35f;
        trail.minVertexDistance = 0.05f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.22f), new Keyframe(1f, 0f));
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(0.75f, 0.05f, 0.12f), 0f), new GradientColorKey(new Color(0.08f, 0f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
        trail.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        temporaryProjectile = root;
        return root;
    }

    private void MoveView(ref Shot shot, Vector3 previous)
    {
        Vector3 delta = shot.Position - previous;
        Quaternion rotation = delta.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(delta.normalized) : Quaternion.identity;
        if (shot.Projectile != null) shot.Projectile.transform.SetPositionAndRotation(shot.Position, rotation);
        if (shot.Trail != null) shot.Trail.transform.SetPositionAndRotation(shot.Position, rotation);
#if UNITY_EDITOR
        if (DebugDraw && delta.sqrMagnitude > 0.000001f)
            Debug.DrawLine(previous, shot.Position, shot.Finisher ? Color.red : new Color(.55f, 0f, .1f), 0.25f);
#endif
    }

    private void HideProjectile(ref Shot shot, float now)
    {
        if (shot.Projectile == null && shot.Trail == null) return;
        liveProjectileViews = Mathf.Max(0, liveProjectileViews - 1);
        Linger(shot.Projectile, now);
        Linger(shot.Trail, now);
        shot.Projectile = shot.Trail = null;
    }

    private GameObject AcquireView(GameObject prefab, Vector3 position, float scale)
    {
        if (prefab == null) return null;
        if (!viewPool.TryGetValue(prefab, out Stack<GameObject> pool)) viewPool[prefab] = pool = new Stack<GameObject>(32);
        GameObject view = null;
        while (pool.Count > 0 && view == null) view = pool.Pop();
        if (view == null)
        {
            view = Instantiate(prefab, position, Quaternion.identity, viewRoot);
            view.name = prefab.name;
            viewParticles[view] = view.GetComponentsInChildren<ParticleSystem>(true);
            viewTrails[view] = view.GetComponentsInChildren<TrailRenderer>(true);
            viewPrefab[view] = prefab;
        }
        view.transform.SetPositionAndRotation(position, Quaternion.identity);
        view.transform.localScale = prefab.transform.localScale * scale;
        view.SetActive(true);
        foreach (TrailRenderer trail in viewTrails[view]) { trail.Clear(); trail.emitting = true; }
        foreach (ParticleSystem particle in viewParticles[view]) { particle.Clear(true); particle.Play(true); }
        return view;
    }

    private void Linger(GameObject view, float now)
    {
        if (view == null) return;
        foreach (ParticleSystem particle in viewParticles[view]) particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (TrailRenderer trail in viewTrails[view]) trail.emitting = false;
        lingering.Add(new Lingering { Instance = view, Prefab = viewPrefab[view], ReleaseAt = now + TrailLinger });
    }

    private void ReleaseLingering(float now)
    {
        for (int i = lingering.Count - 1; i >= 0; i--)
        {
            Lingering item = lingering[i];
            if (item.Instance != null && now < item.ReleaseAt) continue;
            lingering.RemoveAt(i);
            if (item.Instance == null) continue;
            item.Instance.SetActive(false);
            viewPool[item.Prefab].Push(item.Instance);
        }
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
