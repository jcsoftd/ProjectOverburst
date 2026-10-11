using System;
using System.Collections.Generic;
using System.Linq;
using Overburst.Persistence;
using Overburst.Quests;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(290)]
public sealed class QuestMerchantUI : MonoBehaviour
{
    public const string ResourcePath = "UI/Quests/PF_QuestMerchantUI";
    public enum WindowMode { None, Merchant, Journal, Dialogue }
    [Serializable] public sealed class DetailView
    {
        public Text title, status, body, bodySecond, counts, objectives, rewardsTitle, gold, experience;
        public RectTransform objectiveRule;
        public Button primary, secondary, headerClose;
        public Text primaryLabel;
        public ScrollRect scroll;
        public bool npc;
    }
    public GameObject approach, merchantList, merchantDetail, journal, dialogue, reportPopup, tracker;
    public Text approachName, approachLocation, approachBadge, merchantName, merchantLocation, greeting, listCount, listHint;
    public Text dialogueName, dialogueBody, dialoguePage, dialoguePrimaryLabel, popupMessage, trackerTitle, trackerProgress;
    public Text popupGold, popupExperience;
    public GameObject popupRewards;
    public RawImage merchantPortrait;
    public Button[] approachButtons;
    public GameObject[] approachSelections, approachKeys;
    public QuestRowView merchantRowTemplate, journalRowTemplate;
    public RectTransform merchantRows, journalRows;
    public DetailView merchantInfo, journalInfo;
    public Button listClose, listHeaderClose, dialoguePrimary, dialogueSecondary, popupConfirm, popupCancel;
    [SerializeField] private bool previewOnly;
    public bool IsPreviewOnly => previewOnly;
    public static QuestMerchantUI Current { get; private set; }
    public WindowMode Mode { get; private set; }
    public GeneralGoodsMerchantInteractable ApproachMerchant => menuTarget;
    public int SelectedOption { get; private set; } = 1;
    public bool DetailIsOpen => merchantDetail && merchantDetail.activeSelf;
    public bool ReportIsOpen => reportPopup && reportPopup.activeSelf;
    private GeneralGoodsMerchantInteractable menuTarget, sessionMerchant;
    private PlayerActorRuntime actor;
    private PlayerInteractionController interaction;
    private InventoryUI inventory;
    private PlayerStateCoordinator coordinator;
    private string selectedQuestId;
    private long shownRevision = -1;
    private long popupRevision;
    private int executeFrame = -1, escapeFrame = -1, dialogueIndex;
    private float nextWheel;
    private bool hasTracking;
    private long badgeRevision = -1;
    private GeneralGoodsMerchantInteractable badgeMerchant;
    private readonly List<QuestRowView> merchantPool = new List<QuestRowView>();
    private readonly List<QuestRowView> journalPool = new List<QuestRowView>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Current = null;
    public static void Install()
    {
        if (Current) return;
        var prefab = Resources.Load<GameObject>(ResourcePath);
        var shop = FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(s => s.HasUsableCanvasRoot && s.gameObject.scene.name == PersistentSceneFlow.PersistentSceneName);
        if (!prefab || !shop) throw new InvalidOperationException("상인·퀘스트 UI 프리팹/Canvas가 준비되지 않았습니다.");
        var view = Instantiate(prefab, shop.GetComponentInParent<Canvas>(true).transform, false);
        view.name = "QuestMerchantUI";
    }
    private void Awake()
    {
        if (previewOnly) return;
        Current = this;
        inventory = GetComponentInParent<Canvas>(true).GetComponentInChildren<InventoryUI>(true);
        for (int i = 0; i < approachButtons.Length; i++)
        {
            int option = i;
            approachButtons[i].onClick.AddListener(() => ClickOption(option));
        }
        listClose.onClick.AddListener(Close); listHeaderClose.onClick.AddListener(Close);
        merchantInfo.secondary.onClick.AddListener(CloseDetail); merchantInfo.headerClose.onClick.AddListener(CloseDetail);
        merchantInfo.primary.onClick.AddListener(PrimaryQuestAction);
        journalInfo.secondary.onClick.AddListener(Close); journalInfo.headerClose.onClick.AddListener(Close);
        journalInfo.primary.onClick.AddListener(ToggleTracking);
        dialoguePrimary.onClick.AddListener(AdvanceDialogue); dialogueSecondary.onClick.AddListener(Close);
        popupConfirm.onClick.AddListener(ConfirmReport); popupCancel.onClick.AddListener(HideReport);
        merchantRowTemplate.gameObject.SetActive(false); journalRowTemplate.gameObject.SetActive(false);
        HideAll();
    }
    private void OnDisable() { Close(); if (approach) approach.SetActive(false); }
    private void OnDestroy() { Close(); if (Current == this) Current = null; }
    private void HideAll()
    {
        approach.SetActive(false); merchantList.SetActive(false); merchantDetail.SetActive(false);
        journal.SetActive(false); dialogue.SetActive(false); reportPopup.SetActive(false); tracker.SetActive(false);
    }
    public void Present(GeneralGoodsMerchantInteractable merchant, PlayerActorRuntime player, PlayerInteractionController controller)
    {
        actor = player; interaction = controller;
        if (Mode != WindowMode.None) { approach.SetActive(false); return; }
        if (merchant != menuTarget) { menuTarget = merchant; SelectedOption = 1; nextWheel = 0; }
        bool visible = merchant && merchant.Definition && actor && !GameplayInputBlocker.IsGameplayInputBlocked
            && WorldSessionState.IsHideout && PersistentSceneFlow.Instance?.IsSwitching != true;
        approach.SetActive(visible);
        if (!visible) return;
        approachName.text = merchant.Definition.MerchantName;
        approachLocation.text = MerchantDisplayPolicy.GetCategoryLabel(merchant.Definition) == "무기" ? "대장간" : "상점";
        RefreshApproachBadge(); RefreshSelection();
    }
    public void SelectOption(int index)
    { SelectedOption = Mathf.Clamp(index, 0, 2); RefreshSelection(); }
    private void RefreshSelection()
    {
        for (int i = 0; i < approachSelections.Length; i++)
        { approachSelections[i].SetActive(i == SelectedOption); approachKeys[i].SetActive(i == SelectedOption); }
    }
    private void ClickOption(int index)
    {
        if (!approach.activeInHierarchy || !interaction || !menuTarget) return;
        SelectOption(index);
        interaction.ExecuteMerchantOption(menuTarget, index);
    }
    public InteractionExecutionResult Execute(GeneralGoodsMerchantInteractable target, PlayerActorRuntime player, int option)
    {
        if (!target || !player || Mode != WindowMode.None || !approach.activeInHierarchy
            || target != menuTarget || executeFrame == Time.frameCount || GameplayInputBlocker.IsGameplayInputBlocked
            || player.Health == null || player.Health.IsDead || PersistentSceneFlow.Instance?.IsSwitching == true
            || !WorldSessionState.IsHideout || Vector3.Distance(player.transform.position, target.transform.position) > target.InteractionRange)
            return InteractionExecutionResult.Rejected;
        var director = player.GetComponent<InteractionDirector>();
        if (!director || !ReferenceEquals(director.Refresh(player), target)) return InteractionExecutionResult.Rejected;
        executeFrame = Time.frameCount; actor = player; sessionMerchant = target;
        if (option == 1) { target.OpenTrade(); approach.SetActive(false); return InteractionExecutionResult.Succeeded; }
        if (option == 0) { Mode = WindowMode.Dialogue; dialogueIndex = 0; dialogue.SetActive(true); RefreshDialogue(); }
        else { Mode = WindowMode.Merchant; selectedQuestId = null; merchantList.SetActive(true); merchantDetail.SetActive(false); RefreshMerchant(); }
        BeginModal();
        return InteractionExecutionResult.Succeeded;
    }
    private void BeginModal()
    {
        approach.SetActive(false);
        GameplayInputBlocker.Block(this);
        inventory?.SetInputToggleLocked(this, true);
        coordinator = actor ? actor.GetComponent<PlayerStateCoordinator>() : PlayerStateCoordinator.Current;
        coordinator?.RequestInteracting(this);
        transform.SetAsLastSibling();
    }
    public void OpenJournal()
    {
        if (Mode != WindowMode.None || GameplayInputBlocker.IsGameplayInputBlocked
            || PersistentSceneFlow.Instance?.IsSwitching == true || AccountGameplaySession.Current == null) return;
        actor = PlayerContext.Instance?.CurrentActor;
        if (!actor || actor.Health == null || actor.Health.IsDead) return;
        Mode = WindowMode.Journal; sessionMerchant = null; selectedQuestId = null;
        journal.SetActive(true); RefreshJournal(); BeginModal();
    }
    public void Close()
    {
        Mode = WindowMode.None; sessionMerchant = null; selectedQuestId = null;
        if (merchantList) merchantList.SetActive(false);
        if (merchantDetail) merchantDetail.SetActive(false);
        if (journal) journal.SetActive(false);
        if (dialogue) dialogue.SetActive(false);
        if (reportPopup) reportPopup.SetActive(false);
        inventory?.SetInputToggleLocked(this, false);
        GameplayInputBlocker.Unblock(this);
        if (coordinator) coordinator.ReleaseInteracting(this);
        coordinator = null; shownRevision = -1;
    }
    private void CloseDetail() { merchantDetail.SetActive(false); selectedQuestId = null; RefreshMerchant(); }
    private void Update()
    {
        if (previewOnly) return;
        var input = PlayerInputFacade.Current;
        if (Mode != WindowMode.None && (!actor || !actor.isActiveAndEnabled || actor.Health == null || actor.Health.IsDead
            || PersistentSceneFlow.Instance?.IsSwitching == true || (sessionMerchant && (!sessionMerchant.isActiveAndEnabled
            || Vector3.Distance(sessionMerchant.transform.position, actor.transform.position) > sessionMerchant.InteractionRange))))
        { Close(); return; }
        if (Mode == WindowMode.Merchant || Mode == WindowMode.Dialogue)
            if (!sessionMerchant) { Close(); return; }
        if (input != null)
        {
            if (input.QuestListPressedThisFrame)
            {
                if (Mode == WindowMode.Journal) Close(); else OpenJournal();
            }
            if (input.UiCancelPressedThisFrame && escapeFrame != Time.frameCount && Mode != WindowMode.None)
            {
                escapeFrame = Time.frameCount;
                if (ReportIsOpen) HideReport(); else if (Mode == WindowMode.Merchant && DetailIsOpen) CloseDetail(); else Close();
            }
            if (Mode == WindowMode.None && approach.activeInHierarchy && !GameplayInputBlocker.IsGameplayInputBlocked
                && input.ScrollWheelValue.y != 0 && Time.unscaledTime >= nextWheel)
            { SelectOption(SelectedOption + (input.ScrollWheelValue.y < 0 ? 1 : -1)); nextWheel = Time.unscaledTime + .18f; }
        }
        var account = AccountGameplaySession.Current;
        if (account != null && account.Revision != shownRevision)
        {
            shownRevision = account.Revision;
            if (Mode == WindowMode.Merchant) RefreshMerchant();
            else if (Mode == WindowMode.Journal) RefreshJournal();
            RefreshTracker(); RefreshApproachBadge();
        }
        if (tracker) tracker.SetActive(false); // Existing HUD tracker owns presentation.
    }
    private string MerchantId => sessionMerchant ? MerchantReputationService.GetStableMerchantId(sessionMerchant.Definition) : null;
    private static string Status(QuestAvailability value) => value switch
    { QuestAvailability.Available => "수락 가능", QuestAvailability.Active => "진행 중", QuestAvailability.ReadyToReport => "보고 가능", QuestAvailability.Completed => "완료", _ => "선행 의뢰 필요" };
    private void RefreshApproachBadge()
    {
        if (!menuTarget || AccountGameplaySession.Current == null) return;
        if (badgeRevision == AccountGameplaySession.Current.Revision && badgeMerchant == menuTarget) return;
        badgeRevision = AccountGameplaySession.Current.Revision; badgeMerchant = menuTarget;
        var state = AccountGameplaySession.Current.Read();
        var definitions = QuestCatalog.All.Where(q => q.giverMerchantId == MerchantReputationService.GetStableMerchantId(menuTarget.Definition));
        int ready = definitions.Count(q => AccountQuestCommands.Availability(state, q) == QuestAvailability.ReadyToReport);
        int available = definitions.Count(q => AccountQuestCommands.Availability(state, q) == QuestAvailability.Available);
        approachBadge.text = ready > 0 ? "보고 가능 " + ready : available > 0 ? "새 의뢰 " + available : "";
        approachBadge.gameObject.SetActive(ready + available > 0);
    }
    private void RefreshMerchant()
    {
        if (!sessionMerchant || AccountGameplaySession.Current == null) return;
        var state = AccountGameplaySession.Current.Read();
        var definitions = QuestCatalog.All.Where(q => q.giverMerchantId == MerchantId).OrderBy(q => q.sortOrder).ToArray();
        merchantName.text = sessionMerchant.Definition.MerchantName;
        merchantLocation.text = sessionMerchant.Definition.LocationLabel;
        merchantPortrait.texture = sessionMerchant.Definition.Portrait;
        bool ready = definitions.Any(q => AccountQuestCommands.Availability(state, q) == QuestAvailability.ReadyToReport);
        bool active = definitions.Any(q => AccountQuestCommands.Availability(state, q) == QuestAvailability.Active);
        greeting.text = definitions.Length == 0 ? "지금은 맡길 의뢰가 없네.\n다음에 다시 찾아오게." : ready ? "돌아왔군. 약속한 보상을\n준비해 두었네." : active ? "길목을 정리하고 돌아오게.\n이곳에서 기다리고 있겠네."
            : definitions.Any(q => AccountQuestCommands.Availability(state, q) == QuestAvailability.Completed) ? "덕분에 길목이 안전해졌네.\n다음 의뢰도 살펴보게." : "마을 밖 길목을 정리해 줄\n모험가를 찾고 있네.";
        listCount.text = ready ? "보고 가능 " + definitions.Count(q => AccountQuestCommands.Availability(state, q) == QuestAvailability.ReadyToReport)
            : active ? "진행 중 " + definitions.Count(q => AccountQuestCommands.Availability(state, q) == QuestAvailability.Active)
            : "수락 가능 " + definitions.Count(q => AccountQuestCommands.Availability(state, q) == QuestAvailability.Available);
        listHint.text = DetailIsOpen ? "Esc  상세 닫기" : "Esc  닫기";
        EnsureRows(merchantPool, merchantRowTemplate, merchantRows, definitions.Length);
        for (int i = 0; i < merchantPool.Count; i++)
        {
            var row = merchantPool[i]; row.gameObject.SetActive(i < definitions.Length);
            if (i >= definitions.Length) continue;
            var definition = definitions[i]; string id = definition.id;
            var status = AccountQuestCommands.Availability(state, definition);
            row.Present(definition.title, status == QuestAvailability.Locked ? "첫 사냥을 완료하면 받을 수 있습니다."
                : definition.objectives[0].label + (definition.objectives.Length == 1 ? " · " + merchantName.text + "에게 보고" : " · 지도 던전 입장"),
                definition.rewardGold + " G  ·  경험치 " + definition.rewardExperience, Status(status), status != QuestAvailability.Locked, DetailIsOpen && id == selectedQuestId);
            row.button.onClick.RemoveAllListeners(); row.button.onClick.AddListener(() => SelectQuest(id));
        }
        if (DetailIsOpen) PresentDetail(merchantInfo, state, selectedQuestId, true);
    }
    private void RefreshJournal()
    {
        var state = AccountGameplaySession.Current?.Read(); if (state == null) return;
        if (!state.quests.Exists(q => q.questId == selectedQuestId)) selectedQuestId = state.quests.FirstOrDefault()?.questId;
        EnsureRows(journalPool, journalRowTemplate, journalRows, state.quests.Count);
        for (int i = 0; i < journalPool.Count; i++)
        {
            var row = journalPool[i]; row.gameObject.SetActive(i < state.quests.Count);
            if (i >= state.quests.Count) continue;
            var q = state.quests[i]; string id = q.questId;
            var status = q.state == SavedQuestState.Completed ? QuestAvailability.Completed : AccountQuestCommands.IsReady(q) ? QuestAvailability.ReadyToReport : QuestAvailability.Active;
            row.Present(q.title, "", "", Status(status), true, id == selectedQuestId);
            row.button.onClick.RemoveAllListeners(); row.button.onClick.AddListener(() => { selectedQuestId = id; RefreshJournal(); journalInfo.scroll.verticalNormalizedPosition = 1; });
        }
        PresentDetail(journalInfo, state, selectedQuestId, false);
    }
    private void SelectQuest(string id)
    {
        if (ReportIsOpen || Mode != WindowMode.Merchant) return;
        var definition = QuestCatalog.Find(id); var state = AccountGameplaySession.Current?.Read();
        if (!definition || state == null || AccountQuestCommands.Availability(state, definition) == QuestAvailability.Locked) return;
        selectedQuestId = id; merchantDetail.SetActive(true); RefreshMerchant(); merchantInfo.scroll.verticalNormalizedPosition = 1;
    }
    public void PresentPreview(AccountSnapshot state, string id, bool personal)
    {
        if (!previewOnly) throw new InvalidOperationException("Preview state is restricted to the management scene.");
        PresentDetail(personal ? journalInfo : merchantInfo, state, id, !personal);
    }
    private void PresentDetail(DetailView view, AccountSnapshot state, string id, bool npc)
    {
        var q = state.quests.Find(x => x.questId == id);
        var definition = QuestCatalog.Find(id);
        if (q == null && !definition)
        {
            view.title.text = "진행 중인 퀘스트가 없습니다."; view.status.text = "";
            LayoutDetail(view, "상인에게서 의뢰를 수락하면 이곳에서 목표와 보상을 확인할 수 있습니다.", 0);
            view.objectiveRule.gameObject.SetActive(false);
            view.counts.text = view.objectives.text = ""; view.gold.text = view.experience.text = "—";
            view.rewardsTitle.text = "보상"; view.primaryLabel.text = "목표 표시";
            view.primary.interactable = false; return;
        }
        view.objectiveRule.gameObject.SetActive(npc);
        var status = q != null ? q.state == SavedQuestState.Completed ? QuestAvailability.Completed : AccountQuestCommands.IsReady(q) ? QuestAvailability.ReadyToReport : QuestAvailability.Active
            : AccountQuestCommands.Availability(state, definition);
        view.title.text = q?.title ?? definition.title; view.status.text = Status(status);
        string story = q == null ? definition.description : npc
            ? status == QuestAvailability.Completed && !string.IsNullOrEmpty(q.completedDescription) ? q.completedDescription
                : status == QuestAvailability.ReadyToReport && !string.IsNullOrEmpty(q.readyDescription) ? q.readyDescription : q.description
            : string.IsNullOrEmpty(q.journalDescription) ? q.description : q.journalDescription;
        var objectives = q?.objectives ?? definition.objectives.Select(o => new QuestObjectiveSnapshot { kind = o.kind, targetId = o.targetId, label = o.label, required = o.required }).ToList();
        view.counts.text = string.Join("\n", objectives.Select(o => o.current + " / " + o.required));
        view.objectives.text = string.Join("\n", objectives.Select(o => o.label));
        if (objectives.Count == 1 && objectives[0].kind == QuestObjectiveKind.KillEnemy)
        {
            view.counts.text += "\n" + (status == QuestAvailability.Completed ? "완료" : status == QuestAvailability.ReadyToReport ? "보고 가능" : "—");
            view.objectives.text += "\n무기상인에게 보고";
        }
        LayoutDetail(view, story, view.objectives.text.Split('\n').Length);
        view.rewardsTitle.text = status == QuestAvailability.Completed ? "보상 · 수령 완료" : "보상";
        view.gold.text = (q?.rewardGold ?? definition.rewardGold) + " <size=26>G</size>";
        view.experience.text = (q?.rewardExperience ?? definition.rewardExperience).ToString();
        view.primaryLabel.text = npc ? status == QuestAvailability.Completed ? "보고 완료" : status == QuestAvailability.ReadyToReport ? "의뢰 보고" : status == QuestAvailability.Active ? "수락한 의뢰" : "의뢰 수락"
            : state.trackedQuestId == id ? "추적 중" : "목표 표시";
        view.primary.interactable = npc ? definition && (status == QuestAvailability.Available || status == QuestAvailability.ReadyToReport)
            : q != null && q.state == SavedQuestState.Active;
    }
    private static void LayoutDetail(DetailView view, string story, int rows)
    {
        string[] paragraphs = (story ?? "").Split(new[] { "\n\n" }, 2, StringSplitOptions.None);
        view.body.text = paragraphs[0]; view.bodySecond.text = paragraphs.Length > 1 ? paragraphs[1] : "";
        float line = view.npc ? 28.9f : 28.875f, start = view.npc ? 62 : 60;
        float first = Lines(view.body) * line, second = Lines(view.bodySecond) * line;
        Position(view.body.rectTransform, start, first);
        Position(view.bodySecond.rectTransform, start + first + 18, second);
        float ruleY = start + first + (paragraphs.Length > 1 ? 18 + second : 0) + (view.npc ? 26 : 22);
        Position(view.objectiveRule, ruleY, 1);
        float objectiveY = ruleY + (view.npc ? 21 : 0);
        Position(view.counts.rectTransform, objectiveY, rows * 30);
        Position(view.objectives.rectTransform, objectiveY, rows * 30);
        view.scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, objectiveY + rows * 30);
    }
    private static int Lines(Text text)
    {
        if(string.IsNullOrEmpty(text.text)) return 0;
        var settings=text.GetGenerationSettings(new Vector2(text.rectTransform.rect.width,10000));
        text.cachedTextGeneratorForLayout.Populate(text.text,settings);
        return Math.Max(1,text.cachedTextGeneratorForLayout.lineCount);
    }
    private static void Position(RectTransform rect, float y, float height)
    { rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -y); rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height / rect.localScale.y); }
    private void PrimaryQuestAction()
    {
        if (Mode != WindowMode.Merchant || !sessionMerchant || ReportIsOpen) return;
        var runtime = QuestSession.Current; var account = AccountGameplaySession.Current;
        if (!runtime || account == null) return;
        var state = account.Read(); var definition = QuestCatalog.Find(selectedQuestId); if (!definition) return;
        var status = AccountQuestCommands.Availability(state, definition);
        if (status == QuestAvailability.Available)
        {
            if (!runtime.Accept(selectedQuestId, MerchantId)) ShowFailure(runtime.LastError);
            RefreshMerchant();
        }
        else if (status == QuestAvailability.ReadyToReport)
        {
            popupRevision = account.Revision;
            var q = state.quests.Find(x => x.questId == selectedQuestId);
            popupMessage.text = q.title + "의 목표를 달성했습니다.\n" + sessionMerchant.Definition.MerchantName + "에게 보고하고 보상을 받으시겠습니까?";
            popupGold.text = q.rewardGold + " <size=26>G</size>"; popupExperience.text = q.rewardExperience.ToString();
            popupRewards.SetActive(true);popupConfirm.interactable = true; reportPopup.SetActive(true); reportPopup.transform.SetAsLastSibling();
        }
    }
    private void ConfirmReport()
    {
        if (!ReportIsOpen || Mode != WindowMode.Merchant || !sessionMerchant) return;
        popupConfirm.interactable = false;
        var account = AccountGameplaySession.Current;
        if (account == null) { HideReport(); return; }
        if (account.Revision != popupRevision)
        { popupRevision = account.Revision; popupConfirm.interactable = true; popupMessage.text = "계정 상태가 변경되었습니다.\n보상을 다시 확인하고 보고해 주세요."; return; }
        var runtime = QuestSession.Current;
        if (runtime && runtime.TurnIn(selectedQuestId, MerchantId)) { HideReport(); RefreshMerchant(); }
        else { popupMessage.text = runtime ? runtime.LastError ?? "보고 상태를 다시 확인해 주세요." : "계정이 준비되지 않았습니다."; popupConfirm.interactable = true; }
    }
    private void ShowFailure(string error)
    { popupRewards.SetActive(false);popupMessage.text = error ?? "상태를 다시 확인해 주세요."; popupConfirm.interactable = false; reportPopup.SetActive(true);reportPopup.transform.SetAsLastSibling(); }
    private void HideReport() => reportPopup.SetActive(false);
    private void ToggleTracking()
    {
        var state = AccountGameplaySession.Current?.Read(); if (state == null || selectedQuestId == null) return;
        var runtime = QuestSession.Current;
        if (runtime && !runtime.Track(state.trackedQuestId == selectedQuestId ? null : selectedQuestId))
        { ShowFailure(runtime.LastError); }
        RefreshJournal(); RefreshTracker();
    }
    private void RefreshTracker()
    {
        var state = AccountGameplaySession.Current?.Read();
        var q = state?.quests.Find(x => x.questId == state.trackedQuestId);
        hasTracking = q != null;
        tracker.SetActive(false);
        if (q == null) return;
        trackerTitle.text = q.title;
        trackerProgress.text = AccountQuestCommands.IsReady(q) ? "무기상인에게 보고" : string.Join("\n", q.objectives.Where(o => o.current < o.required).Select(o => o.label + "  " + o.current + " / " + o.required));
    }
    private void RefreshDialogue()
    {
        dialogueName.text = sessionMerchant.Definition.MerchantName;
        dialogueBody.text = dialogueIndex == 0 ? "마을 밖으로 나갈 생각인가?\n무기부터 점검하게. 밖에서는 자네를 지켜줄 사람이 없으니."
            : "몬스터 열 마리를 처치하고 돌아오게.\n의뢰는 퀘스트 메뉴에서 확인할 수 있네.";
        dialoguePage.text = (dialogueIndex + 1).ToString("00") + "  /  02";
        dialoguePrimaryLabel.text = dialogueIndex == 0 ? "계속 듣기" : "대화 마치기";
        dialogueSecondary.gameObject.SetActive(dialogueIndex == 0);
        dialoguePrimary.GetComponent<RectTransform>().anchoredPosition=new Vector2(dialogueIndex==0 ? 274 : 196,-241.4f);
    }
    private void AdvanceDialogue()
    {
        if (dialogueIndex == 0) { dialogueIndex = 1; RefreshDialogue(); }
        else { QuestSession.Current?.RecordTalk(MerchantId); Close(); }
    }
    private static void EnsureRows(List<QuestRowView> pool, QuestRowView template, RectTransform container, int count)
    {
        while (pool.Count < count)
        {
            var row = Instantiate(template, container, false); row.name = "Quest " + pool.Count;
            var rect = (RectTransform)row.transform;
            var start = (RectTransform)template.transform;
            rect.anchoredPosition = start.anchoredPosition + Vector2.down * (start.sizeDelta.y + (start.sizeDelta.y > 100 ? 12 : 10)) * pool.Count;
            pool.Add(row);
        }
        var startRect = (RectTransform)template.transform;
        float gap = startRect.sizeDelta.y > 100 ? 12 : 10;
        float height = count == 0 ? 0 : -startRect.anchoredPosition.y + count * startRect.sizeDelta.y + (count - 1) * gap;
        container.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(((RectTransform)container.parent).rect.height, height));
    }
}
