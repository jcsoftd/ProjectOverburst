using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>게임 설정의 모션블러 선택을 모든 빌드에 적용한다. 꺼짐은 맵의 블러도 0으로 덮는다.</summary>
[DisallowMultipleComponent]
public sealed class OverburstMotionBlur : MonoBehaviour
{
    public const string ResourcePath = "Camera/PF_OverburstMotionBlur";
    public const float BlurClamp = .003f;
    private static OverburstMotionBlur instance;
    private Volume volume;
    private VolumeProfile ownedProfile;
    private MotionBlur motionBlur;
    private Camera targetCamera;
    private float nextCameraSearch;
    public bool IsEnabled => isActiveAndEnabled && OverburstGameSettings.MotionBlurEnabled;
    public float SelectedIntensity => OverburstGameSettings.MotionBlurIntensity;
    public Camera TargetCamera => targetCamera;
    public Volume Volume => volume;
    public MotionBlur Settings => motionBlur;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab != null) Instantiate(prefab);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        ownedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        ownedProfile.name = "OVERBURST Motion Blur (Runtime)";
        ownedProfile.hideFlags = HideFlags.DontSave;
        motionBlur = ownedProfile.Add<MotionBlur>(true);
        motionBlur.mode.Override(MotionBlurMode.CameraAndObjects);
        motionBlur.quality.Override(MotionBlurQuality.Low);
        motionBlur.clamp.Override(BlurClamp);
        volume = GetComponentInChildren<Volume>(true);
        if (volume == null)
        {
            var host = new GameObject("MotionBlurVolume", typeof(Volume));
            host.transform.SetParent(transform, false);
            volume = host.GetComponent<Volume>();
        }
        volume.isGlobal = true;
        volume.priority = 10000f;
        volume.sharedProfile = ownedProfile;
        BindCamera();
        OverburstGameSettings.Changed += ApplyState;
        ApplyState();
    }

    private void BindCamera()
    {
        var quarterView = FindFirstObjectByType<QuarterViewCamera>();
        targetCamera = quarterView != null ? quarterView.GetComponent<Camera>() : Camera.main;
        if (targetCamera == null || volume == null) return;
        var data = targetCamera.GetComponent<UniversalAdditionalCameraData>();
        int mask = data != null ? data.volumeLayerMask.value : 1;
        for (int layer = 0; layer < 32; layer++)
            if ((mask & (1 << layer)) != 0) { volume.gameObject.layer = layer; break; }
    }

    private void Update()
    {
        if ((targetCamera == null || !targetCamera.isActiveAndEnabled) && Time.unscaledTime >= nextCameraSearch)
        { nextCameraSearch = Time.unscaledTime + 1f; BindCamera(); }
    }

    private void ApplyState()
    {
        if (motionBlur != null) motionBlur.intensity.Override(IsEnabled ? SelectedIntensity : 0f);
        if (volume != null) volume.enabled = isActiveAndEnabled;
    }
    private void OnEnable() { if (instance == this) ApplyState(); }
    private void OnDisable() { if (volume != null) volume.enabled = false; }
    private void OnDestroy()
    {
        OverburstGameSettings.Changed -= ApplyState;
        if (volume != null) { volume.enabled = false; volume.sharedProfile = null; }
        if (ownedProfile != null)
        {
            foreach (var component in ownedProfile.components) Destroy(component);
            Destroy(ownedProfile);
        }
        if (instance == this) instance = null;
    }
}
