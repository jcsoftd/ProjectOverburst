using System;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 2026-10-01 ESC 메뉴. 창이 하나도 열려 있지 않을 때 ESC로 열고, 여는 동안 게임을 멈춘다.
/// 멈춤은 OverburstTimeEffectArbiter가 맡아 패링 슬로우·히트스톱 도중에 열었다 닫아도 남은 연출이 이어진다.
/// 메뉴 안에서 ESC는 한 단계 뒤로(확인창 → 설정 → 메인 → 닫기)다. 화면은 OverburstGameMenuBuilder가 저작한다.
/// Enabled=false면 ESC가 예전처럼 창 닫기에만 쓰인다(롤백 스위치).
/// </summary>
[DefaultExecutionOrder(2000)]
public sealed class OverburstGameMenu : MonoBehaviour
{
    public const string ResourcePath = "UI/Menu/PF_OverburstGameMenu_Rpg11";
    public static bool Enabled = true;
    public static bool IsOpen { get; private set; }
    public static OverburstGameMenu Instance { get; private set; }

    private const float FadeSeconds = .12f;

    [Header("Frame")]
    public CanvasGroup group;
    public RectTransform mainPanel;
    public Text title;
    public Text subtitle;
    public Button resumeButton;
    public Button settingsButton;
    public Button returnButton;
    public Text returnLabel;
    public Button quitButton;
    public OverburstSettingsPanel settings;

    [Header("Confirm")]
    public GameObject modal;
    public Text modalHeadline;
    public Text modalDescription;
    public Button modalConfirm;
    public Text modalConfirmLabel;
    public Button modalCancel;
    public Text modalCancelLabel;

    [Header("Sound")]
    public AudioSource audioSource;
    public AudioClip openClip;
    public AudioClip closeClip;
    public AudioClip hoverClip;
    public AudioClip clickClip;
    public AudioClip confirmClip;

