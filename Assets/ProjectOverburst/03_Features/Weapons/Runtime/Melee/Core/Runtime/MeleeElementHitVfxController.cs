using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class MeleeElementHitVfxController : MonoBehaviour, ITransientVfxPlayback
{
    [SerializeField] private WeaponElement selectedElement = WeaponElement.None;
    [SerializeField] private GameObject fireHit;
    [SerializeField] private GameObject iceHit;
    [SerializeField] private GameObject electricHit;
    [SerializeField] private GameObject darkHit;
    [SerializeField] private GameObject lightHit;
    private ParticleSystem[][] cachedParticles;
    private bool playbackCleared;

    public WeaponElement SelectedElement => selectedElement;

    private void Awake()
    {
        EnsureParticleCache();
        ApplySelection(); // 첫 활성 전 단일 모듈 선택
    }

    private void OnEnable()
    {
        playbackCleared = false; // ParticleSystem playOnAwake may have run on activation.
        ApplySelection(); // 풀 복귀 선택 복원
    }

    public void SetElement(WeaponElement element)
    {
        if (selectedElement != element) StopAndClearVfx();
        selectedElement = MeleeElementHitVfxCatalog.Supports(element)
            ? element
            : WeaponElement.None;
        ApplySelection();
    }

    public GameObject GetElementObject(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire:
                return fireHit;
            case WeaponElement.Ice:
                return iceHit;
            case WeaponElement.Electric:
                return electricHit;
            case WeaponElement.Dark:
                return darkHit;
            case WeaponElement.Light:
                return lightHit;
            default:
                return null;
        }
    }

    public bool HasPlayableContent(WeaponElement element)
    {
        return GetParticles(element).Length != 0;
    }

    public void RestartVfx()
    {
        StopAndClearVfx();
        ParticleSystem[] particles = GetParticles(selectedElement);
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null) continue;
            var main = particles[i].main;
            main.useUnscaledTime = true; // Contact flashes remain visible during hit-stop.
            particles[i].Play(false);
        }
        playbackCleared = false;
    }

    public void StopAndClearVfx()
    {
        if (playbackCleared) return;
        EnsureParticleCache();
        for (int i = 0; i < cachedParticles.Length; i++)
            for (int j = 0; j < cachedParticles[i].Length; j++)
                if (cachedParticles[i][j] != null)
                    cachedParticles[i][j].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        playbackCleared = true;
    }

    private void ApplySelection()
    {
        SetActive(fireHit, false);
        SetActive(iceHit, false);
        SetActive(electricHit, false);
        SetActive(darkHit, false);
        SetActive(lightHit, false);
        SetActive(GetElementObject(selectedElement), true);
    }

    private void EnsureParticleCache()
    {
        if (cachedParticles != null) return;
        cachedParticles = new[] { Cache(fireHit), Cache(iceHit), Cache(electricHit), Cache(darkHit), Cache(lightHit) };
    }

    private static ParticleSystem[] Cache(GameObject root) => root != null
        ? root.GetComponentsInChildren<ParticleSystem>(true) : System.Array.Empty<ParticleSystem>();

    private ParticleSystem[] GetParticles(WeaponElement element)
    {
        EnsureParticleCache();
        switch (element)
        {
            case WeaponElement.Fire: return cachedParticles[0];
            case WeaponElement.Ice: return cachedParticles[1];
            case WeaponElement.Electric: return cachedParticles[2];
            case WeaponElement.Dark: return cachedParticles[3];
            case WeaponElement.Light: return cachedParticles[4];
            default: return System.Array.Empty<ParticleSystem>();
        }
    }

    private void OnValidate() { cachedParticles = null; playbackCleared = false; }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
