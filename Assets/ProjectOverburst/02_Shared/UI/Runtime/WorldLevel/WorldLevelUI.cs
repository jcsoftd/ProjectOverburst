using System;
using System.Collections.Generic;
using System.Linq;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class WorldLevelUI : MonoBehaviour
{
    public const string ResourcePath = "UI/WorldLevel/PF_WorldLevelUI";
    public static WorldLevelUI Current { get; private set; }
    [SerializeField] private GameObject window, confirmation, toast;
    [SerializeField] private CanvasGroup windowInput;
    [SerializeField] private Button unlock, apply, cancel, confirm;
    [SerializeField] private Button[] stages;
    [SerializeField] private WorldLevelButtonSkin[] stageSkins;
    [SerializeField] private Text[] labels;
    private readonly Dictionary<string, Text> text = new Dictionary<string, Text>();
    private AccountSnapshot shown;
    private long shownRevision = -1, pendingRevision;
    private string pendingCharacter;
    private int selected, pendingStage;
    private float toastUntil;
    private PlayerActorRuntime actor;
    private PlayerStateCoordinator coordinator;
    private InventoryUI inventory;
    public WorldLevelNpcInteractable Owner { get; private set; }
    public bool IsOpen => window && window.activeSelf;
    public bool ConfirmationIsOpen => confirmation && confirmation.activeSelf;
    public int SelectedStage => selected;
    public AccountSnapshot Displayed => shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Current = null;
    public static WorldLevelUI Install()
    {
        if (Current) return Current;
        var shop = FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(s => s.HasUsableCanvasRoot && s.gameObject.scene.name == PersistentSceneFlow.PersistentSceneName);
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (!shop || !prefab) return null;
        return Instantiate(prefab, shop.GetComponentInParent<Canvas>(true).transform, false).GetComponent<WorldLevelUI>();
    }
    private void Awake()
    {
        Current = this;
        foreach (var label in labels) text[label.name] = label;
        for (int i = 0; i < stages.Length; i++) { int stage = i + 1; stages[i].onClick.AddListener(() => Select(stage)); }
        unlock.onClick.AddListener(Unlock); apply.onClick.AddListener(RequestApply);
        cancel.onClick.AddListener(CancelApply); confirm.onClick.AddListener(ConfirmApply);
        foreach (var button in GetComponentsInChildren<Button>(true))
        {
            if (button.name == "close-window" || button.name == "leave-button") button.onClick.AddListener(Close);
            if (button.name == "close-confirm" || button.name == "confirmation-scrim") button.onClick.AddListener(CancelApply);
        }
        inventory = GetComponentInParent<Canvas>(true)?.GetComponentInChildren<InventoryUI>(true);
        Close(); Fit();
    }
    private void Fit()
    {
        var rect = (RectTransform)transform;
        var parent = rect.parent as RectTransform;
        if (parent) rect.localScale = Vector3.one * Mathf.Min(parent.rect.width / 1920f, parent.rect.height / 1080f);
    }
    public bool Open(WorldLevelNpcInteractable owner, PlayerActorRuntime player)
    {
        if (!owner || !player || GameplayInputBlocker.IsGameplayInputBlocked || IsOpen) return false;
        var account = AccountGameplaySession.Current;
        if (account == null || account.NeedsProjectionRecovery) return false;
        Owner = owner; actor = player; shown = account.Read(); shownRevision = shown.revision;
        selected = Mathf.Clamp(AccountWorldLevelCommands.EligibleStage(shown.level), 2, 10);
        window.SetActive(true); confirmation.SetActive(false); toast.SetActive(false); windowInput.interactable = true;
        GameplayInputBlocker.Block(this); inventory?.SetInputToggleLocked(this, true);
        coordinator = actor.GetComponent<PlayerStateCoordinator>() ?? PlayerStateCoordinator.Current;
        coordinator?.RequestInteracting(this); transform.SetAsLastSibling(); Refresh();
        EventSystem.current?.SetSelectedGameObject(stages[selected - 1].gameObject);
        return true;
    }
    public void Close()
    {
        if (window) window.SetActive(false); if (confirmation) confirmation.SetActive(false); if (toast) toast.SetActive(false);
        pendingStage = 0; Owner = null; actor = null; shownRevision = -1;
        inventory?.SetInputToggleLocked(this, false); GameplayInputBlocker.Unblock(this);
        coordinator?.ReleaseInteracting(this); coordinator = null;
    }
    private void OnDisable() { Close(); }
    private void OnDestroy() { if (Current == this) Current = null; }
    private void Update()
    {
        Fit(); if (!IsOpen) return;
        if (!Owner || !actor || !actor.isActiveAndEnabled || !actor.Health || actor.Health.IsDead
            || !WorldSessionState.IsHideout || PersistentSceneFlow.Instance?.IsSwitching == true
            || Vector3.Distance(Owner.transform.position, actor.transform.position) > Owner.InteractionRange)
        { Close(); return; }
        if (PlayerInputFacade.Current?.UiCancelPressedThisFrame == true)
        { if (ConfirmationIsOpen) CancelApply(); else Close(); return; }
        var account = AccountGameplaySession.Current;
        if (account == null || account.NeedsProjectionRecovery) { Close(); return; }
        if (account.Revision != shownRevision)
        {
            var next = account.Read();
            if (shown != null && next.activeCharacterId != shown.activeCharacterId) { Close(); return; }
            shown = next; shownRevision = next.revision; Refresh();
        }
        if (toast.activeSelf && Time.unscaledTime >= toastUntil) toast.SetActive(false);
    }
    public void Select(int stage)
    {
        if (!IsOpen || ConfirmationIsOpen || stage < 1 || stage > 10) return;
        selected = stage; Refresh();
    }
    private static string Range(int stage) => $"Lv.{AccountWorldLevelCommands.MinimumMonsterLevel(stage)} – {AccountWorldLevelCommands.MaximumMonsterLevel(stage)}";
    private string Status(int stage) => stage == shown.appliedWorldLevel ? "현재 적용" : stage <= shown.unlockedWorldLevel ? "해금 완료"
        : stage <= AccountWorldLevelCommands.EligibleStage(shown.level) ? "해금 가능" : $"Lv.{AccountWorldLevelCommands.RequiredLevel(stage)} 필요";
    private void Set(string key, string value) { if (text.TryGetValue(key, out var label)) label.text = value; }
    private void Visible(string key, bool visible) { if (text.TryGetValue(key, out var label)) label.gameObject.SetActive(visible); }
    private void Refresh()
    {
        if (shown == null) return;
        int maximum = AccountWorldLevelCommands.EligibleStage(shown.level);
        int unlocked = shown.unlockedWorldLevel, applied = shown.appliedWorldLevel;
        var character = shown.characters?.FirstOrDefault(c => c.id == shown.activeCharacterId);
        Set("character-name", character?.name ?? "모험가"); Set("character-level", $"Lv.{shown.level}");
        if (text.TryGetValue("character-name", out var nameLabel) && text.TryGetValue("character-level", out var levelLabel))
        {
            float nameWidth = Mathf.Min(240, nameLabel.preferredWidth * nameLabel.rectTransform.localScale.x);
            var nameNode = (RectTransform)nameLabel.transform.parent;
            var levelNode = (RectTransform)levelLabel.transform.parent;
            levelNode.anchoredPosition = new Vector2(nameNode.anchoredPosition.x + nameWidth + 12, levelNode.anchoredPosition.y);
        }
        Set("current-summary", $"{applied}단계"); Set("unlock-summary", $"{unlocked}단계까지");
        for (int i = 0; i < 10; i++)
        {
            int stage = i + 1; Set($"row-{stage}-status", Status(stage)); stageSkins[i].SetRowSelected(stage == selected);
            var locked = stage > maximum && stage > unlocked;
            Color number = Hex(stage == selected ? "E1C28D" : locked ? "7F7762" : "CFB381");
            foreach (var label in stages[i].GetComponentsInChildren<Text>(true))
                label.color = label.name.EndsWith("status") ? Hex(locked ? "7F7762" : "B8A276")
                    : label.name.EndsWith("range") ? Hex(locked ? "7F7762" : "A69B85") : number;
        }
        unlock.interactable = maximum > unlocked;
        if (maximum > unlocked)
        {
            var list = Enumerable.Range(unlocked + 1, maximum - unlocked).ToArray();
            Set("unlock-kicker", "새로 해금할 수 있습니다");
            Set("unlock-detail", list.Length > 4 ? $"{list[0]} – {maximum}단계 · {list.Length}개" : string.Join(" · ", list) + "단계");
            Set("unlock-button-text", $"{maximum}단계까지 해금"); Set("unlock-caption", "해금해도 현재 적용 단계는 유지됩니다.");
            Dialogue("“더 먼 길을 맡겨도 되겠군.", "순찰 기록에 이름을 올려두지.”");
        }
        else if (unlocked == 10)
        {
            Set("unlock-kicker", "모든 단계가 열렸습니다"); Set("unlock-detail", "1 – 10단계 해금 완료");
            Set("unlock-button-text", "전체 해금 완료"); Set("unlock-caption", "원하는 사냥 단계를 선택해 적용해 주세요.");
            Dialogue("“이제 모든 길을 맡길 수 있겠군.", "어느 길로 갈지는 네가 정해.”");
        }
        else
        {
            int required = AccountWorldLevelCommands.RequiredLevel(unlocked + 1);
            Set("unlock-kicker", "다음 단계 해금 조건"); Set("unlock-detail", $"캐릭터 Lv.{required}");
            Set("unlock-button-text", $"{unlocked + 1}단계 해금 대기"); Set("unlock-caption", $"Lv.{required}을 달성한 뒤 다시 찾아와 주세요.");
            if (shown.level == 1) Dialogue("“먼 길을 서두를 필요는 없어.", "마을 가까이에서 손을 익혀봐.”");
            else Dialogue("“열어둔 길 중에서 골라봐.", "준비가 되면 다음 길을 맡기지.”");
        }
        Set("detail-stage", selected.ToString()); Set("detail-monsters", "몬스터 " + Range(selected));
        Set("detail-requirement", selected == 1 ? "처음부터 이용 가능한 단계입니다." : $"캐릭터 Lv.{AccountWorldLevelCommands.RequiredLevel(selected)}부터 해금 가능");
        Set("detail-status", Status(selected)); Set("from-stage", $"{applied}단계"); Set("from-monsters", Range(applied));
        Set("to-stage", $"{selected}단계"); Set("to-monsters", Range(selected));
        apply.interactable = selected <= unlocked && selected != applied;
        Set("apply-button-text", selected == applied ? "현재 적용 중" : $"{selected}단계 적용");
        Visible("apply-hint", selected > unlocked);
        Set("apply-hint", selected <= maximum ? $"왼쪽에서 {maximum}단계까지 해금한 뒤 적용할 수 있습니다."
            : $"캐릭터 Lv.{AccountWorldLevelCommands.RequiredLevel(selected)}이 필요합니다. (현재 Lv.{shown.level})");
        if (text.TryGetValue("apply-hint", out var hint)) hint.color = Hex(selected <= maximum ? "BFAB7D" : "B19B7C");
        if (text.TryGetValue("detail-status", out var status)) status.color = Hex(selected > maximum && selected > unlocked ? "7F7762" : "B8A276");
        foreach (var skin in GetComponentsInChildren<WorldLevelButtonSkin>(true)) skin.Refresh();
    }
    private void Dialogue(string first, string second) { Set("npc-dialogue:0", first); Set("npc-dialogue:1", second); }
    public void Unlock()
    {
        if (!ValidCommand() || ConfirmationIsOpen || !unlock.interactable) return;
        try
        {
            if (!AccountGameplaySession.Current.UnlockWorldLevels(shownRevision)) return;
            ReadCommitted(); ShowToast($"{shown.unlockedWorldLevel}단계까지 해금했습니다. 현재 적용은 {shown.appliedWorldLevel}단계입니다.");
            if (apply.interactable) EventSystem.current?.SetSelectedGameObject(apply.gameObject);
        }
        catch (Exception error) when (error is InvalidOperationException || error is System.IO.IOException || error is UnauthorizedAccessException)
        { ShowToast(error is InvalidOperationException ? error.Message : "해금을 저장하지 못했습니다. 다시 시도해 주세요."); }
    }
    public void RequestApply()
    {
        if (!ValidCommand() || !apply.interactable || ConfirmationIsOpen) return;
        pendingStage = selected; pendingRevision = shownRevision; pendingCharacter = shown.activeCharacterId;
        Set("confirm-lead", $"야외 월드레벨을 {pendingStage}단계로 변경할까요?");
        Set("confirm-from", $"{shown.appliedWorldLevel}단계 · {Range(shown.appliedWorldLevel)}");
        Set("confirm-to", $"{pendingStage}단계 · {Range(pendingStage)}");
        confirmation.SetActive(true); windowInput.interactable = false; EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
    }
    public void CancelApply()
    {
        confirmation.SetActive(false); windowInput.interactable = true; pendingStage = 0;
        if (IsOpen) EventSystem.current?.SetSelectedGameObject(apply.gameObject);
    }
    public void ConfirmApply()
    {
        if (!ValidCommand() || !ConfirmationIsOpen || pendingStage == 0) return;
        try
        {
            var current = AccountGameplaySession.Current;
            if (pendingCharacter != current.Read().activeCharacterId) throw new InvalidOperationException("캐릭터가 변경됐습니다.");
            int target = pendingStage;
            if (!current.ApplyWorldLevel(pendingRevision, target)) { CancelApply(); return; }
            OutdoorWorldSpawner.Current?.ResetForWorldLevel();
            CancelApply(); ReadCommitted(); ShowToast($"{target}단계가 적용되었습니다. 야외 몬스터가 초기화됩니다.");
            EventSystem.current?.SetSelectedGameObject(stages[target - 1].gameObject);
        }
        catch (Exception error) when (error is InvalidOperationException || error is System.IO.IOException || error is UnauthorizedAccessException)
        { CancelApply(); ReadCommitted(); ShowToast(error is InvalidOperationException ? error.Message : "변경을 저장하지 못했습니다. 다시 시도해 주세요."); }
    }
    private bool ValidCommand() => IsOpen && Owner && actor && actor.Health && !actor.Health.IsDead
        && Owner.IsInteractionAvailable(actor) && Vector3.Distance(Owner.transform.position, actor.transform.position) <= Owner.InteractionRange;
    private void ReadCommitted() { shown = AccountGameplaySession.Current.Read(); shownRevision = shown.revision; Refresh(); }
    private void ShowToast(string message)
    {
        Set("toast-message", message);
        if (text.TryGetValue("toast-message", out var label))
        {
            float width = Mathf.Min(1700, label.preferredWidth * .5f + 48);
            var rect = (RectTransform)toast.transform; rect.sizeDelta = new Vector2(width, 57);
            rect.anchoredPosition = new Vector2((1920 - width) * .5f, -32);
            label.rectTransform.sizeDelta = new Vector2((width - 48) * 2, 80);
            foreach (var rule in toast.GetComponentsInChildren<Image>(true))
            {
                var r = rule.rectTransform;
                if (rule.name == "Border 0" || rule.name == "Border 2") r.sizeDelta = new Vector2(width, 1);
                else if (rule.name == "Border 1") r.anchoredPosition = new Vector2(width - 1, 0);
            }
        }
        toast.SetActive(true); toastUntil = Time.unscaledTime + 3.5f;
    }
    public void SetPreview(AccountSnapshot state, int stage)
    {
        shown = state; shownRevision = state.revision; selected = stage; window.SetActive(true); confirmation.SetActive(false); toast.SetActive(false);
        foreach (var label in labels) text[label.name] = label;
        Refresh();
    }
    private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }
}
