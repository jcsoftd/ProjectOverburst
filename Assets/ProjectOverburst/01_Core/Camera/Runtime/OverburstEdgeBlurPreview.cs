using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class OverburstEdgeBlurPreview : MonoBehaviour
{
    public const string ResourcePath = "Debug/PF_OverburstEdgeBlurToggle";
    [SerializeField] private Button toggleButton;
    [SerializeField] private TMP_Text caption;
    private static OverburstEdgeBlurPreview instance;
    private bool desiredEnabled = true;
    public static bool IsEnabled { get; private set; }
    public static float CurrentStrength => PlayerCombatModeController.IsSharedCombatModeActive() ? .42f : .72f;
    public Button ToggleButton => toggleButton;
    public TMP_Text Caption => caption;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        instance = null;
        IsEnabled = false;
    }

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
        IsEnabled = true;
        if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
        RefreshCaption();
    }

    private void Update()
    {
        // UI 처리보다 먼저 포인터를 검사해 버튼 클릭이 공격으로 흘러가지 않게 한다.
        bool overButton = toggleButton != null && toggleButton.gameObject.activeInHierarchy &&
            Mouse.current != null && RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)toggleButton.transform, Mouse.current.position.ReadValue(), null);
        GameplayInputBlocker.SetBlocked(this, overButton);
    }

    public void Toggle() => SetEnabled(!IsEnabled);

    public void SetEnabled(bool enabled)
    {
        desiredEnabled = enabled;
        IsEnabled = enabled;
        RefreshCaption();
    }

    private void RefreshCaption()
    {
        if (caption != null) caption.text = IsEnabled ? "가장자리 흐림: 켜짐" : "가장자리 흐림: 꺼짐";
        if (toggleButton != null && toggleButton.targetGraphic != null)
            toggleButton.targetGraphic.color = IsEnabled ? new Color(.20f, .31f, .27f, .94f) : new Color(.16f, .17f, .19f, .94f);
    }

    private void OnEnable()
    {
        if (instance == this) IsEnabled = desiredEnabled;
        RefreshCaption();
    }

    private void OnDisable()
    {
        GameplayInputBlocker.Unblock(this);
        if (instance == this) IsEnabled = false;
    }

    private void OnDestroy()
    {
        GameplayInputBlocker.Unblock(this);
        if (toggleButton != null) toggleButton.onClick.RemoveListener(Toggle);
        if (instance == this)
        {
            instance = null;
            IsEnabled = false;
        }
    }
}
