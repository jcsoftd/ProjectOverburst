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
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Verifies real collision/damage at the impact boundary and ground-extracted elite without generating a temporary scene asset.
[InitializeOnLoad]
public static class CrustaspikanImpactExtractionVerifier
{
    const string Key = "Overburst.CrustaspikanImpactExtractionVerifier.plan";
    sealed class Plan { public string output, phase, previousStart; public JArray scenes; public bool background; public float capture, timeScale; public double deadline; }
    static Plan plan;
    static readonly List<Object> owned = new List<Object>();
    static readonly List<EnemyActor> leases = new List<EnemyActor>();
    static readonly JArray cases = new JArray();
    static EnemySpawnService service;
    static EnemyMotor host;
    static Coroutine routine;
    static CombatTarget previousPlayerTarget;
    static readonly Vector3 Origin = new Vector3(1600, .035f, 1600);
    static string Account => Path.Combine(plan.output, "Account");
    static bool OwnPlay => plan != null && string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);
    static JArray Scenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount }));
    static void Require(bool pass, string error) { if (!pass) throw new InvalidOperationException(error); }
    static void Persist() => SessionState.SetString(Key, JsonConvert.SerializeObject(plan));
    static CrustaspikanImpactExtractionVerifier()
    {
        string saved = SessionState.GetString(Key, ""); if (!string.IsNullOrEmpty(saved)) plan = JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update += Tick;
    }
    public static string Start(string output)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        Require(plan == null && !EditorUtility.scriptCompilationFailed, "A verifier is pending or compilation failed.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Require(!Directory.Exists(output), "Fresh private evidence directory required."); Directory.CreateDirectory(output);
        plan = new Plan { output = output, phase = "booting", scenes = Scenes(), previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            background = Application.runInBackground, capture = Time.captureDeltaTime, timeScale = Time.timeScale, deadline = EditorApplication.timeSinceStartup + 600 };
        cases.Clear(); Persist(); File.WriteAllText(Path.Combine(output, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        Application.runInBackground = true; IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return "Started isolated range collision and damage checks.";
    }
    static void Tick()
    {
        if (plan == null) return;
        if (plan.phase == "returning") { Return(); return; }
        if (EditorApplication.timeSinceStartup > plan.deadline) { Finish("Range verification timed out."); return; }
        if (plan.phase != "booting" || !EditorApplication.isPlaying || !OwnPlay) return;
        plan.phase = "running"; Persist(); previousPlayerTarget = EnemyStrongAttackWarning.PlayerTarget;
        var root = new GameObject("Owned boss range verifier"); owned.Add(root); host = root.AddComponent<EnemyMotor>();
        root.GetComponent<Rigidbody>().isKinematic = true; routine = host.StartCoroutine(Drive(Run()));
    }
    static IEnumerator Drive(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(body);
        while (stack.Count > 0)
        {
            bool more; object current;
            try { more = stack.Peek().MoveNext(); current = more ? stack.Peek().Current : null; }
            catch (Exception error) { foreach (var item in stack) (item as IDisposable)?.Dispose(); Finish(error.ToString()); yield break; }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        Finish(null);
    }
    static void Position(Transform target, Vector3 point) { target.position = point; Physics.SyncTransforms(); }
    static void Record(JObject row) { row["pass"] = true; cases.Add(row); Write("RUNNING", null); }
    static void Write(string status, string error) => File.WriteAllText(Path.Combine(plan.output, "result.json"), new JObject { ["status"] = status, ["failure"] = error, ["cases"] = cases }.ToString());
    static IEnumerator Run()
    {
        float wait = Time.realtimeSinceStartup + 120f;
        while (Time.realtimeSinceStartup < wait && (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout || PlayerContext.GetOrCreate().CurrentActor == null)) yield return null;
        Require(Time.realtimeSinceStartup < wait, "Isolated hideout boot timed out.");
        Require(string.Equals(Overburst.Persistence.AccountBootstrap.SaveDirectory, Account, StringComparison.OrdinalIgnoreCase), "Account differs.");
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        service = EnemySpawnService.Current;
        if (service == null)
        {
            var root = new GameObject("Owned range spawn service"); owned.Add(root);
            var inactive = new GameObject("Owned inactive actors"); inactive.transform.SetParent(root.transform, false); inactive.SetActive(false);
            var pool = root.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
            service = root.AddComponent<EnemySpawnService>(); service.Configure(collection.catalog, pool);
        }
        Require(service.RegisterAdditionalCatalog(collection.catalog, out var reason), reason);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Origin + Vector3.down * .535f; floor.transform.localScale = new Vector3(100, 1, 100);
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var physical = playerPrefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c => c.enabled && !c.isTrigger);
        var victim = new GameObject("Owned saved player physical body"); owned.Add(victim); victim.layer = playerPrefab.layer;
        var capsule = victim.AddComponent<CapsuleCollider>(); capsule.center = playerPrefab.transform.InverseTransformPoint(physical.transform.TransformPoint(physical.center));
        capsule.radius = physical.radius * Mathf.Max(Mathf.Abs(physical.transform.lossyScale.x), Mathf.Abs(physical.transform.lossyScale.z)); capsule.height = physical.height * Mathf.Abs(physical.transform.lossyScale.y);
        var target = victim.AddComponent<CombatTarget>(); target.Configure(CombatTeam.PlayerParty, false);
        var volume = playerPrefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero); target.ConfigureVolume(volume.Center, volume.Radius, volume.HalfHeight * 2);
        var health = victim.GetComponent<CombatHealth>(); health.SetMaxHp(100000, true); EnemyStrongAttackWarning.PlayerTarget = target;
        var oldRows = JArray.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(plan.output), "geometry-comparison.json"))).ToDictionary(r => (string)r["id"]);
        var probeWarning = typeof(CrustaspikanMaterialVerifier).GetMethod("ProbeWarning", BindingFlags.Static | BindingFlags.NonPublic);
        Time.timeScale = 1f; Time.captureDeltaTime = 0f;
        foreach (var material in collection.attacks.Where(m => m.delivery == EnemyBossMaterialDelivery.Melee && (bool)oldRows[m.runtimeClip.name]["changed"]))
        foreach (string scenario in new[] { "inside", "expanded-band", "outside" })
        {
            Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition, Origin, Quaternion.identity, target.transform, context: EncounterContext.Test), out var actor), "Saved actor spawn failed.");
            leases.Add(actor); actor.AI.enabled = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var c in actor.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(c, capsule);
            var executor = actor.GetComponent<EnemyBossMaterialExecutor>(); var damage = new JArray(); var warnings = new JArray();
            Action<CombatHealth, DamageInfo> observer = (_, info) => damage.Add(new JObject { ["phase"] = info.sourceAttackPhaseIndex, ["damage"] = info.damage });
            health.OnDamaged += observer;
            try
            {
                Action<int> place = phase =>
                {
                    var s = material.strikes[phase]; float distance = scenario == "inside" ? Mathf.Lerp(s.innerRadius, s.radius, .65f) : scenario == "expanded-band" ? s.radius - .02f : s.radius + capsule.radius + .3f;
                    Position(victim.transform, s.Origin(actor.transform) + s.Rotation(actor.transform) * Vector3.forward * distance);
                    Require(s.Intersects(capsule, actor.transform) == (scenario != "outside"), "Actual body geometry differs from scenario.");
                };
                place(0);
                var first = material.strikes[0];
                Position(victim.transform, first.Origin(actor.transform) + first.Rotation(actor.transform) * Vector3.forward * Mathf.Lerp(first.innerRadius, first.radius, .65f));
                yield return new WaitForFixedUpdate();
                Require(actor.AbilityController.TryStartAbility(material.ability, target.transform, EnemyAbilityStartContext.RearCounter(target.transform.position, actor.Movement.PhysicalRotation)), "Attack start failed: " + material.name);
                place(0);
                float deadline = Time.realtimeSinceStartup + material.runtimeClip.length * 3f + 10f;
                while (actor.AbilityController.IsExecuting && Time.realtimeSinceStartup < deadline)
                {
                    int phase = 0; while (phase < material.strikes.Length - 1 && executor.NormalizedTime > material.strikes[phase].contactEnd) phase++;
                    place(phase);
                    if (!warnings.Any(w => (int)w["phase"] == phase) && executor.NormalizedTime < material.strikes[phase].impact)
                        probeWarning.Invoke(null, new object[] { executor, material, phase, warnings });
                    yield return null;
                }
                Require(!actor.AbilityController.IsExecuting, "Attack deadline exceeded: " + material.name);
                Require(executor.ImpactCount == material.strikes.Length, "Actual strikes did not execute.");
                Require(warnings.Count == material.strikes.Length, "Native telegraph checks incomplete.");
                Require(damage.Count == (scenario != "outside" ? material.strikes.Length : 0), "Damage differs at the current impact boundary: " + material.name + " / " + scenario + " / " + damage.Count);
                Record(new JObject { ["attack"] = material.runtimeClip.name, ["scenario"] = scenario, ["strikes"] = material.strikes.Length, ["impacts"] = executor.ImpactCount, ["damage"] = damage, ["warnings"] = warnings });
            }
            finally { health.OnDamaged -= observer; if (actor.IsLeased) service.Release(actor); }
            yield return null;
        }
        yield return ThrowCases(collection, target, capsule, health);
    }
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static Transform Held(EnemyBossCompositePatternExecutor executor)
    {
        var visual = typeof(EnemyBossCompositePatternExecutor).GetField("held", Private).GetValue(executor);
        return visual == null ? null : ((GameObject)visual.GetType().GetField("root").GetValue(visual)).transform;
    }
    static Vector3 Hands(EnemyBossCompositePatternExecutor executor) => (Vector3)typeof(EnemyBossCompositePatternExecutor).GetMethod("Hands", Private).Invoke(executor, null);
    static Camera reviewCamera;
    static RenderTexture reviewSurface;
    static Texture2D reviewPixels;
    static void Capture(string group, int index, float time, JArray frames)
    {
        if (reviewCamera == null)
        {
            var root = new GameObject("Owned elite extraction review camera"); owned.Add(root); reviewCamera = root.AddComponent<Camera>();
            reviewCamera.enabled = false; reviewCamera.fieldOfView = 48; reviewCamera.nearClipPlane = .1f; reviewCamera.farClipPlane = 100f;
            reviewCamera.transform.position = Origin + new Vector3(18, 11, 15); reviewCamera.transform.LookAt(Origin + new Vector3(0, 3, 3));
            reviewCamera.clearFlags = CameraClearFlags.SolidColor; reviewCamera.backgroundColor = new Color(.08f, .09f, .1f);
            var lightRoot = new GameObject("Owned extraction key light"); owned.Add(lightRoot); var light = lightRoot.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(40, -30, 0);
            reviewSurface = new RenderTexture(960, 640, 24); reviewSurface.Create(); reviewPixels = new Texture2D(960, 640, TextureFormat.RGB24, false);
            Directory.CreateDirectory(Path.Combine(plan.output, "Frames"));
        }
        var previous = RenderTexture.active;
        try
        {
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(reviewCamera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = reviewSurface });
            RenderTexture.active = reviewSurface; reviewPixels.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); reviewPixels.Apply(false, false);
            string name = group + "-" + index.ToString("D4") + ".jpg"; File.WriteAllBytes(Path.Combine(plan.output, "Frames", name), reviewPixels.EncodeToJPG(88));
            frames.Add(new JObject { ["name"] = name, ["time"] = time });
        }
        finally { RenderTexture.active = previous; }
    }
    static IEnumerator ThrowCases(EnemyBossMaterialCollection collection, CombatTarget victim, CapsuleCollider capsule, CombatHealth health)
    {
        var material = collection.attacks.Single(m => m.runtimeClip.name == "ThrowRock");
        int group = 8100;
        foreach (var kind in new[] { EnemyBossThrowPayload.Rock, EnemyBossThrowPayload.Elite })
        foreach (string scenario in new[] { "inside", "expanded-band", "outside" })
        {
            Position(victim.transform, Origin + Vector3.forward * 15f);
            Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition, Origin, Quaternion.identity, victim.transform, context: EncounterContext.Test), out var actor), "Throw actor spawn failed.");
            leases.Add(actor); actor.AI.enabled = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var c in actor.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(c, capsule);
            var composite = actor.GetComponent<EnemyBossCompositePatternExecutor>(); var basic = actor.GetComponent<EnemyBossMaterialExecutor>();
            var samples = new JArray(); var frames = new JArray(); var damage = new JArray(); var warnings = new JArray();
            bool movie = scenario == "inside"; float start = Time.realtimeSinceStartup, nextFrame = start; int index = 0; float deadline = start + 40f;
            Action<CombatHealth, DamageInfo> observer = (_, info) => damage.Add(new JObject { ["damage"] = info.damage, ["knockdown"] = info.knocksDownPlayer });
            health.OnDamaged += observer;
            try
            {
                Require(composite.TryBeginPreparation(kind, ++group, 0, victim.transform), "Actual preparation rejected.");
                bool buried = false; int handSamples = 0;
                while (!composite.TryGetPreparedStartContext(out _) && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    if (Held(composite) is Transform body && composite.IsEliteHeld && actor.AnimationBridge.TryReadMotion(basic.PlaybackHandle, out var motion))
                    {
                        var ground = (Vector3)typeof(EnemyBossCompositePatternExecutor).GetField("extractionGround", Private).GetValue(composite);
                        Require(Vector3.Distance(body.localScale, Vector3.one * composite.Patterns.elite.visualScale) < .001f, "Extraction shrank or grew the elite body.");
                        buried |= body.position.y < ground.y;
                        samples.Add(new JObject { ["frame"] = motion.Normalized * motion.Clip.length * motion.Clip.frameRate, ["heightAboveGround"] = body.position.y - ground.y,
                            ["handDistance"] = Vector3.Distance(body.position, Hands(composite)), ["scale"] = body.localScale.x }); handSamples++;
                    }
                    if (movie && Time.realtimeSinceStartup >= nextFrame) { Capture(kind.ToString(), index++, Time.realtimeSinceStartup - start, frames); nextFrame = Time.realtimeSinceStartup + .1f; }
                }
                Require(composite.TryGetPreparedStartContext(out var context), "Preparation deadline exceeded.");
                var beforeLate = new JObject { ["rockHeld"] = basic.IsRockHeld, ["buriedObserved"] = buried, ["samples"] = handSamples,
                    ["handDistance"] = Held(composite) == null ? -1f : Vector3.Distance(Held(composite).position, Hands(composite)) };
                // ObserveOwnedSupport updates the basic rock in Update; the composite substitutes it in LateUpdate.
                // Judge the final rendered pose after that substitution, rather than the intermediate Update state.
                yield return new WaitForEndOfFrame();
                File.WriteAllText(Path.Combine(plan.output, kind + "-" + scenario + "-preparation.json"), new JObject { ["beforeLateUpdate"] = beforeLate,
                    ["renderedRockHeld"] = basic.IsRockHeld, ["renderedHandDistance"] = Held(composite) == null ? -1f : Vector3.Distance(Held(composite).position, Hands(composite)), ["samples"] = samples }.ToString());
                if (kind == EnemyBossThrowPayload.Elite)
                    Require(buried && handSamples > 10 && composite.IsEliteHeld && !basic.IsRockHeld && Vector3.Distance(Held(composite).position, Hands(composite)) < .02f,
                        "Elite did not move from ground into the hands.");
                Require(composite.ActiveFlightCount == 0 && composite.SummonedCount == 0, "Held elite became live before being thrown.");
                if (movie) Capture(kind.ToString(), index++, Time.realtimeSinceStartup - start, frames);
                Vector3 landing = context.AimPosition;
                Require(actor.AbilityController.TryStartAbility(material.ability, victim.transform, context), "Prepared throw handoff failed.");
                float distance = scenario == "inside" ? 2f : scenario == "expanded-band" ? material.strikes[0].radius - .02f : material.strikes[0].radius + capsule.radius + .3f;
                Position(victim.transform, landing + Vector3.right * distance);
                bool released = false; Vector3 releasePoint = default;
                while (actor.AbilityController.IsExecuting && Time.realtimeSinceStartup < deadline)
                {
                    if (!released && composite.ActiveFlightCount > 0)
                    {
                        var list = (IEnumerable)typeof(EnemyBossCompositePatternExecutor).GetField("flights", Private).GetValue(composite);
                        foreach (var flight in list) { releasePoint = (Vector3)flight.GetType().GetField("start").GetValue(flight); break; }
                        Require(Vector3.Distance(releasePoint, Hands(composite)) < .15f && releasePoint.y > Origin.y + 1f, "Throw did not depart from the lifted hand pose."); released = true;
                    }
                    if (composite.ReleaseCount == 0 && warnings.Count == 0 && composite.GetComponentsInChildren<ProceduralGroundIndicator>().Any())
                    {
                        var indicators = composite.GetComponentsInChildren<ProceduralGroundIndicator>();
                        foreach (var indicator in indicators) Require(Mathf.Abs(indicator.OuterRadius - material.strikes[0].radius) < .001f, "Throw warning differs from landing radius.");
                        warnings.Add(new JObject { ["radius"] = material.strikes[0].radius, ["count"] = indicators.Length });
                    }
                    if (movie && Time.realtimeSinceStartup >= nextFrame) { Capture(kind.ToString(), index++, Time.realtimeSinceStartup - start, frames); nextFrame = Time.realtimeSinceStartup + .1f; }
                    yield return null;
                }
                Require(!actor.AbilityController.IsExecuting && released && composite.LastFailure == null, "Throw did not finish cleanly: " + composite.LastFailure);
                Require(damage.Count == (scenario == "outside" ? 0 : 1) && damage.All(d => (bool)d["knockdown"] == (kind == EnemyBossThrowPayload.Elite)), "Landing damage or elite knockdown flag differs.");
                Require(composite.SummonedCount == (kind == EnemyBossThrowPayload.Elite ? 1 : 0), "Elite did not become a live monster on landing.");
                Require(warnings.Count > 0, "Throw telegraph not checked.");
                Record(new JObject { ["attack"] = "ThrowRock", ["payload"] = kind.ToString(), ["scenario"] = scenario, ["extractionSamples"] = samples,
                    ["releaseHeight"] = releasePoint.y - Origin.y, ["damage"] = damage, ["summoned"] = composite.SummonedCount, ["warnings"] = warnings });
                if (movie) File.WriteAllText(Path.Combine(plan.output, kind + "-frames.json"), frames.ToString());
            }
            finally { health.OnDamaged -= observer; if (actor.IsLeased) service.Release(actor); }
            yield return null;
        }
        foreach (string scenario in new[] { "cancel-during-dig", "cancel-while-held", "target-lost" })
        {
            Position(victim.transform, Origin + Vector3.forward * 15f);
            Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition, Origin + Vector3.right * 3, Quaternion.identity, victim.transform, context: EncounterContext.Test), out var actor), "Cancellation actor spawn failed.");
            leases.Add(actor); actor.AI.enabled = false; var composite = actor.GetComponent<EnemyBossCompositePatternExecutor>();
            try
            {
                Require(composite.TryBeginPreparation(EnemyBossThrowPayload.Elite, ++group, 0, victim.transform), "Pooled elite preparation rejected.");
                float deadline = Time.realtimeSinceStartup + 15;
                while ((scenario == "cancel-while-held" ? !composite.TryGetPreparedStartContext(out _) : !composite.IsEliteHeld) && Time.realtimeSinceStartup < deadline) yield return null;
                Require(Time.realtimeSinceStartup < deadline, "Cancellation checkpoint did not enter.");
                if (scenario == "target-lost") victim.gameObject.SetActive(false); else composite.Cancel();
                yield return null; yield return null;
                Require(!composite.HasPreparation && !composite.IsEliteHeld && composite.ActiveFlightCount == 0 && composite.ActiveVisualCount == 0 && !actor.Movement.IsActionLocked, "Cancelled extraction retained payload or ownership.");
                victim.gameObject.SetActive(true); Record(new JObject { ["attack"] = "EliteExtraction", ["scenario"] = scenario, ["payloadCleared"] = true, ["pooledLease"] = actor.LeaseVersion });
            }
            finally { victim.gameObject.SetActive(true); if (actor.IsLeased) service.Release(actor); }
            yield return null;
        }
    }
    static void Finish(string error)
    {
        if (plan == null) return;
        foreach (var actor in leases) if (actor != null && actor.IsLeased) service?.Release(actor); leases.Clear();
        EnemyStrongAttackWarning.PlayerTarget = previousPlayerTarget; Write(error == null ? "PASS" : "FAIL", error);
        if (reviewSurface != null) { reviewSurface.Release(); Object.DestroyImmediate(reviewSurface); reviewSurface = null; }
        if (reviewPixels != null) { Object.DestroyImmediate(reviewPixels); reviewPixels = null; }
        plan.phase = "returning"; Persist(); if (OwnPlay) EditorApplication.isPlaying = false;
    }
    static void Return()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) return;
        var prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""); var env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if (!string.IsNullOrEmpty(prepared) && !string.Equals(prepared, Account, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(env) && !string.Equals(env, Account, StringComparison.OrdinalIgnoreCase)) return;
        if (host != null && routine != null) host.StopCoroutine(routine);
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.Clear(); host = null; routine = null;
        Time.captureDeltaTime = plan.capture; Time.timeScale = plan.timeScale; Application.runInBackground = plan.background;
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(plan.previousStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart); IsolatedSavePlayGuard.UseRealAccount();
        File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = JToken.DeepEquals(plan.scenes, Scenes()) && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL", ["scenesBefore"] = plan.scenes, ["scenesAfter"] = Scenes(), ["guardChoice"] = IsolatedSavePlayGuard.RequiresAccountChoice,
            ["active"] = IsolatedSavePlayGuard.ActiveDirectory, ["prepared"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), ["expires"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), ["environment"] = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) }.ToString());
        SessionState.EraseString(Key); plan = null;
    }
}
