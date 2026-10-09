using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>A removable MainScene lighting study; the original night is captured once at installation.</summary>
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(-1000)]
public sealed class MainTownAtmosphereTrial : MonoBehaviour
{
    public enum Atmosphere { OriginalNight, OvercastDay }

    [Serializable]
    public sealed class EnvironmentState
    {
        public Quaternion sunRotation;
        public Color sunColor;
        public float sunIntensity, shadowStrength;
        public AmbientMode ambientMode;
        public Color ambientSky, ambientEquator, ambientGround;
        public float ambientIntensity, reflectionIntensity;
        public bool fog;
        public FogMode fogMode;
        public Color fogColor;
        public float fogDensity, fogStart, fogEnd;
        public Material skybox;
        public VolumeProfile volumeProfile;

        public static EnvironmentState Capture(Light light, Volume volume) => new EnvironmentState
        {
            sunRotation = light.transform.localRotation,
            sunColor = light.color,
            sunIntensity = light.intensity,
            shadowStrength = light.shadowStrength,
            ambientMode = RenderSettings.ambientMode,
            ambientSky = RenderSettings.ambientSkyColor,
            ambientEquator = RenderSettings.ambientEquatorColor,
            ambientGround = RenderSettings.ambientGroundColor,
            ambientIntensity = RenderSettings.ambientIntensity,
            reflectionIntensity = RenderSettings.reflectionIntensity,
            fog = RenderSettings.fog,
            fogMode = RenderSettings.fogMode,
            fogColor = RenderSettings.fogColor,
            fogDensity = RenderSettings.fogDensity,
            fogStart = RenderSettings.fogStartDistance,
            fogEnd = RenderSettings.fogEndDistance,
            skybox = RenderSettings.skybox,
            volumeProfile = volume.sharedProfile
        };

        public void Apply(Light light, Volume volume)
        {
            light.transform.localRotation = sunRotation;
            light.color = sunColor;
            light.intensity = sunIntensity;
            light.shadowStrength = shadowStrength;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.skybox = skybox;
            volume.sharedProfile = volumeProfile;
        }
    }

    [SerializeField] private Atmosphere atmosphere = Atmosphere.OvercastDay;
    [SerializeField] private Light sun;
    [SerializeField] private Volume globalVolume;
    [SerializeField] private EnvironmentState overcastDay;
    [SerializeField, HideInInspector] private EnvironmentState originalNight;
    private bool refreshRequested;
    private GameObject temporaryToggleCanvas;
    private UnityEngine.UI.Button temporaryToggle;
    private TMPro.TMP_Text temporaryToggleLabel;

    public Atmosphere Current => atmosphere;
    public EnvironmentState OriginalNight => originalNight;
    public EnvironmentState OvercastDay => overcastDay;
    public Light Sun => sun;
    public Volume GlobalVolume => globalVolume;

    public void Initialize(Light light, Volume volume, EnvironmentState night, EnvironmentState day)
    {
        if (light == null || volume == null || night == null || day == null)
            throw new ArgumentException("The trial requires both environments and scene lighting references.");
        if (light.gameObject.scene != gameObject.scene || volume.gameObject.scene != gameObject.scene)
            throw new ArgumentException("The trial only controls lighting in its own scene.");
        sun = light;
        globalVolume = volume;
        originalNight = night;
        overcastDay = day;
        ApplyCurrent();
    }

