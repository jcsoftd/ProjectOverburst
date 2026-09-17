using UnityEngine;

[CreateAssetMenu(fileName = "NewQuestItem", menuName = "Items/QuestItem")]
public class QuestItemData : BaseItemData
{
    [Header("퀘스트 정보")]
    public string questId; // 퀘스트 ID
    public bool isInstallable; // 설치 가능한 아이템인지
}