using System.Collections.Generic;
using System.Linq;
using DuloGames.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 2026-10-01 ESC 메뉴·설정 프리팹 저작(99A: Editor API). RPG11 키트 원본(게임 메뉴·설정 창·확인창·슬라이더·스위치·탭)을 복제해
// 한글 글꼴과 프로젝트 색(장비·인벤토리 창과 같은 값)으로 다듬는다. 다시 실행하면 프리팹을 통째로 새로 만든다.
public static class OverburstGameMenuBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/UI/Menu/PF_OverburstGameMenu_Rpg11.prefab";
    private const string Kit = "Assets/ThirdParty/RPG and MMO UI 11/Prefabs/";
    private const string EquipmentPrefab = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstEquipment_Rpg11.prefab";
    private const string SoundFolder = "Assets/ThirdParty/11_사운드/Pro Sound Collection/User_Interface_Menu/";

    private static readonly Color Gold = Hex("E0BD7A");
    private static readonly Color Label = Hex("BDB5A8");
    private static readonly Color Bright = Hex("EDE6D4");
    private static readonly Color Muted = Hex("8C857C");
    private static readonly Color Rule = Hex("574C3BA6");
    private static readonly Color HeaderTint = Hex("85241F");
    private static readonly Color Surface = Hex("0C0B0A");

    private static Font serif;
    private static Font sans;

    [MenuItem("OVERBURST/Codex/Objectizers/UI/Build Game Menu (ESC)")]
    private static void RunMenu() => Debug.Log("[OverburstGameMenuBuilder] " + Build());

    public static string Build()
    {
        LoadFonts();
        if (serif == null || sans == null) return "FAILED: fonts not found";

        var root = new GameObject("PF_OverburstGameMenu_Rpg11", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        // 키트의 일부 스크립트는 에디터에서도 OnEnable에서 루트 캔버스를 건드리고 검은 오버레이를 붙인다.
        // 조립하는 동안 루트를 꺼 두어 그 코드가 돌지 않게 하고, 저장 직전에 캔버스 설정을 다시 확정한다.
        root.SetActive(false);
        try
        {
            Stretch(root.transform);
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500; // HUD·창·툴팁보다 위
            var menu = root.AddComponent<OverburstGameMenu>();
            root.AddComponent<OverburstGameMenuGate>();
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.ignoreListenerPause = true;
            menu.audioSource = audio;
            menu.openClip = Clip("ui_menu_popup_01");
            menu.closeClip = Clip("ui_menu_button_cancel_01");
            menu.hoverClip = Clip("ui_menu_button_scroll_01");
            menu.clickClip = Clip("ui_menu_button_click_01");
            menu.confirmClip = Clip("ui_menu_button_confirm_01");

            var frame = new GameObject("Menu Frame", typeof(RectTransform), typeof(CanvasGroup));
            frame.transform.SetParent(root.transform, false);
            Stretch(frame.transform);
            menu.group = frame.GetComponent<CanvasGroup>();

            // 리니어 색 공간이라 반투명 검정이 눈에는 약하게 보인다(.82면 뒤 화면이 충분히 가라앉는다).
            var dim = NewImage(frame.transform, "Dim", new Color(0f, 0f, 0f, .82f));
            Stretch(dim.transform);
            dim.raycastTarget = true; // 메뉴 뒤 게임 화면 클릭을 막는다.

            var content = new GameObject("Scaled Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(frame.transform, false);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, .5f);
            content.sizeDelta = new Vector2(3840f, 2160f); // 다른 RPG11 창과 같은 2배 단위, 배율 0.5
            content.localScale = Vector3.one * .5f;

            BuildMain(menu, content);
            var settings = BuildSettings(menu, content);
            BuildKeyPrompt(settings, content);
            BuildModal(menu, content);

            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
                if (!selectable.GetComponent<OverburstMenuSoundHook>()) selectable.gameObject.AddComponent<OverburstMenuSoundHook>();

            foreach (var stray in root.GetComponentsInChildren<UIBlackOverlay>(true)) Object.DestroyImmediate(stray.gameObject);
            root.SetActive(true);
            canvas.enabled = true;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;
            foreach (var stray in root.GetComponentsInChildren<UIBlackOverlay>(true)) Object.DestroyImmediate(stray.gameObject);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            return (saved ? "saved " : "SAVE FAILED ") + PrefabPath;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ───────── 메인 메뉴: 키트 "Window (Game Menu)"
    private static void BuildMain(OverburstGameMenu menu, RectTransform content)
    {
        var panel = Kitted("Windows/Window (Game Menu).prefab", content, "Main Panel");
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(844f, 1440f);
        panel.anchoredPosition = new Vector2(0f, 10f);
        menu.mainPanel = panel;

        menu.title = NewText(panel, "Title", "일시정지", serif, 84, Gold, TextAnchor.MiddleCenter, Top(), new Vector2(0f, -190f), new Vector2(760f, 120f));
        AddShadow(menu.title);
        menu.subtitle = NewText(panel, "Subtitle", "던전  ·  게임이 멈춰 있습니다", sans, 32, Label, TextAnchor.MiddleCenter, Top(), new Vector2(0f, -305f), new Vector2(760f, 50f));
        var rule = NewImage(panel, "Title Rule", new Color(Gold.r, Gold.g, Gold.b, .45f));
        Place(rule.rectTransform, Top(), new Vector2(0f, -370f), new Vector2(420f, 2f));

        var group = panel.Find("Buttons Group");
        // 버튼 묶음은 제목 아래에서 아래로 자라게 둔다(던전에서만 "귀환"이 하나 더 있다). 안내 줄도 묶음의 마지막 칸이다.
        var groupRect = (RectTransform)group;
        groupRect.anchorMin = groupRect.anchorMax = new Vector2(.5f, 1f);
        groupRect.pivot = new Vector2(.5f, 1f);
        groupRect.anchoredPosition = new Vector2(0f, -430f);
        Object.DestroyImmediate(group.Find("Button (Gameplay)").gameObject);
        Object.DestroyImmediate(group.Find("Spacer (2)").gameObject);
        group.Find("Button (Options)").SetSiblingIndex(1);
        group.Find("Spacer (1)").SetSiblingIndex(2);
        menu.resumeButton = MenuButton(group, "Button (Resume)", "계속하기", out _);
        menu.settingsButton = MenuButton(group, "Button (Options)", "설정", out _);
        menu.returnButton = MenuButton(group, "Button (Logout)", "은신처로 귀환", out menu.returnLabel);
        menu.returnButton.name = "Button (Return)";
        menu.quitButton = MenuButton(group, "Button (Exit)", "게임 종료", out _);
        menu.quitButton.name = "Button (Quit)";

        var hint = NewText(group, "Hint", "ESC  닫기      ↑ ↓  선택      Enter  결정", sans, 28, Muted, TextAnchor.LowerCenter,
            new Vector2(.5f, 1f), Vector2.zero, new Vector2(760f, 90f));
        hint.horizontalOverflow = HorizontalWrapMode.Overflow;
        var hintLayout = hint.gameObject.AddComponent<LayoutElement>();
        hintLayout.minHeight = hintLayout.preferredHeight = 90f;
    }

    private static Button MenuButton(Transform group, string name, string label, out Text text)
    {
        var button = group.Find(name).GetComponent<Button>();
        text = button.transform.Find("Text").GetComponent<Text>();
        Restyle(text, label, serif, 50);
        StyleMainButtonLabel(text);
        return button;
    }

    // 기존 프리팹의 네 메인 버튼만 갱신한다. 다른 배치와 직렬화 연결은 보존한다.
    [MenuItem("OVERBURST/Codex/Objectizers/UI/Center Game Menu Button Labels")]
    public static void ImproveMainButtonLabels()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Stop Play before editing the menu prefab.");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var menu = root.GetComponent<OverburstGameMenu>();
            foreach (var button in new[] { menu.resumeButton, menu.settingsButton, menu.returnButton, menu.quitButton })
                StyleMainButtonLabel(button.transform.Find("Text").GetComponent<Text>());
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved) throw new System.InvalidOperationException("Menu prefab save failed.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void StyleMainButtonLabel(Text text)
    {
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = new Vector2(44f, 0f);
        rect.offsetMax = new Vector2(-44f, 0f);
        text.alignment = TextAnchor.MiddleCenter;
        // 한글 글꼴의 위아래 여백 대신 실제 글자 정점을 기준으로 가운데에 맞춘다.
        text.alignByGeometry = true;
        var shadow = text.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, .9f);
        shadow.effectDistance = new Vector2(3f, -3f);
        shadow.useGraphicAlpha = true;
    }

    // ───────── 설정 창: 키트 "Window (Settings)"
    private static OverburstSettingsPanel BuildSettings(OverburstGameMenu menu, RectTransform content)
    {
        var window = Kitted("Windows/Window (Settings).prefab", content, "Settings Window");
        window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
        window.sizeDelta = new Vector2(2400f, 1480f);
        window.anchoredPosition = new Vector2(0f, 10f);
        var panel = window.gameObject.AddComponent<OverburstSettingsPanel>();
        menu.settings = panel;

        // 장비 창과 같은 불투명 본문 바탕
        var surface = NewImage(window, "Opaque Content Surface", Surface);
        surface.transform.SetSiblingIndex(0);
        Place(surface.rectTransform, new Vector2(0f, 1f), new Vector2(10f, -148f), new Vector2(2380f, 1322f), new Vector2(0f, 1f));

        var header = window.Find("Header");
        var headerBackground = header.Find("Background").GetComponent<Image>();
        StripScheme(headerBackground.gameObject);
        headerBackground.color = HeaderTint;
        foreach (var effect in new[] { "Background/Effect Left", "Background/Effect Right" })
            header.Find(effect)?.gameObject.SetActive(false);
        Restyle(header.Find("Text").GetComponent<Text>(), "설정", serif, 56, Gold);
        panel.closeButton = header.Find("Button (Close)").GetComponent<Button>();

        // 탭 4개
        var buttons = window.Find("Tab Menu/Buttons Group");
        var tabTemplate = buttons.Find("Tab Button (3)");
        var extra = Object.Instantiate(tabTemplate.gameObject, buttons, false);
        extra.name = "Tab Button (4)";
        string[] tabNames = { "소리", "화면", "전투 표시", "조작" };
        panel.tabs = new UITab[4];
        var toggleGroup = buttons.GetComponent<ToggleGroup>();
        for (int i = 0; i < 4; i++)
        {
            var tab = buttons.Find("Tab Button (" + (i + 1) + ")").GetComponent<UITab>();
            Restyle(tab.transform.Find("Text").GetComponent<Text>(), tabNames[i], serif, 40);
            tab.group = toggleGroup;
            tab.isOn = i == 0;
            panel.tabs[i] = tab;
        }

        // 본문: 키트 예시 내용을 비우고 탭마다 한 쪽씩
        var tabContent = window.Find("Tab Content");
        foreach (Transform child in tabContent.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
        panel.pages = new GameObject[4];
        for (int i = 0; i < 4; i++)
        {
            var page = new GameObject("Page • " + tabNames[i], typeof(RectTransform));
            page.transform.SetParent(tabContent, false);
            Stretch(page.transform);
            page.SetActive(i == 0);
            panel.pages[i] = page;
            panel.tabs[i].targetContent = page;
        }

        var sound = (RectTransform)panel.pages[0].transform;
        panel.masterVolume = SliderRow(sound, 0, "전체 음량", "게임 전체 소리 크기", 1f);
        panel.uiVolume = SliderRow(sound, 1, "메뉴 소리", "메뉴·확인창을 누를 때 나는 소리 크기", .8f);
        panel.muteInBackground = SwitchRow(sound, 2, "뒤에 있을 때 소리 끄기", "다른 창을 보고 있으면 게임 소리를 끕니다", false, true);

        var screen = (RectTransform)panel.pages[1].transform;
        panel.screenMode = SelectRow(screen, 0, "화면 모드", "바꾸면 10초 안에 유지를 눌러야 적용됩니다", out _);
        panel.resolution = SelectRow(screen, 1, "해상도", "게임 화면의 가로 × 세로 픽셀", out _);
        panel.vSync = SwitchRow(screen, 2, "수직 동기화", "화면이 찢어져 보이지 않게 모니터 주사율에 맞춥니다", true, false);
        panel.frameLimit = SelectRow(screen, 3, "프레임 제한", "초당 최대 화면 갱신 수", out var frameRow, true);
        panel.frameLimitRow = frameRow.gameObject.AddComponent<CanvasGroup>();
        panel.frameLimitNote = frameRow.Find("Description").GetComponent<Text>();

        var combat = (RectTransform)panel.pages[2].transform;
        panel.cameraShake = SliderRow(combat, 0, "카메라 흔들림", "타격·피격·큰 몬스터 발소리에 화면이 흔들리는 세기", 1f);
        panel.hitEffect = SliderRow(combat, 1, "피격 화면 효과", "맞았을 때 화면 가장자리가 붉어지는 세기", 1f);
        panel.combatFacingIndicator = SwitchRow(combat, 2, "전투 방향 표시", "전투 중 발밑에 캐릭터가 바라보는 방향을 표시합니다", true, false);

        AddCombatFacingOptions(panel, combat);
        AddPresentationOptions(panel);

        BuildControls(panel, (RectTransform)panel.pages[3].transform);

        // 아래 줄: 기본값 · 안내 · 닫기
        var footer = window.Find("Button Group");
        var footerRule = NewImage(footer, "Footer Rule", Rule);
        Place(footerRule.rectTransform, new Vector2(0f, 0f), new Vector2(RowInset, 230f), new Vector2(RowWidth, 2f), new Vector2(0f, .5f));
        var close = footer.Find("Button (Apply)");
        close.name = "Button (Close Settings)";
        var closeRect = (RectTransform)close;
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 0f);
        closeRect.pivot = new Vector2(1f, .5f);
        // 열 맞춤: "닫기"의 오른쪽 끝 = 설정 조작 칸 오른쪽 끝(창 오른쪽에서 200), "기본값으로"의 왼쪽 끝 = 이름 열(200).
        closeRect.anchoredPosition = new Vector2(-RowInset, 116f);
        Restyle(close.Find("Text").GetComponent<Text>(), "닫기", serif, 46);
        panel.footerCloseButton = close.GetComponent<Button>();

        var reset = KitButton("Controls/Buttons/Rectangular/Button (Simple).prefab", footer, "Button (Reset)", "기본값으로", 36);
        Place((RectTransform)reset.transform, new Vector2(0f, 0f), new Vector2(RowInset - KeySpriteInset, 116f), new Vector2(440f, 130f), new Vector2(0f, .5f));
        panel.resetButton = reset;
        panel.statusText = NewText(footer, "Status", string.Empty, sans, 30, Label, TextAnchor.MiddleCenter, new Vector2(.5f, 0f), new Vector2(0f, 116f), new Vector2(1200f, 60f), new Vector2(.5f, .5f));
        return panel;
    }

    // 설정 한 줄: 왼쪽에 이름·설명, 오른쪽에 조작 칸. 줄 사이는 장비 창과 같은 구분선.
    // 모든 탭·아래 줄이 같은 두 세로선(왼쪽 RowInset, 오른쪽 창폭-RowInset)에 맞춘다.
    public static void AddPresentationSettings()
    {
        PlayerCombatFacingVfxBuilder.RequireIdle();
        LoadFonts();
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if(asset==null || EditorUtility.IsDirty(asset))throw new System.InvalidOperationException("Menu prefab missing or dirty.");
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            AddPresentationOptions(root.GetComponentInChildren<OverburstSettingsPanel>(true));
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath,out bool saved);
            if(!saved)throw new System.InvalidOperationException("Settings prefab save failed.");
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    private static void AddPresentationOptions(OverburstSettingsPanel panel)
    {
        var page=(RectTransform)panel.pages[2].transform;
        if(panel.combatScroll==null)
        {
            var existing=page.Cast<Transform>().Where(t=>t.name.StartsWith("Row • ")).ToArray();
            var viewport=new GameObject("Combat Viewport",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect)).GetComponent<RectTransform>();
            viewport.SetParent(page,false);Stretch(viewport);
            viewport.offsetMin=new Vector2(0,270);viewport.offsetMax=new Vector2(0,-20);
            viewport.GetComponent<Image>().color=Color.clear;
            var list=new GameObject("Combat List",typeof(RectTransform)).GetComponent<RectTransform>();
            list.SetParent(viewport,false);list.anchorMin=new Vector2(0,1);list.anchorMax=new Vector2(1,1);list.pivot=new Vector2(.5f,1);list.sizeDelta=new Vector2(0,1430);
            foreach(var row in existing){row.SetParent(list,false);Stretch(row);}
            var scroll=viewport.GetComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=list;scroll.horizontal=false;scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=80;scroll.inertia=true;
            var bar=new GameObject("Combat Scrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar)).GetComponent<RectTransform>();
            bar.SetParent(page,false);Stretch(bar);bar.anchorMin=new Vector2(1,0);bar.anchorMax=Vector2.one;
            bar.offsetMin=new Vector2(-145,270);bar.offsetMax=new Vector2(-135,-20);bar.GetComponent<Image>().color=Hex("403B3073");
            var handle=NewImage(bar,"Handle",new Color(Bright.r,Bright.g,Bright.b,.38f));Stretch(handle.transform);
            var scrollbar=bar.GetComponent<Scrollbar>();scrollbar.direction=Scrollbar.Direction.BottomToTop;scrollbar.handleRect=handle.rectTransform;scrollbar.targetGraphic=handle;
            scrollbar.navigation=new Navigation{mode=Navigation.Mode.None};scroll.verticalScrollbar=scrollbar;
            panel.combatScroll=scroll;
        }
        var content=panel.combatScroll.content;content.sizeDelta=new Vector2(0,1770);
        if(panel.parryPresentation==null)
            EffectRow(content,5,"패링 연출","패링 성공 순간의 금속 불꽃과 검날 빛",true,1,2,out panel.parryPresentation,out panel.parryPresentationIntensity);
        if(panel.heavyPresentation==null)
            EffectRow(content,6,"완충 강공 연출","에너지가 가득 찬 강공의 원소 발광과 화면 반응",true,1,2,out panel.heavyPresentation,out panel.heavyPresentationIntensity);
        if(panel.motionBlur==null)
            EffectRow(content,7,"모션블러","움직이는 카메라와 캐릭터의 잔상 · 기본 꺼짐",false,.01f,1,out panel.motionBlur,out panel.motionBlurIntensity);
        if(panel.edgeBlur==null)
            EffectRow(content,8,"가장자리 흐림","탐험 중 화면 가장자리를 부드럽게 흐립니다",true,.72f,1,out panel.edgeBlur,out panel.explorationEdgeBlurIntensity);
        if(panel.combatEdgeBlurIntensity==null)
            panel.combatEdgeBlurIntensity=SliderRow(content,9,"전투 중 흐림 강도","전투 중에는 이 강도로 가장자리 흐림을 적용합니다",.42f,true);
        var rows=new[]{panel.cameraShake.transform.parent.parent,panel.hitEffect.transform.parent.parent,panel.combatFacingIndicator.transform.parent.parent,
            panel.combatFacingStyle.transform.parent.parent,panel.combatFacingBrightness.transform.parent.parent,
            panel.parryPresentation.transform.parent.parent,panel.heavyPresentation.transform.parent.parent,panel.motionBlur.transform.parent.parent,panel.edgeBlur.transform.parent.parent,panel.combatEdgeBlurIntensity.transform.parent.parent};
        for(int i=0;i<rows.Length;i++)
        {
            float y=RowTop-i*170;
            ((RectTransform)rows[i].Find("Label")).anchoredPosition=new Vector2(RowInset,y-40);
            ((RectTransform)rows[i].Find("Description")).anchoredPosition=new Vector2(RowInset,y-108);
            ((RectTransform)rows[i].Find("Control")).anchoredPosition=new Vector2(-RowInset,y-96);
            var rule=rows[i].Find("Rule");
            if(i<rows.Length-1){if(rule==null)rule=NewImage(rows[i],"Rule",Rule).transform;Place((RectTransform)rule,new Vector2(0,1),new Vector2(RowInset,y-156),new Vector2(RowWidth,2),new Vector2(0,1));}
            else if(rule!=null)Object.DestroyImmediate(rule.gameObject);
            foreach(var selectable in rows[i].GetComponentsInChildren<Selectable>(true))
                if(!selectable.GetComponent<OverburstMenuSoundHook>())selectable.gameObject.AddComponent<OverburstMenuSoundHook>();
        }
    }
    private static void EffectRow(RectTransform page,int index,string title,string description,bool enabled,float value,float maximum,out Toggle toggle,out Slider slider)
    {
        toggle=SwitchRow(page,index,title,description,enabled,index==7);
        var slot=(RectTransform)toggle.transform.parent;
        var toggleRect=(RectTransform)toggle.transform;toggleRect.anchorMin=toggleRect.anchorMax=new Vector2(0,.5f);toggleRect.pivot=new Vector2(0,.5f);toggleRect.anchoredPosition=new Vector2(10,0);
        var group=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Kit+"Controls/Sliders/Slider Group (Horizontal).prefab"));
        var control=group.transform.Find("Slider (Horizontal)");control.SetParent(slot,false);Object.DestroyImmediate(group);
        var rect=(RectTransform)control;rect.anchorMin=new Vector2(.30f,.5f);rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(0,104);
        slider=control.GetComponent<Slider>();slider.minValue=0;slider.maxValue=maximum;slider.wholeNumbers=false;slider.SetValueWithoutNotify(value);
        Restyle(control.Find("Text").GetComponent<Text>(),Mathf.RoundToInt(value*100)+"%",sans,34,Bright);
    }
    private const float RowInset = 200f;
    private const float RowWidth = 2400f - RowInset * 2f;
    private const float RowTop = -70f;
    private const float RowHeight = 210f;

    private static RectTransform Row(RectTransform page, int index, string title, string description, out RectTransform slot, bool last)
    {
        var row = new GameObject("Row • " + title, typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(page, false);
        Stretch(row);
        float height = page.name == "Page • 전투 표시" ? 170f : RowHeight;
        float y = RowTop - index * height;
        NewText(row, "Label", title, sans, 42, Bright, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(RowInset, y - 40f), new Vector2(1000f, 64f), new Vector2(0f, 1f));
        NewText(row, "Description", description, sans, 30, Muted, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(RowInset, y - 108f), new Vector2(1150f, 50f), new Vector2(0f, 1f));
        slot = new GameObject("Control", typeof(RectTransform)).GetComponent<RectTransform>();
        slot.SetParent(row, false);
        Place(slot, new Vector2(1f, 1f), new Vector2(-RowInset, y - 96f), new Vector2(900f, 130f), new Vector2(1f, .5f));
        if (!last)
        {
            var line = NewImage(row, "Rule", Rule);
            Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(RowInset, y - height + 14f), new Vector2(RowWidth, 2f), new Vector2(0f, 1f));
        }
        return row;
    }

    private static Slider SliderRow(RectTransform page, int index, string title, string description, float value, bool last = false)
    {
        Row(page, index, title, description, out var slot, last);
        var group = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "Controls/Sliders/Slider Group (Horizontal).prefab"));
        var sliderTransform = group.transform.Find("Slider (Horizontal)");
        sliderTransform.SetParent(slot, false);
        Object.DestroyImmediate(group);
        var rect = (RectTransform)sliderTransform;
        rect.anchorMin = new Vector2(0f, .5f);
        rect.anchorMax = new Vector2(1f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 104f);
        var slider = sliderTransform.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(value);
        Restyle(sliderTransform.Find("Text").GetComponent<Text>(), Mathf.RoundToInt(value * 100f) + "%", sans, 34, Bright);
        return slider;
    }

    // 기존 메뉴의 배치와 다른 설정을 보존하고 전투 표시 행 하나만 추가한다.
    public static void AddCombatFacingSetting()
    {
        PlayerCombatFacingVfxBuilder.RequireIdle();
        LoadFonts();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (asset == null || EditorUtility.IsDirty(asset)) throw new System.InvalidOperationException("Menu prefab missing or dirty.");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var panel = root.GetComponentInChildren<OverburstSettingsPanel>(true);
            if (panel == null || panel.pages.Length != 4) throw new System.InvalidOperationException("Settings panel contract changed.");
            if (panel.combatFacingIndicator == null)
            {
                var combat = (RectTransform)panel.pages[2].transform;
                panel.combatFacingIndicator = SwitchRow(combat, 2, "전투 방향 표시", "전투 중 발밑에 캐릭터가 바라보는 방향을 표시합니다", true, false);
                var previousRow = panel.hitEffect.transform.parent.parent;
                if (previousRow.Find("Rule") == null)
                {
                    var rule = NewImage(previousRow, "Rule", Rule);
                    Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(RowInset, RowTop - 2f * RowHeight + 14f), new Vector2(RowWidth, 2f), new Vector2(0f, 1f));
                }
                foreach (var selectable in panel.combatFacingIndicator.transform.parent.parent.GetComponentsInChildren<Selectable>(true))
                    if (!selectable.GetComponent<OverburstMenuSoundHook>()) selectable.gameObject.AddComponent<OverburstMenuSoundHook>();
            }
            AddCombatFacingOptions(panel, (RectTransform)panel.pages[2].transform);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved) throw new System.InvalidOperationException("Menu prefab save failed.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void AddCombatFacingOptions(OverburstSettingsPanel panel, RectTransform combat)
    {
        if (panel.combatFacingStyle == null)
            panel.combatFacingStyle = SelectRow(combat, 3, "방향 표시 모양", "기존 곡선형과 끝을 연장한 형태를 선택합니다", out _);
        panel.combatFacingStyle.options.Clear();
        panel.combatFacingStyle.options.Add("기존 절제형");
        panel.combatFacingStyle.options.Add("끝 연장형");
        panel.combatFacingStyle.SelectOptionByIndex(1);
        panel.combatFacingStyle.transform.Find("Text").GetComponent<Text>().text = "끝 연장형";
        if (panel.combatFacingBrightness == null)
            panel.combatFacingBrightness = SliderRow(combat, 4, "방향 표시 밝기", "100%는 현재 밝기이며 0~200%까지 조절할 수 있습니다", 1f, true);
        panel.combatFacingBrightness.minValue = 0f; panel.combatFacingBrightness.maxValue = 2f;
        panel.combatFacingBrightness.SetValueWithoutNotify(1f);
        var rows = new[] { panel.cameraShake.transform.parent.parent, panel.hitEffect.transform.parent.parent,
            panel.combatFacingIndicator.transform.parent.parent, panel.combatFacingStyle.transform.parent.parent,
            panel.combatFacingBrightness.transform.parent.parent };
        for (int i = 0; i < rows.Length; i++)
        {
            float y = RowTop - i * 170f;
            ((RectTransform)rows[i].Find("Label")).anchoredPosition = new Vector2(RowInset, y - 40f);
            ((RectTransform)rows[i].Find("Description")).anchoredPosition = new Vector2(RowInset, y - 108f);
            ((RectTransform)rows[i].Find("Control")).anchoredPosition = new Vector2(-RowInset, y - 96f);
            var rule = rows[i].Find("Rule");
            if (i < rows.Length - 1)
            {
                if (rule == null) rule = NewImage(rows[i], "Rule", Rule).transform;
                Place((RectTransform)rule, new Vector2(0f, 1f), new Vector2(RowInset, y - 170f + 14f), new Vector2(RowWidth, 2f), new Vector2(0f, 1f));
            }
            else if (rule != null) Object.DestroyImmediate(rule.gameObject);
            foreach (var selectable in rows[i].GetComponentsInChildren<Selectable>(true))
                if (!selectable.GetComponent<OverburstMenuSoundHook>()) selectable.gameObject.AddComponent<OverburstMenuSoundHook>();
        }
    }

    private static Toggle SwitchRow(RectTransform page, int index, string title, string description, bool on, bool last)
    {
        Row(page, index, title, description, out var slot, last);
        var group = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "Controls/Toggles/Switch Toggle.prefab"));
        var sw = group.transform.Find("Switch");
        sw.SetParent(slot, false);
        Object.DestroyImmediate(group);
        var rect = (RectTransform)sw;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
        rect.pivot = new Vector2(1f, .5f);
        rect.anchoredPosition = new Vector2(-10f, 0f); // 테두리 그림이 틀보다 10(2배 단위) 밖으로 나와서 안으로 당긴다
        rect.localScale = Vector3.one * 1.4f;
        Restyle(sw.Find("On Text").GetComponent<Text>(), "켬", sans, 32);
        Restyle(sw.Find("Off Text").GetComponent<Text>(), "끔", sans, 32);
        var toggle = sw.GetComponent<Toggle>();
        toggle.group = null;
        toggle.SetIsOnWithoutNotify(on);
        return toggle;
    }

    private static UISwitchSelect SelectRow(RectTransform page, int index, string title, string description, out RectTransform row, bool last = false)
    {
        row = Row(page, index, title, description, out var slot, last);
        var select = Kitted("Controls/Select Fields/Switch Select.prefab", slot, "Switch Select");
        const float scale = .7f;
        // 키트 선택칸은 화살표가 틀 안쪽으로 44(2배 단위)씩 들어가 있다. 보이는 화살표 끝을 조작 칸 양끝(슬라이더·스위치와 같은 선)에 맞춘다.
        const float arrowInset = 44f;
        select.anchorMin = select.anchorMax = new Vector2(1f, .5f);
        select.pivot = new Vector2(1f, .5f);
        select.anchoredPosition = new Vector2(arrowInset, 0f);
        select.sizeDelta = new Vector2((900f + arrowInset * 2f) / scale, 192f);
        select.localScale = Vector3.one * scale;
        Restyle(select.Find("Text").GetComponent<Text>(), "—", sans, 46, Bright);
        var component = select.GetComponent<UISwitchSelect>();
        component.options.Clear();
        return component;
    }

    // ───────── 조작 탭: 스크롤 목록
    private struct KeyEntry
    {
        public string Section, Label, Action, Part, Mirror;
        public KeyEntry(string section, string label, string action, string part = null, string mirror = null)
        { Section = section; Label = label; Action = action; Part = part; Mirror = mirror; }
    }

    private static readonly KeyEntry[] Keys =
    {
        new KeyEntry("이동", "위로 이동", "Move", "Up"),
        new KeyEntry("이동", "아래로 이동", "Move", "Down"),
        new KeyEntry("이동", "왼쪽으로 이동", "Move", "Left"),
        new KeyEntry("이동", "오른쪽으로 이동", "Move", "Right"),
        new KeyEntry("이동", "걷기 / 달리기 전환", "WalkToggle"),
        new KeyEntry("이동", "회피", "Evade"),
        new KeyEntry("전투", "공격", "Attack"),
        new KeyEntry("전투", "막기 · 조준", "Aim"),
        new KeyEntry("전투", "전투 태세 전환", "CombatMode"),
        new KeyEntry("상호작용 · 창", "상호작용", "Interact"),
        new KeyEntry("상호작용 · 창", "인벤토리", "Inventory"),
        new KeyEntry("상호작용 · 창", "장비 · 능력치", "Equipment", null, "Equipment"),
        new KeyEntry("상호작용 · 창", "줍기 방식 전환", "LootModeCycle"),
        new KeyEntry("상호작용 · 창", "시점 초기화", "ZoomReset"),
        new KeyEntry("퀵슬롯", "퀵슬롯 1", "QuickSlot1"), new KeyEntry("퀵슬롯", "퀵슬롯 2", "QuickSlot2"),
        new KeyEntry("퀵슬롯", "퀵슬롯 3", "QuickSlot3"), new KeyEntry("퀵슬롯", "퀵슬롯 4", "QuickSlot4"),
        new KeyEntry("퀵슬롯", "퀵슬롯 5", "QuickSlot5"), new KeyEntry("퀵슬롯", "퀵슬롯 6", "QuickSlot6"),
        new KeyEntry("퀵슬롯", "퀵슬롯 7", "QuickSlot7"), new KeyEntry("퀵슬롯", "퀵슬롯 8", "QuickSlot8"),
        new KeyEntry("퀵슬롯", "퀵슬롯 9", "QuickSlot9"), new KeyEntry("퀵슬롯", "퀵슬롯 10", "QuickSlot10"),
    };

    // 조작 탭도 다른 탭과 같은 두 세로선에 맞춘다: 목록 창은 그 선보다 KeyPad만큼 넓고(줄무늬 여백), 글자·키 칸은 KeyPad 안쪽에 놓는다.
    private const float KeyPad = 24f;
    // 키트 "Button (Simple)" 그림은 틀 바깥에 투명 여백이 있어 보이는 테두리가 18(2배 단위) 안쪽에서 끝난다.
    private const float KeySpriteInset = 18f;

    private static void Band(RectTransform rect, float y, float height, float inset = 0f)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.offsetMin = new Vector2(inset, y - height);
        rect.offsetMax = new Vector2(-inset, y);
    }

    private static void BuildControls(OverburstSettingsPanel panel, RectTransform page)
    {
        var viewport = new GameObject("Key Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)).GetComponent<RectTransform>();
        viewport.SetParent(page, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(RowInset - KeyPad, 270f);
        viewport.offsetMax = new Vector2(-(RowInset - KeyPad), -40f);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        var list = new GameObject("Key List", typeof(RectTransform)).GetComponent<RectTransform>();
        list.SetParent(viewport, false);
        list.anchorMin = new Vector2(0f, 1f);
        list.anchorMax = new Vector2(1f, 1f);
        list.pivot = new Vector2(.5f, 1f);
        list.anchoredPosition = Vector2.zero;

        var rows = new List<OverburstKeyBindingRow>();
        const float sectionHeight = 96f, rowHeight = 108f;
        const float keyWidth = 500f, keyHeight = 92f;
        float y = 0f;
        string section = null;
        int stripe = 0;
        void Section(string name)
        {
            var heading = NewText(list, "Section • " + name, name, serif, 36, Gold, TextAnchor.LowerLeft, new Vector2(0f, 1f), new Vector2(KeyPad, y - 20f), new Vector2(900f, 64f), new Vector2(0f, 1f));
            heading.name = "Section • " + name;
            var line = NewImage(list, "Section Rule • " + name, Rule);
            Band(line.rectTransform, y - sectionHeight + 8f, 2f, KeyPad);
            y -= sectionHeight;
            stripe = 0;
        }
        RectTransform KeyRow(string name)
        {
            var row = new GameObject("Key • " + name, typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(list, false);
            Band(row, y, rowHeight);
            if (stripe++ % 2 == 1)
            {
                var band = NewImage(row, "Stripe", new Color(1f, 1f, 1f, .012f)); // 리니어 합성이라 아주 옅게
                Stretch(band.transform);
            }
            y -= rowHeight;
            return row;
        }
        foreach (var key in Keys)
        {
            if (key.Section != section) { section = key.Section; Section(section); }
            var row = KeyRow(key.Label);
            var label = NewText(row, "Label", key.Label, sans, 38, Label, TextAnchor.MiddleLeft, new Vector2(0f, .5f), new Vector2(KeyPad, 0f), new Vector2(1100f, 70f), new Vector2(0f, .5f));
            var button = KitButton("Controls/Buttons/Rectangular/Button (Simple).prefab", row, "Key Button", "—", 36);
            Place((RectTransform)button.transform, new Vector2(1f, .5f), new Vector2(-KeyPad + KeySpriteInset, 0f), new Vector2(keyWidth, keyHeight), new Vector2(1f, .5f));
            var binding = row.gameObject.AddComponent<OverburstKeyBindingRow>();
            binding.actionName = key.Action;
            binding.compositePart = key.Part ?? string.Empty;
            binding.mirrorUiAction = key.Mirror ?? string.Empty;
            binding.label = label;
            binding.keyButton = button;
            binding.keyText = button.transform.Find("Text").GetComponent<Text>();
            rows.Add(binding);
        }
        // 고정 키: 메뉴(ESC)는 바꿀 수 없다. 다른 줄과 같은 키 칸 모양으로, 누를 수 없게 흐리게 둔다.
        Section("고정");
        var fixedRow = KeyRow("메뉴");
        NewText(fixedRow, "Label", "메뉴 열기 · 창 닫기  <color=#8C857C><size=30>바꿀 수 없음</size></color>", sans, 38, Label, TextAnchor.MiddleLeft,
            new Vector2(0f, .5f), new Vector2(KeyPad, 0f), new Vector2(1100f, 70f), new Vector2(0f, .5f));
        var escButton = KitButton("Controls/Buttons/Rectangular/Button (Simple).prefab", fixedRow, "Key Button (Fixed)", "ESC", 36);
        Place((RectTransform)escButton.transform, new Vector2(1f, .5f), new Vector2(-KeyPad + KeySpriteInset, 0f), new Vector2(keyWidth, keyHeight), new Vector2(1f, .5f));
        escButton.interactable = false;
        escButton.transform.Find("Text").GetComponent<Text>().color = Muted;
        y -= 30f;
        list.sizeDelta = new Vector2(0f, -y);

        var scroll = viewport.GetComponent<ScrollRect>();
        scroll.content = list;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 60f;
        scroll.inertia = true;

        // 가는 스크롤 막대(창고 창과 같은 모양)
        var bar = new GameObject("Key Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar)).GetComponent<RectTransform>();
        bar.SetParent(page, false);
        bar.anchorMin = new Vector2(1f, 0f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(1f, .5f);
        bar.offsetMin = new Vector2(-(RowInset - KeyPad) + 20f, 270f); // 목록 오른쪽 선 바깥 여백에 둔다
        bar.offsetMax = new Vector2(-(RowInset - KeyPad) + 30f, -40f);
        bar.GetComponent<Image>().color = Hex("403B3073");
        var area = new GameObject("Sliding Area", typeof(RectTransform)).GetComponent<RectTransform>();
        area.SetParent(bar, false);
        Stretch(area);
        var handle = NewImage(area, "Handle", new Color(Bright.r, Bright.g, Bright.b, .38f));
        Stretch(handle.transform);
        var scrollbar = bar.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        panel.keyRows = rows.ToArray();
        panel.keyScroll = scroll;
    }

    private static void BuildKeyPrompt(OverburstSettingsPanel panel, RectTransform content)
    {
        var box = BuildBox(content, "Key Prompt", out var headline, out var description, out var buttons);
        Object.DestroyImmediate(buttons.gameObject);
        headline.fontSize = 50;
        description.text = "ESC  취소";
        description.color = Muted;
        panel.keyPrompt = box;
        panel.keyPromptLabel = headline;
    }

    private static void BuildModal(OverburstGameMenu menu, RectTransform content)
    {
        var box = BuildBox(content, "Confirm Modal", out var headline, out var description, out var buttons);
        menu.modal = box;
        menu.modalHeadline = headline;
        menu.modalDescription = description;
        var confirm = buttons.Find("Button (Confirm)");
        var cancel = Object.Instantiate(confirm.gameObject, buttons, false).transform;
        cancel.name = "Button (Cancel)";
        cancel.SetSiblingIndex(0);
        menu.modalConfirm = confirm.GetComponent<Button>();
        menu.modalCancel = cancel.GetComponent<Button>();
        menu.modalConfirmLabel = confirm.Find("Text").GetComponent<Text>();
        menu.modalCancelLabel = cancel.Find("Text").GetComponent<Text>();
        Restyle(menu.modalConfirmLabel, "확인", serif, 46);
        Restyle(menu.modalCancelLabel, "취소", serif, 46);
        foreach (var button in new[] { confirm, cancel })
        {
            var element = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 560f;
            element.minWidth = 560f;
            ((RectTransform)button).sizeDelta = new Vector2(560f, 190f);
        }
        var layout = buttons.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 60f;
        layout.childForceExpandWidth = false;
        layout.childControlWidth = false;
    }

    // 키트 "Modal Box"를 복제해 제목·설명·버튼 줄이 있는 상자를 만든다.
    private static GameObject BuildBox(RectTransform content, string name, out Text headline, out Text description, out Transform buttons)
    {
        var box = Kitted("Modal Box.prefab", content, name);
        Stretch(box);
        var overlay = box.Find("Overlay").GetComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, .72f);
        overlay.raycastTarget = true;
        var inner = box.Find("Inner Box");
        ((RectTransform)inner).sizeDelta = new Vector2(1560f, 0f);
        Object.DestroyImmediate(inner.Find("Button (Close)").gameObject);
        // 키트 상자 바탕은 반투명이라 뒤 메뉴가 비친다. 불투명 바탕을 깐다.
        var surface = NewImage(inner, "Opaque Surface", new Color(Surface.r, Surface.g, Surface.b, .97f));
        surface.transform.SetSiblingIndex(0);
        surface.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch(surface.transform);
        surface.rectTransform.offsetMin = new Vector2(14f, 14f);
        surface.rectTransform.offsetMax = new Vector2(-14f, -14f);
        headline = inner.Find("Text Group/Headline Text").GetComponent<Text>();
        description = inner.Find("Text Group/Description Text").GetComponent<Text>();
        Restyle(headline, "제목", serif, 54, Gold);
        Restyle(description, "설명", sans, 36, Label);
        description.lineSpacing = 1.15f;
        buttons = inner.Find("Buttons Group");
        box.gameObject.SetActive(false);
        return box.gameObject;
    }

    // ───────── 공용
    private static RectTransform Kitted(string kitPath, Transform parent, string name)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + kitPath);
        var go = Object.Instantiate(source, parent, false);
        go.name = name;
        // 키트의 창·모달 동작 스크립트는 쓰지 않는다(열기·닫기·끌기는 OverburstGameMenu가 맡는다).
        foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour is UIWindow || behaviour is UIModalBox || behaviour is UIAlwaysOnTop || behaviour is UIDragObject || behaviour is UIBlackOverlay)
                Object.DestroyImmediate(behaviour);
        // 키트 예시에 걸려 있던 이벤트 연결도 비운다. 동작은 런타임 코드가 건다.
        foreach (var button in go.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
        foreach (var toggle in go.GetComponentsInChildren<Toggle>(true)) toggle.onValueChanged = new Toggle.ToggleEvent();
        var group = go.GetComponent<CanvasGroup>();
        if (group != null) { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }
        return (RectTransform)go.transform;
    }

    private static Button KitButton(string kitPath, Transform parent, string name, string label, int size)
    {
        var rect = Kitted(kitPath, parent, name);
        var text = rect.Find("Text").GetComponent<Text>();
        Restyle(text, label, sans, size, Bright);
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 0f);
        textRect.offsetMax = new Vector2(-24f, 0f);
        text.alignment = TextAnchor.MiddleCenter;
        var button = rect.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        return button;
    }

    private static void Restyle(Text text, string value, Font font, int size, Color? color = null)
    {
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Normal;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (color.HasValue) { StripScheme(text.gameObject); text.color = color.Value; }
    }

    private static void StripScheme(GameObject go)
    {
        foreach (var scheme in go.GetComponents<ColorSchemeElement>()) Object.DestroyImmediate(scheme);
    }

    private static Text NewText(Transform parent, string name, string value, Font font, int size, Color color, TextAnchor align,
        Vector2 anchor, Vector2 position, Vector2 sizeDelta, Vector2? pivot = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        Place(text.rectTransform, anchor, position, sizeDelta, pivot ?? new Vector2(.5f, 1f));
        return text;
    }

    private static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void AddShadow(Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, .75f);
        shadow.effectDistance = new Vector2(3f, -3f);
    }

    private static Vector2 Top() => new Vector2(.5f, 1f);

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot ?? new Vector2(.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(Transform transform)
    {
        var rect = (RectTransform)transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void LoadFonts()
    {
        var equipment = AssetDatabase.LoadAssetAtPath<GameObject>(EquipmentPrefab);
        serif = equipment != null ? equipment.transform.Find("Header/Text")?.GetComponent<Text>()?.font : null;
        sans = equipment != null ? equipment.transform.Find("Layout/Character Detail")?.GetComponent<Text>()?.font : null;
    }

    private static AudioClip Clip(string name)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundFolder + name + ".wav");
        if (clip == null) Debug.LogWarning("[OverburstGameMenuBuilder] 소리를 찾지 못했습니다: " + name);
        return clip;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var color);
        return color;
    }
}
