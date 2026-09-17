using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MeleeElementStatusAuraSetupUtility
{
    private const string FormalRoot =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementStatusAura";
    private const string ModuleRoot = FormalRoot + "/Modules";
    private const string FormalPrefabPath =
        FormalRoot + "/PF_VFX_MeleeElementStatusAura.prefab";
    private const string RuntimeRoot = "Assets/ProjectOverburst/Resources/Combat/VFX";
    private const string RuntimePrefabPath =
        RuntimeRoot + "/PF_VFX_MeleeElementStatusAura.prefab";

    private static readonly string[] MonsterPrefabPaths =
    {
        "Assets/ProjectOverburst/Resources/Enemies/PF_StageMonster.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/PF_StageMonster_FishmanTest.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Guard.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Scout.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Spearling.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Brute.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Warlord.prefab"
    };

    private static readonly AuraSpec[] Specs =
    {
        new AuraSpec(
            MeleeElementStatusAuraType.Burning,
            "Assets/ThirdParty/06_VFX/Hovl Studio/Auras pack/Prefabs/Fire ayra.prefab"),
        new AuraSpec(
            MeleeElementStatusAuraType.Wet,
            "Assets/ThirdParty/06_VFX/Hovl Studio/Auras pack/Prefabs/Water aura.prefab"),
        new AuraSpec(
            MeleeElementStatusAuraType.Shocked,
            "Assets/ThirdParty/06_VFX/Hovl Studio/Auras pack/Prefabs/Lightning aura.prefab"),
        new AuraSpec(
            MeleeElementStatusAuraType.Chilled,
            "Assets/ThirdParty/06_VFX/Hovl Studio/Auras pack/Prefabs/Freeze aura.prefab")
    };

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Element Status Aura VFX")]
    public static void RunFromMenu()
    {
        RunOnceFromCommandLine();
    }

    public static void RunOnceFromCommandLine()
    {
        EnsureFolder(ModuleRoot);
        EnsureFolder(RuntimeRoot);
        CreateModules();
        CreateSharedPrefab();
        ConnectRuntimePresentation();
        FormalizeMonsterAuraOwners();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateFromCommandLine();
        Debug.Log("[ProjectVTP] 원소 상태 Aura VFX 정식화/검증 완료.");
    }

    [MenuItem("OVERBURST/Codex/Validate/Element Status Aura VFX")]
    public static void ValidateFromMenu()
    {
        ValidateFromCommandLine();
    }

    public static void ValidateFromCommandLine()
    {
        GameObject shared = LoadRequiredPrefab(FormalPrefabPath);
        if (!shared.activeSelf || shared.transform.childCount != Specs.Length)
            throw new InvalidOperationException("상태 Aura 통합 루트 또는 4모듈 구성이 잘못됐습니다.");

        MeleeElementStatusAuraPresentation presentation =
            shared.GetComponent<MeleeElementStatusAuraPresentation>();
        if (presentation == null || shared.GetComponent<MeleeElementStatusAuraController>() != null)
            throw new InvalidOperationException("상태 Aura presentation 구성이 잘못됐습니다.");

        for (int i = 0; i < Specs.Length; i++)
        {
            AuraSpec spec = Specs[i];
            GameObject module = LoadRequiredPrefab(spec.ModulePath);
            GameObject slot = FindSlot(shared, spec.AuraType);
            if (PrefabUtility.GetCorrespondingObjectFromSource(slot) != module
                || slot.activeSelf
                || presentation.GetAuraObject(spec.AuraType) != slot)
            {
                throw new InvalidOperationException($"상태 Aura 슬롯 연결 실패: {spec.AuraType}");
            }

            ValidateNormalizedTransform(slot.transform, spec.AuraType + " Slot");
            ValidateModule(module, spec);
        }

        ValidateIndependentActivation(shared);
        ValidateRuntimePrefab();
        ValidateMonsterAuraOwners();
        ValidateDependencies();
        Debug.Log("[ProjectVTP] 상태 Aura 원본 참조/독립 활성/잔존 정리 검증 완료.");
    }

    private static void CreateModules()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            AuraSpec spec = Specs[i];
            if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModulePath) != null)
                continue; // 기존 정식 wrapper 조정 보존

            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(spec.ModulePath));
            try
            {
                GameObject source = LoadRequiredPrefab(spec.SourcePath);
                GameObject sourceInstance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (sourceInstance == null)
                    throw new InvalidOperationException($"상태 Aura 원본 생성 실패: {spec.AuraType}");

                sourceInstance.transform.SetParent(root.transform, false);
                NormalizeTransform(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, spec.ModulePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    private static void CreateSharedPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FormalPrefabPath) != null)
            return; // 기존 정식 통합 프리팹 조정 보존

        GameObject root = new GameObject("PF_VFX_MeleeElementStatusAura");
        try
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                AuraSpec spec = Specs[i];
                GameObject module = LoadRequiredPrefab(spec.ModulePath);
                GameObject slot = PrefabUtility.InstantiatePrefab(module) as GameObject;
                if (slot == null)
                    throw new InvalidOperationException($"상태 Aura 모듈 생성 실패: {spec.AuraType}");

                slot.name = spec.AuraType + "_Aura";
                slot.transform.SetParent(root.transform, false);
                NormalizeTransform(slot.transform);
                slot.SetActive(false); // 상태 런타임 호출 전 안전 기본값
            }

            NormalizeTransform(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, FormalPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ConnectRuntimePresentation()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(FormalPrefabPath);
        try
        {
            MeleeElementStatusAuraController legacyController =
                root.GetComponent<MeleeElementStatusAuraController>();
            if (legacyController != null)
                UnityEngine.Object.DestroyImmediate(legacyController);

            MeleeElementStatusAuraPresentation presentation =
                root.GetComponent<MeleeElementStatusAuraPresentation>();
            if (presentation == null)
                presentation = root.AddComponent<MeleeElementStatusAuraPresentation>();

            SerializedObject serialized = new SerializedObject(presentation);
            serialized.FindProperty("burningAura").objectReferenceValue =
                FindSlot(root, MeleeElementStatusAuraType.Burning);
            serialized.FindProperty("wetAura").objectReferenceValue =
                FindSlot(root, MeleeElementStatusAuraType.Wet);
            serialized.FindProperty("shockedAura").objectReferenceValue =
                FindSlot(root, MeleeElementStatusAuraType.Shocked);
            serialized.FindProperty("chilledAura").objectReferenceValue =
                FindSlot(root, MeleeElementStatusAuraType.Chilled);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            for (int i = 0; i < root.transform.childCount; i++)
                root.transform.GetChild(i).gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, FormalPrefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidateModule(GameObject module, AuraSpec spec)
    {
        if (module.transform.childCount != 1)
            throw new InvalidOperationException($"상태 Aura 원본 수 불일치: {spec.AuraType}");

        GameObject source = LoadRequiredPrefab(spec.SourcePath);
        GameObject sourceInstance = module.transform.GetChild(0).gameObject;
        if (PrefabUtility.GetCorrespondingObjectFromSource(sourceInstance) != source)
            throw new InvalidOperationException($"상태 Aura nested 원본 참조 실패: {spec.AuraType}");

        ValidateNormalizedTransform(module.transform, spec.AuraType + " Module");
        ValidateSourceRootPreserved(sourceInstance.transform, source.transform, spec.AuraType);
        ParticleSystem[] particles = sourceInstance.GetComponentsInChildren<ParticleSystem>(true);
        if (particles.Length == 0)
            throw new InvalidOperationException($"ParticleSystem 없는 상태 Aura: {spec.AuraType}");

        int loopCount = 0;
        float maximumSeconds = 0f;
        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem.MainModule main = particles[i].main;
            if (main.loop)
                loopCount++;
            maximumSeconds = Mathf.Max(maximumSeconds, EstimatePlaybackSeconds(main));
        }

        Debug.Log(
            $"[ProjectVTP] Aura {spec.AuraType}: Source={spec.SourcePath}, " +
            $"GUID={AssetDatabase.AssetPathToGUID(spec.SourcePath)}, " +
            $"RootPosition={source.transform.localPosition}, " +
            $"RootRotation={source.transform.localEulerAngles}, " +
            $"RootScale={source.transform.localScale}, " +
            $"Loop={loopCount}/{particles.Length}, " +
            $"NonLoopMax={maximumSeconds:0.###}s");
    }

    private static void ValidateIndependentActivation(GameObject shared)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(shared) as GameObject;
        if (instance == null)
            throw new InvalidOperationException("상태 Aura 통합 검증 인스턴스 생성 실패.");

        try
        {
            MeleeElementStatusAuraPresentation presentation =
                instance.GetComponent<MeleeElementStatusAuraPresentation>();
            presentation.ClearAllAuras();
            for (int i = 0; i < Specs.Length; i++)
            {
                if (!presentation.SetAuraActive(Specs[i].AuraType, true))
                    throw new InvalidOperationException($"상태 Aura 활성 실패: {Specs[i].AuraType}");
            }

            if (CountActiveChildren(instance.transform) != Specs.Length)
                throw new InvalidOperationException("복수 상태 Aura 동시 활성에 실패했습니다.");

            if (!presentation.ClearAura(MeleeElementStatusAuraType.Wet)
                || presentation.IsAuraActive(MeleeElementStatusAuraType.Wet)
                || CountActiveChildren(instance.transform) != Specs.Length - 1)
            {
                throw new InvalidOperationException("상태 Aura 독립 정리에 실패했습니다.");
            }

            presentation.ClearAllAuras();
            if (CountActiveChildren(instance.transform) != 0
                || HasAliveParticles(instance))
            {
                throw new InvalidOperationException("상태 Aura 전체 정리 뒤 잔존 파티클이 있습니다.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void FormalizeMonsterAuraOwners()
    {
        for (int i = 0; i < MonsterPrefabPaths.Length; i++)
        {
            string path = MonsterPrefabPaths[i];
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                MeleeElementStatusAuraPresentation[] residentPresentations =
                    root.GetComponentsInChildren<MeleeElementStatusAuraPresentation>(true);
                for (int j = 0; j < residentPresentations.Length; j++)
                {
                    if (residentPresentations[j] != null
                        && residentPresentations[j].gameObject != root)
                    {
                        UnityEngine.Object.DestroyImmediate(
                            residentPresentations[j].gameObject);
                    }
                }

                MeleeElementStatusAuraController[] legacyControllers =
                    root.GetComponentsInChildren<MeleeElementStatusAuraController>(true);
                for (int j = 0; j < legacyControllers.Length; j++)
                {
                    if (legacyControllers[j] != null
                        && legacyControllers[j].gameObject != root)
                    {
                        UnityEngine.Object.DestroyImmediate(legacyControllers[j].gameObject);
                    }
                }

                Transform legacyAuraRoot = root.transform.Find("ElementStatusAura");
                if (legacyAuraRoot != null)
                    UnityEngine.Object.DestroyImmediate(legacyAuraRoot.gameObject);

                MeleeElementStatusAuraController owner =
                    root.GetComponent<MeleeElementStatusAuraController>();
                if (owner == null)
                    owner = root.AddComponent<MeleeElementStatusAuraController>();

                ElementalStatusController status = root.GetComponent<ElementalStatusController>();
                if (status == null)
                    throw new InvalidOperationException(path + " 상태 컨트롤러가 누락됐습니다.");

                SerializedObject serializedStatus = new SerializedObject(status);
                serializedStatus.FindProperty("auraController").objectReferenceValue = owner;
                serializedStatus.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(owner);
                EditorUtility.SetDirty(status);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void ValidateRuntimePrefab()
    {
        GameObject runtime = LoadRequiredPrefab(RuntimePrefabPath);
        if (runtime.GetComponent<MeleeElementStatusAuraPresentation>() == null
            || runtime.GetComponent<MeleeElementStatusAuraController>() != null
            || runtime.transform.childCount != Specs.Length)
        {
            throw new InvalidOperationException("Resources Aura presentation 프리팹 구성이 잘못됐습니다.");
        }
    }

    private static void ValidateMonsterAuraOwners()
    {
        for (int i = 0; i < MonsterPrefabPaths.Length; i++)
        {
            string path = MonsterPrefabPaths[i];
            GameObject root = LoadRequiredPrefab(path);
            MeleeElementStatusAuraController owner =
                root.GetComponent<MeleeElementStatusAuraController>();
            ElementalStatusController status = root.GetComponent<ElementalStatusController>();
            if (owner == null || status == null)
                throw new InvalidOperationException(path + " Aura logical owner가 누락됐습니다.");
            if (root.GetComponentInChildren<MeleeElementStatusAuraPresentation>(true) != null)
                throw new InvalidOperationException(path + " 상주 Aura presentation이 남았습니다.");

            SerializedObject serializedStatus = new SerializedObject(status);
            if (serializedStatus.FindProperty("auraController").objectReferenceValue != owner)
                throw new InvalidOperationException(path + " Aura logical owner 참조가 잘못됐습니다.");
        }
    }

    private static GameObject FindSlot(GameObject root, MeleeElementStatusAuraType auraType)
    {
        Transform slot = root.transform.Find(auraType + "_Aura");
        if (slot == null)
            throw new InvalidOperationException($"상태 Aura 슬롯 누락: {auraType}");
        return slot.gameObject;
    }

    private static bool HasAliveParticles(GameObject root)
    {
        ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i].IsAlive(true))
                return true;
        }
        return false;
    }

    private static int CountActiveChildren(Transform root)
    {
        int count = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).gameObject.activeSelf)
                count++;
        }
        return count;
    }

    private static void ValidateDependencies()
    {
        string[] dependencies = AssetDatabase.GetDependencies(FormalPrefabPath, true);
        for (int i = 0; i < dependencies.Length; i++)
        {
            if (dependencies[i].StartsWith("Assets/Temp", StringComparison.OrdinalIgnoreCase)
                || dependencies[i].StartsWith("Assets/Legacy", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"상태 Aura 정식 프리팹 금지 의존성: {dependencies[i]}");
            }
        }
    }

    private static GameObject LoadRequiredPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new InvalidOperationException($"Prefab 누락: {path}");
        return prefab;
    }

    private static void NormalizeTransform(Transform target)
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
            throw new InvalidOperationException($"상태 Aura Transform 비정규화: {label}");
        }
    }

    private static void ValidateSourceRootPreserved(
        Transform current,
        Transform source,
        MeleeElementStatusAuraType auraType)
    {
        if ((current.localPosition - source.localPosition).sqrMagnitude > 0.000001f
            || Quaternion.Angle(current.localRotation, source.localRotation) > 0.001f
            || (current.localScale - source.localScale).sqrMagnitude > 0.000001f)
        {
            throw new InvalidOperationException($"상태 Aura 원본 루트 변경 감지: {auraType}");
        }
    }

    private static float EstimatePlaybackSeconds(ParticleSystem.MainModule main)
    {
        if (main.loop)
            return 0f; // 루프 Aura는 상태 해제 API가 수명을 소유

        float speed = Mathf.Max(0.0001f, main.simulationSpeed);
        return (ResolveCurveMaximum(main.startDelay)
                + main.duration
                + ResolveCurveMaximum(main.startLifetime)) / speed;
    }

    private static float ResolveCurveMaximum(ParticleSystem.MinMaxCurve curve)
    {
        float maximum = Mathf.Max(curve.constantMin, curve.constantMax);
        if (curve.curve != null)
            maximum = Mathf.Max(maximum, ResolveAnimationCurveMaximum(curve.curve) * curve.curveMultiplier);
        if (curve.curveMin != null)
            maximum = Mathf.Max(maximum, ResolveAnimationCurveMaximum(curve.curveMin) * curve.curveMultiplier);
        if (curve.curveMax != null)
            maximum = Mathf.Max(maximum, ResolveAnimationCurveMaximum(curve.curveMax) * curve.curveMultiplier);
        return maximum;
    }

    private static float ResolveAnimationCurveMaximum(AnimationCurve curve)
    {
        if (curve == null || curve.length == 0)
            return 0f;

        float maximum = 0f;
        Keyframe[] keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
            maximum = Mathf.Max(maximum, keys[i].value);
        return maximum;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new InvalidOperationException($"폴더 경로 오류: {folder}");
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private readonly struct AuraSpec
    {
        public AuraSpec(MeleeElementStatusAuraType auraType, string sourcePath)
        {
            AuraType = auraType;
            SourcePath = sourcePath;
        }

        public MeleeElementStatusAuraType AuraType { get; }
        public string SourcePath { get; }
        public string ModulePath =>
            $"{ModuleRoot}/PF_VFX_MeleeElementStatusAura_{AuraType}Module.prefab";
    }
}
