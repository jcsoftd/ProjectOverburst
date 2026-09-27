using UnityEngine;

[ExecuteAlways]
[DefaultExecutionOrder(-10000)]
public sealed class MeleeElementSlashController : MonoBehaviour, ITransientVfxPlayback, ITransientVfxCompletion
{
    [SerializeField] private WeaponElement selectedElement = WeaponElement.None;
    [SerializeField] private GameObject neutralSlash;
    [SerializeField] private GameObject fireSlash;
    [SerializeField] private GameObject iceSlash;
    [SerializeField] private GameObject electricSlash;
    [SerializeField] private GameObject darkSlash;
    [SerializeField] private GameObject lightSlash;
    [SerializeField, Min(0f)] private float startOffsetSeconds = 0.06f;

    public WeaponElement SelectedElement => selectedElement;
    public float StartOffsetSeconds => Mathf.Max(0f, startOffsetSeconds);
    public bool IsPlaybackAlive
    {
        get
        {
            GameObject target = GetElementObject(selectedElement);
            if (target == null || !target.activeInHierarchy)
                return false;

            ParticleSystem[] particles = target.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null && particles[i].IsAlive(false))
                    return true;
            }

            return false;
        }
    }

    private void Awake()
    {
        ApplySelection(); // 최초 프레임 단일 활성 보장
    }

    private void OnEnable()
    {
        ApplySelection(); // 활성 복귀 시 선택 복원
    }

    private void OnValidate()
    {
        selectedElement = NormalizeElement(selectedElement);
        ApplySelection(); // Inspector 즉시 미리보기
    }

    public void SetElement(WeaponElement element)
    {
        selectedElement = NormalizeElement(element);
        ApplySelection();
    }

    public void Play()
    {
        ApplySelection();
        RestartVfx();
    }

    public void Play(WeaponElement element)
    {
        SetElement(element);
        RestartVfx();
    }

    public GameObject GetElementObject(WeaponElement element)
    {
        switch (NormalizeElement(element))
        {
            case WeaponElement.Fire:
                return fireSlash;
            case WeaponElement.Ice:
                return iceSlash;
            case WeaponElement.Electric:
                return electricSlash;
            case WeaponElement.Dark:
                return darkSlash;
            case WeaponElement.Light:
                return lightSlash;
            default:
                return neutralSlash;
        }
    }

    private void ApplySelection()
    {
        SetActive(neutralSlash, false);
        SetActive(fireSlash, false);
        SetActive(iceSlash, false);
        SetActive(electricSlash, false);
        SetActive(darkSlash, false);
        SetActive(lightSlash, false);

        SetActive(GetElementObject(selectedElement), true);
    }

    public void RestartVfx()
    {
        GameObject target = GetElementObject(selectedElement);
        if (target == null)
            return;

        ParticleSystem[] particles = target.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
            particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

        float offset = ResolveStartOffsetSeconds(target);
        if (offset > 0f)
        {
            for (int i = 0; i < particles.Length; i++)
                particles[i].Simulate(offset, false, true, true); // 각 시스템을 한 번만 선행 재생
        }

        for (int i = 0; i < particles.Length; i++)
            particles[i].Play(false);
    }

    public void StopAndClearVfx()
    {
        StopAndClear(neutralSlash);
        StopAndClear(fireSlash);
        StopAndClear(iceSlash);
        StopAndClear(electricSlash);
        StopAndClear(darkSlash);
        StopAndClear(lightSlash);
    }

    private static void StopAndClear(GameObject target)
    {
        if (target == null)
            return;

        ParticleSystem[] particles = target.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
            particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private static WeaponElement NormalizeElement(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.None:
            case WeaponElement.Fire:
            case WeaponElement.Ice:
            case WeaponElement.Electric:
            case WeaponElement.Dark:
            case WeaponElement.Light:
                return element;
            default:
                return WeaponElement.None;
        }
    }

    private float ResolveStartOffsetSeconds(GameObject target)
    {
        MeleeElementSlashPlaybackSettings settings =
            target != null ? target.GetComponent<MeleeElementSlashPlaybackSettings>() : null;
        return settings != null
            ? settings.ResolveStartOffsetSeconds(StartOffsetSeconds) // 모듈 프리팹 설정 우선
            : StartOffsetSeconds;
    }
}
