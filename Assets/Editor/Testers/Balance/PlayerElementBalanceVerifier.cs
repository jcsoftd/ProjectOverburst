using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 제품 공격 진입점·실제 피해/충전·빛 후속 타격을 사용한다. 체감과 Player 성능 측정은 별도다.
[InitializeOnLoad]
public static class PlayerElementBalanceVerifier
{
    const string Key = "PlayerElementBalanceVerifier";
    const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    static readonly List<object> results = new List<object>();
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static IEnumerator work;
    static int frame;
    static double deadline;
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static PlayerElementBalanceVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        // 도메인 재로딩·중단 뒤에도 소유 반환을 이어 간다.
        if (SessionState.GetBool(Key + ".running", false) && Status == "RUNNING")
        {
            SessionState.SetString(Key + ".status", "INTERRUPTED Domain reload");
            SessionState.SetBool(Key + ".return", true);
            SessionState.SetBool(Key + ".abort", true);
        }
        if (SessionState.GetBool(Key + ".return", false) || SessionState.GetBool(Key + ".running", false))
            EditorApplication.update += ReturnWhenIdle;
    }
    static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
    static void Near(float a, float b, string label, float tolerance = .0001f) => Check(Mathf.Abs(a - b) <= tolerance, label + $" ({a}/{b})");

    public static string VerifyEditMode()
    {
        PlayerElementBalanceBuilder.RequireIdle(); checks.Clear(); results.Clear(); errors.Clear();
        var t = OverburstElementTuning.Current;
        Check(AssetDatabase.GetAssetPath(t) == PlayerElementBalanceBuilder.AssetPath, "현재 Resource SO 연결과 Missing Script 없음");
        Near(OverburstCombatBalance.GreatswordWeakDamage, .65f, "약공 기본");
        Near(OverburstCombatBalance.FullHeavyDamage, 1.7f, "완충 기본");
        Near(t.dischargeDamageAtFullEnergy, 2f, "에너지 증폭 유지");
        float[] expected = { .3f, 1.0375f, 2.125f, 3.5625f, 5.35f };
        for (int i = 0; i < expected.Length; i++)
        {
            float e = i / 4f;
            Near(CombatBalanceFormulas.HeavyFirstBlastDamage(100f, e, 2f * e, 25f) / 100f, expected[i], "H/D e=" + e);
        }
        Near(CombatBalanceFormulas.LightTripleHitScale(t, 0, 100, 1), 1.8f, "광휘100 첫 타격");
        Near(CombatBalanceFormulas.LightTripleHitScale(t, 1, 100, 1), .7f, "과충전1 중간 타격");
        Near(CombatBalanceFormulas.LightTripleHitScale(t, 1, 0, 0), .65f, "2타형 첫 타격");
        Near(CombatBalanceFormulas.LightTripleHitScale(t, 2, 100, 1), .8f, "마지막 타격");
        Near(CombatBalanceFormulas.LightTripleHitScale(t, 0, 5, .1f), .28f, "조기3타 광휘5 경계 유지");
        Near(CombatBalanceFormulas.PhaseEnergyGain(t, false, 0), 10f, "일반 충전10 유지");
        Near(CombatBalanceFormulas.PhaseEnergyGain(t, true, 0), 20f, "치명 충전20 유지");
        Near(t.shatterBlastFraction, .6f, "얼음 쇄빙 계수 유지");
        Near(t.darkBarrageShotDamage, .08f, "어둠 탄 계수 유지");
        var fallback = ScriptableObject.CreateInstance<OverburstElementTuning>();
        try
        {
            Near(fallback.SafeLightTripleHit1PerStack, 1.6f, "fallback 기본 광휘");
            fallback.lightTripleHit1PerStack = 0; fallback.lightTripleHit2Base = 0;
            fallback.lightTripleHit2PerOvercharge = 0; fallback.lightTripleHit3Scale = 0;
            Near(fallback.SafeLightTripleHit1PerStack, 1.6f, "fallback 유효성 광휘");
            Near(fallback.SafeLightTripleHit2Base, .65f, "fallback 중간 기본");
            Near(fallback.SafeLightTripleHit2PerOvercharge, .05f, "fallback 과충전 추가");
            Near(fallback.SafeLightTripleHit3Scale, .8f, "fallback 마지막");
        }
        finally { UnityEngine.Object.DestroyImmediate(fallback); }
        Directory.CreateDirectory(PlayerElementBalanceBuilder.OutputRoot);
        string file = Path.Combine(PlayerElementBalanceBuilder.OutputRoot, "edit-results.json");
        File.WriteAllText(file, JsonConvert.SerializeObject(new { status = "PASS", checks }, Formatting.Indented));
        return file;
    }

    public static string StartPlay(int run = 1)
    {
        PlayerElementBalanceBuilder.RequireIdle();
        if (SessionState.GetBool(Key + ".running", false) || SessionState.GetBool(Key + ".return", false)) throw new InvalidOperationException("본인 검증 또는 반환이 남아 있습니다.");
        string output = Path.Combine(PlayerElementBalanceBuilder.OutputRoot, "Play" + run);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetInt(Key + ".pid", System.Diagnostics.Process.GetCurrentProcess().Id);
        SessionState.SetString(Key + ".project", Application.dataPath);
        SessionState.SetString(Key + ".start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + ".sceneBefore", SceneSnapshot());
        SessionState.SetBool(Key + ".running", true);
        SessionState.SetString(Key + ".status", "STARTING");
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        SessionState.SetFloat(Key + ".returnUntil", (float)(EditorApplication.timeSinceStartup + 420));
        SessionState.SetFloat(Key + ".startUntil", (float)(EditorApplication.timeSinceStartup + 20));
        EditorApplication.update -= ReturnWhenIdle; EditorApplication.update += ReturnWhenIdle;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount")); }
        catch { SessionState.SetString(Key + ".status", "FAIL Start"); SessionState.SetBool(Key + ".return", true); throw; }
        return output;
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + ".running", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); results.Clear(); errors.Clear(); stack.Clear(); frame = -1;
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetInt(Key + ".fps", Application.targetFrameRate);
            Application.runInBackground = true; Application.targetFrameRate = 60;
            SessionState.SetString(Key + ".status", "RUNNING"); deadline = EditorApplication.timeSinceStartup + 240;
            work = VerifyPlay(); EditorApplication.update += Tick; Application.logMessageReceived += Log;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Application.targetFrameRate = SessionState.GetInt(Key + ".fps", -1);
            if (Status == "RUNNING") SessionState.SetString(Key + ".status", "INTERRUPTED");
            SessionState.SetBool(Key + ".return", true);
        }
        if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key + ".return", true);
    }
    static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            CheckDeadline();
            if (work != null) { stack.Push(work); work = null; }
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return; }
                stack.Pop(); (top as IDisposable)?.Dispose();
            }
            Check(errors.Count == 0, "신규 Play 오류0"); Finish("PASS");
        }
        catch (Exception e) { errors.Add(e.ToString()); Finish("FAIL"); }
    }
    static void CheckDeadline() { if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("전투 검증 시간 초과"); }
    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, checks, results, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    static string SceneSnapshot() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    {
        var s = SceneManager.GetSceneAt(i);
        return new { s.path, s.isLoaded, s.isDirty, active = s == SceneManager.GetActiveScene(), roots = s.GetRootGameObjects().Select(g => g.name).OrderBy(n => n).ToArray() };
    }));
    static void ReturnWhenIdle()
    {
        if (!SessionState.GetBool(Key + ".running", false) && !SessionState.GetBool(Key + ".return", false)) { EditorApplication.update -= ReturnWhenIdle; return; }
        if (SessionState.GetBool(Key + ".abort", false) && EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            string ownPath = Path.GetFullPath(Path.Combine(Output, "IsolatedAccount"));
            string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
            if (string.Equals(current, ownPath, StringComparison.OrdinalIgnoreCase)
                && System.Diagnostics.Process.GetCurrentProcess().Id == SessionState.GetInt(Key + ".pid", -1)
                && Application.dataPath == SessionState.GetString(Key + ".project", ""))
            { SessionState.EraseBool(Key + ".abort"); EditorApplication.ExitPlaymode(); }
            return;
        }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".returnUntil", 0))
        { SessionState.SetString(Key + ".status", "FAIL Return timeout"); EditorApplication.update -= ReturnWhenIdle; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Status == "STARTING" && !SessionState.GetBool(Key + ".return", false))
        {
            if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + ".startUntil", 0)) return;
            SessionState.SetString(Key + ".status", "FAIL Play entry cancelled");
            SessionState.SetBool(Key + ".return", true);
        }
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        string own = Path.GetFullPath(Path.Combine(Output, "IsolatedAccount"));
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if ((!string.IsNullOrEmpty(env) && !string.Equals(Path.GetFullPath(env), own, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(prepared) && !string.Equals(Path.GetFullPath(prepared), own, StringComparison.OrdinalIgnoreCase))) return;
        if (System.Diagnostics.Process.GetCurrentProcess().Id != SessionState.GetInt(Key + ".pid", -1)
            || Application.dataPath != SessionState.GetString(Key + ".project", "")) return;
        try
        {
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            string start = SessionState.GetString(Key + ".start", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            IsolatedSavePlayGuard.UseRealAccount();
            bool scenePreserved = SceneSnapshot() == SessionState.GetString(Key + ".sceneBefore", "");
            bool accountReady = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
                && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
                && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""))
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable));
            File.WriteAllText(Path.Combine(Output, "return-results.json"), JsonConvert.SerializeObject(new { status = scenePreserved && accountReady ? "PASS" : "FAIL", scenePreserved, accountReady, startScene = start, sceneBefore = SessionState.GetString(Key + ".sceneBefore", ""), sceneAfter = SceneSnapshot() }, Formatting.Indented));
            if (!scenePreserved || !accountReady) SessionState.SetString(Key + ".status", "FAIL Return");
            SessionState.EraseBool(Key + ".running"); SessionState.EraseBool(Key + ".return"); SessionState.EraseFloat(Key + ".returnUntil");
            SessionState.EraseBool(Key + ".abort");
            SessionState.EraseFloat(Key + ".startUntil");
            EditorApplication.update -= ReturnWhenIdle;
        }
        catch (Exception e) { SessionState.SetString(Key + ".status", "FAIL Return " + e.Message); EditorApplication.update -= ReturnWhenIdle; }
    }
    static void Set(object target, string property, object value) => target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
    static WeaponActionResult startResult;
    static IEnumerator Start(MeleeRuntime melee, bool heavy)
    {
        float until = Time.time + 2f;
        do
        {
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee.SetManualInputEnabled(true);
            var request = new WeaponActionRequest(WeaponActionSource.PlayerInput, null, Vector3.forward);
            startResult = heavy ? melee.TryStartHeavyAttack(Vector3.forward) : melee.TryStartAction(request, out _);
            if (startResult == WeaponActionResult.Accepted) yield break;
            yield return null;
        } while (Time.time < until);
    }
    static void Warp(PlayerInputFacade p, Vector3 at)
    {
        var cc = p.GetComponent<CharacterController>(); bool enabled = cc != null && cc.enabled;
        if (enabled) cc.enabled = false; p.transform.SetPositionAndRotation(at, Quaternion.identity);
        if (enabled) cc.enabled = true; Physics.SyncTransforms();
    }
    static IEnumerator VerifyPlay()
    {
        EnemySpawnService spawn = null; EnemyActor enemy = null; MeleeRuntime melee = null;
        CombatHealth hooked = null; Action<CombatHealth, DamageInfo> hook = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            Check(string.Equals(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory), Path.GetFullPath(Path.Combine(Output, "IsolatedAccount")), StringComparison.OrdinalIgnoreCase), "격리 계정 부팅");
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            var ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "제품 스폰 서비스");
            foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), "카탈로그 " + error);
            var def = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null && d.EnemyId.Contains("Scolokarck"));
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath);
            melee = player.GetComponent<MeleeRuntime>(); var origin = player.transform.position;
            OverburstElementEnergy energy = null; int seq = 900000;
            foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
            {
                melee.CancelCurrentAction();
                Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: element)), "장착 " + element);
                var gemDefinition = Resources.LoadAll<ElementGemItemData>("Items/ElementGems")
                    .First(g => g.element == element && g.fixedGrade == ItemGrade.Legendary);
                var gem = new ItemData(gemDefinition, 1, gemDefinition.fixedGrade);
                gem.gemState = ElementGemQuality.Roll(gemDefinition, 20261004, ElementGemArchetype.Balanced);
                var inventory = PlayerAccountInventoryService.SharedInventory;
                Check(inventory.AddItem(gem) && ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(gem)), "제품 보석 장착 " + element);
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                Warp(player, origin); yield return null;
                energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
                Check(energy.Element == element && actor.Equipment.ActiveElement == element, "보석 기반 활성 원소 " + element);
                Check(spawn.TrySpawn(new EnemySpawnRequest(def, origin + Vector3.forward * 1.7f, Quaternion.LookRotation(Vector3.back), player.transform), out enemy), "스폰 " + element);
                enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(1000000, true);
                enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var hits = new List<(float time, DamageInfo info)>();
                hook = (h, d) => { if (!d.isDamageOverTime) hits.Add((Time.time, d)); };
                hooked = enemy.Health; hooked.OnDamaged += hook;
                energy.Clear();
                Check(energy.RecordConfirmedHit(energy.WeaponInstanceId, element, ++seq, 1), "일반 Phase 충전 " + element);
                Near(energy.Amount, 10, "일반10 " + element);
                Check(!energy.RecordConfirmedHit(energy.WeaponInstanceId, element, seq, 1), "동일 Phase 중복 제한 " + element);
                Check(energy.RecordConfirmedHit(energy.WeaponInstanceId, element, seq, 1, true), "같은 Phase 후행 치명 승격 " + element);
                Near(energy.Amount, 20, "후행 치명 총20 " + element);
                energy.Clear();
                Check(!energy.TryCommitDischarge(100f, out _), "빈 게이지 원소 방출 없음 " + element);
                for (int i = 0; i < (element == WeaponElement.Light ? 20 : 10); i++) energy.RecordConfirmedHit(energy.WeaponInstanceId, element, ++seq, 1);
                Check(energy.TryCommitDischarge(100f, out var refund), "환급 fixture 방출 " + element);
                Near(refund.FirstBlastDamage, 535f, "방출 실제 H(D100) " + element, .01f);
                Check(refund.TryRefundParried(), "패링 환급 첫1회 " + element);
                Near(energy.Amount, 50f, "환급50·빛 과충전 제외 " + element);
                Check(!refund.TryRefundParried(), "패링 중복 환급 제한 " + element);
                energy.Clear();
                yield return Start(melee, false);
                Check(startResult == WeaponActionResult.Accepted, "제품 약공 시작 " + element + ": " + startResult);
                yield return Wait(1.5f);
                var weak = hits.Where(h => (h.info.playerAttackKind & PlayerAttackKind.Weak) != 0).ToList();
                Check(weak.Count > 0, "약공 실제 적중 " + element);
                Check(energy.Amount > 0, "실제 약공 충전 " + element);
                results.Add(new { element = element.ToString(), weak = weak.Select(h => new { h.info.damage, h.info.isCritical }).ToArray(), energyAfterWeak = energy.Amount });
                while (melee.IsAttackInProgress) yield return null;
                energy.Clear(); hits.Clear(); Warp(player, origin); enemy.transform.position = origin + Vector3.forward * 1.7f; Physics.SyncTransforms();
                int cases = element == WeaponElement.Light ? 2 : 1;
                for (int mode = 0; mode < cases; mode++)
                {
                    energy.Clear(); hits.Clear();
                    // 동기 fixture: 제품 충전 API로100 또는200/광휘100을 만들고 즉시 행동을 시작한다.
                    int phases = element == WeaponElement.Light && mode == 1 ? 30 : 10;
                    for (int i = 0; i < phases; i++) energy.RecordConfirmedHit(energy.WeaponInstanceId, element, ++seq, 1);
                    float before = energy.Amount; int radiance = energy.RadianceStacks;
                    yield return Start(melee, true);
                    Check(startResult == WeaponActionResult.Accepted, "제품 강공 시작 " + element + "/" + mode + ": " + startResult);
                    yield return Wait(4.5f);
                    var heavy = hits.Where(h => (h.info.playerAttackKind & (PlayerAttackKind.Heavy | PlayerAttackKind.Elemental)) != 0).ToList();
                    Check(heavy.Count > 0, "강공 실제 적중 " + element + "/" + mode);
                    Near(energy.Amount, 0, "강공 소비·파생 재충전 없음 " + element + "/" + mode);
                    if (element == WeaponElement.Light)
                    {
                        int expected = mode == 0 ? 2 : 3;
                        Check(heavy.Count == expected, "빛 " + expected + "타 실제 적중");
                        float gap = heavy[1].time - heavy[0].time;
                        Near(gap, LightTripleImpactScheduler.ResolveDelay(mode == 0 ? 2 : 1, mode == 0 ? 1 : 0), "빛 후속 간격", .14f);
                        Check(heavy.Skip(1).All(h => !h.info.isCritical && !h.info.triggersOnHitEffects), "빛 후속 치명·적중 재귀 없음");
                        if (mode == 1) Near(heavy[1].info.damage / heavy[2].info.damage, .7f / .8f, "빛 중간/마지막 배분", .025f);
                        Check(energy.RadianceStacks == 0, "광휘 소비");
                    }
                    results.Add(new { element = element.ToString(), mode, before, radiance, hits = heavy.Select(h => new { h.time, h.info.damage, h.info.isCritical, kind = h.info.playerAttackKind.ToString() }).ToArray() });
                    while (melee.IsAttackInProgress) yield return null;
                    Warp(player, origin); enemy.transform.position = origin + Vector3.forward * 1.7f; Physics.SyncTransforms();
                }
                hooked.OnDamaged -= hook; hooked = null; hook = null;
                if (enemy != null && enemy.IsLeased) spawn.Release(enemy); enemy = null;
                yield return null;
            }
        }
        finally
        {
            if (hooked != null && hook != null) hooked.OnDamaged -= hook;
            if (melee != null) melee.CancelCurrentAction();
            if (enemy != null && enemy.IsLeased && spawn != null) spawn.Release(enemy);
        }
    }
}
