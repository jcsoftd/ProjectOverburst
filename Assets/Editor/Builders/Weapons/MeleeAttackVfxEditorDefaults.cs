using UnityEditor;

public static class MeleeAttackVfxEditorDefaults
{
    private const string BasicSlashDefinitionPath =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/MeleeSlash/VFX_MeleeSlash_Horizontal.asset";
    private const string CircularSlashDefinitionPath =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/MeleeSlash/VFX_MeleeSlash_Circular.asset";
    private const string ThrustDefinitionPath =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/Thrust/VFX_Prick02.asset";
    private const string VerticalRisingDefinitionPath =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/MeleeSlash/VFX_MeleeSlash_VerticalRising.asset";
    private const string VerticalFallingDefinitionPath =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/MeleeSlash/VFX_MeleeSlash_VerticalFalling.asset";
    private const string GroundSlamImpactDefinitionPath =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/GroundSlam/VFX_SpikesAttack.asset";
    private const string GroundSlamCrackDefinitionPath =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/GroundSlam/VFX_GroundSlamCrack.asset";

    public static AttackVfxCueData[] CreateDefaultCues(AttackPatternDefinition pattern)
    {
        if (pattern == null)
            return null;

        if (pattern.shape == AttackAreaShape.Sector)
        {
            return CreateBasicSlashCues(pattern);
        }

        if (pattern.shape == AttackAreaShape.Circle
            && pattern.fillMode == AttackFillMode.AngularSweep)
        {
            return CreateCircularSlashCues(pattern);
        }

        if (pattern.shape == AttackAreaShape.Rectangle
            && pattern.fillMode == AttackFillMode.LinearFill)
        {
            return CreateThrustCues();
        }

        if (pattern.shape == AttackAreaShape.Circle
            && pattern.fillMode == AttackFillMode.RadialExpand)
        {
            return CreateGroundSlamCues();
        }

        return null;
    }

    public static AttackVfxCueData[] CreateCircularSlashCues(AttackPatternDefinition pattern)
    {
        if (pattern == null
            || pattern.shape != AttackAreaShape.Circle
            || pattern.fillMode != AttackFillMode.AngularSweep)
        {
            return null;
        }

        MeleeAttackVfxDefinition definition =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(CircularSlashDefinitionPath);
        return definition != null
            ? new[]
            {
                new AttackVfxCueData
                {
                    motionRole = AttackVfxMotionRole.HorizontalCircular,
                    definition = definition,
                    triggerProgress = 0.35f,
                    placementMode = AttackVfxPlacementMode.PatternOrigin,
                    scaleMultiplier = 1f,
                    autoSwingSlope = false,
                    elementOverrideKey = "CircularSlash"
                }
            }
            : null;
    }

    public static AttackVfxCueData[] CreateBasicSlashCues(AttackPatternDefinition pattern)
    {
        if (pattern == null || pattern.shape != AttackAreaShape.Sector)
        {
            return null;
        }

        string definitionPath = pattern.fillMode == AttackFillMode.RadialExpand
            ? VerticalRisingDefinitionPath
            : BasicSlashDefinitionPath;
        MeleeAttackVfxDefinition definition =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(definitionPath);
        return definition != null
            ? new[]
            {
                new AttackVfxCueData
                {
                    motionRole = pattern.fillMode == AttackFillMode.RadialExpand
                        ? AttackVfxMotionRole.VerticalRising
                        : AttackVfxMotionRole.HorizontalSweep,
                    mirrorAxis = AttackVfxMirrorAxis.None,
                    definition = definition,
                    triggerProgress = 0.35f,
                    placementMode = AttackVfxPlacementMode.PatternOrigin,
                    scaleMultiplier = 1f,
                    autoSwingSlope = false,
                    elementOverrideKey = "BasicSlash"
                }
            }
            : null;
    }

    public static AttackVfxCueData[] CreateThrustCues()
    {
        MeleeAttackVfxDefinition definition =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(ThrustDefinitionPath);
        return definition != null
            ? new[]
            {
                new AttackVfxCueData
                {
                    motionRole = AttackVfxMotionRole.Thrust,
                    definition = definition,
                    triggerProgress = 0.05f,
                    placementMode = AttackVfxPlacementMode.PatternOrigin,
                    scaleMultiplier = 1f,
                    autoSwingSlope = false,
                    elementOverrideKey = "Thrust"
                }
            }
            : null;
    }

    public static AttackVfxCueData[] CreateGroundSlamCues()
    {
        MeleeAttackVfxDefinition slash =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(VerticalFallingDefinitionPath);
        MeleeAttackVfxDefinition impact =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(GroundSlamImpactDefinitionPath);
        MeleeAttackVfxDefinition crack =
            AssetDatabase.LoadAssetAtPath<MeleeAttackVfxDefinition>(GroundSlamCrackDefinitionPath);
        if (slash == null || crack == null || impact == null)
            return null;

        return new[]
        {
            new AttackVfxCueData
            {
                motionRole = AttackVfxMotionRole.VerticalFalling,
                mirrorAxis = AttackVfxMirrorAxis.None,
                definition = slash,
                triggerProgress = 0.2f,
                placementMode = AttackVfxPlacementMode.OwnerOrigin,
                scaleMultiplier = 1f,
                autoSwingSlope = true,
                elementOverrideKey = "GroundSlamSlash"
            },
            new AttackVfxCueData
            {
                motionRole = AttackVfxMotionRole.GroundImpact,
                definition = crack,
                triggerProgress = 0.35f,
                placementMode = AttackVfxPlacementMode.PatternGround,
                scaleMultiplier = 1f,
                autoSwingSlope = false,
                elementOverrideKey = "GroundSlamCrack"
            },
            new AttackVfxCueData
            {
                motionRole = AttackVfxMotionRole.GroundImpact,
                definition = impact,
                triggerProgress = 0.35f,
                placementMode = AttackVfxPlacementMode.PatternGround,
                scaleMultiplier = 1.5f,
                autoSwingSlope = false,
                elementOverrideKey = "GroundSlamImpact"
            }
        };
    }
}
