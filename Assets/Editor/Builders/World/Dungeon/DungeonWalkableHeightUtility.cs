using System;
using DunGen;
using UnityEngine;

internal static class DungeonWalkableHeightUtility
{
    internal const float DoorwayFloorTolerance = 0.5f;

    internal static float ResolveMinimumHeight(GameObject root)
    {
        Doorway[] doorways =
            root.GetComponentsInChildren<Doorway>(true);
        if (doorways.Length == 0)
        {
            throw new InvalidOperationException(
                root.name + ": DunGen Doorway 누락");
        }

        float minimum = float.PositiveInfinity;
        for (int i = 0; i < doorways.Length; i++)
        {
            if (doorways[i] != null)
            {
                minimum = Mathf.Min(
                    minimum,
                    doorways[i].transform.position.y);
            }
        }

        if (float.IsPositiveInfinity(minimum))
        {
            throw new InvalidOperationException(
                root.name + ": 유효한 DunGen Doorway 누락");
        }

        return minimum - DoorwayFloorTolerance;
    }
}
