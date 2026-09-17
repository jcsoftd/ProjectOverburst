using UnityEngine;

public static class MagicProjectileSpawner // 마법 투사체 생성
{
    public static void Spawn(MagicCastRequest request)
    {
        if (!request.IsValid)
            return; // 요청 오류

        MonoBehaviour projectileBehaviour = Object.Instantiate(
            request.projectilePrefab,
            request.origin,
            Quaternion.LookRotation(request.direction, Vector3.up));

        projectileBehaviour.transform.localScale *= Mathf.Max(0.01f, request.projectileScale); // 크기 보정
        IMagicProjectile projectile = projectileBehaviour as IMagicProjectile; // 마법 인터페이스
        if (projectile == null)
        {
            Object.Destroy(projectileBehaviour.gameObject); // 잘못된 prefab
            return;
        }

        projectile.Configure(request.projectileConfig);
        projectile.Launch(request.direction);
    }
}
