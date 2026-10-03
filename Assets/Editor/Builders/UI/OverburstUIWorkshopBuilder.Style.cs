using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using Object=UnityEngine.Object;

public static partial class OverburstUIWorkshopBuilder
{
    // Window dimensions are design pixels at 1920x1080; vendor frames use a common 0.5 scale.
    private static Font bodyFont;
    private const float WindowScale=.5f;
    private const float WindowHeight=776;
    private const float Cell=84, Gap=8;
    private static readonly Color Muted=new Color(.74f,.71f,.66f);
    private static readonly Color RuleColor=new Color(.34f,.30f,.23f,.65f);
    private static void Box(RectTransform r,float x,float y,float w,float h)
    {r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.localScale=Vector3.one;r.anchoredPosition=new Vector2(x*2,-y*2);r.sizeDelta=new Vector2(w*2,h*2);}
    private static Text Type(Transform parent,string name,string value,float x,float y,float width,float height,int size,Color color,TextAnchor alignment=TextAnchor.MiddleLeft,bool heading=false)
    {var t=Label(name,parent,Vector2.zero,Vector2.one,value,size*2,color,alignment);Box(t.rectTransform,x,y,width,height);t.font=heading?font:bodyFont;t.lineSpacing=1;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Truncate;return t;}
    private static void Rule(Transform parent,string name,float x,float y,float w)
    {var im=Image(name,parent,null,RuleColor,Vector2.one);Box(im.rectTransform,x,y,w,.5f);}
    private static RectTransform Panel(Transform parent,string name,float x,float y,float w,float h)
    {var r=Rect(name,parent,Vector2.zero,Vector2.one);Box(r,x,y,w,h);return r;}
    private static void TextStyle(Text t,int px,Color color,bool heading=false)
    {t.font=heading?font:bodyFont;t.fontSize=px;t.color=color;t.lineSpacing=1;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Overflow;t.supportRichText=false;}

