#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace Overburst.DebugTools
{
    /// <summary>디버그 창 탭 이름과 표시 순서. 모듈은 이 상수로 탭을 고른다.</summary>
    public static class DebugTabs
    {
        public const string Favorites = "★ 즐겨찾기";
        public const string Player = "플레이어";
        public const string Combat = "전투";
        public const string Enemies = "적·AI";
        public const string Spawn = "스폰·시험장";
        public const string Items = "아이템·경제";
        public const string World = "던전·씬";
        public const string Presentation = "UI·연출";
        public const string SystemTab = "시스템";

        public static readonly string[] Order =
        {
            Favorites, Player, Combat, Enemies, Spawn, Items, World, Presentation, SystemTab
        };

        /// <summary>목록에 없는 탭 이름은 맨 뒤로 보낸다.</summary>
        public static int IndexOf(string tab)
        {
            int index = Array.IndexOf(Order, tab);
            return index < 0 ? Order.Length : index;
        }
    }
}
#endif
