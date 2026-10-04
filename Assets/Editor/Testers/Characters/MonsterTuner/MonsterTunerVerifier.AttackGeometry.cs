using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    private const string AttackGeometryMenu = "OVERBURST/테스트/몬스터 튜너/공격 범위";
    [MenuItem(AttackGeometryMenu)] private static void AttackGeometryFromMenu() => AttackGeometry();
    [MenuItem(AttackGeometryMenu, true)] private static bool CanVerifyAttackGeometry() => AttackGeometryIdle;
    private static string AttackGeometryOutput => Path.Combine(MonsterTunerSession.OutputRoot, "20261005_AttackGeometry", "QA");
    private static bool AttackGeometryIdle => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
        && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer && !IsolatedSavePlayGuard.RequiresAccountChoice
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
        && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
        && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""));

    public static string AttackGeometry()
    {
        if (!AttackGeometryIdle) throw new InvalidOperationException("Shared Editor safe idle and account return required.");
        Directory.CreateDirectory(AttackGeometryOutput);
        var checks = new List<object>();
        void Test(string name, bool pass, string detail = "")
        { checks.Add(new { name, pass, detail }); if (!pass) throw new InvalidOperationException(name + ": " + detail); }
        string ScenesBefore = AttackGeometrySceneState();
        int stages = MonsterTunerPreviewStage.LiveStages, previews = EditorSceneManager.previewSceneCount;
        int textures = MonsterTunerWindow.LiveThumbnailTextures;
        float listWidth = EditorPrefs.GetFloat("Overburst.MonsterTuner.ListWidth", 240);
        float detailWidth = EditorPrefs.GetFloat("Overburst.MonsterTuner.DetailWidth", 360);
        var catalog = new MonsterTunerCatalog(); catalog.Refresh();
        var slots = catalog.Entries.Where(e => e.Definition.ActorPrefab != null && e.Definition.AbilitySet != null)
            .SelectMany(e => Enumerable.Range(0, e.Definition.AbilitySet.Count).Select(i => new { entry = e, index = i, ability = e.Definition.AbilitySet.GetAbility(i) }))
            .Where(s => s.ability != null).ToArray();
        var paths = catalog.Entries.SelectMany(e => new Object[] { e.Definition, e.Definition.ActorPrefab, e.Definition.Variant, e.Definition.AnimationProfile, e.Definition.AbilitySet })
            .Concat(slots.SelectMany(s => new Object[] { s.ability, s.ability.WeakAttackExecution }))
            .Where(o => o != null).Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
        var hashes = paths.ToDictionary(p => p, MonsterTunerStamp.FileHash);
        var originals = slots.SelectMany(s => new Object[] { s.ability, s.ability.WeakAttackExecution }).Where(o => o != null).Distinct()
            .ToDictionary(o => o, EditorJsonUtility.ToJson);
        var window = ScriptableObject.CreateInstance<MonsterTunerWindow>(); window.VerificationOnly = true;
        window.titleContent = new GUIContent("몬스터 튜너 공격 범위 검증"); window.position = new Rect(40, 40, 1500, 940); window.ShowUtility();
        double deadline = EditorApplication.timeSinceStartup + 120; int frames = 0;
        AssemblyReloadEvents.AssemblyReloadCallback cancel = null;
        cancel = () =>
        {
            EditorApplication.update -= Run; AssemblyReloadEvents.beforeAssemblyReload -= cancel;
            if (window != null) window.Close();
            EditorPrefs.SetFloat("Overburst.MonsterTuner.ListWidth", listWidth); EditorPrefs.SetFloat("Overburst.MonsterTuner.DetailWidth", detailWidth);
            File.WriteAllText(Path.Combine(AttackGeometryOutput, "verification.json"), "{\"success\":false,\"status\":\"DEFERRED_RELOAD\",\"error\":\"Verifier callback cancelled before reload.\"}");
        };
        AssemblyReloadEvents.beforeAssemblyReload += cancel;
        EditorApplication.update += Run;
        return "Attack geometry verification queued; report: " + Path.Combine(AttackGeometryOutput, "verification.json");
        void Run()
        {
            if (++frames < 3 && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= Run; AssemblyReloadEvents.beforeAssemblyReload -= cancel;
            bool success = false; string error = "";
            try
            {
                Test("native safe idle", AttackGeometryIdle && EditorApplication.timeSinceStartup < deadline);
                Test("actual catalog", catalog.Entries.Count > 0, catalog.Entries.Count + " definitions, " + slots.Length + " attack slots");
                var contactSlots = slots.Where(s => MonsterTunerWindow.UsesContactGeometry(s.ability)).ToArray();
                Test("V3 contact assets present", contactSlots.Length > 0, contactSlots.Length.ToString());
                foreach (var slot in contactSlots)
                {
                    var actor = slot.entry.Definition.ActorPrefab; var profile = slot.ability.WeakAttackExecution;
                    for (int phase = 0; phase < profile.ContactGeometryCount; phase++)
                    {
                        Test("contact window " + slot.ability.name + "/" + phase, profile.TryGetContactWindow(phase, out var range));
                        foreach (float time in new[] { range.x, (range.x + range.y) * .5f, range.y })
                        {
                            var segments = MonsterTunerWindow.AttackSegments(actor, slot.ability, time).ToArray();
                            var shapes = Enumerable.Range(0, profile.ContactGeometryCount).Select(profile.GetContactGeometry)
                                .Where(g => g != null).SelectMany(g => Enumerable.Range(0, g.CapsuleCount)
                                    .Select(i => g.TryEvaluateCapsule(i, time, out var capsule) ? (EnemyWeakAttackContactCapsule?)capsule : null))
                                .Where(c => c.HasValue).Select(c => c.Value).ToArray();
                            Test("capsule topology " + slot.ability.name + "/" + time, segments.Length == shapes.Length * 148);
                            bool surfacesMatch = true;
                            for (int shape = 0; shape < shapes.Length; shape++)
                            {
                                Vector3 a = actor.transform.position + actor.transform.rotation * shapes[shape].A;
                                Vector3 b = actor.transform.position + actor.transform.rotation * shapes[shape].B;
                                foreach (var edge in segments.Skip(shape * 148).Take(148))
                                    surfacesMatch &= Mathf.Abs(AttackGeometryDistance(edge.Item1, a, b) - shapes[shape].Radius) < .0002f
                                        && Mathf.Abs(AttackGeometryDistance(edge.Item2, a, b) - shapes[shape].Radius) < .0002f;
                            }
                            Test("runtime capsule surface " + slot.ability.name + "/" + time, surfacesMatch);
                        }
                    }
                    float before = Enumerable.Range(0, profile.ContactWindowCount).Select(i => { profile.TryGetContactWindow(i, out var r); return r.x; }).Min() - .001f;
                    float after = Enumerable.Range(0, profile.ContactWindowCount).Select(i => { profile.TryGetContactWindow(i, out var r); return r.y; }).Max() + .001f;
                    Test("outside contact frames hidden " + slot.ability.name,
                        !MonsterTunerWindow.AttackSegments(actor, slot.ability, before).Any() && !MonsterTunerWindow.AttackSegments(actor, slot.ability, after).Any());
                }

                var sectors = slots.Where(s => !MonsterTunerWindow.UsesBossMaterial(s.entry.Definition.ActorPrefab, s.ability)
                    && EnemyAttackThreatGeometry.ResolveSectorInnerRadius(s.entry.Definition.ActorPrefab, s.ability) > 0f).ToArray();
                Test("standard annular sectors present", sectors.Length > 0, sectors.Length.ToString());
                foreach (var slot in sectors)
                {
                    var actor = slot.entry.Definition.ActorPrefab;
                    Vector3 center = MonsterTunerWindow.AttackCenter(actor, slot.ability);
                    float inner = EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor, slot.ability);
                    float radius = EnemyAttackThreatGeometry.ResolveRadius(actor, slot.ability);
                    var edges = MonsterTunerWindow.AttackSegments(actor, slot.ability).ToArray();
                    Test("sector actor center " + slot.ability.name, Vector3.Distance(center, actor.transform.position) < .0001f);
                    Test("inner and outer arcs " + slot.ability.name, edges.Length == 98
                        && edges.SelectMany(e => new[] { e.Item1, e.Item2 }).All(p => Mathf.Min(Mathf.Abs(Vector3.Distance(p, center) - inner), Mathf.Abs(Vector3.Distance(p, center) - radius)) < .0002f));
                    Test("no filled apex " + slot.ability.name, edges.All(e => Vector3.Distance(e.Item1, center) >= inner - .0002f && Vector3.Distance(e.Item2, center) >= inner - .0002f));
                }
                foreach (var slot in slots.Where(s => !MonsterTunerWindow.UsesContactGeometry(s.ability)
                    && !MonsterTunerWindow.UsesBossMaterial(s.entry.Definition.ActorPrefab, s.ability)
                    && EnemyAbilityDefinition.IsMeleeExecution(s.ability.ExecutionMode)))
                {
                    int expected = slot.ability.ExecutionMode == EnemyAbilityExecutionMode.Charge ? 148
                        : EnemyAttackThreatGeometry.ResolveSectorInnerRadius(slot.entry.Definition.ActorPrefab, slot.ability) > 0f ? 98
                        : EnemyAttackThreatGeometry.ResolveHitAngle(slot.entry.Definition.ActorPrefab, slot.ability) < 359.9f ? 50 : 48;
                    Test("legacy circle arc charge " + slot.ability.name,
                        MonsterTunerWindow.AttackSegments(slot.entry.Definition.ActorPrefab, slot.ability).Count() == expected);
                }
                foreach (var slot in slots.Where(s => MonsterTunerWindow.UsesBossMaterial(s.entry.Definition.ActorPrefab, s.ability)))
                {
                    var copy = Object.Instantiate(slot.ability);
                    try
                    {
                        Test("boss custom execution excludes generic area " + slot.ability.name, !MonsterTunerWindow.AttackSegments(slot.entry.Definition.ActorPrefab, slot.ability).Any());
                        Test("boss working copy preserves executor selection " + slot.ability.name, MonsterTunerWindow.UsesBossMaterial(slot.entry.Definition.ActorPrefab, copy)
                            && !MonsterTunerWindow.AttackSegments(slot.entry.Definition.ActorPrefab, copy).Any());
                    }
                    finally { Object.DestroyImmediate(copy); }
                }

                var selected = sectors.First();
                window.GetType().GetMethod("SelectEntry", Private).Invoke(window, new object[] { selected.entry });
                Set(window, "abilityIndex", selected.index); Set(window, "tab", 3); Call(window, "BuildFields"); Call(window, "RefreshPoints");
                var session = Get<MonsterTunerSession>(window, "session"); var stage = Get<MonsterTunerPreviewStage>(window, "stage");
                var viewport = Get<MonsterTunerViewport>(window, "viewport"); var fields = Get<ScrollView>(window, "fields");
                var point = viewport.Points.Single(p => p.Key == "attack-volume"); viewport.Select(point);
                Test("actual point uses runtime origin", Vector3.Distance(point.World(), stage.Actor.transform.position) < .0001f);
                var source = selected.ability; float originalRadius = source.HitRadius;
                var radiusField = fields.Q("ability:" + selected.index + "|hitRadius").Q<FloatField>();
                Change(radiusField, originalRadius + .35f);
                var working = Get<EnemyAbilityDefinition>(window, "workingAbility");
                Test("Toolkit edits working copy", session.Dirty && Mathf.Abs(working.HitRadius - originalRadius - .35f) < .0001f && source.HitRadius == originalRadius);
                Test("summary refreshes after edit", fields.Q<Label>("attack-geometry-summary").text.Contains(EnemyAttackThreatGeometry.ResolveRadius(stage.Enemy, working).ToString("F2")));
                Test("point segments refresh after edit", point.Segments().Count() == 98);
                CapturePanel(window, Path.Combine(AttackGeometryOutput, "strong-sector.png"));
                session.Discard(); Call(window, "UndoRedo");

                var weak = contactSlots.First();
                window.GetType().GetMethod("SelectEntry", Private).Invoke(window, new object[] { weak.entry });
                Set(window, "abilityIndex", weak.index); Call(window, "BuildFields"); Call(window, "RefreshPoints");
                session = Get<MonsterTunerSession>(window, "session"); stage = Get<MonsterTunerPreviewStage>(window, "stage");
                fields = Get<ScrollView>(window, "fields"); viewport = Get<MonsterTunerViewport>(window, "viewport");
                foreach (string property in new[] { "hitRadius", "hitAngle", "verticalTolerance", "range" })
                    Test("V3 ignored field read-only " + property, !fields.Q("ability:" + weak.index + "|" + property).Q<FloatField>().enabledSelf);
                Test("V3 profile and windows shown", fields.Q<Label>("attack-geometry-summary").text.Contains("접촉 1")
                    && fields.Q<Label>("attack-geometry-summary").text.Contains(weak.ability.WeakAttackExecution.name));
                point = viewport.Points.Single(p => p.Key == "attack-volume"); viewport.Select(point);
                Test("weak contact selection opens attack fields", Get<int>(window, "tab") == 3
                    && fields.Q<Label>("attack-geometry-summary") != null);
                Test("pre-play snapshot first impact", point.Segments().Count() == MonsterTunerWindow.AttackSegments(stage.Enemy, weak.ability, weak.ability.HitNormalizedTime).Count());
                Call(window, "PreviewAttack"); stage.Playing = false;
                working = Get<EnemyAbilityDefinition>(window, "workingAbility");
                Test("preview uses V3 runtime contact clip", stage.Clip == working.WeakAttackExecution.RuntimeClip);
                weak.ability.WeakAttackExecution.TryGetContactWindow(0, out var firstWindow);
                float sample = (firstWindow.x + firstWindow.y) * .5f;
                stage.Sample(working.ResolveWindupDelay(stage.AttackSpeed) + working.ResolvePacedTime(sample, stage.AttackSpeed));
                Test("native sampling and contact use same normalized time", Mathf.Abs(stage.NormalizedTime - sample) < .00001f
                    && point.Segments().Count() == MonsterTunerWindow.AttackSegments(stage.Enemy, working, stage.NormalizedTime).Count());
                var basis = stage.Actor.transform;
                basis.SetPositionAndRotation(new Vector3(3, 1, -2), Quaternion.Euler(0, 73, 0)); basis.localScale = new Vector3(2, 3, .5f);
                var reference = point.Segments().ToArray(); basis.localScale = Vector3.one;
                Test("contact final metres avoid scale duplication", reference.SequenceEqual(point.Segments().ToArray()));
                AttackGeometryPhysics(working.WeakAttackExecution, sample, basis.position, basis.rotation, Test);
                basis.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Call(window, "RenderNow"); CapturePanel(window, Path.Combine(AttackGeometryOutput, "weak-contact.png"));
                float outside = Mathf.Max(0f, firstWindow.x - .01f);
                stage.Sample(working.ResolveWindupDelay(stage.AttackSpeed) + working.ResolvePacedTime(outside, stage.AttackSpeed));
                Test("scrub outside contact window hides volume", !point.Segments().Any());
                Test("preview does not create save edits", !session.Dirty);
                Test("source serialized memory preserved", originals.All(p => EditorJsonUtility.ToJson(p.Key) == p.Value));
                Test("source disk preserved", hashes.All(p => MonsterTunerStamp.FileHash(p.Key) == p.Value));
                success = true;
            }
            catch (Exception e) { error = e.ToString(); }
            finally
            {
                if (window != null) window.Close();
                EditorPrefs.SetFloat("Overburst.MonsterTuner.ListWidth", listWidth); EditorPrefs.SetFloat("Overburst.MonsterTuner.DetailWidth", detailWidth);
                try
                {
                    Test("owned preview stages released", MonsterTunerPreviewStage.LiveStages == stages && EditorSceneManager.previewSceneCount == previews);
                    Test("thumbnail textures released", MonsterTunerWindow.LiveThumbnailTextures == textures);
                    Test("user scenes and dirty state preserved", AttackGeometrySceneState() == ScenesBefore);
                    Test("account environment remains returned", AttackGeometryIdle);
                }
                catch (Exception e) { success = false; error += "\n" + e; }
                File.WriteAllText(Path.Combine(AttackGeometryOutput, "verification.json"), Newtonsoft.Json.JsonConvert.SerializeObject(
                    new { success, error, count = checks.Count, catalogCount = catalog.Entries.Count, abilitySlotCount = slots.Length, checks }, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }

    private static float AttackGeometryDistance(Vector3 point, Vector3 a, Vector3 b)
    { var delta = b - a; return Vector3.Distance(point, a + delta * (delta.sqrMagnitude < .000001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - a, delta) / delta.sqrMagnitude))); }
    private static string AttackGeometrySceneState() => string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount)
        .Select(SceneManager.GetSceneAt).Select(s => s.path + "|" + s.isDirty + "|" + s.rootCount));

    private static void AttackGeometryPhysics(EnemyWeakAttackExecutionProfile profile, float time, Vector3 position, Quaternion facing, Action<string, bool, string> test)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var inside = new GameObject("owned inside") { hideFlags = HideFlags.HideAndDontSave };
            var outside = new GameObject("owned outside") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(inside, scene); SceneManager.MoveGameObjectToScene(outside, scene);
            var hit = inside.AddComponent<SphereCollider>(); hit.radius = .01f;
            var miss = outside.AddComponent<SphereCollider>(); miss.radius = .01f;
            var buffer = new Collider[8]; var physics = scene.GetPhysicsScene();
            test("owned preview physics is isolated", physics.IsValid() && physics != Physics.defaultPhysicsScene, "");
            for (int phase = 0; phase < profile.ContactGeometryCount; phase++)
            {
                var geometry = profile.GetContactGeometry(phase); if (geometry == null) continue;
                for (int shape = 0; shape < geometry.CapsuleCount; shape++)
                {
                    if (!geometry.TryEvaluateCapsule(shape, time, out var capsule)) continue;
                    var a = position + facing * capsule.A; var b = position + facing * capsule.B;
                    var center = (a + b) * .5f;
                    var normal = Vector3.Cross(b - a, Vector3.up); if (normal.sqrMagnitude < .000001f) normal = Vector3.right;
                    inside.transform.position = center; outside.transform.position = center + normal.normalized * (capsule.Radius + .1f);
                    physics.Simulate(.001f);
                    int count = EnemyWeakAttackContactQuery.Overlap(physics, geometry, shape, time, position, facing, buffer, ~0, out var queryCenter);
                    test("actual isolated physics contact " + phase + "/" + shape, buffer.Take(count).Contains(hit) && !buffer.Take(count).Contains(miss)
                        && Vector3.Distance(queryCenter, center) < .0001f, count.ToString());
                }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
