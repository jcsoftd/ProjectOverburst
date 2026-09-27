using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class GreatswordElementFxTunerWindow
{
    [SerializeField] private int rangeTarget = 1;
    [SerializeField] private bool showRangeHandles = true;
    private VisualElement rangeOverlay;
    private enum RangeHandle { None, Tip, Guard, Center, Width }
    private RangeHandle rangeHandle;
    private Vector2 rangeDragStart, rangeDragAxis, rangeDragSide;
    private float rangeStartCenter, rangeStartLength, rangeStartWidth;
    private string rangeDragKey;
    private bool rangeDragTrail;
    private bool rangeStartDirty;

    private string RangeKey => ElementKeys[System.Array.IndexOf(Elements, selectedElement)] + "Settings.";
    private string CenterField(bool trail) => trail ? "trailCenter" : "bladeCenter";
    private string LengthField(bool trail) => trail ? "trailLength" : "bladeLength";
    private string WidthField(bool trail) => trail ? "trailScale" : "bladeThickness";

    private void BuildRangeToolbar(VisualElement parent)
    {
        if (rangeTarget == 0) { showRangeHandles = false; rangeTarget = 1; }
        var toolbar = Row();
        var visible = new Toggle("조절선 표시") { name = "show-effect-range-handles", value = showRangeHandles };
        visible.style.marginRight = 15;
        visible.RegisterValueChangedCallback(evt =>
        {
            EndDrag();
            showRangeHandles = evt.newValue;
            rangeOverlay?.MarkDirtyRepaint();
        });
        toolbar.Add(visible);
        var selector = new DropdownField("직접 조절", new List<string> { "검신 효과", "트레일" }, rangeTarget - 1)
            { name = "effect-range-target" };
        selector.RegisterValueChangedCallback(_ =>
        {
            EndDrag();
            rangeTarget = selector.index + 1;
            rangeOverlay?.MarkDirtyRepaint();
        });
        toolbar.Add(selector);
        parent.Add(toolbar);
        parent.Add(new Label("끝점: 길이 · 선/중앙점: 검날 방향 위치 · 옆점: 폭 · Esc: 드래그 취소")
            { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 5 } });
    }

    private void BuildRangeOverlay()
    {
        rangeOverlay = new VisualElement { pickingMode = PickingMode.Ignore, name = "effect-range-handles" };
        rangeOverlay.style.position = Position.Absolute;
        rangeOverlay.style.left = rangeOverlay.style.right = rangeOverlay.style.top = rangeOverlay.style.bottom = 0;
        rangeOverlay.generateVisualContent += DrawRangeHandles;
        previewImage.Add(rangeOverlay);
        previewImage.focusable = true;
        previewImage.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape || dragMode != DragMode.AdjustRange) return;
            SetRangeValue(CenterField(rangeDragTrail), rangeStartCenter);
            SetRangeValue(LengthField(rangeDragTrail), rangeStartLength);
            SetRangeValue(WidthField(rangeDragTrail), rangeStartWidth);
            dirty = rangeStartDirty;
            UpdateStatus();
            EndDrag();
            QueueRender();
            evt.StopPropagation();
        });
    }

    // The line is the authored emission interval, not the outer edge of drifting particles.
    private bool GetRangePoints(out Vector2 tip, out Vector2 guard, out Vector2 center,
        out Vector2 width, out Vector2 axis, out Vector2 side)
    {
        tip = guard = center = width = axis = side = default;
        if (!showRangeHandles || rangeTarget == 0 || previewFx == null || previewVisual == null || previewCamera == null
            || previewImage.contentRect.width < 2 || previewImage.contentRect.height < 2) return false;
        string key = RangeKey;
        bool trail = rangeTarget == 2;
        if (!draft.ContainsKey(key + CenterField(trail))) return false;
        Bounds bounds;
        using (var serialized = new SerializedObject(previewFx))
            bounds = serialized.FindProperty("bladeEffectBounds").boundsValue;
        Vector3 scale = previewVisual.transform.lossyScale;
        Vector3 reciprocal = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        Vector2 Project(float z)
        {
            Vector3 world = previewVisual.transform.TransformPoint(Vector3.Scale(
                new Vector3(bounds.center.x, bounds.center.y, z), reciprocal));
            Vector3 p = previewCamera.WorldToViewportPoint(world);
            Rect rect = previewImage.contentRect;
            return new Vector2(rect.x + p.x * rect.width, rect.y + (1f - p.y) * rect.height);
        }
        Vector2 start = Project(bounds.min.z);
        axis = Project(bounds.max.z) - start;
        if (axis.sqrMagnitude < 400f) return false; // Orbit out of an end-on view to edit accurately.
        side = new Vector2(-axis.y, axis.x).normalized;
        float c = draft[key + CenterField(trail)], length = draft[key + LengthField(trail)];
        // Offset the ruler slightly so its handles do not obscure the blade.
        center = Project(bounds.min.z + bounds.size.z * c) + side * 18f;
        tip = Project(bounds.min.z + bounds.size.z * (c - length * .5f)) + side * 18f;
        guard = Project(bounds.min.z + bounds.size.z * (c + length * .5f)) + side * 18f;
        width = center + side * (30f + 30f * draft[key + WidthField(trail)]);
        return true;
    }

    private void DrawRangeHandles(MeshGenerationContext context)
    {
        if (!GetRangePoints(out var tip, out var guard, out var center, out var width, out _, out _)) return;
        Color color = rangeTarget == 2 ? new Color(1f, .72f, .2f) : new Color(.3f, .9f, 1f);
        // Explicit quads avoid Painter2D arc tessellation in the Editor renderer.
        var mesh = context.Allocate(40, 60);
        ushort index = 0;
        DrawHandleLine(mesh, tip, guard, color, ref index);
        DrawHandleLine(mesh, center, width, color, ref index);
        foreach (Vector2 point in new[] { tip, guard, center, width })
        {
            DrawHandleSquare(mesh, point, 8f, color, ref index);
            DrawHandleSquare(mesh, point, 5f, new Color(.12f, .16f, .2f), ref index);
        }
    }

    private static void DrawHandleLine(MeshWriteData mesh, Vector2 from, Vector2 to, Color color, ref ushort index)
    {
        Vector2 direction = to - from;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * 1.25f;
        DrawHandleQuad(mesh, from + normal, from - normal, to - normal, to + normal, color, ref index);
    }

    private static void DrawHandleSquare(MeshWriteData mesh, Vector2 point, float radius, Color color, ref ushort index)
    {
        DrawHandleQuad(mesh, point + new Vector2(-radius, -radius), point + new Vector2(radius, -radius),
            point + new Vector2(radius, radius), point + new Vector2(-radius, radius), color, ref index);
    }

    private static void DrawHandleQuad(MeshWriteData mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color, ref ushort index)
    {
        foreach (Vector2 point in new[] { a, b, c, d })
            mesh.SetNextVertex(new Vertex { position = new Vector3(point.x, point.y, Vertex.nearZ), tint = color });
        mesh.SetNextIndex(index); mesh.SetNextIndex((ushort)(index + 1)); mesh.SetNextIndex((ushort)(index + 2));
        mesh.SetNextIndex((ushort)(index + 2)); mesh.SetNextIndex((ushort)(index + 3)); mesh.SetNextIndex(index);
        index += 4;
    }

    private bool TryBeginRangeDrag(PointerDownEvent evt)
    {
        if (evt.button != 0 || evt.altKey || !GetRangePoints(out var tip, out var guard,
            out var center, out var width, out var axis, out var side)) return false;
        Vector2 point = previewImage.WorldToLocal(evt.position);
        rangeHandle = HitRangeHandle(point, tip, guard, center, width);
        if (rangeHandle == RangeHandle.None) return false;
        rangeDragKey = RangeKey;
        rangeDragTrail = rangeTarget == 2;
        rangeStartCenter = draft[rangeDragKey + CenterField(rangeDragTrail)];
        rangeStartLength = draft[rangeDragKey + LengthField(rangeDragTrail)];
        rangeStartWidth = draft[rangeDragKey + WidthField(rangeDragTrail)];
        rangeStartDirty = dirty;
        rangeDragStart = point; rangeDragAxis = axis; rangeDragSide = side;
        swingPlaying = false;
        UpdateSwingUI();
        dragMode = DragMode.AdjustRange;
        dragPointer = evt.pointerId;
        previewImage.Focus();
        previewImage.CapturePointer(dragPointer);
        evt.StopPropagation();
        return true;
    }

    private static RangeHandle HitRangeHandle(Vector2 point, Vector2 tip, Vector2 guard, Vector2 center, Vector2 width)
    {
        if (Vector2.Distance(point, tip) <= 12f) return RangeHandle.Tip;
        if (Vector2.Distance(point, guard) <= 12f) return RangeHandle.Guard;
        if (Vector2.Distance(point, center) <= 12f) return RangeHandle.Center;
        if (Vector2.Distance(point, width) <= 12f) return RangeHandle.Width;
        Vector2 line = guard - tip;
        float t = Mathf.Clamp01(Vector2.Dot(point - tip, line) / line.sqrMagnitude);
        return Vector2.Distance(point, tip + line * t) <= 7f ? RangeHandle.Center : RangeHandle.None;
    }

    private void UpdateRangeDrag(Vector2 point)
    {
        Vector2 delta = point - rangeDragStart;
        float shift = Vector2.Dot(delta, rangeDragAxis) / rangeDragAxis.sqrMagnitude;
        if (rangeHandle == RangeHandle.Width)
            SetRangeValue(WidthField(rangeDragTrail), Mathf.Clamp(rangeStartWidth + Vector2.Dot(delta, rangeDragSide) / 30f, .1f, 3f));
        else if (rangeHandle == RangeHandle.Center)
            SetRangeValue(CenterField(rangeDragTrail), Mathf.Clamp01(rangeStartCenter + shift));
        else
        {
            float length = Mathf.Clamp(rangeStartLength + (rangeHandle == RangeHandle.Tip ? -shift : shift), rangeDragTrail ? .1f : .5f, 3f);
            float center = rangeStartCenter + (length - rangeStartLength) * (rangeHandle == RangeHandle.Tip ? -.5f : .5f);
            SetRangeValue(LengthField(rangeDragTrail), length);
            SetRangeValue(CenterField(rangeDragTrail), Mathf.Clamp01(center));
        }
        dirty = true;
        UpdateStatus();
        QueueRender();
        rangeOverlay.MarkDirtyRepaint();
    }

    private void SetRangeValue(string field, float value)
    {
        string path = rangeDragKey + field;
        draft[path] = value;
        controls.Q<Slider>("tuning-" + path)?.SetValueWithoutNotify(value);
    }
}
