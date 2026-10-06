using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuloGames.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// B concept uses 1600 x 900 design coordinates. The existing menu renders at half scale.
// Preserve the gameplay controller and every setting reference while replacing its presentation.
public static class OverburstSettingsGothicBuilder
{
    const float D = 2.4f;
    const string Art = "Assets/ProjectOverburst/05_Art/UI/SettingsGothic/";
    const string Fonts = "Assets/ProjectOverburst/Resources/UI/Fonts/SettingsGothic/";
    const string Kit = "Assets/ThirdParty/RPG and MMO UI 11/Textures/";
    static readonly Color Ivory = Hex("E6DDCA"), Gold = Hex("C1A16C"), Muted = Hex("9D9280");
    static TMP_FontAsset titleFont, bodyFont;
    static Font legacyBody;
    static OverburstSettingsGothicView view;
    static readonly List<OverburstSettingsGothicRow> rows = new List<OverburstSettingsGothicRow>();
    static readonly List<OverburstSettingsGothicFoldout> folds = new List<OverburstSettingsGothicFoldout>();

    [MenuItem("OVERBURST/Codex/Objectizers/UI/Apply Settings Concept B")]
    public static string Apply()
    {
        RequireIdle();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath);
        if (!asset || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Menu prefab missing or dirty");
        ImportArt();
        legacyBody = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        titleFont = FontAsset("Title", "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-SemiBold-HUD.ttf");
        bodyFont = FontAsset("Body", AssetDatabase.GetAssetPath(legacyBody));
        var root = PrefabUtility.LoadPrefabContents(OverburstGameMenuBuilder.PrefabPath);
        try
        {
            var panel = root.GetComponentInChildren<OverburstSettingsPanel>(true);
            if (panel.GetComponent<OverburstSettingsGothicView>()) throw new InvalidOperationException("Concept B is already applied; restore the owned backup before rebuilding");
            Build(panel);
            string characters = string.Concat(panel.GetComponentsInChildren<TMP_Text>(true).Select(t=>t.text)) + "소리화면전투표시조작음량과재생방식을조절합니다플레이할프레임설정항목눌러사용키변경0123456789%ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-+—·×◇켬끔";
            foreach (var font in new[]{titleFont,bodyFont})
            {
                if (!font.TryAddCharacters(characters, out string missing)) throw new InvalidOperationException("Missing settings glyphs: " + missing);
                EditorUtility.SetDirty(font); foreach(var atlas in font.atlasTextures) if(atlas)EditorUtility.SetDirty(atlas);
            }
            PrefabUtility.SaveAsPrefabAsset(root, OverburstGameMenuBuilder.PrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Settings prefab save failed");
            foreach(var font in new[]{titleFont,bodyFont})
            { AssetDatabase.SaveAssetIfDirty(font); foreach(var atlas in font.atlasTextures)if(atlas)AssetDatabase.SaveAssetIfDirty(atlas); if(font.material)AssetDatabase.SaveAssetIfDirty(font.material); }
            return "PASS Concept B: " + rows.Count + " setting rows, " + folds.Count + " foldouts, four categories";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); view=null; rows.Clear(); folds.Clear(); }
    }

    public static void RequireIdle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        if(IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Account guard must be ready for normal Play");
    }

