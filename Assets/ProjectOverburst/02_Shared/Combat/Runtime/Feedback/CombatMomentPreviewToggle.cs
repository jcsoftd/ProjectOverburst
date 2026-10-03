using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Removable comparison buttons, separate from the debug panel and saved settings.
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class CombatMomentPreviewToggle : MonoBehaviour
{
    [SerializeField] private Button parryButton, heavyButton;
    [SerializeField] private TMP_Text parryCaption, heavyCaption;
    private bool displayedParry, displayedHeavy;
    public Button ParryButton => parryButton;
    public Button HeavyButton => heavyButton;
    public TMP_Text ParryCaption => parryCaption;
    public TMP_Text HeavyCaption => heavyCaption;

    private void Awake()
    {
        parryButton.onClick.AddListener(ToggleParry);
        heavyButton.onClick.AddListener(ToggleHeavy);
        Refresh();
    }
    private void Update()
    {
        GameplayInputBlocker.SetBlocked(this, IsPointerOver(parryButton) || IsPointerOver(heavyButton));
        if (displayedParry != CombatMomentPresentation.ParryEnabled || displayedHeavy != CombatMomentPresentation.HeavyEnabled)
            Refresh();
    }
    private static bool IsPointerOver(Button button) => button != null && button.gameObject.activeInHierarchy &&
        Mouse.current != null && RectTransformUtility.RectangleContainsScreenPoint(
            (RectTransform)button.transform, Mouse.current.position.ReadValue(), null);
    public void ToggleParry()
    {
        CombatMomentPresentation.SetParryEnabled(!CombatMomentPresentation.ParryEnabled);
        Refresh();
    }
    public void ToggleHeavy()
    {
        CombatMomentPresentation.SetHeavyEnabled(!CombatMomentPresentation.HeavyEnabled);
        Refresh();
    }
    private void Refresh()
    {
        displayedParry = CombatMomentPresentation.ParryEnabled;
        displayedHeavy = CombatMomentPresentation.HeavyEnabled;
        if (parryCaption != null) parryCaption.text = displayedParry ? "패링 연출: 켜짐" : "패링 연출: 꺼짐";
        if (heavyCaption != null) heavyCaption.text = displayedHeavy ? "완충 강공: 켜짐" : "완충 강공: 꺼짐";
        Color on = new Color(.20f, .31f, .27f, .94f), off = new Color(.16f, .17f, .19f, .94f);
        if (parryButton != null) parryButton.targetGraphic.color = displayedParry ? on : off;
        if (heavyButton != null) heavyButton.targetGraphic.color = displayedHeavy ? on : off;
    }
    private void OnEnable() => Refresh();
    private void OnDisable() => GameplayInputBlocker.Unblock(this);
    private void OnDestroy()
    {
        GameplayInputBlocker.Unblock(this);
        if (parryButton != null) parryButton.onClick.RemoveListener(ToggleParry);
        if (heavyButton != null) heavyButton.onClick.RemoveListener(ToggleHeavy);
    }
}
