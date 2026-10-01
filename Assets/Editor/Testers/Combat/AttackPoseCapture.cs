using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Local-only: real Play Mode pose filmstrips of enemy attack states (Animator.Play + Update(0) per frame),
// rendered by a side camera into a RenderTexture. Works for humanoid clips. Writes meta in the strip-sheet format.
[InitializeOnLoad]
public static class AttackPoseCapture
{
    const string Key = "AttackPoseCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> meta = new List<string>(), errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static AttackPoseCapture() { EditorApplication.playModeStateChanged += State; }

    // ids: "|"-separated enemy ids; comboOnly: only abilities with more than one hit.
    public static void Run(string output, string tag, string ids, bool comboOnly)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output); SessionState.SetString(Key + ".tag", tag);
        SessionState.SetString(Key + ".ids", ids); SessionState.SetBool(Key + ".combo", comboOnly);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    // Follow mode: camera re-centres on the visible mesh every frame (clips whose motion is baked into the pose),
    // and only abilities whose id contains one of the "|"-separated filter tokens are captured.
    public static void RunFollow(string output, string tag, string ids, string filter)
    {
        SessionState.SetBool(Key + ".follow", true); SessionState.SetString(Key + ".filter", filter ?? "");
        try { Run(output, tag, ids, false); }
        catch { SessionState.SetBool(Key + ".follow", false); throw; }
    }
    static bool Follow => SessionState.GetBool(Key + ".follow", false);
    static string[] Filter => SessionState.GetString(Key + ".filter", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            meta.Clear(); errors.Clear(); stack.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 600;
            work = Capture(SessionState.GetString(Key + ".ids", "").Split('|'), SessionState.GetBool(Key + ".combo", false));
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
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".follow", false); SessionState.SetString(Key + ".filter", "");
        }
    }
    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception) errors.Add(m); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish("COMPLETE"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }
    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }
    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        string tag = SessionState.GetString(Key + ".tag", "play");
        File.WriteAllLines(Path.Combine(Output, "meta_" + tag + ".txt"), meta);
        File.WriteAllLines(Path.Combine(Output, "status_" + tag + ".txt"), new[] { status }.Concat(errors.Take(20)));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    static IEnumerator Wait(float s) { float until = Time.time + s; while (Time.time < until) yield return null; }

    static readonly string[] StrikeKeys = { "claw", "hand", "jaw", "head", "tongue", "tail", "finger", "scythe", "weapon", "sword", "blade", "toe", "foot", "mouth", "feeler", "spike" };
    static string[] Wanted(string id)
    {
        string an = id.ToLowerInvariant();
        return an.Contains("scythe") ? new[] { "scythe", "weapon", "hand" }
            : an.Contains("spell") ? new[] { "hand", "finger" }
            : an.Contains("kick") || an.Contains("spin") ? new[] { "foot", "toe", "hand", "weapon" }
            : an.Contains("claw") ? new[] { "claw", "hand", "finger", "thumb" }
            : new[] { "weapon", "sword", "hand", "foot", "claw" };
    }
    static int Side(string n) { n = n.ToLowerInvariant(); return n.Contains("left") || n.EndsWith("_l") || n.Contains(" l ") || n.Contains("_l_") ? 1 : n.Contains("right") || n.EndsWith("_r") || n.Contains(" r ") || n.Contains("_r_") ? 2 : 0; }

    static IEnumerator Capture(string[] ids, bool comboOnly)
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; var leased = new List<EnemyActor>();
        var rt = new RenderTexture(180, 180, 24); var tex = new Texture2D(180, 180, TextureFormat.RGB24, false);
        var camGo = new GameObject("PoseCam"); var cam = camGo.AddComponent<Camera>();
        cam.enabled = false; cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.22f, .23f, .26f); cam.targetTexture = rt;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current;
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            Vector3 spot = player.transform.position + new Vector3(0f, 0f, -14f);
            var seenClips = new HashSet<string>();
            foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var def = defs.FirstOrDefault(d => d.EnemyId == id);
                if (def == null) { meta.Add(id + "|MISSING"); continue; }
                if (!spawn.TrySpawn(new EnemySpawnRequest(def, spot, Quaternion.identity, player.transform), out var e)) throw new Exception("Spawn " + id);
                leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                var animator = e.Animator; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false;
                yield return Wait(0.6f);
                var bones = animator.GetComponentsInChildren<Transform>(true).Where(t => t != animator.transform && t.GetComponent<Renderer>() == null).ToArray();
                var renderers = e.GetComponentsInChildren<Renderer>(true).Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r.enabled).ToArray();
                for (int i = 0; i < def.AbilitySet.Count; i++)
                {
                    var a = def.AbilitySet.GetAbility(i); if (a == null || (comboOnly && a.HitCount < 2)) continue;
                    var filter = Filter; if (filter.Length > 0 && !filter.Any(a.AbilityId.Contains)) continue;
                    string trig = a.AnimatorTrigger; string stateName = trig.StartsWith("Attack") && trig.Length > 6 ? "Attack_" + trig.Substring(6) : trig;
                    int hash = Animator.StringToHash(stateName);
                    if (!animator.HasState(0, hash)) { meta.Add(id + "|" + a.AbilityId + "|NOSTATE " + stateName); continue; }
                    animator.speed = 1f;
                    animator.Play(hash, 0, 0f); animator.Update(0f);
                    var info = animator.GetCurrentAnimatorClipInfo(0);
                    string clipName = info.Length > 0 ? info[0].clip.name : "?"; float len = info.Length > 0 ? info[0].clip.length : 1f;
                    bool first = seenClips.Add(clipName + "@" + def.Species?.SpeciesId);
                    const int S = 60, N = 16;
                    var pos = new Vector3[S + 1, bones.Length];
                    Bounds bounds = default; bool has = false;
                    for (int k = 0; k <= S; k++)
                    {
                        animator.Play(hash, 0, (float)k / S); animator.Update(0f);
                        for (int b = 0; b < bones.Length; b++) pos[k, b] = e.transform.InverseTransformPoint(bones[b].position);
                        if (k % 6 == 0) foreach (var r in renderers) { if (!has) { bounds = r.bounds; has = true; } else bounds.Encapsulate(r.bounds); }
                    }
                    var wanted = Wanted(a.AbilityId);
                    int[] best = { -1, -1, -1 }; float[] peak = { 0, 0, 0 };
                    for (int b = 0; b < bones.Length; b++)
                    {
                        string n = bones[b].name.ToLowerInvariant();
                        if (!wanted.Any(n.Contains)) continue;
                        float p = 0; for (int k = 5; k <= S - 5; k++) p = Mathf.Max(p, (pos[k, b] - pos[k - 1, b]).magnitude * S / len);
                        int side = Side(bones[b].name);
                        if (p > peak[side]) { peak[side] = p; best[side] = b; }
                    }
                    int s1 = peak[1] >= peak[2] ? (best[1] >= 0 ? best[1] : best[0]) : best[2];
                    int s2 = s1 == best[1] ? best[2] : best[1];
                    if (s1 < 0) s1 = best[0];
                    string Curve(int b, Func<Vector3, float> f) => b < 0 ? "" : string.Join(",", Enumerable.Range(0, S + 1).Select(k => f(pos[k, b]).ToString("F3")));
                    string Speed(int b) => b < 0 ? "" : string.Join(",", Enumerable.Range(0, S + 1).Select(k => (k > 0 ? (pos[k, b] - pos[k - 1, b]).magnitude * S / len : 0f).ToString("F2")));
                    bool wide = a.HitCount > 1 || a.ExecutionMode == EnemyAbilityExecutionMode.Projectile || a.ExecutionMode == EnemyAbilityExecutionMode.Charge;
                    float t0 = wide ? .02f : .15f, t1 = wide ? .98f : .85f;
                    Vector3 view = (e.transform.right + Vector3.up * .21f).normalized;
                    cam.orthographicSize = Mathf.Max(bounds.extents.y, Mathf.Max(bounds.extents.z, bounds.extents.x), .4f) * 1.25f;
                    cam.transform.position = bounds.center + view * (bounds.extents.magnitude * 3f + 3f); cam.transform.LookAt(bounds.center);
                    cam.nearClipPlane = .1f; cam.farClipPlane = bounds.extents.magnitude * 8f + 10f;
                    int mask = 0; foreach (var r in renderers) mask |= 1 << r.gameObject.layer; cam.cullingMask = mask;
                    var strip = new Texture2D(180 * 8, 360, TextureFormat.RGB24, false);
                    var dots = new List<string>(); var travel = new List<string>();
                    var centers = new Vector3[N];
                    if (Follow)
                    {
                        // Pre-pass with the frame hold so the retargeted mesh is what gets measured.
                        // Centre = centroid of the visible skinning bones (renderer bounds do not follow baked travel).
                        var skin = renderers.OfType<SkinnedMeshRenderer>().SelectMany(s => s.bones).Where(b => b != null).Distinct().ToArray();
                        float spread = .4f;
                        for (int f = 0; f < N; f++)
                        {
                            float nt = t0 + (t1 - t0) * f / (N - 1);
                            animator.speed = 0f; animator.Play(hash, 0, nt); animator.Update(0f); yield return null;
                            animator.Play(hash, 0, nt); animator.Update(0f); yield return null;
                            Vector3 c = Vector3.zero; foreach (var b in skin) c += b.position;
                            c = skin.Length > 0 ? c / skin.Length : e.transform.position;
                            foreach (var b in skin) { var d = b.position - c; spread = Mathf.Max(spread, Mathf.Abs(d.y), Mathf.Abs(Vector3.Dot(d, e.transform.forward))); }
                            centers[f] = c;
                            var lc = e.transform.InverseTransformPoint(c);
                            travel.Add(lc.z.ToString("F2") + ":" + lc.x.ToString("F2") + ":" + lc.y.ToString("F2"));
                        }
                        cam.orthographicSize = spread * 1.2f;
                        cam.farClipPlane = spread * 12f + 10f;
                    }
                    for (int f = 0; f < N; f++)
                    {
                        float nt = t0 + (t1 - t0) * f / (N - 1);
                        // Hold the pose for one real frame so LateUpdate retargeting (KillerDoll rigs) reaches the visible mesh.
                        animator.speed = 0f; animator.Play(hash, 0, nt); animator.Update(0f);
                        yield return null;
                        animator.Play(hash, 0, nt); animator.Update(0f);
                        yield return null;
                        if (Follow) { cam.transform.position = centers[f] + view * (cam.orthographicSize * 3f + 3f); cam.transform.LookAt(centers[f]); }
                        cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 180, 180), 0, 0); tex.Apply(); RenderTexture.active = null;
                        strip.SetPixels((f % 8) * 180, f < 8 ? 180 : 0, 180, 180, tex.GetPixels());
                        if (s1 >= 0) { var vp = cam.WorldToViewportPoint(bones[s1].position); dots.Add(vp.x.ToString("F3") + ":" + vp.y.ToString("F3")); }
                    }
                    strip.Apply();
                    string file = "P_" + id + "__" + a.AbilityId.Replace(id + "_", "") + ".png";
                    File.WriteAllBytes(Path.Combine(Output, file), strip.EncodeToPNG()); UnityEngine.Object.Destroy(strip);
                    string hits = string.Join("/", Enumerable.Range(0, a.HitCount).Select(h => a.GetHitNormalizedTime(h).ToString("F2")));
                    meta.Add(id + "|" + a.AbilityId.Replace(id + "_", "") + "|" + a.ExecutionMode + (a.IsTelegraphedStrongAttack ? " STRONG" : "") + "|" + clipName + "|" + len.ToString("F2")
                        + "|hit=" + hits + "|reachPeak=-|speedPeak=-|contact=-1|striker=" + (s1 >= 0 ? bones[s1].name : "-") + (s2 >= 0 ? " + " + bones[s2].name : "")
                        + "|cz=" + Curve(s1, p => p.z) + "|cy=" + Curve(s1, p => p.y) + "|cv=" + Speed(s1) + "|cz2=" + Curve(s2, p => p.z) + "|cy2=" + Curve(s2, p => p.y)
                        + "|dots=" + string.Join(",", dots) + "|file=" + file + "|first=" + first + "|t0=" + t0 + "|t1=" + t1 + (travel.Count > 0 ? "|travel=" + string.Join(",", travel) : ""));
                    yield return null;
                }
                spawn.Release(e); leased.Remove(e);
                yield return null;
            }
        }
        finally
        {
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
            UnityEngine.Object.Destroy(camGo); rt.Release();
        }
    }
}
