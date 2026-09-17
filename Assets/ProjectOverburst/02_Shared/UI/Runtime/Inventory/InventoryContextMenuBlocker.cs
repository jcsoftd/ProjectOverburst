using UnityEngine;
using UnityEngine.EventSystems;

public class InventoryContextMenuBlocker : MonoBehaviour, IPointerClickHandler
{
    private InventoryContextMenuController owner;

    public void Init(InventoryContextMenuController controller)
    {
        owner = controller;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerPress != gameObject && eventData.pointerEnter != gameObject)
            return;

        owner?.Close();
    }
}
