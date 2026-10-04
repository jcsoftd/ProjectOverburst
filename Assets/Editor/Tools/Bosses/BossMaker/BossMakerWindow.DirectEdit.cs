using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.BossMaker
{
    public sealed partial class BossMakerWindow
    {
        string gestureMaterial, gestureAbility;
        int gestureGroup;
        enum ShapeHandle { None, Center, Radius, Inner, Angle, Width, Length }
        ShapeHandle geometryDrag;
        int geometryPointer = -1;
        void BeginGesture(string label)
        {
            preview.Playing = false; preview.FreezeFrame = true; gestureMaterial = EditorJsonUtility.ToJson(Draft.Material); gestureAbility = EditorJsonUtility.ToJson(Draft.Ability);
            Undo.IncrementCurrentGroup(); gestureGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(label);
            Undo.RecordObjects(new UnityEngine.Object[] { Draft.Material, Draft.Ability }, label);
        }
        void EndGesture(bool cancel)
        {
            if (gestureMaterial == null) return;
            if (cancel)
            { EditorJsonUtility.FromJsonOverwrite(gestureMaterial, Draft.Material); EditorJsonUtility.FromJsonOverwrite(gestureAbility, Draft.Ability); Draft.Material.ability = Draft.Ability; }
            else Undo.CollapseUndoOperations(gestureGroup);
            if (preview != null) preview.FreezeFrame = false;
            gestureMaterial = gestureAbility = null; Changed(); BuildFields();
        }
        internal void BeginTimingDrag(int phase, BossTimelineHandle handle)
        {
            strikeIndex = phase; if (preview != null) preview.Strike = phase; BeginGesture("보스 타격 구간 이동");
            var p = Draft.Material.tuning.parries[phase];
            if ((handle == BossTimelineHandle.ParryStart || handle == BossTimelineHandle.ParryEnd) && !p.overrideWindow)
            { var w = Draft.ParryWindow(phase, preview.BaseSpeed); p.startNormalized = w.x; p.endNormalized = w.y; p.overrideWindow = true; }
        }
        internal void MoveTimingDrag(int phase, BossTimelineHandle handle, float at)
        {
            var m = Draft.Material; var s = m.strikes[phase]; var p = m.tuning.parries[phase];
            if (handle == BossTimelineHandle.Impact)
            {
                float low = phase == 0 ? .05f : Mathf.Max(.05f, m.strikes[phase - 1].impact + .001f);
                float high = phase == m.strikes.Length - 1 ? .95f : Mathf.Min(.95f, m.strikes[phase + 1].impact - .001f);
                float next = Mathf.Clamp(at, low, high), delta = next - s.impact;
                s.impact = s.contactStart = next; s.contactEnd = Mathf.Clamp(s.contactEnd + delta, next, 1f);
                if (p.overrideWindow) { p.startNormalized = Mathf.Clamp(p.startNormalized + delta, 0, next); p.endNormalized = Mathf.Clamp(p.endNormalized + delta, p.startNormalized, next); }
            }
            else if (handle == BossTimelineHandle.ContactEnd) s.contactEnd = Mathf.Clamp(at, s.impact, 1f);
            else if (handle == BossTimelineHandle.ParryStart) p.startNormalized = Mathf.Clamp(at, 0f, p.endNormalized);
            else p.endNormalized = Mathf.Clamp(at, p.startNormalized, s.impact);
            Draft.SyncHits(); preview.Seek(s.impact); Changed(false);
        }
        void RegisterShapeEditing()
        {
            overlay.pickingMode = PickingMode.Position;
            overlay.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || preview == null || !preview.ShowGeometry || motionPicker.index > 0 || sourceView || Draft.Validate().Count > 0) return;
                geometryDrag = FindShapeHandle(e.localPosition);
                if (geometryDrag == ShapeHandle.None) return;
                BeginGesture("보스 판정 범위 조절"); geometryPointer = e.pointerId; overlay.CapturePointer(e.pointerId); overlay.Focus(); e.StopImmediatePropagation();
            });
            overlay.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (geometryDrag == ShapeHandle.None || !overlay.HasPointerCapture(e.pointerId)) return;
                var viewport = new Vector3(e.localPosition.x / overlay.contentRect.width, 1f - e.localPosition.y / overlay.contentRect.height, 0f);
                var ray = preview.Camera.ViewportPointToRay(viewport); var plane = new Plane(Vector3.up, preview.Origin(strikeIndex) + Vector3.up * .045f);
                if (!plane.Raycast(ray, out float distance)) return;
                var world = ray.GetPoint(distance); var s = Draft.Material.strikes[strikeIndex]; var local = Quaternion.Inverse(preview.Rotation(strikeIndex)) * (world - preview.Origin(strikeIndex));
                float radius = new Vector2(local.x, local.z).magnitude;
                if (geometryDrag == ShapeHandle.Center) { s.localOrigin.x += world.x - preview.Origin(strikeIndex).x; s.localOrigin.z += world.z - preview.Origin(strikeIndex).z; }
                if (geometryDrag == ShapeHandle.Radius) { s.radius = Mathf.Clamp(radius, Mathf.Max(.05f, s.innerRadius + .01f), 100f); if (s.shape == GroundIndicatorShape.Sector) s.innerRadius = Mathf.Max(s.innerRadius, s.radius * .05f); }
                if (geometryDrag == ShapeHandle.Inner) s.innerRadius = Mathf.Clamp(radius, s.shape == GroundIndicatorShape.Sector ? s.radius * .05f : .01f, s.radius - .01f);
                if (geometryDrag == ShapeHandle.Angle) s.angle = Mathf.Clamp(Mathf.Abs(Mathf.Atan2(local.x, local.z)) * Mathf.Rad2Deg * 2f, 1f, 359.5f);
                if (geometryDrag == ShapeHandle.Width) s.width = Mathf.Clamp(Mathf.Abs(local.x) * 2f, .05f, 100f);
                if (geometryDrag == ShapeHandle.Length) s.length = Mathf.Clamp(local.z, .05f, 100f);
                Draft.SyncHits(); Changed(false); e.StopImmediatePropagation();
            });
            overlay.RegisterCallback<PointerUpEvent>(e => { if (geometryDrag == ShapeHandle.None) return; geometryDrag = ShapeHandle.None; overlay.ReleasePointer(e.pointerId); EndGesture(false); e.StopImmediatePropagation(); });
            overlay.RegisterCallback<PointerCaptureOutEvent>(_ => { if (geometryDrag != ShapeHandle.None) { geometryDrag = ShapeHandle.None; EndGesture(false); } });
            overlay.RegisterCallback<KeyDownEvent>(e => { if (geometryDrag != ShapeHandle.None && e.keyCode == KeyCode.Escape) { geometryDrag = ShapeHandle.None; overlay.ReleasePointer(geometryPointer); EndGesture(true); e.StopImmediatePropagation(); } });
        }
        ShapeHandle FindShapeHandle(Vector2 point)
        {
            ShapeHandle nearest = ShapeHandle.None; float best = 12f;
            foreach (var pair in ShapeHandles())
            { float distance = Vector2.Distance(point, preview.Project(pair.point, overlay.contentRect.size)); if (distance < best) { best = distance; nearest = pair.kind; } }
            return nearest;
        }
        System.Collections.Generic.IEnumerable<(ShapeHandle kind, Vector3 point)> ShapeHandles()
        {
            var s = Draft.Material.strikes[strikeIndex]; var origin = preview.Origin(strikeIndex) + Vector3.up * .045f; var rotation = preview.Rotation(strikeIndex);
            if (Draft.Material.delivery == EnemyBossMaterialDelivery.Melee) yield return (ShapeHandle.Center, origin);
            if (s.shape == GroundIndicatorShape.Rectangle)
            { yield return (ShapeHandle.Width, origin + rotation * new Vector3(s.width * .5f, 0, s.length * .5f)); yield return (ShapeHandle.Length, origin + rotation * Vector3.forward * s.length); }
            else
            {
                yield return (ShapeHandle.Radius, origin + rotation * Vector3.forward * s.radius);
                if (s.shape == GroundIndicatorShape.Donut || s.shape == GroundIndicatorShape.Sector) yield return (ShapeHandle.Inner, origin + rotation * Vector3.forward * s.innerRadius);
                if (s.shape == GroundIndicatorShape.Sector) yield return (ShapeHandle.Angle, origin + rotation * Quaternion.Euler(0, s.angle * .5f, 0) * Vector3.forward * s.radius);
            }
        }
        void DrawShapeHandles(Painter2D painter, Vector2 size)
        {
            if (sourceView) return;
            foreach (var pair in ShapeHandles())
            { var p = preview.Project(pair.point, size); BossMakerTimeline.Rect(painter, new Rect(p - Vector2.one * 4, Vector2.one * 8), pair.kind == ShapeHandle.Center ? new Color(.45f, .88f, 1f) : new Color(1f, .9f, .65f)); }
        }
    }
}
