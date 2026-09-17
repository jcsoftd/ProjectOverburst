public static class DungeonRunLaunchContextHolder
{
    private static DungeonRunEntryRequest current;

    public static bool HasRequest => current != null;

    public static void Set(DungeonRunEntryRequest request)
    {
        current = request;
    }

    public static bool TryConsume(out DungeonRunEntryRequest request)
    {
        request = current;
        current = null;
        return request != null;
    }

    public static DungeonRunEntryRequest Peek()
    {
        return current;
    }

    public static void Clear()
    {
        current = null;
    }
}
