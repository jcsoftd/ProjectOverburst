using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EnemySquadGeometryDebugToggleUI : MonoBehaviour // 부대 슬롯점·반경 토글
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

        CombatDebugSettings.EnemySquadGeometryDebugChanged += HandleSettingChanged;
        RefreshLabel();
    }

    private void OnDisable()
    {
        CombatDebugSettings.EnemySquadGeometryDebugChanged -= HandleSettingChanged;
    }

    private void OnDestroy()
    {
        if (button != null && listenerRegistered)
            button.onClick.RemoveListener(Toggle);
    }

    private void Toggle()
    {
        CombatDebugSettings.ToggleEnemySquadGeometryDebug();
    }

    private void HandleSettingChanged(bool visible)
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
        if (label != null)
            label.text = CombatDebugSettings.ShowEnemySquadGeometryDebug
                ? "부대 점/반경: 켜짐"
                : "부대 점/반경: 꺼짐";
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
