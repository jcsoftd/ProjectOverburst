using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.BossMaker
{
    public sealed partial class BossMakerWindow
    {
        List<EnemyBossMaterialCollection.Motion> supportMotions = new List<EnemyBossMaterialCollection.Motion>();
        void BuildPreviewUI(VisualElement center)
        {
            var bar = Row("bm-transport"); center.Add(bar);
            playButton = Button("▶ 재생", TogglePlay, "boss-play"); bar.Add(playButton);
            bar.Add(Button("◀ 1F", () => StepFrame(-1), "boss-frame-back")); bar.Add(Button("1F ▶", () => StepFrame(1), "boss-frame-next"));
            var loop = new Toggle("반복") { value = true }; loop.RegisterValueChangedCallback(e => { if (preview != null) preview.Loop = e.newValue; }); bar.Add(loop);
            var speed = new DropdownField(new List<string> { "감상 0.25×", "감상 0.5×", "감상 1×", "감상 2×" }, 2);
            speed.RegisterValueChangedCallback(_ => { if (preview != null) preview.AuditionSpeed = new[] { .25f, .5f, 1f, 2f }[speed.index]; }); bar.Add(speed);
            motionPicker = new DropdownField(new List<string> { "공격 모션" }, 0) { name = "boss-motion" }; motionPicker.style.flexGrow = 1;
            motionPicker.RegisterValueChangedCallback(_ => { if (preview != null && Draft != null) preview.Load(sourceDraft ?? Draft, motionPicker.index > 0 && motionPicker.index <= supportMotions.Count ? supportMotions[motionPicker.index - 1].runtime : null); renderDirty = true; }); bar.Add(motionPicker);
            previewHost = new VisualElement { name = "boss-viewport", focusable = true }; previewHost.AddToClassList("bm-viewport"); center.Add(previewHost);
            image = new Image { name = "boss-preview", scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore }; image.AddToClassList("bm-image"); previewHost.Add(image);
            overlay = new VisualElement { name = "boss-geometry", focusable = true, tooltip = "노란 네모: 반경·안쪽 반경·각도 조절 / 파란 네모: 중심 이동" }; overlay.AddToClassList("bm-overlay"); previewHost.Add(overlay); overlay.generateVisualContent += DrawOverlay; RegisterShapeEditing();
            previewLabel = new Label { name = "boss-preview-label", pickingMode = PickingMode.Ignore }; previewLabel.AddToClassList("bm-preview-label"); previewHost.Add(previewLabel);
            int dragging = -1; Vector2 previous = default;
            previewHost.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0 && e.button != 2) return; dragging = e.button; previous = e.position; previewHost.CapturePointer(e.pointerId); previewHost.Focus(); e.StopPropagation(); });
            previewHost.RegisterCallback<PointerMoveEvent>(e => { if (dragging < 0 || preview == null || !previewHost.HasPointerCapture(e.pointerId)) return; Vector2 delta = (Vector2)e.position - previous; previous = e.position; if (dragging == 0) preview.Orbit(delta); else preview.Pan(delta); renderDirty = true; });
            previewHost.RegisterCallback<PointerUpEvent>(e => { dragging = -1; previewHost.ReleasePointer(e.pointerId); }); previewHost.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = -1);
            previewHost.RegisterCallback<WheelEvent>(e => { preview?.Zoom(e.delta.y); renderDirty = true; e.StopPropagation(); });
            previewHost.RegisterCallback<GeometryChangedEvent>(_ => renderDirty = true);
            var views = Row("bm-views"); center.Add(views); var labels = new[] { "사선", "정면", "측면", "탑뷰" };
            for (int i = 0; i < labels.Length; i++) { int view = i; views.Add(Button(labels[i], () => { preview?.View(view); renderDirty = true; }, "boss-view-" + i)); }
            views.Add(Button("맞춤 F", () => { preview?.Fit(); renderDirty = true; }));
            var geometry = new Toggle("판정") { name = "boss-show-geometry", value = true }; geometry.RegisterValueChangedCallback(e => { if (preview != null) preview.ShowGeometry = e.newValue; renderDirty = true; }); views.Add(geometry);
            var all = new Toggle("전체 타격") { name = "boss-all-strikes", value = true }; all.RegisterValueChangedCallback(e => { if (preview != null) preview.ShowAllStrikes = e.newValue; renderDirty = true; }); views.Add(all);
            var height = new Toggle("높이") { value = false }; height.RegisterValueChangedCallback(e => { if (preview != null) preview.ShowHeight = e.newValue; renderDirty = true; }); views.Add(height);
            var compare = new Toggle("원본 보기") { name = "boss-show-source" }; compare.RegisterValueChangedCallback(e => SetSourceView(e.newValue)); views.Add(compare);
            var time = Row("bm-time"); center.Add(time); frameInput = new FloatField("원본 프레임") { name = "boss-frame", isDelayed = true }; frameInput.style.width = 170;
            frameInput.RegisterValueChangedCallback(e => { if (preview != null && preview.Clip != null) Seek(e.newValue / Mathf.Max(1f, preview.Clip.length * preview.Clip.frameRate)); }); time.Add(frameInput);
            timeLabel = new Label(); timeLabel.AddToClassList("bm-time-label"); time.Add(timeLabel);
            scrub = new Slider(0, 1) { name = "boss-scrub" }; scrub.RegisterValueChangedCallback(e => Seek(e.newValue)); center.Add(scrub);
            var legend = new Label("각 타격의 위 줄: 패링 허용 구간  ·  아래 줄: 피해 접촉 창  ·  노란 선: 타격/발사"); legend.AddToClassList("bm-muted"); center.Add(legend);
            timeline = new BossMakerTimeline((p, phase) => { strikeIndex = phase; if (preview != null) preview.Strike = phase; Seek(p); BuildFields(); }, BeginTimingDrag, MoveTimingDrag, EndGesture); center.Add(timeline);
            var footer = new Label("네모 드래그: 범위 · 타임라인 손잡이: 타격/패링 · 빈 곳 드래그: 회전\n휠 확대 · 가운데 드래그 이동 · Space 재생 · ← → 1F · Esc 조절 취소\n청록 기둥: 1.8m · 파란 궤적: 프리뷰 표적을 향한 비행 경로"); footer.AddToClassList("bm-note"); center.Add(footer);
        }
        void RebuildMotionPicker()
        {
            supportMotions = selectedCollection.motions.Where(m => m != null && m.IsPlayable && !m.rootMotionVariant).ToList();
            motionPicker.choices = new[] { "공격 모션" }.Concat(supportMotions.Select(m => m.id)).ToList(); motionPicker.SetValueWithoutNotify("공격 모션");
        }
        void TogglePlay() { if (preview == null) return; if (Draft.Validate().Count > 0) { SetMessage("설정 오류를 수정한 뒤 재생하세요."); return; } if (preview.Elapsed >= preview.TotalDuration) preview.Seek(0); preview.Playing = !preview.Playing; renderDirty = true; UpdateTransport(); }
        internal void Seek(float value) { if (preview == null) return; preview.Playing = false; preview.Seek(value); renderDirty = true; UpdateTransport(); }
        void StepFrame(int direction) { if (preview?.Clip == null) return; Seek(preview.Progress + direction / Mathf.Max(1f, preview.Clip.length * preview.Clip.frameRate)); }
        void UpdateTransport()
        {
            if (preview == null || Draft == null || preview.Clip == null) return;
            var active = preview.ActiveDraft ?? Draft; var m = active.Material; scrub.SetValueWithoutNotify(preview.Progress);
            frameInput.SetValueWithoutNotify(preview.Progress * preview.Clip.length * preview.Clip.frameRate);
            timeLabel.text = $"원본 {preview.Progress * preview.Clip.length:0.000}s / {preview.Clip.length:0.000}s  ·  실제 {preview.Elapsed:0.000}s / {preview.TotalDuration:0.000}s";
            playButton.text = preview.Playing ? "Ⅱ 일시정지" : "▶ 재생";
            previewLabel.text = $"{(sourceView ? "원본" : "편집 사본")} · {preview.Clip.name}\n{(motionPicker.index == 0 ? $"{strikeIndex + 1}타 선택 · {ShapeLabel(m.strikes[strikeIndex].shape)} · 피해 ×{m.DamageMultiplier:0.##} / 속도 ×{m.AnimationSpeedMultiplier:0.##}" : "기본 모션 검토")}";
            timeline.ReadOnly = sourceView; timeline.Set(active, preview.Progress, preview.BaseSpeed);
        }
        void DrawOverlay(MeshGenerationContext context)
        {
            if (preview == null || Draft == null || !preview.ShowGeometry || motionPicker.index > 0 || !Draft.Material.IsValid) return;
            var painter = context.painter2D; var size = overlay.contentRect.size; var active = preview.ActiveDraft ?? Draft; var m = active.Material;
            for (int phase = 0; phase < m.strikes.Length; phase++)
            {
                if (!preview.ShowAllStrikes && phase != strikeIndex) continue;
                var s = m.strikes[phase]; var window = active.ParryWindow(phase, preview.BaseSpeed);
                bool contact = preview.Progress >= s.impact && preview.Progress <= s.contactEnd;
                bool parry = m.IsStrikeParryable(phase) && preview.Progress >= window.x && preview.Progress <= window.y;
                Color color = parry ? new Color(.3f, .95f, .74f) : contact ? new Color(1f, .35f, .23f) : new Color(1f, .73f, .34f);
                if (phase != strikeIndex) color.a = .45f;
                painter.BeginPath();
                foreach (var outline in preview.Outlines(phase)) Path(painter, outline.Select(p => preview.Project(p, size)).ToArray(), true);
                painter.fillColor = new Color(color.r, color.g, color.b, phase == strikeIndex ? .15f : .06f); painter.Fill(FillRule.OddEven);
                painter.strokeColor = color; painter.lineWidth = phase == strikeIndex ? 2.3f : 1.3f; painter.Stroke();
                var origin = preview.Project(preview.Origin(phase) + Vector3.up * .045f, size);
                BossMakerTimeline.Line(painter, origin - Vector2.right * 5, origin + Vector2.right * 5, color, 2);
                BossMakerTimeline.Line(painter, origin - Vector2.up * 5, origin + Vector2.up * 5, color, 2);
                if (preview.ShowHeight)
                    foreach (var outline in preview.Outlines(phase))
                    {
                        painter.BeginPath(); Path(painter, outline.Select(p => preview.Project(p + Vector3.up * s.maximumHeight, size)).ToArray(), true); painter.strokeColor = new Color(color.r, color.g, color.b, .35f); painter.lineWidth = 1f; painter.Stroke();
                        for (int i = 0; i < outline.Length; i += Mathf.Max(1, outline.Length / 8)) BossMakerTimeline.Line(painter, preview.Project(outline[i] + Vector3.up * s.minimumHeight, size), preview.Project(outline[i] + Vector3.up * s.maximumHeight, size), new Color(color.r, color.g, color.b, .35f), 1);
                    }
            }
            foreach (var trajectory in preview.Trajectories())
            { painter.BeginPath(); Path(painter, trajectory.Select(p => preview.Project(p, size)).ToArray(), false); painter.strokeColor = new Color(.4f, .77f, 1f, .85f); painter.lineWidth = 1.6f; painter.Stroke(); }
            foreach (var point in preview.ProjectilePositions())
            {
                var p = preview.Project(point, size); BossMakerTimeline.Rect(painter, new Rect(p - Vector2.one * 3, Vector2.one * 6), new Color(.6f, .86f, 1f));
                for (int plane = 0; plane < 2; plane++)
                {
                    int axis = plane; painter.BeginPath();
                    Path(painter, Enumerable.Range(0, 33).Select(i => { float a = i * Mathf.PI / 16f; var offset = axis == 0 ? new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) : new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0); return preview.Project(point + offset * m.projectileRadius, size); }).ToArray(), true);
                    painter.strokeColor = new Color(.5f, .83f, 1f, .9f); painter.lineWidth = 1.4f; painter.Stroke();
                }
            }
            if (m.delivery != EnemyBossMaterialDelivery.Melee)
            { var target = preview.Project(preview.Target + Vector3.up * .045f, size); BossMakerTimeline.Line(painter, target - Vector2.one * 7, target + Vector2.one * 7, Color.cyan, 2); BossMakerTimeline.Line(painter, target + new Vector2(-7, 7), target + new Vector2(7, -7), Color.cyan, 2); }
            DrawShapeHandles(painter, size);
        }
        void SetSourceView(bool value)
        {
            if (Draft == null || preview == null) return; float at = preview.Progress; sourceView = value;
            rootVisualElement.Q<Toggle>("boss-show-source")?.SetValueWithoutNotify(value);
            sourceDraft?.Dispose(); sourceDraft = value ? new BossMakerDraft(Draft.Source) : null;
            preview.Load(sourceDraft ?? Draft, motionPicker.index > 0 && motionPicker.index <= supportMotions.Count ? supportMotions[motionPicker.index - 1].runtime : null); preview.Seek(at); renderDirty = true; fields.SetEnabled(!value);
            SetMessage(value ? "원본을 같은 프레임에서 비교 중입니다. 원본 보기를 끄면 편집을 계속합니다." : "편집 사본으로 돌아왔습니다.");
        }
        static void Path(Painter2D painter, Vector2[] points, bool close)
        { if (points.Length == 0) return; painter.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]); if (close) painter.ClosePath(); }
    }
}
