using UnityEngine;
using UnityEngine.UI;

public static partial class OverburstUIWorkshopBuilder
{
    private static void AttachGrade(Transform slot,ItemGrade grade)
    {
        var existing=slot.GetComponent<OverburstUISlotGradePreview>();if(existing){existing.Present(grade);return;}
        var icon=slot.Find("Icon")?.GetComponent<Image>();if(!icon)return;
        var frame=Rect("Editor Grade Color",slot,Vector2.zero,Vector2.zero);Stretch(frame);frame.offsetMin=Vector2.one*6;frame.offsetMax=Vector2.one*-6;
        for(int i=0;i<4;i++){
            var edge=Image("Edge "+i,frame,null,GradeConfig.GetGradeColor(grade),Vector2.zero).rectTransform;
            edge.anchorMin=new Vector2(i==1?1:0,i==2?1:0);edge.anchorMax=new Vector2(i==0?0:1,i==3?0:1);edge.offsetMin=edge.offsetMax=Vector2.zero;
            if(i<2)edge.sizeDelta=new Vector2(1.5f,0);else edge.sizeDelta=new Vector2(0,1.5f);
        }
        slot.gameObject.AddComponent<OverburstUISlotGradePreview>().Configure(icon,grade,frame.gameObject);
    }
    private static GameObject BuildGradeSpecimen(Transform parent)
    {
        var root=Rect("PF_OverburstGradeSpecimen_Rpg11",parent,Vector2.zero,new Vector2(1920,1080));Stretch(root);
        Label("Heading",root,new Vector2(0,340),new Vector2(1200,50),"아이템 크기 · 등급 효과",28,Gold);
        var note=Label("Note",root,new Vector2(0,296),new Vector2(1200,32),"동일 아이콘 / 기존 등급 배경 · 외곽선 · 실드 · 상단 VFX",16,Muted);note.font=bodyFont;
        float[] sizes={68,84,100};
        for(int col=0;col<8;col++){var label=Label("Grade "+col,root,new Vector2(-420+col*120,224),new Vector2(110,30),ItemTooltipFormatter.GetGradeName((ItemGrade)col),17,GradeConfig.GetGradeColor((ItemGrade)col));label.font=bodyFont;}
        for(int row=0;row<3;row++){
            var label=Label("Size "+row,root,new Vector2(-650,130-row*160),new Vector2(170,36),sizes[row]+" × "+sizes[row],17,Ivory);label.font=bodyFont;
            for(int col=0;col<8;col++){
                var slot=SharedSlot(root,"Grade "+col+" Size "+sizes[row],ItemIcon(0),(ItemGrade)col);var r=(RectTransform)slot.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(-420+col*120,130-row*160);r.localScale=Vector3.one*(sizes[row]/84f);
            }
        }
        return SaveAndReplace(root.gameObject,"PF_OverburstGradeSpecimen_Rpg11",parent);
    }
}
