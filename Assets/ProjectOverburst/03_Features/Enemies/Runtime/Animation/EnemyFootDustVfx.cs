using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Three shared world-space particle systems. No particle system lives on an enemy.</summary>
[DefaultExecutionOrder(600)]
public sealed class EnemyFootDustVfx : MonoBehaviour
{
    private const string ResourceRoot = "Enemies/FootDust/";
    private static readonly string[] ResourceNames =
    {
        "PF_EnemyFootDust_Light", "PF_EnemyFootDust_Standard", "PF_EnemyFootDust_Heavy"
    };
    private static readonly int[] BurstLimits = { 70, 40, 30 };
    private static readonly float[] Distances = { 12f, 16f, 20f };
    private static readonly int[] CloseCounts = { 3, 5, 7 };
    private static EnemyFootDustVfx instance;
    public static bool Enabled { get; set; } = true;

    private readonly ParticleSystem[] systems = new ParticleSystem[3];
    private readonly int[] burstCounts = new int[3];
    private Camera gameCamera;
    private int frame;
    private int frameBursts;
    private float secondStart;
    private int secondBursts;

    public int EmittedBursts { get; private set; }
    public int BudgetDrops { get; private set; }
    public int VisibilityDrops { get; private set; }
    public int GroundMisses { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic()
    {
        instance = null;
        Enabled = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Prewarm() => GetOrCreate();

    public static bool TryEmit(Vector3 point, Vector3 groundNormal, Vector3 travelDirection,
        EnemyHitWeight weight, float distance, Collider ground)
    {
        if (!Enabled) return false;
        EnemyFootDustVfx service = GetOrCreate();
        return service != null && service.Emit(point, groundNormal, travelDirection,
            weight, distance, ground);
    }

    public static bool IsEligible(Vector3 point, EnemyHitWeight weight, float distance)
    {
        if (!Enabled) return false;
        EnemyFootDustVfx service = GetOrCreate();
        if (service == null) return false;
        int tier = Mathf.Clamp((int)weight, 0, Distances.Length - 1);
        bool eligible = distance <= Distances[tier] && service.IsOnScreen(point);
        if (!eligible) service.VisibilityDrops++;
        return eligible;
    }

    public static void RecordGroundMiss()
    {
        if (instance != null) instance.GroundMisses++;
    }

    // Limit each actor before ground raycasts when many actors share a visual tier.
    public static float RecommendedContactInterval(EnemyHitWeight weight, int nearbyActors)
    {
        int tier = Mathf.Clamp((int)weight, 0, BurstLimits.Length - 1);
        return Mathf.Max(.11f, (float)nearbyActors / BurstLimits[tier]);
    }

    public static float MaxVisibleDistance(EnemyHitWeight weight) =>
        Distances[Mathf.Clamp((int)weight, 0, Distances.Length - 1)];

    private static EnemyFootDustVfx GetOrCreate()
    {
        if (instance != null) return instance;
        instance = FindFirstObjectByType<EnemyFootDustVfx>(FindObjectsInactive.Include);
        if (instance != null) return instance;
        var root = new GameObject("EnemyFootDustVfx", typeof(EnemyFootDustVfx));
        DontDestroyOnLoad(root);
        return root.GetComponent<EnemyFootDustVfx>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem prefab = Resources.Load<ParticleSystem>(ResourceRoot + ResourceNames[i]);
            if (prefab == null)
            {
                Debug.LogError("[EnemyFootDustVfx] Missing " + ResourceNames[i], this);
                continue;
            }
            systems[i] = Instantiate(prefab, transform);
            systems[i].name = ResourceNames[i];
            // EmitParams uses world positions. Keep a small render bound around the
            // camera target instead of leaving the shared emitter at world origin.
            systems[i].GetComponent<ParticleSystemRenderer>().localBounds =
                new Bounds(Vector3.zero, new Vector3(52f, 30f, 52f));
            systems[i].Play(true);
        }
        secondStart = Time.unscaledTime;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void LateUpdate()
    {
        QuarterViewCamera rig = QuarterViewCamera.ActiveInstance;
        if (rig != null && rig.CurrentTarget != null)
            transform.position = rig.CurrentTarget.position;
    }

    private bool Emit(Vector3 point, Vector3 groundNormal, Vector3 travelDirection,
        EnemyHitWeight weight, float distance, Collider ground)
    {
        int tier = Mathf.Clamp((int)weight, 0, systems.Length - 1);
        ParticleSystem system = systems[tier];
        if (system == null) return false;
        if (distance > Distances[tier] || !IsOnScreen(point))
        {
            VisibilityDrops++;
            return false;
        }

        float now = Time.unscaledTime;
        if (now - secondStart >= 1f)
        {
            secondStart = now;
            secondBursts = 0;
            Array.Clear(burstCounts, 0, burstCounts.Length);
        }
        if (frame != Time.frameCount) { frame = Time.frameCount; frameBursts = 0; }
        int count = distance > 10f ? Mathf.Max(1, CloseCounts[tier] / 2) : CloseCounts[tier];
        if (frameBursts >= 10 || secondBursts >= 140 || burstCounts[tier] >= BurstLimits[tier]
            || system.particleCount + count > system.main.maxParticles)
        {
            BudgetDrops++;
            return false;
        }

        Color tint = SurfaceTint(ground);
        if (tint.a <= 0f) return false;
        Vector3 normal = groundNormal.sqrMagnitude > .5f ? groundNormal.normalized : Vector3.up;
        float radius = tier == 0 ? .13f : tier == 1 ? .23f : .36f;
        float speed = tier == 0 ? .45f : tier == 1 ? .62f : .78f;
        float size = tier == 0 ? .45f : tier == 1 ? .75f : 1.10f;
        float life = tier == 0 ? .31f : tier == 1 ? .43f : .58f;
        Vector3 forward = travelDirection.sqrMagnitude > .001f
            ? travelDirection.normalized : Vector3.forward;
        for (int i = 0; i < count; i++)
        {
            Vector2 spread = UnityEngine.Random.insideUnitCircle;
            Vector3 radial = Vector3.ProjectOnPlane(new Vector3(spread.x, 0f, spread.y), normal);
            var emission = new ParticleSystem.EmitParams
            {
                position = point + radial * radius * .35f + normal * .035f,
                velocity = (radial - forward * .18f) * speed + normal *
                    UnityEngine.Random.Range(.12f, .32f),
                startColor = new Color(tint.r, tint.g, tint.b,
                    tint.a * UnityEngine.Random.Range(.75f, 1f)),
                startSize = size * UnityEngine.Random.Range(.7f, 1.3f),
                startLifetime = life * UnityEngine.Random.Range(.8f, 1.2f),
                rotation = UnityEngine.Random.Range(0f, 360f)
            };
            system.Emit(emission, 1);
        }
        frameBursts++;
        secondBursts++;
        burstCounts[tier]++;
        EmittedBursts++;
        return true;
    }

    private bool IsOnScreen(Vector3 point)
    {
        QuarterViewCamera rig = QuarterViewCamera.ActiveInstance;
        Camera active = rig != null ? rig.GetComponent<Camera>() : Camera.main;
        if (active == null) return false;
        if (gameCamera != active) gameCamera = active;
        Vector3 viewport = gameCamera.WorldToViewportPoint(point);
        return viewport.z > 0f && viewport.x > -.08f && viewport.x < 1.08f
            && viewport.y > -.08f && viewport.y < 1.08f;
    }

    private static Color SurfaceTint(Collider ground)
    {
        string surface = null;
        if (ground != null)
        {
            SurfaceOverride custom = ground.GetComponentInParent<SurfaceOverride>();
            if (custom != null && custom.Profile != null) surface = custom.Profile.SurfaceId;
            if (surface == null && ground.sharedMaterial != null)
                surface = ground.sharedMaterial.name;
        }
        if (!string.IsNullOrEmpty(surface))
        {
            if (surface.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0f, 0f, 0f, 0f);
            if (surface.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(.48f, .48f, .36f, .68f);
            if (surface.IndexOf("Concrete", StringComparison.OrdinalIgnoreCase) >= 0
                || surface.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(.58f, .56f, .52f, .62f);
            if (surface.IndexOf("Wood", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(.52f, .44f, .36f, .55f);
        }
        return new Color(.60f, .53f, .43f, .70f);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            systems[i].Clear(true);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }
}
