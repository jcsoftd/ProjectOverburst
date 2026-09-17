using System;
using UnityEngine;

public static class MeleeElementAttackVfxBootstrap
{
    public const string CatalogResourcePath = "Combat/VFX/MeleeElementAttackVfxCatalog";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetResolver()
    {
        MeleeAttackVfxResolver.SetResolver(null); // 재생 세션 초기화
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeResolver()
    {
        try
        {
            MeleeElementAttackVfxCatalog catalog =
                Resources.Load<MeleeElementAttackVfxCatalog>(CatalogResourcePath);
            if (catalog == null)
            {
                Debug.LogWarning(
                    $"[MeleeElementAttackVfx] Catalog load failed: Resources/{CatalogResourcePath}");
                MeleeAttackVfxResolver.SetResolver(null); // 공용 Slash는 미재생
                return;
            }

            MeleeAttackVfxResolver.SetResolver(
                new ElementalMeleeAttackVfxResolver(catalog));
        }
        catch (Exception exception)
        {
            MeleeAttackVfxResolver.SetResolver(null); // 공격 판정은 계속
            Debug.LogException(exception);
        }
    }
}
