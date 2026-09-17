using System;
using UnityEngine;

[Flags]
public enum DungeonRoomAnchorRole
{
    None = 0,
    Start = 1 << 0,
    Exit = 1 << 1,
    Spawn = 1 << 2
}

[DisallowMultipleComponent]
public sealed class DungeonRoomAnchor : MonoBehaviour
{
    [SerializeField] private DungeonRoomAnchorRole roles = DungeonRoomAnchorRole.Spawn;
    [SerializeField] private string anchorId = "Primary";

    public DungeonRoomAnchorRole Roles => roles;
    public string AnchorId => anchorId;

    public bool Supports(DungeonRoomAnchorRole role)
    {
        return (roles & role) != 0;
    }

    public void Configure(DungeonRoomAnchorRole newRoles, string newAnchorId)
    {
        roles = newRoles;
        anchorId = string.IsNullOrWhiteSpace(newAnchorId) ? "Primary" : newAnchorId;
    }
}
