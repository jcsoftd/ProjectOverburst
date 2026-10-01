using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MonsterTunerPlayVerifier
{
    private const string Key = "MonsterTunerPlay";
    private static IEnumerator routine;
    private static double deadline;
    private static int frame;
    private static readonly List<object> checks = new List<object>();
    private static readonly List<string> errors = new List<string>();
    private static string Output => Path.Combine(MonsterTunerSession.OutputRoot, "20261001_Implementation/QA");
    static MonsterTunerPlayVerifier() { EditorApplication.playModeStateChanged += State; }
    public static string Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return "Editor busy";
        string saved = MonsterTunerVerifier.SaveRoundTrip(true);
        if (!saved.Contains("PASS")) return saved;
        string root = SessionState.GetString(Key + ".Root", "");
        SessionState.SetString(Key + ".PreviousStart", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + ".Scenes", JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty }).ToArray()));
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Owned verification ground"; ground.transform.localScale = Vector3.one * 4;
            var camera = new GameObject("Owned verification camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new Vector3(6, 8, -10), Quaternion.Euler(32, -30, 0));
            var light = new GameObject("Owned verification light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, 30, 0);
            var services = new GameObject("Owned monster services");
            var inactive = new GameObject("Owned inactive pool"); inactive.transform.SetParent(services.transform, false); inactive.SetActive(false);
            var pool = services.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
            var catalog = ScriptableObject.CreateInstance<EnemyCatalog>();
            catalog.Configure(new[] { AssetDatabase.LoadAssetAtPath<EnemyDefinition>(root + "/Definition.asset") }); AssetDatabase.CreateAsset(catalog, root + "/Catalog.asset");
            services.AddComponent<EnemySpawnService>().Configure(catalog, pool);
            if (!EditorSceneManager.SaveScene(scene, root + "/PlayFixture.unity")) throw new InvalidOperationException("Fixture scene save failed");
        }
        finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(root + "/PlayFixture.unity");
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".Status", "RUNNING");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(Output, "OwnedPlayAccount"));
        EditorApplication.EnterPlaymode(); return "Isolated Play fixture queued";
    }
    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); deadline = EditorApplication.timeSinceStartup + 90; frame = -1;
            SessionState.SetBool(Key + ".Background", Application.runInBackground); Application.runInBackground = true;
            routine = Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log; (routine as IDisposable)?.Dispose(); routine = null;
            Application.runInBackground = SessionState.GetBool(Key + ".Background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null);
            IsolatedSavePlayGuard.UseRealAccount();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ".PreviousStart", ""));
            string actual = JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty }).ToArray());
            bool preserved = actual == SessionState.GetString(Key + ".Scenes", "");
            File.WriteAllText(Path.Combine(Output, "play-return.json"), JsonConvert.SerializeObject(new { sceneDirtyPreserved = preserved, accountEnvironmentCleared = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY")), status = SessionState.GetString(Key + ".Status", "") }, Formatting.Indented));
            string root = SessionState.GetString(Key + ".Root", "");
            if (root.StartsWith("Assets/Editor/Testers/Characters/MonsterTuner/Fixtures/Run_", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(root);
            SessionState.SetBool(Key, false);
        }
    }
    private static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return; frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException("Play verification timeout");
            if (routine.MoveNext()) return;
            Assert("새 런타임 오류 없음", errors.Count == 0, string.Join(" | ", errors)); Finish(true, "");
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    private static void Finish(bool success, string error)
    {
        SessionState.SetString(Key + ".Status", success ? "PASS" : "FAIL");
        File.WriteAllText(Path.Combine(Output, "play-verification.json"), JsonConvert.SerializeObject(new { success, error, checks, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    private static void Assert(string name, bool success, string detail = "") { checks.Add(new { name, success, detail }); if (!success) throw new InvalidOperationException(name + ": " + detail); }
    private static IEnumerator Verify()
    {
        Assert("제품 씬 실행 없음", SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().name == "PlayFixture");
        Assert("계정 Bootstrap 없음", !Overburst.Persistence.AccountBootstrap.Attempted);
        var spawn = EnemySpawnService.Current; Assert("실제 Spawn 서비스", spawn != null);
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(SessionState.GetString(Key + ".Root", "") + "/Definition.asset");
        var targetObject = new GameObject("Owned attack target"); targetObject.transform.position = Vector3.forward * 5;
        int playerLayer = LayerMask.NameToLayer("Player"); if (playerLayer >= 0) targetObject.layer = playerLayer;
        var targetCollider = targetObject.AddComponent<CapsuleCollider>(); targetCollider.height = 2; targetCollider.center = Vector3.up;
        var health = targetObject.AddComponent<CombatHealth>(); health.SetMaxHp(100000, true); targetObject.AddComponent<CombatTarget>().Configure(CombatTeam.PlayerParty, true);
        for (int pass = 0; pass < 2; pass++)
        {
            targetObject.transform.position = Vector3.forward * 5; Physics.SyncTransforms();
            Assert("저장 후 실제 Actor 출현 " + pass, spawn.TrySpawn(new EnemySpawnRequest(definition, Vector3.up * .03f, Quaternion.identity, targetObject.transform), out var actor));
            actor.AI.enabled = false; actor.Movement.StopMovement(); actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return null;
            Assert("실전 외형·충돌·기준점 크기", actor.VisualRoot.localScale == definition.ResolveRuntimeStats().VisualScale && actor.CollisionRoot.localScale == definition.ResolveRuntimeStats().CollisionScale && actor.Anchors.localScale == definition.ResolveRuntimeStats().AnchorScale);
            Assert("실전 Controller 교체", actor.Animator.runtimeAnimatorController == definition.AnimationProfile.RuntimeController);
            var executor = actor.GetComponent<EnemyThemeSpecialExecutor>();
            int index = Enumerable.Range(0, definition.AbilitySet.Count).First(i => definition.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.Projectile);
            var ability = definition.AbilitySet.GetAbility(index);
            var muzzles = new SerializedObject(executor).FindProperty("muzzleOverrides"); var entry = muzzles.GetArrayElementAtIndex(0);
            var socket = entry.FindPropertyRelative("socket").objectReferenceValue as Transform;
            Assert("실제 발사 소켓 계산", Vector3.Distance(executor.ResolveMuzzlePosition(ability, Vector3.forward), socket.TransformPoint(entry.FindPropertyRelative("localOffset").vector3Value)) < .001f);
            actor.Movement.CancelActionLock(); yield return null;
            int launches = executor.LaunchCount;
            Assert("실제 원거리 공격 시작", executor.TryStart(ability, index, targetObject.transform));
            float end = Time.time + ability.ResolveExecutionDuration(actor.Melee.AbilityAnimationSpeed) + 2f;
            while (executor.LaunchCount == launches && Time.time < end) yield return null;
            Assert("실제 발사 실행", executor.LaunchCount > launches);
            while (Time.time < end) yield return null;
            int meleeIndex = Enumerable.Range(0, definition.AbilitySet.Count).First(i => definition.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc || definition.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam);
            var melee = definition.AbilitySet.GetAbility(meleeIndex);
            targetObject.transform.position = actor.Melee.AttackPoint.position + actor.transform.forward * .4f - Vector3.up * .7f;
            actor.Movement.CancelActionLock(); Physics.SyncTransforms(); yield return null;
            Assert("시험 대상 실제 근접 판정 포함", actor.Melee.WouldAbilityHitTarget(melee, targetObject.GetComponent<CombatTarget>()));
            int impacts = 0;
            void DamageReceived(CombatHealth damaged, DamageInfo info) { impacts++; }
            health.OnDamaged += DamageReceived;
            Assert("저장한 추가 타격 공격 시작", actor.Melee.TryStartAbility(targetObject.transform, melee, meleeIndex));
            float meleeEnd = Time.time + melee.ResolveExecutionDuration(actor.Melee.AbilityAnimationSpeed) + 2f;
            while (impacts < melee.HitCount && Time.time < meleeEnd) yield return null;
            health.OnDamaged -= DamageReceived;
            Assert("실제 첫·추가 타격 실행", impacts == melee.HitCount && impacts == 3, impacts.ToString());
            while (Time.time < meleeEnd) yield return null;
            Assert("다른 이름 패링 3단계 시작", actor.AnimationBridge.TryPlayParryStun(out float lockSeconds));
            Assert("패링 동작 잠금", actor.AnimationBridge.IsParryStunAnimating && lockSeconds > 2f);
            var parryStates = new List<string>();
            float parryEnd = Time.time + lockSeconds + definition.AnimationProfile.StunRecover.length + 1f;
            while (actor.AnimationBridge.IsParryStunAnimating && Time.time < parryEnd)
            {
                var state = actor.Animator.IsInTransition(0) ? actor.Animator.GetNextAnimatorStateInfo(0) : actor.Animator.GetCurrentAnimatorStateInfo(0);
                foreach (string name in new[] { EnemyAnimationBridge.ParryCollapseStateName, EnemyAnimationBridge.StunnedLoopStateName, EnemyAnimationBridge.StunRecoverStateName })
                    if (state.IsName(name) && !parryStates.Contains(name)) parryStates.Add(name);
                yield return null;
            }
            Assert("실제 패링 무너짐·반복·회복 순서", parryStates.SequenceEqual(new[] { EnemyAnimationBridge.ParryCollapseStateName, EnemyAnimationBridge.StunnedLoopStateName, EnemyAnimationBridge.StunRecoverStateName }), string.Join(" > ", parryStates));
            Assert("패링 3단계 종료 잠금 해제", !actor.AnimationBridge.IsParryStunAnimating);
            spawn.Release(actor); yield return null;
            Assert("풀 반환", spawn.Pool.LeasedCount == 0);
        }
        Object.Destroy(targetObject);
    }
}
