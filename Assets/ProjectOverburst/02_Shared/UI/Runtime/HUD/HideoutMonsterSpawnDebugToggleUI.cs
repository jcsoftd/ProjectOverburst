using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class HideoutMonsterSpawnDebugToggleUI : MonoBehaviour
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

        CombatDebugSettings.HideoutMonsterSpawnChanged += HandleSettingChanged;
        RefreshLabel();
    }

    private void OnDisable()
    {
        CombatDebugSettings.HideoutMonsterSpawnChanged -= HandleSettingChanged;
    }

    private void OnDestroy()
    {
        if (button != null && listenerRegistered)
            button.onClick.RemoveListener(Toggle);
    }

    private void Toggle()
    {
        CombatDebugSettings.ToggleHideoutMonsterSpawn();
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
        if (label != null)
            label.text = CombatDebugSettings.SpawnHideoutMonsters
                ? "하이드아웃 몬스터: 켜짐"
                : "하이드아웃 몬스터: 꺼짐";
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