    private bool CanApply => originalNight != null && overcastDay != null && sun != null
        && globalVolume != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded
        && SceneManager.GetActiveScene() == gameObject.scene;

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += ActiveSceneChanged;
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.activeSceneChangedInEditMode += ActiveSceneChanged;
#endif
        if (Application.isPlaying) CreateTemporaryToggle();
        ApplyCurrent();
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= ActiveSceneChanged;
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.activeSceneChangedInEditMode -= ActiveSceneChanged;
#endif
        GameplayInputBlocker.Unblock(this);
        if (temporaryToggleCanvas != null)
        {
            temporaryToggleCanvas.SetActive(false);
            if (Application.isPlaying) Destroy(temporaryToggleCanvas);
            else DestroyImmediate(temporaryToggleCanvas);
        }
        temporaryToggleCanvas = null;
        temporaryToggle = null;
        temporaryToggleLabel = null;
        if (CanApply) originalNight.Apply(sun, globalVolume);
    }

    private void OnValidate() => refreshRequested = true;

    private void Update()
    {
        if (Application.isPlaying && temporaryToggleCanvas != null)
        {
            temporaryToggleCanvas.SetActive(CanApply && !OverburstGameMenu.IsOpen);
            var mouse = UnityEngine.InputSystem.Mouse.current;
            GameplayInputBlocker.SetBlocked(this, temporaryToggleCanvas.activeInHierarchy && mouse != null
                && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)temporaryToggle.transform, mouse.position.ReadValue(), null));
        }
        if (!refreshRequested) return;
        refreshRequested = false;
        ApplyCurrent();
    }

    private void ActiveSceneChanged(Scene previous, Scene current) => ApplyCurrent();

    public void ApplyCurrent()
    {
        if (!isActiveAndEnabled || !CanApply) return;
        (atmosphere == Atmosphere.OvercastDay ? overcastDay : originalNight).Apply(sun, globalVolume);
        if (temporaryToggleLabel != null)
            temporaryToggleLabel.text = atmosphere == Atmosphere.OvercastDay ? "흐린 낮 · 밤으로" : "밤 · 낮으로";
    }

    public void SetAtmosphere(Atmosphere value)
    {
        atmosphere = value;
        ApplyCurrent();
    }

    [ContextMenu("Show Overcast Day")]
    public void ShowOvercastDay() => SetAtmosphere(Atmosphere.OvercastDay);

    [ContextMenu("Restore Original Night")]
    public void RestoreOriginalNight() => SetAtmosphere(Atmosphere.OriginalNight);

    private void CreateTemporaryToggle()
    {
        var font = Resources.Load<TMPro.TMP_FontAsset>("UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body");
        if (font == null) throw new InvalidOperationException("The temporary atmosphere toggle requires the existing UI body font.");
        temporaryToggleCanvas = new GameObject("Temporary Atmosphere Toggle", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        temporaryToggleCanvas.layer = 5;
        temporaryToggleCanvas.transform.SetParent(transform, false);
        var canvas = temporaryToggleCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = temporaryToggleCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        temporaryToggle = RunUiLayout.Button(temporaryToggleCanvas.transform, "Day Night Toggle", "", font, null,
            0, 0, 194, 38, () => SetAtmosphere(atmosphere == Atmosphere.OvercastDay ? Atmosphere.OriginalNight : Atmosphere.OvercastDay));
        var rect = (RectTransform)temporaryToggle.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-24, -442);
        temporaryToggle.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        temporaryToggle.targetGraphic.color = new Color(.09f, .12f, .14f, .94f);
        temporaryToggleLabel = temporaryToggle.GetComponentInChildren<TMPro.TMP_Text>();
        temporaryToggleLabel.fontSize = 17;
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(MainTownAtmosphereTrial))]
public sealed class MainTownAtmosphereTrialInspector : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        UnityEditor.EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (UnityEditor.EditorGUI.EndChangeCheck()) ((MainTownAtmosphereTrial)target).ApplyCurrent();
        GUILayout.Space(6);
        if (GUILayout.Button("흐린 낮으로 전환")) Switch(MainTownAtmosphereTrial.Atmosphere.OvercastDay);
        if (GUILayout.Button("기존 밤으로 전환")) Switch(MainTownAtmosphereTrial.Atmosphere.OriginalNight);
        using (new UnityEditor.EditorGUI.DisabledScope(Application.isPlaying))
            if (GUILayout.Button("밤 복원 후 시안 오브젝트 제거")) RemoveFromScene((MainTownAtmosphereTrial)target);
    }

    private void Switch(MainTownAtmosphereTrial.Atmosphere value)
    {
        var trial = (MainTownAtmosphereTrial)target;
        Record(trial);
        trial.SetAtmosphere(value);
        Dirty(trial);
    }

    private static void Record(MainTownAtmosphereTrial trial)
    {
        UnityEditor.Undo.RecordObjects(new UnityEngine.Object[] {
            trial, trial.Sun, trial.Sun.transform, trial.GlobalVolume,
            UnityEditor.Unsupported.GetRenderSettings()
        }, "Switch main town atmosphere trial");
    }

    private static void Dirty(MainTownAtmosphereTrial trial)
    {
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(trial.gameObject.scene);
        UnityEditor.SceneView.RepaintAll();
    }

    public static void RemoveFromScene(MainTownAtmosphereTrial trial)
    {
        if (trial == null || Application.isPlaying || UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        Record(trial);
        trial.RestoreOriginalNight();
        Dirty(trial);
        // Only a scene object is removed; asset files are retained for a later Recycle Bin operation.
        UnityEditor.Undo.DestroyObjectImmediate(trial.gameObject);
    }
}
#endif

