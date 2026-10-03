using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

public static class CombatFacingContourPointBuilder
{
    public const string Root = "Assets/ProjectOverburst/03_Features/Player/VFX/CombatFacing/QuietFill";
    public const string NewRoot = Root + "/ContourPoint02";
    public const string BeforePrefab = NewRoot + "/Prefabs/PF_ContourPoint_Baseline.prefab";
    public const string AfterPrefab = NewRoot + "/Prefabs/PF_ContourPoint_ClearCircle.prefab";
    public const string ProductPrefab = Root + "/Prefabs/PF_VFX_CombatFacing_QuietFill.prefab";
    const string PlayerPrefab = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const float ClearRadius = .838f;
    public const float TipExtension = .175f;
    public const float StrokeHalfWidth = .027f;
    static readonly string[] Nodes = { "RingToTip_-1", "RingToTip_1", "InnerFeather_-1", "InnerFeather_1", "FilledForwardCap", "QuietNoseCore" };
    static readonly string[] Originals = { "02_Thin45_RingToTip_-1", "02_Thin45_RingToTip_1", "02_Thin45_InnerFeather_-1", "02_Thin45_InnerFeather_1", "02_FullFilledTipSoft_FilledTip", "02_FullFilledTipSoft_NoseCore" };

    [MenuItem("OVERBURST/플레이어/전투 방향 표시/기존 곡선과 연장한 끝 적용")]
    static void ApplyMenu() => Install(DefaultOutput);
    [MenuItem("OVERBURST/플레이어/전투 방향 표시/기존 절제형으로 복원")]
    static void RestoreMenu() => RestoreOriginal(DefaultOutput);
    static string DefaultOutput => Path.GetFullPath("../개인파일/코덱스산출/VFX/CombatFacingContourPoint/EditorActions");

