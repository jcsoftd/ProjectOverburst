using UnityEngine;

public static class MeleeSharedSlashSpawnContract
{
    public const int BasicInstanceCount = 1;
    public const int CircularInstanceCount = 2;

    public static bool ResolveHorizontalMirror(
        string overrideKey,
        WeaponElement element,
        bool horizontalMirror)
    {
        if (overrideKey != MeleeElementAttackVfxCatalog.BasicSlashKey
            && !MeleeElementAttackVfxCatalog.IsCircularSlashKey(overrideKey))
        {
            return horizontalMirror; // 내려찍기 등 다른 VFX는 기존 방향 유지
        }

        switch (element)
        {
            case WeaponElement.Fire:
            case WeaponElement.Water:
            case WeaponElement.Ice:
            case WeaponElement.Electric:
                return !horizontalMirror; // 수정 원소 VFX 발동 방향 반전
            default:
                return horizontalMirror; // 무속성·바람 기존 방향 유지
        }
    }

    public static int ResolveInstanceCount(string overrideKey)
    {
        return MeleeElementAttackVfxCatalog.IsCircularSlashKey(overrideKey)
            ? CircularInstanceCount
            : BasicInstanceCount;
    }

    public static TransientVfxReturnMode ResolveReturnMode(string overrideKey)
    {
        return MeleeElementAttackVfxCatalog.IsSharedSlashKey(overrideKey)
            ? TransientVfxReturnMode.NaturalParticleCompletion
            : TransientVfxReturnMode.FixedLifetime;
    }

    public static Quaternion ResolveInstanceRotation(
        int instanceIndex,
        Quaternion baseRotation,
        Vector3 scale)
    {
        if (instanceIndex <= 0)
            return baseRotation;

        return ComposeLocalRotation(
            baseRotation,
            scale,
            Quaternion.Euler(0f, 90f, 0f)); // 기존 원형 상대 회전
    }

    private static Quaternion ComposeLocalRotation(
        Quaternion parentRotation,
        Vector3 parentScale,
        Quaternion localRotation)
    {
        Vector3 absoluteScale = new Vector3(
            Mathf.Abs(parentScale.x),
            Mathf.Abs(parentScale.y),
            Mathf.Abs(parentScale.z));
        if (Mathf.Abs(absoluteScale.x - absoluteScale.y) > 0.0001f
            || Mathf.Abs(absoluteScale.x - absoluteScale.z) > 0.0001f)
        {
            return parentRotation * localRotation;
        }

        Matrix4x4 effective = Matrix4x4.Rotate(parentRotation)
            * Matrix4x4.Scale(parentScale)
            * Matrix4x4.Rotate(localRotation);
        Vector3 inverseScale = new Vector3(
            1f / parentScale.x,
            1f / parentScale.y,
            1f / parentScale.z);
        Matrix4x4 rotationMatrix = effective * Matrix4x4.Scale(inverseScale);
        Vector3 forward = rotationMatrix.GetColumn(2);
        Vector3 up = rotationMatrix.GetColumn(1);
        return forward.sqrMagnitude > 0.000001f && up.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(forward, up)
            : parentRotation * localRotation;
    }
}
