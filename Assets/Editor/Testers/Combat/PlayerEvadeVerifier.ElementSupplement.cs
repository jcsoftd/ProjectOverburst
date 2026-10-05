using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
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
        { SessionState.EraseBool(ElementSupplementKey); SessionState.EraseString(ElementSupplementKey + ".Elements"); }
    }
    public static void StartElementSupplementIsolated(string directory, string elements = "Fire,Ice,Electric,Dark,Light")
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
        try
        {
            StartSwordFacingIsolated(directory);
            SessionState.SetString(ReturnKey + ".Deadline", (EditorApplication.timeSinceStartup + 1800).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { SessionState.EraseBool(ElementSupplementKey); SessionState.EraseString(ElementSupplementKey + ".Elements"); throw; }
    }

    static IEnumerator CaptureElementSupplement()
    {
        deadline = EditorApplication.timeSinceStartup + 1700;
        string elementNames = SessionState.GetString(ElementSupplementKey + ".Elements", "Fire");
        SessionState.EraseString(ElementSupplementKey + ".Elements");
        var elements = elementNames.Split(',').Select(e => (WeaponElement)Enum.Parse(typeof(WeaponElement), e)).ToArray();
        var targets = new List<EnemyActor>();
        var listeners = new List<Action<CombatHealth, DamageInfo>>();
        var cases = new List<object>();
        var hits = new List<object>();
        string phase = "setup";
        ElementSupplementMovieRecorder movie = null;
        var previousGem = actor.Equipment.EquippedElementGem;
        float captureDelta = Time.captureDeltaTime;
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
                    cases.Add(new { phase, element = element.ToString(), action = "heavy", startingGauge = gauge, endingGauge = energy.Amount, hits = hits.Count - hitBefore, take }); Progress(phase);
                }
                yield return SupplementReset(targets, 2f); phase = "setup_element_states"; yield return SupplementPrime();
                yield return SupplementReset(targets, 5.7f); SetSupplementGauge(energy, 100);
                phase = element + "_dash_heavy_100"; int dashHitBefore = hits.Count;
                movie.Begin(Path.Combine(output, "Raw", phase), phase, 100); yield return Wait(.8f);
                yield return StartDodge(false, false, true); Send();
                yield return PresentationAttackEnd(); yield return Wait(1.5f);
                var dashTake = movie.End(); Check(hits.Count > dashHitBefore, phase + " 실제 대시 강공 피해");
                cases.Add(new { phase, element = element.ToString(), action = "dash_heavy", startingGauge = 100, endingGauge = energy.Amount, hits = hits.Count - dashHitBefore, take = dashTake }); Progress(phase);

                ReleasePresentationTargets(targets, listeners);
                float range = Mathf.Max(1.1f, ability.Range * .85f);
                Check(Physics.Raycast(origin + forward * range + Vector3.up * 4, Vector3.down, out var eliteFloor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "패링 몬스터 접지");
                Check(spawn.TrySpawn(new EnemySpawnRequest(parryEnemy, eliteFloor.point + Vector3.up * .035f, Quaternion.LookRotation(-forward), actor.transform), out var elite), "실제 패링 정예 몬스터");
                targets.Add(elite); leased.Add(elite); elite.AI.enabled = false; elite.Movement.StopMovement(); elite.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                elite.AbilityController.Configure(abilitySet, .1f, 1);
                var parry = actor.GetComponent<PlayerParryController>();
                foreach (int gauge in new[] { 0, 50, 100 })
                {
                    yield return SupplementReset(targets, range); actor.Health.ResetHealth(); SetSupplementGauge(energy, gauge);
                    phase = element + "_parry_" + gauge; int successBefore = parry.SuccessCount;
                    movie.Begin(Path.Combine(output, "Raw", phase), phase, gauge); yield return Wait(.8f);
                    Check(elite.AbilityController.TryStart(actor.transform), phase + " 실제 적 공격 시작");
                    float threatEnd = Time.unscaledTime + 6;
                    while (!elite.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()) && Time.unscaledTime < threatEnd) yield return null;
                    Check(elite.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()), phase + " 실제 적 위협");
                    yield return StartDodge(false); Send(false, false, false, true); yield return Frames(2); Send();
                    yield return PresentationAttackEnd(); yield return Wait(1.5f);
                    var take = movie.End();
                    Check(parry.SuccessCount == successBefore + 1, phase + " 실제 패링 성공");
                    cases.Add(new { phase, element = element.ToString(), action = "parry", startingGauge = gauge, endingGauge = energy.Amount, success = parry.SuccessCount - successBefore, expectedGrade = PlayerParryController.ResolveGrade(gauge / 100f).ToString(), take }); Progress(phase);
                }
                File.WriteAllText(Path.Combine(output, "Supplement.json"), JsonConvert.SerializeObject(new { status = "RUNNING", actualPlayer = true, actualHits = true, actualAudio = true, cases, hits }, Formatting.Indented));
            }
            File.WriteAllText(Path.Combine(output, "Supplement.json"), JsonConvert.SerializeObject(new { status = "PASS", actualPlayer = true, actualHits = true, actualAudio = true, cases, hits }, Formatting.Indented));
        }
        finally
        {
            Send(); if (movie != null) { movie.End(); UnityEngine.Object.DestroyImmediate(movie); }
            Time.captureDeltaTime = captureDelta;
            ReleasePresentationTargets(targets, listeners);
            typeof(PlayerEquipment).GetMethod("SetElementGem", Private).Invoke(actor.Equipment, new object[] { previousGem });
            SessionState.EraseBool(ElementSupplementKey); SessionState.EraseString(ElementSupplementKey + ".Elements");
        }
    }
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
    string directory, phase, error;
    int startingGauge;
    long samples;
    float peak;
    readonly List<object> frames = new List<object>();
    public int FrameCount { get; private set; }
    public void Bind(PlayerActorRuntime a, OverburstElementEnergy e, MeleeRuntime m, PlayerEvadeController v)
    { actor = a; energy = e; melee = m; evade = v; }
    public void Begin(string path, string shot, int gauge)
    {
        if (encoder != null) throw new InvalidOperationException("Previous shot still owned.");
        directory = path; phase = shot; startingGauge = gauge; Directory.CreateDirectory(path);
        error = null; FrameCount = 0; samples = 0; peak = 0; frames.Clear();
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
                frames.Add(new { index = FrameCount, time = Time.time, gauge = energy.Amount, element = actor.Equipment.ActiveElement.ToString(), attacking = melee.IsAttackInProgress, heavy = melee.IsHeavyAttackInProgress, evading = evade.IsEvading, parrySuccess = actor.GetComponent<PlayerParryController>().SuccessCount, grade = actor.GetComponent<PlayerParryController>().ActionGrade.ToString() });
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
        var result = new { status = error == null && FrameCount > 0 && peak > 0 ? "PASS" : "FAIL", phase, startingGauge, frames = FrameCount, fps = Fps, duration = FrameCount / (float)Fps, audioSamples = samples, audioPeak = peak, error, nativeAudio = true, productHud = true };
        File.WriteAllText(Path.Combine(directory, "take.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        File.WriteAllText(Path.Combine(directory, "frames.json"), JsonConvert.SerializeObject(frames));
        if (error != null) throw new InvalidOperationException(error);
        return result;
    }
    void OnDestroy() { End(); }
}
