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
    static TemporaryBloodComparisonToggle instance;
    bool displayed, displayedRed;
    public Button Button => button;
    public TMP_Text Caption => caption;
    public Button ColorButton => colorButton;
    public TMP_Text ColorCaption => colorCaption;
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
        button.onClick.AddListener(Toggle); colorButton.onClick.AddListener(ToggleColor); Refresh();
    }
    public void Toggle()
    {
        BloodHitVfxService.SetPackEnabled(!BloodHitVfxService.PackEnabled);
        Refresh();
    }
    public void ToggleColor()
    {
        BloodHitVfxService.SetUniformRed(!BloodHitVfxService.UniformRed); Refresh();
    }
    void Update()
    {
        bool over = IsOver(button) || IsOver(colorButton);
        GameplayInputBlocker.SetBlocked(this, over);
        if (displayed != BloodHitVfxService.PackEnabled || displayedRed != BloodHitVfxService.UniformRed) Refresh();
    }
    static bool IsOver(Button value) => value != null && value.gameObject.activeInHierarchy && Mouse.current != null &&
        RectTransformUtility.RectangleContainsScreenPoint((RectTransform)value.transform, Mouse.current.position.ReadValue(), null);
    void Refresh()
    {
        displayed = BloodHitVfxService.PackEnabled;
        displayedRed = BloodHitVfxService.UniformRed;
        if (caption != null) caption.text = displayed ? "혈흔 B · 새 팩" : "혈흔 A · 기존";
        if (button != null) button.targetGraphic.color = displayed ? new Color(.36f,.16f,.18f,.96f) : new Color(.18f,.20f,.24f,.96f);
        if (colorCaption != null) colorCaption.text = displayedRed ? "색상 · 전체 붉은색" : "색상 · 몬스터별 조정";
        if (colorButton != null) colorButton.targetGraphic.color = displayedRed ? new Color(.36f,.16f,.18f,.96f) : new Color(.20f,.29f,.24f,.96f);
    }
    void OnDisable() => GameplayInputBlocker.Unblock(this);
    void OnDestroy()
    {
        GameplayInputBlocker.Unblock(this);
        if (button != null) button.onClick.RemoveListener(Toggle);
        if (colorButton != null) colorButton.onClick.RemoveListener(ToggleColor);
        if (instance == this) instance = null;
    }
}
