using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MonsterMediumStrongCooldownVerifier
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Run(string relative)
    {
        MonsterBlenderParryR6Builder.RequireIdle(); string output = MonsterMediumStrongCooldownBuilder.Output(relative);
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(output, "plan.json")));
        var checks = new JArray(); void Check(bool pass, string name) { checks.Add(new JObject { ["pass"] = pass, ["name"] = name }); }
        var scene = EditorSceneManager.NewPreviewScene(); var owned = new List<Object>();
        var random = UnityEngine.Random.state;
        try
        {
            var weakFixture = ScriptableObject.CreateInstance<EnemyAbilityDefinition>(); owned.Add(weakFixture);
            weakFixture.Configure("medium_cooldown_weak_fixture", "Attack1", 10, 10, 1, 360, 1, .4f, .4f, 1, 1, false, animationDuration: 1);
            var setFixture = ScriptableObject.CreateInstance<EnemyAbilitySet>(); owned.Add(setFixture);
            var go = new GameObject("Medium cooldown contract"); owned.Add(go); SceneManager.MoveGameObjectToScene(go, scene);
            var rank = go.AddComponent<EnemyRank>(); var movement = go.AddComponent<EnemyMovement>();
            var reaction = go.GetComponent<EnemyMovementReaction>() ?? go.AddComponent<EnemyMovementReaction>();
            var bind = new SerializedObject(reaction); bind.FindProperty("rank").objectReferenceValue = rank;
            bind.FindProperty("movement").objectReferenceValue = movement; bind.ApplyModifiedPropertiesWithoutUndo();
            var executor = go.AddComponent<MediumStrongCooldownFixtureExecutor>();
            var controller = go.AddComponent<EnemyAbilityController>(); bind = new SerializedObject(controller);
            var executors = bind.FindProperty("executors"); executors.arraySize = 1;
            executors.GetArrayElementAtIndex(0).objectReferenceValue = executor; bind.ApplyModifiedPropertiesWithoutUndo();
            var target = new GameObject("Cooldown selection target"); owned.Add(target); SceneManager.MoveGameObjectToScene(target, scene);
            target.transform.position = Vector3.forward * .5f;
            var type = typeof(EnemyAbilityController);
            void Set(string name, object value) => type.GetField(name, Private).SetValue(controller, value);
            var cooldowns = (Dictionary<EnemyAbilityDefinition, float>)type.GetField("readyTimeByAbility", Private).GetValue(controller);
            var select = type.GetMethod("TrySelectAbility", Private);
            void Begin(EnemyAbilityDefinition ability, bool running)
            { Set("lastCommittedAbility", ability); Set("committedAttackRunning", true); executor.Running = running; }
            foreach (var d in MonsterMediumStrongCooldownBuilder.Roster())
            {
                rank.ConfigureFromDefinition(d); movement.SetProfile(d.MovementProfile); controller.Configure(d.AbilitySet, 1, 1, 0);
                var abilities = Enumerable.Range(0, d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).ToArray();
                var strong = abilities.FirstOrDefault(a => a.IsTelegraphedStrongAttack);
                if (strong == null) continue;
                setFixture.Configure("medium_cooldown_fixture", new[] { weakFixture, strong });
                controller.Configure(setFixture, 1, 1, 0);
                bool medium = MonsterMediumStrongCooldownBuilder.Medium(d);
                if (medium)
                {
                    var row = plan["rows"].Single(r => (string)r["id"] == d.EnemyId);
                    Check(Mathf.Abs(strong.Cooldown - (float)row["cooldown"]) < .0001f && strong.Cooldown >= 3 && strong.Cooldown <= 6, d.EnemyId + ": saved fixed cooldown");
                    Begin(strong, true); cooldowns[strong] = Time.time - 100;
                    controller.GetRemainingCooldown(strong); Check(executor.Running, d.EnemyId + ": executing motion untouched");
                    executor.Running = false; float remaining = controller.GetRemainingCooldown(strong);
                    Check(Mathf.Abs(remaining - strong.Cooldown) < .001f && !controller.IsCooldownReady(strong), d.EnemyId + ": full cooldown starts after completion");
                    Check(Mathf.Abs(controller.GetRemainingCooldown(strong) - remaining) < .001f, d.EnemyId + ": reads do not restart cooldown");
                    var args = new object[] { target.transform, null };
                    bool selected = (bool)select.Invoke(controller, args);
                    var chosen = selected ? (EnemyAbilityDefinition)args[1].GetType().GetProperty("Ability").GetValue(args[1]) : null;
                    Check(chosen != null && !chosen.IsTelegraphedStrongAttack, d.EnemyId + ": weak remains available during strong cooldown");
                    controller.ClearTarget(); Check(!controller.IsCooldownReady(strong), d.EnemyId + ": target loss preserves cooldown");
                    var weak = abilities.First(a => !a.IsTelegraphedStrongAttack);
                    cooldowns[strong] = Time.time + 1.25f; Begin(weak, false);
                    Check(Mathf.Abs(controller.GetRemainingCooldown(strong) - 1.25f) < .001f, d.EnemyId + ": weak completion does not reset strong cooldown");
                    Begin(strong, true); controller.Cancel();
                    Check(Mathf.Abs(controller.GetRemainingCooldown(strong) - strong.Cooldown) < .001f, d.EnemyId + ": interrupted strong receives full cooldown");
                    cooldowns[strong] = Time.time - .001f; Check(controller.IsCooldownReady(strong), d.EnemyId + ": cooldown expiry unlocks strong");
                }
                else
                {
                    cooldowns[strong] = Time.time + 1.25f; Begin(strong, false);
                    Check(Mathf.Abs(controller.GetRemainingCooldown(strong) - 1.25f) < .001f, d.EnemyId + ": non-medium cooldown unchanged");
                }
                controller.ResetForReuse();
                Check(controller.GetRemainingCooldown(strong) == 0 && controller.RemainingAttackInterval == 0, d.EnemyId + ": pool reset clears cooldown");
            }
            Check(EnemyAbilityController.MediumStrongAttackChance == .30f, "70/30 selection chance preserved");
        }
        finally
        {
            UnityEngine.Random.state = random;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        int failed = checks.Count(r => !(bool)r["pass"]); string path = Path.Combine(output, "native-results.json");
        File.WriteAllText(path, new JObject { ["status"] = failed == 0 ? "PASS_NATIVE" : "FAIL", ["failed"] = failed, ["checks"] = checks }.ToString());
        if (failed != 0) throw new InvalidOperationException("Medium cooldown contract failures: " + failed);
        return path;
    }
}

public sealed class MediumStrongCooldownFixtureExecutor : EnemyAbilityExecutor
{
    public bool Running;
    public override bool IsExecuting => Running;
    public override bool Supports(EnemyAbilityDefinition ability) => ability != null;
    public override bool CanStart(EnemyAbilityDefinition ability, Transform target) => !Running;
    public override bool TryStart(EnemyAbilityDefinition ability, int index, Transform target) { Running = true; return true; }
    public override float ResolveCooldown(float cooldown) => cooldown;
    public override void Cancel() => Running = false;
    public override void ResetForReuse() => Running = false;
}
