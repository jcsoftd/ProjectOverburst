#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Overburst.DebugTools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>모든 등록 항목의 UI가 원래 모델 인스턴스와 동작 대리자에 연결되는지 검사한다.
/// 동작 대리자는 검사 중에만 관찰용 함수로 바꿔 계정/게임 부작용을 막고 finally에서 복원한다.</summary>
public static class DebugHubBindingVerifier
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static object ValidateCurrentPlay()
    {
        if (!Application.isPlaying || DebugHub.Instance == null)
            throw new InvalidOperationException("디버그 허브 Play가 필요합니다.");
        var results = new List<object>();
        object window = typeof(DebugHub).GetField("window", Fields).GetValue(DebugHub.Instance);
        foreach (DebugItem item in DebugRegistry.AllItems.ToArray())
        {
            var restore = new Stack<Action>();
            try
            {
                Replace(item, typeof(DebugItem), "enabledWhen", null, restore);
                Replace(item, typeof(DebugItem), "visibleWhen", null, restore);
                Replace(item, typeof(DebugItem), "confirm", null, restore);
                DebugHub.OpenTab(item.Section.Tab);
                object row = ((IEnumerable)window.GetType().GetField("rows", Fields).GetValue(window))
                    .Cast<object>().Single(view => ReferenceEquals(view.GetType().GetField("Item", Fields).GetValue(view), item));
                RectTransform root = (RectTransform)row.GetType().GetProperty("Root").GetValue(row);
                row.GetType().GetMethod("Refresh").Invoke(row, new object[] { true });
                Button Button(string name) => root.GetComponentsInChildren<Button>(false).First(b => b.name == name);
                int calls = 0;
                switch (item)
                {
                    case DebugToggle toggle:
                        bool expected = !toggle.Value, observed = !expected;
                        Replace(toggle, typeof(DebugToggle), "set", (Action<bool>)(value => { calls++; observed = value; }), restore);
                        Button("Switch").onClick.Invoke();
                        Require(calls == 1 && observed == expected, item.Id);
                        break;
                    case DebugButtons buttons:
                        IList entries = (IList)typeof(DebugButtons).GetField("entries", Fields).GetValue(buttons);
                        for (int i = 0; i < entries.Count; i++)
                        {
                            object entry = entries[i];
                            Replace(entry, entry.GetType(), "Action", (Func<DebugResult>)(() => { calls++; return DebugResult.Ok(); }), restore);
                            Button("Button " + i).onClick.Invoke();
                            Require(calls == i + 1, item.Id + "/" + i);
                        }
                        break;
                    case DebugOptionsItem options:
                        int selected = -1;
                        Replace(options, typeof(DebugOptionsItem), "select", (Action<int>)(index => { calls++; selected = index; }), restore);
                        if (options.OptionCount > 0)
                        {
                            if (item.Kind == DebugItemKind.Picker)
                            {
                                Button("Open").onClick.Invoke();
                                Button[] slots = root.GetComponentsInChildren<Button>(false).Where(b => b.name.StartsWith("Option ")).ToArray();
                                Require(slots.Length == Mathf.Min(options.OptionCount, 12), item.Id + " picker slots");
                                int index = slots.Length > 1 && options.CurrentIndex == 0 ? 1 : 0;
                                slots[index].onClick.Invoke();
                                Require(calls == 1, item.Id);
                            }
                            else if (options.OptionCount <= 6)
                            {
                                int index = options.OptionCount > 1 && options.CurrentIndex == 0 ? 1 : 0;
                                Button("Option " + index).onClick.Invoke();
                                Require(calls == 1, item.Id);
                            }
                            else
                            {
                                Button("Next").onClick.Invoke();
                                Require(calls == 1 && selected >= 0 && selected < options.OptionCount, item.Id);
                            }
                        }
                        else
                            Require(root.GetComponentsInChildren<TMP_Text>(false).Any(t => t.text.Contains("선택지 없음") || t.name == "Open" || t.text.Contains("›")), item.Id + " empty options");
                        row.GetType().GetMethod("ResetTransientState").Invoke(row, null);
                        break;
                    case DebugNumber number:
                        float observedNumber = float.NaN;
                        Replace(number, typeof(DebugNumber), "set", (Action<float>)(value => { calls++; observedNumber = value; }), restore);
                        float min = (float)typeof(DebugNumber).GetField("min", Fields).GetValue(number);
                        float max = (float)typeof(DebugNumber).GetField("max", Fields).GetValue(number);
                        float candidate = Mathf.Approximately(number.Value, min) ? max : min;
                        root.GetComponentsInChildren<TMP_InputField>(false).Single().onEndEdit.Invoke(candidate.ToString(CultureInfo.InvariantCulture));
                        Require(calls == (Mathf.Approximately(candidate, number.Value) ? 0 : 1)
                            && (calls == 0 || Mathf.Approximately(candidate, observedNumber)), item.Id);
                        break;
                    case DebugText text:
                        string observedText = null;
                        Replace(text, typeof(DebugText), "set", (Action<string>)(value => { calls++; observedText = value; }), restore);
                        root.GetComponentsInChildren<TMP_InputField>(false).Single().onEndEdit.Invoke("프리팹 연결 검증");
                        Require(calls == 1 && observedText == "프리팹 연결 검증", item.Id);
                        break;
                    default:
                        Require(root.GetComponentsInChildren<TMP_Text>(false).Length > 0, item.Id + " readout");
                        break;
                }
                results.Add(new { id = item.Id, kind = item.Kind.ToString(), status = "PASS", observedCalls = calls });
            }
            finally
            {
                while (restore.Count > 0)
                    restore.Pop()();
            }
        }
        return new { status = "PASS", registeredItems = DebugRegistry.AllItems.Count(), checkedItems = results.Count, results };
    }

    private static void Replace(object target, Type declaringType, string name, object value, Stack<Action> restore)
    {
        FieldInfo field = declaringType.GetField(name, Fields);
        object previous = field.GetValue(target);
        restore.Push(() => field.SetValue(target, previous));
        field.SetValue(target, value);
    }

    private static void Require(bool condition, string id)
    {
        if (!condition)
            throw new InvalidOperationException("UI 모델/동작 연결 불일치: " + id);
    }
}
#endif
