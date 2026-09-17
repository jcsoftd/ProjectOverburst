using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MeleeElementCircularSlashSetupUtility
{
    private const float CircularStartOffsetSeconds = 0f;
    private const string FormalRoot =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementSlash";
    private const string GeneralModuleRoot = FormalRoot + "/Modules";
    private const string CircularModuleRoot = FormalRoot + "/CircularModules";
    private const string GeneralPrefabPath =
        FormalRoot + "/PF_VFX_MeleeElementSlash.prefab";
    private const string CircularPrefabPath =
        FormalRoot + "/PF_VFX_MeleeElementCircularSlash.prefab";

    private static readonly ElementSpec[] Specs =
    {
        new ElementSpec(
            WeaponElement.None,
            "neutralSlash",
            "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/MeleeSlash/PF_VFX_MeleeSlash_Base.prefab"),
        new ElementSpec(
            WeaponElement.Fire,
            "fireSlash",
            "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/" +
            "ARPG Realistic Essentials Fire/Prefabs/Melee/Red_Fire/Slash_Circle_Shape_Heavy.prefab"),
        new ElementSpec(
            WeaponElement.Water,
            "waterSlash",
            "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/" +
            "ARPG Realistic Ice and Water pack/Water/Water_Slash.prefab"),
        new ElementSpec(
            WeaponElement.Ice,
            "iceSlash",
            "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/" +
            "ARPG Realistic Ice and Water pack/Ice/Ice_Slash.prefab"),
        new ElementSpec(
            WeaponElement.Electric,
            "electricSlash",
            "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/" +
            "ARPG Realistic Lightning Starter Kit/Purple/1) slash Purple.prefab"),
        new ElementSpec(
            WeaponElement.Wind,
            "windSlash",
            "Assets/ThirdParty/06_VFX/UniqueVFXUltra/UniqueSwordSlashesVol_1/" +
            "Prefabs/vfx_SwordSlash10.prefab"),
        new ElementSpec(
            WeaponElement.Earth,
            "earthSlash",
            null)
    };

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Element Circular Slash")]
    public static void RunFromMenu()
    {
        RunOnceFromCommandLine();
    }

    public static void RunOnceFromCommandLine()
    {
        EnsureFolder(GeneralModuleRoot);
        EnsureFolder(CircularModuleRoot);
        CreateEmptyGeneralModules();
        CreateCircularModules();
        NormalizeCircularSourceRoots();
        SynchronizeGeneralSharedPrefab();
        SynchronizeCircularSharedPrefab();
        ApplyPlaybackOffset();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateFromCommandLine();
        Debug.Log("[ProjectVTP] 원본 중첩형 공용 원소 원형 슬래시 정식화/검증 완료.");
    }

    public static bool NeedsEarthSlotSetup()
    {
        ElementSpec earth = Specs[Specs.Length - 1];
        if (AssetDatabase.LoadAssetAtPath<GameObject>(earth.GeneralModulePath) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(earth.CircularModulePath) == null)
        {
            return true;
        }

        return !HasControllerSlot(GeneralPrefabPath, WeaponElement.Earth)
            || !HasControllerSlot(CircularPrefabPath, WeaponElement.Earth);
    }

    public static void EnsureEarthSlotsFromCommandLine()
    {
        EnsureFolder(GeneralModuleRoot);
        EnsureFolder(CircularModuleRoot);
        CreateEmptyGeneralModules();
        CreateCircularModules();
        SynchronizeGeneralSharedPrefab();
        SynchronizeCircularSharedPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateEarthSlotsFromCommandLine();
    }

    public static void ValidateEarthSlotsFromCommandLine()
    {
        ElementSpec earth = Specs[Specs.Length - 1];
        ValidateGeneralEarthSlot();

        GameObject shared = LoadRequiredPrefab(CircularPrefabPath);
        MeleeElementSlashController controller =
            shared.GetComponent<MeleeElementSlashController>();
        GameObject module = LoadRequiredPrefab(earth.CircularModulePath);
        GameObject slot = controller != null
            ? controller.GetElementObject(WeaponElement.Earth)
            : null;
        if (slot == null
            || PrefabUtility.GetCorrespondingObjectFromSource(slot) != module
            || slot.activeSelf)
        {
            throw new InvalidOperationException("원형 대지 Slash 슬롯 연결 실패");
        }

        ValidateNormalizedTransform(slot.transform, "Earth Circular Slot");
        ValidateCircularModule(module, earth);
        ValidateDependencies();
        Debug.Log("[ProjectVTP] 대지 일반/원형 Slash 빈 슬롯 연결 검증 완료.");
    }

    [MenuItem("OVERBURST/Codex/Validate/Element Circular Slash")]
    public static void ValidateFromMenu()
    {
        ValidateFromCommandLine();
    }

    public static void ValidateFromCommandLine()
    {
        GameObject sharedPrefab = LoadRequiredPrefab(CircularPrefabPath);
        MeleeElementSlashController controller =
            sharedPrefab.GetComponent<MeleeElementSlashController>();
        if (controller == null || sharedPrefab.activeSelf)
            throw new InvalidOperationException("원형 공용 프리팹의 안전 기본 상태가 잘못됐습니다.");
        SerializedObject serializedController = new SerializedObject(controller);
        if (!Mathf.Approximately(
                serializedController.FindProperty("startOffsetSeconds").floatValue,
                CircularStartOffsetSeconds))
        {
            throw new InvalidOperationException("원형 원소 Slash 선행 재생 오프셋은 0초여야 합니다.");
        }

        if (sharedPrefab.transform.childCount != Specs.Length)
            throw new InvalidOperationException(
                $"원형 공용 프리팹 자식 수가 {Specs.Length}개가 아닙니다.");

        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            GameObject module = LoadRequiredPrefab(spec.CircularModulePath);
            GameObject child = controller.GetElementObject(spec.Element);
            if (child == null || PrefabUtility.GetCorrespondingObjectFromSource(child) != module)
                throw new InvalidOperationException($"원형 모듈 참조 실패: {spec.Element}");
            ValidateNormalizedTransform(child.transform, spec.Element.ToString());
            ValidateCircularModule(module, spec);
        }

        ValidateGeneralEarthSlot();
        ValidateInstanceSelectionAndRestart(sharedPrefab);
        ValidateDependencies();
        Debug.Log("[ProjectVTP] 원형 Slash 원본 중첩/단일 활성/재시작 검증 완료.");
    }

    public static void ApplyPlaybackOffsetFromCommandLine()
    {
        ApplyPlaybackOffset();
        AssetDatabase.SaveAssets();
        ValidatePlaybackOffset();
    }

    [MenuItem("OVERBURST/Codex/Inspect/Element Slash Module State")]
    public static void InspectFromMenu()
    {
        InspectFromCommandLine();
    }

    public static void InspectFromCommandLine()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            GameObject prefab = LoadRequiredPrefab(spec.GeneralModulePath);
            ParticleSystemRenderer[] renderers =
                prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            var inactivePaths = new List<string>();
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                if (!transforms[transformIndex].gameObject.activeSelf)
                    inactivePaths.Add(GetPath(prefab.transform, transforms[transformIndex]));
            }

            Debug.Log(
                $"[ElementSlashInspect] {spec.Element} renderers={renderers.Length} " +
                $"inactiveSelf={inactivePaths.Count} [{string.Join(", ", inactivePaths)}]");
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                ParticleSystemRenderer renderer = renderers[rendererIndex];
                Mesh mesh = renderer.mesh;
                Debug.Log(
                    $"[ElementSlashInspect] {spec.Element}/{GetPath(prefab.transform, renderer.transform)} " +
                    $"activeSelf={renderer.gameObject.activeSelf} mode={renderer.renderMode} " +
                    $"mesh={(mesh != null ? mesh.name : "<null>")} " +
                    $"meshPath={(mesh != null ? AssetDatabase.GetAssetPath(mesh) : "<null>")}");
            }
        }
    }

    private static void ApplyPlaybackOffset()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(CircularPrefabPath);
        try
        {
            MeleeElementSlashController controller =
                root.GetComponent<MeleeElementSlashController>();
            if (controller == null)
                throw new InvalidOperationException("원형 원소 Slash 컨트롤러가 없습니다.");

            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("startOffsetSeconds").floatValue =
                CircularStartOffsetSeconds;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, CircularPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidatePlaybackOffset()
    {
        GameObject root = LoadRequiredPrefab(CircularPrefabPath);
        MeleeElementSlashController controller =
            root.GetComponent<MeleeElementSlashController>();
        if (controller == null)
            throw new InvalidOperationException("원형 원소 Slash 컨트롤러가 없습니다.");

        SerializedObject serializedController = new SerializedObject(controller);
        if (!Mathf.Approximately(
                serializedController.FindProperty("startOffsetSeconds").floatValue,
                CircularStartOffsetSeconds))
        {
            throw new InvalidOperationException("원형 원소 Slash 선행 재생 오프셋은 0초여야 합니다.");
        }
    }

    private static void CreateEmptyGeneralModules()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            if (spec.HasSource
                || AssetDatabase.LoadAssetAtPath<GameObject>(spec.GeneralModulePath) != null)
            {
                continue;
            }

            CreateEmptyContentModule(spec.GeneralModulePath);
        }
    }

    private static void CreateCircularModules()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.CircularModulePath) != null)
                continue; // 원형 모듈 수동 조정 보존

            if (!spec.HasSource)
            {
                CreateEmptyContentModule(spec.CircularModulePath);
                continue;
            }

            GameObject sourcePrefab = LoadRequiredPrefab(spec.SourcePrefabPath);
            GameObject root = new GameObject(
                Path.GetFileNameWithoutExtension(spec.CircularModulePath));
            try
            {
                GameObject sourceInstance =
                    PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
                if (sourceInstance == null)
                    throw new InvalidOperationException($"원형 원본 인스턴스 생성 실패: {spec.Element}");

                sourceInstance.transform.SetParent(root.transform, false);
                NormalizeTransform(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, spec.CircularModulePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    private static void SynchronizeGeneralSharedPrefab()
    {
        ElementSpec earth = Specs[Specs.Length - 1];
        SynchronizeSharedSlot(
            GeneralPrefabPath,
            earth.GeneralModulePath,
            earth.GeneralChildName,
            earth.ControllerField);
    }

    private static void SynchronizeCircularSharedPrefab()
    {
        CreateCircularSharedPrefab();
        ElementSpec earth = Specs[Specs.Length - 1];
        SynchronizeSharedSlot(
            CircularPrefabPath,
            earth.CircularModulePath,
            earth.ChildName,
            earth.ControllerField);
    }

    private static void CreateCircularSharedPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CircularPrefabPath) != null)
            return; // 정식 프리팹 수동 조정 보존

        GameObject root = new GameObject("PF_VFX_MeleeElementCircularSlash");
        try
        {
            MeleeElementSlashController controller =
                root.AddComponent<MeleeElementSlashController>();
            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("selectedElement").enumValueIndex =
                (int)WeaponElement.None;
            serializedController.FindProperty("startOffsetSeconds").floatValue =
                CircularStartOffsetSeconds;

            for (int i = 0; i < Specs.Length; i++)
            {
                ElementSpec spec = Specs[i];
                GameObject module = LoadRequiredPrefab(spec.CircularModulePath);
                GameObject child = PrefabUtility.InstantiatePrefab(module) as GameObject;
                if (child == null)
                    throw new InvalidOperationException($"원형 모듈 인스턴스 생성 실패: {spec.Element}");

                child.name = spec.ChildName;
                child.transform.SetParent(root.transform, false);
                NormalizeTransform(child.transform);
                child.SetActive(spec.Element == WeaponElement.None); // 저장 기본값
                serializedController.FindProperty(spec.ControllerField).objectReferenceValue = child;
            }

            serializedController.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false); // 원소 주입 전 재생 차단
            PrefabUtility.SaveAsPrefabAsset(root, CircularPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SynchronizeSharedSlot(
        string sharedPrefabPath,
        string modulePath,
        string childName,
        string controllerField)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(sharedPrefabPath);
        if (root == null)
            throw new InvalidOperationException($"공용 Slash 프리팹 누락: {sharedPrefabPath}");

        try
        {
            MeleeElementSlashController controller =
                root.GetComponent<MeleeElementSlashController>();
            if (controller == null)
                throw new InvalidOperationException($"Slash Controller 누락: {sharedPrefabPath}");

            SerializedObject serializedController = new SerializedObject(controller);
            SerializedProperty slotProperty =
                serializedController.FindProperty(controllerField);
            if (slotProperty == null)
                throw new InvalidOperationException($"Slash Controller 슬롯 누락: {controllerField}");

            GameObject module = LoadRequiredPrefab(modulePath);
            GameObject child = slotProperty.objectReferenceValue as GameObject;
            if (child == null)
            {
                Transform existing = root.transform.Find(childName);
                if (existing != null
                    && PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) == module)
                {
                    child = existing.gameObject;
                }
                else
                {
                    child = PrefabUtility.InstantiatePrefab(module, root.scene) as GameObject;
                    if (child == null)
                        throw new InvalidOperationException($"대지 Slash 슬롯 생성 실패: {modulePath}");

                    child.name = childName;
                    child.transform.SetParent(root.transform, false);
                    NormalizeTransform(child.transform);
                }

                child.SetActive(false);
                slotProperty.objectReferenceValue = child;
                serializedController.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, sharedPrefabPath);
            }
            else if (PrefabUtility.GetCorrespondingObjectFromSource(child) != module)
            {
                throw new InvalidOperationException($"대지 Slash 슬롯 참조 불일치: {sharedPrefabPath}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void NormalizeCircularSourceRoots()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            GameObject root = PrefabUtility.LoadPrefabContents(spec.CircularModulePath);
            try
            {
                if (root.transform.childCount != 1)
                    throw new InvalidOperationException($"원형 모듈 원본 수 오류: {spec.Element}");
                NormalizeTransform(root.transform.GetChild(0)); // 원본 전체 루트만 정렬
                PrefabUtility.SaveAsPrefabAsset(root, spec.CircularModulePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void ValidateCircularModule(GameObject module, ElementSpec spec)
    {
        if (module.transform.childCount != 1)
            throw new InvalidOperationException($"원형 모듈 원본 수가 1개가 아닙니다: {spec.Element}");

        GameObject sourceInstance = module.transform.GetChild(0).gameObject;
        if (!spec.HasSource)
        {
            if (sourceInstance.name != "VFX_CONTENT")
                throw new InvalidOperationException($"빈 원형 Slash 슬롯 구조 불일치: {spec.Element}");
            ValidateNormalizedTransform(sourceInstance.transform, spec.Element + " VFX_CONTENT");
            return;
        }

        GameObject sourcePrefab = LoadRequiredPrefab(spec.SourcePrefabPath);
        if (PrefabUtility.GetCorrespondingObjectFromSource(sourceInstance) != sourcePrefab)
            throw new InvalidOperationException($"원형 원본 중첩 참조 실패: {spec.Element}");
        ValidateNormalizedTransform(sourceInstance.transform, spec.Element + " SourceRoot");

        ParticleSystem[] particles = sourceInstance.GetComponentsInChildren<ParticleSystem>(true);
        if (particles.Length == 0)
            throw new InvalidOperationException($"ParticleSystem 없는 원형 원본: {spec.Element}");

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem sourceParticle =
                PrefabUtility.GetCorrespondingObjectFromSource(particles[i]);
            if (sourceParticle == null)
                throw new InvalidOperationException($"원형 ParticleSystem 원본 참조 누락: {spec.Element}");
            ValidateParticleShapeUnchanged(particles[i], sourceParticle, spec.Element);
        }

        ParticleSystemRenderer[] renderers =
            sourceInstance.GetComponentsInChildren<ParticleSystemRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            ParticleSystemRenderer sourceRenderer =
                PrefabUtility.GetCorrespondingObjectFromSource(renderers[i]);
            if (sourceRenderer == null)
                throw new InvalidOperationException($"원형 Renderer 원본 참조 누락: {spec.Element}");
            ValidateRendererUnchanged(renderers[i], sourceRenderer, spec.Element);
        }

        Transform[] transforms = sourceInstance.GetComponentsInChildren<Transform>(true);
        for (int i = 1; i < transforms.Length; i++)
        {
            Transform sourceTransform =
                PrefabUtility.GetCorrespondingObjectFromSource(transforms[i]);
            if (sourceTransform == null
                || transforms[i].localPosition != sourceTransform.localPosition
                || transforms[i].localRotation != sourceTransform.localRotation
                || transforms[i].localScale != sourceTransform.localScale)
            {
                throw new InvalidOperationException($"원형 내부 Transform 변경 감지: {spec.Element}");
            }
        }
    }

    private static void ValidateParticleShapeUnchanged(
        ParticleSystem current,
        ParticleSystem source,
        WeaponElement element)
    {
        ParticleSystem.ShapeModule currentShape = current.shape;
        ParticleSystem.ShapeModule sourceShape = source.shape;
        if (currentShape.enabled != sourceShape.enabled
            || currentShape.shapeType != sourceShape.shapeType
            || !Mathf.Approximately(currentShape.arc, sourceShape.arc)
            || currentShape.arcMode != sourceShape.arcMode
            || !Mathf.Approximately(currentShape.arcSpread, sourceShape.arcSpread)
            || currentShape.position != sourceShape.position
            || currentShape.rotation != sourceShape.rotation
            || currentShape.scale != sourceShape.scale)
        {
            throw new InvalidOperationException($"원형 Shape 변경 감지: {element}/{current.name}");
        }
    }

    private static void ValidateRendererUnchanged(
        ParticleSystemRenderer current,
        ParticleSystemRenderer source,
        WeaponElement element)
    {
        if (current.renderMode != source.renderMode
            || current.mesh != source.mesh
            || current.flip != source.flip
            || current.pivot != source.pivot
            || current.alignment != source.alignment)
        {
            throw new InvalidOperationException($"원형 Renderer 변경 감지: {element}/{current.name}");
        }
    }

    private static void ValidateInstanceSelectionAndRestart(GameObject prefab)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
            throw new InvalidOperationException("원형 공용 프리팹 검증 인스턴스 생성 실패.");

        try
        {
            instance.SetActive(true);
            MeleeElementSlashController controller =
                instance.GetComponent<MeleeElementSlashController>();
            for (int i = 0; i < Specs.Length; i++)
            {
                ElementSpec spec = Specs[i];
                controller.Play(spec.Element);
                GameObject selected = controller.GetElementObject(spec.Element);
                if (CountActiveChildren(instance.transform) != 1 || !selected.activeSelf)
                    throw new InvalidOperationException($"원형 원소 단일 활성 실패: {spec.Element}");

                ParticleSystem[] particles = selected.GetComponentsInChildren<ParticleSystem>(true);
                bool playing = false;
                for (int particleIndex = 0; particleIndex < particles.Length; particleIndex++)
                    playing |= particles[particleIndex].isPlaying;
                if (particles.Length > 0 && !playing)
                    throw new InvalidOperationException($"원형 ParticleSystem 재시작 실패: {spec.Element}");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void ValidateGeneralEarthSlot()
    {
        ElementSpec earth = Specs[Specs.Length - 1];
        GameObject shared = LoadRequiredPrefab(GeneralPrefabPath);
        MeleeElementSlashController controller =
            shared.GetComponent<MeleeElementSlashController>();
        GameObject module = LoadRequiredPrefab(earth.GeneralModulePath);
        GameObject slot = controller != null
            ? controller.GetElementObject(WeaponElement.Earth)
            : null;

        if (slot == null
            || PrefabUtility.GetCorrespondingObjectFromSource(slot) != module
            || slot.activeSelf)
        {
            throw new InvalidOperationException("일반 대지 Slash 슬롯 연결 실패");
        }

        ValidateNormalizedTransform(slot.transform, "Earth General Slot");
        ValidateEmptyContentModule(module, "Earth General Module");
    }

    private static bool HasControllerSlot(string sharedPrefabPath, WeaponElement element)
    {
        GameObject shared = AssetDatabase.LoadAssetAtPath<GameObject>(sharedPrefabPath);
        MeleeElementSlashController controller =
            shared != null ? shared.GetComponent<MeleeElementSlashController>() : null;
        GameObject slot = controller != null ? controller.GetElementObject(element) : null;
        if (slot == null)
            return false;

        ElementSpec earth = Specs[Specs.Length - 1];
        string modulePath = sharedPrefabPath == GeneralPrefabPath
            ? earth.GeneralModulePath
            : earth.CircularModulePath;
        GameObject module = AssetDatabase.LoadAssetAtPath<GameObject>(modulePath);
        return module != null
            && PrefabUtility.GetCorrespondingObjectFromSource(slot) == module;
    }

    private static void CreateEmptyContentModule(string modulePath)
    {
        GameObject root = new GameObject(Path.GetFileNameWithoutExtension(modulePath));
        try
        {
            GameObject content = new GameObject("VFX_CONTENT");
            content.transform.SetParent(root.transform, false);
            NormalizeTransform(content.transform);
            NormalizeTransform(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, modulePath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ValidateEmptyContentModule(GameObject module, string label)
    {
        if (module.transform.childCount != 1
            || module.transform.GetChild(0).name != "VFX_CONTENT")
        {
            throw new InvalidOperationException($"빈 VFX_CONTENT 슬롯 구조 불일치: {label}");
        }

        ValidateNormalizedTransform(module.transform.GetChild(0), label + " VFX_CONTENT");
    }

    private static void ValidateDependencies()
    {
        string[] dependencies = AssetDatabase.GetDependencies(CircularPrefabPath, true);
        for (int i = 0; i < dependencies.Length; i++)
        {
            if (dependencies[i].StartsWith("Assets/Temp", StringComparison.OrdinalIgnoreCase)
                || dependencies[i].StartsWith("Assets/Legacy", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"원형 공용 프리팹 금지 의존성: {dependencies[i]}");
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
            throw new InvalidOperationException($"원형 모듈 Transform 비정규화: {label}");
        }
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

    private static string GetPath(Transform root, Transform target)
    {
        if (target == root)
            return root.name;

        string path = target.name;
        Transform current = target.parent;
        while (current != null && current != root)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private readonly struct ElementSpec
    {
        public ElementSpec(
            WeaponElement element,
            string controllerField,
            string sourcePrefabPath)
        {
            Element = element;
            ControllerField = controllerField;
            SourcePrefabPath = sourcePrefabPath;
        }

        public WeaponElement Element { get; }
        public string ControllerField { get; }
        public string SourcePrefabPath { get; }
        public bool HasSource => !string.IsNullOrEmpty(SourcePrefabPath);
        public string GeneralModulePath =>
            $"{GeneralModuleRoot}/PF_VFX_MeleeElementSlash_{Element}Module.prefab";
        public string CircularModulePath =>
            $"{CircularModuleRoot}/PF_VFX_MeleeElementCircularSlash_{Element}Module.prefab";
        public string GeneralChildName => $"{Element}_Slash";
        public string ChildName => $"{Element}_CircularSlash";
    }
}
