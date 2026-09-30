#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 디버그 창 상태를 PlayerPrefs(<c>OB.Debug.*</c>)에 저장한다. 계정 저장(Easy Save)에는 아무것도 쓰지 않는다.
    /// 목록 값은 줄바꿈으로 이어 붙인 문자열이다.
    /// </summary>
    public static class DebugPrefs
    {
        public const string Prefix = "OB.Debug.";
        public const string WindowKey = Prefix + "Window";
        public const string OverlayKey = Prefix + "Overlay";
        public const string PresetsKey = Prefix + "Presets";
        private const string FavoritesKey = Prefix + "Favorites";
        private const string RecentKey = Prefix + "Recent";
        private const string PinsKey = Prefix + "Pins";
        private const string SectionsKey = Prefix + "Sections";
        private const string ValuePrefix = Prefix + "Value.";
        public const int RecentLimit = 5;
        public const int PinLimit = 10;

        /// <summary>처음 실행할 때의 핀. 옛 분대 오버레이가 보여 주던 값이다(3단계에서 같은 ID로 등록).</summary>
        public static readonly string[] DefaultPins =
        {
            "enemies.live.total",
            "enemies.live.aggro",
            "enemies.live.deaths",
            "enemies.squad.state",
            "enemies.squad.roles"
        };

        private static List<string> favorites;
        private static List<string> recent;
        private static List<string> pins;
        private static HashSet<string> collapsed;

        /// <summary>즐겨찾기·최근 사용·핀이 바뀔 때마다 오른다.</summary>
        public static int Version { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            favorites = null;
            recent = null;
            pins = null;
            collapsed = null;
            Version = 0;
        }

        public static IReadOnlyList<string> Favorites => favorites ??= ReadList(FavoritesKey, null);
        public static IReadOnlyList<string> Recent => recent ??= ReadList(RecentKey, null);
        public static IReadOnlyList<string> Pins => pins ??= ReadList(PinsKey, DefaultPins);

        public static bool IsFavorite(string id) => id != null && Contains(Favorites, id);
        public static bool IsPinned(string id) => id != null && Contains(Pins, id);

        public static bool ToggleFavorite(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            var list = (List<string>)Favorites;
            bool nowFavorite = !list.Remove(id);
            if (nowFavorite)
                list.Add(id);
            WriteList(FavoritesKey, list);
            return nowFavorite;
        }

        public static void RememberRecent(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;
            var list = (List<string>)Recent;
            list.Remove(id);
            list.Insert(0, id);
            if (list.Count > RecentLimit)
                list.RemoveRange(RecentLimit, list.Count - RecentLimit);
            WriteList(RecentKey, list);
        }

        /// <summary>핀을 켜거나 끈다. 10개가 차 있으면 false.</summary>
        public static DebugResult TogglePin(string id)
        {
            if (string.IsNullOrEmpty(id))
                return DebugResult.Fail("ID가 없는 항목이에요");
            var list = (List<string>)Pins;
            if (list.Remove(id))
            {
                WriteList(PinsKey, list);
                return DebugResult.Ok("고정 해제");
            }
            if (CountRegisteredPins(list) >= PinLimit)
                return DebugResult.Fail($"핀은 {PinLimit}개까지예요");
            list.Add(id);
            WriteList(PinsKey, list);
            return DebugResult.Ok("오버레이에 고정");
        }

        public static void ClearPins()
        {
            var list = (List<string>)Pins;
            list.Clear();
            WriteList(PinsKey, list);
        }

        public static bool IsCollapsed(string sectionKey)
        {
            EnsureCollapsed();
            return sectionKey != null && collapsed.Contains(sectionKey);
        }

        public static void SetCollapsed(string sectionKey, bool value)
        {
            if (string.IsNullOrEmpty(sectionKey))
                return;
            EnsureCollapsed();
            bool changed = value ? collapsed.Add(sectionKey) : collapsed.Remove(sectionKey);
            if (changed)
                PlayerPrefs.SetString(SectionsKey, string.Join("\n", collapsed));
        }

        public static void SaveValue(DebugItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id) || !item.HasValue)
                return;
            string value = item.CaptureValue();
            if (value != null)
                PlayerPrefs.SetString(ValuePrefix + item.Id, value);
        }

        public static bool TryLoadValue(string id, out string value)
        {
            string key = ValuePrefix + id;
            if (!string.IsNullOrEmpty(id) && PlayerPrefs.HasKey(key))
            {
                value = PlayerPrefs.GetString(key);
                return true;
            }
            value = null;
            return false;
        }

        private static int CountRegisteredPins(List<string> list)
        {
            // 아직 등록되지 않은 기본 핀 ID는 자리 수에 넣지 않는다.
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (DebugRegistry.Find(list[i]) != null)
                    count++;
            }
            return count;
        }

        private static void EnsureCollapsed()
        {
            if (collapsed != null)
                return;
            collapsed = new HashSet<string>(ReadList(SectionsKey, null));
        }

        private static bool Contains(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id)
                    return true;
            }
            return false;
        }

        private static List<string> ReadList(string key, string[] fallback)
        {
            if (!PlayerPrefs.HasKey(key))
                return fallback != null ? new List<string>(fallback) : new List<string>();
            string raw = PlayerPrefs.GetString(key);
            var list = new List<string>();
            foreach (string part in raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0 && !list.Contains(trimmed))
                    list.Add(trimmed);
            }
            return list;
        }

        private static void WriteList(string key, List<string> list)
        {
            PlayerPrefs.SetString(key, string.Join("\n", list));
            unchecked { Version++; }
        }
    }
}
#endif
