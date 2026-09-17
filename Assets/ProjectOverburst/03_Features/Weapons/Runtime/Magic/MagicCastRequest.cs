using UnityEngine;

public struct MagicCastRequest // 마법 시전 요청
{
    public MonoBehaviour projectilePrefab; // 투사체 prefab
    public Vector3 origin; // 시작점
    public Vector3 direction; // 방향
    public float projectileScale; // 크기 배율
    public bool isAimedCast; // 조준 시전
    public bool appliesHipFirePenalty; // 비조준 패널티
    public MagicProjectileConfig projectileConfig; // 투사체 설정

    public bool IsValid
    {
        get { return projectilePrefab != null && direction.sqrMagnitude > 0.0001f; } // 요청 유효성
    }
}
