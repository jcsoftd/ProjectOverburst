using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Reusable product-action capture fixtures. No production scene, tuning or enemy asset is changed.
public static partial class PlayerEvadeVerifier
{
    const string ElementShowcaseKey = "Overburst.PlayerEvadeVerifier.ElementShowcase";
    [InitializeOnLoadMethod]
    static void RegisterElementShowcaseReturn()
    {
        EditorApplication.playModeStateChanged -= ClearElementShowcaseMode;
        EditorApplication.playModeStateChanged += ClearElementShowcaseMode;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) ClearElementShowcaseMode(PlayModeStateChange.EnteredEditMode);
    }
    static void ClearElementShowcaseMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(PendingKey, "") == "")
        { SessionState.EraseBool(ElementShowcaseKey); SessionState.EraseString(ElementShowcaseKey + ".Elements"); }
    }
    [MenuItem("OVERBURST/검증/영상/원소 강공 시연 촬영")]
    public static void CaptureElementShowcaseMenu()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Video/ElementShowcase"));
        StartElementShowcaseIsolated(Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss")));
    }
    public static void StartElementShowcaseIsolated(string directory, string elements = "Fire,Electric,Ice,Dark,Light")
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(SessionState.GetString(FacingQueueKey, ""))
            || !string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))
            || !string.IsNullOrEmpty(SessionState.GetString(ReturnKey, "")))
            throw new InvalidOperationException("The shared Editor and real account must be idle and returned.");
        var parsed = elements.Split(',').Select(e => (WeaponElement)Enum.Parse(typeof(WeaponElement), e.Trim())).ToArray();
        if (parsed.Length == 0 || parsed.Any(e => !OverburstElementRules.IsActive(e))) throw new ArgumentException("Active elements required.");
        SessionState.SetString(ElementShowcaseKey + ".Elements", string.Join(",", parsed));
        SessionState.SetBool(ElementShowcaseKey, true);
        try
        {
            StartSwordFacingIsolated(directory);
            SessionState.SetString(ReturnKey + ".Deadline", (EditorApplication.timeSinceStartup + 2400).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { SessionState.EraseBool(ElementShowcaseKey); SessionState.EraseString(ElementShowcaseKey + ".Elements"); throw; }
    }
    static IEnumerator CaptureElementShowcase()
    {
        deadline = EditorApplication.timeSinceStartup + 2300;
        var elements = SessionState.GetString(ElementShowcaseKey + ".Elements", "Fire,Electric,Ice,Dark,Light")
            .Split(',').Select(e => (WeaponElement)Enum.Parse(typeof(WeaponElement), e)).ToArray();
        var targets = new List<EnemyActor>();
        var listeners = new List<Action<CombatHealth, DamageInfo>>();
        var hits = new List<ShowcaseHit>();
        var cases = new List<object>();
        int expectedTakes = elements.Length * 2;
        bool captureCompleted = false;
        string phase = "warmup";
        ElementSupplementMovieRecorder movie = null;
        var priorGem = actor.Equipment.EquippedElementGem;
        float priorClock = Time.captureDeltaTime;
        WriteRecordingSummary("Showcase.json", cases, expectedTakes, false);
        try
        {
            Check(EnemyThemeTrialService.InArena, "원소 시연 실제 시험장");
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "원소 시연 제품 스폰 서비스");
            foreach (var table in EnemyThemeTrialHarness.Current.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "시연 카탈로그");
            var roster = EnemyThemeTrialHarness.Current.tables.SelectMany(t => t.Entries).ToArray();
            var small = roster.First(e => e.definition.EnemyId == "SpiderBrood_RostrokarckLarvae");
            var medium = roster.First(e => e.definition.EnemyId == "SpiderBrood_Cavecrawler");
            Check(small.tier == EnemyThemeTier.Small && medium.tier == EnemyThemeTier.Medium, "거미 소형/중형 실제 편성");
            EquipDashHeavyGem(elements[0]);
            var energy = actor.GetComponent<OverburstElementEnergy>();
            if (energy == null) { energy = actor.gameObject.AddComponent<OverburstElementEnergy>(); fixtures.Add(energy); }
            forward = movement.ResolveMoveDirection(new Vector2(-1, 1).normalized); forward.y = 0; forward.Normalize(); testFacing = forward;
            actor.Health.SetMaxHp(1000, true);
            movie = blocker.AddComponent<ElementSupplementMovieRecorder>(); movie.Bind(actor, energy, melee, evade);
            Time.captureDeltaTime = 1f / ElementSupplementMovieRecorder.Fps;
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            foreach (var element in elements)
            {
                ReleasePresentationTargets(targets, listeners); yield return Reset(); EquipDashHeavyGem(element); yield return Wait(.6f);
                int smallCount = element == WeaponElement.Dark ? 5 : element == WeaponElement.Light ? 20 : 10;
                int mediumCount = element == WeaponElement.Dark ? 6 : 1;
                for (int i = 0; i < smallCount + mediumCount; i++)
                {
                    var definition = i < smallCount ? small.definition : medium.definition;
                    Vector3 point = origin + forward * 3.3f;
                    Check(Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "시연 배치 접지");
                    Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-forward), actor.transform), out var enemy), "시연 거미 생성");
                    targets.Add(enemy); leased.Add(enemy); enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    int index = i;
                    Action<CombatHealth, DamageInfo> listener = (hp, damage) => hits.Add(new ShowcaseHit
                    { phase = phase, frame = movie.FrameCount, target = index, kind = damage.playerAttackKind.ToString(), primary = damage.triggersOnHitEffects,
                        dot = damage.isDamageOverTime, position = Vec(enemy.transform.position), gauge = energy.Amount, hp = hp.CurrentHp });
                    listeners.Add(listener); enemy.Health.OnDamaged += listener;
                }
                // Warm every deployed status and follow-up before retained capture.
                yield return PrepareShowcase(targets, element, false, energy); phase = "warmup";
                yield return StartFocusHeavy("시연 예열 " + element); yield return PresentationAttackEnd(); yield return Wait(3f);
                foreach (bool dash in new[] { false, true })
                {
                    phase = element + (dash ? "_dash_heavy" : "_heavy");
                    yield return PrepareShowcase(targets, element, dash, energy);
                    var before = targets.Select((e, i) => new { target = i, tier = i < smallCount ? "Small" : "Medium", position = Vec(e.transform.position),
                        stacks = e.GetComponent<ElementalStatusController>().GetStackCount(element), frozen = e.GetComponent<ElementalStatusController>().IsFrozen }).ToArray();
                    Check(element == WeaponElement.Light || before.All(t => t.stacks == (element == WeaponElement.Dark && t.target >= 5 ? 0 : 5)), phase + " 시작 상태 최대5");
                    Check(element != WeaponElement.Ice || before.All(t => t.frozen), phase + " 전원 실제 빙결");
                    Check(Mathf.Abs(energy.Amount - (element == WeaponElement.Light ? 200 : 100)) < .001f, phase + " 시작 원소 게이지");
                    Check(element != WeaponElement.Light || energy.RadianceStacks == 100, phase + " 광휘100");
                    int hitBefore = hits.Count, launchedBefore = DarkBarrageScheduler.TotalLaunched, lightBefore = LightTripleImpactScheduler.DispatchedHitCount;
                    int startingRadiance = energy.RadianceStacks;
                    movie.Begin(Path.Combine(output, "Raw", phase), phase, element == WeaponElement.Light ? 200 : 100);
                    yield return Wait(.8f);
                    if (dash) { yield return StartDodge(false, false, true); Send(); }
                    else yield return StartFocusHeavy(phase + " 실제 강공 수락");
                    yield return PresentationAttackEnd(); yield return Wait(element == WeaponElement.Dark ? 3f : 2f);
                    var take = movie.End();
                    var shotHits = hits.Skip(hitBefore).Where(h => h.phase == phase && !h.dot).ToArray();
                    int primaryCount = shotHits.Where(h => h.kind.Contains("Heavy")).Select(h => h.target).Distinct().Count();
                    int derivedCount = shotHits.Count(h => h.kind.Contains("Elemental") && !h.kind.Contains("Heavy"));
                    File.WriteAllText(Path.Combine(output, "Raw", phase, "evidence.json"), JsonConvert.SerializeObject(new { before, hits = shotHits, startingRadiance,
                        darkLaunched = DarkBarrageScheduler.TotalLaunched - launchedBefore, lightFollowups = LightTripleImpactScheduler.DispatchedHitCount - lightBefore }, Formatting.Indented));
                    Check(primaryCount > 0, phase + " 실제 강공 피해");
                    if (element != WeaponElement.Light || !dash) Check(derivedCount > 0, phase + " 실제 원소 후속 피해");
                    if (element == WeaponElement.Electric)
                    {
                        Check(primaryCount == 5 && shotHits.Where(h => h.kind.Contains("Heavy")).All(h => h.target < 5), phase + " 중앙5마리 직접 적중");
                        Check(shotHits.Any(h => h.kind == "Elemental" && h.target >= 5), phase + " 주변으로 실제 연쇄번개 전파");
                    }
                    if (element == WeaponElement.Dark) Check(DarkBarrageScheduler.TotalLaunched - launchedBefore == 25, phase + " 잠식5명 총25발 실제 발사");
                    if (element == WeaponElement.Light) Check(LightTripleImpactScheduler.DispatchedHitCount - lightBefore == (dash ? 0 : 2),
                        phase + (dash ? " 제품 대시 전진 파동" : " 실제2회 후속으로3연타"));
                    cases.Add(new { phase, element = element.ToString(), action = dash ? "dash_heavy" : "heavy", smallCount, mediumCount,
                        startingGauge = element == WeaponElement.Light ? 200 : 100, startingRadiance, before, primaryCount, derivedCount,
                        darkLaunched = DarkBarrageScheduler.TotalLaunched - launchedBefore, lightFollowups = LightTripleImpactScheduler.DispatchedHitCount - lightBefore, hits = shotHits, take });
                    Check(IsValidRecordedTake(take), phase + " 실제 영상·오디오 촬영 완료");
                    WriteRecordingSummary("Showcase.json", cases, expectedTakes, false); Progress(phase);
                }
            }
            WriteRecordingSummary("Showcase.json", cases, expectedTakes, true);
            captureCompleted = true;
        }
        finally
        {
            try
            {
                if (!captureCompleted) WriteRecordingSummary("Showcase.json", cases, expectedTakes, true, null, true);
                Send(); if (movie != null) movie.End();
            }
            finally
            {
                if (movie != null) UnityEngine.Object.DestroyImmediate(movie);
                Time.captureDeltaTime = priorClock; ReleasePresentationTargets(targets, listeners);
                typeof(PlayerEquipment).GetMethod("SetElementGem", Private).Invoke(actor.Equipment, new object[] { priorGem });
                SessionState.EraseBool(ElementShowcaseKey); SessionState.EraseString(ElementShowcaseKey + ".Elements");
            }
        }
    }
    static float[] Vec(Vector3 p) => new[] { p.x, p.y, p.z };
    sealed class ShowcaseHit
    {
        public string phase, kind;
        public int frame, target;
        public bool primary, dot;
        public float[] position;
        public float gauge, hp;
    }
    static IEnumerator PrepareShowcase(List<EnemyActor> targets, WeaponElement element, bool dash, OverburstElementEnergy energy)
    {
        yield return Reset();
        Vector3 center = origin + forward * (dash ? 5.7f : 3.3f), side = Vector3.Cross(Vector3.up, forward);
        for (int i = 0; i < targets.Count; i++)
        {
            var e = targets[i]; e.AbilityController.Cancel(); e.Movement.StopMovement(); e.Health.SetMaxHp(10000, true); e.Health.ResetHealth();
            Vector2 offset = ShowcaseOffset(element, i);
            ActorTeleportUtility.TeleportSafely(e.transform, center + side * offset.x + forward * offset.y, Quaternion.LookRotation(-forward));
        }
        yield return Wait(.7f);
        foreach (var e in targets.Select((enemy, i) => new { enemy, i }))
        {
            var status = e.enemy.GetComponent<ElementalStatusController>();
            status.ClearAllStatuses();
            if (element == WeaponElement.Light || element == WeaponElement.Dark && e.i >= 5) continue;
            for (int k = 0; k < 5; k++) Check(status.TryApplyDirectHit(new ElementalStatusApplication(element, 10, actor.gameObject,
                energy.WeaponInstanceId, true, false, e.enemy.transform.position, forward, new ElementGemAttackSnapshot(actor.Equipment))), "시연 전용 최대 상태 준비");
        }
        SetSupplementGauge(energy, element == WeaponElement.Light ? 200 : 100);
        if (element == WeaponElement.Light)
        {
            typeof(OverburstElementEnergy).GetProperty(nameof(OverburstElementEnergy.RadianceStacks)).SetValue(energy, 100);
            typeof(OverburstElementEnergy).GetField("holdUntil", Private).SetValue(energy, Time.time + 8f);
            (typeof(OverburstElementEnergy).GetField("Changed", Private)?.GetValue(energy) as Action)?.Invoke();
        }
        yield return Frames(2);
    }
    static Vector2 ShowcaseOffset(WeaponElement element, int i)
    {
        if (element == WeaponElement.Fire)
        {
            // A central source and spaced branches at <=1.8m propagation intervals.
            Vector2[] points = { new Vector2(0, 0), new Vector2(-1.5f, .5f), new Vector2(1.5f, .5f), new Vector2(-2.8f, 1.6f),
                new Vector2(2.8f, 1.6f), new Vector2(-3.9f, 2.8f), new Vector2(3.9f, 2.8f), new Vector2(-4.8f, 4.2f),
                new Vector2(4.8f, 4.2f), new Vector2(0, 3.4f), new Vector2(0, 5f) };
            return points[i];
        }
        if (element == WeaponElement.Electric || element == WeaponElement.Dark)
        {
            if (i < 5) { float angle = i * Mathf.PI * 2 / 5; return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .9f; }
            if (element == WeaponElement.Electric)
            {
                // Keep the five donors inside the slam/wave; columns remain outside direct damage.
                int column = (i - 5) / 3, row = (i - 5) % 3;
                return new Vector2(column == 0 ? 5f : i == 10 ? -5.8f : -5f, (row - 1) * 1.8f);
            }
            float a = (i - 5) * Mathf.PI * 2 / 6;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6f;
        }
        if (element == WeaponElement.Ice)
        { if (i == 10) return Vector2.zero; float a = i * Mathf.PI * 2 / 10; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.5f; }
        if (i == 20) return Vector2.zero;
        float theta = i % 10 * Mathf.PI * 2 / 10 + (i >= 10 ? .31f : 0);
        return new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * (i < 10 ? 1.6f : 3.4f);
    }
}
