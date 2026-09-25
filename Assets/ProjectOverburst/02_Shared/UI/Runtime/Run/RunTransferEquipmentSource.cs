using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Temporary child of an equipment slot while transfer is open. Never unequips.</summary>
public sealed class RunTransferEquipmentSource : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private OverburstRunUi owner;
    private Func<ItemData> resolve;
    private GameObject ghost;
    public ItemData Item => resolve?.Invoke();
    public void Bind(OverburstRunUi view,Func<ItemData> item) { owner=view;resolve=item; }
    public void OnPointerClick(PointerEventData eventData)
    { if(owner!=null && eventData.button==PointerEventData.InputButton.Left)owner.TryStageTransfer(Item?.runtimeInstanceId); }
    public void OnBeginDrag(PointerEventData eventData)
    {
        if(owner==null || !owner.IsTransferOpen || Item==null)return;
        ghost=RunUiLayout.Image(owner.transform,"EquipmentDrag",Item.icon,Color.white,0,0,54,54).gameObject;
        ghost.transform.position=eventData.position;
    }
    public void OnDrag(PointerEventData eventData){if(ghost!=null)ghost.transform.position=eventData.position;}
    public void OnEndDrag(PointerEventData eventData){if(ghost!=null)Destroy(ghost);ghost=null;}
    private void OnDisable(){if(ghost!=null)Destroy(ghost);ghost=null;}
}
