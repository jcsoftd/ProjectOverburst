using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: 피해 숫자 종류별 연출을 실제 Play(격리 계정)에서 프레임 단위로 찍는다. 저장하지 않는다.
// mode "styled" = 새 연출, "legacy" = DamageNumberStyleSettings를 꺼서 이전 연출. 종류마다 숫자 주변을 잘라 연속 촬영한다.
[InitializeOnLoad]
public static class DamageNumberFxCapture
{
    const string Key = "DamageNumberFxCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> notes = new List<string>(), errors = new List<string>();
    static readonly List<object> clips = new List<object>();
    static string Output => SessionState.GetString(Key + ".output", "");
    static string Mode => SessionState.GetString(Key + ".mode", "styled");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static DamageNumberFxCapture() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output, string mode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".mode", mode);
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
            notes.Clear(); errors.Clear(); clips.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 300;
            work = Capture();
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
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, mode = Mode, screen = new[] { Screen.width, Screen.height }, clips, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    static IEnumerator Wait(float seconds) { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) yield return null; }

    static DamageInfo Swing(float damage, Vector3 at, GameObject source, bool critical) =>
        new DamageInfo(damage, at, source, Vector3.forward, isCritical: critical, element: WeaponElement.Fire,
            playerAttackKind: PlayerAttackKind.Weak | PlayerAttackKind.Elemental);
    // Same flags as the real discharge paths (ElementDischargeBatch, ShatterWaveScheduler, UpperElementCombatUtility).
    static DamageInfo Discharge(float damage, Vector3 at, GameObject source, WeaponElement element, bool critical = false) =>
        new DamageInfo(damage, at, source, Vector3.forward, isCritical: critical, triggersOnHitEffects: false,
            suppressDefaultHitVfx: true, element: element, playerAttackKind: PlayerAttackKind.Elemental);
    // Same flags as ElementalStatusController ticks.
    static DamageInfo DotTick(float damage, Vector3 at, GameObject source, WeaponElement element) =>
        new DamageInfo(damage, at, source, triggersOnHitEffects: false, isDamageOverTime: true, suppressDefaultHitVfx: true,
            element: element, playerAttackKind: PlayerAttackKind.Elemental, usesResolvedTickDamage: true);

    static IEnumerator Clip(DamageFxFrameRecorder recorder, string label, Vector3 anchorWorld, Vector2 size, Vector2 shift,
        float seconds, Action<float> spawnAt, float[] spawnTimes)
    {
        Camera cam = Camera.main;
        Vector3 screen = cam.WorldToScreenPoint(anchorWorld + Vector3.up * .85f);
        var crop = new RectInt(Mathf.RoundToInt(screen.x + shift.x - size.x * .5f), Mathf.RoundToInt(screen.y + shift.y - size.y * .5f),
            Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
        float start = Time.unscaledTime;
        recorder.Record(Path.Combine(Output, "frames"), label, crop, seconds, start);
        int next = 0;
        while (recorder.Busy || next < spawnTimes.Length)
        {
            float t = Time.unscaledTime - start;
            while (next < spawnTimes.Length && t >= spawnTimes[next]) { spawnAt(t); next++; }
            yield return null;
        }
        if (!string.IsNullOrEmpty(recorder.Error)) errors.Add(label + ": " + recorder.Error);
        clips.Add(new { label, crop = new[] { crop.x, crop.y, crop.width, crop.height }, frames = recorder.LastFrames.ToArray() });
        yield return Wait(.35f);
    }

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>(); EnemyThemeTrialHarness debug = null;
        GameObject recorderObject = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            DamageNumberStyleSettings.SetEnabled(Mode != "legacy");
            notes.Add("styles enabled=" + DamageNumberStyleSettings.Enabled + " preset=" + DamageNumberPopup.SelectedPreset + " font=" + DamageNumberPopup.SelectedFont);
            yield return Wait(2f);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);

            debug = EnemyThemeTrialHarness.Current; if (!debug.InArena) debug.ToggleArena();
            yield return Wait(.8f);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var table in debug.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
            var defs = debug.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var def = defs.FirstOrDefault(d => d.EnemyId == "DeathHarvest_BoneAsh") ?? defs[0];
            Vector3 at = Ground(.63f, .47f); Vector3 look = origin - at; look.y = 0f;
            if (!spawn.TrySpawn(new EnemySpawnRequest(def, at, Quaternion.LookRotation(look), player.transform), out var enemy)) throw new Exception("spawn failed");
            leased.Add(enemy); enemy.Health.SetMaxHp(100000, true);
            yield return Wait(.6f);
            enemy.AI.enabled = false; enemy.Movement.StopMovement();
            yield return Wait(.3f);
            var body = enemy.GetComponent<CombatTarget>();
            Vector3 hit = body.CurrentHurtVolume.Center;
            GameObject src = player.gameObject;
            notes.Add("enemy " + def.EnemyId + " hit " + hit);

            recorderObject = new GameObject("DamageFxFrameRecorder");
            var recorder = recorderObject.AddComponent<DamageFxFrameRecorder>();
            var size = new Vector2(420f, 300f);
            float[] once = { 0f };

            yield return Clip(recorder, "01_normal", hit, size, new Vector2(0f, 50f), 1f, t => DamageNumberSpawner.Spawn(Swing(187f, hit, src, false), 187f), once);
            yield return Clip(recorder, "02_critical", hit, size, new Vector2(0f, 50f), 1f, t => DamageNumberSpawner.Spawn(Swing(426f, hit, src, true), 426f), once);
            // 지속 피해: 실제 틱 간격(불 0.5초, 번개 1.5초)대로 몇 번 이어서.
            int tick = 0; float[] dotTimes = { 0f, .25f, .5f, 1f, 1.5f };
            yield return Clip(recorder, "03_dot", hit, size, new Vector2(0f, 50f), 2.2f, t =>
            {
                WeaponElement element = tick == 1 ? WeaponElement.Electric : WeaponElement.Fire;
                float dmg = element == WeaponElement.Electric ? 21f : 12f + tick;
                DamageNumberSpawner.Spawn(DotTick(dmg, enemy.transform.position, src, element), dmg); tick++;
            }, dotTimes);
            yield return Clip(recorder, "04_fire", hit, size, new Vector2(0f, 60f), 1.05f, t => DamageNumberSpawner.Spawn(Discharge(1240f, hit, src, WeaponElement.Fire), 1240f), once);
            yield return Clip(recorder, "05_ice", hit, size, new Vector2(0f, 40f), 1.1f, t => DamageNumberSpawner.Spawn(Discharge(980f, hit, src, WeaponElement.Ice), 980f), once);
            int hop = 0; float[] chainTimes = { 0f, .07f, .14f, .21f };
            yield return Clip(recorder, "06_electric", hit, new Vector2(520f, 300f), new Vector2(0f, 50f), 1.1f, t =>
            {
                Vector3 p = hit + cam.transform.right * (-1.1f + hop * .75f);
                DamageNumberSpawner.Spawn(Discharge(312f - hop * 23f, p, src, WeaponElement.Electric), 312f - hop * 23f); hop++;
            }, chainTimes);
            yield return Clip(recorder, "07_dark", hit, size, new Vector2(0f, 0f), 1.15f, t => DamageNumberSpawner.Spawn(Discharge(540f, hit, src, WeaponElement.Dark), 540f), once);
            yield return Clip(recorder, "08_light", hit, size, new Vector2(0f, 60f), 1.25f, t => DamageNumberSpawner.Spawn(Discharge(760f, hit, src, WeaponElement.Light), 760f), once);
            yield return Clip(recorder, "09_fire_crit", hit, size, new Vector2(0f, 60f), 1.05f, t => DamageNumberSpawner.Spawn(Discharge(890f, hit, src, WeaponElement.Fire, true), 890f), once);
            Vector3 self = actor.transform.position;
            yield return Clip(recorder, "10_player_hit", self + Vector3.up * .8f, size, new Vector2(0f, 50f), .9f, t => DamageNumberSpawner.SpawnPlayerDamage(self, 348f), once);
            yield return Clip(recorder, "11_heal", self + Vector3.up * .8f, size, new Vector2(0f, 50f), 1.15f, t => DamageNumberSpawner.SpawnHeal(self, 120f), once);

            // 무리 장면: 원소 방출 한 번이 12곳을 맞힌 순간 전체 화면.
            var spots = Enumerable.Range(0, 12).Select(i => Ground(.3f + (i % 6) * .08f, .38f + (i / 6) * .2f)).ToArray();
            float crowdStart = Time.unscaledTime;
            recorder.Record(Path.Combine(Output, "frames"), "12_crowd", new RectInt(0, 0, Screen.width, Screen.height), .5f, crowdStart, 3);
            for (int i = 0; i < spots.Length; i++)
            {
                WeaponElement e = i % 3 == 0 ? WeaponElement.Fire : i % 3 == 1 ? WeaponElement.Ice : WeaponElement.Dark;
                DamageNumberSpawner.Spawn(Discharge(400f + i * 37f, spots[i] + Vector3.up * .8f, src, e), 400f + i * 37f);
                DamageNumberSpawner.Spawn(DotTick(14f, spots[i], src, WeaponElement.Fire), 14f);
            }
            while (recorder.Busy) yield return null;
            clips.Add(new { label = "12_crowd", crop = new[] { 0, 0, Screen.width, Screen.height }, frames = recorder.LastFrames.ToArray() });
            yield return Wait(1.2f);
        }
        finally
        {
            DamageNumberStyleSettings.SetEnabled(true);
            if (recorderObject != null) UnityEngine.Object.Destroy(recorderObject);
            foreach (var e in leased) if (e != null && e.IsLeased) spawn?.Release(e);
            if (debug != null && debug.InArena) debug.ToggleArena();
        }
    }
}

