#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>
    /// Unity 로그를 받아 최근 50줄과 오류·경고 수를 보관한다.
    /// 로그는 다른 스레드에서도 오므로 큐에 넣고, 디버그 창이 메인 스레드에서 옮긴다.
    /// </summary>
    public static class DebugLogCapture
    {
        public struct Entry
        {
            public LogType Type;
            public string Text;
            public DateTime At;
        }

        private const int Limit = 50;
        private const int MaxLineLength = 160;
        private static readonly ConcurrentQueue<Entry> pending = new ConcurrentQueue<Entry>();
        private static readonly List<Entry> recent = new List<Entry>(Limit + 1);
        private static readonly StringBuilder builder = new StringBuilder(1024);
        private static bool installed;

        public static int ErrorCount { get; private set; }
        public static int WarningCount { get; private set; }
        public static int Version { get; private set; }
        public static IReadOnlyList<Entry> Recent => recent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            Uninstall();
            while (pending.TryDequeue(out _))
            {
            }
            recent.Clear();
            ErrorCount = 0;
            WarningCount = 0;
            Version = 0;
            Install();
        }

        public static void Install()
        {
            if (installed)
                return;
            Application.logMessageReceivedThreaded += Handle;
            installed = true;
        }

        public static void Uninstall()
        {
            if (!installed)
                return;
            Application.logMessageReceivedThreaded -= Handle;
            installed = false;
        }

        public static void Drain()
        {
            bool changed = false;
            while (pending.TryDequeue(out Entry entry))
            {
                recent.Add(entry);
                if (recent.Count > Limit)
                    recent.RemoveAt(0);
                if (IsError(entry.Type))
                    ErrorCount++;
                else if (entry.Type == LogType.Warning)
                    WarningCount++;
                changed = true;
            }
            if (changed)
                unchecked { Version++; }
        }

        public static void Clear()
        {
            Drain();
            recent.Clear();
            ErrorCount = 0;
            WarningCount = 0;
            unchecked { Version++; }
        }

        /// <summary>최근 로그를 새것부터 서식 있는 글자로 만든다.</summary>
        public static string FormatRecent(int max, DebugHubStyle style)
        {
            if (recent.Count == 0)
                return "<color=" + DebugHubStyle.Hex(style.muted) + ">로그 없음</color>";
            builder.Clear();
            int shown = 0;
            for (int i = recent.Count - 1; i >= 0 && shown < max; i--, shown++)
            {
                Entry entry = recent[i];
                Color color = IsError(entry.Type) ? style.error : entry.Type == LogType.Warning ? style.warn : style.muted;
                if (shown > 0)
                    builder.Append('\n');
                builder.Append("<color=").Append(DebugHubStyle.Hex(color)).Append(">● ")
                    .Append(entry.At.ToString("HH:mm:ss")).Append("</color> <noparse>")
                    .Append(entry.Text).Append("</noparse>");
            }
            return builder.ToString();
        }

        private static bool IsError(LogType type)
        {
            return type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        }

        private static void Handle(string condition, string stackTrace, LogType type)
        {
            string text = condition ?? string.Empty;
            int newline = text.IndexOf('\n');
            if (newline >= 0)
                text = text.Substring(0, newline);
            text = text.Replace("</noparse>", string.Empty).TrimEnd();
            if (text.Length > MaxLineLength)
                text = text.Substring(0, MaxLineLength) + "…";
            pending.Enqueue(new Entry { Type = type, Text = text, At = DateTime.Now });
        }
    }
}
#endif
