using UnityEngine;

/// <summary>Positions the approved view using the existing gameplay tooltip service.</summary>
public sealed class OverburstGameTooltip : MonoBehaviour
{
    public OverburstUITooltipView view;
    public void Show(ItemData item,string price=null){view.Present(item,price);transform.SetAsLastSibling();Place();}
    public void Hide(){if(view)view.gameObject.SetActive(false);}
    public void Place(){
        if(!view||!view.gameObject.activeSelf)return;
        var root=(RectTransform)transform;var canvas=GetComponentInParent<Canvas>();
        var screen=PlayerInputFacade.Current!=null?PlayerInputFacade.Current.PointerPosition:Vector2.zero;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var p);
        var area=root.rect;var size=view.Rect.sizeDelta;float x=p.x+22;
        if(x+size.x>area.xMax-16)x=p.x-size.x-22;
        view.Rect.anchoredPosition=new Vector2(Mathf.Clamp(x,area.xMin+16,Mathf.Max(area.xMin+16,area.xMax-size.x-16)),Mathf.Clamp(p.y+16,area.yMin+size.y+16,area.yMax-16));
    }
    private void OnDisable()=>Hide();
}
