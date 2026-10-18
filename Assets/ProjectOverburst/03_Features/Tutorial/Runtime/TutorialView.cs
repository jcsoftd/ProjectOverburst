using System;
using Overburst.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class TutorialView : MonoBehaviour
{
    public GameObject goal, tools, comparison, shade, modal, footer;
    public TMP_Text progress, heading, instruction, keys, feedback, modalTitle, modalBody, modalNote;
    public Button retry, skip, exit, primary, secondary, worldConfirm;
    public Image[] gradeBackgrounds;
    public TutorialKeyGuide keyGuide;
    private OptionalTutorialController owner;
    private float baseGoalHeight = 176;
    private TutorialLesson shownLesson;
    private long potionGuideRevision = -1;

    public void Bind(OptionalTutorialController controller)
    {
        owner = controller;
        retry.onClick.AddListener(owner.Retry); skip.onClick.AddListener(owner.Skip); exit.onClick.AddListener(owner.Exit);
        worldConfirm.onClick.AddListener(owner.ConfirmWorld);
    }
    public void Hide()
    {
        goal.SetActive(false); tools.SetActive(false); comparison.SetActive(false); shade.SetActive(false);
        modal.SetActive(false); footer.SetActive(false);
    }
    private void Modal(string title, string body, string note)
    {
        Hide(); shade.SetActive(true); modal.SetActive(true);
        var rect = (RectTransform)modal.transform; rect.sizeDelta = new Vector2(980,420); rect.anchoredPosition = new Vector2(470,-280);
        modalTitle.text = title; modalBody.text = body;
        modalNote.text = owner.HasUnsavedProgress ? "학습 기록 미저장 · 다시 시도해 주세요." : note;
        primary.gameObject.SetActive(false); secondary.gameObject.SetActive(false);
    }
    private static void ActionButton(Button button, string label, Action action)
    {
        button.gameObject.SetActive(true); button.GetComponentInChildren<TMP_Text>().text = label;
        button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => action());
    }
    private void CenterActions()
    {
        Button[] buttons = { primary, secondary };
        int count = 0; foreach (var button in buttons) if (button.gameObject.activeSelf) count++;
        float x = (884 - (count * 260 + (count - 1) * 28)) / 2;
        foreach (var button in buttons)
        {
            if (!button.gameObject.activeSelf) continue;
            ((RectTransform)button.transform).anchoredPosition = new Vector2(x, 0); x += 288;
        }
    }
    public void ShowInvitation()
    {
        Modal("튜토리얼을 체험하시겠습니까?", "약공과 강공, 패링과 회피 등 기본 조작을 직접 익힐 수 있습니다.", "약 5분 · 언제든 종료할 수 있습니다.");
        ActionButton(primary, "튜토리얼 시작", () => owner.StartLesson(0));
        ActionButton(secondary, "바로 게임 시작", owner.Decline);
        CenterActions();
        OverburstGameMenu.Select(primary);
    }
    public void ShowLesson(TutorialLesson lesson)
    {
        Hide(); goal.SetActive(true); tools.SetActive(true);
        baseGoalHeight = lesson == TutorialLesson.World ? 236 : 176;
        shownLesson = lesson;
        progress.text = "조작 튜토리얼 · " + ((int)lesson + 1) + " / " + TutorialLessons.Count;
        heading.text = TutorialLessons.Names[(int)lesson]; instruction.text = TutorialLessons.Description(lesson);
        RefreshKeys();
        Feedback("");
        comparison.SetActive(TutorialLessons.IsParry(lesson));
        for (int i = 0; i < gradeBackgrounds.Length; i++)
            gradeBackgrounds[i].color = (int)TutorialLessons.Grade(lesson) == i ? Color.white : Color.clear;
        worldConfirm.gameObject.SetActive(lesson == TutorialLesson.World);
        skip.gameObject.SetActive(true);
    }
    public void ShowComplete(TutorialProgress state)
    {
        int completed = 0; for (int i = 0; i < TutorialLessons.Count; i++) if ((state.completed & (1 << i)) != 0) completed++;
        Modal("이제 직접 싸워볼 차례입니다", "실습 완료 " + completed + " / " + TutorialLessons.Count + "\n약공으로 모으고, 강공으로 방출하세요.", "");
        ActionButton(primary, "게임으로 돌아가기", owner.Exit);
        CenterActions();
        OverburstGameMenu.Select(primary);
    }
    public void ShowError(string error)
    {
        Modal("훈련 준비를 확인해 주세요", error, "실제 계정의 저장 데이터는 훈련에 사용하지 않습니다.");
        ActionButton(primary, owner.IsTraining ? "복원 다시 시도" : "닫기", owner.Exit);
        CenterActions();
        OverburstGameMenu.Select(primary);
    }
    public void Feedback(string message)
    {
        feedback.text = message;
        feedback.gameObject.SetActive(!string.IsNullOrEmpty(message));
        ((RectTransform)feedback.transform).anchoredPosition = new Vector2(20,
            shownLesson == TutorialLesson.World && worldConfirm.gameObject.activeSelf ? -230 : -174);
        float height = baseGoalHeight + (string.IsNullOrEmpty(message) ? 0 : 28);
        ((RectTransform)goal.transform).sizeDelta = new Vector2(720, height);
        var belowGoal = new Vector2(24, -(24 + height + 16));
        ((RectTransform)comparison.transform).anchoredPosition = belowGoal;
    }
    private void OnEnable() => InputSystem.onActionChange += BindingChanged;
    private void OnDisable() => InputSystem.onActionChange -= BindingChanged;
    private void BindingChanged(object target, InputActionChange change)
    {
        if (change == InputActionChange.BoundControlsChanged && goal && goal.activeSelf) RefreshKeys();
    }
    private void LateUpdate()
    {
        if (goal.activeSelf && shownLesson == TutorialLesson.Potion && AccountGameplaySession.Current != null
            && potionGuideRevision != AccountGameplaySession.Current.Revision) RefreshKeys();
    }
    private void RefreshKeys()
    {
        bool world = shownLesson == TutorialLesson.World;
        keys.gameObject.SetActive(world); keyGuide.gameObject.SetActive(!world);
        if (world) { keys.text = "원소보석을 장착해야 약공 충전과 강공 원소 방출을 사용할 수 있습니다."; return; }
        var asset = PlayerInputFacade.Current?.RuntimeAsset;
        var guide = keyGuide; guide.Clear();
        switch (shownLesson)
        {
            case TutorialLesson.Move:
                guide.Key(asset,"Move",part:"Up"); guide.Key(asset,"Move",part:"Left");
                guide.Key(asset,"Move",part:"Down"); guide.Key(asset,"Move","이동 ·","Right");
                guide.Key(asset,"CombatMode","전투 모드"); break;
            case TutorialLesson.Charge: guide.Key(asset,"Attack","약공"); break;
            case TutorialLesson.Combat:
                guide.Key(asset,"Attack","약공 ·"); guide.Key(asset,"Aim","강공"); break;
            case TutorialLesson.Dash: case TutorialLesson.Roll:
                guide.Text("이동 방향 +"); guide.Key(asset,"Evade","회피 · 탐험 회피에는 무적이 없습니다."); break;
            case TutorialLesson.DashLight:
                guide.Key(asset,"Evade"); guide.Text("→"); guide.Key(asset,"Attack","약공 · 재사용 1.2초"); break;
            case TutorialLesson.DashHeavy:
                guide.Key(asset,"Evade"); guide.Text("→"); guide.Key(asset,"Aim","강공 · 연습 게이지 85%"); break;
            case TutorialLesson.Equipment:
                guide.Key(asset,"Interact","줍기 ·"); guide.Key(asset,"Inventory","인벤토리 ·"); guide.Key(asset,"Equipment","장비"); break;
            case TutorialLesson.Potion:
                guide.Key(asset,"Inventory","열기 →"); guide.MouseRight("물약 선택 → 퀵슬롯 등록 →");
                var session = AccountGameplaySession.Current;
                int slotNumber = session != null ? session.Read().quickSlots.FindIndex(s=>s.consumableContentId == OptionalTutorialController.PotionId)+1 : 0;
                potionGuideRevision = session?.Revision ?? -1;
                if (slotNumber > 0) guide.Key(asset,"QuickSlot"+slotNumber,"사용"); else guide.Text("등록한 키");
                break;
            default: guide.Key(asset,"Aim","강공 / 패링"); break;
        }
        guide.Finish();
    }
}
