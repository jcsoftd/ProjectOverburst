#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
    /// OVERBURST 디버그 창. 씬에 두지 않고 Play 시작 때 스스로 만들어 DontDestroyOnLoad로 둔다.
    /// F1 창, F2 핀 오버레이, Ctrl+F 검색. 창·오버레이 위에 포인터가 있거나 입력칸에 글자를 넣는 동안
    /// <see cref="GameplayInputBlocker"/>로 게임 입력을 막는다. 게임 코드보다 먼저 돌도록 실행 순서를 앞당긴다.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class DebugHub : MonoBehaviour
    {
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

        public static DebugHub Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.window != null && Instance.window.Visible;

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
            var go = new GameObject("OVERBURST Debug Hub", typeof(RectTransform));
            DontDestroyOnLoad(go);
            go.AddComponent<DebugHub>();
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
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            gameObject.layer = 5;
            BuildCanvas();
            DebugUi.Initialize(DebugHubStyle.Load());
            overlay = new DebugOverlay(this, canvasRect);
            window = new DebugHubWindow(canvasRect);
            window.SetVisible(false);
            DebugRuntime.ConfirmHandler = HandleConfirm;
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
            DebugPerf.Tick(Time.unscaledDeltaTime);
            DebugLogCapture.Drain();
            DebugTime.Tick();
            ApplyPersistedValues();
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
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
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
            bool want = pointerOverWindow || (window.Visible && window.IsTyping);
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
                    window.BlurInputs();
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
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null)
                return;
            var go = new GameObject("EventSystem (Debug Hub)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            Debug.LogWarning("[DebugHub] 씬에 EventSystem이 없어 디버그 창용으로 하나 만들었어요.");
        }
    }
}
#endif
