using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MapRunEntryPlayVerifier
{
    const string Key = "Overburst.MapRunEntryPlayVerifier.";
    static string output;
    static double deadline;
    static int lastFrame;
    static bool finishing;
    static string lastStage;
    static double lastStageAt;
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static readonly List<string> checks = new List<string>();
    static readonly List<object> observations = new List<object>();
    static MapRunEntryPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(Key + "pending", "") != "") EditorApplication.update += Begin;
        if (SessionState.GetString(Key + "return", "") != "")
        {
            EditorApplication.update += Return;
            if (SessionState.GetString(Key + "pending", "") == "") EditorApplication.update += StopInterruptedPlay;
        }
    }
    static void StopInterruptedPlay()
    {
        EditorApplication.update -= StopInterruptedPlay;
        if (!EditorApplication.isPlaying || !OwnPlay || SessionState.GetString(Key + "pending", "") != "") return;
        output = SessionState.GetString(Key + "return", "");
        Write("reload-interrupted.json", new { status = "INTERRUPTED", utc = DateTime.UtcNow,
            reason = "Assembly reload interrupted the owned Play; prior progress.json retains completed checks." });
        Finish(new InvalidOperationException("Assembly reload interrupted owned Play. See progress.json and reload-interrupted.json."));
    }
    static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }));
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        checks.Add(name); Write("progress.json", new { checks = checks.Count, last = name, utc = DateTime.UtcNow });
    }
    public static void Start(string directory, bool desertOnly = false, bool largeOnly = false, int completionCases = 0, int bossCases = 0, bool minimapOnly = false, bool guidanceOnly = false, bool minimapCycles = false, bool includeLargeCave = true, int desertCandidateIndex = 0, bool minimapCompatibility = false, bool minimapResolution = false, bool progressionOnly = false, bool escapeSettlementOnly = false, int escapeCases = 0, int caveCount = 0, bool verifyMapRequired = false, bool speciesGroupsOnly = false, bool caveOnly = false, bool caveBakedSpawnOnly = false, string caveTheme = null, bool desertRoadSpawnOnly = false, bool caveStairTraversalOnly = false, string desertPerformanceProfile = null, bool desertBoundaryOnly = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.ActiveDirectory != "" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))
            throw new InvalidOperationException("Use an idle real-account Editor.");
        if (caveCount != 0 && caveCount != 5 && caveCount != 10 && caveCount != 15) throw new ArgumentException("Scale case must be 5, 10 or 15.");
        finishing = false;
        var performance = string.IsNullOrEmpty(desertPerformanceProfile) ? null : DesertPerformanceProfile.Read(desertPerformanceProfile);
        if (minimapCycles) minimapOnly = true;
        output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Use a new owned evidence folder.");
        Directory.CreateDirectory(output);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var state = NewAccountFactory.Create(registry); AccountCharacterCommands.EnsureLegacy(state);
        for (int i = 0; i < (escapeCases > 0 ? escapeCases * 2 + 1 : minimapCycles ? 10 : 3 + Mathf.Max(0, Mathf.Max(completionCases, bossCases) - 1)); i++)
        {
            var item = MapGenerationPolicy.CreateItem(definition, registry, bossCases > 0 && i >= 2 ? 5 : 1, ItemGrade.Common);
            var map = item.mapState; map.seed = 48310 + (caveCount > 0 && i == 0 ? caveCount * 97 : 0) + (minimapCycles && i >= 3 && i % 2 == 1 ? i * 97 : 0);
            if (i < 2 || i >= 3 && bossCases == 0 && (!minimapCycles || i % 2 == 1) && !(escapeCases > 0 && i % 2 == 0)) { map.terrain = MapTerrainKind.Cave; map.areaCount = i == 1 ? 20 : i == 0 && caveCount > 0 ? caveCount : 4; map.width = map.depth = 0; map.desertCandidateId = null; }
            else
            {
                var row = MapTerrainCatalog.DesertCandidates[minimapCycles ? (i - 2) / 2 : desertCandidateIndex];
                if (escapeCases > 0)
                    row = MapTerrainCatalog.DesertCandidates.First(c => Resources.Load<DesertMinimapData>(DesertMinimapData.ResourceRoot + c.id).combatTiles.Any(t => t.combatSize != Overburst.Mojave.MojaveCombatSize.Small));
                if (bossCases > 0)
                {
                    var wanted = i == 5 ? Overburst.Mojave.MojaveCombatSize.Medium : i >= 6 ? Overburst.Mojave.MojaveCombatSize.Small : Overburst.Mojave.MojaveCombatSize.Large;
                    row = MapTerrainCatalog.DesertCandidates.First(c => Resources.Load<DesertMinimapData>(DesertMinimapData.ResourceRoot + c.id).combatTiles.Min(p => (int)p.combatSize) == (int)wanted);
                }
                map.terrain = MapTerrainKind.Desert;
                map.desertCandidateId = row.id; map.areaCount = row.areaCount; map.width = row.width; map.depth = row.depth;
            }
            if (escapeCases > 0) map.seed = MapEscapeRuntimeVerifier.SelectedSeed(48310);
            map.monsterThemeId = map.terrain == MapTerrainKind.Desert ? "PrimalHunt" : i == 1 ? "SpiderBrood" : "CavernMutants";
            if (bossCases > 0 && map.terrain == MapTerrainKind.Desert)
            { map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.EnemyHealth, value = .5f }); }
            if (caveStairTraversalOnly && i == 0) map.seed = 19091;
            if (map.terrain == MapTerrainKind.Cave && !string.IsNullOrEmpty(caveTheme)) map.monsterThemeId = caveTheme;
            map.monsterThemes[0].themeId = map.monsterThemeId;
            if (performance != null && map.terrain == MapTerrainKind.Desert)
            {
                map.level = performance.level; map.seed = performance.seed; map.monsterThemeId = performance.theme; map.monsterThemes[0].themeId = performance.theme;
                if (performance.monsterMultiplier > 1)
                    foreach (string id in new[] { MapOptionPolicy.MoreSmall, MapOptionPolicy.MoreMedium, MapOptionPolicy.MoreElite })
                        map.options.Add(new MapOptionRoll { optionId = id, value = performance.monsterMultiplier - 1 });
            }
            if (i == 1)
            {
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreSmall, value = .9f });
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreMedium, value = .6f });
                map.options.Add(new MapOptionRoll { optionId = MapOptionPolicy.MoreElite, value = .4f });
            }
            var value = ItemSnapshotCodec.Capture(item, registry); value.acquisitionOrder = state.nextAcquisitionOrder++;
            int slot = state.inventory.FindIndex(0, state.unlockedSlots, string.IsNullOrEmpty);
            if (slot < 0) throw new InvalidOperationException("The owned map fixture needs a free inventory slot.");
            state.items.Add(value); state.inventory[slot] = value.instanceId;
        }
        AccountInvariants.Validate(state, registry);
        new EasySaveAccountStore(Path.Combine(output, "Account")).Save(state, "map-entry-fixture");
        Write("fixture.json", state.items.Where(i => i.map != null));
        SessionState.SetString(Key + "pending", output); SessionState.SetString(Key + "return", output);
        SessionState.SetBool(Key + "desertOnly", desertOnly);
        SessionState.SetBool(Key + "desertRoadSpawnOnly", desertRoadSpawnOnly);
        SessionState.SetBool(Key + "desertBoundaryOnly", desertBoundaryOnly);
        SessionState.SetString(Key + "desertPerformanceProfile", desertPerformanceProfile ?? "");
        SessionState.SetBool(Key + "caveStairTraversalOnly", caveStairTraversalOnly);
        SessionState.SetBool(Key + "largeOnly", largeOnly);
        SessionState.SetInt(Key + "completionCases", completionCases);
        SessionState.SetInt(Key + "bossCases", bossCases);
        SessionState.SetBool(Key + "minimapOnly", minimapOnly);
        SessionState.SetBool(Key + "guidanceOnly", guidanceOnly);
        SessionState.SetBool(Key + "minimapCycles", minimapCycles);
        SessionState.SetBool(Key + "minimapCompatibility", minimapCompatibility);
        SessionState.SetBool(Key + "minimapResolution", minimapResolution);
        SessionState.SetBool(Key + "progressionOnly", progressionOnly);
        SessionState.SetBool(Key + "escapeSettlementOnly", escapeSettlementOnly);
        SessionState.SetInt(Key + "escapeCases", escapeCases);
        SessionState.SetInt(Key + "caveCount", caveCount);
        SessionState.SetBool(Key + "verifyMapRequired", verifyMapRequired);
        SessionState.SetBool(Key + "speciesGroupsOnly", speciesGroupsOnly);
        SessionState.SetBool(Key + "caveOnly", caveOnly);
        SessionState.SetBool(Key + "caveBakedSpawnOnly", caveBakedSpawnOnly);
        SessionState.SetBool(Key + "includeLargeCave", includeLargeCave);
        Write("scope.json", new { caveCount, scaleOnly = caveCount > 0, excludes = caveCount > 0 ? "prior failure-injection, escape, resolution and full-exhaustion tests" : null, includeLargeCave, deferred = includeLargeCave ? null : "20-platform cave deferred by user until platform roster expansion" });
        SessionState.SetString(Key + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "scenes", Scenes()); SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + (performance != null ? 7200 : minimapCycles ? 1200 : 600));
        Write("edit-before.json", new { scenes = Scenes(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), previewCount = EditorSceneManager.previewSceneCount });
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", Path.Combine(output, "Settings"));
        Application.runInBackground = true;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update -= Begin; EditorApplication.update += Begin;
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "Account"));
    }
    static bool OwnPlay => IsolatedSavePlayGuard.ActiveDirectory == Path.Combine(SessionState.GetString(Key + "return", ""), "Account");
    static void Begin()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        output = SessionState.GetString(Key + "pending", "");
        if (output == "") { EditorApplication.update -= Begin; return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) { Finish(new Exception("Boot timeout")); return; }
        if (!EditorApplication.isPlaying || !OwnPlay || !TitleCharacterPanel.Instance) return;
        try
        {
            EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + "reloadOwned", true);
            AssetDatabase.DisallowAutoRefresh(); SessionState.SetBool(Key + "refreshOwned", true);
        }
        catch (Exception error) { Finish(error); return; }
        EditorApplication.update -= Begin; SessionState.EraseString(Key + "pending");
        checks.Clear(); observations.Clear(); work.Clear(); Application.runInBackground = true;
        deadline = EditorApplication.timeSinceStartup + (SessionState.GetString(Key + "desertPerformanceProfile", "") != "" ? 7200 : SessionState.GetBool(Key + "minimapCycles", false) || SessionState.GetInt(Key + "completionCases", 0) + SessionState.GetInt(Key + "bossCases", 0) + SessionState.GetInt(Key + "escapeCases", 0) > 0 ? 1200 : 600); lastFrame = -1; work.Push(Verify()); EditorApplication.update += Tick;
    }
    static IEnumerator Frames(int n) { while (n-- > 0) yield return null; }
    static IEnumerator Wait(Func<bool> ready, string label, float seconds = 100)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!ready() && EditorApplication.timeSinceStartup < end) yield return null;
        Check(ready(), label);
    }
    static void Tick()
    {
        if (finishing) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (SessionState.GetString(Key + "desertPerformanceProfile", "") != "")
        {
            if (Time.frameCount == lastFrame && EditorApplication.timeSinceStartup < deadline) return;
            lastFrame = Time.frameCount;
            DesertPerformanceProbe.Tick();
            try
            {
                if (!EditorApplication.isPlaying || !OwnPlay) throw new Exception("Owned performance Play ended unexpectedly");
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Desert performance map timeout");
                while (work.Count > 0)
                {
                    var current = work.Peek();
                    if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); work.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { work.Push(nested); continue; }
                    return;
                }
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
            finally { DesertPerformanceProbe.EndTick(); }
            return;
        }
        var generator = UnityEngine.Object.FindFirstObjectByType<Overburst.Caves.CaveRuntimeGenerator>();
        var pendingGate = UnityEngine.Object.FindFirstObjectByType<RunWorldGate>();
        string stage = pendingGate && pendingGate.PreparationStage != null ? pendingGate.PreparationStage : generator ? generator.Stage : null;
        bool stalled = stage != null && stage == lastStage && EditorApplication.timeSinceStartup - lastStageAt >= 5;
        if (stage != lastStage || stalled)
        {
            lastStage = stage; lastStageAt = EditorApplication.timeSinceStartup;
            File.AppendAllText(Path.Combine(output, "stages.jsonl"), JsonConvert.SerializeObject(new { utc = DateTime.UtcNow,
                stage, courts = generator ? generator.GetComponent<Overburst.Caves.CaveWorld>().courts.Count : 0, frame = Time.frameCount }) + "\n");
            if (stalled && SessionState.GetBool(Key + "minimapOnly", false)) MapExplorationVerifier.InspectPreparing(pendingGate, output);
        }
        if (Time.frameCount == lastFrame && EditorApplication.timeSinceStartup < deadline) return;
        lastFrame = Time.frameCount;
        try
        {
            if (!EditorApplication.isPlaying || !OwnPlay) throw new Exception("Owned Play ended unexpectedly");
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Entry verification timeout");
            while (work.Count > 0)
            {
                var current = work.Peek();
                if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); work.Pop(); continue; }
                if (current.Current is IEnumerator nested) { work.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception e) { Finish(e); }
    }
    static IEnumerator Verify()
    {
        yield return Frames(6); TitleCharacterPanel.Instance.start.onClick.Invoke(); yield return Frames(3);
        TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(() => AccountBootstrap.Ready && WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, "Isolated title and MainScene boot");
        var account = AccountGameplaySession.Current; var flow = PersistentSceneFlow.Instance;
        PlayerContext.Instance.CurrentActor.Health.SetDamageDeathPrevention(flow, true);
        Check(AccountBootstrap.SaveDirectory == Path.Combine(output, "Account"), "Account is isolated");
        string performanceProfile = SessionState.GetString(Key + "desertPerformanceProfile", "");
        if (performanceProfile != "")
        {
            var item = account.Read().items.Single(i => i.map != null && i.map.terrain == MapTerrainKind.Desert);
            yield return DesertPerformanceProbe.Verify(item.map, item.instanceId, output, DesertPerformanceProfile.Read(performanceProfile));
            yield break;
        }
        if (SessionState.GetBool(Key + "speciesGroupsOnly", false) && !SessionState.GetBool(Key + "caveOnly", false))
            yield return SpeciesSpawnGroupVerifier.VerifyOutdoor(output, Check);
        if (SessionState.GetBool(Key + "minimapCompatibility", false))
        {
            yield return OverburstMinimapTerrainVerifier.VerifyInOwnedPlay(output, Check);
            yield break;
        }
        if (SessionState.GetBool(Key + "escapeSettlementOnly", false))
        {
            MapEscapeSettlementVerifier.VerifyPlay(output, Check);
            yield break;
        }
        var maps = account.Read().items.Where(i => i.map != null).ToArray();
        int caveCount = SessionState.GetInt(Key + "caveCount", 0);
        var cave4 = maps.First(i => i.map.terrain == MapTerrainKind.Cave && i.map.areaCount == (caveCount > 0 ? caveCount : 4));
        var cave20 = maps.Single(i => i.map.terrain == MapTerrainKind.Cave && i.map.areaCount == 20);
        var desert = maps.First(i => i.map.terrain == MapTerrainKind.Desert);
        if (caveCount == 0 && !SessionState.GetBool(Key + "desertRoadSpawnOnly", false))
        {
            long revision = account.Revision;
            Check(!flow.EnterRun(DiamondDungeonWorld.SceneName, cave4.map, cave4.instanceId), "Wrong destination is rejected before preparation");
            Check(account.Revision == revision && account.Read().items.Any(i => i.instanceId == cave4.instanceId), "Wrong destination preserves map and account revision");
            Check(flow.EnterMapRun(cave20.map, cave20.instanceId), "Begin cancellation case"); flow.CancelRunEntry();
            yield return Wait(() => !flow.IsSwitching && WorldSessionState.IsHideout, "Cancelled preparation returns to hub");
            Check(account.Read().items.Any(i => i.instanceId == cave20.instanceId), "Cancelled map remains owned");
        }
        var selected = SessionState.GetBool(Key + "largeOnly", false) ? new[] { cave20 } : SessionState.GetBool(Key + "desertOnly", false) ? new[] { desert } : new[] { cave4, desert, cave20 };
        if (SessionState.GetInt(Key + "completionCases", 0) > 0) selected = maps.Where(i => i.map.terrain == MapTerrainKind.Cave && i.map.areaCount == 4).ToArray();
        if (SessionState.GetInt(Key + "bossCases", 0) > 0) selected = maps.Where(i => i.map.terrain == MapTerrainKind.Desert).ToArray();
        if (SessionState.GetBool(Key + "guidanceOnly", false)) selected = new[] { cave4, desert };
        if (SessionState.GetBool(Key + "minimapCycles", false)) selected = maps;
        if (SessionState.GetInt(Key + "escapeCases", 0) > 0) selected = maps.Where(i => i.map.terrain == MapTerrainKind.Desert || i.map.areaCount == 4).ToArray();
        if (!SessionState.GetBool(Key + "includeLargeCave", true)) selected = selected.Where(i => i.map.terrain != MapTerrainKind.Cave || i.map.areaCount < 20).ToArray();
        if (caveCount > 0) selected = new[] { cave4 };
        if (SessionState.GetBool(Key + "caveOnly", false)) selected = new[] { cave4 };
        var priorTextures = new List<Texture2D>();
        int cycle = 0;
        int completionCase = 0;
        foreach (var item in selected)
        {
            cycle++;
            string caseOutput = SessionState.GetBool(Key + "minimapCycles", false) ? Path.Combine(output, "Cycle-" + cycle.ToString("D2")) : output;
            Directory.CreateDirectory(caseOutput);
            PlayerContext.Instance.CurrentActor.Health.SetDamageDeathPrevention(flow, true);
            string label = MapGenerationPolicy.Describe(item.map); double began = EditorApplication.timeSinceStartup;
            if (SessionState.GetBool(Key + "verifyMapRequired", false))
            {
                var portal = UnityEngine.Object.FindFirstObjectByType<MapDungeonPortal>();
                Check(portal && portal.TryInteract(PlayerContext.Instance.CurrentActor) == InteractionExecutionResult.Succeeded, "Map-required panel opens");
                yield return Frames(2);
                var panel = MapDungeonPortalPanel.Active;
                var button = panel.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(value => value.name == "Enter");
                long revision = account.Revision;
                Check(!button.interactable && panel.SelectedMap == null, "No selected map disables entry");
                button.onClick.Invoke(); yield return Frames(2);
                Check(WorldSessionState.IsHideout && !flow.IsSwitching && account.Revision == revision, "No-map button invocation cannot start or save a run");
                Check(!flow.EnterMapRun(item.map) && account.Revision == revision, "Normal scene flow rejects a level-one map without owned item");
                var ui = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
                var slot = ui.inventoryWindow.GetComponentsInChildren<SlotUI>(true).Single(value => value.DisplayItem != null && value.DisplayItem.runtimeInstanceId == item.instanceId);
                Check(panel.TryInsert(slot) && button.interactable, "Owned level-one map enables entry");
                var remove = panel.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(value => value.name == "RemoveMap");
                remove.onClick.Invoke();
                Check(!button.interactable && account.Read().items.Any(value => value.instanceId == item.instanceId), "Removing selection disables entry and preserves map");
                Check(panel.TryInsert(slot), "Owned map can be selected again");
                button.onClick.Invoke();
                Check(flow.IsSwitching, label + ": owned map panel starts entry");
            }
            else Check(flow.EnterMapRun(item.map, item.instanceId), label + ": entry starts");
            yield return Wait(() => !flow.IsSwitching, label + ": transition terminates", 180);
            observations.Add(new { label, seconds = EditorApplication.timeSinceStartup - began, error = flow.RunEntryError, phase = WorldSessionState.Phase.ToString() });
            Write("observations.json", observations);
            if (flow.RunEntryError != null)
                Check(account.Read().items.Any(i => i.instanceId == item.instanceId), label + ": failed preparation preserves map");
            Check(flow.RunEntryError == null && WorldSessionState.Phase == WorldPhase.Run, label + ": entry succeeded: " + flow.RunEntryError);
            Check(account.ReadRun().phase == RunPhase.Active && !account.Read().items.Any(i => i.instanceId == item.instanceId), label + ": map consumed once after preparation");
            var scene = WorldSessionState.ContentScene;
            Check(scene.name == MapTerrainCatalog.SceneNameFor(item.map), label + ": destination matches stored map");
            var gate = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RunWorldGate>()).Single();
            Check(gate.IsReady && gate.EntryPoint && Vector3.Distance(PlayerContext.Instance.CurrentActor.transform.position, gate.EntryPoint.position) < 2, label + ": player placed at prepared entry");
            var roster = gate.Roster;
            Check(roster != null && roster.PlacementReady && roster.Total >= roster.Areas.Count(a => a.Ground.AllowsMonsters && a.Ground.Index != roster.EntryAreaIndex && (gate.BossArena == null || a.Ground.Index != gate.BossArena.Ground.Index)) * (item.map.terrain == MapTerrainKind.Cave ? 8 : 40), label + ": entry-excluded finite population and physical placements prepared before consumption");
            DesertMovementBoundaryVerifier.VerifyEntry(gate, Check);
            Check(roster.ThemeId == item.map.monsterThemeId && roster.Areas.Count == MapGenerationPolicy.EffectiveAreaCount(item.map)
                && roster.Kills == 0 && roster.Active == gate.SpawnBudget.InitialPreparedCount && gate.SpawnBudget.InitialReady
                && roster.Active + roster.Pending == roster.Total, label + ": stored single theme and finite initial actors prepared before entry");
            Check(item.map.terrain == MapTerrainKind.Cave ? roster.Target == MapRunRoster.CaveTarget(roster.Total) : roster.Target == 0, label + ": terrain-specific objective denominator");
            Check(roster.Areas.All(a => a.Count == a.Small + a.Medium + a.Elite
                && roster.Members.Skip(a.FirstMember).Take(a.Count).Count(m => m.Tier == EnemyThemeTier.Small) == a.Small
                && roster.Members.Skip(a.FirstMember).Take(a.Count).Count(m => m.Tier == EnemyThemeTier.Medium) == a.Medium
                && roster.Members.Skip(a.FirstMember).Take(a.Count).Count(m => m.Tier == EnemyThemeTier.Elite) == a.Elite && a.InitialCount == roster.Members.Skip(a.FirstMember).Take(a.Count).Count(m => m.Initial)), label + ": natural and initial allocation sums");
            foreach (var area in roster.Areas)
            {
                var initial = roster.Members.Skip(area.FirstMember).Take(area.Count).Where(m => m.Initial).ToArray();
                Check(initial.All(m => MapCombatGeometry.CanPlace(area.Ground, m.Definition, m.PlannedPosition, true, m.PlannedOnRoad)), label + ": actual body clearance at all initial positions in area " + area.Ground.Index);
                bool separated = true;
                for (int i = 0; i < initial.Length; i++) for (int j = i + 1; j < initial.Length; j++)
                {
                    var delta = initial[i].PlannedPosition - initial[j].PlannedPosition; delta.y = 0;
                    float space = MapCombatGeometry.BodyFor(initial[i].Definition).Radius + MapCombatGeometry.BodyFor(initial[j].Definition).Radius + MapCombatGeometry.BodyGap;
                    if (delta.sqrMagnitude + .00001f < space * space) separated = false;
                }
                Check(separated, label + ": initial bodies do not overlap in area " + area.Ground.Index);
            }
            Write("roster-" + item.map.terrain + "-" + item.map.areaCount + ".json", new { roster.Total, roster.Target, roster.ThemeId,
                areas = roster.Areas.Select(a => new { a.Ground.Index, a.Ground.Name, a.Ground.Band, a.Ground.SquareMetres, points = a.Ground.Points.Count, platformSize = MapRunRoster.PlatformSize(roster.Terrain, a.Ground).ToString(), a.ConcurrentLimit, a.BasePopulation, a.Count, a.InitialCount, a.Small, a.Medium, a.Elite }),
                members = roster.Members.Select(m => new { m.Id, enemy = m.Definition.EnemyId, m.AreaIndex, m.Initial, m.PlannedOnRoad, position = new[] { m.PlannedPosition.x, m.PlannedPosition.y, m.PlannedPosition.z } }) });
            if (item.map.terrain == MapTerrainKind.Cave)
            {
                var cave = gate.GetComponent<Overburst.Caves.CaveWorld>();
                Check(cave.courts.Count == MapGenerationPolicy.EffectiveAreaCount(item.map) && cave.seed == item.map.seed, label + ": count and seed match saved map");
                var sizes = roster.Areas.Select(a => MapRunRoster.PlatformSize(roster.Terrain, a.Ground)).ToArray();
                Check(sizes.Count(size => size == Overburst.Mojave.MojaveCombatSize.Small) <= roster.Areas.Count / 3
                    && sizes[roster.EntryAreaIndex] == Overburst.Mojave.MojaveCombatSize.Small,
                    label + ": small safe entry and at most one third small platforms");
                Check(roster.Areas.Any(a => a.Count > 0 && MapRunRoster.PlatformSize(roster.Terrain, a.Ground) == Overburst.Mojave.MojaveCombatSize.Large)
                    && roster.Areas.Any(a => a.Count > 0 && MapRunRoster.PlatformSize(roster.Terrain, a.Ground) == Overburst.Mojave.MojaveCombatSize.Medium),
                    label + ": a combat large and medium platform are guaranteed");
                Check(roster.Areas.Where(a => MapRunRoster.InitialOnly(roster.Terrain, a.Ground)).All(a => a.Count <= 8 && a.Count == a.InitialCount && a.Elite == 0),
                    label + ": small platforms have initial-only population and no elites");
                Check(!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Overburst.Caves.CaveDungeonPortal>()).Any(), label + ": no premature abandon portal");
            }
            else
            {
                var world = gate.GetComponent<Overburst.Mojave.DesertRunWorld>();
                Check(world && world.AreaCenters.Count == item.map.areaCount, label + ": all areas reachable");
                Check(world.ConservativeObstacleCount > 0, label + ": unreadable rock meshes retain blocking navigation footprints");
                Check(!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Overburst.Mojave.MojaveExplorer>(true)).Any(c => c.isActiveAndEnabled), label + ": authoring explorer disabled");
                Check(!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Overburst.Mojave.MojaveCamera>(true)).Any(c => c.isActiveAndEnabled), label + ": authoring camera disabled");
            }
            if (roster.Active == 0)
            {
                var combat = roster.Areas.First(a => a.Count > 0);
                var approach = MapCombatGeometry.Approaches(combat.Ground)
                    .Where(p => Vector3.Distance(p, combat.Ground.Center) >= 18 && Vector3.Distance(p, combat.Ground.Center) <= 30)
                    .OrderBy(p => Vector3.Distance(p, gate.EntryPoint.position)).First();
                var player = PlayerContext.Instance.CurrentActor;
                ActorTeleportUtility.TeleportSafely(player.transform, approach + Vector3.up * .12f, player.transform.rotation);
                yield return Wait(() => roster.Active > 0, label + ": approaching a combat tile activates its natural initial population", 30);
            }
            if (SessionState.GetBool(Key + "desertRoadSpawnOnly", false)) yield return VerifyRoadSpawns(gate);
            if (SessionState.GetBool(Key + "desertBoundaryOnly", false) && gate.GetComponent<Overburst.Mojave.DesertRunWorld>())
                yield return DesertMovementBoundaryVerifier.VerifyPlay(gate, output, Check);
            if (SessionState.GetBool(Key + "guidanceOnly", false))
            {
                yield return MapGuidanceVerifier.VerifyPlay(gate, output, Check);
                continue;
            }
            if (SessionState.GetInt(Key + "completionCases", 0) > 0)
            {
                yield return MapCompletionPlayVerifier.Verify(gate, completionCase++, output, Check);
                continue;
            }
            if (SessionState.GetInt(Key + "bossCases", 0) > 0)
            {
                yield return MapDesertCompletionPlayVerifier.Verify(gate, completionCase++, output, Check);
                continue;
            }
            if (SessionState.GetInt(Key + "escapeCases", 0) > 0)
            {
                yield return MapEscapeRuntimeVerifier.Verify(gate, completionCase++ / 2, output, Check);
                continue;
            }
            if (SessionState.GetBool(Key + "caveBakedSpawnOnly", false) && gate.GetComponent<Overburst.Caves.CaveWorld>())
                CaveBakedSpawnIntegrationVerifier.VerifyPlay(gate, output, Check);
            if (SessionState.GetBool(Key + "caveStairTraversalOnly", false)) yield return CaveStairTraversalVerifier.Verify(gate, output, Check);
            else if (caveCount > 0) yield return MapFiniteSpawnPlayVerifier.VerifyScale(gate, output, Check);
            else if (SessionState.GetBool(Key + "progressionOnly", false)) yield return MapCaveProgressionPlayVerifier.Verify(gate, Check);
            if (SessionState.GetBool(Key + "minimapOnly", false))
            {
                if (SessionState.GetBool(Key + "minimapResolution", false)) yield return MapMinimapResolutionVerifier.Verify(gate, caseOutput, Check);
                else yield return MapExplorationVerifier.VerifyPlay(gate, caseOutput, Check);
                var current = WorldMinimapController.Instance.Exploration;
                Check(priorTextures.All(t => !t), label + ": previous run textures remain released");
                priorTextures.Add(current.TerrainTexture); priorTextures.Add(current.OutlineTexture);
            }
            else if (SessionState.GetBool(Key + "speciesGroupsOnly", false)) yield return SpeciesSpawnGroupVerifier.VerifyMap(gate, output, Check);
            else if (caveCount == 0) yield return MapFiniteSpawnPlayVerifier.Verify(gate, output, Check);
            yield return Frames(12);
            if (gate.GetComponent<Overburst.Caves.CaveWorld>()) CavePlatformEditVerifier.VerifyPlay(gate, Check, output);
            flow.GetComponent<RunLifetimeDriver>().RequestAbandon();
            yield return Wait(() => !flow.IsSwitching && WorldSessionState.IsHideout, label + ": abandon returns to hub");
            Check(account.ReadRun().phase == RunPhase.Failed, label + ": existing abandon settlement maintained");
            if (SessionState.GetBool(Key + "minimapOnly", false))
            {
                var controller = WorldMinimapController.Instance;
                Write("minimap-hub-return-" + item.map.terrain + "-before.json", new { explored = controller.Exploration != null,
                    texture = controller.View.TerrainImage.texture ? controller.View.TerrainImage.texture.name : null });
                yield return Wait(() => controller.Exploration == null && controller.View.TerrainImage.texture != null,
                    label + ": unloading the run releases exploration and restores the hub bake", 5);
                Check(priorTextures.All(t => !t), label + ": both textures from every visited run have been destroyed");
                Check(!Resources.FindObjectsOfTypeAll<Texture2D>().Any(t => t.name == "Explored terrain (run)" || t.name == "Explored outline (run)"),
                    label + ": no run-owned exploration texture accumulates at the hub");
                var source = (MinimapEnemySource)typeof(WorldMinimapController).GetField("source", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                Check(source.RegisteredCount == 0 && controller.Guides.Count == 0 && !controller.OverlayRequested,
                    label + ": old enemy subscriptions, guidance and overlay request are cleared at the hub");
            }
        }
    }
    static IEnumerator VerifyRoadSpawns(RunWorldGate gate)
    {
        var roster = gate.Roster; var world = gate.GetComponent<Overburst.Mojave.MojaveWorld>();
        var bank = DesertSpawnData.Load(world);
        Check(bank && bank.areas.Length == roster.Areas.Count, "Desert road: all platforms use the saved spawn bank");
        var baseline = MapRunRoster.Build(roster.RunId, gate.Map, roster.Areas.Select(a => a.Ground).ToArray(), gate.BossArena.Ground.Index, roster.EntryAreaIndex);
        Check(baseline.Total == roster.Total && baseline.Target == roster.Target
            && baseline.Members.Select(m => (m.Id, m.Definition, m.Tier)).SequenceEqual(roster.Members.Select(m => (m.Id, m.Definition, m.Tier)))
            && baseline.Areas.Select(a => (a.Count, a.InitialCount, a.Small, a.Medium, a.Elite)).SequenceEqual(roster.Areas.Select(a => (a.Count, a.InitialCount, a.Small, a.Medium, a.Elite))),
            "Desert road: population, initial counts, definitions, tiers and seeded IDs are unchanged");
        var roads = roster.Members.Where(m => m.PlannedOnRoad).ToArray();
        Check(roads.Length > 0 && roads.All(m => m.Initial && m.Tier == EnemyThemeTier.Small), "Desert road: sparse initial small packs exist");
        foreach (var group in roads.GroupBy(m => m.AreaIndex))
        {
            Check(group.Count() == MapCombatGeometry.GroupSize(EnemyThemeTier.Small) && group.Select(m => m.Definition).Distinct().Count() == 1,
                "Desert road: one same-species small pack in area " + group.Key);
            foreach (var member in group)
            {
                var p = member.PlannedPosition; var xz = new Vector2(p.x, p.z);
                var path = new UnityEngine.AI.NavMeshPath();
                Check(world.layout.places.All(place => place.Distance(xz) > 1f) && world.layout.RoomDistance(xz, out _) > 1f
                    && Vector2.Distance(xz, new Vector2(gate.EntryPoint.position.x, gate.EntryPoint.position.z)) >= MapCombatGeometry.EntrySafety
                    && MapCombatGeometry.CanPlace(roster.Areas[member.AreaIndex].Ground, member.Definition, p, true, true)
                    && UnityEngine.AI.NavMesh.CalculatePath(p, roster.Areas[member.AreaIndex].Ground.Center, UnityEngine.AI.NavMesh.AllAreas, path)
                    && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete, "Desert road: safe connected physical road point " + member.Index);
            }
        }
        var observed = roads.GroupBy(m => m.AreaIndex).OrderBy(g => (g.First().PlannedPosition - gate.EntryPoint.position).sqrMagnitude).First().ToArray();
        var owner = gate.SpawnBudget.Fields[observed[0].AreaIndex];
        var player = PlayerContext.Instance.CurrentActor; var original = player.transform.position; var rotation = player.transform.rotation;
        try
        {
            if (!observed.All(m => owner.TryGetActor(m.Index, out _)))
            {
                var path = new UnityEngine.AI.NavMeshPath();
                // The preceding zero-population activation probe may already be inside 24m.
                // Search the whole entry route, including the safe section behind that probe.
                Check(UnityEngine.AI.NavMesh.CalculatePath(gate.EntryPoint.position, observed[0].PlannedPosition, UnityEngine.AI.NavMesh.AllAreas, path)
                    && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete, "Desert road: player can approach the road pack");
                bool found = false; Vector3 approach = original;
                for (int i = 1; i < path.corners.Length && !found; i++)
                {
                    float length = Vector3.Distance(path.corners[i-1], path.corners[i]);
                    for (float step = 0; step <= length && !found; step += .5f)
                    {
                        var p = Vector3.Lerp(path.corners[i-1], path.corners[i], length > 0 ? step / length : 0);
                        float nearest = observed.Min(m => Vector2.Distance(new Vector2(p.x,p.z), new Vector2(m.PlannedPosition.x,m.PlannedPosition.z)));
                        if (nearest < 24 || nearest > 34 || roster.Members.Any(m => m.Initial
                            && Vector2.Distance(new Vector2(p.x,p.z),new Vector2(m.PlannedPosition.x,m.PlannedPosition.z)) < MapCombatGeometry.EntrySafety)) continue;
                        approach = p; found = true;
                    }
                }
                Check(found, "Desert road: safe approach wakes the pack before it enters view");
                ActorTeleportUtility.TeleportSafely(player.transform, approach + Vector3.up * .12f, Quaternion.identity);
                yield return Wait(() => observed.All(m => owner.TryGetActor(m.Index, out var actor) && actor && !actor.Health.IsDead),
                    "Desert road: approaching creates real living road actors", 30);
            }
            Check(observed.All(m => m.State == MapRunRoster.Life.Active && owner.TryGetActor(m.Index, out var actor)
                && world.layout.RoomDistance(new Vector2(actor.transform.position.x, actor.transform.position.z), out _) > 0),
                "Desert road: actual small pack stands on the road outside combat platforms");
        }
        finally { ActorTeleportUtility.TeleportSafely(player.transform, original, rotation); }
        Write("road-spawns.json", new { status = "PASS", gate.Map.desertCandidateId, roster.Total,
            initial = roster.Members.Count(m => m.Initial), roadMembers = roads.Length, roadPacks = roads.Select(m => m.AreaIndex).Distinct().Count(),
            actualRoadActorIds = observed.Select(m => m.Id),
            points = roads.Select(m => new { m.Id, enemy = m.Definition.EnemyId, m.AreaIndex, position = new[] { m.PlannedPosition.x, m.PlannedPosition.y, m.PlannedPosition.z } }),
            scope = "Saved platform bank binding, population parity, every planned road body's clearance/path and actual approached road pack actors; finite lifecycle is checked by the subsequent suite." });
    }

    static void Finish(Exception error)
    {
        if (finishing) return;
        finishing = true;
        EditorApplication.update -= Tick; EditorApplication.update -= Begin;
        while (work.Count > 0)
            try { (work.Pop() as IDisposable)?.Dispose(); }
            catch (Exception cleanupError) { error = error == null ? cleanupError : new AggregateException(error, cleanupError); }
        SessionState.EraseString(Key + "pending"); output = SessionState.GetString(Key + "return", output);
        try { Write("play-result.json", new { status = error == null ? "PASS" : "FAIL", checks, observations, error = error?.ToString(), scope = SessionState.GetString(Key + "desertPerformanceProfile", "") != "" ? "Editor desert traversal capture; completion is coverage, not an FPS acceptance or Player result" : "Editor Play: entry, finite population and selected completion/persistence cases; Player performance and direct control feel not covered" }); }
        finally
        {
            if (EditorApplication.isPlaying && OwnPlay) EditorApplication.ExitPlaymode();
            EditorApplication.update -= Return; EditorApplication.update += Return;
        }
    }
    public static void StopDesertPerformance(string directory)
    {
        if (SessionState.GetString(Key + "return", "") != directory || SessionState.GetString(Key + "desertPerformanceProfile", "") == "")
            throw new InvalidOperationException("This folder does not own a desert performance session.");
        Finish(new OperationCanceledException("Desert performance cancelled by user"));
    }
    static void ReleaseImportLocks()
    {
        try
        {
            if (SessionState.GetBool(Key + "reloadOwned", false))
            { SessionState.EraseBool(Key + "reloadOwned"); EditorApplication.UnlockReloadAssemblies(); }
        }
        finally
        {
            if (SessionState.GetBool(Key + "refreshOwned", false))
            { SessionState.EraseBool(Key + "refreshOwned"); AssetDatabase.AllowAutoRefresh(); }
        }
    }
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) ReleaseImportLocks();
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(Key + "return", "") != "")
        { EditorApplication.update -= Return; EditorApplication.update += Return; }
    }
    static void Return()
    {
        output = SessionState.GetString(Key + "return", "");
        if (output == "") { EditorApplication.update -= Return; return; }
        if (!EditorApplication.isPlayingOrWillChangePlaymode) ReleaseImportLocks();
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.ActiveDirectory != "") return;
        string scene = SessionState.GetString(Key + "start", "");
        EditorSceneManager.playModeStartScene = scene == "" ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        if (Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY") == Path.Combine(output, "Settings")) Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", null);
        Application.runInBackground = SessionState.GetBool(Key + "background", false); IsolatedSavePlayGuard.UseRealAccount();
        bool preserved = Scenes() == SessionState.GetString(Key + "scenes", "");
        Write("return-result.json", new { status = preserved && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL", scenes = Scenes(), preserved, blocked = IsolatedSavePlayGuard.RequiresAccountChoice, save = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) });
        foreach (var key in new[] { "return", "start", "scenes" }) SessionState.EraseString(Key + key);
        SessionState.EraseFloat(Key + "deadline"); SessionState.EraseBool(Key + "background"); EditorApplication.update -= Return;
        SessionState.EraseBool(Key + "desertOnly");
        SessionState.EraseBool(Key + "desertRoadSpawnOnly");
        SessionState.EraseBool(Key + "desertBoundaryOnly");
        SessionState.EraseString(Key + "desertPerformanceProfile");
        SessionState.EraseBool(Key + "caveStairTraversalOnly");
        SessionState.EraseBool(Key + "largeOnly"); SessionState.EraseInt(Key + "completionCases"); SessionState.EraseInt(Key + "bossCases");
        SessionState.EraseBool(Key + "minimapOnly");
        SessionState.EraseBool(Key + "guidanceOnly");
        SessionState.EraseBool(Key + "minimapCycles");
        SessionState.EraseBool(Key + "minimapCompatibility");
        SessionState.EraseBool(Key + "minimapResolution");
        SessionState.EraseBool(Key + "progressionOnly");
        SessionState.EraseBool(Key + "escapeSettlementOnly");
        SessionState.EraseInt(Key + "escapeCases");
        SessionState.EraseInt(Key + "caveCount");
        SessionState.EraseBool(Key + "verifyMapRequired");
        SessionState.EraseBool(Key + "speciesGroupsOnly");
        SessionState.EraseBool(Key + "caveOnly");
        SessionState.EraseBool(Key + "caveBakedSpawnOnly");
        SessionState.EraseBool(Key + "includeLargeCave");
    }
}
