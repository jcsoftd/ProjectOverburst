using UnityEngine;

// 60D 빛: 광휘 중첩(에너지 101~200 구간) 동안 플레이어 발밑에 EnchantedGround_Holy 프로젝트 변형을 켠다
// (2026-09-30 HolyAura에서 교체). 중첩 비율에 따라 방출량·크기·불투명도를 올리고, 중첩이 0이 되면 방출을 멈춘다.
// 이 변형은 수명이 무한한 장판·광원 입자를 한 번만 내므로, 그런 입자는 살아 있는 입자의 알파를 직접 바꾸고
// 중첩이 0이 되면 알파 0까지 흐린 뒤 지운다.
// OverburstElementEnergy가 같은 오브젝트에 붙인다.
[DisallowMultipleComponent]
public sealed class LightRadianceAuraPresenter : MonoBehaviour
{
    public const string ResourcePath = "Combat/VFX/VFX_Light_RadianceAura";
    private const float HeightOffset = .02f;   // 프리팹 원점이 바닥(장판)
    private const float PersistentLifetime = 10f; // 이보다 길게 사는 입자는 한 번 낸 뒤 계속 남는다
    private const float MinVisual = .3f;       // 중첩 1일 때의 최소 세기
    private const float FollowSpeed = 3f;      // 세기 변화 속도(초당)
    private const int Levels = 10;             // 색 알파를 다시 계산하는 단계 수

    private static GameObject prefab;
    private static bool prefabLoaded;
    public const string CrownResourcePath = "Combat/VFX/VFX_Light_RadianceCrown";
    private const float CrownAboveHead = .45f; // 머리 뼈 위(머리 꼭대기 ~0.2m + 여유)
    private const float CrownAboveBody = .8f;  // 머리 뼈가 없을 때 몸 부피 꼭대기 위
    private static GameObject crownPrefab;
    private static bool crownLoaded;
    private GameObject crown;
    private ParticleSystem crownRoot;
    private CombatTarget body;
    private Transform headBone;
    private bool crownOn;

    private OverburstElementEnergy energy;
    private GameObject aura;
    private ParticleSystem auraRoot;
    private ParticleSystem[] systems;
    private float[] baseRate, baseSize;
    private ParticleSystem.MinMaxGradient[] baseColor;
    private bool[] persistent;
    private ParticleSystem.Particle[] liveBuffer;
    private float appliedPersistentAlpha = -1f;
    private float current;
    private int appliedLevel = -1;
    private bool emitting;

    public float CurrentIntensity => current;
    public bool IsEmitting => emitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { prefab = null; prefabLoaded = false; crownPrefab = null; crownLoaded = false; }

    private void Awake() => energy = GetComponent<OverburstElementEnergy>();

    private void OnDisable()
    {
        current = 0f; appliedLevel = -1; appliedPersistentAlpha = -1f;
        if (auraRoot != null) auraRoot.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (crownRoot != null) crownRoot.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        emitting = false; crownOn = false;
    }

    private void LateUpdate()
    {
        if (energy == null) energy = GetComponent<OverburstElementEnergy>();
        UpdateCrown();
        float target = energy != null && energy.Element == WeaponElement.Light && energy.RadianceStacks > 0
            ? Mathf.Lerp(MinVisual, 1f, energy.RadianceNormalized) : 0f;
        current = Mathf.MoveTowards(current, target, FollowSpeed * Time.deltaTime);
        if (target <= 0f && current <= 0f)
        {
            if (emitting && auraRoot != null)
            {
                auraRoot.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                ClearPersistent(); // 무한 수명 입자는 멈춰도 남으므로 알파 0에서 지운다
            }
            emitting = false;
            appliedPersistentAlpha = -1f;
            return;
        }
        if (!EnsureAura()) return;
        Apply(Mathf.Max(current, .01f));
        ApplyPersistent(Mathf.Lerp(.45f, 1f, current) * Mathf.Clamp01(current / MinVisual));
        if (!emitting) { auraRoot.Play(true); emitting = true; }
    }

