using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>RPG11 presentation of the existing item formatter. Workshop binding owns sample items.</summary>
public sealed class OverburstUITooltipView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI title,subtitle,gradeLabel,body;
    [SerializeField] private OverburstUIItemSlotView itemSlot;
    [SerializeField] private Image separator;
    [SerializeField] private OverburstTooltipHybridSkin approvedSkin;
    public RectTransform Rect => (RectTransform)transform;
    public TextMeshProUGUI Body => body;
    public void Configure(TextMeshProUGUI name,TextMeshProUGUI sub,TextMeshProUGUI tier,TextMeshProUGUI details,OverburstUIItemSlotView slot,Image rule){title=name;subtitle=sub;gradeLabel=tier;body=details;itemSlot=slot;separator=rule;}
    // 2026-10-01 장착 무기 비교: Alt 나란히 보기의 "장착 중" 쪽은 비교 없이 그 표시만 단다. 한 번 그린 뒤 Auto로 되돌린다.
    public void Present(ItemData item,string priceOverride,TooltipCompareMode mode){
        if(!approvedSkin)approvedSkin=GetComponent<OverburstTooltipHybridSkin>();
        if(approvedSkin)approvedSkin.CompareMode=mode;
        try{Present(item,priceOverride);}
        finally{if(approvedSkin)approvedSkin.CompareMode=TooltipCompareMode.Auto;}
    }
    public void Present(ItemData item,string priceOverride=null){
        if(item==null||!item.baseData){gameObject.SetActive(false);return;}
        gameObject.SetActive(true);title.text=item.itemName;title.color=new Color(.88f,.74f,.48f);
        gradeLabel.text=ItemTooltipFormatter.GetGradeName(item.grade);gradeLabel.color=GradeConfig.GetGradeColor(item.grade);
        if(!approvedSkin)approvedSkin=GetComponent<OverburstTooltipHybridSkin>();
        if(!body.spriteAsset)body.spriteAsset=Resources.Load<TMP_SpriteAsset>("OverburstUI/QualityDiamonds");
        string[] lines=SimpleItemTooltipBuilder.Build(item).Replace("\r","").Split('\n');
        bool hasSubtitle=item.baseData is ElementGemItemData||item.baseData is WeaponItemData||item.baseData is GearItemData||item.baseData is FlaskItemData||item.baseData is BagItemData||item.baseData is ConsumableItemData||item.baseData is MapItemData;
        subtitle.text=hasSubtitle&&lines.Length>1?System.Text.RegularExpressions.Regex.Replace(lines[1],"<[^>]+>",""):item.baseData is ConsumableItemData?"소비 아이템":"아이템";
        if(item.baseData is MapItemData)subtitle.text+=" · 레벨 "+Mathf.Clamp(item.mapState!=null?item.mapState.level:item.level,1,100);
        if(item.baseData is WeaponItemData){
            string element=OverburstElementRules.Label(item.ResolvedElement);
            if(!string.IsNullOrEmpty(element)&&!subtitle.text.Contains(" · "+element))subtitle.text+=" · "+element;
        }
        // 2026-10-01: 아이템 레벨은 종류 줄 아래 둘째 줄로 내린다(사용자 지정).
        if(item.baseData is ElementGemItemData||item.baseData is WeaponItemData||item.baseData is GearItemData||item.baseData is FlaskItemData||item.baseData is BagItemData)
            subtitle.text+="\n아이템 레벨 "+OverburstGrowthRules.ClampLevel(item.level);
        if(approvedSkin&&approvedSkin.TryPresent(item,priceOverride,lines,body))return;
        OverburstTooltipHybridSkin.FormatLegacyBody(item,priceOverride,lines,body);
        body.enabled=true;
        itemSlot.Present(item);
        title.ForceMeshUpdate();float header=Mathf.Max(128,title.GetPreferredValues(title.text,248,0).y+98);
        subtitle.rectTransform.anchoredPosition=new Vector2(144,-(40+title.GetPreferredValues(title.text,248,0).y+8));
        gradeLabel.rectTransform.anchoredPosition=new Vector2(144,-(header-22));
        separator.rectTransform.anchoredPosition=new Vector2(40,-(header+20));
        body.rectTransform.anchoredPosition=new Vector2(40,-(header+38));
        float height=body.GetPreferredValues(body.text,352,0).y;
        body.rectTransform.sizeDelta=new Vector2(352,height+4);Rect.sizeDelta=new Vector2(432,header+38+height+44);
    }
}
