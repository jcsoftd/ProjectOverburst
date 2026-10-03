using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Play 중 약한 카메라/캐릭터 모션블러를 비교하는 임시 버튼.</summary>
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class OverburstMotionBlurPreview : MonoBehaviour
{
    public const string ResourcePath = "Debug/PF_OverburstMotionBlurToggle";
    public const float PreviewIntensity = .01f;
    public const float PreviewClamp = .003f;
    [SerializeField] private Button toggleButton;
    [SerializeField] private Button decreaseButton;
    [SerializeField] private Button increaseButton;
    [SerializeField] private TMP_Text caption;
    [SerializeField] private TMP_Text intensityCaption;
    private static OverburstMotionBlurPreview instance;
    private Volume previewVolume;
    private VolumeProfile ownedProfile;
    private MotionBlur motionBlur;
    private Camera targetCamera;
    private bool desiredEnabled = true;
    private int intensityHundredths = 1;
    private float nextCameraSearch;

    public bool IsEnabled => desiredEnabled && isActiveAndEnabled;
    public Button ToggleButton => toggleButton;
    public Button DecreaseButton => decreaseButton;
    public Button IncreaseButton => increaseButton;
    public TMP_Text Caption => caption;
    public TMP_Text IntensityCaption => intensityCaption;
    public float SelectedIntensity => intensityHundredths / 100f;
    public Camera TargetCamera => targetCamera;
    public Volume PreviewVolume => previewVolume;
    public MotionBlur Settings => motionBlur;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (instance != null) return;
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab != null) Instantiate(prefab);
#endif
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        ownedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        ownedProfile.name = "Temporary OVERBURST Motion Blur";
        ownedProfile.hideFlags = HideFlags.DontSave;
        motionBlur = ownedProfile.Add<MotionBlur>(true);
        motionBlur.mode.Override(MotionBlurMode.CameraAndObjects);
        motionBlur.quality.Override(MotionBlurQuality.Low);
        motionBlur.clamp.Override(PreviewClamp);
        var volumeObject = new GameObject("MotionBlurPreviewVolume", typeof(Volume));
        volumeObject.transform.SetParent(transform, false);
        previewVolume = volumeObject.GetComponent<Volume>();
        previewVolume.isGlobal = true;
        previewVolume.priority = 10000f;
        previewVolume.sharedProfile = ownedProfile;
        BindCamera();
        if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
        if (decreaseButton != null) decreaseButton.onClick.AddListener(DecreaseIntensity);
        if (increaseButton != null) increaseButton.onClick.AddListener(IncreaseIntensity);
        ApplyState();
    }

    private void BindCamera()
    {
        var quarterView = FindFirstObjectByType<QuarterViewCamera>();
        targetCamera = quarterView != null ? quarterView.GetComponent<Camera>() : Camera.main;
        if (targetCamera == null || previewVolume == null) return;
        var data = targetCamera.GetComponent<UniversalAdditionalCameraData>();
        int mask = data != null ? data.volumeLayerMask.value : 1;
        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) == 0) continue;
            previewVolume.gameObject.layer = layer;
            break;
        }
    }

    private void Update()
    {
        if (targetCamera == null && Time.unscaledTime >= nextCameraSearch)
        {
            nextCameraSearch = Time.unscaledTime + 1f;
            BindCamera();
        }
        bool overButton = IsPointerOver(toggleButton) || IsPointerOver(decreaseButton) || IsPointerOver(increaseButton);
        GameplayInputBlocker.SetBlocked(this, overButton);
    }

    private static bool IsPointerOver(Button button) => button != null && button.gameObject.activeInHierarchy &&
        Mouse.current != null && RectTransformUtility.RectangleContainsScreenPoint(
            (RectTransform)button.transform, Mouse.current.position.ReadValue(), null);

    public void Toggle() => SetEnabled(!desiredEnabled);
    public void DecreaseIntensity() => AdjustIntensity(-1);
    public void IncreaseIntensity() => AdjustIntensity(1);

    private void AdjustIntensity(int steps)
    {
        // 정수 단위로 보관해 반복 클릭에도 0.01 간격과 표시 값이 일치한다.
        intensityHundredths = Mathf.Clamp(intensityHundredths + steps, 0, 100);
        ApplyState();
    }

    public void SetEnabled(bool value)
    {
        desiredEnabled = value;
        ApplyState();
    }

    private void ApplyState()
    {
        // 꺼짐도 intensity=0으로 명시해 맵 프로필의 다른 블러 값이 되살아나지 않게 한다.
        if (motionBlur != null) motionBlur.intensity.Override(IsEnabled ? SelectedIntensity : 0f);
        if (previewVolume != null) previewVolume.enabled = isActiveAndEnabled;
        if (caption != null) caption.text = IsEnabled ? "모션블러: 켜짐" : "모션블러: 꺼짐";
        if (intensityCaption != null) intensityCaption.text = "강도 " + SelectedIntensity.ToString("F2", CultureInfo.InvariantCulture);
        if (toggleButton != null && toggleButton.targetGraphic != null)
            toggleButton.targetGraphic.color = IsEnabled
                ? new Color(.20f, .31f, .27f, .94f) : new Color(.16f, .17f, .19f, .94f);
    }

    private void OnEnable() { if (instance == this) ApplyState(); }

    private void OnDisable()
    {
        GameplayInputBlocker.Unblock(this);
        if (previewVolume != null) previewVolume.enabled = false;
    }

    private void OnDestroy()
    {
        GameplayInputBlocker.Unblock(this);
        if (toggleButton != null) toggleButton.onClick.RemoveListener(Toggle);
        if (decreaseButton != null) decreaseButton.onClick.RemoveListener(DecreaseIntensity);
        if (increaseButton != null) increaseButton.onClick.RemoveListener(IncreaseIntensity);
        if (previewVolume != null) { previewVolume.enabled = false; previewVolume.sharedProfile = null; }
        if (ownedProfile != null)
        {
            // sharedProfile의 런타임 소유자이므로 컴포넌트까지 직접 반환한다.
            foreach (var component in ownedProfile.components) Destroy(component);
            Destroy(ownedProfile);
        }
        if (instance == this) instance = null;
    }
}
