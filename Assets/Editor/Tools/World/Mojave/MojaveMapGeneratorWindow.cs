using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Uses the shipped generator in a private preview scene; saves each accepted map as a new scene.</summary>
public sealed class MojaveMapGeneratorWindow : EditorWindow
{
    public const string MenuPath = "Overburst/Mojave/Map Generator";
    const int themeIndex = 2;
    [SerializeField] bool expandedMap = true;
    [SerializeField] bool compactMap = true;
    [SerializeField] bool useBackdrops = true;
    Toggle backdropField;
    [SerializeField] bool mixedCombatSizes = true;
    Toggle combatSizeField;
    [SerializeField] bool randomCombatLayout;
    Toggle randomCombatField;
    DropdownField sizeField;
    [SerializeField] int customMapSize;
    IntegerField mapMetresField;
    [SerializeField] int combatAreaCount = 9;
    DropdownField combatCountField;
    public bool ExpandedMap => expandedMap;
    string SourceScenePath => MojaveWorldBuilder.ScenePath;
    string OwnedRoot => MojaveWorldBuilder.OwnedRoot;
    string ThemeName => "Mojave";
    public int ThemeIndex => themeIndex;
    [SerializeField] int requestedSeed = 73129;
    [SerializeField] string lastSavedPath;
    [SerializeField] List<int> history = new List<int>();
    [SerializeField] int historyIndex = -1;
    [SerializeField] int previewPlaceIndex;
    [SerializeField] float previewDistance = 20;
    [SerializeField] int viewMode;
    [SerializeField] bool restoreDraft;
    [SerializeField] bool tileBounds;
    [SerializeField] bool freeCamera;
    [SerializeField] bool hasFreePose;
    [SerializeField] Vector3 freePosition;
    [SerializeField] Vector2 freeAngles;
    [SerializeField] float flySpeed = 12;
    Scene draft;
    MojaveWorld world;
    MojaveExplorer explorer;
    MojaveCamera rig;
    Camera camera;
    RenderTexture frame;
    Hash128 baselineHash;
    IntegerField seedField;
    DropdownField placeField;
    DropdownField viewField;
    DropdownField cameraModeField;
    Slider zoom;
    Label stats, status;
    Image preview;
    VisualElement previewViewport, legend;
    MojaveGameplayOverlayElement overlay;
    readonly List<Label> areaLabels = new List<Label>();
    readonly List<Label> tileLabels = new List<Label>();
    readonly HashSet<KeyCode> navigationKeys = new HashSet<KeyCode>();
    Button save, open, previous, next;
    bool busy;
    double restoreDeadline;
    double lastNavigationTime, lastRenderTime;
    bool previewDirty;
    int capturedPointer = -1, dragButton = -1;
    Vector2 lastPointer;

    public MojaveLayout QuickLayout {get;private set;}
    public double QuickLayoutSeconds {get;private set;}
    public MojaveWorld DraftWorld => world;
    public string LastSavedPath => lastSavedPath;
    public int RequestedSeed => requestedSeed;
    public bool HasDraft => world != null && draft.IsValid();
    public RenderTexture PreviewTexture => frame;
    public Camera PreviewCamera => camera;
    public int PreviewPlaceIndex => previewPlaceIndex;
    public float PreviewDistance => previewDistance;
    public int ViewMode => viewMode;
    public MojaveGameplayOverlayElement Overlay => overlay;
    public bool FreeCamera => freeCamera;
    public bool TileBounds => tileBounds;

    [MenuItem(MenuPath)]
    public static MojaveMapGeneratorWindow ShowTool()
    {
        var window = GetWindow<MojaveMapGeneratorWindow>();
        window.titleContent = new GUIContent("Mojave 맵 생성");
        window.minSize = new Vector2(940, 650);
        window.Show();
        window.SetTheme(2,false);
        if (!window.expandedMap) window.SetMapPreset(2,false);
        window.SetBackdrops(true,false);
        window.SetCombatSizes(true,false);
        if (window.combatAreaCount == 0) window.SetCombatCount(9,false);
        return window;
    }

    [MenuItem("Overburst/Mojave/Combat Tiles/Map Generator")]
    public static MojaveMapGeneratorWindow ShowCombatTool() => ShowTool();
    public void SetTheme(int value,bool generate=true)
    {
        Guard();if(value!=2)throw new InvalidOperationException("이관된 확정 Mojave 방식만 사용할 수 있습니다.");
        RefreshControls();if(generate)Generate(requestedSeed);
    }
    public void SetExpandedMap(bool value,bool generate=true)
        =>SetMapPreset(value?1:0,generate);
    public void SetMapPreset(int value,bool generate=true)
    {
        Guard();value=Mathf.Clamp(value,0,2);if(value>0&&themeIndex!=2)throw new InvalidOperationException("확장 맵은 다듬은 Mojave에서 선택해.");
        bool expanded=value>0,compact=value==2;
        if(expandedMap!=expanded||compactMap!=compact||customMapSize!=0) {customMapSize=0;ReleaseDraft();ReleaseFrame();restoreDraft=false;expandedMap=expanded;compactMap=compact;history.Clear();historyIndex=-1;previewPlaceIndex=0;hasFreePose=false;}
        sizeField?.SetValueWithoutNotify(sizeField.choices[value]);
        if(!expandedMap)combatAreaCount=0;
        if(placeField!=null){placeField.choices=PlaceNames();placeField.SetValueWithoutNotify(placeField.choices[0]);}
        RefreshControls();if(generate)Generate(requestedSeed);
    }
    public void SetMapSize(int metres,bool generate=false)
    {
        Guard();
        if(themeIndex!=2)throw new InvalidOperationException("맵 크기 직접 입력은 다듬은 Mojave에서 선택해.");
        if(metres<MojaveLayout.MinimumMapSize||metres>MojaveLayout.MaximumMapSize) {RefreshControls();throw new ArgumentOutOfRangeException(nameof(metres),"맵 한 변은 256~768m로 입력해.");}
        if(customMapSize!=metres) {ReleaseDraft();ReleaseFrame();restoreDraft=false;customMapSize=metres;history.Clear();historyIndex=-1;previewPlaceIndex=0;hasFreePose=false;}
        RefreshControls();SetStatus($"맵 한 변 {metres}m · 생성 버튼으로 적용");if(generate)Generate(requestedSeed);
    }
    public void SetBackdrops(bool value,bool generate=true)
    {
        Guard();if(value&&themeIndex!=2)throw new InvalidOperationException("배경 타일은 다듬은 Mojave에서 선택해.");
        if(useBackdrops!=value){ReleaseDraft();ReleaseFrame();restoreDraft=false;useBackdrops=value;history.Clear();historyIndex=-1;}
        backdropField?.SetValueWithoutNotify(value);RefreshControls();if(generate)Generate(requestedSeed);
    }
    public void SetCombatCount(int value,bool generate=true)
    {
        Guard();if(value!=0&&value!=6&&value!=9&&value!=12&&value!=15)throw new InvalidOperationException("전투 구역은 6·9·12·15개 중 선택해.");
        if(value!=0&&(themeIndex!=2||!expandedMap))throw new InvalidOperationException("전투 구역 수는 다듬은 Mojave 확장 맵에서 선택해.");
        if(combatAreaCount!=value){ReleaseDraft();ReleaseFrame();restoreDraft=false;combatAreaCount=value;history.Clear();historyIndex=-1;previewPlaceIndex=0;hasFreePose=false;}
        RefreshControls();if(generate)Generate(requestedSeed);
    }