    static void RequireIdle()
    {
        const string guard = "Overburst.IsolatedSavePlayGuard.";
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))
            || !string.IsNullOrEmpty(SessionState.GetString(guard + "active", ""))
            || !string.IsNullOrEmpty(SessionState.GetString(guard + "prepared", ""))
            || SessionState.GetBool(guard + "blocked", false))
            throw new InvalidOperationException("실제 계정의 유휴 Editor에서만 방향 표시 자산을 변경합니다.");
    }

    public static void Install(string output)
    {
        RequireIdle();
        Directory.CreateDirectory(output);
        string backup = Path.Combine(output, "OriginalProduct.prefab");
        if (!File.Exists(backup))
        {
            File.Copy(ProductPrefab, backup);
            File.Copy(ProductPrefab + ".meta", backup + ".meta");
        }
        Build(output);
        SetProductMeshes(true);
        ValidateInherited(output, true);
    }

    public static void RestoreOriginal(string output)
    {
        RequireIdle();
        SetProductMeshes(false);
        ValidateInherited(output, false);
    }

    static void SetProductMeshes(bool modified)
    {
        RequireIdle();
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(ProductPrefab);
            string before = Configuration(root);
            for (int i = 0; i < Nodes.Length; i++)
            {
                Mesh mesh = modified ? AssetDatabase.LoadAssetAtPath<Mesh>(NewRoot + "/Meshes/Mesh_ContourPoint_" + Nodes[i] + ".asset") : Original(i);
                if (mesh == null) throw new InvalidOperationException("방향 표시 메시가 없습니다: " + Nodes[i]);
                root.transform.Find(Nodes[i]).GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            if (Configuration(root) != before) throw new InvalidOperationException("메시 이외의 방향 표시 설정이 변경됐습니다.");
            PrefabUtility.SaveAsPrefabAsset(root, ProductPrefab);
        }
        finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
    }

    static string Configuration(GameObject root) => JsonConvert.SerializeObject(root.GetComponentsInChildren<Renderer>(true).OrderBy(r => r.name).Select(r => new {
        node = r.name, active = r.gameObject.activeSelf,
        position = new[] { r.transform.localPosition.x, r.transform.localPosition.y, r.transform.localPosition.z },
        rotation = new[] { r.transform.localRotation.x, r.transform.localRotation.y, r.transform.localRotation.z, r.transform.localRotation.w },
        scale = new[] { r.transform.localScale.x, r.transform.localScale.y, r.transform.localScale.z },
        materials = r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()
    }));

    public static void ValidateInherited(string output, bool modified)
    {
        GameObject player = null;
        try
        {
            player = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            var component = player.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().Name == "PlayerCombatFacingVfx");
            var settings = new SerializedObject(component);
            var visual = settings.FindProperty("visualRoot").objectReferenceValue as Transform;
            float scale = settings.FindProperty("worldScale").floatValue;
            if (visual == null || Mathf.Abs(scale - .45f) > .0001f) throw new Exception("플레이어 방향 표시 연결/크기 불일치");
            var paths = Nodes.Select(n => AssetDatabase.GetAssetPath(visual.Find(n).GetComponent<MeshFilter>().sharedMesh)).ToArray();
            for (int i = 0; i < paths.Length; i++)
            {
                string expected = modified ? NewRoot + "/Meshes/Mesh_ContourPoint_" + Nodes[i] + ".asset" : Root + "/Meshes/" + Originals[i] + ".asset";
                if (paths[i] != expected) throw new Exception("중첩 프리팹 메시 상속 불일치: " + Nodes[i]);
            }
            int missing = visual.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (missing != 0 || renderers.Length != 9 || renderers.Any(r => r.sharedMaterials.Any(m => m == null || m.shader == null || !m.shader.isSupported))) throw new Exception("제품 방향 표시 렌더러/재질 불일치");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, modified ? "product_validation.json" : "rollback_validation.json"), JsonConvert.SerializeObject(new {
                status = "PASS", modified, prefab = ProductPrefab, guid = AssetDatabase.AssetPathToGUID(ProductPrefab),
                inheritedPlayerPrefab = PlayerPrefab, missingScripts = missing, renderers = renderers.Length, scale, meshes = paths
            }, Formatting.Indented));
        }
        finally { if (player != null) PrefabUtility.UnloadPrefabContents(player); }
    }

    public static void Build(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("유휴 Editor에서만 시안을 생성합니다.");
        Ensure(NewRoot + "/Meshes"); Ensure(NewRoot + "/Prefabs");
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(Root + "/Prefabs/PF_VFX_CombatFacing_QuietFill.prefab");
            for (int i = 0; i < Nodes.Length; i++) root.transform.Find(Nodes[i]).GetComponent<MeshFilter>().sharedMesh = Original(i);
            PrefabUtility.SaveAsPrefabAsset(root, BeforePrefab);
            for (int i = 0; i < Nodes.Length; i++)
            {
                Mesh mesh = i < 4 ? Wing(Original(i), i >= 2) : i == 4 ? OutsideFill(Original(0)) : Nose(Original(i));
                root.transform.Find(Nodes[i]).GetComponent<MeshFilter>().sharedMesh = Save(mesh, NewRoot + "/Meshes/Mesh_ContourPoint_" + Nodes[i] + ".asset");
            }
            PrefabUtility.SaveAsPrefabAsset(root, AfterPrefab);
        }
        finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
        Validate(output);
    }
    static Mesh Original(int i)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/" + Originals[i] + ".asset");
        if (mesh == null) throw new InvalidOperationException("이전 메시가 없습니다: " + Originals[i]);
        return mesh;
    }

    static Vector3 Point(Vector3[] original, int j, out Vector3 direction)
    {
        int steps = original.Length / 2 - 1, split = Mathf.RoundToInt(steps * .805f);
        if (j <= split)
        {
            direction = Center(original, Mathf.Min(steps, j + 1)) - Center(original, Mathf.Max(0, j - 1));
            return Center(original, j);
        }
        var start = Center(original, split);
        float side = Mathf.Sign(start.x), tip = Center(original, steps).z + TipExtension, radius = .034f;
        var d = new Vector2(Mathf.Abs(start.x), tip - start.z).normalized;
        float phi = Mathf.Atan2(d.x, d.y), centerZ = tip - radius / d.x;
        var tangent = new Vector3(side * Mathf.Cos(phi) * radius, 0, centerZ + Mathf.Sin(phi) * radius);
        float t = (j - split) / (float)(steps - split);
        if (t <= .82f) { direction = tangent - start; return Vector3.Lerp(start, tangent, t / .82f); }
        float a = Mathf.Lerp(phi, Mathf.PI * .5f, (t - .82f) / .18f);
        direction = new Vector3(-side * Mathf.Sin(a), 0, Mathf.Cos(a));
        return new Vector3(side * Mathf.Cos(a) * radius, 0, centerZ + Mathf.Sin(a) * radius);
    }
    static Vector3 Center(Vector3[] v, int j) => (v[j * 2] + v[j * 2 + 1]) * .5f;

    static Mesh Wing(Mesh original, bool feather)
    {
        var mesh = Object.Instantiate(original); mesh.name = "Preserved curve to extended straight tip";
        var source = original.vertices; var vertices = mesh.vertices; var colors = mesh.colors;
        int steps = vertices.Length / 2 - 1, split = Mathf.RoundToInt(steps * .805f);
        float alpha = colors[split * 2].a;
        for (int j = split + 1; j <= steps; j++)
        {
            Vector3 direction; var center = Point(source, j, out direction);
            var n = Vector3.Cross(Vector3.up, direction.normalized);
            float target = feather ? Mathf.Min(.030f, StrokeHalfWidth * 1.25f) : StrokeHalfWidth;
            float width = Mathf.Lerp(Vector3.Distance(source[split * 2], source[split * 2 + 1]) * .5f, target, Mathf.Min(1, (j - split) / 3f));
            vertices[j * 2] = center - n * width; vertices[j * 2 + 1] = center + n * width;
            float t = (j - split) / (float)(steps - split);
            float end = Mathf.Lerp(1, .5f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.94f, 1, t)));
            float visibility = Mathf.Lerp(1, .75f, Mathf.Min(1, (j - split) / 6f));
            colors[j * 2] = colors[j * 2 + 1] = new Color(1, 1, 1, alpha * end * visibility);
        }
        mesh.vertices = vertices; mesh.colors = colors; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }

    static Mesh OutsideFill(Mesh original)
    {
        var source = original.vertices; int steps = source.Length / 2 - 1, start = Mathf.CeilToInt(steps * .61f);
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            int offset = vertices.Count, count = 0;
            for (int j = start; j <= steps; j++)
            {
                Vector3 direction; var outer = Point(source, j, out direction); outer.x = (sideIndex == 0 ? -1 : 1) * Mathf.Abs(outer.x);
                float innerZ = Mathf.Sqrt(Mathf.Max(0, ClearRadius * ClearRadius - outer.x * outer.x));
                if (outer.z <= innerZ) continue;
                vertices.Add(new Vector3(outer.x, 0, innerZ)); vertices.Add(outer);
                float span = (j - start) / (float)(steps - start);
                uv.Add(new Vector2(0, span)); uv.Add(new Vector2(1, span));
                if (count > 0)
                {
                    int q = offset + (count - 1) * 2;
                    triangles.AddRange(new[] { q, q + 2, q + 1, q + 1, q + 2, q + 3 });
                }
                count++;
            }
        }
        var mesh = new Mesh { name = "Outside-circle filled pointer" };
        mesh.vertices = vertices.ToArray(); mesh.uv = uv.ToArray(); mesh.colors = Enumerable.Repeat(Color.white, vertices.Count).ToArray(); mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    static Mesh Nose(Mesh original)
    {
        var mesh = Object.Instantiate(original); mesh.name = "Subtle extended-tip glint";
        mesh.vertices = mesh.vertices.Select(v => v + Vector3.forward * TipExtension).ToArray(); mesh.RecalculateBounds(); return mesh;
    }
    static Mesh Save(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); return existing;
    }

    public static void Validate(string output)
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(AfterPrefab);
            int missing = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            int invalid = renderers.SelectMany(r => r.sharedMaterials).Count(m => m == null || m.shader == null || !m.shader.isSupported);
            var fill = root.transform.Find("FilledForwardCap").GetComponent<MeshFilter>().sharedMesh;
            var v = fill.vertices; var tri = fill.triangles; float minRadius = float.MaxValue;
            for (int i = 0; i < tri.Length; i += 3)
                for (int a = 0; a <= 8; a++) for (int b = 0; b <= 8 - a; b++)
                {
                    var p = (v[tri[i]] * a + v[tri[i + 1]] * b + v[tri[i + 2]] * (8 - a - b)) / 8f;
                    minRadius = Mathf.Min(minRadius, new Vector2(p.x, p.z).magnitude);
                }
            float maxWidthError = 0, straightError = 0;
            for (int i = 0; i < 4; i++)
            {
                var mesh = root.transform.Find(Nodes[i]).GetComponent<MeshFilter>().sharedMesh;
                var p = mesh.vertices; int steps = p.Length / 2 - 1, split = Mathf.RoundToInt(steps * .805f), end = split + Mathf.FloorToInt((steps - split) * .82f);
                float width = (i >= 2 ? Mathf.Min(.030f, StrokeHalfWidth * 1.25f) : StrokeHalfWidth) * 2;
                var a = Center(p, split); var b = Center(p, end);
                for (int j = split + 3; j <= steps; j++) maxWidthError = Mathf.Max(maxWidthError, Mathf.Abs(Vector3.Distance(p[j * 2], p[j * 2 + 1]) - width));
                for (int j = split + 1; j <= end; j++) straightError = Mathf.Max(straightError, Vector3.Cross(Center(p, j) - a, (b - a).normalized).magnitude);
                var source = Original(i).vertices;
                for (int j = 0; j <= split * 2 + 1; j++) if (p[j] != source[j]) throw new Exception("기존 곡선 영역이 변경됐습니다.");
            }
            if (missing != 0 || invalid != 0 || renderers.Length != 9 || minRadius < .836f || maxWidthError > .00001f || straightError > .00001f) throw new Exception("포인터 형상/원 내부 비움/일정 선 폭 검사 실패");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "shape_validation.json"), JsonConvert.SerializeObject(new { status = "PASS", prefab = AfterPrefab, guid = AssetDatabase.AssetPathToGUID(AfterPrefab), missingScripts = missing, invalidMaterials = invalid, renderers = renderers.Length, minFillRadius = minRadius, clearRingRadius = .832f, maxStrokeWidthError = maxWidthError, straightError, originalCurvePreserved = true, strokeHalfWidth = StrokeHalfWidth, tipExtensionWorld = TipExtension * .45f, roundedTip = true }, Formatting.Indented));
        }
        finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
    }
    static void Ensure(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int n = path.LastIndexOf('/'); Ensure(path.Substring(0, n)); AssetDatabase.CreateFolder(path.Substring(0, n), path.Substring(n + 1));
    }
}
