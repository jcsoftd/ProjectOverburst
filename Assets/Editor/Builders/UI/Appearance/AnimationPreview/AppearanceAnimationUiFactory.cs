using Overburst.Appearance;
using Overburst.Appearance.AnimationPreview;
using UnityEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static partial class AppearanceCustomizationBuilder
{
    public static void CreateAnimationTab(Transform parent,AppearanceCustomizationPanel owner,AppearanceAnimationLibrary library)
    {
        var extension=Stretch(parent,"Animation Preview Extension");
        var panel=extension.gameObject.AddComponent<AppearanceAnimationPreviewTab>();panel.library=library;
        panel.tabButton=ButtonAt(extension,"Animation Tab","애니메이션",260,144,180,46);
        var root=At(extension,"Animation Page",64,217,462,757);panel.page=root.gameObject;
        panel.gameCategory=ButtonAt(root,"Game Category","게임",0,0,134,42);
        panel.kawaiiCategory=ButtonAt(root,"Kawaii Category","모션",142,0,134,42);
        panel.expressionCategory=ButtonAt(root,"Expression Category","표정",284,0,134,42);
        var searchRect=At(root,"Animation Search",0,58,426,46);var background=searchRect.gameObject.AddComponent<Image>();background.color=new Color(.04f,.032f,.025f,.95f);
        var border=Stretch(searchRect,"Native Search Border").gameObject.AddComponent<Image>();border.sprite=KitSprite("Controls/Buttons/Rectangular/Button_RS_Border.png");border.type=Image.Type.Sliced;border.pixelsPerUnitMultiplier=3f;border.color=Muted;border.raycastTarget=false;
        panel.search=searchRect.gameObject.AddComponent<TMP_InputField>();panel.search.targetGraphic=background;
        var viewport=At(searchRect,"Text Viewport",13,4,399,38);viewport.gameObject.AddComponent<RectMask2D>();
        var value=Text(viewport,"Text","",0,0,399,38,19,false,Ivory);var placeholder=Text(viewport,"Placeholder","애니메이션 검색",0,0,399,38,19,false,Muted);
        panel.search.textViewport=viewport;panel.search.textComponent=(TextMeshProUGUI)value;panel.search.placeholder=placeholder;
        panel.search.characterLimit=64;panel.search.lineType=TMP_InputField.LineType.SingleLine;
        var scrollRoot=At(root,"Animation Scroll",0,119,432,375);var scroll=scrollRoot.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=33;
        var view=Stretch(scrollRoot,"Viewport");view.gameObject.AddComponent<RectMask2D>();var viewImage=view.gameObject.AddComponent<Image>();viewImage.color=new Color(.022f,.019f,.016f,.66f);
        var content=At(view,"Content",0,0,432,0);content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.sizeDelta=Vector2.zero;
        var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(8,8,7,7);layout.spacing=5;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
        var fitter=content.gameObject.AddComponent<ContentSizeFitter>();fitter.horizontalFit=ContentSizeFitter.FitMode.Unconstrained;fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport=view;scroll.content=content;panel.list=content;
        view.offsetMax=new Vector2(-14,0);
        var barRoot=At(scrollRoot,"Motion Scrollbar",420,0,10,375);var bar=barRoot.gameObject.AddComponent<Scrollbar>();
        var track=barRoot.gameObject.AddComponent<Image>();track.color=new Color(.14f,.11f,.075f,.8f);
        var handle=Stretch(barRoot,"Handle");var handleImage=handle.gameObject.AddComponent<Image>();
        handleImage.sprite=KitSprite("Controls/Scroll Bars/ScrollBar_Vertical_Handle.png");handleImage.type=Image.Type.Sliced;handleImage.color=Gold;
        bar.handleRect=handle;bar.targetGraphic=handleImage;bar.direction=Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
        panel.rowTemplate=ButtonAt(root,"Animation Button Template","",0,0,414,40);
        var preferred=panel.rowTemplate.gameObject.AddComponent<LayoutElement>();preferred.preferredHeight=66;
        ImageAt(panel.rowTemplate.transform,"Motion Thumbnail",3,3,60,60,null,Color.white);
        Text(panel.rowTemplate.transform,"Motion Name","Animation",78,0,321,66,18,false,Ivory);
        panel.rowTemplate.GetComponent<AppearanceButtonVisual>().selectedMark.name="Selection";
        panel.rowTemplate.gameObject.SetActive(false);
        panel.playPause=ButtonAt(root,"Play Pause","재생",0,550,133,40);panel.playPauseLabel=panel.playPause.GetComponentInChildren<TMP_Text>();
        panel.stop=ButtonAt(root,"Stop","정지",143,550,133,40);
        var toggleRoot=At(root,"Loop",293,553,134,35);panel.loop=toggleRoot.gameObject.AddComponent<Toggle>();
        var toggleBg=ImageAt(toggleRoot,"Box",0,3,27,27,KitSprite("Lobby/Character Create/Box/Lobby_CC_Box_Frame.png"),Gold);toggleBg.raycastTarget=true;
        panel.loop.targetGraphic=toggleBg;panel.loop.graphic=ImageAt(toggleRoot,"Check",6,9,15,15,KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Crystal.png"),Gold);panel.loop.isOn=true;
        Text(toggleRoot,"Label","반복",36,0,90,35,19,false,Ivory);
        panel.timeline=SliderAt(root,"Timeline",0,610,432,16,0,1,0);
        panel.timeLabel=Text(root,"Playback Time","0.0 / 0.0 s",0,632,200,28,16,false,Muted);
        panel.speed=SliderAt(root,"Speed",228,638,127,12,.25f,2,1);
        panel.speedLabel=Text(root,"Playback Speed","1.00×",363,628,73,28,16,false,Gold,TextAlignmentOptions.Right);
        panel.selectedLabel=Text(root,"Selected Animation","애니메이션을 선택하세요",0,505,432,34,20,true,Gold);
        panel.excludeSelected=ButtonAt(root,"Exclude Selected","목록에서 제외",0,686,210,42);panel.excludeSelected.GetComponent<Image>().color=new Color(.4f,.09f,.07f);
        panel.excludedList=ButtonAt(root,"Excluded List","제외 목록 (0)",222,686,210,42,false);panel.excludedCount=panel.excludedList.GetComponentInChildren<TMP_Text>();
        Text(root,"Exclusion Hint","제외한 항목은 목록에서 숨겨집니다.",0,734,432,24,15,false,Muted);
        root.gameObject.SetActive(false);panel.tabButton.gameObject.SetActive(false);
    }
    static Slider SliderAt(Transform parent,string name,float x,float y,float w,float h,float min,float max,float value)
    {
        var rect=At(parent,name,x,y,w,h);var slider=rect.gameObject.AddComponent<Slider>();
        ImageAt(rect,"Track",0,h*.4f,w,h*.3f,null,new Color(.21f,.17f,.12f));
        var fillArea=Stretch(rect,"Fill Area");fillArea.offsetMin=new Vector2(7,0);fillArea.offsetMax=new Vector2(-7,0);
        var fill=Stretch(fillArea,"Fill");var fillImage=fill.gameObject.AddComponent<Image>();fillImage.color=Gold;fillImage.raycastTarget=false;slider.fillRect=fill;
        var handleArea=Stretch(rect,"Handle Area");handleArea.offsetMin=new Vector2(7,0);handleArea.offsetMax=new Vector2(-7,0);
        var handle=At(handleArea,"Handle",0,0,18,25);handle.pivot=new Vector2(.5f,.5f);var handleImage=handle.gameObject.AddComponent<Image>();handleImage.sprite=KitSprite("Lobby/Character Create/Section/Lobby_CC_Section_Crystal.png");handleImage.color=Gold;
        slider.handleRect=handle;slider.targetGraphic=handleImage;slider.direction=Slider.Direction.LeftToRight;slider.minValue=min;slider.maxValue=max;slider.value=value;return slider;
    }
}
