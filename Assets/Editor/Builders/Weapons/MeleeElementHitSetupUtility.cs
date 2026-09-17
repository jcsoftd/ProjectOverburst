using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MeleeElementHitSetupUtility
{
    private const string FormalRoot =
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementHit";
    private const string ModuleRoot = FormalRoot + "/Modules";
    private const string FormalPrefabPath = FormalRoot + "/PF_VFX_MeleeElementHit.prefab";
    private const string CatalogPath =
        "Assets/ProjectOverburst/Resources/Combat/VFX/MeleeElementHitVfxCatalog.asset";
    private const string ReportPath = "Logs/MeleeElementHitSetupReport.txt";
    private const float SharedHitScale = 0.7f;

    private static readonly ElementSpec[] Specs =
    {
        new ElementSpec(WeaponElement.None, null),
        new ElementSpec(
            WeaponElement.Fire,
            "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/" +
            "ARPG Realistic Essentials Fire/Prefabs/Melee/Red_Fire/Hit_Nova_Heavy.prefab"),
        new ElementSpec(
            WeaponElement.Water,
            "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/" +
            "ARPG Realistic Ice and Water pack/Water/Water_Hit_FX.prefab"),
        new ElementSpec(
            WeaponElement.Ice,
            "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/" +
            "ARPG Realistic Ice and Water pack/Ice/Ice_Hit_FX.prefab"),
        new ElementSpec(
            WeaponElement.Electric,
            "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle/" +
            "ARPG Realistic Lightning Starter Kit/Purple/1) Melee Hit Purple.prefab"),
        new ElementSpec(WeaponElement.Wind, null),
        new ElementSpec(WeaponElement.Earth, null, true)
    };

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Element Hit VFX")]
    public static void RunFromMenu()
    {
        RunOnceFromCommandLine();
    }

    public static void RunOnceFromCommandLine()
    {
        EnsureFolder(ModuleRoot);
        CreateModules();
        NormalizeWrapperOverrides();
        CreateSharedPrefab();
        ApplySharedPrefabScale();
        ConnectRuntimeController();
        CreateRuntimeCatalog();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateFromCommandLine();
        WriteReport();
        Debug.Log("[ProjectVTP] 공용 원소 Hit VFX 정식화/검증 완료.");
    }

    public static bool NeedsEarthSlotSetup()
    {
        ElementSpec earth = Specs[Specs.Length - 1];
        GameObject module =
            AssetDatabase.LoadAssetAtPath<GameObject>(earth.ModulePath);
        GameObject shared =
            AssetDatabase.LoadAssetAtPath<GameObject>(FormalPrefabPath);
        MeleeElementHitVfxController controller =
            shared != null ? shared.GetComponent<MeleeElementHitVfxController>() : null;
        GameObject slot = controller != null
            ? controller.GetElementObject(WeaponElement.Earth)
            : null;
        MeleeElementHitVfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<MeleeElementHitVfxCatalog>(CatalogPath);

        return module == null
            || slot == null
            || PrefabUtility.GetCorrespondingObjectFromSource(slot) != module
            || catalog == null
            || catalog.sharedHitPrefab != shared
            || catalog.ResolveLifetime(WeaponElement.Earth) <= 0f;
    }

    public static void EnsureEarthSlotFromCommandLine()
    {
        EnsureFolder(ModuleRoot);
        CreateModules();
        CreateSharedPrefab();
        ConnectRuntimeController();
        CreateRuntimeCatalog();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateEarthSlotFromCommandLine();
    }

    public static void ValidateEarthSlotFromCommandLine()
    {
        ElementSpec earth = Specs[Specs.Length - 1];
        GameObject module = LoadRequiredPrefab(earth.ModulePath);
        GameObject shared = LoadRequiredPrefab(FormalPrefabPath);
        MeleeElementHitVfxController controller =
            shared.GetComponent<MeleeElementHitVfxController>();
        GameObject slot = controller != null
            ? controller.GetElementObject(WeaponElement.Earth)
            : null;
        if (slot == null
            || PrefabUtility.GetCorrespondingObjectFromSource(slot) != module
            || slot.activeSelf)
        {
            throw new InvalidOperationException("대지 Hit 슬롯 연결 실패");
        }

        ValidateNormalizedTransform(slot.transform, "Earth Hit Slot");
        ValidateModule(module, earth);

        MeleeElementHitVfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<MeleeElementHitVfxCatalog>(CatalogPath);
        if (catalog == null
            || !catalog.TryResolve(WeaponElement.Earth, out GameObject resolved)
            || resolved != shared
            || catalog.ResolveLifetime(WeaponElement.Earth) <= 0f)
        {
            throw new InvalidOperationException("대지 Hit 카탈로그 연결 실패");
        }

        Debug.Log("[ProjectVTP] 대지 Hit 빈 슬롯과 런타임 카탈로그 연결 검증 완료.");
    }

    [MenuItem("OVERBURST/Codex/Validate/Element Hit VFX")]
    public static void ValidateFromMenu()
    {
        ValidateFromCommandLine();
    }

    public static void ValidateFromCommandLine()
    {
        GameObject shared = LoadRequiredPrefab(FormalPrefabPath);
        if (!shared.activeSelf || shared.transform.childCount != Specs.Length)
            throw new InvalidOperationException(
                $"공용 원소 Hit 루트 또는 {Specs.Length}슬롯 구성이 잘못됐습니다.");
        ValidateSharedPrefabTransform(shared.transform);
        ValidateRuntimeController(shared);
        ValidateRuntimeCatalog(shared);

        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            GameObject module = LoadRequiredPrefab(spec.ModulePath);
            GameObject child = shared.transform.GetChild(i).gameObject;
            if (PrefabUtility.GetCorrespondingObjectFromSource(child) != module
                || child.activeSelf)
            {
                throw new InvalidOperationException($"Hit 모듈 슬롯 연결/기본 비활성 실패: {spec.Element}");
            }

            ValidateNormalizedTransform(child.transform, spec.Element + " Slot");
            ValidateModule(module, spec);
        }

        ValidateSingleSelectionAndRestart(shared);
        ValidateDependencies();
        Debug.Log(
            $"[ProjectVTP] 공용 원소 Hit {Specs.Length}슬롯/원본 참조/단일 활성 검증 완료.");
    }

    private static void CreateModules()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModulePath) != null)
                continue; // 정식 모듈 수동 조정 보존

            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(spec.ModulePath));
            try
            {
                if (spec.HasSource)
                {
                    GameObject source = LoadRequiredPrefab(spec.SourcePath);
                    GameObject sourceInstance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                    if (sourceInstance == null)
                        throw new InvalidOperationException($"Hit 원본 인스턴스 생성 실패: {spec.Element}");
                    sourceInstance.transform.SetParent(root.transform, false);
                }
                else if (spec.CreatesContentSlot)
                {
                    GameObject content = new GameObject("VFX_CONTENT");
                    content.transform.SetParent(root.transform, false);
                    NormalizeTransform(content.transform);
                }

                NormalizeTransform(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, spec.ModulePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    private static void NormalizeWrapperOverrides()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            GameObject root = PrefabUtility.LoadPrefabContents(spec.ModulePath);
            try
            {
                NormalizeTransform(root.transform);
                if (spec.HasSource)
                {
                    if (root.transform.childCount != 1)
                        throw new InvalidOperationException($"Hit 원본 수 오류: {spec.Element}");

                    Transform sourceRoot = root.transform.GetChild(0);
                    NormalizeTransform(sourceRoot); // 피격점 기준 전체 루트만 정렬
                    ParticleSystem[] particles =
                        sourceRoot.GetComponentsInChildren<ParticleSystem>(true);
                    for (int particleIndex = 0; particleIndex < particles.Length; particleIndex++)
                    {
                        ParticleSystem.MainModule main = particles[particleIndex].main;
                        if (main.loop)
                            main.loop = false; // Hit 잔존 방지 wrapper override
                    }
                }
                else if (spec.CreatesContentSlot)
                {
                    if (root.transform.childCount != 1
                        || root.transform.GetChild(0).name != "VFX_CONTENT")
                    {
                        throw new InvalidOperationException(
                            $"Hit VFX_CONTENT 슬롯 구조 불일치: {spec.Element}");
                    }

                    NormalizeTransform(root.transform.GetChild(0));
                }
                else if (root.transform.childCount != 0)
                {
                    throw new InvalidOperationException($"빈 Hit 확장 슬롯에 원본이 있습니다: {spec.Element}");
                }

                PrefabUtility.SaveAsPrefabAsset(root, spec.ModulePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void CreateSharedPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FormalPrefabPath) != null)
        {
            SynchronizeEarthHitSlot();
            return; // 기존 슬롯과 사용자 조정은 유지한다
        }

        GameObject root = new GameObject("PF_VFX_MeleeElementHit");
        try
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                ElementSpec spec = Specs[i];
                GameObject module = LoadRequiredPrefab(spec.ModulePath);
                GameObject child = PrefabUtility.InstantiatePrefab(module) as GameObject;
                if (child == null)
                    throw new InvalidOperationException($"Hit 모듈 슬롯 생성 실패: {spec.Element}");

                child.name = spec.Element + "_Hit";
                child.transform.SetParent(root.transform, false);
                NormalizeTransform(child.transform);
                child.SetActive(false); // 10번대 선택 컴포넌트 연결 전 안전 기본값
            }

            NormalizeTransform(root.transform);
            root.transform.localScale = Vector3.one * SharedHitScale;
            PrefabUtility.SaveAsPrefabAsset(root, FormalPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SynchronizeEarthHitSlot()
    {
        ElementSpec earth = Specs[Specs.Length - 1];
        GameObject root = PrefabUtility.LoadPrefabContents(FormalPrefabPath);
        if (root == null)
            throw new InvalidOperationException($"공용 Hit 프리팹 누락: {FormalPrefabPath}");

        try
        {
            GameObject module = LoadRequiredPrefab(earth.ModulePath);
            Transform existing = root.transform.Find(WeaponElement.Earth + "_Hit");
            if (existing != null)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) != module)
                    throw new InvalidOperationException("대지 Hit 슬롯 참조 불일치");
                return;
            }

            GameObject child = PrefabUtility.InstantiatePrefab(module, root.scene) as GameObject;
            if (child == null)
                throw new InvalidOperationException("대지 Hit 슬롯 생성 실패");

            child.name = WeaponElement.Earth + "_Hit";
            child.transform.SetParent(root.transform, false);
            NormalizeTransform(child.transform);
            child.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, FormalPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ApplySharedPrefabScale()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(FormalPrefabPath);
        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * SharedHitScale; // Hit 통합 루트만 70%
            PrefabUtility.SaveAsPrefabAsset(root, FormalPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConnectRuntimeController()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(FormalPrefabPath);
        try
        {
            MeleeElementHitVfxController controller =
                root.GetComponent<MeleeElementHitVfxController>();
            if (controller == null)
                controller = root.AddComponent<MeleeElementHitVfxController>();

            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("selectedElement").intValue = (int)WeaponElement.None;
            serialized.FindProperty("fireHit").objectReferenceValue = FindSlot(root, WeaponElement.Fire);
            serialized.FindProperty("waterHit").objectReferenceValue = FindSlot(root, WeaponElement.Water);
            serialized.FindProperty("iceHit").objectReferenceValue = FindSlot(root, WeaponElement.Ice);
            serialized.FindProperty("electricHit").objectReferenceValue = FindSlot(root, WeaponElement.Electric);
            serialized.FindProperty("earthHit").objectReferenceValue = FindSlot(root, WeaponElement.Earth);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            for (int i = 0; i < root.transform.childCount; i++)
                root.transform.GetChild(i).gameObject.SetActive(false); // 저장 시 전체 비활성

            PrefabUtility.SaveAsPrefabAsset(root, FormalPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void CreateRuntimeCatalog()
    {
        MeleeElementHitVfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<MeleeElementHitVfxCatalog>(CatalogPath);
        GameObject shared = LoadRequiredPrefab(FormalPrefabPath);
        if (catalog != null)
        {
            if (catalog.sharedHitPrefab != shared)
                catalog.sharedHitPrefab = shared;

            if (catalog.earthLifetime <= 0f)
                catalog.earthLifetime = 5f;

            EditorUtility.SetDirty(catalog); // 새 Earth 직렬화 필드도 에셋에 명시한다
            return; // 기존 원소 수명과 풀 수동 조정은 유지한다
        }

        catalog = ScriptableObject.CreateInstance<MeleeElementHitVfxCatalog>();
        catalog.sharedHitPrefab = shared;
        catalog.fireLifetime = 5.15f;
        catalog.waterLifetime = 10f;
        catalog.iceLifetime = 10f;
        catalog.electricLifetime = 7.5f;
        catalog.earthLifetime = 5f;
        catalog.poolCapacity = 32;
        AssetDatabase.CreateAsset(catalog, CatalogPath);
    }

    private static void ValidateRuntimeController(GameObject shared)
    {
        MonoBehaviour[] behaviours = shared.GetComponents<MonoBehaviour>();
        if (behaviours.Length != 1 || !(behaviours[0] is MeleeElementHitVfxController controller))
            throw new InvalidOperationException("공용 Hit 런타임 Controller 연결이 잘못됐습니다.");

        WeaponElement[] supported =
        {
            WeaponElement.Fire,
            WeaponElement.Water,
            WeaponElement.Ice,
            WeaponElement.Electric,
            WeaponElement.Earth
        };
        for (int i = 0; i < supported.Length; i++)
        {
            if (controller.GetElementObject(supported[i]) != FindSlot(shared, supported[i]))
                throw new InvalidOperationException($"Hit Controller 슬롯 연결 실패: {supported[i]}");
        }

        if (controller.GetElementObject(WeaponElement.None) != null
            || controller.GetElementObject(WeaponElement.Wind) != null)
        {
            throw new InvalidOperationException("None/Wind Hit 확장 슬롯은 런타임 미지정이어야 합니다.");
        }
    }

    private static void ValidateRuntimeCatalog(GameObject shared)
    {
        MeleeElementHitVfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<MeleeElementHitVfxCatalog>(CatalogPath);
        if (catalog == null || catalog.sharedHitPrefab != shared || catalog.poolCapacity < 1)
            throw new InvalidOperationException("원소 Hit Catalog 연결이 잘못됐습니다.");

        WeaponElement[] supported =
        {
            WeaponElement.Fire,
            WeaponElement.Water,
            WeaponElement.Ice,
            WeaponElement.Electric,
            WeaponElement.Earth
        };
        for (int i = 0; i < supported.Length; i++)
        {
            if (!catalog.TryResolve(supported[i], out GameObject resolved)
                || resolved != shared
                || catalog.ResolveLifetime(supported[i]) <= 0f)
            {
                throw new InvalidOperationException($"원소 Hit Catalog 해결 실패: {supported[i]}");
            }
        }

        if (catalog.TryResolve(WeaponElement.None, out _)
            || catalog.TryResolve(WeaponElement.Wind, out _))
        {
            throw new InvalidOperationException("None/Wind는 신규 원소 Hit를 해결하면 안 됩니다.");
        }
    }

    private static GameObject FindSlot(GameObject root, WeaponElement element)
    {
        Transform slot = root.transform.Find(element + "_Hit");
        if (slot == null)
            throw new InvalidOperationException($"Hit 슬롯 누락: {element}");
        return slot.gameObject;
    }

    private static void ValidateModule(GameObject module, ElementSpec spec)
    {
        int expectedChildren = spec.HasSource || spec.CreatesContentSlot ? 1 : 0;
        if (module.transform.childCount != expectedChildren)
            throw new InvalidOperationException($"Hit 모듈 원본 수 불일치: {spec.Element}");
        if (spec.CreatesContentSlot)
        {
            Transform content = module.transform.GetChild(0);
            if (content.name != "VFX_CONTENT")
                throw new InvalidOperationException($"Hit VFX_CONTENT 슬롯 구조 불일치: {spec.Element}");
            ValidateNormalizedTransform(content, spec.Element + " VFX_CONTENT");
            return;
        }
        if (!spec.HasSource)
            return;

        GameObject source = LoadRequiredPrefab(spec.SourcePath);
        GameObject sourceInstance = module.transform.GetChild(0).gameObject;
        if (PrefabUtility.GetCorrespondingObjectFromSource(sourceInstance) != source)
            throw new InvalidOperationException($"Hit 원본 nested 참조 실패: {spec.Element}");
        ValidateNormalizedTransform(sourceInstance.transform, spec.Element + " SourceRoot");

        ParticleSystem[] particles = sourceInstance.GetComponentsInChildren<ParticleSystem>(true);
        if (particles.Length == 0)
            throw new InvalidOperationException($"ParticleSystem 없는 Hit 원본: {spec.Element}");

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem sourceParticle =
                PrefabUtility.GetCorrespondingObjectFromSource(particles[i]);
            if (sourceParticle == null)
                throw new InvalidOperationException($"Hit Particle 원본 참조 누락: {spec.Element}");
            if (particles[i].main.loop)
                throw new InvalidOperationException($"Hit wrapper Loop 잔존: {spec.Element}/{particles[i].name}");

            ValidateParticleUnchangedExceptLoop(particles[i], sourceParticle, spec.Element);
        }

        ParticleSystemRenderer[] renderers =
            sourceInstance.GetComponentsInChildren<ParticleSystemRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            ParticleSystemRenderer sourceRenderer =
                PrefabUtility.GetCorrespondingObjectFromSource(renderers[i]);
            if (sourceRenderer == null
                || renderers[i].renderMode != sourceRenderer.renderMode
                || renderers[i].mesh != sourceRenderer.mesh
                || renderers[i].flip != sourceRenderer.flip
                || renderers[i].pivot != sourceRenderer.pivot
                || renderers[i].alignment != sourceRenderer.alignment)
            {
                throw new InvalidOperationException($"Hit Renderer 변경 감지: {spec.Element}/{renderers[i].name}");
            }
        }
    }

    private static void ValidateParticleUnchangedExceptLoop(
        ParticleSystem current,
        ParticleSystem source,
        WeaponElement element)
    {
        ParticleSystem.MainModule currentMain = current.main;
        ParticleSystem.MainModule sourceMain = source.main;
        ParticleSystem.ShapeModule currentShape = current.shape;
        ParticleSystem.ShapeModule sourceShape = source.shape;
        if (!Mathf.Approximately(currentMain.duration, sourceMain.duration)
            || currentMain.startDelay.mode != sourceMain.startDelay.mode
            || currentMain.startLifetime.mode != sourceMain.startLifetime.mode
            || currentShape.enabled != sourceShape.enabled
            || currentShape.shapeType != sourceShape.shapeType
            || !Mathf.Approximately(currentShape.arc, sourceShape.arc)
            || currentShape.arcMode != sourceShape.arcMode
            || currentShape.position != sourceShape.position
            || currentShape.rotation != sourceShape.rotation
            || currentShape.scale != sourceShape.scale)
        {
            throw new InvalidOperationException($"Hit Particle 내부 변경 감지: {element}/{current.name}");
        }
    }

    private static void ValidateSingleSelectionAndRestart(GameObject shared)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(shared) as GameObject;
        if (instance == null)
            throw new InvalidOperationException("Hit 통합 검증 인스턴스 생성 실패.");

        try
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                for (int childIndex = 0; childIndex < instance.transform.childCount; childIndex++)
                    instance.transform.GetChild(childIndex).gameObject.SetActive(childIndex == i);
                if (CountActiveChildren(instance.transform) != 1)
                    throw new InvalidOperationException($"Hit 단일 활성 실패: {Specs[i].Element}");

                if (!Specs[i].HasSource)
                    continue;
                ParticleSystem[] particles = instance.transform.GetChild(i)
                    .GetComponentsInChildren<ParticleSystem>(true);
                bool playing = false;
                for (int particleIndex = 0; particleIndex < particles.Length; particleIndex++)
                {
                    particles[particleIndex].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particles[particleIndex].Play(false);
                    playing |= particles[particleIndex].isPlaying;
                }
                if (!playing)
                    throw new InvalidOperationException($"Hit Particle 재시작 실패: {Specs[i].Element}");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void WriteReport()
    {
        var report = new StringBuilder();
        report.AppendLine("Melee Element Hit VFX Setup Report");
        report.AppendLine("Generated=" + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        report.AppendLine("RuntimeController=MeleeElementHitVfxController");
        report.AppendLine("RuntimeCatalog=" + CatalogPath);
        report.AppendLine(
            "SharedPrefabRootScale=" +
            SharedHitScale.ToString("0.###", CultureInfo.InvariantCulture));

        for (int i = 0; i < Specs.Length; i++)
        {
            ElementSpec spec = Specs[i];
            report.AppendLine();
            report.AppendLine("Element=" + spec.Element);
            report.AppendLine("Module=" + spec.ModulePath);
            if (!spec.HasSource)
            {
                report.AppendLine("Source=<empty extension slot>");
                continue;
            }

            GameObject source = LoadRequiredPrefab(spec.SourcePath);
            Transform sourceTransform = source.transform;
            report.AppendLine("Source=" + spec.SourcePath);
            report.AppendLine("GUID=" + AssetDatabase.AssetPathToGUID(spec.SourcePath));
            report.AppendLine("OriginalRootPosition=" + FormatVector(sourceTransform.localPosition));
            report.AppendLine("OriginalRootRotation=" + FormatVector(sourceTransform.localEulerAngles));
            report.AppendLine("OriginalRootScale=" + FormatVector(sourceTransform.localScale));
            report.AppendLine("WrapperRootPosition=(0,0,0)");
            report.AppendLine("WrapperRootRotation=(0,0,0)");
            report.AppendLine("WrapperRootScale=(1,1,1)");

            ParticleSystem[] sourceParticles = source.GetComponentsInChildren<ParticleSystem>(true);
            int sourceLoopCount = 0;
            int subEmitterCount = 0;
            for (int particleIndex = 0; particleIndex < sourceParticles.Length; particleIndex++)
            {
                if (sourceParticles[particleIndex].main.loop)
                    sourceLoopCount++;
                subEmitterCount += sourceParticles[particleIndex].subEmitters.subEmittersCount;
            }

            GameObject module = LoadRequiredPrefab(spec.ModulePath);
            ParticleSystem[] wrapperParticles =
                module.GetComponentsInChildren<ParticleSystem>(true);
            float maxPlaybackSeconds = 0f;
            for (int particleIndex = 0; particleIndex < wrapperParticles.Length; particleIndex++)
            {
                maxPlaybackSeconds = Mathf.Max(
                    maxPlaybackSeconds,
                    EstimatePlaybackSeconds(wrapperParticles[particleIndex]));
            }

            report.AppendLine("ParticleCount=" + wrapperParticles.Length);
            report.AppendLine("OriginalLoopCount=" + sourceLoopCount);
            report.AppendLine("WrapperLoopCount=0");
            report.AppendLine("SubEmitterLinks=" + subEmitterCount);
            report.AppendLine(
                "EstimatedMaxPlaybackSeconds=" +
                maxPlaybackSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "Logs");
        File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(false));
    }

    private static float EstimatePlaybackSeconds(ParticleSystem particle)
    {
        ParticleSystem.MainModule main = particle.main;
        float speed = Mathf.Max(0.0001f, main.simulationSpeed);
        return (GetCurveMaximum(main.startDelay)
                + main.duration
                + GetCurveMaximum(main.startLifetime)) / speed;
    }

    private static float GetCurveMaximum(ParticleSystem.MinMaxCurve curve)
    {
        float maximum = Mathf.Max(curve.constantMin, curve.constantMax);
        if (curve.curve != null)
            maximum = Mathf.Max(maximum, GetAnimationCurveMaximum(curve.curve) * curve.curveMultiplier);
        if (curve.curveMin != null)
            maximum = Mathf.Max(maximum, GetAnimationCurveMaximum(curve.curveMin) * curve.curveMultiplier);
        if (curve.curveMax != null)
            maximum = Mathf.Max(maximum, GetAnimationCurveMaximum(curve.curveMax) * curve.curveMultiplier);
        return maximum;
    }

    private static float GetAnimationCurveMaximum(AnimationCurve curve)
    {
        float maximum = 0f;
        Keyframe[] keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
            maximum = Mathf.Max(maximum, keys[i].value);
        return maximum;
    }

    private static void ValidateDependencies()
    {
        string[] dependencies = AssetDatabase.GetDependencies(FormalPrefabPath, true);
        for (int i = 0; i < dependencies.Length; i++)
        {
            if (dependencies[i].StartsWith("Assets/Temp", StringComparison.OrdinalIgnoreCase)
                || dependencies[i].StartsWith("Assets/Legacy", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Hit 정식 프리팹 금지 의존성: {dependencies[i]}");
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
            throw new InvalidOperationException($"Hit Transform 비정규화: {label}");
        }
    }

    private static void ValidateSharedPrefabTransform(Transform target)
    {
        Vector3 expectedScale = Vector3.one * SharedHitScale;
        if (target.localPosition.sqrMagnitude > 0.000001f
            || Quaternion.Angle(target.localRotation, Quaternion.identity) > 0.001f
            || (target.localScale - expectedScale).sqrMagnitude > 0.000001f)
        {
            throw new InvalidOperationException("Hit 통합 루트 Transform은 위치/회전 0, 크기 0.7이어야 합니다.");
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

    private static string FormatVector(Vector3 value)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "({0:0.###},{1:0.###},{2:0.###})",
            value.x,
            value.y,
            value.z);
    }

    private readonly struct ElementSpec
    {
        public ElementSpec(
            WeaponElement element,
            string sourcePath,
            bool createsContentSlot = false)
        {
            Element = element;
            SourcePath = sourcePath;
            CreatesContentSlot = createsContentSlot;
        }

        public WeaponElement Element { get; }
        public string SourcePath { get; }
        public bool HasSource => !string.IsNullOrEmpty(SourcePath);
        public bool CreatesContentSlot { get; }
        public string ModulePath =>
            $"{ModuleRoot}/PF_VFX_MeleeElementHit_{Element}Module.prefab";
    }
}
