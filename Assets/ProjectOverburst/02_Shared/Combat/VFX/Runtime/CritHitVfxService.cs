using UnityEngine;

// 치명타 공용 타격 효과(ARPG_White__Projectile_Hit_Impact 프로젝트 변형).
// 가운데 섬광(Flare 계열)은 흰색으로 남겨 치명타로 읽히게 하고, 불티·불꽃·원형 섬광은 타격 원소 색을 따른다.
// 무속성은 전부 흰색. 풀 24개, 한 프레임 6개까지, 화면 밖은 건너뛴다.
public sealed class CritHitVfxService : MonoBehaviour
{
    public const string ResourcePath = "Combat/VFX/VFX_CritHit";
    private const int PoolSize = 24;
    private const int MaxPerFrame = 6;
    private const float Lifetime = .9f;

    private static CritHitVfxService instance;

    private sealed class Slot
    {
        public GameObject Root;
        public ParticleSystem All;
        public ParticleSystem[] Tinted;
        public Color[] BaseColors;
        public float[] Mix;
        public float FreeAt;
    }

    private Slot[] slots;
    private int cursor, frame, playedThisFrame;
    public static int PlayedCount { get; private set; }
    public static int SkippedCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; PlayedCount = 0; SkippedCount = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null) return;
        var root = new GameObject(nameof(CritHitVfxService));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<CritHitVfxService>();
        instance.Build(prefab);
    }

    // 원소 무기 이펙트 색 기준. 어둠은 무기 불씨(#BF0106) 계열 암적색.
    public static Color ResolveTint(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return new Color(1f, .42f, .08f, 1f);
            case WeaponElement.Ice: return new Color(.35f, .88f, 1f, 1f);
            case WeaponElement.Electric: return new Color(.72f, .38f, 1f, 1f);
            case WeaponElement.Dark: return new Color(.78f, .03f, .05f, 1f);
            case WeaponElement.Light: return new Color(1f, .86f, .42f, 1f);
            default: return Color.white;
        }
    }

    public static bool TryPlay(WeaponElement element, Vector3 point, float sizeMultiplier = 1f)
    {
        if (instance == null) Bootstrap();
        if (instance == null) return false;
        return instance.Play(element, point, sizeMultiplier);
    }

    private void Build(GameObject prefab)
    {
        slots = new Slot[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var go = Instantiate(prefab, transform);
            go.name = "Crit " + i;
            var all = go.GetComponent<ParticleSystem>();
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            int count = 0;
            foreach (var ps in systems) if (TintMix(ps.name) > 0f) count++;
            var slot = new Slot { Root = go, All = all, Tinted = new ParticleSystem[count], BaseColors = new Color[count], Mix = new float[count] };
            int k = 0;
            foreach (var ps in systems)
            {
                float mix = TintMix(ps.name);
                if (mix <= 0f) continue;
                slot.Tinted[k] = ps; slot.BaseColors[k] = ps.main.startColor.color; slot.Mix[k] = mix; k++;
            }
            if (all != null) all.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.SetActive(false);
            slots[i] = slot;
        }
    }

    // 섬광(Flare) 계열은 흰색 유지, 불티·불꽃·원형 섬광은 원소 색, 연기는 절반만.
    private static float TintMix(string name)
    {
        if (name.StartsWith("Flare")) return 0f;
        if (name.StartsWith("Smoke")) return .5f;
        if (name.StartsWith("Flecks") || name.StartsWith("Fire_Flames") || name.StartsWith("Circle")) return 1f;
        return 0f;
    }

    private bool Play(WeaponElement element, Vector3 point, float sizeMultiplier)
    {
        if (frame != Time.frameCount) { frame = Time.frameCount; playedThisFrame = 0; }
        if (playedThisFrame >= MaxPerFrame || IsOffscreen(point)) { SkippedCount++; return false; }
        Slot slot = null;
        for (int n = 0; n < slots.Length; n++)
        {
            var candidate = slots[(cursor + n) % slots.Length];
            if (candidate.Root != null && (!candidate.Root.activeSelf || Time.time >= candidate.FreeAt))
            {
                slot = candidate; cursor = (cursor + n + 1) % slots.Length; break;
            }
        }
        if (slot == null || slot.All == null) { SkippedCount++; return false; }
        Color tint = ResolveTint(element);
        for (int i = 0; i < slot.Tinted.Length; i++)
        {
            Color baseColor = slot.BaseColors[i];
            // 원래 회색(0.64)의 밝기를 유지한 채 색만 바꾼다.
            Color target = new Color(tint.r * .9f + .1f, tint.g * .9f + .1f, tint.b * .9f + .1f, baseColor.a);
            var main = slot.Tinted[i].main;
            main.startColor = Color.Lerp(baseColor, target, slot.Mix[i]);
        }
        var t = slot.Root.transform;
        t.position = point;
        t.localScale = Vector3.one * Mathf.Clamp(sizeMultiplier, .5f, 2f);
        slot.Root.SetActive(true);
        slot.All.Clear(true);
        slot.All.Play(true);
        slot.FreeAt = Time.time + Lifetime;
        playedThisFrame++; PlayedCount++;
        return true;
    }

    private void Update()
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot.Root != null && slot.Root.activeSelf && Time.time >= slot.FreeAt)
            {
                slot.All.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                slot.Root.SetActive(false);
            }
        }
    }

    private static bool IsOffscreen(Vector3 point)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 v = cam.WorldToViewportPoint(point);
        return v.z < 0f || v.x < -.1f || v.x > 1.1f || v.y < -.1f || v.y > 1.1f;
    }
}
