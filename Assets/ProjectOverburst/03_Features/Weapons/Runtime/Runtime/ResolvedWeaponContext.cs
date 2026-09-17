public readonly struct ResolvedWeaponContext
{
    public readonly WeaponItemData Source;
    public readonly WeaponCombatDefinition CombatDefinition;
    public readonly WeaponFinalStats Stats;

    public ResolvedWeaponContext(WeaponItemData source, WeaponFinalStats stats)
    {
        Source = source;
        CombatDefinition = source != null ? source.combatDefinition : null;
        Stats = stats;
    }

    public bool IsValid => Source != null && CombatDefinition != null;
    public WeaponCombatFamily Family => IsValid ? CombatDefinition.Family : WeaponCombatFamily.None;
    public WeaponClass Class => Source != null ? Source.weaponClass : default;
    public WeaponElement Element => Source != null ? Source.defaultElement : WeaponElement.None;
    public WeaponUsageSettings Usage => CombatDefinition != null ? CombatDefinition.usage : default;
    public WeaponAimSettings Aim => CombatDefinition != null ? CombatDefinition.aim : default;
    public WeaponAnimationSettings Animation => CombatDefinition != null ? CombatDefinition.animation : default;
    public MeleeWeaponDefinition Melee => CombatDefinition as MeleeWeaponDefinition;
    public MagicWeaponDefinition Magic => CombatDefinition as MagicWeaponDefinition;

    public static ResolvedWeaponContext Empty => default;
}
