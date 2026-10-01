using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// 2026-10-01 대표 보스 격리 계정 Play 검증. 제품 씬(PersistentScene→Hideout→DiamondDungeon01)과 실제 AI·Ability 경로를 쓴다.
// 공격 강제 호출은 하지 않는다. 특정 패턴이 필요할 때만 보스 두뇌에 "다음 선호"를 주고, 시작은 AI가 조건을 통과할 때 한다.
// 격리 계정(OVERBURST_SAVE_DIRECTORY)만 쓰며 씬을 저장하지 않는다. 결과는 PASS/FAIL 조건별로 기록한다.
[InitializeOnLoad]
public static class RepresentativeBossPlayVerifier
{
    const string Key = "RepresentativeBossPlayVerifier";
    const string Weapon = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<object> results = new List<object>();
    static readonly List<string> errors = new List<string>(), conditions = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static RepresentativeBossPlayVerifier() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        SessionState.SetBool(Key + ".cornerOnly", false);
        Start(output);
    }

    // 진단 전용: 레벨 1 런에서 모서리 교착 상황만 재현하고 0.2초마다 AI 거리·상태를 기록한다.
    public static void RunCornerProbe(string output)
    {
        SessionState.SetBool(Key + ".cornerOnly", true);
        Start(output);
    }

    static void Start(string output)
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
            results.Clear(); errors.Clear(); conditions.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 1100;
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
            // 검증 전 값도 다른 테스트 계정일 수 있으므로 종료 뒤에는 실제 계정 경로로 해제한다.
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", "");
            SessionState.EraseString(Key + ".env");
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
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, conditions, results, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Cond(string name, bool pass, string detail = "") => conditions.Add((pass ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " | " + detail : ""));
    static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Output, name + ".png"));

    // ---------------- 공용 상태 ----------------
    static PlayerInputFacade player; static PlayerActorRuntime playerActor; static MeleeRuntime melee; static PlayerParryController parry;
    static DriveInput input;
    static EnemyActor boss; static EnemyBossCombatDirector director; static EnemyBossPhaseController phases; static DiamondDungeonWorld world;
    static readonly List<(float time, EnemyAbilityDefinition ability, float damage)> playerHits = new List<(float, EnemyAbilityDefinition, float)>();
    static float maxPlayerHeight;

    static void OnPlayerDamaged(CombatHealth h, DamageInfo d, float actual, bool fatal)
    {
        if (d.enemyAbility != null) playerHits.Add((Time.time, d.enemyAbility, actual));
    }

    static IEnumerator Verify()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        Check(AccountBootstrap.SaveDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Account not isolated");
        player = PlayerInputFacade.Current; playerActor = PlayerContext.GetOrCreate().CurrentActor;
        melee = player.GetComponent<MeleeRuntime>();
        input = new DriveInput(player);
        try
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(Weapon);
            Check(playerActor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Fire)), "Equip fixture weapon");
            playerActor.Health.OnDamageResolved += OnPlayerDamaged;

            if (SessionState.GetBool(Key + ".cornerOnly", false))
            {
                yield return EnterRun(1, "CavernMutants");
                holdPhaseOne = true;
                yield return CornerProbe();
                // 전체 검증과 같은 순서(원거리→측면→후방→모서리)로 경계 시험을 다시 돌려 교착을 재현한다.
                yield return Boundaries();
                holdPhaseOne = false;
                yield return Abandon();
                yield break;
            }
            // 비교: 등록되지 않은 테마는 기존 캡슐 보스를 유지한다.
            yield return EnterRun(1, "SpiderBrood");
            Cond("다른 테마 캡슐 보스 유지", world.RepresentativeBoss == null && GameObject.Find("CapsuleBoss_Temporary") != null);
            yield return Abandon();

            // 1회차: 레벨 1 전체 전투
            yield return EnterRun(1, "CavernMutants");
            yield return VerifySpawn(1);
            holdPhaseOne = true;
            yield return NaturalPhase(0, 75f, new[] { "A_LeftSwipe", "B_RightSwipe", "C_DoubleSmash", "D_LeapSmash", "E_QuakeStomp" });
            yield return ParryAndDodge();
            yield return Groggy();
            yield return Elements();
            yield return Boundaries();
            holdPhaseOne = false;
            yield return PhaseTransition();
            yield return NaturalPhase(1, 55f, new[] { "A_LeftSwipe", "F_TwinClaw", "C_DoubleSmash", "D_LeapSmash", "E2_GreatQuake" });
            yield return KillAndSettle(1, true);

            // 2회차: 레벨 50, 그로기 중 임계값·전환 중 사망·풀 재대여, 그 뒤 이탈
            yield return EnterRun(50, "CavernMutants");
            yield return VerifySpawn(50);
            yield return EdgeCases();

            // 3·4회차: 99/100 지도 보장 경계
            yield return EnterRun(99, "CavernMutants");
            yield return VerifySpawn(99);
            yield return KillAndSettle(99, false);
            yield return PlayerDeathAfterClear();
            yield return EnterRun(100, "CavernMutants");
            yield return VerifySpawn(100);
            yield return KillAndSettle(100, true);
            Cond("플레이어 몸이 보스 위로 올라가지 않음", maxPlayerHeight < .6f, "maxY=" + maxPlayerHeight.ToString("0.00"));
        }
        finally
        {
            if (playerActor != null) playerActor.Health.OnDamageResolved -= OnPlayerDamaged;
            input?.Dispose(); input = null;
        }
    }

    // ---------------- 런 진입·이탈 ----------------
    static IEnumerator EnterRun(int level, string theme)
    {
        var flow = PersistentSceneFlow.Instance; var account = AccountGameplaySession.Current;
        while (flow.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        // ContentRegistry는 런타임 어셈블리 내부 접근자라 검증기에서는 반사로 읽는다.
        var registry = typeof(AccountGameplaySession).GetProperty("ContentRegistry",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(account);
        string contentId = ((AccountContentRegistry)registry).IdFor(definition);
        var map = new MapInstanceState { mapContentId = contentId, level = level, grade = ItemGrade.Common, monsterThemeId = theme };
        Check(flow.EnterDebugRun(DiamondDungeonWorld.SceneName, map), "Debug run entry " + level + " " + flow.RunEntryError);
        float until = Time.unscaledTime + 60f;
        while (flow.IsSwitching || WorldSessionState.Phase != WorldPhase.Run) { Check(Time.unscaledTime < until, "Run load timeout"); yield return null; }
        world = Object.FindFirstObjectByType<DiamondDungeonWorld>();
        Check(world != null, "World");
        foreach (var field in world.Fields) field.enabled = false; // 보스 전투만 격리: 필드 무리는 끈다
        foreach (var evt in world.EventDirector.Events) evt.enabled = false;
        playerActor.Health.SetMaxHp(1000000f, true);
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        melee.SetManualInputEnabled(true);
        playerHits.Clear();
        boss = world.RepresentativeBoss;
        director = boss != null ? boss.GetComponent<EnemyBossCombatDirector>() : null;
        phases = boss != null ? boss.BossPhaseController : null;
        yield return null;
    }

    static IEnumerator Abandon()
    {
        melee.CancelCurrentAttackState();
        PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon();
        while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        yield return null;
        VerifyNoResidue("이탈");
    }

    static void VerifyNoResidue(string label)
    {
        var hud = Object.FindFirstObjectByType<EnemyBossHudView>(FindObjectsInactive.Include);
        int directors = Object.FindObjectsByType<EnemyBossCombatDirector>(FindObjectsSortMode.None).Count(d => d.isActiveAndEnabled);
        int warnings = Object.FindObjectsByType<EnemyStrongAttackWarning>(FindObjectsSortMode.None).Count(w => w.IsVisible);
        bool ok = EnemyBossEncounterRegistry.ActiveCount == 0 && (hud == null || !hud.IsVisible) && directors == 0 && warnings == 0
            && Mathf.Abs(Time.timeScale - 1f) < .001f;
        Cond(label + " 뒤 잔여 보스·HUD·예고·시간 없음", ok,
            $"registry={EnemyBossEncounterRegistry.ActiveCount} hud={(hud != null && hud.IsVisible)} directors={directors} warnings={warnings} timeScale={Time.timeScale}");
    }

    static IEnumerator VerifySpawn(int level)
    {
        Check(boss != null && director != null, "Representative boss missing at level " + level);
        var rank = boss.GetComponent<EnemyRank>();
        float expected = OverburstCombatBalance.RoundStat(boss.Definition.ReferenceHealthCoefficient * OverburstCombatBalance.ReferenceExpectedHit(level));
        Vector3 corner = DiamondDungeonLayout.InsetPoint(world.BossCorner, 12f);
        float fromCorner = Vector3.Distance(new Vector3(boss.transform.position.x, 0, boss.transform.position.z), corner);
        float fromStart = Vector3.Distance(boss.transform.position, DiamondDungeonLayout.InsetPoint(world.StartCorner, 12f));
        var hud = Object.FindFirstObjectByType<EnemyBossHudView>(FindObjectsInactive.Include);
        bool hudBound = hud != null && hud.BoundBoss == phases;
        bool hudHidden = hud != null && !hud.IsVisible;
        // 교전 전 보스는 기존 AI 대기 배회(행동 프로필 patrolRadius)로 모서리 주변을 조금 걷는다.
        float patrol = boss.AI != null && boss.AI.BehaviorProfile != null ? boss.AI.BehaviorProfile.PatrolRadius : 0f;
        Cond($"Lv{level} 반대 모서리 대표 보스·등급·레벨·체력", rank.GradeType == EnemyGradeType.Boss && rank.Level == level
            && Mathf.Abs(boss.Health.MaxHp - expected) <= 1f && fromCorner <= patrol + .5f && fromStart > 140f && phases.CurrentPhaseIndex == 0,
            $"hp={boss.Health.MaxHp} expected={expected} corner={fromCorner:0.00}/patrol{patrol:0.0} start={fromStart:0.0} phase={phases.CurrentPhaseIndex}");
        Cond($"Lv{level} HUD 연결·교전 전 숨김", hudBound && hudHidden);
        if (level == 1 && hud != null)
        {
            var nameText = hud.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t.name == "BossName");
            var fills = hud.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name == "Fill" || i.name == "GroggyFill").ToArray();
            var reporter = boss.GetComponent<EnemyTargetHpReporter>();
            bool glyphs = nameText != null && nameText.font != null && !string.IsNullOrEmpty(nameText.text)
                && nameText.font.HasCharacters(nameText.text, out uint[] _, true, true);
            bool bars = fills.Length == 2 && fills.All(i => i.sprite != null && i.type == UnityEngine.UI.Image.Type.Filled);
            bool single = reporter == null || !reporter.enabled;
            Cond("HUD 한글 이름 글리프·체력/그로기 막대 실제 채움·일반 대상 체력 칸 중복 없음", glyphs && bars && single,
                $"name='{(nameText != null ? nameText.text : "")}' glyphs={glyphs} bars={bars} reporterOff={single}");
            // 다른 작업의 히트박스 조사(정적 측정)가 거수왕 판정이 일반 거수 값 그대로라고 보고했다. 실제 런타임 판정 부피를 잰다.
            var target = boss.GetComponent<CombatTarget>();
            var volume = target != null ? target.CurrentHurtVolume : default;
            Cond("보스 피격 판정이 몸 배율(1.3)을 따름", target != null && Mathf.Abs(target.VariantHurtScale.y - 1.3f) < .01f,
                $"variantHurtScale={(target != null ? target.VariantHurtScale.ToString("0.00") : "-")} radius={volume.Radius:0.00} halfHeight={volume.HalfHeight:0.00} top={(volume.Center.y + volume.HalfHeight - boss.transform.position.y):0.00}");
        }
        results.Add(new { level, bossHp = boss.Health.MaxHp, expectedHit = OverburstCombatBalance.ReferenceExpectedHit(level),
            smashDamageBudget = boss.AbilityController.AbilitySet.GetAbility(2).ResolveDamage(level), playerEffectiveHp = OverburstCombatBalance.ReferenceEffectiveHealth(level) });
        yield return null;
    }

    // ---------------- 자연 선택 ----------------
    static void Place(float distance, float lateralDegrees = 0f)
    {
        Vector3 forward = boss.transform.forward; forward.y = 0; forward.Normalize();
        Vector3 dir = Quaternion.Euler(0, lateralDegrees, 0) * forward;
        Vector3 p = boss.transform.position + dir * distance; p.y = .05f;
        p = ClampInside(p);
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false;
        player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(-dir);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }

    static Vector3 ClampInside(Vector3 p)
    {
        for (int i = 0; i < 20 && !DiamondDungeonLayout.Contains(p, 3f); i++) p *= .96f;
        return p;
    }

    static void TrackHeight() { if (player != null) maxPlayerHeight = Mathf.Max(maxPlayerHeight, player.transform.position.y); }

    sealed class CommitRecord { public string id; public int phase; public float at; public bool motion; public int hits; public int expectedHits; public bool warning; public bool danger; public float dangerRadius; public float bodyOffset; public float distanceAtStart; }

    static IEnumerator NaturalPhase(int phaseIndex, float seconds, string[] expected)
    {
        var records = new List<CommitRecord>();
        int logStart = director.CommitLog.Count;
        float end = Time.time + seconds; int placement = 0; float nextPlacement = 0f;
        float[] distances = { 2.2f, 2.2f, 7.0f, 3.5f, 2.2f, 6.5f };
        CommitRecord current = null; var bossSet = new HashSet<string>();
        // 대기 자세에서도 골반과 캡슐 중심은 조금 떨어져 있다. 공격이 더한 어긋남만 잰다.
        var idleCol = boss.GetComponentInChildren<CapsuleCollider>();
        var idlePelvis = boss.Animator != null ? boss.Animator.GetComponentInChildren<SkinnedMeshRenderer>()?.rootBone : null;
        float idleOffset = idlePelvis != null && !boss.AbilityController.IsExecuting ? Vector3.Distance(Flat(idlePelvis.position), Flat(idleCol.bounds.center)) : 0f;
        while (Time.time < end)
        {
            TrackHeight();
            HoldHealth(); // 1페이즈 동안 임계값을 넘지 않게 유지(피해 판정·체력 잠금과 무관)
            if (phaseIndex == 1 && boss.Health.NormalizedHp < .25f) boss.Health.Heal(boss.Health.MaxHp * .2f); // 2페이즈 관찰 중 사망 방지
            if (!boss.AbilityController.IsExecuting && Time.time >= nextPlacement)
            {
                Place(distances[placement++ % distances.Length], placement % 3 == 0 ? 25f : 0f);
                nextPlacement = Time.time + 3.5f;
            }
            if (director.CommitLog.Count > logStart + records.Count)
            {
                string entry = director.CommitLog[logStart + records.Count];
                var ability = boss.AbilityController.LastCommittedAbility;
                current = new CommitRecord { id = entry.Split('@')[0], phase = int.Parse(entry.Split('@')[1]), at = Time.time, expectedHits = ability != null ? ability.HitCount : 0,
                    distanceAtStart = Vector3.Distance(Flat(boss.transform.position), Flat(player.transform.position)) };
                records.Add(current); bossSet.Add(current.id);
            }
            if (current != null)
            {
                var ability = boss.AbilityController.LastCommittedAbility;
                if (ability != null && boss.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float n) && n > ability.HitNormalizedTime) current.motion = true;
                current.warning |= boss.GetComponent<EnemyStrongAttackWarning>() != null && boss.GetComponent<EnemyStrongAttackWarning>().IsVisible;
                if (director.IsDangerCueVisible) { current.danger = true; current.dangerRadius = director.DangerCueRadius; }
                var col = boss.GetComponentInChildren<CapsuleCollider>();
                var pelvis = boss.Animator != null ? boss.Animator.GetComponentInChildren<SkinnedMeshRenderer>()?.rootBone : null;
                if (pelvis != null) current.bodyOffset = Mathf.Max(current.bodyOffset, Vector3.Distance(Flat(pelvis.position), Flat(col.bounds.center)) - idleOffset);
                current.hits = playerHits.Count(h => h.time >= current.at && h.ability == ability);
                if (current.id.StartsWith("E") && current.danger && !captured.Contains("danger")) { captured.Add("danger"); Capture("P" + phaseIndex + "_unparryable_quake"); }
                if (current.id == "C_DoubleSmash" && current.warning && !captured.Contains("smash" + phaseIndex)) { captured.Add("smash" + phaseIndex); Capture("P" + phaseIndex + "_parryable_smash"); }
            }
            yield return null;
        }
        while (boss.AbilityController.IsExecuting)
        {
            // 관찰 창 끝에 시작한 공격도 모션 진입을 끝까지 확인한다(Play05의 마지막 도약 기록).
            var tail = boss.AbilityController.LastCommittedAbility;
            if (current != null && tail != null && boss.AnimationBridge.TryGetAttackNormalizedTime(tail.AnimatorTrigger, out float tn) && tn > tail.HitNormalizedTime)
                current.motion = true;
            yield return null;
        }
        foreach (var r in records) r.hits = playerHits.Count(h => h.time >= r.at && h.time <= r.at + 4f && h.ability != null && h.ability.AbilityId.EndsWith(r.id));
        var missing = expected.Where(e => !bossSet.Contains(e)).ToArray();
        var wrongPhase = records.Where(r => !expected.Contains(r.id)).Select(r => r.id).Distinct().ToArray();
        var consecutive = records.Zip(records.Skip(1), (a, b) => a.id == b.id).Count(x => x);
        var badMotion = records.Where(r => !r.motion).Select(r => r.id).ToArray();
        var overHits = records.Where(r => r.hits > r.expectedHits).Select(r => r.id + ":" + r.hits).ToArray();
        var melee = records.Where(r => (r.id.StartsWith("A") || r.id.StartsWith("B") || r.id.StartsWith("C") || r.id.StartsWith("E") || r.id.StartsWith("F")) && r.distanceAtStart < 3f).ToArray();
        var underHits = melee.Where(r => r.hits != r.expectedHits).Select(r => r.id + ":" + r.hits + "/" + r.expectedHits).ToArray();
        var strongWarn = records.Where(r => (r.id.StartsWith("C") || r.id.StartsWith("D")) && !r.warning).Select(r => r.id).ToArray();
        var dangerBad = records.Where(r => r.id.StartsWith("E") && (!r.danger || Mathf.Abs(r.dangerRadius - boss.AbilityController.AbilitySet.GetAbility(4).HitRadius) > .01f)).Select(r => r.id + ":" + r.dangerRadius).ToArray();
        var parryableDanger = records.Where(r => !r.id.StartsWith("E") && r.danger).Select(r => r.id).ToArray();
        Cond($"P{phaseIndex + 1} 모든 패턴 자연 선택", missing.Length == 0, "missing=" + string.Join(",", missing) + " seq=" + string.Join(" ", records.Select(r => r.id.Split('_')[0])));
        Cond($"P{phaseIndex + 1} 페이즈 밖 패턴 없음·같은 패턴 연속 0", wrongPhase.Length == 0 && consecutive == 0, "wrong=" + string.Join(",", wrongPhase) + " repeat=" + consecutive);
        Cond($"P{phaseIndex + 1} 모든 공격 실제 모션 진입", badMotion.Length == 0, string.Join(",", badMotion));
        Cond($"P{phaseIndex + 1} 근거리 타격 횟수=설계 횟수, 초과 0", underHits.Length == 0 && overHits.Length == 0, "under=" + string.Join(",", underHits) + " over=" + string.Join(",", overHits));
        Cond($"P{phaseIndex + 1} 패링 가능 강공 장판·패링 불가 붉은 장판(판정 반경 동일)", strongWarn.Length == 0 && dangerBad.Length == 0 && parryableDanger.Length == 0,
            "strongNoWarn=" + string.Join(",", strongWarn) + " dangerBad=" + string.Join(",", dangerBad) + " wrongDanger=" + string.Join(",", parryableDanger));
        float maxOffset = records.Count > 0 ? records.Max(r => r.bodyOffset) : 0f;
        Cond($"P{phaseIndex + 1} 공격이 더한 몸-판정 어긋남 ≤ 1.0m", maxOffset <= 1.0f, "maxDelta=" + maxOffset.ToString("0.00") + " idle=" + idleOffset.ToString("0.00"));
        if (phaseIndex == 1)
        {
            int follow = 0;
            for (int i = 1; i < records.Count; i++)
                if (records[i - 1].id == "C_DoubleSmash" && records[i].id == "F_TwinClaw" || records[i - 1].id == "D_LeapSmash" && records[i].id == "A_LeftSwipe") follow++;
            Cond("P2 후속 연계(C→F 또는 D→A) 관찰", follow > 0, "count=" + follow);
        }
        results.Add(new { phase = phaseIndex + 1, commits = records.Select(r => new { r.id, r.hits, r.expectedHits, r.motion, r.warning, r.danger, r.dangerRadius, dist = r.distanceAtStart, r.bodyOffset }) });
    }
    static readonly HashSet<string> captured = new HashSet<string>();
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    static Rect WorldRect(RectTransform t)
    {
        var c = new Vector3[4]; t.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    // 원하는 패턴을 다음으로 선호시키고(조건은 그대로 검사) 실제 시작까지 기다린다.
    static IEnumerator WaitCommit(string id, float distance, float timeout, Action<float> onCommitted)
    {
        float until = Time.time + timeout; int start = director.CommitLog.Count;
        while (Time.time < until)
        {
            TrackHeight();
            HoldHealth();
            if (!boss.AbilityController.IsExecuting && !director.IsLockedOut)
            {
                if (director.CurrentDecisionId != id) director.PreferNextPattern(id);
                // 강공이 빈도 규칙으로 잠겨 있으면 가까이 서서 비강공을 먼저 받게 둔다(원거리에는 비강공이 없어 잠금이 안 풀린다).
                var wanted = director.Profile.Find(id)?.ability;
                float want = wanted != null && wanted.IsTelegraphedStrongAttack && boss.AbilityController.IsStrongAttackLocked ? 2.2f : distance;
                float now = Vector3.Distance(Flat(player.transform.position), Flat(boss.transform.position));
                if (now > want + 1.5f || now < want - 1.5f) Place(want);
            }
            for (int i = start; i < director.CommitLog.Count; i++)
                if (director.CommitLog[i].StartsWith(id + "@")) { onCommitted(Time.time); yield break; }
            yield return null;
        }
        onCommitted(-1f);
    }

    static float FirstImpact(float committedAt) =>
        committedAt + boss.AbilityController.LastCommittedAbility.ResolveFirstImpactTime(boss.Melee.AbilityAnimationSpeed);

    static IEnumerator Heavy(Vector3 toward)
    {
        float guard = Time.unscaledTime + 6f;
        while (melee.IsHeavyAttackInProgress) { Check(Time.unscaledTime < guard, "Heavy stalled"); yield return null; }
        Vector3 dir = toward - player.transform.position; dir.y = 0;
        var result = melee.TryStartHeavyAttack(dir.sqrMagnitude > .01f ? dir.normalized : player.transform.forward);
        heavyAccepted = result == WeaponActionResult.Accepted;
        if (parry == null) parry = player.GetComponent<PlayerParryController>();
    }
    static bool heavyAccepted;

    static bool holdPhaseOne;
    static void HoldHealth() { if (holdPhaseOne && boss != null && !boss.Health.IsDead) boss.Health.Heal(boss.Health.MaxHp); }

    static IEnumerator WaitUntil(float time)
    {
        float guard = Time.unscaledTime + Mathf.Max(0f, time - Time.time) * 2f + 5f;
        while (Time.time < time) { Check(Time.unscaledTime < guard, "WaitUntil stalled"); TrackHeight(); HoldHealth(); yield return null; }
    }

    static IEnumerator WaitIdle(float extra)
    {
        float guard = Time.unscaledTime + 15f;
        while (boss.AbilityController.IsExecuting || director.IsLockedOut || melee.IsHeavyAttackInProgress)
        { Check(Time.unscaledTime < guard, $"WaitIdle stalled exec={boss.AbilityController.IsExecuting} locked={director.IsLockedOut} heavy={melee.IsHeavyAttackInProgress} trans={director.IsTransitioning}"); HoldHealth(); yield return null; }
        yield return WaitUntil(Time.time + extra);
    }

    // ---------------- 패링·회피 ----------------
    static IEnumerator ParryAndDodge()
    {
        yield return WaitIdle(.5f);
        // 정면/후방 패링 성공(360° 내려찍기)
        foreach (float side in new[] { 0f, 180f })
        {
            float at = -1f;
            yield return WaitCommit("C_DoubleSmash", 2.2f, 25f, t => at = t);
            Check(at > 0f, "Smash not selected for parry test");
            if (side > 0f) Place(2.0f, side);
            float impact = FirstImpact(at);
            parry = player.GetComponent<PlayerParryController>();
            int before = parry != null ? parry.SuccessCount : 0; int parries = director.ParryCount; float g0 = director.Groggy01;
            yield return WaitUntil(impact - .45f);
            int hitsBefore = playerHits.Count;
            yield return Heavy(boss.transform.position);
            yield return null;
            parry = player.GetComponent<PlayerParryController>();
            bool success = parry != null && parry.SuccessCount == before + 1 && director.ParryCount == parries + 1;
            bool cancelled = !boss.AbilityController.IsExecuting && boss.GetComponent<EnemyMovementReaction>().IsParryStunned;
            if (side == 0f) Capture("parry_success_boss");
            yield return WaitUntil(Time.time + 1.6f);
            int extra = playerHits.Skip(hitsBefore).Count(h => h.ability == boss.AbilityController.LastCommittedAbility);
            Cond($"패링 성공({(side == 0f ? "정면" : "후방")})·취소 뒤 추가 피해 0·그로기 적립", success && cancelled && extra == 0 && director.Groggy01 > g0,
                $"success={success} cancelled={cancelled} extraHits={extra} groggy={director.Groggy01:0.00}");
            yield return WaitIdle(.4f);
        }
        // 이른 입력은 실패하고 피해를 받는다
        {
            float at = -1f; yield return WaitCommit("C_DoubleSmash", 2.2f, 30f, t => at = t);
            Check(at > 0f, "Smash not selected for early parry");
            float impact = FirstImpact(at); int before = parry.SuccessCount; int hits = playerHits.Count;
            yield return WaitUntil(impact - 1.35f);
            yield return Heavy(boss.transform.position);
            yield return WaitUntil(impact + .6f);
            int landed = playerHits.Skip(hits).Count(h => h.ability == boss.AbilityController.LastCommittedAbility);
            Cond("이른 강공 입력은 패링 실패·피해 1회", parry.SuccessCount == before && landed == 1, "hits=" + landed);
            yield return WaitIdle(.4f);
        }
        // 패링 불가 발구르기: 강공으로 받아쳐도 실패, 범위 경계 안/밖, 구르기 무적
        foreach (var mode in new[] { "parry", "inside", "outside", "roll" })
        {
            float at = -1f; yield return WaitCommit("E_QuakeStomp", 2.5f, 35f, t => at = t);
            Check(at > 0f, "Quake not selected: " + mode);
            var ability = boss.AbilityController.LastCommittedAbility;
            float radius = EnemyAttackThreatGeometry.ResolveRadius(boss, ability);
            if (mode == "inside") Place(radius - .6f, 90f);
            if (mode == "outside") Place(radius + 1.0f, 90f);
            float impact = FirstImpact(at); int before = parry.SuccessCount; int hits = playerHits.Count;
            if (mode == "parry") { yield return WaitUntil(impact - .45f); yield return Heavy(boss.transform.position); }
            if (mode == "roll") { yield return WaitUntil(impact - .14f); input.Roll = true; yield return null; yield return null; input.Roll = false; }
            yield return WaitUntil(impact + .6f);
            int landed = playerHits.Skip(hits).Count(h => h.ability == ability);
            bool pass = mode == "parry" ? parry.SuccessCount == before && landed == 1
                : mode == "inside" ? landed == 1 : landed == 0;
            Cond("패링 불가 광역 " + mode, pass, $"hits={landed} radius={radius:0.0} parries={parry.SuccessCount - before}");
            yield return WaitIdle(.4f);
        }
        // 도약 강타 원거리 패링·구르기
        foreach (var mode in new[] { "parry", "roll" })
        {
            float at = -1f; yield return WaitCommit("D_LeapSmash", 7f, 35f, t => at = t);
            Check(at > 0f, "Leap not selected: " + mode);
            var ability = boss.AbilityController.LastCommittedAbility;
            float impact = FirstImpact(at); int before = parry.SuccessCount; int hits = playerHits.Count;
            if (mode == "parry") { yield return WaitUntil(impact - .35f); yield return Heavy(boss.transform.position); }
            else { yield return WaitUntil(impact - .14f); input.Roll = true; yield return null; yield return null; input.Roll = false; }
            yield return WaitUntil(impact + .8f);
            int landed = playerHits.Skip(hits).Count(h => h.ability == ability);
            bool pass = mode == "parry" ? parry.SuccessCount == before + 1 && landed == 0 : landed == 0;
            Cond("도약 강타 " + mode, pass, $"hits={landed} parries={parry.SuccessCount - before}");
            yield return WaitIdle(.4f);
        }
    }

    // ---------------- 그로기 ----------------
    static IEnumerator Groggy()
    {
        int safety = 0;
        while (!director.IsGroggy && safety++ < 6)
        {
            float at = -1f; yield return WaitCommit("C_DoubleSmash", 2.2f, 30f, t => at = t);
            if (at < 0f) break;
            yield return WaitUntil(FirstImpact(at) - .45f);
            yield return Heavy(boss.transform.position);
            yield return null; yield return null;
        }
        Cond("반복 패링으로 그로기 진입", director.IsGroggy, "parries=" + director.ParryCount + " groggyCount=" + director.GroggyCount);
        if (!director.IsGroggy) yield break;
        float started = Time.time;
        yield return WaitUntil(Time.time + .5f);
        Capture("groggy");
        var hud = Object.FindFirstObjectByType<EnemyBossHudView>(FindObjectsInactive.Include);
        bool hudState = hud != null && hud.IsVisible && hud.DisplayedState == "그로기";
        // 받는 피해 ×1.4(기존 패링 기절 보너스) 확인
        float hp0 = boss.Health.CurrentHp;
        boss.Health.TakeDamage(new DamageInfo(100f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 7001, playerAttackKind: PlayerAttackKind.Weak));
        float groggyDamage = hp0 - boss.Health.CurrentHp;
        bool blocked = !boss.AbilityController.TryStart(player.transform);
        float groggyGauge = director.Groggy01;
        while (director.IsGroggy) yield return null;
        float duration = Time.time - started;
        float hp1 = boss.Health.CurrentHp;
        boss.Health.TakeDamage(new DamageInfo(100f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 7002, playerAttackKind: PlayerAttackKind.Weak));
        float normalDamage = hp1 - boss.Health.CurrentHp;
        // 보호 중에는 강공 적립 0
        boss.Health.TakeDamage(new DamageInfo(10f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 7003, playerAttackKind: PlayerAttackKind.Heavy));
        bool protectedOk = director.IsProtected && director.Groggy01 < .001f;
        int commitsBefore = director.CommitLog.Count; float resumeUntil = Time.time + 12f; Place(2.2f);
        while (director.CommitLog.Count == commitsBefore && Time.time < resumeUntil) { TrackHeight(); yield return null; }
        Cond("그로기 4.5초·공격 차단·HUD 표시·피해 ×1.4·보호·재행동", Mathf.Abs(duration - 4.5f) < .35f && blocked && hudState
            && Mathf.Abs(groggyDamage / Mathf.Max(1f, normalDamage) - 1.4f) < .06f && protectedOk && director.CommitLog.Count > commitsBefore,
            $"duration={duration:0.00} blocked={blocked} hud={hudState} ratio={groggyDamage / Mathf.Max(1f, normalDamage):0.00} protected={protectedOk} resumed={director.CommitLog.Count > commitsBefore}");
        yield return WaitUntil(Time.time + 8.2f); // 보호 종료
        // 같은 강공 순번 중복 적립 금지
        float g0 = director.Groggy01;
        for (int i = 0; i < 3; i++)
            boss.Health.TakeDamage(new DamageInfo(5f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 7100, playerAttackKind: PlayerAttackKind.Heavy));
        float g1 = director.Groggy01;
        Cond("한 강공 여러 적중은 그로기 1회만 적립", Mathf.Abs((g1 - g0) * 100f - 10f) < .6f, $"gain={(g1 - g0) * 100f:0.0}");
        results.Add(new { groggyDuration = duration, groggyDamage, normalDamage, parries = director.ParryCount });
    }

    // ---------------- 원소 상태 ----------------
    static IEnumerator Elements()
    {
        yield return WaitIdle(.3f);
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(Weapon);
        var status = boss.GetComponent<ElementalStatusController>();
        var details = new List<string>(); bool ok = true;
        foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
        {
            Check(playerActor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: element)), "Equip " + element);
            Place(2.4f);
            int commits = director.CommitLog.Count;
            float hp0 = boss.Health.CurrentHp;
            for (int i = 0; i < 3; i++) { yield return Heavy(boss.transform.position); while (melee.IsHeavyAttackInProgress) yield return null; boss.Health.Heal(boss.Health.MaxHp * .3f); }
            float until = Time.time + 8f;
            while (director.CommitLog.Count == commits && Time.time < until) { TrackHeight(); yield return null; }
            bool acted = director.CommitLog.Count > commits;
            float speed = status != null ? status.MoveSpeedMultiplier : 1f;
            bool alive = !boss.Health.IsDead;
            // 강공 적중은 그로기를 정상 적립한다. 영구 기절이 아닌지는 "8초 안 재행동"으로 판정하고 그로기는 기록만 한다.
            ok &= acted && alive && speed > .8f;
            details.Add($"{element}:acted={acted} speed={speed:0.00} alive={alive} groggyCount={director.GroggyCount}");
            boss.Health.Heal(boss.Health.MaxHp);
            yield return WaitIdle(.2f);
        }
        Check(playerActor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Fire)), "Re-equip fire");
        Cond("5원소 강공 뒤 보스 이동·행동 유지(빙결 면제·영구 기절 없음)", ok, string.Join(" ", details));
    }

    // ---------------- 거리·경계 교착 ----------------
    static IEnumerator Boundaries()
    {
        var details = new List<string>(); bool ok = true;
        foreach (var (label, dist, lateral) in new[] { ("원거리12m", 12f, 0f), ("측면", 2.4f, 90f), ("후방", 2.4f, 180f), ("모서리", 0f, 0f) })
        {
            yield return WaitIdle(.3f);
            if (label == "모서리")
            {
                Vector3 p = DiamondDungeonLayout.InsetPoint(world.BossCorner, 3.5f); p.y = .05f;
                var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.position = p; cc.enabled = true; Physics.SyncTransforms();
            }
            else Place(dist, lateral);
            int commits = director.CommitLog.Count; float until = Time.time + 14f, nextSample = 0f;
            var samples = new List<string>();
            while (director.CommitLog.Count == commits && Time.time < until)
            {
                TrackHeight(); boss.Health.Heal(boss.Health.MaxHp);
                // 2026-10-01 Play08: 모서리에서 10.9m 접근 상태로 14초 교착. 원인을 추측하지 않도록 0.2초마다 이동 차단 근거를 남긴다.
                if (Time.time >= nextSample) { nextSample = Time.time + .2f; samples.Add($"{label} t={14f - (until - Time.time):0.0} {BossDiag()}"); }
                yield return null;
            }
            samples.Add($"{label} RESULT {(director.CommitLog.Count > commits ? director.CommitLog[director.CommitLog.Count - 1] : "none")}");
            File.AppendAllLines(Path.Combine(Output, "boundaries-trace.txt"), samples);
            bool acted = director.CommitLog.Count > commits;
            bool inside = DiamondDungeonLayout.Contains(boss.transform.position, 0f);
            ok &= acted && inside;
            string why = acted ? "" : $" ai={boss.AI?.CurrentDebugStateName} dist={Vector3.Distance(Flat(boss.transform.position), Flat(player.transform.position)):0.0}"
                + $" strongLock={boss.AbilityController.IsStrongAttackLocked} decision={director.CurrentDecisionId} locked={director.IsLockedOut}";
            details.Add($"{label}:{(acted ? director.CommitLog[director.CommitLog.Count - 1] : "none")} inside={inside} t={14f - (until - Time.time):0.0}{why}");
        }
        Cond("근·원거리·측후방·경계 모서리에서 패턴 교착 없음", ok, string.Join(" ", details));
    }

    // 이동이 멈춘 이유를 가리는 진단값: 애니메이션 차단(도발·피격 등), 동작 잠금, 경직, 현재 클립, 목적지까지 거리.
    static string BossDiag()
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var movement = boss.Movement;
        var bridge = boss.AnimationBridge;
        var animator = boss.GetComponentInChildren<Animator>();
        var clips = animator != null ? animator.GetCurrentAnimatorClipInfo(0) : null;
        string clip = clips != null && clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "-";
        var destinationField = typeof(EnemyMovement).GetField("destination", flags);
        string destination = movement != null && movement.HasDestination && destinationField != null
            ? Vector3.Distance(Flat((Vector3)destinationField.GetValue(movement)), Flat(boss.transform.position)).ToString("0.00")
            : "-";
        Vector3 aim = boss.AbilityController.ResolveAimPosition(player.transform);
        return $"ai={boss.AI?.CurrentDebugStateName} dist={Vector3.Distance(Flat(boss.transform.position), Flat(player.transform.position)):0.00}"
            + $" pos=({boss.transform.position.x:0.0},{boss.transform.position.z:0.0}) dest={destination}"
            + $" mode={(movement != null ? movement.LocomotionMode.ToString() : "?")} hasDest={(movement != null && movement.HasDestination)}"
            + $" block={(bridge != null && bridge.IsBlockingActionActive)} allows={(bridge == null || movement == null || bridge.AllowsMovement(movement.LocomotionMode))}"
            + $" actLock={(movement != null && movement.IsActionLocked)} stun={(boss.AI?.MovementReaction != null && boss.AI.MovementReaction.IsHitStunActive)}"
            + $" clip={clip} decision={director.CurrentDecisionId} strongLock={boss.AbilityController.IsStrongAttackLocked} lockedOut={director.IsLockedOut}"
            + $" angle={Vector3.Angle(Flat(boss.transform.forward), Flat(aim - boss.transform.position)):0.0}";
    }

    static IEnumerator CornerProbe()
    {
        var log = new List<string>();
        var ai = boss.AI;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var aimDistance = typeof(EnemyAIController).GetProperty("AttackTargetDistance", flags);
        var quake = director.Profile.Find("E_QuakeStomp")?.ability;
        foreach (float inset in new[] { 3.5f, 6f })
        {
            yield return WaitIdle(.3f);
            Vector3 p = DiamondDungeonLayout.InsetPoint(world.BossCorner, inset); p.y = .05f;
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.position = p; cc.enabled = true; Physics.SyncTransforms();
            int commits = director.CommitLog.Count; float until = Time.time + 12f, next = 0f;
            while (director.CommitLog.Count == commits && Time.time < until)
            {
                HoldHealth();
                if (Time.time >= next)
                {
                    next = Time.time + .2f;
                    float root = Vector3.Distance(Flat(boss.transform.position), Flat(player.transform.position));
                    float aim = aimDistance != null ? (float)aimDistance.GetValue(ai) : -1f;
                    Vector3 aimPoint = boss.AbilityController.ResolveAimPosition(player.transform);
                    log.Add($"inset={inset} t={12f - (until - Time.time):0.0} ai={ai.CurrentDebugStateName} root={root:0.00} aim={aim:0.00} aimOff={Vector3.Distance(Flat(aimPoint), Flat(player.transform.position)):0.00}"
                        + $" enter={ai.AttackEnterRange:0.00} within={ai.IsTargetWithin(ai.AttackEnterRange)} decision={director.CurrentDecisionId} strongLock={boss.AbilityController.IsStrongAttackLocked}"
                        + $" quakeReady={(quake != null && boss.AbilityController.IsCooldownReady(quake))} quakeStart={(quake != null ? EnemyAttackThreatGeometry.ResolveStartRange(boss, quake) : 0f):0.00}"
                        + $" moving={(boss.Movement != null ? boss.Movement.HasDestination.ToString() : "?")} bossInset={DiamondDungeonLayout.Radius - (Mathf.Abs(boss.transform.position.x) + Mathf.Abs(boss.transform.position.z)):0.0}"
                        + $" angle={Vector3.Angle(Flat(boss.transform.forward), Flat(aimPoint - boss.transform.position)):0.0} facingOk={(boss.Movement != null && boss.Movement.IsFacingForAttack(aimPoint))}"
                        + " | " + BossDiag());
                }
                yield return null;
            }
            log.Add($"inset={inset} RESULT {(director.CommitLog.Count > commits ? director.CommitLog[director.CommitLog.Count - 1] : "none")}");
        }
        File.WriteAllLines(Path.Combine(Output, "corner-probe.txt"), log);
        Cond("모서리 진단 기록", true, log.Count + " lines");
    }

    // ---------------- 페이즈 전환 ----------------
    static IEnumerator PhaseTransition()
    {
        yield return WaitIdle(.3f);
        boss.Health.Heal(boss.Health.MaxHp);
        float max = boss.Health.MaxHp;
        // 60% → 한 번에 38%까지(임계 55%를 크게 넘김)
        boss.Health.TakeDamage(new DamageInfo(max * .40f, boss.transform.position, isDamageOverTime: true));
        Check(phases.CurrentPhaseIndex == 0, "Phase changed early");
        Place(2.2f);
        float at = -1f; yield return WaitCommit("A_LeftSwipe", 2.2f, 15f, t => at = t);
        yield return WaitUntil(Time.time + .2f);
        boss.Health.TakeDamage(new DamageInfo(max * .22f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 8001, playerAttackKind: PlayerAttackKind.Weak));
        yield return null; yield return null;
        bool started = director.IsTransitioning && !boss.AbilityController.IsExecuting;
        float hpAtStart = boss.Health.CurrentHp;
        yield return WaitUntil(Time.time + .6f);
        Capture("phase2_roar");
        boss.Health.TakeDamage(new DamageInfo(max * .02f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 8002, playerAttackKind: PlayerAttackKind.Weak));
        bool damaged = boss.Health.CurrentHp < hpAtStart - max * .015f;
        bool noAttack = !boss.AbilityController.TryStart(player.transform);
        var hud = Object.FindFirstObjectByType<EnemyBossHudView>(FindObjectsInactive.Include);
        bool hudPhase = hud != null && hud.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t => t.name == "Phase" && t.text.StartsWith("PHASE 2"));
        string hudState = hud != null ? hud.DisplayedState : "";
        var stateBox = hud != null ? hud.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "BossState") : null;
        var phaseBox = hud != null ? hud.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "Phase") : null;
        bool apart = stateBox != null && phaseBox != null && !WorldRect(stateBox).Overlaps(WorldRect(phaseBox));
        hudState += apart ? "" : "(겹침)";
        while (director.IsTransitioning) yield return null;
        boss.Health.TakeDamage(new DamageInfo(max * .02f, boss.transform.position, isDamageOverTime: true));
        yield return null;
        Cond("큰 피해로 임계 통과 시 전환 1회·전환 중 피해 적용·공격 불가·HUD 2페이즈·포효 표시",
            started && damaged && noAttack && director.TransitionCount == 1 && phases.CurrentPhaseIndex == 1 && hudPhase && hudState == "포효" && !director.IsTransitionPending,
            $"started={started} damaged={damaged} noAttack={noAttack} transitions={director.TransitionCount} phase={phases.CurrentPhaseIndex} hud={hudPhase} state={hudState}");
    }

    // ---------------- 처치·정산 ----------------
    static IEnumerator KillAndSettle(int level, bool extract)
    {
        yield return WaitIdle(.2f);
        var account = AccountGameplaySession.Current;
        var run = account.ReadRun(); Check(run.phase == RunPhase.Active, "Run not active before kill");
        int exp0 = PlayerProgression.Current != null ? PlayerProgression.Current.Experience + PlayerProgression.Current.Level * 1000000 : 0;
        var pickupsBefore = new HashSet<WorldItemPickup>(Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None));
        int defeated = 0, cleared = 0; Action<EnemyBossPhaseController> onDefeat = b => defeated++; EnemyBossEncounterRegistry.BossDefeated += onDefeat;
        Action<EnemyBossOutcomeController, EnemyBossPhaseController> onCleared = (o, b) => cleared++; EnemyBossOutcomeController.EncounterCleared += onCleared;
        try
        {
            Place(2.0f);
            boss.Health.TakeDamage(new DamageInfo(boss.Health.MaxHp * 3f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 9001, playerAttackKind: PlayerAttackKind.Heavy));
            boss.Health.TakeDamage(new DamageInfo(boss.Health.MaxHp * 3f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 9002, playerAttackKind: PlayerAttackKind.Heavy));
            float until = Time.unscaledTime + 5f;
            while (account.ReadRun().phase != RunPhase.BossCleared && Time.unscaledTime < until) yield return null;
            yield return WaitUntil(Time.time + 1.2f);
        }
        finally { EnemyBossEncounterRegistry.BossDefeated -= onDefeat; EnemyBossOutcomeController.EncounterCleared -= onCleared; }
        Capture("Lv" + level + "_boss_cleared");
        run = account.ReadRun();
        var newPickups = Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Where(p => !pickupsBefore.Contains(p) && p.RuntimeItem != null).ToList();
        var maps = newPickups.Where(p => p.RuntimeItem.baseData is MapItemData && p.RuntimeItem.mapState != null).Select(p => p.RuntimeItem.mapState.level).ToList();
        int minLevel = Mathf.Min(level + 1, 100), maxLevel = Mathf.Min(level + 7, 100);
        var portal = Object.FindObjectsByType<DungeonExitPortal>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        var hud = Object.FindFirstObjectByType<EnemyBossHudView>(FindObjectsInactive.Include);
        int exp1 = PlayerProgression.Current != null ? PlayerProgression.Current.Experience + PlayerProgression.Current.Level * 1000000 : 0;
        Cond($"Lv{level} 처치 1회·클리어 기록·보상 지도 1장(Lv{minLevel}~{maxLevel})·포탈·HUD 숨김",
            run.phase == RunPhase.BossCleared && defeated == 1 && cleared == 1 && maps.Count == 1 && maps.All(m => m >= minLevel && m <= maxLevel)
            && portal != null && portal.gameObject.activeInHierarchy && hud != null && !hud.IsVisible,
            $"phase={run.phase} defeated={defeated} cleared={cleared} maps=[{string.Join(",", maps)}] drops={newPickups.Count} portal={(portal != null && portal.gameObject.activeInHierarchy)} expGain={exp1 - exp0}");
        results.Add(new { level, rewardMaps = maps, drops = newPickups.Select(p => p.RuntimeItem.baseData != null ? p.RuntimeItem.baseData.name : "?").ToArray(), expGain = exp1 - exp0 });
        // 정산 재호출은 추가 지급이 없어야 한다(RunPhase 멱등).
        var again = new AccountRunSession(account).ClearBossWithMapReward(run.runId, Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"), boss.transform.position);
        Cond($"Lv{level} 정산 재호출 이중 지급 없음", again == null && account.ReadRun().bossClearedAtUtcTicks == run.bossClearedAtUtcTicks);
        // 사체 페이드 뒤 풀 반환
        float poolUntil = Time.time + 8f;
        while (boss.IsLeased && Time.time < poolUntil) yield return null;
        Cond($"Lv{level} 보스 풀 반환·등록 해제", !boss.IsLeased && EnemyBossEncounterRegistry.ActiveCount == 0, "leased=" + boss.IsLeased);
        if (!extract) yield break;
        // 180초 자동 귀환: 실제 RunLifetimeDriver 경로에 시각만 앞당겨 준다.
        var driver = PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>();
        long clearedAt = account.ReadRun().bossClearedAtUtcTicks;
        driver.Poll(clearedAt + TimeSpan.TicksPerSecond * 170);
        bool stayed = account.ReadRun().phase == RunPhase.BossCleared;
        driver.Poll(clearedAt + TimeSpan.TicksPerSecond * 181);
        while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        yield return null;
        var last = account.ReadRun();
        Cond($"Lv{level} 180초 자동 귀환(170초 유지·181초 반출)", stayed && last.phase == RunPhase.Extracted, "phase=" + last.phase);
        VerifyNoResidue("Lv" + level + " 귀환");
    }

    static IEnumerator PlayerDeathAfterClear()
    {
        var account = AccountGameplaySession.Current;
        Check(account.ReadRun().phase == RunPhase.BossCleared, "Expected cleared run");
        playerActor.Health.TakeDamage(new DamageInfo(1e9f, player.transform.position));
        while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        yield return null;
        Cond("보스 처치 뒤 사망은 실패 정산(전리품 규칙 유지)", account.ReadRun().phase == RunPhase.Failed, "phase=" + account.ReadRun().phase);
        VerifyNoResidue("사망");
    }

    // ---------------- 2회차 경계 사례 ----------------
    static IEnumerator EdgeCases()
    {
        // 풀 반환·재대여: 상태·HUD·예고가 초기화된다.
        var spawn = world.SpawnBudget.GetComponent<EnemySpawnService>();
        Place(2.2f);
        float at = -1f; yield return WaitCommit("E_QuakeStomp", 2.5f, 30f, t => at = t);
        Check(at > 0f, "Quake for pool test");
        yield return WaitUntil(Time.time + .3f);
        bool cueVisible = director.IsDangerCueVisible;
        var def = boss.Definition; var pos = boss.transform.position; var rot = boss.transform.rotation;
        spawn.Release(boss);
        yield return null;
        Check(spawn.TrySpawn(new EnemySpawnRequest(def, pos, rot, player.transform, spawnParent: world.transform,
            context: new EncounterContext(AccountGameplaySession.Current.ReadRun().runId, 50, ItemGrade.Common)), out var re), "Boss re-lease");
        bool same = re == boss;
        boss = re; director = re.GetComponent<EnemyBossCombatDirector>(); phases = re.BossPhaseController;
        yield return null;
        var hud = Object.FindFirstObjectByType<EnemyBossHudView>(FindObjectsInactive.Include);
        bool reset = !director.IsDangerCueVisible && director.CommitLog.Count == 0 && director.Groggy01 < .001f && director.ParryCount == 0
            && phases.CurrentPhaseIndex == 0 && EnemyBossEncounterRegistry.ActiveCount == 1 && hud.BoundBoss == phases && Mathf.Approximately(boss.Health.NormalizedHp, 1f);
        Cond("풀 반환·재대여 후 페이즈·그로기·예고·HUD 초기화", cueVisible && reset, $"cueBefore={cueVisible} sameInstance={same} reset={reset}");
        // 월드 정산 구독은 같은 인스턴스를 재사용할 때만 유지된다. 이 검사 뒤 런은 이탈로 끝낸다.
        // 그로기 중 임계값 도달 → 그로기 뒤 전환
        int safety = 0;
        while (!director.IsGroggy && safety++ < 6)
        {
            float t = -1f; yield return WaitCommit("C_DoubleSmash", 2.2f, 30f, v => t = v);
            if (t < 0f) break;
            yield return WaitUntil(FirstImpact(t) - .45f);
            yield return Heavy(boss.transform.position);
            yield return null; yield return null;
        }
        Check(director.IsGroggy, "Groggy for deferral test");
        boss.Health.TakeDamage(new DamageInfo(boss.Health.CurrentHp - boss.Health.MaxHp * .5f, boss.transform.position, isDamageOverTime: true));
        yield return null;
        bool deferred = director.IsTransitionPending && !director.IsTransitioning && director.IsGroggy;
        while (director.IsGroggy) yield return null;
        yield return null; yield return null;
        bool afterGroggy = director.IsTransitioning && director.TransitionCount == 1;
        // 전환 중 사망 → 사망 우선, 전환 종료
        yield return WaitUntil(Time.time + .4f);
        boss.Health.TakeDamage(new DamageInfo(boss.Health.MaxHp * 3f, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 9500, playerAttackKind: PlayerAttackKind.Weak));
        yield return null;
        bool deathWins = boss.Health.IsDead && !director.IsTransitioning && !director.IsGroggy;
        bool deathAnim = boss.Animator.GetCurrentAnimatorStateInfo(0).IsName("Death") || boss.Animator.IsInTransition(0) && boss.Animator.GetNextAnimatorStateInfo(0).IsName("Death");
        yield return WaitUntil(Time.time + .3f);
        deathAnim |= boss.Animator.GetCurrentAnimatorStateInfo(0).IsName("Death");
        Cond("그로기 중 임계 도달은 그로기 뒤 전환 1회", deferred && afterGroggy, $"deferred={deferred} after={afterGroggy}");
        Cond("전환 중 사망은 사망 우선·연출 정리", deathWins && deathAnim, $"dead={boss.Health.IsDead} anim={deathAnim}");
        yield return WaitUntil(Time.time + 1f);
        yield return Abandon();
    }

    // 입력 시스템에 가상 키보드·마우스를 붙여 구르기(LeftShift)만 누른다.
    sealed class DriveInput : IDisposable
    {
        public bool Roll;
        readonly Keyboard keyboard = InputSystem.AddDevice<Keyboard>("BossVerifierKeyboard");
        readonly InputSettings original = InputSystem.settings; readonly InputSettings fixture;
        readonly PlayerInputFacade facade; readonly UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? devices;
        public DriveInput(PlayerInputFacade p)
        {
            facade = p; devices = p.RuntimeAsset.devices;
            var list = new List<InputDevice>(); if (devices.HasValue) list.AddRange(devices.Value); list.Add(keyboard);
            p.RuntimeAsset.devices = list.ToArray();
            fixture = Object.Instantiate(original);
            fixture.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            fixture.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = fixture;
            InputSystem.onBeforeUpdate += Drive;
        }
        void Drive()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.QueueStateEvent(keyboard, Roll ? new KeyboardState(UnityEngine.InputSystem.Key.LeftShift) : new KeyboardState());
        }
        public void Dispose()
        {
            InputSystem.onBeforeUpdate -= Drive; facade.RuntimeAsset.devices = devices;
            InputSystem.RemoveDevice(keyboard); InputSystem.settings = original; Object.Destroy(fixture);
        }
    }
}
