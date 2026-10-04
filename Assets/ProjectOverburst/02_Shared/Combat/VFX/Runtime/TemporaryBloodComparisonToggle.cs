using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Remove PF_TemporaryBloodComparison to retire the comparison. No saved preference.
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class TemporaryBloodComparisonToggle : MonoBehaviour
{
    public const string ResourcePath = "Combat/VFX/PF_TemporaryBloodComparison";
    [SerializeField] Button button;
    [SerializeField] TMP_Text caption;
    [SerializeField] Button colorButton;
    [SerializeField] TMP_Text colorCaption;
    [SerializeField] Button tuningButton, resetButton;
    [SerializeField] GameObject tuningPanel;
    [SerializeField] Button[] decreaseButtons = System.Array.Empty<Button>(), increaseButtons = System.Array.Empty<Button>();
    [SerializeField] TMP_Text[] valueCaptions = System.Array.Empty<TMP_Text>();
    static TemporaryBloodComparisonToggle instance;
    bool displayed, displayedRed;
    int revision = -1;
    public Button Button => button;
    public TMP_Text Caption => caption;
    public Button ColorButton => colorButton;
    public TMP_Text ColorCaption => colorCaption;
    public Button TuningButton => tuningButton;
    public Button ResetButton => resetButton;
    public GameObject TuningPanel => tuningPanel;
    public Button[] DecreaseButtons => decreaseButtons;
    public Button[] IncreaseButtons => increaseButtons;
    public TMP_Text[] ValueCaptions => valueCaptions;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => instance = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Prepare()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (instance == null)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab != null) Instantiate(prefab);
        }
#endif
    }
    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this; DontDestroyOnLoad(gameObject);
        button.onClick.AddListener(Toggle); colorButton.onClick.AddListener(ToggleColor);
        if (tuningButton) tuningButton.onClick.AddListener(ToggleTuning);
        if (resetButton) resetButton.onClick.AddListener(ResetTuning);
        for (int i = 0; i < valueCaptions.Length; i++)
        {
            int index = i;
            decreaseButtons[i].onClick.AddListener(() => Adjust(index, -1));
            increaseButtons[i].onClick.AddListener(() => Adjust(index, 1));
        }
        if (tuningPanel) tuningPanel.SetActive(false);
        Refresh();
    }
    public void Toggle()
    {
        BloodHitVfxService.SetPackEnabled(!BloodHitVfxService.PackEnabled); Refresh();
    }
    public void ToggleColor() { BloodHitVfxService.SetUniformRed(!BloodHitVfxService.UniformRed); Refresh(); }
    public void ToggleTuning() { if (tuningPanel) tuningPanel.SetActive(!tuningPanel.activeSelf); Refresh(); }
    public void ResetTuning() { BloodComparisonTuning.ResetCurrent(); Refresh(); }
    public void Adjust(int index, int steps) { BloodComparisonTuning.Adjust((BloodComparisonTuning.Control)index, steps); Refresh(); }
    void Update()
    {
        bool over = IsOver(button) || IsOver(colorButton) || IsOver(tuningButton);
        if (tuningPanel && tuningPanel.activeInHierarchy && Mouse.current != null)
            over |= RectTransformUtility.RectangleContainsScreenPoint((RectTransform)tuningPanel.transform, Mouse.current.position.ReadValue(), null);
        GameplayInputBlocker.SetBlocked(this, over);
        if (displayed != BloodHitVfxService.PackEnabled || displayedRed != BloodHitVfxService.UniformRed || revision != BloodComparisonTuning.Revision) Refresh();
    }
    static bool IsOver(Button value) => value != null && value.gameObject.activeInHierarchy && Mouse.current != null &&
        RectTransformUtility.RectangleContainsScreenPoint((RectTransform)value.transform, Mouse.current.position.ReadValue(), null);
    void Refresh()
    {
        displayed = BloodHitVfxService.PackEnabled; displayedRed = BloodHitVfxService.UniformRed; revision = BloodComparisonTuning.Revision;
        if (caption) caption.text = displayed ? "혈흔 B · 새 팩" : "혈흔 A · 기존";
        if (button) button.targetGraphic.color = displayed ? new Color(.36f,.16f,.18f,.96f) : new Color(.18f,.20f,.24f,.96f);
        if (colorCaption) colorCaption.text = displayedRed ? "색상 · 전체 붉은색" : "색상 · 몬스터별 조정";
        if (colorButton) colorButton.targetGraphic.color = displayedRed ? new Color(.36f,.16f,.18f,.96f) : new Color(.20f,.29f,.24f,.96f);
        for (int i = 0; i < valueCaptions.Length; i++)
            if (valueCaptions[i]) valueCaptions[i].text = BloodComparisonTuning.Value((BloodComparisonTuning.Control)i).ToString("F1");
        if (tuningButton) tuningButton.GetComponentInChildren<TMP_Text>().text = tuningPanel && tuningPanel.activeSelf ? "세부 조절 닫기" : "크기 · 색상 조절";
    }
    void OnDisable() => GameplayInputBlocker.Unblock(this);
    void OnDestroy()
    {
        GameplayInputBlocker.Unblock(this);
        if (button) button.onClick.RemoveListener(Toggle);
        if (colorButton) colorButton.onClick.RemoveListener(ToggleColor);
        if (tuningButton) tuningButton.onClick.RemoveListener(ToggleTuning);
        if (resetButton) resetButton.onClick.RemoveListener(ResetTuning);
        if (instance == this) instance = null;
    }
}
