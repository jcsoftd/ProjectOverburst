using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Authored corner navigation. Rows grow upward without changing window or inventory ownership.</summary>
[DefaultExecutionOrder(-1900)]
public sealed class OverburstHudMenu : MonoBehaviour
{
    public const string ResourcePath = "UI/HudMenu/PF_OverburstHudMenu_Rpg11";
    public enum Destination { Inventory, Equipment, SkillTree, Settings }
    [Serializable] public sealed class Entry
    {
        public string label;
        public Sprite icon;
        public Sprite hoverIcon;
        public Destination destination;
    }
    public Canvas canvas;
    public Button trigger, outside;
    public Image triggerIcon;
    public Sprite menuIcon, closeIcon;
    public OverburstHudMenuTrigger triggerVisual;
    public CanvasGroup popup;
    public RectTransform dock, panel, viewport, content;
    public ScrollRect scroll;
    public OverburstHudMenuItem rowTemplate;
    public Entry[] entries;
    public bool IsOpen { get; private set; }
    public OverburstHudMenuItem[] Rows { get; private set; } = Array.Empty<OverburstHudMenuItem>();
    public static OverburstHudMenu Instance { get; private set; }
    public static bool IsExpanded => Instance != null && Instance.IsOpen;
    OverburstGameUI owner;
    PlayerInputFacade lockedInput;
    bool initialized, restoreGameplay;
    float animation, slide;
    Vector2 lastRootSize;
    Rect lastSafeArea;
    const float Width = 244, ButtonSize = 58, Edge = 24, Gap = 12, RowHeight = 52, Padding = 8;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    public static OverburstHudMenu Install(OverburstGameUI game)
    {
        if (Instance) return Instance;
        var prefab = Resources.Load<OverburstHudMenu>(ResourcePath);
        if (!prefab) { Debug.LogWarning("[HudMenu] Missing authored corner-menu prefab."); return null; }
        var instance = Instantiate(prefab, game.transform, false);
        instance.owner = game;
        OverburstSkillTreeUI.Instance?.UseExternalEntry(true);
        return instance;
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        canvas.overrideSorting = true; canvas.sortingOrder = 210;
        Initialize(); CloseImmediate();
    }
    void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneUnloaded += SceneUnloaded;
    void OnDisable() { UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= SceneUnloaded; CloseImmediate(); }
    void OnDestroy() { CloseImmediate(); if (Instance == this) Instance = null; }
    void SceneUnloaded(UnityEngine.SceneManagement.Scene scene) => CloseImmediate();

    public void Initialize()
    {
        if (initialized) return;
        trigger.onClick.AddListener(Toggle);
        outside.onClick.AddListener(Close);
        initialized = true;
        SetEntries(entries);
    }

