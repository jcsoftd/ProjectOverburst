using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MonsterStrongBalanceVerifier
{
    public static string Run(string relative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string output = MonsterMediumStrongCooldownBuilder.Output(relative);
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(output, "plan.json")));
        var checks = new JArray(); var records = new JArray();
        void Check(bool pass, string name) => checks.Add(new JObject { ["pass"] = pass, ["name"] = name });
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var row in plan["balanceRows"])
            {
                var d = AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definitionPath"]);
                var ability = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>((string)row["path"]);
                bool elite = (string)row["grade"] == "elite";
                Check(d.IsValid && ability.IsValid, d.EnemyId + ": saved assets valid");
                Check(Mathf.Abs(ability.Cooldown - (float)row["newCooldown"]) < .00001f, d.EnemyId + ": saved cooldown");
                Check(Mathf.Abs(d.ResolveRuntimeStats().AttackInterval - (elite ? 3 : (float)row["interval"])) < .0001f,
                    d.EnemyId + ": saved completion interval");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(d.ActorPrefab.gameObject, scene);
                try
                {
                    var actor = instance.GetComponent<EnemyActor>();
                    actor.GetComponent<EnemyRank>().ConfigureFromDefinition(d);
                    actor.Movement.SetProfile(d.MovementProfile);
                    var reaction = new SerializedObject(actor.GetComponent<EnemyMovementReaction>());
                    reaction.FindProperty("movement").objectReferenceValue = actor.Movement;
                    reaction.FindProperty("rank").objectReferenceValue = actor.GetComponent<EnemyRank>();
                    reaction.ApplyModifiedPropertiesWithoutUndo();
                    float radius = EnemyAttackThreatGeometry.ResolveRadius(actor, ability);
                    float start = EnemyAttackThreatGeometry.ResolveStartRange(actor, ability);
                    float expected = ability.HitRadius + (elite ? 2.7f : 1.7f);
                    Check(Mathf.Abs(radius - expected) < .0001f, d.EnemyId + ": shared damage/telegraph/parry radius");
                    bool stationary = ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc || ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam;
                    Check(!stationary || start <= radius + .0801f, d.EnemyId + ": stationary start within effective reach");
                    Check(EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability) == Mathf.Max(150, ability.HitAngle), d.EnemyId + ": angle preserved");
                    records.Add(new JObject { ["id"] = d.EnemyId, ["radius"] = radius, ["startRange"] = start, ["cooldown"] = ability.Cooldown,
                        ["interval"] = d.ResolveRuntimeStats().AttackInterval });
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        int failed = checks.Count(c => !(bool)c["pass"]);
        string path = Path.Combine(output, "balance-native.json");
        File.WriteAllText(path, new JObject { ["status"] = failed == 0 ? "PASS_NATIVE" : "FAIL", ["failed"] = failed,
            ["checks"] = checks, ["records"] = records }.ToString());
        if (failed != 0) throw new InvalidOperationException("Strong balance failures: " + failed);
        return path;
    }

    internal static JObject CheckBoundary(EnemyActor actor, EnemyAbilityDefinition ability, CombatTarget target)
    {
        var warning = actor.GetComponent<EnemyStrongAttackWarning>();
        float radius = EnemyAttackThreatGeometry.ResolveRadius(actor, ability);
        float angle = EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability);
        bool charge = ability.ExecutionMode == EnemyAbilityExecutionMode.Charge;
        Vector3 center = charge ? actor.transform.position : EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability, actor.Melee.AttackPoint.position);
        Vector3 previous = target.transform.position;
        var special = actor.GetComponent<EnemyThemeSpecialExecutor>();
        var directionField = typeof(EnemyThemeSpecialExecutor).GetField("chargeDirection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Vector3 previousDirection = charge ? special.ChargeDirection : Vector3.zero;
        // The cooldown test has cancelled the action; restore a committed direction only for these boundary samples.
        if (charge) directionField.SetValue(special, actor.transform.forward);
        var samples = new JArray(); bool pass = true;
        warning.Show(radius, true, angle, charge, true, 1.1f, EnemyAttackThreatGeometry.ChargeHalfWidth,
            EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor, ability));
        warning.SetCenter(center); warning.SetFacing(actor.transform.forward); warning.SetRemaining(.2f, false);
        try
        {
            var indicator = actor.GetComponentInChildren<ProceduralGroundIndicator>();
            if (indicator == null || indicator.Surface == null) throw new InvalidOperationException("Actual telegraph required.");
            var renderer = indicator.Surface; var vertices = renderer.mesh.vertices;
            float outer = radius + (charge ? EnemyAttackThreatGeometry.ChargeHalfWidth : 0);
            float visualRadius = vertices.Max(v => { var p = renderer.transform.TransformPoint(v) - center; p.y = 0;
                return charge ? Vector3.Dot(p, actor.transform.forward) : p.magnitude; });
            pass &= Mathf.Abs(visualRadius - outer) < .025f;
            foreach (float offset in new[] { -.12f, .12f, .25f })
            {
                target.transform.position = center + actor.transform.forward * (outer + target.CurrentVolume.Radius + offset);
                Physics.SyncTransforms();
                bool damageGeometry = EnemyAttackThreatGeometry.OverlapsStrongArea(actor, ability, center, actor.transform.forward, target.CurrentVolume);
                bool parryPreview = EnemyAttackThreatGeometry.WouldHit(actor, ability, target);
                bool expected = offset < 0;
                pass &= damageGeometry == expected && parryPreview == expected;
                samples.Add(new JObject { ["edgeOffset"] = offset, ["damageGeometry"] = damageGeometry, ["parryPreview"] = parryPreview, ["expected"] = expected });
            }
            return new JObject { ["pass"] = pass, ["radius"] = radius, ["visualRadius"] = visualRadius, ["samples"] = samples };
        }
        finally
        {
            if (charge) directionField.SetValue(special, previousDirection);
            warning.Hide(); target.transform.position = previous; Physics.SyncTransforms();
        }
    }
}