    static void Build(OverburstSettingsPanel p)
    {
        if(p.tabs.Length!=4 || p.pages.Length!=4 || p.bloodRows.Length<4)throw new InvalidOperationException("Settings contract changed");
        foreach(var invalid in p.bloodRows.Where(r=>!Enum.IsDefined(typeof(BloodComparisonTuning.Control),r.control)))invalid.gameObject.SetActive(false);
        p.bloodRows=p.bloodRows.Where(r=>Enum.IsDefined(typeof(BloodComparisonTuning.Control),r.control)).ToArray();
        rows.Clear(); folds.Clear();
        var window=(RectTransform)p.transform;
        window.sizeDelta=new Vector2(1344,790)*D; window.anchoredPosition=new Vector2(0,1)*D;
        view=p.gameObject.AddComponent<OverburstSettingsGothicView>(); view.panel=p;
        foreach(var old in new[]{"Opaque Content Surface","Borders","Header","Tab Menu","Button Group"})
            foreach(var layout in window.Find(old).GetComponentsInChildren<LayoutGroup>(true)) layout.enabled=false;
        var surface=window.Find("Opaque Content Surface").GetComponent<Image>();
        Stretch(surface.rectTransform); surface.sprite=Sprite("surface");surface.type=Image.Type.Simple;surface.color=Color.white;
        window.Find("Borders").gameObject.SetActive(false);
        Img(window,"Gothic Outer Frame",new Rect(0,0,1344,790),Color.white,KitSprite("Miscellaneous/General/General_Container_Border.png"),true);
        for(int i=0;i<4;i++)
        {
            var corner=Img(window,"Gothic Corner " + i,new Rect(i%2==0?10:1290,i<2?10:736,44,44),new Color(1,1,1,.7f),KitSprite("Miscellaneous/General/General_Container_CornerOrnament.png"));
            corner.transform.localScale=new Vector3(i%2==0?1:-1,i<2?1:-1,1);
        }
        var header=window.Find("Header");
        header.Find("Text").gameObject.SetActive(false);
        foreach(Transform t in header) if(t!=p.closeButton.transform)t.gameObject.SetActive(false);
        Rect((RectTransform)header,new Rect(8,8,1328,78));
        Txt(header,"Options","OPTIONS",new Rect(440,5,448,17),10,Gold,false,TextAlignmentOptions.Center);
        Txt(header,"Title","설정",new Rect(440,25,448,42),29,Hex("E1C28A"),true,TextAlignmentOptions.Center);
        p.closeButton.gameObject.SetActive(true);Rect((RectTransform)p.closeButton.transform,new Rect(1259,15,47,47));p.closeButton.transform.SetAsLastSibling();
        Navigation(p.closeButton); RemoveVisualTransitions(p.closeButton.gameObject);

        var tabMenu=window.Find("Tab Menu");
        foreach(Transform child in tabMenu)if(child.name!="Buttons Group")child.gameObject.SetActive(false);
        Rect((RectTransform)tabMenu,new Rect(38,123,185,480));
        var buttons=(RectTransform)tabMenu.Find("Buttons Group");Stretch(buttons);
        foreach(var fitter in buttons.GetComponents<ContentSizeFitter>())fitter.enabled=false;
        view.tabBackgrounds=new Image[4];view.tabBorders=new Image[4];view.tabIcons=new Image[4];view.tabLabels=new TMP_Text[4];
        string[] names={"소리","화면","전투 표시","조작"}, icons={"sound","screen","combat","keys"};
        for(int i=0;i<4;i++)
        {
            var tab=p.tabs[i]; RemoveVisualTransitions(tab.gameObject);
            foreach(Transform child in tab.transform)child.gameObject.SetActive(false);
            Rect((RectTransform)tab.transform,new Rect(0,i*82,185,72));
            var element=tab.GetComponent<LayoutElement>();if(element)element.ignoreLayout=true;
            tab.GetComponent<Image>().enabled=false; tab.graphic=null;tab.transition=Selectable.Transition.None;
            view.tabBackgrounds[i]=Img(tab.transform,"Gothic Selection",new Rect(0,0,185,72),Color.clear);view.tabBackgrounds[i].raycastTarget=true;
            view.tabBorders[i]=Outline(tab.transform,"Selection Border",new Rect(0,0,185,72),Color.clear);
            view.tabIcons[i]=Img(tab.transform,"Category Icon",new Rect(16,25,22,22),Gold,Sprite(icons[i]));
            view.tabLabels[i]=Txt(tab.transform,"Category",names[i],new Rect(51,14,126,43),17,Gold,true);
            tab.targetGraphic=view.tabBackgrounds[i];Navigation(tab);
        }
        Img(window,"Sidebar Divider",new Rect(246,124,1,545),Hex("473A2B"));
        Txt(window,"Brand","OVERBURST",new Rect(53,503,180,25),11,Gold);
        Txt(window,"Brand Description","나에게 맞는 감각으로\n전투를 조절하세요.",new Rect(53,533,182,62),12,Muted);
        view.pageTitle=Txt(window,"Page Title","전투 표시",new Rect(274,118,300,42),24,Ivory,true);
        view.pageDescription=Txt(window,"Page Description","타격의 무게감과 전투 가독성을 조절합니다.",new Rect(480,130,528,28),11,Muted,false,TextAlignmentOptions.Right);
        Rect((RectTransform)window.Find("Tab Content"),new Rect(274,181,739,506));
        view.scrolls=new ScrollRect[4];
        for(int i=0;i<4;i++)view.scrolls[i]=ScrollPage(p.pages[i],i==2?p.combatScroll:i==3?p.keyScroll:null);
        var c=view.scrolls.Select(s=>s.content).ToArray();
        Heading(c[0],"음량","AUDIO");
        StyleRow(p.masterVolume,c[0],0,"sound", "주변 효과음과 전투 소리까지 함께 조절됩니다.");
        StyleRow(p.uiVolume,c[0],0,"sound","전체 음량은 유지하면서 메뉴 효과음을 따로 낮출 수 있습니다.");
        StyleRow(p.muteInBackground,c[0],0,"sound","다른 창을 보고 있을 때 게임 소리를 잠시 멈춥니다.");
        Heading(c[1],"디스플레이","DISPLAY");
        StyleRow(p.screenMode,c[1],1,"screen","화면이 맞지 않으면 10초 뒤 이전 설정으로 돌아갑니다.");
        StyleRow(p.resolution,c[1],1,"screen","사용 중인 모니터 비율에 맞는 해상도를 선택하세요.");
        StyleRow(p.vSync,c[1],1,"screen","수직 동기화를 켜면 프레임 제한이 모니터 주사율을 따릅니다.");
        StyleRow(p.frameLimit,c[1],1,"screen","수직 동기화를 끈 상태에서 조절할 수 있습니다.");

        Heading(c[2],"피격과 화면","SCREEN FEEDBACK");
        StyleRow(p.cameraShake,c[2],2,"camera","화면 흔들림이 부담스럽다면 강도를 낮춰 보세요.","카메라 흔들림","타격·피격·큰 몬스터 발소리의 흔들림");
        StyleRow(p.hitEffect,c[2],2,"eye","강도를 낮추면 피격 중에도 전투 상황을 편하게 볼 수 있습니다.",null,"피격 시 화면 가장자리가 붉어지는 강도");
        StyleRow(p.edgeBlur,c[2],2,"eye","탐험과 전투의 흐림 강도를 아래에서 따로 설정할 수 있습니다.","가장자리 흐림","탐험 중 화면 가장자리를 부드럽게 흐립니다");
        var screenFold=Fold(c[2],"탐험·전투 흐림 강도 및 모션블러");
        // The exploration slider originally shares the edge-blur row. Give it an independent row.
        var explore=NewRow(screenFold.body,"탐험 흐림 강도","탐험 중 가장자리 흐림의 세기");
        p.explorationEdgeBlurIntensity.transform.SetParent(explore.Find("Control"),false);
        StyleRow(p.explorationEdgeBlurIntensity,screenFold.body,2,"eye","흐림을 끄거나 0%로 조절하면 화면이 선명해집니다.");
        StyleRow(p.combatEdgeBlurIntensity,screenFold.body,2,"eye","전투 중의 흐림 강도를 따로 조절합니다.");
        StyleRow(p.motionBlur,screenFold.body,2,"eye","모션블러는 현재 사용하지 않는 기능입니다.");
        Heading(c[2],"전투 방향 표시","DIRECTION");
        StyleRow(p.combatFacingIndicator,c[2],2,"direction","발밑 표시로 캐릭터가 바라보는 방향을 확인할 수 있습니다.","방향 표시","캐릭터가 바라보는 방향을 발밑에 표시합니다");
        var directionFold=Fold(c[2],"표시 모양과 밝기");
        StyleRow(p.combatFacingStyle,directionFold.body,2,"direction","방향 표시의 형태를 선택하세요.");
        StyleRow(p.combatFacingBrightness,directionFold.body,2,"direction","주변 배경에 맞춰 밝기를 조절해 보세요.");
        Heading(c[2],"혈흔","BLOOD EFFECTS");
        StyleRow(p.bloodStyle,c[2],2,"blood","혈흔마다 크기와 밝기의 기본값이 다릅니다.",null,"전투에 사용할 혈흔 스타일");
        StyleRow(p.bloodPalette,c[2],2,"blood","몬스터별 색상을 유지하거나 모두 붉게 표시할 수 있습니다.",null,"몬스터별 색상 또는 모두 붉은색으로 표시");
        var bloodFold=Fold(c[2],"크기 · 밝기 조절");
        foreach(var row in p.bloodRows)StyleRow(row,bloodFold.body,2,"blood","선택한 혈흔의 조절값만 변경됩니다.");
        StyleRow(p.bloodResetButton,bloodFold.body,2,"blood","현재 선택한 혈흔의 조절값을 기본값으로 되돌립니다.");
        foreach(var old in new[]{"Feedback Section","Facing Section","Blood Section"}) c[2].Find(old)?.gameObject.SetActive(false);

        foreach(Transform child in c[3].Cast<Transform>().ToArray())
            if(!child.name.StartsWith("Key • "))child.gameObject.SetActive(false);
        Heading(c[3],"키 설정","KEY BINDINGS");
        foreach(var key in p.keyRows) StyleKey(key,c[3]);
        var fixedButton=p.pages[3].GetComponentsInChildren<Button>(true).FirstOrDefault(b=>b.name=="Key Button (Fixed)");
        if(fixedButton){ fixedButton.transform.parent.gameObject.SetActive(true);StyleKeyRect((RectTransform)fixedButton.transform.parent,fixedButton,c[3]); }
        view.rows=rows.ToArray();view.foldouts=folds.ToArray();
        foreach(var fold in folds){ fold.body.gameObject.SetActive(false);fold.expanded=false; }
        Help(window);
        Footer(p,window);
        foreach(var scroll in view.scrolls){ LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);scroll.verticalNormalizedPosition=1; }
        // Existing menu starts on its audio tab; do not change saved or remembered selection policy.
        PolishPanel(p);
        view.RefreshTab(0);
        foreach(var selectable in p.GetComponentsInChildren<Selectable>(true)) if(!selectable.GetComponent<OverburstMenuSoundHook>())selectable.gameObject.AddComponent<OverburstMenuSoundHook>();
    }

    // Safe, repeatable presentation correction for an already installed B prefab.
    public static string Polish()
    {
        RequireIdle();
        var root=PrefabUtility.LoadPrefabContents(OverburstGameMenuBuilder.PrefabPath);
        try
        {
            var panel=root.GetComponentInChildren<OverburstSettingsPanel>(true);
            if(!panel.GetComponent<OverburstSettingsGothicView>())throw new InvalidOperationException("Concept B is required");
            PolishPanel(panel);
            PrefabUtility.SaveAsPrefabAsset(root,OverburstGameMenuBuilder.PrefabPath,out bool saved);
            if(!saved)throw new InvalidOperationException("Settings polish save failed");
            return "PASS settings presentation polish";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    static void PolishPanel(OverburstSettingsPanel panel)
    {
        RestoreHeaderLayers(panel);
        var presentation=panel.GetComponent<OverburstSettingsGothicView>();
        var oldTabSurface=panel.transform.Find("Tab Menu").GetComponent<Image>();
        if(oldTabSurface)oldTabSurface.enabled=false;
        foreach(var row in presentation.rows)
        {
            var oldRowSurface=row.GetComponent<Image>();if(oldRowSurface)oldRowSurface.color=Color.clear;
            row.transform.Find("Stripe")?.gameObject.SetActive(false);
            foreach(var name in new[]{"Label","Description"})
            {var rect=row.transform.Find(name) as RectTransform;if(rect)rect.sizeDelta=new Vector2(418*D,rect.sizeDelta.y);}
            foreach(var slider in row.GetComponentsInChildren<Slider>(true))
            {
                var fill=slider.fillRect;fill.anchorMin=new Vector2(fill.anchorMin.x,0);fill.anchorMax=new Vector2(slider.normalizedValue,1);fill.offsetMin=fill.offsetMax=Vector2.zero;
                var handle=slider.handleRect;handle.anchorMin=new Vector2(slider.normalizedValue,0);handle.anchorMax=new Vector2(slider.normalizedValue,1);handle.sizeDelta=new Vector2(18*D,0);handle.anchoredPosition=Vector2.zero;
                foreach(var image in handle.GetComponentsInChildren<Image>(true))if(image.name.Contains("Overlay"))image.gameObject.SetActive(false);
                var fillImage=fill.GetComponent<Image>();fillImage.sprite=null;fillImage.type=Image.Type.Simple;fillImage.color=Hex("9C3027");
            }
            if(row.slider==panel.cameraShake)
            {row.descriptionSource=null;row.description="타격·피격·큰 몬스터 발소리에 화면이 흔들리는 세기를 조절합니다.";}
            if(row.toggle)
            {
                bool combined=row.GetComponentsInChildren<Slider>(true).Length>0;
                Rect((RectTransform)row.toggle.transform,new Rect(combined?0:154,1,114,38));
            }
            if(row.selector)
            {
                var selector=row.selector;
                foreach(var image in selector.GetComponentsInChildren<Image>(true))
                    if(image.name.StartsWith("Background") || image.name=="Foreground" || image.name.Contains("Overlay"))image.gameObject.SetActive(false);
                if(!selector.transform.Find("Gothic Field"))
                {var surface=Img(selector.transform,"Gothic Field",new Rect(0,0,268,36),Hex("191611"));surface.transform.SetAsFirstSibling();Outline(selector.transform,"Gothic Field Frame",new Rect(0,0,268,36),Hex("6B5638"));}
                foreach(var button in selector.GetComponentsInChildren<Button>(true))
                {
                    RemoveVisualTransitions(button.gameObject);
                    var hit=button.GetComponent<Image>()??button.gameObject.AddComponent<Image>();hit.sprite=null;hit.color=Color.clear;hit.raycastTarget=true;
                    var iconImage=button.transform.Find("Icon").GetComponent<Image>();iconImage.color=Gold;iconImage.raycastTarget=false;button.targetGraphic=iconImage;button.transition=Selectable.Transition.ColorTint;
                    var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.2f,1.2f,1.2f,1);colors.pressedColor=new Color(.75f,.75f,.75f,1);colors.selectedColor=colors.highlightedColor;button.colors=colors;
                }
            }
            if(row.key)PolishQuietButton(row.key.keyButton,268,36);
        }
        var diamond=panel.transform.Find("Gothic Help/Diamond") as RectTransform;
        diamond.pivot=new Vector2(.5f,.5f);diamond.anchoredPosition=new Vector2(52,-59)*D;
        presentation.helpTitle.alignment=TextAlignmentOptions.TopLeft;
        presentation.helpDescription.alignment=TextAlignmentOptions.TopLeft;
        presentation.helpTip.alignment=TextAlignmentOptions.TopLeft;
        var close=panel.footerCloseButton;
        foreach(var image in close.GetComponentsInChildren<Image>(true))
        {
            if(image.name.Contains("Ornament") || image.name.Contains("Hover") || image.name.Contains("Press")){image.gameObject.SetActive(false);continue;}
            Stretch(image.rectTransform);
            if(image.name!="Background"){image.rectTransform.offsetMin=Vector2.one*5*D;image.rectTransform.offsetMax=-Vector2.one*5*D;}
        }
        foreach(var text in close.GetComponentsInChildren<Text>(true))text.text="닫기";
        PolishQuietButton(panel.resetButton,154,37);
        PolishQuietButton(panel.bloodResetButton,268,40);
        foreach(var image in panel.GetComponentsInChildren<Image>(true).ToArray())
            if(image.name=="Selection Border" || image.name=="Selection Outline" || image.name=="Gothic Field Frame" || image.name=="Gothic Frame"
                || image.transform.parent.name=="Gothic Help" && image.name=="Frame")PolishFrame(image);
        if(!panel.transform.Find("Navigation Hint"))
        {
            var body=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts+"Body SDF.asset");
            var previous=bodyFont;bodyFont=body;
            try{Txt(panel.transform,"Navigation Hint","Esc 닫기   ·   방향키 항목 이동   ·   휠 스크롤",new Rect(400,773,544,16),9,Hex("8D806B"),false,TextAlignmentOptions.Center);}
            finally{bodyFont=previous;}
        }
        panel.transform.Find("Navigation Hint").GetComponent<TMP_Text>().text="Esc 닫기   ·   방향키 항목 이동   ·   휠 스크롤";
    }

    public static string RestoreAssetHeader()
    {
        RequireIdle();
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath);
        if(!asset || EditorUtility.IsDirty(asset))throw new InvalidOperationException("Menu prefab missing or dirty");
        var root=PrefabUtility.LoadPrefabContents(OverburstGameMenuBuilder.PrefabPath);
        try
        {
            var panel=root.GetComponentInChildren<OverburstSettingsPanel>(true);
            if(!panel.GetComponent<OverburstSettingsGothicView>())throw new InvalidOperationException("Concept B is required");
            RestoreHeaderLayers(panel);
            PrefabUtility.SaveAsPrefabAsset(root,OverburstGameMenuBuilder.PrefabPath,out bool saved);
            if(!saved)throw new InvalidOperationException("Settings header save failed");
            return "PASS supplier header layers restored";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    static void RestoreHeaderLayers(OverburstSettingsPanel panel)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/RPG and MMO UI 11/Prefabs/Windows/Window (Settings).prefab");
        if(!source)throw new InvalidOperationException("Supplier settings window missing");
        var sourceHeader=(RectTransform)source.transform.Find("Header");
        var header=(RectTransform)panel.transform.Find("Header");
        float scale=header.rect.height/sourceHeader.rect.height;
        if(scale<=0)throw new InvalidOperationException("Invalid header size");
        foreach(var branch in new[]{"Border","Background"})
        {
            var sourceRoot=sourceHeader.Find(branch);
            foreach(var original in sourceRoot.GetComponentsInChildren<RectTransform>(true))
            {
                string path=AnimationUtility.CalculateTransformPath(original,sourceHeader);
                var target=header.Find(path) as RectTransform;
                if(!target)throw new InvalidOperationException("Supplier header layer missing: "+path);
                target.anchorMin=original.anchorMin;target.anchorMax=original.anchorMax;target.pivot=original.pivot;
                target.sizeDelta=original.sizeDelta*scale;target.anchoredPosition=original.anchoredPosition*scale;
                target.localScale=original.localScale;target.localRotation=original.localRotation;
                target.gameObject.SetActive(original.gameObject.activeSelf && path!="Background/Effect Left" && path!="Background/Effect Right");
                var sourceImage=original.GetComponent<Image>();var image=target.GetComponent<Image>();
                if(!sourceImage || !image)continue;
                image.enabled=sourceImage.enabled;image.sprite=sourceImage.sprite;image.type=sourceImage.type;
                image.color=path=="Background"?Hex("85241F"):sourceImage.color;
                image.fillCenter=sourceImage.fillCenter;image.preserveAspect=sourceImage.preserveAspect;
                image.pixelsPerUnitMultiplier=sourceImage.pixelsPerUnitMultiplier/scale;
                image.raycastTarget=false;
            }
        }
        // Retain the supplier UIFlippable components; negative scale shifts a top-left pivot inward.
        foreach(var name in new[]{"Gothic Header","Header Texture","Left Ornament","Right Ornament","Header Rule"})
            header.Find(name)?.gameObject.SetActive(false);
        panel.closeButton.transform.SetAsLastSibling();
    }

    static void PolishQuietButton(Button button,float width,float height)
    {
        RemoveVisualTransitions(button.gameObject);
        foreach(var image in button.GetComponentsInChildren<Image>(true))
            if(image.name!="Gothic Button Surface" && image.name!="Gothic Frame" && !image.name.StartsWith("Edge "))image.enabled=false;
        var surface=button.transform.Find("Gothic Button Surface")?.GetComponent<Image>();
        if(!surface){surface=Img(button.transform,"Gothic Button Surface",new Rect(0,0,width,height),Hex("211D16"));surface.transform.SetAsFirstSibling();}
        surface.raycastTarget=true;button.targetGraphic=surface;button.transition=Selectable.Transition.ColorTint;
        var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.65f,1.55f,1.4f,1);colors.pressedColor=new Color(.8f,.8f,.8f,1);colors.selectedColor=colors.highlightedColor;button.colors=colors;
        if(!button.transform.Find("Gothic Frame"))Outline(button.transform,"Gothic Frame",new Rect(0,0,width,height),Hex("6B5638"));
    }

    static void PolishFrame(Image frame)
    {
        frame.enabled=false;
        if(frame.transform.Find("Edge Top"))return;
        foreach(var side in new[]{"Top","Bottom","Left","Right"})
        {
            var rect=Node(frame.transform,"Edge "+side);Stretch(rect);
            if(side=="Top"){rect.anchorMin=new Vector2(0,1);rect.sizeDelta=new Vector2(0,D);rect.pivot=new Vector2(.5f,1);}
            if(side=="Bottom"){rect.anchorMax=new Vector2(1,0);rect.sizeDelta=new Vector2(0,D);rect.pivot=new Vector2(.5f,0);}
            if(side=="Left"){rect.anchorMax=new Vector2(0,1);rect.sizeDelta=new Vector2(D,0);rect.pivot=new Vector2(0,.5f);}
            if(side=="Right"){rect.anchorMin=new Vector2(1,0);rect.sizeDelta=new Vector2(D,0);rect.pivot=new Vector2(1,.5f);}
            rect.anchoredPosition=Vector2.zero;var image=rect.gameObject.AddComponent<Image>();image.color=frame.color;image.raycastTarget=false;
        }
    }

    static ScrollRect ScrollPage(GameObject page,ScrollRect existing)
    {
        var rect=(RectTransform)page.transform; Stretch(rect);
        RectTransform content,viewport;ScrollRect scroll;
        if(existing){ scroll=existing; viewport=scroll.viewport;content=scroll.content; }
        else
        {
            var old=page.transform.Cast<Transform>().ToArray();
            scroll=page.AddComponent<ScrollRect>();viewport=Node(page.transform,"Viewport");content=Node(viewport,"Content");
            foreach(var child in old)child.SetParent(content,false);
        }
        Stretch((RectTransform)scroll.transform);Stretch(viewport);viewport.offsetMax=new Vector2(-12*D,0);
        if(!viewport.GetComponent<RectMask2D>())viewport.gameObject.AddComponent<RectMask2D>();
        if(!viewport.GetComponent<Image>()){var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;}
        content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=Vector2.zero;
        ListLayout(content);scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;
        scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48*D;scroll.decelerationRate=.08f;
        if(scroll.verticalScrollbar){scroll.verticalScrollbar.gameObject.SetActive(false);scroll.verticalScrollbar=null;}
        return scroll;
    }
    static void ListLayout(RectTransform t)
    {
        var layout=t.GetComponent<VerticalLayoutGroup>()??t.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;layout.spacing=0;layout.padding=new RectOffset();
        var fit=t.GetComponent<ContentSizeFitter>()??t.gameObject.AddComponent<ContentSizeFitter>();fit.horizontalFit=ContentSizeFitter.FitMode.Unconstrained;fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
    }
    static void Heading(Transform list,string text,string english)
    {
        var h=Node(list,"Gothic Section • "+text);Element(h,27);
        Txt(h,"Heading",text,new Rect(0,1,250,25),13,Gold,true);
        Img(h,"Rule",new Rect(text.Length*14+13,10,590-text.Length*14-13,1),Hex("564532"));
        Txt(h,"English",english,new Rect(600,0,128,26),9,Hex("7B6B54"),false,TextAlignmentOptions.Right);
    }
    static OverburstSettingsGothicFoldout Fold(Transform list,string text)
    {
        var root=Node(list,"Gothic Details • "+text);var element=Element(root,30);
        var head=Img(root,"Header",new Rect(0,0,728,30),new Color(1,1,1,0));head.raycastTarget=true;
        var button=head.gameObject.AddComponent<Button>();button.targetGraphic=head;button.transition=Selectable.Transition.ColorTint;
        var colors=button.colors;colors.normalColor=Color.clear;colors.highlightedColor=new Color(.32f,.25f,.17f,.3f);colors.pressedColor=new Color(.32f,.25f,.17f,.6f);colors.selectedColor=colors.highlightedColor;button.colors=colors;Navigation(button);
        var arrow=Txt(head.transform,"Arrow","+",new Rect(12,0,15,30),13,Gold);
        Txt(head.transform,"Label",text,new Rect(29,0,660,30),10,Gold);
        var body=Node(root,"Body");body.anchorMin=new Vector2(0,1);body.anchorMax=Vector2.one;body.pivot=new Vector2(.5f,1);body.anchoredPosition=new Vector2(0,-30*D);body.sizeDelta=Vector2.zero;ListLayout(body);
        var fold=root.gameObject.AddComponent<OverburstSettingsGothicFoldout>();fold.owner=view;fold.button=button;fold.arrow=arrow;fold.body=body;fold.element=element;fold.headerHeight=30*D;folds.Add(fold);return fold;
    }
    static RectTransform RowOf(Component control)
    {
        var cursor=control.transform;
        while(cursor && !cursor.name.StartsWith("Row • "))cursor=cursor.parent;
        if(!cursor)throw new InvalidOperationException("Setting row missing: "+control.name);
        return (RectTransform)cursor;
    }
    static RectTransform NewRow(Transform parent,string title,string description)
    {
        var row=Node(parent,"Row • "+title);var label=Node(row,"Label").gameObject.AddComponent<Text>();label.text=title;
        var desc=Node(row,"Description").gameObject.AddComponent<Text>();desc.text=description;Node(row,"Control");return row;
    }
    static void StyleRow(Component control,Transform parent,int tab,string icon,string tip,string title=null,string description=null)
    {
        var row=RowOf(control);row.SetParent(parent,false);row.SetAsLastSibling();row.localScale=Vector3.one;Element(row,54);
        var label=row.Find("Label").GetComponent<Text>();var desc=row.Find("Description").GetComponent<Text>();
        if(title!=null)label.text=title;if(description!=null)desc.text=description;
        Legacy(label,new Rect(12,5,418,24),15,Ivory,TextAnchor.MiddleLeft);Legacy(desc,new Rect(12,29,418,20),10,Muted,TextAnchor.MiddleLeft);
        var slot=(RectTransform)row.Find("Control");Rect(slot,new Rect(448,7,268,40));slot.localScale=Vector3.one;
        if(row.Find("Rule"))row.Find("Rule").gameObject.SetActive(false);
        Img(row,"Gothic Rule",new Rect(0,53,728,1),Hex("3A3228"));
        var highlight=Img(row,"Selection Fill",new Rect(0,0,728,54),Color.clear);highlight.transform.SetAsFirstSibling();highlight.raycastTarget=true;
        var outline=Outline(row,"Selection Outline",new Rect(0,0,728,54),Color.clear);outline.transform.SetSiblingIndex(1);
        var presentation=row.gameObject.AddComponent<OverburstSettingsGothicRow>();presentation.owner=view;presentation.tabIndex=tab;presentation.title=label.text;presentation.description=desc.text;presentation.tip=tip;presentation.icon=Sprite(icon);presentation.highlight=highlight;presentation.outline=outline;presentation.descriptionSource=desc;
        presentation.slider=control as Slider;presentation.toggle=control as Toggle;presentation.selector=control as UISwitchSelect;presentation.number=control as OverburstSettingsNumberRow;
        foreach(var text in slot.GetComponentsInChildren<Text>(true)){text.font=legacyBody;text.fontSize=Mathf.RoundToInt(13*D);text.color=Ivory;text.alignByGeometry=true;text.raycastTarget=false;}
        foreach(var slider in slot.GetComponentsInChildren<Slider>(true)) StyleSlider(slider,slot.GetComponentInChildren<Toggle>(true)!=null,presentation.number!=null);
        foreach(var toggle in slot.GetComponentsInChildren<Toggle>(true))StyleToggle(toggle,slot.GetComponentInChildren<Slider>(true)!=null);
        foreach(var selector in slot.GetComponentsInChildren<UISwitchSelect>(true))StyleSelector(selector);
        if(presentation.number)StyleNumber(presentation.number);
        if(control is Button b){Rect((RectTransform)b.transform,new Rect(0,0,268,40));StyleButton(b,false,13);}
        rows.Add(presentation);
    }
    static void StyleSlider(Slider slider,bool combined,bool number)
    {
        if(number)return;
        Rect((RectTransform)slider.transform,new Rect(combined?123:0,0,combined?145:268,40));slider.transform.localScale=Vector3.one;
        var width=combined?145f:268f;
        var value=slider.transform.Find("Text")?.GetComponent<Text>();if(value)Legacy(value,new Rect(width-61,0,61,40),15,Hex("E0C391"),TextAnchor.MiddleRight);
        var fill=(RectTransform)slider.fillRect.parent;fill.anchorMin=new Vector2(0,.5f);fill.anchorMax=new Vector2(1,.5f);fill.offsetMin=new Vector2(0,-1.5f*D);fill.offsetMax=new Vector2(-69*D,1.5f*D);
        var area=(RectTransform)slider.handleRect.parent;area.anchorMin=new Vector2(0,.5f);area.anchorMax=new Vector2(1,.5f);area.offsetMin=new Vector2(0,-14*D);area.offsetMax=new Vector2(-69*D,14*D);
        slider.handleRect.sizeDelta=new Vector2(18*D,0);slider.handleRect.anchoredPosition=Vector2.zero;
        slider.fillRect.GetComponent<Image>().color=Hex("9C3027");
        var bg=slider.GetComponent<Image>();if(bg){bg.color=Color.clear;bg.raycastTarget=true;}
        var track=Img(slider.transform,"Gothic Track",new Rect(0,18.5f,width-69,3),Hex("3D3830"));track.transform.SetAsFirstSibling();
        RemoveVisualTransitions(slider.gameObject);Navigation(slider);
    }
    static void StyleToggle(Toggle toggle,bool combined)
    {
        Rect((RectTransform)toggle.transform,new Rect(combined?0:154,1,114,38));toggle.transform.localScale=Vector3.one;
        foreach(var pair in new[]{("On Text",0f),("Off Text",57f)})
        {var text=toggle.transform.Find(pair.Item1)?.GetComponent<Text>();if(text)Legacy(text,new Rect(pair.Item2,0,57,38),12,Gold,TextAnchor.MiddleCenter);}
        // The supplier animation drives the horizontal half; keep its toggle contract and size its metal grip.
        var handle=toggle.transform.Find("Handle") as RectTransform;
        if(handle)
        {
            handle.anchorMin=handle.anchorMax=new Vector2(0,.5f);handle.pivot=new Vector2(0,.5f);handle.sizeDelta=new Vector2(47,36)*D;
            var animation=toggle.GetComponent<UIToggle_OnOff>();
            if(animation){var so=new SerializedObject(animation);so.FindProperty("m_InactivePosition").vector2Value=Vector2.zero;so.FindProperty("m_ActivePosition").vector2Value=new Vector2(61*D,0);so.ApplyModifiedPropertiesWithoutUndo();animation.OnValueChanged(toggle.isOn);}
        }
        Navigation(toggle);
    }
    static void StyleSelector(UISwitchSelect selector)
    {
        Rect((RectTransform)selector.transform,new Rect(0,2,268,36));selector.transform.localScale=Vector3.one;
        var text=selector.transform.Find("Text").GetComponent<Text>();Legacy(text,new Rect(34,0,200,36),13,Hex("DAC6A3"),TextAnchor.MiddleCenter);
        foreach(var b in selector.GetComponentsInChildren<Button>(true))
        {bool previous=b.name.Contains("Previous");Rect((RectTransform)b.transform,new Rect(previous?0:234,0,34,36));Navigation(b);}
    }
    static void StyleNumber(OverburstSettingsNumberRow row)
    {
        Rect((RectTransform)row.decrease.transform,new Rect(0,4,30,32));Rect((RectTransform)row.increase.transform,new Rect(238,4,30,32));
        StyleButton(row.decrease,false,15);StyleButton(row.increase,false,15);
        Legacy(row.valueLabel,new Rect(194,0,40,40),15,Gold,TextAnchor.MiddleCenter);
        Rect((RectTransform)row.slider.transform,new Rect(39,0,148,40));
        foreach(var img in row.slider.GetComponentsInChildren<Image>(true)) if(img.name=="Background")Rect(img.rectTransform,new Rect(0,18.5f,148,3));
        var fill=(RectTransform)row.slider.fillRect.parent;Stretch(fill);fill.offsetMin=new Vector2(0,18.5f*D);fill.offsetMax=new Vector2(0,-18.5f*D);row.slider.fillRect.GetComponent<Image>().color=Hex("9C3027");
        var area=(RectTransform)row.slider.handleRect.parent;Stretch(area);area.offsetMin=new Vector2(0,6*D);area.offsetMax=new Vector2(0,-6*D);
        var handle=row.slider.handleRect;handle.sizeDelta=new Vector2(18*D,0);handle.GetComponent<Image>().sprite=KitSprite("Controls/Slider/Slider_Horizontal_Handle.png");handle.GetComponent<Image>().color=Color.white;Navigation(row.slider);
    }
    static void StyleKey(OverburstKeyBindingRow key,Transform list)
    {
        var row=(RectTransform)key.transform;StyleKeyRect(row,key.keyButton,list);
        var presentation=row.gameObject.AddComponent<OverburstSettingsGothicRow>();presentation.owner=view;presentation.tabIndex=3;presentation.title=key.label.text;presentation.description="키 버튼을 누른 다음 사용할 키를 입력하세요.";presentation.tip="ESC로 변경을 취소할 수 있습니다.";presentation.icon=Sprite("keys");presentation.key=key;presentation.highlight=row.Find("Selection Fill").GetComponent<Image>();presentation.outline=row.Find("Selection Outline").GetComponent<Image>();rows.Add(presentation);
    }
    static void StyleKeyRect(RectTransform row,Button button,Transform list)
    {
        row.SetParent(list,false);row.SetAsLastSibling();Element(row,54);
        var label=row.Find("Label")?.GetComponent<Text>();if(!label)label=row.GetComponentsInChildren<Text>(true).First(t=>!t.transform.IsChildOf(button.transform));
        Legacy(label,new Rect(12,5,418,44),15,Ivory,TextAnchor.MiddleLeft);
        Rect((RectTransform)button.transform,new Rect(448,9,268,36));StyleButton(button,false,13);
        foreach(Transform child in row)if(child.name=="Rule")child.gameObject.SetActive(false);
        Img(row,"Gothic Rule",new Rect(0,53,728,1),Hex("3A3228"));var fill=Img(row,"Selection Fill",new Rect(0,0,728,54),Color.clear);fill.transform.SetAsFirstSibling();fill.raycastTarget=true;Outline(row,"Selection Outline",new Rect(0,0,728,54),Color.clear).transform.SetSiblingIndex(1);
    }
    static void Help(Transform window)
    {
        var help=Img(window,"Gothic Help",new Rect(1041,123,265,415),Hex("201B15"));Outline(help.transform,"Frame",new Rect(0,0,265,415),Hex("4F412E"));
        var diamond=Img(help.transform,"Diamond",new Rect(27,34,50,50),Hex("342619"));diamond.transform.localEulerAngles=new Vector3(0,0,45);Outline(diamond.transform,"Frame",new Rect(0,0,50,50),Hex("6B5438"));
        view.helpIcon=Img(help.transform,"Icon",new Rect(40,46,24,24),Gold,Sprite("camera"));
        Txt(help.transform,"Selected","선택 항목",new Rect(23,115,219,22),10,Gold);
        view.helpTitle=Txt(help.transform,"Help Title","카메라 흔들림",new Rect(23,141,219,60),21,Hex("E1C28A"),true);
        view.helpDescription=Txt(help.transform,"Help Description","타격·피격·큰 몬스터 발소리에 화면이 흔들리는 세기를 조절합니다.",new Rect(23,191,219,87),12,Hex("C1B6A2"));view.helpDescription.lineSpacing=10;
        Img(help.transform,"Rule",new Rect(23,279,219,1),Hex("51412B"));
        Txt(help.transform,"Current","현재 값",new Rect(23,297,219,20),10,Muted);
        view.helpValue=Txt(help.transform,"Help Value","40%",new Rect(23,318,219,49),28,Hex("E0C391"),true);view.helpValue.enableAutoSizing=true;view.helpValue.fontSizeMin=13*D;view.helpValue.fontSizeMax=28*D;
        view.helpTip=Txt(help.transform,"Help Tip","화면 흔들림이 부담스럽다면 강도를 낮춰 보세요.",new Rect(23,370,219,39),11,Hex("A79370"));
    }
    static void Footer(OverburstSettingsPanel p,Transform window)
    {
        var footer=(RectTransform)window.Find("Button Group");Stretch(footer);
        footer.Find("Footer Rule").gameObject.SetActive(false);Img(footer,"Gothic Footer Rule",new Rect(46,703,1252,1),Hex("51412B"));
        Rect((RectTransform)p.resetButton.transform,new Rect(46,723,154,37));StyleButton(p.resetButton,false,13);p.resetButton.transform.Find("Text").GetComponent<Text>().text="이 탭 기본값";
        Rect((RectTransform)p.footerCloseButton.transform,new Rect(1131,714,167,57));StyleButton(p.footerCloseButton,true,17);
        Txt(p.footerCloseButton.transform,"Esc","Esc",new Rect(117,15,41,27),10,Hex("C7A37D"),false,TextAlignmentOptions.Center);
        Legacy(p.statusText,new Rect(275,725,750,33),11,Muted,TextAnchor.MiddleCenter);
        view.saveHint=Txt(footer,"Save Hint","◇  변경 사항은 닫을 때 저장됩니다",new Rect(275,725,750,33),11,Muted,false,TextAlignmentOptions.Center).gameObject;
    }
    static void StyleButton(Button button,bool red,int size)
    {
        RemoveVisualTransitions(button.gameObject);button.transition=Selectable.Transition.ColorTint;
        var graphic=button.targetGraphic as Image;if(graphic){graphic.color=Color.white;}
        foreach(var img in button.GetComponentsInChildren<Image>(true))if(img.name=="Foreground")img.color=red?Hex("C33622"):Hex("796447");
        var label=button.transform.Find("Text")?.GetComponent<Text>();if(label){Stretch(label.rectTransform);label.font=legacyBody;label.fontSize=Mathf.RoundToInt(size*D);label.alignment=TextAnchor.MiddleCenter;label.alignByGeometry=true;label.color=red?Hex("E7CC9F"):Hex("C2AF8B");label.raycastTarget=false;}
        Navigation(button);
    }
    static void RemoveVisualTransitions(GameObject go)
    {
        foreach(var behaviour in go.GetComponents<Behaviour>())
            if(behaviour.GetType().Name=="UIHighlightTransition" || behaviour.GetType().Name=="UIPressTransition" || behaviour.GetType().Name=="UIToggleActiveTransition")behaviour.enabled=false;
    }
    static void Navigation(Selectable s){var nav=s.navigation;nav.mode=UnityEngine.UI.Navigation.Mode.Automatic;s.navigation=nav;}
    static TMP_FontAsset FontAsset(string name,string source)
    {
        Directory.CreateDirectory(Fonts);string path=Fonts+name+" SDF.asset";var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(font)
        {
            if(font.atlasTextures==null || font.atlasTextures.Length==0 || !font.atlasTextures[0])
            {
                var parts=AssetDatabase.LoadAllAssetsAtPath(path);var textures=parts.OfType<Texture2D>().ToArray();var material=parts.OfType<Material>().SingleOrDefault();
                if(textures.Length==0 || !material)throw new InvalidOperationException("Owned font sub-assets missing: "+path);
                font.atlasTextures=textures;font.material=material;material.mainTexture=textures[0];font.ReadFontAssetDefinition();
                EditorUtility.SetDirty(font);EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(font);
            }
            return font;
        }
        var original=AssetDatabase.LoadAssetAtPath<Font>(source);if(!original)throw new InvalidOperationException("Source font missing: "+source);
        font=TMP_FontAsset.CreateFontAsset(original,90,9,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);font.name=name+" SDF";
        if(!font.TryAddCharacters("설정소리화면전투표시조작0123456789%ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-+—·×◇켬끔",out string missing))throw new InvalidOperationException("Missing base font characters: "+missing);
        AssetDatabase.CreateAsset(font,path);foreach(var texture in font.atlasTextures)if(texture && !AssetDatabase.Contains(texture))AssetDatabase.AddObjectToAsset(texture,font);
        if(font.material && !AssetDatabase.Contains(font.material))AssetDatabase.AddObjectToAsset(font.material,font);
        EditorUtility.SetDirty(font);foreach(var texture in font.atlasTextures)if(texture)EditorUtility.SetDirty(texture);if(font.material)EditorUtility.SetDirty(font.material);AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }
    static void ImportArt()
    {
        foreach(string file in Directory.GetFiles(Art,"*.png"))
        {AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(file);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.spritePixelsPerUnit=100;importer.maxTextureSize=256;importer.SaveAndReimport();}
    }
    static Sprite Sprite(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>(Art+name+".png");
    static Sprite KitSprite(string path)=>AssetDatabase.LoadAssetAtPath<Sprite>(Kit+path)??throw new InvalidOperationException("Sprite missing: "+path);
    static RectTransform Node(Transform parent,string name){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);return r;}
    static LayoutElement Element(RectTransform t,float height){var e=t.GetComponent<LayoutElement>()??t.gameObject.AddComponent<LayoutElement>();e.minHeight=e.preferredHeight=height*D;e.flexibleHeight=0;return e;}
    static void Rect(RectTransform t,Rect r){t.anchorMin=t.anchorMax=new Vector2(0,1);t.pivot=new Vector2(0,1);t.anchoredPosition=new Vector2(r.x,-r.y)*D;t.sizeDelta=new Vector2(r.width,r.height)*D;}
    static void Stretch(RectTransform t){t.anchorMin=Vector2.zero;t.anchorMax=Vector2.one;t.pivot=new Vector2(.5f,.5f);t.offsetMin=t.offsetMax=Vector2.zero;}
    static Color Hex(string value){ColorUtility.TryParseHtmlString("#"+value,out var c);return c;}
    static Image Img(Transform parent,string name,Rect rect,Color color,Sprite sprite=null,bool sliced=false){var t=Node(parent,name);Rect(t,rect);var i=t.gameObject.AddComponent<Image>();i.color=color;i.sprite=sprite;i.type=sliced?Image.Type.Sliced:Image.Type.Simple;i.raycastTarget=false;return i;}
    static Image Outline(Transform parent,string name,Rect rect,Color color){var i=Img(parent,name,rect,color,KitSprite("Miscellaneous/General/General_Container_Border.png"),true);i.fillCenter=false;i.pixelsPerUnitMultiplier=6;return i;}
    static TMP_Text Txt(Transform parent,string name,string text,Rect rect,float size,Color color,bool serif=false,TextAlignmentOptions align=TextAlignmentOptions.Left){var r=Node(parent,name);Rect(r,rect);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=serif?titleFont:bodyFont;t.text=text;t.fontSize=size*D;t.color=color;t.alignment=align;t.raycastTarget=false;t.enableWordWrapping=true;return t;}
    static void Legacy(Text text,Rect rect,float size,Color color,TextAnchor align){Rect(text.rectTransform,rect);text.font=legacyBody;text.fontSize=Mathf.RoundToInt(size*D);text.color=color;text.alignment=align;text.alignByGeometry=true;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;}
}
