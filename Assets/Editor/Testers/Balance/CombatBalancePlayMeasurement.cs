using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Overburst.EditorBalance.Analysis
{
    // 기존 전투 런타임(PlayerEquipment·MeleeRuntime·CombatHealth·OverburstElementEnergy·EnemyRank·원소 스케줄러)으로
    // 대표 조합을 격리 Play에서 측정하고, 같은 시드의 계산 모델 예상치와 나란히 기록한다.
    // 격리: 계정 저장 경로를 측정 폴더로 돌리고, 종료·중단·시작 실패 시 빈 값(실제 계정)으로 해제한다. 씬은 저장하지 않는다.
    [InitializeOnLoad]
    public static class CombatBalancePlayMeasurement
    {
        const string Key = "OverburstBalanceMeasure";
        static IEnumerator work;
        static int frame;
        static double deadline;
        static MeasurementReport report;
        public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
        public static string Output => SessionState.GetString(Key + ".output", "");
        static double lastBusy;
        static CombatBalancePlayMeasurement()
        {
            EditorApplication.playModeStateChanged += State;
            lastBusy = EditorApplication.timeSinceStartup;
            EditorApplication.update += WatchSchedule;
        }

        // 공유 Editor용 예약(Codex 검토 10-01 P2-7): Editor가 비었다는 것만으로는 소유권이 아니다.
        // 예약은 소유권을 가진 작업이 이름을 밝혀 거는 임대(기본 15분)이고, 시작 직전에 임대가 살아 있는지 다시 확인한다.
        // 만료·취소되면 시작하지 않는다. 다른 작업의 소유권을 알아내는 장치는 없으므로, 임대는 예약자의 선언이다.
        [MenuItem("OVERBURST/Balance/전투 분석 Play 측정 예약(소유권 있을 때만)")]
        public static void ScheduleFromMenu()
        {
            if (!EditorUtility.DisplayDialog("Play 측정 예약",
                    "이 작업이 공유 Editor 소유권을 가진 경우에만 예약하세요.\n\nEditor가 20초 비면 시작합니다. 예약은 15분 뒤 만료되고, 시작 직전에 예약자와 만료를 다시 확인합니다. '예약 취소' 메뉴로 거둘 수 있습니다.",
                    "소유권 있음, 예약", "취소")) return;
            Schedule(20f, "메뉴 " + Environment.UserName, 15f);
        }

        [MenuItem("OVERBURST/Balance/전투 분석 Play 측정 예약 취소")]
        public static void CancelSchedule()
        {
            bool had = SessionState.GetFloat(Key + ".pending", 0f) > 0f;
            ClearLease();
            if (had) SessionState.SetString(Key + ".status", "NOT_RUN 예약 취소");
            Debug.Log("[전투 분석] Play 측정 예약 " + (had ? "취소" : "없음"));
        }

        public static string LeaseOwner => SessionState.GetString(Key + ".leaseOwner", "");

        public static void Schedule(float idleSeconds, string owner, float leaseMinutes = 15f)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("예약자(소유권을 가진 작업 이름)가 필요합니다.");
            double now = EditorApplication.timeSinceStartup;
            SessionState.SetFloat(Key + ".pending", Mathf.Max(5f, idleSeconds));
            SessionState.SetString(Key + ".leaseOwner", owner.Trim());
            SessionState.SetFloat(Key + ".leaseUntil", (float)(now + Mathf.Clamp(leaseMinutes, 1f, 60f) * 60f));
            SessionState.SetString(Key + ".status", "SCHEDULED " + owner.Trim());
            lastBusy = now;
            Debug.Log($"[전투 분석] Play 측정 예약: 예약자 {owner.Trim()}, Editor가 {idleSeconds}초 비면 시작, {leaseMinutes}분 뒤 만료");
        }

        static void ClearLease()
        {
            SessionState.EraseFloat(Key + ".pending");
            SessionState.EraseString(Key + ".leaseOwner");
            SessionState.EraseFloat(Key + ".leaseUntil");
        }

        static bool LeaseValid(double now, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(LeaseOwner)) reason = "예약자 없음";
            else if (now > SessionState.GetFloat(Key + ".leaseUntil", 0f)) reason = "소유권 임대 만료";
            return reason == null;
        }

        static void WatchSchedule()
        {
            float idle = SessionState.GetFloat(Key + ".pending", 0f);
            if (idle <= 0f) return;
            double now = EditorApplication.timeSinceStartup;
            if (!LeaseValid(now, out string reason))
            {
                ClearLease(); SessionState.SetString(Key + ".status", "BLOCKED " + reason);
                Debug.LogWarning("[전투 분석] 예약 측정을 시작하지 않음: " + reason);
                return;
            }
            bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
                        || SessionState.GetBool(Key, false);
            if (busy) { lastBusy = now; return; }
            if (now - lastBusy < idle) return;
            // 시작 직전 재확인: 대기 중에 만료·취소됐으면 시작하지 않는다.
            if (!LeaseValid(EditorApplication.timeSinceStartup, out reason))
            {
                ClearLease(); SessionState.SetString(Key + ".status", "BLOCKED " + reason);
                return;
            }
            string owner = LeaseOwner;
            ClearLease();
            try { Run(); Debug.Log("[전투 분석] 예약 측정 시작, 예약자 " + owner); }
            catch (Exception e) { SessionState.SetString(Key + ".status", "BLOCKED " + e.Message); Debug.LogWarning("[전투 분석] 예약 측정 시작 실패: " + e.Message); }
        }

        // 계획이 바뀌면 올린다. 이어 하기·병합은 같은 계획 버전·같은 지문끼리만 한다.
        public const string PlanVersion = "2026-10-01c";

        public sealed class Plan
        {
            public string key, role = "검증"; public int level; public ItemGrade grade; public GearPreset preset; public WeaponElement element;
            public EnemyClass enemy; public bool crowd; public int crowdCount;
            public int seedOffset; public float spreadScale = 1f, angleOffset; // 검증 배치: 다른 시드·더 넓은 배치·회전
        }

        static readonly WeaponElement[] Elements = { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };

        public static List<Plan> DefaultPlan()
        {
            var list = new List<Plan>();
            foreach (int level in new[] { 5, 55, 95 })
                foreach (WeaponElement e in Elements)
                    list.Add(new Plan { level = level, grade = ItemGrade.Common, preset = GearPreset.Rolled, element = e, enemy = EnemyClass.Medium });
            list.Add(new Plan { level = 55, grade = ItemGrade.Common, preset = GearPreset.Rolled, element = WeaponElement.Fire, enemy = EnemyClass.Small });
            list.Add(new Plan { level = 55, grade = ItemGrade.Epic, preset = GearPreset.Offense, element = WeaponElement.Electric, enemy = EnemyClass.Elite });
            // 일반 품질 정예 5원소(모델의 정예 단일 원소 편차 R04를 Play로 대조)
            foreach (WeaponElement e in Elements)
                list.Add(new Plan { level = 55, grade = ItemGrade.Common, preset = GearPreset.Rolled, element = e, enemy = EnemyClass.Elite });
            // 보정: 군집 입력(약공 4·강공 8마리·밀착 1.4/㎡)을 맞출 때 쓴 조합. 검증 근거로 쓰지 않는다.
            foreach (WeaponElement e in new[] { WeaponElement.Fire, WeaponElement.Electric, WeaponElement.Ice, WeaponElement.Dark })
                list.Add(new Plan { level = 55, grade = ItemGrade.Common, preset = GearPreset.Rolled, element = e, enemy = EnemyClass.Small, crowd = true, crowdCount = 20, role = "보정" });
            // 검증: 다른 시드·더 넓고 회전한 배치의 5원소 군집(보정에 쓰지 않은 데이터)
            foreach (WeaponElement e in Elements)
                list.Add(new Plan { level = 55, grade = ItemGrade.Common, preset = GearPreset.Rolled, element = e, enemy = EnemyClass.Small, crowd = true, crowdCount = 20,
                    seedOffset = 7919, spreadScale = 1.25f, angleOffset = 1.1f });
            foreach (var p in list)
                p.key = $"Lv{p.level} {AnalysisLabels.Grade(p.grade)} {AnalysisLabels.Preset(p.preset)} {AnalysisLabels.Element(p.element)} {AnalysisLabels.Enemy(p.enemy)}"
                        + (p.crowd ? " 군집" + p.crowdCount + (p.role == "보정" ? "" : " 검증배치") : "");
            return list;
        }

        [MenuItem("OVERBURST/Balance/전투 분석 Play 측정 실행")]
        public static void RunFromMenu() => Run();

        public static string Run(string output = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("이미 Play 중입니다.");
            if (EditorApplication.isCompiling) throw new InvalidOperationException("컴파일 중입니다.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene")
                throw new InvalidOperationException("PersistentScene에서 실행해야 합니다.");
            output = output ?? Path.Combine(CombatBalanceAnalysisReport.MeasurementRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(output);
            SessionState.SetString(Key + ".output", output);
            SessionState.SetString(Key + ".fingerprint", Fingerprint());
            SessionState.SetBool(Key, true);
            SessionState.SetString(Key + ".status", "RUNNING");
            try
            {
                BeginIsolatedPlay(Path.Combine(output, "IsolatedAccount"), () =>
                {
                    EditorApplication.EnterPlaymode();
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        throw new InvalidOperationException("Play 진입이 취소되었습니다: " + IsolatedSavePlayGuard.LastRejection);
                });
            }
            catch
            {
                SessionState.SetBool(Key, false);
                SessionState.SetString(Key + ".status", "BLOCKED Play 시작 실패");
                throw;
            }
            return output;
        }

        // 예전 테스트 경로를 '원래 값'으로 복원하지 않는다(99 공통 지침).
        internal static void ClearIsolatedSaveDirectory()
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null);
            SessionState.EraseString(Key + ".env");
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
                IsolatedSavePlayGuard.UseRealAccount();
        }

        internal static void BeginIsolatedPlay(string directory, Action start)
        {
            try
            {
                IsolatedSavePlayGuard.PrepareIsolatedPlay(directory);
                start();
            }
            catch { ClearIsolatedSaveDirectory(); throw; }
        }

        public static string CurrentFingerprint => SessionState.GetString(Key + ".fingerprint", "");

        // 실행 지문: 계획·조건·입력/아이템/전투 소스, 카탈로그 선택 후보와 실제 자산 의존성 및 meta의 SHA256.
        // 지문이 다르면 옛 결과를 이어 하거나 병합하지 않는다(다른 작업이 중간에 규칙을 바꾼 경우를 막는다).
        public static string Fingerprint()
        {
            var parts = new List<string> { PlanVersion, CombatBalanceAnalysisReport.ToJson(DefaultPlan()), CombatBalanceAnalysisReport.ToJson(new AnalysisConditions()) };
            string project = Directory.GetParent(Application.dataPath).FullName;
            var files = new SortedSet<string>(StringComparer.Ordinal);
            AddFingerprintFile(files, "Assets/Editor/Tools/Common/IsolatedSavePlayGuard.cs");
            foreach (var dir in new[] { "Assets/Editor/Testers/Balance", "Assets/ProjectOverburst/01_Core", "Assets/ProjectOverburst/02_Shared", "Assets/ProjectOverburst/03_Features/Weapons/Runtime",
                         "Assets/ProjectOverburst/03_Features/Player/Runtime", "Assets/ProjectOverburst/03_Features/Enemies/Runtime", "Assets/ProjectOverburst/03_Features/Items/Runtime" })
            {
                string full = Path.Combine(project, dir);
                if (Directory.Exists(full))
                    foreach (var f in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories)) AddFingerprintFile(files, f.Substring(project.Length + 1).Replace('\\', '/'));
            }
            var assets = new List<string>();
            if (OverburstElementTuning.Current != null) assets.Add(AssetDatabase.GetAssetPath(OverburstElementTuning.Current));
            assets.Add(new AnalysisConditions().weaponPath);
            var catalog = CombatBalanceAnalysisModel.Catalog.Load();
            // 대표 몬스터·장비 선택은 전체 후보의 정렬/레벨 조건에 좌우된다.
            assets.AddRange(catalog.enemies.Select(AssetDatabase.GetAssetPath));
            assets.AddRange(catalog.gear.Values.SelectMany(g => g).Select(AssetDatabase.GetAssetPath));
            assets.Add("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
            var roots = assets.Where(a => !string.IsNullOrEmpty(a)).Distinct().OrderBy(a => a, StringComparer.Ordinal).ToArray();
            // 수 GB의 모델/텍스처를 매번 읽지 않고 Unity의 의존성 내용 해시를 사용한다.
            // 자산 의존성은 확장자로 버리지 않으며, 소스·직렬화 데이터·모든 meta는 원본 SHA256도 기록한다.
            foreach (var a in roots) parts.Add(a + ":import:" + AssetDatabase.GetAssetDependencyHash(a));
            foreach (var dep in AssetDatabase.GetDependencies(roots, true)) AddFingerprintFile(files, dep);
            return HashFingerprintInputs(project, parts, files, path =>
                !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && new FileInfo(Path.Combine(project, path)).Length > 1024 * 1024
                    ? AssetDatabase.GetAssetDependencyHash(path).ToString() : null);
        }

        internal static void AddFingerprintFile(ISet<string> files, string path)
        {
            files.Add(path);
            files.Add(path + ".meta"); // GUID와 import 설정도 측정 조건에 포함한다.
        }

        internal static string HashFingerprintInputs(string project, IEnumerable<string> parts, IEnumerable<string> files, Func<string, string> importedHash = null)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (var p in parts) sb.Append(p).Append('\n');
                foreach (var f in files.Distinct().OrderBy(f => f, StringComparer.Ordinal))
                {
                    string path = Path.Combine(project, f);
                    if (!File.Exists(path)) { sb.Append(f).Append(":MISSING\n"); continue; }
                    string cached = importedHash?.Invoke(f);
                    if (cached != null) { sb.Append(f).Append(":import:").Append(cached).Append('\n'); continue; }
                    using (var stream = File.OpenRead(path))
                        sb.Append(f).Append(':').Append(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "")).Append('\n');
                }
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }

        static void State(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetBool(Key + ".background", Application.runInBackground);
                SessionState.SetInt(Key + ".fps", Application.targetFrameRate);
                Application.runInBackground = true; Application.targetFrameRate = 60;
                report = new MeasurementReport { status = "RUNNING", startedAt = Now(), unityVersion = Application.unityVersion,
                    planVersion = PlanVersion, fingerprint = CurrentFingerprint,
                    scope = "HideoutScene 독립 투기장, 제품 몬스터 정의·실제 입력 이벤트(가상 키보드·마우스)·실제 무기 충돌. 대상 AI 끔(군집은 AI 켬). 플레이어 사망 방지 켬. 받는 피해는 TakeDamage 직접 호출(몬스터 공격 실행은 미검증). 물약·지도 버프 없음." };
                frame = -1; deadline = EditorApplication.timeSinceStartup + 1200;
                work = Measure(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
                if (work != null && report != null && report.status == "RUNNING")
                {
                    // 다른 작업이 Play를 멈춘 경우: 끝난 시나리오까지 남기고 중단으로 기록한다.
                    report.status = "INTERRUPTED"; report.finishedAt = Now();
                    report.interruptions.Add("측정 도중 Play가 외부에서 종료됨(완료 " + report.scenarios.Count(s => s.status != null) + "개)");
                    SessionState.SetString(Key + ".status", "INTERRUPTED");
                    Save();
                }
                try { (work as IDisposable)?.Dispose(); }
                finally
                {
                    work = null;
                    Application.runInBackground = SessionState.GetBool(Key + ".background", false);
                    Application.targetFrameRate = SessionState.GetInt(Key + ".fps", -1);
                }
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                ClearIsolatedSaveDirectory();
                SessionState.SetBool(Key, false);
            }
        }

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        static void Log(string m, string s, LogType t) { if (t == LogType.Exception || t == LogType.Assert) report?.errors.Add(m); }

        static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("측정 시간 초과");
                if (work.MoveNext()) return;
                foreach (var s in report.scenarios) s.status = CombatBalanceMeasurementStatus.OfScenario(s);
                var keys = DefaultPlan().Select(p => p.key).ToList();
                // 이어 한 회차는 이번에 잰 시나리오만 담으므로 개수 비교는 병합 단계에서 한다.
                Finish(CombatBalanceMeasurementStatus.OfRun(report.scenarios, report.errors, 0, keys));
            }
            catch (Exception e) { report.errors.Add(e.ToString()); Finish("FAIL"); }
        }

        static void Finish(string status)
        {
            report.status = status; report.finishedAt = Now();
            SessionState.SetString(Key + ".status", status);
            EditorApplication.update -= Tick;
            try { (work as IDisposable)?.Dispose(); }
            catch (Exception e) { report.errors.Add("측정 정리 실패: " + e); report.status = "FAIL"; SessionState.SetString(Key + ".status", "FAIL"); }
            finally { work = null; }
            try { Save(); }
            finally { EditorApplication.ExitPlaymode(); }
        }

        static void Save()
        {
            try
            {
                File.WriteAllText(Path.Combine(Output, "measurement.json"), CombatBalanceAnalysisReport.ToJson(report), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogError("[전투 분석] 측정 결과 저장 실패: " + e); }
        }

        sealed class MeasureInput : IDisposable
        {
            public bool Weak, Heavy;
            public Vector3 Aim;
            readonly Keyboard keyboard = InputSystem.AddDevice<Keyboard>("BalanceMeasureKeyboard");
            readonly Mouse mouse = InputSystem.AddDevice<Mouse>("BalanceMeasureMouse");
            readonly InputSettings original = InputSystem.settings;
            readonly InputSettings fixture;
            readonly PlayerInputFacade facade;
            readonly UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? devices;
            public MeasureInput(PlayerInputFacade p)
            {
                facade = p; devices = p.RuntimeAsset.devices; p.RuntimeAsset.devices = new InputDevice[] { keyboard, mouse };
                fixture = Object.Instantiate(original);
                fixture.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                fixture.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = fixture;
                InputSystem.onBeforeUpdate += Drive;
            }
            void Drive()
            {
                if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                var state = new MouseState { position = Camera.main != null ? (Vector2)Camera.main.WorldToScreenPoint(Aim) : Vector2.zero };
                if (Weak) state = state.WithButton(MouseButton.Left);
                if (Heavy) state = state.WithButton(MouseButton.Right);
                InputSystem.QueueStateEvent(mouse, state);
            }
            public void Dispose()
            {
                InputSystem.onBeforeUpdate -= Drive; facade.RuntimeAsset.devices = devices;
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse); InputSystem.settings = original; Object.Destroy(fixture);
            }
        }

        static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
        {
            var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
            if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
            if (on) cc.enabled = true; Physics.SyncTransforms();
        }

        static float F2(float a, float b) => Mathf.Abs(a - b);
        static string R(float v) => float.IsNaN(v) ? "—" : v.ToString("0.##", CultureInfo.InvariantCulture);

        static IEnumerator Measure()
        {
            EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; MeleeRuntime melee = null; MeasureInput input = null;
            var leased = new List<EnemyActor>();
            try
            {
                while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                       || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene" || PlayerContext.Instance?.CurrentActor == null) yield return null;
                if (!Overburst.Persistence.AccountBootstrap.SaveDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("계정 저장이 격리되지 않았습니다: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
                var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
                ui = EnemyThemeTrialHarness.Current;
                if (ui == null) throw new InvalidOperationException("독립 투기장 UI 없음");
                if (!ui.InArena) ui.ToggleArena();
                if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new InvalidOperationException("스폰 서비스 없음");
                foreach (var t in ui.tables) if (!spawn.RegisterAdditionalCatalog(t.Catalog, out string error)) throw new InvalidOperationException(error);
                yield return null; yield return null;
                var catalog = CombatBalanceAnalysisModel.Catalog.Load();
                var conditions = new AnalysisConditions();
                WeaponItemData weapon = catalog.Weapon(conditions.weaponPath);
                var origin = player.transform.position;
                input = new MeasureInput(player); melee = player.GetComponent<MeleeRuntime>(); melee.SetManualInputEnabled(true);
                actor.Health.SetDamageDeathPrevention(melee, true);
                var done = DoneKeys();
                foreach (var plan in DefaultPlan())
                {
                    if (done.Contains(plan.key)) continue; // 앞선 측정에서 끝난 시나리오는 건너뛴다(중단 뒤 이어 하기).
                    var s = new MeasurementScenario { key = plan.key, role = plan.role, level = plan.level, grade = plan.grade, preset = plan.preset, element = plan.element, enemyClass = plan.enemy };
                    report.scenarios.Add(s);
                    IEnumerator run = plan.crowd ? Crowd(plan, s, catalog, conditions, weapon, player, actor, melee, input, spawn, origin, leased)
                        : Single(plan, s, catalog, conditions, weapon, player, actor, melee, input, spawn, origin, leased);
                    while (true)
                    {
                        bool next;
                        try { next = run.MoveNext(); }
                        catch (Exception e) { s.status = "FAIL"; s.note = "예외: " + e.Message; s.checks.Add("FAIL 예외 " + e.GetType().Name); next = false; }
                        if (!next) break;
                        yield return run.Current;
                    }
                    input.Weak = input.Heavy = false; melee.CancelCurrentAttackState();
                    foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
                    leased.Clear();
                    Save(); // 시나리오마다 저장: 외부 종료에도 끝난 결과가 남는다.
                    float settle = Time.unscaledTime + .8f; while (Time.unscaledTime < settle) yield return null;
                }
            }
            finally
            {
                input?.Dispose(); melee?.CancelCurrentAttackState();
                if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
                if (ui != null && ui.InArena) ui.ToggleArena();
            }
        }

        static int SeedFor(Plan p, AnalysisConditions c) => c.seedBase + (p.level - 1) / 10 * 1009 + (int)p.grade * 97 + p.seedOffset;

        static HashSet<string> DoneKeys()
        {
            // 직전 측정이 중단됐고 계획 버전·지문이 지금과 같을 때만 끝난 시나리오를 건너뛴다. 아니면 전부 새로 잰다.
            var chain = CombatBalanceAnalysisReport.MeasurementChain();
            if (chain.Count == 0) return new HashSet<string>();
            var last = CombatBalanceAnalysisReport.FromJson<MeasurementReport>(File.ReadAllText(chain[chain.Count - 1].FullName, Encoding.UTF8));
            var current = new MeasurementReport { planVersion = PlanVersion, fingerprint = CurrentFingerprint };
            if (last == null || last.status != "INTERRUPTED" || !CombatBalanceAnalysisReport.SameRun(current, last))
            {
                if (last != null && last.status == "INTERRUPTED") Debug.Log("[전투 분석] 직전 중단 회차와 계획·지문이 달라 처음부터 잰다");
                return new HashSet<string>();
            }
            var merged = CombatBalanceAnalysisReport.LoadLatestMeasurement();
            return new HashSet<string>(merged != null ? merged.scenarios.Where(s => s.status == "PASS" || s.status == "FAIL").Select(s => s.key) : Enumerable.Empty<string>());
        }

        static bool Prepare(Plan plan, MeasurementScenario s, CombatBalanceAnalysisModel.Catalog catalog, AnalysisConditions c, WeaponItemData weapon,
            PlayerActorRuntime actor, out PlayerBuild model)
        {
            model = CombatBalanceAnalysisModel.BuildPlayer(catalog, weapon, plan.level, plan.grade, plan.preset, plan.element, SeedFor(plan, c), c.combatStance);
            s.seed = model.seed;
            bool levelSet = Overburst.Persistence.AccountGameplaySession.Current.ExecuteState("balance-measure-level", st => { st.level = plan.level; st.experience = 0; });
            var equipment = actor.Equipment;
            if (!equipment.EquipWeaponItem(model.weaponItem)) { s.status = "BLOCKED"; s.note = "무기 장착 실패"; return false; }
            for (int slot = 0; slot < CombatBalanceAnalysisModel.Slots.Length; slot++)
            {
                var item = slot < model.gearItems.Count ? model.gearItems[slot] : null;
                if (item == null) { equipment.ClearGearSlot(slot, out _); continue; }
                if (!equipment.EquipGearItemToSlot(item, slot, out _)) { s.status = "BLOCKED"; s.note = "장비 장착 실패 " + item.baseData.name; return false; }
            }
            PlayerProgression.Current?.RefreshStats();
            actor.Health.SetMaxHp(actor.Health.MaxHp, true);
            s.modelAttack = model.attack; s.measuredAttack = equipment.CurrentWeaponStats.damage;
            s.modelCrit = model.statCrit; s.measuredCrit = equipment.CurrentWeaponStats.critChance;
            s.modelArmor = model.armor; s.measuredArmor = PlayerProgression.Current != null ? PlayerProgression.Current.Armor : float.NaN;
            s.modelMaxHealth = model.maxHealth; s.measuredMaxHealth = actor.Health.MaxHp;
            bool ok = levelSet && PlayerProgression.CurrentLevel == plan.level && F2(s.modelAttack, s.measuredAttack) < .5f && F2(s.modelCrit, s.measuredCrit) < .01f
                      && F2(s.modelArmor, s.measuredArmor) < .01f && F2(s.modelMaxHealth, s.measuredMaxHealth) < .5f;
            s.checks.Add((ok ? "PASS" : "FAIL") + $" 플레이어 조립 Lv{PlayerProgression.CurrentLevel} 공격 {R(s.modelAttack)}/{R(s.measuredAttack)} 치확 {R(s.modelCrit)}/{R(s.measuredCrit)} 방어 {R(s.modelArmor)}/{R(s.measuredArmor)} HP {R(s.modelMaxHealth)}/{R(s.measuredMaxHealth)}");
            if (!ok) s.status = "FAIL";
            return true;
        }

        static EnemyActor SpawnEnemy(EnemySpawnService spawn, EnemyDefinition def, Vector3 point, Transform player, int level, bool ai)
        {
            if (Physics.Raycast(point + Vector3.up * 4f, Vector3.down, out var floor, 9f, LayerMask.GetMask("Default", "Ground")))
                point = floor.point + Vector3.up * .035f;
            var request = new EnemySpawnRequest(def, point, Quaternion.LookRotation(player.position - point), player,
                context: new EncounterContext(null, level, ItemGrade.Common));
            if (!spawn.TrySpawn(request, out var enemy)) return null;
            enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (!ai) { enemy.AI.enabled = false; enemy.Movement.StopMovement(); }
            return enemy;
        }

        static IEnumerator Single(Plan plan, MeasurementScenario s, CombatBalanceAnalysisModel.Catalog catalog, AnalysisConditions c, WeaponItemData weapon,
            PlayerInputFacade player, PlayerActorRuntime actor, MeleeRuntime melee, MeasureInput input, EnemySpawnService spawn, Vector3 origin, List<EnemyActor> leased)
        {
            if (!Prepare(plan, s, catalog, c, weapon, actor, out var model)) yield break;
            if (!catalog.representative.TryGetValue(plan.enemy, out var def)) { s.status = "BLOCKED"; s.note = "대표 몬스터 없음"; yield break; }
            var enemyModel = CombatBalanceAnalysisModel.BuildEnemy(def, plan.level, model);
            var outcome = CombatBalanceAnalysisModel.Simulate(model, enemyModel, weapon, c, CombatMode.Single);
            var charge = CombatBalanceAnalysisModel.ChargeOnly(model, enemyModel, weapon, c);
            s.enemyId = def.EnemyId;
            s.modelEnemyHealth = enemyModel.maxHealth; s.modelChargeTime = charge.chargeTime; s.modelChargePhases = charge.chargePhases;
            s.modelWeakNormalHit = outcome.weakNormal; s.modelWeakCritHit = outcome.weakCrit;
            s.modelPrepSurvived = outcome.prepSurvived; s.modelHeavyKilled = outcome.heavyKilled;
            float modelKillChance = outcome.prepSurvived ? outcome.heavyKillChance : 0f;
            s.modelHeavyKillChance = outcome.prepSurvived ? modelKillChance : float.NaN;
            s.modelDerivedTimes = outcome.firstHeavyDerivedTimes.ToList();
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            Warp(player, origin, Vector3.forward); yield return null;
            var enemy = SpawnEnemy(spawn, def, origin + Vector3.forward * 1.7f, player.transform, plan.level, false);
            if (enemy == null) { s.status = "BLOCKED"; s.note = "스폰 실패"; yield break; }
            leased.Add(enemy);
            yield return null;
            s.measuredEnemyHealth = enemy.Health.MaxHp;
            bool hpOk = F2(s.modelEnemyHealth, s.measuredEnemyHealth) < .5f;
            s.checks.Add((hpOk ? "PASS" : "FAIL") + $" 몬스터 체력 {def.EnemyId} Lv{plan.level} {R(s.modelEnemyHealth)}/{R(s.measuredEnemyHealth)}");
            if (!hpOk) s.status = "FAIL";
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            energy.Clear();
            float start = Time.time, fullAt = float.NaN; float lastAmount = 0f; int lastRadiance = 0;
            var tuning = OverburstElementTuning.Current;
            bool light = plan.element == WeaponElement.Light && c.lightTriple;
            float target = light ? tuning.SafeLightOverchargeMaximum : tuning.maximumEnergy;
            var phasesUntilFull = new HashSet<(int, int)>();
            void Hit(CombatHealth h, DamageInfo d, float actual, bool fatal)
            {
                s.hits.Add(new MeasuredHit { time = Time.time - start, damage = d.damage, critical = d.isCritical, dot = d.isDamageOverTime,
                    heavy = (d.playerAttackKind & PlayerAttackKind.Heavy) != 0,
                    derived = d.playerAttackKind == PlayerAttackKind.Elemental && !d.isDamageOverTime,
                    phase = d.sourceAttackPhaseIndex, sequence = d.sourceAttackSequenceId });
                if ((d.playerAttackKind & PlayerAttackKind.Weak) != 0 && float.IsNaN(fullAt)) phasesUntilFull.Add((d.sourceAttackSequenceId, d.sourceAttackPhaseIndex));
            }
            enemy.Health.OnDamageResolved += Hit;
            input.Weak = true;
            while (!enemy.Health.IsDead && Time.time < start + 25f)
            {
                input.Aim = enemy.transform.position;
                if (energy.Amount > 0f) { lastAmount = energy.Amount; lastRadiance = energy.RadianceStacks; }
                bool ready = light ? energy.Amount >= target - .01f && energy.RadianceStacks >= tuning.SafeLightRadianceMaxStacks : energy.Amount >= target - .01f;
                if (ready) { fullAt = Time.time - start; break; }
                yield return null;
            }
            input.Weak = false;
            s.measuredChargeTime = fullAt; s.measuredChargePhases = phasesUntilFull.Count;
            s.measuredPrepSurvived = !enemy.Health.IsDead && !float.IsNaN(fullAt);
            float finish = Time.unscaledTime + 8f; while (melee.IsAttackInProgress && Time.unscaledTime < finish) yield return null;
            if (s.measuredPrepSurvived)
            {
                lastAmount = energy.Amount; lastRadiance = energy.RadianceStacks;
                input.Aim = enemy.transform.position; input.Heavy = true;
                float heavyStart = Time.unscaledTime;
                while (!melee.IsHeavyAttackInProgress && Time.unscaledTime < heavyStart + 3f) { lastAmount = Mathf.Max(lastAmount, energy.Amount); yield return null; }
                input.Heavy = false;
                while (energy.Amount > 0f && melee.IsAttackInProgress) { lastAmount = energy.Amount; lastRadiance = energy.RadianceStacks; yield return null; }
                finish = Time.unscaledTime + 10f; while (melee.IsAttackInProgress && Time.unscaledTime < finish) yield return null;
                float tail = Time.unscaledTime + 2.5f; while (Time.unscaledTime < tail && !enemy.Health.IsDead) yield return null;
                s.measuredHeavyKilled = enemy.Health.IsDead;
            }
            enemy.Health.OnDamageResolved -= Hit;
            s.measuredElapsed = Time.time - start;
            var weak = s.hits.Where(h => !h.heavy && !h.derived && !h.dot).ToList();
            s.measuredWeakHits = weak.Count; s.measuredCrits = weak.Count(h => h.critical);
            var normal = weak.Where(h => !h.critical).Select(h => h.damage).ToList();
            s.measuredWeakNormalHit = normal.Count > 0 ? normal.Max() : float.NaN;
            s.measuredWeakCritHit = weak.Where(h => h.critical).Select(h => h.damage).DefaultIfEmpty(float.NaN).Max();
            s.measuredDot = s.hits.Where(h => h.dot).Sum(h => h.damage);
            s.measuredDerived = s.hits.Where(h => h.derived).Sum(h => h.damage);
            // 약공 판정 대조(Codex 검토 10-01 P2-4): 공격 시퀀스 순서로 콤보 타를 정하고(첫 타 0, 이후 이어 치기),
            // 각 적중의 판정 번호로 계수를 골라 치명 여부까지 같은 값인지 본다. 끝까지 친 타는 판정 누락·추가도 센다.
            var combo = weapon.GetMeleeComboDefinition();
            float weakMultiplier = CombatBalanceFormulas.AttackDamageMultiplier(weapon, CombatBalanceAnalysisModel.HeavyDefinition(weapon), false, false);
            PlayerAttackKind weakKind = plan.element == WeaponElement.None ? PlayerAttackKind.Weak : PlayerAttackKind.Weak | PlayerAttackKind.Elemental;
            float Expected(float coefficient, bool critical) => CombatBalanceFormulas.ApplyPlayerOutgoing(
                CombatBalanceFormulas.RoundedHitDamage(model.attack * coefficient * weakMultiplier, critical, model.critDamage), model.totals, true, enemyModel.grade, weakKind, 0, 0);
            var sequences = weak.OrderBy(h => h.time).Select(h => h.sequence).Distinct().ToList();
            int badPhase = 0, badDamage = 0, missing = 0, extra = 0;
            float maxNormalModel = 0f;
            for (int k = 0; k < sequences.Count; k++)
            {
                int stepIndex = k % Mathf.Max(1, combo.StepCount);
                var defs = combo.GetStep(stepIndex).attackPhases ?? Array.Empty<AttackPhaseData>();
                var timing = CombatBalanceAnalysisModel.TimeComboStep(weapon, stepIndex, k > 0, model.attackSpeed);
                var chained = new HashSet<int>(timing.phases.Select(p => p.index));
                var tail = new HashSet<int>(timing.tailPhases.Select(p => p.index));
                var got = weak.Where(h => h.sequence == sequences[k]).ToList();
                var seen = new HashSet<int>();
                foreach (var h in got)
                {
                    if (h.phase < 0 || h.phase >= defs.Length) { badPhase++; continue; }
                    if (!seen.Add(h.phase)) extra++;
                    float v = Expected(defs[h.phase].impact.SafeDamageMultiplier, h.critical);
                    if (!h.critical) maxNormalModel = Mathf.Max(maxNormalModel, v);
                    if (Mathf.Abs(v - h.damage) >= .51f) badDamage++;
                }
                bool last = k == sequences.Count - 1;
                if (!last) { missing += chained.Count(i => !seen.Contains(i)); extra += seen.Count(i => !chained.Contains(i)); }
                else extra += seen.Count(i => !chained.Contains(i) && !tail.Contains(i));
            }
            bool weakOk = weak.Count > 0 && badPhase + badDamage + missing + extra == 0;
            s.checks.Add((weakOk ? "PASS" : "FAIL") + $" 약공 판정 피해 {weak.Count}회·{sequences.Count}타(치명 {s.measuredCrits}): 타·판정 번호별 모델 값 대조 — 값 불일치 {badDamage}, 판정 번호 오류 {badPhase}, 누락 {missing}, 추가 {extra}. 비치명 최대 {R(s.measuredWeakNormalHit)}/모델 {R(maxNormalModel)}");
            if (!weakOk) s.status = "FAIL";
            // 강공: 측정된 확정 직전 에너지로 모델 H를 다시 계산해 경로를 대조한다.
            var heavyHit = s.hits.FirstOrDefault(h => h.heavy);
            if (heavyHit != null)
            {
                var heavy = CombatBalanceAnalysisModel.TimeHeavy(weapon, model.attackSpeed);
                float e = Mathf.Clamp01(lastAmount / Mathf.Max(1f, tuning.maximumEnergy));
                float attackDamage = model.attack * heavy.phaseMultiplier;
                float blast = CombatBalanceFormulas.HeavyFirstBlastDamage(attackDamage, e, CombatBalanceFormulas.DischargeEnergyCoefficient(tuning, e, 0, 0),
                    WeaponStatCalculator.GetElementalDischargePower(model.weaponItem, attackDamage));
                if (plan.element == WeaponElement.Light)
                {
                    bool triple = lastAmount > tuning.maximumEnergy + .0001f;
                    float over = Mathf.Clamp01((lastAmount - tuning.maximumEnergy) / Mathf.Max(.0001f, tuning.SafeLightOverchargeMaximum - tuning.maximumEnergy));
                    blast *= CombatBalanceFormulas.LightTripleHitScale(tuning, triple ? 0 : 1, lastRadiance, over);
                }
                PlayerAttackKind heavyKind = PlayerAttackKind.Heavy | PlayerAttackKind.Elemental;
                s.modelHeavyDirect = CombatBalanceFormulas.ApplyPlayerOutgoing(CombatBalanceFormulas.RoundedHitDamage(blast, heavyHit.critical, model.critDamage), model.totals, true, enemyModel.grade, heavyKind, 0, 0);
                s.measuredHeavyDirect = heavyHit.damage;
                bool heavyOk = Mathf.Abs(s.modelHeavyDirect - s.measuredHeavyDirect) <= Mathf.Max(.51f, s.modelHeavyDirect * .01f);
                s.checks.Add((heavyOk ? "PASS" : "FAIL") + $" 강공 첫 폭발(에너지 {R(lastAmount)}·광휘 {lastRadiance}, 치명 {(heavyHit.critical ? "예" : "아니오")}) 모델 {R(s.modelHeavyDirect)}/실측 {R(s.measuredHeavyDirect)}");
                if (!heavyOk) s.status = "FAIL";
            }
            else if (s.measuredPrepSurvived) { s.checks.Add("FAIL 강공 적중 없음"); s.status = "FAIL"; }
            else s.checks.Add("NOT_RUN 강공(준비 중 처치)");
            // 강공 후속(파생) 타격의 횟수·시각: 첫 강공 적중을 0초로 두고 모델 이벤트 시각과 차례로 비교한다.
            if (heavyHit != null)
            {
                var measuredDerived = s.hits.Where(h => h.derived && h.time >= heavyHit.time - 1e-4f).Select(h => h.time - heavyHit.time).OrderBy(t => t).ToList();
                var modelDerived = s.modelDerivedTimes.OrderBy(t => t).ToList();
                string timingSource = "기대 전투 모형";
                if (plan.element == WeaponElement.Light)
                {
                    // 기대 치명 피해로 먼저 죽은 모델의 적중 0회를 실제 비치명 생존 분기와 비교하지 않는다.
                    // 실측한 강공 에너지로 2/3연타 분기를 정하고 예약 시간표 자체를 대조한다.
                    int first = lastAmount > tuning.maximumEnergy + .0001f ? 0 : 1;
                    modelDerived = Enumerable.Range(first + 1, 2 - first).Select(i => LightTripleImpactScheduler.ResolveDelay(i, first)).ToList();
                    timingSource = "빛 실측 에너지 조건의 예약 시간표";
                }
                bool died = enemy.Health.IsDead;
                string timingStatus = CombatBalanceMeasurementStatus.OfFollowupTimes(measuredDerived, modelDerived, died, out float worst);
                s.checks.Add(timingStatus + $" 강공 후속 타격 횟수·시각(±0.15초, {timingSource}) 실측 {measuredDerived.Count}회 {R(measuredDerived.FirstOrDefault())}~{R(measuredDerived.LastOrDefault())}초 / 모델 {modelDerived.Count}회 {R(modelDerived.FirstOrDefault())}~{R(modelDerived.LastOrDefault())}초, 최대 차이 {R(worst)}초{(died ? ", 대상 사망" : "")}{(timingStatus == "NOT_RUN" ? ", 후속 적중 미관측" : "")}");
                if (timingStatus == "FAIL") s.status = "FAIL";
            }
            // 받는 피해: 실제 ResolveDamage·CombatHealth.TakeDamage 경로(방어·하한)를 직접 호출한다.
            // 몬스터 공격 실행(사거리·타이밍·여러 타·겹침)은 이 검사 범위 밖이다. 플레이어 사망 방지가 켜져 있다.
            Incoming(s, enemyModel, enemy, actor, plan.level);
            // 시간·판정은 모델 정확도 검사다(피해 경로와 분리해 기록). 치명 여부로 필요한 판정 수가 달라지므로,
            // 실측 판정 수 N의 모델 시각(콤보 시간표)과 비교한다. 기대값 충전 시간은 참고로 함께 남긴다.
            float atN = CombatBalanceAnalysisModel.WeakPhaseTime(weapon, model.attackSpeed, (int)s.measuredChargePhases);
            bool timeOk = !float.IsNaN(atN) && !float.IsNaN(s.measuredChargeTime) && Mathf.Abs(s.measuredChargeTime - atN) <= Mathf.Max(.1f, atN * .15f);
            s.checks.Add((float.IsNaN(s.measuredChargeTime) ? "NOT_RUN" : timeOk ? "PASS" : "FAIL") + $" 콤보 시간표(±15%) 실측 {R(s.measuredChargePhases)}판정 {R(s.measuredChargeTime)}초 / 모델 {R(s.measuredChargePhases)}번째 판정 {R(atN)}초 (기대 충전 {R(s.modelChargeTime)}초·{R(s.modelChargePhases)}판정, 치명 {s.measuredCrits}회)");
            s.checks.Add((s.modelPrepSurvived == s.measuredPrepSurvived ? "PASS" : "FAIL") + $" 충전 중 대상 생존(에너지 완충까지 몬스터가 살아 있음) 모델 {(s.modelPrepSurvived ? "생존" : "사망")}/실측 {(s.measuredPrepSurvived ? "생존" : "사망")}");
            if (s.measuredPrepSurvived)
            {
                string observed = $"실측 {(s.measuredHeavyKilled ? "처치" : "실패")}(강공 치명 {(heavyHit != null && heavyHit.critical ? "예" : "아니오")})";
                if (modelKillChance > 0f && modelKillChance < 1f)
                    s.checks.Add($"NOT_RUN 1주기 처치 모델 확률 {R(modelKillChance * 100f)}% / {observed} — 한 번의 관측으로는 확률을 판정할 수 없다(보고서 확률 요약 참고)");
                else
                {
                    bool consistent = (modelKillChance >= 1f) == s.measuredHeavyKilled;
                    s.checks.Add((consistent ? "PASS" : "FAIL") + $" 1주기 처치 확정 예측 모델 {R(modelKillChance * 100f)}% / {observed}");
                }
            }
            s.status = CombatBalanceMeasurementStatus.OfScenario(s);
            s.note = $"판정 {s.status}, 틱 {R(s.measuredDot)}, 파생 {R(s.measuredDerived)}, 경과 {R(s.measuredElapsed)}초";
        }

        static void Incoming(MeasurementScenario s, EnemyView enemyModel, EnemyActor enemy, PlayerActorRuntime actor, int level)
        {
            void One(EnemyAttackView view, bool strong)
            {
                if (view == null) { s.checks.Add("NOT_RUN 받는 " + (strong ? "강공" : "평타") + "(해당 공격 없음)"); return; }
                EnemyAbilityDefinition ability = null;
                var set = enemyModel.definition.AbilitySet;
                for (int i = 0; i < set.Count; i++) if (set.GetAbility(i) != null && set.GetAbility(i).AbilityId == view.abilityId) ability = set.GetAbility(i);
                actor.Health.SetMaxHp(actor.Health.MaxHp, true);
                float before = actor.Health.CurrentHp;
                float raw = ability.ResolveDamage(level) * enemy.RuntimeStats.DamageMultiplier;
                Vector3 dir = actor.transform.position - enemy.transform.position; dir.y = 0f;
                actor.Health.TakeDamage(new DamageInfo(raw, actor.transform.position, enemy.gameObject, dir.normalized, enemyAbility: ability));
                float measured = before - actor.Health.CurrentHp;
                if (strong) { s.modelIncomingStrong = view.perHit; s.measuredIncomingStrong = measured; }
                else { s.modelIncomingNormal = view.perHit; s.measuredIncomingNormal = measured; }
                bool ok = Mathf.Abs(measured - view.perHit) < .51f;
                s.checks.Add((ok ? "PASS" : "FAIL") + $" 받는 {(strong ? "강공" : "평타")} 계산 경로(TakeDamage 직접 호출) {view.abilityId} 모델 {R(view.perHit)}/실측 {R(measured)}");
                if (!ok) s.status = "FAIL";
                actor.Health.SetMaxHp(actor.Health.MaxHp, true);
            }
            One(enemyModel.worstNormal, false);
            One(enemyModel.worstStrong, true);
        }

        static IEnumerator Crowd(Plan plan, MeasurementScenario s, CombatBalanceAnalysisModel.Catalog catalog, AnalysisConditions c, WeaponItemData weapon,
            PlayerInputFacade player, PlayerActorRuntime actor, MeleeRuntime melee, MeasureInput input, EnemySpawnService spawn, Vector3 origin, List<EnemyActor> leased)
        {
            if (!Prepare(plan, s, catalog, c, weapon, actor, out var model)) yield break;
            if (!catalog.representative.TryGetValue(plan.enemy, out var def)) { s.status = "BLOCKED"; s.note = "대표 몬스터 없음"; yield break; }
            var enemyModel = CombatBalanceAnalysisModel.BuildEnemy(def, plan.level, model);
            var copy = c.Clone(); copy.crowdCount = plan.crowdCount;
            var outcome = CombatBalanceAnalysisModel.Simulate(model, enemyModel, weapon, copy, CombatMode.Crowd);
            s.enemyId = def.EnemyId; s.modelEnemyHealth = enemyModel.maxHealth; s.modelClearTime = outcome.killTime;
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            Warp(player, origin, Vector3.forward); yield return null;
            for (int i = 0; i < plan.crowdCount; i++)
            {
                float angle = plan.angleOffset + i * 2.39996f, r = (2.2f + Mathf.Sqrt(i) * .9f) * plan.spreadScale;
                var enemy = SpawnEnemy(spawn, def, origin + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r), player.transform, plan.level, true);
                if (enemy == null) { s.status = "BLOCKED"; s.note = "스폰 실패 " + i; yield break; }
                leased.Add(enemy);
            }
            float start = Time.time; float weak = 0, heavy = 0, derived = 0, dot = 0;
            void Hit(CombatHealth h, DamageInfo d, float actual, bool fatal)
            {
                if (d.isDamageOverTime) dot += actual; else if ((d.playerAttackKind & PlayerAttackKind.Heavy) != 0) heavy += actual;
                else if ((d.playerAttackKind & PlayerAttackKind.Weak) != 0) weak += actual; else derived += actual;
            }
            foreach (var e in leased) e.Health.OnDamageResolved += Hit;
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>(); energy.Clear();
            float clear = float.NaN;
            while (Time.time < start + 40f)
            {
                var alive = leased.Where(e => e.IsLeased && !e.Health.IsDead).ToList();
                if (alive.Count == 0) { clear = Time.time - start; break; }
                var nearest = alive.OrderBy(e => (e.transform.position - player.transform.position).sqrMagnitude).First();
                input.Aim = nearest.transform.position;
                bool full = energy.Amount >= OverburstElementTuning.Current.maximumEnergy - .01f;
                input.Heavy = full && !melee.IsHeavyAttackInProgress; input.Weak = !full;
                yield return null;
            }
            input.Weak = input.Heavy = false;
            foreach (var e in leased) if (e != null) e.Health.OnDamageResolved -= Hit;
            float total = Mathf.Max(1f, weak + heavy + derived + dot);
            s.measuredElapsed = float.IsNaN(clear) ? Time.time - start : clear;
            s.measuredDot = dot; s.measuredDerived = derived;
            // 허용오차 기준(Codex 검토 10-01): |모델−실측|/실측 ≤ 50%. 모델 근사가 크게 벗어났는지 알리는 경고 기준이다.
            float relative = float.IsNaN(clear) ? float.NaN : Mathf.Abs(outcome.killTime - clear) / Mathf.Max(.01f, clear);
            s.checks.Add((float.IsNaN(clear) ? "FAIL" : relative <= .5f ? "PASS" : "FAIL")
                + $" 군집 {plan.crowdCount}마리 정리({plan.role}, |모델−실측|/실측 ≤50%) 실측 {(float.IsNaN(clear) ? "미완료(40초)" : R(clear) + "초")} / 모델 {R(outcome.killTime)}초"
                + (float.IsNaN(relative) ? "" : $", 차이 {R(relative * 100f)}%·모델/실측 {R(outcome.killTime / Mathf.Max(.01f, clear))}배"));
            s.checks.Add($"{(Mathf.Abs((heavy + derived) / total * 100f - (outcome.heavy + outcome.derived) / Mathf.Max(1f, outcome.Total) * 100f) <= 20f ? "PASS" : "FAIL")} 범위 기여(강공+파생) 실측 {R((heavy + derived) / total * 100f)}% / 모델 {R((outcome.heavy + outcome.derived) / Mathf.Max(1f, outcome.Total) * 100f)}% (±20%p)");
            s.status = CombatBalanceMeasurementStatus.OfScenario(s);
            s.note = $"{plan.role} 배치(시드 +{plan.seedOffset}, 반경 ×{R(plan.spreadScale)}). 실측 약공 {R(weak)} 강공 {R(heavy)} 파생 {R(derived)} 틱 {R(dot)}; 적 AI 켬(접근·공격), 모델은 균일 밀도 근사";
        }
    }
}
