using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class OverburstDeathScreen : MonoBehaviour
{
    public const string ResourcePath = "UI/Death/PF_OverburstDeathScreen";
    public Canvas canvas;
    public CanvasGroup headingGroup, detailsGroup, actionGroup;
    public TMP_Text location, message, loss, keep, buttonLabel, status;
    public Button returnButton;
    public AudioSource sound;
    public AudioClip lowBoom150;
    public RawImage worldBackdrop;
    private RenderTexture capturedWorld;
    public bool IsOpen { get; private set; }
    public int SoundPlayCount { get; private set; }
    public float Elapsed => IsOpen ? Time.unscaledTime - openedAt : 0;
    private float openedAt;
    private bool played, ready, inputReleased;
    private Action confirm;
    private GameObject previousSelection;
    private bool cursorVisible;
    private CursorLockMode cursorLock;

    public static OverburstDeathScreen Create(Transform owner, Action onConfirm)
    {
        var prefab = Resources.Load<OverburstDeathScreen>(ResourcePath);
        if (!prefab) throw new InvalidOperationException("사망 화면 프리팹이 없습니다.");
        var screen = Instantiate(prefab, owner, false);
        screen.name = prefab.name;
        screen.confirm = onConfirm;
        return screen;
    }
    private void Awake()
    {
        canvas.overrideSorting = true; canvas.sortingOrder = 900;
        returnButton.onClick.AddListener(() => { if (IsOpen && returnButton.interactable) confirm?.Invoke(); });
        sound.ignoreListenerPause = true;
        canvas.enabled = false;
    }
    public void Show(bool mapRun, string region)
    {
        if (IsOpen) return;
        IsOpen = true; openedAt = Time.unscaledTime; played = ready = inputReleased = false;
        previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        cursorVisible = Cursor.visible; cursorLock = Cursor.lockState;
        Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        canvas.enabled = true;
        location.text = mapRun ? region + " · 지도 런" : "야외";
        message.text = mapRun ? "이번 원정이 끝났습니다." : "마을에서 다시 준비할 수 있습니다.";
        loss.text = mapRun ? "미반출 전리품은 잃습니다." : "보유한 전리품은 유지됩니다.";
        keep.text = mapRun ? "기존 보유품 · 전송품 · 획득 경험치는 유지됩니다." : "마을로 돌아가 체력을 회복합니다.";
        headingGroup.alpha = detailsGroup.alpha = actionGroup.alpha = 0;
        returnButton.interactable = false;
        SetSaving();
    }
    public void SetSaving() { ready = false; buttonLabel.text = "정산 저장 중…"; status.text = "원정 결과를 저장하고 있습니다."; }
    public void SetReady() { ready = true; buttonLabel.text = "마을로 돌아가기"; status.text = ""; }
    public void SetError(bool returning)
    {
        ready = true;
        buttonLabel.text = returning ? "마을 이동 다시 시도" : "정산 다시 시도";
        status.text = returning ? "마을로 이동하지 못했습니다. 다시 시도해 주세요." : "정산을 저장하지 못했습니다. 다시 시도해 주세요.";
    }
    public void SetReturning() { ready = false; returnButton.interactable = false; buttonLabel.text = "마을로 이동 중…"; status.text = "마을로 돌아가고 있습니다."; }
    public void CaptureWorld()
    {
        var camera = Camera.main;
        if (!camera || capturedWorld) return;
        int width = Mathf.Clamp(Screen.width, 64, 1920);
        int height = Mathf.Max(64, Mathf.RoundToInt(width * (float)Screen.height / Mathf.Max(1, Screen.width)));
        var previous = camera.targetTexture;
        capturedWorld = RenderTexture.GetTemporary(width, height, 24);
        try { camera.targetTexture = capturedWorld; camera.Render(); worldBackdrop.texture = capturedWorld; worldBackdrop.enabled = true; }
        finally { camera.targetTexture = previous; }
    }
    private void Update()
    {
        if (!IsOpen) return;
        float elapsed = Elapsed;
        headingGroup.alpha = Mathf.Clamp01((elapsed - .55f) / .65f);
        detailsGroup.alpha = Mathf.Clamp01((elapsed - 1.05f) / .45f);
        actionGroup.alpha = Mathf.Clamp01((elapsed - 1.35f) / .4f);
        if (!played && elapsed >= .55f)
        {
            played = true; sound.clip = lowBoom150; sound.volume = .4041f; sound.Play(); SoundPlayCount++;
        }
        if (elapsed >= 1.35f && !(PlayerInputFacade.Current?.UiSubmitHeld ?? false)
            && !(Mouse.current?.leftButton.isPressed ?? false)) inputReleased = true;
        bool enabled = elapsed >= 1.35f && inputReleased && ready;
        if (enabled && !returnButton.interactable && EventSystem.current)
            EventSystem.current.SetSelectedGameObject(returnButton.gameObject);
        returnButton.interactable = enabled;
    }
    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false; sound.Stop(); canvas.enabled = false; returnButton.interactable = false;
        worldBackdrop.texture = null; worldBackdrop.enabled = false;
        if (capturedWorld) { RenderTexture.ReleaseTemporary(capturedWorld); capturedWorld = null; }
        Cursor.visible = cursorVisible; Cursor.lockState = cursorLock;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(previousSelection);
    }
    private void OnDisable() => Hide();
}
