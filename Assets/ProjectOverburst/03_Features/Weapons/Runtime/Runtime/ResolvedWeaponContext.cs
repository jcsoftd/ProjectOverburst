public readonly struct ResolvedWeaponContext
{
    public readonly WeaponItemData Source;
    public readonly WeaponCombatDefinition CombatDefinition;
    public readonly WeaponFinalStats Stats;
    private readonly WeaponElement element;

    public ResolvedWeaponContext(WeaponItemData source, WeaponFinalStats stats, WeaponElement? instanceElement = null)
    {
        Source = source;
        CombatDefinition = source != null ? source.combatDefinition : null;
        Stats = stats;
        WeaponElement candidate = instanceElement ?? (source != null ? source.defaultElement : WeaponElement.None);
        element = OverburstElementRules.IsActive(candidate) ? candidate : WeaponElement.None;
    }

    public bool IsValid => Source != null && CombatDefinition != null;
    public WeaponCombatFamily Family => IsValid ? CombatDefinition.Family : WeaponCombatFamily.None;
    public WeaponClass Class => Source != null ? Source.weaponClass : default;
    public WeaponElement Element => element;
    public WeaponUsageSettings Usage => CombatDefinition != null ? CombatDefinition.usage : default;
    public WeaponAimSettings Aim => CombatDefinition != null ? CombatDefinition.aim : default;
    public WeaponAnimationSettings Animation => CombatDefinition != null ? CombatDefinition.animation : default;
    public MeleeWeaponDefinition Melee => CombatDefinition as MeleeWeaponDefinition;
    public MagicWeaponDefinition Magic => CombatDefinition as MagicWeaponDefinition;

    public static ResolvedWeaponContext Empty => default;
}
