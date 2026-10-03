using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overburst.DebugTools
{
    /// <summary>정식 DebugHub 프리팹에 제작된 재생 안내. 창을 닫아도 현재 장면과 조작을 볼 수 있다.</summary>
    public sealed class VisualPlayOverlay : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] TMP_Text title, observe, progress;
        [SerializeField] Button pause, replay, next, stop, checkedButton, problemButton, open;
        [SerializeField] GameObject details;
        [SerializeField] Button expand;
        [SerializeField] TMP_Text pauseLabel, expandLabel;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool expanded;
        void OnEnable()
        {
            pause.onClick.AddListener(Pause); replay.onClick.AddListener(Replay); next.onClick.AddListener(Next);
            stop.onClick.AddListener(Stop); checkedButton.onClick.AddListener(Checked); problemButton.onClick.AddListener(Problem); open.onClick.AddListener(Open);
            expand.onClick.AddListener(ToggleDetails);
            ApplyDetails();
        }
        void OnDisable()
        {
            pause.onClick.RemoveListener(Pause); replay.onClick.RemoveListener(Replay); next.onClick.RemoveListener(Next);
            stop.onClick.RemoveListener(Stop); checkedButton.onClick.RemoveListener(Checked); problemButton.onClick.RemoveListener(Problem); open.onClick.RemoveListener(Open);
            expand.onClick.RemoveListener(ToggleDetails);
        }
        void Update()
        {
            var state = VisualPlayBridge.State;
            bool visible = state.Running && !DebugHub.IsOpen;
            if (panel.activeSelf != visible) panel.SetActive(visible);
            if (!visible) return;
            title.text = state.Current;
            observe.text = state.Observe;
            progress.text = $"{state.Completed}/{state.Planned}  ·  {state.Phase}";
            pauseLabel.text = state.Paused || state.Waiting ? "계속" : "항목 뒤 멈춤";
        }
        void ToggleDetails() { expanded = !expanded; ApplyDetails(); }
        void ApplyDetails()
        {
            details.SetActive(expanded);
            ((RectTransform)panel.transform).sizeDelta = new Vector2(390, expanded ? 232 : 112);
            expandLabel.text = expanded ? "접기" : "설명";
        }
        void Pause() => VisualPlayBridge.Control(VisualPlayControl.Pause);
        void Replay() => VisualPlayBridge.Control(VisualPlayControl.Replay);
        void Next() => VisualPlayBridge.Control(VisualPlayControl.Next);
        void Stop() => VisualPlayBridge.Control(VisualPlayControl.Stop);
        void Checked() => VisualPlayBridge.Review(VisualPlayReview.Checked);
        void Problem() => VisualPlayBridge.Review(VisualPlayReview.Problem);
        void Open() => DebugHub.OpenTab(DebugTabs.VisualPlay);
#else
        void Awake() { if (panel != null) panel.SetActive(false); }
#endif
    }
}
