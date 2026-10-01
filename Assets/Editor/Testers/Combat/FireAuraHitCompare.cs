using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

// Local-only 2026-09-30: 불 오라(Fire Loop sim 2)와 불 타격 후보(Fire Burst sim 3)를 지금 게임 것과 나란히 보여 주는 관찰용 Play.
// 소형·중형·정예 3줄 × 4칸: 지금 FireAura(실제 화상 5중첩) | Fire Loop sim 2 | 지금 불 타격 | Fire Burst sim 3 (타격은 1.4초마다).
// 조작(Game 뷰 클릭 후): F5 조절 대상 오라↔타격, F6/F7 높이 내림/올림, F8/F9 크기 줄임/키움.
// 키는 InputSystem.onAnyButtonPress로 받는다(에디터 update에서 wasPressedThisFrame을 읽으면 Game 뷰 입력이 안 잡힘).
// 값은 화면 안내판과 끝날 때 result.txt에 남는다. 최대 5분, 그 전에 멈춰도 된다. 격리 계정(OVERBURST_SAVE_DIRECTORY).
[InitializeOnLoad]
public static class FireAuraHitCompare
{
    const string Key = "FireAuraHitCompare";
    const float HoldSeconds = 300f;
    const string Pack = "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle 02/Realistic Environmental Fire and Explosions Pack/";
    const string AuraCandidate = "Fire Loop sim 2";
    const string HitCandidate = "Fire Burst sim 3";
    const float HitInterval = 1.4f;

    // 오라 = 몸 크기 배율(현재 FireAura와 같은 clamp(반경/0.55, 0.6, 3) × 몬스터별 불 보정) × auraScale, 발에서 auraHeight × 몸 높이.
    // 타격 = hitScale 고정 크기, 몸 중심에서 hitHeight × 몸 높이.
    static float auraScale = .3f, auraHeight = 0f, hitScale = .4f, hitHeight = 0f;
    static bool adjustHit;

