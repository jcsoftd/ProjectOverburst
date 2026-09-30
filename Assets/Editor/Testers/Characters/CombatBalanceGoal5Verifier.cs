using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class CombatBalanceGoal3Verifier
{
    static IEnumerator VerifyGoal5()
    {
        EnemyActor enemy = null; EnemySpawnService spawn = null; EnemyThemeDebugUI ui = null;
        EnemyAbilitySet fixture = null; MeleeRuntime melee = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            Check(Overburst.Persistence.AccountBootstrap.SaveDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Account isolation");
            var player = PlayerInputFacade.Current;
            var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Equip greatsword");
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee = player.GetComponent<MeleeRuntime>(); melee.SetManualInputEnabled(true);
            ui = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);
            ui.gameObject.SetActive(true); if (!ui.InArena) ui.ToggleArena();
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service");
            foreach (var t in ui.tables) Check(spawn.RegisterAdditionalCatalog(t.Catalog, out string error), error);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct().ToArray();
            var d = defs.First(x => x.EnemyId.Contains("Scolokarck"));
            var a = Enumerable.Range(0, d.AbilitySet.Count).Select(i => d.AbilitySet.GetAbility(i)).First(x => x.IsParryable);
            var energy = player.GetComponent<OverburstElementEnergy>();
            if (energy == null) energy = player.gameObject.AddComponent<OverburstElementEnergy>();
            float readyDeadline = Time.unscaledTime + 5f;
            while (melee.TryStartHeavyAttack(Vector3.forward) != WeaponActionResult.Accepted)
            { Check(Time.unscaledTime < readyDeadline, "Warm heavy not ready"); yield return null; }
            float warmDeadline = Time.unscaledTime + 15f;
            while (melee.IsAttackInProgress) { Check(Time.unscaledTime < warmDeadline, "Warm heavy stuck"); yield return null; }
            CombatActionSfxService.PlayParrySuccess(player.transform.position);
            float settle = Time.unscaledTime + .6f; while (Time.unscaledTime < settle) yield return null;
            foreach (int encodedCharge in new[] { 0, 50, 100, -1 })
            {
                int charge = Mathf.Max(0, encodedCharge);
                if (encodedCharge < 0)
                {
                    d = defs.First(x => x.Grade.GradeType == EnemyGradeType.Elite
                        && Enumerable.Range(0, x.AbilitySet.Count).Any(i => x.AbilitySet.GetAbility(i).IsParryable));
                    a = Enumerable.Range(0, d.AbilitySet.Count).Select(i => d.AbilitySet.GetAbility(i)).First(x => x.IsParryable);
                }
                melee.CancelCurrentAttackState();
                var p = player.transform.position + Vector3.forward * (a.Range * .65f);
                Check(Physics.Raycast(p + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "Floor");
                var request = new EnemySpawnRequest(d, floor.point + Vector3.up * .035f, Quaternion.LookRotation(Vector3.back), player.transform);
                Check(spawn.TrySpawn(request, out enemy), "Spawn");
                enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                fixture = ScriptableObject.CreateInstance<EnemyAbilitySet>();
                var so = new SerializedObject(fixture); so.FindProperty("abilitySetId").stringValue = "parry-fixture";
                var arr = so.FindProperty("abilities"); arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = a; so.ApplyModifiedPropertiesWithoutUndo();
                enemy.AbilityController.Configure(fixture, 1, 1);
                energy.Clear();
                for (int i = 0; i < charge / 10; i++) Check(energy.RecordConfirmedHit(energy.WeaponInstanceId, energy.Element, 1000 + i, 1), "Charge");
                Check(Mathf.Abs(energy.Amount - charge) < .01f, "Charge total");
                float timeout = Time.time + 3;
                while (!enemy.AbilityController.TryStart(player.transform)) { Check(Time.time < timeout, "Enemy start"); yield return null; }
                float attackAt = Time.time;
                while (Time.time < attackAt + a.ResolveFirstImpactTime(1) - .17f) yield return null;
                player.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                float hp = actor.Health.CurrentHp;
                var generationField = typeof(OverburstElementEnergy).GetField("generation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                int generationBefore = (int)generationField.GetValue(energy);
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "Heavy accepted");
                var parry = player.GetComponent<PlayerParryController>();
                Check(parry != null && parry.IsWindowOpen, "Window opened");
                int before = parry.SuccessCount;
                var incoming = new DamageInfo(1, player.transform.position, enemy.gameObject, enemyAbility: a);
                player.transform.rotation = Quaternion.LookRotation(Vector3.back);
                Check(!parry.TryCancelDamage(incoming) && parry.IsWindowOpen, "Rear attack accepted or consumed window");
                player.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                incoming.isDamageOverTime = true;
                Check(!parry.TryCancelDamage(incoming) && parry.IsWindowOpen, "DOT parried");
                incoming.isDamageOverTime = false; incoming.enemyAbility = null;
                Check(!parry.TryCancelDamage(incoming) && parry.IsWindowOpen, "Ordinary attack parried");
                float remaining = parry.RemainingWindow;
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.RejectedBusy && parry.RemainingWindow <= remaining, "Repeated heavy extended window");
                float realtimeLimit = Time.unscaledTime + .65f;
                var trace = new System.Collections.Generic.List<object>();
                while (parry.SuccessCount == before && Time.unscaledTime < realtimeLimit)
                {
                    trace.Add(new { elapsed = Time.time - attackAt, realRemaining = parry.RemainingWindow, delta = Time.deltaTime, unscaledDelta = Time.unscaledDeltaTime, heavy = melee.IsHeavyAttackInProgress,
                        hp = actor.Health.CurrentHp, enemyHp = enemy.Health.CurrentHp, enemyExecuting = enemy.AbilityController.IsExecuting,
                        playerPosition = player.transform.position.ToString("F3"), enemyPosition = enemy.transform.position.ToString("F3"),
                        facing = player.transform.forward.ToString("F3"), blocked = GameplayInputBlocker.IsGameplayInputBlocked, scale = Time.timeScale });
                    yield return null;
                }
                if (parry.SuccessCount == before) results.Add(new { charge, trace });
                Check(parry.SuccessCount == before + 1, "Actual collision did not parry charge=" + charge);
                Check(actor.Health.CurrentHp == hp && !enemy.AbilityController.IsExecuting, "Damage or remaining enemy attack");
                Check(energy.Amount == charge && melee.IsHeavyAttackInProgress, "Parry consumed energy or cancelled heavy");
                var reaction = enemy.GetComponent<EnemyMovementReaction>();
                float stun = reaction.ParryStunRemaining;
                float expectedStun = encodedCharge < 0 ? .8f : 1.2f;
                Check(stun > expectedStun - .1f && stun <= expectedStun + .01f, "Independent stun duration");
                reaction.ApplyHitStun(.05f); reaction.ApplyKnockbackDistance(Vector3.forward, .02f);
                Check(reaction.ParryStunRemaining >= stun - .01f, "Ordinary reaction shortened parry stun");
                Check(!parry.TryCancelDamage(new DamageInfo(1, player.transform.position, enemy.gameObject, enemyAbility: a)), "Window success reused");
                float feedbackAt = Time.unscaledTime;
                bool slowSeen = false;
                while (Time.unscaledTime < feedbackAt + .25f) { slowSeen |= Mathf.Abs(Time.timeScale - .75f) < .01f; yield return null; }
                Check(slowSeen && Mathf.Abs(Time.timeScale - 1) < .001f, "Feedback time did not recover");
                float finishDeadline = Time.unscaledTime + 8f;
                while (melee.IsAttackInProgress) { Check(Time.unscaledTime < finishDeadline, "Original heavy stuck"); yield return null; }
                Check((int)generationField.GetValue(energy) == generationBefore + 1 && energy.Amount == 0, "Heavy discharge did not commit exactly once");
                float idleUntil = Time.unscaledTime + .3f; while (Time.unscaledTime < idleUntil) yield return null;
                Check(!melee.IsAttackInProgress && parry.SuccessCount == before + 1, "Extra heavy or extra parry");
                results.Add(new { charge, elite = encodedCharge < 0, parries = parry.SuccessCount - before, hpUnchanged = true, energyAtParry = charge,
                    initialStun = stun, slowSeen, originalHeavyCommits = 1, extraHeavy = 0, rearRejected = true, dotRejected = true, ordinaryRejected = true });
                melee.CancelCurrentAttackState(); spawn.Release(enemy); enemy = null; UnityEngine.Object.Destroy(fixture); fixture = null;
                yield return null;
            }
            // Arbiter cleanup during overlap must restore the pre-effect baseline.
            var owner = player.GetComponent<PlayerParryController>();
            OverburstTimeEffectArbiter.Request(owner, OverburstTimeEffectKind.ParrySlow, .75f, .21f);
            OverburstTimeEffectArbiter.Request(owner, OverburstTimeEffectKind.ParryHitStop, .01f, .06f);
            owner.enabled = false;
            Check(Mathf.Abs(Time.timeScale - 1) < .001f && OverburstTimeEffectArbiter.ActiveRequestCount == 0, "Disable did not clear feedback");
            owner.enabled = true;
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "Expiry heavy");
            float expire = Time.unscaledTime + .45f; while (Time.unscaledTime < expire) yield return null;
            Check(!owner.IsWindowOpen, "Expired window remained open");
            results.Add(new { disableCleanup = true });
        }
        finally { melee?.CancelCurrentAttackState(); if (enemy != null && enemy.IsLeased && spawn != null) spawn.Release(enemy); if (fixture != null) UnityEngine.Object.Destroy(fixture); if (ui != null && ui.InArena) ui.ToggleArena(); }
    }
}
