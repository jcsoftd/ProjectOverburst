using UnityEngine;

public interface IMeleeAttackVfxResolver
{
    GameObject ResolvePrefab(
        MeleeAttackVfxDefinition definition,
        string overrideKey);
}

public static class MeleeAttackVfxResolver
{
    private sealed class DefaultResolver : IMeleeAttackVfxResolver
    {
        public GameObject ResolvePrefab(
            MeleeAttackVfxDefinition definition,
            string overrideKey)
        {
            if (MeleeElementAttackVfxCatalog.IsSharedSlashKey(overrideKey))
                return null; // 구 속성별 fallback 금지

            return definition != null ? definition.neutralPrefab : null;
        }
    }

    private static readonly IMeleeAttackVfxResolver Default = new DefaultResolver();
    private static IMeleeAttackVfxResolver current = Default;

    public static IMeleeAttackVfxResolver Current => current;

    public static void SetResolver(IMeleeAttackVfxResolver resolver)
    {
        current = resolver ?? Default;
    }
}
