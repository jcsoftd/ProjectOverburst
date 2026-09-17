using UnityEngine;

public class WeaponAimSource : MonoBehaviour // 무기 조준 기준점
{
    [Header("Aim Points")]
    [SerializeField] private Transform aimBackPoint;
    [SerializeField] private Transform aimForwardPoint;
    [SerializeField] private Transform attackSpawnPoint;

    [Header("VFX")]
    [SerializeField] private Transform fireVfxAnchor;
    [SerializeField] private Transform reloadVfxAnchor;
    [SerializeField] private Transform shellEjectAnchor;

    public Transform AimBackPoint
    {
        get
        {
            RefreshMissingPoints();
            return aimBackPoint;
        }
    }

    public Transform AimForwardPoint
    {
        get
        {
            RefreshMissingPoints();
            return aimForwardPoint;
        }
    }

    public Transform AttackSpawnPoint
    {
        get
        {
            RefreshMissingPoints();
            return attackSpawnPoint;
        }
    }

    public Transform FireVfxAnchor
    {
        get { return fireVfxAnchor; }
    }

    public Transform ReloadVfxAnchor
    {
        get { return reloadVfxAnchor; }
    }

    public Transform ShellEjectAnchor
    {
        get { return shellEjectAnchor; }
    }

    public bool HasAimPoints
    {
        get
        {
            RefreshMissingPoints();
            return IsAlive(aimBackPoint) && IsAlive(aimForwardPoint);
        }
    }

    public bool HasAttackSpawnPoint
    {
        get
        {
            RefreshMissingPoints();
            return IsAlive(attackSpawnPoint);
        }
    }

    private void Awake()
    {
        RefreshMissingPoints();
    }

    public Vector3 GetFlatAimDirection()
    {
        RefreshMissingPoints();

        if (!HasAimPoints)
            return Vector3.zero;

        if (!TryGetPosition(aimBackPoint, out Vector3 backPosition) || !TryGetPosition(aimForwardPoint, out Vector3 forwardPosition))
            return Vector3.zero;

        Vector3 direction = forwardPosition - backPosition; // anchor 방향
        direction.y = 0f; // XZ 평면

        if (direction.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        return direction.normalized;
    }

    public Vector3 GetAttackSpawnPosition()
    {
        RefreshMissingPoints();

        if (!IsAlive(attackSpawnPoint))
            return transform.position;

        return attackSpawnPoint.position;
    }

    public Transform GetFireVfxAnchor()
    {
        RefreshMissingPoints();

        if (IsAlive(fireVfxAnchor))
            return fireVfxAnchor;

        return attackSpawnPoint;
    }

    public Transform GetReloadVfxAnchor()
    {
        RefreshMissingPoints();

        if (IsAlive(reloadVfxAnchor))
            return reloadVfxAnchor;

        return transform;
    }

    public Transform GetShellEjectAnchor()
    {
        RefreshMissingPoints();

        if (IsAlive(shellEjectAnchor))
            return shellEjectAnchor;

        return attackSpawnPoint;
    }

    public void RefreshMissingPoints()
    {
        if (!IsAlive(aimBackPoint))
            aimBackPoint = FindDeepChild(transform, "AimBackPoint");

        if (!IsAlive(aimForwardPoint))
            aimForwardPoint = FindDeepChild(transform, "AimForwardPoint");

        if (!IsAlive(attackSpawnPoint))
            attackSpawnPoint = FindDeepChild(transform, "ProjectileSpawnPoint");

        if (!IsAlive(attackSpawnPoint))
            attackSpawnPoint = aimForwardPoint;
    }

    private bool IsAlive(Transform target)
    {
        try
        {
            return target != null;
        }
        catch (MissingReferenceException)
        {
            return false;
        }
    }

    private bool TryGetPosition(Transform target, out Vector3 position)
    {
        position = Vector3.zero;
        if (!IsAlive(target))
            return false;

        try
        {
            position = target.position;
            return true;
        }
        catch (MissingReferenceException)
        {
            return false;
        }
    }

    private Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);

            if (found != null)
                return found;
        }

        return null;
    }
}
