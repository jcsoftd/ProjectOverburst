using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.Vfx
{
    /// <summary>미리보기 RenderTexture를 보여 주고 드래그 회전·휠 확대·더블클릭 맞춤을 받는 UI Toolkit 뷰포트.</summary>
    internal sealed class VfxBoardViewport : VisualElement
    {
        private static readonly Color AxisX = new Color(1f, .42f, .38f);
        private static readonly Color AxisY = new Color(.46f, .92f, .52f);
        private static readonly Color AxisZ = new Color(.45f, .68f, 1f);

        private readonly Image image;
        private readonly VisualElement axis;
        private readonly Label axisLabelX;
        private readonly Label axisLabelY;
        private readonly Label axisLabelZ;
        private readonly Func<Quaternion> cameraRotation;
        private int dragPointer = -1;
        private Vector2 lastPointer;

        public event Action<Vector2> Orbited;
        public event Action<float> Zoomed;
        public event Action FitRequested;
        public event Action<int, int> SurfaceResized;

        public VfxBoardViewport(Func<Quaternion> rotation)
        {
            cameraRotation = rotation;
            AddToClassList("vb-viewport");
            focusable = true;

            image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            image.AddToClassList("vb-viewport__image");
            Add(image);

            axis = new VisualElement { pickingMode = PickingMode.Ignore };
            axis.AddToClassList("vb-axis");
            axis.generateVisualContent += DrawAxis;
            axisLabelX = CreateAxisLabel("X", AxisX);
            axisLabelY = CreateAxisLabel("Y", AxisY);
            axisLabelZ = CreateAxisLabel("Z", AxisZ);
            Add(axis);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => dragPointer = -1);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        public void SetTexture(Texture texture)
        {
            if (image.image != texture)
                image.image = texture;
        }

        public void Refresh()
        {
            image.MarkDirtyRepaint();
            axis.MarkDirtyRepaint();
            PlaceAxisLabels();
        }

        private Label CreateAxisLabel(string text, Color color)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("vb-axis__label");
            label.style.color = color;
            axis.Add(label);
            return label;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0 && evt.clickCount == 2)
            {
                FitRequested?.Invoke();
                evt.StopPropagation();
                return;
            }

            if (evt.button != 0 && evt.button != 1)
                return;

            dragPointer = evt.pointerId;
            lastPointer = evt.position;
            this.CapturePointer(evt.pointerId);
            Focus();
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != dragPointer || !this.HasPointerCapture(evt.pointerId))
                return;

            Vector2 position = evt.position;
            Vector2 delta = position - lastPointer;
            lastPointer = position;
            if (delta.sqrMagnitude > 0f)
                Orbited?.Invoke(delta);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != dragPointer)
                return;

            this.ReleasePointer(evt.pointerId);
            dragPointer = -1;
            evt.StopPropagation();
        }

        private void OnWheel(WheelEvent evt)
        {
            Zoomed?.Invoke(evt.delta.y);
            evt.StopPropagation();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float scale = EditorGUIUtility.pixelsPerPoint;
            SurfaceResized?.Invoke(Mathf.RoundToInt(evt.newRect.width * scale), Mathf.RoundToInt(evt.newRect.height * scale));
        }

        private void DrawAxis(MeshGenerationContext context)
        {
            Rect rect = axis.contentRect;
            if (rect.width < 8f || rect.height < 8f)
                return;

            Vector2 center = rect.center + new Vector2(0f, 4f);
            float length = Mathf.Min(rect.width, rect.height) * .34f;
            Quaternion inverse = Quaternion.Inverse(cameraRotation());
            Painter2D painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            DrawAxisLine(painter, center, AxisEnd(center, inverse * Vector3.forward, length), AxisZ);
            DrawAxisLine(painter, center, AxisEnd(center, inverse * Vector3.right, length), AxisX);
            DrawAxisLine(painter, center, AxisEnd(center, inverse * Vector3.up, length), AxisY);
            painter.fillColor = new Color(.93f, .95f, .98f);
            painter.BeginPath();
            painter.Arc(center, 2.5f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        private static void DrawAxisLine(Painter2D painter, Vector2 from, Vector2 to, Color color)
        {
            painter.strokeColor = new Color(0f, 0f, 0f, .45f);
            painter.lineWidth = 5f;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
            painter.strokeColor = color;
            painter.lineWidth = 2.5f;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        private static Vector2 AxisEnd(Vector2 center, Vector3 local, float length)
        {
            var direction = new Vector2(local.x, -local.y);
            float magnitude = direction.magnitude;
            if (magnitude < .05f)
                direction = (local.z >= 0f ? 1f : -1f) * new Vector2(.35f, -.35f);
            else
                direction /= magnitude;
            return center + direction * Mathf.Lerp(length * .45f, length, Mathf.Clamp01(magnitude));
        }

        private void PlaceAxisLabels()
        {
            Rect rect = axis.contentRect;
            if (rect.width < 8f)
                return;

            Vector2 center = rect.center + new Vector2(0f, 4f);
            float length = Mathf.Min(rect.width, rect.height) * .34f + 8f;
            Quaternion inverse = Quaternion.Inverse(cameraRotation());
            Place(axisLabelX, AxisEnd(center, inverse * Vector3.right, length));
            Place(axisLabelY, AxisEnd(center, inverse * Vector3.up, length));
            Place(axisLabelZ, AxisEnd(center, inverse * Vector3.forward, length));
        }

        private static void Place(Label label, Vector2 position)
        {
            label.style.left = position.x - 6f;
            label.style.top = position.y - 8f;
        }
    }
}
