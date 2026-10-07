using System;
using System.Collections.Generic;
using System.Linq;
using Overburst.Appearance;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class AppearanceCustomizationBuilder
{
    static readonly List<AppearanceChoiceButton> builtChoices=new List<AppearanceChoiceButton>();
    static void BuildPanel(CharacterAppearanceCatalog catalog)
    {
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
        try
        {
            root=new GameObject("PF_OverburstAppearance_Rpg11",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            root.layer=5;
            SceneManager.MoveGameObjectToScene(root,scene);root.SetActive(false);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=350;canvas.overrideSorting=true;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=1f;
            var panel=root.AddComponent<AppearanceCustomizationPanel>();panel.rootCanvas=canvas;panel.catalog=catalog;
            var surface=Stretch(root.transform,"Fullscreen Surface");panel.surface=surface.gameObject;
            var background=Stretch(surface,"Dressing Alcove").gameObject.AddComponent<Image>();background.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+"/AppearanceBackground.png");background.color=new Color(.64f,.59f,.56f,1);background.raycastTarget=true;
            var raw=Stretch(surface,"Character Render").gameObject.AddComponent<RawImage>();raw.color=Color.white;raw.raycastTarget=true;panel.characterTarget=raw;
            panel.preview=raw.gameObject.AddComponent<AppearanceCharacterPreview>();
            // Section shadows use the native UI11 feather texture. No enclosing window frame.
            var leftShade=At(surface,"Left Feather",0,0,590,1080);var shadow=leftShade.gameObject.AddComponent<Image>();shadow.sprite=KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Shadow.png");shadow.color=new Color(0,0,0,.86f);shadow.raycastTarget=false;
            var rightShade=At(surface,"Right Feather",1340,0,580,1080);var rightShadow=rightShade.gameObject.AddComponent<Image>();rightShadow.sprite=shadow.sprite;rightShadow.color=shadow.color;rightShadow.raycastTarget=false;rightShade.localScale=new Vector3(-1,1,1);
            rightShade.anchorMin=rightShade.anchorMax=Vector2.one;rightShade.anchoredPosition=Vector2.zero;
            // Keep decorative dimming behind the model, including wide motion poses.
            raw.transform.SetAsLastSibling();
            ImageAt(surface,"Title Crystal",64,45,32,36,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Crystal.png"),new Color(.75f,.16f,.12f));
            Text(surface,"Title","외모 변경",118,26,360,72,42,true,Gold);
            ImageAt(surface,"Title Ornament",118,87,370,9,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Border.png"),Muted);
            Text(surface,"Location","은신처",120,98,300,32,22,false,Ivory);
            panel.appearanceTab=ButtonAt(surface,"Appearance Tab","외모",68,144,180,46);
            var left=At(surface,"Appearance Options",0,192,560,770);panel.appearanceOptions=left.gameObject;
            builtChoices.Clear();
            Section(left,"성별",64,0,425);
            var female=ButtonAt(left,"Female","",92,42,112,96);ImageAt(female.transform,"Female Symbol",29,17,54,56,KitSprite("Lobby/Character Create/Genders/Gender_Female.png"),Gold);
            female.GetComponent<AppearanceButtonVisual>().SetSelected(true);
            Text(left,"Female Label","여성",92,139,112,29,19,false,Gold,TextAlignmentOptions.Center);
            panel.maleLocked=ButtonAt(left,"Male Locked","",232,42,112,96);ImageAt(panel.maleLocked.transform,"Male Symbol",29,17,54,56,KitSprite("Lobby/Character Create/Genders/Gender_Male.png"),Muted);
            Text(panel.maleLocked.transform,"Lock","잠금",58,68,55,24,13,false,Muted,TextAlignmentOptions.Right);
            Text(left,"Male Label","남성 · 준비 중",219,139,144,29,18,false,Muted,TextAlignmentOptions.Center);
            Section(left,"얼굴",64,200,425);
            for(int i=0;i<catalog.faces.Length;i++)Choice(left,AppearanceChoiceKind.Face,catalog.faces[i].id,catalog.faces[i].thumbnail,"",92+i*126,244,110,110);
            Section(left,"머리",64,397,425);
            for(int i=0;i<catalog.hairStyles.Length;i++)Choice(left,AppearanceChoiceKind.Hair,catalog.hairStyles[i].id,catalog.hairStyles[i].thumbnail,"",92+(i%4)*96,442,86,96);
            panel.hairPrevious=ButtonAt(left,"Hair Previous","‹",48,471,32,48,false);
            panel.hairNext=ButtonAt(left,"Hair Next","›",488,471,32,48,false);
            panel.pageHair=Text(left,"Hair Page","",215,546,164,25,14,false,Muted,TextAlignmentOptions.Center);
            for(int i=0;i<catalog.hairColors.Length;i++)Choice(left,AppearanceChoiceKind.HairColor,catalog.hairColors[i].id,catalog.hairColors[i].icon,"",92+i%5*78,582+i/5*68,60,60);
            // Right sections stay available while inspecting animation motion.
            var right=At(surface,"Body and Equipment",1430,168,445,782);right.anchorMin=right.anchorMax=new Vector2(1,1);right.anchoredPosition=new Vector2(-490,-168);
            Section(right,"피부색",0,0,432);
            for(int i=0;i<catalog.skinColors.Length;i++)Choice(right,AppearanceChoiceKind.SkinColor,catalog.skinColors[i].id,catalog.skinColors[i].icon,"",28+i*101,48,80,80);
            Section(right,"눈 색상",0,174,432);
            for(int i=0;i<catalog.eyeColors.Length;i++)Choice(right,AppearanceChoiceKind.EyeColor,catalog.eyeColors[i].id,catalog.eyeColors[i].icon,"",28+i*80,220,68,68);
            Section(right,"상체스타일",0,342,432);
            for(int i=0;i<catalog.bodyStyles.Length;i++)Choice(right,AppearanceChoiceKind.BodyStyle,catalog.bodyStyles[i].id,null,catalog.bodyStyles[i].displayName,28+i*124,389,112,50);
            Section(right,"장비 착용 예시",0,484,432);
            Choice(right,AppearanceChoiceKind.Equipment,"",AssetDatabase.LoadAssetAtPath<Sprite>(Art+"/Thumbnails/underwear.png"),"속옷",28,532,86,96);
            for(int i=0;i<catalog.equipmentExamples.Length;i++)Choice(right,AppearanceChoiceKind.Equipment,catalog.equipmentExamples[i].id,catalog.equipmentExamples[i].thumbnail,catalog.equipmentExamples[i].displayName,28+((i+1)%4)*96,532,86,96);
            panel.equipmentPrevious=ButtonAt(right,"Equipment Previous","‹",-10,560,32,48,false);panel.equipmentNext=ButtonAt(right,"Equipment Next","›",416,560,32,48,false);
            panel.pageEquipment=Text(right,"Equipment Page","",144,662,164,24,14,false,Muted,TextAlignmentOptions.Center);
            Text(right,"Preview Only","미리보기 전용",48,697,338,28,18,false,Muted,TextAlignmentOptions.Center);
            panel.headgearToggle=ButtonAt(right,"Headgear Preview","모자 켜짐",28,744,180,38);
            panel.choices=builtChoices.ToArray();
            panel.back=ButtonAt(surface,"Back","‹  돌아가기",64,1000,180,48,false);
            panel.resetAppearance=ButtonAt(surface,"Reset Appearance","기본 외모",256,1000,180,48,false);
            panel.faceFrame=ButtonAt(surface,"Frame Face","얼굴",732,1008,108,44);
            panel.upperFrame=ButtonAt(surface,"Frame Upper","상체",848,1008,108,44);
            panel.fullFrame=ButtonAt(surface,"Frame Full","전신",964,1008,108,44);
            panel.resetView=ButtonAt(surface,"Reset View","시점",1080,1008,108,44);
            foreach(var control in new[]{panel.faceFrame,panel.upperFrame,panel.fullFrame})control.GetComponent<AppearanceButtonVisual>().selectedMark.name="Active";
            var viewHint=Text(surface,"View Hint","드래그 · 회전     휠 · 확대",706,1057,490,20,15,false,Muted,TextAlignmentOptions.Center);
            panel.apply=ButtonAt(surface,"Apply","외모 적용",1514,990,342,62);
            panel.apply.GetComponent<Image>().color=new Color(.17f,.035f,.023f,1);
            var applySkin=panel.apply.transform.Find("Native Skin").GetComponent<Image>();applySkin.sprite=KitSprite("Controls/Buttons/Rectangular/Button_RL_Foreground.png");applySkin.pixelsPerUnitMultiplier=3f;applySkin.color=new Color(.34f,.07f,.045f,1);
            var primaryFrame=Frame(panel.apply.transform,"Primary Ornament Frame",342,62,new Color(.63f,.46f,.24f,1),1.4f);primaryFrame.beveled=true;primaryFrame.SetVerticesDirty();
            panel.apply.GetComponent<Image>().color=Color.clear;
            primaryFrame.fillColor=Color.clear;
            var primaryMask=Stretch(panel.apply.transform,"Primary Shape").gameObject.AddComponent<AppearanceUiFrame>();primaryMask.beveled=true;primaryMask.color=Color.clear;primaryMask.fillColor=Color.white;primaryMask.raycastTarget=false;
            var mask=primaryMask.gameObject.AddComponent<Mask>();mask.showMaskGraphic=false;
            var primaryBacking=Stretch(primaryMask.transform,"Opaque Beveled Backing").gameObject.AddComponent<AppearanceUiFrame>();primaryBacking.beveled=true;primaryBacking.color=Color.clear;primaryBacking.fillColor=new Color(.17f,.035f,.023f,1);primaryBacking.raycastTarget=false;
            var skinRect=(RectTransform)applySkin.transform;skinRect.SetParent(primaryMask.transform,false);skinRect.anchorMin=Vector2.zero;skinRect.anchorMax=Vector2.one;skinRect.offsetMin=skinRect.offsetMax=Vector2.zero;
            primaryFrame.transform.SetAsLastSibling();
            panel.apply.transform.Find("Native Border").GetComponent<Image>().enabled=false;panel.apply.targetGraphic=primaryFrame;
            var primaryColors=panel.apply.colors;primaryColors.normalColor=new Color(.63f,.46f,.24f,1);primaryColors.highlightedColor=new Color(.98f,.81f,.43f,1);primaryColors.pressedColor=new Color(.46f,.27f,.12f,1);primaryColors.selectedColor=primaryColors.normalColor;panel.apply.colors=primaryColors;
            var applyLabel=panel.apply.GetComponentInChildren<TMP_Text>();applyLabel.transform.SetAsLastSibling();applyLabel.fontSize=29;applyLabel.characterSpacing=5;applyLabel.color=Gold;
            panel.feedback=Text(surface,"Feedback","",570,916,804,40,18,false,Ivory,TextAlignmentOptions.Center);
            Bottom(panel.back.transform,0);Bottom(panel.resetAppearance.transform,0);Bottom(panel.apply.transform,1);
            foreach(var control in new[]{panel.faceFrame,panel.upperFrame,panel.fullFrame,panel.resetView})Bottom(control.transform,.5f);
            Bottom(viewHint.transform,.5f);Bottom(panel.feedback.transform,.5f);
            foreach(var extension in extensions)extension.CreateUI(surface,panel);
            BuildDiscardConfirmation(surface,panel);
            var developer=At(surface,"Temporary Preview Extension",20,1020,24,24);
            Bottom(developer,0);
            var dev=developer.gameObject.AddComponent<AppearanceDeveloperPreview>();dev.panel=panel;
            dev.iconButton=ButtonAt(developer,"Icon","",0,0,24,24,false);
            Object.DestroyImmediate(dev.iconButton.transform.Find("Native Skin").gameObject);Object.DestroyImmediate(dev.iconButton.transform.Find("Native Border").gameObject);Object.DestroyImmediate(dev.iconButton.GetComponent<AppearanceButtonVisual>());Object.DestroyImmediate(dev.iconButton.transform.Find("Native Selection").gameObject);
            var quiet=dev.iconButton.GetComponent<Image>();quiet.color=Color.clear;dev.iconButton.targetGraphic=quiet;
            ImageAt(dev.iconButton.transform,"Quiet Icon",8,8,8,8,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Crystal.png"),new Color(.32f,.32f,.32f,.7f));
            surface.gameObject.SetActive(false);root.SetActive(true);
            var rootRect=(RectTransform)root.transform;rootRect.localScale=Vector3.one;rootRect.localRotation=Quaternion.identity;
            rootRect.anchorMin=Vector2.zero;rootRect.anchorMax=Vector2.one;rootRect.offsetMin=Vector2.zero;rootRect.offsetMax=Vector2.zero;rootRect.anchoredPosition3D=Vector3.zero;
            // Preview scenes have no overlay viewport. Stop its canvas driver while serializing
            // and enable the persistent prefab canvas after it has left the live preview scene.
            canvas.enabled=false;rootRect.localScale=Vector3.one;
            SaveEditorPreviewExtensions(root);
            var saved=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            var canvasData=new SerializedObject(saved.GetComponent<Canvas>());canvasData.FindProperty("m_Enabled").boolValue=true;canvasData.ApplyModifiedPropertiesWithoutUndo();
            var rectData=new SerializedObject(saved.transform);rectData.FindProperty("m_LocalScale").vector3Value=Vector3.one;rectData.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(saved);
        }
        finally{if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Bottom(Transform transform,float horizontalAnchor)
    {
        var rect=(RectTransform)transform;var position=rect.anchoredPosition;
        float y=1080f+position.y-rect.rect.height;
        rect.anchorMin=rect.anchorMax=new Vector2(horizontalAnchor,0);rect.pivot=Vector2.zero;
        rect.anchoredPosition=new Vector2(position.x-1920f*horizontalAnchor,y);
    }
    static RectTransform At(Transform parent,string name,float x,float y,float width,float height)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
        rect.gameObject.layer=parent.gameObject.layer;
        rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.sizeDelta=new Vector2(width,height);rect.anchoredPosition=new Vector2(x,-y);return rect;
    }
    static RectTransform Stretch(Transform parent,string name)
    {
        var rect=At(parent,name,0,0,0,0);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;return rect;
    }
    static Sprite KitSprite(string path)=>AssetDatabase.LoadAssetAtPath<Sprite>(Kit+path)??throw new InvalidOperationException("UI11 원본이 없습니다: "+path);
    static Image ImageAt(Transform parent,string name,float x,float y,float w,float h,Sprite sprite,Color color)
    {
        var image=At(parent,name,x,y,w,h).gameObject.AddComponent<Image>();image.sprite=sprite;image.color=color;image.raycastTarget=false;return image;
    }
    static AppearanceUiFrame Frame(Transform parent,string name,float w,float h,Color color,float thickness)
    {
        var frame=Stretch(parent,name).gameObject.AddComponent<AppearanceUiFrame>();frame.color=color;frame.thickness=thickness;frame.raycastTarget=false;return frame;
    }
    static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,float size,bool heading,Color color,TextAlignmentOptions alignment=TextAlignmentOptions.Left)
    {
        var text=At(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();text.font=heading?headingFont:bodyFont;text.text=value;
        text.fontSize=size;text.color=color;text.alignment=alignment;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;return text;
    }
    static Button ButtonAt(Transform parent,string name,string label,float x,float y,float w,float h,bool framed=true)
    {
        var rect=At(parent,name,x,y,w,h);var fill=rect.gameObject.AddComponent<Image>();fill.color=AppearanceButtonVisual.NormalFill;
        var skin=Stretch(rect,"Native Skin").gameObject.AddComponent<Image>();skin.sprite=KitSprite("Controls/Buttons/Rectangular/Button_RS_Background.png");skin.type=Image.Type.Sliced;skin.pixelsPerUnitMultiplier=4f;skin.color=new Color(.65f,.62f,.58f,.45f);skin.raycastTarget=false;
        var border=Stretch(rect,"Native Border").gameObject.AddComponent<Image>();border.sprite=KitSprite("Controls/Buttons/Rectangular/Button_RS_Border2.png");border.type=Image.Type.Sliced;border.pixelsPerUnitMultiplier=2f;border.raycastTarget=false;
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=border;button.navigation=new Navigation{mode=Navigation.Mode.None};
        var colors=button.colors;colors.normalColor=new Color(.90f,.86f,.76f,1);colors.highlightedColor=new Color(1f,.96f,.86f,1);colors.pressedColor=new Color(.65f,.59f,.48f,1);colors.selectedColor=colors.normalColor;colors.disabledColor=new Color(.32f,.31f,.29f,1);colors.fadeDuration=.12f;button.colors=colors;
        var visual=rect.gameObject.AddComponent<AppearanceButtonVisual>();visual.backing=fill;visual.skin=skin;visual.normalSkin=skin.color;visual.selectedSkin=skin.color;visual.selectedMark=NativeSelection(rect,"Native Selection",false,false);
        visual.selectedOutline=Frame(rect,"Selected Outline",w,h,new Color(.98f,.81f,.43f,1),1.5f);visual.selectedOutline.enabled=false;
        if(!string.IsNullOrEmpty(label)){visual.label=Text(rect,"Label",label,0,0,w,h,h<50?20:26,true,Ivory,TextAlignmentOptions.Center);visual.label.characterSpacing=1;}
        return button;
    }
    static Image NativeSelection(Transform parent,string name,bool card,bool enabled)
    {
        var image=Stretch(parent,name).gameObject.AddComponent<Image>();image.raycastTarget=false;
        image.sprite=KitSprite("Lobby/Character Create/Box/Lobby_CC_Box_Hover.png");image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=8f;image.color=new Color(1f,.92f,.65f,.95f);image.enabled=enabled;return image;
    }
    static void Section(Transform parent,string title,float x,float y,float width)
    {
        ImageAt(parent,title+" Crystal",x,y+12,12,14,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Crystal.png"),new Color(.84f,.21f,.14f));
        Text(parent,title+" Heading",title,x+30,y,width-30,38,26,true,Gold);
        ImageAt(parent,title+" Ornament",x+30,y+35,width-30,11,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Border.png"),new Color(.69f,.62f,.48f,.7f));
    }
    static AppearanceChoiceButton Choice(Transform parent,AppearanceChoiceKind kind,string id,Sprite thumbnail,string caption,float x,float y,float w,float h)
    {
        var button=ButtonAt(parent,kind+" "+id,"",x,y,w,h,false);var rect=(RectTransform)button.transform;
        var visual=button.GetComponent<AppearanceButtonVisual>();var skin=visual.skin;
        bool card=kind!=AppearanceChoiceKind.BodyStyle;
        if(card){skin.sprite=KitSprite("Lobby/Character Create/Box/Lobby_CC_Box_Frame.png");skin.type=Image.Type.Sliced;skin.pixelsPerUnitMultiplier=3.5f;skin.color=new Color(.85f,.81f,.73f,1);visual.normalSkin=skin.color;visual.selectedSkin=skin.color;}
        var art=ImageAt(rect,"Actual Model",card?5:4,card?5:4,w-(card?10:8),h-(card?10:8),thumbnail,Color.white);art.preserveAspect=true;if(!thumbnail)art.enabled=false;
        var border=button.transform.Find("Native Border");border.SetAsLastSibling();var selected=visual.selectedMark;selected.name="Selected";selected.transform.SetAsLastSibling();visual.selectedOutline.transform.SetAsLastSibling();
        var choice=button.gameObject.AddComponent<AppearanceChoiceButton>();choice.kind=kind;choice.optionId=id;choice.button=button;choice.selectedMark=selected;choice.artwork=art;
        if(!string.IsNullOrEmpty(caption))choice.caption=Text(rect,"Caption",caption,0,kind==AppearanceChoiceKind.BodyStyle?0:h+3,w,kind==AppearanceChoiceKind.BodyStyle?h:27,kind==AppearanceChoiceKind.BodyStyle?21:16,kind==AppearanceChoiceKind.BodyStyle,Gold,TextAlignmentOptions.Center);
        if(kind==AppearanceChoiceKind.BodyStyle)visual.label=choice.caption;
        builtChoices.Add(choice);return choice;
    }
    static void BuildDiscardConfirmation(Transform parent,AppearanceCustomizationPanel panel)
    {
        var overlay=Stretch(parent,"Discard Confirmation");panel.discardConfirmation=overlay.gameObject;
        var dim=overlay.gameObject.AddComponent<Image>();dim.color=new Color(0,0,0,.72f);
        var box=At(overlay,"Confirmation",650,382,620,270);var image=box.gameObject.AddComponent<Image>();image.sprite=KitSprite("Controls/Buttons/Rectangular/Button_RS_Background.png");image.type=Image.Type.Sliced;image.color=new Color(.19f,.13f,.10f);
        Text(box,"Question","변경 내용을 버릴까요?",45,32,530,56,30,true,Gold,TextAlignmentOptions.Center);
        Text(box,"Explanation","변경한 외모는 저장되지 않습니다.",45,105,530,35,20,false,Ivory,TextAlignmentOptions.Center);
        panel.continueEditing=ButtonAt(box,"Continue","계속 편집",45,190,240,48);panel.discardAndClose=ButtonAt(box,"Discard","돌아가기",335,190,240,48);
        overlay.gameObject.SetActive(false);
    }
    [MenuItem("Overburst/UI/외모 커스터마이징/UI만 다시 만들기")]
    public static void RebuildPanel()
    {
        RequireIdle();string before=SceneState();
        var catalog=AssetDatabase.LoadAssetAtPath<CharacterAppearanceCatalog>(CatalogPath);
        headingFont=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/Fonts/AppearanceHeading.asset");
        bodyFont=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/Fonts/AppearanceBody.asset");
        if(!catalog||!headingFont||!bodyFont)throw new InvalidOperationException("제품 자산을 먼저 만들어 주세요.");
        extensions=TypeCache.GetTypesDerivedFrom<AppearanceAuthoringExtension>().Where(t=>!t.IsAbstract).Select(t=>(AppearanceAuthoringExtension)Activator.CreateInstance(t)).ToArray();
        foreach(var extension in extensions)extension.Prepare(catalog);
        CaptureThumbnails(catalog);BuildPanel(catalog);BuildShowcase();AssetDatabase.SaveAssetIfDirty(catalog);
        if(before!=SceneState())throw new InvalidOperationException("열린 사용자 씬 상태가 변경됐습니다.");
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath(Output+"/panel-build-result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status="PASS_NATIVE_PANEL_REBUILD",sceneBefore=before,sceneAfter=SceneState(),utc=DateTime.UtcNow},Newtonsoft.Json.Formatting.Indented));
    }
    [MenuItem("Overburst/UI/외모 커스터마이징/임시 아이콘 제거")]
    public static void RemoveDeveloperExtension()
    {
        RequireIdle();var root=PrefabUtility.LoadPrefabContents(AppearanceCustomizationPanel.EditorPreviewPath);
        try{foreach(var component in root.GetComponentsInChildren<AppearanceDeveloperPreview>(true))Object.DestroyImmediate(component.gameObject);PrefabUtility.SaveAsPrefabAsset(root,AppearanceCustomizationPanel.EditorPreviewPath);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
}
