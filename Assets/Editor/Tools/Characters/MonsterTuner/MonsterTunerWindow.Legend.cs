using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        private VisualElement legendRoot, activeLegend;
        private ScrollView legendOptions;
        private bool legendExpanded;
        [SerializeField] private List<string> hiddenOverlayKeys = new List<string>();
        [SerializeField] private List<string> enabledOverlayKeys = new List<string>();
        private void BuildLegend(VisualElement controls)
        {
            controls.Add(new Button(() => { legendExpanded = !legendExpanded; RefreshLegend(); }) { text = "표시·범례" });
            legendRoot = new VisualElement(); legendRoot.AddToClassList("mt-legend");
            legendRoot.style.position = Position.Absolute; legendRoot.style.top = 8; legendRoot.style.left = 8;
            legendRoot.style.maxWidth = 280; legendRoot.style.maxHeight = 350;
            legendRoot.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            legendRoot.RegisterCallback<WheelEvent>(e => e.StopPropagation());
            activeLegend = new VisualElement(); legendRoot.Add(activeLegend);
            legendOptions = new ScrollView(); legendOptions.style.maxHeight = 250; legendRoot.Add(legendOptions);
            viewport.Add(legendRoot);
            foreach (string key in hiddenOverlayKeys) viewport.Visibility[key] = false;
            foreach (string key in enabledOverlayKeys) viewport.Visibility[key] = true;
        }
        private void RefreshLegend()
        {
            if (legendRoot == null) return;
            if (viewport.Visibility.Count == 0 && viewport.Points.Count > 0)
            {
                foreach (var point in viewport.Points) viewport.Visibility[point.Key] = false;
                var body = viewport.Points.FirstOrDefault(p => p.Label.StartsWith("몸 충돌"));
                if (body != null) viewport.Visibility[body.Key] = true;
            }
            foreach (var point in viewport.Points) if (!viewport.Visibility.ContainsKey(point.Key)) viewport.Visibility[point.Key] = false;
            activeLegend.Clear(); legendOptions.Clear();
            if (legendExpanded)
            {
                var quick = new VisualElement(); quick.AddToClassList("mt-row");
                quick.Add(new Button(() => { foreach (var point in viewport.Points) viewport.Visibility[point.Key] = true; viewport.Visibility["projectile-path"] = true; RefreshLegend(); viewport.Refresh(); }) { text = "모두 켜기" });
                quick.Add(new Button(() => { foreach (string key in viewport.Visibility.Keys.ToArray()) viewport.Visibility[key] = false; RefreshLegend(); viewport.Refresh(); }) { text = "모두 끄기" });
                quick.Add(new Button(() => { foreach (string key in viewport.Visibility.Keys.ToArray()) viewport.Visibility[key] = viewport.Selected?.Key == key; RefreshLegend(); viewport.Refresh(); }) { text = "선택만" });
                activeLegend.Add(quick);
            }
            else
            {
                var visible = viewport.Points.Where(viewport.Visible).ToArray();
                foreach (var point in visible) activeLegend.Add(LegendLabel(point.Label, point.Color));
                if (viewport.Visibility.TryGetValue("projectile-path", out bool flight) && flight && workingAbility?.ExecutionMode == EnemyAbilityExecutionMode.Projectile) activeLegend.Add(LegendLabel("투사체 발사 궤적", new Color(.4f, 1f, .75f)));
                if (activeLegend.childCount == 0) activeLegend.Add(new Label("표시 꺼짐 · 표시·범례에서 선택"));
            }
            foreach (var point in viewport.Points)
            {
                var captured = point;
                var toggle = new Toggle { value = viewport.Visible(point), name = "overlay:" + point.Key,
                    text = "● " + point.Label + (point.Segments != null ? " · 범위/중심" : " · 점") };
                toggle.style.color = point.Color; toggle.style.fontSize = 11; toggle.style.whiteSpace = WhiteSpace.Normal;
                toggle.RegisterValueChangedCallback(e => { viewport.Visibility[captured.Key] = e.newValue; PersistVisibility(); RefreshLegend(); viewport.Refresh(); });
                legendOptions.Add(toggle);
            }
            if (workingAbility?.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
            {
                var toggle = new Toggle { text = "투사체 발사 궤적", value = viewport.Visibility.TryGetValue("projectile-path", out bool show) && show };
                toggle.RegisterValueChangedCallback(e => { viewport.Visibility["projectile-path"] = e.newValue; PersistVisibility(); RefreshLegend(); viewport.Refresh(); }); legendOptions.Add(toggle);
            }
            legendOptions.style.display = legendExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            PersistVisibility();
        }
        private static Label LegendLabel(string text, Color color)
        {
            var label = new Label("● " + text); label.style.color = color; label.style.fontSize = 11; label.style.whiteSpace = WhiteSpace.Normal; return label;
        }
        private void PersistVisibility()
        {
            enabledOverlayKeys = viewport.Visibility.Where(p => p.Value).Select(p => p.Key).ToList();
            hiddenOverlayKeys = viewport.Visibility.Where(p => !p.Value).Select(p => p.Key).ToList();
        }
    }
}
