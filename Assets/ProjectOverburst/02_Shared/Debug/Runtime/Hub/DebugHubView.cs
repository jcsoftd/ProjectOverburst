using UnityEngine;
using UnityEngine.EventSystems;

namespace Overburst.DebugTools
{
    /// <summary>프리팹의 영구 참조와 제작된 항목 목록. 릴리스에서도 직렬화 형식을 유지한다.</summary>
    public sealed class DebugHubView : MonoBehaviour
    {
        public const string ResourcePath = "Debug/PF_OverburstDebugHub";
        [SerializeField] private DebugHubStyle style;
        [SerializeField] private RectTransform window;
        [SerializeField] private RectTransform tabs;
        [SerializeField] private RectTransform content;
        [SerializeField] private RectTransform overlay;
        [SerializeField] private RectTransform[] pages;
        [SerializeField] private string[] itemIds;
        [SerializeField] private EventSystem fallbackEventSystem;

        public DebugHubStyle Style => style;
        public RectTransform Window => window;
        public RectTransform Tabs => tabs;
        public RectTransform Content => content;
        public RectTransform Overlay => overlay;
        public RectTransform[] Pages => pages;
        public string[] ItemIds => itemIds;
        public EventSystem FallbackEventSystem => fallbackEventSystem;
    }
}
