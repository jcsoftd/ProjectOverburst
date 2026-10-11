using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Appearance;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class TitleCharacterBuilder
{
    public const string Root="Assets/ProjectOverburst/Resources/UI/Title";
    public const string Art="Assets/ProjectOverburst/05_Art/UI/Title";
    public const string PrefabPath=Root+"/PF_OverburstTitle.prefab";
    public const string Output="../개인파일/코덱스산출/UI/20261011_TitleCharacterGoal";
    const string Design="../개인파일/코덱스산출/Design/20261011_TitleAndCharacters";
    const string Kit="Assets/ThirdParty/RPG and MMO UI 11/Textures/";
    static TMP_FontAsset serif,sans;
    static readonly Color Gold=new Color(.82f,.68f,.40f),Ivory=new Color(.90f,.85f,.73f),Muted=new Color(.48f,.46f,.42f);

    [MenuItem("Overburst/UI/타이틀과 캐릭터/제품 UI 만들기")]
    public static void Build()
    {
        AppearanceCustomizationBuilder.RequireIdle();string before=Scenes();Folder(Root);Folder(Art);
        serif=AppearanceCustomizationBuilder.EnsureHeadingFont();
        sans=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AppearanceCustomizationBuilder.Root+"/Fonts/AppearanceBody.asset");
        if(!serif||!sans)throw new InvalidOperationException("기존 외모 창 글꼴이 없습니다.");
        var characterBg=Import("CharactersBackground",Design+"/Assets/Backgrounds/characters-v1.png");
        var classBg=Import("ClassesBackground",Design+"/Assets/Backgrounds/classes-v2.png");
        var logo=Import("OverburstLogo",Design+"/Assets/Web/game-logo.png");
        SyncApprovedThumbnails();
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
        try
        {
            root=new GameObject("PF_OverburstTitle",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.layer=5;
            SceneManager.MoveGameObjectToScene(root,scene);root.SetActive(false);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.overrideSorting=true;canvas.sortingOrder=340;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var dark=Stretch(root.transform,"Letterbox").gameObject.AddComponent<Image>();dark.color=new Color(.031f,.031f,.031f);dark.raycastTarget=false;
            var stage=At(root.transform,"Stage",0,0,1920,1080);stage.anchorMin=stage.anchorMax=new Vector2(.5f,.5f);stage.pivot=new Vector2(.5f,.5f);stage.anchoredPosition=Vector2.zero;
            stage.gameObject.AddComponent<RectMask2D>();
            var panel=root.AddComponent<TitleCharacterPanel>();panel.canvas=canvas;
            panel.catalog=AssetDatabase.LoadAssetAtPath<CharacterAppearanceCatalog>(AppearanceCustomizationBuilder.CatalogPath);
            panel.background=Stretch(stage,"Background").gameObject.AddComponent<RawImage>();panel.background.raycastTarget=false;
            panel.titleBackground=AssetDatabase.LoadAssetAtPath<Texture>("Assets/ProjectOverburst/05_Art/UI/Loading/Loading_Hideout_Camp.png");panel.charactersBackground=characterBg;panel.classesBackground=classBg;
            panel.titleMaterial=Backdrop("Title",.72f,.8f,true);panel.charactersMaterial=Backdrop("Characters",.8f,.78f,false);panel.classesMaterial=Backdrop("Classes",.84f,.86f,false);
            var title=Stretch(stage,"Title Screen");panel.titleScreen=title.gameObject;
            var logoImage=At(title,"Original Logo",72,-17,948,711).gameObject.AddComponent<RawImage>();logoImage.texture=logo;logoImage.raycastTarget=false;
            var ko=Text(title,"Original Korean Title","오 버 버 스 트",72,609.716f,948,31.636f,21.6f,true,new Color(.776f,.706f,.576f),TextAlignmentOptions.Center);ko.characterSpacing=7*1.2f/21.6f*100;
            panel.start=Button(title,"Game Start","게임 시작",326.4f,698.95f,439.2f,80.4f,true);panel.start.GetComponentInChildren<TMP_Text>().fontSize=30;
            panel.start.GetComponentInChildren<TMP_Text>().characterSpacing=12;
            panel.settings=Button(title,"Settings","설정",340.8f,796.12f,410.4f,64.8f);panel.settings.GetComponentInChildren<TMP_Text>().fontSize=25.2f;
            panel.quit=Button(title,"Quit","종료",340.8f,877.70f,410.4f,64.8f);panel.quit.GetComponentInChildren<TMP_Text>().fontSize=25.2f;
            var characters=Stretch(stage,"Characters Screen");panel.charactersScreen=characters.gameObject;Feathers(characters);
            var classes=Stretch(stage,"Classes Screen");panel.classesScreen=classes.gameObject;Feathers(classes);
            var header=At(stage,"Screen Heading",64,26,450,77);
            Image(header,"Crystal",0,12,32,36,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Crystal.png"),Color.white);
            panel.header=Text(header,"Title","캐릭터 선택",54,2.65f,396,52.5f,42,true,Gold);
            Image(header,"Rule",54,59.89f,370,9,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Border.png"),Muted);
            panel.subtitle=Text(stage,"Class Lead","당신의 모험을 시작할 캐릭터를 선택하세요.",120,116.506f,580,23.386f,15.6f,false,new Color(.635f,.584f,.510f));
            panel.subtitle.characterSpacing=3.84615f;
            panel.back=Button(stage,"Back","돌아가기",64,1004.915f,180,48);panel.back.GetComponentInChildren<TMP_Text>().fontSize=21;
            Image(panel.back.transform,"Back Icon",17,16,15.6f,15.6f,KitSprite("Controls/Buttons/Square/Icons/Back.png"),Gold);
            panel.back.GetComponentInChildren<TMP_Text>().margin=new Vector4(24,0,0,0);
            panel.feedback=Text(stage,"Feedback","",550,944,820,38,18,false,Ivory,TextAlignmentOptions.Center);
            BuildClasses(classes,panel);BuildRoster(characters,panel);BuildName(stage,panel);BuildQuit(stage,panel);
            foreach(var button in root.GetComponentsInChildren<Button>(true))ConfigureHover(button);
            title.gameObject.SetActive(true);characters.gameObject.SetActive(false);classes.gameObject.SetActive(false);header.gameObject.SetActive(false);panel.subtitle.gameObject.SetActive(false);panel.back.gameObject.SetActive(false);
            panel.nameDialog.SetActive(false);
            var allText=root.GetComponentsInChildren<TMP_Text>(true);serif.TryAddCharacters(string.Concat(allText.Where(t=>t.font==serif).Select(t=>t.text))+"마법사궁수캐릭터생성새로운모험기존1234567890");sans.TryAddCharacters(string.Concat(allText.Where(t=>t.font==sans).Select(t=>t.text))+"마법사궁수캐릭터없음개발중추후추가예정MAGEARCHERKNIGHT0123456789");
            AssetDatabase.SaveAssetIfDirty(serif);AssetDatabase.SaveAssetIfDirty(sans);
            root.SetActive(true);canvas.enabled=false;root.transform.localScale=Vector3.one;
            UIPrefabStructureBuilder.Organize(root,PrefabPath);
            var saved=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            var data=new SerializedObject(saved.GetComponent<Canvas>());data.FindProperty("m_Enabled").boolValue=true;data.ApplyModifiedPropertiesWithoutUndo();
            var modalData=new SerializedObject(saved.GetComponent<TitleCharacterPanel>().nameDialog.GetComponent<Canvas>());modalData.FindProperty("m_OverrideSorting").boolValue=true;modalData.ApplyModifiedPropertiesWithoutUndo();
            var rectData=new SerializedObject(saved.transform);rectData.FindProperty("m_LocalScale").vector3Value=Vector3.one;rectData.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SavePrefabAsset(saved);
        }
        finally{if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        var shared=PrefabUtility.LoadPrefabContents(AppearanceCustomizationBuilder.PrefabPath);
        try{foreach(var text in shared.GetComponentsInChildren<TMP_Text>(true))if(text.font&&text.font.name=="AppearanceHeading")text.font=serif;ConfigureCreationIdentity(shared.GetComponent<AppearanceCustomizationPanel>());PrefabUtility.SaveAsPrefabAsset(shared,AppearanceCustomizationBuilder.PrefabPath);}
        finally{PrefabUtility.UnloadPrefabContents(shared);}
        if(before!=Scenes())throw new InvalidOperationException("사용자 씬 상태가 바뀌었습니다.");
        Directory.CreateDirectory(Path.GetFullPath(Output+"/Evidence/Native"));File.WriteAllText(Path.GetFullPath(Output+"/Evidence/Native/build.json"),JsonConvert.SerializeObject(new{status="PASS_ASSET_BUILD",prefab=PrefabPath,scenesBefore=before,scenesAfter=Scenes(),utc=DateTime.UtcNow},Formatting.Indented));
    }
    public static void ConfigureCreationIdentity(AppearanceCustomizationPanel panel)
    {
        var parent=panel.surface.transform;serif=parent.Find("Title").GetComponent<TMP_Text>().font;sans=parent.Find("Location").GetComponent<TMP_Text>().font;
        if(!panel.back.transform.Find("Creation Back Icon"))Image(panel.back.transform,"Creation Back Icon",17,16,15.6f,15.6f,KitSprite("Controls/Buttons/Square/Icons/Back.png"),Gold).gameObject.SetActive(false);
        var confirmation=panel.discardConfirmation.transform.Find("Confirmation");ConfigureConfirmation(confirmation,panel.continueEditing,panel.discardAndClose);
        var blur=panel.discardConfirmation.transform.Find("Creation Confirmation Backdrop");panel.creationConfirmationBackdrop=blur?blur.GetComponent<RawImage>():Stretch(panel.discardConfirmation.transform,"Creation Confirmation Backdrop").gameObject.AddComponent<RawImage>();panel.creationConfirmationBackdrop.raycastTarget=false;panel.creationConfirmationBackdrop.transform.SetAsFirstSibling();panel.creationConfirmationBackdrop.gameObject.SetActive(false);
        var background=parent.Find("Dressing Alcove").GetComponent<UnityEngine.UI.Image>();background.color=Color.white;background.material=Backdrop("Appearance",.85f,.86f,false);
        var fitter=background.GetComponent<AspectRatioFitter>()??background.gameObject.AddComponent<AspectRatioFitter>();fitter.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fitter.aspectRatio=(float)background.sprite.texture.width/background.sprite.texture.height;
        if(!panel.surface.GetComponent<RectMask2D>())panel.surface.AddComponent<RectMask2D>();
        var existing=parent.Find("Creation Identity");if(existing){panel.creationIdentity=existing.gameObject;panel.creationRename=existing.Find("Rename").GetComponent<Button>();var badgeRect=(RectTransform)existing.Find("Class Badge");badgeRect.sizeDelta=new Vector2(51.0852f,29.5398f);badgeRect.anchoredPosition=new Vector2(0,-1.1421f);var renameRect=(RectTransform)panel.creationRename.transform;renameRect.anchoredPosition=new Vector2(79.875f,0);renameRect.sizeDelta=new Vector2(67.6875f,31.8409f);panel.creationRename.GetComponentInChildren<TMP_Text>().rectTransform.sizeDelta=renameRect.sizeDelta;return;}
        var identity=At(parent,"Creation Identity",320,102,214,32);panel.creationIdentity=identity.gameObject;
        var badge=At(identity,"Class Badge",0,1.1421f,51.0852f,29.5398f).gameObject.AddComponent<AppearanceUiFrame>();badge.color=new Color(.439f,.325f,.18f);badge.fillColor=new Color(.161f,.035f,.024f);badge.thickness=1.2f;badge.raycastTarget=false;
        Text(badge.transform,"Class","기사",0,0,51.0852f,29.5398f,14.4f,false,Gold,TextAlignmentOptions.Center);
        panel.creationRename=Button(identity,"Rename","이름 변경",79.875f,0,67.6875f,31.8409f);panel.creationRename.GetComponentInChildren<TMP_Text>().font=sans;panel.creationRename.GetComponentInChildren<TMP_Text>().fontSize=14.4f;
        panel.creationRename.GetComponent<Image>().color=Color.clear;panel.creationRename.transform.Find("Skin").GetComponent<Image>().enabled=false;
        identity.gameObject.SetActive(false);
    }
    static void BuildClasses(RectTransform parent,TitleCharacterPanel panel)
    {
        var cast=Stretch(parent,"Class Cast");panel.classButtons=new Button[3];panel.classTargets=new RawImage[3];panel.classPreviews=new AppearanceCharacterPreview[3];panel.classCaptions=new TMP_Text[3];
        for(int i=0;i<3;i++)
        {
            string name=new[]{"기사","마법사","궁수"}[i];var rect=At(cast,name,0,0,384,828);rect.pivot=new Vector2(.5f,0);
            var hit=rect.gameObject.AddComponent<Image>();hit.color=Color.clear;var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.transition=Selectable.Transition.None;button.navigation=new Navigation{mode=Navigation.Mode.None};
            var target=At(rect,"Model",-84,0,552,828).gameObject.AddComponent<RawImage>();target.raycastTarget=false;
            panel.classButtons[i]=button;panel.classTargets[i]=target;panel.classPreviews[i]=target.gameObject.AddComponent<AppearanceCharacterPreview>();
            panel.classCaptions[i]=Text(rect,"Class Caption",name,0,775.2f,384,47.04f,33.6f,true,i==0?Gold:new Color(.722f,.635f,.506f),TextAlignmentOptions.Center);
            Text(rect,"Availability",i==0?"생성 가능":"개발 중",0,827.04f,384,20.16f,14.4f,false,new Color(.553f,.514f,.455f),TextAlignmentOptions.Center);
        }
        panel.classEnglish=Text(parent,"English","KNIGHT",1488,268.909f,342,21.6f,14.4f,false,new Color(.651f,.576f,.447f));panel.classEnglish.characterSpacing=50;
        panel.className=Text(parent,"Class Name","기사",1488,307.295f,342,104.165f,74.4f,true,Gold);
        Image(parent,"Details Rule",1488,437.847f,342,10.8f,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Border.png"),new Color(.82f,.68f,.40f,.65f));
        panel.classDescription=Text(parent,"Description","강철 갑옷을 입고 대검을 휘두르는 근접 전투 클래스.",1488,494.289f,342,100.734f,19.2f,false,new Color(.737f,.706f,.647f));panel.classDescription.alignment=TextAlignmentOptions.TopLeft;panel.classDescription.textWrappingMode=TextWrappingModes.Normal;panel.classDescription.lineSpacing=54.5f;
        Line(parent,"Profile Top",1488,627.4f,342,new Color(.576f,.502f,.384f,.20f));Line(parent,"Profile Bottom",1488,773.52f,342,new Color(.576f,.502f,.384f,.20f));
        Text(parent,"Role Label","전투 방식",1488,665.676f,170,27,18,false,new Color(.608f,.549f,.463f));
        Text(parent,"Weapon Label","주 무기",1488,709.449f,170,27,18,false,new Color(.608f,.549f,.463f));
        panel.classRole=Text(parent,"Role","근접 전투",1658,665.676f,172,27,18,false,Ivory,TextAlignmentOptions.Right);
        panel.classWeapon=Text(parent,"Weapon","대검",1658,709.449f,172,27,18,false,Ivory,TextAlignmentOptions.Right);
        panel.classAvailability=Text(parent,"Class Availability","현재 생성 가능",1488,803.506f,342,26.5f,15.6f,false,new Color(.655f,.580f,.459f));
        panel.classNext=Primary(parent,"Class Next","기사로 생성",1514,997.909f,342,62);
    }
    static void BuildRoster(RectTransform parent,TitleCharacterPanel panel)
    {
        Text(parent,"Roster Heading","나의 캐릭터",96.8f,190.91f,320,32,21.6f,true,new Color(.804f,.710f,.545f));
        panel.rosterCount=Text(parent,"Roster Count","0명",451,190.91f,56,32,14.4f,false,new Color(.549f,.486f,.396f),TextAlignmentOptions.Right);
        var viewport=At(parent,"Roster Viewport",93.187f,251.335f,411.614f,456);viewport.gameObject.AddComponent<RectMask2D>();
        panel.rosterContent=At(viewport,"Roster Content",0,0,411.614f,139.2f);var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=panel.rosterContent;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
        var row=At(panel.rosterContent,"Character Template",0,0,411.614f,139.2f);var fill=row.gameObject.AddComponent<Image>();fill.color=new Color(.055f,.047f,.039f,.937f);
        var native=Image(row,"Slot Skin",0,0,411.614f,139.2f,KitSprite("Lobby/Character Select List/Lobby_CSList_Character_Background.png"),new Color(1,1,1,.65f));native.type=UnityEngine.UI.Image.Type.Sliced;native.pixelsPerUnitMultiplier=3;
        var border=Stretch(row,"Border").gameObject.AddComponent<AppearanceUiFrame>();border.thickness=1.2f;border.color=new Color(.349f,.29f,.22f);border.raycastTarget=false;
        var button=row.gameObject.AddComponent<Button>();button.targetGraphic=border;var visual=row.gameObject.AddComponent<AppearanceButtonVisual>();visual.backing=fill;visual.skin=native;visual.normalSkin=native.color;visual.selectedSkin=new Color(1,1,1,.45f);
        var mark=Stretch(row,"Selected Border").gameObject.AddComponent<AppearanceUiFrame>();mark.color=new Color(.69f,.569f,.345f);mark.thickness=1.2f;mark.raycastTarget=false;visual.selectedOutline=mark;
        var avatar=Image(row,"Avatar",26.4f,22.2f,86.4f,94.8f,panel.catalog.faces[0].thumbnail,Color.white);avatar.preserveAspect=false;
        panel.rosterTemplate=button;Text(row,"Name","캐릭터",134.4f,31.5f,254,36,28.8f,true,new Color(.945f,.835f,.631f));Text(row,"Info","Lv. 1 · 기사",134.4f,75.9f,254,23,14.4f,false,new Color(.557f,.518f,.451f));row.gameObject.SetActive(false);
        panel.create=Button(parent,"Create Character","",92,728.864f,412.8f,112.8f);
        Text(panel.create.transform,"Plus","＋",30,20,46,70,45.6f,true,new Color(.671f,.537f,.329f));Text(panel.create.transform,"Create Label","캐릭터 생성",103,23,280,34,24,true,new Color(.788f,.682f,.51f));Text(panel.create.transform,"Create Hint","새로운 모험을 시작하세요",103,65,280,22,13.2f,false,new Color(.608f,.541f,.443f));
        panel.rosterTarget=Stretch(parent,"Selected Character Render").gameObject.AddComponent<RawImage>();panel.rosterTarget.raycastTarget=false;panel.rosterPreview=panel.rosterTarget.gameObject.AddComponent<AppearanceCharacterPreview>();panel.rosterTarget.transform.SetSiblingIndex(2);
        panel.characterName=Text(parent,"Character Name","새로운 모험",1482,248.489f,312,62.625f,43.2f,true,Gold);
        panel.characterLevel=Text(parent,"Character Level","",1482,323.114f,72,20,13.2f,false,new Color(.706f,.627f,.518f));
        panel.characterDetails=Text(parent,"Character Type","",1570,323.114f,242,20,13.2f,false,new Color(.706f,.627f,.518f));
        Line(parent,"Identity Rule",1482,369.6f,312,new Color(.455f,.365f,.239f,.467f));
        Text(parent,"Record Heading","모험 기록",1482,399.6f,312,44,24,true,new Color(.843f,.741f,.565f));
        Image(parent,"Record Rule",1482,438,312,10.8f,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Border.png"),Color.white);
        Text(parent,"Location Label","마지막 위치",1482,474,312,19,13.2f,false,new Color(.6f,.553f,.475f));panel.characterLocation=Text(parent,"Location","은신처",1482,505,312,34,21.6f,true,Ivory);
        Text(parent,"Time Label","플레이 시간",1482,563,312,19,13.2f,false,new Color(.6f,.553f,.475f));panel.characterTime=Text(parent,"Time","—",1482,594,312,34,21.6f,true,Ivory);
        Text(parent,"Last Label","마지막 접속",1482,652,312,19,13.2f,false,new Color(.6f,.553f,.475f));panel.characterLast=Text(parent,"Last","—",1482,683,312,34,21.6f,true,Ivory);
        Line(parent,"Element Rule",1482,746,312,new Color(.455f,.376f,.259f,.4f));Text(parent,"Element Label","장착 원소",1482,775,312,20,13.2f,false,new Color(.6f,.553f,.475f));panel.characterElement=Text(parent,"Element","없음",1482,809,312,38,25.2f,true,new Color(.824f,.725f,.557f));
        panel.enter=Primary(parent,"Enter Adventure","모험 시작",1514,997.909f,342,62);
    }
    static void BuildName(RectTransform parent,TitleCharacterPanel panel)
    {
        var overlay=Stretch(parent,"Name Dialog");panel.nameDialog=overlay.gameObject;var dim=overlay.gameObject.AddComponent<Image>();dim.color=new Color(0,0,0,.6f);
        var overlayCanvas=overlay.gameObject.AddComponent<Canvas>();overlayCanvas.overrideSorting=true;overlayCanvas.sortingOrder=400;overlay.gameObject.AddComponent<GraphicRaycaster>();
        panel.modalBackdrop=Stretch(overlay,"Blurred Stage").gameObject.AddComponent<RawImage>();panel.modalBackdrop.raycastTarget=false;
        string blurPath=Art+"/ModalBlur.mat";panel.modalBlur=AssetDatabase.LoadAssetAtPath<Material>(blurPath);if(!panel.modalBlur){panel.modalBlur=new Material(Shader.Find("Overburst/UI/Title Modal Blur"));AssetDatabase.CreateAsset(panel.modalBlur,blurPath);}
        var box=At(overlay,"Window",585.6f,247.6f,748.8f,582.6f);var backing=box.gameObject.AddComponent<Image>();backing.color=Color.white;backing.material=WindowMaterial();
        var border=Image(box,"Frame",0,0,748.8f,582.6f,KitSprite("Miscellaneous/General/General_Container_Border.png"),Color.white);border.type=UnityEngine.UI.Image.Type.Sliced;border.pixelsPerUnitMultiplier=2.25f;
        for(int i=0;i<4;i++){var corner=Image(box,"Corner "+i,i%2==0?0:748.8f,i<2?0:582.6f,67.2f,67.2f,KitSprite("Miscellaneous/General/General_Container_CornerOrnament.png"),new Color(1,1,1,.7f));corner.rectTransform.localScale=new Vector3(i%2==0?1:-1,i<2?1:-1,1);}
        var header=Image(box,"Header",8.73f,8.73f,731.34f,108,KitSprite("Windows/Header/Window_Header_Background.png"),new Color(.52f,.141f,.122f));
        HeaderOrnament(header.transform,"Left Ornament",0,false);HeaderOrnament(header.transform,"Right Ornament",731.34f,true);
        Text(box,"English","NEW CHARACTER",240,34.468f,268.8f,12,10.8f,false,new Color(.745f,.635f,.435f),TextAlignmentOptions.Center).characterSpacing=20;
        Text(box,"Heading","이름 지정",220,52.468f,308.8f,37.432f,31.2f,true,Gold,TextAlignmentOptions.Center);
        var close=Button(box,"Close Name","",672.866f,40.587f,43.2f,43.2f);close.onClick=new Button.ButtonClickedEvent();close.transform.Find("Skin").GetComponent<Image>().enabled=false;
        Image(close.transform,"Square",0,0,43.2f,43.2f,KitSprite("Controls/Buttons/Square/Button_Square_Foreground_Red.png"),Color.white);Image(close.transform,"X",10.8f,10.8f,21.6f,21.6f,KitSprite("Controls/Buttons/Square/Icons/X.png"),new Color(1,1,1,.95f));
        panel.nameBack=Button(box,"Name Back","클래스 선택으로",131.673f,471.292f,228,62.4f);panel.nameBack.GetComponentInChildren<TMP_Text>().fontSize=19.2f;
        Text(box,"Intro","기사 캐릭터의 이름을 정해 주세요.",54.32f,149.116f,640.14f,30.24f,16.8f,false,new Color(.702f,.647f,.553f));
        Text(box,"Name Label","캐릭터 이름",54.32f,211.741f,640.14f,32.73f,22.8f,true,new Color(.82f,.714f,.51f));
        var inputRect=At(box,"Name Input",54.32f,260.065f,640.14f,74.39f);var inputBg=inputRect.gameObject.AddComponent<Image>();inputBg.color=new Color(.043f,.035f,.027f);
        var inputFrame=Image(inputRect,"Input Frame",0,0,640.14f,74.39f,KitSprite("Controls/Input Field/InputField_Border.png"),new Color(1,1,1,.8f));inputFrame.type=UnityEngine.UI.Image.Type.Sliced;inputFrame.pixelsPerUnitMultiplier=2.5f;
        var focus=Stretch(inputRect,"Focus").gameObject.AddComponent<AppearanceUiFrame>();focus.color=new Color(.557f,.443f,.286f);focus.thickness=1.2f;focus.raycastTarget=false;
        var textArea=At(inputRect,"Text Area",27.6f,0,520.14f,74.39f);textArea.gameObject.AddComponent<RectMask2D>();
        var inputText=Text(textArea,"Text","",0,0,520.14f,74.39f,27.6f,true,new Color(.922f,.82f,.627f));
        var placeholder=Text(textArea,"Placeholder","이름을 입력하세요",0,0,520.14f,74.39f,20.4f,true,new Color(.435f,.392f,.329f));
        panel.nameInput=inputRect.gameObject.AddComponent<TMP_InputField>();panel.nameInput.textViewport=textArea;panel.nameInput.textComponent=(TextMeshProUGUI)inputText;panel.nameInput.placeholder=placeholder;panel.nameInput.targetGraphic=inputBg;panel.nameInput.characterLimit=12;panel.nameInput.lineType=TMP_InputField.LineType.SingleLine;
        panel.nameCounter=Text(inputRect,"Counter","0 / 12",542,26,70,22,14.4f,false,new Color(.667f,.592f,.467f),TextAlignmentOptions.Right);
        Text(box,"Help","한글, 영문, 숫자 · 2–12자",54.32f,350.048f,640.14f,16.36f,13.2f,false,new Color(.549f,.494f,.408f));
        panel.nameError=Text(box,"Name Error","",54.32f,384.411f,640.14f,22.79f,14.4f,false,new Color(.839f,.620f,.529f));
        Line(box,"Actions Rule",54.32f,438,640.14f,new Color(.36f,.286f,.188f,.4f));
        panel.nameNext=Button(box,"Name Next","외모 설정으로  ›",378.866f,469.485f,315.6f,66,true);panel.nameNext.GetComponentInChildren<TMP_Text>().fontSize=21.6f;
        panel.nameClose=close;
    }
    static void BuildQuit(RectTransform parent,TitleCharacterPanel panel)
    {
        var overlay=Stretch(parent,"Quit Dialog");panel.quitDialog=overlay.gameObject;overlay.gameObject.AddComponent<Image>().color=new Color(0,0,0,.6f);var canvas=overlay.gameObject.AddComponent<Canvas>();canvas.overrideSorting=true;canvas.sortingOrder=400;overlay.gameObject.AddComponent<GraphicRaycaster>();
        panel.quitBackdrop=Stretch(overlay,"Blurred Stage").gameObject.AddComponent<RawImage>();panel.quitBackdrop.raycastTarget=false;
        var box=At(overlay,"Confirmation",619.2f,372.7f,681.6f,332.42f);box.gameObject.AddComponent<Image>();Text(box,"Question","게임을 종료할까요?",0,0,568,46,32.4f,true,Gold,TextAlignmentOptions.Center);Text(box,"Explanation","OVERBURST를 종료합니다.",0,0,568,28,15.6f,false,Muted,TextAlignmentOptions.Center);
        panel.quitCancel=Button(box,"Quit Cancel","취소",0,0,180,62.4f);panel.quitConfirm=Button(box,"Quit Confirm","종료",0,0,264,66,true);ConfigureConfirmation(box,panel.quitCancel,panel.quitConfirm);overlay.gameObject.SetActive(false);
    }
    static Material WindowMaterial(){var material=Backdrop("Window",1,1,false);material.SetFloat("_WindowGradient",1);EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;}
    static void ConfigureConfirmation(Transform box,Button cancel,Button confirm)
    {
        var rect=(RectTransform)box;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(0,1.09f);rect.sizeDelta=new Vector2(681.6f,332.42f);
        var backing=box.GetComponent<UnityEngine.UI.Image>();backing.sprite=null;backing.type=UnityEngine.UI.Image.Type.Simple;backing.color=Color.white;backing.material=WindowMaterial();
        if(!box.Find("Frame")){var frame=Image(box,"Frame",0,0,681.6f,332.42f,KitSprite("Miscellaneous/General/General_Container_Border.png"),Color.white);frame.type=UnityEngine.UI.Image.Type.Sliced;frame.pixelsPerUnitMultiplier=2.25f;}
        for(int i=0;i<4;i++){var corner=box.Find("Corner "+i);if(corner)corner.gameObject.SetActive(false);}
        if(!box.Find("Actions Rule"))Line(box,"Actions Rule",56.72f,187.006f,568.14f,new Color(.36f,.286f,.188f,.4f));
        var question=box.Find("Question").GetComponent<TMP_Text>();question.font=serif;question.fontSize=32.4f;question.color=new Color(.863f,.757f,.553f);Position(question.rectTransform,56.72f,60.324f,568.14f,45.818f);question.overflowMode=TextOverflowModes.Overflow;
        var explanation=box.Find("Explanation").GetComponent<TMP_Text>();explanation.font=sans;explanation.fontSize=15.6f;explanation.color=new Color(.667f,.592f,.471f);Position(explanation.rectTransform,56.72f,127.739f,568.14f,28.074f);explanation.overflowMode=TextOverflowModes.Overflow;
        Position((RectTransform)cancel.transform,109.193f,219.903f,180,62.4f);Position((RectTransform)confirm.transform,308.386f,218.096f,264,66);
        foreach(var control in new[]{cancel,confirm}){bool rich=control==confirm;control.GetComponent<Image>().color=new Color(.043f,.035f,.031f);var label=control.GetComponentInChildren<TMP_Text>();label.font=serif;label.fontSize=rich?21.6f:20.4f;label.color=rich?new Color(.906f,.8f,.624f):Ivory;label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.offsetMin=label.rectTransform.offsetMax=Vector2.zero;label.overflowMode=TextOverflowModes.Overflow;var skin=control.transform.Find("Native Skin");if(skin){var image=skin.GetComponent<Image>();image.enabled=rich;image.sprite=KitSprite("Controls/Buttons/Rectangular/Button_RL_Foreground.png");image.pixelsPerUnitMultiplier=2.5f;image.color=new Color(.765f,.212f,.133f);var visual=control.GetComponent<AppearanceButtonVisual>();visual.normalSkin=visual.selectedSkin=image.color;var border=control.transform.Find("Native Border").GetComponent<Image>();border.enabled=!rich;border.sprite=KitSprite("Controls/Buttons/Rectangular/Button_RS_Border2.png");border.pixelsPerUnitMultiplier=2;}}
    }
    static void Position(RectTransform rect,float x,float y,float width,float height){rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new{path=SceneManager.GetSceneAt(i).path,dirty=SceneManager.GetSceneAt(i).isDirty,roots=SceneManager.GetSceneAt(i).rootCount}));
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;Folder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
    static Texture Import(string name,string source)
    {
        string path=Art+"/"+name+".png";File.Copy(Path.GetFullPath(source),path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.npotScale=TextureImporterNPOTScale.None;importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }
    static void SyncApprovedThumbnails()
    {
        var references=Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(Path.GetFullPath(Design+"/asset-sources.json")));
        foreach(var entry in references)
        {
            string target=((string)entry["source"]).Replace('\\','/');const string prefix="ProjectOverburst/";
            if(!target.StartsWith(prefix+"Assets/ProjectOverburst/05_Art/UI/Appearance/Thumbnails/",StringComparison.Ordinal))continue;
            target=target.Substring(prefix.Length);string source=Path.GetFullPath(Design+"/"+(string)entry["webPath"]);
            if(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target)))continue;
            string recovery=Path.GetFullPath(Output+"/Recovery/Thumbnails-before-approved/");Directory.CreateDirectory(recovery);string backup=recovery+Path.GetFileName(target);if(!File.Exists(backup))File.Copy(target,backup,false);
            File.Copy(source,target,true);AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport);
        }
    }
    static Material Backdrop(string name,float brightness,float saturation,bool vignette)
    {
        var shader=Shader.Find("Overburst/UI/Title Backdrop")??throw new InvalidOperationException("배경 shader가 없습니다.");string path=Art+"/"+name+"Backdrop.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}material.SetFloat("_Brightness",brightness);material.SetFloat("_Saturation",saturation);material.SetFloat("_Vignette",vignette?1:0);EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;
    }
    static RectTransform At(Transform parent,string name,float x,float y,float w,float h){var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.gameObject.layer=5;rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.sizeDelta=new Vector2(w,h);rect.anchoredPosition=new Vector2(x,-y);return rect;}
    static RectTransform Stretch(Transform parent,string name){var rect=At(parent,name,0,0,0,0);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;return rect;}
    static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,float size,bool heading,Color color,TextAlignmentOptions align=TextAlignmentOptions.Left){var text=At(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();text.font=heading?serif:sans;text.text=value;text.fontSize=size;text.color=color;text.alignment=align;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Overflow;text.raycastTarget=false;return text;}
    static Sprite KitSprite(string path)=>AssetDatabase.LoadAssetAtPath<Sprite>(Kit+path)??throw new InvalidOperationException(path);
    static void HeaderOrnament(Transform parent,string name,float x,bool mirrored){var texture=KitSprite("Windows/Header/Window_Header_Ornaments.png").texture;var image=At(parent,name,x,0,246,106.91f).gameObject.AddComponent<RawImage>();image.texture=texture;image.uvRect=new Rect(0,0,246/(106.91f*texture.width/texture.height),1);image.raycastTarget=false;if(mirrored)image.rectTransform.localScale=new Vector3(-1,1,1);}
    static Image Image(Transform parent,string name,float x,float y,float w,float h,Sprite sprite,Color color){var image=At(parent,name,x,y,w,h).gameObject.AddComponent<Image>();image.sprite=sprite;image.color=color;image.raycastTarget=false;return image;}
    static void Line(Transform parent,string name,float x,float y,float w,Color color){var image=At(parent,name,x,y,w,1.2f).gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;}
    static void Feathers(Transform parent){var sprite=KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Shadow.png");Image(parent,"Left Feather",0,0,590,1080,sprite,new Color(0,0,0,.86f));var right=Image(parent,"Right Feather",1920,0,580,1080,sprite,new Color(0,0,0,.86f));right.rectTransform.localScale=new Vector3(-1,1,1);}
    static Button Button(Transform parent,string name,string value,float x,float y,float w,float h,bool rich=false)
    {
        var rect=At(parent,name,x,y,w,h);var fill=rect.gameObject.AddComponent<Image>();fill.color=new Color(.043f,.035f,.031f);
        var skin=Image(rect,"Skin",0,0,w,h,KitSprite(rich?"Controls/Buttons/Rectangular/Button_RL_Foreground.png":"Controls/Buttons/Rectangular/Button_RS_Border2.png"),rich?new Color(.765f,.212f,.133f):Color.white);skin.type=UnityEngine.UI.Image.Type.Sliced;skin.pixelsPerUnitMultiplier=rich?2.5f:2;
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=skin;var colors=button.colors;colors.highlightedColor=new Color(1,.96f,.85f);colors.pressedColor=new Color(.7f,.65f,.55f);colors.selectedColor=colors.normalColor;colors.disabledColor=new Color(.4f,.4f,.4f,.58f);button.colors=colors;
        if(value.Length>0)Text(rect,"Label",value,0,0,w,h,rich?29:21,true,rich?new Color(.906f,.8f,.624f):Ivory,TextAlignmentOptions.Center);
        return button;
    }
    public static void ConfigureHover(Button button)
    {
        bool existing=button.GetComponent<AppearanceButtonVisual>();
        var visual=button.GetComponent<AppearanceButtonVisual>()??button.gameObject.AddComponent<AppearanceButtonVisual>();
        visual.label=visual.label?visual.label:button.GetComponentInChildren<TMP_Text>(true);
        if(!existing)
        {
            var fill=button.GetComponent<Image>();
            if(fill&&fill.color.a>.01f){visual.backing=fill;visual.normalBacking=visual.selectedBacking=fill.color;}
            var skin=button.transform.Find("Skin");if(skin)visual.skin=skin.GetComponent<Image>();
            if(visual.skin)visual.normalSkin=visual.selectedSkin=visual.skin.color;
            if(visual.label)visual.normalLabel=visual.selectedLabel=visual.label.color;
        }
        var border=button.targetGraphic as Image;
        if(border&&border!=visual.skin&&border!=visual.backing&&border.color.a>.01f){visual.hoverBorder=border;visual.normalBorder=border.color;}
        if(visual.skin&&visual.skin.sprite&&visual.skin.sprite.name.Contains("Button_RL"))visual.hoverFill=visual.normalBacking;
        button.transition=Selectable.Transition.None;
    }
    public static void RefreshHoverVisuals()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        foreach(string path in new[]{PrefabPath,AppearanceCustomizationBuilder.PrefabPath})
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try{foreach(var button in root.GetComponentsInChildren<Button>(true))ConfigureHover(button);PrefabUtility.SaveAsPrefabAsset(root,path);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
    }
    static Button Primary(Transform parent,string name,string value,float x,float y,float w,float h)
    {
        var source=AssetDatabase.LoadAssetAtPath<AppearanceCustomizationPanel>(AppearanceCustomizationBuilder.PrefabPath).apply;
        var button=Object.Instantiate(source,parent,false);button.name=name;var rect=(RectTransform)button.transform;rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);button.GetComponentInChildren<TMP_Text>().text=value;return button;
    }
}
