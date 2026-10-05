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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CrustaspikanTemporaryReactionVerifier
{
    const string Key = "Overburst.CrustaspikanTemporaryReactionVerifier.";
    sealed class Plan { public string output, phase, previousStart; public JArray scenes; public bool background, extrasOnly, cameraOnly; public float timeScale, capture; public double deadline; }
    static Plan plan;
    static readonly JArray cases = new JArray();
    static readonly List<Object> owned = new List<Object>();
    static readonly List<EnemyActor> leases = new List<EnemyActor>();
    static EnemySpawnService service;
    static EnemyMotor host;
    static Camera camera;
    static RenderTexture rt;
    static Texture2D pixels;
    static PlayerActorRuntime player;
    static EnemyBossMaterialCollection collection;
    static CrustaspikanEncounterBrain brain;
    static Coroutine routine;
    static CombatHealth observedHealth;
    static Action<CombatHealth, DamageInfo, float, bool> damageObserver;
    static readonly Vector3 Origin = new Vector3(1600, .04f, 1600);
    static string Account => Path.Combine(plan.output, "Account");
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static JArray Scenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i))
        .Select(s => new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount }));
    static void SavePlan() => SessionState.SetString(Key + "plan", JsonConvert.SerializeObject(plan));
    static CrustaspikanTemporaryReactionVerifier()
    {
        var saved = SessionState.GetString(Key + "plan", ""); if (!string.IsNullOrEmpty(saved)) plan = JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Changed;
    }
    public static string Start(string output) => StartInternal(output, false, false);
    public static string StartExtras(string output) => StartInternal(output, true, false);
    public static string StartCamera(string output) => StartInternal(output, true, true);
    static string StartInternal(string output, bool extrasOnly, bool cameraOnly)
    {
        Require(plan == null && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorUtility.scriptCompilationFailed, "Idle Editor required.");
        Require(!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "Unoccupied real-account selection required.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output); Require(!Directory.Exists(output), "Fresh evidence directory required."); Directory.CreateDirectory(output);
        plan = new Plan { output = output, phase = "booting", scenes = Scenes(), previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            background = Application.runInBackground, capture = Time.captureDeltaTime, timeScale = Time.timeScale, extrasOnly = extrasOnly, cameraOnly = cameraOnly, deadline = EditorApplication.timeSinceStartup + 1200 };
        cases.Clear(); SavePlan(); File.WriteAllText(Path.Combine(output, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        Application.runInBackground = true; IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return "Started actual-player boss reaction verification.";
    }
    static bool OwnPlay => plan != null && string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);
    static void Tick()
    {
        if (plan == null) return;
        if (plan.phase == "returning") { FinishReturn(); return; }
        if (EditorApplication.timeSinceStartup > plan.deadline) { Finish("Verification timed out."); return; }
        if (plan.phase != "booting" || !EditorApplication.isPlaying || !OwnPlay) return;
        plan.phase = "running"; SavePlan();
        var root = new GameObject("Owned Crustaspikan reaction verifier"); owned.Add(root); host = root.AddComponent<EnemyMotor>(); routine = host.StartCoroutine(Guarded(Run()));
    }
    static IEnumerator Guarded(IEnumerator first)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(first);
        while (stack.Count > 0)
        {
            object value = null; Exception error = null; bool more = false;
            try { more = stack.Peek().MoveNext(); if (more) value = stack.Peek().Current; }
            catch (Exception exception) { error = exception; }
            if (error != null) { Finish(error.ToString()); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (value is IEnumerator nested) { stack.Push(nested); continue; }
            yield return value;
        }
        Finish(null);
    }
    static void Record(string id, JObject detail = null)
    {
        var row = detail ?? new JObject(); row["id"] = id; row["pass"] = true; cases.Add(row);
        File.WriteAllText(Path.Combine(plan.output, "result.json"), new JObject { ["status"] = "RUNNING", ["cases"] = cases }.ToString());
    }
    static void Warp(Vector3 position)
    {
        player.CharacterController.enabled = false; player.transform.SetPositionAndRotation(position, Quaternion.identity); player.CharacterController.enabled = true;
        player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms();
    }
    static EnemyActor Spawn()
    {
        Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition, Origin, Quaternion.identity, player.transform, context: EncounterContext.Test), out var actor), "Saved boss spawn failed.");
        leases.Add(actor); actor.AI.enabled = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; actor.Health.SetMaxHp(1000000, true);
        foreach (var collider in actor.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(collider, player.CharacterController);
        Require(actor.GetComponent<CrustaspikanTemporaryReaction>()?.GetUpClip != null, "Saved prefab authored get-up missing."); return actor;
    }
    static void Release(EnemyActor actor) { if (actor != null && actor.IsLeased) service.Release(actor); }
    static void PrepareHeavy(float amount)
    {
        var melee = player.GetComponent<MeleeRuntime>(); melee.CancelCurrentAttackState(); player.GetComponent<PlayerParryController>().CloseWindow();
        OverburstTimeEffectArbiter.ClearOwner(player.GetComponent<PlayerParryController>());
        var energy = player.GetComponent<OverburstElementEnergy>();
        Require(energy != null && player.Equipment.CurrentWeaponItem != null, "Owned energy and equipped weapon required.");
        energy.BindWeapon(player.Equipment.CurrentWeaponItem.runtimeInstanceId, player.Equipment.ActiveElement, player.Equipment.EquippedElementGem?.runtimeInstanceId, player.Equipment.GemRevision);
        typeof(OverburstElementEnergy).GetField("<Amount>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(energy, amount);
    }
    static void Capture(string folder, int frame)
    {
        var previous = RenderTexture.active;
        try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); pixels.Apply();
            var path = Path.Combine(plan.output, "Frames", folder); Directory.CreateDirectory(path); File.WriteAllBytes(Path.Combine(path, frame.ToString("D4") + ".png"), pixels.EncodeToPNG()); }
        finally { RenderTexture.active = previous; camera.targetTexture = null; }
    }
    static JObject Pose(EnemyActor actor, CrustaspikanTemporaryReaction reaction)
    {
        Transform Bone(string name) => actor.Animator.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        JArray Point(string name) { var bone = Bone(name); var p = bone != null ? bone.position - Origin : Vector3.zero; return new JArray(p.x, p.y, p.z); }
        return new JObject { ["time"] = Time.unscaledTime, ["gameTime"] = Time.time, ["phase"] = reaction.Phase.ToString(),
            ["normalized"] = reaction.SampledNormalizedTime, ["leftHand"] = Point("Crustaspikan_ L Hand"), ["rightHand"] = Point("Crustaspikan_ R Hand"),
            ["root"] = new JArray(actor.transform.position.x, actor.transform.position.y, actor.transform.position.z), ["speed"] = actor.Animator.speed };
    }
    static IEnumerator Parry(EnemyBossAttackMaterial material, float energyAmount = 100, bool movie = true)
    {
        PrepareHeavy(energyAmount); var actor = Spawn(); var executor = actor.GetComponent<EnemyBossMaterialExecutor>(); var reaction = actor.GetComponent<CrustaspikanTemporaryReaction>();
        var strike = material.strikes.Last();
        Vector3 position = strike.Origin(actor.transform) + strike.Rotation(actor.transform) * Vector3.forward
            * (strike.shape == GroundIndicatorShape.Donut ? (strike.innerRadius + strike.radius) * .5f : Mathf.Min(6, strike.radius * .5f));
        position.y = Origin.y; Warp(position); yield return new WaitForSeconds(.3f);
        Require(actor.AbilityController.TryStartAbility(material.ability, player.transform), "Attack start failed: " + material.materialId);
        float deadline = Time.unscaledTime + 90, lastProgress = 0; int frame = 0, impacts = 0, rewinds = reaction.RewindCount;
        var poses = new JArray(); var snapshots = new JArray(); bool requested = false, observed = false, positioned = false, avoidedEarlier = false; float requestAt = 0; Vector3 reactionRoot = Origin;
        int parries = player.GetComponent<PlayerParryController>().ParriedAttackCount;
        while (Time.unscaledTime < deadline)
        {
            yield return null;
            // Earlier combo hits are avoided by leaving their lane; the actual heavy/parry path is unchanged.
            if (!avoidedEarlier && material.strikes.Length > 1 && executor.HasEnteredMotion && executor.NormalizedTime < strike.impact - .14f)
            { Warp(actor.transform.position - actor.transform.forward * 18f); avoidedEarlier = true; }
            // Allow grounding to settle before the last-hit input.
            if (!positioned && executor.HasEnteredMotion && executor.NormalizedTime >= Mathf.Max(.03f, strike.impact - .14f))
            { position = strike.Origin(actor.transform) + strike.Rotation(actor.transform) * Vector3.forward * (strike.shape == GroundIndicatorShape.Donut ? (strike.innerRadius + strike.radius) * .5f : Mathf.Min(6, strike.radius * .5f)); position.y = Origin.y; Warp(position); positioned = true; }
            if (!requested && executor.HasEnteredMotion && executor.NormalizedTime >= Mathf.Max(.03f, strike.impact - .035f)
                && actor.AbilityController.IsParryThreatTo(player.GetComponent<CombatTarget>()))
            {
                lastProgress = executor.NormalizedTime; impacts = executor.ImpactCount; reactionRoot = actor.transform.position;
                var action = player.GetComponent<MeleeRuntime>().TryStartHeavyAttack((actor.transform.position - player.transform.position).normalized);
                Require(action == WeaponActionResult.Accepted, "Actual player heavy not accepted: " + material.runtimeClip.name + " / " + action
                    + " / grounded=" + player.Movement.IsGrounded + " / condition=" + player.GetComponent<PlayerStateCoordinator>()?.CurrentCondition);
                requestAt = Time.unscaledTime; requested = true;
            }
            if (movie && executor.HasEnteredMotion && executor.NormalizedTime >= strike.impact - .15f || requested && movie)
            { Capture(material.runtimeClip.name + (energyAmount < 80 ? "-lower" : ""), frame++); snapshots.Add(Pose(actor, reaction)); }
            if (reaction.BlocksActions)
            {
                observed = true; poses.Add(Pose(actor, reaction));
                Require(!actor.AbilityController.IsExecuting && executor.ImpactCount == impacts, "Cancelled attack released damage during sampled rewind.");
                Require(!actor.Health.IsDead && actor.GetComponent<EnemyMovementReaction>().BlocksAttack, "Reaction released a live boss action lock.");
                Require(!actor.Animator.applyRootMotion && Vector3.Distance(actor.transform.position, reactionRoot) < .06f, "Presentation moved the boss root.");
            }
            if (requested && Time.unscaledTime - requestAt > .2f && !reaction.BlocksActions)
            {
                if (energyAmount >= 80 && !observed) continue;
                break;
            }
        }
        Require(requested && Time.unscaledTime < deadline, "Parry flow did not finish: " + material.materialId);
        Require(player.GetComponent<PlayerParryController>().ParriedAttackCount == parries + 1, "Actual player parry did not confirm exactly once.");
        if (energyAmount >= 80)
        {
            Require(reaction.RewindCount == rewinds + 1 && poses.OfType<JObject>().Any(p => (string)p["phase"] == "Prone")
                && poses.OfType<JObject>().Any(p => (string)p["phase"] == "Recover"), "Rewind/prone/recover stage missing.");
            Require(Mathf.Abs(reaction.LastRewindStart - lastProgress) < .08f && reaction.LastRewindEnd < reaction.LastRewindStart
                && (reaction.LastRewindStart - reaction.LastRewindEnd) * material.runtimeClip.length <= .451f, "Contact pose snapshot or bounded recoil extent mismatch.");
            Require(actor.Animator.speed > .99f && !reaction.BlocksActions, "Animator speed or reaction lock left behind.");
        }
        else Require(!observed && reaction.RewindCount == rewinds, "Incomplete/normal parry acquired perfect recoil.");
        Record(material.runtimeClip.name + (energyAmount >= 80 ? "-actual-perfect" : "-grade-" + energyAmount), new JObject { ["frames"] = frame,
            ["rewindStart"] = reaction.LastRewindStart, ["rewindEnd"] = reaction.LastRewindEnd, ["poses"] = poses, ["movie"] = snapshots });
        Release(actor); player.GetComponent<MeleeRuntime>().CancelCurrentAttackState(); yield return new WaitForSeconds(.4f);
    }
    static JObject PunishDebug(EnemyActor boss)
    {
        var melee = player.GetComponent<MeleeRuntime>(); var volume = boss.GetComponent<CombatTarget>().CurrentHurtVolume;
        var executor = typeof(MeleeRuntime).GetField("attackPhaseExecutor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(melee);
        var active = (IEnumerable)typeof(AttackPhaseExecutor).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(executor); var rows = new JArray();
        foreach (object phase in active)
        {
            var type = phase.GetType(); var pattern = (AttackPatternRuntimeData)type.GetField("Pattern").GetValue(phase); var basis = (AttackPatternBasis)type.GetField("Basis").GetValue(phase);
            bool intersects = AttackPatternEvaluator.TryEvaluate(pattern, basis, volume, out float required);
            rows.Add(new JObject { ["started"] = (bool)type.GetField("Started").GetValue(phase), ["completed"] = (bool)type.GetField("Completed").GetValue(phase), ["intersects"] = intersects, ["required"] = required,
                ["snapshotTargets"] = ((AttackTargetSnapshot)type.GetField("Targets").GetValue(phase)).Entries.Count, ["range"] = pattern.Range });
        }
        return new JObject { ["time"] = Time.time, ["playerPosition"] = new JArray(player.transform.position.x, player.transform.position.y, player.transform.position.z),
            ["hurtCenter"] = new JArray(volume.Center.x, volume.Center.y, volume.Center.z), ["radius"] = volume.Radius, ["halfHeight"] = volume.HalfHeight,
            ["attack"] = melee.IsAttackInProgress, ["progress"] = (float)typeof(MeleeRuntime).GetMethod("GetAttackNormalizedTime", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(melee, null),
            ["hp"] = boss.Health.CurrentHp, ["phases"] = rows };
    }
    static IEnumerator Run()
    {
        float wait = Time.unscaledTime + 180;
        while (Time.unscaledTime < wait && (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || !WorldSessionState.IsHideout || PlayerInputFacade.Current == null || AccountGameplaySession.Current == null)) yield return null;
        Require(Time.unscaledTime < wait, "Stable hideout boot timed out."); player = PlayerContext.GetOrCreate().CurrentActor;
        Require(player != null && string.Equals(AccountBootstrap.SaveDirectory, Account, StringComparison.OrdinalIgnoreCase), "Stable isolated player missing."); player.PlayerKit.ApplyAuthority(ActorControlAuthority.AI); player.Health.SetMaxHp(1000000, true);
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Require(player.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Actual weapon equip failed.");
        if (player.GetComponent<OverburstElementEnergy>() == null) owned.Add(player.gameObject.AddComponent<OverburstElementEnergy>());
        var fire = AssetDatabase.LoadAssetAtPath<ElementGemItemData>("Assets/ProjectOverburst/Resources/Items/ElementGems/EG_Fire_Common.asset");
        typeof(PlayerEquipment).GetMethod("SetElementGem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(player.Equipment, new object[] { new ItemData(fire, 1, ItemGrade.Common) });
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); player.GetComponent<MeleeRuntime>().SetManualInputEnabled(true);
        collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath); service = EnemySpawnService.Current;
        if (service == null)
        {
            var serviceRoot = new GameObject("Owned reaction spawn service"); owned.Add(serviceRoot);
            var inactive = new GameObject("Inactive owned bosses"); inactive.transform.SetParent(serviceRoot.transform, false); inactive.SetActive(false);
            var pool = serviceRoot.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
            service = serviceRoot.AddComponent<EnemySpawnService>(); service.Configure(collection.catalog, pool);
        }
        Require(service.RegisterAdditionalCatalog(collection.catalog, out _), "Reaction boss catalog registration failed.");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Origin + Vector3.down * .54f; floor.transform.localScale = new Vector3(100, 1, 100);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.18f, .23f, .26f) }; owned.Add(material); floor.GetComponent<Renderer>().sharedMaterial = material;
        var lightRoot = new GameObject("Owned reaction light"); owned.Add(lightRoot); var light = lightRoot.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Quaternion.Euler(45, -30, 0);
        var cameraRoot = new GameObject("Owned reaction camera"); owned.Add(cameraRoot); camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 9.5f;
        camera.transform.position = Origin + new Vector3(18, 15, 20); camera.transform.LookAt(Origin + new Vector3(0, 4, 3)); camera.farClipPlane = 150; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .065f, .08f);
        camera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
        rt = new RenderTexture(960, 540, 24); rt.Create(); owned.Add(rt); pixels = new Texture2D(960, 540, TextureFormat.RGB24, false); owned.Add(pixels);
        Time.captureDeltaTime = 1f / 30f; Time.timeScale = 1; Warp(Origin + Vector3.forward * 6); yield return new WaitForSeconds(.6f);
        if (plan.cameraOnly) { yield return CameraFraming(); yield break; }
        var basic = collection.attacks.First(m => m.delivery == EnemyBossMaterialDelivery.Melee);
        if (!plan.extrasOnly)
        {
            foreach (var attack in collection.attacks.Where(m => m.delivery == EnemyBossMaterialDelivery.Melee)) yield return Parry(attack);
            yield return Parry(basic, 0, false); yield return Parry(basic, 50, false);
        }
        PrepareHeavy(100); var boss = Spawn(); var temporary = boss.GetComponent<CrustaspikanTemporaryReaction>();
        var encounterRoot = new GameObject("Owned groggy presentation encounter"); owned.Add(encounterRoot); var encounter = encounterRoot.AddComponent<CrustaspikanEncounter>();
        var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(CrustaspikanEncounterBuilder.AssetPath)); owned.Add(settings); settings.groggyMax = 10;
        typeof(CrustaspikanEncounter).GetProperty("Settings").SetValue(encounter, settings); brain = new CrustaspikanEncounterBrain(encounter, boss, player); brain.ReviewMode = true; boss.Health.SetMaxHp(10000, true); var hurtOriginal = boss.GetComponent<CombatTarget>().CurrentHurtVolume; Vector3 hurtOriginalLocal = boss.transform.InverseTransformPoint(hurtOriginal.Center); int recoveryBefore = temporary.RecoveryCount;
        boss.Health.TakeDamage(new DamageInfo(1, boss.transform.position, player.gameObject, Vector3.forward, sourceAttackSequenceId: 60001, playerAttackKind: PlayerAttackKind.Heavy));
        Require(brain.IsGroggy && temporary.IsGroggyAnimating, "Actual health/poise path did not start prone groggy.");
        var groggyPoses = new JArray(); int groggyFrame = 0; float groggyDeadline = Time.unscaledTime + 90; bool froze = false, punished = false; float hpBeforePunish = 0; var damageEvents = new JArray(); var punishDebug = new JArray();
        observedHealth = boss.Health; damageObserver = (h, d, amount, fatal) => damageEvents.Add(new JObject { ["kind"] = d.playerAttackKind.ToString(), ["inputDamage"] = d.damage, ["actualDamage"] = amount, ["hp"] = h.CurrentHp, ["fatal"] = fatal }); observedHealth.OnDamageResolved += damageObserver;
        while (temporary.BlocksActions && Time.unscaledTime < groggyDeadline)
        {
            brain.Tick(); yield return null; Capture("Groggy", groggyFrame++); groggyPoses.Add(Pose(boss, temporary));
            if (!froze && temporary.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Prone)
            {
                float pose = temporary.SampledNormalizedTime; boss.AnimationBridge.SetFrozen(true); yield return new WaitForSeconds(.3f);
                Require(temporary.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Prone && temporary.SampledNormalizedTime == pose, "Freeze advanced the reaction clock.");
                boss.AnimationBridge.SetFrozen(false); froze = true; Record("prone-freeze-resume");
            }
            if (froze && !punished && temporary.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Prone)
            {
                var chest = boss.Animator.GetComponentsInChildren<Transform>(true).First(t => t.name == "Crustaspikan_ Spine2");
                Vector3 target = boss.GetComponent<CombatTarget>().CurrentHurtVolume.Center, standing = boss.GetComponent<CombatTarget>().WorldCenter;
                Require(Vector3.Distance(standing, boss.GetComponent<CombatTarget>().CurrentHurtVolume.Center) > 2, "Prone hurt volume remained at standing origin.");
                Warp(new Vector3(target.x + boss.GetComponent<CombatTarget>().CurrentHurtVolume.Radius + .35f, Origin.y, target.z)); yield return new WaitForSeconds(.15f);
                hpBeforePunish = boss.Health.CurrentHp; player.GetComponent<MeleeRuntime>().CancelCurrentAttackState();
                Require(player.GetComponent<MeleeRuntime>().TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput, null, target - player.transform.position), out _) == WeaponActionResult.Accepted, "Prone punish weak attack rejected.");
                punished = true;
            }
            if (punished) punishDebug.Add(PunishDebug(boss));
            if (temporary.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Recover)
                Require(boss.GetComponent<EnemyMovementReaction>().BlocksAttack && !boss.AbilityController.IsExecuting, "BT attacked while the boss was still rising.");
        }
        File.WriteAllText(Path.Combine(plan.output, "prone-punish-debug.json"), new JObject { ["hpBefore"] = hpBeforePunish, ["hpAfter"] = boss.Health.CurrentHp, ["damageEvents"] = damageEvents, ["frames"] = punishDebug, ["poses"] = groggyPoses }.ToString());
        observedHealth.OnDamageResolved -= damageObserver; observedHealth = null; damageObserver = null;
        Require(Time.unscaledTime < groggyDeadline && !boss.Health.IsDead && temporary.RecoveryCount == recoveryBefore + 1 && boss.Animator.speed > .99f, "Groggy recovery failed: phase=" + temporary.Phase + " hp=" + boss.Health.CurrentHp + " recover=" + temporary.RecoveryCount + "/before=" + recoveryBefore + " speed=" + boss.Animator.speed);
        Require(punished && boss.Health.CurrentHp < hpBeforePunish, "Actual weak attack failed to hit the visible prone torso.");
        var volume = boss.GetComponent<CombatTarget>(); Require(Vector3.Distance(volume.CurrentHurtVolume.Center, boss.transform.TransformPoint(hurtOriginalLocal)) < .01f && Mathf.Abs(volume.CurrentHurtVolume.Radius - hurtOriginal.Radius) < .001f && Mathf.Abs(volume.CurrentHurtVolume.HalfHeight - hurtOriginal.HalfHeight) < .001f, "Pose hurt volume scope remained after recovery.");
        Record("actual-weak-hit-visible-prone-torso");
        brain.Tick(); Require(!brain.IsGroggy, "Groggy BT did not return to waiting.");
        Record("actual-health-groggy-prone-recover", new JObject { ["frames"] = groggyFrame, ["poses"] = groggyPoses }); brain.Dispose(); brain = null;
        Require(temporary.TryPlayGroggy(.8f), "Repeat groggy start failed."); yield return new WaitForSeconds(.6f);
        Release(boss); var reused = Spawn(); Require(reused == boss && !temporary.BlocksActions && reused.Animator.speed > .99f, "Pool reuse retained the sampled death pose/lock."); Record("pool-reuse-restores-animator");
        Require(temporary.TryPlayGroggy(.8f), "Death interruption start failed."); yield return new WaitForSeconds(.6f);
        reused.Health.TakeDamage(new DamageInfo(2000000, reused.transform.position, player.gameObject, Vector3.forward));
        yield return null; Require(reused.Health.IsDead && !temporary.BlocksActions && reused.Animator.speed > .99f, "Logical death left the temporary sampler active."); Record("actual-death-cancels-temporary-reaction"); Release(reused);
        var cancelled = Spawn(); int before = cancelled.GetComponent<CrustaspikanTemporaryReaction>().RewindCount;
        cancelled.AbilityController.TryStartAbility(basic.ability, player.transform); yield return new WaitForSeconds(.2f); cancelled.AbilityController.Cancel(); yield return null;
        Require(!cancelled.GetComponent<CrustaspikanTemporaryReaction>().BlocksActions && cancelled.GetComponent<CrustaspikanTemporaryReaction>().RewindCount == before, "Ordinary cancellation caused a parry reaction.");
        Record("ordinary-cancel-no-rewind"); Release(cancelled);
        yield return CameraFraming();
    }
    static JObject BodyProjection(EnemyActor actor)
    {
        var view = Camera.main; float minX = 1, minY = 1, maxX = 0, maxY = 0; int count = 0;
        var mesh = new Mesh();
        try
        {
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!renderer.enabled || renderer.sharedMesh == null) continue; renderer.BakeMesh(mesh);
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 p = view.WorldToViewportPoint(renderer.transform.TransformPoint(vertex)); if (p.z <= 0) continue;
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); count++;
                }
            }
        }
        finally { Object.Destroy(mesh); }
        Require(count > 0, "Boss body vertices unavailable.");
        return new JObject { ["vertices"] = count, ["minX"] = minX, ["maxX"] = maxX, ["minY"] = minY, ["maxY"] = maxY, ["distanceScale"] = QuarterViewCamera.ActiveInstance.FramingDistanceScale };
    }
    static void CaptureGameplay(string name)
    {
        var view = Camera.main; Require(view != null, "Gameplay camera missing.");
        var oldTarget = view.targetTexture; var oldActive = RenderTexture.active;
        try
        {
            view.targetTexture = rt; view.Render(); RenderTexture.active = rt;
            pixels.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(plan.output, name + ".png"), pixels.EncodeToPNG());
        }
        finally { view.targetTexture = oldTarget; RenderTexture.active = oldActive; }
    }
    static IEnumerator CameraFraming()
    {
        var view = QuarterViewCamera.ActiveInstance; Require(view != null, "Existing QuarterView camera missing.");
        float beforeDistance = view.CurrentDistance, beforeYaw = view.CurrentYaw, beforePitch = view.CurrentPitch;
        var wrongOwner = new GameObject("Owned other framing request"); owned.Add(wrongOwner);
        for (int run = 0; run < 2; run++)
        {
            var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(CrustaspikanEncounterBuilder.AssetPath)); owned.Add(settings);
            settings.entrance.enabled = false; settings.protectPlayerFromDeath = true;
            var root = new GameObject("Owned actual boss-room framing"); owned.Add(root); var room = root.AddComponent<CrustaspikanEncounter>();
            Require(room.Begin(null, settings, player), "Actual encounter entry failed."); room.Brain.ReviewMode = true;
            Require(Mathf.Abs(view.FramingDistanceScale - 1.65f) < .001f && !view.TryBeginFramingScope(wrongOwner, 1.8f), "Room framing scope/owner mismatch.");
            view.EndFramingScope(wrongOwner); Require(view.FramingDistanceScale > 1.64f, "Foreign owner ended room framing.");
            Warp(room.ArenaCenter + new Vector3(0, .08f, -4)); view.SetTarget(view.CurrentTarget); yield return new WaitForSeconds(.4f);
            if (run == 0)
            {
                var projection = BodyProjection(room.Brain.Actor); File.WriteAllText(Path.Combine(plan.output, "camera-framing.json"), projection.ToString());
                Require((float)projection["minY"] > .025f && (float)projection["maxY"] < .975f && (float)projection["minX"] > .025f && (float)projection["maxX"] < .975f, "Default room framing cropped the actual boss body.");
                CaptureGameplay("BossRoom-Wider"); view.EndFramingScope(room); yield return new WaitForSeconds(.2f); CaptureGameplay("BossRoom-Previous");
                Require(view.TryBeginFramingScope(room, 1.65f), "Room framing reapply failed.");
            }
            view.ResetZoom(); yield return new WaitForSeconds(.2f);
            Require(Mathf.Abs(view.EffectiveDistance - view.CurrentDistance * 1.65f) < .001f, "Zoom/reset stopped using scoped distance.");
            room.Restart(); room.Brain.ReviewMode = true; yield return null;
            Require(view.FramingDistanceScale > 1.64f, "Restart dropped room framing."); room.Exit(true); yield return null;
            Require(view.FramingDistanceScale == 1f && Mathf.Abs(view.CurrentDistance - beforeDistance) < .01f
                && Mathf.Abs(view.CurrentYaw - beforeYaw) < .01f && Mathf.Abs(view.CurrentPitch - beforePitch) < .01f, "Room exit failed to restore prior camera.");
            Record("actual-room-camera-entry-reset-restart-exit-" + run);
        }
    }
    static void Finish(string error)
    {
        if (plan == null) return;
        if (observedHealth != null && damageObserver != null) observedHealth.OnDamageResolved -= damageObserver; observedHealth = null; damageObserver = null;
        brain?.Dispose(); brain = null;
        foreach (var actor in leases) Release(actor); leases.Clear(); if (rt != null) rt.Release();
        File.WriteAllText(Path.Combine(plan.output, "result.json"), new JObject { ["status"] = error == null ? "PASS" : "FAIL", ["failure"] = error, ["cases"] = cases }.ToString());
        plan.phase = "returning"; SavePlan(); if (OwnPlay) EditorApplication.isPlaying = false;
    }
    static void Changed(PlayModeStateChange change) { if (plan != null && change == PlayModeStateChange.EnteredEditMode) { plan.phase = "returning"; SavePlan(); } }
    static void FinishReturn()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) return;
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if (!string.IsNullOrEmpty(env) && !string.Equals(env, Account, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(prepared) && !string.Equals(prepared, Account, StringComparison.OrdinalIgnoreCase)) return;
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.Clear(); routine = null; host = null; rt = null; pixels = null;
        Time.captureDeltaTime = plan.capture; Time.timeScale = plan.timeScale; Application.runInBackground = plan.background;
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(plan.previousStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart);
        IsolatedSavePlayGuard.UseRealAccount();
        File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = JToken.DeepEquals(plan.scenes, Scenes()) && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL",
            ["scenesBefore"] = plan.scenes, ["scenesAfter"] = Scenes(), ["guardChoice"] = IsolatedSavePlayGuard.RequiresAccountChoice, ["ownedObjects"] = owned.Count,
            ["active"] = IsolatedSavePlayGuard.ActiveDirectory, ["prepared"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""),
            ["expires"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), ["environment"] = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) }.ToString());
        SessionState.EraseString(Key + "plan"); plan = null;
    }
}
