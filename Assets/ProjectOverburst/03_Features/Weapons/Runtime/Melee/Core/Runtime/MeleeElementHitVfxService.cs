using UnityEngine;
using System.Collections.Generic;
using Unity.Profiling;

public static class MeleeElementHitVfxService
{
    private static MeleeElementHitVfxCatalog catalog;
    private static bool loadAttempted;
    private static readonly ProfilerMarker PlayMarker = new ProfilerMarker("Overburst.ElementHit.Play");
    public const float UniformHitScale = MeleeElementHitVfxCatalog.SmallTierHitScale; // 크기값 없이 부를 때(소형 기준) 크기

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        catalog = null;
        loadAttempted = false;
    }

    // Explicit loading requests expire; equipment owns persistent demand until disabled.
    public static void PrepareForElement(WeaponElement element)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.ElementHit)) return;
#endif
        if (Application.isPlaying && TryResolve(element, out var prefab))
            MeleeElementPoolMaintenance.Request(prefab, catalog.ResolvePrewarmCount(element));
    }

    public static void SetElementDemand(Object owner, WeaponElement element)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.ElementHit)) return;
#endif
        if (!Application.isPlaying || owner == null) return;
        if (TryResolve(element, out var prefab))
            MeleeElementPoolMaintenance.SetOwner(owner, prefab, catalog.ResolvePrewarmCount(element));
        else MeleeElementPoolMaintenance.ReleaseOwner(owner);
    }

    public static void ReleaseElementDemand(Object owner) => MeleeElementPoolMaintenance.ReleaseOwner(owner);

    public static bool IsPrepared(WeaponElement element)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.ElementHit)) return true;
#endif
        return TryResolve(element, out var prefab) && MeleeElementPoolMaintenance.IsPrepared(prefab);
    }

    public static bool CanPlay(WeaponElement element)
    {
        return TryResolve(element, out GameObject prefab)
            && prefab.TryGetComponent(out MeleeElementHitVfxController controller)
            && controller.HasPlayableContent(element);
    }

    // 맞은 몬스터 크기를 모르는 곳(연쇄번개 튐·빛 3연타·몬스터 번개탄)은 소형 기준 0.65배로 고정한다.
    public static bool TryPlay(WeaponElement element, Vector3 hitPoint)
    {
        using (PlayMarker.Auto())
            return Play(element, hitPoint, -1f, 0);
    }

    // 몸 크기를 모르는 적중도 생성한 콘텐츠 씬과 함께 반환한다.
    public static bool TryPlayInScene(WeaponElement element, Vector3 hitPoint, int contentSceneHandle)
    {
        using (PlayMarker.Auto())
            return Play(element, hitPoint, -1f, contentSceneHandle);
    }

    // sizeMultiplier = CombatTargetVfxPlacement.ResolveContact의 몸 크기 배율(0.55~1.5).
    public static bool TryPlay(WeaponElement element, Vector3 hitPoint, float sizeMultiplier, int contentSceneHandle = 0)
    {
        using (PlayMarker.Auto())
            return Play(element, hitPoint, Mathf.Max(.01f, sizeMultiplier), contentSceneHandle);
    }

    private static bool Play(WeaponElement element, Vector3 hitPoint, float bodySizeMultiplier, int contentSceneHandle)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.ElementHit)) return false;
#endif
        if (!TryResolve(element, out GameObject prefab))
            return false;

        MeleeElementHitVfxController prefabController =
            prefab.GetComponent<MeleeElementHitVfxController>();
        if (prefabController == null || !prefabController.HasPlayableContent(element))
            return false;

        MeleeElementPoolMaintenance.Touch(prefab);
        float playbackSpeed = catalog.ResolvePlaybackSpeed(element);
        float tierScale = bodySizeMultiplier > 0f ? catalog.ResolveTierScale(bodySizeMultiplier) : UniformHitScale;
        float hitScale = tierScale * catalog.ResolveHitScale(element);
        GameObject instance = TransientVfxPool.Spawn(
            prefab,
            hitPoint,
            Quaternion.identity,
            catalog.ResolveLifetime(element) / playbackSpeed, // 빨리 재생한 만큼 풀 반환도 앞당긴다
            catalog.ResolvePoolCapacity(element),
            null,
            spawned =>
            {
                MeleeElementHitVfxController controller =
                    spawned.GetComponent<MeleeElementHitVfxController>();
                if (controller == null)
                    throw new MissingComponentException(nameof(MeleeElementHitVfxController));

                controller.SetElement(element); // 활성 전 원소 주입
                controller.SetPlaybackSpeed(playbackSpeed);
                // 2026-10-01: 몸 크기 비례로 되돌리되 차이는 카탈로그 체급 차이 강도(0.6)만큼만 남긴다
                // (소형 0.63·중형 약 0.8~0.9·대형 약 1.0배). 혈흔은 BloodHitVfxService가 몸 크기대로 따로 키운다.
                // 원소끼리 크기를 맞추려 카탈로그의 원소별 배율(불 1.1·빛 0.8)을 곱한다.
                spawned.transform.localScale = prefab.transform.localScale * hitScale;
            },
            useUnscaledTime: true,
            contentSceneHandle: contentSceneHandle);
        return instance != null;
    }

    private static bool TryResolve(WeaponElement element, out GameObject prefab)
    {
        prefab = null;
        if (!MeleeElementHitVfxCatalog.Supports(element))
            return false;

        EnsureCatalog();
        return catalog != null && catalog.TryResolve(element, out prefab);
    }

    private static void EnsureCatalog()
    {
        if (loadAttempted)
            return;

        loadAttempted = true;
        catalog = Resources.Load<MeleeElementHitVfxCatalog>(
            MeleeElementHitVfxCatalog.ResourcePath);
        if (catalog == null)
        {
            Debug.LogWarning(
                $"[MeleeElementHitVfx] Catalog load failed: Resources/{MeleeElementHitVfxCatalog.ResourcePath}");
        }
    }
}
