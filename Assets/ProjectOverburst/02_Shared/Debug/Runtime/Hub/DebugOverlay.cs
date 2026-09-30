#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 핀으로 고정한 값을 창이 닫혀도 화면 왼쪽에 보여 준다. 줄을 누르면 창의 해당 항목으로 간다.
    /// 창이 닫혀 있고 오류가 있으면 첫 줄에 오류 수를 띄운다.
    /// </summary>
    internal sealed class DebugOverlay
    {
        [Serializable]
        private sealed class State
        {
            public float x = 16f;
            public float y = 40f;
            public bool visible = true;
        }

        private sealed class LineView
        {
            public GameObject Root;
            public TextMeshProUGUI Text;
            public string Id;
            public string Last;
        }

        private readonly DebugHub hub;
        private readonly List<LineView> lines = new List<LineView>();
        private readonly List<DebugItem> scratch = new List<DebugItem>();
        private readonly RectTransform root;
        private readonly RectTransform list;
        private readonly LineView errorLine;
        private readonly State state;

        public DebugOverlay(DebugHub hub, RectTransform canvas)
        {
            this.hub = hub;
            state = LoadState();
            DebugHubStyle style = DebugUi.Style;

            Image background = DebugUi.Panel(canvas, "Debug Pin Overlay", style.overlay, 7, true);
            root = background.rectTransform;
            root.anchorMin = root.anchorMax = new Vector2(0f, 0.5f);
            root.pivot = new Vector2(0f, 0.5f);
            DebugUi.Column(background, 2f, new RectOffset(12, 12, 8, 10), false);
            var fitter = background.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI header = DebugUi.Text(root, "Header", "핀  ·  F2 숨김  ·  끌어서 이동", style.smallSize - 1f, style.muted);
            header.raycastTarget = true;
            DebugUi.FixedHeight(header, 16f);
            DebugDragHandle drag = header.gameObject.AddComponent<DebugDragHandle>();
            drag.Dragged = Move;
            drag.Ended = SaveState;

            list = DebugUi.Rect(root, "Lines");
            DebugUi.Column(list, 1f, null, false);
            errorLine = CreateLine();
            errorLine.Root.transform.SetAsFirstSibling();
            errorLine.Id = null;
            Apply();
        }

        public bool Visible => state.visible;

        public bool Contains(Vector2 screenPoint)
        {
            return root.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(root, screenPoint, null);
        }

        public void ToggleVisible()
        {
            state.visible = !state.visible;
            SaveState();
            DebugRuntime.Report("핀 오버레이", DebugResult.Ok(state.visible ? "보임" : "숨김"));
        }

        public void Refresh(bool windowOpen)
        {
            scratch.Clear();
            IReadOnlyList<string> pins = DebugPrefs.Pins;
            for (int i = 0; i < pins.Count; i++)
            {
                DebugItem item = DebugRegistry.Find(pins[i]);
                if (item != null && item.IsVisible)
                    scratch.Add(item);
            }

            int errors = DebugLogCapture.ErrorCount;
            bool showErrors = !windowOpen && errors > 0;
            bool show = state.visible && (scratch.Count > 0 || showErrors);
            if (root.gameObject.activeSelf != show)
                root.gameObject.SetActive(show);
            if (!show)
                return;

            DebugHubStyle style = DebugUi.Style;
            errorLine.Root.SetActive(showErrors);
            if (showErrors)
                SetText(errorLine, $"<color={DebugHubStyle.Hex(style.error)}>● 오류 {errors}</color>  <color={DebugHubStyle.Hex(style.muted)}>누르면 로그</color>");

            while (lines.Count < scratch.Count)
                lines.Add(CreateLine());
            for (int i = 0; i < lines.Count; i++)
            {
                LineView line = lines[i];
                bool used = i < scratch.Count;
                if (line.Root.activeSelf != used)
                    line.Root.SetActive(used);
                if (!used)
                    continue;
                DebugItem item = scratch[i];
                line.Id = item.Id;
                SetText(line, $"<color={DebugHubStyle.Hex(style.label)}>{item.Label}</color>  <b>{(item.DisplayValue() ?? string.Empty).Replace('\n', ' ')}</b>");
            }
        }

        private LineView CreateLine()
        {
            DebugHubStyle style = DebugUi.Style;
            Image background = DebugUi.Panel(list, "Line", Color.white, 4, true);
            DebugUi.Row(background, 0f, new RectOffset(4, 4, 0, 0));
            DebugUi.FixedHeight(background, 21f);
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = DebugUi.GhostColors();

            var line = new LineView { Root = background.gameObject };
            line.Text = DebugUi.Text(background.transform, "Text", string.Empty, style.smallSize + 1f, style.text,
                TextAlignmentOptions.MidlineLeft, false, true);
            button.onClick.AddListener(() =>
            {
                DebugUi.Deselect();
                if (line.Id != null)
                    hub.JumpTo(line.Id);
                else
                    DebugHub.OpenTab(DebugTabs.SystemTab);
            });
            return line;
        }

        private static void SetText(LineView line, string text)
        {
            if (text == line.Last)
                return;
            line.Last = text;
            line.Text.text = text;
        }

        private void Move(Vector2 delta)
        {
            state.x += delta.x;
            state.y += delta.y;
            Apply();
        }

        private void Apply()
        {
            state.x = Mathf.Max(0f, state.x);
            root.anchoredPosition = new Vector2(state.x, state.y);
        }

        private static State LoadState()
        {
            string json = PlayerPrefs.GetString(DebugPrefs.OverlayKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    State loaded = JsonUtility.FromJson<State>(json);
                    if (loaded != null)
                        return loaded;
                }
                catch (Exception)
                {
                    // 깨진 값은 기본값으로 되돌린다.
                }
            }
            return new State();
        }

        public void SaveState()
        {
            PlayerPrefs.SetString(DebugPrefs.OverlayKey, JsonUtility.ToJson(state));
        }
    }
}
#endif
