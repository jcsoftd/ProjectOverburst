using UnityEngine;
using UnityEngine.EventSystems;

public sealed class QuestMerchantOption : MonoBehaviour, IPointerEnterHandler
{
    public int option;
    public void OnPointerEnter(PointerEventData eventData) => GetComponentInParent<QuestMerchantUI>()?.SelectOption(option);
}
