#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StatusEffectCoreVerifier
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static BuffDefinition Effect(string id, float duration = 4f, float interval = .5f) => new BuffDefinition
    { buffId = id, displayName = id, duration = duration, tickInterval = interval, initialHealPercent = 0, healPercentPerTick = 0 };

    public static object Validate(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool pass, string name) { if (!pass) throw new InvalidOperationException(name); checks.Add(name); }
        var runtime = new StatusEffectRuntime();
        int changed = 0;
        runtime.Changed += () => changed++;
        var definition = Effect("refresh"); definition.moveSpeedMultiplier = 1.1f;
        Check(runtime.TryApply(definition, out var first) == BuffApplyResult.Applied, "new effect accepted");
        definition.moveSpeedMultiplier = 9f;
        Check(Mathf.Approximately(runtime.MoveSpeedMultiplier(), 1.1f), "authoring data mutation cannot change active effect");
        runtime.Advance(1f);
        var replacement = Effect("refresh", 8f); replacement.moveSpeedMultiplier = 1.4f;
        Check(runtime.TryApply(replacement, out var refreshed) == BuffApplyResult.Refreshed && first == refreshed, "refresh retains effect handle");
        Check(Mathf.Approximately(refreshed.RemainingTime, 8f) && Mathf.Approximately(runtime.MoveSpeedMultiplier(), 1.4f), "refresh replaces old duration and values");
        Check(replacement.duration == 8f && replacement.moveSpeedMultiplier == 1.4f, "runtime does not edit definition");
        var values = new List<BuffSnapshot>(); runtime.GetSnapshots(values); var saved = values[0]; runtime.Advance(1f);
        Check(saved.RemainingTime == 8f && refreshed.RemainingTime == 7f, "snapshot cannot advance with runtime");
        Check(runtime.Remove(first) && !runtime.Remove(first), "handle removal is idempotent");
        Check(Mathf.Approximately(runtime.MoveSpeedMultiplier(), 1f), "removal restores calculated baseline");
        int before = changed; runtime.Clear(); runtime.Clear();
        Check(changed == before, "empty lifecycle clear does not notify");
        Check(runtime.TryApply(null, out _) == BuffApplyResult.Rejected && runtime.TryApply(new BuffDefinition { buffId = " " }, out _) == BuffApplyResult.Rejected, "missing identity rejected");
        var invalid = Effect("finite"); invalid.duration = float.NaN; invalid.moveSpeedMultiplier = float.PositiveInfinity; invalid.damagePerTick = float.NaN;
        runtime.TryApply(invalid, out var finite); runtime.Advance(float.NaN); runtime.Advance(float.PositiveInfinity);
        Check(!float.IsNaN(finite.RemainingTime) && runtime.MoveSpeedMultiplier() == 1f, "nonfinite input cannot corrupt clock or movement");
        runtime.Clear();

        var slow = Effect("slow", 5f); slow.moveSpeedMultiplier = .6f; slow.strength = .4f; slow.stackingPolicy = BuffStackingPolicy.ReplaceStronger;
        runtime.TryApply(slow, out var slowing); runtime.Advance(1f);
        var weaker = slow.Clone(); weaker.moveSpeedMultiplier = .9f; weaker.strength = .1f; weaker.duration = 20f;
        Check(runtime.TryApply(weaker, out _) == BuffApplyResult.Rejected && slowing.RemainingTime == 4f && runtime.MoveSpeedMultiplier() == .6f, "weaker slow neither overwrites nor prolongs strong slow");
        var stronger = slow.Clone(); stronger.moveSpeedMultiplier = .5f; stronger.strength = .5f;
        runtime.TryApply(stronger, out _);
        Check(runtime.MoveSpeedMultiplier() == .5f && runtime.MoveSpeedMultiplier(.5f) == .75f, "stronger replacement and slow resistance use active values");
        runtime.Remove("slow");

        var stacking = Effect("stacking"); stacking.maxStacks = 3; stacking.stackingPolicy = BuffStackingPolicy.StackAndRefresh;
        stacking.moveSpeedMultiplier = .9f; stacking.damagePerTick = 2f; stacking.healPercentPerTick = .02f;
        for (int i = 0; i < 5; i++) runtime.TryApply(stacking, out _);
        var stacked = runtime.Find("stacking"); var snapshot = stacked.Snapshot;
        Check(stacked.StackCount == 3 && snapshot.DamagePerTick == 6f && Mathf.Approximately(snapshot.HealPercentPerTick, .06f), "stack cap and tick values agree");
        Check(Mathf.Approximately(snapshot.MoveSpeedMultiplier, .7f), "stacked movement is calculated once");
        Check(BuffTooltipText.BuffEffect(stacked).Contains("6 지속 피해") && BuffTooltipText.BuffEffect(stacked).Contains("3중첩"), "tooltip reads actual stacked values");
        runtime.Clear();

        var timed = Effect("tick", 3f, .5f); timed.resetTickOnRefresh = false;
        runtime.TryApply(timed, out _); int ticks = 0;
        runtime.Advance(.25f, (_, _, count) => ticks += count); runtime.TryApply(timed, out _);
        runtime.Advance(.25f, (_, _, count) => ticks += count);
        Check(ticks == 1, "refresh preserving tick phase cannot starve periodic effect");
        runtime.Clear(); timed.resetTickOnRefresh = true; runtime.TryApply(timed, out _); ticks = 0;
        runtime.Advance(.25f); runtime.TryApply(timed, out _); runtime.Advance(.25f, (_, _, count) => ticks += count);
        Check(ticks == 0, "legacy recovery refresh resets first tick");
        runtime.Advance(.25f, (_, _, count) => ticks += count);
        Check(ticks == 1, "legacy delayed tick resumes after refresh");
        runtime.Clear(); runtime.TryApply(timed, out _); ticks = 0;
        runtime.Advance(2.25f, (_, _, count) => ticks += count);
        Check(ticks == 4, "long frame catches up every due tick");
        runtime.Advance(.75f, (_, _, count) => ticks += count);
        Check(ticks == 5 && runtime.Find("tick") == null, "expiry excludes tick on deadline and retains earlier due ticks");

        runtime.TryApply(Effect("first"), out _); runtime.TryApply(Effect("second"), out _); ticks = 0;
        runtime.Advance(.5f, (_, _, _) => { ticks++; runtime.Clear(); });
        Check(ticks == 1 && runtime.Find("second") == null, "clear inside tick stops old target lifetime");
        runtime.TryApply(Effect("first"), out _); runtime.TryApply(Effect("second"), out _); ticks = 0;
        runtime.Advance(.5f, (_, _, _) => { ticks++; runtime.Remove("second"); });
        Check(ticks == 1, "removal inside callback cannot execute removed effect");
        runtime.Clear(); runtime.TryApply(Effect("first"), out _);
        runtime.Advance(.5f, (_, _, _) => runtime.TryApply(Effect("new"), out _));
        Check(runtime.Find("new").RemainingTime == 4f, "effect added by tick starts next advance");
        int nestedTicks = 0; runtime.Advance(.5f, (_, _, _) => { nestedTicks++; runtime.Advance(1f); });
        Check(nestedTicks == 2 && runtime.Find("new").RemainingTime == 3.5f, "recursive advance does not double tick");

        runtime.Clear(); runtime.TryApply(Effect("first"), out _); runtime.TryApply(Effect("later"), out _);
        int laterTicks = 0;
        runtime.Advance(.5f, (state, source, count) =>
        {
            if (state.BuffId == "first") runtime.TryApply(Effect("later", 8f), out _);
            else laterTicks += count;
        });
        Check(runtime.Find("later").RemainingTime == 8f && laterTicks == 0, "callback refresh starts at current frame time and invalidates old tick");
        runtime.Clear(); runtime.TryApply(Effect("first"), out _); runtime.TryApply(Effect("later"), out _);
        runtime.Advance(.5f, (state, source, count) =>
        {
            if (state.BuffId == "later") runtime.TryApply(Effect("first", 8f), out _);
        });
        Check(runtime.Find("first").RemainingTime == 8f, "callback refresh duration is independent of iteration order");
        runtime.Clear(); var overspeed = Effect("overspeed"); overspeed.moveSpeedMultiplier = 10f;
        runtime.TryApply(overspeed, out _); var overspeed2 = overspeed.Clone(); overspeed2.buffId = "overspeed2"; runtime.TryApply(overspeed2, out _);
        Check(runtime.MoveSpeedMultiplier() == 100f, "actor final movement clamp is deferred until all modifiers are combined");
        runtime.Clear(); runtime.TryApply(Effect("cross-expiry", 1f, .25f), out _); int beforeExpiryTicks = 0;
        runtime.Advance(2f, (_, _, count) => beforeExpiryTicks += count);
        Check(beforeExpiryTicks == 3 && runtime.Find("cross-expiry") == null, "frame crossing expiry retains all earlier ticks");

        runtime.Clear(); var highHealing = Effect("high-healing"); highHealing.healPercentPerTick = .6f;
        highHealing.stackingPolicy = BuffStackingPolicy.StackAndRefresh; highHealing.maxStacks = 3;
        for (int i = 0; i < 3; i++) runtime.TryApply(highHealing, out _);
        Check(Mathf.Approximately(runtime.Find("high-healing").Snapshot.HealPercentPerTick, 1.8f), "stacked healing retains declared rate before target healing modifiers");
        var oldSnapshot = runtime.Find("high-healing").Snapshot;
        runtime.TryApply(highHealing, out var newRevision);
        Check(!runtime.IsCurrent(oldSnapshot) && runtime.IsCurrent(newRevision.Snapshot), "reapplication invalidates a previously captured snapshot");
        runtime.Clear(); runtime.TryApply(Effect("pending-expiry", .75f, .25f), out _);
        bool validPending = false, removedPending = false, invalidAfterRemove = false;
        runtime.Advance(1f, (state, source, count) =>
        {
            validPending = runtime.IsCurrent(state); removedPending = runtime.Remove(state.BuffId);
            invalidAfterRemove = !runtime.IsCurrent(state);
        });
        Check(validPending && removedPending && invalidAfterRemove, "pending pre-expiry ticks can be canceled by ID during execution");
        runtime.TryApply(Effect("renew-expired", .5f, .25f), out var expiredHandle);
        long expiredId = expiredHandle.Snapshot.InstanceId; bool expiredInvalid = false;
        runtime.Advance(.75f, (state, source, count) =>
        {
            runtime.TryApply(Effect("renew-expired", 8f), out _); expiredInvalid = !runtime.IsCurrent(state);
        });
        Check(expiredInvalid && runtime.Find("renew-expired").Snapshot.InstanceId != expiredId, "new application after expiry cannot continue the old instance's ticks");
        Check(runtime.GetSnapshots(values) == 1 && values[0].RemainingTime == 8f, "renewal during expiry leaves one fresh active effect");
        runtime.Clear(); runtime.TryApply(Effect("earlier"), out _); runtime.Advance(.25f);
        runtime.TryApply(Effect("later"), out _); var order = new List<string>();
        runtime.Advance(.75f, (state, source, count) => order.Add(state.BuffId));
        Check(string.Join(",", order) == "earlier,later,earlier", "effects with different start times deliver ticks in scheduled order");
        runtime.Clear(); runtime.TryApply(Effect("later-tick", 4f, 1f), out _); runtime.TryApply(Effect("expiry-first", .75f, 2f), out _);
        int afterExpiryTicks = 0;
        Action clearOnExpiry = () => { if (runtime.Find("expiry-first") == null && runtime.Find("later-tick") != null) runtime.Clear(); };
        runtime.Changed += clearOnExpiry;
        runtime.Advance(1.25f, (state, source, count) => afterExpiryTicks += count);
        runtime.Changed -= clearOnExpiry;
        Check(afterExpiryTicks == 0, "earlier expiry callback cancels later due ticks regardless of list order");

        var scene = EditorSceneManager.NewPreviewScene(); GameObject actor = null; ConsumableItemData potionData = null;
        try
        {
            actor = new GameObject("StatusEffectNativeFixture"); SceneManager.MoveGameObjectToScene(actor, scene);
            actor.transform.position = new Vector3(99999, 99999, 99999);
            var health = actor.AddComponent<CombatHealth>();
            typeof(CombatHealth).GetField("showDamageNumbers", All).SetValue(health, false);
            health.SetMaxHp(100, true); health.TakeDamage(new DamageInfo(50, default, triggersOnHitEffects: false));
            var damage = Effect("damage"); damage.damagePerTick = 5f; damage.isDebuff = true;
            var damageInstance = new BuffInstance(damage); DamageInfo delivered = default;
            health.OnDamageResolved += (_, info, _, _) => delivered = info;
            BuffEffectExecutor.ApplyTick(health, damageInstance.Snapshot, null, 2);
            Check(Mathf.Approximately(health.CurrentHp, 40), "executor applies catch-up damage through actual health");
            Check(delivered.isDamageOverTime && !delivered.triggersOnHitEffects && !delivered.usesResolvedTickDamage, "generic tick uses normal mitigation and cannot trigger on-hit recursion");
            var healing = Effect("healing"); healing.healPercentPerTick = .1f;
            BuffEffectExecutor.ApplyTick(health, new BuffInstance(healing).Snapshot, null, 2);
            Check(Mathf.Approximately(health.CurrentHp, 60), "common healing executor uses target maximum HP");
            damage.damagePerTick = 200; damage.healPercentPerTick = 1;
            BuffEffectExecutor.ApplyTick(health, new BuffInstance(damage).Snapshot, null, 1);
            Check(health.IsDead && health.CurrentHp == 0, "fatal compound tick cannot heal dead target");
            health.ResetHealth();
            health.TakeDamage(new DamageInfo(90, default, triggersOnHitEffects: false));
            var catchingUp = Effect("compound-catchup"); catchingUp.damagePerTick = 8; catchingUp.healPercentPerTick = .1f;
            BuffEffectExecutor.ApplyTick(health, new BuffInstance(catchingUp).Snapshot, null, 2);
            Check(!health.IsDead && health.CurrentHp == 14f, "catch-up ticks preserve per-tick damage and healing order");
            health.ResetHealth(); health.TakeDamage(new DamageInfo(90, default, triggersOnHitEffects: false));
            health.SetRunMapModifiers(1f, .5f); healing.healPercentPerTick = .3f;
            BuffEffectExecutor.ApplyTick(health, new BuffInstance(healing).Snapshot, null, 4);
            Check(Mathf.Approximately(health.CurrentHp, 70f), "catch-up healing preserves rate under run healing reduction");
            health.SetRunMapModifiers(1f, 1f); health.ResetHealth();
            health.TakeDamage(new DamageInfo(90, default, triggersOnHitEffects: false));
            var interleaved = new StatusEffectRuntime(); var periodicDamage = Effect("ordered-dot"); periodicDamage.damagePerTick = 8;
            var periodicHeal = Effect("ordered-heal"); periodicHeal.healPercentPerTick = .1f;
            interleaved.TryApply(periodicDamage, out _); interleaved.TryApply(periodicHeal, out _);
            interleaved.Advance(1f, (state, source, count) => BuffEffectExecutor.ApplyTick(health, state, source, count, null, () => interleaved.IsCurrent(state)));
            Check(!health.IsDead && health.CurrentHp == 14f, "separate damage and healing statuses preserve HP when one frame catches up multiple ticks");
            health.ResetHealth();
            var player = actor.AddComponent<PlayerBuffController>(); typeof(PlayerBuffController).GetMethod("OnEnable", All).Invoke(player, null);
            var active = new List<BuffInstance>(); int notifications = 0; player.BuffsChanged += () => notifications++;
            player.ApplyBuff(replacement); player.ApplySlowDebuff(4, .7f);
            Check(player.GetActiveBuffs(active) == 2, "existing player adapter API uses common runtime");
            health.Heal(1); Check(player.GetActiveBuffs(active) == 2, "ordinary heal preserves statuses");
            int countBeforeReset = notifications; health.ResetHealth();
            Check(player.GetActiveBuffs(active) == 0 && notifications == countBeforeReset + 1, "reset clears all effects with one notification");
            player.ApplyBuff(replacement); typeof(PlayerBuffController).GetMethod("OnDisable", All).Invoke(player, null); typeof(PlayerBuffController).GetMethod("OnEnable", All).Invoke(player, null);
            Check(player.GetActiveBuffs(active) == 0, "disable and re-enable cannot carry prior effects");
            player.ApplyBuff(replacement); health.TakeDamage(new DamageInfo(200, default, triggersOnHitEffects: false));
            Check(player.GetActiveBuffs(active) == 0 && player.TryApplyBuff(replacement) == BuffApplyResult.Rejected, "death clears and rejects subsequent applications");
            var inventory = actor.AddComponent<PlayerInventory>();
            potionData = ScriptableObject.CreateInstance<ConsumableItemData>(); potionData.consumableType = ConsumableType.SpeedBoost;
            var item = new ItemData(potionData, 1, ItemGrade.Common);
            typeof(PlayerInventory).GetField("items", All).SetValue(inventory, new List<ItemData> { item });
            var context = new ItemUseContext(inventory, health, player, null, null, item, 0, ItemUseSource.QuickSlot);
            var handler = new MoveSpeedPotionUseHandler();
            Check(!handler.CanUse(context).Success && !handler.Use(context).Success && item.stackCount == 1, "dead actor rejects speed potion before consumption");
            health.ResetHealth();
            Check(handler.CanUse(context).Success, "live actor can still use speed potion");
            player.enabled = false;
            Check(!handler.CanUse(context).Success && !handler.Use(context).Success && item.stackCount == 1, "disabled status adapter rejects speed potion before consumption");
            player.enabled = true;
            typeof(PlayerBuffController).GetMethod("OnEnable", All).Invoke(player, null);
            Check(Overburst.Persistence.AccountGameplaySession.Current == null, "native fixture cannot route through a real account session");
            var actions = actor.AddComponent<InventoryItemActionService>();
            typeof(InventoryItemActionService).GetField("inventory", All).SetValue(actions, inventory);
            typeof(InventoryItemActionService).GetField("playerHealth", All).SetValue(actions, health);
            typeof(InventoryItemActionService).GetField("playerBuffController", All).SetValue(actions, player);
            var useItem = typeof(InventoryItemActionService).GetMethod("UseConsumableItem", All);
            bool UsePotion() => (bool)useItem.Invoke(actions, new object[] { item, 0, ItemUseSource.QuickSlot });
            item.stackCount = 3; potionData.targetBuffId = "native-speed-potion"; potionData.duration = 8; potionData.moveSpeedMultiplier = 1.3f;
            health.TakeDamage(new DamageInfo(200, default, triggersOnHitEffects: false));
            Check(!UsePotion() && item.stackCount == 3 && inventory.ContainsItem(item), "actual item service rejects dead actor without consuming a potion");
            health.ResetHealth(); player.enabled = false;
            Check(!UsePotion() && item.stackCount == 3, "actual item service rejects inactive adapter without consuming a potion");
            player.enabled = true; typeof(PlayerBuffController).GetMethod("OnEnable", All).Invoke(player, null);
            Check(UsePotion() && item.stackCount == 2 && player.HasBuff(potionData.targetBuffId), "actual item service consumes exactly one potion and applies its status");
            var playerRuntime = (StatusEffectRuntime)typeof(PlayerBuffController).GetField("effects", All).GetValue(player);
            playerRuntime.Advance(1);
            Check(UsePotion() && item.stackCount == 1 && player.GetActiveBuffs(active) == 1 && active[0].RemainingTime == 8f
                && Mathf.Approximately(player.ActiveMoveSpeedMultiplier, 1.3f), "potion reuse consumes one and refreshes one existing effect with current values");
            health.ResetHealth(); health.TakeDamage(new DamageInfo(50, default, triggersOnHitEffects: false));
            var compound = Effect("reset-during-tick"); compound.damagePerTick = 5; compound.healPercentPerTick = .1f;
            player.ApplyBuff(compound); player.GetActiveBuffs(active);
            Action<CombatHealth, DamageInfo, float, bool> resetOnDamage = null;
            resetOnDamage = (target, info, amount, killed) =>
            {
                if (!info.isDamageOverTime) return;
                target.OnDamageResolved -= resetOnDamage;
                target.ResetHealth(); target.TakeDamage(new DamageInfo(20, default, triggersOnHitEffects: false));
            };
            health.OnDamageResolved += resetOnDamage;
            typeof(PlayerBuffController).GetMethod("ExecuteTick", All).Invoke(player, new object[] { active[0].Snapshot, null, 1 });
            Check(health.CurrentHp == 80 && player.GetActiveBuffs(active) == 0, "reset inside damage callback blocks old tick healing on new target lifetime");
            health.ResetHealth(); health.TakeDamage(new DamageInfo(50, default, triggersOnHitEffects: false));
            player.ApplyBuff(compound); player.GetActiveBuffs(active); int cancelDamageEvents = 0;
            Action<CombatHealth, DamageInfo, float, bool> cancelOnDamage = (target, info, amount, killed) =>
            {
                if (!info.isDamageOverTime) return;
                cancelDamageEvents++; player.RemoveBuff(compound.buffId);
            };
            health.OnDamageResolved += cancelOnDamage;
            typeof(PlayerBuffController).GetMethod("ExecuteTick", All).Invoke(player, new object[] { active[0].Snapshot, null, 3 });
            health.OnDamageResolved -= cancelOnDamage;
            Check(health.CurrentHp == 45 && cancelDamageEvents == 1 && player.GetActiveBuffs(active) == 0, "removal inside damage callback stops remaining catch-up ticks and healing");
            health.ResetHealth(); player.ApplyBuff(replacement);
            var replacementObject = new GameObject("ReplacementHealthFixture"); replacementObject.transform.SetParent(actor.transform, false);
            var replacementHealth = replacementObject.AddComponent<CombatHealth>(); replacementHealth.SetMaxHp(100, true);
            typeof(PlayerBuffController).GetField("health", All).SetValue(player, replacementHealth);
            typeof(PlayerBuffController).GetMethod("BindHealth", All).Invoke(player, null);
            Check(player.GetActiveBuffs(active) == 0, "binding replacement health starts a fresh target lifetime");
            player.ApplyBuff(replacement); health.ResetHealth();
            Check(player.GetActiveBuffs(active) == 1, "old health callbacks cannot clear replacement target effects");
            replacementHealth.ResetHealth();
            Check(player.GetActiveBuffs(active) == 0, "replacement target reset owns current effect cleanup");
            player.ApplyBuff(replacement);
            UnityEngine.Object.DestroyImmediate(replacementHealth); UnityEngine.Object.DestroyImmediate(health);
            typeof(PlayerBuffController).GetMethod("Update", All).Invoke(player, null);
            Check(player.GetActiveBuffs(active) == 0 && player.ActiveMoveSpeedMultiplier == 1f, "destroyed health resolves real null and clears movement instead of retaining Unity fake-null effects");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
            Check(prefab != null && prefab.GetComponentInChildren<PlayerBuffController>(true) != null, "existing player prefab still resolves original component");
        }
        finally { if (potionData != null) UnityEngine.Object.DestroyImmediate(potionData); if (actor != null) UnityEngine.Object.DestroyImmediate(actor); EditorSceneManager.ClosePreviewScene(scene); }
        var report = new { status = "PASS_SCOPED", checks, count = checks.Count, scope = "common core, actual HP, existing player adapter and consumable service; no monster integration" };
        File.WriteAllText(Path.Combine(output, "native.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
        return report;
    }

    public static IEnumerator RunPlay(string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException(label); checks.Add(label); File.WriteAllText(Path.Combine(output, "status-effects-progress.json"), JsonConvert.SerializeObject(checks, Formatting.Indented)); }
        var actor = new GameObject("StatusEffectPlayFixture"); actor.SetActive(false); actor.transform.position = new Vector3(99999, 99999, 99999);
        var health = actor.AddComponent<CombatHealth>(); typeof(CombatHealth).GetField("showDamageNumbers", All).SetValue(health, false);
        var player = actor.AddComponent<PlayerBuffController>(); var source = new GameObject("StatusEffectSourceFixture");
        GameObject pickupObject = null;
        PlayerBuffController hudPlayer = null;
        BuffTooltipUI hudTooltip = null;
        BuffIconSlotUI hudSlot = null;
        var active = new List<BuffInstance>();
        try
        {
            health.SetMaxHp(100, true); actor.SetActive(true); yield return null;
            var haste = Effect("play-haste", .25f); haste.moveSpeedMultiplier = 1.2f;
            player.ApplyBuff(haste);
            Check(player.HasBuff(haste.buffId) && Mathf.Approximately(player.ActiveMoveSpeedMultiplier, 1.2f), "automatic enable and common movement calculation");
            float deadline = Time.realtimeSinceStartup + 5;
            while (player.HasBuff(haste.buffId) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!player.HasBuff(haste.buffId) && player.ActiveMoveSpeedMultiplier == 1f, "automatic Update expires movement effect");
            var dot = Effect("play-dot", 1f, .1f); dot.damagePerTick = 5; dot.isDebuff = true;
            int delivered = 0; health.OnDamageResolved += (_, info, _, _) => { if (info.isDamageOverTime && !info.triggersOnHitEffects && info.source == source) delivered++; };
            player.TryApplyBuff(dot, source); deadline = Time.realtimeSinceStartup + 5;
            while (health.CurrentHp > 85 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(health.CurrentHp <= 85 && delivered > 0, "automatic periodic damage preserves source and no-on-hit flag");
            player.RemoveBuff(dot.buffId); health.ResetHealth();
            health.TakeDamage(new DamageInfo(50, default, triggersOnHitEffects: false));
            var regen = Effect("play-regen", 1f, .1f); regen.healPercentPerTick = .1f;
            player.ApplyBuff(regen); deadline = Time.realtimeSinceStartup + 5;
            while (health.CurrentHp < 60 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(health.CurrentHp >= 60, "automatic periodic recovery uses existing player feedback path");
            health.ResetHealth(); Check(player.GetActiveBuffs(active) == 0, "actual health Reset clears common runtime");
            player.ApplySlowDebuff(3, .6f); player.ApplySlowDebuff(8, .9f);
            Check(Mathf.Approximately(player.ActiveMoveSpeedMultiplier, .6f), "existing floor API rejects weaker replacement");
            actor.SetActive(false); actor.SetActive(true); yield return null;
            Check(player.GetActiveBuffs(active) == 0 && player.ActiveMoveSpeedMultiplier == 1f, "automatic disable/re-enable clears movement and timers");
            var fatal = Effect("play-fatal", 1f, .1f); fatal.damagePerTick = 200; fatal.healPercentPerTick = 1;
            player.ApplyBuff(fatal); deadline = Time.realtimeSinceStartup + 5;
            while (!health.IsDead && Time.realtimeSinceStartup < deadline) yield return null;
            Check(health.IsDead && health.CurrentHp == 0 && player.GetActiveBuffs(active) == 0, "fatal tick clears lifetime and cannot recover or continue");
            health.ResetHealth(); actor.tag = "Player"; typeof(CombatHealth).GetField("currentHp", All).SetValue(health, 50f);
            var collider = actor.AddComponent<BoxCollider>();
            pickupObject = new GameObject("StatusEffectHealthPickupFixture"); var pickup = pickupObject.AddComponent<HealthPickup>();
            var recovery = new BuffDefinition { duration = .8f, tickInterval = .2f };
            typeof(HealthPickup).GetField("healingBuff", All).SetValue(pickup, recovery);
            typeof(HealthPickup).GetMethod("OnTriggerEnter", All).Invoke(pickup, new object[] { collider });
            Check(Mathf.Approximately(health.CurrentHp, 80) && player.HasBuff(recovery.buffId), "real health pickup applies initial recovery exactly once and registers timed recovery");
            deadline = Time.realtimeSinceStartup + 5;
            while (health.CurrentHp < 85 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(health.CurrentHp >= 85, "health pickup recovery continues through common tick runtime");
            hudPlayer = PlayerContext.Instance.CurrentActorBuffController;
            var hudBar = UnityEngine.Object.FindFirstObjectByType<BuffBarUI>();
            Check(hudPlayer != null && hudBar != null, "isolated production actor and saved HUD are available");
            var hudEffect = Effect("status-verifier-hud", 20f); hudEffect.moveSpeedMultiplier = 1.1f;
            hudEffect.stackingPolicy = BuffStackingPolicy.StackAndRefresh; hudEffect.maxStacks = 3;
            hudPlayer.ApplyBuff(hudEffect); hudPlayer.ApplyBuff(hudEffect); yield return null;
            foreach (var slot in hudBar.GetComponentsInChildren<BuffIconSlotUI>(true))
                if (slot.DisplayedKey == hudEffect.buffId) { hudSlot = slot; break; }
            Check(hudSlot != null && hudSlot.TooltipEffect.Contains("2중첩") && hudSlot.TooltipEffect.Contains("20%"), "production HUD displays actual common-effect stacks and movement");
            hudTooltip = (BuffTooltipUI)typeof(BuffIconSlotUI).GetField("tooltip", All).GetValue(hudSlot);
            hudSlot.OnPointerEnter(null);
            Check(hudTooltip != null && hudTooltip.IsShown && hudTooltip.Owner == hudSlot, "production slot pointer handler opens authored tooltip");
            hudPlayer.ApplyBuff(hudEffect); yield return null;
            Check(hudTooltip.IsShown && hudSlot.TooltipEffect.Contains("3중첩") && hudSlot.TooltipEffect.Contains("30%"), "hovered production slot refreshes tooltip after stack changes");
            hudPlayer.RemoveBuff(hudEffect.buffId); yield return null;
            Check(!hudTooltip.IsShown && hudBar.VisibleTimedCount == 0, "removing the common effect hides its production icon and hover tooltip");
            File.WriteAllText(Path.Combine(output, "status-effects-play.json"), JsonConvert.SerializeObject(new { status = "PASS_SCOPED", checks, count = checks.Count }, Formatting.Indented));
        }
        finally
        {
            if (hudPlayer != null) hudPlayer.RemoveBuff("status-verifier-hud");
            if (hudTooltip != null && hudSlot != null) hudTooltip.Hide(hudSlot);
            if (pickupObject != null) UnityEngine.Object.Destroy(pickupObject);
            UnityEngine.Object.Destroy(actor); UnityEngine.Object.Destroy(source);
        }
    }
}
#endif
