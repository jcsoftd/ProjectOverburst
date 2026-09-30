#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>탭 안의 섹션 하나. 항목 등록 메서드를 가진다.</summary>
    public sealed class DebugSection
    {
        internal readonly List<DebugItem> items = new List<DebugItem>();

        internal DebugSection(string tab, string title, int order, string note)
        {
            Tab = tab;
            Title = title;
            Order = order;
            Note = note;
        }

        public string Tab { get; }
        public string Title { get; }
        public int Order { get; }
        public string Note { get; internal set; }
        public string Key => Tab + "/" + Title;
        public IReadOnlyList<DebugItem> Items => items;

        public DebugToggle Toggle(string label, Func<bool> get, Action<bool> set)
            => Add(new DebugToggle(label, get, set));

        /// <summary>라벨 칸을 비우고 라벨 글자를 가진 버튼 하나를 그린다.</summary>
        public DebugButtons Button(string label, Action action)
            => Add(new DebugButtons(() => label, false)).Add(label, action);

        public DebugButtons Button(string label, Func<DebugResult> action)
            => Add(new DebugButtons(() => label, false)).Add(label, action);

        public DebugButtons Button(Func<string> label, Action action)
            => Add(new DebugButtons(label, false)).Add(label, action);

        public DebugButtons Button(Func<string> label, Func<DebugResult> action)
            => Add(new DebugButtons(label, false)).Add(label, action);

        /// <summary>라벨 칸 + 버튼 여러 개. 버튼은 반환값의 Add로 더한다.</summary>
        public DebugButtons Buttons(string label)
            => Add(new DebugButtons(() => label, true));

        public DebugChoice Choice<T>(string label, Func<T> get, Action<T> set,
            Func<T, string> labelOf = null, IReadOnlyList<T> options = null)
        {
            IReadOnlyList<T> list = options ?? DefaultOptions<T>();
            return Choice(label, get, set, labelOf, () => list);
        }

        public DebugChoice Choice<T>(string label, Func<T> get, Action<T> set,
            Func<T, string> labelOf, Func<IReadOnlyList<T>> options)
        {
            var item = new DebugChoice(label);
            Bind(item, options, get, set, labelOf);
            return Add(item);
        }

        public DebugPicker Picker<T>(string label, Func<IReadOnlyList<T>> source, Func<T, string> labelOf,
            Func<T> get, Action<T> set)
        {
            var item = new DebugPicker(label);
            Bind(item, source, get, set, labelOf);
            return Add(item);
        }

        public DebugNumber Number(string label, Func<float> get, Action<float> set,
            float min, float max, float step = 1f, string format = "0.##")
            => Add(new DebugNumber(label, get, set, min, max, step, format, false));

        public DebugNumber Integer(string label, Func<int> get, Action<int> set,
            int min, int max, int step = 1)
            => Add(new DebugNumber(label,
                () => get != null ? get() : 0,
                value => set?.Invoke(Mathf.RoundToInt(value)),
                min, max, step, "0", true));

        public DebugText Text(string label, Func<string> get, Action<string> set)
            => Add(new DebugText(label, get, set));

        public DebugReadout Readout(string label, Func<string> value)
            => Add(new DebugReadout(label, value));

        public DebugProgressItem Progress(string label, Func<DebugProgress> value)
            => Add(new DebugProgressItem(label, value));

        public DebugBar Bar(string label, Func<DebugBarSegment[]> value)
            => Add(new DebugBar(label, value));

        public DebugCustom Custom(string label, Func<RectTransform, IDisposable> build, float height)
            => Add(new DebugCustom(label, build, height));

        private T Add<T>(T item) where T : DebugItem
        {
            item.section = this;
            item.id = Key + "/" + item.Label;
            items.Add(item);
            DebugRegistry.MarkDirty();
            return item;
        }

        private static void Bind<T>(DebugOptionsItem item, Func<IReadOnlyList<T>> options,
            Func<T> get, Action<T> set, Func<T, string> labelOf)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            Func<T, string> text = labelOf ?? (value => value != null ? value.ToString() : "-");
            item.count = () => options?.Invoke()?.Count ?? 0;
            item.optionLabel = index => text(options()[index]);
            item.currentIndex = () =>
            {
                IReadOnlyList<T> list = options?.Invoke();
                if (list == null || get == null)
                    return -1;
                T current = get();
                for (int i = 0; i < list.Count; i++)
                {
                    if (comparer.Equals(list[i], current))
                        return i;
                }
                return -1;
            };
            item.select = index =>
            {
                IReadOnlyList<T> list = options?.Invoke();
                if (list != null && index >= 0 && index < list.Count)
                    set?.Invoke(list[index]);
            };
        }

        private static IReadOnlyList<T> DefaultOptions<T>()
        {
            if (typeof(T).IsEnum)
                return (T[])Enum.GetValues(typeof(T));
            if (typeof(T) == typeof(bool))
                return (T[])(object)new[] { false, true };
            Debug.LogWarning($"[DebugHub] {typeof(T).Name} 선택에는 옵션 목록이 필요해요.");
            return Array.Empty<T>();
        }
    }

    /// <summary>
    /// 디버그 창의 등록부. 탭·섹션·항목 정의만 보관하고 값은 각 기능이 소유한다.
    /// 모듈은 <c>[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]</c>에서 등록한다.
    /// </summary>
    public static class DebugRegistry
    {
        private static readonly List<DebugSection> sections = new List<DebugSection>();
        private static readonly Dictionary<string, DebugItem> byId = new Dictionary<string, DebugItem>();
        private static readonly HashSet<string> warnedDuplicateIds = new HashSet<string>();
        private static int indexedRevision = -1;
        private static bool sortDirty;

        /// <summary>등록 내용이 바뀔 때마다 오른다. 창은 이 값으로 다시 그릴지 정한다.</summary>
        public static int Revision { get; private set; }

        public static IReadOnlyList<DebugSection> Sections
        {
            get
            {
                EnsureSorted();
                return sections;
            }
        }

        public static IEnumerable<DebugItem> AllItems
        {
            get
            {
                IReadOnlyList<DebugSection> list = Sections;
                for (int s = 0; s < list.Count; s++)
                {
                    List<DebugItem> items = list[s].items;
                    for (int i = 0; i < items.Count; i++)
                        yield return items[i];
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            sections.Clear();
            byId.Clear();
            warnedDuplicateIds.Clear();
            indexedRevision = -1;
            sortDirty = false;
            Revision = 0;
        }

        /// <summary>같은 탭·제목의 섹션이 있으면 그것을 돌려준다. 표시 순서는 탭 순서 → order → 제목이다.</summary>
        public static DebugSection Section(string tab, string title, int order = 100, string note = null)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                DebugSection existing = sections[i];
                if (existing.Tab == tab && existing.Title == title)
                {
                    if (note != null)
                        existing.Note = note;
                    return existing;
                }
            }

            var section = new DebugSection(tab, title, order, note);
            sections.Add(section);
            sortDirty = true;
            MarkDirty();
            return section;
        }

        public static bool HasTab(string tab)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (sections[i].Tab == tab && sections[i].items.Count > 0)
                    return true;
            }
            return false;
        }

        public static DebugItem Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            EnsureIndex();
            return byId.TryGetValue(id, out DebugItem item) ? item : null;
        }

        public static void MarkDirty()
        {
            unchecked { Revision++; }
        }

        private static void EnsureIndex()
        {
            if (indexedRevision == Revision)
                return;
            byId.Clear();
            foreach (DebugItem item in AllItems)
            {
                if (!byId.ContainsKey(item.id))
                {
                    byId.Add(item.id, item);
                }
                else if (warnedDuplicateIds.Add(item.id))
                {
                    Debug.LogWarning($"[DebugHub] 항목 ID '{item.id}'가 겹쳐요. 먼저 등록한 항목만 즐겨찾기·핀에 쓰여요.");
                }
            }
            indexedRevision = Revision;
        }

        private static void EnsureSorted()
        {
            if (!sortDirty)
                return;
            sections.Sort((a, b) =>
            {
                int compare = DebugTabs.IndexOf(a.Tab).CompareTo(DebugTabs.IndexOf(b.Tab));
                if (compare != 0)
                    return compare;
                compare = string.CompareOrdinal(a.Tab, b.Tab);
                if (compare != 0)
                    return compare;
                compare = a.Order.CompareTo(b.Order);
                return compare != 0 ? compare : string.CompareOrdinal(a.Title, b.Title);
            });
            sortDirty = false;
        }
    }
}
#endif
