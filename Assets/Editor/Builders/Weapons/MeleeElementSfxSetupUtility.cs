using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MeleeElementSfxSetupUtility
{
    public const string CatalogPath = MeleeElementSfxEditorDefaults.CatalogPath;
    private const string SwordComboPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/Combos/OneHandSwordPrimaryCombo.asset";
    private const string GreatswordComboPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Combos/GreatswordComboSet01.asset";
    private static MeleeElementSfxDefaultSpec[] Specs => MeleeElementSfxEditorDefaults.Specs;

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Melee Element SFX")]
    public static void RunFromMenu()
    {
        RunOnceFromCommandLine();
    }

    public static void RunOnceFromCommandLine()
    {
        EnsureFolder("Assets/ProjectOverburst/Resources/Combat/SFX");
        MeleeElementSfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<MeleeElementSfxCatalog>(CatalogPath);
        if (catalog == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(CatalogPath) != null)
                throw new InvalidOperationException("Melee element SFX catalog has an unexpected type.");

            catalog = ScriptableObject.CreateInstance<MeleeElementSfxCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        ApplyDefaultMapping(catalog);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateCatalog(catalog);
        Debug.Log("[ProjectVTP] Melee element SFX setup and validation passed.");
    }

    public static void ApplyDefaultMapping(MeleeElementSfxCatalog catalog)
    {
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

        MeleeElementSfxEntry[] entries = new MeleeElementSfxEntry[Specs.Length];
        for (int i = 0; i < Specs.Length; i++)
            entries[i] = CreateDefaultEntry(Specs[i]);

        catalog.entries = entries;
    }

    public static MeleeElementSfxEntry CreateDefaultEntry(MeleeElementSfxDefaultSpec spec)
    {
        return new MeleeElementSfxEntry
        {
            element = spec.Element,
            slash = CreateSettings(MeleeElementSfxEditorDefaults.FindRequiredSlash(spec.SlashName)),
            hit = CreateSettings(MeleeElementSfxEditorDefaults.FindRequiredHit(spec.HitName))
        };
    }

    [MenuItem("OVERBURST/Codex/Validate/Melee Element SFX")]
    public static void ValidateFromMenu()
    {
        ValidateFromCommandLine();
    }

    public static void ValidateFromCommandLine()
    {
        MeleeElementSfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<MeleeElementSfxCatalog>(CatalogPath);
        if (catalog == null)
            throw new MissingReferenceException("Melee element SFX catalog is missing.");

        ValidateCatalog(catalog);
        Debug.Log("[ProjectVTP] Melee element SFX validation passed.");
    }

    private static MeleeElementSfxCueSettings CreateSettings(AudioClip clip)
    {
        return new MeleeElementSfxCueSettings
        {
            clips = new[] { clip },
            volume = 1f,
            minPitch = 1f,
            maxPitch = 1f,
            spatial = true,
            minDistance = 2f,
            maxDistance = 40f,
            cooldown = 0.03f
        };
    }

    public static void ValidateCatalog(MeleeElementSfxCatalog catalog)
    {
        if (catalog.entries == null || catalog.entries.Length != Specs.Length)
            throw new InvalidOperationException("Melee element SFX catalog entry count is invalid.");

        HashSet<WeaponElement> elements = new HashSet<WeaponElement>();
        MeleeElementSfxResolver resolver = new MeleeElementSfxResolver(catalog);
        for (int i = 0; i < Specs.Length; i++)
        {
            MeleeElementSfxDefaultSpec spec = Specs[i];
            if (!elements.Add(spec.Element))
                throw new InvalidOperationException("Duplicate melee SFX element: " + spec.Element);

            MeleeElementSfxEntry entry = FindRequiredEntry(catalog, spec.Element);
            ValidateCue(
                entry.slash,
                MeleeElementSfxEditorDefaults.FindRequiredSlash(spec.SlashName),
                spec.Element,
                "Slash");
            ValidateCue(
                entry.hit,
                MeleeElementSfxEditorDefaults.FindRequiredHit(spec.HitName),
                spec.Element,
                "Hit");
            if (!resolver.TryResolve(spec.Element, MeleeElementSfxCueType.Slash, out var slash)
                || slash != entry.slash
                || !resolver.TryResolve(spec.Element, MeleeElementSfxCueType.Hit, out var hit)
                || hit != entry.hit)
            {
                throw new InvalidOperationException("Melee element SFX resolver mapping failed: " + spec.Element);
            }
        }

        ValidateForbiddenReferences(catalog);
        ValidateCueContracts();
        if (MeleeElementSfxService.InitialPoolSize > MeleeElementSfxService.MaximumPoolSize
            || MeleeElementSfxService.MaximumPoolSize != 32)
        {
            throw new InvalidOperationException("Melee SFX pool capacity contract is invalid.");
        }
    }

    private static MeleeElementSfxEntry FindRequiredEntry(
        MeleeElementSfxCatalog catalog,
        WeaponElement element)
    {
        MeleeElementSfxEntry result = null;
        int count = 0;
        for (int i = 0; i < catalog.entries.Length; i++)
        {
            MeleeElementSfxEntry entry = catalog.entries[i];
            if (entry == null || entry.element != element)
                continue;
            result = entry;
            count++;
        }

        if (count != 1 || result == null)
            throw new InvalidOperationException("Melee SFX element entry count is invalid: " + element);
        return result;
    }

    private static void ValidateCue(
        MeleeElementSfxCueSettings settings,
        AudioClip expected,
        WeaponElement element,
        string cueName)
    {
        if (settings == null
            || settings.clips == null
            || settings.clips.Length != 1
            || settings.clips[0] != expected
            || settings.volume < 0f
            || settings.minPitch <= 0f
            || settings.maxPitch <= 0f
            || settings.minDistance <= 0f
            || settings.maxDistance <= settings.minDistance
            || settings.cooldown < 0f)
        {
            throw new InvalidOperationException(
                "Melee SFX cue settings are invalid: " + element + "/" + cueName);
        }
    }

    private static void ValidateForbiddenReferences(MeleeElementSfxCatalog catalog)
    {
        for (int i = 0; i < catalog.entries.Length; i++)
        {
            ValidateForbiddenCue(catalog.entries[i]?.slash);
            ValidateForbiddenCue(catalog.entries[i]?.hit);
        }
    }

    private static void ValidateForbiddenCue(MeleeElementSfxCueSettings settings)
    {
        if (settings?.clips == null)
            return;
        for (int i = 0; i < settings.clips.Length; i++)
        {
            string name = settings.clips[i] != null ? settings.clips[i].name : string.Empty;
            if (name == MeleeElementSfxEditorDefaults.ForbiddenFireCircle
                || name == MeleeElementSfxEditorDefaults.ForbiddenElectricHit)
                throw new InvalidOperationException("Forbidden melee SFX clip is referenced: " + name);
        }
    }

    private static void ValidateCueContracts()
    {
        MeleeComboDefinition sword =
            AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(SwordComboPath);
        if (sword == null || sword.steps == null || sword.steps.Length != 3
            || CountCueKey(sword.steps[0], MeleeElementAttackVfxCatalog.BasicSlashKey) != 1
            || CountCueKey(sword.steps[1], MeleeElementAttackVfxCatalog.BasicSlashKey) != 1
            || CountCueKey(sword.steps[2], MeleeElementAttackVfxCatalog.CircularSlashKey) != 1
            || CountSlashSfxCues(sword.steps[2]) != 1)
        {
            throw new InvalidOperationException("One-hand sword Slash SFX cue contract is invalid.");
        }

        MeleeComboDefinition greatsword =
            AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(GreatswordComboPath);
        if (greatsword == null || greatsword.steps == null || greatsword.steps.Length != 3
            || CountCueKey(greatsword.steps[0], MeleeElementAttackVfxCatalog.BasicSlashKey) != 1
            || CountCueKey(greatsword.steps[1], MeleeElementAttackVfxCatalog.BasicSlashKey) != 1
            || CountCueKey(greatsword.steps[2], MeleeElementAttackVfxCatalog.GroundSlamSlashKey) != 1
            || CountCueKey(greatsword.steps[2], "GroundSlamCrack") != 1
            || CountCueKey(greatsword.steps[2], "GroundSlamImpact") != 1
            || CountSlashSfxCues(greatsword.steps[2]) != 1
            || MeleeElementSfxService.IsSlashCueKey("GroundSlamCrack")
            || MeleeElementSfxService.IsSlashCueKey("GroundSlamImpact"))
        {
            throw new InvalidOperationException("Greatsword Slash/ground SFX cue contract is invalid.");
        }
    }

    private static int CountSlashSfxCues(MeleeComboStepData step)
    {
        int count = 0;
        VisitCues(step, cue =>
        {
            if (MeleeElementSfxService.IsSlashCueKey(cue.elementOverrideKey))
                count++;
        });
        return count;
    }

    private static int CountCueKey(MeleeComboStepData step, string key)
    {
        int count = 0;
        VisitCues(step, cue =>
        {
            if (string.Equals(cue.elementOverrideKey, key, StringComparison.Ordinal))
                count++;
        });
        return count;
    }

    private static void VisitCues(MeleeComboStepData step, Action<AttackVfxCueData> visitor)
    {
        if (step.attackPhases == null)
            return;
        for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
        {
            AttackVfxCueData[] cues = step.attackPhases[phaseIndex].vfxCues;
            if (cues == null)
                continue;
            for (int cueIndex = 0; cueIndex < cues.Length; cueIndex++)
                visitor(cues[cueIndex]);
        }
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

}
