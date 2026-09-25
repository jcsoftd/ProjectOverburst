using System;
using System.Linq;
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
    public void Present(ItemData item,string priceOverride=null){
        if(item==null||!item.baseData){gameObject.SetActive(false);return;}
        gameObject.SetActive(true);title.text=item.itemName;title.color=new Color(.88f,.74f,.48f);
        gradeLabel.text=ItemTooltipFormatter.GetGradeName(item.grade);gradeLabel.color=GradeConfig.GetGradeColor(item.grade);
        if(!approvedSkin)approvedSkin=GetComponent<OverburstTooltipHybridSkin>();
        if(!body.spriteAsset)body.spriteAsset=Resources.Load<TMP_SpriteAsset>("OverburstUI/QualityDiamonds");
        string[] lines=SimpleItemTooltipBuilder.Build(item).Replace("\r","").Split('\n');
        bool hasSubtitle=item.baseData is WeaponItemData||item.baseData is FlaskItemData||item.baseData is BagItemData||item.baseData is ComboGemItemData||item.baseData is ConsumableItemData;
        subtitle.text=hasSubtitle&&lines.Length>1?System.Text.RegularExpressions.Regex.Replace(lines[1],"<[^>]+>",""):item.baseData is ConsumableItemData?"소비 아이템":"아이템";
        if(item.baseData is WeaponItemData){
            string element=OverburstElementRules.Label(item.ResolvedElement);
            if(!string.IsNullOrEmpty(element)&&!subtitle.text.Contains(" · "+element))subtitle.text+=" · "+element;
        }
        string detail=string.Join("\n",lines.Skip(hasSubtitle?2:1)).Replace("<color=#7A6A4B>--------------------------</color>","").Trim();
        var formatted=new System.Text.StringBuilder();bool gap=false;
        foreach(var raw in detail.Split('\n')){
            if(string.IsNullOrWhiteSpace(raw)){if(!gap&&formatted.Length>0)formatted.AppendLine("<line-height=10> </line-height><line-height=26>");gap=true;continue;}gap=false;
            string plain=System.Text.RegularExpressions.Regex.Replace(raw,"<[^>]+>","");
            if(priceOverride!=null&&plain.StartsWith("가치")){formatted.AppendLine("<color=#BBB6AB>거래 가격</color><pos=110><color=#EEE7D5>"+priceOverride+"</color>");continue;}
            // Status and body remain separate from the stat comparison.
            if(plain=="최종 효과"||plain=="장비칸에 장착하면 사용할 수 있습니다."||plain=="One-handed melee weapon.")continue;
            var row=System.Text.RegularExpressions.Regex.Match(plain,@"^(.{1,14}?)\s*:?\s+([(+\-−]?\d.*)$");
            if(row.Success){
                string value=System.Text.RegularExpressions.Regex.Replace(row.Groups[2].Value,@"[★◆].*$","").Trim();
                string marks=string.Join("",System.Text.RegularExpressions.Regex.Matches(raw,@"(?:<color=[^>]+>)?[★◆]+(?:</color>)?").Cast<System.Text.RegularExpressions.Match>().Select(m=>m.Value.Replace("★","◆")));
                bool breakdown=OverburstUIQualityBreakdown.TryGet(item,row.Groups[1].Value,out var change,out var exact,out var baseline,out var delta,out var improved);
                if(breakdown){
                    string color=improved?"#8BD0A6":"#E29A8E";
                    formatted.Append("<color=#BBB6AB>").Append(row.Groups[1].Value).Append("</color><pos=110><color=#EEE7D5>").Append(exact.TrimStart('+')).Append("</color>");
                    if(delta!="—")formatted.Append(" <size=80%><color=").Append(color).Append(">(").Append(delta).Append(")</color></size>");
                    if(marks.Length>0){
                        marks=marks.Replace("#F2F2F2","#D5D8D8").Replace("#59FF59","#68AA84").Replace("#FFD84A","#D2A85D").Replace("#FF4A4A","#E29A8E");
                        if(body.spriteAsset)marks=System.Text.RegularExpressions.Regex.Replace(marks,@"(?:<color=(#[A-Fa-f0-9]+)>)?(◆+)(?:</color>)?",m=>{
                            string tint=m.Groups[1].Value; int index=tint=="#68AA84"?1:tint=="#D2A85D"?2:tint=="#E29A8E"?3:0;
                            return string.Concat(Enumerable.Repeat("<sprite index="+index+" tint=0>",m.Groups[2].Value.Length));
                        });
                        string diamonds="<size=86%>"+marks+"</size>";
                        float markerWidth=body.GetPreferredValues(diamonds,1000,0).x;
                        if(markerWidth>90){diamonds="<size="+(86f*90/markerWidth).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"%>"+marks+"</size>";markerWidth=body.GetPreferredValues(diamonds,1000,0).x;}
                        formatted.Append("<pos=").Append(Mathf.Max(260,350-markerWidth).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)).Append('>').Append(diamonds);
                    }
                    formatted.AppendLine();
                }else{formatted.Append("<color=#BBB6AB>").Append(row.Groups[1].Value).Append("</color><pos=110><color=#EEE7D5>").Append(value).AppendLine("</color>");}
            }else formatted.AppendLine(raw.Replace("#9EAAB5","#BBB6AB").Replace("#F2D48C","#EEE7D5"));
        }
        body.text="<line-height=26>"+formatted.ToString().Trim()+"</line-height>";
        if(approvedSkin&&approvedSkin.TryPresent(item,priceOverride,lines,body))return;
        body.enabled=true;
        itemSlot.Present(item.icon,item.grade);
        title.ForceMeshUpdate();float header=Mathf.Max(128,title.GetPreferredValues(title.text,248,0).y+98);
        subtitle.rectTransform.anchoredPosition=new Vector2(144,-(40+title.GetPreferredValues(title.text,248,0).y+8));
        gradeLabel.rectTransform.anchoredPosition=new Vector2(144,-(header-22));
        separator.rectTransform.anchoredPosition=new Vector2(40,-(header+20));
        body.rectTransform.anchoredPosition=new Vector2(40,-(header+38));
        float height=body.GetPreferredValues(body.text,352,0).y;
        body.rectTransform.sizeDelta=new Vector2(352,height+4);Rect.sizeDelta=new Vector2(432,header+38+height+44);
    }
}
