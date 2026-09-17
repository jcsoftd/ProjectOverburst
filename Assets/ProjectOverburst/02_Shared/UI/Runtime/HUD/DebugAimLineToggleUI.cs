using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebugAimLineToggleUI : MonoBehaviour // Debug 조준선 버튼
{
    [SerializeField] private UnifiedDebugAimLine debugAimLine; // 조준선 대상
    [SerializeField] private Button button; // 버튼
    [SerializeField] private TextMeshProUGUI label; // 라벨
    [SerializeField] private bool showInEditorOrDevelopmentBuild = true; // debug 조건
    private bool listenerRegistered; // 리스너 중복 방지

    private void Awake()
    {
        if (!IsDebugUiAllowed())
        {
            gameObject.SetActive(false);
            return;
        }

        ResolveReferences();
        ResolveUiReferences();
        RegisterButton();
        RefreshLabel();
    }

    private void OnDestroy()
    {
        if (button != null && listenerRegistered)
            button.onClick.RemoveListener(Toggle);
    }

    private void Update()
    {
        if (!IsDebugUiAllowed())
            return;

        ResolveReferences();
        ResolveUiReferences();
        RegisterButton();
        RefreshLabel();
    }

    private void ResolveUiReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (label == null)
            label = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void ResolveReferences()
    {
        if (debugAimLine == null)
            debugAimLine = FindFirstObjectByType<UnifiedDebugAimLine>();
    }

    private void Toggle()
    {
        ResolveReferences();
        if (debugAimLine != null)
            debugAimLine.ToggleDebugLine();
        RefreshLabel();
    }

    private void RegisterButton()
    {
        if (button == null || listenerRegistered)
            return;

        button.onClick.AddListener(Toggle);
        listenerRegistered = true;
    }

    private void RefreshLabel() // 라벨 문구는 씬 오브젝트에서 직접 관리
    {
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
