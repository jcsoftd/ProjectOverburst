using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: 경험치 바 연동 확인. 격리 계정 Play에서 XP Bar 구조·채움 계산값, 경험치 지급(직접 지급·몬스터 처치)을 기록한다. 저장하지 않는다.
[InitializeOnLoad]
public static class ExpBarProbe
{
    const string Key = "ExpBarProbe";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> lines = new List<string>(), errors = new List<string>(), shots = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static ExpBarProbe() { EditorApplication.playModeStateChanged += State; }

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
            lines.Clear(); errors.Clear(); shots.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 180;
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
        File.WriteAllText(Path.Combine(Output, "expbar-probe.json"), JsonConvert.SerializeObject(new { status, lines, shots, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    static IEnumerator Wait(float seconds) { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) yield return null; }
    static void Shot(string name) { string path = Path.Combine(Output, name + ".png"); ScreenCapture.CaptureScreenshot(path); shots.Add(path); }

    static string Rect(RectTransform r) => r == null ? "null" : $"{r.name} aMin={r.anchorMin} aMax={r.anchorMax} pivot={r.pivot} sizeDelta={r.sizeDelta} rectW={r.rect.width:F1} rectH={r.rect.height:F1} pos={r.anchoredPosition}";

    static void Snapshot(string tag, OverburstGameUI ui)
    {
        var p = PlayerProgression.Current;
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var mask = ui != null ? typeof(OverburstGameUI).GetField("xpFillMask", bf).GetValue(ui) as RectTransform : null;
        var width = ui != null ? (float)typeof(OverburstGameUI).GetField("xpTrackWidth", bf).GetValue(ui) : -1f;
        var text = ui != null ? typeof(OverburstGameUI).GetField("xpText", bf).GetValue(ui) as UnityEngine.UI.Text : null;
        var percentField = typeof(OverburstGameUI).GetField("xpPercentText", bf);
        var percent = ui != null && percentField != null ? percentField.GetValue(ui) as UnityEngine.UI.Text : null;
        lines.Add($"[{tag}] level={(p ? p.Level : -1)} exp={(p ? p.Experience : -1)} toNext={(p ? p.ExperienceToNext : -1)} progress={(p ? p.ExperienceProgress : -1f):F3} trackWidth={width:F1} maskSizeDelta={(mask ? mask.sizeDelta.ToString() : "null")} maskRectW={(mask ? mask.rect.width.ToString("F1") : "-")} text='{(text ? text.text : "null")}' percent='{(percent ? percent.text : "null")}' route={Overburst.Persistence.AccountGameplaySession.ShouldRoute}");
    }

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            yield return Wait(1f);
            var ui = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>(FindObjectsInactive.Include);
            lines.Add("OverburstGameUI found=" + (ui != null));
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var mask = ui != null ? typeof(OverburstGameUI).GetField("xpFillMask", bf).GetValue(ui) as RectTransform : null;
            var xpBar = mask != null ? mask.parent != null ? mask.parent.parent : null : null;
            if (xpBar != null)
            {
                lines.Add("XP Bar active=" + xpBar.gameObject.activeInHierarchy);
                foreach (var t in xpBar.GetComponentsInChildren<RectTransform>(true))
                {
                    var comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is RectTransform)).Select(c =>
                        c is UnityEngine.UI.Image img ? $"Image(type={img.type},fill={img.fillAmount:F2},method={img.fillMethod},enabled={img.enabled})" : c.GetType().Name));
                    lines.Add("  " + Rect(t) + " active=" + t.gameObject.activeSelf + " [" + comps + "]");
                }
            }
            Snapshot("start", ui);
            Shot("01_start");
            var p = PlayerProgression.Current;
            // 1) 직접 지급: 다음 레벨까지의 절반
            int half = Mathf.Max(1, p.ExperienceToNext / 2);
            p.AddExperience(half);
            Snapshot("after AddExperience(" + half + ") same frame", ui);
            yield return Wait(1.6f);
            Snapshot("after AddExperience +1.6s", ui);
            Shot("02_half");
            // 2) 몬스터 처치(하이드아웃 시험장)
            var themeUi = EnemyThemeTrialHarness.Current; if (!themeUi.InArena) themeUi.ToggleArena();
            yield return Wait(0.8f);
            var player = PlayerInputFacade.Current;
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var table in themeUi.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
            var def = themeUi.tables.SelectMany(x => x.Entries).Select(x => x.definition).Where(x => x != null).First(x => x.EnemyId == "CavernMutants_Ceratoferox");
            if (!spawn.TrySpawn(new EnemySpawnRequest(def, player.transform.position + player.transform.forward * 2.5f, Quaternion.identity, player.transform), out var e)) throw new Exception("spawn");
            leased.Add(e); e.AI.enabled = false;
            yield return Wait(0.5f);
            var rank = e.GetComponent<EnemyRank>();
            lines.Add($"enemy level={(rank ? rank.Level : -1)} canGrantRewards={(rank ? rank.Encounter.CanGrantRewards : false)}");
            int before = p.Experience; int levelBefore = p.Level;
            e.Health.TakeDamage(new DamageInfo(1000000f, e.transform.position + Vector3.up, player.gameObject, player.transform.forward, playerAttackKind: PlayerAttackKind.Weak));
            yield return Wait(1.6f);
            Snapshot("after arena kill (exp before=" + before + " level before=" + levelBefore + ")", ui);
            Shot("03_kill");
            // 3) 레벨업 넘기기
            p.AddExperience(p.ExperienceToNext + 5);
            yield return Wait(1.6f);
            Snapshot("after level-up grant", ui);
            Shot("04_levelup");
            // 4) 마우스 오버 설명창: 바 가운데를 레이캐스트해 바가 맨 위인지 보고, 들어가기/나가기 이벤트를 보낸다.
            var tipComp = xpBar != null ? xpBar.GetComponent<ExperienceBarTooltip>() : null;
            var tipObj = xpBar != null ? xpBar.Find("Tooltip") : null;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (tipComp == null || tipObj == null || es == null) lines.Add($"hover setup: comp={(tipComp != null)} tooltip={(tipObj != null)} eventSystem={(es != null)}");
            else
            {
                var canvas = xpBar.GetComponentInParent<Canvas>().rootCanvas;
                var rt = (RectTransform)xpBar; var corners = new Vector3[4]; rt.GetWorldCorners(corners);
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, (corners[0] + corners[2]) * .5f);
                var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screen };
                var hits = new List<UnityEngine.EventSystems.RaycastResult>(); es.RaycastAll(ped, hits);
                lines.Add($"raycast at {screen}: " + string.Join(" > ", hits.Take(4).Select(h => h.gameObject.name)));
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hits.Count > 0 ? hits[0].gameObject : xpBar.gameObject, ped, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                yield return Wait(0.6f);
                var tipRect = (RectTransform)tipObj; tipRect.GetWorldCorners(corners);
                lines.Add($"hover shown={tipComp.IsShown} active={tipObj.gameObject.activeInHierarchy} tooltipScreenY={corners[0].y:F0}..{corners[1].y:F0} x={corners[0].x:F0}..{corners[2].x:F0}");
                Snapshot("hover", ui);
                Shot("05_hover");
                yield return null; yield return null;
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hits.Count > 0 ? hits[0].gameObject : xpBar.gameObject, ped, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                yield return null;
                lines.Add($"after exit shown={tipComp.IsShown} active={tipObj.gameObject.activeInHierarchy}");
            }
        }
        finally { if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); }
    }
}
