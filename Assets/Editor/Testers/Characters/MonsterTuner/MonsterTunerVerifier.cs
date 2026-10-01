using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    private static readonly List<object> Checks = new List<object>();
    private static string Output => Path.Combine(MonsterTunerSession.OutputRoot, "20261001_Implementation", "QA");
    private static void Check(string name, bool pass, string detail = "")
    {
        Checks.Add(new { name, pass, detail });
        if (!pass) throw new InvalidOperationException(name + ": " + detail);
    }
    public static string Foundation()
    {
        Checks.Clear(); Directory.CreateDirectory(Output);
        var beforeScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => (s.path, s.isDirty, s.handle)).ToArray();
        var catalog = new MonsterTunerCatalog(); catalog.Refresh();
        var paths = catalog.Entries.SelectMany(e => new[] { AssetDatabase.GetAssetPath(e.Definition), AssetDatabase.GetAssetPath(e.Definition.ActorPrefab), AssetDatabase.GetAssetPath(e.Definition.Variant), AssetDatabase.GetAssetPath(e.Definition.AnimationProfile) }).Distinct().Where(p => !string.IsNullOrEmpty(p)).ToArray();
        var hashes = paths.ToDictionary(p => p, MonsterTunerStamp.FileHash);
        int liveStages = MonsterTunerPreviewStage.LiveStages;
        try
        {
            Check("목록 전체 게임 정의 포함", catalog.Entries.Count == AssetDatabase.FindAssets("t:EnemyDefinition").Count(g => !AssetDatabase.GUIDToAssetPath(g).StartsWith("Assets/Editor/", StringComparison.Ordinal)) && catalog.Entries.Count >= 30, catalog.Entries.Count.ToString());
            Check("목록 GUID 중복 없음", catalog.Entries.Select(e => e.Guid).Distinct().Count() == catalog.Entries.Count);
            int index = 0;
            foreach (var entry in catalog.Entries)
            {
                var session = MonsterTunerSession.Create(entry.Definition, false); var stage = new MonsterTunerPreviewStage();
                try
                {
                    stage.Load(session); Check(entry.Label + " 격리 모델 로드", stage.Actor != null && stage.Camera != null);
                    Check(entry.Label + " 게임 스크립트 비활성", stage.Actor.GetComponentsInChildren<MonoBehaviour>(true).Where(s => s != null).All(s => !s.enabled));
                    Check(entry.Label + " 실제 모션 슬롯", MonsterTunerAnimationBindings.Read(entry.Definition.AnimationProfile).Count > 0);
                    stage.SetView(0); stage.Render(512, 384);
                    Check(entry.Label + " 프리뷰 렌더", stage.Surface != null && stage.Surface.IsCreated());
                    if (index < 3)
                    {
                        for (int view = 0; view < 3; view++) { stage.SetView(view); stage.Render(640, 480); Capture(stage.Surface, Path.Combine(Output, "model-" + index + "-view-" + view + ".png")); }
                    }
                    stage.SetView(2); var originalRotation = stage.Camera.transform.rotation;
                    stage.Orbit(new Vector2(20f, 10f)); Check(entry.Label + " 자유 회전", Quaternion.Angle(originalRotation, stage.Camera.transform.rotation) > 1f);
                    float zoom = stage.ZoomPercent; stage.Zoom(-2f); Check(entry.Label + " 확대", stage.ZoomPercent > zoom);
                    stage.Zoom(4f); Check(entry.Label + " 축소", stage.ZoomPercent < zoom);
                    stage.Sample(stage.Duration * .5f); Check(entry.Label + " 모션 스크럽", Mathf.Abs(stage.Time - stage.Duration * .5f) < .001f);
                }
                finally { stage.Dispose(); Object.DestroyImmediate(session); }
                Check(entry.Label + " 프리뷰 자원 반환", MonsterTunerPreviewStage.LiveStages == liveStages);
                index++;
            }
            Check("원본 자산 쓰기 없음", hashes.All(pair => pair.Value == MonsterTunerStamp.FileHash(pair.Key)));
            var afterScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => (s.path, s.isDirty, s.handle)).ToArray();
            Check("제품 씬·dirty 상태 보존", beforeScenes.SequenceEqual(afterScenes));
            MonsterTunerWindow.Open();
            Check("UI Toolkit 창 열기", Resources.FindObjectsOfTypeAll<MonsterTunerWindow>().Any());
            return Save("foundation", true, "");
        }
        catch (Exception e) { return Save("foundation", false, e.ToString()); }
    }
    private static string Save(string name, bool success, string error)
    {
        string path = Path.Combine(Output, name + "-verification.json");
        File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new { success, error, count = Checks.Count, checks = Checks }, Newtonsoft.Json.Formatting.Indented));
        return name + ": " + (success ? "PASS" : "FAIL") + " " + Checks.Count + " checks; " + error;
    }
    private static void Capture(RenderTexture source, string path)
    {
        var previous = RenderTexture.active;
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        try { RenderTexture.active = source; texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }
}
