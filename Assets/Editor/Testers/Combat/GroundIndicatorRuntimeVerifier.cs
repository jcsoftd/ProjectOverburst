using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Catalog-connected warnings, physical hit boundaries, pool reuse and isolated-account return.
[InitializeOnLoad]
public static class GroundIndicatorRuntimeVerifier
{
    const string Key = "Overburst.GroundIndicatorRuntimeVerifier.";
    static readonly List<object> rows = new List<object>();
    static readonly List<string> errors = new List<string>();
    static GameObject owned;
    static double deadline;
    static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");

    static GroundIndicatorRuntimeVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetBool(Key + "return", false)) ScheduleReturn();
        if (SessionState.GetBool(Key + "pending", false) && EditorApplication.isPlaying)
        {
            SessionState.SetString(Key + "status", "FAIL: domain reload during verification");
            EditorApplication.delayCall += EditorApplication.ExitPlaymode;
        }
    }

    public static void Run(string output)
    {
        GroundIndicatorBuilder.RequireIdle();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || SessionState.GetBool(Key + "pending", false)) throw new InvalidOperationException("Editor is owned");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene")
            throw new InvalidOperationException("Open PersistentScene first");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", Path.GetFullPath(output));
        SessionState.SetString(Key + "status", "RUNNING");
        SessionState.SetBool(Key + "pending", true);
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount")); }
        catch { SessionState.SetBool(Key + "pending", false); ScheduleReturn(); throw; }
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            rows.Clear(); errors.Clear(); deadline = EditorApplication.timeSinceStartup + 180;
            SessionState.SetBool(Key + "background", Application.runInBackground);
            Application.runInBackground = true;
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (owned != null) Object.Destroy(owned);
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key + "pending", false);
            if (Status == "RUNNING") SessionState.SetString(Key + "status", "INTERRUPTED");
            ScheduleReturn();
        }
    }

    static void Log(string text, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text + "\n" + stack); }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play bootstrap timed out");
            if (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PlayerInputFacade.Current == null || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") return;
            EditorApplication.update -= Tick;
            Require(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Account isolation");
            CheckCatalog();
            Finish(errors.Count == 0 ? "PASS" : "FAIL: runtime logs");
        }
        catch (Exception ex) { errors.Add(ex.ToString()); Finish("FAIL"); }
    }

    static void Finish(string status)
    {
        SessionState.SetString(Key + "status", status);
        File.WriteAllText(Path.Combine(Output, "runtime.json"), JsonConvert.SerializeObject(new { status, attacks = rows, errors }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    static void ScheduleReturn()
    {
        SessionState.SetBool(Key + "return", true); deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update -= ReturnAccount; EditorApplication.update += ReturnAccount;
    }

    static void ReturnAccount()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (EditorApplication.timeSinceStartup >= deadline) DeferReturn("DEFERRED_EDITOR_BUSY");
            return;
        }
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        string active = IsolatedSavePlayGuard.ActiveDirectory;
        bool otherOwner = (!string.IsNullOrEmpty(current) && !current.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(active) && !active.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (otherOwner)
        {
            if (EditorApplication.timeSinceStartup < deadline) return;
            DeferReturn("DEFERRED_OTHER_OWNER");
            return;
        }
        EditorApplication.update -= ReturnAccount;
        IsolatedSavePlayGuard.UseRealAccount();
        SessionState.EraseBool(Key + "pending"); SessionState.EraseBool(Key + "return");
        bool ready = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""));
        File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(new { status = ready ? "PASS" : "FAIL", stopped = !EditorApplication.isPlaying, requiresAccountChoice = IsolatedSavePlayGuard.RequiresAccountChoice, active = IsolatedSavePlayGuard.ActiveDirectory, environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), pending = SessionState.GetBool(Key + "pending", false), returning = SessionState.GetBool(Key + "return", false), startScene = "unchanged" }, Formatting.Indented));
    }

    static void DeferReturn(string status)
    {
        EditorApplication.update -= ReturnAccount;
        File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(new { status }));
    }

    static void CheckCatalog()
    {
        Require(EnemyAttackThreatGeometry.UsesStandardAttackAreas, "Standard flag disabled");
        var catalogs = new[] { Resources.Load<EnemyCatalog>("Enemies/Themes/Catalog"), Resources.Load<EnemyCatalog>("Enemies/Bosses/CavernUrsacetusKing/EC_Boss_UrsKing") };
        Require(catalogs.All(c => c != null), "Catalog missing");
        var defs = catalogs.SelectMany(c => Enumerable.Range(0, c.Count).Select(c.GetDefinition)).Distinct().ToArray();
        owned = new GameObject("Ground indicator verification (owned)");
        var poolRoot = new GameObject("Inactive pool"); poolRoot.transform.SetParent(owned.transform); poolRoot.SetActive(false);
        var pool = owned.AddComponent<EnemyPoolService>(); pool.Configure(poolRoot.transform, 0);
        var service = owned.AddComponent<EnemySpawnService>(); service.Configure(catalogs[0], pool);
        Require(service.RegisterAdditionalCatalog(catalogs[1], out string message), message);
        var targetObject = new GameObject("Hit boundary probe"); targetObject.transform.SetParent(owned.transform);
        targetObject.layer = PlayerInputFacade.Current.gameObject.layer;
        var probeCollider = targetObject.AddComponent<SphereCollider>(); probeCollider.radius = .03f;
        targetObject.AddComponent<CombatHealth>();
        var target = CombatTarget.EnsureConfigured(targetObject, CombatTeam.PlayerParty, false);
        target.ConfigureVolume(Vector3.zero, .03f, .1f); target.DamageReceiver.SetMaxHp(100000, true);
        Vector3 at = new Vector3(3000, 100, 3000);
        var seen = new HashSet<string>();
        int sector = 0, circle = 0, charge = 0, species = 0;
        foreach (var def in defs)
        {
            var sets = new List<EnemyAbilitySet> { def.AbilitySet };
            var boss = def.ActorPrefab.GetComponent<EnemyBossPhaseController>()?.BossDefinition;
            if (boss != null) for (int i = 0; i < boss.PhaseCount; i++) sets.Add(boss.GetPhase(i).AbilitySet);
            var profile = def.ActorPrefab.GetComponent<EnemyBossCombatDirector>()?.Profile;
            var danger = profile != null ? profile.patterns.Where(p => p != null && p.unparryableDangerCue).Select(p => p.ability).ToArray() : Array.Empty<EnemyAbilityDefinition>();
            var abilities = sets.Where(s => s != null).SelectMany(s => Enumerable.Range(0, s.Count).Select(s.GetAbility)).Where(a => a != null && a.IsMeleeStrongAttack).Concat(danger).Distinct().ToArray();
            if (abilities.Length == 0) continue;
            Require(service.TrySpawn(new EnemySpawnRequest(def, PlayerInputFacade.Current.transform.position, Quaternion.identity, null, null, null, owned.transform), out var actor), "Spawn failed: " + def.EnemyId);
            try
            {
                actor.AI.enabled = false;
                foreach (var agent in actor.GetComponentsInChildren<NavMeshAgent>(true)) agent.enabled = false;
                actor.Movement.enabled = false; actor.AbilityController.enabled = false;
                actor.transform.SetPositionAndRotation(at, Quaternion.identity);
                actor.Health.SetMaxHp(100000, true);
                var warning = actor.GetComponent<EnemyStrongAttackWarning>() ?? actor.gameObject.AddComponent<EnemyStrongAttackWarning>();
                species++;
                foreach (var ability in abilities)
                {
                    if (!seen.Add(ability.AbilityId)) continue;
                    float radius = EnemyAttackThreatGeometry.ResolveRadius(actor, ability), angle = EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability);
                    float inner = EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor, ability);
                    bool corridor = ability.ExecutionMode == EnemyAbilityExecutionMode.Charge;
                    var shape = corridor ? GroundIndicatorShape.Rectangle : angle >= 359.9f ? GroundIndicatorShape.Circle : GroundIndicatorShape.Sector;
                    warning.Show(radius, false, angle, corridor, true, 1, EnemyAttackThreatGeometry.ChargeHalfWidth, inner);
                    if (danger.Contains(ability))
                    {
                        warning.Hide();
                        var director = actor.GetComponent<EnemyBossCombatDirector>();
                        typeof(EnemyBossCombatDirector).GetMethod("ShowDangerCue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(director, new object[] { ability });
                        warning = (EnemyStrongAttackWarning)typeof(EnemyBossCombatDirector).GetField("dangerCue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director);
                    }
                    warning.SetCenter(EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability, actor.Melee.AttackPoint.position));
                    warning.SetRemaining(.05f, false);
                    var p = actor.GetComponentInChildren<ProceduralGroundIndicator>();
                    Require(warning.UsesStandardIndicator && p != null && p.Shape == shape && p.UsesApprovedDesign, "Wrong active indicator: " + ability.AbilityId);
                    Require(Mathf.Abs(p.OuterRadius - radius) < .0001f && Mathf.Abs(p.InnerRadius - inner) < .0001f, "Warning dimensions");
                    if (shape == GroundIndicatorShape.Sector)
                    {
                        sector++; Require(inner > 0, "Acute apex remains");
                        Require(inner >= actor.GetComponent<CombatTarget>().CurrentVolume.Radius + .079f, "Monster stands on the near edge");
                    }
                    else if (corridor) charge++; else circle++;
                    var center = EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability, actor.Melee.AttackPoint.position);
                    int probes = 0;
                    if (!corridor)
                    {
                        var points = new List<(Vector3 position, bool hit)> { (center + Vector3.forward * (inner + (radius - inner) * .55f), true), (center + Vector3.forward * (radius + .25f), false) };
                        if (inner > 0) points.Add((center + Vector3.forward * (inner * .5f), false));
                        if (angle < 359.9f) points.Add((center + Quaternion.Euler(0, angle * .5f + 10, 0) * Vector3.forward * (inner + (radius-inner)*.5f), false));
                        foreach (var point in points)
                        {
                            targetObject.transform.position = point.position; Physics.SyncTransforms();
                            Require(EnemyAttackThreatGeometry.WouldHit(actor, ability, target) == point.hit, "Parry preview boundary: " + ability.AbilityId);
                            float before = target.DamageReceiver.CurrentHp;
                            typeof(EnemyMeleeAttackController).GetMethod("ResolveArcHit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(actor.Melee, new object[] { 10f, radius, angle, ability });
                            Require((target.DamageReceiver.CurrentHp < before) == point.hit, "Live damage boundary: " + ability.AbilityId); probes++;
                        }
                    }
                    else
                    {
                        var executor = actor.GetComponent<EnemyThemeSpecialExecutor>();
                        Require(executor != null, "Charge executor missing");
                        typeof(EnemyThemeSpecialExecutor).GetField("chargeDirection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(executor, Vector3.forward);
                        var points = new[] { (at + Vector3.up * .8f + Vector3.forward * radius * .5f, true), (at + Vector3.up * .8f + Vector3.forward * radius * .5f + Vector3.right * .65f, false), (at + Vector3.up * .8f + Vector3.forward * (radius + .65f), false) };
                        foreach (var point in points)
                        {
                            targetObject.transform.position = point.Item1; Physics.SyncTransforms();
                            Require(executor.WouldChargeHit(ability, target) == point.Item2, "Charge preview boundary: " + ability.AbilityId);
                            float before = target.DamageReceiver.CurrentHp;
                            typeof(EnemyThemeSpecialExecutor).GetMethod("ResolveChargeHit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(executor, new object[] { ability, Vector3.forward });
                            Require((target.DamageReceiver.CurrentHp < before) == point.Item2, "Charge damage boundary: " + ability.AbilityId); probes++;
                        }
                    }
                    targetObject.transform.position = at + Vector3.right * 30;
                    for (int i = 0; i < 3; i++)
                    {
                        warning.Hide(); Require(!warning.IsVisible, "Hide failed");
                        warning.Show(radius, false, angle, corridor, true, 1, EnemyAttackThreatGeometry.ChargeHalfWidth, inner);
                        warning.SetRemaining(.05f, false); Require(warning.UsesStandardIndicator, "Reentry failed");
                    }
                    rows.Add(new { enemy = def.EnemyId, ability = ability.AbilityId, shape = shape.ToString(), outer = radius, inner, angle, width = corridor ? .8f : 0f, endCap = corridor ? .4f : 0f, bodyRadius = actor.GetComponent<CombatTarget>().CurrentVolume.Radius, physicsProbes = probes, reentries = 3 });
                    if (shape == GroundIndicatorShape.Sector && sector == 1 || shape == GroundIndicatorShape.Circle && circle == 1 || corridor && charge == 1)
                        Capture(actor, radius, ability.AbilityId);
                    warning.Hide();
                }
            }
            finally { service.Release(actor); }
        }
        Require(seen.Count == 22 && species == 17 && sector == 11 && circle == 7 && charge == 4, "Catalog warning totals changed");
        Require(pool.LeasedCount == 0, "Actor lease leak");
        Object.Destroy(owned); owned = null;
    }

    static void Capture(EnemyActor actor, float radius, string name)
    {
        var g = new GameObject("Indicator capture camera"); g.transform.SetParent(owned.transform);
        var camera = g.AddComponent<Camera>(); camera.GetUniversalAdditionalCameraData();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.028f,.034f,.04f);
        camera.orthographic = true; camera.orthographicSize = radius * 1.25f;
        var center = actor.transform.position + Vector3.forward * radius * .45f;
        camera.transform.position = center + new Vector3(0, 14, -10);
        camera.transform.LookAt(center); camera.nearClipPlane = .05f; camera.farClipPlane = 100;
        var texture = new RenderTexture(960, 720, 24); var png = new Texture2D(960,720,TextureFormat.RGB24,false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            png.ReadPixels(new Rect(0,0,960,720),0,0); png.Apply(); File.WriteAllBytes(Path.Combine(Output,name+".png"),png.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; texture.Release(); Object.Destroy(texture); Object.Destroy(png); Object.Destroy(g); }
    }

    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
