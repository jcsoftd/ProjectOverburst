#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overburst.DebugTools
{
    /// <summary>항목 하나를 그리는 행. 라벨 칸 | 내용 | ☆ | 핀 순서다.</summary>
    internal abstract class DebugRowView
    {
        protected const float LabelWidth = 132f;
        protected const float IconWidth = 20f;
        protected const float ControlHeight = 24f;
        protected static readonly StringBuilder Builder = new StringBuilder(256);

        public readonly DebugItem Item;
        public RectTransform Root { get; private set; }
        protected RectTransform Line { get; private set; }
        protected RectTransform Content { get; private set; }
        protected static DebugHubStyle Style => DebugUi.Style;
        protected bool Interactable { get; private set; } = true;

        private Image background;
        private TextMeshProUGUI labelText;
        private TextMeshProUGUI reasonText;
        private TextMeshProUGUI favoriteText;
        private TextMeshProUGUI pinText;
        private string lastReason;
        private string lastLabel;
        private bool? lastEnabled;
        private bool? lastFavorite;
        private bool? lastPinned;

        protected DebugRowView(DebugItem item)
        {
            Item = item;
        }

        public static DebugRowView Create(DebugItem item)
        {
            switch (item.Kind)
            {
                case DebugItemKind.Toggle: return new ToggleRowView(item);
                case DebugItemKind.Buttons: return new ButtonsRowView(item);
                case DebugItemKind.Choice: return new ChoiceRowView(item);
                case DebugItemKind.Number: return new NumberRowView(item);
                case DebugItemKind.Text: return new TextRowView(item);
                case DebugItemKind.Picker: return new PickerRowView(item);
                case DebugItemKind.Readout: return new ReadoutRowView(item);
                case DebugItemKind.Progress: return new ProgressRowView(item);
                case DebugItemKind.Bar: return new BarRowView(item);
                default: return new CustomRowView(item);
            }
        }

        protected virtual float LineHeight => 32f;
        protected virtual string RowLabel => Item.Label;
        protected abstract void BuildContent();
        protected abstract void RefreshContent(bool force);
        protected virtual void BuildExtra(RectTransform root)
        {
        }

        public virtual void Dispose()
        {
        }

        public virtual void ResetTransientState()
        {
        }

        /// <param name="context">즐겨찾기처럼 원래 탭 밖에 그릴 때 툴팁에 붙일 '탭 › 섹션'.</param>
        public void Build(Transform parent, string context)
        {
            background = DebugUi.Panel(parent, "Row " + Item.Id, Color.clear, 5);
            Root = background.rectTransform;
            DebugUi.Column(background, 0f);

            bool tall = LineHeight > 34f;
            Line = DebugUi.Rect(Root, "Line");
            DebugUi.Row(Line, 8f, new RectOffset(6, 2, 0, 0), tall ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            DebugUi.FixedHeight(Line, LineHeight);

            labelText = DebugUi.Text(Line, "Label", RowLabel, Style.bodySize - 1f, Style.label,
                tall ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft);
            if (tall)
                labelText.margin = new Vector4(0f, 8f, 0f, 0f);
            DebugUi.Layout(labelText, preferredWidth: LabelWidth, flexibleWidth: 0f, minWidth: LabelWidth);
            DebugUi.AddTip(labelText, BuildTip(context));

            Content = DebugUi.Rect(Line, "Content");
            DebugUi.Row(Content, 6f, null, tall ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, false);
            DebugUi.Layout(Content, flexibleWidth: 1f, minWidth: 0f);
            BuildContent();

            reasonText = DebugUi.Text(Content, "Reason", string.Empty, Style.smallSize, Style.muted);
            DebugUi.Layout(reasonText, flexibleWidth: 1f, minWidth: 0f);

            Button favorite = DebugUi.Button(Line, "Favorite", "☆", ToggleFavorite, out favoriteText, IconWidth,
                IconWidth, 13f, DebugButtonKind.Icon, 4);
            DebugUi.AddTip(favorite.targetGraphic, "즐겨찾기 탭에 모은다");
            if (Item.IsPinnable)
            {
                Button pin = DebugUi.Button(Line, "Pin", "●", TogglePin, out pinText, IconWidth, IconWidth, 10f,
                    DebugButtonKind.Icon, 4);
                DebugUi.AddTip(pin.targetGraphic, "핀: 창을 닫아도 화면 왼쪽에 값을 계속 보여 준다 (F2로 숨김)");
            }
            else
            {
                RectTransform spacer = DebugUi.Rect(Line, "Pin Space");
                DebugUi.Layout(spacer, preferredWidth: IconWidth, flexibleWidth: 0f, minWidth: IconWidth);
            }

            BuildExtra(Root);
            Refresh(true);
        }

        public void Refresh(bool force = false)
        {
            if (Root == null)
                return;
            bool visible = Item.IsVisible;
            if (Root.gameObject.activeSelf != visible)
                Root.gameObject.SetActive(visible);
            if (!visible)
                return;

            bool enabled = Item.IsEnabled;
            if (force || lastEnabled != enabled)
            {
                lastEnabled = enabled;
                Interactable = enabled;
                SetInteractable(enabled);
                labelText.color = enabled ? Style.label : Style.muted;
            }

            string reason = !enabled && !string.IsNullOrEmpty(Item.DisabledReason)
                ? Item.DisabledReason
                : Item.NoteText ?? string.Empty;
            if (force || reason != lastReason)
            {
                lastReason = reason;
                reasonText.text = reason;
                reasonText.color = enabled ? Style.muted : Style.warn;
                reasonText.gameObject.SetActive(reason.Length > 0);
            }

            string label = RowLabel;
            if (label != lastLabel)
            {
                lastLabel = label;
                labelText.text = label;
            }

            bool isFavorite = DebugPrefs.IsFavorite(Item.Id);
            if (force || lastFavorite != isFavorite)
            {
                lastFavorite = isFavorite;
                favoriteText.text = isFavorite ? "★" : "☆";
                favoriteText.color = isFavorite ? Style.favorite : Dim(Style.muted, 0.55f);
            }

            if (pinText != null)
            {
                bool pinned = DebugPrefs.IsPinned(Item.Id);
                if (force || lastPinned != pinned)
                {
                    lastPinned = pinned;
                    pinText.color = pinned ? Style.ok : Dim(Style.muted, 0.3f);
                }
            }

            RefreshContent(force);
        }

        public void SetHighlight(bool on)
        {
            if (background != null)
                background.color = on ? Style.highlight : Color.clear;
        }

        protected virtual void SetInteractable(bool enabled)
        {
            Selectable[] selectables = Content.GetComponentsInChildren<Selectable>(true);
            for (int i = 0; i < selectables.Length; i++)
                selectables[i].interactable = enabled;
        }

        protected void ReapplyInteractable()
        {
            SetInteractable(Interactable);
        }

        protected static Color Dim(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        private string BuildTip(string context)
        {
            Builder.Clear();
            if (!string.IsNullOrEmpty(context))
                Builder.Append(context);
            if (!string.IsNullOrEmpty(Item.TipText))
            {
                if (Builder.Length > 0)
                    Builder.Append('\n');
                Builder.Append(Item.TipText);
            }
            if (Item.HotkeyKey != UnityEngine.InputSystem.Key.None)
            {
                if (Builder.Length > 0)
                    Builder.Append('\n');
                Builder.Append("단축키 ").Append(Item.HotkeyCtrl ? "Ctrl+" : string.Empty).Append(Item.HotkeyKey);
            }
            return Builder.ToString();
        }

        private void ToggleFavorite()
        {
            bool now = DebugPrefs.ToggleFavorite(Item.Id);
            DebugRuntime.Report($"{Item.Label} 즐겨찾기", DebugResult.Ok(now ? "추가" : "해제"));
        }

        private void TogglePin()
        {
            DebugRuntime.Report($"{Item.Label} 핀", DebugPrefs.TogglePin(Item.Id));
        }
    }

    /// <summary>둥근 스위치 + 켜짐/꺼짐 글자.</summary>
    internal sealed class ToggleRowView : DebugRowView
    {
        private const float TrackWidth = 36f;
        private const float TrackHeight = 20f;
        private const float KnobSize = 14f;
        private Image track;
        private RectTransform knob;
        private TextMeshProUGUI state;
        private bool? last;

        public ToggleRowView(DebugItem item) : base(item)
        {
        }

        private DebugToggle Target => (DebugToggle)Item;

        protected override void BuildContent()
        {
            track = DebugUi.Panel(Content, "Switch", Style.off, 10, true);
            DebugUi.Layout(track, preferredWidth: TrackWidth, preferredHeight: TrackHeight, flexibleWidth: 0f,
                flexibleHeight: 0f, minWidth: TrackWidth, minHeight: TrackHeight);
            var button = DebugUi.Component<Button>(track.gameObject);
            button.onClick.RemoveAllListeners();
            button.targetGraphic = track;
            button.colors = DebugUi.SolidColors();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                DebugUi.Deselect();
                Target.Flip();
            });

            Image knobImage = DebugUi.Panel(track.transform, "Knob", Style.knob, 7);
            knob = knobImage.rectTransform;
            knob.sizeDelta = new Vector2(KnobSize, KnobSize);

            state = DebugUi.Text(Content, "State", "꺼짐", Style.smallSize, Style.muted);
            DebugUi.Layout(state, preferredWidth: 30f, flexibleWidth: 0f, minWidth: 30f);
        }

        protected override void RefreshContent(bool force)
        {
            bool value = Target.Value;
            if (!force && last == value)
                return;
            last = value;
            track.color = value ? Style.on : Style.off;
            float x = value ? 1f : 0f;
            knob.anchorMin = knob.anchorMax = new Vector2(x, 0.5f);
            knob.pivot = new Vector2(x, 0.5f);
            knob.anchoredPosition = new Vector2(value ? -3f : 3f, 0f);
            state.text = value ? "켜짐" : "꺼짐";
            state.color = value ? Style.text : Style.muted;
        }
    }

    internal sealed class ButtonsRowView : DebugRowView
    {
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
        private readonly List<string> texts = new List<string>();

        public ButtonsRowView(DebugItem item) : base(item)
        {
        }

        private DebugButtons Target => (DebugButtons)Item;
        protected override string RowLabel => Target.showLabel ? Item.Label : string.Empty;

        protected override void BuildContent()
        {
            for (int i = 0; i < Target.Count; i++)
            {
                int index = i;
                string text = Target.TextOf(i);
                Button button = DebugUi.Button(Content, "Button " + i, text, () => Target.Press(index),
                    out TextMeshProUGUI label, 52f, ControlHeight);
                buttons.Add(button);
                labels.Add(label);
                texts.Add(text);
            }
        }

        protected override void RefreshContent(bool force)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                string text = Target.TextOf(i);
                if (!force && text == texts[i])
                    continue;
                texts[i] = text;
                labels[i].text = text;
                DebugUi.FitWidth(buttons[i], labels[i], 52f);
            }
        }
    }

    /// <summary>6개 이하는 세그먼트 버튼, 그보다 많으면 ‹ 현재 값 › 넘기기.</summary>
    internal sealed class ChoiceRowView : DebugRowView
    {
        private const int SegmentLimit = 6;
        private readonly List<Button> segments = new List<Button>();
        private readonly List<TextMeshProUGUI> segmentLabels = new List<TextMeshProUGUI>();
        private RectTransform options;
        private TextMeshProUGUI current;
        private string signature;
        private int lastIndex = -2;

        public ChoiceRowView(DebugItem item) : base(item)
        {
        }

        private DebugOptionsItem Target => (DebugOptionsItem)Item;

        protected override void BuildContent()
        {
            Image group = DebugUi.Panel(Content, "Options", Style.segment, 6);
            options = group.rectTransform;
            DebugUi.Row(group, 2f, new RectOffset(2, 2, 2, 2), TextAnchor.MiddleLeft, true);
            DebugUi.Layout(group, preferredHeight: ControlHeight + 2f, flexibleWidth: 0f, flexibleHeight: 0f,
                minHeight: ControlHeight + 2f);
            Rebuild();
            if (!Application.isPlaying)
            {
                for (int i = 0; i < SegmentLimit; i++)
                    DebugUi.Button(options, "Option " + i, "-", null, out _, 34f, ControlHeight - 2f,
                        -1f, DebugButtonKind.Ghost, 4).gameObject.SetActive(false);
                DebugUi.Button(options, "Previous", "‹", null, out _, 24f, ControlHeight - 2f, 15f,
                    DebugButtonKind.Ghost, 4).gameObject.SetActive(false);
                DebugUi.Button(options, "Next", "›", null, out _, 24f, ControlHeight - 2f, 15f,
                    DebugButtonKind.Ghost, 4).gameObject.SetActive(false);
                DebugUi.Layout(DebugUi.Text(options, "Current", "-", Style.bodySize - 1f, Style.textStrong,
                    TextAlignmentOptions.Center, true), preferredWidth: 150f, minWidth: 90f);
                DebugUi.Layout(DebugUi.Text(options, "Empty", "선택지 없음", Style.smallSize, Style.muted,
                    TextAlignmentOptions.Center), preferredWidth: 80f);
            }
        }

        protected override void RefreshContent(bool force)
        {
            if (Signature() != signature)
            {
                Rebuild();
                force = true;
            }
            int index = Target.CurrentIndex;
            if (!force && index == lastIndex)
                return;
            lastIndex = index;
            if (current != null)
            {
                current.text = Target.CurrentLabel;
                return;
            }
            for (int i = 0; i < segments.Count; i++)
            {
                bool selected = i == index;
                segments[i].colors = selected ? DebugUi.SolidColors() : DebugUi.GhostColors();
                segments[i].targetGraphic.color = selected ? Style.accent : Color.white;
                segmentLabels[i].color = selected ? Style.textStrong : Style.label;
                segmentLabels[i].fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
                DebugUi.FitWidth(segments[i], segmentLabels[i], 34f);
            }
        }

        private void Rebuild()
        {
            DebugUi.ClearChildren(options);
            segments.Clear();
            segmentLabels.Clear();
            current = null;
            lastIndex = -2;
            signature = Signature();

            int count = Target.OptionCount;
            if (count == 0)
            {
                TextMeshProUGUI empty = DebugUi.Text(options, "Empty", "선택지 없음", Style.smallSize, Style.muted,
                    TextAlignmentOptions.Center);
                DebugUi.Layout(empty, preferredWidth: 80f);
                return;
            }
            if (count <= SegmentLimit)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = i;
                    Button button = DebugUi.Button(options, "Option " + i, Target.OptionLabel(i), () => Pick(index),
                        out TextMeshProUGUI label, 34f, ControlHeight - 2f, -1f, DebugButtonKind.Ghost, 4);
                    segments.Add(button);
                    segmentLabels.Add(label);
                }
            }
            else
            {
                DebugUi.Button(options, "Previous", "‹", () => StepBy(-1), out _, 24f, ControlHeight - 2f, 15f,
                    DebugButtonKind.Ghost, 4);
                current = DebugUi.Text(options, "Current", Target.CurrentLabel, Style.bodySize - 1f, Style.textStrong,
                    TextAlignmentOptions.Center, true);
                DebugUi.Layout(current, preferredWidth: 150f, minWidth: 90f);
                DebugUi.Button(options, "Next", "›", () => StepBy(1), out _, 24f, ControlHeight - 2f, 15f,
                    DebugButtonKind.Ghost, 4);
            }
            ReapplyInteractable();
        }

        private string Signature()
        {
            Builder.Clear();
            int count = Target.OptionCount;
            Builder.Append(count);
            for (int i = 0; i < count && i < 32; i++)
                Builder.Append('|').Append(Target.OptionLabel(i));
            return Builder.ToString();
        }

        private void Pick(int index)
        {
            DebugHub.Focus(Target);
            Target.Choose(index);
        }

        private void StepBy(int delta)
        {
            DebugHub.Focus(Target);
            Target.Step(delta);
        }
    }

    internal sealed class NumberRowView : DebugRowView
    {
        private TMP_InputField input;
        private string last;

        public NumberRowView(DebugItem item) : base(item)
        {
        }

        private DebugNumber Target => (DebugNumber)Item;

        protected override void BuildContent()
        {
            DebugUi.Button(Content, "Minus", "-", () => Target.Nudge(-1), out _, ControlHeight, ControlHeight, 15f);
            input = DebugUi.Input(Content, "Value", string.Empty, Style.bodySize - 1f);
            input.contentType = Target.integer ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
            input.textComponent.alignment = TextAlignmentOptions.Center;
            DebugUi.Layout(input, preferredWidth: 72f, preferredHeight: ControlHeight, flexibleWidth: 0f,
                flexibleHeight: 0f, minWidth: 72f, minHeight: ControlHeight);
            input.onEndEdit.AddListener(Submit);
            DebugUi.Button(Content, "Plus", "+", () => Target.Nudge(1), out _, ControlHeight, ControlHeight, 15f);
            string range = Target.Format(Target.min) + " ~ " + Target.Format(Target.max);
            TextMeshProUGUI hint = DebugUi.Text(Content, "Range", range, Style.smallSize, Style.muted);
            DebugUi.Layout(hint, preferredWidth: hint.GetPreferredValues(range).x + 4f, flexibleWidth: 0f);
        }

        protected override void RefreshContent(bool force)
        {
            if (input.isFocused)
                return;
            string text = Target.DisplayValue();
            if (!force && text == last)
                return;
            last = text;
            input.SetTextWithoutNotify(text);
        }

        private void Submit(string text)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                Target.SetValue(value);
            last = null;
        }
    }

    internal sealed class TextRowView : DebugRowView
    {
        private TMP_InputField input;

        public TextRowView(DebugItem item) : base(item)
        {
        }

        private DebugText Target => (DebugText)Item;

        protected override void BuildContent()
        {
            input = DebugUi.Input(Content, "Value", "입력", Style.bodySize - 1f);
            DebugUi.Layout(input, preferredWidth: 220f, preferredHeight: ControlHeight, flexibleWidth: 1f,
                flexibleHeight: 0f, minWidth: 80f, minHeight: ControlHeight);
            input.onEndEdit.AddListener(value => Target.Submit(value));
        }

        protected override void RefreshContent(bool force)
        {
            if (input.isFocused)
                return;
            string value = Target.Value;
            if (force || input.text != value)
                input.SetTextWithoutNotify(value);
        }
    }

    internal sealed class PickerRowView : DebugRowView
    {
        private const int MaxShown = 12;
        private Button open;
        private TextMeshProUGUI openLabel;
        private RectTransform panel;
        private TMP_InputField search;
        private RectTransform list;
        private string lastLabel;

        public PickerRowView(DebugItem item) : base(item)
        {
        }

        private DebugOptionsItem Target => (DebugOptionsItem)Item;

        public override void ResetTransientState()
        {
            if (panel != null)
                panel.gameObject.SetActive(false);
        }

        protected override void BuildContent()
        {
            open = DebugUi.Button(Content, "Open", "-", ToggleOpen, out openLabel, 140f, ControlHeight);
            openLabel.alignment = TextAlignmentOptions.MidlineLeft;
        }

        protected override void BuildExtra(RectTransform root)
        {
            Image frame = DebugUi.Panel(root, "Picker", Style.segment, 6);
            panel = frame.rectTransform;
            DebugUi.Column(frame, 2f, new RectOffset(6, 6, 6, 6));
            search = DebugUi.Input(panel, "Search", "검색", Style.bodySize - 1f);
            DebugUi.FixedHeight(search, ControlHeight);
            search.onValueChanged.AddListener(_ => RebuildList());
            list = DebugUi.Rect(panel, "List");
            DebugUi.Column(list, 1f);
            if (!Application.isPlaying)
            {
                for (int i = 0; i < MaxShown; i++)
                {
                    Button slot = DebugUi.Button(list, "Option " + i, "-", null, out TextMeshProUGUI text,
                        60f, 24f, -1f, DebugButtonKind.Ghost, 4);
                    text.alignment = TextAlignmentOptions.MidlineLeft;
                    DebugUi.Layout(slot, preferredHeight: 24f, flexibleWidth: 1f, flexibleHeight: 0f, minHeight: 24f);
                    slot.gameObject.SetActive(false);
                }
                DebugUi.Text(list, "Note", string.Empty, Style.smallSize, Style.muted).gameObject.SetActive(false);
            }
            panel.gameObject.SetActive(false);
        }

        protected override void RefreshContent(bool force)
        {
            string label = Target.CurrentLabel + "   ›";
            if (!force && label == lastLabel)
                return;
            lastLabel = label;
            openLabel.text = label;
            DebugUi.FitWidth(open, openLabel, 140f);
        }

        protected override void SetInteractable(bool enabled)
        {
            base.SetInteractable(enabled);
            if (!enabled && panel != null)
                panel.gameObject.SetActive(false);
        }

        private void ToggleOpen()
        {
            bool show = !panel.gameObject.activeSelf;
            panel.gameObject.SetActive(show);
            if (!show)
                return;
            search.SetTextWithoutNotify(string.Empty);
            RebuildList();
            search.ActivateInputField();
        }

        private void RebuildList()
        {
            DebugUi.ClearChildren(list);
            string query = (search.text ?? string.Empty).Trim();
            int count = Target.OptionCount;
            int currentIndex = Target.CurrentIndex;
            int matched = 0;
            for (int i = 0; i < count; i++)
            {
                string label = Target.OptionLabel(i);
                if (query.Length > 0 && label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                matched++;
                if (matched > MaxShown)
                    continue;
                int index = i;
                Button button = DebugUi.Button(list, "Option " + (matched - 1), label, () => Pick(index), out TextMeshProUGUI text,
                    60f, 24f, -1f, DebugButtonKind.Ghost, 4);
                text.alignment = TextAlignmentOptions.MidlineLeft;
                DebugUi.Layout(button, preferredHeight: 24f, flexibleWidth: 1f, flexibleHeight: 0f, minHeight: 24f);
                if (i == currentIndex)
                {
                    button.colors = DebugUi.SolidColors();
                    button.targetGraphic.color = Style.accent;
                    text.color = Style.textStrong;
                }
            }

            string note = matched == 0 ? "결과 없음"
                : matched > MaxShown ? $"… {matched - MaxShown}개 더 있어요. 검색으로 좁혀 주세요."
                : null;
            if (note != null)
                DebugUi.Text(list, "Note", note, Style.smallSize, Style.muted);
        }

        private void Pick(int index)
        {
            panel.gameObject.SetActive(false);
            Target.Choose(index);
        }
    }

    internal sealed class ReadoutRowView : DebugRowView
    {
        private TextMeshProUGUI value;
        private string last;

        public ReadoutRowView(DebugItem item) : base(item)
        {
        }

        private DebugReadout Target => (DebugReadout)Item;
        private bool Multiline => Target.LineCount > 1;
        protected override float LineHeight => Multiline ? Target.LineCount * 17f + 14f : 32f;

        protected override void BuildContent()
        {
            if (Multiline)
            {
                Image box = DebugUi.Panel(Content, "Box", Style.segment, 6);
                DebugUi.Layout(box, preferredHeight: LineHeight - 6f, flexibleWidth: 1f, flexibleHeight: 0f, minWidth: 0f);
                value = DebugUi.Text(box.transform, "Value", "-", Style.smallSize, Style.text,
                    TextAlignmentOptions.TopLeft, false, true);
                DebugUi.Stretch(value.rectTransform, 9f, 6f, 9f, 6f);
                value.textWrappingMode = TextWrappingModes.Normal;
                value.overflowMode = TextOverflowModes.Truncate;
                return;
            }
            value = DebugUi.Text(Content, "Value", "-", Style.bodySize - 1f, Style.textStrong,
                TextAlignmentOptions.MidlineLeft, true, true);
            DebugUi.Layout(value, flexibleWidth: 1f, minWidth: 0f);
        }

        protected override void RefreshContent(bool force)
        {
            string text = Target.Value;
            if (!force && text == last)
                return;
            last = text;
            value.text = text;
        }
    }

    internal sealed class ProgressRowView : DebugRowView
    {
        private RectTransform fill;
        private TextMeshProUGUI text;
        private float lastRatio = -1f;
        private string lastText;

        public ProgressRowView(DebugItem item) : base(item)
        {
        }

        private DebugProgressItem Target => (DebugProgressItem)Item;

        protected override void BuildContent()
        {
            Image track = DebugUi.Panel(Content, "Track", Style.segment, 5);
            DebugUi.Layout(track, preferredHeight: 18f, flexibleWidth: 1f, flexibleHeight: 0f, minWidth: 60f, minHeight: 18f);
            Image fillImage = DebugUi.Panel(track.transform, "Fill", Style.on, 5);
            fill = fillImage.rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            text = DebugUi.Text(track.transform, "Text", string.Empty, Style.smallSize, Style.textStrong, TextAlignmentOptions.Center);
            DebugUi.Stretch(text.rectTransform);
        }

        protected override void RefreshContent(bool force)
        {
            DebugProgress progress = Target.Value;
            if (force || !Mathf.Approximately(progress.Ratio, lastRatio))
            {
                lastRatio = progress.Ratio;
                fill.anchorMax = new Vector2(progress.Ratio, 1f);
            }
            string label = Target.DisplayValue();
            if (force || label != lastText)
            {
                lastText = label;
                text.text = label;
            }
        }
    }

    internal sealed class BarRowView : DebugRowView
    {
        private readonly List<Image> parts = new List<Image>();
        private readonly List<LayoutElement> partLayouts = new List<LayoutElement>();
        private RectTransform bar;
        private TextMeshProUGUI legend;
        private string lastLegend;

        public BarRowView(DebugItem item) : base(item)
        {
        }

        private DebugBar Target => (DebugBar)Item;
        protected override float LineHeight => 24f;

        protected override void BuildContent()
        {
            Image track = DebugUi.Panel(Content, "Bar", Style.segment, 4);
            bar = track.rectTransform;
            DebugUi.Layout(track, preferredHeight: 12f, flexibleWidth: 1f, flexibleHeight: 0f, minWidth: 60f, minHeight: 12f);
            DebugUi.Row(track, 2f, new RectOffset(1, 1, 1, 1), TextAnchor.MiddleLeft, true);
            if (!Application.isPlaying)
                for (int i = 0; i < 16; i++)
                {
                    Image part = DebugUi.Panel(bar, "Part " + i, Color.white, 3);
                    parts.Add(part);
                    partLayouts.Add(DebugUi.Layout(part, preferredWidth: 0f, flexibleWidth: 1f));
                    part.gameObject.SetActive(false);
                }
        }

        protected override void BuildExtra(RectTransform root)
        {
            legend = DebugUi.Text(root, "Legend", string.Empty, Style.smallSize, Style.label, TextAlignmentOptions.MidlineLeft, false, true);
            legend.margin = new Vector4(LabelWidth + 14f, 0f, 0f, 0f);
            DebugUi.FixedHeight(legend, 18f);
        }

        protected override void RefreshContent(bool force)
        {
            DebugBarSegment[] segments = Target.Segments;
            while (parts.Count < segments.Length)
            {
                Image part = DebugUi.Panel(bar, "Part " + parts.Count, Color.white, 3);
                parts.Add(part);
                partLayouts.Add(DebugUi.Layout(part, preferredWidth: 0f, flexibleWidth: 1f));
            }

            float total = 0f;
            for (int i = 0; i < segments.Length; i++)
                total += Mathf.Max(0f, segments[i].Value);

            Builder.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                bool used = i < segments.Length && total > 0f && segments[i].Value > 0f;
                if (parts[i].gameObject.activeSelf != used)
                    parts[i].gameObject.SetActive(used);
                if (i >= segments.Length)
                    continue;
                DebugBarSegment segment = segments[i];
                if (used)
                {
                    parts[i].color = segment.Color;
                    partLayouts[i].flexibleWidth = segment.Value;
                }
                if (Builder.Length > 0)
                    Builder.Append("    ");
                Builder.Append("<color=").Append(DebugHubStyle.Hex(segment.Color)).Append(">●</color> ")
                    .Append(segment.Label).Append(' ')
                    .Append(segment.Value.ToString("0.#", CultureInfo.InvariantCulture));
            }

            string text = Builder.ToString();
            if (force || text != lastLegend)
            {
                lastLegend = text;
                legend.text = text;
            }
        }
    }

    internal sealed class CustomRowView : DebugRowView
    {
        private IDisposable handle;

        public CustomRowView(DebugItem item) : base(item)
        {
        }

        private DebugCustom Target => (DebugCustom)Item;
        protected override float LineHeight => Target.height;

        protected override void BuildContent()
        {
            RectTransform area = DebugUi.Rect(Content, "Custom");
            DebugUi.Layout(area, preferredHeight: Target.height - 4f, flexibleWidth: 1f, flexibleHeight: 0f);
            try
            {
                handle = Target.build?.Invoke(area);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        protected override void RefreshContent(bool force)
        {
        }

        public override void Dispose()
        {
            try
            {
                handle?.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            handle = null;
        }
    }

    /// <summary>섹션 머리글. 누르면 접히고 펼쳐진다(접힘 상태는 저장된다).</summary>
    internal static class DebugSectionHeader
    {
        public static RectTransform Build(Transform parent, string title, string note, bool collapsible,
            bool collapsed, Action onToggle)
        {
            DebugHubStyle style = DebugUi.Style;
            RectTransform root = DebugUi.Rect(parent, "Section " + title);
            DebugUi.Column(root, 4f, new RectOffset(0, 0, 14, 4));

            Image bar = DebugUi.Panel(root, "Header", Color.white, 4, collapsible);
            DebugUi.FixedHeight(bar, 22f);
            DebugUi.Row(bar, 6f, new RectOffset(6, 4, 0, 0));

            if (collapsible)
            {
                // 글꼴에 ▾가 없어 ›를 돌려 쓴다: 펼침 = 아래, 접힘 = 오른쪽.
                TextMeshProUGUI mark = DebugUi.Text(bar.transform, "Mark", "›", style.bodySize + 2f, style.muted,
                    TextAlignmentOptions.Center, true);
                DebugUi.Layout(mark, preferredWidth: 12f, flexibleWidth: 0f, minWidth: 12f);
                mark.rectTransform.localEulerAngles = new Vector3(0f, 0f, collapsed ? 0f : -90f);
            }
            TextMeshProUGUI titleText = DebugUi.Text(bar.transform, "Title", title, style.headerSize, style.textStrong,
                TextAlignmentOptions.MidlineLeft, true);
            DebugUi.Layout(titleText, preferredWidth: titleText.GetPreferredValues(title).x + 4f, flexibleWidth: 0f);
            TextMeshProUGUI noteText = DebugUi.Text(bar.transform, "Note", note ?? string.Empty, style.smallSize, style.muted,
                TextAlignmentOptions.MidlineRight);
            DebugUi.Layout(noteText, flexibleWidth: 1f, minWidth: 0f);

            var button = DebugUi.Component<Button>(bar.gameObject);
            button.onClick.RemoveAllListeners();
            button.targetGraphic = bar;
            button.colors = DebugUi.GhostColors();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.interactable = collapsible;
            if (collapsible)
            {
                button.onClick.AddListener(() =>
                {
                    DebugUi.Deselect();
                    onToggle?.Invoke();
                });
            }

            DebugUi.Separator(root);
            return root;
        }
    }
}
#endif
