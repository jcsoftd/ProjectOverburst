using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// 2026-10-01: 몬스터 패링 3클립(무너짐 -> 기절 루프 -> 회복) 실제 Play 확인(격리 계정). 아무것도 저장하지 않는다.
// 12종마다 AI로 싸우게 두고 강공 준비 중에 실제 패링 처리(PlayerParryController.CancelAndStun)를 부른다.
// 확인: 상태 순서와 구간 길이(무너짐 = 클립, 기절 루프 = 한 바퀴를 2~3초로 제한, 회복 = 클립),
//   기절 중 약공을 맞아도 피격 모션이 기절을 끊지 않는지, 회복이 끝나기 전에는 공격·이동을 시작하지 않는지.
// 무너짐·기절·회복 장면을 한 장씩 촬영한다.
[InitializeOnLoad]
public static class ParryStunAnimationVerifier
{
    const string Key = "ParryStunAnimationVerifier";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>(), violations = new List<string>();
    static readonly List<object> monsters = new List<object>();
    static object feel;
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static ParryStunAnimationVerifier() { EditorApplication.playModeStateChanged += State; }

    public static readonly string[] Ids =
    {
        "CavernMutants_Ursacetus", "CavernMutants_Gasterobrach", "CavernMutants_Gorhorrid",
        "PrimalHunt_Dimaxillosaurus", "PrimalHunt_Occisodonte", "PrimalHunt_Venosaur_Tint_Brown",
        "SpiderBrood_Carcinoptera", "SpiderBrood_Scolokarck_Tint3", "SpiderBrood_Rostrokarck",
        "VenomBrood_Arathrox", "VenomBrood_Kupolojuve_Tint_Orange", "VenomBrood_Kupolobrach_Tint_Orange",
    };

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
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
            feel = null; shots.Clear(); notes.Clear(); errors.Clear(); violations.Clear(); monsters.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 480;
            work = Capture();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null; Time.timeScale = 1f;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            SessionState.SetBool(Key, false);
        }
    }
    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m + "\n" + s); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish(violations.Count == 0 && errors.Count == 0 ? "PASS" : "FAIL"); }
        catch (Exception e) { Finish("ERROR " + e); }
    }
    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }
    static void Finish(string status)
    {
        Time.timeScale = 1f;
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, violations, feel, monsters, shots, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
    static void Shot(string name) { string path = Path.Combine(Output, name + ".png"); ScreenCapture.CaptureScreenshot(path); shots.Add(path); }
    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string StateOf(Animator a)
    {
        var s = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
        foreach (var n in new[] { EnemyAnimationBridge.ParryCollapseStateName, EnemyAnimationBridge.StunnedLoopStateName,
                     EnemyAnimationBridge.StunRecoverStateName, "Get_hit", "Locomotion", "Death" })
            if (s.IsName(n)) return n;
        return "Other";
    }
    static float ClipLength(Animator a, string suffix)
    {
        foreach (var c in a.runtimeAnimatorController.animationClips) if (c != null && c.name.EndsWith(suffix)) return c.length;
        return -1f;
    }

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>();
        void ReleaseAll() { if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); leased.Clear(); }
        EnemyThemeTrialHarness ui = null;
        var cancelAndStun = typeof(PlayerParryController).GetMethod("CancelAndStun", BindingFlags.NonPublic | BindingFlags.Static);
        if (cancelAndStun == null) throw new Exception("PlayerParryController.CancelAndStun not found");
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
            var cam = Camera.main; var origin = player.transform.position;
            var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Vector3 up = Flat(Ground(.5f, .62f) - Ground(.5f, .5f)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 spot = origin + right * 6f;

            // 2026-10-01: 패링 성공 연출(히트스톱 -> 강한 슬로우 -> 서서히 복귀, 카메라 확대)을 한 번 재서 기록한다.
            var playSuccess = typeof(PlayerParryController).GetMethod("PlaySuccess", BindingFlags.NonPublic | BindingFlags.Instance);
            var parryController = UnityEngine.Object.FindFirstObjectByType<PlayerParryController>();
            if (playSuccess == null || parryController == null) violations.Add("패링 연출 측정: PlayerParryController.PlaySuccess 없음");
            else
            {
                yield return Wait(1.6f); // 슬로우 재사용 대기(1.5초)가 지난 뒤
                float baseSize = cam.orthographicSize, minSize = baseSize, minScale = 1f, slowScale = 1f, slowEnd = -1f, zoomEnd = -1f;
                var samples = new List<string>(); bool shotFeel = false;
                Shot("parry_feel_0_before");
                yield return null;
                float u0 = Time.unscaledTime;
                playSuccess.Invoke(parryController, new object[] { player.transform.position + right * 2f, 1, true });
                while (Time.unscaledTime < u0 + 1.4f)
                {
                    float u = Time.unscaledTime - u0;
                    minScale = Mathf.Min(minScale, Time.timeScale);
                    if (u > .15f) slowScale = Mathf.Min(slowScale, Time.timeScale); // 히트스톱이 끝난 뒤의 슬로우 세기
                    minSize = Mathf.Min(minSize, cam.orthographicSize);
                    if (slowEnd < 0f && u > .05f && Time.timeScale >= .999f) slowEnd = u;
                    if (zoomEnd < 0f && u > .1f && cam.orthographicSize >= baseSize - .002f) zoomEnd = u;
                    if (!shotFeel && u >= .2f) { shotFeel = true; Shot("parry_feel_1_zoom"); }
                    samples.Add(u.ToString("0.00") + " ts=" + Time.timeScale.ToString("0.00") + " size=" + (cam.orthographicSize / baseSize).ToString("0.000"));
                    yield return null;
                }
                feel = new { minTimeScale = minScale, slowTimeScale = slowScale, slowEndUnscaled = slowEnd,
                    zoomRatioMin = minSize / baseSize, zoomEndUnscaled = zoomEnd, samples = samples.Where((s, i) => i % 4 == 0).ToList() };
                if (slowScale > .2f) violations.Add($"패링 슬로우 약함: 최저 {slowScale:0.00}배");
                if (slowEnd < 0f) violations.Add("패링 슬로우가 1.4초 안에 안 풀림");
                if (minSize / baseSize > .95f) violations.Add($"카메라 확대 부족: {minSize / baseSize:0.000}");
                if (zoomEnd < 0f) violations.Add("카메라 확대가 1.4초 안에 안 돌아옴");
                yield return Wait(0.5f);
            }

            foreach (string id in Ids)
            {
                Warp(player, spot, right); yield return Wait(0.3f);
                var def = defs.FirstOrDefault(d => d.EnemyId == id);
                if (def == null || !spawn.TrySpawn(new EnemySpawnRequest(def, spot + right * 3f, Quaternion.LookRotation(-right), player.transform), out var e))
                { violations.Add(id + ": 생성 실패"); continue; }
                leased.Add(e); e.AI.enabled = true; e.Health.SetMaxHp(1000000, true);
                e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var reaction = e.GetComponent<EnemyMovementReaction>();
                float collapseClip = ClipLength(e.Animator, "_ParryCollapse"), loopLen = ClipLength(e.Animator, "_StunnedLoop"), recoverLen = ClipLength(e.Animator, "_StunRecover");
                float collapseLen = collapseClip / EnemyAnimationBridge.ParryCollapseSpeed; // 컨트롤러 무너짐 상태 속도
                float stunnedExpect = EnemyAnimationBridge.ParryStunnedSeconds;

                // 강공 준비가 시작되면(최대 12초) 0.25초 뒤 패링 처리. 강공이 안 나오면 평타 중이나 대기 중에 건다.
                float waitStart = Time.time, execAt = -1f; string parriedDuring = "idle";
                while (Time.time < waitStart + 12f)
                {
                    Vector3 gap = Flat(player.transform.position - e.transform.position);
                    if (gap.magnitude > 2.4f) Warp(player, e.transform.position + (gap.sqrMagnitude > .01f ? gap.normalized : e.transform.forward) * 2f, -gap);
                    var ab = e.AbilityController;
                    if (ab.IsExecuting && ab.LastCommittedAbility != null && ab.LastCommittedAbility.IsTelegraphedStrongAttack)
                    {
                        if (execAt < 0f) execAt = Time.time;
                        if (Time.time >= execAt + .25f) { parriedDuring = "strong"; break; }
                    }
                    else execAt = -1f;
                    yield return null;
                }
                if (parriedDuring == "idle") notes.Add(id + ": 12초 안에 강공이 없어 대기 중에 패링 처리");
                cancelAndStun.Invoke(null, new object[] { e, player.transform.position });

                float t0 = Time.time; string last = null; var timeline = new List<string>();
                var enter = new Dictionary<string, float>();
                bool hitSent = false, attackDuringStun = false, movedDuringStun = false, hitBroke = false;
                float stunEndAt = -1f; Vector3 stunPos = e.transform.position;
                bool shotC = false, shotL = false, shotR = false;
                float total = collapseLen + stunnedExpect + recoverLen;
                while (Time.time < t0 + total + 1.2f)
                {
                    float t = Time.time - t0;
                    string s = StateOf(e.Animator);
                    if (s != last) { timeline.Add(t.ToString("0.00") + " " + s); if (!enter.ContainsKey(s)) enter[s] = t; last = s; }
                    if (stunEndAt < 0f && !reaction.IsParryStunned) stunEndAt = t;
                    bool locked = e.AnimationBridge.IsParryStunAnimating;
                    if (locked && e.AbilityController.IsExecuting) attackDuringStun = true;
                    if (locked && Flat(e.transform.position - stunPos).magnitude > .9f) movedDuringStun = true; // 패링 밀림(0.4m)은 허용
                    if (!hitSent && s == EnemyAnimationBridge.StunnedLoopStateName && t > collapseLen + .4f)
                    {
                        hitSent = true;
                        e.Health.TakeDamage(new DamageInfo(1, e.transform.position, player.gameObject, right, 4,
                            sourceAttackSequenceId: 910000, playerAttackKind: PlayerAttackKind.Weak, sourceAttackPhaseIndex: 0, weakKnockbackDistance: .05f));
                    }
                    if (hitSent && locked && s == "Get_hit") hitBroke = true;
                    if (!shotC && s == EnemyAnimationBridge.ParryCollapseStateName && t >= collapseLen * .55f) { shotC = true; Shot(id + "_1_collapse"); }
                    if (!shotL && s == EnemyAnimationBridge.StunnedLoopStateName && t >= collapseLen + .9f) { shotL = true; Shot(id + "_2_stunned"); }
                    if (!shotR && s == EnemyAnimationBridge.StunRecoverStateName && enter.TryGetValue(s, out float rIn) && t >= rIn + recoverLen * .5f) { shotR = true; Shot(id + "_3_recover"); }
                    yield return null;
                }

                float Enter(string n) => enter.TryGetValue(n, out float v) ? v : -1f;
                float c = Enter(EnemyAnimationBridge.ParryCollapseStateName), l = Enter(EnemyAnimationBridge.StunnedLoopStateName),
                      r = Enter(EnemyAnimationBridge.StunRecoverStateName), loco = -1f;
                foreach (var row in timeline) { var parts = row.Split(' '); if (parts[1] == "Locomotion" && r >= 0f && float.Parse(parts[0]) > r) { loco = float.Parse(parts[0]); break; } }
                if (c < 0f || l < 0f || r < 0f || loco < 0f) violations.Add(id + ": 상태 순서 불완전 " + string.Join(" > ", timeline));
                else
                {
                    if (!(c < l && l < r && r < loco)) violations.Add(id + ": 상태 순서 어긋남 " + string.Join(" > ", timeline));
                    if (Mathf.Abs((l - c) - collapseLen) > .2f) violations.Add($"{id}: 무너짐 {l - c:0.00}초(클립 {collapseLen:0.00})");
                    if (Mathf.Abs((r - l) - stunnedExpect) > .25f) violations.Add($"{id}: 기절 루프 {r - l:0.00}초(기대 {stunnedExpect:0.00})");
                    if (Mathf.Abs((loco - r) - recoverLen) > .25f) violations.Add($"{id}: 회복 {loco - r:0.00}초(클립 {recoverLen:0.00})");
                }
                if (attackDuringStun) violations.Add(id + ": 패링 반응 중 공격 시작");
                if (movedDuringStun) violations.Add(id + ": 패링 반응 중 이동");
                if (hitBroke) violations.Add(id + ": 기절 중 피격 모션이 기절을 끊음");
                if (stunEndAt >= 0f && r >= 0f && Mathf.Abs(stunEndAt - r) > .15f) violations.Add($"{id}: 기절 판정 종료 {stunEndAt:0.00}초와 회복 시작 {r:0.00}초가 어긋남");
                monsters.Add(new
                {
                    enemy = id, parriedDuring, clips = new { collapse = collapseClip, collapsePlayed = collapseLen, loop = loopLen, recover = recoverLen }, stunnedExpect,
                    observed = new { collapse = l - c, stunned = r - l, recover = loco - r, stunEndAt }, hitSent, timeline,
                });
                spawn.Release(e); leased.Remove(e); yield return Wait(0.4f);
            }
        }
        finally { ReleaseAll(); if (ui != null && ui.InArena) ui.ToggleArena(); }
    }
}
