using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MapMinimapResolutionVerifier
{
    const string Key = "Overburst.MapResolutionVerifier.";
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type ViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
    static readonly Type SizeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSize");
    static object Get(object owner, string name) => owner.GetType().GetProperty(name, Flags).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name, Flags).SetValue(owner, value);
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Flags).Invoke(owner, args);
    static object Group(EditorWindow view)
    {
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
        return Call(type.BaseType.GetProperty("instance", Flags).GetValue(null), "GetGroup", Get(view, "currentSizeGroupType"));
    }
    static MapMinimapResolutionVerifier()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) Restore(); };
        // An interrupted owned test does not retain a temporary Game-view choice after reload.
        if (SessionState.GetString(Key + "owner", "") != "") EditorApplication.delayCall += Restore;
    }
    public static void Restore()
    {
        string owner = SessionState.GetString(Key + "owner", "");
        if (owner == "") return;
        var view = EditorUtility.InstanceIDToObject(SessionState.GetInt(Key + "view", 0)) as EditorWindow;
        if (!view) throw new InvalidOperationException("The owned Game view must be available to restore its size.");
        var group = Group(view);
        Call(view, "SizeSelectionCallback", SessionState.GetInt(Key + "original", 0), null);
        for (int i = (int)Call(group, "GetTotalCount") - 1; i >= 0; i--)
        {
            var size = Call(group, "GetGameViewSize", i);
            if (((string)Get(size, "baseText")).StartsWith(owner, StringComparison.Ordinal)) Call(group, "RemoveCustomSize", i);
        }
        view.Repaint();
        SessionState.EraseString(Key + "owner"); SessionState.EraseInt(Key + "view"); SessionState.EraseInt(Key + "original");
    }
    static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
    static Rect ScreenRect(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var points = corners.Select(p => RectTransformUtility.WorldToScreenPoint(camera, p)).ToArray();
        return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }
    static bool Inside(Rect a, Rect b) => a.xMin >= b.xMin - 1 && a.yMin >= b.yMin - 1 && a.xMax <= b.xMax + 1 && a.yMax <= b.yMax + 1;
    public static IEnumerator Verify(RunWorldGate gate, string output, Action<bool, string> check)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) throw new InvalidOperationException("Owned isolated Play required.");
        yield return Frames(12);
        var controller = WorldMinimapController.Instance; var map = controller.Exploration;
        check(map != null, "Resolution checks use the actual generated map");
        var view = (EditorWindow)Resources.FindObjectsOfTypeAll(ViewType).First();
        var group = Group(view); int count = (int)Call(group, "GetTotalCount"), original = (int)Get(view, "selectedSizeIndex");
        if (SessionState.GetString(Key + "owner", "") != "") throw new InvalidOperationException("An earlier size probe must return first.");
        string owner = "__OBMap_" + Guid.NewGuid().ToString("N").Substring(0, 16);
        SessionState.SetString(Key + "owner", owner); SessionState.SetInt(Key + "view", view.GetInstanceID()); SessionState.SetInt(Key + "original", original);
        float scale = OverburstGameSettings.MapOverlayScale, opacity = OverburstGameSettings.MapOverlayOpacity;
        float yaw = QuarterViewCamera.ActiveInstance.CurrentYaw; int builds = controller.TerrainBuildCount;
        var rows = new List<object>(); output = Path.Combine(output, "Resolution-" + gate.Map.terrain); Directory.CreateDirectory(output);
        try
        {
            // Fill this owned display fixture through the same topology/reveal function; this is not a movement simulation.
            foreach (var area in gate.Roster.Areas) map.Reveal(area.Ground.Points[0], map.CellSize * 256);
            var visited = Enumerable.Range(0, map.Width * map.Height).Where(map.IsRevealedCell).ToArray();
            var kind = Enum.Parse(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeType"), "FixedResolution");
            foreach (var resolution in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(2560, 1080) })
            {
                var size = Activator.CreateInstance(SizeType, kind, resolution.x, resolution.y, owner + "_" + resolution.x);
                Call(group, "AddCustomSize", size);
                // IndexOf uses a custom-only index; the Game view expects the combined list.
                Call(view, "SizeSelectionCallback", (int)Call(group, "GetTotalCount") - 1, null); view.Repaint();
                double until = EditorApplication.timeSinceStartup + 12;
                while ((Screen.width != resolution.x || Screen.height != resolution.y) && EditorApplication.timeSinceStartup < until) yield return null;
                check(Screen.width == resolution.x && Screen.height == resolution.y, "Actual Game resolution " + resolution);
                controller.SetOverlay(false); yield return Frames(8); Canvas.ForceUpdateCanvases();
                var circle = ScreenRect((RectTransform)controller.View.ViewRoot.transform);
                check(Inside(circle, Screen.safeArea), "Circular minimap and controls fit the safe area " + resolution);
                ScreenCapture.CaptureScreenshot(Path.Combine(output, resolution.x + "x" + resolution.y + "-circle.png")); yield return Frames(4);
                foreach (float zoom in new[] { 1f, .5f })
                {
                    OverburstGameSettings.MapOverlayScale = zoom; OverburstGameSettings.MapOverlayOpacity = .35f;
                    controller.SetOverlay(true); QuarterViewCamera.ActiveInstance.SetYaw(zoom == 1 ? yaw : 45); yield return Frames(8); Canvas.ForceUpdateCanvases();
                    var overlay = ScreenRect(controller.Overlay.viewport);
                    check(controller.IsVisible && controller.OverlayVisible && Inside(overlay, Screen.safeArea), "Both maps fit at " + resolution + " scale " + zoom);
                    check(Mathf.Abs(overlay.width / Screen.safeArea.width - .72f) < .01f && Mathf.Abs(overlay.height / Screen.safeArea.height - .68f) < .01f,
                        "Overlay leaves the authored screen margins " + resolution + " scale " + zoom);
                    check(Mathf.Approximately(controller.Overlay.outline.color.a, .35f) && controller.Overlay.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g => !g.raycastTarget),
                        "Overlay stays translucent and passes clicks " + resolution + " scale " + zoom);
                    check(ReferenceEquals(map, controller.Exploration) && visited.All(map.IsRevealedCell) && builds == controller.TerrainBuildCount, "Resolution changes keep the explored topology " + resolution + " scale " + zoom);
                    var path = Path.Combine(output, resolution.x + "x" + resolution.y + "-overlay-" + (zoom == 1 ? "default" : "wide") + ".png");
                    ScreenCapture.CaptureScreenshot(path); yield return Frames(4);
                    rows.Add(new { resolution = new[] { Screen.width, Screen.height }, scale = zoom, circle = new[] { circle.x, circle.y, circle.width, circle.height }, overlay = new[] { overlay.x, overlay.y, overlay.width, overlay.height }, map.RevealedCount });
                }
            }
        }
        finally
        {
            controller.SetOverlay(false); QuarterViewCamera.ActiveInstance.SetYaw(yaw);
            OverburstGameSettings.MapOverlayScale = scale; OverburstGameSettings.MapOverlayOpacity = opacity;
            Restore();
        }
        yield return Frames(8);
        check((int)Get(view, "selectedSizeIndex") == original && (int)Call(group, "GetTotalCount") == count, "Original Game size and list restored");
        File.WriteAllText(Path.Combine(output, "resolution-result.json"), JsonConvert.SerializeObject(new { status = "PASS", rows, originalSize = original, scope = "Editor Game-view resolution and current authored UI; visited display fixture, not Player/GPU or manual movement" }, Formatting.Indented));
    }
}
