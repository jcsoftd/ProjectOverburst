using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 2026-10-01 상단 대상·보스 HUD 재구성 Play 검증(격리 계정 OVERBURST_SAVE_DIRECTORY, 씬 저장 없음).
// 은신처 시험장에서 일반·정예 몬스터를 실제 강공(MeleeRuntime.TryStartHeavyAttack)으로 때려 대상 HUD 값·잔상·등급 모양·사망 뒤 숨김을 보고,
// 대표 보스 런(암굴 거수왕)에서 보스 HUD 교전 표시·레벨·단계 보석·눈금·마름모 연출·단계 전환·이탈 뒤 숨김을 본다.
[InitializeOnLoad]
public static class TargetHudPlayVerifier
{
    const string Key = "TargetHudPlayVerifier";
    const string Weapon = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> errors = new List<string>(), conditions = new List<string>(), notes = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static TargetHudPlayVerifier() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("PersistentScene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode(); // 저장하지 않은 씬은 종료 때 Unity가 되돌린다
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            Application.runInBackground = true;
            errors.Clear(); conditions.Clear(); notes.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 420;
            work = Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
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
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null);
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout");
            if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                return;
            }
            Finish(errors.Count == 0 && !conditions.Any(c => c.StartsWith("FAIL")) ? "PASS" : "FAIL");
        }
        catch (Exception e) { conditions.Add("FAIL exception: " + e.Message); Finish("FAIL " + e.Message); }
    }

    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, conditions, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Cond(string name, bool pass, string detail = "") => conditions.Add((pass ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " | " + detail : ""));
    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
    static IEnumerator Shot(string name) { yield return null; ScreenCapture.CaptureScreenshot(Path.Combine(Output, name + ".png")); yield return null; yield return null; }

    static string Health(float current, float max) =>
        Mathf.CeilToInt(current).ToString("N0", CultureInfo.InvariantCulture) + " / " + Mathf.CeilToInt(max).ToString("N0", CultureInfo.InvariantCulture);

    static T Child<T>(Component root, string name) where T : Component =>
        root.GetComponentsInChildren<T>(true).FirstOrDefault(c => c.name == name);

    // ---------------- 공용 상태 ----------------
    static PlayerInputFacade player; static PlayerActorRuntime actor; static MeleeRuntime melee;

    static IEnumerator Verify()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        Check(AccountBootstrap.SaveDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Account not isolated");
        yield return Wait(1.5f);
        player = PlayerInputFacade.Current; actor = PlayerContext.GetOrCreate().CurrentActor;
        melee = player.GetComponent<MeleeRuntime>();
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(Weapon);
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Fire)), "Equip fixture weapon");
        notes.Add("screen " + Screen.width + "x" + Screen.height);
        yield return TargetHud();
        yield return BossHud();
    }

    // ---------------- 대상 HUD(일반·정예) ----------------
    static IEnumerator TargetHud()
    {
        var debug = EnemyThemeTrialHarness.Current;
        Check(debug != null, "EnemyThemeTrialHarness");
        if (!debug.InArena) debug.ToggleArena();
        yield return Wait(1f);
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        melee.SetManualInputEnabled(true);
        actor.Health.SetMaxHp(1000000f, true);
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out EnemySpawnService spawn), "Spawn service");
        foreach (var table in debug.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
        var defs = debug.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
        var leased = new List<EnemyActor>();
        try
        {
            EnemyActor normal = null, elite = null;
            Vector3 origin = player.transform.position;
            Vector3 front = player.transform.forward; front.y = 0f; front.Normalize();
            for (int i = 0; i < defs.Length && (normal == null || elite == null) && i < 20; i++)
            {
                Vector3 at = origin + front * 2.3f + Vector3.right * (leased.Count * 30f + 30f); // 쓰지 않을 것은 멀리
                if (!spawn.TrySpawn(new EnemySpawnRequest(defs[i], at, Quaternion.LookRotation(-front), player.transform), out EnemyActor e)) continue;
                leased.Add(e);
                var rank = e.GetComponent<EnemyRank>();
                if (rank == null) continue;
                if (rank.Rank == EnemyRankType.Elite && elite == null) elite = e;
                else if (rank.Rank == EnemyRankType.Normal && normal == null && rank.GradeType == EnemyGradeType.Normal) normal = e;
            }
            Check(normal != null && elite != null, "normal/elite spawn " + (normal != null) + "/" + (elite != null));
            foreach (var e in leased) { if (e.AI != null) e.AI.enabled = false; e.Movement.StopMovement(); }
            var hud = Object.FindFirstObjectByType<EnemyTargetHpHud>(FindObjectsInactive.Include);
            Check(hud != null, "EnemyTargetHpHud");
            yield return HitAndCheck(hud, normal, false, "normal");
            normal.transform.position += Vector3.right * 40f; // 다음 강공에 같이 맞지 않게 치운다
            Physics.SyncTransforms();
            yield return HitAndCheck(hud, elite, true, "elite");

            // 원소 상태 칸: 정예에 불 상태를 2번 걸어 칸·남은 시간 덮개·중첩 수를 본다(지속 피해로 죽지 않게 체력을 먼저 올린다).
            elite.Health.SetMaxHp(3000f, true);
            var energy = actor.Equipment.GetComponent<OverburstElementEnergy>() ?? actor.Equipment.gameObject.AddComponent<OverburstElementEnergy>();
            var status = elite.GetComponent<ElementalStatusController>();
            for (int k = 0; k < 2 && status != null; k++)
                status.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Fire, 100, player.gameObject, energy.WeaponInstanceId, true, false, elite.transform.position, Vector3.forward));
            yield return Wait(0.8f);
            var row = hud.GetComponentInChildren<EnemyTargetStatusRow>(true);
            var cell0 = row != null ? row.transform.Find("Cell 0") : null;
            var sweep = cell0 != null ? cell0.Find("Sweep")?.GetComponent<Image>() : null;
            var stackText = cell0 != null ? cell0.Find("Stack")?.GetComponent<TMP_Text>() : null;
            bool statusOn = status != null && status.TryGetStatus(WeaponElement.Fire, out ElementalStatusSnapshot snap) && snap.IsActive;
            Cond("정예: 원소 상태 칸(아이콘·남은 시간 덮개)", !statusOn || (row.VisibleCount >= 1 && cell0.gameObject.activeSelf && sweep != null && sweep.fillAmount > 0f && sweep.fillAmount < 1f),
                $"statusOn={statusOn} cells={row?.VisibleCount} sweep={sweep?.fillAmount:0.00} stack='{stackText?.text}'");
            yield return Shot("target_elite_status");

            // 사망하면 숨김(마지막 대상이 죽으면 칸을 비운다)
            var body = elite.GetComponent<CombatTarget>();
            elite.Health.TakeDamage(new DamageInfo(elite.Health.CurrentHp + 10f, body.CurrentHurtVolume.Center, player.gameObject, Vector3.forward, playerAttackKind: PlayerAttackKind.Weak));
            yield return Wait(0.4f);
            var group = hud.GetComponent<CanvasGroup>();
            Cond("대상 사망 뒤 HUD 숨김", group != null && group.alpha < 0.01f, "alpha=" + (group != null ? group.alpha : -1f));
        }
        finally
        {
            foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (debug.InArena) debug.ToggleArena();
        }
        yield return Wait(1f);
    }

    static IEnumerator HitAndCheck(EnemyTargetHpHud hud, EnemyActor enemy, bool elite, string label)
    {
        // 적을 플레이어 앞 2.3m로 옮기고 그쪽을 본 채 실제 강공을 낸다.
        Vector3 f = player.transform.forward; f.y = 0f; f.Normalize();
        enemy.transform.position = player.transform.position + f * 2.3f;
        enemy.transform.rotation = Quaternion.LookRotation(-f);
        Physics.SyncTransforms();
        enemy.Health.SetMaxHp(200f, true); // 강공 한 번(약 8~13)이 막대에서 보이는 크기(4~6%)가 되게
        yield return Wait(0.3f);
        float before = enemy.Health.CurrentHp;
        var result = melee.TryStartHeavyAttack(f);
        float until = Time.time + 3f;
        while (Time.time < until && enemy.Health.CurrentHp >= before - 0.01f) yield return null;
        bool realHit = enemy.Health.CurrentHp < before - 0.01f;
        notes.Add($"{label} heavy={result} realHit={realHit} hp {before:0}->{enemy.Health.CurrentHp:0}");
        if (!realHit)
        {
            // 강공이 닿지 않으면(거리·판정) 플레이어 출처 피해로 같은 보고 경로를 태운다. 결과에 기록한다.
            var body = enemy.GetComponent<CombatTarget>();
            enemy.Health.TakeDamage(new DamageInfo(before * 0.35f, body.CurrentHurtVolume.Center, player.gameObject, f, playerAttackKind: PlayerAttackKind.Weak));
        }

        // HUD가 이 대상을 띄운 첫 프레임에 잰다: 잔상(주황)은 맞기 전 체력에서 시작해야 한다.
        float showUntil = Time.time + 1f;
        while (Time.time < showUntil && hud.CurrentTarget != enemy.Health) yield return null;
        var fill = Child<Image>(hud, "Fill"); var trail = Child<Image>(hud, "Trail");
        float trailNow = trail != null ? trail.fillAmount : -1f, fillNow = fill != null ? fill.fillAmount : -1f;
        yield return Shot("target_" + label + "_hit");
        yield return Wait(1.0f);
        var rank = enemy.GetComponent<EnemyRank>();
        var name = Child<TMP_Text>(hud, "NameText"); var level = Child<TMP_Text>(hud, "LevelText");
        var hp = Child<TMP_Text>(hud, "HpText"); var pct = Child<TMP_Text>(hud, "PercentText");
        var glow = hud.transform.Find("EliteGlow"); var bar = hud.transform.Find("Bar") as RectTransform;
        var group = hud.GetComponent<CanvasGroup>();
        float n = enemy.Health.CurrentHp / enemy.Health.MaxHp;
        bool visible = group != null && group.alpha > 0.99f && hud.CurrentTarget == enemy.Health;
        bool nameOk = name != null && name.text.StartsWith(rank.DisplayName) && (name.text.Contains("엘리트") == elite);
        bool values = level != null && level.text == rank.Level.ToString(CultureInfo.InvariantCulture)
            && hp != null && hp.text == Health(enemy.Health.CurrentHp, enemy.Health.MaxHp)
            && fill != null && Mathf.Abs(fill.fillAmount - n) < 0.01f;
        int expectedWidth = elite ? 820 : 640;
        // 2026-10-01: 정예 마름모 주변 빛은 뺐다(사용자 결정). 채움 그림은 막대 폭에 맞춘 것이어야 끝 사선이 테두리와 맞는다.
        bool shape = glow == null && bar != null && Mathf.Abs(bar.sizeDelta.x - expectedWidth) < 0.5f
            && fill != null && fill.sprite != null && Mathf.RoundToInt(fill.sprite.rect.width) == expectedWidth - 8
            && trail != null && trail.sprite == fill.sprite;
        bool glyphs = name != null && name.font != null && name.font.HasCharacters(rank.DisplayName + (elite ? "엘리트" : ""), out uint[] _, true, true);
        Cond($"{label}: 실제 타격 뒤 대상 HUD 표시·대상 일치", visible, $"alpha={(group != null ? group.alpha : -1f)}");
        Cond($"{label}: 이름·등급 표식·레벨·체력 수치·채움", nameOk && values,
            $"name='{name?.text}' level={level?.text}/{rank.Level} hp='{hp?.text}' pct='{pct?.text}' fill={fill?.fillAmount:0.000}/{n:0.000}");
        Cond($"{label}: 등급 모양(막대 폭·폭에 맞춘 채움 그림·주변 빛 없음)·한글 글리프", shape && glyphs,
            $"barW={bar?.sizeDelta.x} fillSpriteW={fill?.sprite?.rect.width} glow={(glow != null)} glyphs={glyphs} font={name?.font?.name}");
        float lost = 1f - fillNow; // 맞기 전은 가득(1.0)
        Cond($"{label}: 잔상이 먼저 남았다가 따라잡음", lost > 0f && trailNow - fillNow >= lost * 0.5f && trail != null && Mathf.Abs(trail.fillAmount - fill.fillAmount) < 0.01f,
            $"직후 trail={trailNow:0.000} fill={fillNow:0.000} 1초 뒤 trail={trail?.fillAmount:0.000}");
        var row = hud.GetComponentInChildren<EnemyTargetStatusRow>(true);
        notes.Add($"{label} statusCells={(row != null ? row.VisibleCount : -1)}");
        yield return Shot("target_" + label + "_settled");
    }

    // ---------------- 보스 HUD ----------------
    static IEnumerator BossHud()
    {
        var flow = PersistentSceneFlow.Instance; var account = AccountGameplaySession.Current;
        while (flow.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var registry = typeof(AccountGameplaySession).GetProperty("ContentRegistry",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(account);
        string contentId = ((AccountContentRegistry)registry).IdFor(definition);
        var map = new MapInstanceState { mapContentId = contentId, level = 1, grade = ItemGrade.Common, monsterThemeId = "CavernMutants" };
        Check(flow.EnterDebugRun(DiamondDungeonWorld.SceneName, map), "Debug run entry " + flow.RunEntryError);
        float until = Time.unscaledTime + 60f;
        while (flow.IsSwitching || WorldSessionState.Phase != WorldPhase.Run) { Check(Time.unscaledTime < until, "Run load timeout"); yield return null; }
        var world = Object.FindFirstObjectByType<DiamondDungeonWorld>();
        Check(world != null && world.RepresentativeBoss != null, "Representative boss");
        foreach (var field in world.Fields) field.enabled = false;
        foreach (var evt in world.EventDirector.Events) evt.enabled = false;
        actor.Health.SetMaxHp(1000000f, true);
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        melee.SetManualInputEnabled(true);
        var boss = world.RepresentativeBoss;
        var phases = boss.BossPhaseController;
        var director = boss.GetComponent<EnemyBossCombatDirector>();
        var hud = Object.FindFirstObjectByType<EnemyBossHudView>(FindObjectsInactive.Include);
        Check(hud != null, "EnemyBossHudView");
        yield return Wait(0.5f);
        Cond("보스 교전 전 HUD 숨김·보스 연결", !hud.IsVisible && hud.BoundBoss == phases, $"visible={hud.IsVisible}");

        // 보스 앞 7m로 이동해 교전을 연다.
        Vector3 fwd = boss.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 p = boss.transform.position + fwd * 7f; p.y = 0.05f;
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false;
        player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(-fwd);
        if (on) cc.enabled = true; Physics.SyncTransforms();
        until = Time.time + 12f;
        while (Time.time < until && !hud.IsVisible) yield return null;
        yield return Wait(0.6f);
        var bossDef = phases.BossDefinition;
        var rank = boss.GetComponent<EnemyRank>();
        var name = Child<TMP_Text>(hud, "BossName"); var level = Child<TMP_Text>(hud, "LevelText");
        var hp = Child<TMP_Text>(hud, "HealthText");
        int count = bossDef != null ? bossDef.PhaseCount : 1;
        int ticks = hud.GetComponentsInChildren<RectTransform>(true).Count(t => t.name.StartsWith("Tick ") && t.gameObject.activeSelf);
        var fxBack = Child<Image>(hud, "FxBack");
        string shaderName = fxBack != null && fxBack.material != null ? fxBack.material.shader.name : "";
        Cond("보스 교전 시 HUD 표시", hud.IsVisible, $"engaged={(director != null && director.IsEngaged)}");
        Cond("보스 이름+보스 표식·레벨·체력 수치", name != null && name.text.Contains("보스") && level != null && level.text == rank.Level.ToString(CultureInfo.InvariantCulture)
            && hp != null && hp.text == Health(boss.Health.CurrentHp, boss.Health.MaxHp), $"name='{name?.text}' level={level?.text}/{rank.Level} hp='{hp?.text}'");
        Cond("단계 보석(남은 단계)·경계 눈금", hud.DisplayedLitPhaseGems == count - phases.CurrentPhaseIndex && ticks == count - 1,
            $"lit={hud.DisplayedLitPhaseGems} count={count} phase={phases.CurrentPhaseIndex} ticks={ticks}");
        Cond("마름모 연출 = 보스 정의 EmblemFx·셰이더", bossDef != null && hud.DisplayedEmblemFx == bossDef.EmblemFx && shaderName == "OVERBURST/UI/HUD Boss Emblem Fx",
            $"fx={hud.DisplayedEmblemFx} def={bossDef?.EmblemFx} shader={shaderName}");
        notes.Add("groggyBar=" + (Child<RectTransform>(hud, "GroggyBar")?.gameObject.activeSelf) + " state='" + hud.DisplayedState + "'");
        yield return Shot("boss_engaged");

        // 단계 전환: 다음 단계 임계값 바로 아래까지 플레이어 출처 피해.
        if (count > 1)
        {
            int beforeLit = hud.DisplayedLitPhaseGems;
            float target = bossDef.GetPhase(1).EnterAtOrBelowNormalizedHealth - 0.03f;
            float dmg = boss.Health.CurrentHp - boss.Health.MaxHp * target;
            var body = boss.GetComponent<CombatTarget>();
            boss.Health.TakeDamage(new DamageInfo(dmg, body.CurrentHurtVolume.Center, player.gameObject, -fwd, playerAttackKind: PlayerAttackKind.Weak));
            yield return null; yield return null;
            var trail = Child<Image>(hud, "Trail"); var fill = Child<Image>(hud, "Fill");
            float trailNow = trail.fillAmount, fillNow = fill.fillAmount;
            yield return Shot("boss_after_damage");
            until = Time.time + 8f;
            while (Time.time < until && phases.CurrentPhaseIndex < 1) yield return null;
            yield return Wait(1.2f);
            Cond("보스 피해 잔상·단계 전환 뒤 보석 1개 꺼짐", trailNow > fillNow + 0.004f && phases.CurrentPhaseIndex >= 1 && hud.DisplayedLitPhaseGems == beforeLit - 1,
                $"trail={trailNow:0.000} fill={fillNow:0.000} phase={phases.CurrentPhaseIndex} lit {beforeLit}->{hud.DisplayedLitPhaseGems}");
            yield return Shot("boss_phase2");
        }

        // 이탈하면 보스 HUD가 남지 않는다.
        melee.CancelCurrentAttackState();
        PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon();
        while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        yield return Wait(0.5f);
        Cond("런 이탈 뒤 보스 HUD 숨김", !hud.IsVisible);
    }
}
