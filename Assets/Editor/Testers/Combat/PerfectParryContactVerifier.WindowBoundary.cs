using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class PerfectParryContactVerifier
{
    private const string WindowBoundaryRequest = "window-boundary.request.json";
    public static void StartWindowBoundary(string directory, bool fullRegression = true)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("Window regression requires the returned shared Editor.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, WindowBoundaryRequest), JsonConvert.SerializeObject(new { fullRegression }));
        ThreeTierParryVerifier.StartIsolated(directory, true);
    }
    private static IEnumerator WindowBoundary(string output)
    {
        var actor = PlayerContext.GetOrCreate().CurrentActor;
        var melee = actor.GetComponent<MeleeRuntime>(); var parry = actor.GetComponent<PlayerParryController>();
        var energy = actor.GetComponent<OverburstElementEnergy>(); bool ownEnergy = energy == null;
        if (ownEnergy) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
        var profile = PerfectParryContactProfile.Current;
        bool enabledBefore = profile.enhancedPresentation;
        float maxHpBefore = actor.Health.MaxHp;
        ItemData weaponBefore = actor.Equipment.CurrentWeaponItem, gemBefore = actor.Equipment.EquippedElementGem;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var equipGem = typeof(PlayerEquipment).GetMethod("SetElementGem", flags);
        EnemySpawnService spawn = null; EnemyActor enemy = null; EnemyAbilitySet set = null;
        PerfectParryWindowBoundaryProbe probe = null; var cases = new List<object>();
        bool full = (bool)JObject.Parse(File.ReadAllText(Path.Combine(output, WindowBoundaryRequest)))["fullRegression"];
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        try
        {
            melee.CancelCurrentAttackState(); actor.Health.SetMaxHp(1000000, true);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var item = new ItemData(weapon, 1, ItemGrade.Common);
            Check(actor.Equipment.EquipWeaponItem(item), "Actual greatsword equipped");
            var fire = AssetDatabase.LoadAssetAtPath<ElementGemItemData>("Assets/ProjectOverburst/Resources/Items/ElementGems/EG_Fire_Common.asset");
            equipGem.Invoke(actor.Equipment, new object[] { new ItemData(fire, 1, ItemGrade.Common) });
            float timeout = Time.unscaledTime + 30f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            { Check(Time.unscaledTime < timeout, "Hideout boot completes"); yield return null; }
            var trial = EnemyThemeTrialHarness.Current;
            if (!trial.InArena) trial.ToggleArena(); yield return WaitContact(1f);
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "Product trial spawn service");
            foreach (var table in trial.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "Trial catalog registration");
            var definition = trial.tables.SelectMany(t => t.Entries).First(e => e.definition != null && e.definition.EnemyId == "CavernMutants_Ursacetus").definition;
            var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility).First(a => a != null && a.name == "CavernMutants_Ursacetus_2HandsSmashAttack");
            Check(ability.IsParryable, "Deployed two-hand smash is parryable");
            set = ScriptableObject.CreateInstance<EnemyAbilitySet>(); var serialized = new SerializedObject(set);
            serialized.FindProperty("abilitySetId").stringValue = "perfect-parry-window-boundary";
            var entries = serialized.FindProperty("abilities"); entries.arraySize = 1; entries.GetArrayElementAtIndex(0).objectReferenceValue = ability;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
            var movement = actor.GetComponent<PlayerMovement>(); Vector3 forward = movement.ResolveMoveDirection(new Vector2(-1, 1).normalized); forward.y = 0; forward.Normalize();
            Vector3 origin = actor.transform.position;
            probe = actor.gameObject.AddComponent<PerfectParryWindowBoundaryProbe>();
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            string[] names = full ? new[] { "EarlyPerfect", "LatePerfect", "ProfileOff", "Normal", "Incomplete",
                "ExplicitCancel", "NewAction", "WeaponChanged", "TargetReleased", "OwnerDisabled", "PresenterDisabled", "Death" }
                : new[] { "EarlyPerfect", "LatePerfect" };
            foreach (string name in names)
            {
                profile.enhancedPresentation = name != "ProfileOff";
                parry.enabled = true; actor.GetComponent<PerfectParryContactPresenter>().enabled = true;
                melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
                Check(actor.Equipment.EquipWeaponItem(item), "Greatsword restored between cases");
                ActorTeleportUtility.TeleportSafely(actor.transform, origin, Quaternion.LookRotation(forward));
                movement.ResetMotionAfterTeleport(); actor.GetComponent<PlayerCombatFacingController>()?.ResetAfterTeleport();
                Vector3 spawnPoint = origin + forward * 1.76f;
                Check(Physics.Raycast(spawnPoint + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "Trial ground");
                Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-forward), actor.transform), out enemy), "Actual enemy spawned");
                enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(100000, true);
                enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; enemy.AbilityController.Configure(set, 1.75f, 1f);
                energy.BindWeapon(actor.Equipment.CurrentWeaponItem.runtimeInstanceId, actor.Equipment.ActiveElement, actor.Equipment.EquippedElementGem.runtimeInstanceId, actor.Equipment.GemRevision);
                float gauge = name == "Normal" ? 50f : name == "Incomplete" ? 20f : 100f;
                typeof(OverburstElementEnergy).GetField("<Amount>k__BackingField", flags).SetValue(energy, gauge);
                yield return WaitContact(1.2f);
                int success = parry.SuccessCount, feedback = parry.FeedbackCount;
                var presenter = actor.GetComponent<PerfectParryContactPresenter>(); int main = presenter.MainCount;
                float hp = actor.Health.CurrentHp;
                Check(enemy.AbilityController.TryStart(actor.transform), "Actual source attack commits");
                timeout = Time.unscaledTime + 8f;
                while (!enemy.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()))
                { Check(Time.unscaledTime < timeout, "Source reaches the parry threat phase"); yield return null; }
                Check(melee.TryStartHeavyAttack(forward) == WeaponActionResult.Accepted, "Heavy action accepts fixed gauge");
                Check(parry.SuccessCount == success + 1, "Real source parries once on acceptance");
                // Control only the remaining acceptance clock. Real contact, grade, source cancellation,
                // feedback, Update/LateUpdate ordering and particle playback remain production code.
                if (name != "EarlyPerfect") typeof(PlayerParryController).GetField("windowEndsAt", flags).SetValue(parry, Time.unscaledTime + .025f);
                probe.Arm(name, actor, parry, presenter, melee, enemy, spawn, feedback, main);
                yield return WaitContact(1.1f);
                bool expectedMain = name == "EarlyPerfect" || name == "LatePerfect";
                bool pass = presenter.MainCount - main == (expectedMain ? 1 : 0)
                    && probe.VisibleMain == expectedMain && parry.FeedbackCount - feedback == 1
                    && (!expectedMain || probe.MaxFragments >= 7 && probe.HaloParticles == 0);
                if (name == "EarlyPerfect" || name == "LatePerfect" || name == "ProfileOff")
                    pass &= parry.ActionGrade == ParryGrade.Perfect && Mathf.Abs(actor.Health.CurrentHp - hp) < .01f;
                if (name == "Normal") pass &= parry.ActionGrade == ParryGrade.Normal;
                if (name == "Incomplete") pass &= parry.ActionGrade == ParryGrade.Incomplete;
                pass &= ParryFeedbackService.LastAdditionalTingCount == (name == "Incomplete" ? 0 : name == "Normal" ? 1 : 2);
                if (name != "EarlyPerfect" && name != "LatePerfect" && name != "ProfileOff" && name != "Normal" && name != "Incomplete") pass &= probe.Intervened;
                cases.Add(new { name, pass, fixture = name == "EarlyPerfect" ? "original 0.60s window" : "25ms acceptance time remains after confirmed real contact", grade = parry.ActionGrade.ToString(),
                    successDelta = parry.SuccessCount - success, feedbackDelta = parry.FeedbackCount - feedback, mainDelta = presenter.MainCount - main,
                    probe.QueueObserved, probe.Intervened, probe.VisibleMain, probe.MaxFragments, probe.HaloParticles,
                    deathCommitted = actor.Health.IsDead,
                    originalTingLayers = ParryFeedbackService.LastAdditionalTingCount });
                File.WriteAllText(Path.Combine(output, "window-boundary.json"), JsonConvert.SerializeObject(new { status = "RUNNING", cases }, Formatting.Indented));
                probe.Disarm();
                if (enemy != null && enemy.IsLeased) spawn.Release(enemy); enemy = null;
            }
            bool passed = cases.All(value => (bool)JObject.FromObject(value)["pass"]);
            File.WriteAllText(Path.Combine(output, "window-boundary.json"), JsonConvert.SerializeObject(new { status = passed ? "PASS" : "FAIL", cases }, Formatting.Indented));
            Check(passed, "Window expiry and actual cancellation regression");
        }
        finally
        {
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            if (enemy != null && enemy.IsLeased) spawn?.Release(enemy);
            if (set != null) UnityEngine.Object.DestroyImmediate(set);
            parry.enabled = true; actor.GetComponent<PerfectParryContactPresenter>().enabled = true;
            melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
            profile.enhancedPresentation = enabledBefore; actor.Health.SetMaxHp(maxHpBefore, true);
            if (weaponBefore != null) actor.Equipment.EquipWeaponItem(weaponBefore); else actor.Equipment.ClearCurrentWeapon();
            equipGem.Invoke(actor.Equipment, new object[] { gemBefore });
            if (ownEnergy && energy != null) UnityEngine.Object.DestroyImmediate(energy);
        }
    }
}

