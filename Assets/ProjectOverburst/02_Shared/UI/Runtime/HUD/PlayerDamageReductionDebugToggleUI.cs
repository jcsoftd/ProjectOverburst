using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerDamageReductionDebugToggleUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private bool showInEditorOrDevelopmentBuild = true;

    private bool listenerRegistered;

    private void Awake()
    {
        if (!IsDebugUiAllowed())
        {
            gameObject.SetActive(false);
            return;
        }

        ResolveReferences();
        RegisterButton();
        RefreshLabel();
    }

    private void OnEnable()
    {
        if (!IsDebugUiAllowed())
            return;

        CombatDebugSettings.PlayerDamageReductionDebugChanged += HandleSettingChanged;
        RefreshLabel();
    }

    private void OnDisable()
    {
        CombatDebugSettings.PlayerDamageReductionDebugChanged -= HandleSettingChanged;
    }

    private void OnDestroy()
    {
        if (button != null && listenerRegistered)
            button.onClick.RemoveListener(Toggle);
    }

    private void Toggle()
    {
        CombatDebugSettings.TogglePlayerDamageReductionDebug();
    }

    private void HandleSettingChanged(bool enabled)
    {
        RefreshLabel();
    }

    private void ResolveReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (label == null)
            label = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void RegisterButton()
    {
        if (button == null || listenerRegistered)
            return;

        button.onClick.AddListener(Toggle);
        listenerRegistered = true;
    }

    private void RefreshLabel()
    {
        if (label == null)
            return;

        label.text = CombatDebugSettings.ReduceIncomingPlayerDamageBy99_9Percent
            ? "피해 99.9% 감소: 켜짐"
            : "피해 99.9% 감소: 꺼짐";
    }

    private bool IsDebugUiAllowed()
    {
        if (!showInEditorOrDevelopmentBuild)
            return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }
}