    static IEnumerator work; static int frame;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> notes = new List<string>(), errors = new List<string>();
    static readonly Queue<string> keys = new Queue<string>();
    static IDisposable keySub;
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static FireAuraHitCompare() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        foreach (var n in new[] { AuraCandidate, HitCandidate })
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Pack + n + ".prefab") == null) throw new ArgumentException("Missing " + n);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            notes.Clear(); errors.Clear(); stack.Clear(); keys.Clear(); frame = -1; adjustHit = false;
            keySub = InputSystem.onAnyButtonPress.Call(control => { if (control?.device is Keyboard) keys.Enqueue(control.name); });
            work = Show(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            keySub?.Dispose(); keySub = null;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            if (Status == "RUNNING") WriteResult("STOPPED_BY_USER");
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            SessionState.SetBool(Key, false);
        }
    }
    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception) errors.Add(m); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (Step()) return; Finish("COMPLETE"); }
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
    static string Values() => $"auraScale={auraScale:F2} auraHeight={auraHeight:F2} hitScale={hitScale:F2} hitHeight={hitHeight:F2}";
    static void WriteResult(string status)
    {
        SessionState.SetString(Key + ".status", status);
        if (Output.Length > 0) File.WriteAllLines(Path.Combine(Output, "result.txt"), new[] { status, "final " + Values() }.Concat(notes).Concat(errors.Take(20)));
    }
    static void Finish(string status) { WriteResult(status); EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); }
    static IEnumerator Wait(float s) { float until = Time.time + s; while (Time.time < until) yield return null; }

    static TMPro.TextMeshPro Label(string text, Vector3 at, Camera cam, Color color, float size = 4f)
    {
        var go = new GameObject("CompareLabel");
        var tmp = go.AddComponent<TMPro.TextMeshPro>();
        tmp.text = text; tmp.fontSize = size; tmp.alignment = TMPro.TextAlignmentOptions.Center; tmp.color = color;
        tmp.rectTransform.sizeDelta = new Vector2(12f, 2f);
        go.transform.position = at; go.transform.rotation = cam.transform.rotation;
        return tmp;
    }

    sealed class Cell { public EnemyActor Enemy; public GameObject Fire; public CombatTargetVolume Volume; public Vector3 BurnOffset; public float BurnTune; }

    static void PlaceAura(Cell c)
    {
        if (c.Fire == null) return;
        float body = Mathf.Clamp(c.Volume.Radius / .55f, .6f, 3f) * c.BurnTune;
        c.Fire.transform.localScale = Vector3.one * Mathf.Max(.05f, body * auraScale);
        c.Fire.transform.position = c.Enemy.transform.position + c.BurnOffset + Vector3.up * (auraHeight * c.Volume.HalfHeight * 2f + .05f);
    }

    static IEnumerator Show()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null;
        var leased = new List<EnemyActor>(); var temp = new List<GameObject>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var small = defs.First(d => d.EnemyId.Contains("Ceratoferox"));
            var medium = defs.First(d => d.EnemyId.Contains("Scolokarck"));
            var elite = defs.FirstOrDefault(d => d.Grade != null && d.Grade.GradeType == EnemyGradeType.Elite)
                ?? defs.First(d => d.EnemyId.Contains("Ursacetus"));
            var rows = new[] { ("SMALL " + small.EnemyId, small, .84f), ("MEDIUM " + medium.EnemyId, medium, .66f), ("ELITE " + elite.EnemyId, elite, .27f) };
            notes.Add("rows: " + string.Join(", ", rows.Select(r => r.Item1)));
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            yield return Wait(1.0f);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Quaternion FaceCamera(Vector3 at) { Vector3 f = cam.transform.position - at; f.y = 0f; return Quaternion.LookRotation(f.sqrMagnitude > .01f ? f : Vector3.back); }
            EnemyActor Spawn(EnemyDefinition d, Vector3 at)
            {
                if (!spawn.TrySpawn(new EnemySpawnRequest(d, at, FaceCamera(at), player.transform), out var e)) throw new Exception("Spawn " + d.EnemyId);
                leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                return e;
            }
            float[] xs = { .16f, .38f, .62f, .84f };
            string[] cols = { "NOW FireAura", AuraCandidate, "NOW fire hit", HitCandidate };
            var grid = new Cell[3, 4];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                    grid[r, c] = new Cell { Enemy = Spawn(rows[r].Item2, Ground(xs[c], rows[r].Item3)) };
            yield return Wait(0.6f);
            void Burn(EnemyActor e)
            {
                var s = e.GetComponent<ElementalStatusController>();
                for (int k = 0; k < 5; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Fire, 10, player.gameObject, energy.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
            }
            var auraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + AuraCandidate + ".prefab");
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    var cell = grid[r, c]; var target = cell.Enemy.GetComponent<CombatTarget>();
                    cell.Volume = CombatTargetVfxPlacement.ResolveVolume(target);
                    CombatTargetVfxPlacement.ResolveBurnTuning(target, out cell.BurnOffset, out cell.BurnTune);
                }
                Burn(grid[r, 0].Enemy);
                var fire = grid[r, 1];
                fire.Fire = UnityEngine.Object.Instantiate(auraPrefab);
                fire.Fire.transform.rotation = Quaternion.identity;
                PlaceAura(fire);
                fire.Fire.transform.SetParent(fire.Enemy.transform, true);
                temp.Add(fire.Fire);
                temp.Add(Label(rows[r].Item1, Ground(.02f, rows[r].Item3) + Vector3.up * .5f, cam, new Color(.7f, .9f, 1f), 3f).gameObject);
            }
            for (int c = 0; c < 4; c++)
                temp.Add(Label(cols[c], Ground(xs[c], .95f) + Vector3.up * .5f, cam, c % 2 == 0 ? new Color(1f, .85f, .3f) : Color.white).gameObject);
            var help = Label("", Ground(.5f, .47f) + Vector3.up * 3.5f, cam, new Color(1f, 1f, .6f), 3.2f);
            temp.Add(help.gameObject);

            var hitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + HitCandidate + ".prefab");
            float end = Time.time + HoldSeconds, nextHit = Time.time + .3f, nextBurn = Time.time + 3f;
            while (Time.time < end)
            {
                bool changed = false;
                while (keys.Count > 0)
                {
                    string k = keys.Dequeue();
                    float dh = k == "f7" ? .05f : k == "f6" ? -.05f : 0f;
                    float ds = k == "f9" ? 1.1f : k == "f8" ? 1f / 1.1f : 1f;
                    if (k == "f5") { adjustHit = !adjustHit; changed = true; }
                    else if (dh != 0f || ds != 1f)
                    {
                        changed = true;
                        if (adjustHit) { hitHeight += dh; hitScale *= ds; }
                        else { auraHeight += dh; auraScale *= ds; for (int r = 0; r < 3; r++) PlaceAura(grid[r, 1]); }
                    }
                }
                if (changed) notes.Add($"{Time.time:F1}s adjust={(adjustHit ? "HIT" : "AURA")} {Values()}");
                string target = adjustHit ? "HIT (Fire Burst sim 3)" : "AURA (Fire Loop sim 2)";
                help.text = $"adjust: {target}   [F5] switch\n[F6/F7] height  aura {auraHeight:+0.00;-0.00}  hit {hitHeight:+0.00;-0.00}   [F8/F9] scale  aura {auraScale:F2}  hit {hitScale:F2}";

                if (Time.time >= nextHit)
                {
                    nextHit = Time.time + HitInterval;
                    for (int r = 0; r < 3; r++)
                    {
                        var a = CombatTargetVfxPlacement.ResolveVolume(grid[r, 2].Enemy.GetComponent<CombatTarget>());
                        MeleeElementHitVfxService.TryPlay(WeaponElement.Fire, a.Center);
                        var b = CombatTargetVfxPlacement.ResolveVolume(grid[r, 3].Enemy.GetComponent<CombatTarget>());
                        var burst = UnityEngine.Object.Instantiate(hitPrefab, b.Center + Vector3.up * (hitHeight * b.HalfHeight * 2f), Quaternion.identity);
                        burst.transform.localScale = Vector3.one * hitScale;
                        UnityEngine.Object.Destroy(burst, 3.5f);
                    }
                }
                if (Time.time >= nextBurn) { nextBurn = Time.time + 3f; for (int r = 0; r < 3; r++) Burn(grid[r, 0].Enemy); }
                yield return null;
            }
        }
        finally
        {
            foreach (var g in temp) if (g != null) UnityEngine.Object.Destroy(g);
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }
}
