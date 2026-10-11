using System;

public static class MerchantReputationPolicy
{
    public const int Version = 1;
    public const int MaximumExperience = 180;
    private static readonly int[] thresholds = { 0, 30, 90, 180 };
    private static readonly int[] discounts = { 0, 300, 600, 1000 };
    public static int Clamp(int total) => Math.Max(0, Math.Min(MaximumExperience, total));
    public static int Level(int total)
    {
        total = Clamp(total);
        for (int i = thresholds.Length - 1; i > 0; i--) if (total >= thresholds[i]) return i;
        return 0;
    }
    public static int Threshold(int level) => thresholds[Math.Max(0, Math.Min(3, level))];
    public static int Experience(int total) => Level(total) == 3 ? 0 : Clamp(total) - Threshold(Level(total));
    public static int Required(int total) => Level(total) == 3 ? 0 : Threshold(Level(total) + 1) - Threshold(Level(total));
    public static int DiscountBasisPoints(int total) => discounts[Level(total)];
    public static int Migrate(int level, int experience)
    {
        if (level < 0 || experience < 0) throw new ArgumentException("Invalid legacy reputation.");
        return Clamp((int)Math.Min(MaximumExperience, Math.Max((long)level * 30 + experience, Threshold(level))));
    }
}