    private static void PolishWindow(GameObject go,string name)
    {
        bool bag=name.Contains("Inventory"),gear=name.Contains("Equipment");float width=bag?608:gear?760:896;
        var r=(RectTransform)go.transform;r.sizeDelta=new Vector2(width*2,WindowHeight*2);r.localScale=Vector3.one*WindowScale;r.anchoredPosition=new Vector2(bag?432:gear?-268:-336,64);
        var surface=Image("Opaque Content Surface",go.transform,null,new Color(.047f,.043f,.040f,1),Vector2.one);Box(surface.rectTransform,5,74,width-10,WindowHeight-80);surface.transform.SetAsFirstSibling();
        var header=(RectTransform)go.transform.Find("Header");header.sizeDelta=new Vector2(0,152);
        var title=header.Find("Text").GetComponent<Text>();Box(title.rectTransform,64,14,width-128,44);TextStyle(title,52,Gold,true);title.alignment=TextAnchor.MiddleCenter;
        var close=(RectTransform)header.Find("Button (Close)");Box(close,width-58,20,36,36);
        var fg=(RectTransform)close.Find("Foreground");fg.anchorMin=fg.anchorMax=fg.pivot=new Vector2(.5f,.5f);fg.anchoredPosition=Vector2.zero;fg.sizeDelta=new Vector2(54,54);
        var ci=(RectTransform)close.Find("Icon");ci.anchorMin=ci.anchorMax=ci.pivot=new Vector2(.5f,.5f);ci.anchoredPosition=Vector2.zero;ci.sizeDelta=new Vector2(24,24);
        // Remove stretched ornamental glows, preserving the vendor header artwork and frame.
        Off(go,"Header/Background/Effect Left");Off(go,"Header/Background/Effect Right");
        if(gear){PolishEquipment(go);return;}
        if(bag)PolishInventory(go);else PolishStash(go);
    }
    private static void GridSpec(Transform grid,int columns,float x,float y)
    {
        Box((RectTransform)grid,x,y,columns*Cell+(columns-1)*Gap,7*Cell+6*Gap);
        var layout=grid.GetComponent<GridLayoutGroup>();layout.cellSize=Vector2.one*Cell*2;layout.spacing=Vector2.one*Gap*2;layout.padding=new RectOffset();layout.childAlignment=TextAnchor.UpperLeft;layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=columns;
        int slotIndex=0;foreach(Transform slot in grid){AttachGrade(slot,(ItemGrade)(slotIndex++%8));var icon=slot.Find("Icon")?.GetComponent<Image>();if(icon!=null){icon.rectTransform.anchorMin=Vector2.zero;icon.rectTransform.anchorMax=Vector2.one;icon.rectTransform.offsetMin=Vector2.one*12;icon.rectTransform.offsetMax=Vector2.one*-12;}foreach(var text in slot.GetComponentsInChildren<Text>(true))TextStyle(text,24,Ivory);}
    }
    private static void PolishInventory(GameObject go)
    {
        Off(go,"Inventory Subtitle");Off(go,"Capacity");Off(go,"Content/Currencies Group");
        var grid=go.transform.Find("Content/Slots Grid");SharedGrid(grid,6,32,144,17);
        var layer=Panel(go.transform,"Layout",0,0,608,WindowHeight);
        Type(layer,"Section","소지품",32,94,200,28,17,Gold,TextAnchor.MiddleLeft,true);Type(layer,"Capacity","17 / 42",468,94,108,28,14,Muted,TextAnchor.MiddleRight);
        Rule(layer,"Top Rule",32,128,544);Rule(layer,"Footer Rule",32,708,544);
        Type(layer,"Currency Label","보유 골드",32,726,120,26,14,Muted);Type(layer,"Currency Value","12,840",408,724,168,28,18,Ivory,TextAnchor.MiddleRight);
    }
    private static void PolishStash(GameObject go)
    {
        var storageGrid=go.transform.Find("Storage Grid"); storageGrid=SharedGrid(storageGrid,9,38,144,12);
        var tabs=(RectTransform)go.transform.Find("Tab Menu");tabs.anchoredPosition=new Vector2(0,-152);tabs.sizeDelta=new Vector2(-4,132);
        var group=tabs.Find("Buttons Group");var grid=group.GetComponent<HorizontalLayoutGroup>();if(grid!=null){grid.padding=new RectOffset(64,64,0,0);grid.spacing=12;}
        for(int i=0;i<3;i++){
            var tab=group.Find("Tab Button ("+(i+1)+")");var t=tab.Find("Text").GetComponent<Text>();TextStyle(t,32,Gold,true);
            var active=tab.Find("Active");foreach(var im in active.GetComponentsInChildren<Image>(true)){if(im.name=="Overlay")im.color=new Color(1,.82f,.56f,.3f);if(im.name=="Arrow")im.color=new Color(.78f,.61f,.34f,.85f);}
        }
        Off(go,"Footer Rule");Rule(go.transform,"Aligned Footer Rule",38,708,820);
        Type(go.transform,"Storage Caption","개인 보관함",38,726,220,26,14,Muted);
        var hint=go.transform.Find("Storage Hint").GetComponent<Text>();Box(hint.rectTransform,682,726,176,26);TextStyle(hint,28,Ivory);hint.alignment=TextAnchor.MiddleRight;
        go.GetComponent<OverburstUIStashPreview>().Configure(storageGrid.Cast<Transform>().Select(t=>t.Find("Icon").GetComponent<Image>()).ToArray(),Enumerable.Range(0,6).Select(ItemIcon).ToArray(),Enumerable.Range(1,3).Select(i=>group.Find("Tab Button ("+i+")/Active").GetComponent<Image>()).ToArray(),hint);
    }
    private static void PolishEquipment(GameObject go)
    {
        
        var originalCharacter=AssetDatabase.LoadAssetAtPath<GameObject>(Vendor+"Prefabs/Windows/Window (Character).prefab");
        var templates=originalCharacter.transform.Find("Content/Character Content/Equip Slots");
        var equipmentTemplates=templates.GetComponentsInChildren<RectTransform>(true).Where(t=>t.name.StartsWith("Equip Slot (")).ToArray();
        Off(go,"Content");Off(go,"Tab Menu");
        var content=Panel(go.transform,"Layout",0,0,760,WindowHeight);
        Type(content,"Loadout Heading","장착 장비",28,94,444,28,17,Gold,TextAnchor.MiddleCenter,true);
        Type(content,"Stats Heading","능력치",528,94,204,28,17,Gold,TextAnchor.MiddleLeft,true);
        Rule(content,"Loadout Rule",28,136,444);Rule(content,"Stats Rule",528,136,204);
        var divider=Image("Column Divider",content,null,RuleColor,Vector2.one);Box(divider.rectTransform,500,98,.5f,652);
        Type(content,"Character Name","OVERBURST",120,154,260,28,18,Ivory,TextAnchor.MiddleCenter,true);
        Type(content,"Character Detail","레벨 18  ·  한손검",120,186,260,24,14,Muted,TextAnchor.MiddleCenter);
        var portrait=Panel(content,"Player 3D Preview",124,216,252,378);
        var image=portrait.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
        portrait.gameObject.AddComponent<OverburstUICharacterPreview>().Configure(characterPreview,AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/ProjectOverburst/03_Features/Player/Animations/Fixed/Idle_JawFixed.anim"),image);
        string[] names={"투구","갑옷","장갑","신발","목걸이","귀걸이","원소보석","가방"};
        string[] types={"Helmet","Chest","Gloves","Boots","Necklace","Earring","Earring","Belt"};
        for(int i=0;i<8;i++){
            var source=i==7?null:equipmentTemplates.First(t=>t.name=="Equip Slot ("+types[i]+")");
            var slot=SharedSlot(content,"Slot • "+names[i],null,ItemGrade.Common);
            float x=i<4?28:388,y=184+(i%4)*106;Box((RectTransform)slot.transform,x,y,84,84);((RectTransform)slot.transform).sizeDelta=Vector2.one*84;slot.transform.localScale=Vector3.one*2;
            var icon=slot.transform.Find("Slot Icon").GetComponent<Image>();icon.sprite=source?source.Find("Slot Icon").GetComponent<Image>().sprite:null;icon.gameObject.SetActive(source!=null);
            Type(content,"Label • "+names[i],names[i],x,y+84,84,22,14,Muted,TextAnchor.MiddleCenter);
        }
        Rule(content,"Weapon Rule",28,624,444);
        for(int i=0;i<4;i++){
            var slot=SharedSlot(content,i==0?"Weapon Slot":"Flask Slot "+i,ItemIcon(i),(ItemGrade)(i+2));Box((RectTransform)slot.transform,28+i*120,640,84,84);((RectTransform)slot.transform).sizeDelta=Vector2.one*84;slot.transform.localScale=Vector3.one*2;
            Type(content,"Loadout Label "+i,i==0?"무기":"물약 "+i,28+i*120,728,84,22,14,Muted,TextAnchor.MiddleCenter);
        }
        string[][] labels={new[]{"최대 체력","원소 에너지","방어력","이동 속도"},new[]{"공격력","공격 속도","치명타 확률","치명타 피해"},new[]{"화염 피해","얼음 피해","번개 피해","물 피해"}};
        string[][] values={new[]{"1,200","65 / 100","—","5.0"},new[]{"128–162","1.25","12%","150%"},new[]{"0","0","32","0"}};
        string[] categories={"기본","공격","원소"};
        for(int group=0;group<3;group++){
            float y=158+group*170;Type(content,"Category "+group,categories[group],528,y,204,22,14,Gold);
            for(int i=0;i<4;i++){
                Type(content,"Stat Label "+group+" "+i,labels[group][i],528,y+28+i*28,132,26,16,Muted);
                Type(content,"Stat Value "+group+" "+i,values[group][i],660,y+28+i*28,72,26,16,Ivory,TextAnchor.MiddleRight);
            }
            if(group<2)Rule(content,"Stat Separator "+group,528,y+142,204);
        }
        // Keep the vendor button, aligned to the same footer baseline as the other windows.
        var original=go.transform.Find("Content/Character Content/Button (Inventory)");var button=Object.Instantiate(original.gameObject,content,false);button.name="Inventory Button";button.SetActive(true);Box((RectTransform)button.transform,560,716,172,44);
        var text=button.GetComponentInChildren<Text>(true);TextStyle(text,28,Gold);text.text="인벤토리 열기";Stretch(text.rectTransform);text.alignment=TextAnchor.MiddleCenter;
        Object.DestroyImmediate(go.transform.Find("Content").gameObject);Object.DestroyImmediate(go.transform.Find("Tab Menu").gameObject);
    }
    private static void PolishHud(GameObject go)
    {
        var action=(RectTransform)go.transform.Find("Action Bar");
        var backplate=action.Find("Slot Backplate") as RectTransform;
        if(!backplate){
            var backing=Image("Slot Backplate",action,null,new Color(.075f,.064f,.058f,.98f),new Vector2(1644,140));
            backplate=backing.rectTransform;
            backplate.SetAsFirstSibling();
        }
        backplate.GetComponent<Image>().sprite=null;
        backplate.GetComponent<Image>().color=new Color(.075f,.064f,.058f,.98f);
        backplate.anchorMin=backplate.anchorMax=new Vector2(.5f,0);
        backplate.pivot=new Vector2(.5f,0);
        backplate.anchoredPosition=new Vector2(0,20);
        backplate.sizeDelta=new Vector2(1644,140);
        action.anchoredPosition=new Vector2(0,14);
        var region=go.transform.Find("Current Region");var title=region.Find("Region Name").GetComponent<Text>();TextStyle(title,26,Gold,true);
        var detail=region.Find("Region Details").GetComponent<Text>();TextStyle(detail,16,Muted);detail.lineSpacing=1.35f;
        var unit=go.transform.Find("Action Bar Unit Frame");
        ((RectTransform)unit).anchoredPosition+=new Vector2(0,14);
        foreach(var barName in new[]{"Bar (Health)","Bar (Power)"}){
            var group=unit.Find(barName+"/Text Group");if(!group)continue;
            var layout=group.GetComponent<HorizontalLayoutGroup>();if(layout)Object.DestroyImmediate(layout);
            var label=group.Find("Label Text").GetComponent<Text>();var value=group.Find("Percentage Text").GetComponent<Text>();
            var r=(RectTransform)group;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(26,0);r.offsetMax=new Vector2(-26,0);
            foreach(var t in new[]{label,value}){var tr=t.rectTransform;tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;TextStyle(t,24,Ivory);}
            label.text="";value.alignment=barName=="Bar (Health)"?TextAnchor.MiddleRight:TextAnchor.MiddleLeft;
            if(barName=="Bar (Health)"){value.text="1,200 / 1,200 체력";value.rectTransform.offsetMax=new Vector2(-50,0);}else{value.rectTransform.offsetMin=new Vector2(50,0);value.text="원소 에너지 65 / 100";}
        }
        foreach(var t in unit.GetComponentsInChildren<Text>(true))if(t.transform.parent.name=="Level Frame"){TextStyle(t,28,Ivory,true);CenterLevelNumber(t);}
        var slots=go.transform.Find("Action Bar/Main Slots/Grid");foreach(var child in slots.CastChildren())Object.DestroyImmediate(child.gameObject);
        var sr=(RectTransform)slots;sr.anchorMin=sr.anchorMax=sr.pivot=new Vector2(.5f,.5f);sr.anchoredPosition=Vector2.zero;sr.sizeDelta=new Vector2(912,84);sr.localScale=Vector3.one/.58f;
        var quickLayout=slots.GetComponent<GridLayoutGroup>();quickLayout.cellSize=Vector2.one*84;quickLayout.spacing=new Vector2(8,0);quickLayout.padding=new RectOffset();quickLayout.constraintCount=10;
        for(int i=0;i<10;i++)SharedSlot(slots,"Quick Slot "+((i+1)%10),i<4?ItemIcon(i+1):null,(ItemGrade)((i+2)%8),((i+1)%10).ToString());
        action.sizeDelta=new Vector2(1644,186);
        var map=go.transform.Find("Circular Minimap");map.GetComponent<RectTransform>().localScale=Vector3.one*.50f;map.GetComponent<RectTransform>().anchoredPosition=new Vector2(-164,-188);
        TextStyle(map.Find("Zone Name Text").GetComponent<Text>(),32,Gold,true);
        var quest=go.transform.Find("Quest Tracker");if(quest){
            var backdrop=quest.Find("Objective Backdrop") as RectTransform;
            if(!backdrop)backdrop=Image("Objective Backdrop",quest,null,new Color(.05f,.035f,.03f,.78f),new Vector2(460,180)).rectTransform;
            backdrop.SetAsFirstSibling();backdrop.anchorMin=backdrop.anchorMax=backdrop.pivot=new Vector2(.5f,1);backdrop.anchoredPosition=Vector2.zero;backdrop.sizeDelta=new Vector2(460,180);
            foreach(var t in quest.GetComponentsInChildren<Text>(true))TextStyle(t,t.name.Contains("Header")?32:28,Muted);
        }
    }
    private static void PolishEnemy(GameObject go)
    {
        TextStyle(go.transform.Find("Unit Name Text").GetComponent<Text>(),25,Ivory);
        TextStyle(go.transform.Find("Percentage Text").GetComponent<Text>(),24,Ivory);
        var level=go.transform.Find("Level Frame/Text").GetComponent<Text>();TextStyle(level,28,Ivory,true);CenterLevelNumber(level);
    }
    public static void CenterLevelNumber(Text text)
    {
        var rect=text.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=rect.offsetMax=Vector2.zero;
        text.alignment=TextAnchor.MiddleCenter;text.resizeTextForBestFit=false;
        var generator=new TextGenerator();generator.Populate(text.text,text.GetGenerationSettings(rect.rect.size));
        float low=float.MaxValue,high=float.MinValue;
        for(int i=0;i<generator.verts.Count-4;i++){low=Mathf.Min(low,generator.verts[i].position.y);high=Mathf.Max(high,generator.verts[i].position.y);}
        if(low<=high)rect.anchoredPosition=new Vector2(0,-(low+high)*.5f/text.pixelsPerUnit);
    }
    private static void PolishNotifications(GameObject go)
    {
        foreach(var t in go.GetComponentsInChildren<Text>(true)){
            bool title=t.text=="새로운 구역 발견"||t.text=="레벨 18";TextStyle(t,title?52:30,title?Gold:Ivory,title);t.alignment=TextAnchor.MiddleCenter;
        }
    }
    private static void PolishWorkshop(GameObject canvasGo)
    {
        // Controls belong to the Editor panel; the preview itself shows only game UI.
        foreach(var name in new[]{"90 Workshop Controls • EditorOnly","Mode","Workshop Stamp"}){var t=canvasGo.transform.Find(name);if(t)t.gameObject.SetActive(false);}
        canvasGo.name="00_PlayPreview • 게임 화면 조합";
    }
}

