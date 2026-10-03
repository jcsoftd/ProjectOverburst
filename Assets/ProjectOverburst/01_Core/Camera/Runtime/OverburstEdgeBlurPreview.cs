using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class OverburstEdgeBlurPreview : MonoBehaviour
{
    public const string ResourcePath = "Debug/PF_OverburstEdgeBlurToggle";
    [SerializeField] private Button toggleButton;
    [SerializeField] private Button decreaseButton;
    [SerializeField] private Button increaseButton;
    [SerializeField] private TMP_Text caption;
    [SerializeField] private TMP_Text intensityCaption;
    private static OverburstEdgeBlurPreview instance;
    private bool desiredEnabled = true;
    private int explorationHundredths = 72;
    private int combatHundredths = 42;
    private bool displayedCombatMode;
    public static bool IsEnabled { get; private set; }
    public static float CurrentStrength => instance != null ? instance.SelectedIntensity : 0f;
    public float SelectedIntensity => (PlayerCombatModeController.IsSharedCombatModeActive() ? combatHundredths : explorationHundredths) / 100f;
    public Button ToggleButton => toggleButton;
    public Button DecreaseButton => decreaseButton;
    public Button IncreaseButton => increaseButton;
    public TMP_Text Caption => caption;
    public TMP_Text IntensityCaption => intensityCaption;

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
        if (decreaseButton != null) decreaseButton.onClick.AddListener(DecreaseIntensity);
        if (increaseButton != null) increaseButton.onClick.AddListener(IncreaseIntensity);
        RefreshCaption();
    }

    private void Update()
    {
        // UI 처리보다 먼저 포인터를 검사해 버튼 클릭이 공격으로 흘러가지 않게 한다.
        bool overButton = IsPointerOver(toggleButton) || IsPointerOver(decreaseButton) || IsPointerOver(increaseButton);
        GameplayInputBlocker.SetBlocked(this, overButton);
        if (displayedCombatMode != PlayerCombatModeController.IsSharedCombatModeActive()) RefreshCaption();
    }

    private static bool IsPointerOver(Button button) => button != null && button.gameObject.activeInHierarchy &&
        Mouse.current != null && RectTransformUtility.RectangleContainsScreenPoint(
            (RectTransform)button.transform, Mouse.current.position.ReadValue(), null);

    public void Toggle() => SetEnabled(!desiredEnabled);
    public void DecreaseIntensity() => AdjustIntensity(-1);
    public void IncreaseIntensity() => AdjustIntensity(1);

    private void AdjustIntensity(int steps)
    {
        if (PlayerCombatModeController.IsSharedCombatModeActive()) combatHundredths = Mathf.Clamp(combatHundredths + steps, 0, 100);
        else explorationHundredths = Mathf.Clamp(explorationHundredths + steps, 0, 100);
        RefreshCaption();
    }

    public void SetEnabled(bool enabled)
    {
        desiredEnabled = enabled;
        IsEnabled = enabled && isActiveAndEnabled;
        RefreshCaption();
    }

    private void RefreshCaption()
    {
        if (caption != null) caption.text = IsEnabled ? "가장자리 흐림: 켜짐" : "가장자리 흐림: 꺼짐";
        displayedCombatMode = PlayerCombatModeController.IsSharedCombatModeActive();
        if (intensityCaption != null) intensityCaption.text = (displayedCombatMode ? "전투 " : "탐험 ") +
            SelectedIntensity.ToString("F2", CultureInfo.InvariantCulture);
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
        if (decreaseButton != null) decreaseButton.onClick.RemoveListener(DecreaseIntensity);
        if (increaseButton != null) increaseButton.onClick.RemoveListener(IncreaseIntensity);
        if (instance == this)
        {
            instance = null;
            IsEnabled = false;
        }
    }
}
