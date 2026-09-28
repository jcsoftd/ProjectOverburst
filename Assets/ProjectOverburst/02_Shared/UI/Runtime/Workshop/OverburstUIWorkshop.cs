using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Authored-scene preview controls. This component is not installed in gameplay scenes.</summary>
[DisallowMultipleComponent]
public sealed class OverburstUIWorkshop : MonoBehaviour
{
    [SerializeField] private OverburstUIWindow inventory;
    [SerializeField] private OverburstUIWindow equipment;
    [SerializeField] private OverburstUIWindow stash;
    [SerializeField] private GameObject enemySamples;
    [SerializeField] private GameObject notificationSamples;
    [SerializeField] private Text modeText;
    [SerializeField] private GameObject gradeSamples;
    [SerializeField] private GameObject tooltipSamples;
    [SerializeField] private OverburstUIShopPreview shopPreview;
    [SerializeField] private OverburstUIMinimapPreview minimapPreview;
    public void ConfigureTooltips(GameObject samples){tooltipSamples=samples;samples.SetActive(false);}
    public void ShowTooltips(){HideWindows();if(tooltipSamples)tooltipSamples.SetActive(true);Label("아이템 툴팁");}
    public void ConfigureGrades(GameObject samples){gradeSamples=samples;gradeSamples.SetActive(false);}
    public void ShowGrades(){HideWindows();gradeSamples.SetActive(true);Label("등급 효과 · 크기 비교");}
    public OverburstUIWindow Inventory => inventory;
    public OverburstUIWindow Equipment => equipment;
    public OverburstUIWindow Stash => stash;

    public void Configure(OverburstUIWindow bag, OverburstUIWindow character, OverburstUIWindow storage,
        GameObject enemies, GameObject notifications, Text label)
    { inventory = bag; equipment = character; stash = storage; enemySamples = enemies; notificationSamples = notifications; modeText = label; }

    private void HideWindows()
    {
        inventory.Close(); equipment.Close(); stash.Close();shopPreview?.Hide();minimapPreview?.Hide();if(tooltipSamples)tooltipSamples.SetActive(false);GetComponentInChildren<OverburstUITooltipHost>(true)?.Hide();
        enemySamples.SetActive(false); notificationSamples.SetActive(false);if(gradeSamples)gradeSamples.SetActive(false);
    }
    public void ShowHud() { HideWindows(); minimapPreview?.Show(); Label("HUD · I 인벤토리 / C 장비 / B 창고 / S 상점"); }
    public void ShowInventory() { HideWindows(); minimapPreview?.Show(true); inventory.ResetPosition(); inventory.Show(); Label("인벤토리 · 제목 표시줄 드래그 / X 닫기"); }
    public void ShowEquipment() { HideWindows(); minimapPreview?.Show(true); equipment.ResetPosition(); equipment.Show(); Label("장비 · 능력치 통합창"); }
    public void ShowComparison() { HideWindows(); minimapPreview?.Show(true); inventory.ResetPosition(); equipment.ResetPosition(); equipment.Show(); inventory.Show(); Label("장비 + 인벤토리 · 창 위치와 겹침 확인"); }
    public void ShowStash() { HideWindows(); minimapPreview?.Show(true); inventory.ResetPosition(); stash.ResetPosition(); stash.Show(); inventory.Show(); Label("창고 + 인벤토리 · 3개 보관함 배치"); }
    public void ShowShop() { HideWindows(); minimapPreview?.Show(true); inventory.ResetPosition(); shopPreview?.Show(); inventory.Show(); inventory.WindowRect.anchoredPosition = new Vector2(622, 64); Label("상점 + 인벤토리 · 공용 상점 뷰"); }
    public void ShowEnemies() { HideWindows(); enemySamples.SetActive(true); Label("몬스터 체력바 · 100% / 45% / 10% 표시"); }
    public void ShowNotifications() { HideWindows(); notificationSamples.SetActive(true); Label("알림 · 레벨 상승 표시"); }
    public void ResetLayout() { inventory.ResetPosition(); equipment.ResetPosition(); stash.ResetPosition(); }
    private void Label(string text) { if (modeText != null) modeText.text = text; }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.iKey.wasPressedThisFrame)
        {
            if (shopPreview != null && shopPreview.IsVisible) ShowInventory();
            else inventory.Toggle();
        }
        if (keyboard.cKey.wasPressedThisFrame)
        {
            if (shopPreview != null && shopPreview.IsVisible) ShowEquipment();
            else equipment.Toggle();
        }
        if (keyboard.bKey.wasPressedThisFrame)
        {
            if (shopPreview != null && shopPreview.IsVisible) ShowStash();
            else stash.Toggle();
        }
        if (keyboard.sKey.wasPressedThisFrame) ShowShop();
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (shopPreview != null && shopPreview.IsVisible)
            {
                ShowHud();
                return;
            }
            OverburstUIWindow front = null;
            foreach (var window in new[] { inventory, equipment, stash })
                if (window.gameObject.activeSelf && (front == null || window.transform.GetSiblingIndex() > front.transform.GetSiblingIndex())) front = window;
            if (front != null) front.Close();
        }
#endif
    }
}
