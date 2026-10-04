using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Overburst.EditorTools.ToolHub;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class OverburstToolHubVerifier // 실제 카드와 메뉴·아이콘·창 수명을 확인
{
    [Serializable] private sealed class Check { public string name, detail; public bool passed; }
    [Serializable] private sealed class Report
    {
        public string status, error, project, sceneBefore, sceneAfter;
        public int previewsBefore, previewsAfter;
        public List<Check> checks = new List<Check>();
    }

    private static OverburstToolHubWindow window;
    private static Report report;
    private static string output;
    private static double nextTick, deadline;
    private static int phase, launchIndex;
    private static Button[] buttons;
    private static HashSet<int> windowsBefore;
    private static HashSet<int> launchWindowsBefore;
    private static readonly HashSet<int> ownedWindows = new HashSet<int>();
    public static bool Running { get; private set; }

    [MenuItem("OVERBURST/테스트/도구 허브")]
    public static void VerifyFromMenu() => Verify(Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../개인파일/코덱스산출/Tools/ToolHub", DateTime.Now.ToString("yyyyMMdd_HHmmss"), "QA")));

    public static void Verify(string outputFolder)
    {
        if (Running) throw new InvalidOperationException("도구 허브 검증이 이미 진행 중입니다.");
        RequireIdle();
        output = Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(output);
        report = new Report { status = "RUNNING", project = Application.dataPath, sceneBefore = SceneSnapshot(),
            previewsBefore = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount };
        windowsBefore = CurrentWindows();
        ownedWindows.Clear();
        phase = launchIndex = 0;
        deadline = EditorApplication.timeSinceStartup + 180;
        Running = true;
        try
        {
            window = ScriptableObject.CreateInstance<OverburstToolHubWindow>();
            window.ShowUtility();
            window.position = new Rect(40, 60, 1295, 955);
            window.Focus();
            nextTick = EditorApplication.timeSinceStartup + .8;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            Save();
        }
        catch (Exception e) { Finish(e.ToString()); }
    }

    private static void Tick()
    {
        if (!Running || EditorApplication.timeSinceStartup < nextTick) return;
        try
        {
            RequireIdle();
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("도구 허브 검증 시간 초과");
            if (window == null) throw new InvalidOperationException("검증 창이 닫혔습니다.");
            var root = window.rootVisualElement;
            var scroll = root.Q<ScrollView>("tool-hub-scroll");
            if (phase == 0)
            {
                buttons = root.Query<Button>().ToList().Where(b => b.userData is string).ToArray();
                Add("실행 버튼 12개", buttons.Length == 12, buttons.Length.ToString());
                Add("핵심 카드 8개", root.Q("core-tool-grid").childCount == 8);
                Add("범용 카드 3개", root.Q("general-tool-grid").childCount == 3);
                Add("전용 아이콘 12개", root.Query<Image>().ToList().Count(i => i.image != null) == 12);
                foreach (var button in buttons)
                {
                    Add(button.name + " 메뉴", button.enabledSelf, (string)button.userData);
                    var image = button.name == "open-combo-maker" ? root.Q<Image>("icon-combo-maker") : button.Q<Image>();
                    var path = AssetDatabase.GetAssetPath(image.image);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    Add(button.name + " 아이콘", importer != null && importer.DoesSourceTextureHaveAlpha()
                        && importer.maxTextureSize == 256 && !importer.mipmapEnabled, path);
                }
                CheckLayout("wide");
                Capture(window, Path.Combine(output, "tool-hub-wide.png"));
                scroll.verticalScroller.value = scroll.verticalScroller.highValue;
                phase = 1;
            }
            else if (phase == 1)
            {
                Capture(window, Path.Combine(output, "tool-hub-general.png"));
                window.position = new Rect(40, 60, 1000, 660);
                scroll.verticalScroller.value = 0;
                phase = 2;
            }
            else if (phase == 2)
            {
                CheckLayout("minimum");
                Capture(window, Path.Combine(output, "tool-hub-minimum.png"));
                scroll.verticalScroller.value = scroll.verticalScroller.highValue;
                phase = 3;
            }
            else if (phase == 3)
            {
                Capture(window, Path.Combine(output, "tool-hub-minimum-general.png"));
                window.position = new Rect(40, 60, 1295, 955);
                phase = 4;
            }
            else if (phase == 4)
            {
                if (launchIndex >= buttons.Length) { Finish(null); return; }
                scroll.ScrollTo(buttons[launchIndex]);
                window.Focus();
                phase = 5;
            }
            else if (phase == 5)
            {
                var button = buttons[launchIndex];
                launchWindowsBefore = CurrentWindows();
                var point = button.worldBound.center;
                var picked = root.panel.Pick(point);
                Add(button.name + " 표시 좌표", picked != null && (picked == button || button.Contains(picked)), picked?.name ?? "none");
                if (picked == null || !(picked == button || button.Contains(picked)))
                    throw new InvalidOperationException(button.name + "의 표시 좌표가 다른 요소를 가리킵니다.");
                Pointer(picked, EventType.MouseDown, point);
                Pointer(picked, EventType.MouseUp, point);
                foreach (var id in CurrentWindows().Except(launchWindowsBefore)) ownedWindows.Add(id);
                phase = 6;
            }
            else
            {
                var button = buttons[launchIndex];
                var status = root.Q<Label>("tool-hub-status").text;
                Add(button.name + " 실제 포인터 실행", status.Contains("창을 열었습니다."), status);
                CloseCreatedWindows(launchWindowsBefore);
                Add(button.name + " 프리뷰 반환", UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount == report.previewsBefore);
                launchIndex++;
                phase = 4;
            }
            nextTick = EditorApplication.timeSinceStartup + .6;
            window.Repaint();
            Save();
        }
        catch (Exception e) { Finish(e.ToString()); }
    }

    private static void CheckLayout(string size)
    {
        var root = window.rootVisualElement;
        Add(size + " 가로 넘침 없음", root.Q<ScrollView>("tool-hub-scroll").horizontalScroller.highValue <= 1);
        foreach (var copy in root.Query<VisualElement>(className: "hub-tool-copy").ToList())
        {
            var labels = copy.Children().OfType<Label>().ToArray();
            bool fits = labels.All(l => l.worldBound.width > 0 && l.worldBound.xMin >= copy.worldBound.xMin - 1
                && l.worldBound.xMax <= copy.worldBound.xMax + 1 && l.worldBound.yMin >= copy.worldBound.yMin - 1
                && l.worldBound.yMax <= copy.worldBound.yMax + 1);
            bool ordered = labels.Zip(labels.Skip(1), (a, b) => a.worldBound.yMax <= b.worldBound.yMin + 1).All(v => v);
            Add(size + " " + copy.parent.name + " 글자 배치", fits && ordered);
        }
        var main = root.Q("combo-maker-card");
        Add(size + " 주 도구 버튼 배치", main.Contains(root.Q<Button>("open-combo-maker"))
            && main.worldBound.Contains(root.Q<Button>("open-combo-maker").worldBound.center));
    }

    private static void Pointer(VisualElement target, EventType type, Vector2 point)
    {
        var evt = new Event { type = type, mousePosition = point, button = 0 };
        EventBase pointer = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(evt) : PointerUpEvent.GetPooled(evt);
        using (pointer) { pointer.target = target; target.SendEvent(pointer); }
    }

    private static void Capture(EditorWindow target, string path)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var view = typeof(EditorWindow).GetField("m_Parent", flags).GetValue(target);
        var type = view.GetType();
        int width = Mathf.RoundToInt(target.position.width), height = Mathf.RoundToInt(target.position.height);
        var surface = new RenderTexture(width, height, 24);
        Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            surface.Create();
            type.GetMethod("RepaintImmediately", flags).Invoke(view, null);
            var grab = type.GetMethod("GrabPixels", flags, null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
            if (grab == null) throw new NotSupportedException("Editor 창 캡처를 사용할 수 없습니다.");
            grab.Invoke(view, new object[] { surface, new Rect(0, 0, width, height) });
            RenderTexture.active = surface;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            var colors = pixels.GetPixels32();
            for (int y = 0; y < height / 2; y++)
                for (int x = 0; x < width; x++)
                { int a = y * width + x, b = (height - 1 - y) * width + x; var color = colors[a]; colors[a] = colors[b]; colors[b] = color; }
            pixels.SetPixels32(colors);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels != null) Object.DestroyImmediate(pixels);
            surface.Release(); Object.DestroyImmediate(surface);
        }
    }

    private static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 EditMode에서만 도구 허브를 검증합니다.");
    }

    private static HashSet<int> CurrentWindows() => new HashSet<int>(Resources.FindObjectsOfTypeAll<EditorWindow>().Select(w => w.GetInstanceID()));
    private static void CloseCreatedWindows(HashSet<int> baseline)
    {
        if (baseline == null) return;
        foreach (var created in Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(w => w != window && ownedWindows.Contains(w.GetInstanceID()) && !baseline.Contains(w.GetInstanceID())).ToArray())
        {
            if (created.hasUnsavedChanges) throw new InvalidOperationException("미저장 검증 도구 창을 보존했습니다: " + created.titleContent.text);
            int id = created.GetInstanceID();
            created.Close();
            ownedWindows.Remove(id);
        }
    }

    private static string SceneSnapshot() => string.Join("|", Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    { var s = SceneManager.GetSceneAt(i); return s.path + ":" + s.isDirty + ":" + string.Join(",", s.GetRootGameObjects().Select(o => o.GetInstanceID())); }));
    private static void Add(string name, bool passed, string detail = "") => report.checks.Add(new Check { name = name, passed = passed, detail = detail });
    private static void BeforeReload() => Finish("검증 중 스크립트 재로딩으로 중단했습니다.");
    private static void Save() => File.WriteAllText(Path.Combine(output, "verification.json"), JsonUtility.ToJson(report, true));

    private static void Finish(string error)
    {
        EditorApplication.update -= Tick;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
        Running = false;
        try { CloseCreatedWindows(windowsBefore); }
        catch (Exception e) { error = (error ?? "") + "\n" + e; }
        if (window != null) { window.Close(); window = null; }
        report.sceneAfter = SceneSnapshot();
        report.previewsAfter = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
        Add("사용자 씬·미저장 상태 보존", report.sceneBefore == report.sceneAfter);
        Add("검증 프리뷰 전부 반환", report.previewsBefore == report.previewsAfter);
        report.error = error ?? "";
        report.status = string.IsNullOrEmpty(error) && report.checks.All(c => c.passed) ? "PASS" : "FAIL";
        buttons = null;
        windowsBefore = launchWindowsBefore = null;
        ownedWindows.Clear();
        Save();
    }
}
