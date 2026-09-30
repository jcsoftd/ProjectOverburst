using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: DeathHarvest KillerDoll 전진 공격의 루트 모션 확인. 실제 Play(격리 계정)에서 공격을 직접 시작해
// 골반-액터 수평 거리, 액터 이동량, 공격 종료 뒤 몸이 되돌아가는 거리, 피해 순간 간격을 기록하고
// 비교 대상은 EnemyAttackRootMotion을 끈 상태(OFF)와 켠 상태(ON)를 같은 구도로 찍는다. 아무것도 저장하지 않는다.
[InitializeOnLoad]
public static class RootMotionAttackCapture
{
    const string Key = "RootMotionAttackCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static readonly List<object> results = new List<object>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static RootMotionAttackCapture() { EditorApplication.playModeStateChanged += State; }

    // only: "" = 전체, "main" = 이동이 큰 6개만
    public static void Run(string output, string only = "")
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output); SessionState.SetString(Key + ".only", only ?? "");
        SessionState.SetString(Key + ".env", Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY") ?? "");
        Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); results.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 480;
            work = Capture(SessionState.GetString(Key + ".only", ""));
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
    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m + "\n" + s); }
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
            IEnumerator top = stack.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }
    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "rootmotion-results.json"), JsonConvert.SerializeObject(new { status, screen = new[] { Screen.width, Screen.height }, results, shots, notes, errors }, Formatting.Indented));
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

    struct Case { public string Enemy, Clip; public bool Main; public Case(string e, string c, bool m) { Enemy = e; Clip = c; Main = m; } }

    static IEnumerator Capture(string only)
    {
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>(); var markers = new List<GameObject>();
        void ReleaseAll()
        {
            foreach (var m in markers) if (m != null) UnityEngine.Object.Destroy(m); markers.Clear();
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); leased.Clear();
        }
        var cases = new[]
        {
            new Case("DeathHarvest_BoneMoss", "KillerDoll_AttackForward05", true),
            new Case("DeathHarvest_BoneMoss", "KillerDoll_AttackForwardHandStandKicks02", true),
            new Case("DeathHarvest_BoneWarden", "KillerDoll_AttackForwardHandStandKicks02", true),
            new Case("DeathHarvest_RakeBrute", "KillerDoll_AttackForwardHandStandKicks02", true),
            new Case("DeathHarvest_RakeSkulker", "KillerDoll_AttackForwardHandStandKicks01", true),
            new Case("DeathHarvest_RakeStalker", "KillerDoll_AttackForward06", true),
            new Case("DeathHarvest_BoneAsh", "KillerDoll_AttackForward02", false),
            new Case("DeathHarvest_BoneAsh", "KillerDoll_AttackForward04", false),
            new Case("DeathHarvest_BoneWarden", "KillerDoll_AttackForward02", false),
            new Case("DeathHarvest_RakeBrute", "KillerDoll_AttackForward03", false),
            new Case("DeathHarvest_RakeStalker", "KillerDoll_AttackForward03", false),
            new Case("DeathHarvest_RakeSkulker", "KillerDoll_AttackForward01", false),
        };
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            var ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var cam = Camera.main; var origin = player.transform.position;
            var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Vector3 up = Flat(Ground(.5f, .62f) - Ground(.5f, .5f)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 spot = origin + right * 7f;
            var markerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); markerMat.SetColor("_BaseColor", new Color(1f, .1f, .1f));

            int caseIndex = 0;
            foreach (var c in cases)
            {
                caseIndex++;
                if (only == "main" && !c.Main) continue;
                var def = defs.FirstOrDefault(d => d.EnemyId == c.Enemy);
                if (def == null) { notes.Add(c.Enemy + ": definition missing"); continue; }
                EnemyAbilityDefinition ability = null; int abilityIndex = -1;
                for (int i = 0; i < def.AbilitySet.Count; i++) { var a = def.AbilitySet.GetAbility(i); if (a != null && (a.AbilityId + "|" + a.name).Contains(c.Clip)) { ability = a; abilityIndex = i; break; } }
                if (ability == null) { notes.Add(c.Enemy + " " + c.Clip + ": ability missing"); continue; }
                foreach (var mode in new[] { "off", "on", "travel" })
                {
                    Warp(player, spot + right * 3f, up); yield return Wait(0.3f);
                    if (!spawn.TrySpawn(new EnemySpawnRequest(def, spot - right * 1.5f, Quaternion.LookRotation(right), player.transform), out var e)) { notes.Add(c.Enemy + ": spawn failed"); continue; }
                    leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                    e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    float startRange = EnemyAttackThreatGeometry.ResolveStartRange(e, ability);
                    float dist = Mathf.Max(1.2f, startRange * .8f);
                    Warp(player, e.transform.position + right * dist, -right);
                    var rm = e.GetComponent<EnemyAttackRootMotion>();
                    if (rm == null) { notes.Add(c.Enemy + ": EnemyAttackRootMotion missing"); ReleaseAll(); continue; }
                    rm.enabled = mode != "off";
                    var pelvis = e.Animator.GetBoneTransform(HumanBodyBones.Hips);
                    var capsule = e.GetComponentInChildren<CapsuleCollider>(true);
                    float r = capsule != null ? capsule.radius * capsule.transform.lossyScale.x : .4f;
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder); UnityEngine.Object.Destroy(marker.GetComponent<Collider>());
                    marker.GetComponent<Renderer>().sharedMaterial = markerMat; marker.transform.SetParent(e.transform, false);
                    marker.transform.localPosition = new Vector3(0f, .03f, 0f); marker.transform.localScale = new Vector3(r * 2f, .015f, r * 2f); markers.Add(marker);
                    yield return Wait(0.5f);

                    int damageCount = 0; float damageNt = -1f, damageGap = -1f;
                    void OnHit(CombatHealth h, DamageInfo info)
                    {
                        if (info.source == null || info.source.GetComponentInParent<EnemyActor>() != e) return;
                        damageCount++;
                        if (damageCount == 1) { e.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out damageNt); damageGap = Flat(player.transform.position - e.transform.position).magnitude; }
                    }
                    actor.Health.OnDamaged += OnHit;
                    try
                    {
                        Vector3 start = e.transform.position;
                        bool started = false; float until = Time.time + 3f;
                        while (!started && Time.time < until) { e.Movement.StopMovement(); started = e.Melee.TryStartAbility(player.transform, ability, abilityIndex); if (!started) yield return null; }
                        if (!started) { notes.Add(c.Enemy + " " + c.Clip + " " + mode + ": could not start"); continue; }
                        if (mode == "travel") { yield return null; Warp(player, spot + up * 9f, up); }
                        float maxOffset = 0f, offsetAtHit = -1f; float lastNt = 0f; bool seenState = false; int stateFrames = 0;
                        Vector3 pelvisAtEnd = Vector3.zero, actorAtEnd = Vector3.zero; string tag = caseIndex.ToString("D2") + "_" + mode;
                        var shotMarks = new List<float> { .12f, ability.HitNormalizedTime, .9f }; int shotIndex = 0;
                        var screenPos = new List<object>();
                        float stateUntil = Time.time + 8f;
                        while (Time.time < stateUntil)
                        {
                            bool inState = e.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float nt);
                            if (inState) { seenState = true; stateFrames++; lastNt = nt; pelvisAtEnd = pelvis.position; actorAtEnd = e.transform.position; }
                            else if (seenState) break;
                            float offset = Flat(pelvis.position - e.transform.position).magnitude;
                            if (inState) maxOffset = Mathf.Max(maxOffset, offset);
                            if (inState && offsetAtHit < 0f && nt >= ability.HitNormalizedTime) offsetAtHit = offset;
                            if (c.Main && mode != "travel" && inState && shotIndex < shotMarks.Count && nt >= shotMarks[shotIndex])
                            {
                                Shot(tag + "_" + (shotIndex + 1)); var sp = cam.WorldToScreenPoint(e.transform.position); var pp = cam.WorldToScreenPoint(pelvis.position);
                                screenPos.Add(new { shot = shotIndex + 1, nt, actor = new[] { sp.x, sp.y }, pelvis = new[] { pp.x, pp.y } }); shotIndex++;
                            }
                            yield return null;
                        }
                        // 공격 상태가 끝난 뒤 몸이 되돌아가는지: 0.4초 동안 골반 이동
                        float after = Time.time + .4f; Vector3 pelvisBefore = pelvisAtEnd; float snap = 0f;
                        bool afterShot = false;
                        while (Time.time < after)
                        {
                            snap = Mathf.Max(snap, Flat(pelvis.position - pelvisBefore).magnitude);
                            if (c.Main && mode != "travel" && !afterShot && Time.time > after - .15f)
                            {
                                afterShot = true; Shot(tag + "_4"); var sp = cam.WorldToScreenPoint(e.transform.position); var pp = cam.WorldToScreenPoint(pelvis.position);
                                screenPos.Add(new { shot = 4, nt = 1.1f, actor = new[] { sp.x, sp.y }, pelvis = new[] { pp.x, pp.y } });
                            }
                            yield return null;
                        }
                        float finalOffset = Flat(pelvis.position - e.transform.position).magnitude;
                        results.Add(new
                        {
                            index = caseIndex, enemy = c.Enemy, clip = c.Clip, mode, main = c.Main,
                            hit = ability.HitNormalizedTime, startRange, spawnDistance = dist,
                            stateFrames, lastNt,
                            maxPelvisOffset = maxOffset, pelvisOffsetAtHit = offsetAtHit, finalPelvisOffset = finalOffset,
                            actorTravel = Flat(actorAtEnd - start).magnitude, actorTravelForward = Vector3.Dot(actorAtEnd - start, right),
                            snapAfterEnd = snap, appliedTravel = rm.AppliedTravel,
                            damageCount, damageNt, damageGap,
                            finalGap = Flat(player.transform.position - e.transform.position).magnitude,
                            screenPos
                        });
                    }
                    finally { actor.Health.OnDamaged -= OnHit; }
                    ReleaseAll(); yield return Wait(0.3f);
                }
            }
        }
        finally { ReleaseAll(); }
    }
}
