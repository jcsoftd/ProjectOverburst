using System;
using System.Collections;
using DunGen;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonPlayerSpawnPlacer : MonoBehaviour
{
    private const int DefaultPlayerWaitFrames = 180;
    private const float DefaultGroundRayHeight = 5f;
    private const float DefaultGroundRayDistance = 14f;
    private const float DefaultGroundOffset = 0.12f;
    private const float MaximumAnchorGroundDelta = 1.25f;
    private const float MinimumSpawnSurfaceNormalY = 0.9f;

    private static readonly float[] ForwardSearchOffsets =
    {
        4.5f,
        5.5f,
        3.5f,
        6.5f,
        2.5f,
        1.5f,
        0f
    };

    [SerializeField] private GameObject generatedRoot;
    [UnityEngine.Serialization.FormerlySerializedAs("partyWaitFrames"), SerializeField, Min(1)] private int playerWaitFrames =
        DefaultPlayerWaitFrames;
    [SerializeField, Min(0.1f)] private float groundRayHeight =
        DefaultGroundRayHeight;
    [SerializeField, Min(0.1f)] private float groundRayDistance =
        DefaultGroundRayDistance;
    [SerializeField, Min(0f)] private float groundOffset =
        DefaultGroundOffset;

    public GameObject GeneratedRoot => generatedRoot;
    public bool PlacementSucceeded { get; private set; }
    public string FailureMessage { get; private set; } = string.Empty;
    public Vector3 LastSpawnPosition { get; private set; }
    public Quaternion LastSpawnRotation { get; private set; } =
        Quaternion.identity;
    public DungeonRoomAnchor LastStartAnchor { get; private set; }


    public void Configure(GameObject root)
    {
        generatedRoot = root;
        playerWaitFrames = DefaultPlayerWaitFrames;
        groundRayHeight = DefaultGroundRayHeight;
        groundRayDistance = DefaultGroundRayDistance;
        groundOffset = DefaultGroundOffset;
    }

    public IEnumerator PlacePlayer(Dungeon dungeon)
    {
        ResetResult();
        if (!TryResolveStartTile(dungeon, out Tile startTile, out DungeonRoomAnchor startAnchor))
            yield break;
        LastStartAnchor = startAnchor;
        int waitedFrames = 0;
        PlayerContext context = PlayerContext.GetOrCreate();
        while (context.CurrentActor == null && waitedFrames < playerWaitFrames)
        {
            waitedFrames++;
            yield return null;
        }
        PlayerActorRuntime actor = context.CurrentActor;
        if (actor == null)
        {
            Fail($"Player was not ready after {waitedFrames} frames.");
            yield break;
        }
        if (!TryResolveSpawnPose(startTile, startAnchor, out Vector3 position, out Quaternion rotation))
            yield break;
        actor.PlayerKit?.CancelCurrentActions(WeaponActionCancelReason.Recovery);
        ActorTeleportUtility.TeleportSafely(actor.transform, position, rotation);
        yield return null;
        if (actor == null || Vector3.Distance(actor.transform.position, position) > 0.75f)
        {
            Fail("Player spawn placement drifted too far.");
            yield break;
        }
        LastSpawnPosition = position;
        LastSpawnRotation = rotation;
        PlacementSucceeded = true;
    }



    private bool TryResolveStartTile(
        Dungeon dungeon,
        out Tile startTile,
        out DungeonRoomAnchor startAnchor)
    {
        startTile = null;
        startAnchor = null;
        if (generatedRoot == null)
        {
            Fail("Generated dungeon root reference is missing.");
            return false;
        }

        if (dungeon == null
            || dungeon.MainPathTiles == null
            || dungeon.MainPathTiles.Count == 0)
        {
            Fail("Generated dungeon has no main-path start tile.");
            return false;
        }

        startTile = dungeon.MainPathTiles[0];
        if (startTile == null
            || !startTile.transform.IsChildOf(generatedRoot.transform))
        {
            Fail("Main-path start tile is outside the generated root.");
            return false;
        }

        DungeonRoomAnchor[] anchors =
            startTile.GetComponentsInChildren<DungeonRoomAnchor>(true);
        for (int i = 0; i < anchors.Length; i++)
        {
            DungeonRoomAnchor candidate = anchors[i];
            if (candidate != null
                && candidate.Supports(DungeonRoomAnchorRole.Start)
                && candidate.Supports(DungeonRoomAnchorRole.Spawn))
            {
                startAnchor = candidate;
                return true;
            }
        }

        Fail("Start tile has no Start + Spawn anchor.");
        return false;
    }

    private bool TryResolveSpawnPose(
        Tile startTile, DungeonRoomAnchor startAnchor,
        out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        spawnPosition = default;
        Vector3 forward = Vector3.ProjectOnPlane(startAnchor.transform.forward, Vector3.up);
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();
        spawnRotation = Quaternion.LookRotation(forward, Vector3.up);
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
        {
            Fail("Ground layer is missing.");
            return false;
        }
        for (int i = 0; i < ForwardSearchOffsets.Length; i++)
        {
            Vector3 sample = startAnchor.transform.position + forward * ForwardSearchOffsets[i];
            if (!TryFindTileGround(startTile, sample, startAnchor.transform.position.y,
                    1 << groundLayer, out RaycastHit hit)
                || Mathf.Abs(hit.point.y - startAnchor.transform.position.y) > MaximumAnchorGroundDelta)
                continue;
            spawnPosition = new Vector3(sample.x, hit.point.y + groundOffset, sample.z);
            return true;
        }
        Fail("No flat Ground-supported player spawn was found near the start anchor.");
        return false;
    }

    private bool TryFindTileGround(
        Tile tile,
        Vector3 samplePosition,
        float referenceY,
        int groundMask,
        out RaycastHit bestHit)
    {
        Vector3 rayOrigin = new(
            samplePosition.x,
            referenceY + groundRayHeight,
            samplePosition.z);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            groundRayDistance,
            groundMask,
            QueryTriggerInteraction.Ignore);

        bool found = false;
        bool intersectsNearbyStairRamp = false;
        bestHit = default;
        float bestHeightDelta = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null
                || !hit.collider.transform.IsChildOf(tile.transform))
            {
                continue;
            }

            float heightDelta = Mathf.Abs(hit.point.y - referenceY);
            if (hit.collider.GetComponentInParent<DungeonStairRampProxy>()
                    != null
                && heightDelta <= MaximumAnchorGroundDelta)
            {
                intersectsNearbyStairRamp = true;
                continue;
            }

            if (Vector3.Dot(hit.normal, Vector3.up)
                < MinimumSpawnSurfaceNormalY)
            {
                continue;
            }

            if (!found || heightDelta < bestHeightDelta)
            {
                found = true;
                bestHit = hit;
                bestHeightDelta = heightDelta;
            }
        }

        return found && !intersectsNearbyStairRamp;
    }

    private void ResetResult()
    {
        PlacementSucceeded = false;
        FailureMessage = string.Empty;
        LastSpawnPosition = default;
        LastSpawnRotation = Quaternion.identity;
        LastStartAnchor = null;
    }

    private void Fail(string message)
    {
        PlacementSucceeded = false;
        FailureMessage = message ?? "Unknown player placement failure.";
    }
}
