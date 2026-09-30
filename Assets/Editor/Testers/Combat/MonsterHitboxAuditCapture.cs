using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// 몬스터별 판정 부피(피격 판정·몸 부피·이펙트 부피·물리 충돌체)를 실제 메쉬 위에 그려 캡처하고,
// 화상·잠식·감전·빙결 오라가 붙는 위치를 실제 Play에서 확인한다. 격리 계정(OVERBURST_SAVE_DIRECTORY)으로
// 들어가 스스로 나오며 자산은 저장하지 않는다. 수치는 대기 자세 메쉬 정점 기준이다.
[InitializeOnLoad]
public static class MonsterHitboxAuditCapture
{
    const string Key = "MonsterHitboxAudit";
    const int Px = 512;
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<string> notes = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly List<object> monsters = new List<object>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static MonsterHitboxAuditCapture() { EditorApplication.playModeStateChanged += State; }

    // ids: 비우면 전부. 쉼표로 구분한 EnemyId 일부(부분 일치)만 넣으면 그 몬스터만.
    public static void Run(string output, string ids = "")
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".ids", ids ?? "");
        SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".env", Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY") ?? "");
        Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            Application.runInBackground = true;
            notes.Clear(); errors.Clear(); monsters.Clear(); stack.Clear(); guards.Clear();
            frame = -1; deadline = EditorApplication.timeSinceStartup + 1500;
            work = Audit();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", SessionState.GetString(Key + ".env", ""));
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string m, string s, LogType t)
    {
        if ((t == LogType.Error || t == LogType.Exception || t == LogType.Assert) && errors.Count < 200) errors.Add(m);
    }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish("COMPLETE"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }

    // 몬스터 하나가 실패해도 나머지를 계속 찍도록, Guard 경계에서 예외를 기록하고 그 몬스터만 건너뛴다.
    sealed class Guard
    {
        public readonly IEnumerator Body; public readonly string Label; public readonly Action Cleanup;
        public Guard(IEnumerator body, string label, Action cleanup) { Body = body; Label = label; Cleanup = cleanup; }
    }
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly Stack<KeyValuePair<int, Guard>> guards = new Stack<KeyValuePair<int, Guard>>();

    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            bool moved;
            try { moved = top.MoveNext(); }
            catch (Exception ex)
            {
                if (guards.Count == 0) throw;
                var g = guards.Pop();
                while (stack.Count > g.Key) (stack.Pop() as IDisposable)?.Dispose();
                errors.Add("[" + g.Value.Label + "] " + ex);
                try { g.Value.Cleanup?.Invoke(); } catch (Exception c) { errors.Add("[" + g.Value.Label + "] cleanup " + c.Message); }
                Flush("RUNNING");
                continue;
            }
            if (moved)
            {
                if (top.Current is Guard guard) { guards.Push(new KeyValuePair<int, Guard>(stack.Count, guard)); stack.Push(guard.Body); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                return true;
            }
            stack.Pop();
            if (guards.Count > 0 && guards.Peek().Key == stack.Count) guards.Pop();
        }
        return false;
    }

    static void Flush(string status)
    {
        File.WriteAllText(Path.Combine(Output, "audit.json"), JsonConvert.SerializeObject(new { status, notes, errors, monsters }, Formatting.Indented));
    }

    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        try { Flush(status); } catch (Exception e) { Debug.LogError("[HitboxAudit] write failed " + e.Message); }
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    // ---- 실행 상태 ----
    static EnemySpawnService spawn;
    static EnemyActor current;
    static Vector3 spot;
    static Quaternion facing;
    static Transform playerRoot;
    static GameObject fixture;
    static Camera cam;
    static RenderTexture rt;
    static Texture2D tex;
    static int layer;
    static readonly List<Material> materials = new List<Material>();

    static void ReleaseCurrent()
    {
        if (current != null && current.IsLeased && spawn != null) spawn.Release(current);
        current = null;
        if (fixture != null) Object.Destroy(fixture);
        fixture = null;
    }

    static IEnumerator Audit()
    {
        EnemyThemeTrialHarness ui = null; GameObject rig = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current; playerRoot = player.transform;
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(.5f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(x => x.definition).Where(d => d != null)
                .GroupBy(d => d.EnemyId).Select(g => g.First()).OrderBy(d => d.EnemyId, StringComparer.Ordinal).ToList();
            var bossCatalog = Resources.Load<EnemyCatalog>("Enemies/Bosses/CavernUrsacetusKing/EC_Boss_UrsKing");
            var boss = Resources.Load<EnemyDefinition>("Enemies/Bosses/CavernUrsacetusKing/ED_Boss_UrsKing");
            if (bossCatalog != null && boss != null)
            {
                if (!spawn.RegisterAdditionalCatalog(bossCatalog, out string message)) notes.Add("boss catalog: " + message);
                if (!defs.Any(d => d.EnemyId == boss.EnemyId)) defs.Add(boss); // 부작용이 있어도 다른 몬스터에 영향이 없도록 맨 끝
            }
            else notes.Add("boss definition not found");
            string filter = SessionState.GetString(Key + ".ids", "");
            if (filter.Length > 0)
            {
                var keys = filter.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                defs = defs.Where(d => keys.Any(k => d.EnemyId.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            }
            notes.Add("definitions " + defs.Count + ": " + string.Join(", ", defs.Select(d => d.EnemyId)));
            var energy = player.GetComponent<OverburstElementEnergy>();
            if (energy == null) energy = player.gameObject.AddComponent<OverburstElementEnergy>();
            yield return Wait(1f);

            // 오라 표시 예산이 게임 카메라 시야를 보므로 몬스터는 게임 화면 안(플레이어 오른쪽 위)에 세운다.
            var main = Camera.main;
            var ground = new Plane(Vector3.up, player.transform.position);
            var ray = main.ViewportPointToRay(new Vector3(.7f, .45f, 0f));
            spot = ground.Raycast(ray, out float hit) ? ray.GetPoint(hit) : player.transform.position + Vector3.right * 4f;
            Vector3 face = main.transform.position - spot; face.y = 0f;
            facing = Quaternion.LookRotation(face.sqrMagnitude > .01f ? face.normalized : Vector3.back);

            layer = FreeLayer();
            rig = new GameObject("Hitbox audit camera");
            cam = rig.AddComponent<Camera>(); cam.enabled = false; cam.orthographic = true;
            cam.cullingMask = 1 << layer; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.12f, .14f, .17f);
            rt = new RenderTexture(Px, Px, 24, RenderTextureFormat.ARGB32); rt.Create();
            cam.targetTexture = rt;
            tex = new Texture2D(Px, Px, TextureFormat.RGB24, false);
            notes.Add("render layer " + layer + ", spot " + spot.ToString("F2"));

            for (int i = 0; i < defs.Count; i++)
                yield return new Guard(Monster(i, defs[i], main, player.gameObject, energy), defs[i].EnemyId, ReleaseCurrent);
        }
        finally
        {
            ReleaseCurrent();
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
            if (tex != null) Object.Destroy(tex);
            foreach (var m in materials) if (m != null) Object.Destroy(m);
            materials.Clear();
            if (rig != null) Object.Destroy(rig);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    static int FreeLayer()
    {
        for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
        return 31;
    }

    static IEnumerator Spawn(EnemyDefinition def)
    {
        ReleaseCurrent();
        yield return null;
        if (!spawn.TrySpawn(new EnemySpawnRequest(def, spot, facing, playerRoot), out var e)) throw new Exception("Spawn " + def.EnemyId);
        current = e;
        e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
        if (e.Animator != null) e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    static void Apply(EnemyActor e, WeaponElement element, int times, GameObject source, string weaponId)
    {
        var s = e.GetComponent<ElementalStatusController>();
        for (int k = 0; k < times; k++)
            s.TryApplyDirectHit(new ElementalStatusApplication(element, 10, source, weaponId, true, false, e.transform.position, Vector3.forward));
    }

    // ---- 좌표 ----
    // 몬스터 기준 좌표(m): x 오른쪽, y 발 기준 높이, z 앞쪽.
    struct Basis
    {
        public Vector3 O, R, F;
        public Basis(Transform t)
        {
            O = t.position; F = t.forward; F.y = 0f; F = F.sqrMagnitude > 1e-4f ? F.normalized : Vector3.forward;
            R = Vector3.Cross(Vector3.up, F).normalized;
        }
        public Vector3 L(Vector3 w) { Vector3 d = w - O; return new Vector3(Vector3.Dot(d, R), d.y, Vector3.Dot(d, F)); }
    }

    static double[] V(Vector3 v) => new[] { Math.Round(v.x, 3), Math.Round(v.y, 3), Math.Round(v.z, 3) };
    static double R3(float f) => Math.Round(f, 3);

    static float Percentile(List<float> sorted, float p)
    {
        if (sorted.Count == 0) return 0f;
        float i = Mathf.Clamp01(p) * (sorted.Count - 1); int a = Mathf.FloorToInt(i), b = Mathf.Min(sorted.Count - 1, a + 1);
        return Mathf.Lerp(sorted[a], sorted[b], i - a);
    }

    static float AabbSize(Vector3[] w)
    {
        if (w.Length == 0) return 0f;
        Vector3 mn = w[0], mx = w[0];
        foreach (var p in w) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        return (mx - mn).magnitude;
    }

    // 대기 자세 메쉬 정점(월드). 스키닝 메쉬만 몸으로 본다(손에 든 무기 같은 MeshRenderer는 제외).
    static List<Vector3> BodyVertices(EnemyActor e, List<string> log)
    {
        var result = new List<Vector3>(); var baked = new Mesh();
        try
        {
            foreach (var smr in e.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
                // BakeMesh(false)는 렌더러 Transform의 크기를 뺀 공간으로 굽는다 → 위치·회전만 곱해야 월드 좌표가 된다.
                // (시험 실행: 크기 0.46 렌더러에서 다른 조합은 0.46배·1/0.46배로 틀어짐. 렌더러 경계 비교는 부위별로 들쭉날쭉해 기준으로 못 씀)
                var t = smr.transform; var bounds = smr.bounds;
                smr.BakeMesh(baked, false);
                var unit = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                var best = baked.vertices.Select(v => unit.MultiplyPoint3x4(v)).ToArray();
                var loose = bounds; loose.Expand(bounds.size * .06f + Vector3.one * .03f);
                float bestIn = best.Length > 0 ? best.Count(v => loose.Contains(v)) / (float)best.Length : 0f;
                result.AddRange(best);
                log.Add(smr.name + " verts " + best.Length + " size " + AabbSize(best).ToString("F2") + " bounds " + bounds.size.magnitude.ToString("F2")
                    + " inBounds " + bestIn.ToString("F2") + " lossy " + t.lossyScale.x.ToString("F2"));
            }
            int props = e.VisualRoot.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled && r.gameObject.activeInHierarchy);
            if (props > 0) log.Add("mesh renderers excluded " + props);
        }
        finally { Object.Destroy(baked); }
        return result;
    }

    static object MeshStats(List<Vector3> verts, Basis b, out Vector3 trimmedCenterWorld, out float top, out float bottom)
    {
        var xs = new List<float>(verts.Count); var ys = new List<float>(verts.Count); var zs = new List<float>(verts.Count);
        foreach (var w in verts) { var l = b.L(w); xs.Add(l.x); ys.Add(l.y); zs.Add(l.z); }
        xs.Sort(); ys.Sort(); zs.Sort();
        Vector3 lo = new Vector3(Percentile(xs, .03f), Percentile(ys, .01f), Percentile(zs, .03f));
        Vector3 hi = new Vector3(Percentile(xs, .97f), Percentile(ys, .99f), Percentile(zs, .97f));
        Vector3 c = (lo + hi) * .5f;
        trimmedCenterWorld = b.O + b.R * c.x + Vector3.up * c.y + b.F * c.z;
        top = hi.y; bottom = lo.y;
        return new
        {
            vertices = verts.Count,
            fullMin = V(new Vector3(xs.FirstOrDefault(), ys.FirstOrDefault(), zs.FirstOrDefault())),
            fullMax = V(new Vector3(xs.LastOrDefault(), ys.LastOrDefault(), zs.LastOrDefault())),
            trimmedMin = V(lo), trimmedMax = V(hi), trimmedCenter = V(c),
            halfWidth = R3((hi.x - lo.x) * .5f), halfDepth = R3((hi.z - lo.z) * .5f), height = R3(hi.y - lo.y),
        };
    }

    static object VolumeStats(CombatTargetVolume v, List<Vector3> verts, Basis b, Vector3 meshCenter)
    {
        int inside = 0; var planar = new List<float>(verts.Count);
        foreach (var w in verts)
        {
            float dx = w.x - v.Center.x, dz = w.z - v.Center.z; float d = Mathf.Sqrt(dx * dx + dz * dz);
            planar.Add(d);
            if (d <= v.Radius && Mathf.Abs(w.y - v.Center.y) <= v.HalfHeight) inside++;
        }
        planar.Sort();
        Vector3 off = b.L(new Vector3(v.Center.x, b.O.y, v.Center.z)) - b.L(new Vector3(meshCenter.x, b.O.y, meshCenter.z));
        return new
        {
            center = V(b.L(v.Center)), radius = R3(v.Radius), height = R3(v.HalfHeight * 2f),
            bottom = R3(v.Center.y - v.HalfHeight - b.O.y), top = R3(v.Center.y + v.HalfHeight - b.O.y),
            coverage = verts.Count > 0 ? Math.Round(inside / (double)verts.Count, 3) : 0,
            bodyRadius90 = R3(Percentile(planar, .9f)), bodyRadius97 = R3(Percentile(planar, .97f)),
            offsetFromMeshCenter = new[] { R3(off.x), R3(off.z) },
        };
    }

    static bool Same(CombatTargetVolume a, CombatTargetVolume b) =>
        (a.Center - b.Center).sqrMagnitude < 1e-4f && Mathf.Abs(a.Radius - b.Radius) < .01f && Mathf.Abs(a.HalfHeight - b.HalfHeight) < .01f;

    static T Field<T>(object o, string name)
    {
        if (o == null) return default;
        var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return f != null && f.GetValue(o) is T v ? v : default;
    }

    // ---- 그리기 ----
    static readonly Color32 CHurt = new Color32(255, 64, 64, 255), CBody = new Color32(70, 150, 255, 255),
        CVfx = new Color32(80, 230, 110, 255), CHitFx = new Color32(40, 220, 220, 255), CCollider = new Color32(255, 214, 60, 255),
        CFire = new Color32(255, 140, 0, 255), CDark = new Color32(200, 90, 255, 255), CShock = new Color32(90, 210, 255, 255),
        CIce = new Color32(210, 245, 255, 255), CWhite = new Color32(255, 255, 255, 255), CMesh = new Color32(235, 235, 235, 255);

    sealed class Painter
    {
        readonly Color32[] px; readonly float[] mask; readonly int w, h; readonly Camera c;
        int x0, y0, x1, y1;
        public Painter(Texture2D t, Camera camera) { px = t.GetPixels32(); w = t.width; h = t.height; c = camera; mask = new float[w * h]; Reset(); }
        void Reset() { x0 = w; y0 = h; x1 = -1; y1 = -1; }
        Vector2 S(Vector3 world, out bool ok) { var s = c.WorldToScreenPoint(world); ok = s.z > 0f; return new Vector2(s.x, s.y); }
        void Stamp(Vector2 p, float r)
        {
            int ax = Mathf.Max(0, Mathf.FloorToInt(p.x - r - 1)), bx = Mathf.Min(w - 1, Mathf.CeilToInt(p.x + r + 1));
            int ay = Mathf.Max(0, Mathf.FloorToInt(p.y - r - 1)), by = Mathf.Min(h - 1, Mathf.CeilToInt(p.y + r + 1));
            for (int y = ay; y <= by; y++)
                for (int x = ax; x <= bx; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), p);
                    float cov = Mathf.Clamp01(r + .5f - d);
                    if (cov <= 0f) continue;
                    int i = y * w + x; if (cov > mask[i]) mask[i] = cov;
                }
            x0 = Mathf.Min(x0, ax); y0 = Mathf.Min(y0, ay); x1 = Mathf.Max(x1, bx); y1 = Mathf.Max(y1, by);
        }
        void Commit(Color32 col, float alpha)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * w + x; float a = mask[i] * alpha; if (a <= 0f) continue; mask[i] = 0f;
                    var o = px[i];
                    px[i] = new Color32((byte)Mathf.Lerp(o.r, col.r, a), (byte)Mathf.Lerp(o.g, col.g, a), (byte)Mathf.Lerp(o.b, col.b, a), 255);
                }
            Reset();
        }
        public void Poly(IList<Vector3> pts, bool closed, Color32 col, float width, float dash = 0f, float alpha = .95f)
        {
            float run = 0f; int n = closed ? pts.Count : pts.Count - 1;
            for (int k = 0; k < n; k++)
            {
                Vector2 a = S(pts[k], out bool okA), b = S(pts[(k + 1) % pts.Count], out bool okB);
                if (!okA || !okB) continue;
                float len = Vector2.Distance(a, b); int steps = Mathf.Max(1, Mathf.CeilToInt(len * 1.5f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps; float d = run + len * t;
                    if (dash > 0f && (d / dash) % 2f > 1f) continue;
                    Stamp(Vector2.Lerp(a, b, t), width * .5f);
                }
                run += len;
            }
            Commit(col, alpha);
        }
        public void Dot(Vector3 world, float radius, Color32 col)
        {
            Vector2 p = S(world, out bool ok); if (!ok) return;
            Stamp(p, radius + 1.5f); Commit(new Color32(20, 20, 20, 255), .9f);
            Stamp(p, radius); Commit(col, 1f);
        }
        public void Save(Texture2D t, string path) { t.SetPixels32(px); t.Apply(false); File.WriteAllBytes(path, t.EncodeToPNG()); }
    }

    static Vector3[] Ring(Vector3 c, Vector3 axis, float r, int n = 56)
    {
        axis = axis.normalized;
        Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.forward).normalized, v = Vector3.Cross(axis, u);
        var p = new Vector3[n];
        for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; p[i] = c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r; }
        return p;
    }

    static void Cylinder(Painter p, CombatTargetVolume v, Vector3 view, Color32 col, float width, float dash, float alpha = .95f)
    {
        Vector3 bot = v.Center - Vector3.up * v.HalfHeight, top = v.Center + Vector3.up * v.HalfHeight;
        p.Poly(Ring(bot, Vector3.up, v.Radius), true, col, width, dash, alpha);
        p.Poly(Ring(top, Vector3.up, v.Radius), true, col, width, dash, alpha);
        Vector3 s = Vector3.Cross(Vector3.up, view);
        if (s.sqrMagnitude > 1e-3f)
        {
            s = s.normalized * v.Radius;
            p.Poly(new[] { bot + s, top + s }, false, col, width, dash, alpha);
            p.Poly(new[] { bot - s, top - s }, false, col, width, dash, alpha);
        }
        float m = Mathf.Min(.08f, v.Radius * .2f);
        p.Poly(new[] { v.Center - Vector3.up * m, v.Center + Vector3.up * m }, false, col, width, 0f, alpha);
        Vector3 hz = Vector3.Cross(view, Vector3.up); if (hz.sqrMagnitude < 1e-3f) hz = Vector3.right; hz = hz.normalized * m;
        p.Poly(new[] { v.Center - hz, v.Center + hz }, false, col, width, 0f, alpha);
    }

    static void Capsule(Painter p, Vector3 c, Vector3 axis, float r, float h, Vector3 view, Color32 col, float width, float dash)
    {
        axis = axis.normalized; float half = Mathf.Max(0f, h * .5f - r);
        Vector3 top = c + axis * half, bot = c - axis * half;
        p.Poly(Ring(top, axis, r), true, col, width, dash); p.Poly(Ring(bot, axis, r), true, col, width, dash);
        Vector3 s = Vector3.Cross(axis, view);
        if (s.sqrMagnitude < 1e-3f) return;
        s.Normalize();
        p.Poly(new[] { bot + s * r, top + s * r }, false, col, width, dash);
        p.Poly(new[] { bot - s * r, top - s * r }, false, col, width, dash);
        var arcT = new Vector3[25]; var arcB = new Vector3[25];
        for (int i = 0; i < 25; i++)
        {
            float a = i * Mathf.PI / 24f;
            arcT[i] = top + (s * Mathf.Cos(a) + axis * Mathf.Sin(a)) * r;
            arcB[i] = bot + (s * Mathf.Cos(a) - axis * Mathf.Sin(a)) * r;
        }
        p.Poly(arcT, false, col, width, dash); p.Poly(arcB, false, col, width, dash);
    }

    static void Box(Painter p, Transform t, Vector3 center, Vector3 size, Color32 col, float width, float dash)
    {
        var k = new Vector3[8];
        for (int i = 0; i < 8; i++)
            k[i] = t.TransformPoint(center + Vector3.Scale(size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        int[,] e = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
        for (int i = 0; i < 12; i++) p.Poly(new[] { k[e[i, 0]], k[e[i, 1]] }, false, col, width, dash);
    }

    static void DrawCollider(Painter p, Collider c, Vector3 view)
    {
        float dash = c.isTrigger ? 7f : 0f; var t = c.transform; var s = t.lossyScale;
        switch (c)
        {
            case CharacterController cc:
                Capsule(p, t.TransformPoint(cc.center), Vector3.up, cc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)), cc.height * Mathf.Abs(s.y), view, CCollider, 2f, dash);
                break;
            case CapsuleCollider cap:
            {
                Vector3 axis = cap.direction == 0 ? t.right : cap.direction == 2 ? t.forward : t.up;
                float along = cap.direction == 0 ? Mathf.Abs(s.x) : cap.direction == 2 ? Mathf.Abs(s.z) : Mathf.Abs(s.y);
                float across = cap.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)) : cap.direction == 2 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)) : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                Capsule(p, t.TransformPoint(cap.center), axis, cap.radius * across, Mathf.Max(cap.height * along, cap.radius * across * 2f), view, CCollider, 2f, dash);
                break;
            }
            case SphereCollider sph:
            {
                float r = sph.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                p.Poly(Ring(t.TransformPoint(sph.center), view, r), true, CCollider, 2f, dash);
                p.Poly(Ring(t.TransformPoint(sph.center), Vector3.up, r), true, CCollider, 1.5f, dash);
                break;
            }
            case BoxCollider box: Box(p, t, box.center, box.size, CCollider, 2f, dash); break;
            default:
            {
                var bnd = c.bounds; var go = new GameObject(); try { Box(p, go.transform, bnd.center, bnd.size, CCollider, 1.5f, 7f); } finally { Object.Destroy(go); }
                break;
            }
        }
    }

    static object ColliderInfo(Collider c, Basis b)
    {
        var info = new Dictionary<string, object> { ["type"] = c.GetType().Name, ["name"] = c.name, ["trigger"] = c.isTrigger, ["layer"] = LayerMask.LayerToName(c.gameObject.layer) };
        var bnd = c.bounds; info["boundsCenter"] = V(b.L(bnd.center)); info["boundsSize"] = V(bnd.size);
        var s = c.transform.lossyScale;
        if (c is CharacterController cc) { info["radius"] = R3(cc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z))); info["height"] = R3(cc.height * Mathf.Abs(s.y)); info["center"] = V(b.L(c.transform.TransformPoint(cc.center))); }
        if (c is CapsuleCollider cap) { info["radius"] = R3(cap.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z))); info["height"] = R3(cap.height * Mathf.Abs(s.y)); info["direction"] = cap.direction; info["center"] = V(b.L(c.transform.TransformPoint(cap.center))); }
        return info;
    }

    // ---- 촬영 ----
    static void Aim(Vector3 center, Vector3 dir, Vector3 up, float ortho)
    {
        cam.orthographic = true; cam.orthographicSize = ortho;
        cam.transform.rotation = Quaternion.LookRotation(dir, up);
        float dist = ortho * 6f + 10f;
        cam.transform.position = center - dir * dist;
        cam.nearClipPlane = .05f; cam.farClipPlane = dist * 2f + 10f;
    }

    // 촬영하는 동안만 대상 계층을 촬영 전용 레이어로 옮겨 경기장·플레이어가 찍히지 않게 한다.
    static Texture2D Render(IEnumerable<GameObject> roots)
    {
        var saved = new List<KeyValuePair<GameObject, int>>();
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) { saved.Add(new KeyValuePair<GameObject, int>(t.gameObject, t.gameObject.layer)); t.gameObject.layer = layer; }
        }
        try { cam.Render(); }
        finally { foreach (var s in saved) if (s.Key != null) s.Key.layer = s.Value; }
        var previous = RenderTexture.active;
        try { RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, Px, Px), 0, 0); tex.Apply(false); }
        finally { RenderTexture.active = previous; }
        return tex;
    }

    static Material Unlit(Color color)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", color); materials.Add(m); return m;
    }

    // 바닥판과 0.5m(큰 몬스터는 1m) 눈금. 촬영 레이어에만 둔다.
    static void BuildFloor(Vector3 root, float floorY, float span)
    {
        if (fixture != null) Object.Destroy(fixture);
        fixture = new GameObject("Hitbox audit floor");
        float step = span > 6f ? 1f : .5f; float extent = Mathf.Ceil(span * .9f / step) * step + step;
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.name = "floor"; plane.transform.SetParent(fixture.transform);
        plane.layer = layer; plane.GetComponent<Collider>().enabled = false;
        plane.transform.position = new Vector3(root.x, floorY, root.z); plane.transform.localScale = Vector3.one * (extent * 2f / 10f);
        var floorMat = Unlit(new Color(.2f, .23f, .26f)); plane.GetComponent<Renderer>().sharedMaterial = floorMat;
        var gridMat = Unlit(new Color(.33f, .37f, .41f));
        for (float n = -extent; n <= extent + 1e-3f; n += step)
        {
            Line(new Vector3(root.x + n, floorY + .003f, root.z - extent), new Vector3(root.x + n, floorY + .003f, root.z + extent), gridMat, span * .004f);
            Line(new Vector3(root.x - extent, floorY + .003f, root.z + n), new Vector3(root.x + extent, floorY + .003f, root.z + n), gridMat, span * .004f);
        }
    }

    static void Line(Vector3 a, Vector3 b, Material m, float width)
    {
        var go = new GameObject("grid"); go.transform.SetParent(fixture.transform); go.layer = layer;
        var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = m; line.useWorldSpace = true; line.positionCount = 2;
        line.SetPosition(0, a); line.SetPosition(1, b); line.startWidth = line.endWidth = Mathf.Max(.006f, width);
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
    }

    static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());

    static readonly (string key, WeaponElement element, MeleeElementStatusAuraType aura, float wait)[] Passes =
    {
        ("fire", WeaponElement.Fire, MeleeElementStatusAuraType.Burning, 1.4f),
        ("dark", WeaponElement.Dark, MeleeElementStatusAuraType.Corroded, 2.0f),
        ("shock", WeaponElement.Electric, MeleeElementStatusAuraType.Shocked, .6f),
        ("ice", WeaponElement.Ice, MeleeElementStatusAuraType.Chilled, .9f),
    };

    static IEnumerator Monster(int index, EnemyDefinition def, Camera main, GameObject source, OverburstElementEnergy energy)
    {
        string tag = index.ToString("00") + "_" + Safe(def.EnemyId);
        yield return Spawn(def);
        yield return Wait(.6f);
        var e = current;
        var b = new Basis(e.transform);
        var log = new List<string>();
        var verts = BodyVertices(e, log);
        if (verts.Count == 0) throw new Exception("no skinned vertices");
        var target = e.GetComponent<CombatTarget>();
        e.TryGetComponent(out CombatTargetVfxPlacement placement);
        var hurt = target.CurrentHurtVolume; var body = target.CurrentVolume;
        var vfx = CombatTargetVfxPlacement.ResolveVolume(target);
        var hitFx = placement != null ? placement.HitVolume : hurt;
        var colliders = e.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToArray();
        var mesh = MeshStats(verts, b, out Vector3 meshCenter, out float meshTop, out float meshBottom);

        // 모든 판정과 메쉬가 들어가도록 같은 축척으로 네 방향을 찍는다.
        Vector3 mn = verts[0], mx = verts[0];
        foreach (var w in verts) { mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w); }
        var frameBounds = new Bounds((mn + mx) * .5f, mx - mn);
        foreach (var v in new[] { hurt, body, vfx })
            frameBounds.Encapsulate(new Bounds(v.Center, new Vector3(v.Radius * 2f, v.HalfHeight * 2f, v.Radius * 2f)));
        foreach (var c in colliders) frameBounds.Encapsulate(c.bounds);
        float span = Mathf.Max(frameBounds.size.x, Mathf.Max(frameBounds.size.y, frameBounds.size.z));
        float ortho = span * .5f * 1.12f;
        float floorY = Mathf.Min(b.O.y, mn.y) - .005f;
        BuildFloor(b.O, floorY, span);

        var images = new Dictionary<string, string>();
        var views = new (string key, Vector3 dir, Vector3 up)[]
        {
            ("front", -b.F, Vector3.up), ("side", -b.R, Vector3.up), ("top", Vector3.down, b.F), ("quarter", main.transform.forward, main.transform.up),
        };
        bool bodyDiffers = !Same(body, hurt), vfxDiffers = !Same(vfx, hurt), hitFxDiffers = !Same(hitFx, vfx);
        foreach (var view in views)
        {
            Aim(frameBounds.center, view.dir, view.up, ortho);
            var p = new Painter(Render(new[] { e.gameObject, fixture }), cam);
            foreach (var c in colliders) DrawCollider(p, c, view.dir);
            if (bodyDiffers) Cylinder(p, body, view.dir, CBody, 2.5f, 0f);
            if (hitFxDiffers) Cylinder(p, hitFx, view.dir, CHitFx, 2f, 5f);
            if (vfxDiffers) Cylinder(p, vfx, view.dir, CVfx, 2.5f, 9f);
            Cylinder(p, hurt, view.dir, CHurt, 3f, 0f);
            p.Dot(meshCenter, 3.5f, CMesh);
            string file = tag + "_" + view.key + ".png"; p.Save(tex, Path.Combine(Output, file)); images[view.key] = file;
        }

        var pres0 = e.GetComponentInChildren<MeleeElementStatusAuraPresentation>(true);
        var entry = new Dictionary<string, object>
        {
            ["index"] = index, ["id"] = def.EnemyId, ["name"] = def.DisplayName, ["prefab"] = e.name.Replace("(Clone)", ""),
            ["grade"] = e.TryGetComponent(out EnemyRank rank) ? rank.GradeType.ToString() : "?",
            ["visualScale"] = V(e.VisualRoot.localScale), ["collisionScale"] = V(e.CollisionRoot.localScale),
            ["ortho"] = R3(ortho), ["frameCenter"] = V(b.L(frameBounds.center)), ["meshLog"] = log,
            ["mesh"] = mesh,
            ["hurt"] = VolumeStats(hurt, verts, b, meshCenter), ["hurtCustom"] = target.HasCustomHurtVolume,
            ["body"] = VolumeStats(body, verts, b, meshCenter), ["bodyDiffers"] = bodyDiffers,
            ["vfx"] = VolumeStats(vfx, verts, b, meshCenter), ["vfxDiffers"] = vfxDiffers,
            ["vfxAuthored"] = placement != null && Field<bool>(placement, "useAuthoredVolume"),
            ["hitFx"] = VolumeStats(hitFx, verts, b, meshCenter), ["hitFxDiffers"] = hitFxDiffers,
            ["hitFxAuthored"] = placement != null && Field<bool>(placement, "useAuthoredHitVolume"),
            ["hasVfxPlacement"] = placement != null,
            ["burnTuning"] = placement != null ? new { offset = V(Field<Vector3>(placement, "burnOffset")), scale = R3(Field<float>(placement, "burnScale")) } : null,
            ["colliders"] = colliders.Select(c => ColliderInfo(c, b)).ToList(),
            ["images"] = images,
        };

        // 오라: 원소마다 새로 소환해 5중첩을 준 뒤 게임 시점과 정면에서 찍는다.
        var auras = new List<object>();
        var service = Object.FindFirstObjectByType<ElementalReactionVfxRuntimeService>();
        foreach (var pass in Passes)
        {
            yield return Spawn(def);
            BuildFloor(b.O, floorY, span);
            yield return Wait(.35f);
            e = current;
            Apply(e, pass.element, 5, source, energy.WeaponInstanceId);
            yield return Wait(pass.wait);
            var pres = e.GetComponentInChildren<MeleeElementStatusAuraPresentation>(true);
            if (pass.aura == MeleeElementStatusAuraType.Shocked)
            {
                float limit = Time.time + 3f;
                while (Time.time < limit && (pres == null || !pres.IsAuraActive(MeleeElementStatusAuraType.Shocked)))
                { yield return null; pres = e.GetComponentInChildren<MeleeElementStatusAuraPresentation>(true); }
                yield return Wait(.25f);
            }
            GameObject go = null; string source2 = "presentation";
            if (pass.element == WeaponElement.Ice && service != null)
            {
                var status = e.GetComponent<ElementalStatusController>();
                int id = service.GetLoopInstanceIdForValidation(status, ElementalReactionType.Freeze);
                go = id != 0 ? EditorUtility.InstanceIDToObject(id) as GameObject : null;
                source2 = "freezeLoop";
            }
            if (go == null && pres != null) { go = pres.GetAuraObject(pass.aura); if (pass.element == WeaponElement.Ice) source2 = "chilledAura"; }
            bool active = go != null && go.activeInHierarchy;
            Vector3 anchor = go != null ? go.transform.position : Vector3.zero;
            Vector3 emitter = go != null && go.transform.childCount > 0 && pass.aura == MeleeElementStatusAuraType.Burning ? go.transform.GetChild(0).position : anchor;

            // 살아 있는 입자 위치 범위(입자 크기 제외).
            int alive = 0; Vector3 pmn = Vector3.positiveInfinity, pmx = Vector3.negativeInfinity;
            if (active)
            {
                var buffer = new ParticleSystem.Particle[4096];
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                {
                    int n = ps.GetParticles(buffer); alive += n;
                    var mainModule = ps.main;
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 pos = buffer[k].position;
                        if (mainModule.simulationSpace == ParticleSystemSimulationSpace.Local) pos = ps.transform.TransformPoint(pos);
                        else if (mainModule.simulationSpace == ParticleSystemSimulationSpace.Custom && mainModule.customSimulationSpace != null) pos = mainModule.customSimulationSpace.TransformPoint(pos);
                        var l = b.L(pos); pmn = Vector3.Min(pmn, l); pmx = Vector3.Max(pmx, l);
                    }
                }
            }
            var roots = new List<GameObject> { e.gameObject, fixture };
            if (go != null && !go.transform.IsChildOf(e.transform)) roots.Add(go);
            Color32 mark = pass.aura == MeleeElementStatusAuraType.Burning ? CFire : pass.aura == MeleeElementStatusAuraType.Corroded ? CDark
                : pass.aura == MeleeElementStatusAuraType.Shocked ? CShock : CIce;
            var auraImages = new Dictionary<string, string>();
            foreach (var view in new[] { ("quarter", main.transform.forward, main.transform.up), ("front", -b.F, Vector3.up) })
            {
                Aim(frameBounds.center + Vector3.up * span * .12f, view.Item2, view.Item3, ortho * 1.3f);
                var p = new Painter(Render(roots), cam);
                Cylinder(p, vfx, view.Item2, CVfx, 1.5f, 9f, .6f);
                if (active) { p.Dot(anchor, 5f, mark); if ((emitter - anchor).sqrMagnitude > 1e-4f) p.Dot(emitter, 3.5f, CWhite); }
                string file = tag + "_" + pass.key + "_" + view.Item1 + ".png"; p.Save(tex, Path.Combine(Output, file)); auraImages[view.Item1] = file;
            }
            auras.Add(new
            {
                type = pass.key, source = source2, active, anchor = V(b.L(anchor)), emitter = V(b.L(emitter)),
                worldScale = go != null ? R3(go.transform.lossyScale.x) : 0,
                particles = alive, particleMin = alive > 0 ? V(pmn) : null, particleMax = alive > 0 ? V(pmx) : null,
                images = auraImages,
            });
        }
        var presFinal = current != null ? current.GetComponentInChildren<MeleeElementStatusAuraPresentation>(true) : pres0;
        entry["auraTuning"] = presFinal != null ? new
        {
            burningScale = R3(Field<float>(presFinal, "burningAuraScale")), burningHeight = R3(Field<float>(presFinal, "burningAuraHeight")),
            corrodedScale = R3(Field<float>(presFinal, "corrodedAuraScale")),
        } : null;
        entry["auras"] = auras;
        entry["meshTop"] = R3(meshTop); entry["meshBottom"] = R3(meshBottom);
        ReleaseCurrent();
        monsters.Add(entry);
        Flush("RUNNING");
        Debug.Log("[HitboxAudit] " + tag + " done");
        yield return Wait(.2f);
    }
}
