using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only 2026-09-30 강공 임팩트·광휘 검증: 빛 3연타(광휘 100중첩)·빛 2연타·암흑 강공을 실제 Play에서 쓰고
// 재생 중인 AudioSource 클립, 단계음 재생 시각, 충격파·흡인 왜곡·카메라 요청 수, 왕관·오라 상태와 Game 뷰 캡처를 남긴다.
// Isolated account (OVERBURST_SAVE_DIRECTORY); exits Play itself.
[InitializeOnLoad]
public static class HeavyImpactPolishCapture
{
    const string Key = "HeavyImpactPolishCapture";
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<string> shots = new List<string>();
    static readonly List<object> records = new List<object>();
    static readonly List<string> errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static HeavyImpactPolishCapture() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
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
            shots.Clear(); records.Clear(); errors.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 240;
            stack.Clear(); work = Capture(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
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
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish("COMPLETE"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }

    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
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
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, records, shots, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    static void Shot(string name)
    {
        string path = Path.Combine(Output, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        shots.Add(path);
    }

    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true;
    }

    // 지금 소리를 내고 있는 AudioSource의 클립 이름(재생 위치 0.25초 이내 = 방금 시작한 소리만).
    static List<string> FreshClips()
    {
        var list = new List<string>();
        foreach (var src in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (src.isPlaying && src.clip != null && src.time <= .25f) list.Add(src.clip.name + "@" + src.volume.ToString("F2"));
        list.Sort();
        return list;
    }

    static readonly UpperHeavySfxStage[] Stages = (UpperHeavySfxStage[])Enum.GetValues(typeof(UpperHeavySfxStage));

    static IEnumerator Capture()
    {
        EnemyThemeTrialHarness ui = null; MeleeRuntime melee = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            melee = player.GetComponent<MeleeRuntime>();
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Vector3 up = Ground(.5f, .62f) - Ground(.5f, .5f); up.y = 0f; up.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 spot = origin + right * 7f;
            var tuning = OverburstElementTuning.Current;
            records.Add(new { part = "tuning", light = tuning.SafeLightTripleVfxPlaybackSpeed, dark = tuning.SafeDarkVfxPlaybackSpeed });
            // label, element, overcharge(light triple + radiance 100), sample times after commit
            var plans = new (string label, WeaponElement element, bool over, float[] times)[]
            {
                ("light3", WeaponElement.Light, true, new[] { .03f, .66f, 1.29f, 1.45f }),
                ("light2", WeaponElement.Light, false, new[] { .03f, .66f }),
                ("dark", WeaponElement.Dark, false, new[] { .03f, .7f, 1.1f, 1.53f, 1.75f }),
            };
            foreach (var plan in plans)
            {
                melee.CancelCurrentAttackState();
                if (!actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: plan.element))) throw new Exception("Equip " + plan.element);
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                Warp(player, spot, up);
                var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
                yield return Wait(1.5f);
                energy.Clear(); int seq = 95000 + (int)plan.element * 100 + (plan.over ? 50 : 0);
                for (int i = 0; i < 10; i++) energy.RecordConfirmedHit(energy.WeaponInstanceId, energy.Element, ++seq, 1);
                if (plan.over)
                {
                    typeof(OverburstElementEnergy).GetProperty("Amount").GetSetMethod(true).Invoke(energy, new object[] { 200f });
                    typeof(OverburstElementEnergy).GetProperty("RadianceStacks").GetSetMethod(true).Invoke(energy, new object[] { 100 });
                    yield return Wait(1.2f);
                    var presenter = player.GetComponent<LightRadianceAuraPresenter>();
                    var crownT = player.transform.Find("Radiance crown");
                    var anim = player.GetComponentInChildren<Animator>();
                    var headBone = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                    records.Add(new { part = "radiance", crown = presenter != null && presenter.IsCrownShown, aura = presenter != null && presenter.IsEmitting,
                        intensity = presenter != null ? presenter.CurrentIntensity : -1f,
                        crownY = crownT != null ? crownT.position.y - player.transform.position.y : -1f,
                        headY = headBone != null ? headBone.position.y - player.transform.position.y : -1f,
                        auraName = player.transform.Find("Radiance aura") != null });
                    var sp = cam.WorldToScreenPoint(player.transform.position + Vector3.up * 1.2f);
                    records.Add(new { part = "playerScreen", x = sp.x, y = sp.y, w = Screen.width, h = Screen.height });
                    Shot("radiance_100_before_heavy");
                }
                MeleeElementSfxService.ResetUpperHeavyCounters();
                int shock0 = UpperHeavyImpactFeedback.ShockwaveCount, cam0 = UpperHeavyImpactFeedback.CameraCount;
                WeaponActionResult result = WeaponActionResult.RejectedNotReady; float until = Time.time + 2f;
                while (Time.time < until && result != WeaponActionResult.Accepted)
                {
                    PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                    result = melee.TryStartHeavyAttack(up); if (result != WeaponActionResult.Accepted) yield return null;
                }
                if (result != WeaponActionResult.Accepted) throw new Exception("Heavy " + plan.label + " " + result);
                float commit = -1f; until = Time.time + 3f;
                while (Time.time < until) { if (energy.Amount <= 0f) { commit = Time.time; break; } yield return null; }
                if (commit < 0f) throw new Exception("No commit " + plan.label);
                var disc = typeof(MeleeRuntime).GetField("activeDischarge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(melee) as OverburstElementDischarge;
                records.Add(new { part = plan.label + ".commit", commitAt = commit, lightTriple = disc != null && disc.LightTriple, firstHit = disc != null ? disc.LightFirstHitIndex : -1, fresh = FreshClips() });
                foreach (float t in plan.times)
                {
                    while (Time.time < commit + t) yield return null;
                    records.Add(new { part = plan.label + ".t" + t.ToString("F2"), fresh = FreshClips(),
                        shock = UpperHeavyImpactFeedback.ShockwaveCount - shock0, camera = UpperHeavyImpactFeedback.CameraCount - cam0 });
                    Shot(plan.label + "_" + t.ToString("F2"));
                }
                yield return Wait(1.2f);
                var stages = new Dictionary<string, string>();
                foreach (var s in Stages)
                {
                    int n = MeleeElementSfxService.UpperHeavyPlayCount(s);
                    if (n > 0) stages[s.ToString()] = n + "x @" + (MeleeElementSfxService.UpperHeavyLastPlayTime(s) - commit).ToString("F2") + "s";
                }
                records.Add(new { part = plan.label + ".summary", stages,
                    shock = UpperHeavyImpactFeedback.ShockwaveCount - shock0, camera = UpperHeavyImpactFeedback.CameraCount - cam0 });
                yield return Wait(2.5f);
            }
        }
        finally
        {
            melee?.CancelCurrentAttackState();
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }
}
