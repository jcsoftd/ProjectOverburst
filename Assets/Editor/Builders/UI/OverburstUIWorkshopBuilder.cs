using System;
using System.IO;
using System.Linq;
using DuloGames.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class OverburstUIWorkshopBuilder
{
    public const string ScenePath = "Assets/ProjectOverburst/00_Scenes/DEV_UIManagement.unity";
    public const string Root = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11";
    private const string Vendor = "Assets/ThirdParty/RPG and MMO UI 11/";
    private static Font font;
    private static readonly Color Gold = new Color(.88f,.74f,.48f,1);
    private static readonly Color Ivory = new Color(.93f,.90f,.83f,1);

    [MenuItem("OVERBURST/UI/Open UI Management Workshop")]
    public static void Open()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
        else Build();
    }

    public static string Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Cannot author during Play Mode.");
        for (int i=0;i<SceneManager.sceneCount;i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scene changes before building the workshop.");
        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        bodyFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-VariableFont_wght.ttf");
        if (font == null) throw new InvalidOperationException("Korean UI font missing.");
        BuildSharedSlot();
        BuildCharacterPreviewModel();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("UI Preview Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0,0,-10); camera.orthographic=true; camera.orthographicSize=5;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.055f,.038f,.032f);
        camera.cullingMask=1<<5; camera.nearClipPlane=.1f; camera.farClipPlane=100; camera.tag="MainCamera";
        var canvasGo = new GameObject("UI Management • 1920x1080", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.layer=5;
        var canvas=canvasGo.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=10;
        var scaler=canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
        var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
        var module=events.GetComponent<InputSystemUIInputModule>(); module.UnassignActions(); module.AssignDefaultActions();
        var background=Rect("00 Preview Backdrop",canvas.transform,Vector2.zero,new Vector2(1920,1080)); Stretch(background);
        var raw=background.gameObject.AddComponent<RawImage>(); raw.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Vendor+"Textures/Demo/Background.png"); raw.raycastTarget=false; raw.color=new Color(.72f,.72f,.72f,1);
        var hud=BuildHud(canvas.transform);
        var windowLayer=Rect("20 Windows • Connected Project Prefabs",canvas.transform,Vector2.zero,new Vector2(1920,1080)); Stretch(windowLayer);
        var bag=BuildInventory(windowLayer);
        var equipment=BuildEquipment(windowLayer);
        var stash=BuildStash(windowLayer);
        var enemies=BuildEnemies(canvas.transform);
        var notices=BuildNotices(canvas.transform);
        var grades=BuildGradeSpecimen(canvas.transform);
        var toolbar=Rect("90 Workshop Controls • EditorOnly",canvas.transform,new Vector2(0,-28),new Vector2(1250,48));
        toolbar.anchorMin=toolbar.anchorMax=new Vector2(.5f,1); toolbar.gameObject.tag="EditorOnly";
        var controller=canvasGo.AddComponent<OverburstUIWorkshop>();
        var mode=Label("Mode",canvas.transform,new Vector2(0,-65),new Vector2(1150,24),"",14,Ivory);
        mode.rectTransform.anchorMin=mode.rectTransform.anchorMax=new Vector2(.5f,1); mode.gameObject.tag="EditorOnly";
        controller.Configure(bag,equipment,stash,enemies,notices,mode);controller.ConfigureGrades(grades);
        string[] captions={"HUD","인벤토리","장비 · 능력치","함께 보기","몬스터 체력바","창고","알림","위치 초기화"};
        UnityEngine.Events.UnityAction[] actions={controller.ShowHud,controller.ShowInventory,controller.ShowEquipment,controller.ShowComparison,controller.ShowEnemies,controller.ShowStash,controller.ShowNotifications,controller.ResetLayout};
        for(int i=0;i<captions.Length;i++) UnityEventTools.AddPersistentListener(Button("View "+i,toolbar,new Vector2(-548+i*156,0),new Vector2(148,36),captions[i]).onClick,actions[i]);
        Label("Workshop Stamp",canvas.transform,new Vector2(20,20),new Vector2(410,24),"UI 미리보기 · 표시 수치는 예시",14,new Color(.6f,.53f,.43f)).rectTransform.anchorMin=Vector2.zero;
        var stamp=canvas.transform.Find("Workshop Stamp") as RectTransform; stamp.anchorMax=Vector2.zero; stamp.pivot=Vector2.zero; stamp.gameObject.tag="EditorOnly";
        controller.ShowComparison();
        UnityEventTools.AddPersistentListener(equipment.transform.Find("Layout/Inventory Button").GetComponent<Button>().onClick,bag.Show);
        PolishWorkshop(canvasGo);
        BuildTooltipPreview(canvasGo,controller);
        BuildGallery();
        Canvas.ForceUpdateCanvases();
        foreach(var scroll in Object.FindObjectsByType<ScrollRect>(FindObjectsInactive.Include,FindObjectsSortMode.None)){scroll.verticalNormalizedPosition=1;scroll.content.anchoredPosition=Vector2.zero;PrefabUtility.RecordPrefabInstancePropertyModifications(scroll.content);}
        EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
        Selection.activeGameObject=canvasGo;
        return "CREATED "+ScenePath+"; project prefabs="+AssetDatabase.FindAssets("t:Prefab",new[]{Root}).Length;
    }

    private static GameObject BuildHud(Transform parent)
    {
        var hud=Rect("PF_OverburstHUD_Rpg11",parent,Vector2.zero,new Vector2(1920,1080)); Stretch(hud);
        var action=Instance("HUD/Action Bar",hud); Clean(action);
        var ar=(RectTransform)action.transform; ar.anchorMin=ar.anchorMax=new Vector2(.5f,0); ar.anchoredPosition=Vector2.zero; ar.sizeDelta=new Vector2(1530,186); ar.localScale=Vector3.one*.58f;
        var xp=action.transform.Find("XP Bar/Fill Rect") as RectTransform; xp.sizeDelta=new Vector2(1556,14);
        (xp.Find("Fill Mask") as RectTransform).sizeDelta=new Vector2(1556*.65f,14);
        (xp.Find("Fill Mask/Fill") as RectTransform).sizeDelta=new Vector2(1556,14);
        var grid=action.transform.Find("Main Slots/Grid");
        for(int i=0;i<grid.childCount;i++) grid.GetChild(i).gameObject.SetActive(i<10);
        var layout=grid.GetComponent<GridLayoutGroup>(); layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount=10; layout.cellSize=new Vector2(144,144); layout.spacing=new Vector2(6,0);
        for(int i=0;i<10;i++)
        {
            var slot=grid.GetChild(i); var hotkey=slot.Find("Hotkey/Hotkey Text").GetComponent<Text>(); hotkey.text=((i+1)%10).ToString(); hotkey.fontSize=30; hotkey.color=Color.white; slot.Find("Hotkey").SetAsLastSibling();
            var icon=slot.Find("Icon")?.GetComponent<Image>(); if(icon!=null) { icon.sprite=ItemIcon(i+1); icon.gameObject.SetActive(i<4); icon.preserveAspect=true; }
        }
        Off(action,"Test Assign Slots"); Off(action,"XP Bar/Tooltip");
        var unit=Instance("HUD/Unit Frames/Action Bar Unit Frame",hud); Clean(unit);
        var ur=(RectTransform)unit.transform; ur.localScale=Vector3.one*.58f; ur.anchoredPosition=new Vector2(0,258*.58f);
        TextAt(unit,"Bar (Health)/Text Group/Label Text","체력"); TextAt(unit,"Bar (Health)/Text Group/Percentage Text","100%");
        TextAt(unit,"Bar (Power)/Text Group/Label Text","원소 에너지"); TextAt(unit,"Bar (Power)/Text Group/Percentage Text","65%"); TextAt(unit,"Level Frame/Text","18");
        var power=unit.transform.Find("Bar (Power)/Fill").GetComponent<Image>(); power.type=UnityEngine.UI.Image.Type.Filled; power.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal; power.fillAmount=.65f;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Vendor+"Prefabs/HUD/Unit Frames/Unit Frame (Player).prefab");
        var buffs=Object.Instantiate(source.transform.Find("Buffs Grid").gameObject,hud,false); buffs.name="Buffs • Above Health"; Clean(buffs);
        var br=(RectTransform)buffs.transform; br.anchorMin=br.anchorMax=new Vector2(.5f,0); br.pivot=new Vector2(0,0); br.anchoredPosition=new Vector2(-372,175); br.localScale=Vector3.one*.67f;
        var cf=buffs.GetComponent<ContentSizeFitter>(); if(cf!=null) Object.DestroyImmediate(cf);
        br.sizeDelta=new Vector2(260,56); var bl=buffs.GetComponent<GridLayoutGroup>(); bl.cellSize=new Vector2(50,50); bl.spacing=new Vector2(7,0); bl.constraint=GridLayoutGroup.Constraint.FixedRowCount; bl.constraintCount=1;
        var region=Rect("Current Region",hud,new Vector2(36,-32),new Vector2(410,125)); region.anchorMin=region.anchorMax=region.pivot=new Vector2(0,1);
        Label("Region Name",region,new Vector2(0,-4),new Vector2(400,40),"잊힌 회랑",28,Gold,TextAnchor.MiddleLeft).rectTransform.pivot=new Vector2(0,1);
        var title=region.Find("Region Name") as RectTransform; title.anchorMin=title.anchorMax=new Vector2(0,1);
        var details=Label("Region Details",region,new Vector2(0,-50),new Vector2(400,58),"지하 1층 · 탐험 구역\n지역 특성: 고대 유적",17,Ivory,TextAnchor.UpperLeft).rectTransform; details.anchorMin=details.anchorMax=details.pivot=new Vector2(0,1);
        var line=Image("Separator",region,null,new Color(.43f,.33f,.22f),new Vector2(330,1)); line.rectTransform.anchorMin=line.rectTransform.anchorMax=line.rectTransform.pivot=new Vector2(0,1); line.rectTransform.anchoredPosition=new Vector2(0,-43);
        BuildMinimap(hud);
        var objectives=Instance("HUD/Quest Tracker",hud); Clean(objectives);
        var qr=(RectTransform)objectives.transform; qr.anchorMin=qr.anchorMax=qr.pivot=new Vector2(1,1); qr.anchoredPosition=new Vector2(-34,-380); qr.localScale=Vector3.one*.54f;
        var qt=objectives.GetComponentsInChildren<Text>(true); string[] qtexts={"현재 목표","잊힌 회랑 탐험","다음 구역으로 이동","지역을 탐색하세요"};
        for(int i=0;i<qt.Length;i++){qt[i].text=i<qtexts.Length?qtexts[i]:""; qt[i].fontSize=Math.Max(qt[i].fontSize,28);}
        Off(objectives,"Body/Quests Group/Quest (1)/Objectives/Objective (3)"); Off(objectives,"Body/Quests Group/Quest (2)");
        PolishHud(hud.gameObject);
        return SaveAndReplace(hud.gameObject,"PF_OverburstHUD_Rpg11",parent);
    }

    private static void BuildMinimap(Transform parent)
    {
        var map=Instance("HUD/Minimap",parent); Clean(map); map.name="Circular Minimap";
        var r=(RectTransform)map.transform; r.anchorMin=r.anchorMax=new Vector2(1,1); r.pivot=new Vector2(.5f,.5f); r.anchoredPosition=new Vector2(-184,-205); r.localScale=Vector3.one*.58f;
        Off(map,"Glow Top"); Off(map,"Glow Bottom"); Off(map,"Background"); Off(map,"Overlay");
        var mask=map.transform.Find("Mask").GetComponent<Image>(); mask.sprite=Sprite("Assets/ProjectOverburst/Resources/UI/Minimap/Circle.png"); mask.type=UnityEngine.UI.Image.Type.Simple; mask.GetComponent<Mask>().showMaskGraphic=false;
        TextAt(map,"Zone Name Text","주변 지도");
        var zone=map.transform.Find("Zone Name Text") as RectTransform; var fit=zone.GetComponent<ContentSizeFitter>(); if(fit!=null) Object.DestroyImmediate(fit);
        zone.anchorMin=zone.anchorMax=zone.pivot=new Vector2(.5f,.5f); zone.anchoredPosition=new Vector2(0,270); zone.sizeDelta=new Vector2(450,48); zone.GetComponent<Text>().fontSize=32;
        string framePath="Assets/ProjectOverburst/Resources/UI/Minimap/Frame.png";
        var outer=Image("Bronze Rim",map.transform,Sprite(framePath),new Color(.44f,.36f,.25f),new Vector2(474,474));
        Image("Crimson Rim",map.transform,Sprite(framePath),new Color(.52f,.1f,.09f),new Vector2(458,458));
        Image("Inner Rim",map.transform,Sprite(framePath),new Color(.69f,.56f,.37f),new Vector2(438,438));
        var nameplate=AssetDatabase.LoadAssetAtPath<GameObject>(Vendor+"Prefabs/HUD/Unit Frames/Nameplate.prefab");
        var diamond=nameplate.transform.Find("Level Frame").GetComponent<Image>().sprite;
        foreach(var p in new[]{new Vector2(0,235),new Vector2(0,-235),new Vector2(235,0),new Vector2(-235,0)}) Image("Cardinal Ornament",map.transform,diamond,Color.white,new Vector2(36,36)).rectTransform.anchoredPosition=p;
        var menu=map.transform.Find("Button (Menu)") as RectTransform; menu.anchorMin=menu.anchorMax=menu.pivot=new Vector2(.5f,.5f); menu.anchoredPosition=new Vector2(0,-232); menu.SetAsLastSibling();
        var buttons=map.transform.Find("Button Menu") as RectTransform; buttons.anchorMin=buttons.anchorMax=buttons.pivot=new Vector2(.5f,.5f); buttons.anchoredPosition=new Vector2(245,0); buttons.SetAsLastSibling();
    }

    private static OverburstUIWindow BuildInventory(Transform parent)
    {
        var go=Instance("Windows/Window (Inventory)",parent); Clean(go); go.name="PF_OverburstInventory_Rpg11";
        ((RectTransform)go.transform).sizeDelta=new Vector2(724,1200); PositionWindow(go,new Vector2(390,80),.62f); TextAt(go,"Header/Text","인벤토리");
        Off(go,"Content/Test Assign Slot"); Off(go,"Content/Button (Send)");
        var grid=go.transform.Find("Content/Slots Grid");
        var gr=(RectTransform)grid;gr.anchorMin=gr.anchorMax=gr.pivot=new Vector2(.5f,1);gr.anchoredPosition=new Vector2(0,-300);gr.sizeDelta=new Vector2(664,776);
        var gl=grid.GetComponent<GridLayoutGroup>();gl.cellSize=new Vector2(104,104);gl.spacing=new Vector2(8,8);
        var sub=Label("Inventory Subtitle",go.transform,new Vector2(0,-210),new Vector2(664,48),"소지품",30,Gold,TextAnchor.MiddleLeft);sub.rectTransform.anchorMin=sub.rectTransform.anchorMax=new Vector2(.5f,1);
        var capacity=Label("Capacity",go.transform,new Vector2(0,-210),new Vector2(664,48),"17 / 42",27,Ivory,TextAnchor.MiddleRight);capacity.rectTransform.anchorMin=capacity.rectTransform.anchorMax=new Vector2(.5f,1);
        for(int i=0;i<grid.childCount;i++) FillSlot(grid.GetChild(i),i<17?ItemIcon(i):null,i%4==0?"3":"");
        var currencyTexts=go.transform.Find("Content/Currencies Group").GetComponentsInChildren<Text>(true);
        for(int i=0;i<currencyTexts.Length;i++) currencyTexts[i].text=i==0?"12,840":i==1?"254":"63";
        return FinishWindow(go,parent,"PF_OverburstInventory_Rpg11");
    }

    private static OverburstUIWindow BuildEquipment(Transform parent)
    {
        var go=Instance("Windows/Window (Character)",parent); Clean(go); go.name="PF_OverburstEquipment_Rpg11";
        PositionWindow(go,new Vector2(-210,80),.44f); TextAt(go,"Header/Text","장비 · 능력치");
        Off(go,"Tab Menu/Buttons Group");
        var tab=go.transform.Find("Tab Menu") as RectTransform; Label("Unified Title",tab,Vector2.zero,new Vector2(1250,65),"장착 장비                              최종 능력치",34,Gold);
        Off(go,"Content/Tab Content/Content (Titles)"); Off(go,"Content/Tab Content/Content (Reputations)");
        Off(go,"Content/Character Content/Equip Receiver"); Off(go,"Content/Character Content/Rotation"); Off(go,"Content/Character Content/Equip Slots/Test Assign Slots");
        TextAt(go,"Content/Character Content/Name Text","OVERBURST"); TextAt(go,"Content/Character Content/Title Text","레벨 18 · 한손검");
        TextAt(go,"Content/Character Content/Button (Inventory)/Text","인벤토리");
        var left=go.transform.Find("Content/Character Content/Equip Slots/Left"); var right=go.transform.Find("Content/Character Content/Equip Slots/Right");
        var slots=go.GetComponentsInChildren<RectTransform>(true).Where(t=>t.name.StartsWith("Equip Slot (")).ToDictionary(t=>t.name,t=>t.Find("Slot Icon")?.GetComponent<Image>()?.sprite);
        string[] ln={"투구","갑옷","장갑","신발"}; string[] li={"Helmet","Chest","Gloves","Boots"};
        string[] rn={"목걸이","귀걸이 1","귀걸이 2","가방"}; string[] ri={"Necklace","Earring","Earring","Belt"};
        EquipColumn(left,ln,li,slots); EquipColumn(right,rn,ri,slots);
        var middle=go.transform.Find("Content/Character Content/Equip Slots/Middle") as RectTransform;
        middle.sizeDelta=new Vector2(588,126); var mg=middle.GetComponent<GridLayoutGroup>(); mg.cellSize=new Vector2(126,126); mg.spacing=new Vector2(26,0); mg.constraint=GridLayoutGroup.Constraint.FixedRowCount; mg.constraintCount=1;
        var original=middle.GetChild(0).gameObject; middle.GetChild(1).gameObject.SetActive(false);
        FillSlot(original.transform,ItemIcon(0),""); AddSlotLabel(original.transform,"무기");
        for(int i=0;i<3;i++){var flask=Object.Instantiate(original,middle,false); flask.name="Flask Equipment "+(i+1); FillSlot(flask.transform,ItemIcon(i+1),""); var label=flask.transform.Find("Equipment Label").GetComponent<Text>();label.text="물약 "+(i+1);}
        var content=go.transform.Find("Content/Tab Content/Content (Statistics)/Scroll Rect/Viewport/Content");
        var rows=content.Cast<Transform>().Where(t=>t.Find("Label Text")!=null).ToArray();
        string[] labels={"최대 체력","원소 에너지","공격력","공격 속도","치명타 확률","치명타 피해","이동 속도","화염 피해","얼음 피해","번개 피해","물 피해","방어력"};
        string[] values={"1,200","65 / 100","128–162","1.25","12%","150%","5.0","0","0","32","0","—"};
        for(int i=0;i<rows.Length;i++){rows[i].gameObject.SetActive(i<labels.Length); if(i>=labels.Length)continue; var label=rows[i].Find("Label Text").GetComponent<Text>();label.text=labels[i];label.fontSize=34;label.color=new Color(.73f,.68f,.6f);var value=rows[i].Find("Value Text").GetComponent<Text>();value.text=values[i];value.fontSize=34;value.color=Ivory;}
        return FinishWindow(go,parent,"PF_OverburstEquipment_Rpg11");
    }

    private static void EquipColumn(Transform column,string[] names,string[] iconNames,System.Collections.Generic.Dictionary<string,UnityEngine.Sprite> icons)
    {
        var grid=column.GetComponent<GridLayoutGroup>();grid.cellSize=new Vector2(126,126);grid.spacing=new Vector2(0,70);
        for(int i=0;i<column.childCount;i++){var slot=column.GetChild(i);slot.gameObject.SetActive(i<names.Length);if(i>=names.Length)continue; FillSlot(slot,null,"");var icon=slot.Find("Slot Icon")?.GetComponent<Image>();if(icon!=null){icon.sprite=icons["Equip Slot ("+iconNames[i]+")"];icon.gameObject.SetActive(true);icon.color=Color.white;}AddSlotLabel(slot,names[i]);}
    }

    private static void AddSlotLabel(Transform slot,string value)
    {
        var text=Label("Equipment Label",slot,new Vector2(0,-84),new Vector2(160,42),value,32,Ivory); text.rectTransform.anchorMin=text.rectTransform.anchorMax=new Vector2(.5f,.5f);
    }

    private static OverburstUIWindow BuildStash(Transform parent)
    {
        var go=Instance("Windows/Empty/Window (Empty with Tabs)",parent);Clean(go);go.name="PF_OverburstStash_Rpg11";
        ((RectTransform)go.transform).sizeDelta=new Vector2(1060,1200);PositionWindow(go,new Vector2(-210,80),.62f);TextAt(go,"Header/Text","창고");
        ((RectTransform)go.transform.Find("Header")).sizeDelta=new Vector2(0,146);
        Off(go,"Content");
        var tabMenu=(RectTransform)go.transform.Find("Tab Menu");tabMenu.anchoredPosition=new Vector2(0,-148);tabMenu.sizeDelta=new Vector2(-4,110);
        var group=tabMenu.Find("Buttons Group");var tabButtons=new Button[3];var highlights=new Image[3];
        for(int i=0;i<3;i++){
            var tab=group.Find("Tab Button ("+(i+1)+")");
            var hit=tab.GetComponent<Image>();if(hit==null)hit=tab.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            var button=tab.GetComponent<Button>();if(button==null)button=tab.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.transition=Selectable.Transition.None;tabButtons[i]=button;
            var label=tab.Find("Text").GetComponent<Text>();label.text="보관함 "+(i+1);label.fontSize=30;label.color=Gold;
            highlights[i]=tab.Find("Active").GetComponent<Image>();
        }
        var grid=Rect("Storage Grid",go.transform,new Vector2(0,-300),new Vector2(1000,776));grid.anchorMin=grid.anchorMax=grid.pivot=new Vector2(.5f,1);
        var layout=grid.gameObject.AddComponent<GridLayoutGroup>();layout.cellSize=new Vector2(104,104);layout.spacing=new Vector2(8,8);layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=9;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Vendor+"Prefabs/HUD/Icon Slots/Item Slot (1).prefab");
        for(int i=0;i<63;i++){var slot=(GameObject)PrefabUtility.InstantiatePrefab(source,grid);Clean(slot);FillSlot(slot.transform,null,"");}
        var rule=Image("Footer Rule",go.transform,null,new Color(.34f,.29f,.21f),new Vector2(1000,1));rule.rectTransform.anchoredPosition=new Vector2(0,-512);
        var hint=Label("Storage Hint",go.transform,new Vector2(0,-555),new Vector2(1000,44),"",27,Ivory,TextAnchor.MiddleRight);
        var preview=go.AddComponent<OverburstUIStashPreview>();
        preview.Configure(grid.Cast<Transform>().Select(t=>t.Find("Icon").GetComponent<Image>()).ToArray(),Enumerable.Range(0,6).Select(ItemIcon).ToArray(),highlights,hint);
        UnityEventTools.AddPersistentListener(tabButtons[0].onClick,preview.First);UnityEventTools.AddPersistentListener(tabButtons[1].onClick,preview.Second);UnityEventTools.AddPersistentListener(tabButtons[2].onClick,preview.Third);
        return FinishWindow(go,parent,"PF_OverburstStash_Rpg11");
    }

    private static GameObject BuildEnemies(Transform parent)
    {
        var group=Rect("30 Monster Health Preview",parent,Vector2.zero,new Vector2(1920,1080));Stretch(group);
        var go=Instance("HUD/Unit Frames/Nameplate",group);Clean(go);go.name="PF_OverburstEnemyHealthBar_Rpg11";
        var fill=go.transform.Find("Bar (Health)").GetComponent<Image>();fill.type=UnityEngine.UI.Image.Type.Filled;fill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;fill.fillOrigin=0;
        var view=go.AddComponent<OverburstEnemyHealthBarView>();view.Configure(fill,go.transform.Find("Percentage Text").GetComponent<Text>(),go.transform.Find("Level Frame/Text").GetComponent<Text>(),go.transform.Find("Unit Name Text").GetComponent<Text>());
        view.Present("유적의 감시자",18,1);
        PolishEnemy(go);
        var prefab=PrefabUtility.SaveAsPrefabAsset(go,Root+"/PF_OverburstEnemyHealthBar_Rpg11.prefab");Object.DestroyImmediate(go);
        string[] names={"유적의 감시자","독낭 추적자","암굴 파괴자"};float[] values={1,.45f,.1f};
        for(int i=0;i<3;i++){var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,group);var r=(RectTransform)instance.transform;r.anchoredPosition=new Vector2(-450+i*450,80);r.localScale=Vector3.one*.62f;instance.GetComponent<OverburstEnemyHealthBarView>().Present(names[i],18+i,values[i]);}
        return group.gameObject;
    }

    private static GameObject BuildNotices(Transform parent)
    {
        var group=Rect("40 Notification Preview",parent,Vector2.zero,new Vector2(1920,1080));Stretch(group);
        foreach(var name in new[]{"Text","Level"}){var go=Instance("HUD/Notifications/Notification ("+name+")",group);Clean(go);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(0,name=="Text"?140:-100);r.localScale=Vector3.one*.55f;foreach(var text in go.GetComponentsInChildren<Text>(true)){text.text=name=="Level"?(text.text.Contains("10")||text.text.Contains("LEVEL")?"레벨 18":"새로운 힘을 얻었습니다"):(text.text.Contains("NOTIFICATION")?"새로운 구역 발견":"잊힌 회랑의 깊은 곳을 탐험하세요.");}}
        PolishNotifications(group.gameObject);
        return SaveAndReplace(group.gameObject,"PF_OverburstNotifications_Rpg11",parent);
    }

    private static OverburstUIWindow FinishWindow(GameObject go,Transform parent,string name)
    {
        PolishWindow(go,name);
        var rect=(RectTransform)go.transform;var window=go.AddComponent<OverburstUIWindow>();window.Configure(rect);
        var drag=go.transform.Find("Header").GetComponent<UIDragObject>();if(drag==null)drag=go.transform.Find("Header").gameObject.AddComponent<UIDragObject>();drag.target=rect;drag.inertia=false;drag.constrainWithinCanvas=true;
        var close=go.transform.Find("Header/Button (Close)").GetComponent<Button>();UnityEventTools.AddPersistentListener(close.onClick,window.Close);
        var header=go.transform.Find("Header/Text").GetComponent<Text>();header.fontSize=52; header.color=Gold;header.raycastTarget=false;
        return SaveAndReplace(go,name,parent).GetComponent<OverburstUIWindow>();
    }

    private static void PositionWindow(GameObject go,Vector2 position,float scale)
    {var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.localScale=Vector3.one*scale;}

    private static GameObject SaveAndReplace(GameObject go,string name,Transform parent)
    {
        var prefab=PrefabUtility.SaveAsPrefabAsset(go,Root+"/"+name+".prefab");Object.DestroyImmediate(go);
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);instance.name=name;return instance;
    }
    private static GameObject Instance(string path,Transform parent) => (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Vendor+"Prefabs/"+path+".prefab"),parent);
    private static void Off(GameObject go,string path) {var t=go.transform.Find(path);if(t!=null)t.gameObject.SetActive(false);}
    private static void TextAt(GameObject go,string path,string text) {var t=go.transform.Find(path)?.GetComponent<Text>();if(t!=null)t.text=text;}
    private static void Clean(GameObject root)
    {
        foreach(var scheme in root.GetComponentsInChildren<ColorSchemeElement>(true)) scheme.Apply(new Color(.52f,.14f,.12f));
        foreach(var c in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if(c==null)continue;var type=c.GetType();string n=type.Name;
            if((type.Namespace??"").StartsWith("DuloGames")||n.StartsWith("Test_")||n.StartsWith("Demo_"))
            { if(n!="UIDragObject"&&n!="UIFlippable"&&n!="UICircularRaycastFilter"&&n!="UIRectangularRaycastFilter") Object.DestroyImmediate(c); }
        }
        foreach(var text in root.GetComponentsInChildren<Text>(true)){text.font=font;text.raycastTarget=false;text.verticalOverflow=VerticalWrapMode.Overflow;}
        foreach(var b in root.GetComponentsInChildren<Button>(true)){b.onClick=new Button.ButtonClickedEvent();b.navigation=new Navigation{mode=Navigation.Mode.None};}
        foreach(var g in root.GetComponentsInChildren<CanvasGroup>(true)){g.alpha=1;g.interactable=true;g.blocksRaycasts=true;}
        foreach(var image in root.GetComponentsInChildren<Image>(true)){if(image.name.Contains("Hover")||image.name.Contains("Press"))image.color=new Color(image.color.r,image.color.g,image.color.b,0);}
    }
    private static void FillSlot(Transform slot,UnityEngine.Sprite sprite,string count)
    {
        var icon=slot.Find("Icon")?.GetComponent<Image>();if(icon!=null){icon.sprite=sprite;icon.gameObject.SetActive(sprite!=null);icon.preserveAspect=true; icon.rectTransform.anchorMin=Vector2.zero;icon.rectTransform.anchorMax=Vector2.one;icon.rectTransform.offsetMin=Vector2.one*18;icon.rectTransform.offsetMax=Vector2.one*-18;}
        var placeholder=slot.Find("Slot Icon");if(placeholder!=null)placeholder.gameObject.SetActive(sprite==null && placeholder.GetComponent<Image>() && placeholder.GetComponent<Image>().sprite);
        slot.GetComponent<OverburstUISlotGradePreview>()?.Refresh();
        foreach(var text in slot.GetComponentsInChildren<Text>(true)){if(!text.name.Contains("Hotkey"))text.text=count;}
    }
    private static UnityEngine.Sprite ItemIcon(int index)
    {
        string[] paths={"Assets/ProjectOverburst/05_Art/UI/Icons/Weapons/Icon_Weapon_OneHandSword.png","Assets/ProjectOverburst/03_Features/Items/Art/Icons/Flasks/Flask_Life.png","Assets/ProjectOverburst/03_Features/Items/Art/Icons/Flasks/Flask_Lightning.png","Assets/ProjectOverburst/03_Features/Items/Art/Icons/Flasks/Flask_Ironclad.png","Assets/ProjectOverburst/05_Art/UI/Icons/Consumables/Icon_MoveSpeedPotion.png",Vendor+"Textures/Spell & Item Icons/Icon_Book_128.png"};
        return Sprite(paths[index%paths.Length]);
    }
    private static UnityEngine.Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(path)??AssetDatabase.LoadAllAssetsAtPath(path).OfType<UnityEngine.Sprite>().FirstOrDefault();
    private static RectTransform Rect(string name,Transform parent,Vector2 position,Vector2 size)
    {var go=new GameObject(name,typeof(RectTransform));go.layer=5;var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;return r;}
    private static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    private static Image Image(string name,Transform parent,UnityEngine.Sprite sprite,Color color,Vector2 size)
    {var img=Rect(name,parent,Vector2.zero,size).gameObject.AddComponent<Image>();img.sprite=sprite;img.color=color;img.raycastTarget=false;return img;}
    private static Text Label(string name,Transform parent,Vector2 position,Vector2 size,string value,int fontSize,Color color,TextAnchor alignment=TextAnchor.MiddleCenter)
    {var text=Rect(name,parent,position,size).gameObject.AddComponent<Text>();text.font=font;text.fontSize=fontSize;text.color=color;text.text=value;text.alignment=alignment;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Overflow;return text;}
    private static Button Button(string name,Transform parent,Vector2 position,Vector2 size,string caption)
    {var image=Image(name,parent,Sprite(Vendor+"Textures/Controls/Buttons/Rectangular/Button_RS_Background.png"),new Color(.48f,.2f,.16f),size);image.rectTransform.anchoredPosition=position;image.type=UnityEngine.UI.Image.Type.Sliced;image.raycastTarget=true;var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;b.navigation=new Navigation{mode=Navigation.Mode.None};Label("Text",image.transform,Vector2.zero,size-Vector2.one*8,caption,size.y>50?30:15,Gold);return b;}
}
