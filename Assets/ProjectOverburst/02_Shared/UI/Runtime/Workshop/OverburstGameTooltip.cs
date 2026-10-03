using UnityEngine;

/// <summary>Positions the approved view using the existing gameplay tooltip service.</summary>
public sealed class OverburstGameTooltip : MonoBehaviour
{
    private const float PointerGap = 22f;
    private const float ViewGap = 12f;
    private const float EdgePadding = 16f;

    public OverburstUITooltipView view;

    // 2026-10-01 장착 비교: Alt를 누르고 있으면 같은 승인 뷰를 한 벌 더 써서 장착 중인 무기·방어구·장신구를 띄운다.
    // 항상 왼쪽이 장착 중인 것, 오른쪽이 올려 둔 것이다. 두 툴팁은 같은 폭(432)·같은 높이로 선다.
    private OverburstUITooltipView equippedView;
    private ItemData shownItem;
    private ItemData equippedShown;
    private float primaryHeight;
    private float equippedHeight;

    public void Show(ItemData item,string price=null){shownItem=item;equippedShown=null;view.Present(item,price);primaryHeight=view.Rect.sizeDelta.y;transform.SetAsLastSibling();Place();}
    public void Hide(){shownItem=null;equippedShown=null;if(view)view.gameObject.SetActive(false);HideEquipped();}
    public void Place()=>Place(PlayerInputFacade.Current!=null?PlayerInputFacade.Current.PointerPosition:Vector2.zero);
    public void Place(Vector2 screen){
        if(!view||!view.gameObject.activeSelf){HideEquipped();return;}
        var root=(RectTransform)transform;var canvas=GetComponentInParent<Canvas>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var p);
        var area=root.rect;
        if(PlacePair(p,area))return;
        var size=view.Rect.sizeDelta;float x=p.x+PointerGap;
        if(x+size.x>area.xMax-EdgePadding)x=p.x-size.x-PointerGap;
        view.Rect.anchoredPosition=new Vector2(ClampX(x,size.x,area),ClampY(p.y+16,size.y,area));
    }

    // 두 툴팁을 한 묶음으로 포인터 오른쪽에 둔다. 자리가 없으면 포인터 왼쪽으로 옮겨 커서와 슬롯을 가리지 않게 한다.
    private bool PlacePair(Vector2 pointer,Rect area){
        if(shownItem==null||!EquippedWeaponComparison.IsAltHeld()||!EquippedWeaponComparison.TryGetComparableEquipped(shownItem,out ItemData equipped)){HideEquipped();return false;}
        if(!equippedView){
            equippedView=Instantiate(view,view.transform.parent);
            equippedView.name="Equipped Compare View";
        }
        if(!equippedView.gameObject.activeSelf||equippedShown!=equipped){
            equippedView.Present(equipped,null,TooltipCompareMode.EquippedReference);
            equippedShown=equipped;equippedHeight=equippedView.Rect.sizeDelta.y;
        }
        equippedView.transform.SetAsLastSibling();
        float height=Mathf.Max(primaryHeight,equippedHeight);
        SetHeight(view,height);SetHeight(equippedView,height);
        float left=equippedView.Rect.sizeDelta.x,right=view.Rect.sizeDelta.x,width=left+ViewGap+right;
        float x=pointer.x+PointerGap;
        if(x+width>area.xMax-EdgePadding)x=pointer.x-PointerGap-width;
        x=ClampX(x,width,area);
        float y=ClampY(pointer.y+16,height,area);
        equippedView.Rect.anchoredPosition=new Vector2(x,y);
        view.Rect.anchoredPosition=new Vector2(x+left+ViewGap,y);
        return true;
    }

    private static float ClampX(float x,float width,Rect area)=>Mathf.Clamp(x,area.xMin+EdgePadding,Mathf.Max(area.xMin+EdgePadding,area.xMax-width-EdgePadding));
    private static float ClampY(float y,float height,Rect area)=>Mathf.Clamp(y,area.yMin+height+EdgePadding,area.yMax-EdgePadding);

    private void HideEquipped(){
        bool wasShown=equippedView&&equippedView.gameObject.activeSelf;
        equippedShown=null;
        if(equippedView)equippedView.gameObject.SetActive(false);
        if(wasShown&&view&&view.gameObject.activeSelf&&primaryHeight>0f)SetHeight(view,primaryHeight);
    }
    private static void SetHeight(OverburstUITooltipView target,float height){
        if(!Mathf.Approximately(target.Rect.sizeDelta.y,height))target.Rect.sizeDelta=new Vector2(target.Rect.sizeDelta.x,height);
    }
    private void OnDisable()=>Hide();
}
