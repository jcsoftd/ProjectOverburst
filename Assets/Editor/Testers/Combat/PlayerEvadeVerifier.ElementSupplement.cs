using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;

// Uses the existing isolated player, product actions, deployed enemies and return contract.
public static partial class PlayerEvadeVerifier
{
    const string ElementSupplementKey = "Overburst.PlayerEvadeVerifier.ElementSupplement";
    [InitializeOnLoadMethod]
    static void RegisterElementSupplementReturn()
    {
        EditorApplication.playModeStateChanged -= ClearElementSupplementMode;
        EditorApplication.playModeStateChanged += ClearElementSupplementMode;
    }
    static void ClearElementSupplementMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(PendingKey, "") == "")
        { SessionState.EraseBool(ElementSupplementKey); SessionState.EraseString(ElementSupplementKey + ".Elements"); SessionState.EraseBool(ElementSupplementKey + ".ParryOnly"); }
    }
    [MenuItem("OVERBURST/검증/영상/원소 패링 3단계 촬영")]
    public static void CaptureParrySupplementMenu()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Video/ElementParry"));
        StartElementSupplementIsolated(Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss")), "Fire,Electric,Ice,Dark,Light", true);
    }
    public static void StartElementSupplementIsolated(string directory, string elements = "Fire,Ice,Electric,Dark,Light", bool parryOnly = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(SessionState.GetString(FacingQueueKey, ""))
            || !string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))
            || !string.IsNullOrEmpty(SessionState.GetString(ReturnKey, "")))
            throw new InvalidOperationException("The shared Editor and account must be idle and returned.");
        var parsed = elements.Split(',').Select(e => (WeaponElement)Enum.Parse(typeof(WeaponElement), e.Trim())).ToArray();
        if (parsed.Length == 0 || parsed.Any(e => e == WeaponElement.None)) throw new ArgumentException("Explicit elements required.");
        SessionState.SetString(ElementSupplementKey + ".Elements", string.Join(",", parsed));
        SessionState.SetBool(ElementSupplementKey, true);
        SessionState.SetBool(ElementSupplementKey + ".ParryOnly", parryOnly);
        try
        {
            StartSwordFacingIsolated(directory);
            SessionState.SetString(ReturnKey + ".Deadline", (EditorApplication.timeSinceStartup + 1800).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { SessionState.EraseBool(ElementSupplementKey); SessionState.EraseString(ElementSupplementKey + ".Elements"); SessionState.EraseBool(ElementSupplementKey + ".ParryOnly"); throw; }
    }

    static IEnumerator CaptureElementSupplement()
    {
        deadline = EditorApplication.timeSinceStartup + 1700;
        string elementNames = SessionState.GetString(ElementSupplementKey + ".Elements", "Fire");
        SessionState.EraseString(ElementSupplementKey + ".Elements");
        bool parryOnly = SessionState.GetBool(ElementSupplementKey + ".ParryOnly", false);
        SessionState.EraseBool(ElementSupplementKey + ".ParryOnly");
        var elements = elementNames.Split(',').Select(e => (WeaponElement)Enum.Parse(typeof(WeaponElement), e)).ToArray();
        var targets = new List<EnemyActor>();
        var listeners = new List<Action<CombatHealth, DamageInfo>>();
        var cases = new List<object>();
        var hits = new List<object>();
        int expectedTakes = elements.Length * (parryOnly ? 3 : 7);
        bool captureCompleted = false;
        string phase = "setup";
        ElementSupplementMovieRecorder movie = null;
        var previousGem = actor.Equipment.EquippedElementGem;
        float captureDelta = Time.captureDeltaTime;
        WriteRecordingSummary("Supplement.json", cases, expectedTakes, false, hits);
        try
        {
            Check(EnemyThemeTrialService.InArena, "보강 촬영 실제 시험장");
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "보강 촬영 실제 스폰 서비스");
            foreach (var table in EnemyThemeTrialHarness.Current.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "보강 촬영 카탈로그");
            var roster = EnemyThemeTrialHarness.Current.tables.SelectMany(t => t.Entries).ToArray();
            var small = roster.First(e => e.definition != null && e.definition.EnemyId == "SpiderBrood_RostrokarckLarvae").definition;
            var parryEnemy = roster.First(e => e.definition != null && e.definition.EnemyId == "CavernMutants_Ursacetus").definition;
            var ability = Enumerable.Range(0, parryEnemy.AbilitySet.Count).Select(parryEnemy.AbilitySet.GetAbility)
                .First(a => a != null && a.name == "CavernMutants_Ursacetus_2HandsSmashAttack");
            Check(ability.IsParryable && ability.UsesPacedTimeline, "배치된 실제 패링 공격");
            var abilitySet = ScriptableObject.CreateInstance<EnemyAbilitySet>(); fixtures.Add(abilitySet);
            var so = new SerializedObject(abilitySet);
            so.FindProperty("abilitySetId").stringValue = "owned-element-presentation";
            var entries = so.FindProperty("abilities"); entries.arraySize = 1; entries.GetArrayElementAtIndex(0).objectReferenceValue = ability;
            so.ApplyModifiedPropertiesWithoutUndo();
            EquipDashHeavyGem(elements[0]);
            var energy = actor.GetComponent<OverburstElementEnergy>();
            if (energy == null) { energy = actor.gameObject.AddComponent<OverburstElementEnergy>(); fixtures.Add(energy); }
            Check(energy != null, "제품 원소 게이지");
            actor.Health.SetMaxHp(1000, true);
            // Keep the live camera. Aim toward the viewport's upper-left corner.
            forward = movement.ResolveMoveDirection(new Vector2(-1f, 1f).normalized);
            forward.y = 0; forward.Normalize(); testFacing = forward;
            movie = blocker.AddComponent<ElementSupplementMovieRecorder>();
            movie.Bind(actor, energy, melee, evade);
            Time.captureDeltaTime = 1f / ElementSupplementMovieRecorder.Fps;
            var view = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            EditorWindow.GetWindow(view).Focus();

            foreach (var element in elements)
            {
                ReleasePresentationTargets(targets, listeners);
                yield return Reset(); EquipDashHeavyGem(element); yield return Wait(.8f);
                if (!parryOnly)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var point = origin + forward * (2f + PresentationEnemyOffset(i, 3)) + Vector3.Cross(Vector3.up, forward) * PresentationEnemyLateral(i, 3);
                        Check(Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "시연 몬스터 접지");
                        Check(spawn.TrySpawn(new EnemySpawnRequest(small, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-forward), actor.transform), out var enemy), "실제 시연 몬스터");
                        targets.Add(enemy); leased.Add(enemy); enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        int index = i;
                        Action<CombatHealth, DamageInfo> listener = (hp, damage) => hits.Add(new { phase, frame = movie.FrameCount, target = index, kind = damage.playerAttackKind.ToString(), primary = damage.triggersOnHitEffects, hp = hp.CurrentHp });
                        listeners.Add(listener); enemy.Health.OnDamaged += listener;
                    }
                    // Warm the deployed effect once, outside every retained shot.
                    yield return SupplementReset(targets, 2f); SetSupplementGauge(energy, 100);
                    yield return StartFocusHeavy("원소 촬영 준비 " + element); yield return PresentationAttackEnd(); yield return Wait(1.4f);
                    foreach (int gauge in new[] { 0, 50, 100 })
                    {
                        yield return SupplementReset(targets, 2f);
                        if (gauge > 0) { phase = "setup_element_states"; yield return SupplementPrime(); yield return SupplementReset(targets, 2f); }
                        SetSupplementGauge(energy, gauge);
                        phase = element + "_heavy_" + gauge; int hitBefore = hits.Count;
                        movie.Begin(Path.Combine(output, "Raw", phase), phase, gauge);
                        yield return Wait(.8f);
                        yield return StartFocusHeavy(phase + " 실제 강공 수락");
                        yield return PresentationAttackEnd(); yield return Wait(1.5f);
                        var take = movie.End(); Check(hits.Count > hitBefore, phase + " 실제 피해");
                        cases.Add(new { phase, element = element.ToString(), action = "heavy", startingGauge = gauge, endingGauge = energy.Amount, hits = hits.Count - hitBefore, take });
                        Check(IsValidRecordedTake(take), phase + " 실제 영상·오디오 촬영 완료"); Progress(phase);
                    }
                    yield return SupplementReset(targets, 2f); phase = "setup_element_states"; yield return SupplementPrime();
                    yield return SupplementReset(targets, 5.7f); SetSupplementGauge(energy, 100);
                    phase = element + "_dash_heavy_100"; int dashHitBefore = hits.Count;
                    movie.Begin(Path.Combine(output, "Raw", phase), phase, 100); yield return Wait(.8f);
                    yield return StartDodge(false, false, true); Send();
                    yield return PresentationAttackEnd(); yield return Wait(1.5f);
                    var dashTake = movie.End(); Check(hits.Count > dashHitBefore, phase + " 실제 대시 강공 피해");
                    cases.Add(new { phase, element = element.ToString(), action = "dash_heavy", startingGauge = 100, endingGauge = energy.Amount, hits = hits.Count - dashHitBefore, take = dashTake });
                    Check(IsValidRecordedTake(dashTake), phase + " 실제 영상·오디오 촬영 완료"); Progress(phase);

                }

                ReleasePresentationTargets(targets, listeners);
                float range = Mathf.Max(1.1f, ability.Range * .85f);
                Check(Physics.Raycast(origin + forward * range + Vector3.up * 4, Vector3.down, out var eliteFloor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "패링 몬스터 접지");
                Check(spawn.TrySpawn(new EnemySpawnRequest(parryEnemy, eliteFloor.point + Vector3.up * .035f, Quaternion.LookRotation(-forward), actor.transform), out var elite), "실제 패링 정예 몬스터");
                targets.Add(elite); leased.Add(elite); elite.AI.enabled = false; elite.Movement.StopMovement(); elite.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                elite.AbilityController.Configure(abilitySet, .1f, 1);
                var parry = actor.GetComponent<PlayerParryController>();
                var parryDamage = new List<DamageInfo>();
                Action<CombatHealth, DamageInfo> parryListener = (hp, damage) => {
                    parryDamage.Add(damage);
                    hits.Add(new { phase, frame = movie.FrameCount, target = elite.GetInstanceID(), kind = damage.playerAttackKind.ToString(), primary = damage.triggersOnHitEffects, attackPhase = damage.sourceAttackPhaseIndex, dot = damage.isDamageOverTime, reaction = damage.elementalReactionType.ToString(), critical = damage.isCritical, hp = hp.CurrentHp });
                };
                listeners.Add(parryListener); elite.Health.OnDamaged += parryListener;
                movie.BindParryTarget(elite);
                foreach (int gauge in parryOnly ? new[] { 100, 50, 0 } : new[] { 0, 50, 100 })
                {
                    yield return SupplementReset(targets, range); actor.Health.ResetHealth(); SetSupplementGauge(energy, gauge);
                    phase = element + "_parry_" + gauge; int successBefore = parry.SuccessCount; parryDamage.Clear();
                    movie.Begin(Path.Combine(output, "Raw", phase), phase, gauge); yield return Wait(.8f);
                    Check(elite.AbilityController.TryStart(actor.transform), phase + " 실제 적 공격 시작");
                    float threatEnd = Time.unscaledTime + 6;
                    while (!elite.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()) && Time.unscaledTime < threatEnd) yield return null;
                    Check(elite.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()), phase + " 실제 적 위협");
                    // Start every parry demonstration with standing heavy input. A dodge here
                    // can cross the close enemy before the target-limited counter even begins.
                    Send(false, false, false, true);
                    float acceptEnd = Time.unscaledTime + 2f;
                    while (!melee.IsHeavyAttackInProgress && Time.unscaledTime < acceptEnd) yield return null;
                    Check(melee.IsHeavyAttackInProgress, phase + " 제자리 실제 강공 입력 수락");
                    Check(!evade.IsEvading, phase + " 선행 회피 없음");
                    Send();
                    yield return PresentationAttackEnd(); yield return Wait(1.5f);
                    var take = movie.End();
                    Check(parry.SuccessCount == successBefore + 1, phase + " 실제 패링 성공");
                    Check(parry.ActionGrade == PlayerParryController.ResolveGrade(gauge / 100f), phase + " 실제 패링 등급");
                    if (parryOnly && gauge == 50)
                        Check(movie.NormalCollapseSeen && movie.NormalHoldSeen && movie.NormalRecoverSeen, phase + " 현재 일반 무너짐 정지 회복");
                    if (parryOnly && gauge == 100)
                        Check(movie.PerfectStunSeen && movie.PerfectContactCount == 1 && movie.PerfectAfterimageCount > 0, phase + " 현재 완벽 기절·금속 접촉·검 잔상");
                    if (parryOnly && gauge == 0)
                    {
                        var weakDamage = parryDamage.Where(d => (d.playerAttackKind & PlayerAttackKind.Weak) != 0
                            && !d.isDamageOverTime && d.elementalReactionType == ElementalReactionType.None).ToArray();
                        Check(weakDamage.Length == 1 && weakDamage[0].sourceAttackPhaseIndex == 0
                            && !parryDamage.Any(d => (d.playerAttackKind & PlayerAttackKind.Heavy) != 0), phase + " 현재 불완전 약공 반격 1타");
                        float gain = CombatBalanceFormulas.PhaseEnergyGain(OverburstElementTuning.Current,
                            weakDamage[0].isCritical, FlaskCombatModifiers.Bonus(actor.gameObject, FlaskEffect.EnergyGain));
                        Check(Mathf.Abs(energy.Amount - Mathf.Min(energy.Capacity, gain)) < .01f, phase + " 약공 반격 정상 게이지 획득");
                        Check(movie.IncompleteCounterSeen && movie.MaximumIncompleteClipProgress >= .98f, phase + " 불완전 전체 클립 끝까지 재생");
                    }
                    if (parryOnly) Check(movie.MinimumApproachSeparation >= -.02f && !movie.EvadeSeen, phase + " 적 관통과 선행 회피 없음");
                    cases.Add(new { phase, element = element.ToString(), action = "parry", startingGauge = gauge, endingGauge = energy.Amount, success = parry.SuccessCount - successBefore, expectedGrade = PlayerParryController.ResolveGrade(gauge / 100f).ToString(), counterHits = parryDamage.Count, take });
                    Check(IsValidRecordedTake(take), phase + " 실제 영상·오디오 촬영 완료"); Progress(phase);
                }
                WriteRecordingSummary("Supplement.json", cases, expectedTakes, false, hits);
            }
            WriteRecordingSummary("Supplement.json", cases, expectedTakes, true, hits);
            captureCompleted = true;
        }
        finally
        {
            try
            {
                if (!captureCompleted) WriteRecordingSummary("Supplement.json", cases, expectedTakes, true, hits, true);
                Send(); if (movie != null) movie.End();
            }
            finally
            {
                if (movie != null) UnityEngine.Object.DestroyImmediate(movie);
                Time.captureDeltaTime = captureDelta;
                ReleasePresentationTargets(targets, listeners);
                typeof(PlayerEquipment).GetMethod("SetElementGem", Private).Invoke(actor.Equipment, new object[] { previousGem });
                SessionState.EraseBool(ElementSupplementKey); SessionState.EraseString(ElementSupplementKey + ".Elements"); SessionState.EraseBool(ElementSupplementKey + ".ParryOnly");
            }
        }
    }
    internal static bool IsValidRecordedTake(object take)
    {
        if (take == null) return false;
        if (take is JToken token && token.Type != JTokenType.Object) return false;
        var data = take as JObject ?? JObject.FromObject(take);
        return (string)data["status"] == "PASS" && (int?)data["frames"] > 0
            && (double?)data["audioPeak"] > 0 && (data["error"] == null || data["error"].Type == JTokenType.Null);
    }
    internal static JObject RecordingSummary(IEnumerable<object> recordedCases, int expectedTakes, bool final, object hits = null, bool interrupted = false)
    {
        var rows = JArray.FromObject(recordedCases);
        bool complete = !interrupted && expectedTakes > 0 && rows.Count == expectedTakes && rows.All(c => IsValidRecordedTake(c["take"]));
        return new JObject {
            ["status"] = final ? (complete ? "PASS" : "FAIL") : "RUNNING",
            ["expectedTakes"] = expectedTakes, ["actualTakes"] = rows.Count,
            ["actualAudio"] = final && complete, ["nativeAudio"] = final && complete,
            ["nativeGameView"] = final && complete, ["actualPlayer"] = rows.Count > 0, ["actualHits"] = rows.Count > 0,
            ["cases"] = rows, ["hits"] = hits == null ? null : JToken.FromObject(hits),
            ["error"] = final && !complete ? "Recording is incomplete or includes a failed/missing take." : null
        };
    }
    static void WriteRecordingSummary(string file, IEnumerable<object> recordedCases, int expectedTakes, bool final, object hits = null, bool interrupted = false)
        => File.WriteAllText(Path.Combine(output, file), RecordingSummary(recordedCases, expectedTakes, final, hits, interrupted).ToString());
    static IEnumerator SupplementReset(List<EnemyActor> targets, float distance)
    {
        Send(); yield return Reset();
        for (int i = 0; i < targets.Count; i++)
        {
            var enemy = targets[i]; enemy.AbilityController.Cancel(); enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(10000, true); enemy.Health.ResetHealth();
            ActorTeleportUtility.TeleportSafely(enemy.transform, origin + forward * (distance + PresentationEnemyOffset(i, targets.Count))
                + Vector3.Cross(Vector3.up, forward) * PresentationEnemyLateral(i, targets.Count), Quaternion.LookRotation(-forward));
        }
        yield return Wait(1f);
    }
    static void SetSupplementGauge(OverburstElementEnergy energy, int amount)
    {
        energy.Clear();
        typeof(OverburstElementEnergy).GetProperty(nameof(OverburstElementEnergy.Amount)).SetValue(energy, (float)amount);
        (typeof(OverburstElementEnergy).GetField("Changed", Private)?.GetValue(energy) as Action)?.Invoke();
        Check(Mathf.Abs(energy.Amount - amount) < .001f, "보강 시연 시작 게이지 " + amount);
    }
    static IEnumerator SupplementPrime()
    {
        int count = 0, previous = -1; float limit = Time.unscaledTime + 10;
        Send(false, false, true);
        while (count < 4 && Time.unscaledTime < limit)
        {
            if (melee.IsAttackInProgress && Field<int>(melee, "comboStepIndex") != previous)
            { previous = Field<int>(melee, "comboStepIndex"); count++; }
            yield return null;
        }
        Send(); Check(count == 4, "실제 약공격으로 원소 상태 준비");
        yield return PresentationAttackEnd();
    }
}

