using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ProtofactorEnemyPilotBuilder
{
    internal const float GroundContactInset = 0.02f;
    public const string GeneratedRoot = "Assets/ProjectOverburst/Resources/Enemies/Protofactor";
    public const string DefinitionFolder = GeneratedRoot + "/Definitions";
    public const string SpeciesFolder = GeneratedRoot + "/Species";
    public const string GradeFolder = GeneratedRoot + "/Grades";
    public const string VariantFolder = GeneratedRoot + "/Variants";
    public const string AnimationProfileFolder = GeneratedRoot + "/AnimationProfiles";
    public const string AbilityFolder = GeneratedRoot + "/Abilities";
    public const string PrefabFolder = GeneratedRoot + "/Prefabs";
    public const string CatalogPath = GeneratedRoot + "/Catalogs/EC_ProtofactorPilot.asset";
    public const string AiPresetPath = "Assets/ProjectOverburst/Resources/Enemies/AiPresets/AIP_ProtofactorSquad.asset";
    public const string AnimatorFolder = "Assets/ProjectOverburst/03_Features/Enemies/Animations/Protofactor";
    public const string BaseControllerPath = AnimatorFolder + "/AC_EnemyRuntime_Base.controller";
    public const string CeratoferoxControllerPath = AnimatorFolder + "/AOC_Ceratoferox.overrideController";
    public const string RapaxControllerPath = AnimatorFolder + "/AOC_Rapax.overrideController";
    public const string GobblerControllerPath = AnimatorFolder + "/AOC_Gobbler.overrideController";
    public const string UrsacetusControllerPath = AnimatorFolder + "/AOC_Ursacetus.overrideController";
    public const string NormalGradePath = GradeFolder + "/EGP_Normal.asset";
    public const string EliteGradePath = GradeFolder + "/EGP_Elite.asset";
    public const string BossGradePath = GradeFolder + "/EGP_Boss.asset";
    public const string DefaultVariantPath = VariantFolder + "/EVP_Default.asset";
    public const string CeratoferoxEliteVariantPath = VariantFolder + "/EVP_Ceratoferox_Elite.asset";
    public const string RapaxEliteVariantPath = VariantFolder + "/EVP_Rapax_Elite.asset";
    public const string GobblerEliteVariantPath = VariantFolder + "/EVP_Gobbler_Elite.asset";
    public const string CeratoferoxMovementProfilePath =
        "Assets/ProjectOverburst/Resources/Enemies/MovementProfiles/EMP_Protofactor_Ceratoferox.asset";
    public const string RapaxMovementProfilePath =
        "Assets/ProjectOverburst/Resources/Enemies/MovementProfiles/EMP_Protofactor_Rapax.asset";
    public const string GobblerMovementProfilePath =
        "Assets/ProjectOverburst/Resources/Enemies/MovementProfiles/EMP_Protofactor_Gobbler.asset";
    public const string UrsacetusMovementProfilePath =
        "Assets/ProjectOverburst/Resources/Enemies/MovementProfiles/EMP_Protofactor_Ursacetus.asset";
    public const string CeratoferoxBehaviorProfilePath =
        "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles/EBP_Protofactor_Ceratoferox.asset";
    public const string RapaxBehaviorProfilePath =
        "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles/EBP_Protofactor_Rapax.asset";
    public const string GobblerBehaviorProfilePath =
        "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles/EBP_Protofactor_Gobbler.asset";
    public const string UrsacetusBehaviorProfilePath =
        "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles/EBP_Protofactor_Ursacetus.asset";
    public const string UrsacetusBossDefinitionPath =
        GeneratedRoot + "/Boss/EBD_Ursacetus.asset";

    public static readonly string[] DefinitionPaths =
    {
        DefinitionFolder + "/ED_Ceratoferox_Normal.asset",
        DefinitionFolder + "/ED_Ceratoferox_Elite.asset",
        DefinitionFolder + "/ED_Rapax_Normal.asset",
        DefinitionFolder + "/ED_Rapax_Elite.asset",
        DefinitionFolder + "/ED_Gobbler_Normal.asset",
        DefinitionFolder + "/ED_Gobbler_Elite.asset",
        DefinitionFolder + "/ED_Ursacetus_Boss.asset"
    };

    public static readonly string[] PrefabPaths =
    {
        PrefabFolder + "/PF_EnemyActor_Ceratoferox.prefab",
        PrefabFolder + "/PF_EnemyActor_Rapax.prefab",
        PrefabFolder + "/PF_EnemyActor_Gobbler.prefab",
        PrefabFolder + "/PF_EnemyActor_Ursacetus.prefab"
    };

    public static readonly string[] AnimationProfilePaths =
    {
        AnimationProfileFolder + "/EAP_Ceratoferox.asset",
        AnimationProfileFolder + "/EAP_Rapax.asset",
        AnimationProfileFolder + "/EAP_Gobbler.asset",
        AnimationProfileFolder + "/EAP_Ursacetus.asset"
    };

    private const string VendorRoot =
        "Assets/ThirdParty/01_비인간캐릭터/Protofactor/Sci Fi/" +
        "Sci Fi Characters Mega Pack Vol 2/Sci Fi Creatures Vol 2";

    private static readonly PilotSpeciesSpec Ceratoferox = new PilotSpeciesSpec(
        "Ceratoferox",
        "Ceratoferox",
        VendorRoot + "/Ceratoferox/Prefab/Ceratoferox.prefab",
        VendorRoot + "/Ceratoferox/FBX Files",
        CeratoferoxControllerPath,
        new ClipSpec("IdleBreathe", "Ceratoferox@IdleBreathe.fbx"),
        new ClipSpec("WalkForward", "Ceratoferox@WalkForward.fbx"),
        new ClipSpec("RunForward", "Ceratoferox@RunForward.fbx"),
        new ClipSpec("BiteAttack", "Ceratoferox@BiteAttack.fbx"),
        new ClipSpec("ClawsAttackLeft", "Ceratoferox@ClawsAttackLeft.fbx"),
        new ClipSpec("ClawsAttackRight", "Ceratoferox@ClawsAttackRight.fbx"),
        new ClipSpec("GetHitFront", "Ceratoferox@GetHitFront.fbx"),
        new ClipSpec("Death", "Ceratoferox@Death.fbx"),
        new ClipSpec("Roar1", "Ceratoferox@Roar1.fbx"),
        new ClipSpec("IdleLookAround", "Ceratoferox@IdleLookAround.fbx"));

    private static readonly PilotSpeciesSpec Rapax = new PilotSpeciesSpec(
        "Rapax",
        "Rapax",
        VendorRoot + "/Rapax/Prefabs/Rapax_Red.prefab",
        VendorRoot + "/Rapax/FBX Files",
        RapaxControllerPath,
        new ClipSpec("IdleBreathe", "Rapax@IdleBreathe.fbx"),
        new ClipSpec("Walk", "Rapax@Walk.fbx"),
        new ClipSpec("Run", "Rapax@Run.fbx"),
        new ClipSpec("LeftClawsAttack", "Rapax@LeftClawsAttack.fbx"),
        new ClipSpec("RightClawsAttack", "Rapax@RightClawsAttack.fbx"),
        new ClipSpec("2HitComboClawsAttack", "Rapax@2HitComboClawsAttack.fbx"),
        new ClipSpec("GetHitFront", "Rapax@GetHitFront.fbx"),
        new ClipSpec("Death", "Rapax@Death.fbx"),
        new ClipSpec("Roar", "Rapax@Roar.fbx"),
        new ClipSpec("IdleLookAround", "Rapax@IdleLookAround.fbx"));

    private static readonly PilotSpeciesSpec Gobbler = new PilotSpeciesSpec(
        "Gobbler",
        "Gobbler",
        VendorRoot + "/Gobbler/Prefabs/Gobbler_TintYellow.prefab",
        VendorRoot + "/Gobbler/FBX Files",
        GobblerControllerPath,
        new ClipSpec("IdleBreathe", "Gobbler@IdleBreathe.fbx"),
        new ClipSpec("Walk", "Gobbler@Walk.fbx"),
        new ClipSpec("Run", "Gobbler@Run.fbx"),
        new ClipSpec("BiteAttack", "Gobbler@BiteAttack.fbx"),
        new ClipSpec("RamAttack", "Gobbler@RamAttack.fbx"),
        new ClipSpec("BiteAttack", "Gobbler@BiteAttack.fbx"),
        new ClipSpec("GetHitFront", "Gobbler@GetHitFront.fbx"),
        new ClipSpec("Death", "Gobbler@Death.fbx"),
        new ClipSpec("Roar", "Gobbler@Roar.fbx"),
        new ClipSpec("IdleLookAround", "Gobbler@IdleLookAround.fbx"));

    private static readonly PilotSpeciesSpec Ursacetus = new PilotSpeciesSpec(
        "Ursacetus",
        "Ursacetus",
        VendorRoot + "/Ursacetus/Prefab/Ursacetus.prefab",
        VendorRoot + "/Ursacetus/FBX Files",
        UrsacetusControllerPath,
        new ClipSpec("IdleBreathe", "Ursacetus@IdleBreathe.fbx"),
        new ClipSpec("WalkForward", "Ursacetus@WalkForward.fbx"),
        new ClipSpec("WalkForward", "Ursacetus@WalkForward.fbx"),
        new ClipSpec("RightHandAttack", "Ursacetus@RightHandAttack.fbx"),
        new ClipSpec("LeftFootStompAttack", "Ursacetus@LeftFootStompAttack.fbx"),
        new ClipSpec("2HandsSmashAttack", "Ursacetus@2HandsSmashAttack.fbx"),
        new ClipSpec("GetHitFront", "Ursacetus@GetHitFront.fbx"),
        new ClipSpec("Death", "Ursacetus@Death.fbx"),
        new ClipSpec("Roar1", "Ursacetus@Roar1.fbx"),
        new ClipSpec("IdleLookAround", "Ursacetus@IdleLookAround.fbx"));

    [MenuItem("OVERBURST/Codex/Setup/Enemies/Build Protofactor Enemy Pilot")]
    public static void BuildFromMenu()
    {
        BuildAll();
    }

    public static void RunOnceFromCommandLine()
    {
        BuildAll();
    }

    private static void BuildAll()
    {
        Type speciesType = RequireProjectType("EnemySpeciesDefinition", typeof(ScriptableObject));
        Type gradeType = RequireProjectType("EnemyGradeProfile", typeof(ScriptableObject));
        Type variantType = RequireProjectType("EnemyVariantProfile", typeof(ScriptableObject));
        Type definitionType = RequireProjectType("EnemyDefinition", typeof(ScriptableObject));
        Type catalogType = RequireProjectType("EnemyCatalog", typeof(ScriptableObject));
        Type animationProfileType = RequireProjectType("EnemyAnimationProfile", typeof(ScriptableObject));
        Type abilityDefinitionType = RequireProjectType("EnemyAbilityDefinition", typeof(ScriptableObject));
        Type abilitySetType = RequireProjectType("EnemyAbilitySet", typeof(ScriptableObject));
        Type actorType = RequireProjectType("EnemyActor", typeof(MonoBehaviour));

        EnsureFolders();

        SpeciesClips ceratoferoxClips = LoadSpeciesClips(Ceratoferox);
        SpeciesClips rapaxClips = LoadSpeciesClips(Rapax);
        SpeciesClips gobblerClips = LoadSpeciesClips(Gobbler);
        SpeciesClips ursacetusClips = LoadSpeciesClips(Ursacetus);
        AnimatorController baseController = BuildBaseAnimatorController(ceratoferoxClips);
        AnimatorOverrideController ceratoferoxController =
            BuildOverrideController(Ceratoferox, baseController, ceratoferoxClips);
        AnimatorOverrideController rapaxController =
            BuildOverrideController(Rapax, baseController, rapaxClips);
        AnimatorOverrideController gobblerController =
            BuildOverrideController(Gobbler, baseController, gobblerClips);
        AnimatorOverrideController ursacetusController =
            BuildOverrideController(Ursacetus, baseController, ursacetusClips);

        ScriptableObject ceratoferoxAnimation = BuildAnimationProfile(
            animationProfileType,
            AnimationProfilePaths[0],
            Ceratoferox,
            ceratoferoxController,
            ceratoferoxClips);
        ScriptableObject rapaxAnimation = BuildAnimationProfile(
            animationProfileType,
            AnimationProfilePaths[1],
            Rapax,
            rapaxController,
            rapaxClips);
        ScriptableObject gobblerAnimation = BuildAnimationProfile(
            animationProfileType,
            AnimationProfilePaths[2],
            Gobbler,
            gobblerController,
            gobblerClips);
        ScriptableObject ursacetusAnimation = BuildAnimationProfile(
            animationProfileType,
            AnimationProfilePaths[3],
            Ursacetus,
            ursacetusController,
            ursacetusClips);

        ScriptableObject normalGrade = CreateOrLoadAsset(gradeType, NormalGradePath);
        ConfigureNormalGrade(normalGrade);
        ScriptableObject eliteGrade = CreateOrLoadAsset(gradeType, EliteGradePath);
        ConfigureEliteGrade(eliteGrade);
        ScriptableObject bossGrade = CreateOrLoadAsset(gradeType, BossGradePath);
        ConfigureBossGrade(bossGrade);
        ScriptableObject defaultVariant = CreateOrLoadAsset(variantType, DefaultVariantPath);
        ConfigureDefaultVariant(defaultVariant);
        ScriptableObject ceratoferoxEliteVariant =
            CreateOrLoadAsset(variantType, CeratoferoxEliteVariantPath);
        ConfigureEliteVariant(
            ceratoferoxEliteVariant,
            "Ceratoferox_Elite",
            new Color(1f, 0.66f, 0.58f, 1f));
        ScriptableObject rapaxEliteVariant =
            CreateOrLoadAsset(variantType, RapaxEliteVariantPath);
        ConfigureEliteVariant(
            rapaxEliteVariant,
            "Rapax_Elite",
            new Color(0.72f, 0.9f, 1f, 1f));
        ScriptableObject gobblerEliteVariant =
            CreateOrLoadAsset(variantType, GobblerEliteVariantPath);
        ConfigureEliteVariant(
            gobblerEliteVariant,
            "Gobbler_Elite",
            new Color(1f, 0.72f, 0.46f, 1f));
        EnemyMovementProfile ceratoferoxMovement = BuildMovementProfile(
            CeratoferoxMovementProfilePath,
            "Protofactor_Ceratoferox",
            0.989f,
            4.2f);
        EnemyMovementProfile rapaxMovement = BuildMovementProfile(
            RapaxMovementProfilePath,
            "Protofactor_Rapax",
            1.175f,
            7.496f);
        EnemyMovementProfile gobblerMovement = BuildMovementProfile(
            GobblerMovementProfilePath,
            "Protofactor_Gobbler",
            ResolveRootMotionReferenceSpeed(Gobbler, "Gobbler@Walk_RM.fbx", 1f),
            ResolveRootMotionReferenceSpeed(Gobbler, "Gobbler@Run_RM.fbx", 4f));
        float ursacetusWalkReference = ResolveRootMotionReferenceSpeed(
            Ursacetus,
            "Ursacetus@WalkForward_RM.fbx",
            1f);
        EnemyMovementProfile ursacetusMovement = BuildMovementProfile(
            UrsacetusMovementProfilePath,
            "Protofactor_Ursacetus",
            ursacetusWalkReference,
            ursacetusWalkReference);
        EnemyBehaviorProfile ceratoferoxBehavior = BuildBehaviorProfile(
            CeratoferoxBehaviorProfilePath,
            "Protofactor_Ceratoferox");
        EnemyBehaviorProfile rapaxBehavior = BuildBehaviorProfile(
            RapaxBehaviorProfilePath,
            "Protofactor_Rapax");
        EnemyBehaviorProfile gobblerBehavior = BuildBehaviorProfile(
            GobblerBehaviorProfilePath,
            "Protofactor_Gobbler");
        EnemyBehaviorProfile ursacetusBehavior = BuildBehaviorProfile(
            UrsacetusBehaviorProfilePath,
            "Protofactor_Ursacetus");

        ScriptableObject ceratoferoxAbilitySet = BuildAbilitySet(
            abilityDefinitionType,
            abilitySetType,
            Ceratoferox,
            new[]
            {
                new AbilitySpec("Bite", "Attack1", 12f, 1.85f, 1.2f, 0.45f, 1f),
                new AbilitySpec("ClawLeft", "Attack2", 11f, 1.9f, 1.2f, 0.43f, 1f),
                new AbilitySpec("ClawRight", "Attack3", 11f, 1.9f, 1.2f, 0.43f, 1f)
            });
        ScriptableObject rapaxAbilitySet = BuildAbilitySet(
            abilityDefinitionType,
            abilitySetType,
            Rapax,
            new[]
            {
                new AbilitySpec("ClawLeft", "Attack1", 10f, 1.8f, 1.1f, 0.42f, 1f),
                new AbilitySpec("ClawRight", "Attack2", 10f, 1.8f, 1.1f, 0.42f, 1f),
                new AbilitySpec("ClawCombo", "Attack3", 13f, 1.9f, 1.35f, 0.5f, 0.8f)
            });
        ScriptableObject gobblerAbilitySet = BuildAbilitySet(
            abilityDefinitionType,
            abilitySetType,
            Gobbler,
            new[]
            {
                new AbilitySpec("Bite", "Attack1", 10f, 1.8f, 1.2f, 0.45f, 1f),
                new AbilitySpec("Ram", "Attack2", 12f, 2.05f, 1.35f, 0.5f, 0.85f)
            });
        UrsacetusBossContent ursacetusBossContent = BuildUrsacetusBossContent(
            abilityDefinitionType,
            abilitySetType);

        ScriptableObject ceratoferoxSpecies = BuildSpecies(
            speciesType,
            Ceratoferox,
            ceratoferoxAnimation,
            ceratoferoxAbilitySet,
            ceratoferoxMovement,
            ceratoferoxBehavior,
            EnemyCombatRole.Vanguard);
        ScriptableObject rapaxSpecies = BuildSpecies(
            speciesType,
            Rapax,
            rapaxAnimation,
            rapaxAbilitySet,
            rapaxMovement,
            rapaxBehavior,
            EnemyCombatRole.Skirmisher);
        ScriptableObject gobblerSpecies = BuildSpecies(
            speciesType,
            Gobbler,
            gobblerAnimation,
            gobblerAbilitySet,
            gobblerMovement,
            gobblerBehavior,
            EnemyCombatRole.Vanguard);
        ScriptableObject ursacetusSpecies = BuildSpecies(
            speciesType,
            Ursacetus,
            ursacetusAnimation,
            ursacetusBossContent.Phase1AbilitySet,
            ursacetusMovement,
            ursacetusBehavior,
            EnemyCombatRole.Boss);

        EnemyAiPreset aiPreset = CreateOrLoadAsset(typeof(EnemyAiPreset), AiPresetPath) as EnemyAiPreset;
        if (aiPreset == null)
            throw new InvalidOperationException("Failed to create Protofactor AI preset.");

        ScriptableObject ceratoferoxDefinition = BuildDefinition(
            definitionType,
            DefinitionPaths[0],
            "Ceratoferox_Normal",
            "Ceratoferox Normal",
            ceratoferoxSpecies,
            normalGrade,
            defaultVariant,
            ceratoferoxAnimation,
            ceratoferoxAbilitySet,
            ceratoferoxBehavior,
            ceratoferoxMovement,
            aiPreset);
        ScriptableObject ceratoferoxEliteDefinition = BuildDefinition(
            definitionType,
            DefinitionPaths[1],
            "Ceratoferox_Elite",
            "Ceratoferox Elite",
            ceratoferoxSpecies,
            eliteGrade,
            ceratoferoxEliteVariant,
            ceratoferoxAnimation,
            ceratoferoxAbilitySet,
            ceratoferoxBehavior,
            ceratoferoxMovement,
            aiPreset);
        ScriptableObject rapaxDefinition = BuildDefinition(
            definitionType,
            DefinitionPaths[2],
            "Rapax_Normal",
            "Rapax Normal",
            rapaxSpecies,
            normalGrade,
            defaultVariant,
            rapaxAnimation,
            rapaxAbilitySet,
            rapaxBehavior,
            rapaxMovement,
            aiPreset);
        ScriptableObject rapaxEliteDefinition = BuildDefinition(
            definitionType,
            DefinitionPaths[3],
            "Rapax_Elite",
            "Rapax Elite",
            rapaxSpecies,
            eliteGrade,
            rapaxEliteVariant,
            rapaxAnimation,
            rapaxAbilitySet,
            rapaxBehavior,
            rapaxMovement,
            aiPreset);
        ScriptableObject gobblerDefinition = BuildDefinition(
            definitionType,
            DefinitionPaths[4],
            "Gobbler_Normal",
            "Gobbler Normal",
            gobblerSpecies,
            normalGrade,
            defaultVariant,
            gobblerAnimation,
            gobblerAbilitySet,
            gobblerBehavior,
            gobblerMovement,
            aiPreset);
        ScriptableObject gobblerEliteDefinition = BuildDefinition(
            definitionType,
            DefinitionPaths[5],
            "Gobbler_Elite",
            "Gobbler Elite",
            gobblerSpecies,
            eliteGrade,
            gobblerEliteVariant,
            gobblerAnimation,
            gobblerAbilitySet,
            gobblerBehavior,
            gobblerMovement,
            aiPreset);
        ScriptableObject ursacetusDefinition = BuildDefinition(
            definitionType,
            DefinitionPaths[6],
            "Ursacetus_Boss",
            "Ursacetus",
            ursacetusSpecies,
            bossGrade,
            defaultVariant,
            ursacetusAnimation,
            ursacetusBossContent.Phase1AbilitySet,
            ursacetusBehavior,
            ursacetusMovement,
            null,
            EnemySquadParticipationMode.Independent);

        GameObject ceratoferoxPrefab = BuildActorPrefab(
            Ceratoferox,
            PrefabPaths[0],
            ceratoferoxDefinition,
            ceratoferoxAbilitySet,
            ceratoferoxMovement,
            ceratoferoxBehavior,
            aiPreset,
            ceratoferoxController,
            actorType);
        GameObject rapaxPrefab = BuildActorPrefab(
            Rapax,
            PrefabPaths[1],
            rapaxDefinition,
            rapaxAbilitySet,
            rapaxMovement,
            rapaxBehavior,
            aiPreset,
            rapaxController,
            actorType);
        GameObject gobblerPrefab = BuildActorPrefab(
            Gobbler,
            PrefabPaths[2],
            gobblerDefinition,
            gobblerAbilitySet,
            gobblerMovement,
            gobblerBehavior,
            aiPreset,
            gobblerController,
            actorType);
        GameObject ursacetusPrefab = BuildActorPrefab(
            Ursacetus,
            PrefabPaths[3],
            ursacetusDefinition,
            ursacetusBossContent.Phase1AbilitySet,
            ursacetusMovement,
            ursacetusBehavior,
            null,
            ursacetusController,
            actorType,
            EnemySquadParticipationMode.Independent,
            ursacetusBossContent.BossDefinition);

        ConfigureDefinitionPrefab(ceratoferoxDefinition, ceratoferoxPrefab);
        ConfigureDefinitionPrefab(ceratoferoxEliteDefinition, ceratoferoxPrefab);
        ConfigureDefinitionPrefab(rapaxDefinition, rapaxPrefab);
        ConfigureDefinitionPrefab(rapaxEliteDefinition, rapaxPrefab);
        ConfigureDefinitionPrefab(gobblerDefinition, gobblerPrefab);
        ConfigureDefinitionPrefab(gobblerEliteDefinition, gobblerPrefab);
        ConfigureDefinitionPrefab(ursacetusDefinition, ursacetusPrefab);
        ConfigureSpeciesActorPrefab(ceratoferoxSpecies, ceratoferoxPrefab);
        ConfigureSpeciesActorPrefab(rapaxSpecies, rapaxPrefab);
        ConfigureSpeciesActorPrefab(gobblerSpecies, gobblerPrefab);
        ConfigureSpeciesActorPrefab(ursacetusSpecies, ursacetusPrefab);

        aiPreset.ConfigureIdentity(
            "ProtofactorSquad",
            "Protofactor 일반 몬스터 부대",
            new[] { ceratoferoxPrefab, rapaxPrefab, gobblerPrefab });
        aiPreset.ConfigureSquadPursuit(
            41,
            4,
            8,
            6f,
            10f,
            4.5f,
            13f,
            0.82f,
            0.35f,
            1.6f,
            15f,
            6f,
            1f);
        EditorUtility.SetDirty(aiPreset);

        ScriptableObject catalog = CreateOrLoadAsset(catalogType, CatalogPath);
        ConfigureCatalog(
            catalog,
            new[]
            {
                ceratoferoxDefinition,
                ceratoferoxEliteDefinition,
                rapaxDefinition,
                rapaxEliteDefinition,
                gobblerDefinition,
                gobblerEliteDefinition,
                ursacetusDefinition
            });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ProtofactorEnemyPilotValidator.ValidateOrThrow();
        Debug.Log(
            "[ProtofactorEnemyPilotBuilder] PASS definitions=7 prefabs=4 " +
            "Ceratoferox/Rapax/Gobbler Normal+Elite + Ursacetus Boss authored.");
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets/ProjectOverburst/03_Features/Enemies", "Animations");
        EnsureFolder("Assets/ProjectOverburst/03_Features/Enemies/Animations", "Protofactor");
        EnsureFolder("Assets/ProjectOverburst/Resources/Enemies", "Protofactor");
        EnsureFolder(GeneratedRoot, "Definitions");
        EnsureFolder(GeneratedRoot, "Species");
        EnsureFolder(GeneratedRoot, "Grades");
        EnsureFolder(GeneratedRoot, "Variants");
        EnsureFolder(GeneratedRoot, "AnimationProfiles");
        EnsureFolder(GeneratedRoot, "Abilities");
        EnsureFolder(GeneratedRoot, "Prefabs");
        EnsureFolder(GeneratedRoot, "Catalogs");
        EnsureFolder(GeneratedRoot, "Boss");
        EnsureFolder("Assets/ProjectOverburst/Resources/Enemies", "AiPresets");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static SpeciesClips LoadSpeciesClips(PilotSpeciesSpec spec)
    {
        GameObject vendorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.VendorPrefabPath);
        if (vendorPrefab == null)
            throw new InvalidOperationException("Vendor prefab missing: " + spec.VendorPrefabPath);

        AnimationClip[] required =
        {
            LoadClip(spec, spec.Idle),
            LoadClip(spec, spec.Walk),
            LoadClip(spec, spec.Run),
            LoadClip(spec, spec.Attack1),
            LoadClip(spec, spec.Attack2),
            LoadClip(spec, spec.Attack3),
            LoadClip(spec, spec.Hit),
            LoadClip(spec, spec.Death),
            LoadClip(spec, spec.Taunt),
            LoadClip(spec, spec.IdleBreak)
        };

        HashSet<AnimationClip> requiredSet = new HashSet<AnimationClip>(
            required.Take(8)); // Taunt/IdleBreak는 Presentation 선택 클립으로 분류
        List<AnimationClip> optional = new List<AnimationClip>();
        List<string> excludedRootMotionPaths = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { spec.FbxFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsRootMotionPath(path))
            {
                excludedRootMotionPaths.Add(path);
                continue;
            }

            AnimationClip clip = LoadFirstAnimationClip(path);
            if (clip != null && !requiredSet.Contains(clip))
                optional.Add(clip);
        }

        optional.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        excludedRootMotionPaths.Sort(StringComparer.Ordinal);
        return new SpeciesClips(required, optional.ToArray(), excludedRootMotionPaths.ToArray());
    }

    private static AnimationClip LoadClip(PilotSpeciesSpec spec, ClipSpec clipSpec)
    {
        string path = spec.FbxFolder + "/" + clipSpec.FileName;
        if (IsRootMotionPath(path))
            throw new InvalidOperationException("Root-motion clip is not allowed: " + path);

        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate =>
                !candidate.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.name, clipSpec.ClipName, StringComparison.Ordinal));
        if (clip == null)
        {
            string available = string.Join(
                ", ",
                AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Select(candidate => candidate.name));
            throw new InvalidOperationException(
                "Required clip missing: " + path + " / " + clipSpec.ClipName +
                " (available: " + available + ")");
        }

        return clip;
    }

    private static AnimationClip LoadFirstAnimationClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
    }

    private static float ResolveRootMotionReferenceSpeed(
        PilotSpeciesSpec species,
        string fileName,
        float fallback)
    {
        AnimationClip clip = LoadFirstAnimationClip(species.FbxFolder + "/" + fileName);
        float speed = clip != null ? clip.averageSpeed.magnitude : 0f;
        return speed > 0.01f ? speed : fallback;
    }

    private static bool IsRootMotionPath(string path)
    {
        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        return fileName.EndsWith("_RM", StringComparison.OrdinalIgnoreCase);
    }

    private static AnimatorController BuildBaseAnimatorController(SpeciesClips clips)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(BaseControllerPath);

        ClearOwnedControllerSubAssets(controller);
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        controller.layers = new[]
        {
            new AnimatorControllerLayer
            {
                name = "Base Layer",
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine { name = "Base Layer" }
            }
        };
        AssetDatabase.AddObjectToAsset(controller.layers[0].stateMachine, controller);

        AddFloat(controller, "Locomotion", 0f);
        AddFloat(controller, "MoveAnimSpeed", 1f);
        AddFloat(controller, "AttackAnimSpeed", 1f);
        AddTrigger(controller, "Attack1");
        AddTrigger(controller, "Attack2");
        AddTrigger(controller, "Attack3");
        AddTrigger(controller, "GotHit");
        AddTrigger(controller, "Death");
        AddTrigger(controller, "Taunt");
        AddTrigger(controller, "IdleBreak");

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        BlendTree locomotionTree = new BlendTree
        {
            name = "BT_EnemyLocomotion",
            blendParameter = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(locomotionTree, controller);
        locomotionTree.AddChild(clips.Idle, 0f);
        locomotionTree.AddChild(clips.Walk, 1f);
        locomotionTree.AddChild(clips.Run, 2f);

        AnimatorState locomotion = machine.AddState("Locomotion", new Vector3(300f, 60f));
        locomotion.motion = locomotionTree;
        locomotion.speedParameter = "MoveAnimSpeed";
        locomotion.speedParameterActive = true;
        machine.defaultState = locomotion;
        AnimatorState attack1 = AddActionState(machine, "Attack_1", clips.Attack1, "Attack1", locomotion, 500f, 0f);
        AnimatorState attack2 = AddActionState(machine, "Attack_2", clips.Attack2, "Attack2", locomotion, 500f, 70f);
        AnimatorState attack3 = AddActionState(machine, "Attack_3", clips.Attack3, "Attack3", locomotion, 500f, 140f);
        attack1.speedParameter = "AttackAnimSpeed";
        attack1.speedParameterActive = true;
        attack2.speedParameter = "AttackAnimSpeed";
        attack2.speedParameterActive = true;
        attack3.speedParameter = "AttackAnimSpeed";
        attack3.speedParameterActive = true;
        AddActionState(machine, "Get_hit", clips.Hit, "GotHit", locomotion, 700f, 0f);
        AddActionState(machine, "Taunt", clips.Taunt, "Taunt", locomotion, 700f, 70f);
        AddActionState(machine, "Idle_break", clips.IdleBreak, "IdleBreak", locomotion, 700f, 140f);
        AddDeathState(machine, clips.Death);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void ClearOwnedControllerSubAssets(AnimatorController controller)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(BaseControllerPath);
        for (int i = assets.Length - 1; i >= 0; i--)
        {
            Object asset = assets[i];
            if (asset != null && asset != controller)
                Object.DestroyImmediate(asset, true); // 이 Builder가 소유한 Controller 내부만 재생성
        }
    }

    private static void AddFloat(AnimatorController controller, string name, float value)
    {
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = name,
            type = AnimatorControllerParameterType.Float,
            defaultFloat = value
        });
    }

    private static void AddTrigger(AnimatorController controller, string name)
    {
        controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
    }

    private static AnimatorState AddActionState(
        AnimatorStateMachine machine,
        string stateName,
        Motion motion,
        string trigger,
        AnimatorState locomotion,
        float x,
        float y)
    {
        AnimatorState state = machine.AddState(stateName, new Vector3(x, y));
        state.motion = motion;
        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.hasExitTime = false;
        enter.hasFixedDuration = true;
        enter.duration = 0.05f;
        enter.canTransitionToSelf = false;
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        AnimatorStateTransition exit = state.AddTransition(locomotion);
        exit.hasExitTime = true;
        exit.exitTime = 0.9f;
        exit.hasFixedDuration = true;
        exit.duration = 0.08f;
        return state;
    }

    private static void AddDeathState(AnimatorStateMachine machine, Motion motion)
    {
        AnimatorState death = machine.AddState("Death", new Vector3(900f, 70f));
        death.motion = motion;
        AnimatorStateTransition enter = machine.AddAnyStateTransition(death);
        enter.hasExitTime = false;
        enter.duration = 0.03f;
        enter.canTransitionToSelf = false;
        enter.AddCondition(AnimatorConditionMode.If, 0f, "Death");
    }

    private static AnimatorOverrideController BuildOverrideController(
        PilotSpeciesSpec spec,
        AnimatorController baseController,
        SpeciesClips speciesClips)
    {
        AnimatorOverrideController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(spec.OverrideControllerPath);
        if (controller == null)
        {
            controller = new AnimatorOverrideController();
            AssetDatabase.CreateAsset(controller, spec.OverrideControllerPath);
        }

        controller.runtimeAnimatorController = baseController;
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides =
            new List<KeyValuePair<AnimationClip, AnimationClip>>();
        controller.GetOverrides(overrides);
        for (int i = 0; i < overrides.Count; i++)
        {
            AnimationClip source = overrides[i].Key;
            AnimationClip replacement = ResolveOverrideClip(source, speciesClips);
            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(source, replacement);
        }

        controller.ApplyOverrides(overrides);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimationClip ResolveOverrideClip(AnimationClip source, SpeciesClips clips)
    {
        if (source == null)
            return null;
        if (source.name == Ceratoferox.Idle.ClipName)
            return clips.Idle;
        if (source.name == Ceratoferox.Walk.ClipName)
            return clips.Walk;
        if (source.name == Ceratoferox.Run.ClipName)
            return clips.Run;
        if (source.name == Ceratoferox.Attack1.ClipName)
            return clips.Attack1;
        if (source.name == Ceratoferox.Attack2.ClipName)
            return clips.Attack2;
        if (source.name == Ceratoferox.Attack3.ClipName)
            return clips.Attack3;
        if (source.name == Ceratoferox.Hit.ClipName)
            return clips.Hit;
        if (source.name == Ceratoferox.Death.ClipName)
            return clips.Death;
        if (source.name == Ceratoferox.Taunt.ClipName)
            return clips.Taunt;
        if (source.name == Ceratoferox.IdleBreak.ClipName)
            return clips.IdleBreak;
        throw new InvalidOperationException("Unknown base controller clip: " + source.name);
    }

    private static ScriptableObject BuildAnimationProfile(
        Type profileType,
        string path,
        PilotSpeciesSpec spec,
        RuntimeAnimatorController controller,
        SpeciesClips clips)
    {
        ScriptableObject profile = CreateOrLoadAsset(profileType, path);
        SerializedObject serialized = new SerializedObject(profile);
        SetString(serialized, spec.Id, "profileId", "animationProfileId");
        SetObject(serialized, controller, "runtimeController", "animatorController", "controller");
        SetObject(serialized, clips.Idle, "idle", "idleClip");
        SetObject(serialized, clips.Walk, "walk", "walkClip");
        SetObject(serialized, clips.Run, "run", "runClip");
        SetObjectArray(serialized, clips.Attacks, "attackClips");
        SetObject(serialized, clips.Hit, "hit", "hitClip");
        SetObject(serialized, clips.Death, "death", "deathClip");
        SetObject(serialized, clips.Taunt, "tauntClip");
        SetObject(serialized, clips.IdleBreak, "idleBreakClip");
        SetObjectArray(serialized, clips.Optional, "optional", "optionalClips");
        SetStringArray(serialized, clips.ExcludedRootMotionPaths, "excludedRootMotionClipPaths");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void ConfigureNormalGrade(ScriptableObject grade)
    {
        SerializedObject serialized = new SerializedObject(grade);
        SetString(serialized, "Normal", "gradeId");
        SetString(serialized, "일반", "displayName");
        SetEnum(serialized, "Normal", "gradeType");
        SetFloat(serialized, 1f, "healthMultiplier");
        SetFloat(serialized, 1f, "damageMultiplier");
        SetFloat(serialized, 1f, "moveSpeedMultiplier");
        SetFloat(serialized, 1f, "attackSpeedMultiplier");
        SetFloat(serialized, 1f, "scaleMultiplier");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(grade);
    }

    private static void ConfigureEliteGrade(ScriptableObject grade)
    {
        SerializedObject serialized = new SerializedObject(grade);
        SetString(serialized, "Elite", "gradeId");
        SetString(serialized, "정예", "displayName");
        SetEnum(serialized, "Elite", "gradeType");
        SetFloat(serialized, 1f, "healthMultiplier");
        SetFloat(serialized, 1f, "damageMultiplier");
        SetFloat(serialized, 1f, "moveSpeedMultiplier");
        SetFloat(serialized, 1f, "attackSpeedMultiplier");
        SetFloat(serialized, 1f, "scaleMultiplier");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(grade);
    }

    private static void ConfigureBossGrade(ScriptableObject grade)
    {
        SerializedObject serialized = new SerializedObject(grade);
        SetString(serialized, "Boss", "gradeId");
        SetString(serialized, "보스", "displayName");
        SetEnum(serialized, "Boss", "gradeType");
        SetFloat(serialized, 1f, "healthMultiplier");
        SetFloat(serialized, 1f, "damageMultiplier");
        SetFloat(serialized, 1f, "moveSpeedMultiplier");
        SetFloat(serialized, 1f, "attackSpeedMultiplier");
        SetFloat(serialized, 1f, "scaleMultiplier");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(grade);
    }

    private static void ConfigureDefaultVariant(ScriptableObject variant)
    {
        SerializedObject serialized = new SerializedObject(variant);
        SetString(serialized, "Default", "variantId");
        SetString(serialized, "기본", "displayName");
        SetVector3(serialized, Vector3.one, "visualScale");
        SetColor(serialized, Color.white, "tint");
        SetVector3(serialized, Vector3.one, "collisionScale");
        SetVector3(serialized, Vector3.one, "anchorScale");
        SetFloat(serialized, 1f, "healthMultiplier");
        SetFloat(serialized, 1f, "damageMultiplier");
        SetFloat(serialized, 1f, "moveSpeedMultiplier");
        SetFloat(serialized, 1f, "attackSpeedMultiplier");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(variant);
    }

    private static void ConfigureEliteVariant(
        ScriptableObject variant,
        string variantId,
        Color tint)
    {
        Vector3 scale = Vector3.one * 1.15f;
        SerializedObject serialized = new SerializedObject(variant);
        SetString(serialized, variantId, "variantId");
        SetString(serialized, "정예 외형", "displayName");
        SetVector3(serialized, scale, "visualScale");
        SetColor(serialized, tint, "tint");
        SetVector3(serialized, scale, "collisionScale");
        SetVector3(serialized, scale, "anchorScale");
        SetFloat(serialized, 1f, "healthMultiplier");
        SetFloat(serialized, 1f, "damageMultiplier");
        SetFloat(serialized, 1f, "moveSpeedMultiplier");
        SetFloat(serialized, 1f, "attackSpeedMultiplier");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(variant);
    }

    private static EnemyMovementProfile BuildMovementProfile(
        string path,
        string profileId,
        float walkAnimationReferenceSpeed,
        float runAnimationReferenceSpeed)
    {
        EnemyMovementProfile source = AssetDatabase.LoadAssetAtPath<EnemyMovementProfile>(
            "Assets/ProjectOverburst/Resources/Enemies/MovementProfiles/EMP_Default.asset");
        if (source == null)
            throw new InvalidOperationException("EMP_Default asset is missing.");

        EnemyMovementProfile profile = AssetDatabase.LoadAssetAtPath<EnemyMovementProfile>(path);
        if (profile == null)
        {
            profile = Object.Instantiate(source);
            profile.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(profile, path);
        }
        else
        {
            EditorUtility.CopySerialized(source, profile);
        }

        profile.name = System.IO.Path.GetFileNameWithoutExtension(path);
        SerializedObject serialized = new SerializedObject(profile);
        SetString(serialized, profileId, "profileId");
        SetFloat(
            serialized,
            walkAnimationReferenceSpeed,
            "animationReferenceSpeed");
        SetFloat(
            serialized,
            runAnimationReferenceSpeed,
            "runAnimationReferenceSpeed");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static EnemyBehaviorProfile BuildBehaviorProfile(string path, string profileId)
    {
        EnemyBehaviorProfile source = AssetDatabase.LoadAssetAtPath<EnemyBehaviorProfile>(
            "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles/EBP_Default.asset");
        if (source == null)
            throw new InvalidOperationException("EBP_Default asset is missing.");

        EnemyBehaviorProfile profile = AssetDatabase.LoadAssetAtPath<EnemyBehaviorProfile>(path);
        if (profile == null)
        {
            profile = Object.Instantiate(source);
            profile.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(profile, path);
        }
        else
        {
            EditorUtility.CopySerialized(source, profile);
        }

        profile.name = System.IO.Path.GetFileNameWithoutExtension(path);
        SerializedObject serialized = new SerializedObject(profile);
        SetString(serialized, profileId, "profileId");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static ScriptableObject BuildAbilitySet(
        Type abilityDefinitionType,
        Type abilitySetType,
        PilotSpeciesSpec species,
        AbilitySpec[] specs)
    {
        ScriptableObject[] abilities = new ScriptableObject[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            AbilitySpec spec = specs[i];
            string path = AbilityFolder + "/EAD_" + species.Id + "_" + spec.Id + ".asset";
            ScriptableObject ability = CreateOrLoadAsset(abilityDefinitionType, path);
            SerializedObject serialized = new SerializedObject(ability);
            SetString(serialized, species.Id + "_" + spec.Id, "abilityId");
            SetString(serialized, spec.Id, "displayName");
            SetString(serialized, spec.Trigger, "animatorTrigger", "animationTrigger");
            SetFloat(serialized, spec.Damage, "damage");
            SetFloat(serialized, 0f, "minimumRange");
            SetFloat(serialized, spec.Range, "range", "attackRange");
            SetFloat(serialized, spec.Cooldown, "cooldown");
            SetFloat(serialized, spec.HitNormalizedTime, "hitNormalizedTime");
            AnimationClip attackClip = ResolveAttackClip(species, spec.Trigger);
            float attackDuration = attackClip != null ? attackClip.length : 1f;
            SetFloat(serialized, attackDuration, "attackAnimationDuration");
            SetFloat(serialized, attackDuration * 0.9f, "attackLockDuration");
            SetFloat(serialized, spec.Weight, "weight", "selectionWeight");
            SetInt(serialized, 0, "priority");
            SetFloat(serialized, 0f, "minimumSelfHealthNormalized");
            SetFloat(serialized, 1f, "maximumSelfHealthNormalized");
            SetEnum(serialized, "DirectTarget", "executionMode");
            SetFloat(serialized, 0.75f, "verticalTolerance");
            SetBool(serialized, true, "requireLineOfSight");
            SetEnum(serialized, "Melee", "actionType", "abilityType");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ability);
            abilities[i] = ability;
        }

        string setPath = AbilityFolder + "/EAS_" + species.Id + "_Normal.asset";
        ScriptableObject abilitySet = CreateOrLoadAsset(abilitySetType, setPath);
        SerializedObject serializedSet = new SerializedObject(abilitySet);
        SetString(serializedSet, species.Id + "_Normal", "abilitySetId");
        SetObjectArray(serializedSet, abilities, "abilities", "abilityDefinitions");
        serializedSet.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(abilitySet);
        return abilitySet;
    }

    private static UrsacetusBossContent BuildUrsacetusBossContent(
        Type abilityDefinitionType,
        Type abilitySetType)
    {
        EnemyAbilityDefinition handStrike = BuildBossAbility(
            abilityDefinitionType,
            "HandStrike",
            "Attack1",
            EnemyAbilityExecutionMode.DirectTarget,
            10f,
            3.2f,
            1.2f,
            120f,
            1.8f,
            0.45f,
            1f,
            true);
        EnemyAbilityDefinition stomp = BuildBossAbility(
            abilityDefinitionType,
            "Stomp",
            "Attack2",
            EnemyAbilityExecutionMode.AreaSlam,
            12f,
            4f,
            4f,
            360f,
            4.2f,
            0.52f,
            0.8f,
            false);
        EnemyAbilityDefinition quakeSmash = BuildBossAbility(
            abilityDefinitionType,
            "QuakeSmash",
            "Attack3",
            EnemyAbilityExecutionMode.AreaSlam,
            15f,
            4.8f,
            4.8f,
            360f,
            6f,
            0.58f,
            0.65f,
            false);

        EnemyAbilitySet phase1Set = BuildNamedAbilitySet(
            abilitySetType,
            "EAS_Ursacetus_Phase01",
            "Ursacetus_Phase01",
            new[] { handStrike });
        EnemyAbilitySet phase2Set = BuildNamedAbilitySet(
            abilitySetType,
            "EAS_Ursacetus_Phase02",
            "Ursacetus_Phase02",
            new[] { handStrike, stomp });
        EnemyAbilitySet phase3Set = BuildNamedAbilitySet(
            abilitySetType,
            "EAS_Ursacetus_Phase03",
            "Ursacetus_Phase03",
            new[] { handStrike, stomp, quakeSmash });

        EnemyBossPhaseDefinition phase1 =
            CreateOrLoadAsset(
                typeof(EnemyBossPhaseDefinition),
                GeneratedRoot + "/Boss/EBPD_Ursacetus_Phase01.asset")
            as EnemyBossPhaseDefinition;
        EnemyBossPhaseDefinition phase2 =
            CreateOrLoadAsset(
                typeof(EnemyBossPhaseDefinition),
                GeneratedRoot + "/Boss/EBPD_Ursacetus_Phase02.asset")
            as EnemyBossPhaseDefinition;
        EnemyBossPhaseDefinition phase3 =
            CreateOrLoadAsset(
                typeof(EnemyBossPhaseDefinition),
                GeneratedRoot + "/Boss/EBPD_Ursacetus_Phase03.asset")
            as EnemyBossPhaseDefinition;
        if (phase1 == null || phase2 == null || phase3 == null)
            throw new InvalidOperationException("Failed to create Ursacetus phase assets.");

        phase1.Configure("Ursacetus_Phase01", "1페이즈", 1f, phase1Set);
        phase2.Configure("Ursacetus_Phase02", "2페이즈", 0.66f, phase2Set);
        phase3.Configure("Ursacetus_Phase03", "3페이즈", 0.33f, phase3Set);
        EditorUtility.SetDirty(phase1);
        EditorUtility.SetDirty(phase2);
        EditorUtility.SetDirty(phase3);

        EnemyBossDefinition bossDefinition =
            CreateOrLoadAsset(
                typeof(EnemyBossDefinition),
                UrsacetusBossDefinitionPath)
            as EnemyBossDefinition;
        if (bossDefinition == null)
            throw new InvalidOperationException("Failed to create Ursacetus boss definition.");

        bossDefinition.Configure(
            "Ursacetus",
            "Ursacetus",
            new[] { phase1, phase2, phase3 });
        EditorUtility.SetDirty(bossDefinition);
        return new UrsacetusBossContent(
            phase1Set,
            phase2Set,
            phase3Set,
            bossDefinition);
    }

    private static EnemyAbilityDefinition BuildBossAbility(
        Type abilityDefinitionType,
        string id,
        string trigger,
        EnemyAbilityExecutionMode mode,
        float damage,
        float range,
        float radius,
        float angle,
        float cooldown,
        float normalizedHitTime,
        float weight,
        bool keepRangeGate)
    {
        string path = AbilityFolder + "/EAD_Ursacetus_" + id + ".asset";
        EnemyAbilityDefinition ability =
            CreateOrLoadAsset(abilityDefinitionType, path)
            as EnemyAbilityDefinition;
        if (ability == null)
            throw new InvalidOperationException("Failed to create boss ability: " + path);

        AnimationClip attackClip = ResolveAttackClip(Ursacetus, trigger);
        float duration = attackClip != null ? attackClip.length : 1f;
        ability.Configure(
            "Ursacetus_" + id,
            trigger,
            damage,
            range,
            radius,
            angle,
            cooldown,
            0.45f,
            normalizedHitTime,
            duration * 0.92f,
            weight,
            keepRangeGate,
            mode,
            1.25f,
            true,
            duration);
        int priority = id == "QuakeSmash"
            ? 20
            : id == "Stomp"
                ? 10
                : 0;
        ability.ConfigureUsePolicy(0f, priority);
        EditorUtility.SetDirty(ability);
        return ability;
    }

    private static EnemyAbilitySet BuildNamedAbilitySet(
        Type abilitySetType,
        string fileName,
        string id,
        EnemyAbilityDefinition[] abilities)
    {
        string path = AbilityFolder + "/" + fileName + ".asset";
        EnemyAbilitySet abilitySet =
            CreateOrLoadAsset(abilitySetType, path)
            as EnemyAbilitySet;
        if (abilitySet == null)
            throw new InvalidOperationException("Failed to create ability set: " + path);

        abilitySet.Configure(id, abilities);
        EditorUtility.SetDirty(abilitySet);
        return abilitySet;
    }

    private static AnimationClip ResolveAttackClip(
        PilotSpeciesSpec species,
        string trigger)
    {
        ClipSpec? clipSpec = trigger switch
        {
            "Attack1" => species.Attack1,
            "Attack2" => species.Attack2,
            "Attack3" => species.Attack3,
            _ => null
        };
        return clipSpec.HasValue ? LoadClip(species, clipSpec.Value) : null;
    }

    private static ScriptableObject BuildSpecies(
        Type speciesType,
        PilotSpeciesSpec spec,
        ScriptableObject animationProfile,
        ScriptableObject abilitySet,
        EnemyMovementProfile movementProfile,
        EnemyBehaviorProfile behaviorProfile,
        EnemyCombatRole combatRole)
    {
        string path = SpeciesFolder + "/ESD_" + spec.Id + ".asset";
        ScriptableObject species = CreateOrLoadAsset(speciesType, path);
        SerializedObject serialized = new SerializedObject(species);
        SetString(serialized, spec.Id, "speciesId");
        SetString(serialized, spec.DisplayName, "displayName");
        SetObject(
            serialized,
            AssetDatabase.LoadAssetAtPath<GameObject>(spec.VendorPrefabPath),
            "vendorPrefab",
            "sourcePrefab");
        SetObject(serialized, animationProfile, "animationProfile");
        SetObject(serialized, abilitySet, "defaultAbilitySet", "abilitySet");
        SetEnum(serialized, combatRole.ToString(), "combatRole");
        SetFloat(serialized, 100f, "baseMaxHealth");
        SetObject(serialized, movementProfile, "movementProfile");
        SetObject(serialized, behaviorProfile, "behaviorProfile");
        SetFloat(serialized, 1f, "threatCost");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(species);
        return species;
    }

    private static ScriptableObject BuildDefinition(
        Type definitionType,
        string path,
        string id,
        string displayName,
        ScriptableObject species,
        ScriptableObject grade,
        ScriptableObject variant,
        ScriptableObject animationProfile,
        ScriptableObject abilitySet,
        EnemyBehaviorProfile behaviorProfile,
        EnemyMovementProfile movementProfile,
        EnemyAiPreset aiPreset,
        EnemySquadParticipationMode participationMode =
            EnemySquadParticipationMode.SquadMember)
    {
        ScriptableObject definition = CreateOrLoadAsset(definitionType, path);
        SerializedObject serialized = new SerializedObject(definition);
        SetString(serialized, id, "enemyId", "definitionId");
        SetString(serialized, displayName, "displayName");
        SetObject(serialized, species, "species");
        SetObject(serialized, grade, "grade");
        SetObject(serialized, variant, "variant");
        SetObject(serialized, animationProfile, "animationProfile");
        SetObject(serialized, abilitySet, "abilitySet");
        SetObject(serialized, behaviorProfile, "behaviorProfile");
        SetObject(serialized, movementProfile, "movementProfile");
        SetObject(serialized, aiPreset, "aiPreset", "squadPursuitPreset");
        SetEnum(serialized, participationMode.ToString(), "squadParticipationMode");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static GameObject BuildActorPrefab(
        PilotSpeciesSpec spec,
        string prefabPath,
        ScriptableObject definition,
        ScriptableObject abilitySet,
        EnemyMovementProfile movementProfile,
        EnemyBehaviorProfile behaviorProfile,
        EnemyAiPreset aiPreset,
        RuntimeAnimatorController animatorController,
        Type actorType,
        EnemySquadParticipationMode participationMode =
            EnemySquadParticipationMode.SquadMember,
        EnemyBossDefinition bossDefinition = null)
    {
        GameObject vendorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.VendorPrefabPath);
        if (vendorPrefab == null)
            throw new InvalidOperationException("Vendor prefab missing: " + spec.VendorPrefabPath);

        GameObject root = new GameObject("PF_EnemyActor_" + spec.Id);
        root.SetActive(false);
        try
        {
            root.transform.localScale = Vector3.one;
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0)
                root.layer = enemyLayer;

            Transform visualRoot = CreateChild(root.transform, "VisualRoot");
            GameObject vendorInstance = PrefabUtility.InstantiatePrefab(vendorPrefab) as GameObject;
            if (vendorInstance == null)
                throw new InvalidOperationException("Failed to instantiate vendor prefab: " + spec.VendorPrefabPath);
            vendorInstance.name = "VendorModel";
            vendorInstance.transform.SetParent(visualRoot, false);
            vendorInstance.transform.localPosition = Vector3.zero;
            vendorInstance.transform.localRotation = Quaternion.identity;
            vendorInstance.transform.localScale = Vector3.one;

            Animator animator = vendorInstance.GetComponentInChildren<Animator>(true);
            if (animator == null)
                throw new InvalidOperationException(spec.Id + " vendor prefab has no Animator.");
            animator.runtimeAnimatorController = animatorController;
            animator.applyRootMotion = false;
            DisableVendorBehaviours(vendorInstance, animator);

            EvaluateGroundingPose(root, animator);
            Bounds bounds = CalculateBakedVisualBounds(root, visualRoot);
            if (bounds.size.sqrMagnitude <= 0.0001f)
                throw new InvalidOperationException(spec.Id + " baked visual bounds are empty.");
            float groundingOffset = GroundContactInset - bounds.min.y;
            vendorInstance.transform.localPosition =
                new Vector3(0f, groundingOffset, 0f);
            EvaluateGroundingPose(root, animator);
            bounds = CalculateBakedVisualBounds(root, visualRoot);
            if (Mathf.Abs(bounds.min.y - GroundContactInset) > 0.005f)
            {
                throw new InvalidOperationException(
                    spec.Id
                    + " baked visual grounding failed. minY="
                    + bounds.min.y.ToString("F4"));
            }
            Debug.Log(
                "[ProtofactorEnemyPilotBuilder] Grounded "
                + spec.Id
                + " offset="
                + groundingOffset.ToString("F4")
                + " finalMinY="
                + bounds.min.y.ToString("F4"));

            float bodyRadius = Mathf.Clamp(
                Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.42f,
                0.45f,
                1.6f);
            float bodyHeight = Mathf.Max(bodyRadius * 2f, bounds.size.y * 0.88f);
            Transform collisionRoot = CreateChild(root.transform, "CollisionRoot");
            CapsuleCollider bodyCollider = collisionRoot.gameObject.AddComponent<CapsuleCollider>();
            bodyCollider.radius = bodyRadius;
            bodyCollider.height = bodyHeight;
            bodyCollider.center =
                new Vector3(
                    0f,
                    bodyHeight * 0.5f + GroundContactInset,
                    0f);

            Transform anchors = CreateChild(root.transform, "Anchors");
            Transform attackPoint = CreateChild(anchors, "AttackPoint");
            attackPoint.localPosition = new Vector3(0f, bodyHeight * 0.48f, bodyRadius + 0.45f);
            Transform hitVfxPoint = CreateChild(anchors, "HitVfxPoint");
            hitVfxPoint.localPosition = new Vector3(0f, bodyHeight * 0.55f, 0f);
            Transform hpBarAnchor = CreateChild(anchors, "HpBarAnchor");
            hpBarAnchor.localPosition = new Vector3(0f, Mathf.Max(bounds.max.y, bodyHeight) + 0.35f, 0f);
            Transform groundProbe = CreateChild(anchors, "GroundProbe");
            groundProbe.localPosition = new Vector3(0f, 0.08f, 0f);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            RunFallGuard runFallGuard = EnsureComponent<RunFallGuard>(root);
            runFallGuard.enabled = false;

            EnsureComponent<CombatHealth>(root);
            EnemyOverheadHpBar overheadHpBar = EnsureComponent<EnemyOverheadHpBar>(root);
            overheadHpBar.enabled = bossDefinition == null;
            SerializedObject serializedHpBar = new SerializedObject(overheadHpBar);
            SetObject(serializedHpBar, hpBarAnchor, "hpBarAnchor");
            serializedHpBar.ApplyModifiedPropertiesWithoutUndo();
            EnemyRank rank = EnsureComponent<EnemyRank>(root);
            SetComponentEnum(rank, "Normal", "rank");
            CombatTarget.EnsureConfigured(root, CombatTeam.Enemy);
            EnsureComponent<EnemyTargetHpReporter>(root);
            EnsureComponent<EnemyController>(root);
            EnsureComponent<HitFlashFeedback>(root);
            EnsureComponent<CombatVfx>(root);
            EnemyAnimationBridge bridge = EnsureComponent<EnemyAnimationBridge>(root);
            SerializedObject serializedBridge = new SerializedObject(bridge);
            SetObject(serializedBridge, animator, "animator");
            serializedBridge.ApplyModifiedPropertiesWithoutUndo();

            EnemyStatePatternPrefabFormalizer.ConfigureEnemyPrefab(root);
            Transform formalizerAttackPoint = root.transform.Find("AttackPoint");
            if (formalizerAttackPoint != null && formalizerAttackPoint != attackPoint)
                Object.DestroyImmediate(formalizerAttackPoint.gameObject);
            ConfigureExistingEnemyComponents(
                root,
                bodyCollider,
                attackPoint,
                abilitySet,
                movementProfile,
                behaviorProfile,
                aiPreset,
                definition,
                participationMode);

            Component abilityController = null;
            EnemyMeleeAbilityExecutor meleeAbilityExecutor =
                EnsureComponent<EnemyMeleeAbilityExecutor>(root);
            meleeAbilityExecutor.Configure(root.GetComponent<EnemyMeleeAttackController>());
            EnemyAreaSlamAbilityExecutor areaSlamExecutor = null;
            if (bossDefinition != null)
            {
                areaSlamExecutor =
                    EnsureComponent<EnemyAreaSlamAbilityExecutor>(root);
                areaSlamExecutor.Configure(
                    root.GetComponent<EnemyMeleeAttackController>());
            }
            Type abilityControllerType = FindProjectType("EnemyAbilityController", typeof(MonoBehaviour));
            if (abilityControllerType != null)
            {
                abilityController = root.GetComponent(abilityControllerType) ??
                                    root.AddComponent(abilityControllerType);
                SerializedObject serializedAbilityController = new SerializedObject(abilityController);
                SetObject(
                    serializedAbilityController,
                    root.GetComponent<EnemyMeleeAttackController>(),
                    "meleeExecutor",
                    "meleeAttack",
                    "meleeAttackController");
                SetObject(serializedAbilityController, abilitySet, "abilitySet");
                Object[] configuredExecutors = areaSlamExecutor != null
                    ? new Object[] { meleeAbilityExecutor, areaSlamExecutor }
                    : new Object[] { meleeAbilityExecutor };
                SetObjectArray(
                    serializedAbilityController,
                    configuredExecutors,
                    "executors");
                serializedAbilityController.ApplyModifiedPropertiesWithoutUndo();
            }

            EnemyIdentity identity = EnsureComponent<EnemyIdentity>(root);
            identity.SetDefinition(definition as EnemyDefinition);
            Component actor = root.AddComponent(actorType);
            EnemyBossPhaseController bossPhaseController = null;
            EnemyBossOutcomeController bossOutcomeController = null;
            if (bossDefinition != null)
            {
                bossPhaseController =
                    EnsureComponent<EnemyBossPhaseController>(root);
                bossPhaseController.Configure(
                    bossDefinition,
                    actor as EnemyActor,
                    root.GetComponent<CombatHealth>(),
                    abilityController as EnemyAbilityController);
                bossOutcomeController =
                    EnsureComponent<EnemyBossOutcomeController>(root);
                bossOutcomeController.Configure(
                    bossPhaseController,
                    bossDefinition.BossId + "_Reward");
            }
            SerializedObject serializedActor = new SerializedObject(actor);
            SetObject(serializedActor, definition, "definition");
            SetObject(serializedActor, identity, "identity");
            SetObject(serializedActor, visualRoot, "visualRoot");
            SetObject(serializedActor, collisionRoot, "collisionRoot");
            SetObject(serializedActor, anchors, "anchors");
            SetObject(serializedActor, root.GetComponent<CombatHealth>(), "health");
            SetObject(serializedActor, root.GetComponent<EnemyMovement>(), "movement");
            SetObject(serializedActor, root.GetComponent<EnemyAIController>(), "ai");
            SetObject(serializedActor, root.GetComponent<EnemyMeleeAttackController>(), "melee");
            SetObject(serializedActor, abilityController, "abilityController", "abilities");
            SetObject(serializedActor, bridge, "animationBridge");
            SetObject(serializedActor, animator, "animator");
            SetObject(serializedActor, runFallGuard, "runFallGuard");
            SetObject(
                serializedActor,
                bossPhaseController,
                "bossPhaseController");
            SetObject(
                serializedActor,
                bossOutcomeController,
                "bossOutcomeController");
            serializedActor.ApplyModifiedPropertiesWithoutUndo();

            SetLayerRecursively(root, enemyLayer);
            root.SetActive(true);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (saved == null)
                throw new InvalidOperationException("Failed to save actor prefab: " + prefabPath);
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void ConfigureExistingEnemyComponents(
        GameObject root,
        CapsuleCollider bodyCollider,
        Transform attackPoint,
        ScriptableObject abilitySet,
        EnemyMovementProfile movementProfile,
        EnemyBehaviorProfile behaviorProfile,
        EnemyAiPreset aiPreset,
        ScriptableObject definition,
        EnemySquadParticipationMode participationMode)
    {
        EnemyMovement movement = root.GetComponent<EnemyMovement>();
        SerializedObject serializedMovement = new SerializedObject(movement);
        SetObject(
            serializedMovement,
            movementProfile,
            "profile");
        serializedMovement.ApplyModifiedPropertiesWithoutUndo();

        EnemyCrowdAgent crowdAgent = root.GetComponent<EnemyCrowdAgent>();
        SerializedObject serializedCrowd = new SerializedObject(crowdAgent);
        SetObject(serializedCrowd, root.GetComponent<CombatHealth>(), "health");
        SetObject(serializedCrowd, bodyCollider, "bodyCollider");
        serializedCrowd.ApplyModifiedPropertiesWithoutUndo();

        EnemyMeleeAttackController melee = root.GetComponent<EnemyMeleeAttackController>();
        SerializedObject serializedMelee = new SerializedObject(melee);
        SetObject(serializedMelee, attackPoint, "attackPoint");
        SetObject(serializedMelee, abilitySet, "abilitySet");
        SetObject(serializedMelee, definition, "definition");
        SetFloat(serializedMelee, 1.9f, "attackRange");
        SetFloat(serializedMelee, 0.9f, "hitRadius");
        SetFloat(serializedMelee, 120f, "hitAngle");
        SetFloat(serializedMelee, 0.45f, "hitNormalizedTime");
        SerializedProperty targetLayer = serializedMelee.FindProperty("targetLayer");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (targetLayer != null)
            targetLayer.intValue = playerLayer >= 0 ? 1 << playerLayer : ~0;
        serializedMelee.ApplyModifiedPropertiesWithoutUndo();

        EnemyAIController ai = root.GetComponent<EnemyAIController>();
        SerializedObject serializedAi = new SerializedObject(ai);
        SetObject(
            serializedAi,
            behaviorProfile,
            "behaviorProfile");
        SetObject(serializedAi, aiPreset, "squadPursuitPreset");
        SetEnum(
            serializedAi,
            participationMode.ToString(),
            "squadParticipationMode");
        SetBool(serializedAi, true, "useDensityApproachSteering");
        serializedAi.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void DisableVendorBehaviours(GameObject vendorInstance, Animator animator)
    {
        Behaviour[] behaviours = vendorInstance.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour behaviour = behaviours[i];
            if (behaviour != null && behaviour != animator)
                behaviour.enabled = false;
        }
    }

    internal static void EvaluateGroundingPose(
        GameObject root,
        Animator animator)
    {
        if (root == null || animator == null)
            return;

        bool wasActive = root.activeSelf;
        root.SetActive(true);
        animator.Rebind();
        animator.SetFloat("Locomotion", 0f);
        animator.Play("Locomotion", 0, 0f);
        animator.Update(0f);
        root.SetActive(wasActive);
    }

    internal static Bounds CalculateBakedVisualBounds(
        GameObject root,
        Transform visualRoot)
    {
        Bounds result = default;
        bool initialized = false;
        if (root == null || visualRoot == null)
            return result;

        SkinnedMeshRenderer[] skinnedRenderers =
            visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int rendererIndex = 0;
            rendererIndex < skinnedRenderers.Length;
            rendererIndex++)
        {
            SkinnedMeshRenderer renderer = skinnedRenderers[rendererIndex];
            if (renderer == null || renderer.sharedMesh == null)
                continue;

            Mesh bakedMesh = new Mesh();
            try
            {
                renderer.BakeMesh(bakedMesh, false);
                EncapsulateVertices(
                    root.transform,
                    renderer.transform,
                    bakedMesh.vertices,
                    ref result,
                    ref initialized);
            }
            finally
            {
                Object.DestroyImmediate(bakedMesh);
            }
        }

        MeshFilter[] meshFilters =
            visualRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int filterIndex = 0;
            filterIndex < meshFilters.Length;
            filterIndex++)
        {
            MeshFilter filter = meshFilters[filterIndex];
            if (filter == null || filter.sharedMesh == null)
                continue;
            EncapsulateVertices(
                root.transform,
                filter.transform,
                filter.sharedMesh.vertices,
                ref result,
                ref initialized);
        }

        return result;
    }

    private static void EncapsulateVertices(
        Transform root,
        Transform source,
        Vector3[] vertices,
        ref Bounds bounds,
        ref bool initialized)
    {
        for (int vertexIndex = 0;
            vertices != null && vertexIndex < vertices.Length;
            vertexIndex++)
        {
            Vector3 rootLocalPoint =
                root.InverseTransformPoint(
                    source.TransformPoint(vertices[vertexIndex]));
            if (!initialized)
            {
                bounds = new Bounds(rootLocalPoint, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(rootLocalPoint);
            }
        }
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        Transform transform = child.transform;
        transform.SetParent(parent, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        return transform;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (layer < 0)
            return;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.layer = layer;
    }

    private static void ConfigureDefinitionPrefab(ScriptableObject definition, GameObject prefab)
    {
        SerializedObject serialized = new SerializedObject(definition);
        Component actor = prefab != null
            ? prefab.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().Name == "EnemyActor")
            : null;
        SetObject(serialized, actor, "actorPrefab", "prefab");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
    }

    private static void ConfigureSpeciesActorPrefab(ScriptableObject species, GameObject prefab)
    {
        SerializedObject serialized = new SerializedObject(species);
        SetObject(serialized, prefab, "actorPrefab");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(species);
    }

    private static void ConfigureCatalog(ScriptableObject catalog, Object[] definitions)
    {
        SerializedObject serialized = new SerializedObject(catalog);
        SetObjectArray(serialized, definitions, "definitions");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
    }

    private static ScriptableObject CreateOrLoadAsset(Type type, string path)
    {
        Object existing = AssetDatabase.LoadAssetAtPath(path, type);
        if (existing != null)
            return existing as ScriptableObject;

        Object wrongType = AssetDatabase.LoadMainAssetAtPath(path);
        if (wrongType != null)
        {
            throw new InvalidOperationException(
                "Existing asset has unexpected type: " + path + " / " + wrongType.GetType().Name);
        }

        ScriptableObject created = ScriptableObject.CreateInstance(type);
        AssetDatabase.CreateAsset(created, path);
        return created;
    }

    private static Type RequireProjectType(string typeName, Type baseType)
    {
        Type type = FindProjectType(typeName, baseType);
        if (type == null)
        {
            throw new InvalidOperationException(
                "Required runtime type is not compiled yet: " + typeName);
        }

        return type;
    }

    private static Type FindProjectType(string typeName, Type baseType)
    {
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(typeName, false);
            if (type != null && baseType.IsAssignableFrom(type))
                return type;
        }

        return null;
    }

    private static T EnsureComponent<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        return component != null ? component : root.AddComponent<T>();
    }

    private static void SetComponentEnum(Component component, string enumName, params string[] propertyNames)
    {
        SerializedObject serialized = new SerializedObject(component);
        SetEnum(serialized, enumName, propertyNames);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static SerializedProperty FindProperty(SerializedObject serialized, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            SerializedProperty property = serialized.FindProperty(names[i]);
            if (property != null)
                return property;
        }

        return null;
    }

    private static void SetString(SerializedObject serialized, string value, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property != null)
            property.stringValue = value;
    }

    private static void SetFloat(SerializedObject serialized, float value, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property != null)
            property.floatValue = value;
    }

    private static void SetInt(SerializedObject serialized, int value, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property != null)
            property.intValue = value;
    }

    private static void SetBool(SerializedObject serialized, bool value, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property != null)
            property.boolValue = value;
    }

    private static void SetObject(SerializedObject serialized, Object value, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetVector3(SerializedObject serialized, Vector3 value, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property != null)
            property.vector3Value = value;
    }

    private static void SetColor(SerializedObject serialized, Color value, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property != null)
            property.colorValue = value;
    }

    private static void SetEnum(SerializedObject serialized, string enumName, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property == null || property.propertyType != SerializedPropertyType.Enum)
            return;
        int index = Array.IndexOf(property.enumNames, enumName);
        if (index < 0)
            index = Array.IndexOf(property.enumDisplayNames, enumName);
        property.enumValueIndex = Mathf.Max(0, index);
    }

    private static void SetObjectArray(SerializedObject serialized, Object[] values, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property == null || !property.isArray)
            return;
        property.arraySize = values != null ? values.Length : 0;
        for (int i = 0; values != null && i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetStringArray(SerializedObject serialized, string[] values, params string[] names)
    {
        SerializedProperty property = FindProperty(serialized, names);
        if (property == null || !property.isArray)
            return;
        property.arraySize = values != null ? values.Length : 0;
        for (int i = 0; values != null && i < values.Length; i++)
            property.GetArrayElementAtIndex(i).stringValue = values[i];
    }

    private readonly struct ClipSpec
    {
        public readonly string ClipName;
        public readonly string FileName;

        public ClipSpec(string clipName, string fileName)
        {
            ClipName = clipName;
            FileName = fileName;
        }
    }

    private sealed class PilotSpeciesSpec
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string VendorPrefabPath;
        public readonly string FbxFolder;
        public readonly string OverrideControllerPath;
        public readonly ClipSpec Idle;
        public readonly ClipSpec Walk;
        public readonly ClipSpec Run;
        public readonly ClipSpec Attack1;
        public readonly ClipSpec Attack2;
        public readonly ClipSpec Attack3;
        public readonly ClipSpec Hit;
        public readonly ClipSpec Death;
        public readonly ClipSpec Taunt;
        public readonly ClipSpec IdleBreak;

        public PilotSpeciesSpec(
            string id,
            string displayName,
            string vendorPrefabPath,
            string fbxFolder,
            string overrideControllerPath,
            ClipSpec idle,
            ClipSpec walk,
            ClipSpec run,
            ClipSpec attack1,
            ClipSpec attack2,
            ClipSpec attack3,
            ClipSpec hit,
            ClipSpec death,
            ClipSpec taunt,
            ClipSpec idleBreak)
        {
            Id = id;
            DisplayName = displayName;
            VendorPrefabPath = vendorPrefabPath;
            FbxFolder = fbxFolder;
            OverrideControllerPath = overrideControllerPath;
            Idle = idle;
            Walk = walk;
            Run = run;
            Attack1 = attack1;
            Attack2 = attack2;
            Attack3 = attack3;
            Hit = hit;
            Death = death;
            Taunt = taunt;
            IdleBreak = idleBreak;
        }
    }

    private readonly struct SpeciesClips
    {
        private readonly AnimationClip[] required;
        public readonly AnimationClip[] Optional;
        public readonly string[] ExcludedRootMotionPaths;

        public AnimationClip Idle => required[0];
        public AnimationClip Walk => required[1];
        public AnimationClip Run => required[2];
        public AnimationClip Attack1 => required[3];
        public AnimationClip Attack2 => required[4];
        public AnimationClip Attack3 => required[5];
        public AnimationClip Hit => required[6];
        public AnimationClip Death => required[7];
        public AnimationClip Taunt => required[8];
        public AnimationClip IdleBreak => required[9];
        public AnimationClip[] Attacks => new[] { Attack1, Attack2, Attack3 };

        public SpeciesClips(
            AnimationClip[] required,
            AnimationClip[] optional,
            string[] excludedRootMotionPaths)
        {
            this.required = required;
            Optional = optional;
            ExcludedRootMotionPaths = excludedRootMotionPaths;
        }
    }

    private readonly struct AbilitySpec
    {
        public readonly string Id;
        public readonly string Trigger;
        public readonly float Damage;
        public readonly float Range;
        public readonly float Cooldown;
        public readonly float HitNormalizedTime;
        public readonly float Weight;

        public AbilitySpec(
            string id,
            string trigger,
            float damage,
            float range,
            float cooldown,
            float hitNormalizedTime,
            float weight)
        {
            Id = id;
            Trigger = trigger;
            Damage = damage;
            Range = range;
            Cooldown = cooldown;
            HitNormalizedTime = hitNormalizedTime;
            Weight = weight;
        }
    }

    private readonly struct UrsacetusBossContent
    {
        public UrsacetusBossContent(
            EnemyAbilitySet phase1AbilitySet,
            EnemyAbilitySet phase2AbilitySet,
            EnemyAbilitySet phase3AbilitySet,
            EnemyBossDefinition bossDefinition)
        {
            Phase1AbilitySet = phase1AbilitySet;
            Phase2AbilitySet = phase2AbilitySet;
            Phase3AbilitySet = phase3AbilitySet;
            BossDefinition = bossDefinition;
        }

        public EnemyAbilitySet Phase1AbilitySet { get; }
        public EnemyAbilitySet Phase2AbilitySet { get; }
        public EnemyAbilitySet Phase3AbilitySet { get; }
        public EnemyBossDefinition BossDefinition { get; }
    }
}
