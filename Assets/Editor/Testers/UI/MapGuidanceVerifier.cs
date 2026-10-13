using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

public static class MapGuidanceVerifier
{
    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required");
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output)) throw new ArgumentException("Fresh owned output required");
        Directory.CreateDirectory(output); var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        try
        {
            var source = new MinimapGuideSource();
            using (var map = Grid(true))
            {
                var start = new Vector3(2.5f, 0, 3.5f); var target = new Vector3(6.5f, 0, 3.5f);
                Check(!source.TryFrontier(map, start, target, out _, out _), "Unknown origin cannot emit guidance");
                map.Reveal(start, 2); int before = map.RevealedCount;
                Check(source.TryFrontier(map, start, target, out var boundary, out var direction), "Connected hidden target has a frontier");
                Check(map.IsRevealed(boundary) && !map.IsRevealed(boundary + direction), "Only known boundary and one direction are emitted");
                Check(direction.z < 0 && direction.x == 0, "Route goes south around a wall instead of pointing straight through it");
                Check(map.RevealedCount == before && map.TerrainTexture == null, "Guidance does not reveal or build map textures");
                map.Reveal(target, 1);
                Check(source.TryFrontier(map, start, target, out var skipped, out _) && skipped == boundary, "Teleport island does not hide the unknown gap");
                map.Reveal(start, 100);
                Check(!source.TryFrontier(map, start, target, out _, out _), "Fully known route has no false frontier");
                Check(!source.TryFrontier(map, start, new Vector3(4.5f, 0, 3.5f), out _, out _), "Wall target is rejected");
            }
            using (var map = Grid(false))
            {
                var start = new Vector3(2.5f, 0, 3.5f); map.Reveal(start, 2);
                Check(!source.TryFrontier(map, start, new Vector3(6.5f, 0, 3.5f), out _, out _), "Unconnected tile cannot get a direction through a wall");
            }
            source.Clear(); Check(source.Count == 0 && source.Remaining == 0 && !source.Portal, "Run guidance clears without a scene subscription");
            Vector2 diagonal = MinimapGuideView.Clamp(new Vector2(300, 400), new Vector2(100, 100), true);
            Check(Mathf.Abs(diagonal.magnitude - 100) < .001f && (diagonal.normalized - new Vector2(.6f, .8f)).sqrMagnitude < .001f, "Circle edge preserves objective direction");
            Check(MinimapGuideView.Clamp(new Vector2(300, 400), new Vector2(200, 100), false) == new Vector2(75, 100), "Wide overlay clips along the direction, not independently on each axis");
            Check(MinimapGuideView.Clamp(new Vector2(20, 30), new Vector2(200, 100), false) == new Vector2(20, 30), "Inside objective stays at its projected position");
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(MapOverlayBuilder.MinimapPath);
            var controller = root.GetComponent<WorldMinimapController>();
            Check(controller.View.Guides && controller.View.Guides.IsReady && controller.Overlay.guides && controller.Overlay.guides.IsReady, "Both authored guide views are connected");
            Check(controller.View.Guides.transform.GetSiblingIndex() < controller.View.Guides.transform.parent.Find("PlayerMarker").GetSiblingIndex()
                && controller.Overlay.guides.transform.GetSiblingIndex() < controller.Overlay.playerMarker.GetSiblingIndex(), "Player arrows draw above objective hints");
            Check(root.GetComponentsInChildren<MinimapMarkerGraphic>(true).Length == 6 && root.GetComponentsInChildren<Camera>(true).Length == 0, "Six batched graphics, no map camera");
            var player = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectOverburst/Resources/UI/Minimap/Player.png");
            foreach (var view in new[] { controller.View.Guides, controller.Overlay.guides })
                Check(view.icons.Atlas == controller.View.Markers.Atlas && view.arrows.Atlas == player
                    && !view.icons.raycastTarget && !view.arrows.raycastTarget, "Existing atlas/arrow textures and no raycast: " + view.transform.parent.name);
            Check(root.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "No missing scripts");
            string path = Path.Combine(output, "native-result.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = "PASS", checks }, Formatting.Indented)); return path;
        }
        catch (Exception error)
        { File.WriteAllText(Path.Combine(output, "native-result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = error.ToString() }, Formatting.Indented)); throw; }
    }
    static MinimapExplorationMap Grid(bool connected)
    {
        const int width = 9, height = 5; var cells = new byte[width * height]; var links = new byte[cells.Length];
        for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            if (x != 4 || connected && z == 0) cells[z * width + x] = MinimapTerrainPixels.Ground;
        for (int p = 0; p < cells.Length; p++)
        {
            if (cells[p] == 0) continue;
            if (p % width < width - 1 && cells[p + 1] != 0) links[p] |= MinimapExplorationMap.East;
            if (p % width > 0 && cells[p - 1] != 0) links[p] |= MinimapExplorationMap.West;
            if (p + width < cells.Length && cells[p + width] != 0) links[p] |= MinimapExplorationMap.North;
            if (p >= width && cells[p - width] != 0) links[p] |= MinimapExplorationMap.South;
        }
        return new MinimapExplorationMap(width, height, 1, Vector2.zero, cells, links);
    }
    static IEnumerator Frames(int n) { while (n-- > 0) yield return null; }
    static IEnumerator Wait(Func<bool> ready, Action<bool, string> check, string label, float seconds = 30)
    {
        double until = EditorApplication.timeSinceStartup + seconds;
        while (!ready() && EditorApplication.timeSinceStartup < until) yield return null;
        check(ready(), label);
    }
    static MinimapGuideSource.Hint[] Hints(MinimapGuideSource source) => Enumerable.Range(0, source.Count).Select(source.Get).ToArray();
    static void CheckViews(WorldMinimapController controller, Action<bool, string> check, string label)
    {
        foreach (var view in new[] {controller.View.Guides, controller.Overlay.guides})
            check(view.icons.MarkerCount + view.arrows.MarkerCount == controller.Guides.Count, label + ": same guide count in " + view.transform.parent.name);
    }
    static IEnumerator KillTo(RunWorldGate gate, int target, Action<bool, string> check)
    {
        var roster = gate.Roster; var player = PlayerContext.Instance.CurrentActor;
        double until = EditorApplication.timeSinceStartup + 160, movedAt = 0; int destination = 0;
        while (roster.Kills < target && EditorApplication.timeSinceStartup < until)
        {
            foreach (var member in roster.Members)
            {
                if (roster.Kills >= target) break;
                if (gate.SpawnBudget.Fields[member.AreaIndex].TryGetActor(member.Index, out var enemy))
                    enemy.Health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, player.gameObject, suppressDefaultHitVfx: true));
            }
            if (roster.Kills < target && EditorApplication.timeSinceStartup >= movedAt)
            {
                var area = roster.Areas[destination++ % roster.Areas.Count];
                ActorTeleportUtility.TeleportSafely(player.transform, area.Ground.Center + Vector3.up * .12f, Quaternion.identity);
                movedAt = EditorApplication.timeSinceStartup + 2;
            }
            yield return Frames(4);
        }
        check(roster.Kills == target, "Real actor deaths reach guidance checkpoint " + target);
    }
    public static IEnumerator VerifyPlay(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var controller = WorldMinimapController.Instance; var player = PlayerContext.Instance.CurrentActor;
        var flow = PersistentSceneFlow.Instance; var driver = flow.GetComponent<RunLifetimeDriver>();
        var completion = gate.Completion; var roster = gate.Roster;
        string label = gate.Map.terrain.ToString(); float zoom = controller.ZoomRadius;
        yield return Wait(() => controller.Exploration != null, check, "Exploration is ready for guidance");
        int builds = controller.TerrainBuildCount;
        check(controller.Guides.Count == 0, label + ": no premature objective or portal");
        if (gate.Map.terrain == MapTerrainKind.Cave)
        {
            foreach (int remaining in new[] { 11, 10, 5 })
            {
                yield return KillTo(gate, roster.Target - remaining, check); yield return Frames(10);
                controller.SetOverlay(true); yield return Frames(6);
                var hints = Hints(controller.Guides);
                check(controller.Guides.Remaining == remaining, "Guidance uses target remaining, not the full population: " + remaining);
                check(remaining == 11 ? hints.Length == 0 : hints.Length > 0, "11/10/5 guidance threshold: " + remaining);
                check(hints.All(h => controller.Exploration.IsRevealed(h.Position)), "All objective positions are on explored floor: " + remaining);
                check(hints.Where(h => h.Type == MinimapGuideSource.Kind.Approach).All(h => !roster.Areas[h.Id].Cleared),
                    "Pending spawn hints exclude cleared platforms: " + remaining);
                check(hints.Where(h => h.Type == MinimapGuideSource.Kind.Area).All(h => !roster.Areas[h.Id].Cleared
                    || roster.Members.Skip(roster.Areas[h.Id].FirstMember).Take(roster.Areas[h.Id].Count).Any(m => m.State == MapRunRoster.Life.Active)),
                    "Cleared region hints require an already living actor: " + remaining);
                check(hints.Where(h => h.Type == MinimapGuideSource.Kind.Frontier).All(h => !controller.Exploration.IsRevealed(h.Position + h.Direction)), "Frontier arrows stop before unknown floor: " + remaining);
                if (remaining == 10) check(hints.All(h => h.Type == MinimapGuideSource.Kind.Area || h.Type == MinimapGuideSource.Kind.Frontier), "Ten remaining shows regions and known frontiers only");
                if (remaining == 5)
                {
                    check(hints.Count(h => h.Type == MinimapGuideSource.Kind.Enemy) <= remaining, "Five remaining has at most five precise targets");
                    check(hints.Where(h => h.Type == MinimapGuideSource.Kind.Enemy).All(h => MinimapProjection.DistanceSquared(h.Position, player.transform.position) <= controller.ZoomRadius * controller.ZoomRadius), "Precise target guidance cannot extend normal detection range");
                    controller.SetZoom(20); yield return Frames(6);
                    check(Hints(controller.Guides).Where(h => h.Type == MinimapGuideSource.Kind.Enemy).All(h => MinimapProjection.DistanceSquared(h.Position, player.transform.position) <= 400), "Zooming to 20m removes precise distant targets");
                    controller.SetZoom(zoom); yield return Frames(6);
                }
                CheckViews(controller, check, "Remaining " + remaining);
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "guidance-remaining-" + remaining + ".png")); yield return Frames(4);
            }
            yield return KillTo(gate, roster.Target, check);
        }
        else if (!gate.BossArena.HasBoss)
        {
            check(!completion.Boss, "Small-only desert has no boss encounter");
            ActorTeleportUtility.TeleportSafely(player.transform, gate.BossArena.PortalPoint + Vector3.up * .12f, Quaternion.identity);
        }
        else
        {
            var boss = completion.Boss;
            check(boss && boss.Brain == null, "Desert has no boss or portal hint before room entry");
            ActorTeleportUtility.TeleportSafely(player.transform, gate.BossArena.DodgePoints[0] + Vector3.up * .12f, Quaternion.identity);
            yield return Wait(() => boss.IsIntroducing, check, "Arena entry starts intro for portal verification");
            boss.EntranceCinematic.Skip();
            yield return Wait(() => !boss.IsIntroducing && !boss.Brain.Actor.Health.IsDamageImmune, check, "Boss intro releases combat");
            var actor = boss.Brain.Actor;
            actor.Health.TakeDamage(new DamageInfo(1000000, actor.transform.position, player.gameObject, suppressDefaultHitVfx: true));
            check(boss.Defeated, "Real boss death starts completion");
        }
        yield return Wait(() => completion.IsCompleted && completion.Portal && completion.Portal.gameObject.activeInHierarchy, check, label + ": completion creates the real portal");
        yield return Frames(3); controller.SetOverlay(true); yield return Frames(4);
        check(controller.Guides.Count == 1 && controller.Guides.Get(0).Type == MinimapGuideSource.Kind.Portal
            && controller.Guides.Get(0).Position == completion.Portal.transform.position, label + ": exactly one current portal replaces required-target hints");
        CheckViews(controller, check, label + " portal");
        var marker = controller.View.Guides.icons.GetMarker(0);
        var expected = MinimapProjection.Project(completion.Portal.transform.position, player.transform.position, QuarterViewCamera.ActiveInstance.CurrentYaw, controller.ZoomRadius, controller.View.Radius);
        if (controller.View.Guides.icons.MarkerCount > 0) check((marker.Position - expected).sqrMagnitude < .1f, label + ": portal icon uses correct world-to-map scale");
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "guidance-portal-" + label + ".png")); yield return Frames(4);
        var far = roster.Areas.OrderByDescending(a => (a.Ground.Center - completion.Portal.transform.position).sqrMagnitude).First();
        ActorTeleportUtility.TeleportSafely(player.transform, far.Ground.Center + Vector3.up * .12f, Quaternion.identity);
        controller.SetZoom(20); yield return Frames(10);
        check(controller.View.Guides.arrows.MarkerCount == 1, label + ": off-screen return portal has a direction arrow");
        check(controller.View.Guides.arrows.GetMarker(0).Position.magnitude <= controller.View.Radius - 9.9f, label + ": portal arrow stays inside the circular mask");
        check(controller.TerrainBuildCount == builds && controller.Guides.Count == 1, label + ": completion and zoom keep topology and clear guidance fixed");
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "guidance-portal-far-" + label + ".png")); yield return Frames(4);
        controller.SetZoom(zoom);
        check(driver.RequestPortalExit(), label + ": manual return accepted");
        yield return Wait(() => WorldSessionState.IsHideout && !flow.IsSwitching, check, label + ": return reaches the hub");
        check(AccountGameplaySession.Current.ReadRun().phase == RunPhase.Extracted && controller.Guides.Count == 0 && controller.Exploration == null,
            label + ": extraction releases all run guidance and exploration");
    }
}
