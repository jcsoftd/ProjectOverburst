#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overburst.DebugTools
{
    public enum DebugItemKind
    {
        Toggle,
        Buttons,
        Choice,
        Number,
        Text,
        Picker,
        Readout,
        Progress,
        Bar,
        Custom
    }

    /// <summary>
    /// 디버그 창의 행 하나. 값은 각 기능이 소유하고, 항목은 get/set 대리자로 읽고 쓰기만 한다.
    /// 공통 옵션은 <see cref="DebugItemOptions"/>의 체이닝 메서드로 붙인다.
    /// </summary>
    public abstract class DebugItem
    {
        internal DebugSection section;
        internal Func<string> label;
        internal string id;
        internal string tip;
        internal string note;
        internal Func<bool> enabledWhen;
        internal string disabledReason;
        internal Func<bool> visibleWhen;
        internal Action afterChange;
        internal bool pinnable;
        internal bool persist;
        internal bool excludeFromPresets;
        internal Key hotkey = Key.None;
        internal bool hotkeyCtrl;
        internal string confirm;
        internal string[] keywords = Array.Empty<string>();
        private bool reportedReadError;

        protected DebugItem(Func<string> label)
        {
            this.label = label ?? (() => string.Empty);
        }

        public abstract DebugItemKind Kind { get; }
        public DebugSection Section => section;
        public string Id => id;
        public string Label => Eval(label, "?");
        public string TipText => tip;
        public string NoteText => note;
        public string DisabledReason => disabledReason;
        public string ConfirmText => confirm;
        public bool IsPinnable => pinnable;
        public Key HotkeyKey => hotkey;
        public bool HotkeyCtrl => hotkeyCtrl;
        public bool IsEnabled => enabledWhen == null || Eval(enabledWhen, false);
        public bool IsVisible => visibleWhen == null || Eval(visibleWhen, true);

        /// <summary>설정 묶음·Play 간 유지에 쓰는 값이 있는 항목인지.</summary>
        public virtual bool HasValue => false;
        public bool InPresets => HasValue && !excludeFromPresets;
        /// <summary>버튼·선택처럼 '최근 사용'에 남길 항목인지.</summary>
        public virtual bool RemembersUse => true;

        public virtual string CaptureValue() => null;
        public virtual bool ApplyValue(string value) => false;
        /// <summary>핀 오버레이와 검색 결과에 보여 줄 현재 값.</summary>
        public virtual string DisplayValue() => null;
        public virtual bool RunHotkey() => false;

        internal bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            if (Contains(Label, query) || Contains(tip, query) || Contains(note, query))
                return true;
            if (section != null && (Contains(section.Title, query) || Contains(section.Tab, query)))
                return true;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (Contains(keywords[i], query))
                    return true;
            }
            return false;
        }

        protected T Eval<T>(Func<T> getter, T fallback)
        {
            if (getter == null)
                return fallback;
            try
            {
                return getter();
            }
            catch (Exception exception)
            {
                // 0.2초마다 읽으므로 같은 항목의 오류는 한 번만 남긴다.
                if (!reportedReadError)
                {
                    reportedReadError = true;
                    Debug.LogWarning($"[DebugHub] '{id}' 값을 읽지 못했어요: {exception.Message}");
                }
                return fallback;
            }
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrEmpty(source)
                && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class DebugToggle : DebugItem
    {
        private readonly Func<bool> get;
        private readonly Action<bool> set;

        internal DebugToggle(string label, Func<bool> get, Action<bool> set) : base(() => label)
        {
            this.get = get;
            this.set = set;
        }

        public override DebugItemKind Kind => DebugItemKind.Toggle;
        public override bool HasValue => true;
        public override bool RemembersUse => false;
        public bool Value => Eval(get, false);

        public override string CaptureValue() => Value ? "1" : "0";
        public override string DisplayValue() => Value ? "켜짐" : "꺼짐";

        public override bool ApplyValue(string value)
        {
            if (value != "1" && value != "0")
                return false;
            set?.Invoke(value == "1");
            return true;
        }

        public void Flip()
        {
            bool next = !Value;
            DebugRuntime.Run(this, $"{Label}: {(next ? "켜짐" : "꺼짐")}", () =>
            {
                set?.Invoke(next);
                return DebugResult.Ok();
            });
        }

        public override bool RunHotkey()
        {
            Flip();
            return true;
        }
    }

    /// <summary>한 줄에 버튼 1~5개. <see cref="Add(string, Action)"/>로 버튼을 더한다.</summary>
    public sealed class DebugButtons : DebugItem
    {
        internal sealed class Entry
        {
            public Func<string> Text;
            public Func<DebugResult> Action;
        }

        internal readonly List<Entry> entries = new List<Entry>(2);
        internal readonly bool showLabel;

        internal DebugButtons(Func<string> label, bool showLabel) : base(label)
        {
            this.showLabel = showLabel;
        }

        public override DebugItemKind Kind => DebugItemKind.Buttons;
        public int Count => entries.Count;

        public DebugButtons Add(string text, Action action) => Add(() => text, Wrap(action));
        public DebugButtons Add(string text, Func<DebugResult> action) => Add(() => text, action);
        public DebugButtons Add(Func<string> text, Action action) => Add(text, Wrap(action));

        public DebugButtons Add(Func<string> text, Func<DebugResult> action)
        {
            entries.Add(new Entry { Text = text ?? (() => "?"), Action = action });
            DebugRegistry.MarkDirty();
            return this;
        }

        public string TextOf(int index)
        {
            return index >= 0 && index < entries.Count ? Eval(entries[index].Text, "?") : "?";
        }

        public void Press(int index)
        {
            if (index < 0 || index >= entries.Count)
                return;
            string text = TextOf(index);
            string rowLabel = Label;
            string what = showLabel && !string.IsNullOrEmpty(rowLabel) && rowLabel != text
                ? $"{rowLabel} › {text}"
                : text;
            DebugRuntime.Run(this, what, entries[index].Action);
        }

        public override bool RunHotkey()
        {
            Press(0);
            return true;
        }

        private static Func<DebugResult> Wrap(Action action)
        {
            return () =>
            {
                action?.Invoke();
                return DebugResult.Ok();
            };
        }
    }

    /// <summary>선택과 목록 고르기의 공통 부분. 옵션은 매번 대리자로 읽으므로 실행 중에 바뀌어도 된다.</summary>
    public abstract class DebugOptionsItem : DebugItem
    {
        internal Func<int> count;
        internal Func<int, string> optionLabel;
        internal Func<int> currentIndex;
        internal Action<int> select;

        protected DebugOptionsItem(string label) : base(() => label)
        {
        }

        public override bool HasValue => true;
        public int OptionCount => Eval(count, 0);
        public int CurrentIndex => Eval(currentIndex, -1);

        public string CurrentLabel
        {
            get
            {
                int index = CurrentIndex;
                return index >= 0 && index < OptionCount ? OptionLabel(index) : "-";
            }
        }

        public string OptionLabel(int index)
        {
            try
            {
                return optionLabel != null ? optionLabel(index) : index.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return "?";
            }
        }

        public override string CaptureValue() => CurrentIndex >= 0 ? CurrentLabel : null;
        public override string DisplayValue() => CurrentLabel;

        public override bool ApplyValue(string value)
        {
            int total = OptionCount;
            for (int i = 0; i < total; i++)
            {
                if (OptionLabel(i) == value)
                {
                    select?.Invoke(i);
                    return true;
                }
            }
            return false;
        }

        public void Choose(int index)
        {
            if (index < 0 || index >= OptionCount)
                return;
            string text = OptionLabel(index);
            DebugRuntime.Run(this, $"{Label}: {text}", () =>
            {
                select?.Invoke(index);
                return DebugResult.Ok();
            });
        }

        public void Step(int delta)
        {
            int total = OptionCount;
            if (total == 0)
                return;
            int index = CurrentIndex;
            index = index < 0 ? 0 : ((index + delta) % total + total) % total;
            Choose(index);
        }

        public override bool RunHotkey()
        {
            Step(1);
            return true;
        }
    }

    public sealed class DebugChoice : DebugOptionsItem
    {
        internal DebugChoice(string label) : base(label)
        {
        }

        public override DebugItemKind Kind => DebugItemKind.Choice;
    }

    /// <summary>후보가 많은 선택(아이템 목록 등). 검색되는 목록으로 그리고, 설정 묶음에는 넣지 않는다.</summary>
    public sealed class DebugPicker : DebugOptionsItem
    {
        internal DebugPicker(string label) : base(label)
        {
            excludeFromPresets = true;
        }

        public override DebugItemKind Kind => DebugItemKind.Picker;
    }

    public sealed class DebugNumber : DebugItem
    {
        private readonly Func<float> get;
        private readonly Action<float> set;
        internal readonly float min;
        internal readonly float max;
        internal readonly float step;
        internal readonly string format;
        internal readonly bool integer;

        internal DebugNumber(string label, Func<float> get, Action<float> set, float min, float max,
            float step, string format, bool integer) : base(() => label)
        {
            this.get = get;
            this.set = set;
            this.min = Mathf.Min(min, max);
            this.max = Mathf.Max(min, max);
            this.step = step <= 0f ? 1f : step;
            this.format = string.IsNullOrEmpty(format) ? "0.##" : format;
            this.integer = integer;
        }

        public override DebugItemKind Kind => DebugItemKind.Number;
        public override bool HasValue => true;
        public float Value => Eval(get, 0f);

        public string Format(float value) => value.ToString(format, CultureInfo.InvariantCulture);
        public override string CaptureValue() => Value.ToString("R", CultureInfo.InvariantCulture);
        public override string DisplayValue() => Format(Value);

        public override bool ApplyValue(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return false;
            set?.Invoke(Normalize(parsed));
            return true;
        }

        public void SetValue(float value)
        {
            float next = Normalize(value);
            if (Mathf.Approximately(next, Value))
                return;
            DebugRuntime.Run(this, $"{Label}: {Format(next)}", () =>
            {
                set?.Invoke(next);
                return DebugResult.Ok();
            });
        }

        public void Nudge(int direction) => SetValue(Value + direction * step);

        private float Normalize(float value)
        {
            value = Mathf.Clamp(value, min, max);
            return integer ? Mathf.Round(value) : value;
        }
    }

    public sealed class DebugText : DebugItem
    {
        private readonly Func<string> get;
        private readonly Action<string> set;

        internal DebugText(string label, Func<string> get, Action<string> set) : base(() => label)
        {
            this.get = get;
            this.set = set;
            excludeFromPresets = true;
        }

        public override DebugItemKind Kind => DebugItemKind.Text;
        public override bool HasValue => true;
        public override bool RemembersUse => false;
        public string Value => Eval(get, string.Empty) ?? string.Empty;

        public override string CaptureValue() => Value;
        public override string DisplayValue() => Value;

        public override bool ApplyValue(string value)
        {
            set?.Invoke(value ?? string.Empty);
            return true;
        }

        public void Submit(string value)
        {
            value ??= string.Empty;
            if (value == Value)
                return;
            DebugRuntime.Run(this, $"{Label} 입력", () =>
            {
                set?.Invoke(value);
                return DebugResult.Ok();
            });
        }
    }

    public sealed class DebugReadout : DebugItem
    {
        private readonly Func<string> value;
        internal int lines = 1;

        internal DebugReadout(string label, Func<string> value) : base(() => label)
        {
            this.value = value;
        }

        public override DebugItemKind Kind => DebugItemKind.Readout;
        public override bool RemembersUse => false;
        public int LineCount => lines;
        public string Value => Eval(value, "-") ?? "-";
        public override string DisplayValue() => Value;
    }

    public sealed class DebugProgressItem : DebugItem
    {
        private readonly Func<DebugProgress> value;

        internal DebugProgressItem(string label, Func<DebugProgress> value) : base(() => label)
        {
            this.value = value;
        }

        public override DebugItemKind Kind => DebugItemKind.Progress;
        public override bool RemembersUse => false;
        public DebugProgress Value => Eval(value, default);

        public override string DisplayValue()
        {
            DebugProgress progress = Value;
            string percent = (progress.Ratio * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
            return string.IsNullOrEmpty(progress.Text) ? percent : $"{progress.Text} · {percent}";
        }
    }

    public sealed class DebugBar : DebugItem
    {
        private readonly Func<DebugBarSegment[]> value;

        internal DebugBar(string label, Func<DebugBarSegment[]> value) : base(() => label)
        {
            this.value = value;
        }

        public override DebugItemKind Kind => DebugItemKind.Bar;
        public override bool RemembersUse => false;
        public DebugBarSegment[] Segments => Eval(value, null) ?? Array.Empty<DebugBarSegment>();

        public override string DisplayValue()
        {
            DebugBarSegment[] segments = Segments;
            var parts = new string[segments.Length];
            for (int i = 0; i < segments.Length; i++)
                parts[i] = $"{segments[i].Label} {segments[i].Value.ToString("0.#", CultureInfo.InvariantCulture)}";
            return string.Join(" · ", parts);
        }
    }

    /// <summary>최후 수단. 모듈이 칸 안을 직접 그리고, 창이 다시 그릴 때 Dispose된다.</summary>
    public sealed class DebugCustom : DebugItem
    {
        internal readonly Func<RectTransform, IDisposable> build;
        internal readonly float height;

        internal DebugCustom(string label, Func<RectTransform, IDisposable> build, float height) : base(() => label)
        {
            this.build = build;
            this.height = Mathf.Max(20f, height);
        }

        public override DebugItemKind Kind => DebugItemKind.Custom;
        public override bool RemembersUse => false;
    }

    /// <summary>모든 항목에 붙이는 공통 옵션. 체이닝으로 쓴다.</summary>
    public static class DebugItemOptions
    {
        public static T Tip<T>(this T item, string text) where T : DebugItem
        {
            item.tip = text;
            return item;
        }

        public static T Note<T>(this T item, string text) where T : DebugItem
        {
            item.note = text;
            return item;
        }

        public static T EnabledWhen<T>(this T item, Func<bool> condition, string reason = null) where T : DebugItem
        {
            item.enabledWhen = condition;
            item.disabledReason = reason;
            return item;
        }

        public static T VisibleWhen<T>(this T item, Func<bool> condition) where T : DebugItem
        {
            item.visibleWhen = condition;
            return item;
        }

        public static T AfterChange<T>(this T item, Action action) where T : DebugItem
        {
            item.afterChange += action;
            return item;
        }

        public static T Pinnable<T>(this T item) where T : DebugItem
        {
            item.pinnable = true;
            return item;
        }

        public static T Hotkey<T>(this T item, Key key, bool ctrl = false) where T : DebugItem
        {
            item.hotkey = key;
            item.hotkeyCtrl = ctrl;
            DebugRegistry.MarkDirty();
            return item;
        }

        public static T Confirm<T>(this T item, string message) where T : DebugItem
        {
            item.confirm = message;
            return item;
        }

        public static T Persist<T>(this T item) where T : DebugItem
        {
            item.persist = true;
            DebugRegistry.MarkDirty();
            return item;
        }

        public static T NoPreset<T>(this T item) where T : DebugItem
        {
            item.excludeFromPresets = true;
            return item;
        }

        public static T Keywords<T>(this T item, params string[] words) where T : DebugItem
        {
            item.keywords = words ?? Array.Empty<string>();
            return item;
        }

        /// <summary>즐겨찾기·핀·설정 묶음이 쓰는 고정 ID. 라벨이 바뀌어도 유지하려면 지정한다.</summary>
        public static T WithId<T>(this T item, string id) where T : DebugItem
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                item.id = id;
                DebugRegistry.MarkDirty();
            }
            return item;
        }

        public static DebugReadout Lines(this DebugReadout item, int lines)
        {
            item.lines = Mathf.Clamp(lines, 1, 20);
            return item;
        }
    }
}
#endif
