using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class TutorialView : MonoBehaviour
{
    public GameObject goal, tools, comparison, shade, modal, chapters, practice, footer;
    public TMP_Text progress, heading, instruction, keys, feedback, modalTitle, modalBody, modalNote;
    public Button retry, skip, exit, primary, secondary, tertiary, worldConfirm;
    public Button[] chapterButtons, energyButtons;
    public Image[] gradeBackgrounds;
    private OptionalTutorialController owner;
    private static readonly int[] ChapterStarts = { 1, 3, 5, 7, 8, 9, 11 };
    private static readonly string[] ChapterNames = { "약공·강공", "대시·구르기", "대시 공격", "불완전 패링", "일반 패링", "완벽 패링", "장비·물약" };

    public void Bind(OptionalTutorialController controller)
    {
        owner = controller;
        retry.onClick.AddListener(owner.Retry); skip.onClick.AddListener(owner.Skip); exit.onClick.AddListener(owner.Exit);
        worldConfirm.onClick.AddListener(owner.ConfirmWorld);
        for (int i = 0; i < chapterButtons.Length; i++)
        { int chapter = ChapterStarts[i]; chapterButtons[i].onClick.AddListener(() => owner.StartLesson(chapter)); }
        float[] amounts = { .2f, .5f, .85f };
        for (int i = 0; i < energyButtons.Length; i++)
        { float amount = amounts[i]; energyButtons[i].onClick.AddListener(() => owner.SetPracticeEnergy(amount)); }
    }
    public void Hide()
    {
        goal.SetActive(false); tools.SetActive(false); comparison.SetActive(false); shade.SetActive(false);
        modal.SetActive(false); practice.SetActive(false); footer.SetActive(false);
    }
    private void Modal(string title, string body, string note)
    {
        Hide(); shade.SetActive(true); modal.SetActive(true); chapters.SetActive(false); footer.SetActive(true);
        var rect = (RectTransform)modal.transform; rect.sizeDelta = new Vector2(980,420); rect.anchoredPosition = new Vector2(470,-280);
        modalTitle.text = title; modalBody.text = body; modalNote.text = note;
        primary.gameObject.SetActive(false); secondary.gameObject.SetActive(false); tertiary.gameObject.SetActive(false);
    }
    private static void ActionButton(Button button, string label, Action action)
    {
        button.gameObject.SetActive(true); button.GetComponentInChildren<TMP_Text>().text = label;
        button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => action());
    }
    private void CenterActions()
    {
        Button[] buttons = { primary, secondary, tertiary };
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
    public void ShowChapters(TutorialProgress state)
    {
        Modal("필요한 조작부터 익혀보세요", "항목을 선택하면 해당 실습부터 시작합니다.", "완료와 건너뛴 항목은 별도로 기록됩니다.");
        chapters.SetActive(true);
        var rect = (RectTransform)modal.transform; rect.sizeDelta = new Vector2(980,660); rect.anchoredPosition = new Vector2(470,-185);
        for (int i = 0; i < chapterButtons.Length; i++)
            chapterButtons[i].GetComponentInChildren<TMP_Text>().text = ChapterNames[i]
                + ((state.completed & (1 << ChapterStarts[i])) != 0 ? " · 완료" : (state.skipped & (1 << ChapterStarts[i])) != 0 ? " · 건너뜀" : "");
        ActionButton(primary, "처음부터", () => owner.StartLesson(0));
        ActionButton(secondary, "자유 연습", owner.StartFreePractice);
        ActionButton(tertiary, owner.IsTraining ? "훈련 종료" : "닫기", owner.Exit);
        CenterActions();
        OverburstGameMenu.Select(chapterButtons[0]);
    }
    public void ShowLesson(TutorialLesson lesson)
    {
        Hide(); goal.SetActive(true); tools.SetActive(true);
        progress.text = "조작 튜토리얼 · " + ((int)lesson + 1) + " / " + TutorialLessons.Count;
        heading.text = TutorialLessons.Names[(int)lesson]; instruction.text = TutorialLessons.Description(lesson);
        keys.text = Keys(lesson);
        Feedback("");
        comparison.SetActive(TutorialLessons.IsParry(lesson));
        for (int i = 0; i < gradeBackgrounds.Length; i++)
            gradeBackgrounds[i].color = (int)TutorialLessons.Grade(lesson) == i ? new Color(.16f, .157f, .12f, 1) : Color.clear;
        worldConfirm.gameObject.SetActive(lesson == TutorialLesson.World);
        skip.gameObject.SetActive(true);
    }
    public void ShowPractice()
    {
        ShowLesson(TutorialLesson.PerfectParry); progress.text = "자유 연습";
        heading.text = "원하는 조작을 자유롭게 연습하세요";
        instruction.text = "약공으로 모으고 강공으로 방출하세요.\n아래 버튼으로 패링 비교용 게이지를 준비할 수 있습니다.";
        keys.text = Key("Attack") + " 약공 · " + Key("Aim") + " 강공 · " + Key("Evade") + " 회피";
        comparison.SetActive(false); practice.SetActive(true); skip.gameObject.SetActive(false);
    }
    public void ShowComplete(TutorialProgress state)
    {
        int completed = 0; for (int i = 0; i < TutorialLessons.Count; i++) if ((state.completed & (1 << i)) != 0) completed++;
        Modal("이제 직접 싸워볼 차례입니다", "실습 완료 " + completed + " / " + TutorialLessons.Count + "\n약공으로 모으고, 강공으로 방출하세요.", "마을 메뉴의 조작 튜토리얼에서 언제든 다시 연습할 수 있습니다.");
        ActionButton(primary, "게임으로 돌아가기", owner.Exit);
        ActionButton(secondary, "항목 선택", owner.ShowChapters);
        ActionButton(tertiary, "자유 연습", owner.StartFreePractice);
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
        ((RectTransform)goal.transform).sizeDelta = new Vector2(720, string.IsNullOrEmpty(message) ? 176 : 204);
    }
    private static string Key(string name)
    {
        var action = PlayerInputFacade.Current?.RuntimeAsset?.FindAction(PlayerInputFacade.GameplayMapName + "/" + name);
        if (action == null) return "[미지정]";
        if (name == "Move") return "[" + action.GetBindingDisplayString() + "]";
        int index = OverburstKeyBindingRow.ResolveBindingIndex(action, null);
        return "[" + (index >= 0 ? OverburstKeyBindingRow.ShortName(action.bindings[index].effectivePath) : "미지정") + "]";
    }
    private static string Keys(TutorialLesson lesson)
    {
        switch (lesson)
        {
            case TutorialLesson.Move: return Key("Move") + " 이동 · " + Key("CombatMode") + " 전투 모드";
            case TutorialLesson.Charge: return Key("Attack") + " 약공";
            case TutorialLesson.Dash: case TutorialLesson.Roll: return "이동 방향 + " + Key("Evade") + " 회피 · 탐험 회피에는 무적이 없습니다.";
            case TutorialLesson.DashLight: return Key("Evade") + " → " + Key("Attack") + " · 재사용 1.2초";
            case TutorialLesson.DashHeavy: return Key("Evade") + " → " + Key("Aim") + " · 연습 게이지 85%";
            case TutorialLesson.Equipment: return Key("Interact") + " 줍기 · " + Key("Inventory") + " 인벤토리 · " + Key("Equipment") + " 장비";
            case TutorialLesson.Potion: return Key("Inventory") + " 열기 → 물약 우클릭 → 퀵슬롯 등록 → 등록한 키";
            case TutorialLesson.World: return "원소보석을 장착해야 약공 충전과 강공 원소 방출을 사용할 수 있습니다.";
            default: return Key("Aim") + " 강공 / 패링";
        }
    }
}