    // 광휘 최대 중첩(100) 동안 머리 위에 Projectile_Holy 변형을 띄운다. 중첩이 내려가면 방출을 멈춘다.
    private void UpdateCrown()
    {
        bool max = energy != null && energy.Element == WeaponElement.Light
            && energy.RadianceStacks >= OverburstElementTuning.Current.SafeLightRadianceMaxStacks;
        if (!max)
        {
            if (crownOn && crownRoot != null) crownRoot.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            crownOn = false;
            return;
        }
        if (crown == null)
        {
            if (!crownLoaded) { crownPrefab = Resources.Load<GameObject>(CrownResourcePath); crownLoaded = true; }
            if (crownPrefab == null) return;
            crown = Instantiate(crownPrefab, transform);
            crown.name = "Radiance crown";
            crownRoot = crown.GetComponent<ParticleSystem>();
            if (crownRoot == null) return;
            crownRoot.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (body == null) body = GetComponent<CombatTarget>();
            if (headBone == null)
            {
                Animator animator = GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman) headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }
        // 몸 부피 꼭대기는 캡슐 높이라 머리 안에 박혔다(2026-09-30) → 머리 뼈 기준.
        Vector3 head = headBone != null
            ? headBone.position + Vector3.up * CrownAboveHead
            : body != null
                ? body.CurrentVolume.Center + Vector3.up * (body.CurrentVolume.HalfHeight + CrownAboveBody)
                : transform.position + Vector3.up * 2.6f;
        crown.transform.SetPositionAndRotation(head, Quaternion.identity);
        if (!crownOn) { crownRoot.Play(true); crownOn = true; }
    }

    public bool IsCrownShown => crownOn;

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
        persistent = new bool[systems.Length];
        int liveMax = 1;
        for (int i = 0; i < systems.Length; i++)
        {
            baseRate[i] = systems[i].emission.rateOverTimeMultiplier;
            baseSize[i] = systems[i].main.startSizeMultiplier;
            baseColor[i] = systems[i].main.startColor;
            var lifetime = systems[i].main.startLifetime;
            persistent[i] = Mathf.Max(lifetime.constant, lifetime.constantMax) > PersistentLifetime
                && systems[i].main.maxParticles <= 8;
            if (persistent[i]) liveMax = Mathf.Max(liveMax, systems[i].main.maxParticles);
        }
        liveBuffer = new ParticleSystem.Particle[liveMax];
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

    // 한 번 낸 뒤 계속 남는 입자(장판·중심 광원)의 알파를 직접 맞춘다. 입자 수가 몇 개뿐이라 매 프레임 싸다.
    private void ApplyPersistent(float alpha)
    {
        if (systems == null || Mathf.Abs(alpha - appliedPersistentAlpha) < .01f) return;
        appliedPersistentAlpha = alpha;
        for (int i = 0; i < systems.Length; i++)
        {
            if (!persistent[i]) continue;
            Color color = BaseColor(baseColor[i]);
            color.a *= alpha;
            int count = systems[i].GetParticles(liveBuffer);
            for (int p = 0; p < count; p++) liveBuffer[p].startColor = color;
            if (count > 0) systems[i].SetParticles(liveBuffer, count);
        }
    }

    private void ClearPersistent()
    {
        if (systems == null) return;
        for (int i = 0; i < systems.Length; i++)
            if (persistent[i]) systems[i].Clear(false);
    }

    private static Color BaseColor(ParticleSystem.MinMaxGradient source)
    {
        switch (source.mode)
        {
            case ParticleSystemGradientMode.Color: return source.color;
            case ParticleSystemGradientMode.TwoColors: return source.colorMax;
            default: return source.gradient != null ? source.gradient.Evaluate(0f) : Color.white;
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