public sealed class DamageFxFrameRecorder : MonoBehaviour
{
    public bool Busy { get; private set; }
    public string Error { get; private set; }
    public readonly List<object> LastFrames = new List<object>();

    public void Record(string directory, string label, RectInt crop, float seconds, float startTime, int maxFrames = 400)
    {
        LastFrames.Clear(); Error = null; Busy = true;
        StartCoroutine(Run(directory, label, crop, seconds, startTime, maxFrames));
    }

    private IEnumerator Run(string directory, string label, RectInt crop, float seconds, float startTime, int maxFrames)
    {
        Directory.CreateDirectory(directory);
        var end = new WaitForEndOfFrame();
        int index = 0;
        while (Time.unscaledTime - startTime < seconds && index < maxFrames)
        {
            yield return end;
            float t = Time.unscaledTime - startTime;
            Texture2D full = null, part = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                int x = Mathf.Clamp(crop.x, 0, full.width - 1), y = Mathf.Clamp(crop.y, 0, full.height - 1);
                int w = Mathf.Clamp(crop.width, 1, full.width - x), h = Mathf.Clamp(crop.height, 1, full.height - y);
                part = new Texture2D(w, h, TextureFormat.RGB24, false);
                part.SetPixels(full.GetPixels(x, y, w, h)); part.Apply();
                string path = Path.Combine(directory, label + "_" + index.ToString("000") + ".png");
                File.WriteAllBytes(path, part.EncodeToPNG());
                LastFrames.Add(new { index, t = Math.Round(t, 3), file = Path.GetFileName(path) });
                index++;
            }
            catch (Exception e) { Error = e.Message; break; }
            finally { if (full != null) Destroy(full); if (part != null) Destroy(part); }
        }
        Busy = false;
    }
}
