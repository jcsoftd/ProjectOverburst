using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ElementalReactionVfxAuthoringUtility
{
    private const string PrefabRoot = "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Prefabs";
    private const string CatalogPath = "Assets/ProjectOverburst/Resources/Combat/VFX/ElementalReactionVfxCatalog.asset";
    private const string ContentRootName = "VFX_CONTENT";
    private const int IndividualPoolCapacity = 100;

    private static readonly WrapperSpec[] Specs =
    {
        OneShot("Vaporize.Start", "Vaporize", "PF_VFX_Reaction_Vaporize_Start", ElementalReactionType.Vaporize, ElementalReactionVfxSlotType.Start, ElementalReactionVfxSpawnBasis.TargetCenter, IndividualPoolCapacity),

        OneShot("ThermalFracture.Start", "ThermalFracture", "PF_VFX_Reaction_ThermalFracture_Start", ElementalReactionType.ThermalFracture, ElementalReactionVfxSlotType.Start, ElementalReactionVfxSpawnBasis.TargetCenter, IndividualPoolCapacity),
        Loop("ThermalFracture.Loop", "ThermalFracture", "PF_VFX_Reaction_ThermalFracture_Loop", ElementalReactionType.ThermalFracture, ElementalReactionVfxSpawnBasis.TargetVolume, ElementalReactionVfxScaleMode.TargetVolume, IndividualPoolCapacity),
        OneShot("ThermalFracture.End", "ThermalFracture", "PF_VFX_Reaction_ThermalFracture_End", ElementalReactionType.ThermalFracture, ElementalReactionVfxSlotType.End, ElementalReactionVfxSpawnBasis.TargetCenter, IndividualPoolCapacity),

        Loop("Plasma.Loop", "Plasma", "PF_VFX_Reaction_Plasma_Loop", ElementalReactionType.Plasma, ElementalReactionVfxSpawnBasis.TargetCenter, ElementalReactionVfxScaleMode.None, IndividualPoolCapacity),
        OneShot("Plasma.Proc", "Plasma", "PF_VFX_Reaction_Plasma_Proc", ElementalReactionType.Plasma, ElementalReactionVfxSlotType.Proc, ElementalReactionVfxSpawnBasis.WorldPosition, IndividualPoolCapacity),

        OneShot("FreezeShatter.Freeze_Start", "FreezeShatter", "PF_VFX_Reaction_Freeze_Start", ElementalReactionType.Freeze, ElementalReactionVfxSlotType.Start, ElementalReactionVfxSpawnBasis.TargetCenter, IndividualPoolCapacity),
        Loop("FreezeShatter.Freeze_Loop", "FreezeShatter", "PF_VFX_Reaction_Freeze_Loop", ElementalReactionType.Freeze, ElementalReactionVfxSpawnBasis.TargetVolume, ElementalReactionVfxScaleMode.TargetVolume, IndividualPoolCapacity),
        OneShot("FreezeShatter.Shatter_Proc", "FreezeShatter", "PF_VFX_Reaction_Shatter_Proc", ElementalReactionType.Shatter, ElementalReactionVfxSlotType.Proc, ElementalReactionVfxSpawnBasis.TargetCenter, IndividualPoolCapacity),
        OneShot("FreezeShatter.Freeze_End", "FreezeShatter", "PF_VFX_Reaction_Freeze_End", ElementalReactionType.Freeze, ElementalReactionVfxSlotType.End, ElementalReactionVfxSpawnBasis.TargetCenter, IndividualPoolCapacity),

        OneShot("ChainElectricity.Start", "ChainElectricity", "PF_VFX_Reaction_ChainElectricity_Start", ElementalReactionType.ChainElectricity, ElementalReactionVfxSlotType.Start, ElementalReactionVfxSpawnBasis.TargetCenter, IndividualPoolCapacity),
        Link("ChainElectricity.Link", "ChainElectricity", "PF_VFX_Reaction_ChainElectricity_Link", ElementalReactionType.ChainElectricity),
        OneShot("ChainElectricity.Proc", "ChainElectricity", "PF_VFX_Reaction_ChainElectricity_Proc", ElementalReactionType.ChainElectricity, ElementalReactionVfxSlotType.Proc, ElementalReactionVfxSpawnBasis.TargetCenter, IndividualPoolCapacity),

        Loop("ColdCharge.Loop", "ColdCharge", "PF_VFX_Reaction_ColdCharge_Loop", ElementalReactionType.ColdCharge, ElementalReactionVfxSpawnBasis.TargetVolume, ElementalReactionVfxScaleMode.TargetVolume, IndividualPoolCapacity),
        OneShot("ColdCharge.Proc", "ColdCharge", "PF_VFX_Reaction_ColdCharge_Proc", ElementalReactionType.ColdCharge, ElementalReactionVfxSlotType.Proc, ElementalReactionVfxSpawnBasis.WorldPosition, IndividualPoolCapacity)
    };

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Elemental Reaction VFX Authoring")]
    public static void RunSetupFromMenu()
    {
        RunSetupFromCommandLine();
    }

    public static void RunSetupFromCommandLine()
    {
        EnsureFolder(PrefabRoot);
        EnsureFolder(Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));

        for (int i = 0; i < Specs.Length; i++)
        {
            EnsureFolder(Specs[i].FolderPath);
            CreateWrapperIfMissing(Specs[i]);
        }

        CreateOrCompleteCatalog();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RunValidationFromCommandLine();
        Debug.Log($"[ElementalReactionVfxAuthoring] {Specs.Length}개 wrapper와 런타임 카탈로그 연결 검증 완료.");
    }

    [MenuItem("OVERBURST/Codex/Setup/Combat/Sync Elemental Reaction VFX Pool Capacities")]
    public static void SyncExistingPoolCapacitiesFromMenu()
    {
        SyncExistingPoolCapacitiesFromCommandLine();
    }

    public static void SyncExistingPoolCapacitiesFromCommandLine()
    {
        int existingCount = 0;
        int changedCount = 0;
        for (int i = 0; i < Specs.Length; i++)
        {
            WrapperSpec spec = Specs[i];
            if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) == null)
                continue;

            existingCount++;
            GameObject root = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
            try
            {
                ElementalReactionVfxAuthoring authoring =
                    root.GetComponent<ElementalReactionVfxAuthoring>();
                if (authoring == null)
                    throw new InvalidOperationException("VFX authoring 컴포넌트 누락: " + spec.PrefabPath);
                if (!authoring.SetPoolCapacity(spec.PoolCapacity))
                    continue;

                PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
                changedCount++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        for (int i = 0; i < Specs.Length; i++)
        {
            WrapperSpec spec = Specs[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            if (prefab == null)
                continue;

            ElementalReactionVfxAuthoring authoring =
                prefab.GetComponent<ElementalReactionVfxAuthoring>();
            if (authoring == null || authoring.PoolCapacity != IndividualPoolCapacity)
            {
                throw new InvalidOperationException(
                    $"VFX 개별 풀 용량 불일치: {spec.Id}, "
                    + $"{authoring?.PoolCapacity ?? 0}/{IndividualPoolCapacity}");
            }
        }

        Debug.Log(
            $"[ElementalReactionVfxAuthoring] 기존 wrapper {existingCount}개 풀 "
            + $"{IndividualPoolCapacity} 통일 완료, 변경={changedCount}");
    }

    [MenuItem("OVERBURST/Codex/Validate/Elemental Reaction VFX Authoring")]
    public static void RunValidationFromMenu()
    {
        RunValidationFromCommandLine();
    }

    public static void RunValidationFromCommandLine()
    {
        ElementalReactionVfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<ElementalReactionVfxCatalog>(CatalogPath);
        if (catalog == null)
            throw new InvalidOperationException("원소반응 VFX 카탈로그가 누락됐습니다.");
        if (!catalog.RuntimeConsumerConnected)
            throw new InvalidOperationException("런타임 소비자 연결값이 true가 아닙니다.");
        if (catalog.Entries.Count != Specs.Length)
            throw new InvalidOperationException($"카탈로그 슬롯 수 오류: {catalog.Entries.Count}/{Specs.Length}");

        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            ElementalReactionVfxCatalogEntry entry = catalog.Entries[i];
            if (entry == null || !ids.Add(entry.Id))
                throw new InvalidOperationException("카탈로그에 null 또는 중복 슬롯 식별자가 있습니다.");
        }

        for (int i = 0; i < Specs.Length; i++)
            ValidateWrapperAndCatalog(Specs[i], catalog);

        Debug.Log($"[ElementalReactionVfxAuthoring] 6반응 {Specs.Length} wrapper/VFX_CONTENT/런타임 연결 검증 완료.");
    }

    public static bool NeedsCatalogSynchronization()
    {
        ElementalReactionVfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<ElementalReactionVfxCatalog>(CatalogPath);
        if (catalog == null || catalog.Entries.Count != Specs.Length)
            return true;

        for (int i = 0; i < Specs.Length; i++)
        {
            WrapperSpec spec = Specs[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            if (!catalog.TryFind(spec.Id, out ElementalReactionVfxCatalogEntry entry)
                || entry.ReactionType != spec.ReactionType
                || entry.SlotType != spec.SlotType
                || entry.Prefab != prefab)
            {
                return true;
            }
        }

        return false;
    }

    public static void SynchronizeCatalogFromCommandLine()
    {
        EnsureFolder(Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));
        CreateOrCompleteCatalog();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void CreateWrapperIfMissing(WrapperSpec spec)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) != null)
            return; // 사용자의 Prefab Mode 조정을 보존

        GameObject root = new GameObject(spec.PrefabName);
        try
        {
            Normalize(root.transform);
            ElementalReactionVfxAuthoring authoring = root.AddComponent<ElementalReactionVfxAuthoring>();
            authoring.ConfigureInitialDefaults(
                spec.ReactionType,
                spec.SlotType,
                spec.SpawnBasis,
                spec.FollowTarget,
                spec.ScaleMode,
                0f,
                Vector3.zero,
                spec.Lifetime,
                spec.PoolCapacity,
                spec.NaturalCompletion);

            GameObject contentRoot = new GameObject(ContentRootName);
            contentRoot.transform.SetParent(root.transform, false);
            Normalize(contentRoot.transform);
            PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateOrCompleteCatalog()
    {
        ElementalReactionVfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<ElementalReactionVfxCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ElementalReactionVfxCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        bool changed = false;
        HashSet<string> retainedIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Specs.Length; i++)
        {
            WrapperSpec spec = Specs[i];
            retainedIds.Add(spec.Id);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            changed |= catalog.EnsureAuthoringEntry(spec.Id, spec.ReactionType, spec.SlotType, prefab);
        }

        changed |= catalog.RemoveAuthoringEntriesNotIn(retainedIds);
        changed |= catalog.SetRuntimeConsumerConnected(true);

        if (changed)
            EditorUtility.SetDirty(catalog);
    }

    private static void ValidateWrapperAndCatalog(
        WrapperSpec spec,
        ElementalReactionVfxCatalog catalog)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"wrapper 누락: {spec.PrefabPath}");
        if (!catalog.TryFind(spec.Id, out ElementalReactionVfxCatalogEntry entry)
            || entry.ReactionType != spec.ReactionType
            || entry.SlotType != spec.SlotType
            || entry.Prefab != prefab)
        {
            throw new InvalidOperationException($"카탈로그 참조 불일치: {spec.Id}");
        }

        ValidateNormalizedTransform(prefab.transform, spec.Id + " root");
        ElementalReactionVfxAuthoring authoring = prefab.GetComponent<ElementalReactionVfxAuthoring>();
        if (authoring == null
            || authoring.ReactionType != spec.ReactionType
            || authoring.SlotType != spec.SlotType)
        {
            throw new InvalidOperationException($"authoring 반응/슬롯 계약 불일치: {spec.Id}");
        }
        if (authoring.AuthoredRadius < 0f
            || authoring.Lifetime < 0f
            || authoring.PoolCapacity != spec.PoolCapacity)
        {
            throw new InvalidOperationException(
                $"authoring 값 오류: {spec.Id}, pool={authoring.PoolCapacity}/{spec.PoolCapacity}");
        }

        Component[] rootComponents = prefab.GetComponents<Component>();
        if (rootComponents.Length != 2
            || prefab.transform.childCount != 1
            || prefab.transform.GetChild(0).name != ContentRootName)
        {
            throw new InvalidOperationException($"root에는 authoring과 VFX_CONTENT만 있어야 합니다: {spec.Id}");
        }

        Transform contentRoot = prefab.transform.GetChild(0);
        ValidateNormalizedTransform(contentRoot, spec.Id + " VFX_CONTENT");

        Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[i].gameObject) > 0)
                throw new InvalidOperationException($"Missing Script: {spec.Id}/{transforms[i].name}");
        }
    }

    private static WrapperSpec OneShot(
        string id,
        string folder,
        string prefabName,
        ElementalReactionType reactionType,
        ElementalReactionVfxSlotType slotType,
        ElementalReactionVfxSpawnBasis spawnBasis,
        int poolCapacity)
    {
        return new WrapperSpec(
            id,
            folder,
            prefabName,
            reactionType,
            slotType,
            spawnBasis,
            false,
            ElementalReactionVfxScaleMode.None,
            3f,
            poolCapacity,
            true);
    }

    private static WrapperSpec Loop(
        string id,
        string folder,
        string prefabName,
        ElementalReactionType reactionType,
        ElementalReactionVfxSpawnBasis spawnBasis,
        ElementalReactionVfxScaleMode scaleMode,
        int poolCapacity = IndividualPoolCapacity)
    {
        return new WrapperSpec(
            id,
            folder,
            prefabName,
            reactionType,
            ElementalReactionVfxSlotType.Loop,
            spawnBasis,
            true,
            scaleMode,
            0f,
            poolCapacity,
            false);
    }

    private static WrapperSpec Link(
        string id,
        string folder,
        string prefabName,
        ElementalReactionType reactionType)
    {
        return new WrapperSpec(
            id,
            folder,
            prefabName,
            reactionType,
            ElementalReactionVfxSlotType.Link,
            ElementalReactionVfxSpawnBasis.SourceToTarget,
            true,
            ElementalReactionVfxScaleMode.None,
            1f,
            IndividualPoolCapacity,
            true);
    }

    private static void EnsureFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void Normalize(Transform target)
    {
        target.localPosition = Vector3.zero;
        target.localRotation = Quaternion.identity;
        target.localScale = Vector3.one;
    }

    private static void ValidateNormalizedTransform(Transform target, string label)
    {
        if (target.localPosition.sqrMagnitude > 0.000001f
            || Quaternion.Angle(target.localRotation, Quaternion.identity) > 0.001f
            || (target.localScale - Vector3.one).sqrMagnitude > 0.000001f)
        {
            throw new InvalidOperationException($"정규화 Transform 오류: {label}");
        }
    }

    private readonly struct WrapperSpec
    {
        public readonly string Id;
        public readonly string Folder;
        public readonly string PrefabName;
        public readonly ElementalReactionType ReactionType;
        public readonly ElementalReactionVfxSlotType SlotType;
        public readonly ElementalReactionVfxSpawnBasis SpawnBasis;
        public readonly bool FollowTarget;
        public readonly ElementalReactionVfxScaleMode ScaleMode;
        public readonly float Lifetime;
        public readonly int PoolCapacity;
        public readonly bool NaturalCompletion;

        public string FolderPath => $"{PrefabRoot}/{Folder}";
        public string PrefabPath => $"{FolderPath}/{PrefabName}.prefab";

        public WrapperSpec(
            string id,
            string folder,
            string prefabName,
            ElementalReactionType reactionType,
            ElementalReactionVfxSlotType slotType,
            ElementalReactionVfxSpawnBasis spawnBasis,
            bool followTarget,
            ElementalReactionVfxScaleMode scaleMode,
            float lifetime,
            int poolCapacity,
            bool naturalCompletion)
        {
            Id = id;
            Folder = folder;
            PrefabName = prefabName;
            ReactionType = reactionType;
            SlotType = slotType;
            SpawnBasis = spawnBasis;
            FollowTarget = followTarget;
            ScaleMode = scaleMode;
            Lifetime = lifetime;
            PoolCapacity = poolCapacity;
            NaturalCompletion = naturalCompletion;
        }
    }
}
