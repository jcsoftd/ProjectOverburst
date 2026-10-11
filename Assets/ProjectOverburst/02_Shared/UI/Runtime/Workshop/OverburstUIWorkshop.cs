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
    [SerializeField] private OverburstUIRunPreview runPreview;
    [SerializeField] private OverburstUIQuestMerchantPreview questPreview;
    [SerializeField] private OverburstSkillTreeUI skillTreePreview;
    [SerializeField] private OverburstUIBossPreview bossPreview;
    public void ConfigureTooltips(GameObject samples){tooltipSamples=samples;samples.SetActive(false);}
    public void ShowTooltips(){HideWindows();if(tooltipSamples)tooltipSamples.SetActive(true);Label("아이템 툴팁");}
    public void ConfigureGrades(GameObject samples){gradeSamples=samples;gradeSamples.SetActive(false);}
    public void ShowGrades(){HideWindows();gradeSamples.SetActive(true);Label("등급 효과 · 전시 표본");}
    public OverburstUIWindow Inventory => inventory;
    public OverburstUIWindow Equipment => equipment;
    public OverburstUIWindow Stash => stash;

    public void Configure(OverburstUIWindow bag, OverburstUIWindow character, OverburstUIWindow storage,
        GameObject enemies, GameObject notifications, Text label)
    { inventory = bag; equipment = character; stash = storage; enemySamples = enemies; notificationSamples = notifications; modeText = label; }

    private void HideWindows()
    {
        inventory.Close(); equipment.Close(); stash.Close();shopPreview?.Hide();minimapPreview?.Hide();runPreview?.Hide();questPreview?.Hide();skillTreePreview?.Close();bossPreview?.Hide();if(tooltipSamples)tooltipSamples.SetActive(false);GetComponentInChildren<OverburstUITooltipHost>(true)?.Hide();
        enemySamples.SetActive(false); notificationSamples.SetActive(false);if(gradeSamples)gradeSamples.SetActive(false);
    }
    public void ShowHud() { HideWindows(); minimapPreview?.Show(); Label("HUD · I 인벤토리 / C 장비 / B 창고 / S 상점 / R 카드 / T 전송"); }
    public void ShowInventory() { HideWindows(); minimapPreview?.Show(true); inventory.ResetPosition(); inventory.Show(); Label("인벤토리 · 제목 표시줄 드래그 / X 닫기"); }
    public void ShowEquipment() { HideWindows(); minimapPreview?.Show(true); equipment.ResetPosition(); equipment.Show(); Label("장비 · 능력치 통합창"); }
    public void ShowComparison() { HideWindows(); minimapPreview?.Show(true); inventory.ResetPosition(); equipment.ResetPosition(); equipment.Show(); inventory.Show(); Label("장비 + 인벤토리 · 창 위치와 겹침 확인"); }
    public void ShowStash() { HideWindows(); minimapPreview?.Show(true); inventory.ResetPosition(); stash.ResetPosition(); stash.Show(); inventory.Show(); Label("창고 + 인벤토리 · 3개 보관함 배치"); }
    public void ShowShop() { HideWindows(); minimapPreview?.Show(true); inventory.ResetPosition(); shopPreview?.Show(); inventory.Show(); inventory.WindowRect.anchoredPosition = new Vector2(OverburstUIShopSkin.InventoryX, OverburstUIShopSkin.WindowY); Label("상점 + 인벤토리 · 공용 상점 뷰"); }
    void ShowQuest(OverburstUIQuestMerchantPreview.Screen screen,string label){HideWindows();questPreview?.Show(screen);Label(label);}
    public void ShowStylistApproach(){HideWindows();questPreview.Show(OverburstUIQuestMerchantPreview.Screen.StylistApproach);}
    public void ShowQuestApproach()=>ShowQuest(OverburstUIQuestMerchantPreview.Screen.Approach,"상인 접근 · 클릭/휠/F 선택 전시");
    public void ShowQuestDialogue()=>ShowQuest(OverburstUIQuestMerchantPreview.Screen.Dialogue,"상인 대화 · 정식 대화 뷰");
    public void ShowMerchantQuests()=>ShowQuest(OverburstUIQuestMerchantPreview.Screen.MerchantList,"상인 의뢰 목록 · 행 선택으로 오른쪽 상세");
    public void ShowMerchantQuestDetail()=>ShowQuest(OverburstUIQuestMerchantPreview.Screen.MerchantDetail,"상인 의뢰 상세 · 수락/보상 배치");
    public void ShowPersonalQuests()=>ShowQuest(OverburstUIQuestMerchantPreview.Screen.Journal,"개인 퀘스트 목록 · 표시 수치는 전시용");
    public void ShowQuestReport()=>ShowQuest(OverburstUIQuestMerchantPreview.Screen.Report,"의뢰 보고 확인 · 실제 보상 없음");
    public void ShowCornerMenu()=>ShowQuest(OverburstUIQuestMerchantPreview.Screen.HudMenu,"하단 메뉴 · 퀘스트 목록 클릭 연결");
    public void ShowSkillTree(){HideWindows();skillTreePreview?.Open();Label("스킬트리 · K 열기 / 표본 포인트 · 계정에 반영되지 않습니다");}
    public void ShowBoss(){HideWindows();bossPreview?.Show();Label("보스 HUD · 체력/단계/후행 바 표본 · 다음 단계로 상태 전환");}
    public void NextBossPhase(){bossPreview?.NextPhase();}
    public void ShowRunCards() { HideWindows(); runPreview?.ShowCards(); Label("지도 카드 · 전시 표본 / 실제 런 보상 없음"); }
    public void ShowRunTransfer() { HideWindows(); runPreview?.ShowTransfer(inventory, equipment); Label("지도 전송 · 전시 표본 / 실제 아이템 전송 없음"); }
    public void ShowEnemies() { HideWindows(); enemySamples.SetActive(true); Label("몬스터 체력바 · 100% / 45% / 10% 표시"); }
    public void ShowNotifications() { HideWindows(); notificationSamples.SetActive(true); Label("알림 · 전시 표본 / 제품 이벤트 미연결"); }
    public void ResetLayout() { inventory.ResetPosition(); equipment.ResetPosition(); stash.ResetPosition(); }
    private void Label(string text) { if (modeText != null) modeText.text = text; }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        bool modalPreviewOpen = (shopPreview != null && shopPreview.IsVisible)
            || (runPreview != null && runPreview.IsOpen) || (questPreview != null && questPreview.IsVisible)
            || (skillTreePreview != null && skillTreePreview.IsOpen);
        if (keyboard.iKey.wasPressedThisFrame)
        {
            if (modalPreviewOpen) ShowInventory();
            else inventory.Toggle();
        }
        if (keyboard.cKey.wasPressedThisFrame)
        {
            if (modalPreviewOpen) ShowEquipment();
            else equipment.Toggle();
        }
        if (keyboard.bKey.wasPressedThisFrame)
        {
            if (modalPreviewOpen) ShowStash();
            else stash.Toggle();
        }
        if (keyboard.jKey.wasPressedThisFrame) ShowPersonalQuests();
        if (keyboard.kKey.wasPressedThisFrame) ShowSkillTree();
        if (keyboard.sKey.wasPressedThisFrame) ShowShop();
        if (keyboard.rKey.wasPressedThisFrame) ShowRunCards();
        if (keyboard.tKey.wasPressedThisFrame) ShowRunTransfer();
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (modalPreviewOpen)
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
