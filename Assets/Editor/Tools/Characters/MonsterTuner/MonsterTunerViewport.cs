using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.MonsterTuner
{
    internal sealed class MonsterTunerPoint
    {
        public string Key, Label, OriginalName;
        public Color Color;
        public Func<Vector3> World;
        public Action<Vector3> Move;
        public Func<IEnumerable<(Vector3, Vector3)>> Segments;
    }
    internal sealed class MonsterTunerViewport : VisualElement
    {
        private readonly Image image;
        private readonly MonsterTunerPreviewStage stage;
        private readonly Label[] axisLabels = new Label[3];
        public readonly List<MonsterTunerPoint> Points = new List<MonsterTunerPoint>();
        public MonsterTunerPoint Selected { get; private set; }
        public bool ShowPoints = true, ShowVolumes = true;
        public readonly Dictionary<string, bool> Visibility = new Dictionary<string, bool>();
        public bool Visible(MonsterTunerPoint point) => Visibility.TryGetValue(point.Key, out bool visible) && visible;
        public float Snap = .01f;
        public event Action<MonsterTunerPoint> SelectionChanged;
        public event Action EditBegan, EditEnded, EditCancelled, PlaybackRequested;
        public event Action Changed;
        private int pointer = -1, button = -1, axis = -1;
        private Vector2 last, dragStart, projectedAxis;
        private Vector3 startWorld, axisWorld;
        private float startAxisParameter;
        private static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        private static readonly Color[] Colors = { new Color(1f, .4f, .4f), new Color(.4f, 1f, .55f), new Color(.4f, .65f, 1f) };
        public MonsterTunerViewport(MonsterTunerPreviewStage renderer)
        {
            stage = renderer; focusable = true; AddToClassList("mt-viewport");
            image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            image.StretchToParentSize(); Add(image);
            var overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.StretchToParentSize(); overlay.generateVisualContent += Draw; Add(overlay);
            for (int i = 0; i < 3; i++)
            {
                axisLabels[i] = new Label(new[] { "X", "Y", "Z" }[i]) { pickingMode = PickingMode.Ignore };
                axisLabels[i].style.position = Position.Absolute; axisLabels[i].style.color = Colors[i];
                axisLabels[i].style.unityFontStyleAndWeight = FontStyle.Bold; axisLabels[i].style.fontSize = 12f; Add(axisLabels[i]);
            }
            RegisterCallback<PointerDownEvent>(Down); RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(Up); RegisterCallback<PointerCaptureOutEvent>(_ => Finish(false));
            RegisterCallback<WheelEvent>(e => { stage.Zoom(e.delta.y); Changed?.Invoke(); e.StopPropagation(); });
            RegisterCallback<KeyDownEvent>(Key);
            RegisterCallback<FocusOutEvent>(_ => Finish(false));
        }
        public void Refresh()
        {
            image.image = stage.Surface;
            for (int i = 0; i < 3; i++)
            {
                bool show = ShowPoints && Selected?.Move != null && Visible(Selected) && stage.Camera != null;
                axisLabels[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show)
                {
                    Vector2 p = Project(Selected.World() + Axes[i] * AxisLength(Selected.World()));
                    axisLabels[i].style.left = p.x + 5f; axisLabels[i].style.top = p.y - 8f;
                }
            }
            foreach (var child in Children()) child.MarkDirtyRepaint();
        }
        public void Select(MonsterTunerPoint point, bool notify = true) { Selected = point; if (point != null && notify) Visibility[point.Key] = true; if (notify) SelectionChanged?.Invoke(point); Refresh(); }
        public Vector2 Project(Vector3 world)
        {
            if (stage.Camera == null) return Vector2.zero;
            var p = stage.Camera.WorldToViewportPoint(world);
            return new Vector2(p.x * contentRect.width, (1f - p.y) * contentRect.height);
        }
        private float AxisLength(Vector3 world) => stage.Camera != null ? Mathf.Max(.05f, Vector3.Distance(stage.Camera.transform.position, world) * .075f) : .5f;
        private void Down(PointerDownEvent e)
        {
            Focus(); last = dragStart = this.WorldToLocal(e.position);
            if (e.button == 0)
            {
                if (e.clickCount == 2) { stage.Fit(); Changed?.Invoke(); return; }
                axis = -1;
                if (Selected?.Move != null && Visible(Selected))
                {
                    var center = Project(Selected.World()); float length = AxisLength(Selected.World());
                    for (int i = 0; i < 3; i++)
                    {
                        var end = Project(Selected.World() + Axes[i] * length);
                        if ((end - center).magnitude >= 18f && Distance(last, center, end) < 8f) { axis = i; break; }
                    }
                }
                if (axis < 0)
                {
                    float best = 14f; MonsterTunerPoint nearest = null;
                    foreach (var point in Points)
                    {
                        if (!ShowPoints || !Visible(point)) continue;
                        if (stage.Camera == null || stage.Camera.WorldToViewportPoint(point.World()).z <= 0f) continue;
                        float distance = Vector2.Distance(last, Project(point.World()));
                        if (distance < best) { best = distance; nearest = point; }
                    }
                    if (nearest != null) Select(nearest);
                    return;
                }
                stage.Playing = false; startWorld = Selected.World(); axisWorld = Axes[axis];
                projectedAxis = Project(startWorld + axisWorld) - Project(startWorld);
                startAxisParameter = AxisParameter(last);
                EditBegan?.Invoke();
            }
            else if (e.button != 1 && e.button != 2) return;
            pointer = e.pointerId; button = e.button; this.CapturePointer(pointer); e.StopPropagation();
        }
        private void Move(PointerMoveEvent e)
        {
            if (e.pointerId != pointer || !this.HasPointerCapture(pointer)) return;
            Vector2 position = this.WorldToLocal(e.position), delta = position - last; last = position;
            if (button == 1) stage.Orbit(delta);
            else if (button == 2) stage.Pan(delta);
            else if (axis >= 0 && projectedAxis.sqrMagnitude > 1f)
            {
                float amount = AxisParameter(position) - startAxisParameter;
                if (e.shiftKey) amount *= .1f;
                if (Snap > 0f) amount = Mathf.Round(amount / Snap) * Snap;
                Selected.Move(startWorld + axisWorld * amount);
            }
            Changed?.Invoke(); e.StopPropagation();
        }
        private float AxisParameter(Vector2 local)
        {
            var ray = stage.Camera.ViewportPointToRay(new Vector3(local.x / Mathf.Max(1f, contentRect.width), 1f - local.y / Mathf.Max(1f, contentRect.height), 0f));
            float dot = Vector3.Dot(axisWorld, ray.direction);
            Vector3 offset = startWorld - ray.origin;
            return (dot * Vector3.Dot(ray.direction, offset) - Vector3.Dot(axisWorld, offset)) / Mathf.Max(.0001f, 1f - dot * dot);
        }
        private void Up(PointerUpEvent e) { if (e.pointerId == pointer) { Finish(false); e.StopPropagation(); } }
        private void Finish(bool cancel)
        {
            int old = pointer; pointer = -1; button = -1;
            bool editing = axis >= 0; axis = -1;
            if (old >= 0 && this.HasPointerCapture(old)) this.ReleasePointer(old);
            if (editing) { if (cancel) EditCancelled?.Invoke(); else EditEnded?.Invoke(); }
            Changed?.Invoke();
        }
        private void Key(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape) Finish(true);
            else if (e.keyCode == KeyCode.F) stage.Fit();
            else if (e.keyCode == KeyCode.Space) PlaybackRequested?.Invoke();
            else return;
            Changed?.Invoke(); e.StopPropagation();
        }
        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var delta = b - a; return Vector2.Distance(p, a + delta * Mathf.Clamp01(Vector2.Dot(p - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude)));
        }
        private void Draw(MeshGenerationContext context)
        {
            if (stage.Camera == null) return;
            var painter = context.painter2D;
            void Line(Vector3 a, Vector3 b, Color color, float width = 1f)
            {
                if (stage.Camera.WorldToViewportPoint(a).z <= 0f || stage.Camera.WorldToViewportPoint(b).z <= 0f) return;
                painter.strokeColor = color; painter.lineWidth = width; painter.BeginPath(); painter.MoveTo(Project(a)); painter.LineTo(Project(b)); painter.Stroke();
            }
            if (ShowVolumes) foreach (var point in Points) if (Visible(point) && point.Segments != null)
                foreach (var segment in point.Segments()) Line(segment.Item1, segment.Item2, new Color(point.Color.r, point.Color.g, point.Color.b, .65f));
            if (stage.Ability?.ExecutionMode == EnemyAbilityExecutionMode.Projectile && Visibility.TryGetValue("projectile-path", out bool flight) && flight)
            {
                var launch = stage.LaunchOrigin; var forward = stage.Actor.transform.forward;
                Line(launch, launch + forward * (stage.Ability.Range + 2f), new Color(.4f, 1f, .75f, .7f), 2f);
                if (stage.ProjectileVisible)
                {
                    painter.fillColor = new Color(.4f, 1f, .75f); painter.BeginPath();
                    painter.Arc(Project(stage.ProjectilePosition), 5f, 0f, 360f); painter.Fill();
                }
            }
            if (!ShowPoints) return;
            foreach (var point in Points)
            {
                if (!Visible(point)) continue;
                var world = point.World(); if (stage.Camera.WorldToViewportPoint(world).z <= 0f) continue;
                painter.fillColor = point.Color; painter.strokeColor = point == Selected ? Color.white : new Color(0f, 0f, 0f, .8f);
                painter.lineWidth = point == Selected ? 2.5f : 1f;
                painter.BeginPath(); painter.Arc(Project(world), point == Selected ? 6f : 4f, 0f, 360f); painter.Fill(); painter.Stroke();
            }
            if (Selected?.Move == null || !Visible(Selected)) return;
            Vector3 origin = Selected.World(); float axisLength = AxisLength(origin);
            for (int i = 0; i < 3; i++) Line(origin, origin + Axes[i] * axisLength, Colors[i], 3f);
        }
    }
}
