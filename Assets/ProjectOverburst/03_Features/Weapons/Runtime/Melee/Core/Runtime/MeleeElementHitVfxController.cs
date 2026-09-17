using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class MeleeElementHitVfxController : MonoBehaviour, ITransientVfxPlayback
{
    [SerializeField] private WeaponElement selectedElement = WeaponElement.None;
    [SerializeField] private GameObject fireHit;
    [SerializeField] private GameObject waterHit;
    [SerializeField] private GameObject iceHit;
    [SerializeField] private GameObject electricHit;
    [SerializeField] private GameObject earthHit;

    public WeaponElement SelectedElement => selectedElement;

    private void Awake()
    {
        ApplySelection(); // 첫 활성 전 단일 모듈 선택
    }

    private void OnEnable()
    {
        ApplySelection(); // 풀 복귀 선택 복원
    }

    public void SetElement(WeaponElement element)
    {
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
            case WeaponElement.Water:
                return waterHit;
            case WeaponElement.Ice:
                return iceHit;
            case WeaponElement.Electric:
                return electricHit;
            case WeaponElement.Earth:
                return earthHit;
            default:
                return null;
        }
    }

    public bool HasPlayableContent(WeaponElement element)
    {
        GameObject target = GetElementObject(element);
        if (target == null)
            return false;

        // 기존 원소는 이미 내용물이 확정되어 있다. 빈 저작 슬롯인 대지만 실제 입자를 확인한다.
        return element != WeaponElement.Earth
            || target.GetComponentInChildren<ParticleSystem>(true) != null;
    }

    public void RestartVfx()
    {
        StopAndClearVfx();
        GameObject target = GetElementObject(selectedElement);
        if (target == null)
            return;

        ParticleSystem[] particles = target.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
            particles[i].Play(false);
    }

    public void StopAndClearVfx()
    {
        StopAndClear(fireHit);
        StopAndClear(waterHit);
        StopAndClear(iceHit);
        StopAndClear(electricHit);
        StopAndClear(earthHit);
    }

    private void ApplySelection()
    {
        SetActive(fireHit, false);
        SetActive(waterHit, false);
        SetActive(iceHit, false);
        SetActive(electricHit, false);
        SetActive(earthHit, false);
        SetActive(GetElementObject(selectedElement), true);
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
}
