using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ElementalReactionSfxSetupUtility
{
    private const string CatalogPath =
        "Assets/ProjectOverburst/Resources/Combat/SFX/ElementalReactionSfxCatalog.asset";
    private const string AdoptedRoot =
        "Assets/ProjectOverburst/06_Audio/SFX/ElementalReactions";
    private const string LegacyChainBasketFolder =
        "Assets/SFX장바구니/연쇄감전 시작 전이";
    private const string ChainBasketFolder =
        "Assets/SFX장바구니/연쇄감전 전이";
    private static readonly ClipSpec[] ClipSpecs =
    {
        new ClipSpec(
            ElementalReactionSfxCueType.Vaporize,
            "Assets/SFX장바구니/증기/Fire_Hit_2_S.wav",
            AdoptedRoot + "/Vaporize/Fire_Hit_2_S.wav"),
        new ClipSpec(
            ElementalReactionSfxCueType.PlasmaExplosion,
            "Assets/SFX장바구니/플라즈마폭발/RPG3_ElectricMagic2_HeavyImpact04.wav",
            AdoptedRoot + "/Plasma/RPG3_ElectricMagic2_HeavyImpact04.wav"),
        new ClipSpec(
            ElementalReactionSfxCueType.Freeze,
            "Assets/SFX장바구니/빙결/RPG3_IceMagic_FreezeShort02.wav",
            AdoptedRoot + "/FreezeShatter/RPG3_IceMagic_FreezeShort02.wav"),
        new ClipSpec(
            ElementalReactionSfxCueType.Shatter,
            "Assets/SFX장바구니/쇄빙/RPG3_IceMagic2_IceBreak02.wav",
            AdoptedRoot + "/FreezeShatter/RPG3_IceMagic2_IceBreak02.wav"),
        new ClipSpec(
            ElementalReactionSfxCueType.ChainTransition,
            ChainBasketFolder + "/Electric_Hit_2_S.wav",
            AdoptedRoot + "/ChainElectricity/Electric_Hit_2_S.wav"),
        new ClipSpec(
            ElementalReactionSfxCueType.ChainTransition,
            ChainBasketFolder + "/Electric_Hit_3_S.wav",
            AdoptedRoot + "/ChainElectricity/Electric_Hit_3_S.wav"),
        new ClipSpec(
            ElementalReactionSfxCueType.ColdChargeExplosion,
            "Assets/SFX장바구니/냉전하폭발/RPG3_PlasmaMagic_HeavyImpactShort01.wav",
            AdoptedRoot + "/ColdCharge/RPG3_PlasmaMagic_HeavyImpactShort01.wav")
    };

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Elemental Reaction SFX")]
    public static void RunSetupFromMenu()
    {
        RunSetupFromCommandLine();
    }

    public static void RunSetupFromCommandLine()
    {
        NormalizeBasketFolders();
        EnsureFolder(Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));
        Dictionary<ElementalReactionSfxCueType, List<AudioClip>> clipsByCue =
            CopyAndLoadAdoptedClips();

        ElementalReactionSfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<ElementalReactionSfxCatalog>(CatalogPath);
        if (catalog == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(CatalogPath) != null)
                throw new InvalidOperationException("원소반응 SFX 카탈로그 경로에 다른 타입의 에셋이 있습니다.");

            catalog = ScriptableObject.CreateInstance<ElementalReactionSfxCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.vaporize = CreateSettings(clipsByCue, ElementalReactionSfxCueType.Vaporize);
        catalog.thermalFracture = CreateSettings(
            new List<AudioClip>(),
            ElementalReactionSfxCueType.ThermalFracture);
        catalog.plasmaExplosion = CreateSettings(
            clipsByCue,
            ElementalReactionSfxCueType.PlasmaExplosion);
        catalog.freeze = CreateSettings(clipsByCue, ElementalReactionSfxCueType.Freeze);
        catalog.shatter = CreateSettings(clipsByCue, ElementalReactionSfxCueType.Shatter);
        catalog.chainTransition = CreateSettings(
            clipsByCue,
            ElementalReactionSfxCueType.ChainTransition);
        catalog.coldChargeExplosion = CreateSettings(
            clipsByCue,
            ElementalReactionSfxCueType.ColdChargeExplosion);

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
        AssetDatabase.SaveAssets();
        ValidateCatalog(catalog);
        Debug.Log("[ProjectVTP] 원소반응 SFX 카탈로그 생성·연결 검증 완료.");
    }

    private static void NormalizeBasketFolders()
    {
        if (!AssetDatabase.IsValidFolder(LegacyChainBasketFolder))
            return;
        if (AssetDatabase.IsValidFolder(ChainBasketFolder))
            throw new InvalidOperationException("연쇄감전 장바구니의 이전·현재 폴더가 동시에 존재합니다.");

        string error = AssetDatabase.MoveAsset(LegacyChainBasketFolder, ChainBasketFolder);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException("연쇄감전 장바구니 폴더 이름 변경 실패: " + error);
    }

    [MenuItem("OVERBURST/Codex/Validate/Elemental Reaction SFX")]
    public static void RunValidationFromMenu()
    {
        RunValidationFromCommandLine();
    }

    public static void RunValidationFromCommandLine()
    {
        ElementalReactionSfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<ElementalReactionSfxCatalog>(CatalogPath);
        if (catalog == null)
            throw new MissingReferenceException("원소반응 SFX 카탈로그가 없습니다.");

        ValidateCatalog(catalog);
        Debug.Log("[ProjectVTP] 원소반응 SFX 카탈로그 검증 완료.");
    }

    private static Dictionary<ElementalReactionSfxCueType, List<AudioClip>> CopyAndLoadAdoptedClips()
    {
        Dictionary<ElementalReactionSfxCueType, List<AudioClip>> result =
            new Dictionary<ElementalReactionSfxCueType, List<AudioClip>>();

        for (int i = 0; i < ClipSpecs.Length; i++)
        {
            ClipSpec spec = ClipSpecs[i];
            AudioClip source = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.SourcePath);
            if (source == null)
                throw new MissingReferenceException("장바구니 원본 SFX 누락: " + spec.SourcePath);

            EnsureFolder(Path.GetDirectoryName(spec.DestinationPath)?.Replace('\\', '/'));
            AudioClip adopted = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.DestinationPath);
            if (adopted == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(spec.DestinationPath) != null)
                    throw new InvalidOperationException("채택 음원 경로에 다른 타입의 에셋이 있습니다: " + spec.DestinationPath);
                if (!AssetDatabase.CopyAsset(spec.SourcePath, spec.DestinationPath))
                    throw new InvalidOperationException("채택 음원 복사 실패: " + spec.DestinationPath);

                AssetDatabase.ImportAsset(spec.DestinationPath, ImportAssetOptions.ForceSynchronousImport);
                adopted = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.DestinationPath);
            }

            if (adopted == null)
                throw new MissingReferenceException("채택 음원을 불러오지 못했습니다: " + spec.DestinationPath);
            if (!result.TryGetValue(spec.CueType, out List<AudioClip> list))
            {
                list = new List<AudioClip>();
                result.Add(spec.CueType, list);
            }
            list.Add(adopted);
        }

        return result;
    }

    private static ElementalReactionSfxCueSettings CreateSettings(
        Dictionary<ElementalReactionSfxCueType, List<AudioClip>> clipsByCue,
        ElementalReactionSfxCueType cueType)
    {
        clipsByCue.TryGetValue(cueType, out List<AudioClip> clips);
        return CreateSettings(clips ?? new List<AudioClip>(), cueType);
    }

    private static ElementalReactionSfxCueSettings CreateSettings(
        List<AudioClip> clips,
        ElementalReactionSfxCueType cueType)
    {
        return new ElementalReactionSfxCueSettings
        {
            clips = clips.ToArray(),
            volume = 1f,
            minPitch = 1f,
            maxPitch = 1f,
            spatial = true,
            minDistance = 2f,
            maxDistance = 35f,
            cooldown = cueType == ElementalReactionSfxCueType.ChainTransition ? 0f : 0.03f
        };
    }

    private static void ValidateCatalog(ElementalReactionSfxCatalog catalog)
    {
        ValidateCue(catalog, ElementalReactionSfxCueType.Vaporize, 1);
        ValidateCue(catalog, ElementalReactionSfxCueType.ThermalFracture, 0);
        ValidateCue(catalog, ElementalReactionSfxCueType.PlasmaExplosion, 1);
        ValidateCue(catalog, ElementalReactionSfxCueType.Freeze, 1);
        ValidateCue(catalog, ElementalReactionSfxCueType.Shatter, 1);
        ValidateCue(catalog, ElementalReactionSfxCueType.ChainTransition, 2);
        ValidateCue(catalog, ElementalReactionSfxCueType.ColdChargeExplosion, 1);

        if (catalog.chainTransition.cooldown != 0f)
            throw new InvalidOperationException("연쇄감전 전이는 서로 다른 sequence를 전역 쿨다운으로 막지 않아야 합니다.");
        if (ElementalReactionSfxService.InitialPoolSize > ElementalReactionSfxService.MaximumPoolSize
            || ElementalReactionSfxService.MaximumPoolSize != 24)
        {
            throw new InvalidOperationException("원소반응 SFX 풀 용량 계약이 올바르지 않습니다.");
        }
    }

    private static void ValidateCue(
        ElementalReactionSfxCatalog catalog,
        ElementalReactionSfxCueType cueType,
        int expectedClipCount)
    {
        ElementalReactionSfxCueSettings settings = ResolveSettings(catalog, cueType);
        int actualCount = settings?.clips?.Length ?? 0;
        if (actualCount != expectedClipCount)
            throw new InvalidOperationException(
                $"원소반응 SFX 후보 수 오류: {cueType} {actualCount}/{expectedClipCount}");

        if (expectedClipCount == 0)
        {
            if (catalog.TryResolve(cueType, out _))
                throw new InvalidOperationException("빈 SFX 슬롯이 재생 가능 상태입니다: " + cueType);
            return;
        }

        if (!catalog.TryResolve(cueType, out ElementalReactionSfxCueSettings resolved)
            || resolved != settings
            || settings.volume < 0f
            || settings.minPitch <= 0f
            || settings.maxPitch <= 0f
            || settings.minDistance <= 0f
            || settings.maxDistance <= settings.minDistance
            || settings.cooldown < 0f)
        {
            throw new InvalidOperationException("원소반응 SFX 설정 오류: " + cueType);
        }

        HashSet<AudioClip> uniqueClips = new HashSet<AudioClip>();
        for (int i = 0; i < settings.clips.Length; i++)
        {
            AudioClip clip = settings.clips[i];
            if (clip == null || !uniqueClips.Add(clip))
                throw new InvalidOperationException("null 또는 중복 원소반응 SFX: " + cueType);

            string path = AssetDatabase.GetAssetPath(clip);
            if (!path.StartsWith(AdoptedRoot + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("카탈로그가 채택 음원 외부를 참조합니다: " + path);
        }
    }

    private static ElementalReactionSfxCueSettings ResolveSettings(
        ElementalReactionSfxCatalog catalog,
        ElementalReactionSfxCueType cueType)
    {
        switch (cueType)
        {
            case ElementalReactionSfxCueType.Vaporize:
                return catalog.vaporize;
            case ElementalReactionSfxCueType.ThermalFracture:
                return catalog.thermalFracture;
            case ElementalReactionSfxCueType.PlasmaExplosion:
                return catalog.plasmaExplosion;
            case ElementalReactionSfxCueType.Freeze:
                return catalog.freeze;
            case ElementalReactionSfxCueType.Shatter:
                return catalog.shatter;
            case ElementalReactionSfxCueType.ChainTransition:
                return catalog.chainTransition;
            case ElementalReactionSfxCueType.ColdChargeExplosion:
                return catalog.coldChargeExplosion;
            default:
                return null;
        }
    }

    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder))
            return;

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

    private readonly struct ClipSpec
    {
        public readonly ElementalReactionSfxCueType CueType;
        public readonly string SourcePath;
        public readonly string DestinationPath;

        public ClipSpec(
            ElementalReactionSfxCueType cueType,
            string sourcePath,
            string destinationPath)
        {
            CueType = cueType;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
        }
    }
}
