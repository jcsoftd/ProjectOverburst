using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MonsterWeakAttackImpactVerifier
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Run(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("약공 사건 검사는 유휴 EditMode에서만 실행합니다.");
        string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
        string allowed = Path.GetFullPath(Path.Combine(workspace, "개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (!outputDirectory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("산출 경로가 아닙니다.");
        var checks = new List<object>(); int failed = 0;
        void Check(string name, bool pass) { checks.Add(new { name, pass }); if (!pass) failed++; }
        var queue = new EnemyWeakAttackImpactQueue();
        void Begin() => queue.Begin(10, 3, 3, .4f, .6f, .8f, .04f);
        Begin(); queue.Advance(.39f);
        Check("before impact cannot produce contact", queue.ConsumedCount == 0 && !queue.HasPending);
        queue.Advance(.4f);
        Check("motion crossing queues one phase", queue.ConsumedCount == 1 && queue.HasPending);
        Check("correct owner consumes phase once", queue.TryTake(10, 3, out int phase) && phase == 0);
        Check("same event cannot be consumed twice", !queue.TryTake(10, 3, out _));
        queue.Advance(.4f);
        Check("same native sample cannot requeue", !queue.HasPending && queue.ConsumedCount == 1);
        Begin(); queue.Advance(.5f);
        Check("unrecoverable old first hit is a consumed miss", queue.ConsumedCount == 1 && queue.MissedCount == 1 && !queue.HasPending);
        queue.Advance(.6f);
        Check("missed first hit preserves the next phase", queue.TryTake(10, 3, out phase) && phase == 1);
        Begin(); queue.Advance(.8f);
        Check("large frame gap preserves only latest current pose", queue.ConsumedCount == 3 && queue.MissedCount == 2 && queue.TryTake(10, 3, out phase) && phase == 2);
        Begin(); queue.Advance(1);
        Check("completed motion does not recreate old hits", queue.ConsumedCount == 3 && queue.MissedCount == 3 && !queue.HasPending);
        queue.Begin(10, 3, 3, .4f, .42f, .44f, .1f); queue.Advance(.445f);
        Check("nearby crossings cannot burst three hits in one pose", queue.MissedCount == 2 && queue.TryTake(10, 3, out phase) && phase == 2 && !queue.TryTake(10, 3, out _));
        Begin(); queue.Advance(.4f); queue.Advance(.5f);
        Check("pending contact expires with native motion", !queue.HasPending && queue.MissedCount == 1);
        Begin(); queue.Advance(.4f);
        Check("successor attack cannot consume previous strike", !queue.TryTake(11, 3, out _) && !queue.IsActive);
        Begin(); queue.Advance(.4f);
        Check("reused actor lease cannot consume previous strike", !queue.TryTake(10, 4, out _) && !queue.IsActive);
        Begin(); queue.Advance(.4f); queue.Cancel();
        Check("cancel removes pending contact", !queue.HasPending && !queue.TryTake(10, 3, out _));
        Begin(); queue.Advance(.4f); queue.Advance(.3f);
        Check("rewound progress cannot reuse the execution", !queue.IsActive && !queue.HasPending);
        Begin(); queue.Advance(float.NaN);
        Check("invalid progress fails closed", !queue.IsActive);
        Begin(); queue.Advance(.4f); bool invalid = false;
        try { queue.Begin(99, 2, 4, .1f, .2f, .3f, .1f); } catch (ArgumentException) { invalid = true; }
        Check("invalid replacement preserves prior identity", invalid && queue.TryTake(10, 3, out phase) && phase == 0);
        invalid = false;
        try { queue.Begin(99, 2, 2, .3f, .3f, 0, .1f); } catch (ArgumentException) { invalid = true; }
        Check("duplicate impact times rejected", invalid);
        queue.Begin(12, 8, 1, 1, 0, 0, .01f); queue.Advance(1);
        Check("authored terminal-frame impact is retained", queue.TryTake(12, 8, out phase) && phase == 0);

        queue.Begin(20, 4, 2, .4f, .6f, 0, .41f, .72f, 0);
        queue.Advance(.42f);
        Check("short authored first window expires without generic grace",!queue.HasPending && queue.MissedCount==1);
        queue.Advance(.68f);
        Check("longer second window survives its authored duration",queue.TryTake(20,4,out phase) && phase==1);
        queue.Begin(20,4,1,.4f,0,0,.41f,0,0);queue.Advance(.41f);
        Check("authored contact end is inclusive",queue.TryTake(20,4,out phase) && phase==0);
        queue.Begin(20,4,1,.4f,0,0,.41f,0,0);queue.Advance(.4f);
        invalid=false;
        try {queue.Begin(21,4,1,.4f,0,0,.39f,0,0);}catch(ArgumentException){invalid=true;}
        Check("invalid authored deadline preserves active pending strike",invalid && queue.TryTake(20,4,out phase) && phase==0);
        queue.Begin(20,4,1,.4f,0,0,.7f,0,0);queue.Advance(.6f);queue.Cancel();
        Check("cancel removes long authored contact window",!queue.TryTake(20,4,out _));

        VerifyNative(Check);
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "impact-results.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new { status = failed == 0 ? "PASS_SCOPED" : "FAIL", checks, failed,
            ownPlay = false, nativePhysics = "INDEPENDENT_PREVIEW_SCENE", actualPlayerLoopReplay = "NOT_RUN",
            exactGameEventAuthoring = "NOT_COMPLETE", totalDamageBudget = "SEPARATE_NATIVE_VERIFIER", reactionRights = "SEPARATE_NATIVE_VERIFIER", audioApplied = false }, Formatting.Indented));
        if (failed > 0) throw new InvalidOperationException("약공 사건 검사 실패: " + failed);
        return path;
    }

    private static void VerifyNative(Action<string, bool> check)
    {
        int registryBefore = CombatTargetRegistry.RegisteredCount;
        var scene = EditorSceneManager.NewPreviewScene(); var owned = new List<UnityEngine.Object>();
        string folder = "Assets/Editor/Testers/Characters/V3ImpactFixture_" + Guid.NewGuid().ToString("N");
        EnemyMeleeAttackController melee = null;
        try
        {
            AssetDatabase.CreateFolder("Assets/Editor/Testers/Characters", Path.GetFileName(folder));
            var clip = new AnimationClip { name = "V3_ImpactFixture", frameRate = 30 };
            clip.SetCurve("Visual", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 0));
            AssetDatabase.CreateAsset(clip, folder + "/Attack.anim");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Controller.controller");
            var state = controller.layers[0].stateMachine.AddState("Attack_1"); state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            controller.AddParameter("Attack1", AnimatorControllerParameterType.Trigger); AssetDatabase.SaveAssetIfDirty(controller);
            var profile = ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>(); owned.Add(profile);
            profile.Configure("impact-fixture", clip, clip, new Vector2(0, 1), EnemyWeakAttackMotionPolicy.Stationary, 1.6f, 0,
                Vector2.zero, null, "", new [] { new Vector2(.38f,.405f), new Vector2(.58f,.72f) });
            var ability = ScriptableObject.CreateInstance<EnemyAbilityDefinition>(); owned.Add(ability);
            ability.Configure("impact-fixture", "Attack1", 10, 2, 1.6f, 120, 1, 0, .4f, 1, 1, false);
            ability.ConfigureAdditionalHits(.6f); ability.ConfigureWeakAttackExecution(profile);
            var invalidAbility = ScriptableObject.CreateInstance<EnemyAbilityDefinition>(); owned.Add(invalidAbility);
            invalidAbility.Configure("direct-fixture", "Attack1", 10, 2, 1, 120, 1, 0, .2f, 1, 1, false, EnemyAbilityExecutionMode.DirectTarget);
            invalidAbility.ConfigureAdditionalHits(.4f, .6f, .8f);
            bool capped = false;
            try { invalidAbility.ConfigureWeakAttackExecution(profile); } catch (ArgumentException) { capped = true; }
            check("new direct melee also obeys maximum three", capped);
            check("legacy direct four-hit ability remains valid", invalidAbility.IsValid && !invalidAbility.HasWeakAttackExecution);
            check("direct melee cap does not change strong parry classification", !EnemyAbilityDefinition.IsMeleeExecution(EnemyAbilityExecutionMode.DirectTarget));

            GameObject Root(string name, Vector3 position)
            {
                var go = new GameObject(name); owned.Add(go); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = position; return go;
            }
            Vector3 origin = new Vector3(14000, 0, 14000);
            var root = Root("V3_ImpactActor", origin);
            var motor = root.AddComponent<EnemyMotor>(); motor.ResolveReferences();
            var body = root.GetComponent<Rigidbody>(); body.useGravity = false; body.interpolation = RigidbodyInterpolation.None;
            var attacker = root.AddComponent<CombatTarget>(); attacker.Configure(CombatTeam.Enemy, false); attacker.ConfigureVolume(Vector3.up, .25f, 2);
            var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
            var point = new GameObject("AttackPoint"); point.transform.SetParent(root.transform, false); point.transform.localPosition = Vector3.up;
            var animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Update(0);
            var bridge = root.AddComponent<EnemyAnimationBridge>(); bridge.SetAnimator(animator);
            melee = root.AddComponent<EnemyMeleeAttackController>();
            Set(melee, "animationBridge", bridge); Set(melee, "attackPoint", point.transform); Set(melee, "combatTarget", attacker);
            Set(melee, "health", root.GetComponent<CombatHealth>()); Set(melee, "weakAttackMotor", motor); Set(melee, "targetLayer", (LayerMask)(~0));
            var victim = Root("V3_ImpactVictim", origin + Vector3.forward * 1.95f);
            var collider = victim.AddComponent<CapsuleCollider>(); collider.radius = .25f; collider.height = 2; collider.center = Vector3.up;
            var victimTarget = victim.AddComponent<CombatTarget>(); victimTarget.Configure(CombatTeam.Neutral, false); victimTarget.ConfigureVolume(Vector3.up, .25f, 2);
            var hp = victim.GetComponent<CombatHealth>(); hp.ResetHealth();
            var healthSettings = new SerializedObject(hp); healthSettings.FindProperty("showDamageNumbers").boolValue = false; healthSettings.ApplyModifiedPropertiesWithoutUndo();
            var damage = new List<DamageInfo>(); hp.OnDamaged += (_, info) => damage.Add(info);
            int sequence = EnemyAttackSequence.Next(); Set(melee, "attackSequenceId", sequence);
            var routine = (System.Collections.IEnumerator)typeof(EnemyMeleeAttackController).GetMethod("WeakAttackRoutine", Fields)
                .Invoke(melee, new object[] { "Attack1", ability, null, true });
            bool started = routine.MoveNext();
            check("real weak coroutine waits for fixed physics completion", started && routine.Current is WaitForFixedUpdate && damage.Count == 0);
            var clock = (EnemyAttackClock)typeof(EnemyMeleeAttackController).GetField("weakAttackClock", Fields).GetValue(melee);
            clock.Begin(100, false, 0); clock.Observe(101, .1f, true, .4f);
            typeof(EnemyMeleeAttackController).GetMethod("QueueWeakClockCrossings", Fields).Invoke(melee, null);
            check("native crossing queues without immediate health change", damage.Count == 0);
            motor.MoveToPosition(origin + Vector3.forward * .2f);
            Physics.SyncTransforms(); scene.GetPhysicsScene().Simulate(.02f);
            routine.MoveNext();
            check("contact resolves at completed body position", body.position.z > origin.z + .19f && damage.Count == 1 && Mathf.Abs(hp.CurrentHp - 95) < .001f);
            check("actual damage carries frozen sequence and phase", damage.Count == 1 && damage[0].sourceAttackSequenceId == sequence && damage[0].sourceAttackPhaseIndex == 0);
            routine.MoveNext();
            check("another physics step cannot replay same crossing", damage.Count == 1);
            victim.transform.position = origin + Vector3.forward * 4; Physics.SyncTransforms();
            clock.Observe(102, .1f, true, .6f); routine.MoveNext();
            check("out-of-range second hit is a miss", damage.Count == 1);
            victim.transform.position = origin + Vector3.forward * 1.5f; Physics.SyncTransforms(); routine.MoveNext();
            check("returning after consumed miss cannot recreate it", damage.Count == 1);
            melee.CancelAttack(); ((IDisposable)routine).Dispose();
            check("cancel removes queue and selected clip", melee.ActiveWeakExecution == null && !(bool)typeof(EnemyWeakAttackImpactQueue).GetProperty("IsActive")
                .GetValue(typeof(EnemyMeleeAttackController).GetField("weakImpacts", Fields).GetValue(melee)));

            // Run a second execution to prove a missed first phase does not stop
            // the subsequent in-range phase. No AI/scene boot is substituted here.
            hp.ResetHealth(); damage.Clear(); victim.transform.position = origin + Vector3.forward * 4; Physics.SyncTransforms();
            sequence = EnemyAttackSequence.Next(); Set(melee, "attackSequenceId", sequence);
            routine = (System.Collections.IEnumerator)typeof(EnemyMeleeAttackController).GetMethod("WeakAttackRoutine", Fields)
                .Invoke(melee, new object[] { "Attack1", ability, null, true });
            routine.MoveNext(); clock.Begin(200, false, 0); clock.Observe(201, .1f, true, .4f); routine.MoveNext();
            check("missed first native contact does not stop coroutine", damage.Count == 0 && routine.Current is WaitForFixedUpdate);
            victim.transform.position = origin + Vector3.forward * 1.5f; Physics.SyncTransforms();
            clock.Observe(202, .1f, true, .6f); routine.MoveNext();
            check("second native contact succeeds after first miss", damage.Count == 1 && damage[0].sourceAttackPhaseIndex == 1);
            melee.CancelAttack(); ((IDisposable)routine).Dispose();

            // The accepted execution owns its budget even if the ability is edited between phases.
            hp.ResetHealth(); damage.Clear(); victim.transform.position = origin + Vector3.forward * 1.5f; Physics.SyncTransforms();
            sequence = EnemyAttackSequence.Next(); Set(melee, "attackSequenceId", sequence);
            routine = (System.Collections.IEnumerator)typeof(EnemyMeleeAttackController).GetMethod("WeakAttackRoutine", Fields)
                .Invoke(melee, new object[] { "Attack1", ability, null, true });
            routine.MoveNext(); clock.Begin(250, false, 0); clock.Observe(251, .1f, true, .4f); routine.MoveNext();
            Set(ability, "damage", 20f);
            clock.Observe(252, .1f, true, .6f); routine.MoveNext();
            check("two native contacts spend the frozen total budget", damage.Count == 2 && Mathf.Abs(hp.CurrentHp - 90) < .001f
                && damage.TrueForAll(info => Mathf.Abs(info.damage - 5) < .001f));
            check("native second contact keeps damage while suppressing repeated reaction", damage.Count == 2
                && !damage[0].suppressRepeatedAttackReaction && damage[1].suppressRepeatedAttackReaction
                && damage.TrueForAll(info => info.triggersOnHitEffects && !info.isDamageOverTime));
            Set(ability, "damage", 10f); melee.CancelAttack(); ((IDisposable)routine).Dispose();

            hp.ResetHealth(); damage.Clear();
            sequence = EnemyAttackSequence.Next(); Set(melee, "attackSequenceId", sequence);
            routine = (System.Collections.IEnumerator)typeof(EnemyMeleeAttackController).GetMethod("WeakAttackRoutine", Fields)
                .Invoke(melee, new object[] { "Attack1", ability, null, true });
            routine.MoveNext(); clock.Begin(270, false, 0); clock.Observe(271, .1f, true, .42f); routine.MoveNext();
            check("real coroutine expires at first authored contact end",damage.Count==0);
            clock.Observe(272, .1f, true, .69f); routine.MoveNext();
            check("real coroutine preserves longer second authored contact",damage.Count==1
                && damage[0].sourceAttackPhaseIndex==1 && Mathf.Abs(hp.CurrentHp-95)<.001f);
            melee.CancelAttack(); ((IDisposable)routine).Dispose();

            var secondVictim = Root("V3_SecondVictim", origin + new Vector3(.2f, 0, 1.5f));
            var secondCollider = secondVictim.AddComponent<CapsuleCollider>(); secondCollider.center = Vector3.up; secondCollider.radius = .25f; secondCollider.height = 2;
            var secondTarget = secondVictim.AddComponent<CombatTarget>(); secondTarget.Configure(CombatTeam.Neutral, false); secondTarget.ConfigureVolume(Vector3.up, .25f, 2);
            var secondHp = secondVictim.GetComponent<CombatHealth>(); secondHp.ResetHealth();
            var secondSettings = new SerializedObject(secondHp); secondSettings.FindProperty("showDamageNumbers").boolValue = false; secondSettings.ApplyModifiedPropertiesWithoutUndo();
            hp.OnDamaged += (_, __) => melee.CancelAttack();
            secondHp.OnDamaged += (_, info) => { damage.Add(info); melee.CancelAttack(); };
            hp.ResetHealth(); damage.Clear(); victim.transform.position = origin + Vector3.forward * 1.5f; Physics.SyncTransforms();
            sequence = EnemyAttackSequence.Next(); Set(melee, "attackSequenceId", sequence);
            routine = (System.Collections.IEnumerator)typeof(EnemyMeleeAttackController).GetMethod("WeakAttackRoutine", Fields)
                .Invoke(melee, new object[] { "Attack1", ability, null, true });
            routine.MoveNext(); clock.Begin(300, false, 0); clock.Observe(301, .1f, true, .4f); routine.MoveNext();
            check("synchronous cancel stops remaining targets in same weak impact", damage.Count == 1 && melee.ActiveWeakExecution == null);
            ((IDisposable)routine).Dispose();

            int successorSequence = 0;
            Action<CombatHealth, DamageInfo> StartSuccessor = (_, __) =>
            {
                successorSequence = EnemyAttackSequence.Next(); Set(melee, "attackSequenceId", successorSequence);
                Set(melee, "activeWeakExecution", profile);
            };
            hp.OnDamaged += StartSuccessor; secondHp.OnDamaged += StartSuccessor;
            hp.ResetHealth(); secondHp.ResetHealth(); damage.Clear(); Physics.SyncTransforms();
            sequence = EnemyAttackSequence.Next(); Set(melee, "attackSequenceId", sequence);
            routine = (System.Collections.IEnumerator)typeof(EnemyMeleeAttackController).GetMethod("WeakAttackRoutine", Fields)
                .Invoke(melee, new object[] { "Attack1", ability, null, true });
            routine.MoveNext(); clock.Begin(400, false, 0); clock.Observe(401, .1f, true, .4f); routine.MoveNext();
            check("predecessor finally cannot erase successor state", damage.Count == 1 && damage[0].sourceAttackSequenceId == sequence
                && successorSequence != sequence && melee.ActiveWeakExecution == profile);
            melee.CancelAttack(); ((IDisposable)routine).Dispose();
        }
        finally
        {
            melee?.CancelAttack();
            // Preview scenes do not run ordinary MonoBehaviour lifecycle
            // callbacks. Explicitly return this fixture's registry ownership.
            foreach (var item in owned)
                if (item is GameObject go && go != null)
                    foreach (var target in go.GetComponentsInChildren<CombatTarget>(true))
                        CombatTargetRegistry.Unregister(target);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        check("native fixture folder removed", !AssetDatabase.IsValidFolder(folder));
        check("fixture registry count restored", CombatTargetRegistry.RegisteredCount == registryBefore);
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
}
