using UnityEngine;

public static class EnemySquadPursuitDebugPalette // 시뮬레이터와 게임 월드의 고정 디버그 색상
{
    public static readonly Color NearRangeColor = new Color(1f, 0.55f, 0.22f, 0.7f);
    public static readonly Color SlotRangeColor = new Color(0.72f, 0.78f, 0.88f, 0.42f);
    public static readonly Color CommitRangeColor = new Color(0.2f, 0.9f, 0.68f, 0.72f);
    public static readonly Color FarRangeColor = new Color(0.3f, 0.72f, 1f, 0.62f);

    private static readonly Color[] SlotRoleColors =
    {
        new Color(0.18f, 0.78f, 1f, 1f), // 정면
        new Color(1f, 0.78f, 0.22f, 1f), // 정면 우대각
        new Color(1f, 0.42f, 0.16f, 1f), // 우측
        new Color(0.68f, 0.42f, 1f, 1f), // 후방 우대각
        new Color(0.72f, 0.78f, 0.88f, 1f), // 후방
        new Color(1f, 0.34f, 0.68f, 1f), // 후방 좌대각
        new Color(0.2f, 0.88f, 0.48f, 1f), // 좌측
        new Color(0.22f, 0.9f, 0.86f, 1f) // 정면 좌대각
    };

    public static Color ResolveSlotRoleColor(EnemySquadPursuitSlotKind kind)
    {
        int index = (int)kind;
        return index >= 0 && index < SlotRoleColors.Length
            ? SlotRoleColors[index]
            : Color.white;
    }
}
