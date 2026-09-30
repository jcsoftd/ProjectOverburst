using UnityEngine;

// 60D 빛: 광휘 중첩(에너지 101~200 구간) 동안 플레이어 몸에 HolyAura 프로젝트 변형을 켠다.
// 중첩 비율에 따라 방출량·크기·불투명도를 올리고, 중첩이 0이 되면 방출을 멈춰 자연스럽게 사라지게 한다.
// OverburstElementEnergy가 같은 오브젝트에 붙인다.
[DisallowMultipleComponent]
public sealed class LightRadianceAuraPresenter : MonoBehaviour
{
    public const string ResourcePath = "Combat/VFX/VFX_Light_RadianceAura";
    private const float HeightOffset = 1f;     // 프리팹 원점이 가슴 높이(바닥 입자는 -1m)
    private const float MinVisual = .3f;       // 중첩 1일 때의 최소 세기
    private const float FollowSpeed = 3f;      // 세기 변화 속도(초당)
    private const int Levels = 10;             // 색 알파를 다시 계산하는 단계 수

    private static GameObject prefab;
    private static bool prefabLoaded;

    private OverburstElementEnergy energy;
    private GameObject aura;
    private ParticleSystem auraRoot;
    private ParticleSystem[] systems;
    private float[] baseRate, baseSize;
    private ParticleSystem.MinMaxGradient[] baseColor;
    private float current;
    private int appliedLevel = -1;
    private bool emitting;

    public float CurrentIntensity => current;
    public bool IsEmitting => emitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { prefab = null; prefabLoaded = false; }

    private void Awake() => energy = GetComponent<OverburstElementEnergy>();

    private void OnDisable()
    {
        current = 0f; appliedLevel = -1;
        if (auraRoot != null) auraRoot.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        emitting = false;
    }

    private void LateUpdate()
    {
        if (energy == null) energy = GetComponent<OverburstElementEnergy>();
        float target = energy != null && energy.Element == WeaponElement.Light && energy.RadianceStacks > 0
            ? Mathf.Lerp(MinVisual, 1f, energy.RadianceNormalized) : 0f;
        current = Mathf.MoveTowards(current, target, FollowSpeed * Time.deltaTime);
        if (target <= 0f && current <= 0f)
        {
            if (emitting && auraRoot != null) auraRoot.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            emitting = false;
            return;
        }
        if (!EnsureAura()) return;
        Apply(Mathf.Max(current, .01f));
        if (!emitting) { auraRoot.Play(true); emitting = true; }
    }

    private bool EnsureAura()
    {
        if (aura != null) return true;
        if (!prefabLoaded) { prefab = Resources.Load<GameObject>(ResourcePath); prefabLoaded = true; }
        if (prefab == null) return false;
        aura = Instantiate(prefab, transform);
        aura.name = "Radiance aura";
        aura.transform.localPosition = new Vector3(0f, HeightOffset, 0f);
        aura.transform.localRotation = Quaternion.identity;
        auraRoot = aura.GetComponent<ParticleSystem>();
        systems = aura.GetComponentsInChildren<ParticleSystem>(true);
        baseRate = new float[systems.Length]; baseSize = new float[systems.Length];
        baseColor = new ParticleSystem.MinMaxGradient[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            baseRate[i] = systems[i].emission.rateOverTimeMultiplier;
            baseSize[i] = systems[i].main.startSizeMultiplier;
            baseColor[i] = systems[i].main.startColor;
        }
        auraRoot.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return auraRoot != null;
    }

    private void Apply(float intensity)
    {
        int level = Mathf.Clamp(Mathf.CeilToInt(intensity * Levels), 1, Levels);
        if (level == appliedLevel) return;
        appliedLevel = level;
        float k = level / (float)Levels;
        for (int i = 0; i < systems.Length; i++)
        {
            var emission = systems[i].emission;
            emission.rateOverTimeMultiplier = baseRate[i] * k;
            var main = systems[i].main;
            main.startSizeMultiplier = baseSize[i] * Mathf.Lerp(.7f, 1f, k);
            main.startColor = ScaleAlpha(baseColor[i], Mathf.Lerp(.45f, 1f, k));
        }
    }

    private static ParticleSystem.MinMaxGradient ScaleAlpha(ParticleSystem.MinMaxGradient source, float alpha)
    {
        switch (source.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(Fade(source.color, alpha));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(Fade(source.colorMin, alpha), Fade(source.colorMax, alpha));
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(FadeGradient(source.gradient, alpha));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(FadeGradient(source.gradientMin, alpha), FadeGradient(source.gradientMax, alpha));
            case ParticleSystemGradientMode.RandomColor:
                var random = new ParticleSystem.MinMaxGradient(FadeGradient(source.gradient, alpha));
                random.mode = ParticleSystemGradientMode.RandomColor;
                return random;
            default:
                return source;
        }
    }

    private static Color Fade(Color c, float alpha) { c.a *= alpha; return c; }

    private static Gradient FadeGradient(Gradient source, float alpha)
    {
        if (source == null) return null;
        var keys = source.alphaKeys;
        for (int i = 0; i < keys.Length; i++) keys[i].alpha *= alpha;
        var result = new Gradient { mode = source.mode };
        result.SetKeys(source.colorKeys, keys);
        return result;
    }
}
