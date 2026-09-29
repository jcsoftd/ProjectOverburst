using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed partial class GreatswordElementFxTunerWindow : EditorWindow
{
    private const string SwordPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/Prefabs/PF_GRS01_AzureStarblade_Equipped.prefab";
    private const string ProfilePath = "Assets/ProjectOverburst/Resources/Weapons/GreatswordElementFxProfile.asset";
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PackagePath = "Assets/ThirdParty/06_VFX/KriptoFX Weapon Effects 2/Prefabs/Effects/";
    private static readonly WeaponElement[] Elements =
        { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
    private static readonly string[] ElementNames = { "불", "얼음", "번개", "어둠", "빛" };
    private static readonly string[] ElementKeys = { "fire", "ice", "electric", "dark", "light" };
    private static readonly string[] TuningFields =
    {
        "idleStrength", "bladeThickness", "bladeCenter", "bladeLength",
        "trailScale", "trailCenter", "trailLength", "trailDensity", "trailSpread", "trailParticleSize", "trailParticleLifetime"
    };

    [SerializeField] private WeaponElement selectedElement = WeaponElement.Fire;
    [SerializeField] private float energyPercent = 100f;
    [SerializeField] private float sampleTime = 2.2f;
    [SerializeField] private int viewIndex;
    [SerializeField] private bool showSwing = true;
    [SerializeField] private float swingAngle = 100f;
    [SerializeField] private float swingDuration = .64f;
    [SerializeField] private float swingProgress;
    [SerializeField] private float cameraYaw = 37f;
    [SerializeField] private float cameraPitch = 18f;
    [SerializeField] private float cameraDistance = 3.05f;
    [SerializeField] private Vector3 cameraTarget = new Vector3(0f, .73f, 0f);
    [SerializeField] private Vector3 swordOffset;

    private readonly Dictionary<string, float> draft = new Dictionary<string, float>();
    private readonly Dictionary<string, GameObject> draftSources = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, float> loadedValues = new Dictionary<string, float>();
    private readonly Dictionary<string, GameObject> loadedSources = new Dictionary<string, GameObject>();
    private string loadedHash;
    private bool dirty;
    [SerializeField] private string savedDraft;
    [SerializeField] private string conflictingDraft;
    [Serializable] private sealed class DraftState
    {
        public string hash;
        public string assetPath;
        public bool dirty;
        public List<string> keys = new List<string>(), sourceKeys = new List<string>(), sourcePaths = new List<string>();
        public List<float> values = new List<float>();
    }
    private void PreserveDraft()
    {
        if (draft.Count == 0) return;
        var state = new DraftState { hash = loadedHash, assetPath = ProfilePath, dirty = dirty };
        foreach (var pair in draft) { state.keys.Add(pair.Key); state.values.Add(pair.Value); }
        foreach (var pair in draftSources)
        { state.sourceKeys.Add(pair.Key); state.sourcePaths.Add(AssetDatabase.GetAssetPath(pair.Value)); }
        savedDraft = JsonUtility.ToJson(state);
    }
    private void RestoreDraft(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var state = JsonUtility.FromJson<DraftState>(json);
        if (state == null || !state.dirty) return;
        // A saved profile always wins over a draft based on an older profile version.
        if (state.assetPath != ProfilePath || state.hash != loadedHash)
        {
            conflictingDraft = json;
            return;
        }
        ApplyDraftState(state);
    }

    private void ApplyDraftState(DraftState state)
    {
        for (int i = 0; i < state.keys.Count; i++)
            if (i < state.values.Count && draft.ContainsKey(state.keys[i])) draft[state.keys[i]] = state.values[i];
        for (int i = 0; i < state.sourceKeys.Count; i++)
            if (i < state.sourcePaths.Count && draftSources.ContainsKey(state.sourceKeys[i]))
            {
                string path = state.sourcePaths[i];
                var source = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (string.IsNullOrEmpty(path) || source != null)
                    draftSources[state.sourceKeys[i]] = source;
            }
        dirty = true;
    }

    private void RecoverConflictingDraft()
    {
        var state = JsonUtility.FromJson<DraftState>(conflictingDraft);
        if (state == null) return;
        ApplyDraftState(state);
        conflictingDraft = null;
        PreserveDraft();
        BuildControls();
        UpdateStatus("이전 미저장 값을 불러왔습니다. 프리뷰와 값을 확인한 뒤 저장하세요.");
        QueueRender();
    }
    private bool renderQueued;
    private Scene previewScene;
    private GameObject prefabContents;
    private MeleeWeaponElementFx previewFx;
    private GameObject previewVisual;
    private Quaternion previewBaseRotation;
    private Vector3 previewGripLocal;
    private Vector3 previewTipLocal;
    private Camera previewCamera;
    private VolumeProfile previewProfile;
    private RenderTexture previewTexture;
    private Image previewImage;
    private Label statusLabel;
    private Slider energySlider;
    private Slider swingSlider;
    private Button swingButton;
    private VisualElement controls;
    private bool swingPlaying;
    private double lastPreviewTime;
    private int dragPointer = -1;
    private DragMode dragMode;
    private Vector2 lastPointerPosition;

    private enum DragMode { None, Orbit, Pan, MoveSword, AdjustRange }

    public static event Action PrefabSaved;

    public static bool Supports(WeaponItemData item) => item != null && item.weaponClass == WeaponClass.Greatsword
        && item.weaponRootPrefab != null && item.weaponRootPrefab.GetComponent<MeleeWeaponElementFx>() != null;

    [MenuItem("OVERBURST/무기/대검 원소 효과 조절")]
    public static void Open()
    {
        var window = GetWindow<GreatswordElementFxTunerWindow>();
        window.titleContent = new GUIContent("대검 원소 효과");
        window.minSize = new Vector2(1010, 680);
        window.Show();
    }

    public static void OpenForElement(WeaponElement element)
    {
        Open();
        var window = GetWindow<GreatswordElementFxTunerWindow>();
        if (!OverburstElementRules.IsActive(element)) return;
        window.selectedElement = element;
        if (window.controls == null) return;
        window.BuildControls();
        window.QueueRender();
    }

    public void CreateGUI()
    {
        DestroyPreview();
        rootVisualElement.Clear();
        rootVisualElement.style.backgroundColor = new Color(.09f, .105f, .135f);
        rootVisualElement.style.color = new Color(.9f, .94f, 1f);
        try
        {
            string previousDraft = savedDraft;
            LoadDraft();
            RestoreDraft(previousDraft);
            BuildUI();
            BuildPreview();
            QueueRender();
        }
        catch (Exception error)
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new HelpBox("원소 효과 조절 창을 열 수 없습니다: " + error.Message,
                HelpBoxMessageType.Error));
            DestroyPreview();
        }
    }

    private void OnEnable()
    {
        lastPreviewTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += TickPreview;
    }

    private void OnDisable()
    {
        PreserveDraft();
        EditorApplication.update -= TickPreview;
        EditorApplication.delayCall -= RenderQueued;
        renderQueued = false;
        DestroyPreview();
    }

    private void LoadDraft()
    {
        var profile = AssetDatabase.LoadAssetAtPath<GreatswordElementFxProfile>(ProfilePath);
        if (profile == null) throw new InvalidOperationException("대검 공통 원소 효과 설정을 찾을 수 없습니다.");
        var serialized = new SerializedObject(profile);
        draft.Clear();
        draftSources.Clear();
        foreach (string key in ElementKeys)
        {
            foreach (string field in TuningFields)
            {
                string path = key + "Settings." + field;
                SerializedProperty property = serialized.FindProperty(path);
                if (property == null) throw new InvalidOperationException("공통 설정 필드가 없습니다: " + path);
                draft[path] = property.propertyType == SerializedPropertyType.Integer
                    ? property.intValue : property.floatValue;
            }
            foreach (string suffix in new[] { "BladeAccent", "Trail", "TipTrail" })
            {
                string path = key + suffix;
                SerializedProperty property = serialized.FindProperty(path);
                if (property == null) throw new InvalidOperationException("공통 효과 필드가 없습니다: " + path);
                draftSources[path] = property.objectReferenceValue as GameObject;
            }

        }
        foreach (string path in new[] { "tipTrailLifetime", "tipTrailWidthScale" })
            draft[path] = serialized.FindProperty(path).floatValue;
        loadedValues.Clear();
        foreach (var pair in draft) loadedValues[pair.Key] = pair.Value;
        loadedSources.Clear();
        foreach (var pair in draftSources) loadedSources[pair.Key] = pair.Value;
        loadedHash = FileHash();
        dirty = false;
    }

    private void BuildUI()
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.paddingLeft = 18;
        header.style.paddingRight = 18;
        header.style.paddingTop = 12;
        header.style.paddingBottom = 12;
        header.style.backgroundColor = new Color(.14f, .19f, .26f);
        rootVisualElement.Add(header);
        var title = new Label("대검 원소 효과 조절");
        title.style.fontSize = 19;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.flexGrow = 1;
        header.Add(title);
        header.Add(Action("공통 설정 다시 읽기", Reload));
        header.Add(Action("모든 대검에 저장", Save));

        var body = new VisualElement();
        body.style.flexDirection = FlexDirection.Row;
        body.style.flexGrow = 1;
        rootVisualElement.Add(body);

        var previewPanel = new VisualElement();
        previewPanel.style.flexGrow = 1;
        previewPanel.style.minWidth = 480;
        previewPanel.style.paddingLeft = 16;
        previewPanel.style.paddingTop = 13;
        previewPanel.style.paddingRight = 12;
        previewPanel.style.paddingBottom = 10;
        body.Add(previewPanel);
        previewPanel.Add(Section("대표 대검 프리뷰 · GRS01"));
        BuildRangeToolbar(previewPanel);
        previewImage = new Image { scaleMode = ScaleMode.StretchToFill };
        previewImage.style.flexGrow = 1;
        previewImage.style.minHeight = 380;
        previewImage.style.backgroundColor = new Color(.28f, .30f, .33f);
        previewImage.RegisterCallback<GeometryChangedEvent>(_ => ResizePreview());
        previewImage.RegisterCallback<PointerDownEvent>(OnPreviewPointerDown);
        previewImage.RegisterCallback<PointerMoveEvent>(OnPreviewPointerMove);
        previewImage.RegisterCallback<PointerUpEvent>(OnPreviewPointerUp);
        previewImage.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
        previewImage.RegisterCallback<WheelEvent>(OnPreviewWheel);
        previewPanel.Add(previewImage);
        BuildRangeOverlay();

        var levels = Row();
        levels.style.marginTop = 8;
        previewPanel.Add(levels);
        foreach (int level in new[] { 0, 50, 100 })
        {
            int captured = level;
            levels.Add(Action(level + "%", () => SetEnergy(captured)));
        }
        levels.Add(Action("다시 보기", QueueRender));
        levels.Add(Action("시점 초기화", ResetView));
        var note = new Label("좌클릭 드래그: 검 이동과 트레일 · 우클릭 드래그: 시점 회전 · 휠: 확대/축소 · 휠 버튼 드래그: 시점 이동");
        note.style.whiteSpace = WhiteSpace.Normal;
        note.style.marginTop = 8;
        note.style.color = new Color(.66f, .74f, .84f);
        previewPanel.Add(note);

        var sidebar = new ScrollView();
        sidebar.style.width = 365;
        sidebar.style.minWidth = 320;
        sidebar.style.backgroundColor = new Color(.115f, .135f, .175f);
        sidebar.style.paddingLeft = 14;
        sidebar.style.paddingRight = 14;
        sidebar.style.paddingTop = 12;
        sidebar.style.paddingBottom = 12;
        body.Add(sidebar);
        controls = sidebar;
        BuildControls();

        statusLabel = new Label();
        statusLabel.style.paddingLeft = 18;
        statusLabel.style.paddingTop = 7;
        statusLabel.style.paddingBottom = 7;
        statusLabel.style.backgroundColor = new Color(.12f, .17f, .23f);
        rootVisualElement.Add(statusLabel);
        UpdateStatus();
    }


    private void BuildControls()
    {
        controls.Clear();
        if (!string.IsNullOrEmpty(conflictingDraft))
        {
            controls.Add(new HelpBox("저장된 공통 설정이 이전 미저장 조절값보다 최신입니다. 현재 창에는 저장된 값이 표시됩니다.", HelpBoxMessageType.Warning));
            controls.Add(Action("이전 미저장 값 불러오기", RecoverConflictingDraft));
            controls.Add(Action("이전 미저장 값 버리기", () =>
            {
                conflictingDraft = null;
                BuildControls();
                UpdateStatus();
            }));
        }
        controls.Add(Section("원소별 효과 선택"));
        var element = new DropdownField(new List<string>(ElementNames), ElementIndex());
        element.label = "원소";
        element.RegisterValueChangedCallback(_ =>
        {
            selectedElement = Elements[element.index];
            BuildControls();
            QueueRender();
        });
        controls.Add(element);
        string key = ElementKeys[ElementIndex()];
        AddSource(key + "BladeAccent", "검신 효과 프리팹");
        AddSource(key + "Trail", "트레일 효과 프리팹");
        AddTipSource(key + "TipTrail", "검끝 트레일 프리팹");
        if (selectedElement == WeaponElement.Electric)
            controls.Add(new HelpBox("검신 전체에 연속된 번개와 두 갈래 전격 잔상이 남습니다. 검끝 트레일은 별도로 적용됩니다.", HelpBoxMessageType.Info));
        controls.Add(new HelpBox("트레일은 장착 중 계속 재생합니다. 검신과 같은 효과를 선택하면 한 번만 재생하고, 다른 효과를 선택하면 두 효과를 함께 유지합니다. 위치·크기·입자량·퍼짐은 아래에서 조절할 수 있습니다.", HelpBoxMessageType.Info));
        controls.Add(new HelpBox("검끝 트레일은 공격 중에만 나옵니다. 저장한 선택과 수치는 모든 대검에 적용됩니다.", HelpBoxMessageType.Info));

        energySlider = new Slider("원소 에너지", 0f, 100f) { showInputField = true, value = energyPercent };
        energySlider.RegisterValueChangedCallback(change =>
        {
            energyPercent = change.newValue;
            QueueRender();
        });
        controls.Add(energySlider);
        controls.Add(new HelpBox("검신과 트레일의 가로 크기는 에너지 비율을 따릅니다(0/20/40/60/80/100%). 사이 값도 부드럽게 이어집니다. 불·어둠·빛의 트레일 입자 크기와 방출량도 함께 증가합니다. 검끝 트레일은 아래에서 별도로 선택합니다.", HelpBoxMessageType.Info));
        var time = new Slider("초기 예열 시간 (초)", .5f, 5f) { showInputField = true, value = sampleTime };
        time.RegisterValueChangedCallback(change => { sampleTime = change.newValue; QueueRender(); });
        controls.Add(time);
        var view = new DropdownField(new List<string> { "사선 확대", "정면", "측면", "인게임 시점" },
            Mathf.Clamp(viewIndex, 0, 3));
        view.label = "카메라";
        view.RegisterValueChangedCallback(_ => { viewIndex = view.index; SetCameraPreset(); RenderCamera(); });
        controls.Add(view);

        controls.Add(Section("검신 효과 범위와 강도"));
        AddTuningSlider(key, "bladeCenter", "효과 중심: 검 끝 → 가드", 0f, 1f);
        AddTuningSlider(key, "bladeLength", "검날 길이 배율 (1 = 전체)", .5f, 3f);
        AddTuningSlider(key, "bladeThickness", "검신 기본 가로 크기 (에너지 100%)", .1f, 3f);

        AddTuningSlider(key, "idleStrength", selectedElement == WeaponElement.Fire ||
            selectedElement == WeaponElement.Dark || selectedElement == WeaponElement.Light
            ? "낮은 에너지 입자 강도 보정 (0%는 꺼짐)" : "에너지 0% 강도", .01f, 1f);

        controls.Add(Section("트레일 범위"));
        var swing = new Toggle("휘두름 미리보기") { value = showSwing };
        swing.RegisterValueChangedCallback(change =>
        {
            showSwing = change.newValue;
            swingPlaying = false;
            swingProgress = 0f;
            UpdateSwingUI();
            QueueRender();
        });
        controls.Add(swing);
        var transport = Row();
        swingButton = Action("▶ 휘두르기", ToggleSwing);
        transport.Add(swingButton);
        transport.Add(Action("처음으로", () => SeekSwing(0f)));
        controls.Add(transport);
        swingSlider = new Slider("휘두름 위치", 0f, 1f) { showInputField = true, value = swingProgress };
        swingSlider.RegisterValueChangedCallback(change => SeekSwing(change.newValue));
        controls.Add(swingSlider);
        UpdateSwingUI();
        AddSlider("swingAngle", "미리보기 회전 각도", 10f, 180f, "검을 실제로 회전시켜 파티클 트레일을 만듭니다.");
        AddSlider("swingDuration", "회전 시간 (초)", .1f, 1.5f, "짧을수록 빠르게 휘두릅니다.");
        var trailControls = new VisualElement();
        var mainControls = controls;
        controls.Add(trailControls); controls = trailControls;
        AddTuningSlider(key, "trailCenter", "트레일 부착 위치", 0f, 1f);
        AddTuningSlider(key, "trailScale", "트레일 기본 가로 크기 (에너지 100%)", .1f, 3f);

        AddTuningSlider(key, "trailLength", "트레일 검날 길이 배율", .1f, 3f);

        controls = mainControls;
        controls.Add(new Label("검신 효과와 트레일의 위치·크기를 각각 조절합니다."));
        AddSlider(key + "Settings.trailDensity", "트레일 입자량", .25f, 4f,
            "1 = 원본. 검이 지나간 자리에 남는 월드 입자의 시간·거리당 생성량입니다. 원본 최대 입자 수에 도달하면 더 늘어나지 않습니다.");
        AddSlider(key + "Settings.trailParticleSize", "트레일 입자 크기", .1f, 5f,
            "1 = 원본 크기. 뒤에 남는 월드 입자와 입자에 연결된 선의 굵기만 조절합니다. 검신 효과·발생 범위·수명·퍼짐은 유지합니다.");
        AddSlider(key + "Settings.trailParticleLifetime", "트레일 입자 수명", .1f, 5f,
            "원본 수명 배율입니다. 0.5 = 절반, 1 = 원본, 2 = 두 배. 뒤에 남는 월드 입자에 적용하며 검신 효과의 수명은 유지합니다.");
        AddSlider(key + "Settings.trailSpread", "트레일 퍼짐", 0f, 1f,
            "1 = 원본, 0 = 발생한 위치에 가깝게 유지. 월드 입자의 이동·노이즈·속도 상속을 줄입니다. 검신 효과와 입자 크기는 유지합니다.");
        controls.Add(Section("검끝 트레일 공통 크기"));
        AddSlider("tipTrailLifetime", "검끝 잔상 최대 수명 (초)", .05f, 1f,
            "원본 트레일 수명과 이 값 중 짧은 시간을 사용합니다. 모든 원소와 대검에 적용됩니다.");
        AddSlider("tipTrailWidthScale", "검끝 트레일 폭 배율", .01f, 1f,
            "원본 트레일 폭에 곱합니다. 모든 원소와 대검에 적용됩니다.");
        var wakeButtons = Row();
        wakeButtons.Add(new Button(() => SetWakeTuning(key, 2f, 0f)) { text = "궤적 집중 · 입자 2배" });
        wakeButtons.Add(new Button(() => SetWakeTuning(key, 1f, 1f)) { text = "입자량·퍼짐 원본" });
        controls.Add(wakeButtons);
        controls.Add(new HelpBox("조정값은 저장 전까지 프리뷰에만 반영됩니다. '모든 대검에 저장'을 누르면 공통 설정에 적용됩니다.",
            HelpBoxMessageType.Info));
    }

    private void AddTuningSlider(string elementKey, string field, string label, float minimum, float maximum)
    {
        AddSlider(elementKey + "Settings." + field, label, minimum, maximum,
            "선택한 원소에만 적용됩니다.");
    }

    private void SetWakeTuning(string key, float density, float spread)
    {
        draft[key + "Settings.trailDensity"] = density;
        draft[key + "Settings.trailSpread"] = spread;
        dirty = true;
        BuildControls(); UpdateStatus(); QueueRender();
    }

    private void AddSource(string field, string label)
    {
        var sources = Enumerable.Range(1, 15)
            .Select(i => AssetDatabase.LoadAssetAtPath<GameObject>(PackagePath + "Effect" + i + ".prefab"))
            .Where(prefab => prefab != null && prefab.GetComponentInChildren<ParticleSystem>(true) != null)
            .ToList();
        const string variants = "Assets/ProjectOverburst/03_Features/Weapons/Shared/VFX/WeaponEffects2Variants/";
        foreach (string name in new[] { "Effect1_Fire_NoDistortion", "Effect8_Light", "Effect13_Dark",
                     "Effect10_Dark", "Effect3_Light", "Effect9_Electric_FineWake" })
        {
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(variants + name + ".prefab");
            if (variant != null) sources.Add(variant);
        }
        var names = sources.Select(prefab => prefab.name == "Effect1_Fire_NoDistortion" ? "Effect1 · 불 (왜곡 없음)"
            : prefab.name == "Effect8_Light" ? "Effect8 · 빛 (흰색·금색)"
            : prefab.name == "Effect13_Dark" ? "Effect13 · 어둠 (짙은 보라)"
            : prefab.name == "Effect10_Dark" ? "Effect10 · 어둠 (검은 연기·암적색)"
            : prefab.name == "Effect3_Light" ? "Effect3 · 빛 (검끝 Light 색상)"
            : prefab.name == "Effect9_Electric_FineWake" ? "Effect9 · 번개 (검신 연속 전격 잔상)" : prefab.name).ToList();
        int selected = sources.IndexOf(draftSources[field]);
        if (selected < 0)
        {
            var current = draftSources[field];
            sources.Insert(0, current);
            names.Insert(0, current == null ? "효과 선택" : current.name + " (현재 연결)");
            selected = 0;
        }
        var picker = new DropdownField(label, names, selected) { name = field + "Selector" };
        picker.tooltip = "Weapon Effects 2 기본 효과와 프로젝트 변형을 선택하면 실시간 프리뷰에 반영됩니다.";
        picker.SetEnabled(sources.Any(prefab => prefab != null));
        picker.RegisterValueChangedCallback(_ =>
        {
            GameObject candidate = sources[picker.index];
            if (candidate == null) return;
            draftSources[field] = candidate;
            BuildControls();
            dirty = true;
            UpdateStatus();
            QueueRender();
        });
        controls.Add(picker);
    }

    private void AddTipSource(string field, string label)
    {
        const string folder = "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/";
        var sources = new List<GameObject> { null };
        var names = new List<string> { "없음" };
        foreach (string name in new[] { "Fire", "Ice", "Electric", "Dark", "Sound", "Water", "Void", "Nature", "Earth", "Cosmos" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "VFX_Trail_" + name + ".prefab");
            if (prefab == null) continue;
            sources.Add(prefab);
            names.Add(prefab.name);
        }
        var darkVariant = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/ProjectOverburst/03_Features/Weapons/Shared/VFX/SwordTipTrailVariants/VFX_Trail_Dark_DeepCrimson.prefab");
        if (darkVariant != null)
        {
            sources.Add(darkVariant);
            names.Add("VFX_Trail_Dark · 아주 어두운 붉은색");
        }
        int selected = sources.IndexOf(draftSources[field]);
        if (selected < 0)
        {
            sources.Insert(0, draftSources[field]);
            names.Insert(0, draftSources[field].name + " (현재 연결)");
            selected = 0;
        }
        var picker = new DropdownField(label, names, selected) { name = field + "Selector" };
        picker.RegisterValueChangedCallback(_ =>
        {
            draftSources[field] = sources[picker.index];
            BuildControls();
            dirty = true;
            UpdateStatus();
            QueueRender();
        });
        controls.Add(picker);
    }

    private void AddSlider(string field, string label, float minimum, float maximum, string tooltip)
    {
        var slider = new Slider(label, minimum, maximum)
        { showInputField = true, value = field == "swingAngle" ? swingAngle
            : field == "swingDuration" ? swingDuration : draft[field] };
        slider.name = "tuning-" + field;
        slider.tooltip = tooltip;
        slider.style.marginTop = 4;
        slider.RegisterValueChangedCallback(change =>
        {
            if (field == "swingAngle") swingAngle = change.newValue;
            else if (field == "swingDuration") swingDuration = change.newValue;
            else { draft[field] = change.newValue; dirty = true; }
            UpdateStatus();
            QueueRender();
        });
        controls.Add(slider);
    }

    private static VisualElement Row()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    private static Label Section(string text)
    {
        var title = new Label(text);
        title.style.fontSize = 13;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginTop = 11;
        title.style.marginBottom = 7;
        title.style.color = new Color(.75f, .87f, 1f);
        return title;
    }

    private static Button Action(string text, System.Action action)
    {
        var button = new Button(action) { text = text };
        button.style.marginRight = 6;
        button.style.height = 27;
        return button;
    }

    private int ElementIndex()
    {
        for (int i = 0; i < Elements.Length; i++)
            if (Elements[i] == selectedElement) return i;
        return 0;
    }

    private void SetEnergy(float percent)
    {
        energyPercent = percent;
        energySlider?.SetValueWithoutNotify(percent);
        QueueRender();
    }

    private void UpdateSwingUI()
    {
        if (swingButton != null)
        {
            swingButton.text = swingPlaying ? "Ⅱ 일시정지" : "▶ 휘두르기";
            swingButton.SetEnabled(showSwing);
        }
        swingSlider?.SetEnabled(showSwing);
        swingSlider?.SetValueWithoutNotify(swingProgress);
    }

    private void ToggleSwing()
    {
        if (!showSwing || previewFx == null) return;
        if (swingPlaying)
        {
            swingPlaying = false;
            previewFx.EditorEndTrailPreview();
        }
        else
        {
            SeekSwing(0f);
            ApplySwingPose(0f);
            previewFx.EditorSampleEnergy(sampleTime);
            previewFx.EditorBeginTrailPreview();
            swingPlaying = true;
            lastPreviewTime = EditorApplication.timeSinceStartup;
        }
        UpdateSwingUI();
        RenderCamera();
    }

    private void SeekSwing(float progress)
    {
        if (previewFx == null) return;
        swingPlaying = false;
        swingProgress = Mathf.Clamp01(progress);
        previewFx.EditorClearTrailPreview();
        ApplyPreviewPose(Quaternion.identity);
        if (showSwing && swingProgress > 0f)
        {
            ApplySwingPose(0f);
            // Warm up at the starting pose so a scrub does not leave a teleport trail.
            previewFx.EditorSampleEnergy(sampleTime);
            previewFx.EditorBeginTrailPreview();
            float elapsed = 0f;
            float end = swingProgress * swingDuration;
            while (elapsed < end - .00001f)
            {
                float dt = Mathf.Min(1f / 60f, end - elapsed);
                elapsed += dt;
                ApplySwingPose(elapsed / swingDuration);
                previewFx.EditorAdvanceEnergyPreview(dt);
                previewFx.EditorAdvanceTrailPreview(dt);
            }
            previewFx.EditorEndTrailPreview();
        }
        else previewFx.EditorSampleEnergy(sampleTime);
        UpdateSwingUI();
        RenderCamera();
    }

    private void ApplySwingPose(float progress)
    {
        float angle = Mathf.Lerp(-swingAngle * .5f, swingAngle * .5f,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress)));
        ApplyPreviewPose(Quaternion.AngleAxis(angle, Vector3.forward));
    }

    private void ApplyPreviewPose(Quaternion swingRotation)
    {
        var rotation = swingRotation * previewBaseRotation;
        var gripOffset = rotation * Vector3.Scale(previewGripLocal, previewVisual.transform.localScale);
        previewVisual.transform.SetPositionAndRotation(swordOffset - gripOffset, rotation);
    }

    private void TickPreview()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = Mathf.Clamp((float)(now - lastPreviewTime), 0f, .05f);
        lastPreviewTime = now;
        if (previewFx == null || !previewScene.IsValid() || dt <= 0f) return;
        if (swingPlaying)
        {
            float remaining = Mathf.Min(dt, Mathf.Max(0f, swingDuration * (1f - swingProgress)));
            while (remaining > .00001f)
            {
                float step = Mathf.Min(remaining, 1f / 60f);
                swingProgress = Mathf.Min(1f, swingProgress + step / swingDuration);
                ApplySwingPose(swingProgress);
                previewFx.EditorAdvanceEnergyPreview(step);
                previewFx.EditorAdvanceTrailPreview(step);
                remaining -= step;
            }
            if (swingProgress >= 1f - .00001f)
            {
                swingProgress = 1f;
                swingPlaying = false;
                previewFx.EditorEndTrailPreview();
            }
            UpdateSwingUI();
        }
        else
        {
            previewFx.EditorAdvanceEnergyPreview(dt);
            previewFx.EditorAdvanceTrailPreview(dt);
        }
        RenderCamera();
    }

    private void OnPreviewPointerDown(PointerDownEvent evt)
    {
        if (previewFx == null || dragMode != DragMode.None || (evt.button != 0 && evt.button != 1 && evt.button != 2)) return;
        if (TryBeginRangeDrag(evt)) return;
        dragMode = evt.button == 1 || (evt.button == 0 && evt.altKey) ? DragMode.Orbit
            : evt.button == 0 ? DragMode.MoveSword : DragMode.Pan;
        if (dragMode == DragMode.MoveSword)
        {
            swingPlaying = false;
            UpdateSwingUI();
        }
        if (dragMode == DragMode.MoveSword && showSwing) previewFx.EditorBeginTrailPreview();
        dragPointer = evt.pointerId;
        lastPointerPosition = evt.position;
        previewImage.CapturePointer(dragPointer);
        evt.StopPropagation();
    }

    private void OnPreviewPointerMove(PointerMoveEvent evt)
    {
        if (previewFx == null || dragMode == DragMode.None || evt.pointerId != dragPointer) return;
        if (dragMode == DragMode.AdjustRange)
        { UpdateRangeDrag(previewImage.WorldToLocal(evt.position)); evt.StopPropagation(); return; }
        Vector2 delta = (Vector2)evt.position - lastPointerPosition;
        lastPointerPosition = evt.position;
        if (delta.sqrMagnitude < .001f) return;
        if (dragMode == DragMode.Orbit)
        {
            cameraYaw += delta.x * .35f;
            cameraPitch = Mathf.Clamp(cameraPitch - delta.y * .35f, -75f, 75f);
        }
        else
        {
            float unitsPerPixel = cameraDistance * .002f;
            Vector3 translation = (-previewCamera.transform.right * delta.x
                + previewCamera.transform.up * delta.y) * unitsPerPixel;
            if (dragMode == DragMode.Pan) cameraTarget += translation;
            else
            {
                swordOffset -= translation;
                previewVisual.transform.position -= translation;
            }
        }
        RenderCamera();
        evt.StopPropagation();
    }

    private void OnPreviewPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != dragPointer) return;
        EndDrag();
        evt.StopPropagation();
    }

    private void EndDrag()
    {
        if (dragMode == DragMode.MoveSword && previewFx != null)
            previewFx.EditorEndTrailPreview();
        dragMode = DragMode.None;
        if (previewImage != null && dragPointer >= 0 && previewImage.HasPointerCapture(dragPointer))
            previewImage.ReleasePointer(dragPointer);
        dragPointer = -1;
    }

    private void OnPreviewWheel(WheelEvent evt)
    {
        if (previewFx == null) return;
        cameraDistance = Mathf.Clamp(cameraDistance * Mathf.Exp(Mathf.Clamp(evt.delta.y, -5f, 5f) * .04f),
            viewIndex == 3 ? 6f : 1.1f, viewIndex == 3 ? 28f : 8f);
        RenderCamera();
        evt.StopPropagation();
    }

    private void SetCameraPreset()
    {
        cameraYaw = viewIndex == 3 ? 225f : viewIndex == 1 ? 0f : viewIndex == 2 ? 90f : 37f;
        cameraPitch = viewIndex == 3 ? 45f : 18f;
        float length = previewVisual != null
            ? Vector3.Scale(previewTipLocal - previewGripLocal, previewVisual.transform.localScale).magnitude : 1.6f;
        cameraDistance = viewIndex == 3 ? 20f : Mathf.Max(3.8f, length * 3.4f);
        cameraTarget = swordOffset + Vector3.up * (length * .45f);
    }

    private void ResetView()
    {
        swordOffset = Vector3.zero;
        SetCameraPreset();
        SeekSwing(swingProgress);
    }

    private void Reload()
    {
        LoadDraft();
        PreserveDraft();
        BuildControls();
        UpdateStatus("대검 공통 설정을 다시 불러왔습니다.");
        QueueRender();
    }

    private void Save()
    {
        bool saved = false;
        try
        {
            var profile = AssetDatabase.LoadAssetAtPath<GreatswordElementFxProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("대검 공통 원소 효과 설정이 없습니다.");
            var serialized = new SerializedObject(profile);
            serialized.Update();
            var conflicts = new List<string>();
            foreach (var item in draft)
            {
                var property = serialized.FindProperty(item.Key);
                if (property == null) throw new InvalidOperationException("필드가 사라졌습니다: " + item.Key);
                float baseline = loadedValues[item.Key];
                if (Mathf.Abs(item.Value - baseline) < .000001f) continue;
                float current = property.propertyType == SerializedPropertyType.Integer
                    ? property.intValue : property.floatValue;
                if (Mathf.Abs(current - baseline) >= .000001f &&
                    Mathf.Abs(current - item.Value) >= .000001f)
                    conflicts.Add(item.Key);
            }
            foreach (var item in draftSources)
            {
                var property = serialized.FindProperty(item.Key);
                if (property == null) throw new InvalidOperationException("효과 필드가 사라졌습니다: " + item.Key);
                if (item.Value == loadedSources[item.Key]) continue;
                var current = property.objectReferenceValue as GameObject;
                if (current != loadedSources[item.Key] && current != item.Value)
                    conflicts.Add(item.Key);
            }
            if (conflicts.Count > 0)
            {
                dirty = true;
                UpdateStatus("다른 변경과 겹친 항목: " + string.Join(", ", conflicts) +
                    " · 현재 값 확인 후 다시 조절해 주세요.");
                return;
            }
            foreach (var item in draft)
            {
                if (Mathf.Abs(item.Value - loadedValues[item.Key]) < .000001f) continue;
                var property = serialized.FindProperty(item.Key);
                if (property.propertyType == SerializedPropertyType.Integer)
                    property.intValue = Mathf.RoundToInt(item.Value);
                else property.floatValue = item.Value;
            }
            foreach (var item in draftSources)
            {
                if (item.Value != loadedSources[item.Key])
                    serialized.FindProperty(item.Key).objectReferenceValue = item.Value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(profile);
            LoadDraft();
            PreserveDraft();
            saved = true;
            UpdateStatus("모든 대검의 공통 원소 효과 설정을 저장했습니다.");
        }
        catch (Exception error)
        {
            UpdateStatus("저장 실패: " + error.Message);
        }
        if (saved)
        {
            try { PrefabSaved?.Invoke(); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }

    private void UpdateStatus(string message = null)
    {
        if (statusLabel == null) return;
        statusLabel.text = message ?? (dirty ? "미저장 조정값 · 프리뷰에서 확인 중" : "대검 공통 설정과 일치 · 0/50/100을 비교할 수 있습니다");
        statusLabel.style.color = dirty ? new Color(1f, .82f, .55f) : new Color(.7f, .85f, .95f);
    }

    private static string FileHash()
    {
        string diskPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ProfilePath));
        using (var stream = File.OpenRead(diskPath))
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }

    private void BuildPreview()
    {
        prefabContents = PrefabUtility.LoadPrefabContents(SwordPath);
        previewScene = EditorSceneManager.NewPreviewScene();
        var source = new SerializedObject(prefabContents.GetComponent<MeleeWeaponElementFx>());
        var original = source.FindProperty("bladeRenderer").objectReferenceValue as MeshRenderer;
        if (original == null) throw new InvalidOperationException("검날 렌더러가 없습니다.");

        previewVisual = new GameObject("Greatsword visual");
        SceneManager.MoveGameObjectToScene(previewVisual, previewScene);
        previewVisual.AddComponent<MeshFilter>().sharedMesh = original.GetComponent<MeshFilter>().sharedMesh;
        var renderer = previewVisual.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = original.sharedMaterials;
        var grip = prefabContents.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "RightHandGripPoint");
        var tip = prefabContents.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "WeaponTip");
        if (grip == null || tip == null) throw new InvalidOperationException("무기의 손잡이/검 끝 기준점이 없습니다.");
        previewGripLocal = original.transform.InverseTransformPoint(grip.position);
        previewTipLocal = original.transform.InverseTransformPoint(tip.position);
        previewBaseRotation = Quaternion.FromToRotation(tip.position - grip.position, Vector3.up)
            * original.transform.rotation;
        previewVisual.transform.localScale = original.transform.lossyScale;
        ApplyPreviewPose(Quaternion.identity);

        previewFx = previewVisual.AddComponent<MeleeWeaponElementFx>();
        previewFx.enabled = false;
        EditorUtility.CopySerialized(prefabContents.GetComponent<MeleeWeaponElementFx>(), previewFx);
        previewFx.enabled = false;
        previewFx.EditorUseLocalFxSettings();
        Set(previewFx, "bladeRenderer", renderer);
        // The copied prefab's WeaponTip belongs to a different preview scene.
        var previewTip = new GameObject("WeaponTip").transform;
        previewTip.SetParent(previewVisual.transform, false);
        previewTip.localPosition = previewTipLocal;
        Set(previewFx, "trailAnchor", previewTip);

        var cameraObject = new GameObject("Preview camera");
        SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.scene = previewScene;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(.28f, .30f, .33f);
        previewCamera.fieldOfView = 35;
        previewCamera.nearClipPlane = .01f;
        previewCamera.farClipPlane = 100;
        previewCamera.allowHDR = true;
        var cameraData = previewCamera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.requiresColorOption = CameraOverrideOption.On;
        cameraData.requiresDepthOption = CameraOverrideOption.On;
        cameraData.volumeLayerMask = 1 << 31;

        var lightObject = new GameObject("Preview light");
        SceneManager.MoveGameObjectToScene(lightObject, previewScene);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2f;
        light.color = new Color(.9f, .94f, 1f);
        lightObject.transform.rotation = Quaternion.Euler(35, -45, 0);

        var volumeObject = new GameObject("Preview bloom");
        volumeObject.layer = 31;
        SceneManager.MoveGameObjectToScene(volumeObject, previewScene);
        var volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000;
        previewProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.profile = previewProfile;
        var bloom = previewProfile.Add<Bloom>(true);
        bloom.threshold.Override(.7f);
        bloom.intensity.Override(1.2f);
        bloom.scatter.Override(.65f);

        SetCameraPreset();
        ResizePreview();
    }

    private void ResizePreview()
    {
        if (previewCamera == null || previewImage == null) return;
        int width = Mathf.Clamp(Mathf.RoundToInt(previewImage.contentRect.width), 320, 1280);
        int height = Mathf.Clamp(Mathf.RoundToInt(previewImage.contentRect.height), 320, 1280);
        if (previewTexture != null && previewTexture.width == width && previewTexture.height == height) return;
        if (previewTexture != null)
        {
            previewTexture.Release();
            DestroyImmediate(previewTexture);
        }
        previewTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        { hideFlags = HideFlags.HideAndDontSave, name = "Greatsword FX live viewport" };
        previewTexture.Create();
        previewImage.image = previewTexture;
        QueueRender();
    }

    private void QueueRender()
    {
        if (renderQueued || previewFx == null) return;
        renderQueued = true;
        EditorApplication.delayCall += RenderQueued;
    }

    private void RenderQueued()
    {
        EditorApplication.delayCall -= RenderQueued;
        renderQueued = false;
        if (previewFx == null || !previewScene.IsValid()) return;
        try { RenderPreview(); }
        catch (Exception error) { UpdateStatus("프리뷰 실패: " + error.Message); }
    }

    private void RenderPreview()
    {
        swingPlaying = false;
        previewFx.EditorClearEnergyPreview();
        ApplyPreviewPose(Quaternion.identity);
        var serialized = new SerializedObject(previewFx);
        foreach (var item in draft)
        {
            var property = serialized.FindProperty(item.Key);
            if (property == null) throw new InvalidOperationException("프리뷰 설정이 없습니다: " + item.Key);
            if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = Mathf.RoundToInt(item.Value);
            else property.floatValue = item.Value;
        }
        foreach (var item in draftSources)
        {
            var property = serialized.FindProperty(item.Key);
            if (property == null) throw new InvalidOperationException("프리뷰 효과가 없습니다: " + item.Key);
            property.objectReferenceValue = item.Value;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        previewFx.EditorPreviewEnergy(selectedElement, Mathf.Clamp01(energyPercent / 100f));
        if (showSwing && swingProgress > 0f) SeekSwing(swingProgress);
        else { previewFx.EditorSampleEnergy(sampleTime); RenderCamera(); }
        UpdateSwingUI();
    }

    private void RenderCamera()
    {
        if (previewCamera == null || previewTexture == null) return;
        Quaternion orbit = Quaternion.Euler(-cameraPitch, cameraYaw, 0f);
        previewCamera.transform.position = cameraTarget + orbit * (Vector3.forward * cameraDistance);
        previewCamera.transform.LookAt(cameraTarget);
        previewCamera.orthographic = viewIndex == 3;
        previewCamera.fieldOfView = viewIndex == 3 ? 38f : 35f;
        if (previewCamera.orthographic)
            previewCamera.orthographicSize = cameraDistance *
                Mathf.Tan(previewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
        previewCamera.aspect = (float)previewTexture.width / previewTexture.height;
        try
        {
            previewCamera.targetTexture = previewTexture;
            previewCamera.Render();
            previewImage.MarkDirtyRepaint();
            rangeOverlay?.MarkDirtyRepaint();
        }
        finally { previewCamera.targetTexture = null; }
    }

    private void DestroyPreview()
    {
        if (previewFx != null)
        {
            previewFx.EditorClearEnergyPreview();
        }
        previewFx = null;
        previewVisual = null;
        previewCamera = null;
        if (previewImage != null) previewImage.image = null;
        previewImage = null;
        if (previewTexture != null) { previewTexture.Release(); DestroyImmediate(previewTexture); }
        previewTexture = null;
        swingPlaying = false;
        dragMode = DragMode.None;
        dragPointer = -1;
        if (previewProfile != null) DestroyImmediate(previewProfile);
        previewProfile = null;
        if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
        previewScene = default;
        if (prefabContents != null) PrefabUtility.UnloadPrefabContents(prefabContents);
        prefabContents = null;
    }

    private static void Set(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, Private);
        if (info == null) throw new MissingFieldException(target.GetType().Name, field);
        info.SetValue(target, value);
    }

    private static void Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, Private);
        if (info == null) throw new MissingMethodException(target.GetType().Name, method);
        info.Invoke(target, args);
    }
}
