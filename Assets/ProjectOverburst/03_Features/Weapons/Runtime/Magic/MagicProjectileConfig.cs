using UnityEngine;

public struct MagicProjectileConfig // 마법 투사체 설정
{
    public float speed; // 이동 속도
    public int damage; // 최종 피해
    public float baseDamage; // 기본 피해
    public float critChance; // 치명 확률
    public float critDamageMultiplier; // 치명 배율
    public float range; // 사거리
    public float explosionRadius; // 폭발 범위
    public GameObject source; // 시전자
    public float knockback; // 넉백
    public bool isCritical; // 치명타
    public float dotDamageFlat; // DoT 고정
    public float dotDamagePercent; // DoT 비율
    public float dotDuration; // DoT 시간
    public float hitHeal; // 적중 회복
    public float lifeStealPercent; // 흡혈 비율
    public float chainRange; // 체인 거리
    public int maxChainDepth; // 체인 깊이
    public int chainBranchCount; // 체인 분기
    public float chainDelay; // 체인 지연
    public float chainDamageFalloff; // 체인 감쇠
}
