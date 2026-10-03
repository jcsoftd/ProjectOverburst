using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Overburst.DebugTools
{
    /// <summary>
    /// OVERBURST 디버그 창. 정식 프리팹을 한 번 로드해 DontDestroyOnLoad로 둔다.
    /// F1 창, F2 핀 오버레이, Ctrl+F 검색. 창·오버레이 위에 포인터가 있거나 입력칸에 글자를 넣는 동안
    /// <see cref="GameplayInputBlocker"/>로 게임 입력을 막는다. 게임 코드보다 먼저 돌도록 실행 순서를 앞당긴다.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class DebugHub : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const float RefreshInterval = 0.2f;

        private readonly Dictionary<Key, DebugItem> hotkeys = new Dictionary<Key, DebugItem>();
        private readonly HashSet<string> persistApplied = new HashSet<string>();
        private readonly HashSet<string> warnedHotkeys = new HashSet<string>();
        private RectTransform canvasRect;
        private DebugHubWindow window;
        private DebugOverlay overlay;
        private DebugOptionsItem focusedChoice;
        private float nextRefresh;
        private int hotkeyRevision = -1;
        private int persistRevision = -1;
        private bool blocking;
        private bool pointerOverWindow;
        private EventSystem fallbackEventSystem;
        private bool typingEndedThisFrame;

        public static DebugHub Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.window != null && Instance.window.Visible;
        /// <summary>씬 전환에도 남는 오브젝트. 전환을 넘어 이어져야 하는 디버그 동작(던전 입장 등)을 여기에 붙인다.</summary>
        public static GameObject Host => Instance != null ? Instance.gameObject : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null)
                return;
            GameObject prefab = Resources.Load<GameObject>(DebugHubView.ResourcePath);
            if (prefab == null)
                throw new InvalidOperationException("PF_OverburstDebugHub 프리팹이 없어요. Debug Hub 빌더로 제작하세요.");
            GameObject go = Instantiate(prefab);
            go.name = "OVERBURST Debug Hub";
            DontDestroyOnLoad(go);
            go.SetActive(true);
        }

        public static void Open()
        {
            if (Instance != null)
                Instance.SetWindowVisible(true);
        }

        public static void Close()
        {
            if (Instance != null)
                Instance.SetWindowVisible(false);
        }

        public static void Toggle()
        {
            if (Instance != null)
                Instance.SetWindowVisible(!IsOpen);
        }

        public static void OpenTab(string tab)
        {
            if (Instance == null)
                return;
            Instance.SetWindowVisible(true);
            Instance.window.SelectTab(tab);
        }

        public static void ResetLayout()
        {
            if (Instance != null)
                Instance.window.ResetLayout();
        }

        /// <summary>←/→ 키로 넘길 선택 항목. 선택 행을 누르면 바뀐다.</summary>
        internal static void Focus(DebugOptionsItem item)
        {
            if (Instance != null)
                Instance.focusedChoice = item;
        }

        internal void JumpTo(string id)
        {
            DebugItem item = DebugRegistry.Find(id);
            if (item == null)
                return;
            SetWindowVisible(true);
            window.JumpTo(item);
        }

        private void Awake()
        {
            InitializeRuntime();
        }

        private void OnEnable()
        {
            // Play 중 스크립트 재로딩은 Awake를 다시 호출하지 않는다.
            // 직렬화되지 않는 창·오버레이와 정적 Instance를 기존 프리팹에 다시 연결한다.
            if (Application.isPlaying)
                InitializeRuntime();
        }

        private void InitializeRuntime()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (window != null && overlay != null)
                return;
            gameObject.layer = 5;
            BuildCanvas();
            DebugHubView view = GetComponent<DebugHubView>();
            if (view == null || view.Style == null)
                throw new InvalidOperationException("디버그 프리팹의 View/Style 연결을 확인하세요.");
            DebugUi.Initialize(view.Style);
            fallbackEventSystem = view.FallbackEventSystem;
            bool windowVisible = view.Window != null && view.Window.gameObject.activeSelf;
            overlay = new DebugOverlay(this, canvasRect);
            window = new DebugHubWindow(canvasRect);
            window.SetVisible(windowVisible);
            DebugRuntime.ConfirmHandler = HandleConfirm;
            DebugRuntime.Executed -= HandleExecuted;
            DebugRuntime.Executed += HandleExecuted;
            DebugLogCapture.Install();
        }

        private void OnDisable()
        {
            if (blocking)
                GameplayInputBlocker.Unblock(this);
            blocking = false;
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            Instance = null;
            GameplayInputBlocker.Unblock(this);
            DebugRuntime.Executed -= HandleExecuted;
            DebugRuntime.ConfirmHandler = null;
            DebugTime.RestoreNormal();
            window?.Dispose();
            overlay?.SaveState();
            PlayerPrefs.Save();
        }

        private void Update()
        {
            typingEndedThisFrame = false;
            DebugPerf.Tick(Time.unscaledDeltaTime);
            DebugPerfRecorder.Tick(Time.unscaledDeltaTime);
            DebugLogCapture.Drain();
            DebugTime.Tick();
            ApplyPersistedValues();
            if (fallbackEventSystem != null && fallbackEventSystem.gameObject.activeSelf)
                EnsureEventSystem();
            UpdatePointer();
            HandleKeys();
            UpdateInputBlock();
            window.Tick();

            if (Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            window.Refresh();
            overlay.Refresh(IsOpen);
        }

        private void BuildCanvas()
        {
            canvasRect = (RectTransform)transform;
        }

        private void SetWindowVisible(bool visible)
        {
            if (window == null || window.Visible == visible)
                return;
            if (visible)
                EnsureEventSystem();
            window.SetVisible(visible);
            overlay.Refresh(visible);
        }

        private void UpdatePointer()
        {
            pointerOverWindow = false;
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            Vector2 position = mouse.position.ReadValue();
            pointerOverWindow = window.Contains(position) || overlay.Contains(position);
        }

        private void UpdateInputBlock()
        {
            // 검색창이 Esc를 처리한 프레임은 뒤에 실행되는 게임 메뉴에 같은 입력을 넘기지 않는다.
            bool want = pointerOverWindow || (window.Visible && window.IsTyping) || typingEndedThisFrame;
            if (want)
                GameplayInputBlocker.Block(this); // 씬 전환이 차단을 비워도 다음 프레임에 다시 건다.
            else if (blocking)
                GameplayInputBlocker.Unblock(this);
            blocking = want;
        }

        private void HandleKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (window.Visible && window.IsTyping)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    window.BlurInputs();
                    typingEndedThisFrame = true;
                }
                return;
            }
            if (IsTypingElsewhere())
                return;

            bool ctrl = keyboard.ctrlKey.isPressed;
            if (keyboard.f1Key.wasPressedThisFrame)
                Toggle();
            if (keyboard.f2Key.wasPressedThisFrame)
                overlay.ToggleVisible();
            if (ctrl && keyboard.fKey.wasPressedThisFrame)
            {
                SetWindowVisible(true);
                window.FocusSearch();
                return;
            }

            if (window.Visible && pointerOverWindow && focusedChoice != null
                && focusedChoice.IsVisible && focusedChoice.IsEnabled)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame)
                    focusedChoice.Step(-1);
                else if (keyboard.rightArrowKey.wasPressedThisFrame)
                    focusedChoice.Step(1);
            }

            RefreshHotkeys();
            foreach (KeyValuePair<Key, DebugItem> pair in hotkeys)
            {
                DebugItem item = pair.Value;
                if (item.HotkeyCtrl != ctrl || !keyboard[pair.Key].wasPressedThisFrame)
                    continue;
                if (item.IsVisible)
                    item.RunHotkey();
            }
        }

        private void RefreshHotkeys()
        {
            if (hotkeyRevision == DebugRegistry.Revision)
                return;
            hotkeyRevision = DebugRegistry.Revision;
            hotkeys.Clear();
            foreach (DebugItem item in DebugRegistry.AllItems)
            {
                Key key = item.HotkeyKey;
                if (key == Key.None)
                    continue;
                if (key == Key.F1 || key == Key.F2 || hotkeys.ContainsKey(key))
                {
                    if (warnedHotkeys.Add(item.Id))
                        Debug.LogWarning($"[DebugHub] '{item.Id}'의 단축키 {key}가 이미 쓰이고 있어요. 먼저 등록한 쪽만 동작해요.");
                    continue;
                }
                hotkeys.Add(key, item);
            }
        }

        private void ApplyPersistedValues()
        {
            if (persistRevision == DebugRegistry.Revision)
                return;
            persistRevision = DebugRegistry.Revision;
            foreach (DebugItem item in DebugRegistry.AllItems)
            {
                if (!item.persist || !item.HasValue || !persistApplied.Add(item.Id))
                    continue;
                if (!DebugPrefs.TryLoadValue(item.Id, out string value))
                    continue;
                try
                {
                    item.ApplyValue(value);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void HandleConfirm(DebugItem item, Action proceed)
        {
            SetWindowVisible(true);
            window.ShowConfirm(item.ConfirmText, proceed);
        }

        private void HandleExecuted(DebugRuntime.Record record)
        {
            window.ShowRecord(record);
            if (window.Visible)
                window.RefreshRows();
            overlay.Refresh(IsOpen);
        }

        private static bool IsTypingElsewhere()
        {
            EventSystem system = EventSystem.current;
            GameObject selected = system != null ? system.currentSelectedGameObject : null;
            if (selected == null)
                return false;
            if (selected.TryGetComponent(out TMP_InputField tmpField) && tmpField.isFocused)
                return true;
            return selected.TryGetComponent(out InputField legacyField) && legacyField.isFocused;
        }

        private static void EnsureEventSystem()
        {
            EventSystem fallback = Instance != null ? Instance.fallbackEventSystem : null;
            foreach (EventSystem system in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            {
                if (system == fallback)
                    continue;
                if (fallback != null)
                    fallback.gameObject.SetActive(false);
                return;
            }
            if (fallback == null)
                throw new InvalidOperationException("디버그 프리팹의 예비 EventSystem을 연결하세요.");
            fallback.gameObject.SetActive(true);
        }
#endif
    }
}