// Captures the composited Game View (including the product HUD) and native game audio together.
public sealed class ElementSupplementMovieRecorder : MonoBehaviour
{
    public const int Fps = 60;
    MediaEncoder encoder;
    RenderTexture target;
    Texture2D texture;
    PlayerActorRuntime actor;
    OverburstElementEnergy energy;
    MeleeRuntime melee;
    PlayerEvadeController evade;
    bool ownsAudio;
    PlayerAnimation playerAnimation;
    PerfectParryContactPresenter contactPresenter;
    PerfectParryWeaponAfterimage weaponAfterimage;
    int contactAtStart, afterimagesAtStart;
    readonly System.Reflection.FieldInfo incompleteCounter = typeof(MeleeRuntime).GetField("incompleteParryCounterConfigured", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    public bool IncompleteCounterSeen { get; private set; }
    public float MaximumIncompleteClipProgress { get; private set; }
    public int PerfectContactCount => contactPresenter != null ? contactPresenter.MainCount - contactAtStart : 0;
    public int PerfectAfterimageCount => weaponAfterimage != null ? weaponAfterimage.CapturedCount - afterimagesAtStart : 0;
    EnemyActor parryTarget;
    Vector3 startingPlayerPosition;
    Vector3 initialApproachDirection;
    readonly System.Reflection.FieldInfo counterMovement = typeof(MeleeRuntime).GetField("heavyParryCounterMovement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    readonly System.Reflection.FieldInfo parryStage = typeof(MeleeRuntime).GetField("heavyParryStage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    readonly System.Reflection.MethodInfo attackProgress = typeof(MeleeRuntime).GetMethod("GetAttackNormalizedTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    public float MinimumApproachSeparation { get; private set; }
    public bool EvadeSeen { get; private set; }
    public bool NormalCollapseSeen { get; private set; }
    public bool NormalHoldSeen { get; private set; }
    public bool NormalRecoverSeen { get; private set; }
    public bool PerfectStunSeen { get; private set; }
    public void BindParryTarget(EnemyActor target) { parryTarget = target; }
    string directory, phase, error;
    int startingGauge;
    long samples;
    float peak;
    readonly List<object> frames = new List<object>();
    public int FrameCount { get; private set; }
    public void Bind(PlayerActorRuntime a, OverburstElementEnergy e, MeleeRuntime m, PlayerEvadeController v)
    { actor = a; energy = e; melee = m; evade = v;
        playerAnimation = (PlayerAnimation)typeof(MeleeRuntime).GetField("playerAnimatorController", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(m);
        contactPresenter = a.GetComponent<PerfectParryContactPresenter>(); weaponAfterimage = a.GetComponent<PerfectParryWeaponAfterimage>();
    }
    public void Begin(string path, string shot, int gauge)
    {
        if (encoder != null) throw new InvalidOperationException("Previous shot still owned.");
        directory = path; phase = shot; startingGauge = gauge; Directory.CreateDirectory(path);
        error = null; FrameCount = 0; samples = 0; peak = 0; frames.Clear();
        NormalCollapseSeen = NormalHoldSeen = NormalRecoverSeen = PerfectStunSeen = false;
        IncompleteCounterSeen = false; MaximumIncompleteClipProgress = 0f;
        contactAtStart = contactPresenter != null ? contactPresenter.MainCount : 0;
        afterimagesAtStart = weaponAfterimage != null ? weaponAfterimage.CapturedCount : 0;
        startingPlayerPosition = actor.transform.position;
        initialApproachDirection = parryTarget != null ? parryTarget.transform.position - startingPlayerPosition : Vector3.zero;
        initialApproachDirection.y = 0f; initialApproachDirection.Normalize();
        MinimumApproachSeparation = float.PositiveInfinity; EvadeSeen = false;
        try
        {
            if (Screen.width != 1920 || Screen.height != 1080) throw new InvalidOperationException("FHD Game View required.");
            if (AudioSettings.speakerMode != AudioSpeakerMode.Stereo) throw new InvalidOperationException("Existing stereo output required.");
            ownsAudio = AudioRenderer.Start(); if (!ownsAudio) throw new InvalidOperationException("Native audio capture already owned.");
            target = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32); target.Create();
            texture = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
            encoder = new MediaEncoder(Path.Combine(path, "raw.mp4"), new VideoTrackEncoderAttributes
            { frameRate = new MediaRational(Fps), width = 1920, height = 1080, includeAlpha = false, targetBitRate = 36000000, bitRateMode = UnityEditor.VideoBitrateMode.High },
                new AudioTrackAttributes { sampleRate = new MediaRational(AudioSettings.outputSampleRate), channelCount = 2, language = "ko" });
            StartCoroutine(Record());
        }
        catch { End(); throw; }
    }
    IEnumerator Record()
    {
        var end = new WaitForEndOfFrame();
        while (encoder != null)
        {
            yield return end;
            if (encoder == null) yield break;
            var prior = RenderTexture.active;
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(target); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); texture.Apply(false, false);
                if (!encoder.AddFrame(texture)) throw new InvalidOperationException("Native video frame rejected.");
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                using (var audio = new NativeArray<float>(count * 2, Allocator.Temp))
                {
                    if (!AudioRenderer.Render(audio)) throw new InvalidOperationException("Native audio frame rejected.");
                    if (count > 0 && !encoder.AddSamples(audio)) throw new InvalidOperationException("Audio samples rejected.");
                    for (int i = 0; i < audio.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(audio[i])); samples += count;
                }
                bool collapse = false, recover = false, normalReacting = false, stunned = false;
                float enemySpeed = 1f, enemyProgress = 0f;
                if (parryTarget != null && parryTarget.Animator != null)
                {
                    var motion = parryTarget.Animator;
                    var state = motion.GetCurrentAnimatorStateInfo(0);
                    collapse = state.IsName(EnemyAnimationBridge.ParryCollapseStateName);
                    recover = state.IsName(EnemyAnimationBridge.StunRecoverStateName)
                        || motion.IsInTransition(0) && motion.GetNextAnimatorStateInfo(0).IsName(EnemyAnimationBridge.StunRecoverStateName);
                    normalReacting = parryTarget.AnimationBridge != null && parryTarget.AnimationBridge.IsNormalParryReacting;
                    var reaction = parryTarget.GetComponent<EnemyMovementReaction>();
                    stunned = reaction != null && reaction.IsParryStunned;
                    enemySpeed = motion.speed; enemyProgress = state.normalizedTime;
                    NormalCollapseSeen |= normalReacting && collapse;
                    NormalHoldSeen |= normalReacting && collapse && enemyProgress > .98f && Mathf.Abs(enemySpeed) < .001f;
                    NormalRecoverSeen |= normalReacting && recover;
                    PerfectStunSeen |= stunned;
                }
                bool incompleteActive = incompleteCounter != null && (bool)incompleteCounter.GetValue(melee);
                float nativeParryProgress = 0f;
                bool nativeParrySample = playerAnimation != null && playerAnimation.TryGetHeavyParryClipProgress(out nativeParryProgress);
                IncompleteCounterSeen |= incompleteActive;
                if (incompleteActive && nativeParrySample) MaximumIncompleteClipProgress = Mathf.Max(MaximumIncompleteClipProgress, nativeParryProgress);
                Vector3 position = actor.transform.position;
                Vector3 enemyPosition = parryTarget != null ? parryTarget.transform.position : position;
                float approachSeparation = parryTarget != null ? Vector3.Dot(enemyPosition - position, initialApproachDirection) : 0f;
                MinimumApproachSeparation = Mathf.Min(MinimumApproachSeparation, approachSeparation);
                EvadeSeen |= evade.IsEvading;
                frames.Add(new { index = FrameCount, time = Time.time, gauge = energy.Amount, element = actor.Equipment.ActiveElement.ToString(), attacking = melee.IsAttackInProgress, heavy = melee.IsHeavyAttackInProgress, evading = evade.IsEvading, parrySuccess = actor.GetComponent<PlayerParryController>().SuccessCount, grade = actor.GetComponent<PlayerParryController>().ActionGrade.ToString(), playerX = position.x, playerY = position.y, playerZ = position.z, enemyX = enemyPosition.x, enemyY = enemyPosition.y, enemyZ = enemyPosition.z, approachSeparation, counterMoving = counterMovement != null && (bool)counterMovement.GetValue(melee), parryStage = parryStage?.GetValue(melee)?.ToString(), attackProgress = melee.IsAttackInProgress && attackProgress != null ? (float)attackProgress.Invoke(melee, null) : 0f, incompleteActive, nativeParrySample, nativeParryProgress, perfectContactCount = PerfectContactCount, perfectAfterimageCount = PerfectAfterimageCount, afterimageActive = weaponAfterimage != null ? weaponAfterimage.ActiveCount : 0, afterimageEmitting = weaponAfterimage != null && weaponAfterimage.IsEmitting, collapse, recover, normalReacting, stunned, enemySpeed, enemyProgress });
                if (FrameCount == 0) File.WriteAllBytes(Path.Combine(directory, "first.png"), texture.EncodeToPNG());
                FrameCount++;
            }
            catch (Exception e) { error = e.ToString(); End(); }
            finally { RenderTexture.active = prior; }
        }
    }
    public object End()
    {
        bool active = encoder != null || ownsAudio || target != null;
        if (!active) return null;
        StopAllCoroutines();
        try { encoder?.Dispose(); }
        finally
        {
            encoder = null;
            try { if (ownsAudio) AudioRenderer.Stop(); }
            finally
            {
                ownsAudio = false;
                if (target != null) { target.Release(); DestroyImmediate(target); target = null; }
                if (texture != null) { DestroyImmediate(texture); texture = null; }
            }
        }
        var result = new { status = error == null && FrameCount > 0 && peak > 0 ? "PASS" : "FAIL", phase, startingGauge, frames = FrameCount, fps = Fps, duration = FrameCount / (float)Fps, audioSamples = samples, audioPeak = peak, error, nativeAudio = true, productHud = true, normalCollapseSeen = NormalCollapseSeen, normalHoldSeen = NormalHoldSeen, normalRecoverSeen = NormalRecoverSeen, perfectStunSeen = PerfectStunSeen, perfectContactCount = PerfectContactCount, perfectAfterimageCount = PerfectAfterimageCount, incompleteCounterSeen = IncompleteCounterSeen, maximumIncompleteClipProgress = MaximumIncompleteClipProgress, minimumApproachSeparation = MinimumApproachSeparation, evadeSeen = EvadeSeen, playerTravel = Vector3.Distance(startingPlayerPosition, actor.transform.position) };
        File.WriteAllText(Path.Combine(directory, "take.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        File.WriteAllText(Path.Combine(directory, "frames.json"), JsonConvert.SerializeObject(frames));
        if (error != null) throw new InvalidOperationException(error);
        return result;
    }
    void OnDestroy() { End(); }
}
