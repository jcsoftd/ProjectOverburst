using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MonsterWeakAttackMotionVerifier
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("약공 이동 검사는 유휴 EditMode에서만 실행합니다.");
        string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
        string allowed = Path.GetFullPath(Path.Combine(workspace, "개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (!outputDirectory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("산출 경로가 아닙니다.");
        var checks = new List<object>(); int failed = 0;
        void Check(string name, bool pass) { checks.Add(new { name, pass }); if (!pass) failed++; }
        var owned = new List<UnityEngine.Object>();
        Scene originalActive = SceneManager.GetActiveScene();
        Scene fixture = default;
        bool cleaned = false;
        try
        {
            var clip = new AnimationClip { name = "V3_MotionFixture", frameRate = 30 }; owned.Add(clip);
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 0));
            EnemyWeakAttackExecutionProfile Profile(EnemyWeakAttackMotionPolicy policy)
            {
                var p = ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>(); owned.Add(p);
                bool moving = policy == EnemyWeakAttackMotionPolicy.ShortAdvance;
                p.Configure("motion-fixture", clip, clip, new Vector2(0, 1), policy, 1.5f, moving ? .5f : 0,
                    moving ? new Vector2(.1f, .6f) : Vector2.zero, moving ? AnimationCurve.Linear(0, 0, 1, 1) : null, "");
                return p;
            }
            var movingProfile = Profile(EnemyWeakAttackMotionPolicy.ShortAdvance);
            var advance = new EnemyWeakAttackAdvance();
            advance.Begin(movingProfile, 1.4f, Vector3.forward);
            Check("close advance uses zero planar budget", advance.Budget == 0 && advance.Consume(1, .3f) == Vector3.zero);
            advance.Begin(movingProfile, 1.7f, new Vector3(2, 8, 0));
            Check("only missing reach is budgeted", Mathf.Abs(advance.Budget - .2f) < .0001f && advance.Direction == Vector3.right);
            Check("motion entry before advance window does not move", advance.Consume(.09f, .3f) == Vector3.zero);
            var step = advance.Consume(.35f, .3f);
            Check("authored window requests planar portion", Mathf.Abs(step.x - .1f) < .0001f && step.y == 0);
            Check("same native progress cannot request twice", advance.Consume(.35f, .3f) == Vector3.zero);
            advance.Begin(movingProfile, 5, Vector3.forward);
            Check("start distance never enlarges approved maximum", Mathf.Abs(advance.Budget - .5f) < .0001f);
            Check("large frame gap is bounded to one step", Mathf.Abs(advance.Consume(.6f, .3f).magnitude - .3f) < .0001f);
            Check("excessive catch-up is discarded", advance.ConsumedDistance == .5f && advance.Consume(1, .3f) == Vector3.zero);
            advance.Reset();
            Check("reset removes owned budget and direction", !advance.IsActive && advance.Budget == 0 && advance.Direction == Vector3.zero);
            bool rejected = false;
            try { advance.Begin(movingProfile, 2, Vector3.zero); } catch (ArgumentException) { rejected = true; }
            Check("positive advance requires starting direction", rejected && !advance.IsActive);
            foreach (var policy in new[] { EnemyWeakAttackMotionPolicy.Stationary, EnemyWeakAttackMotionPolicy.VisualJump, EnemyWeakAttackMotionPolicy.FlightMelee })
            {
                advance.Begin(Profile(policy), 5, Vector3.forward);
                Check(policy + " never requests actor XZ", advance.Budget == 0 && advance.Consume(1, .3f) == Vector3.zero);
            }

            fixture = EditorSceneManager.NewPreviewScene();
            PhysicsScene physics = fixture.GetPhysicsScene();
            Check("fixture physics is independent of product scene", physics.IsValid() && physics != Physics.defaultPhysicsScene);
            var root = new GameObject("V3_MotionActor"); owned.Add(root); SceneManager.MoveGameObjectToScene(root, fixture);
            root.transform.position = new Vector3(12000, 0, 12000);
            Vector3 origin = root.transform.position;
            var body = root.AddComponent<Rigidbody>(); body.useGravity = false; body.interpolation = RigidbodyInterpolation.None;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.center = Vector3.up; capsule.radius = .25f; capsule.height = 2;
            var health = root.AddComponent<CombatHealth>(); health.ResetHealth();
            var target = root.AddComponent<CombatTarget>(); target.ConfigureVolume(Vector3.up, .25f, 2);
            var movement = root.AddComponent<EnemyMovement>(); movement.ResolveReferences();
            var melee = root.AddComponent<EnemyMeleeAttackController>();
            typeof(EnemyMeleeAttackController).GetField("movement", Fields).SetValue(melee, movement);
            var driver = root.AddComponent<EnemyWeakAttackMotionDriver>(); driver.Configure(melee, movement);
            typeof(EnemyMeleeAttackController).GetField("weakMotionDriver", Fields).SetValue(melee, driver);

            GameObject Box(string name, Vector3 position, Vector3 size, bool trigger = false)
            {
                var go = new GameObject(name); owned.Add(go); SceneManager.MoveGameObjectToScene(go, fixture);
                go.transform.position = origin + position; var c = go.AddComponent<BoxCollider>(); c.size = size; c.isTrigger = trigger;
                return go;
            }
            var floor = Box("Floor", new Vector3(0, -.5f, 0), new Vector3(20, 1, 20));
            var wall = Box("Wall", new Vector3(0, 1, .6f), new Vector3(4, 4, .2f));
            Physics.SyncTransforms();
            Vector3 Clip(float radius, float halfHeight, Vector3 request, out bool saturated)
                => EnemyAttackMovementClearance.Clip(root, origin + Vector3.up, radius, halfHeight, request, out saturated);
            step = Clip(.25f, 1, Vector3.forward * .3f, out bool saturated);
            Check("wall clips swept capsule before overlap", !saturated && step.z > .2f && step.z < .23f && step.y == 0);
            step = Clip(.45f, 1, Vector3.forward * .3f, out saturated);
            Check("larger bodies stop earlier", !saturated && step.z > 0 && step.z < .04f);
            wall.GetComponent<Collider>().enabled = false; Physics.SyncTransforms();
            var trigger = Box("Trigger", new Vector3(0, 1, .4f), new Vector3(4, 4, .1f), true); Physics.SyncTransforms();
            step = Clip(.25f, 1, Vector3.forward * .3f, out saturated);
            Check("own capsule floor and trigger permit travel", !saturated && Mathf.Abs(step.z - .3f) < .0001f);
            var overlap = Box("StartingOverlap", new Vector3(0, 1, .2f), new Vector3(1, 2, .2f)); Physics.SyncTransforms();
            Check("starting overlap cannot deepen", Clip(.25f, 1, Vector3.forward * .3f, out _) == Vector3.zero);
            step = Clip(.25f, 1, Vector3.back * .3f, out _);
            Check("starting overlap can escape", Mathf.Abs(step.z + .3f) < .0001f);
            overlap.SetActive(false); Physics.SyncTransforms();
            Check("invalid volume fails closed", Clip(float.NaN, 1, Vector3.forward * .3f, out _) == Vector3.zero);

            var dense = new List<GameObject>();
            for (int i = 0; i < 35; i++) dense.Add(Box("Dense" + i, new Vector3(.001f * i, 1, 0), new Vector3(.1f, .1f, .1f)));
            Physics.SyncTransforms();
            step = Clip(.25f, 1, Vector3.forward * .3f, out saturated);
            Check("query saturation fails closed", saturated && step == Vector3.zero);
            foreach (var go in dense) go.SetActive(false);
            wall.GetComponent<Collider>().enabled = true; Physics.SyncTransforms();

            // Invoke the real one-writer path, then finish this scene's own physics
            // step before reading position. MovePosition is not treated as a hit pose.
            typeof(EnemyMeleeAttackController).GetField("activeWeakExecution", Fields).SetValue(melee, movingProfile);
            var clock = (EnemyAttackClock)typeof(EnemyMeleeAttackController).GetField("weakAttackClock", Fields).GetValue(melee);
            clock.Begin(10, false, 0); clock.Observe(11, .1f, true, .6f);
            driver.Begin(movingProfile, 2, Quaternion.identity);
            Invoke(driver, "FixedUpdate"); Invoke(movement, "FixedUpdate"); physics.Simulate(.02f);
            Vector3 actual = body.position - origin;
            Check("real movement writer uses clipped physics position", actual.z > .2f && actual.z < .23f && Mathf.Abs(actual.y) < .0001f);
            wall.GetComponent<Collider>().enabled = false; Physics.SyncTransforms();
            Invoke(driver, "FixedUpdate"); Invoke(movement, "FixedUpdate"); physics.Simulate(.02f);
            Check("unblocked wall has no accumulated burst", Vector3.Distance(body.position - origin, actual) < .001f);
            Check("owner budget consumed despite physical clipping", driver.ConsumedDistance == .5f);
            movement.ApplyActionLock(.2f); movement.RequestBoundedAttackDisplacement(Vector3.forward * .2f, Quaternion.identity);
            driver.End();
            Check("end clears pending displacement", !driver.IsActive && Read<Vector3>(movement, "pendingAttackDisplacement") == Vector3.zero);

            driver.Begin(movingProfile, 2, Quaternion.identity);
            movement.SetStatusMoveSpeedMultiplier(0);
            Invoke(driver, "FixedUpdate");
            Check("movement lock discards the attack budget", !driver.IsActive && Read<Vector3>(movement, "pendingAttackDisplacement") == Vector3.zero);
            movement.SetStatusMoveSpeedMultiplier(1);
            driver.Begin(movingProfile, 2, Quaternion.identity); driver.enabled = false; Invoke(driver, "OnDisable");
            Check("disabled driver cannot retain movement", !driver.IsActive);
            driver.enabled = true;
            bool invalidFacing = false;
            try { driver.Begin(movingProfile, 2, new Quaternion(0, 0, 0, 0)); } catch (ArgumentException) { invalidFacing = true; }
            Check("invalid committed facing is rejected", invalidFacing && !driver.IsActive);
            movement.ApplyActionLock(.2f);
            Check("nonfinite movement request rejected", !movement.RequestBoundedAttackDisplacement(new Vector3(float.NaN, 0, 0), Quaternion.identity));

            var oldRootMotion = root.AddComponent<EnemyAttackRootMotion>();
            typeof(EnemyAttackRootMotion).GetField("melee", Fields).SetValue(oldRootMotion, melee);
            typeof(EnemyAttackRootMotion).GetField("owed", Fields).SetValue(oldRootMotion, Vector3.right);
            movement.RequestBoundedAttackDisplacement(Vector3.forward * .1f, Quaternion.identity);
            Vector3 pending = Read<Vector3>(movement, "pendingAttackDisplacement");
            Invoke(oldRootMotion, "FixedUpdate");
            Check("legacy root extractor does not duplicate V3 request", Read<Vector3>(movement, "pendingAttackDisplacement") == pending);
            Invoke(oldRootMotion, "LateUpdate");
            Check("legacy planar debt cleared on V3 handover", Read<Vector3>(oldRootMotion, "owed") == Vector3.zero);
            melee.CancelAttack();
            Check("executor cancel removes selected profile and pending movement", melee.ActiveWeakExecution == null && Read<Vector3>(movement, "pendingAttackDisplacement") == Vector3.zero);

            var playerPrefix = EnemyPlayerApproachClearance.Clip(Vector3.zero, Vector3.forward * 3, Vector3.forward * 1.5f, .8f);
            Check("player clearance cannot tunnel through protected body", playerPrefix.z > .69f && playerPrefix.z < .71f);
            Check("player initial overlap may escape", EnemyPlayerApproachClearance.Clip(Vector3.forward, Vector3.zero, Vector3.forward * 1.5f, .8f) == Vector3.zero);
        }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.ClosePreviewScene(fixture);
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            cleaned = !fixture.IsValid() || !fixture.isLoaded;
        }
        Check("temporary physics scene and objects removed", cleaned);
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "motion-results.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new { status = failed == 0 ? "PASS_SCOPED" : "FAIL", checks, failed,
            nativePhysics = "LOCAL_SCENE_SIMULATION", ownPlay = false, actualGameProfilesAttached = false,
            clipNormalization = "NOT_IMPLEMENTED", attackLiftComposition = "NOT_VERIFIED", physicsDamageQueue = "NOT_IMPLEMENTED", audioApplied = false }, Formatting.Indented));
        if (failed > 0) throw new InvalidOperationException("약공 이동 검사 실패: " + failed);
        return path;
    }

    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Fields).Invoke(target, null);
}
