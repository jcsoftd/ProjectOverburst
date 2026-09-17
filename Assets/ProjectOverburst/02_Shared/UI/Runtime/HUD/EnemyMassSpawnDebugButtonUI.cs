using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EnemyMassSpawnDebugButtonUI : MonoBehaviour // 일회성 대량 스폰 버튼
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private EnemyMassSpawnDebugPattern pattern;
    [SerializeField, Min(1)] private int spawnCount = 100;
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

    private void OnDestroy()
    {
        if (button != null && listenerRegistered)
            button.onClick.RemoveListener(Spawn);
    }

    private void Spawn()
    {
        EnemyMassSpawnDebugService.RequestSpawn(pattern, Mathf.Max(1, spawnCount));
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

        button.onClick.AddListener(Spawn);
        listenerRegistered = true;
    }

    private void RefreshLabel()
    {
        if (label == null)
            return;

        label.text = pattern == EnemyMassSpawnDebugPattern.Circle
            ? "원형 " + Mathf.Max(1, spawnCount) + "마리 스폰"
            : "밀집 " + Mathf.Max(1, spawnCount) + "마리 스폰";
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
