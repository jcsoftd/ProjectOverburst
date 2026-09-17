using System;

public static class RunSeedUtility
{
    public static int Create(int sequenceIndex = 0)
    {
        unchecked
        {
            int guidSeed = Guid.NewGuid().GetHashCode();
            return guidSeed
                ^ (Environment.TickCount * 397)
                ^ (sequenceIndex * 7919);
        }
    }
}
