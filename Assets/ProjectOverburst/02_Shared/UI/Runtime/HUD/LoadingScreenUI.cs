using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LoadingScreenUI : MonoBehaviour // 로딩 화면
{
    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI hintText;
    [SerializeField] private Image progressFill;

    private bool warnedMissingReferences;

    private void Awake()
    {
        ResolveReferences();
        ForceHide(); // 시작 숨김
    }

    private void OnDisable()
    {
        GameplayInputBlocker.Unblock(this); // 잠금 해제
    }

    public void Show(string title, string status)
    {
        ResolveReferences();
        if (titleText != null)
            titleText.text = title;

        SetStatus(status);
        SetProgress(0f);
        SetVisible(true);
        SetInventoryToggleLocked(true);
        GameplayInputBlocker.Block(this); // 입력 차단
    }

    public void SetStatus(string status)
    {
        if (statusText != null)
            statusText.text = status;
    }

    public void SetProgress(float value)
    {
        if (progressFill != null)
            progressFill.fillAmount = Mathf.Clamp01(value);
    }

    public void Hide()
    {
        SetProgress(1f);
        SetVisible(false);
        SetInventoryToggleLocked(false);
        GameplayInputBlocker.Unblock(this); // 잠금 해제
    }

    public void ForceHide()
    {
        SetVisible(false);
        SetInventoryToggleLocked(false);
        GameplayInputBlocker.Unblock(this);
    }

    private void ResolveReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>(); // 루트 캔버스 그룹

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (canvasGroup == null || titleText == null || statusText == null || progressFill == null)
            WarnMissingReferences();
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible; // 입력 가림
        canvasGroup.interactable = visible;
    }

    private void WarnMissingReferences()
    {
        if (warnedMissingReferences)
            return;

        Debug.LogWarning("[LoadingScreenUI] LoadingScreen 참조가 일부 비어 있습니다. PersistentScene HUDCanvas 하위 LoadingScreen 연결을 확인하세요.");
        warnedMissingReferences = true;
    }

    private void SetInventoryToggleLocked(bool locked)
    {
        InventoryUI[] inventoryUis = FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < inventoryUis.Length; i++)
        {
            if (inventoryUis[i] != null)
                inventoryUis[i].InputToggleLocked = locked;
        }
    }
}
