using System;
using UnityEngine;

[Serializable]
public struct WeaponBaseStats
{
    [InspectorName("기본 데미지")]
    [Min(0f)] public float damage;

    [InspectorName("원소 방출 기본력")]
    [Min(0f)] public float elementalDischargePower;

    [InspectorName("치명타 확률")]
    [Range(0f, 100f)] public float criticalChance;

    [InspectorName("치명타 데미지 배율")]
    [Min(1f)] public float criticalDamageMultiplier;

    [InspectorName("사거리")]
    [Min(0.1f)] public float range;

    [InspectorName("넉백")]
    [Min(0f)] public float knockback;
}
