using System;
using System.IO;
using System.Linq;
using DuloGames.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstSkillTreeBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/UI/SkillTree";
    public const string PrefabPath = Root + "/PF_OverburstSkillTree_Rpg11.prefab";
    public const string AtlasPath = "Assets/ProjectOverburst/05_Art/UI/SkillTree/AttackGlyphAtlas.png";
    public const string MoveGlyphPath = "Assets/ProjectOverburst/05_Art/UI/SkillTree/Icon_SkillTreeMove.png";
    const string Vendor = "Assets/ThirdParty/RPG and MMO UI 11/Textures/";
    public const float Width = 1728, Height = 988;
    static Font body, heading;
    static Sprite buttonSprite, buttonBorder, round, square, glow, moveGlyph;
    static readonly Color Gold = new Color(.87f,.73f,.47f), Ivory = new Color(.91f,.88f,.81f), Muted = new Color(.64f,.60f,.53f), Surface = new Color(.051f,.047f,.043f), Rule = new Color(.27f,.23f,.17f);
    [MenuItem("OVERBURST/UI/Build Common Skill Tree")]
    public static void BuildMenu() => Build();
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required for skill-tree authoring.");
        body = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        var referenceWindow = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstInventory_Rpg11.prefab");
        heading = referenceWindow.transform.Find("Header/Text").GetComponent<Text>().font;
        if (!body || !heading) throw new InvalidOperationException("Existing Korean UI fonts required.");
        buttonSprite = SpriteAt("Controls/Buttons/Rectangular/Button_RS_Background.png");
        buttonBorder = SpriteAt("Controls/Buttons/Rectangular/Button_RS_Border2.png");
        round = SpriteAt("Controls/Buttons/Circular/Button_Circular_Foreground.png");
        square = SpriteAt("Miscellaneous/General/General_Container_Border_2.png");
        glow = square;
        moveGlyph = EnsureMoveGlyph();
        // Existing atlas GUID/slicing belongs to the original UI authoring pass.
        var sprites = AssetDatabase.LoadAllAssetsAtPath(AtlasPath).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
        if (sprites.Length != 16) throw new InvalidOperationException("16 generated attack glyph sprites required.");
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/CommonAttackTreeCatalog.json");
        var data = JsonUtility.FromJson<OverburstSkillTreeCatalog>(asset.text); data.Validate();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("PF_OverburstSkillTree_Rpg11", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene); root.layer = 5; Stretch((RectTransform)root.transform);
            var ui = root.AddComponent<OverburstSkillTreeUI>(); ui.catalogAsset = asset; ui.rootCanvas = root.GetComponent<Canvas>();
            ui.entry = Button(root.transform, "Skill Tree Entry", "스킬트리", 0,0,164,44);
            var er = (RectTransform)ui.entry.transform; er.anchorMin = er.anchorMax = er.pivot = new Vector2(1,0); er.anchoredPosition = new Vector2(-30,30);
            var entryIcon = Picture(er,"Tree Glyph",sprites[8],Gold,24,8,25,25); ui.entry.GetComponentInChildren<Text>().rectTransform.offsetMin = new Vector2(32,0);
            ui.surface = Rect(root.transform,"Skill Tree Screen",0,0,Width,Height).gameObject; Stretch((RectTransform)ui.surface.transform);
            var shade = ui.surface.AddComponent<Image>(); shade.color = new Color(0,0,0,.76f); shade.raycastTarget = true;
            ui.window = Rect(ui.surface.transform,"Window",0,0,Width,Height); ui.window.anchorMin = ui.window.anchorMax = ui.window.pivot = new Vector2(.5f,.5f); ui.window.anchoredPosition = Vector2.zero;
            ui.window.gameObject.AddComponent<Image>().color = Surface;
            // Preserve the authored inventory chrome: its header border is a 22px bottom strip,
            // not an image stretched across the full title band.
            var chrome=Rect(ui.window,"Shared Window Chrome",0,0,Width*2,Height*2);chrome.localScale=Vector3.one*.5f;
            var sharedFrame=Object.Instantiate(referenceWindow.transform.Find("Borders").gameObject,chrome,false);sharedFrame.name="Vendor Window Frame";
            var sharedHeader=Object.Instantiate(referenceWindow.transform.Find("Header").gameObject,chrome,false);sharedHeader.name="Header";
            foreach(var drag in sharedHeader.GetComponentsInChildren<UIDragObject>(true))Object.DestroyImmediate(drag);
            var title=sharedHeader.transform.Find("Text").GetComponent<Text>();title.text="스킬트리";var titleRect=title.rectTransform;titleRect.anchorMin=titleRect.anchorMax=titleRect.pivot=new Vector2(0,1);titleRect.anchoredPosition=new Vector2(128,-28);titleRect.sizeDelta=new Vector2((Width-128)*2,88);
            ui.close=sharedHeader.transform.Find("Button (Close)").GetComponent<Button>();ui.close.onClick=new Button.ButtonClickedEvent();var closeRect=(RectTransform)ui.close.transform;closeRect.anchorMin=closeRect.anchorMax=closeRect.pivot=new Vector2(0,1);closeRect.anchoredPosition=new Vector2((Width-58)*2,-40);closeRect.sizeDelta=new Vector2(72,72);
            Label(ui.window,"Common Tree Caption","공통 스킬트리",30,30,240,26,13,Muted);
            Label(ui.window,"Planning Points Caption","남은 스킬포인트",1374,15,236,23,12,Muted,TextAnchor.MiddleRight);
            ui.remaining = Label(ui.window,"Planning Points","0 / 0",1374,37,236,29,22,Ivory,TextAnchor.MiddleRight);
            Label(ui.window,"Map Controls Hint","휠 / + − 확대·축소 · 드래그 이동",28,94,520,25,12,Muted);
            ui.zoomOut = Button(ui.window,"Zoom Out","−",928,90,36,36);ui.zoomOut.GetComponentInChildren<Text>().fontSize=18;
            ui.zoomLabel = Label(ui.window,"Zoom Value","100%",970,90,68,36,14,Ivory,TextAnchor.MiddleCenter);
            ui.zoomIn = Button(ui.window,"Zoom In","+",1044,90,36,36);ui.zoomIn.GetComponentInChildren<Text>().fontSize=18;
            ui.center = Button(ui.window,"Center Map","전체 보기",1094,90,132,36);
            ui.viewport = Rect(ui.window,"Tree Viewport",18,126,1220,744);
            ui.viewport.gameObject.AddComponent<Image>().color = new Color(.060f,.056f,.050f); ui.viewport.gameObject.AddComponent<RectMask2D>();
            ui.viewport.gameObject.AddComponent<OverburstSkillTreeMapInput>().owner = ui;
            var paths = Rect(ui.viewport,"Unique Tree Routes",0,0,1246,744); Stretch(paths); ui.routes = paths.gameObject.AddComponent<OverburstSkillTreeRoutes>();
            var nodeLayer = Rect(ui.viewport,"Nodes",0,0,1246,744); Stretch(nodeLayer);
            var textLayer = Rect(ui.viewport,"Node Labels",0,0,1246,744); Stretch(textLayer);
            ui.nodes = data.nodes.Select(n => Node(ui,nodeLayer,textLayer,n,sprites)).ToArray();
            string[] regions = { "약공 콤보", "강공", "대시 약공", "대시 강공" };
            ui.areaLabels = regions.Select((name,i) => {
                var t = Label(textLayer,"Region " + i,name,0,0,140,24,15,Gold,TextAnchor.MiddleCenter,true); t.rectTransform.sizeDelta=new Vector2(Mathf.Ceil(t.preferredWidth)+10,24); t.rectTransform.anchorMin=t.rectTransform.anchorMax=t.rectTransform.pivot=new Vector2(.5f,.5f); return t.rectTransform;
            }).ToArray();
            Label(ui.window,"Node Legend","✓ 습득 완료    ○ 습득 가능    × 연결 잠김    + 습득 예정    − 환급 예정    … 확장 예정",38,880,1190,25,12,Muted,TextAnchor.MiddleCenter);
            var right = Rect(ui.window,"Selected Node Panel",1262,96,440,796);
            var divider = Picture(ui.window,"Column Rule",null,Rule,1250,104,1,790);
            var elementSprites=new[]{"Fire","Ice","Electric","Dark","Light"}.Select(element=>AssetDatabase.LoadAssetAtPath<Sprite>("Assets/ProjectOverburst/Resources/UI/WeaponElements/Icon_WeaponElement_"+element+".png")).ToArray();if(elementSprites.Any(s=>!s))throw new InvalidOperationException("Existing HUD element icons required.");
            ui.elementSprites=elementSprites;
            ui.equippedIcon=Picture(right,"Equipped Element Icon",elementSprites[0],Color.white,22,0,24,24);ui.equippedIcon.preserveAspect=true;
            ui.elementLabel = Label(right,"Equipped Element","장착 원소 · 미장착",56,0,362,24,13,Muted);
            ui.kindLabel = Label(right,"Kind","약공 콤보 · 기본 강화",22,34,396,22,12,Muted);
            var detailFrame=Picture(right,"Selected Attack Frame",square,new Color(.62f,.54f,.40f),22,61,46,46);detailFrame.type=UnityEngine.UI.Image.Type.Sliced;detailFrame.pixelsPerUnitMultiplier=2;
            ui.detailNodeIcon=Picture(right,"Selected Attack Icon",sprites[0],Gold,29,68,32,32);ui.detailNodeIcon.preserveAspect=true;
            ui.nodeName = Label(right,"Name","약공 2타 강화",82,57,336,39,24,Gold,TextAnchor.MiddleLeft,true);
            ui.meta = Label(right,"Cost and Cooldown","1포인트 · 조건마다 발동",82,99,336,26,13,Muted);
            RuleAt(right,"Detail Header Rule",22,137,396);
            Label(right,"Trigger Heading","적용 방식",22,151,396,21,12,Muted);
            ui.trigger = Label(right,"Trigger","",22,181,396,63,16,Ivory,TextAnchor.UpperLeft); ui.trigger.horizontalOverflow = HorizontalWrapMode.Wrap;
            ui.effectHeading=Label(right,"Effect Heading","강화 효과",22,225,396,27,15,Gold).rectTransform;
            ui.effectRule=Picture(right,"Effect Rule",null,Rule,22,260,396,1).rectTransform;
            var effectView = Rect(right,"Effects Viewport",22,273,396,337); effectView.gameObject.AddComponent<Image>().color = Color.clear; effectView.gameObject.AddComponent<RectMask2D>();
            ui.effectContent = Rect(effectView,"Effect Rows",0,0,374,500); ui.effectContent.anchorMin= new Vector2(0,1);ui.effectContent.anchorMax= new Vector2(1,1);ui.effectContent.pivot=new Vector2(.5f,1);ui.effectContent.offsetMin=new Vector2(0,-500);ui.effectContent.offsetMax=new Vector2(-18,0);
            ui.effectLabels = new Text[6]; ui.effectBodies = new Text[6];ui.effectIcons=new Image[6];ui.effectRowSurfaces=new Image[6];
            for (int i=0;i<6;i++) {
                var row=Rect(ui.effectContent,"Element Effect " + i,0,i*70,374,70);ui.effectRowSurfaces[i]=row.gameObject.AddComponent<Image>();ui.effectRowSurfaces[i].raycastTarget=false;
                ui.effectIcons[i]=Picture(row,"Element Icon",i<5?elementSprites[i]:SpriteAt("HUD/Unit Frames/Unit Frame/Roles/Icon_Melee.png"),Color.white,4,12,26,26);ui.effectIcons[i].preserveAspect=true;
                ui.effectLabels[i]=Label(row,"Element","",38,12,40,26,12,Gold,TextAnchor.MiddleLeft);
                ui.effectBodies[i]=Label(row,"Effect","",82,8,288,60,15,Ivory,TextAnchor.UpperLeft); ui.effectBodies[i].horizontalOverflow=HorizontalWrapMode.Wrap; ui.effectBodies[i].verticalOverflow=VerticalWrapMode.Truncate;
                ui.effectBodies[i].rectTransform.anchorMin=new Vector2(0,0);ui.effectBodies[i].rectTransform.anchorMax=new Vector2(1,1);ui.effectBodies[i].rectTransform.offsetMin=new Vector2(82,8);ui.effectBodies[i].rectTransform.offsetMax=new Vector2(-4,-8);
            }
            ui.effectScroll = effectView.gameObject.AddComponent<ScrollRect>();ui.effectScroll.viewport=effectView;ui.effectScroll.content=ui.effectContent;ui.effectScroll.horizontal=false;ui.effectScroll.vertical=true;ui.effectScroll.movementType=ScrollRect.MovementType.Clamped;ui.effectScroll.scrollSensitivity=28;
            var scrollTrack=Rect(effectView,"Scroll Track",388,0,4,337);scrollTrack.anchorMin=new Vector2(1,0);scrollTrack.anchorMax=new Vector2(1,1);scrollTrack.offsetMin=new Vector2(-8,0);scrollTrack.offsetMax=new Vector2(-4,0);scrollTrack.gameObject.AddComponent<Image>().color=new Color(.16f,.14f,.11f);
            var thumb=Rect(scrollTrack,"Thumb",0,0,4,120);Stretch(thumb);var thumbImage=thumb.gameObject.AddComponent<Image>();thumbImage.color=new Color(.48f,.39f,.25f);var scrollBar=scrollTrack.gameObject.AddComponent<Scrollbar>();scrollBar.handleRect=thumb;scrollBar.targetGraphic=thumbImage;scrollBar.direction=Scrollbar.Direction.BottomToTop;ui.effectScroll.verticalScrollbar=scrollBar;ui.effectScroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            RuleAt(right,"Prerequisite Rule",22,624,396);
            ui.prerequisites=Label(right,"Prerequisites","",22,636,396,42,12,Muted,TextAnchor.UpperLeft);ui.prerequisites.horizontalOverflow=HorizontalWrapMode.Wrap;
            Label(right,"Stat Heading","능력치 미리보기",22,680,396,25,15,Gold);
            ui.statValues = new Text[4]; string[] statNames={"공격력","방어력","최대 체력","이동속도"};
            for(int i=0;i<4;i++){float x=22+i%2*210,y=714+i/2*26;Label(right,"Stat " + i,statNames[i],x,y,88,24,13,Muted);ui.statValues[i]=Label(right,"Stat Value " + i,"",x+90,y,96,24,13,Ivory,TextAnchor.MiddleRight);}
            ui.action=Button(ui.window,"Edit Plan","계획에서 제거",1284,874,396,44);ui.actionLabel=ui.action.GetComponentInChildren<Text>();
            RuleAt(ui.window,"Footer Rule",30,926,Width-60);
            ui.resetAllocation=Button(ui.window,"Reset Allocation","전체 환급",30,932,190,44);
            ui.feedback=Label(ui.window,"Plan Status","5레벨마다 1포인트 · 변경 후 적용하면 저장됩니다",240,942,1000,24,13,Muted);
            ui.cancel=Button(ui.window,"Cancel Plan","변경 취소",1284,932,190,44);ui.apply=Button(ui.window,"Apply Plan","강화 적용",1490,932,190,44);
            ui.tooltip=Rect(ui.window,"Node Tooltip",0,0,360,320);ui.tooltip.anchorMin=ui.tooltip.anchorMax=new Vector2(.5f,.5f);ui.tooltip.pivot=new Vector2(0,1);
            var tb=ui.tooltip.gameObject.AddComponent<Image>();tb.color=new Color(.072f,.063f,.050f);tb.raycastTarget=false;
            var tf=Image(ui.tooltip,"Tooltip Frame",SpriteAt("Miscellaneous/General/General_Container_Border_2.png"),new Color(.88f,.72f,.43f));Stretch(tf.rectTransform);tf.type=UnityEngine.UI.Image.Type.Sliced;tf.pixelsPerUnitMultiplier=2;
            ui.tipName=Label(ui.tooltip,"Name","",18,16,324,30,20,Gold,TextAnchor.UpperLeft,true);ui.tipMeta=Label(ui.tooltip,"Meta","",18,50,324,24,12,Muted,TextAnchor.UpperLeft);
            ui.tipTrigger=Label(ui.tooltip,"Trigger","",18,86,324,52,15,Ivory,TextAnchor.UpperLeft);ui.tipEffect=Label(ui.tooltip,"Effect","",18,148,324,88,15,Ivory,TextAnchor.UpperLeft);
            ui.tipPrerequisites=Label(ui.tooltip,"Prerequisites","",18,246,324,36,12,Muted,TextAnchor.UpperLeft);ui.tipState=Label(ui.tooltip,"State","",18,286,324,24,12,Gold,TextAnchor.UpperLeft);
            foreach(var t in new[]{ui.tipTrigger,ui.tipEffect,ui.tipPrerequisites,ui.tipState})t.horizontalOverflow=HorizontalWrapMode.Wrap;
            ui.tooltip.gameObject.SetActive(false); ui.surface.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            return "CREATED " + PrefabPath + "; shared nodes=" + data.nodes.Length + "; unique routes=" + data.segments.Length;
        }
        finally { if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene); }
    }
    static OverburstSkillTreeNodeView Node(OverburstSkillTreeUI owner,Transform parent,Transform captions,OverburstSkillTreeCatalog.Node n,Sprite[] atlas)
    {
        float size=n.kind=="root"?64:n.kind=="stat"?44:n.kind=="keystone"?64:n.kind=="guide"?36:n.kind=="active"?46:42;
        var r=Rect(parent,"Node " + n.id,0,0,size,size);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);
        var frame=r.gameObject.AddComponent<Image>();frame.sprite=new[]{"keystone","active","bridge"}.Contains(n.kind)?square:round;frame.raycastTarget=true;if(new[]{"keystone","active","bridge"}.Contains(n.kind)){frame.type=UnityEngine.UI.Image.Type.Sliced;frame.pixelsPerUnitMultiplier=3;}
        var view=r.gameObject.AddComponent<OverburstSkillTreeNodeView>();view.nodeId=n.id;view.frame=frame;view.button=r.gameObject.AddComponent<Button>();view.button.targetGraphic=frame;
        var navigation=view.button.navigation;navigation.mode=Navigation.Mode.None;view.button.navigation=navigation;
        var selected=Picture(r,"Selected Frame",new[]{"keystone","active","bridge"}.Contains(n.kind)?glow:round,Gold,0,0,size+8,size+8);selected.rectTransform.anchorMin=selected.rectTransform.anchorMax=selected.rectTransform.pivot=new Vector2(.5f,.5f);selected.rectTransform.anchoredPosition=Vector2.zero;selected.transform.SetAsFirstSibling();if(new[]{"keystone","active","bridge"}.Contains(n.kind)){selected.type=UnityEngine.UI.Image.Type.Sliced;selected.pixelsPerUnitMultiplier=3;}view.selection=selected;
        Sprite glyph=n.stat=="move"?moveGlyph:n.kind=="stat"?SpriteAt(new[]{"attack","defense","hp","move"}.Contains(n.stat)?new[]{"HUD/Unit Frames/Unit Frame/Roles/Icon_Melee.png","HUD/Unit Frames/Unit Frame/Roles/Icon_Defense.png","Mobile/Action Buttons/Icons/Heart.png","Windows/Character/Equip Slot/Icons/Boots.png"}[Array.IndexOf(new[]{"attack","defense","hp","move"},n.stat)]:"HUD/Unit Frames/Unit Frame/Roles/Icon_Melee.png"):atlas[Mathf.Clamp(n.icon,0,15)];
        float iconSize=n.kind=="stat"?24:n.kind=="keystone"?34:n.kind=="active"?28:23;
        view.icon=Picture(r,"Glyph",glyph,Gold,0,0,iconSize,iconSize);view.icon.preserveAspect=true;view.icon.rectTransform.anchorMin=view.icon.rectTransform.anchorMax=view.icon.rectTransform.pivot=new Vector2(.5f,.5f);view.icon.rectTransform.anchoredPosition=Vector2.zero;
        if(n.kind=="keystone") { var inner=Picture(r,"Keystone Inner Border",square,new Color(.63f,.52f,.34f),0,0,size-10,size-10);inner.rectTransform.anchorMin=inner.rectTransform.anchorMax=inner.rectTransform.pivot=new Vector2(.5f,.5f);inner.rectTransform.anchoredPosition=Vector2.zero;inner.type=UnityEngine.UI.Image.Type.Sliced;inner.pixelsPerUnitMultiplier=3; }
        if(!new[]{"root","guide"}.Contains(n.kind)) {
            view.statePlate=Picture(r,"State Badge",round,new Color(.08f,.07f,.06f),0,0,18,18);view.statePlate.rectTransform.anchorMin=view.statePlate.rectTransform.anchorMax=new Vector2(1,1);view.statePlate.rectTransform.pivot=new Vector2(.5f,.5f);view.statePlate.rectTransform.anchoredPosition=new Vector2(-2,-2);
            view.stateMark=Label(view.statePlate.transform,"State Mark","",0,0,18,18,14,Gold,TextAnchor.MiddleCenter);Stretch(view.stateMark.rectTransform);
        }
        if(n.kind=="advanced"){var pip=Picture(r,"Upgrade Mark",round,Gold,0,0,8,8);pip.rectTransform.anchorMin=pip.rectTransform.anchorMax=new Vector2(1,0);pip.rectTransform.pivot=new Vector2(.5f,.5f);pip.rectTransform.anchoredPosition=new Vector2(-1,1);}
        if(!new[]{"root","guide"}.Contains(n.kind)){
            int fontSize=n.kind=="stat"?14:n.kind=="keystone"?15:13;var plate=Rect(captions,"Caption " + n.id,0,0,100,19);var im=plate.gameObject.AddComponent<Image>();im.color=new Color(.060f,.056f,.050f);im.raycastTarget=false;
            view.caption=Label(plate,"Text",n.shortName,0,0,100,19,fontSize,Muted,TextAnchor.MiddleCenter);Stretch(view.caption.rectTransform);
            view.captionRect=plate;plate.anchorMin=plate.anchorMax=plate.pivot=new Vector2(.5f,.5f);plate.sizeDelta=new Vector2(Mathf.Ceil(view.caption.preferredWidth)+6,fontSize*1.25f+2);
            view.captionOffset=new Vector2(0,-(n.kind=="keystone"?48:n.kind=="stat"?36:36));
            if(n.area=="W")view.captionOffset=new Vector2(0,n.kind=="keystone"?48:36);
        }
        return view;
    }
    static Sprite SpriteAt(string path) { var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Vendor+path);if(!sprite)throw new InvalidOperationException("Package sprite missing: " + path);return sprite; }
    static Sprite EnsureMoveGlyph()
    {
        // White vector strokes stay legible under the game's gold/ivory state tints.
        if (!File.Exists(MoveGlyphPath))
        {
            var texture = new Texture2D(128,128,TextureFormat.RGBA32,false);
            try
            {
                Vector2[] starts = {new Vector2(44,36),new Vector2(70,64),new Vector2(70,36),new Vector2(96,64),new Vector2(12,64),new Vector2(18,46),new Vector2(18,82)};
                Vector2[] ends = {new Vector2(70,64),new Vector2(44,92),new Vector2(96,64),new Vector2(70,92),new Vector2(37,64),new Vector2(34,46),new Vector2(34,82)};
                var colors = new Color32[128*128];
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                {
                    var p = new Vector2(x+.5f,y+.5f);float distance=float.MaxValue;
                    for(int i=0;i<starts.Length;i++){var delta=ends[i]-starts[i];var nearest=starts[i]+delta*Mathf.Clamp01(Vector2.Dot(p-starts[i],delta)/delta.sqrMagnitude);distance=Mathf.Min(distance,Vector2.Distance(p,nearest));}
                    colors[y*128+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(Mathf.Clamp01(3.6f-distance)*255));
                }
                texture.SetPixels32(colors);texture.Apply();File.WriteAllBytes(MoveGlyphPath,texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(MoveGlyphPath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(MoveGlyphPath);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=128;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
        var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(MoveGlyphPath);if(!sprite)throw new InvalidOperationException("Movement glyph must import as a sprite.");return sprite;
    }
    static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
    {var go=new GameObject(name,typeof(RectTransform));go.layer=5;var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
    static void Stretch(RectTransform r) { r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;r.pivot=new Vector2(.5f,.5f); }
    static Image Image(Transform parent,string name,Sprite sprite,Color tint){var r=Rect(parent,name,0,0,100,100);var im=r.gameObject.AddComponent<Image>();im.sprite=sprite;im.color=tint;im.raycastTarget=false;return im;}
    static Image Picture(Transform parent,string name,Sprite sprite,Color tint,float x,float y,float w,float h){var im=Image(parent,name,sprite,tint);im.rectTransform.anchoredPosition=new Vector2(x,-y);im.rectTransform.sizeDelta=new Vector2(w,h);return im;}
    static Text Label(Transform parent,string name,string value,float x,float y,float w,float h,int size,Color tint,TextAnchor align=TextAnchor.MiddleLeft,bool title=false)
    {var r=Rect(parent,name,x,y,w,h);var t=r.gameObject.AddComponent<Text>();t.font=title?heading:body;t.fontSize=size;t.fontStyle=FontStyle.Normal;t.text=value;t.color=tint;t.alignment=align;t.raycastTarget=false;t.supportRichText=false;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Truncate;t.lineSpacing=1.15f;return t;}
    static Button Button(Transform parent,string name,string text,float x,float y,float w,float h)
    {var r=Rect(parent,name,x,y,w,h);r.gameObject.AddComponent<CanvasGroup>();bool primary=name=="Apply Plan"||name=="Skill Tree Entry";var im=r.gameObject.AddComponent<Image>();im.sprite=buttonSprite;im.type=UnityEngine.UI.Image.Type.Sliced;im.pixelsPerUnitMultiplier=2;im.color=primary?new Color(.72f,.32f,.22f):new Color(.72f,.63f,.50f);var frame=Image(r,"Button Border",buttonBorder,primary?new Color(.83f,.65f,.39f):new Color(.70f,.62f,.49f));Stretch(frame.rectTransform);frame.type=UnityEngine.UI.Image.Type.Sliced;frame.pixelsPerUnitMultiplier=2;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;var colors=b.colors;colors.highlightedColor=new Color(1,.90f,.72f);colors.pressedColor=new Color(.64f,.54f,.42f);colors.disabledColor=new Color(.45f,.42f,.36f,.7f);colors.fadeDuration=.1f;b.colors=colors;var t=Label(r,"Label",text,0,0,w,h,h>=40?14:13,primary?Gold:Ivory,TextAnchor.MiddleCenter);Stretch(t.rectTransform);return b;}
    static void RuleAt(Transform parent,string name,float x,float y,float w)=>Picture(parent,name,null,Rule,x,y,w,1);
    static void ImportAtlas()
    {
        var importer=AssetImporter.GetAtPath(AtlasPath) as TextureImporter;if(importer==null)throw new InvalidOperationException("Generated atlas not imported.");
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.isReadable=false;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.wrapMode=TextureWrapMode.Clamp;
        int[] edges={0,314,627,940,1254};var cells=new SpriteMetaData[16];
        for(int i=0;i<16;i++){int row=i/4,col=i%4;cells[i]=new SpriteMetaData{name="AttackGlyph_"+i.ToString("D2"),rect=new Rect(edges[col],1254-edges[row+1],edges[col+1]-edges[col],edges[row+1]-edges[row]),alignment=(int)SpriteAlignment.Center,pivot=new Vector2(.5f,.5f)};}
        #pragma warning disable 0618
        importer.spritesheet=cells;
        #pragma warning restore 0618
        importer.SaveAndReimport();
    }
}
