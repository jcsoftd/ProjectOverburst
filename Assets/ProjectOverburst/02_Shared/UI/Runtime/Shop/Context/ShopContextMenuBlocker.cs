using UnityEngine.EventSystems;

public class ShopContextMenuBlocker : UnityEngine.MonoBehaviour, IPointerClickHandler
{
    private ShopContextMenuController owner;

    public void Init(ShopContextMenuController controller)
    {
        owner = controller;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.Close();
    }
}
