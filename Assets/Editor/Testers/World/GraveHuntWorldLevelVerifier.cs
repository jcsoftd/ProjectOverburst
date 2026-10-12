using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GraveHuntWorldLevelVerifier
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object owner, string name) => owner.GetType().GetField(name, Fields).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Fields).SetValue(owner, value);
    static object[] Rows(object owner, string name) => ((IEnumerable)Read(owner, name)).Cast<object>().ToArray();
    static void Write(string output, string name, object value) =>
        File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f * Mathf.Max(1f, Mathf.Abs(b));
    static float PatternDamage(EnemyAbilityDefinition ability, int level, float multiplier)
    {
        if (ability.HasWeakAttackExecution && !ability.IsTelegraphedStrongAttack)
        {
            if (!ability.TryResolveWeakDamageBudget(level, multiplier, out var budget))
                throw new InvalidOperationException("Invalid weak budget: " + ability.name);
            return budget.Total;
        }
        return ability.ResolveDamage(level) * ability.HitCount * multiplier;
    }

    public static void CaptureBefore(string output)
    {
        Directory.CreateDirectory(output);
        var table = MapThemeCatalog.Resolve("GraveHunt");
        Write(output, "native-before.json", new
        {
            rows = table.Entries.Select(e =>
            {
                var d = e.definition; var s = d.ResolveRuntimeStats();
                return new
                {
                    id = d.EnemyId, damage = s.DamageMultiplier, gap = s.AttackInterval,
                    abilities = Enumerable.Range(0, d.AbilitySet.Count).Select(i =>
                    {
                        var a = d.AbilitySet.GetAbility(i);
                        return new
                        {
                            id = a.name, cooldown = a.Cooldown,
                            times = new[] { 1f, OverburstBalanceTable.Current.EnemyAttackCap }.Select(g => new
                            { growth = g, first = a.ResolveFirstImpactTime(s.AttackSpeedMultiplier * g), duration = a.ResolveExecutionDuration(s.AttackSpeedMultiplier * g) }),
                            damage = new[] { 1, 50, 100 }.Select(level => new { level, value = PatternDamage(a, level, s.DamageMultiplier) })
                        };
                    })
                };
            }),
            rosters = Resources.LoadAll<EnemyThemeTable>("Enemies/Themes/Tables")
                .Where(t => t.Validate(out _)).ToDictionary(t => t.ThemeId, t => t.BuildRoster(40, 9, 1, 732).Select(d => d.EnemyId).ToArray())
        });
    }

    public static string Native(string output)
    {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(output, "approved-plan.json")));
        var before = JObject.Parse(File.ReadAllText(Path.Combine(output, "native-before.json")));
        var table = MapThemeCatalog.Resolve("GraveHunt");
        Check(table && table.Validate(out _) && table.Entries.Count == 14, "actual 14-species GraveHunt table validates");
        foreach (var row in plan["rows"])
        {
            string id = (string)row["id"]; var entry = table.Entries.Single(e => e.definition.EnemyId == id);
            var d = entry.definition; var s = d.ResolveRuntimeStats(); var p = row["proposal"];
            float rate = (float)p["attackRate"]; var old = before["rows"].Single(r => (string)r["id"] == id);
            int stage = (int)plan["stages"].Single(t => t["add"].Values<string>().Contains(id))["stage"];
            Check(entry.minimumOutdoorWorldLevel == stage, id + ": minimum outdoor stage");
            Check(Near(s.AttackSpeedMultiplier, rate) && Near(s.DamageMultiplier, (float)old["damage"] / rate)
                && Near(s.AttackInterval, (float)old["gap"] / rate), id + ": authored attack/damage/gap ratios");
            Check(Near(d.MovementProfile.MoveSpeed * s.MoveSpeedMultiplier, (float)p["walk"])
                && Near(d.MovementProfile.MoveSpeed * d.MovementProfile.RunSpeedMultiplier * s.MoveSpeedMultiplier, (float)p["chase"]), id + ": walk/chase speed");
            Check(d.Variant.name == "EVP_" + id && d.Variant.IsValid, id + ": dedicated variant");
            Check(d.ActorPrefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), id + ": prefab has no missing scripts");
            foreach (var a0 in old["abilities"])
            {
                var a = Enumerable.Range(0, d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).Single(x => x.name == (string)a0["id"]);
                Check(Near(a.Cooldown, (float)a0["cooldown"] / rate), a.name + ": cooldown inverse ratio");
                foreach (var t in a0["times"])
                    Check(Near(a.ResolveFirstImpactTime(rate * (float)t["growth"]), (float)t["first"] / rate)
                        && Near(a.ResolveExecutionDuration(rate * (float)t["growth"]), (float)t["duration"] / rate), a.name + ": timeline scaling at growth " + t["growth"]);
                foreach (var damage in a0["damage"])
                    Check(Near(PatternDamage(a, (int)damage["level"], s.DamageMultiplier), (float)damage["value"] / rate), a.name + ": pattern budget at level " + damage["level"]);
            }
        }
        var samples = new List<object>();
        var observed = new HashSet<string>();
        var node = new GameObject("Owned outdoor progression queue verifier"); node.SetActive(false);
        try
        {
            var spawner = node.AddComponent<OutdoorWorldSpawner>(); Set(spawner, "theme", table);
            for (int stage = 1; stage <= 10; stage++)
            {
                var expected = plan["stages"][Math.Min(stage, 5) - 1]["allowed"].Values<string>().ToHashSet();
                var seen = new HashSet<string>(); int elites = 0, total = 0;
                for (int x = 0; x < 16; x++)
                {
                    var cell = new Vector2Int(x, 1); Set(spawner, "stage", stage);
                    typeof(OutdoorWorldSpawner).GetMethod("QueueArea", Fields).Invoke(spawner, new object[] { cell });
                    var queued = Rows(spawner, "pending");
                    Check(queued.Length == 50 && queued.All(r => expected.Contains(((EnemyDefinition)Read(r, "definition")).EnemyId)), "stage " + stage + "/cell " + x + ": 50 members and no locked species");
                    Check(queued.All(r => (int)Read(r, "level") >= AccountWorldLevelCommands.MinimumMonsterLevel(stage)
                        && (int)Read(r, "level") <= AccountWorldLevelCommands.MaximumMonsterLevel(stage)), "stage " + stage + "/cell " + x + ": level range");
                    int elite = queued.Count(r => MapSpawnPolicy.Tier(table, (EnemyDefinition)Read(r, "definition")) == EnemyThemeTier.Elite);
                    int seed = unchecked(73130 + cell.x * 73856093 + cell.y * 19349663 + stage * 83492791);
                    int expectedElite = stage == 1 || stage == 2 && (seed & 1) != 0 ? 0 : 1;
                    Check(elite == expectedElite, "stage " + stage + "/cell " + x + ": elite quota");
                    foreach (var r in queued) seen.Add(((EnemyDefinition)Read(r, "definition")).EnemyId);
                    total += queued.Length; elites += elite;
                    Read(spawner, "pending").GetType().GetMethod("Clear").Invoke(Read(spawner, "pending"), null);
                }
                Check(seen.SetEquals(expected), "stage " + stage + ": every unlocked species can appear");
                samples.Add(new { stage, total, elites, species = seen.OrderBy(s => s).ToArray() });
                if (stage >= 5) observed.UnionWith(seen);
            }
        }
        finally { Object.DestroyImmediate(node); }
        Check(observed.Count == 14, "stage5+ includes both elites and all 14 species");
        foreach (var property in ((JObject)before["rosters"]).Properties())
        {
            var t = MapThemeCatalog.Resolve(property.Name);
            Check(t.BuildRoster(40, 9, 1, 732).Select(d => d.EnemyId).SequenceEqual(property.Value.Values<string>()), property.Name + ": unfiltered map/debug roster unchanged");
        }
        Write(output, "native-result.json", new { success = true, count = checks.Count, checks, samples });
        return "PASS " + checks.Count;
    }

    static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
    static void Freeze(EnemyActor actor)
    { actor.AI.enabled = false; actor.Melee.CancelAttack(); actor.Movement.StopMovement(); actor.Movement.enabled = false; }

    static IEnumerator VerifyAttack(EnemyActor enemy, PlayerActorRuntime player, Terrain terrain, string output, Action<bool, string> check)
    {
        var definition = enemy.Definition;
        var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility).First(a => !a.IsTelegraphedStrongAttack);
        var originalPlayer = player.transform.position;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Owned GraveHunt combat floor";
        floor.transform.position = new Vector3(19000, -.5f, 19000); floor.transform.localScale = new Vector3(30, 1, 30);
        int groundLayer = LayerMask.NameToLayer("Ground"); if (groundLayer >= 0) floor.layer = groundLayer;
        ActorTeleportUtility.TeleportSafely(player.transform, new Vector3(19000, .05f, 19000), Quaternion.identity);
        var crowd = enemy.GetComponent<EnemyCrowdAgent>(); bool crowdEnabled = crowd && crowd.enabled;
        if (crowd) crowd.enabled = false;
        float distance = Mathf.Max(.8f, EnemyAttackThreatGeometry.ResolveStartRange(enemy, ability) - .15f);
        var point = player.transform.position + Vector3.forward * distance;
        point.y = .035f;
        var facing = player.transform.position - point; facing.y = 0f;
        ActorTeleportUtility.TeleportSafely(enemy.transform, point, Quaternion.LookRotation(facing));
        enemy.Movement.enabled = true; enemy.Movement.CancelActionLock();
        float speed = enemy.Melee.AbilityAnimationSpeed;
        enemy.AbilityController.Configure(definition.AbilitySet, enemy.RuntimeStats.DamageMultiplier, speed, definition.ResolveRuntimeStats().AttackInterval);
        yield return Frames(3);
        float facingDeadline = Time.time + 2f;
        while (!enemy.Movement.IsFacingForAttack(player.transform.position) && Time.time < facingDeadline)
        { enemy.Movement.FacePosition(player.transform.position); yield return null; }
        float start = Time.time, first = -1f; int hits = 0;
        Action<CombatHealth, DamageInfo> damaged = (_, info) =>
        { if (info.source != enemy.gameObject || ability.HasWeakAttackExecution && info.sourceAttackSequenceId == 0) return; hits++; if (first < 0) first = Time.time - start; };
        player.Health.OnDamaged += damaged;
        try
        {
            Physics.SyncTransforms();
            Write(output, "start-" + definition.EnemyId + ".json", new { distance, source = enemy.transform.position.ToString(), target = player.transform.position.ToString(),
                canStartMelee = enemy.Melee.CanStartAbility(ability, player.transform), facing = enemy.Movement.IsFacingForAttack(player.transform.position),
                movementLocked = enemy.Movement.IsActionLocked, intervalReady = enemy.AbilityController.IsAttackIntervalReady,
                animationBlocked = enemy.GetComponent<EnemyAnimationBridge>().BlocksAttackStart });
            check(enemy.AbilityController.TryStartAbility(ability, player.transform), definition.EnemyId + ": actual weak attack starts");
            float deadline = Time.time + ability.ResolveExecutionDuration(speed) + 2f;
            while (enemy.Melee.IsAttacking && Time.time < deadline) yield return null;
            Write(output, "impact-" + definition.EnemyId + ".json", new { hits, first, attacking = enemy.Melee.IsAttacking,
                clock = enemy.Melee.WeakAttackNormalizedTime, expectedFirst = ability.ResolveFirstImpactTime(speed),
                elapsed = Time.time - start, source = enemy.transform.position.ToString(), target = player.transform.position.ToString() });
            check(!enemy.Melee.IsAttacking && hits > 0, definition.EnemyId + ": actual attack completes and damages player");
            check(first >= ability.ResolveFirstImpactTime(speed) - .12f && first <= ability.ResolveFirstImpactTime(speed) + .6f, definition.EnemyId + ": actual first contact follows authored timing");
            check(!enemy.AbilityController.IsAttackIntervalReady, definition.EnemyId + ": post-attack interval blocks immediate repeat");
            float gap = enemy.AbilityController.AttackInterval, recoveryAt = Time.time;
            check(Near(enemy.AbilityController.RemainingAttackInterval, gap), definition.EnemyId + ": actual configured gap begins after completion");
            while (!enemy.AbilityController.IsAttackIntervalReady && Time.time < recoveryAt + gap + 2f) yield return null;
            check(enemy.AbilityController.IsAttackIntervalReady && Time.time - recoveryAt >= gap - .01f, definition.EnemyId + ": repeat unlocks after configured gap");
            Write(output, "combat-" + definition.EnemyId + ".json", new { id = definition.EnemyId, ability = ability.name, speed, hits,
                first, expectedFirst = ability.ResolveFirstImpactTime(speed), gap, observedGap = Time.time - recoveryAt });
        }
        finally
        {
            player.Health.OnDamaged -= damaged; Freeze(enemy); if (crowd) crowd.enabled = crowdEnabled;
            ActorTeleportUtility.TeleportSafely(player.transform, originalPlayer, Quaternion.identity); Object.DestroyImmediate(floor);
        }
    }

    public static IEnumerator VerifyPlay(string output, Action<bool, string> check)
    {
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(Directory.GetParent(output).FullName, "approved-plan.json")));
        var account = AccountGameplaySession.Current; var spawner = OutdoorWorldSpawner.Current;
        var player = PlayerContext.Instance.CurrentActor; var town = player.transform.position;
        var terrain = (Terrain)Read(spawner, "terrain"); var table = (EnemyThemeTable)Read(spawner, "theme");
        var samples = new List<object>(); var bindings = new List<object>(); var runtimeSpecies = new HashSet<string>();
        check(spawner && table.ThemeId == "GraveHunt", "actual MainScene outdoor spawner uses GraveHunt");
        check(account.UnlockWorldLevels(account.Revision) && account.Read().unlockedWorldLevel == 10, "isolated character unlocks all existing stages");
        try
        {
            for (int stage = 1; stage <= 10; stage++)
            {
                ActorTeleportUtility.TeleportSafely(player.transform, town, Quaternion.identity);
                if (account.AppliedWorldLevel != stage) check(account.ApplyWorldLevel(account.Revision, stage), "apply actual world stage " + stage);
                spawner.ResetForWorldLevel(); yield return Frames(4);
                check(spawner.ActiveCount == 0 && spawner.PendingCount == 0, "stage " + stage + ": previous generation returned");
                var point = new Vector3(154, 0, 76);
                float size = (float)Read(spawner, "areaSize");
                var local = point - terrain.transform.position;
                var cell = new Vector2Int(Mathf.FloorToInt(local.x / size), Mathf.FloorToInt(local.z / size));
                int seed = unchecked(73130 + cell.x * 73856093 + cell.y * 19349663 + stage * 83492791);
                if (stage == 2 && (seed & 1) != 0) point.x += size;
                point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                ActorTeleportUtility.TeleportSafely(player.transform, point, Quaternion.identity);
                var actors = new Dictionary<int, EnemyActor>(); double deadline = EditorApplication.timeSinceStartup + 35;
                while ((spawner.PendingCount > 0 || actors.Count < 50) && EditorApplication.timeSinceStartup < deadline)
                {
                    foreach (var row in Rows(spawner, "leases"))
                    {
                        var enemy = (EnemyActor)Read(row, "actor"); if (!enemy || actors.ContainsKey(enemy.GetInstanceID())) continue;
                        Freeze(enemy); actors.Add(enemy.GetInstanceID(), enemy);
                    }
                    yield return null;
                }
                foreach (var row in Rows(spawner, "leases"))
                {
                    var enemy = (EnemyActor)Read(row, "actor"); if (enemy && !actors.ContainsKey(enemy.GetInstanceID())) { Freeze(enemy); actors.Add(enemy.GetInstanceID(), enemy); }
                }
                var allowed = plan["stages"][Math.Min(stage, 5) - 1]["allowed"].Values<string>().ToHashSet();
                check(actors.Count == 50 && spawner.PendingCount == 0 && spawner.VisitedCount == 1, "stage " + stage + ": actual cell fully spawns 50");
                check(actors.Values.All(a => allowed.Contains(a.Definition.EnemyId)), "stage " + stage + ": no locked species in actual leases");
                check(actors.Values.All(a => a.GetComponent<EnemyRank>().Level >= AccountWorldLevelCommands.MinimumMonsterLevel(stage)
                    && a.GetComponent<EnemyRank>().Level <= AccountWorldLevelCommands.MaximumMonsterLevel(stage)), "stage " + stage + ": actual enemy levels");
                int elites = actors.Values.Count(a => MapSpawnPolicy.Tier(table, a.Definition) == EnemyThemeTier.Elite);
                check(elites == (stage == 1 ? 0 : 1), "stage " + stage + ": actual elite quota");
                foreach (var enemy in actors.Values.GroupBy(a => a.Definition.EnemyId).Select(g => g.First()))
                {
                    var p = plan["rows"].Single(r => (string)r["id"] == enemy.Definition.EnemyId)["proposal"];
                    var stats = enemy.Definition.ResolveRuntimeStats(); int level = enemy.GetComponent<EnemyRank>().Level;
                    var balance = OverburstBalanceTable.Current;
                    float growth = (OverburstGrowthRules.ClampLevel(level) - 1) / 99f;
                    float expectedWalk = enemy.Definition.MovementProfile.MoveSpeed
                        * Mathf.Min(balance.EnemyMoveCap, stats.MoveSpeedMultiplier * (1f + balance.EnemyMoveGrowth * growth));
                    check(Near(enemy.Movement.MoveSpeed, expectedWalk) && enemy.Movement.Profile == enemy.Definition.MovementProfile, enemy.Definition.EnemyId + ": actual movement binding with existing level growth");
                    check(Near(enemy.Melee.AbilityAnimationSpeed, MapOptionPolicy.AttackSpeedMultiplier(stats, null, level, false))
                        && Near(enemy.AbilityController.AttackInterval, (float)p["postAttackGap"]), enemy.Definition.EnemyId + ": actual attack speed and interval");
                    runtimeSpecies.Add(enemy.Definition.EnemyId);
                    bindings.Add(new { stage, id = enemy.Definition.EnemyId, level, actualWalk = enemy.Movement.MoveSpeed, expectedWalk,
                        actualAttackSpeed = enemy.Melee.AbilityAnimationSpeed, actualGap = enemy.AbilityController.AttackInterval });
                }
                samples.Add(new { stage, count = actors.Count, elites, members = actors.Values.Select(a => new { id = a.Definition.EnemyId, level = a.GetComponent<EnemyRank>().Level }).ToArray() });
                if (stage == 2 || stage == 5) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "stage-" + stage + ".png")); yield return Frames(3); }
                string combatId = stage == 1 ? "GraveHunt_GiantRat" : stage == 2 ? "GraveHunt_Minotaur" : stage == 3 ? "GraveHunt_Barghest"
                    : stage == 4 ? "V3_Ghoul" : stage == 5 ? "DeathHarvest_Reaper" : null;
                var combatActor = combatId == null ? null : actors.Values.FirstOrDefault(a => a.Definition.EnemyId == combatId);
                if (combatActor) yield return VerifyAttack(combatActor, player, terrain, output, check);
                var oldContext = actors.Values.First().GetComponent<EnemyRank>().Encounter;
                ActorTeleportUtility.TeleportSafely(player.transform, town, Quaternion.identity); yield return Frames(5);
                check(spawner.ActiveCount == 0 && spawner.PendingCount == 0 && spawner.VisitedCount == 0 && !oldContext.CanGrantRewards, "stage " + stage + ": town reset and old rewards invalidated");
            }
            check(runtimeSpecies.Count == 14, "all 14 species observed as actual pooled actors across stages1-10");
            check(account.ApplyWorldLevel(account.Revision, 1), "lower applied stage to1 after unlocking10");
            spawner.ResetForWorldLevel(); yield return Frames(3);
            check(account.Read().unlockedWorldLevel == 10 && spawner.AppliedStage == 1, "unlocked maximum does not override applied stage");
            check(account.FlushPendingSave(), "isolated stage checkpoint flushed");
            var saved = new EasySaveAccountStore(Path.Combine(output, "isolated-save")).Load();
            check(saved.appliedWorldLevel == 1 && saved.unlockedWorldLevel == 10, "stage selection survives disk reload");
            Write(output, "stage-samples.json", new { samples, bindings, runtimeSpecies = runtimeSpecies.OrderBy(s => s).ToArray() });
        }
        finally
        {
            ActorTeleportUtility.TeleportSafely(player.transform, town, Quaternion.identity);
            spawner.ResetForWorldLevel();
        }
    }
}
