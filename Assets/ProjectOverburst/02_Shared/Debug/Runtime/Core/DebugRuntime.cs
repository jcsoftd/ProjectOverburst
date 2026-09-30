#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 항목 실행의 단일 경로. 사용 조건 검사, 확인창, 예외 처리, 실행 기록(최근 30건)을 맡는다.
    /// 항목 실행 중 예외가 나도 게임은 멈추지 않고 실패로 기록된다.
    /// </summary>
    public static class DebugRuntime
    {
        public struct Record
        {
            public DateTime At;
            public string Text;
            public bool Success;
            public string Message;
        }

        public const int HistoryLimit = 30;
        private static readonly List<Record> history = new List<Record>(HistoryLimit + 1);

        /// <summary>오래된 것부터 새것 순서.</summary>
        public static IReadOnlyList<Record> History => history;
        public static event Action<Record> Executed;
        /// <summary>창이 설정한다. 확인이 필요한 항목이면 (항목, 실행할 동작)을 넘긴다.</summary>
        public static Action<DebugItem, Action> ConfirmHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            history.Clear();
            Executed = null;
            ConfirmHandler = null;
        }

        public static void Run(DebugItem item, string what, Func<DebugResult> action)
        {
            if (item == null)
                return;
            if (!item.IsEnabled)
            {
                Report(what, DebugResult.Fail(item.DisabledReason ?? "지금은 쓸 수 없어요"));
                return;
            }
            if (!string.IsNullOrEmpty(item.confirm) && ConfirmHandler != null && NeedsConfirm(item))
            {
                ConfirmHandler(item, () => Execute(item, what, action));
                return;
            }
            Execute(item, what, action);
        }

        private static bool NeedsConfirm(DebugItem item)
        {
            if (item.confirmWhen == null)
                return true;
            try
            {
                return item.confirmWhen();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return true;
            }
        }

        /// <summary>항목을 거치지 않는 동작(설정 묶음 적용 등)의 결과를 기록한다.</summary>
        public static void Report(string what, DebugResult result)
        {
            var record = new Record
            {
                At = DateTime.Now,
                Text = what,
                Success = result.Success,
                Message = result.Message
            };
            history.Add(record);
            if (history.Count > HistoryLimit)
                history.RemoveAt(0);
            try
            {
                Executed?.Invoke(record);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public static string Describe(Record record)
        {
            return string.IsNullOrEmpty(record.Message) ? record.Text : $"{record.Text} · {record.Message}";
        }

        private static void Execute(DebugItem item, string what, Func<DebugResult> action)
        {
            DebugResult result;
            try
            {
                result = action != null ? action() : DebugResult.Ok();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                result = DebugResult.Fail($"{exception.GetType().Name}: {exception.Message}");
            }

            if (result.Success)
            {
                try
                {
                    item.afterChange?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                if (item.RemembersUse)
                    DebugPrefs.RememberRecent(item.id);
                if (item.persist && item.HasValue)
                    DebugPrefs.SaveValue(item);
            }
            Report(what, result);
        }
    }
}
#endif
