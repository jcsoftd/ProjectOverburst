using UnityEngine;

/// <summary>이 몬스터가 맞을 때 공용 피격음 위에 얹을 추가음 묶음. 비어 있으면 공용 피격음만 난다.</summary>
[DisallowMultipleComponent]
public sealed class MonsterHitSfxTarget : MonoBehaviour
{
    [SerializeField] private MonsterHitSfxBundle bundle;
    public MonsterHitSfxBundle Bundle => bundle;
}
