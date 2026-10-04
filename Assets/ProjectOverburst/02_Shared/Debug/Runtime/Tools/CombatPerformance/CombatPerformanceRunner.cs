#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Overburst.DebugTools.Performance
{
    [DefaultExecutionOrder(-31000)]
    public sealed class CombatPerformanceRunner : MonoBehaviour
    {
        public static CombatPerformanceRunner Current { get; private set; }
        public static CombatPerformanceRun LastRun { get; private set; }
        public CombatPerformanceRun Run { get; private set; }
        public string Progress { get; private set; } = "준비";
        public bool Finished { get; private set; }
        public double ElapsedSeconds => Math.Max(0, Time.realtimeSinceStartupAsDouble - began);
        public event Action<CombatPerformanceRun> Completed;
        readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        readonly List<EnemyActor> enemies = new List<EnemyActor>(500);
        readonly List<CombatHealth> observationBindings = new List<CombatHealth>();
        readonly Dictionary<WeaponElement, ItemData> gems = new Dictionary<WeaponElement, ItemData>();
        CombatPerformanceRecorder recorder;
        CombatPerformanceSegment segment;
        PlayerActorRuntime player; MeleeRuntime melee; PlayerEvadeController evade; PlayerInputFacade input;
        EnemySpawnService spawner; GameObject ownedRoot; BloodHitVfxService blood;
        WeaponItemData weapon; ItemData oldWeapon, oldGem;
        float oldHp; bool enteredArena, inputOwned, oldGameplay, oldBackground, settingsOwned, playerOwned;
        int oldVsync, oldTarget, releaseKeyboardFrame = -1, poolStart, parryStart, actionOrdinal;
        Keyboard keyboard; Key evadeKey;
        ReadOnlyArray<InputDevice>? oldDevices;
        UnityEngine.Random.State oldRandom;
        double deadline, began, nextAttack;
        CombatHealth observationPlayerHealth;
        WeaponActionHandle lightHandle;
        bool awaitingHeavy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; LastRun = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootstrapPlayer()
        {
#if !UNITY_EDITOR
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--overburst-combat-perf");
            if (i < 0 || i + 1 >= args.Length) return;
            try
            {
                var profile = JsonUtility.FromJson<CombatPerformanceProfile>(File.ReadAllText(args[i + 1]));
                int o = Array.IndexOf(args, "--overburst-perf-output");
                string folder = o >= 0 && o + 1 < args.Length ? args[o + 1] : Path.Combine(CombatPerformancePaths.OutputRoot, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_Player_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                StartRun(profile, folder);
            }
            catch (Exception error) { UnityEngine.Debug.LogException(error); }
#endif
        }
        public static CombatPerformanceRunner StartRun(CombatPerformanceProfile profile, string output, WeaponItemData selectedWeapon = null,
            string revision = "", string fingerprint = "")
        {
            if (!Application.isPlaying || Current != null) throw new InvalidOperationException("Play 중이며 실행 중인 성능 검사가 없어야 합니다.");
            profile = JsonUtility.FromJson<CombatPerformanceProfile>(JsonUtility.ToJson(profile)); profile.Validate();
            output = CombatPerformancePaths.RequireOutput(output);
            if (File.Exists(Path.Combine(output, "run.json"))) throw new IOException("기존 회차 폴더를 덮어쓸 수 없습니다.");
            if (profile.mode == CombatPerformanceMode.Automated)
            {
                string account = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
                if (string.IsNullOrWhiteSpace(account) || !string.Equals(CombatPerformancePaths.RequireOutput(account), Path.Combine(output, "IsolatedAccount"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("자동 전투는 이 회차의 IsolatedAccount로 부팅한 Play에서만 실행합니다.");
            }
            var root = new GameObject("OVERBURST Combat Performance") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(root);
            var runner = root.AddComponent<CombatPerformanceRunner>(); Current = runner;
            runner.weapon = selectedWeapon;
            runner.Run = new CombatPerformanceRun { id = Path.GetFileName(output), output = output, profile = profile,
                startedUtc = DateTime.UtcNow.ToString("O"), sourceRevision = revision, contentFingerprint = fingerprint,
                profileHash = CombatPerformancePaths.Hash(profile.WorkloadJson()), origin = Application.isEditor ? "Editor" : "DevelopmentPlayer" };
            runner.began = Time.realtimeSinceStartupAsDouble;
            runner.deadline = runner.began + (profile.mode == CombatPerformanceMode.Observation ? profile.observationSeconds + 120 : Math.Max(600, profile.soakSeconds + runner.EstimatedSeconds() + 120));
            runner.oldRandom = UnityEngine.Random.state; runner.oldBackground = Application.runInBackground;
            Application.runInBackground = true;
            Application.logMessageReceived += runner.OnLog;
            runner.routines.Push(runner.Execute());
            try
            {
                CombatPerformancePaths.SaveJson(Path.Combine(output, "profile.json"), profile);
                CombatPerformancePaths.SaveJson(Path.Combine(output, "run.json"), runner.Run);
            }
            catch (Exception error)
            {
                runner.Finish("FAILED", "시작 파일 저장 실패: " + error);
                throw;
            }
            return runner;
        }
        double EstimatedSeconds()
        {
            var p = Run.profile;
            return p.themes.Length * p.enemyCounts.Length * p.layouts.Length * p.elements.Length * p.repetitions
                * (p.approachSeconds + p.warmupSeconds + p.measureSeconds * (p.preparedElementStress ? 2 : 1) + p.tailSeconds * 2 + 15);
        }
        void Update()
        {
            if (Finished || Run == null) return;
            recorder?.Tick();
            if (keyboard != null && releaseKeyboardFrame >= 0 && Time.frameCount >= releaseKeyboardFrame)
            { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); releaseKeyboardFrame = -1; }
            try
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("성능 검사 제한 시간이 지났습니다.");
                int steps = 0;
                while (routines.Count > 0 && steps++ < 32)
                {
                    var top = routines.Peek();
                    if (!top.MoveNext()) { (top as IDisposable)?.Dispose(); routines.Pop(); continue; }
                    if (top.Current is IEnumerator nested) { routines.Push(nested); continue; }
                    return;
                }
                if (routines.Count == 0) Finish("COMPLETE", "");
            }
            catch (Exception error) { Finish("FAILED", error.ToString()); }
        }
        void LateUpdate()
        {
            if (recorder == null) return;
            long started = Stopwatch.GetTimestamp();
            recorder.Observe(Alive()); recorder.AddObserverTicks(started);
        }
        IEnumerator Execute()
        {
            if (Run.profile.mode == CombatPerformanceMode.Observation)
            {
                BindObservationPlayer();
                CaptureEnvironment();
                while (Time.realtimeSinceStartupAsDouble - began < Run.profile.observationSeconds)
                {
                    BindObservationPlayer();
                    blood = UnityEngine.Object.FindFirstObjectByType<BloodHitVfxService>();
                    // Bind once per chunk, outside sampling. New targets in this chunk may have incomplete event coverage.
                    foreach (var h in UnityEngine.Object.FindObjectsByType<CombatHealth>(FindObjectsSortMode.None))
                    { h.OnDamageResolved += ObservationHit; observationBindings.Add(h); }
                    yield return Measure("observation", "FixedBindings; new targets may lack hit events", 0, "", CombatPerformanceLayout.Dense, WeaponElement.None, 0, 0,
                        (float)Math.Min(30, Run.profile.observationSeconds - (Time.realtimeSinceStartupAsDouble - began)), false);
                    UnbindObservation();
                    yield return null;
                }
                yield break;
            }
            Progress = "플레이어와 하이드아웃 로딩 대기";
            double readyDeadline = Time.realtimeSinceStartupAsDouble + 90;
            while (PlayerContext.GetOrCreate().CurrentActor == null || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            { if (Time.realtimeSinceStartupAsDouble > readyDeadline) throw new TimeoutException("하이드아웃 준비 실패"); yield return null; }
            Prepare(); yield return Wait(Run.profile.warmupSeconds);
            CaptureEnvironment();
            yield return Measure("baseline", "NoEnemies", 0, "", CombatPerformanceLayout.Dense, WeaponElement.None, 0, 0, Run.profile.baselineSeconds, false);
            int cycle = 0;
            double soakBegan = Time.realtimeSinceStartupAsDouble;
            do
            {
                foreach (string themeId in Run.profile.themes)
                foreach (int count in Run.profile.enemyCounts)
                foreach (CombatPerformanceLayout layout in Run.profile.layouts)
                foreach (WeaponElement element in Run.profile.elements)
                for (int repeat = 0; repeat < Run.profile.repetitions; repeat++)
                {
                    if (Run.profile.soakSeconds > 0 && cycle > 0 && Time.realtimeSinceStartupAsDouble - soakBegan >= Run.profile.soakSeconds) yield break;
                    var theme = MapThemeCatalog.Resolve(themeId);
                    if (theme == null || !theme.Validate(out string reason)) throw new InvalidOperationException("테마가 유효하지 않습니다: " + themeId);
                    ReleaseEnemies(); yield return Wait(.2f);
                    DebugTeleport.To(new Vector3(1000, .2f, 985));
                    melee.CancelCurrentAttackState(); player.Equipment.SetElementGem(gems[element]);
                    lightHandle = WeaponActionHandle.Invalid; awaitingHeavy = false;
                    UnityEngine.Random.InitState(Run.profile.seed);
                    if (!player.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common))) throw new InvalidOperationException("무기 장착 실패");
                    yield return null; yield return null;
                    if (player.Equipment.ActiveElement != element) throw new InvalidOperationException("원소 보석 동기화 실패: " + element);
                    var roster = theme.BuildRoster(count - Mathf.RoundToInt(count * .2f) - Math.Max(1, count / 50), Mathf.RoundToInt(count * .2f), Math.Max(1, count / 50), Run.profile.seed);
                    if (!spawner.RegisterAdditionalCatalog(theme.Catalog, out reason)) throw new InvalidOperationException(reason);
                    var ids = new string[roster.Count]; for (int n = 0; n < ids.Length; n++) ids[n] = roster[n].EnemyId;
                    Run.rosterIds = ids;
                    Begin("spawn", "ProductionSpawn", count, themeId, layout, element, repeat, cycle);
                    yield return null;
                    for (int i = 0; i < count; i++)
                    {
                        float angle = i * 2.399963f, radius = layout == CombatPerformanceLayout.Dense ? 2.8f + i % 5 * .3f : 6 + i % 7 * 1.2f;
                        Vector3 point = player.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                        int ground = LayerMask.NameToLayer("Ground");
                        if (Physics.Raycast(point + Vector3.up * 5, Vector3.down, out var hit, 12, ground >= 0 ? 1 << ground : Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) point.y = hit.point.y + .1f;
                        var enemy = spawner.Spawn(new EnemySpawnRequest(roster[i], point, Quaternion.identity, player.transform, ownedRoot, player.transform, ownedRoot.transform, seed: Run.profile.seed + i));
                        if (enemy == null) throw new InvalidOperationException("스폰 실패: " + i);
                        enemy.Health.SetMaxHp(Run.profile.enemyHp, true); enemy.Health.OnDamageResolved += EnemyHit; enemies.Add(enemy);
                        if ((i + 1) % Run.profile.spawnPerFrame == 0) yield return null;
                    }
                    yield return null; yield return null; End();
                    yield return Measure("approach", "ProductionAI; elevatedHP", count, themeId, layout, element, repeat, cycle, Run.profile.approachSeconds, false);
                    yield return Measure("first-action", cycle == 0 && repeat == 0 ? "FirstActionForThisRecipeEntry; not process-cold" : "ReusedRuntime", count, themeId, layout, element, repeat, cycle, 3, true);
                    yield return Wait(Run.profile.warmupSeconds);
                    yield return Measure("sustained", "PublicWeaponActions+KeyboardEvade; elevatedHP", count, themeId, layout, element, repeat, cycle, Run.profile.measureSeconds, true);
                    if (Run.profile.preparedElementStress)
                        yield return Measure("prepared-element", "FixtureStatus+FixtureEnergy+PublicHeavy; elevatedHP", count, themeId, layout, element, repeat, cycle, Run.profile.measureSeconds, true, true);
                    if (Run.profile.deathBurst)
                    {
                        // Discover pre-existing pickups outside sampling; only new arena drops belong to this wave.
                        var priorPickups = new HashSet<int>();
                        foreach (var pickup in UnityEngine.Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None)) priorPickups.Add(pickup.GetInstanceID());
                        foreach (var pickup in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None)) priorPickups.Add(pickup.GetInstanceID());
                        Vector3 pickupCenter = player.transform.position;
                        Begin("death-burst", "FixtureFatalDamage; production death/loot", count, themeId, layout, element, repeat, cycle);
                        yield return null;
                        melee.CancelCurrentAttackState();
                        foreach (var enemy in enemies) if (enemy != null && !enemy.Health.IsDead)
                        { recorder.Event(6, enemy.GetInstanceID()); enemy.Health.TakeDamage(new DamageInfo(Run.profile.enemyHp * 100, enemy.transform.position, player.gameObject, Vector3.forward)); }
                        yield return Wait(Run.profile.tailSeconds); End();
                        // Discover new drop objects before measuring real pickup calls.
                        var currency = UnityEngine.Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None);
                        var items = UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None);
                        Begin("pickup-save-tail", "ProductionPickup; isolated account", count, themeId, layout, element, repeat, cycle);
                        yield return null;
                        foreach (var pickup in currency) if (pickup != null && !priorPickups.Contains(pickup.GetInstanceID()) && (pickup.transform.position - pickupCenter).sqrMagnitude < 2500) { segment.pickupsRequested++; if (pickup.TryPickup()) segment.pickupsCollected++; }
                        foreach (var pickup in items) if (pickup != null && !priorPickups.Contains(pickup.GetInstanceID()) && (pickup.transform.position - pickupCenter).sqrMagnitude < 2500) { segment.pickupsRequested++; if (pickup.TryPickup()) segment.pickupsCollected++; }
                        yield return Wait(Run.profile.tailSeconds); End();
                    }
                    ReleaseEnemies(); yield return Wait(.2f);
                    yield return Measure("post-release", "PoolReuseAndResidualMemory", count, themeId, layout, element, repeat, cycle, 1, false);
                }
                cycle++;
            } while (Run.profile.soakSeconds > 0 && Time.realtimeSinceStartupAsDouble - soakBegan < Run.profile.soakSeconds);
        }
        void Prepare()
        {
            if (EnemyThemeTrialService.InArena || EnemyThemeTrialService.Busy) throw new InvalidOperationException("기존 시험장을 먼저 종료하세요.");
            player = PlayerContext.GetOrCreate().CurrentActor; melee = player.GetComponent<MeleeRuntime>(); input = player.GetComponent<PlayerInputFacade>();
            evade = player.GetComponent<PlayerEvadeController>();
            if (melee == null || input == null) throw new InvalidOperationException("전투/입력 구성 누락");
            if (weapon == null && !string.IsNullOrEmpty(Run.profile.weaponResourcePath)) weapon = Resources.Load<WeaponItemData>(Run.profile.weaponResourcePath);
            if (weapon == null)
            {
                Resources.Load<Overburst.Persistence.AccountContentRegistry>(Overburst.Persistence.AccountContentRegistry.ResourcePath);
                string name = Path.GetFileNameWithoutExtension(Run.profile.weaponAssetPath);
                foreach (var w in Resources.FindObjectsOfTypeAll<WeaponItemData>()) if (w.name == name) { weapon = w; break; }
            }
            if (weapon == null || weapon.GetMeleeDefinition()?.heavyAttackDefinition == null) throw new InvalidOperationException("검사 무기/강공 자산이 없습니다. 프로필 무기 경로를 확인하세요.");
            foreach (var element in Run.profile.elements)
            {
                ElementGemItemData best = null;
                foreach (var data in Resources.LoadAll<ElementGemItemData>("Items/ElementGems"))
                    if (data.element == element && (best == null || data.fixedGrade < best.fixedGrade)) best = data;
                if (best == null) throw new InvalidOperationException("원소 보석 누락: " + element);
                var item = new ItemData(best, 1, best.fixedGrade); item.gemState = ElementGemQuality.Roll(best, Run.profile.seed); gems[element] = item;
            }
            oldWeapon = player.Equipment.CurrentWeaponItem; oldGem = player.Equipment.EquippedElementGem; oldHp = player.GetComponent<CombatHealth>().MaxHp;
            playerOwned = true;
            oldVsync = QualitySettings.vSyncCount; oldTarget = Application.targetFrameRate; settingsOwned = true;
            if (Run.profile.uncapped) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
            var result = EnemyThemeTrialService.ToggleArena(); if (!result.Success) throw new InvalidOperationException(result.Message); enteredArena = true;
            foreach (var arena in UnityEngine.Object.FindObjectsByType<EnemyThemeDebugArena>(FindObjectsSortMode.None))
                foreach (var zone in arena.GetComponentsInChildren<EnemyThemeTriggerZone>(true)) if (zone.TryGetComponent(out Collider c)) c.enabled = false;
            player.GetComponent<CombatHealth>().SetMaxHp(10000000, true); player.GetComponent<CombatHealth>().OnDamageResolved += PlayerHit;
            oldGameplay = input.IsGameplayEnabled; oldDevices = input.GameplayMap.devices; inputOwned = true;
            keyboard = InputSystem.AddDevice<Keyboard>("OverburstPerformanceKeyboard");
            input.GameplayMap.devices = new InputDevice[] { keyboard }; input.EnableGameplay();
            if (!input.TryGetGameplayAction("Evade", out var evadeAction)) throw new InvalidOperationException("회피 입력이 없습니다.");
            bool keyFound = false;
            foreach (var binding in evadeAction.bindings)
            { string path = binding.effectivePath; if (path != null && path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(path.Substring(11), true, out evadeKey)) { keyFound = true; break; } }
            if (!keyFound) throw new InvalidOperationException("현재 회피 키보드 바인딩을 재현할 수 없습니다.");
            if (evade != null) { evade.OnEvadeStarted += EvadeStarted; evade.OnEvadeEnded += EvadeEnded; }
            ownedRoot = new GameObject("CombatPerformance owned enemies");
            spawner = EnemySpawnService.Current;
            if (spawner == null)
            {
                var inactive = new GameObject("inactive pool"); inactive.transform.SetParent(ownedRoot.transform); inactive.SetActive(false);
                var pool = ownedRoot.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
                spawner = ownedRoot.AddComponent<EnemySpawnService>(); spawner.Configure(MapThemeCatalog.Resolve(Run.profile.themes[0]).Catalog, pool);
            }
            blood = UnityEngine.Object.FindFirstObjectByType<BloodHitVfxService>();
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            Run.weapon = weapon.name;
        }
        void CaptureEnvironment()
        {
            Run.unityVersion = Application.unityVersion; Run.cpu = SystemInfo.processorType; Run.gpu = SystemInfo.graphicsDeviceName;
            Run.os = SystemInfo.operatingSystem; Run.graphicsApi = SystemInfo.graphicsDeviceType.ToString(); Run.scene = SceneManager.GetActiveScene().path;
            Run.width = Screen.width; Run.height = Screen.height; Run.vSync = QualitySettings.vSyncCount; Run.targetFrameRate = Application.targetFrameRate;
            Run.quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            Run.renderPipeline = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "BuiltIn";
            var camera = Camera.main; Run.camera = camera == null ? "UNAVAILABLE" : camera.orthographic ? "Orthographic:" + camera.orthographicSize : "Perspective:" + camera.fieldOfView;
            Run.effectSettings = JsonUtility.ToJson(CombatEffectDiagnosticControls.Snapshot());
            var settings = new System.Text.StringBuilder();
            foreach (var property in typeof(OverburstGameSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (property.PropertyType.IsPrimitive || property.PropertyType.IsEnum)
                    settings.Append(property.Name).Append('=').Append(Convert.ToString(property.GetValue(null), System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            foreach (var property in typeof(QualitySettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (property.CanRead && (property.PropertyType.IsPrimitive || property.PropertyType.IsEnum))
                    settings.Append("Quality.").Append(property.Name).Append('=').Append(Convert.ToString(property.GetValue(null), System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            if (GraphicsSettings.currentRenderPipeline != null) settings.Append("Pipeline=").Append(JsonUtility.ToJson(GraphicsSettings.currentRenderPipeline));
            Run.gameSettings = settings.ToString();
            Run.inputBindings = input != null && input.RuntimeAsset != null ? CombatPerformancePaths.Hash(input.RuntimeAsset.ToJson()) : "UNAVAILABLE";
            Run.environmentHash = CombatPerformancePaths.Hash(Run.origin + Run.unityVersion + Run.cpu + Run.gpu + Run.os + Run.graphicsApi + Run.scene
                + Run.width + "/" + Run.height + "/" + Run.vSync + "/" + Run.targetFrameRate + Run.quality + Run.renderPipeline + Run.camera + Run.effectSettings + Run.gameSettings + Run.inputBindings
                + Time.fixedDeltaTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + Time.maximumDeltaTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + Physics.gravity.ToString("F6"));
            Run.workloadSignature = CombatPerformancePaths.Hash(Run.profileHash + Run.environmentHash + Run.weapon);
        }
        IEnumerator Measure(string scenario, string mode, int count, string theme, CombatPerformanceLayout layout, WeaponElement element, int repeat, int cycle, float seconds, bool attacks, bool prepared = false)
        {
            yield return null; Begin(scenario, mode, count, theme, layout, element, repeat, cycle); yield return null;
            double end = Time.realtimeSinceStartupAsDouble + seconds; nextAttack = Time.realtimeSinceStartupAsDouble;
            actionOrdinal = 0;
            while (Time.realtimeSinceStartupAsDouble < end && !recorder.CapacityReached)
            {
                PollCompletion();
                if (attacks && Time.realtimeSinceStartupAsDouble >= nextAttack && (scenario != "first-action" || actionOrdinal == 0))
                {
                    RequestAction(prepared); nextAttack += Run.profile.attackInterval;
                    if (nextAttack < Time.realtimeSinceStartupAsDouble) nextAttack = Time.realtimeSinceStartupAsDouble + Run.profile.attackInterval;
                }
                PollCompletion(); yield return null;
            }
            PollCompletion(); End();
        }
        void Begin(string scenario, string mode, int count, string theme, CombatPerformanceLayout layout, WeaponElement element, int repeat, int cycle)
        {
            if (recorder != null) throw new InvalidOperationException("측정 구간 중첩");
            if (Run.segments.Count >= Run.profile.segmentLimit) throw new InvalidOperationException("최대 구간 수에 도달했습니다. 저장된 구간을 보존하고 종료합니다.");
            segment = new CombatPerformanceSegment { scenario = scenario, inputMode = mode, theme = theme, layout = layout.ToString(), element = element.ToString(),
                repetition = repeat, cycle = cycle, expectedEnemies = count, aliveAtStart = Alive(), key = scenario + "/" + theme + "/" + count + "/" + layout + "/" + element + "/" + mode };
            segment.folder = "Segments/" + Run.segments.Count.ToString("D5") + "_" + scenario;
            segment.rosterSignature = CombatPerformancePaths.Hash(string.Join("|", Run.rosterIds ?? Array.Empty<string>()));
            poolStart = spawner != null ? spawner.Pool.CreatedCount : 0; parryStart = player != null ? player.GetComponent<PlayerParryController>()?.SuccessCount ?? 0 : 0;
            Progress = scenario + " · " + theme + " · " + count + " · " + element + " · 반복 " + (repeat + 1);
            recorder = new CombatPerformanceRecorder(Run.profile, segment, blood);
        }
        void End()
        {
            if (recorder == null) return;
            segment.aliveAtEnd = Alive(); segment.poolCreated = spawner != null ? spawner.Pool.CreatedCount - poolStart : 0;
            segment.inFlightAtEnd = melee != null && melee.IsAttackInProgress ? 1 : 0;
            segment.poolPendingEnd = spawner != null ? spawner.Pool.PendingReturnCount : 0;
            segment.parries = player != null ? (player.GetComponent<PlayerParryController>()?.SuccessCount ?? 0) - parryStart : 0;
            if (segment.scenario == "sustained" && (segment.accepted == 0 || segment.enemyHits == 0)) { segment.validity = "NO_COMBAT"; segment.reason = "실제 공격 수락/적중 없음"; }
            recorder.End(Path.Combine(Run.output, segment.folder)); recorder.Dispose(); recorder = null;
            Run.segments.Add(segment); segment = null;
            CombatPerformancePaths.SaveJson(Path.Combine(Run.output, "run.json"), Run); // Checkpoint outside the measured interval.
        }
        void RequestAction(bool prepared)
        {
            long pilotStarted = Stopwatch.GetTimestamp();
            Vector3 direction = Vector3.forward; float best = float.MaxValue;
            foreach (var enemy in enemies) if (enemy != null && !enemy.Health.IsDead)
            { var delta = enemy.transform.position - player.transform.position; delta.y = 0; if (delta.sqrMagnitude < best) { best = delta.sqrMagnitude; direction = delta.normalized; } }
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            recorder.AddObserverTicks(pilotStarted);
            segment.requests++; recorder.Event(1);
            int ordinal = actionOrdinal++;
            if (!prepared && ordinal % 8 == 7 && keyboard != null)
            { InputSystem.QueueStateEvent(keyboard, new KeyboardState(evadeKey)); releaseKeyboardFrame = Time.frameCount + 1; return; }
            WeaponActionResult result;
            if (prepared || ordinal % 4 == 3)
            {
                if (prepared)
                {
                    recorder.Event(7);
                    foreach (var enemy in enemies) if (enemy != null && !enemy.Health.IsDead)
                    {
                        var status = enemy.GetComponent<ElementalStatusController>();
                        if (status != null) for (int s = 0; s < 5; s++) status.TryApplyDirectHit(new ElementalStatusApplication(player.Equipment.ActiveElement, 100, player.gameObject,
                            player.Equipment.CurrentWeaponItem.runtimeInstanceId, true, false, enemy.transform.position, direction));
                    }
                    var energy = player.GetComponent<OverburstElementEnergy>();
                    if (energy != null) { energy.Clear(); energy.BindWeapon(player.Equipment.CurrentWeaponItem.runtimeInstanceId, player.Equipment.ActiveElement);
                        for (int n = 0; n < 10; n++) energy.RecordConfirmedHit(player.Equipment.CurrentWeaponItem.runtimeInstanceId, player.Equipment.ActiveElement, n + 1, 10); }
                }
                result = melee.TryStartHeavyAttack(direction); if (result == WeaponActionResult.Accepted) awaitingHeavy = true;
            }
            else
            {
                result = melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput, null, direction), out var requestedHandle);
                if (result == WeaponActionResult.Accepted) lightHandle = requestedHandle;
            }
            if (result == WeaponActionResult.Accepted) { segment.accepted++; recorder.Event(2, attack: prepared ? 2 : 1); }
            else { segment.rejected++; recorder.Event(3, attack: (int)result); }
        }
        void PollCompletion()
        {
            if (lightHandle.IsValid && melee != null && melee.TryGetActionState(lightHandle, out var state) && state.IsTerminal)
            { if (state.Completed) segment.completed++; else segment.cancelled++; lightHandle = WeaponActionHandle.Invalid; }
            if (awaitingHeavy && melee != null && !melee.IsAttackInProgress) { segment.completed++; awaitingHeavy = false; }
        }
        static IEnumerator Wait(float seconds)
        { double end = Time.realtimeSinceStartupAsDouble + seconds; while (Time.realtimeSinceStartupAsDouble < end) yield return null; }
        int Alive()
        {
            if (Run.profile.mode == CombatPerformanceMode.Observation) return EnemyAIController.AliveEnemyCount;
            int count = 0; foreach (var e in enemies) if (e != null && e.gameObject.activeInHierarchy && !e.Health.IsDead) count++; return count;
        }
        void BindObservationPlayer()
        {
            if (evade != null) { evade.OnEvadeStarted -= EvadeStarted; evade.OnEvadeEnded -= EvadeEnded; }
            player = PlayerContext.GetOrCreate().CurrentActor;
            input = player != null ? player.GetComponent<PlayerInputFacade>() : null;
            melee = player != null ? player.GetComponent<MeleeRuntime>() : null;
            observationPlayerHealth = player != null ? player.GetComponent<CombatHealth>() : null;
            evade = player != null ? player.GetComponent<PlayerEvadeController>() : null;
            if (evade != null) { evade.OnEvadeStarted += EvadeStarted; evade.OnEvadeEnded += EvadeEnded; }
        }
        void EnemyHit(CombatHealth health, DamageInfo info, float amount, bool fatal)
        { if (segment == null || amount <= 0) return; segment.enemyHits++; if (fatal) segment.kills++; if (info.isCritical) segment.criticalHits++; if (info.isDamageOverTime) segment.dotHits++;
            if ((info.playerAttackKind & PlayerAttackKind.Elemental) != 0) segment.derivedHits++; recorder?.Event(4, health.GetInstanceID(), amount, (int)info.playerAttackKind, (int)info.element, fatal); }
        void PlayerHit(CombatHealth health, DamageInfo info, float amount, bool fatal)
        { if (segment == null || amount <= 0) return; segment.playerHits++; recorder?.Event(5, health.GetInstanceID(), amount, (int)info.playerAttackKind, (int)info.element, fatal); }
        void ObservationHit(CombatHealth h, DamageInfo d, float amount, bool fatal)
        { if (h == observationPlayerHealth) PlayerHit(h, d, amount, fatal); else EnemyHit(h, d, amount, fatal); }
        void EvadeStarted(PlayerEvadeType type) { if (segment != null) { segment.evades++; segment.accepted++; recorder?.Event(8, attack: (int)type); } }
        void EvadeEnded(PlayerEvadeType type) { if (segment != null) { segment.evadeCompleted++; recorder?.Event(9, attack: (int)type); } }
        void UnbindObservation() { foreach (var h in observationBindings) if (h != null) h.OnDamageResolved -= ObservationHit; observationBindings.Clear(); }
        void ReleaseEnemies()
        { foreach (var e in enemies) if (e != null) { e.Health.OnDamageResolved -= EnemyHit; if (e.IsLeased && spawner != null) spawner.Release(e); } enemies.Clear(); }
        void OnLog(string message, string stack, LogType type) { if (Run != null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) Run.logErrors++; }
        public void Stop() => Finish("CANCELLED", "사용자가 중단했습니다.");
        public void CompleteObservation()
        {
            if (Run.profile.mode != CombatPerformanceMode.Observation) throw new InvalidOperationException("수동 관찰에서만 완료할 수 있습니다.");
            Finish("COMPLETE", "사용자가 관찰을 종료했습니다.");
        }
        void OnDisable() { if (Run != null && !Finished) Finish("INTERRUPTED", "Play 종료/재로딩 또는 러너 비활성화"); }
        void CleanupStep(Action action)
        { try { action(); } catch (Exception error) { Run.status = "FAILED"; Run.reason += "\nCleanup: " + error; } }
        void Finish(string status, string reason)
        {
            if (Finished) return; Finished = true; Run.status = status; Run.reason = reason;
            try { End(); } catch (Exception error) { Run.status = "FAILED"; Run.reason += "\nExport: " + error; recorder?.Dispose(); recorder = null; }
            try
            {
                // Independent teardown steps: one failing pool return must not strand input or arena state.
                CleanupStep(UnbindObservation);
                foreach (var enemy in enemies) if (enemy != null) CleanupStep(() => {
                    enemy.Health.OnDamageResolved -= EnemyHit;
                    if (enemy.IsLeased && spawner != null) spawner.Release(enemy);
                });
                enemies.Clear();
                CleanupStep(() => { if (playerOwned && melee != null) melee.CancelCurrentAttackState(); });
                CleanupStep(() => { if (evade != null) { evade.OnEvadeStarted -= EvadeStarted; evade.OnEvadeEnded -= EvadeEnded; if (playerOwned) evade.CancelForKnockdown(); } });
                CleanupStep(() => { if (inputOwned && input != null) { input.GameplayMap.devices = oldDevices; input.CombatInputs?.Invalidate(); if (oldGameplay) input.EnableGameplay(); else input.DisableGameplay(); } });
                CleanupStep(() => { if (keyboard != null) InputSystem.RemoveDevice(keyboard); });
                CleanupStep(() => { if (playerOwned && player != null) { player.GetComponent<CombatHealth>().OnDamageResolved -= PlayerHit; player.GetComponent<CombatHealth>().SetMaxHp(oldHp, true); } });
                CleanupStep(() => { if (playerOwned && player != null) { player.Equipment.SetElementGem(oldGem); if (oldWeapon != null) player.Equipment.EquipWeaponItem(oldWeapon); else player.Equipment.ClearCurrentWeapon(); } });
                CleanupStep(() => { if (enteredArena && EnemyThemeTrialService.InArena) { var result = EnemyThemeTrialService.ToggleArena(); if (!result.Success) throw new InvalidOperationException(result.Message); } });
                CleanupStep(() => { if (ownedRoot != null) Destroy(ownedRoot); });
            }
            catch (Exception error) { Run.status = "FAILED"; Run.reason += "\nCleanup: " + error; }
            finally
            {
                if (settingsOwned) { QualitySettings.vSyncCount = oldVsync; Application.targetFrameRate = oldTarget; }
                if (Run.profile.mode == CombatPerformanceMode.Automated) UnityEngine.Random.state = oldRandom;
                Application.runInBackground = oldBackground; Application.logMessageReceived -= OnLog;
                while (routines.Count > 0) { try { (routines.Pop() as IDisposable)?.Dispose(); } catch { } }
                Run.endedUtc = DateTime.UtcNow.ToString("O"); LastRun = Run; Current = null;
            }
            try { CombatPerformancePaths.SaveJson(Path.Combine(Run.output, "run.json"), Run); CombatPerformanceReport.Write(Run); }
            catch (Exception error) { Run.status = "FAILED"; Run.reason += "\nReport: " + error; UnityEngine.Debug.LogException(error); }
            Progress = Run.status;
            Completed?.Invoke(Run);
            Destroy(gameObject);
        }
    }
}
#endif