    public void SetCombatSizes(bool value,bool generate=true)
    {
        Guard();if(value&&themeIndex!=2)throw new InvalidOperationException("전투 타일 크기는 다듬은 Mojave에서 선택해.");
        if(mixedCombatSizes!=value){ReleaseDraft();ReleaseFrame();restoreDraft=false;mixedCombatSizes=value;history.Clear();historyIndex=-1;}
        combatSizeField?.SetValueWithoutNotify(value);RefreshControls();if(generate)Generate(requestedSeed);
    }

    public void SetRandomCombatLayout(bool value,bool generate=false)
    {
        Guard();
        if(randomCombatLayout!=value){ReleaseDraft();ReleaseFrame();restoreDraft=false;randomCombatLayout=value;history.Clear();historyIndex=-1;previewPlaceIndex=0;hasFreePose=false;}
        RefreshControls();if(generate)Generate(requestedSeed);
    }

    void OnEnable()
    {
        EditorApplication.playModeStateChanged += PlayState;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        EditorApplication.update += NavigationTick;
        lastNavigationTime = EditorApplication.timeSinceStartup;
    }
    void BeforeReload() { restoreDraft = HasDraft; }
    void OnDisable()
    {
        EditorApplication.playModeStateChanged -= PlayState;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
        EditorApplication.update -= RestoreAfterReload;
        EditorApplication.update -= NavigationTick;StopNavigation();
        ReleaseDraft();
        ReleaseFrame();
    }
    void PlayState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        restoreDraft = false;
        ReleaseDraft(); ReleaseFrame(); RefreshControls();
        SetStatus("Play 종료 후 생성 버튼으로 새 미리보기를 만들 수 있어.");
    }

    public void CreateGUI()
    {
        var root = rootVisualElement; root.Clear();
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/Tools/World/Mojave/MojaveMapGenerator.uss");
        if (sheet != null) root.styleSheets.Add(sheet);
        root.AddToClassList("mojave-tool");
        var header = new VisualElement(); header.AddToClassList("header");
        header.Add(new Label(ThemeName.ToUpperInvariant()+"  /  MAP GENERATOR") { name = "title" });
        header.Add(new Label("원본 장소 12개 · 전투 유형 8종 · 6·9·12·15구역 · 연속 지면") { name = "subtitle" });
        root.Add(header);
        var body = new VisualElement(); body.AddToClassList("body"); root.Add(body);
        var controls = new ScrollView(); controls.AddToClassList("controls"); body.Add(controls);
        controls.Add(Section("01   맵 생성"));
        controls.Add(new Label("지역: Mojave Desert · 확정 방식"));
        sizeField=new DropdownField("배치 프리셋",new List<string>{"기본 · 256m","확장 · 384m","밀집 확장 · 320m"},compactMap?2:expandedMap?1:0) {name="map-size"};
        sizeField.RegisterValueChangedCallback(_=>Execute(()=>SetMapPreset(sizeField.index,false)));controls.Add(sizeField);
        mapMetresField=new IntegerField("맵 한 변(m)") {name="map-metres",isDelayed=true,tooltip="256~768m. 타일과 바위 크기는 유지하고 구역 간격과 배경 범위를 바꿉니다. 입력 후 생성 버튼을 누르세요."};
        mapMetresField.RegisterValueChangedCallback(e=>Execute(()=>SetMapSize(e.newValue,false)));controls.Add(mapMetresField);
        combatCountField=new DropdownField("전투 구역 수",new List<string>{"맵 크기 기본값","6개","9개","12개","15개"},combatAreaCount==6?1:combatAreaCount==9?2:combatAreaCount==12?3:combatAreaCount==15?4:0) {name="combat-count"};
        combatCountField.RegisterValueChangedCallback(_=>Execute(()=>SetCombatCount(new[]{0,6,9,12,15}[combatCountField.index],false)));controls.Add(combatCountField);
        backdropField=new Toggle("빈 공간에 바위 배경 타일 배치") {name="backdrops",value=useBackdrops};
        backdropField.RegisterValueChangedCallback(e=>Execute(()=>SetBackdrops(e.newValue,false)));controls.Add(backdropField);
        combatSizeField=new Toggle("소형 32m · 중형 46m · 대형 62.4m 타일 혼합") {name="combat-sizes",value=mixedCombatSizes};
        combatSizeField.RegisterValueChangedCallback(e=>Execute(()=>SetCombatSizes(e.newValue,false)));controls.Add(combatSizeField);
        randomCombatField=new Toggle("전투 구역 무작위 배치") {name="random-combat-layout",value=randomCombatLayout,tooltip="타일 크기와 겹침을 고려해 위치를 뽑고 가까운 구역 사이에 길을 연결합니다."};
        randomCombatField.RegisterValueChangedCallback(e=>Execute(()=>SetRandomCombatLayout(e.newValue,false)));controls.Add(randomCombatField);
        seedField = new IntegerField("시드") { name = "seed", value = requestedSeed, isDelayed = true };
        seedField.RegisterValueChangedCallback(e => {requestedSeed=e.newValue;ReleaseDraft();ReleaseFrame();RefreshControls();}); controls.Add(seedField);
        controls.Add(new Button(()=>Execute(PreviewLayout)) {text="레이아웃만 프리뷰",name="layout-preview"});
        var generate = new Button(() => Execute(() => Generate(requestedSeed))) { text = "전체 맵 생성", name = "generate" };
        generate.AddToClassList("primary"); controls.Add(generate);
        controls.Add(new Button(() => Execute(() => Generate(NewSeed(requestedSeed)))) { text = "새 시드로 생성", name = "new-seed" });
        var historyRow = new VisualElement(); historyRow.AddToClassList("row"); controls.Add(historyRow);
        previous = new Button(() => Execute(() => RestoreHistory(-1))) { text = "← 이전 생성", name = "previous" };
        next = new Button(() => Execute(() => RestoreHistory(1))) { text = "다음 생성 →", name = "next" };
        historyRow.Add(previous); historyRow.Add(next);
        controls.Add(Section("02   카메라와 영역 보기"));
        cameraModeField = new DropdownField("카메라",new List<string>{"게임 카메라","자유 카메라"},freeCamera?1:0) {name="camera-mode"};
        cameraModeField.RegisterValueChangedCallback(_=>SetCameraMode(cameraModeField.index==1));controls.Add(cameraModeField);
        placeField = new DropdownField("장소", PlaceNames(), previewPlaceIndex) { name = "place" };
        placeField.RegisterValueChangedCallback(_ => Execute(() => ShowPlace(placeField.index))); controls.Add(placeField);
        zoom = new Slider("카메라 거리", 6, 28) { name = "zoom", value = previewDistance, showInputField = true };
        zoom.RegisterValueChangedCallback(e => { previewDistance=e.newValue; if (HasDraft) Execute(() => { rig.distance = e.newValue; ShowPlace(placeField.index); }); }); controls.Add(zoom);
        controls.Add(new Label("기본: 45° · 태양 방향 225° · 거리 20 · FOV 38°") { name = "camera-note" });
        viewField = new DropdownField("영역 표시", new List<string> { "실제 맵", "맵 + 영역 오버레이", "전투지역 · 길만" }, viewMode) { name = "view-mode" };
        viewField.RegisterValueChangedCallback(_ => SetViewMode(viewField.index)); controls.Add(viewField);
        var tileToggle=new Toggle("타일 경계 / 원본 이름") {name="tile-bounds",value=tileBounds};
        tileToggle.RegisterValueChangedCallback(e=>SetTileBounds(e.newValue));controls.Add(tileToggle);
        var speed=new Slider("비행 속도",4,60) {name="fly-speed",value=flySpeed,showInputField=true};
        speed.RegisterValueChangedCallback(e=>flySpeed=e.newValue);controls.Add(speed);
        controls.Add(new Label("자유 카메라: 화면 클릭 → WASD 이동 · Q/E 위아래 · 우클릭 드래그 회전 · 휠 전진/후진 · Shift 빠르게") {name="navigation-note"});
        controls.Add(new Button(()=>Execute(OpenInSceneView)) {text="저장 후 Scene 뷰로 보기",name="scene-view"});
        controls.Add(new Label("‘전체 배치’에서 연결 구조를 보고, 장소를 고르면 같은 게임 카메라로 영역 경계를 볼 수 있어.") { name = "overlay-note" });
        legend = new VisualElement { name="combat-legend" }; controls.Add(legend);
        stats = new Label("시드를 정하고 생성하면 장소별 모습을 볼 수 있어.") { name = "stats" }; controls.Add(stats);
        controls.Add(Section("03   마음에 드는 맵 저장"));
        save = new Button(() => Execute(() => SaveSnapshot())) { text = "새 씬으로 저장", name = "save" }; controls.Add(save);
        open = new Button(() => Execute(OpenSavedScene)) { text = "저장한 씬 열기", name = "open-saved" }; controls.Add(open);
        controls.Add(new Label("미리보기는 별도 씬에서 생성해. 저장한 맵은 Scenes/Generated에 남고, 씬을 열어 Play로 걸어볼 수 있어. 창을 닫으면 저장 전 미리보기는 끝나.") { name = "save-note" });
        var imageArea = new VisualElement(); imageArea.AddToClassList("image-area"); body.Add(imageArea);
        previewViewport=new VisualElement {name="preview-viewport",focusable=true};imageArea.Add(previewViewport);
        preview = new Image { name = "camera-preview", scaleMode = ScaleMode.ScaleToFit, image = frame,pickingMode=PickingMode.Ignore }; previewViewport.Add(preview);
        overlay=new MojaveGameplayOverlayElement();previewViewport.Add(overlay);
        overlay.RegisterCallback<GeometryChangedEvent>(_=>UpdateAreaLabels());
        previewViewport.RegisterCallback<GeometryChangedEvent>(_=>FitOverlay());
        HookNavigation();
        imageArea.Add(new Label("포탈 → 넓은 전투 장소 → 좁은 이동 길 → 다음 장소") { name = "preview-caption" });
        status = new Label { name = "status" }; root.Add(status);
        SetStatus(HasDraft ? "생성된 맵을 보고 있어." : "준비됨 · Play에 들어가지 않고 생성할 수 있어.");
        RefreshControls();
        if (SessionState.GetBool("MojaveTool.UpgradePreview", false))
        {
            previewPlaceIndex=SessionState.GetInt("MojaveTool.UpgradePlace",0);
            previewDistance=SessionState.GetFloat("MojaveTool.UpgradeDistance",20);
            SessionState.EraseBool("MojaveTool.UpgradePreview");restoreDraft=true;
        }
        if(HasDraft) {overlay.Bind(world,camera);FitOverlay();BuildLegend();SetViewMode(viewMode);SetTileBounds(tileBounds);ShowPlace(previewPlaceIndex);}
        else if(restoreDraft) {restoreDeadline=EditorApplication.timeSinceStartup+90;EditorApplication.update-=RestoreAfterReload;EditorApplication.update+=RestoreAfterReload;}
    }

    void RestoreAfterReload()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup>restoreDeadline)
        {EditorApplication.update-=RestoreAfterReload;restoreDraft=false;SetStatus("생성 버튼으로 미리보기를 다시 만들 수 있어.");return;}
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer)return;
        EditorApplication.update-=RestoreAfterReload;restoreDraft=false;
        Execute(()=>GenerateCore(requestedSeed,false));
    }

    static Label Section(string text) { var label = new Label(text); label.AddToClassList("section"); return label; }
    List<string> PlaceNames()
    {
        if(HasDraft){var actual=new List<string>{"포탈 / 출발 지점","전체 배치"};actual.AddRange(world.layout.places.Select(p=>p.name));return actual;}
        return new List<string> { "포탈 / 출발 지점", "전체 배치", "Saguaro Basin", "Dry Wash", "Sunstone Court", "Joshua Hollow", "Redrock Crossing", "High Basin" };
    }
    static int NewSeed(int seed) => unchecked(seed * 1664525 + 1013904223) & int.MaxValue;
    static void Guard()
    {
        MojaveWorldBuilder.Guard();
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Editor 작업이 끝난 뒤 생성할 수 있어.");
    }
    void Execute(Action action)
    {
        if (busy) return;
        busy = true;
        try { action(); }
        catch (Exception e) { SetStatus(e.Message, true); Debug.LogException(e); }
        finally { busy = false; RefreshControls(); }
    }

    public static MojaveBackdropLibrary LoadBackdropLibrary() =>
        AssetDatabase.LoadAssetAtPath<MojaveBackdropLibrary>(MojaveWorldBuilder.BackdropLibraryPath)
        ?? throw new InvalidOperationException("확정된 분리 배경 21종 라이브러리를 찾을 수 없습니다. 삭제예정 자료로 대체하지 않고 현재 라이브러리 연결을 복원해 주세요.");

    public static MojaveLayout BuildLayoutPreview(int seed,int mapSize,int count,bool mixed,bool backdrops,bool expanded,bool compact,out MojaveCatalog catalog,bool randomCombatLayout=false)
    {
        Guard();var scene=EditorSceneManager.NewPreviewScene();GameObject owner=null;
        try {
            owner=new GameObject("Mojave layout preview") {hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(owner,scene);
            var model=owner.AddComponent<MojaveWorld>();var set=owner.AddComponent<MojaveCombatTileSet>();
            model.catalog=AssetDatabase.LoadAssetAtPath<MojaveCatalog>(MojaveWorldBuilder.CatalogPath);
            set.rules=MojaveWorldBuilder.Rules(model.catalog);set.mixedSizes=mixed;
            if(mixed) {var library=AssetDatabase.LoadAssetAtPath<MojaveCombatSizeLibrary>(MojaveWorldBuilder.SizedLibraryPath);model.catalog=library.catalog;set.rules=library.rules;}
            model.seed=seed;model.mapSizeOverride=mapSize;model.combatAreaCount=count;model.expandedMap=expanded;model.compactMap=compact;
            model.randomCombatLayout=randomCombatLayout;
            model.roundedJunctions=backdrops;model.organicConnections=backdrops;model.terrainFinish=backdrops;model.yieldBlockedLargeShoulders=backdrops&&!mixed;
            model.refinedRoads=backdrops;
            model.EnsureLayout();catalog=model.catalog;return model.layout;
        } finally {if(owner!=null)Object.DestroyImmediate(owner);EditorSceneManager.ClosePreviewScene(scene);}
    }
    public void PreviewLayout()
    {
        Guard();if(themeIndex!=2)throw new InvalidOperationException("레이아웃 프리뷰는 다듬은 Mojave에서 선택해.");
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var plan=BuildLayoutPreview(requestedSeed,customMapSize,combatAreaCount,mixedCombatSizes,useBackdrops,expandedMap,compactMap,out var catalog,randomCombatLayout);
        ReleaseDraft();ReleaseFrame();restoreDraft=false;QuickLayout=plan;overlay.BindPlan(plan,catalog);FitOverlay();
        foreach(var place in plan.places) {
            var tier=catalog.patches[place.patch].combatSize;string size=tier==MojaveCombatSize.Large?"대형":tier==MojaveCombatSize.Medium?"중형":"소형";
            var label=new Label($"{areaLabels.Count+1} · {size}"){pickingMode=PickingMode.Ignore};label.AddToClassList("area-label");overlay.Add(label);areaLabels.Add(label);
        }
        QuickLayoutSeconds=clock.Elapsed.TotalSeconds;
        stats.text=$"레이아웃 · {plan.extent:0}m × {plan.extent:0}m\n전투 {plan.places.Count}구역 · 길 {plan.trails.Count}개\n시드 {requestedSeed} · {QuickLayoutSeconds:0.00}초";
        legend.Clear();legend.Add(new Label("색 면: 전투구역 · 연결 띠: 이동 길"));
        SetStatus("레이아웃 확인 중 · 배경 바위와 지형은 전체 맵 생성에서 배치돼.");UpdateAreaLabels();RefreshControls();Repaint();
    }
    public void Generate(int seed) => GenerateCore(seed, true);
    public void Generate(int seed,MojaveBackdropLibrary backdropLibrary) => GenerateCore(seed,true,backdropLibrary);
    void GenerateCore(int seed, bool remember,MojaveBackdropLibrary backdropLibrary=null)
    {
        Guard();
        bool restoreFree=freeCamera&&hasFreePose;var previousFreePosition=freePosition;var previousFreeAngles=freeAngles;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            throw new InvalidOperationException("먼저 해당 지역의 Build expedition 메뉴로 기본 씬을 만들어.");
        var before = AssetDatabase.GetAssetDependencyHash(SourceScenePath);
        var candidate = EditorSceneManager.OpenPreviewScene(SourceScenePath);
        try
        {
            var candidateWorld = Components<MojaveWorld>(candidate).Single();
            candidateWorld.mapSizeOverride=themeIndex==2?customMapSize:0;
            candidateWorld.expandedMap=themeIndex==2&&expandedMap;candidateWorld.compactMap=themeIndex==2&&compactMap;
            candidateWorld.combatAreaCount=themeIndex==2&&expandedMap?combatAreaCount:0;
            candidateWorld.randomCombatLayout=themeIndex==2&&randomCombatLayout;
            candidateWorld.roundedJunctions=themeIndex==2&&useBackdrops;
            candidateWorld.organicConnections=themeIndex==2&&useBackdrops;
            candidateWorld.terrainFinish=themeIndex==2&&useBackdrops;
            candidateWorld.refinedRoads=themeIndex==2&&useBackdrops;
            candidateWorld.playableRelief=themeIndex==2&&useBackdrops;
            candidateWorld.yieldBlockedLargeShoulders=themeIndex==2&&useBackdrops&&!mixedCombatSizes;
            if(themeIndex==2&&useBackdrops) {
                var library=backdropLibrary??LoadBackdropLibrary();
                if(library==null)throw new InvalidOperationException("먼저 Combat Tiles / Cut rock backdrop tiles 메뉴로 배경 타일을 만들어.");
                var backdrops=candidateWorld.GetComponent<MojaveBackdropSet>();if(backdrops==null)backdrops=candidateWorld.gameObject.AddComponent<MojaveBackdropSet>();backdrops.library=library;
                if(library.preserveAuthoredEnvironment)backdrops.fillLibrary=AssetDatabase.LoadAssetAtPath<MojaveBackdropLibrary>(MojaveWorldBuilder.PolishedLibraryPath);
                backdrops.fillGaps=AssetDatabase.GetAssetPath(library)==MojaveWorldBuilder.PolishedLibraryPath;
                if(backdrops.fillLibrary!=null)backdrops.fillGaps=true;
                if(backdrops.fillGaps)backdrops.majorForms=true;
            }
            if(themeIndex==2&&mixedCombatSizes) {
                var library=AssetDatabase.LoadAssetAtPath<MojaveCombatSizeLibrary>(MojaveWorldBuilder.SizedLibraryPath);
                if(library==null)throw new InvalidOperationException("먼저 Combat Tiles / Cut small and medium combat tiles 메뉴로 전투 타일을 만들어.");
                var tileSet=candidateWorld.GetComponent<MojaveCombatTileSet>();
                if(tileSet==null)throw new InvalidOperationException("전투 타일 기준 씬이 필요해.");
                candidateWorld.catalog=library.catalog;tileSet.rules=library.rules;tileSet.mixedSizes=true;
                var backdrop=candidateWorld.GetComponent<MojaveBackdropSet>();if(backdrop!=null)backdrop.majorForms=true;
            }
            candidateWorld.Generate(seed);
            var candidateExplorer = Components<MojaveExplorer>(candidate).Single();
            var candidateRig = Components<MojaveCamera>(candidate).Single();
            var candidatePortal = Components<MojavePortal>(candidate).Single();
            candidateExplorer.world = candidateWorld; candidateExplorer.view = candidateRig; candidateExplorer.portal = candidatePortal;
            candidateExplorer.GetComponent<CharacterController>().enabled = false;
            candidateExplorer.transform.position = candidateWorld.Staging + Vector3.up * .06f;
            candidateExplorer.GetComponent<CharacterController>().enabled = true;
            candidatePortal.Place(candidateWorld);
            foreach (var particles in candidatePortal.GetComponentsInChildren<ParticleSystem>(true)) particles.Simulate(1.2f, false, true, true);
            var candidateCamera = candidateRig.GetComponent<Camera>(); candidateCamera.enabled = false; candidateCamera.scene = candidate;
            candidateRig.target = candidateExplorer.transform; candidateRig.distance = previewDistance; candidateRig.yaw = 225; candidateRig.Snap();
            foreach (var listener in Components<AudioListener>(candidate)) listener.enabled = false;
            if (AssetDatabase.GetAssetDependencyHash(SourceScenePath) != before)
                throw new InvalidOperationException("생성 중 원본 씬 또는 연결 자산이 바뀌었어. 다시 생성해.");
            ReleaseDraft(); draft = candidate; candidate = default;
            world = candidateWorld; explorer = candidateExplorer; rig = candidateRig; camera = candidateCamera; baselineHash = before;
            requestedSeed = seed; seedField?.SetValueWithoutNotify(seed); zoom?.SetValueWithoutNotify(previewDistance);
            previewPlaceIndex=Mathf.Clamp(previewPlaceIndex,0,world.layout.places.Count+1);if(placeField!=null)placeField.choices=PlaceNames();
            if (remember)
            {
                if (historyIndex + 1 < history.Count) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
                history.Add(seed); historyIndex = history.Count - 1;
            }
            overlay?.Bind(world,camera);FitOverlay();BuildLegend();SetViewMode(viewMode);SetTileBounds(tileBounds);
            ShowPlace(previewPlaceIndex); RefreshControls();
            if(restoreFree){freePosition=previousFreePosition;freeAngles=previousFreeAngles;ApplyFreePose();RenderPreview();overlay?.RefreshCamera();UpdateAreaLabels();}
            SetStatus($"생성 완료 · 시드 {seed} · {world.generationMilliseconds / 1000f:0.0}초 · 저장 전 미리보기");
        }
        finally { if (candidate.IsValid()) EditorSceneManager.ClosePreviewScene(candidate); }
    }
    public void RestoreHistory(int offset)
    {
        int index = historyIndex + offset;
        if (index < 0 || index >= history.Count) return;
        GenerateCore(history[index], false); historyIndex = index; RefreshControls();
    }
    public void ShowPlace(int index)
    {
        Guard(); if (!HasDraft) return;
        if (index < 0 || index >= PlaceNames().Count) throw new ArgumentOutOfRangeException(nameof(index));
        previewPlaceIndex=index;
        placeField?.SetValueWithoutNotify(PlaceNames()[index]);
        if (index != 1)
        {
            var cc = explorer.GetComponent<CharacterController>(); cc.enabled = false;
            explorer.transform.position = (index == 0 ? world.Staging : world.Ground(world.layout.places[index - 2].center)) + Vector3.up * .06f;
            cc.enabled = true; rig.Snap();
        }
        else
        {
            camera.transform.SetPositionAndRotation(new Vector3(0, world.MapSize*.703125f, 0), Quaternion.Euler(90, 0, 0));
            camera.orthographic = true; camera.orthographicSize = world.MapSize*.5f+12;
        }
        zoom?.SetEnabled(index != 1);
        if(freeCamera){CaptureFreePose();camera.orthographic=false;ApplyFreePose();}
        Physics.SyncTransforms(); RenderPreview();overlay?.RefreshCamera();UpdateAreaLabels();
        if (stats != null)
        {
            string source = index > 1 ? "\n유형: " + world.layout.places[index - 2].KindLabel + "\n타일: " + world.catalog.patches[world.layout.places[index - 2].patch].size.ToString("0.#")+"m · "+world.catalog.patches[world.layout.places[index - 2].patch].name : "";
            stats.text = $"시드 {world.seed} · {world.MapSize:0}m\n전투 장소 {world.layout.places.Count} · 연결 길 {world.layout.trails.Count}\n원본 배치 {world.authoredPlacementCount:N0} · 추가 군집 {world.dressingCount:N0}" + source;
        }
    }

    public void SetViewMode(int value)
    {
        viewMode=Mathf.Clamp(value,0,2);viewField?.SetValueWithoutNotify(viewField.choices[viewMode]);
        if(preview!=null)preview.style.opacity=viewMode==2?0:1;
        overlay?.SetMode(viewMode);if(legend!=null)legend.style.display=viewMode==0?DisplayStyle.None:DisplayStyle.Flex;
        UpdateAreaLabels();Repaint();
    }
    public void SetTileBounds(bool value)
    {
        tileBounds=value;rootVisualElement.Q<Toggle>("tile-bounds")?.SetValueWithoutNotify(value);overlay?.SetTiles(value);UpdateAreaLabels();
    }
    public void SetCameraMode(bool value)
    {
        freeCamera=value;cameraModeField?.SetValueWithoutNotify(cameraModeField.choices[value?1:0]);StopNavigation();
        if(HasDraft) {
            if(value) {CaptureFreePose();typeof(MojaveCamera).GetMethod("RevealOccluders",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(rig,null);ApplyFreePose();}
            else {hasFreePose=false;ShowPlace(previewPlaceIndex);}
            RenderPreview();overlay?.RefreshCamera();UpdateAreaLabels();
        }
        RefreshControls();
    }
    void CaptureFreePose()
    {
        freePosition=camera.transform.position;var angles=camera.transform.eulerAngles;
        freeAngles=new Vector2(Mathf.Clamp(Mathf.DeltaAngle(0,angles.x),-85,85),angles.y);hasFreePose=true;
    }
    void ApplyFreePose()
    {
        camera.orthographic=false;camera.transform.SetPositionAndRotation(freePosition,Quaternion.Euler(freeAngles.x,freeAngles.y,0));
    }
    public void MoveFreeCamera(Vector3 localMotion,float vertical,Vector2 look)
    {
        if(!HasDraft||!freeCamera)return;
        freeAngles.x=Mathf.Clamp(freeAngles.x+look.y,-85,85);freeAngles.y+=look.x;
        freePosition+=Quaternion.Euler(freeAngles.x,freeAngles.y,0)*localMotion+Vector3.up*vertical;
        float limit=world.MapSize*.5f+92;freePosition.x=Mathf.Clamp(freePosition.x,-limit,limit);freePosition.z=Mathf.Clamp(freePosition.z,-limit,limit);
        float floor=world.Ground(new Vector2(freePosition.x,freePosition.z)).y+.45f;
        freePosition.y=Mathf.Clamp(freePosition.y,floor,280);hasFreePose=true;ApplyFreePose();previewDirty=true;
    }
    void HookNavigation()
    {
        previewViewport.RegisterCallback<PointerDownEvent>(e=> {
            if(!HasDraft)return;previewViewport.Focus();
            if(!freeCamera||!(e.button==1||e.button==2))return;
            capturedPointer=e.pointerId;dragButton=e.button;lastPointer=e.position;previewViewport.CapturePointer(e.pointerId);e.StopPropagation();
        });
        previewViewport.RegisterCallback<PointerMoveEvent>(e=> {
            if(!freeCamera||capturedPointer!=e.pointerId||dragButton<0)return;
            Vector2 now=e.position;var delta=now-lastPointer;lastPointer=now;
            if(dragButton==1)MoveFreeCamera(Vector3.zero,0,delta*.18f);
            else MoveFreeCamera(new Vector3(-delta.x,delta.y,0)*flySpeed*.003f,0,Vector2.zero);
            e.StopPropagation();
        });
        previewViewport.RegisterCallback<PointerUpEvent>(e=> {if(e.pointerId==capturedPointer){StopNavigation();e.StopPropagation();}});
        previewViewport.RegisterCallback<PointerCaptureOutEvent>(_=>{capturedPointer=-1;dragButton=-1;navigationKeys.Clear();});
        previewViewport.RegisterCallback<FocusOutEvent>(_=>StopNavigation());
        previewViewport.RegisterCallback<KeyDownEvent>(e=> {if(freeCamera&&IsNavigationKey(e.keyCode)){navigationKeys.Add(e.keyCode);e.StopPropagation();}});
        previewViewport.RegisterCallback<KeyUpEvent>(e=> {navigationKeys.Remove(e.keyCode);if(freeCamera&&IsNavigationKey(e.keyCode))e.StopPropagation();});
        previewViewport.RegisterCallback<WheelEvent>(e=> {
            if(!HasDraft)return;
            if(freeCamera)MoveFreeCamera(new Vector3(0,0,-e.delta.y*flySpeed*.08f),0,Vector2.zero);
            else if(previewPlaceIndex!=1)zoom.value=Mathf.Clamp(zoom.value+e.delta.y*.4f,6,28);
            e.StopPropagation();
        });
    }
    static bool IsNavigationKey(KeyCode key)=>key==KeyCode.W||key==KeyCode.A||key==KeyCode.S||key==KeyCode.D||key==KeyCode.Q||key==KeyCode.E||key==KeyCode.LeftShift||key==KeyCode.RightShift;
    void StopNavigation()
    {
        navigationKeys.Clear();int pointer=capturedPointer;capturedPointer=-1;dragButton=-1;
        if(pointer>=0&&previewViewport!=null&&previewViewport.HasPointerCapture(pointer))previewViewport.ReleasePointer(pointer);
    }
    void NavigationTick()
    {
        double now=EditorApplication.timeSinceStartup;float dt=Mathf.Min(.05f,(float)(now-lastNavigationTime));lastNavigationTime=now;
        if(!HasDraft||!freeCamera||busy||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(navigationKeys.Count>0) {
            float Key(KeyCode key)=>navigationKeys.Contains(key)?1:0;
            float speed=flySpeed*(Key(KeyCode.LeftShift)+Key(KeyCode.RightShift)>0?3:1)*dt;
            var movement=new Vector3(Key(KeyCode.D)-Key(KeyCode.A),0,Key(KeyCode.W)-Key(KeyCode.S));if(movement.sqrMagnitude>1)movement.Normalize();
            MoveFreeCamera(movement*speed,(Key(KeyCode.E)-Key(KeyCode.Q))*speed,Vector2.zero);
        }
        if(previewDirty&&now-lastRenderTime>1.0/30) {
            previewDirty=false;lastRenderTime=now;RenderPreview();overlay?.RefreshCamera();UpdateAreaLabels();
        }
    }
    public void OpenInSceneView()
    {
        Guard();for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
            throw new InvalidOperationException("현재 씬을 저장한 뒤 Scene 뷰로 열어.");
        SaveSnapshot();OpenSavedScene();MojaveSceneOverlay.SetOptions(true,true);
        var sceneView=SceneView.lastActiveSceneView!=null?SceneView.lastActiveSceneView:GetWindow<SceneView>();
        sceneView.LookAt(world.Ground(world.layout.places[previewPlaceIndex>1?previewPlaceIndex-2:0].center),Quaternion.Euler(55,225,0),45);
        sceneView.Repaint();SetStatus("Scene 뷰에 저장한 맵을 열었어 · 기본 Scene 카메라 조작으로 이동할 수 있어.");
    }
    void FitOverlay()
    {
        if(overlay==null||previewViewport==null)return;
        var r=previewViewport.contentRect;float aspect=QuickLayout!=null?1:1600f/900;float width=Mathf.Min(r.width,r.height*aspect),height=width/aspect;
        overlay.style.left=(r.width-width)*.5f;overlay.style.top=(r.height-height)*.5f;
        overlay.style.right=StyleKeyword.Auto;overlay.style.bottom=StyleKeyword.Auto;overlay.style.width=width;overlay.style.height=height;
        overlay.RefreshCamera();
    }
    void BuildLegend()
    {
        if(legend==null||overlay==null||!HasDraft)return;
        legend.Clear();foreach(var label in areaLabels)label.RemoveFromHierarchy();areaLabels.Clear();
        foreach(var label in tileLabels)label.RemoveFromHierarchy();tileLabels.Clear();
        var road=new Label("●  이동 길");road.style.color=MojaveCombatPalette.Trail;legend.Add(road);
        for(int i=0;i<world.layout.places.Count;i++) {
            var place=world.layout.places[i];var color=MojaveCombatPalette.For(place.kind);
            var item=new Label($"●  {i+1}. {place.KindLabel}");item.style.color=color;legend.Add(item);
            var label=new Label($"{i+1}  {place.KindLabel}") {pickingMode=PickingMode.Ignore};label.AddToClassList("area-label");label.style.color=color;
            overlay.Add(label);areaLabels.Add(label);
            var tileLabel=new Label($"T{i+1}  {world.catalog.patches[place.patch].name}") {pickingMode=PickingMode.Ignore};tileLabel.AddToClassList("tile-label");
            overlay.Add(tileLabel);tileLabels.Add(tileLabel);
        }
    }
    void UpdateAreaLabels()
    {
        if(QuickLayout!=null&&overlay!=null) {for(int i=0;i<areaLabels.Count;i++) {var p=QuickLayout.places[i].center;var q=overlay.Project(new Vector3(p.x,0,p.y));areaLabels[i].style.left=q.x-28;areaLabels[i].style.top=q.y-10;}return;}
        if(camera==null||!HasDraft||overlay==null)return;
        for(int i=0;i<areaLabels.Count;i++) {
            var p=world.Ground(world.layout.places[i].center);var v=camera.WorldToViewportPoint(p);
            bool visible=viewMode!=0&&v.z>camera.nearClipPlane&&v.x>.03f&&v.x<.97f&&v.y>.04f&&v.y<.96f;
            areaLabels[i].style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            var point=overlay.Project(p);areaLabels[i].style.left=point.x-55;areaLabels[i].style.top=point.y-10;
            float half=world.catalog.patches[world.layout.places[i].patch].size*.5f;
            var tilePoint=world.Ground(world.layout.places[i].World(new Vector2(-half,half)),.2f);var tv=camera.WorldToViewportPoint(tilePoint);
            tileLabels[i].style.display=tileBounds&&tv.z>camera.nearClipPlane&&tv.x>.02f&&tv.x<.95f&&tv.y>.02f&&tv.y<.96f?DisplayStyle.Flex:DisplayStyle.None;
            var tp=overlay.Project(tilePoint);tileLabels[i].style.left=tp.x;tileLabels[i].style.top=tp.y;
        }
    }
    void RenderPreview()
    {
        if (frame == null)
        {
            frame = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf) { name = "Mojave tool camera preview", hideFlags = HideFlags.HideAndDontSave };
            frame.Create();
        }
        var target = camera.targetTexture; var active = RenderTexture.active;
        try { camera.targetTexture = frame; camera.Render(); if (preview != null) preview.image = frame; }
        finally { camera.targetTexture = target; RenderTexture.active = active; }
        Repaint();
    }

    public string SaveSnapshot()
    {
        Guard();
        if (!HasDraft) throw new InvalidOperationException("저장할 맵을 먼저 생성해.");
        if (AssetDatabase.GetAssetDependencyHash(SourceScenePath) != baselineHash)
            throw new InvalidOperationException("미리보기 생성 후 원본 또는 연결 자산이 바뀌었어. 새로 생성한 뒤 저장해.");
        Folder(MojaveWorldBuilder.SceneRoot + "/Generated"); Folder(OwnedRoot + "/Data/Generated");
        string suffix=world.compactMap?"_Compact":world.expandedMap?"_Expanded":"";
        if(world.GetComponent<MojaveBackdropSet>()!=null)suffix+="_Backdrops";
        if(world.GetComponent<MojaveBackdropSet>()?.library?.preserveAuthoredEnvironment==true)suffix+="_Natural";
        if(world.GetComponent<MojaveBackdropSet>()?.fillLibrary!=null)suffix+=world.organicConnections?"_OrganicPassages":"_RoundedColonies";
        if(world.GetComponent<MojaveCombatTileSet>()?.mixedSizes==true)suffix+="_MixedSizes";
        if(world.GetComponent<MojaveCombatTileSet>()?.mixedSizes==false)suffix+="_AllLarge";
        if(world.terrainFinish)suffix+="_TerrainFinish";
        if(world.GetComponent<MojaveBackdropSet>()?.library?.tiles.Any(t=>t.placements.Any(a=>a.rockOutline!=null&&a.rockOutline.Length>=3))==true)suffix+="_RockFootprints";
        if(world.mapSizeOverride>0)suffix+="_Size"+world.mapSizeOverride+"m";
        if(world.combatAreaCount>0)suffix+="_"+world.combatAreaCount+"Areas";
        if(world.randomCombatLayout)suffix+="_ScatteredCombat";
        if(world.GetComponent<MojaveBackdropSet>()?.library?.useAllNaturalCandidates==true)suffix+="_SeparatedBackground";
        if(world.refinedRoads)suffix+="_VariedRoads";
        string scenePath = AssetDatabase.GenerateUniqueAssetPath(MojaveWorldBuilder.SceneRoot + $"/Generated/{ThemeName}_{world.seed}{suffix}.unity");
        string terrainPath = AssetDatabase.GenerateUniqueAssetPath(OwnedRoot + $"/Data/Generated/{ThemeName}_{world.seed}{suffix}_Terrain.asset");
        var saved = default(Scene); var oldActive = SceneManager.GetActiveScene();
        GameObject wrapper = null, copy = null;
        var roots = draft.GetRootGameObjects();
        var terrain = new TerrainData { name = ThemeName+" saved terrain · " + world.seed };
        try
        {
            // New TerrainData is persisted before paint. The live draft still owns its transient surface.
            AssetDatabase.CreateAsset(terrain, terrainPath); world.WriteBakedSurface(terrain, false);
            EditorUtility.SetDirty(terrain); AssetDatabase.SaveAssetIfDirty(terrain);
            saved = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(saved);
            wrapper = new GameObject("Mojave snapshot transfer"); SceneManager.MoveGameObjectToScene(wrapper, draft);
            foreach (var root in roots) root.transform.SetParent(wrapper.transform, true);
            // Clone in one operation so links between world, explorer, portal and camera stay internal.
            copy = Object.Instantiate(wrapper); SceneManager.MoveGameObjectToScene(copy, saved);
            foreach (Transform child in copy.transform.Cast<Transform>().ToArray()) child.SetParent(null, true);
            Object.DestroyImmediate(copy); copy = null;
            var savedWorld = Components<MojaveWorld>(saved).Single();
            savedWorld.surface.terrainData = terrain; savedWorld.surface.GetComponent<TerrainCollider>().terrainData = terrain; savedWorld.AdoptBakedTerrain();
            MojaveWorldBuilder.PersistWaterMeshes(savedWorld,OwnedRoot+"/Data/Generated",Path.GetFileNameWithoutExtension(scenePath));
            MojaveTerrainFinish.PersistMeshes(savedWorld,terrainPath);
            var savedExplorer = Components<MojaveExplorer>(saved).Single(); var savedRig = Components<MojaveCamera>(saved).Single();
            savedExplorer.GetComponent<CharacterController>().enabled = false; savedExplorer.transform.position = savedWorld.Staging + Vector3.up * .06f;
            savedExplorer.GetComponent<CharacterController>().enabled = true;
            var savedCamera = savedRig.GetComponent<Camera>(); savedCamera.scene = default; savedCamera.enabled = true;
            savedRig.Snap(); foreach (var listener in Components<AudioListener>(saved)) listener.enabled = true;
            MojaveWorldBuilder.ApplyEnvironment(Components<Light>(saved).First(x => x.type == LightType.Directional),savedWorld.catalog.skybox,savedWorld.catalog.volcano);
            if (savedExplorer.world != savedWorld || savedExplorer.portal.gameObject.scene != saved || savedExplorer.view != savedRig)
                throw new InvalidOperationException("저장 씬 연결 검사에 실패했어.");
            if (AssetDatabase.GetAssetDependencyHash(SourceScenePath) != baselineHash)
                throw new InvalidOperationException("저장 준비 중 원본이 변경되어 저장을 중단했어.");
            if (!EditorSceneManager.SaveScene(saved, scenePath)) throw new IOException("새 씬을 저장하지 못했어.");
            lastSavedPath = scenePath; RefreshControls(); SetStatus("저장 완료 · " + scenePath); return scenePath;
        }
        catch (Exception e) { throw new InvalidOperationException(e.Message + " 준비한 지형 자산은 " + terrainPath + "에 보존했어.", e); }
        finally
        {
            foreach (var root in roots) if (root != null) root.transform.SetParent(null, true);
            if (wrapper != null) Object.DestroyImmediate(wrapper);
            if (copy != null) Object.DestroyImmediate(copy);
            if (oldActive.IsValid() && oldActive.isLoaded) SceneManager.SetActiveScene(oldActive);
            if (saved.IsValid()) EditorSceneManager.CloseScene(saved, true);
        }
    }
    public void OpenSavedScene()
    {
        Guard();
        if (string.IsNullOrEmpty(lastSavedPath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(lastSavedPath) == null) throw new InvalidOperationException("저장한 씬이 없어.");
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty)
            throw new InvalidOperationException("현재 열어 둔 씬을 저장한 뒤 저장 씬을 열어.");
        EditorSceneManager.OpenScene(lastSavedPath, OpenSceneMode.Single);
        SetStatus("저장 씬을 열었어 · Play를 누르면 포탈에서 시작해.");
    }
    static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(true)).ToArray();
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/')); Folder(parent); AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
    void ReleaseDraft()
    {
        QuickLayout=null;overlay?.Bind(null,null);foreach(var label in areaLabels)label.RemoveFromHierarchy();areaLabels.Clear();
        foreach(var label in tileLabels)label.RemoveFromHierarchy();tileLabels.Clear();StopNavigation();
        world = null; explorer = null; rig = null; camera = null;
        if (draft.IsValid()) EditorSceneManager.ClosePreviewScene(draft);
        draft = default;
    }
    void ReleaseFrame()
    {
        if (preview != null) preview.image = null;
        if (frame != null) { frame.Release(); Object.DestroyImmediate(frame); frame = null; }
    }
    void RefreshControls()
    {
        sizeField?.SetEnabled(themeIndex==2);sizeField?.SetValueWithoutNotify(sizeField.choices[compactMap?2:expandedMap?1:0]);
        mapMetresField?.SetEnabled(themeIndex==2);mapMetresField?.SetValueWithoutNotify(customMapSize>0?customMapSize:compactMap?320:expandedMap?384:256);
        combatCountField?.SetEnabled(themeIndex==2&&expandedMap);combatCountField?.SetValueWithoutNotify(combatCountField.choices[combatAreaCount==6?1:combatAreaCount==9?2:combatAreaCount==12?3:combatAreaCount==15?4:0]);
        combatSizeField?.SetEnabled(themeIndex==2);combatSizeField?.SetValueWithoutNotify(mixedCombatSizes);
        randomCombatField?.SetEnabled(themeIndex==2);randomCombatField?.SetValueWithoutNotify(randomCombatLayout);
        backdropField?.SetEnabled(themeIndex==2);backdropField?.SetValueWithoutNotify(useBackdrops);
        rootVisualElement.Q<Button>("layout-preview")?.SetEnabled(themeIndex==2);
        viewField?.SetEnabled(HasDraft);
        save?.SetEnabled(HasDraft); open?.SetEnabled(!string.IsNullOrEmpty(lastSavedPath));
        previous?.SetEnabled(historyIndex > 0); next?.SetEnabled(historyIndex >= 0 && historyIndex + 1 < history.Count);
        placeField?.SetEnabled(HasDraft); zoom?.SetEnabled(HasDraft && !freeCamera && (placeField == null || placeField.index != 1));
    }
    void SetStatus(string text, bool error = false)
    {
        if (status == null) return; status.text = text; status.EnableInClassList("error", error);
    }
}
