using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public readonly struct MeleeElementSfxDefaultSpec
{
    public readonly WeaponElement Element;
    public readonly string SlashName;
    public readonly string HitName;

    public MeleeElementSfxDefaultSpec(
        WeaponElement element,
        string slashName,
        string hitName)
    {
        Element = element;
        SlashName = slashName;
        HitName = hitName;
    }
}

public static class MeleeElementSfxEditorDefaults
{
    public const string SlashFolder =
        "Assets/ThirdParty/06_VFX/Vefects/Stylized VFX URP/Sounds/Slashes Piercing/WAV";
    public const string HitFolder =
        "Assets/ThirdParty/06_VFX/Vefects/Stylized VFX URP/Sounds/Magic Attacks/WAV";
    public const string CatalogPath =
        "Assets/ProjectOverburst/Resources/Combat/SFX/MeleeElementSfxCatalog.asset";
    public const string ForbiddenFireCircle = "SFX_Slash_FireCircle";
    public const string ForbiddenElectricHit = "SFX_Magic_Attack_Electric_Hit";

    public static readonly MeleeElementSfxDefaultSpec[] Specs =
    {
        new MeleeElementSfxDefaultSpec(
            WeaponElement.None,
            "SFX_Slash_Earth",
            "SFX_Magic_Attack_Earth_Hit"),
        new MeleeElementSfxDefaultSpec(
            WeaponElement.Fire,
            "SFX_Slash_Fire",
            "SFX_Magic_Attack_Fire_Hit"),
        new MeleeElementSfxDefaultSpec(
            WeaponElement.Electric,
            "SFX_Slash_Electric",
            "SFX_Magic_Attack_Electric_Hit_No_Blip"),
        new MeleeElementSfxDefaultSpec(
            WeaponElement.Water,
            "SFX_Slash_Water",
            "SFX_Magic_Attack_Water_Hit"),
        new MeleeElementSfxDefaultSpec(
            WeaponElement.Wind,
            "SFX_Slash_Nature",
            "SFX_Magic_Attack_Nature_Hit"),
        new MeleeElementSfxDefaultSpec(
            WeaponElement.Ice,
            "SFX_Slash_Ice",
            "SFX_Magic_Attack_Ice_Hit")
    };

    public static AudioClip FindRequiredClip(string folder, string clipName)
    {
        string expectedPath = folder + "/" + clipName + ".wav";
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        AudioClip exactClip = null;
        int exactCount = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    clipName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            exactCount++;
            exactClip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (!string.Equals(path, expectedPath, StringComparison.Ordinal))
                throw new InvalidOperationException("Melee SFX clip is in an unexpected path: " + path);
        }

        if (exactCount != 1 || exactClip == null)
            throw new InvalidOperationException("Melee SFX clip count is invalid: " + clipName);
        return exactClip;
    }

    public static AudioClip FindRequiredSlash(string clipName)
    {
        return FindRequiredClip(SlashFolder, clipName);
    }

    public static AudioClip FindRequiredHit(string clipName)
    {
        return FindRequiredClip(HitFolder, clipName);
    }
}
