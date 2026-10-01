using UnityEngine;
using System.Collections.Generic;

public enum BagRandomOptionType
{
    MoveSpeedPercent,
    MaxStamina, // 구 저장 입력 전용. 이름과 번호를 유지하며 런타임 능력치로 사용하지 않는다.
    MaxHp
}

[System.Serializable]
public class BagRandomOptionRoll
{
    public BagRandomOptionType optionType; // 옵션 종류
    public float value; // 실제 롤 수치
}

[CreateAssetMenu(fileName = "NewBag", menuName = "Items/Bag")]
public class BagItemData : BaseItemData
{
    [Header("가방 정보")]
    public int level = 1;                        // 가방 등급 단계 (1~8)
    public ItemGrade defaultGrade = ItemGrade.Common; // 기본 등급
    [Tooltip("구 저장·저작 데이터 호환용. 실제 수납은 BagQuality가 계산한다.")]
    public int additionalSlots;                  // 추가 슬롯 수
}