// Intervene after real feedback queues its contact and before presenter LateUpdate (480).
[DefaultExecutionOrder(470)]
public sealed class PerfectParryWindowBoundaryProbe : MonoBehaviour
{
    private string scenario;
    private PlayerActorRuntime actor;
    private PlayerParryController parry;
    private PerfectParryContactPresenter presenter;
    private MeleeRuntime melee;
    private EnemyActor enemy;
    private EnemySpawnService spawn;
    private int feedbackBefore, mainBefore;
    private bool armed;
    public bool QueueObserved { get; private set; }
    public bool Intervened { get; private set; }
    public bool VisibleMain { get; private set; }
    public int MaxFragments { get; private set; }
    public int HaloParticles { get; private set; }
    public void Arm(string name, PlayerActorRuntime owner, PlayerParryController controller, PerfectParryContactPresenter contact,
        MeleeRuntime runtime, EnemyActor source, EnemySpawnService service, int feedback, int main)
    {
        scenario = name; actor = owner; parry = controller; presenter = contact; melee = runtime; enemy = source; spawn = service;
        feedbackBefore = feedback; mainBefore = main; armed = true; QueueObserved = Intervened = VisibleMain = false; MaxFragments = HaloParticles = 0;
    }
    public void Disarm() { armed = false; }
    private void LateUpdate()
    {
        if (!armed) return;
        if (parry.FeedbackCount > feedbackBefore && !Intervened)
        {
            QueueObserved |= (bool)typeof(PerfectParryContactPresenter).GetField("queued", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);
            switch (scenario)
            {
                case "ExplicitCancel": melee.CancelCurrentAttackState(); break;
                case "NewAction":
                    melee.CancelCurrentAttackState();
                    if (melee.TryStartHeavyAttack(actor.transform.forward) != WeaponActionResult.Accepted)
                        throw new InvalidOperationException("Next real heavy action accepts after cancellation");
                    break;
                case "WeaponChanged": actor.Equipment.ClearCurrentWeapon(); break;
                case "TargetReleased": spawn.Release(enemy); break;
                case "OwnerDisabled": parry.enabled = false; break;
                case "PresenterDisabled": presenter.enabled = false; break;
                case "Death":
                    var arena = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugArena>();
                    bool protectedBefore = actor.Health.IsDeathFromDamagePrevented;
                    actor.Health.SetDamageDeathPrevention(arena, false);
                    try
                    {
                        actor.Health.TakeDamage(new DamageInfo(1e12f, actor.transform.position,
                            direction: Vector3.zero, suppressDefaultHitVfx: true));
                        if (!actor.Health.IsDead) throw new InvalidOperationException("Real lethal damage commits death before presentation");
                    }
                    finally { if (protectedBefore) actor.Health.SetDamageDeathPrevention(arena, true); }
                    break;
                default: goto Observe;
            }
            Intervened = true;
        }
        Observe:
        if (presenter.MainCount <= mainBefore) return;
        foreach (var cue in UnityEngine.Object.FindObjectsByType<PerfectParryContactVfx>(FindObjectsSortMode.None))
        {
            if (cue.additional || !cue.isActiveAndEnabled || !cue.IsPlaybackAlive) continue;
            int particles = cue.stroke != null ? cue.stroke.particleCount : 0;
            MaxFragments = Mathf.Max(MaxFragments, particles);
            HaloParticles = Mathf.Max(HaloParticles, cue.glow != null ? cue.glow.particleCount : 0);
            VisibleMain |= particles > 0 && cue.stroke.GetComponent<ParticleSystemRenderer>().enabled;
        }
    }
}
