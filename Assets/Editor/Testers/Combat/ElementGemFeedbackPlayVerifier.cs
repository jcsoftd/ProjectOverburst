using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEngine;
using UnityEditor;

// Real product heavy attacks, including complete dark projectile flight and received-hit audio.
[InitializeOnLoad]
public sealed class ElementGemFeedbackPlayVerifier
{
    static ElementGemFeedbackPlayVerifier current;
    IEnumerator work;
    double deadline;
    int frame = -1;
    string output;
    readonly List<string> checks = new List<string>();
    readonly List<object> rounds = new List<object>();
    readonly List<EnemyActor> leased = new List<EnemyActor>();
    readonly List<(CombatHealth health, Action<CombatHealth, DamageInfo, float, bool> callback)> hooks = new List<(CombatHealth, Action<CombatHealth, DamageInfo, float, bool>)>();
    EnemySpawnService spawn;
    EnemyThemeTrialHarness arena;
    MeleeRuntime melee;
    bool enteredArena;
    void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    static ElementGemFeedbackPlayVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode && current != null) current.Finish("Play cancelled."); };
    }

    public static string Run(string directory)
    {
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        if (!EditorApplication.isPlaying || !AccountBootstrap.Ready || PersistentSceneFlow.Instance.IsSwitching
            || !Path.GetFullPath(AccountBootstrap.SaveDirectory).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run only in the owned isolated product Play.");
        if (current != null)
            throw new InvalidOperationException("Feedback verifier already running.");
        current = new ElementGemFeedbackPlayVerifier { output = directory, deadline = EditorApplication.timeSinceStartup + 150 };
        current.work = current.Verify();
        return "RUNNING: five elemental heavy attacks and repeated dark barrage feedback.";
    }

    static void Tick()
    {
        if (current == null || !EditorApplication.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (current.frame == Time.frameCount) return;
        current.frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > current.deadline) throw new TimeoutException("Feedback verification timed out.");
            if (!current.work.MoveNext()) current.Finish(null);
        }
        catch (Exception error) { current?.Finish(error.ToString()); }
    }

    void Finish(string failure)
    {
        (work as IDisposable)?.Dispose(); Cleanup();
        File.WriteAllText(Path.Combine(output, "ActualHeavyFeedback.json"), JsonConvert.SerializeObject(new {
            status = failure == null ? "PASS" : "FAIL", checks, rounds, failure, account = AccountBootstrap.SaveDirectory
        }, Formatting.Indented));
        current = null;
    }

    IEnumerator Verify()
    {
        var actor = PlayerContext.Instance.CurrentActor;
        var equipment = actor.Equipment;
        var player = PlayerInputFacade.Current;
        arena = EnemyThemeTrialHarness.Current;
        if (!arena.InArena) { arena.ToggleArena(); enteredArena = true; }
        yield return null;
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "spawn service available");
        foreach (var table in arena.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), "register enemy catalog " + error);
        var definition = arena.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null && d.EnemyId.Contains("Ceratoferox"));
        actor.Health.SetMaxHp(1000000f, true);
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        melee = player.GetComponent<MeleeRuntime>(); melee.SetManualInputEnabled(true);
        var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
        var heavy = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(DarkBarrageCubeVfxBuilder.HeavyPath);
        var controller = player.GetComponent<CharacterController>();
        bool enabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        player.transform.position += Vector3.right * 20f;
        player.GetComponent<PlayerMovement>()?.ResetMotionAfterTeleport();
        if (controller != null) controller.enabled = enabled;
        float settle = Time.time + .6f; while (Time.time < settle) yield return null;
        Vector3 origin = player.transform.position;
        for (int i = 0; i < 3; i++)
        {
            Check(spawn.TrySpawn(new EnemySpawnRequest(definition, origin + new Vector3((i - 1) * 1.2f, 0f, 2.5f),
                Quaternion.LookRotation(Vector3.back), player.transform), out var enemy), "spawn target " + i);
            leased.Add(enemy); enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(1000000f, true);
        }
        yield return null;
        int sequence = 930000;
        foreach (var element in new [] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light, WeaponElement.Dark })
        {
            while (melee.IsBusy) yield return null;
            var gemDefinition = Resources.LoadAll<ElementGemItemData>("Items/ElementGems").First(g => g.element == element && g.fixedGrade == ItemGrade.Legendary);
            var gem = new ItemData(gemDefinition, 20, gemDefinition.fixedGrade);
            gem.gemState = ElementGemQuality.Roll(gemDefinition, 11, ElementGemArchetype.Heavy);
            var inventory = PlayerAccountInventoryService.SharedInventory;
            Check(inventory.AddItem(gem) && ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(gem)), "equip " + element);
            var snapshot = new ElementGemAttackSnapshot(equipment);
            Check(equipment.CurrentWeaponItem.ResolvedElement == WeaponElement.None && snapshot.Element == element && energy.Element == element,
                "neutral weapon resolves equipped gem " + element);
            foreach (var enemy in leased)
            {
                enemy.Health.ResetHealth(); var status = enemy.GetComponent<ElementalStatusController>(); status.ClearAllStatuses();
                // Three dark shots keep per-hit audio assertions below the existing 48-voice cap.
                // Dense volleys intentionally share that cap with layered received-hit sounds.
                if (element != WeaponElement.Light)
                    for (int n = 0; n < (element == WeaponElement.Dark ? 1 : 5); n++) status.TryApplyDirectHit(new ElementalStatusApplication(element, 10f,
                        player.gameObject, snapshot.WeaponId, true, false, enemy.transform.position, Vector3.forward, snapshot));
            }
            energy.Clear();
            for (int n = 0; n < (element == WeaponElement.Light ? 22 : 10); n++)
                energy.RecordConfirmedHit(snapshot.WeaponId, element, ++sequence, 10f);
            int derived = 0, wrongKinds = 0, direct = 0;
            foreach (var enemy in leased)
            {
                void Resolved(CombatHealth health, DamageInfo info, float actual, bool lethal)
                {
                    if (info.source != player.gameObject || !(actual > 0f) || info.isDamageOverTime) return;
                    if ((info.playerAttackKind & PlayerAttackKind.Heavy) != 0) direct++;
                    if (!info.triggersOnHitEffects)
                    {
                        if (info.playerAttackKind == PlayerAttackKind.Elemental)
                        {
                            derived++;
                            if (DamageNumberStyles.Classify(info, 1).Kind != DamageNumberKind.Discharge) wrongKinds++;
                        }
                        else if ((info.playerAttackKind & PlayerAttackKind.Elemental) != 0) wrongKinds++;
                    }
                }
                enemy.Health.OnDamageResolved += Resolved; hooks.Add((enemy.Health, Resolved));
            }
            int launched = DarkBarrageScheduler.TotalLaunched, hits = DarkBarrageScheduler.TotalHits, hitSfx = DarkBarrageScheduler.TotalCommonHitSfx;
            long hitVfx = TransientVfxPool.GetStatistics(heavy.elementVfx.darkBarrageHit).Requests;
            int flinches = leased.Sum(e => e.GetComponent<EnemyHitResponseCoordinator>().FlinchCount);
            var soundClips = new HashSet<string>();
            var accepted = WeaponActionResult.RejectedNotReady;
            float startDeadline = Time.time + 3f;
            while (Time.time < startDeadline)
            {
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
                accepted = melee.TryStartHeavyAttack(Vector3.forward);
                if (accepted == WeaponActionResult.Accepted) break;
                yield return null;
            }
            Check(accepted == WeaponActionResult.Accepted, "actual heavy accepted " + element);
            float end = Time.time + 10f, minimum = Time.time + 2.5f;
            while (Time.time < end)
            {
                foreach (var audio in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                    if (audio.isPlaying && audio.clip != null && audio.volume > 0f && !audio.mute) soundClips.Add(audio.clip.name);
                if (Time.time >= minimum && !melee.IsBusy && DarkBarrageScheduler.ActiveCount == 0
                    && ElementChainScheduler.ActiveCastCount == 0 && LightTripleImpactScheduler.PendingCount == 0 && ShatterWaveScheduler.PendingCount == 0) break;
                yield return null;
            }
            Check(derived > 0 && wrongKinds == 0, "actual follow-ups retain elemental metadata and labels " + element);
            Check(energy.Amount == 0f, "actual follow-ups do not recharge energy " + element);
            Check(soundClips.Count > 0, "actual non-muted AudioSources play " + element);
            if (element == WeaponElement.Dark)
            {
                int shots = DarkBarrageScheduler.TotalLaunched - launched;
                Check(shots > 0 && DarkBarrageScheduler.TotalHits - hits == shots, "all dark projectiles confirm their real hits");
                Check(DarkBarrageScheduler.TotalCommonHitSfx - hitSfx == shots, "each dark projectile requests common received-hit sound");
                Check(TransientVfxPool.GetStatistics(heavy.elementVfx.darkBarrageHit).Requests - hitVfx == shots, "each dark projectile requests hit VFX");
                Check(leased.Sum(e => e.GetComponent<EnemyHitResponseCoordinator>().FlinchCount) > flinches, "dark projectiles retain derived flinch");
                Check(soundClips.Any(n => n.Contains("Stab Impact")), "actual common Stab audio voices play during dark barrage");
            }
            rounds.Add(new { element = element.ToString(), direct, derived, wrongKinds, soundClips = soundClips.ToArray(),
                shots = DarkBarrageScheduler.TotalLaunched - launched, hits = DarkBarrageScheduler.TotalHits - hits, commonSfx = DarkBarrageScheduler.TotalCommonHitSfx - hitSfx });
            foreach (var hook in hooks) if (hook.health != null) hook.health.OnDamageResolved -= hook.callback;
            hooks.Clear();
        }
    }

    void Cleanup()
    {
        foreach (var hook in hooks) if (hook.health != null) hook.health.OnDamageResolved -= hook.callback;
        hooks.Clear();
        if (melee != null) melee.SetManualInputEnabled(false);
        if (spawn != null) foreach (var enemy in leased) if (enemy != null && enemy.IsLeased) spawn.Release(enemy);
        leased.Clear();
        if (enteredArena && arena != null && arena.InArena) arena.ToggleArena();
        enteredArena = false;
    }
}
