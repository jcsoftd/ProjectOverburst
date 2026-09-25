using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Isolated workshop hover preview; never registers with the gameplay tooltip service.</summary>
public sealed class OverburstUITooltipHost : MonoBehaviour
{
    [SerializeField] private OverburstUITooltipView view;
    [SerializeField] private BaseItemData[] samples;
    private readonly Dictionary<string,ItemData> cache=new Dictionary<string,ItemData>();
    private OverburstUIItemSlotView owner;
    public OverburstUITooltipView View=>view;
    public void Configure(OverburstUITooltipView panel,BaseItemData[] catalog){view=panel;samples=catalog;Hide();}
    public void Show(OverburstUIItemSlotView slot,PointerEventData pointer){
        var icon=slot.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>();if(!icon.gameObject.activeSelf||!icon.sprite){Hide();return;}
        BaseItemData data=null;foreach(var sample in samples)if(sample&&sample.icon==icon.sprite){data=sample;break;}
        if(!data){Hide();return;}owner=slot;var grade=slot.GetComponent<OverburstUISlotGradePreview>().Grade;string key=data.GetInstanceID()+":"+grade;
        if(!cache.TryGetValue(key,out var item)){var state=Random.state;try{Random.InitState(1907+(int)grade);item=new ItemData(data,18,grade);cache.Add(key,item);}finally{Random.state=state;}}
        view.Present(item);Place(pointer.position);
    }
    public void Place(Vector2 screen){if(!view.gameObject.activeSelf)return;var root=(RectTransform)transform;var canvas=GetComponentInParent<Canvas>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,canvas.worldCamera,out var p);var area=root.rect;var size=view.Rect.sizeDelta;
        float x=p.x+22;if(x+size.x>area.xMax-16)x=p.x-size.x-22;
        x=Mathf.Clamp(x,area.xMin+16,Mathf.Max(area.xMin+16,area.xMax-size.x-16));float y=Mathf.Clamp(p.y+16,area.yMin+size.y+16,area.yMax-16);
        view.Rect.anchoredPosition=new Vector2(x,y);
    }
    public void Hide(OverburstUIItemSlotView slot=null){if(slot&&owner!=slot)return;owner=null;if(view)view.gameObject.SetActive(false);}
    private void Update(){if(owner&&!owner.gameObject.activeInHierarchy)Hide();}
    private void OnDisable(){Hide();}
}
