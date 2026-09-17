public enum AttackProgressSource
{
    NormalizedTime = 0,
    WeaponTipAngularTravel = 1,
    WeaponTipForward = 2,
    WeaponTipSectorAngle = 3
}

public static class AttackProgressSourcePolicy
{
    public static AttackProgressSource ResolveDefault(AttackPatternDefinition pattern)
    {
        if (pattern == null || pattern.fillMode != AttackFillMode.AngularSweep)
            return AttackProgressSource.NormalizedTime;

        if (pattern.shape == AttackAreaShape.Sector)
            return AttackProgressSource.WeaponTipSectorAngle;

        if (pattern.shape == AttackAreaShape.Circle)
            return AttackProgressSource.WeaponTipAngularTravel;

        return AttackProgressSource.NormalizedTime;
    }
}
