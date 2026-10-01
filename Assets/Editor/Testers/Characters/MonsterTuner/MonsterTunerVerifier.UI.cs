using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    private static void Call(object owner, string method) => owner.GetType().GetMethod(method, Private).Invoke(owner, null);
    public static string UIUX()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Editor is in Play mode";
        Checks.Clear(); Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "ui-progress.json"), "{\"phase\":\"queued\",\"version\":2}");
        int live = MonsterTunerPreviewStage.LiveStages;
        float listWidth = EditorPrefs.GetFloat("Overburst.MonsterTuner.ListWidth", 240);
        float detailWidth = EditorPrefs.GetFloat("Overburst.MonsterTuner.DetailWidth", 360);
        var window = ScriptableObject.CreateInstance<MonsterTunerWindow>(); window.VerificationOnly = true;
        window.titleContent = new GUIContent("몬스터 튜너 입력 검증"); window.position = new Rect(60, 60, 1440, 900); window.ShowUtility(); window.Focus();
        int frames = 0; EditorApplication.update += Run;
        return "UI verification queued; report: ui-ux-verification.json";
        void Run()
        {
            if (window == null) { EditorApplication.update -= Run; return; }
            if (++frames < 3 || window.rootVisualElement.panel == null) return;
            EditorApplication.update -= Run;
            File.WriteAllText(Path.Combine(Output, "ui-progress.json"), "{\"phase\":\"running\",\"version\":2}");
            try
            {
                window.position = new Rect(60, 60, 1440, 900);
                var nativeView = typeof(EditorWindow).GetField("m_Parent", Private).GetValue(window);
                nativeView.GetType().GetMethod("RepaintImmediately", Private).Invoke(nativeView, null);
                Call(window, "RenderNow");
                var root = window.rootVisualElement;
                var session = Get<MonsterTunerSession>(window, "session");
                var stage = Get<MonsterTunerPreviewStage>(window, "stage");
                var viewport = Get<MonsterTunerViewport>(window, "viewport");
                Check("도구 내부 IMGUIContainer 없음", root.Query<IMGUIContainer>().ToList().Count == 0);
                Check("Toolkit 3열 배치", root.Q(className: "mt-left").layout.width >= 219 && root.Q(className: "mt-right").layout.width >= 319 && viewport.layout.width > 250);
                Check("정면·측면·게임 쿼터뷰 버튼", new[] { "정면", "측면", "게임 쿼터뷰" }.All(t => root.Query<Button>().ToList().Any(b => b.text == t)));
                Check("기본 표시 몸 충돌 한 항목", viewport.Points.Count(viewport.Visible) == 1 && viewport.Points.First(viewport.Visible).Label.StartsWith("몸 충돌"));
                string draftBeforeVisibility = JsonUtility.ToJson(session);
                Set(window, "legendExpanded", true); Call(window, "RefreshLegend");
                var hidden = viewport.Points.First(p => !viewport.Visible(p));
                var overlayToggle = root.Q<Toggle>("overlay:" + hidden.Key); Check("항목별 표시 Toggle", overlayToggle != null);
                Change(overlayToggle, true); Check("항목별 표시 켜기", viewport.Visible(hidden));
                overlayToggle = root.Q<Toggle>("overlay:" + hidden.Key); Change(overlayToggle, false); Check("항목별 표시 끄기", !viewport.Visible(hidden));
                Check("표시 변경 저장 사본 불변", draftBeforeVisibility == JsonUtility.ToJson(session));
                Check("범례 색과 점 이름", overlayToggle.text.StartsWith("●") && root.Query<Toggle>().ToList().Any(t => t.text != null && t.text.Contains("몸 충돌")));
                Set(window, "legendExpanded", false); Call(window, "RefreshLegend");
                var field = root.Q<Vector3Field>(); var original = field.value;
                var next = original * 1.2f;
                Change(field, next);
                Undo.FlushUndoRecordObjects();
                Check("실제 ChangeEvent 사본 반영", session.Value("variant", "visualScale").vector == next);
                Check("숫자 입력 프리뷰 즉시 반영", Vector3.Distance(stage.Enemy.VisualRoot.localScale, next * (session.Definition.Grade?.ScaleMultiplier ?? 1f)) < .001f);
                Undo.PerformUndo(); Check("숫자 입력 Undo", session.Value("variant", "visualScale").vector == original);
                Undo.PerformRedo(); Check("숫자 입력 Redo", session.Value("variant", "visualScale").vector == next);
                session.Discard(); Call(window, "UndoRedo");
                var point = viewport.Points.First(p => p.Move != null && p.Key.EndsWith("|m_LocalPosition")); viewport.Select(point);
                stage.Playing = true;
                Vector3 before = point.World(); Vector2 center = viewport.Project(before);
                float length = Vector3.Distance(stage.Camera.transform.position, before) * .075f;
                var direction = Vector3.up; var axisEnd = viewport.Project(before + direction * length);
                Vector2 start = center + (axisEnd - center) * .65f;
                Pointer(viewport, EventType.MouseDown, start, 0);
                Pointer(viewport, EventType.MouseDrag, start + (axisEnd - center).normalized * 30, 0);
                Pointer(viewport, EventType.MouseUp, start + (axisEnd - center).normalized * 30, 0);
                Check("실제 포인터 축 이동", Vector3.Distance(before, point.World()) > .01f);
                Check("드래그 시작 포즈 정지", !stage.Playing);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check("드래그 1회 Undo", Vector3.Distance(before, viewport.Selected.World()) < .001f);
                session.Discard(); Call(window, "UndoRedo");
                for (int tab = 0; tab < 5; tab++) { Set(window, "tab", tab); Call(window, "BuildFields"); Check("탭 " + tab + " Toolkit 필드", Get<ScrollView>(window, "fields").childCount > 0); }
                Set(window, "tab", 0); Call(window, "BuildFields");
                foreach (float dpi in new[] { 1f, 1.5f, 2f })
                {
                    stage.Render(Mathf.RoundToInt(620 * dpi), Mathf.RoundToInt(510 * dpi));
                    Check("DPI " + dpi + " RT 화면비", Mathf.Abs(stage.Surface.width / (float)stage.Surface.height - 620f / 510f) < .005f);
                }
                // Refresh the Image after render-texture sizing checks changed its surface.
                viewport.Select(null, false);
                foreach (string key in viewport.Visibility.Keys.ToArray()) viewport.Visibility[key] = false;
                viewport.Visibility[viewport.Points.First(p => p.Label.StartsWith("몸 충돌")).Key] = true;
                Call(window, "RefreshLegend");
                Call(window, "RenderNow");
                // Render only this window's Toolkit panel into an owned offscreen surface.
                try { CapturePanel(window, Path.Combine(Output, "toolkit-window.png")); }
                catch (Exception e) { File.WriteAllText(Path.Combine(Output, "panel-capture-limitation.txt"), e.ToString()); }
                Set(window, "legendExpanded", true); Call(window, "RefreshLegend");
                CapturePanel(window, Path.Combine(Output, "toolkit-legend-window.png"));
                Set(window, "legendExpanded", false); Call(window, "RefreshLegend");
                window.position = new Rect(60, 60, 1100, 720);
                root.style.width = 1100; root.style.height = 720;
                try
                {
                    CapturePanel(window, Path.Combine(Output, "toolkit-minimum-window.png"));
                    Check("1100×720 상세 최소 폭", root.Q(className: "mt-left").layout.width >= 219 && root.Q(className: "mt-right").layout.width >= 319 && viewport.layout.height >= 200);
                    Check("XYZ 숫자 입력 폭", root.Query<Vector3Field>().ToList().SelectMany(v => v.Query<FloatField>().ToList()).All(f => f.Q(className: "unity-base-field__input").layout.width >= 36));
                }
                catch (Exception e) { File.WriteAllText(Path.Combine(Output, "minimum-capture-limitation.txt"), e.ToString()); }
                session.Discard(); Call(window, "UpdateHeader"); window.CloseVerification();
                Check("검증 창 해제", MonsterTunerPreviewStage.LiveStages == live);
                Save("ui-ux", true, "");
                File.WriteAllText(Path.Combine(Output, "ui-progress.json"), "{\"phase\":\"complete\",\"version\":3}");
            }
            catch (Exception e) { Save("ui-ux", false, e.ToString()); }
            finally
            {
                if (window != null) window.CloseVerification();
                EditorPrefs.SetFloat("Overburst.MonsterTuner.ListWidth", listWidth);
                EditorPrefs.SetFloat("Overburst.MonsterTuner.DetailWidth", detailWidth);
            }
        }
    }
    private static void Change<T>(BaseField<T> field, T next)
    {
        T old = field.value; field.SetValueWithoutNotify(next);
        using (var e = ChangeEvent<T>.GetPooled(old, next)) { e.target = field; field.SendEvent(e); }
    }
    private static void Pointer(VisualElement element, EventType type, Vector2 local, int button)
    {
        var evt = new Event { type = type, mousePosition = element.LocalToWorld(local), button = button };
        EventBase pointer = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(evt)
            : type == EventType.MouseUp ? PointerUpEvent.GetPooled(evt) : PointerMoveEvent.GetPooled(evt);
        using (pointer) { pointer.target = element; element.SendEvent(pointer); }
    }
    private static void CapturePanel(MonsterTunerWindow window, string path)
    {
        var view = typeof(EditorWindow).GetField("m_Parent", Private).GetValue(window); var viewType = view.GetType();
        int width = Mathf.RoundToInt(window.position.width), height = Mathf.RoundToInt(window.position.height);
        var surface = new RenderTexture(width, height, 24); surface.Create();
        var previous = RenderTexture.active;
        try
        {
            viewType.GetMethod("RepaintImmediately", Private).Invoke(view, null);
            Call(window, "RenderNow"); viewType.GetMethod("RepaintImmediately", Private).Invoke(view, null);
            var grab = viewType.GetMethod("GrabPixels", Private, null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
            if (grab == null) throw new NotSupportedException("Editor GUIView backbuffer capture unavailable");
            grab.Invoke(view, new object[] { surface, new Rect(0, 0, width, height) });
            // GUIView's top-origin pixels differ from Camera.Render captures.
            RenderTexture.active = surface;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                var pixels = texture.GetPixels32();
                for (int y = 0; y < height / 2; y++)
                    for (int x = 0; x < width; x++)
                    { int a = y * width + x, b = (height - 1 - y) * width + x; var pixel = pixels[a]; pixels[a] = pixels[b]; pixels[b] = pixel; }
                texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
        }
        finally { RenderTexture.active = previous; surface.Release(); Object.DestroyImmediate(surface); }
    }
}
