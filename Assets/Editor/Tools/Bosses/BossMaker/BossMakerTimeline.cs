using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.BossMaker
{
    internal enum BossTimelineHandle { Impact, ContactEnd, ParryStart, ParryEnd }
    internal sealed class BossMakerTimeline : VisualElement
    {
        BossMakerDraft draft;
        float progress, baseSpeed;
        readonly Action<float, int> seek;
        readonly Action<int, BossTimelineHandle> begin;
        readonly Action<int, BossTimelineHandle, float> move;
        readonly Action<bool> end;
        BossTimelineHandle dragging;
        int dragPhase = -1, dragPointer = -1;
        public bool ReadOnly;
        public BossMakerTimeline(Action<float, int> action, Action<int, BossTimelineHandle> beginEdit, Action<int, BossTimelineHandle, float> moveEdit, Action<bool> endEdit)
        {
            seek = action; begin = beginEdit; move = moveEdit; end = endEdit;
            name = "boss-timeline"; style.height = 80; style.flexShrink = 0; focusable = true;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (draft == null || e.button != 0) return;
                int phase = Mathf.Clamp((int)(e.localPosition.y / 34f), 0, draft.Material.strikes.Length - 1);
                var s = draft.Material.strikes[phase]; float width = Mathf.Max(1f, contentRect.width - 64f);
                float at = Mathf.Clamp01((e.localPosition.x - 52f) / width), tolerance = 9f / width;
                var window = draft.ParryWindow(phase, baseSpeed); float y = e.localPosition.y - phase * 34f;
                BossTimelineHandle? handle = null;
                if (y < 19 && draft.Material.IsStrikeParryable(phase))
                { if (Mathf.Abs(at - window.x) < tolerance) handle = BossTimelineHandle.ParryStart; else if (Mathf.Abs(at - window.y) < tolerance) handle = BossTimelineHandle.ParryEnd; }
                if (handle == null && Mathf.Abs(at - s.impact) < tolerance) handle = BossTimelineHandle.Impact;
                if (handle == null && Mathf.Abs(at - s.contactEnd) < tolerance) handle = BossTimelineHandle.ContactEnd;
                if (handle.HasValue && !ReadOnly)
                { dragPhase = phase; dragPointer = e.pointerId; dragging = handle.Value; begin(phase, dragging); this.CapturePointer(e.pointerId); }
                else seek(at, phase);
                Focus(); e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e => { if (dragPhase < 0 || !this.HasPointerCapture(e.pointerId)) return; move(dragPhase, dragging, Mathf.Clamp01((e.localPosition.x - 52f) / Mathf.Max(1f, contentRect.width - 64f))); e.StopPropagation(); });
            RegisterCallback<PointerUpEvent>(e => { if (dragPhase < 0) return; dragPhase = -1; this.ReleasePointer(e.pointerId); end(false); e.StopPropagation(); });
            RegisterCallback<PointerCaptureOutEvent>(_ => { if (dragPhase < 0) return; dragPhase = -1; end(false); });
            RegisterCallback<KeyDownEvent>(e => { if (dragPhase >= 0 && e.keyCode == KeyCode.Escape) { dragPhase = -1; this.ReleasePointer(dragPointer); end(true); e.StopPropagation(); } });
        }
        public void Set(BossMakerDraft value, float at, float speed)
        {
            bool changed = draft != value; draft = value; progress = at; baseSpeed = speed;
            if (changed)
            {
                Clear(); style.height = Mathf.Max(48f, draft.Material.strikes.Length * 34f + 8f);
                for (int i = 0; i < draft.Material.strikes.Length; i++)
                { var label = new Label((i + 1) + "타") { pickingMode = PickingMode.Ignore }; label.style.position = Position.Absolute; label.style.left = 10; label.style.top = i * 34 + 12; Add(label); }
            }
            MarkDirtyRepaint();
        }
        void Draw(MeshGenerationContext context)
        {
            if (draft == null) return; var p = context.painter2D; float width = Mathf.Max(1, contentRect.width - 64f);
            for (int i = 0; i < draft.Material.strikes.Length; i++)
            {
                float top = i * 34f + 8; Rect(p, new Rect(52, top, width, 22), new Color(.16f, .19f, .24f));
                var s = draft.Material.strikes[i];
                if (draft.Material.IsStrikeParryable(i))
                { var w = draft.ParryWindow(i, baseSpeed); Rect(p, new Rect(52 + w.x * width, top, Mathf.Max(1, (w.y - w.x) * width), 9), new Color(.25f, .83f, .68f));
                    Rect(p, new Rect(49 + w.x * width, top - 2, 6, 13), new Color(.54f, 1f, .84f)); Rect(p, new Rect(49 + w.y * width, top - 2, 6, 13), new Color(.54f, 1f, .84f)); }
                Rect(p, new Rect(52 + s.contactStart * width, top + 11, Mathf.Max(2, (s.contactEnd - s.contactStart) * width), 11), new Color(.96f, .46f, .25f));
                Line(p, new Vector2(52 + s.impact * width, top), new Vector2(52 + s.impact * width, top + 23), new Color(1f, .86f, .42f), 2);
                Rect(p, new Rect(49 + s.impact * width, top + 13, 6, 11), new Color(1f, .86f, .42f)); Rect(p, new Rect(49 + s.contactEnd * width, top + 13, 6, 11), new Color(1f, .66f, .4f));
            }
            Line(p, new Vector2(52 + progress * width, 0), new Vector2(52 + progress * width, contentRect.height), Color.white, 1.4f);
        }
        internal static void Rect(Painter2D painter, Rect rect, Color color)
        { painter.fillColor = color; painter.BeginPath(); painter.MoveTo(rect.min); painter.LineTo(new Vector2(rect.xMax, rect.yMin)); painter.LineTo(rect.max); painter.LineTo(new Vector2(rect.xMin, rect.yMax)); painter.ClosePath(); painter.Fill(); }
        internal static void Line(Painter2D painter, Vector2 a, Vector2 b, Color color, float width)
        { painter.strokeColor = color; painter.lineWidth = width; painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke(); }
    }
}
