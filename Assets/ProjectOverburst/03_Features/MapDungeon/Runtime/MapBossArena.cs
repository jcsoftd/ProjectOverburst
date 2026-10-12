using System;
using System.Collections.Generic;
using UnityEngine;
using Overburst.Mojave;
using UnityEngine.AI;

// Reserve the largest authored combat-tile tier, then the farthest reachable room in that tier.
public sealed class MapBossArena
{
    public MapCombatGeometry.Area Ground { get; private set; }
    public bool HasBoss => Settings != null;
    public static MojaveCombatSize TileSize(MapCombatGeometry.Area area)
        => area.Source.desert.catalog.patches[area.Source.place.patch].combatSize;
    public Vector3 BossPoint { get; private set; }
    public Vector3 PortalPoint { get; private set; }
    public Vector3 RewardPoint { get; private set; }
    public float WalkDistance { get; private set; }
    public CrustaspikanEncounterSettings Settings { get; private set; }
    public IReadOnlyList<Vector3> DodgePoints { get; private set; }

    public static MapBossArena Prepare(IReadOnlyList<MapCombatGeometry.Area> areas, Vector3 entry)
    {
        if (areas.Count < 2) throw new InvalidOperationException("사막에는 입구와 별도 완료 전장이 필요합니다.");
        int entrance = 0;
        for (int i = 1; i < areas.Count; i++)
            if ((areas[i].Center - entry).sqrMagnitude < (areas[entrance].Center - entry).sqrMagnitude) entrance = i;
        var preferred = MojaveCombatSize.Small;
        for (int i = 0; i < areas.Count; i++)
            if (i != entrance && TileSize(areas[i]) < preferred) preferred = TileSize(areas[i]);
        var path = new NavMeshPath(); int farthest = -1; float longest = -1;
        for (int i = 0; i < areas.Count; i++)
        {
            if (i == entrance || TileSize(areas[i]) != preferred) continue;
            if (!NavMesh.CalculatePath(entry, areas[i].Center, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("사막 보스 후보까지 걸어갈 수 없습니다: " + areas[i].Name);
            float length = 0; var corners = path.corners;
            for (int j = 1; j < corners.Length; j++) length += Vector3.Distance(corners[j - 1], corners[j]);
            if (length > longest) { longest = length; farthest = i; }
        }
        if (areas.Count < 2 || farthest < 0) throw new InvalidOperationException("사막에는 입구와 별도 보스 전장이 필요합니다.");
        var area = areas[farthest]; var overlaps = new Collider[64];
        var points = new List<Vector3>(area.Points);
        points.Sort((a, b) => (a - area.Center).sqrMagnitude.CompareTo((b - area.Center).sqrMagnitude));
        if (preferred == MojaveCombatSize.Small)
        {
            foreach (var near in points)
            {
                if (!MapCombatGeometry.TryCompletionPoint(area, near, 1f, overlaps, path, out var portal)) continue;
                foreach (var drop in points)
                {
                    float distance = Vector3.Distance(drop, portal);
                    if (distance < 1.5f || distance > 4f
                        || !MapCombatGeometry.TryCompletionPoint(area, drop, .35f, overlaps, path, out var reward)) continue;
                    return new MapBossArena { Ground = area, BossPoint = area.Center, PortalPoint = portal, RewardPoint = reward,
                        WalkDistance = longest, DodgePoints = Array.Empty<Vector3>() };
                }
            }
            throw new InvalidOperationException("소형 사막 완료 전장에 보상·귀환 공간을 확보하지 못했습니다: " + area.Name);
        }
        var settings = Resources.Load<CrustaspikanEncounterSettings>("Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan");
        if (!settings || !settings.Validate(out _) || settings.materials.actorDefinition.Grade.GradeType != EnemyGradeType.Boss
            || settings.entrance == null || !settings.entrance.enabled)
            throw new InvalidOperationException("사막 보스 전투 자산이 유효하지 않습니다.");
        var definition = settings.materials.actorDefinition; var body = MapCombatGeometry.BodyFor(definition);
        foreach (var center in points)
        {
            if (!MapCombatGeometry.CanPlace(area, definition, center)) continue;
            var dodge = new List<Vector3>(); int mask = 0;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                var desired = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (body.Radius + settings.evasionDistance);
                if (!MapCombatGeometry.TryCompletionPoint(area, desired, .5f, overlaps, path, out var point)) continue;
                if (!NavMesh.CalculatePath(center, point, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                dodge.Add(point); mask |= 1 << i;
            }
            if (dodge.Count < 4 || (mask & (mask >> 4)) == 0) continue;
            foreach (var near in points)
            {
                if (Vector3.Distance(near, center) < body.Radius + 2f
                    || !MapCombatGeometry.TryCompletionPoint(area, near, 1f, overlaps, path, out var portal)) continue;
                foreach (var drop in points)
                {
                    float distance = Vector3.Distance(drop, portal);
                    if (distance < 1.5f || distance > 4f || Vector3.Distance(drop, center) < body.Radius + 1f
                        || !MapCombatGeometry.TryCompletionPoint(area, drop, .35f, overlaps, path, out var reward)) continue;
                    return new MapBossArena { Ground = area, BossPoint = center, PortalPoint = portal, RewardPoint = reward,
                        WalkDistance = longest, Settings = settings, DodgePoints = dodge.AsReadOnly() };
                }
            }
        }
        throw new InvalidOperationException("선택한 크기의 사막 전장에 보스·회피·보상·귀환 공간을 확보하지 못했습니다: " + area.Name);
    }
}