    private Action modalConfirmAction;
    private Action modalCancelAction;
    private bool restoreGameplayMap;
    private float fadeTarget;
    private GameObject lastSelection;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOpen = false;
        Instance = null;
    }

    // OverburstGameUI가 게임 UI 캔버스 아래에 한 번 설치한다(씬은 바꾸지 않는다).
    public static OverburstGameMenu Install(Transform canvasRoot)
    {
        if (!Enabled || canvasRoot == null) return null;
        if (Instance != null) return Instance;
        var prefab = Resources.Load<OverburstGameMenu>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning("[OverburstGameMenu] 메뉴 프리팹이 없습니다: Resources/" + ResourcePath);
            return null;
        }
        var menu = Instantiate(prefab, canvasRoot, false);
        menu.name = prefab.name;
        return menu;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // 프리팹 루트 캔버스의 정렬 덮어쓰기는 저장 때 풀리므로(루트 캔버스로 취급) 설치된 뒤 켠다: HUD·창·툴팁보다 위.
        var canvas = GetComponent<Canvas>();
        if (canvas != null) { canvas.overrideSorting = true; canvas.sortingOrder = 500; }
        resumeButton.onClick.AddListener(Close);
        settingsButton.onClick.AddListener(OpenSettings);
        returnButton.onClick.AddListener(AskReturn);
        quitButton.onClick.AddListener(AskQuit);
        modalConfirm.onClick.AddListener(() => ResolveModal(true));
        modalCancel.onClick.AddListener(() => ResolveModal(false));
        if (audioSource != null) audioSource.ignoreListenerPause = true;
        settings.Bind(this);
        HideImmediate();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            if (IsOpen) ReleasePause();
            Instance = null;
            IsOpen = false;
        }
    }

    private void OnDisable()
    {
        if (IsOpen && Instance == this) Close();
    }

    private void Update()
    {
        UpdateFade();
        if (IsOpen) KeepSelection();

        var input = PlayerInputFacade.Current;
        if (input == null || !input.UiCancelPressedThisFrame) return;
        if (!IsOpen)
        {
            if (Enabled && OverburstGameMenuGate.EscapeBelongsToMenu) Open();
            return;
        }
        Back();
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        TooltipManager.Instance?.HideTooltip();
        OverburstTimeEffectArbiter.SetPaused(true);
        AudioListener.pause = true;
        GameplayInputBlocker.Block(this);
        var input = PlayerInputFacade.Current;
        restoreGameplayMap = input != null && input.IsGameplayEnabled;
        if (restoreGameplayMap) input.DisableGameplay(); // 인벤토리·퀵슬롯 같은 게임 키가 메뉴 뒤에서 반응하지 않게 한다.

        settings.gameObject.SetActive(false);
        modal.SetActive(false);
        mainPanel.gameObject.SetActive(true);
        group.gameObject.SetActive(true);
        RefreshMain(); // 켠 뒤에 해야 버튼 묶음 높이가 계산된다(꺼진 채로는 0).
        group.alpha = 0f;
        group.blocksRaycasts = true;
        group.interactable = true;
        fadeTarget = 1f;
        Play(openClip, .7f);
        Select(resumeButton);
    }

    public void Close()
    {
        if (!IsOpen) return;
        settings.CancelRebind();
        IsOpen = false;
        ReleasePause();
        OverburstGameSettings.SaveIfDirty();
        group.blocksRaycasts = false;
        group.interactable = false;
        fadeTarget = 0f;
        Play(closeClip, .6f);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void ReleasePause()
    {
        OverburstTimeEffectArbiter.SetPaused(false);
        AudioListener.pause = false;
        GameplayInputBlocker.Unblock(this);
        if (restoreGameplayMap) PlayerInputFacade.Current?.EnableGameplay();
        restoreGameplayMap = false;
    }

    // ESC 한 번 = 한 단계 뒤로.
    public void Back()
    {
        if (settings.ConsumesEscape) return; // 키 입력을 기다리는 중: ESC는 그 입력을 취소한다.
        if (modal.activeSelf) { ResolveModal(false); return; }
        if (settings.gameObject.activeSelf) { CloseSettings(); return; }
        Close();
    }

    public void OpenSettings()
    {
        Play(clickClip, .8f);
        mainPanel.gameObject.SetActive(false);
        settings.gameObject.SetActive(true);
        settings.Show();
    }

    public void CloseSettings()
    {
        settings.CancelRebind();
        OverburstGameSettings.SaveIfDirty();
        settings.gameObject.SetActive(false);
        mainPanel.gameObject.SetActive(true);
        Play(closeClip, .5f);
        Select(settingsButton);
    }

    // 확인창: 기본 선택은 언제나 안전한 쪽(취소)이다.
    public void Confirm(string headline, string description, string confirmLabel, string cancelLabel, Action onConfirm, Action onCancel = null)
    {
        modalHeadline.text = headline;
        modalDescription.text = description;
        modalConfirmLabel.text = confirmLabel;
        modalCancelLabel.text = cancelLabel;
        modalConfirmAction = onConfirm;
        modalCancelAction = onCancel;
        lastSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        modal.SetActive(true);
        modal.transform.SetAsLastSibling();
        Play(openClip, .5f);
        Select(modalCancel);
    }

    public void SetModalDescription(string description)
    {
        if (modal.activeSelf) modalDescription.text = description;
    }

    public void CancelModal() => ResolveModal(false);

    public bool ModalOpen => modal != null && modal.activeSelf;

    private void ResolveModal(bool confirmed)
    {
        if (!modal.activeSelf) return;
        modal.SetActive(false);
        Action action = confirmed ? modalConfirmAction : modalCancelAction;
        modalConfirmAction = modalCancelAction = null;
        Play(confirmed ? confirmClip : closeClip, .6f);
        if (lastSelection != null && lastSelection.activeInHierarchy) EventSystem.current?.SetSelectedGameObject(lastSelection);
        action?.Invoke();
    }

    private void RefreshMain()
    {
        bool dungeon = WorldSessionState.Phase == WorldPhase.Run;
        var run = AccountGameplaySession.Current?.ReadRun();
        bool cleared = dungeon && run != null && run.phase == RunPhase.BossCleared;
        title.text = "일시정지";
        subtitle.text = (dungeon ? "던전" : "은신처") + "  ·  게임이 멈춰 있습니다";
        returnButton.gameObject.SetActive(dungeon);
        returnLabel.text = cleared ? "포탈로 귀환" : "은신처로 귀환";
        FitMainPanel();

        // 보이는 버튼만 위아래로 잇는다(방향키·패드).
        var order = dungeon
            ? new Selectable[] { resumeButton, settingsButton, returnButton, quitButton }
            : new Selectable[] { resumeButton, settingsButton, quitButton };
        for (int i = 0; i < order.Length; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnUp = order[(i + order.Length - 1) % order.Length];
            nav.selectOnDown = order[(i + 1) % order.Length];
            order[i].navigation = nav;
        }
    }

    // 메인 기둥 높이를 버튼 수에 맞춘다(제목 위 여백과 안내 아래 여백을 같게).
    public void FitMainPanel()
    {
        var group = resumeButton.transform.parent as RectTransform;
        if (group == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(group);
        float height = -group.anchoredPosition.y + group.rect.height + 190f;
        mainPanel.sizeDelta = new Vector2(mainPanel.sizeDelta.x, height);
    }

    private void AskReturn()
    {
        Play(clickClip, .8f);
        var run = AccountGameplaySession.Current?.ReadRun();
        bool cleared = run != null && run.phase == RunPhase.BossCleared;
        if (cleared)
            Confirm("포탈로 귀환할까요?", "보스 보상을 챙겨 은신처로 돌아갑니다.", "귀환", "취소", () => RequestReturn(true));
        else
            Confirm("은신처로 돌아갈까요?", "이번 탐험을 포기합니다.\n창고로 보내지 않은 아이템은 잃습니다.", "포기하고 귀환", "계속하기", () => RequestReturn(false));
    }

    private void RequestReturn(bool portal)
    {
        var driver = PersistentSceneFlow.Instance != null ? PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>() : null;
        Close();
        if (driver == null) { Debug.LogWarning("[OverburstGameMenu] RunLifetimeDriver를 찾지 못해 귀환하지 못했습니다."); return; }
        if (portal) driver.RequestPortalExit();
        else driver.RequestAbandon();
    }

    private void AskQuit()
    {
        Play(clickClip, .8f);
        bool dungeon = WorldSessionState.Phase == WorldPhase.Run;
        Confirm("게임을 종료할까요?",
            dungeon ? "진행 중인 탐험은 다음 실행 때 실패로 처리됩니다.\n창고로 보내지 않은 아이템은 잃습니다." : "진행 상황은 자동으로 저장됩니다.",
            "종료", "취소", QuitGame);
    }

    private static void QuitGame()
    {
        OverburstGameSettings.SaveIfDirty();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
#else
        Application.Quit();
#endif
    }

    public void PlayHover() => Play(hoverClip, .35f);
    public void PlayClick() => Play(clickClip, .8f);

    private void Play(AudioClip clip, float volume)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, volume * OverburstGameSettings.UiVolume);
    }

    public static void Select(Selectable target)
    {
        if (target == null || EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    internal static bool NavigationRequestedThisFrame
    {
        get
        {
            var keyboard = Keyboard.current;
            bool keys = keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
                keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame ||
                keyboard.wKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame);
            var pad = Gamepad.current;
            return keys || (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
                pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame || pad.leftStick.ReadValue().sqrMagnitude > .25f));
        }
    }

    // 마우스로 빈 곳을 눌러 선택이 풀려도 방향키로 바로 이어 쓰게 한다.
    private void KeepSelection()
    {
        var system = EventSystem.current;
        if (system == null || settings.ConsumesEscape) return;
        var current = system.currentSelectedGameObject;
        if (current != null && current.activeInHierarchy && current.transform.IsChildOf(transform)) return;
        if (!NavigationRequestedThisFrame) return;
        if (modal.activeSelf) Select(modalCancel);
        else if (settings.gameObject.activeSelf) settings.SelectDefault();
        else Select(resumeButton);
    }

    private void UpdateFade()
    {
        if (group == null || !group.gameObject.activeSelf) return;
        float step = Time.unscaledDeltaTime / FadeSeconds;
        group.alpha = Mathf.MoveTowards(group.alpha, fadeTarget, step);
        if (fadeTarget <= 0f && group.alpha <= 0f) group.gameObject.SetActive(false);
    }

    private void HideImmediate()
    {
        fadeTarget = 0f;
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        group.gameObject.SetActive(false);
        modal.SetActive(false);
    }
}
