using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DunGen;
using UnityEditor;
using UnityEngine;

public static class DungeonWalkableBandAuditTool
{
    private const string TileFolder =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/"
        + "MultistoryDungeons2/Tiles";
    private const string ReportPath =
        "Logs/DungeonWalkableBandAudit.csv";
    private const string DetailReportPath =
        "Logs/DungeonWalkableSurfaceDetails.csv";

    public static void RunFromCommandLine()
    {
        try
        {
            Directory.CreateDirectory("Logs");
            List<string> rows = new()
            {
                "Tile,DoorwayCount,DoorwayY,GroundCount,"
                + "GroundTopY,GroundBelowLowestDoorCount,"
                + "GroundBelowLowestDoorY,RampCount,RampY"
            };
            List<string> detailRows = new()
            {
                "Tile,ObjectPath,ColliderType,TopY,CenterY,SizeY,"
                + "IsRamp"
            };

            string[] guids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { TileFolder });
            Array.Sort(guids, StringComparer.Ordinal);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    rows.Add(BuildRow(root));
                    AppendDetailRows(root, detailRows);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            File.WriteAllLines(ReportPath, rows);
            File.WriteAllLines(DetailReportPath, detailRows);
            Debug.Log(
                $"[DungeonWalkableBandAuditTool] PASS: "
                + $"{guids.Length}개 타일");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void AppendDetailRows(
        GameObject root,
        List<string> rows)
    {
        float rootY = root.transform.position.y;
        int groundLayer = LayerMask.NameToLayer("Ground");
        Collider[] colliders = root
            .GetComponentsInChildren<Collider>(true)
            .Where(collider =>
                collider != null
                && collider.enabled
                && !collider.isTrigger
                && collider.gameObject.layer == groundLayer)
            .OrderBy(collider => collider.bounds.max.y)
            .ThenBy(collider => GetObjectPath(root.transform, collider.transform))
            .ToArray();

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            rows.Add(string.Join(
                ",",
                Escape(root.name),
                Escape(GetObjectPath(root.transform, collider.transform)),
                Escape(collider.GetType().Name),
                FormatNumber(collider.bounds.max.y - rootY),
                FormatNumber(collider.bounds.center.y - rootY),
                FormatNumber(collider.bounds.size.y),
                collider.GetComponent<DungeonStairRampProxy>() != null
                    ? "1"
                    : "0"));
        }
    }

    private static string GetObjectPath(
        Transform root,
        Transform target)
    {
        Stack<string> names = new();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static string BuildRow(GameObject root)
    {
        float rootY = root.transform.position.y;
        float[] doorwayY = root
            .GetComponentsInChildren<Doorway>(true)
            .Select(doorway =>
                doorway.transform.position.y - rootY)
            .OrderBy(value => value)
            .ToArray();

        int groundLayer = LayerMask.NameToLayer("Ground");
        Collider[] ground = root
            .GetComponentsInChildren<Collider>(true)
            .Where(collider =>
                collider != null
                && collider.enabled
                && !collider.isTrigger
                && collider.gameObject.layer == groundLayer)
            .ToArray();
        float[] groundTopY = ground
            .Select(collider => collider.bounds.max.y - rootY)
            .OrderBy(value => value)
            .ToArray();

        float lowestDoor = doorwayY.Length > 0
            ? doorwayY[0]
            : float.NegativeInfinity;
        float[] belowDoorY = groundTopY
            .Where(value => value < lowestDoor - 0.5f)
            .ToArray();
        DungeonStairRampProxy[] ramps = root
            .GetComponentsInChildren<DungeonStairRampProxy>(true);
        float[] rampY = ramps
            .Select(ramp =>
                ramp.GetComponent<Collider>()?.bounds.center.y
                - rootY
                ?? ramp.transform.position.y - rootY)
            .OrderBy(value => value)
            .ToArray();

        return string.Join(
            ",",
            Escape(root.name),
            doorwayY.Length.ToString(CultureInfo.InvariantCulture),
            Escape(FormatValues(doorwayY)),
            groundTopY.Length.ToString(CultureInfo.InvariantCulture),
            Escape(FormatValues(groundTopY)),
            belowDoorY.Length.ToString(CultureInfo.InvariantCulture),
            Escape(FormatValues(belowDoorY)),
            ramps.Length.ToString(CultureInfo.InvariantCulture),
            Escape(FormatValues(rampY)));
    }

    private static string FormatValues(IEnumerable<float> values)
    {
        return string.Join(
            "|",
            values.Select(value =>
                FormatNumber(value)));
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