    public void SetEntries(Entry[] values)
    {
        entries = values ?? Array.Empty<Entry>();
        foreach (var row in Rows) if (row) { row.gameObject.SetActive(false); if (Application.isPlaying) Destroy(row.gameObject); else DestroyImmediate(row.gameObject); }
        Rows = new OverburstHudMenuItem[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            var row = Instantiate(rowTemplate, content, false);
            row.name = "Menu Row " + entries[i].destination + " " + i;
            row.gameObject.SetActive(true);
            row.Bind(this, i, entries[i]);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(0, -i * RowHeight);
            rect.sizeDelta = new Vector2(Width - Padding * 2 - 2, RowHeight);
            row.separator.gameObject.SetActive(i > 0);
            Rows[i] = row;
        }
        for (int i = 0; i < Rows.Length; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnUp = Rows[(i + Rows.Length - 1) % Rows.Length].button;
            nav.selectOnDown = Rows[(i + 1) % Rows.Length].button;
            Rows[i].button.navigation = nav;
        }
        trigger.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = Rows.Length > 0 ? Rows[Rows.Length - 1].button : null };
        content.sizeDelta = new Vector2(Width - Padding * 2 - 2, entries.Length * RowHeight);
        FitLayout();
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
    }

    void Update()
    {
        if (!initialized) return;
        if (IsOpen && PlayerInputFacade.Current != null && PlayerInputFacade.Current.UiCancelPressedThisFrame) Close();
        if (IsOpen && PersistentSceneFlow.Instance && PersistentSceneFlow.Instance.IsSwitching) CloseImmediate();
        animation = Mathf.MoveTowards(animation, IsOpen ? 1 : 0, Time.unscaledDeltaTime / .13f);
        slide = Mathf.MoveTowards(slide, IsOpen ? 1 : 0, Time.unscaledDeltaTime / .17f);
        popup.alpha = Ease(animation);
        panel.anchoredPosition = new Vector2(0, ButtonSize + Gap - (1 - Ease(slide)) * 10);
        if (!IsOpen && animation == 0 && slide == 0 && popup.gameObject.activeSelf) popup.gameObject.SetActive(false);
    }
    void LateUpdate()
    {
        if (!initialized) return;
        if (lastRootSize != ((RectTransform)transform).rect.size || lastSafeArea != Screen.safeArea) FitLayout();
        bool available = IsOpen || CanOpen();
        if (trigger.gameObject.activeSelf != available) trigger.gameObject.SetActive(available);
        trigger.interactable = available && entries.Length > 0;
    }
    bool CanOpen()
    {
        var flow = PersistentSceneFlow.Instance;
        return !OverburstGameMenu.IsOpen && !OverburstSkillTreeUI.IsWindowOpen && !GameplayInputBlocker.IsGameplayInputBlocked && (!flow || !flow.IsSwitching);
    }
    public void Toggle() { if (IsOpen) Close(); else Open(); }
    public void Open()
    {
        Initialize();
        if (IsOpen || entries.Length == 0 || (Application.isPlaying && !CanOpen())) return;
        IsOpen = true; popup.gameObject.SetActive(true); outside.gameObject.SetActive(true);
        popup.interactable = popup.blocksRaycasts = true;
        triggerIcon.sprite = closeIcon;
        triggerVisual.Refresh();
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
        FitLayout();
        if (Application.isPlaying)
        {
            TooltipManager.Instance?.HideTooltip();
            GameplayInputBlocker.Block(this);
            lockedInput = PlayerInputFacade.Current;
            restoreGameplay = lockedInput != null && lockedInput.IsGameplayEnabled;
            if (restoreGameplay) lockedInput.DisableGameplay();
            Play(false);
        }
    }
    public void Close()
    {
        IsOpen = false;
        if (popup) popup.interactable = popup.blocksRaycasts = false;
        if (outside) outside.gameObject.SetActive(false);
        if (triggerIcon) triggerIcon.sprite = menuIcon;
        if (triggerVisual) triggerVisual.Refresh();
        GameplayInputBlocker.Unblock(this);
        if (restoreGameplay && lockedInput != null) lockedInput.EnableGameplay();
        restoreGameplay = false; lockedInput = null;
        if (EventSystem.current && EventSystem.current.currentSelectedGameObject && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(content))
            EventSystem.current.SetSelectedGameObject(trigger && trigger.gameObject.activeInHierarchy ? trigger.gameObject : null);
    }
    public void CloseImmediate()
    {
        Close(); animation = slide = 0;
        if (popup) { popup.alpha = 0; popup.gameObject.SetActive(false); }
    }
    public void Activate(int index)
    {
        if (!IsOpen || index < 0 || index >= entries.Length) return;
        var destination = entries[index].destination;
        Play(true); CloseImmediate();
        if (!owner) owner = GetComponentInParent<OverburstGameUI>();
        switch (destination)
        {
            case Destination.Inventory: if (owner) owner.ToggleInventory(); break;
            case Destination.Equipment: if (owner) owner.ToggleEquipment(); break;
            case Destination.SkillTree: OverburstSkillTreeUI.Instance?.Open(); break;
            case Destination.Settings:
                var menu = OverburstGameMenu.Instance;
                if (menu) { menu.Open(); menu.OpenSettings(); }
                break;
        }
        if (Application.isPlaying && !CanOpen()) trigger.gameObject.SetActive(false);
    }
    public void KeepVisible(int index)
    {
        if (!IsOpen || index < 0 || index >= Rows.Length) return;
        float top = index * RowHeight, bottom = top + RowHeight;
        float current = content.anchoredPosition.y;
        float next = top < current ? top : bottom > current + viewport.rect.height ? bottom - viewport.rect.height : current;
        content.anchoredPosition = new Vector2(0, Mathf.Clamp(next, 0, Mathf.Max(0, content.rect.height - viewport.rect.height)));
    }
    public void FitLayout()
    {
        var root = (RectTransform)transform;
        var safe = Screen.safeArea;
        float sx = Screen.width > 0 ? root.rect.width / Screen.width : 1;
        float sy = Screen.height > 0 ? root.rect.height / Screen.height : 1;
        float right = Screen.width > 0 ? (Screen.width - safe.xMax) * sx : 0;
        float bottom = Screen.height > 0 ? safe.yMin * sy : 0;
        float top = Screen.height > 0 ? (Screen.height - safe.yMax) * sy : 0;
        dock.anchoredPosition = new Vector2(-Edge - right, Edge + bottom);
        float natural = entries.Length * RowHeight + Padding * 2 + 2;
        float maximum = Mathf.Max(RowHeight + Padding * 2 + 2, root.rect.height - top - bottom - Edge * 2 - ButtonSize - Gap);
        panel.sizeDelta = new Vector2(Width, Mathf.Min(natural, maximum));
        lastRootSize = root.rect.size; lastSafeArea = safe;
    }
    void Play(bool click)
    {
        var menu = OverburstGameMenu.Instance;
        var clip = menu ? (click ? menu.clickClip : menu.openClip) : null;
        if (menu && menu.audioSource && clip) menu.audioSource.PlayOneShot(clip, .45f);
    }
    static float Ease(float progress)
    {
        if(progress<=0||progress>=1)return progress;
        float t=progress;
        for(int i=0;i<5;i++){float u=1-t;float x=3*u*u*t*.25f+3*u*t*t*.25f+t*t*t;float dx=3*u*u*.25f+6*u*t*(.25f-.25f)+3*t*t*(1-.25f);if(dx>.0001f)t=Mathf.Clamp01(t-(x-progress)/dx);}
        float v=1-t;return 3*v*v*t*.1f+3*v*t*t+t*t*t;
    }
}
