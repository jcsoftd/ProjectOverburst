using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MeleeElementStatusAuraVisibilityValidationUtility
{
    [MenuItem("OVERBURST/Codex/Validate/Element Status Aura Visibility")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        GameObject schedulerRoot = new GameObject("AuraVisibilityValidationScheduler");
        GameObject cameraRoot = new GameObject("AuraVisibilityValidationCamera");
        GameObject templateRoot = CreatePresentationTemplate();
        GameObject primaryRoot = null;
        GameObject[] massRoots = new GameObject[1000];
        try
        {
            MeleeElementStatusAuraPresentation template =
                templateRoot.GetComponent<MeleeElementStatusAuraPresentation>();
            MeleeElementStatusAuraVisibilityScheduler scheduler =
                schedulerRoot.AddComponent<MeleeElementStatusAuraVisibilityScheduler>();
            scheduler.InitializeForValidation(template);

            Camera camera = cameraRoot.AddComponent<Camera>();
            cameraRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            camera.orthographic = true;
            camera.orthographicSize = 100f;

            MeleeElementStatusAuraController controller = CreateController(
                "AuraVisibilityPrimary", out primaryRoot);
            primaryRoot.transform.position = new Vector3(0f, 0f, 40f);
            Require(controller.StartAura(MeleeElementStatusAuraType.Burning),
                "Aura logical 시작에 실패했습니다.");
            Require(controller.IsAuraActive(MeleeElementStatusAuraType.Burning)
                && !controller.PresentationVisibleForValidation,
                "초기 등록에서 logical/presentation 분리가 깨졌습니다.");

            scheduler.AdvanceForValidation(camera);
            MeleeElementStatusAuraPresentation firstLease =
                controller.PresentationForValidation;
            Require(firstLease != null
                && firstLease.GetAuraObject(MeleeElementStatusAuraType.Burning).activeSelf,
                "가시 진입에서 presentation을 대여·재생하지 못했습니다.");
            int firstPlayCount = firstLease.PlayCommandCountForValidation;
            scheduler.AdvanceForValidation(camera);
            controller.RefreshAuraLifetime(MeleeElementStatusAuraType.Burning);
            Require(firstLease.PlayCommandCountForValidation == firstPlayCount,
                "동일 visible/상태 갱신이 Aura를 재시작했습니다.");

            primaryRoot.transform.position = new Vector3(0f, 0f, 45f);
            scheduler.AdvanceForValidation(camera);
            Require(controller.PresentationForValidation == firstLease,
                "42~48m hysteresis에서 presentation이 교체됐습니다.");

            primaryRoot.transform.position = new Vector3(0f, 0f, 49f);
            scheduler.AdvanceForValidation(camera);
            Require(controller.PresentationForValidation == null
                && controller.IsAuraActive(MeleeElementStatusAuraType.Burning)
                && !firstLease.gameObject.activeSelf,
                "비가시 전환에서 logical 상태 보존 또는 풀 반환이 실패했습니다.");

            controller.ClearAllAuras();
            Require(MeleeElementStatusAuraVisibilityScheduler.RegisteredControllerCount == 0,
                "Clear 경계에서 scheduler 등록이 남았습니다.");

            ValidateMassCap(scheduler, camera, massRoots);
            Debug.Log("[AuraVisibilityValidation] lazy pool/hysteresis/1000-owner/cap-64 PASS");
        }
        finally
        {
            for (int i = massRoots.Length - 1; i >= 0; i--)
            {
                if (massRoots[i] != null)
                    Object.DestroyImmediate(massRoots[i]);
            }
            if (primaryRoot != null)
                Object.DestroyImmediate(primaryRoot);
            Object.DestroyImmediate(schedulerRoot);
            Object.DestroyImmediate(cameraRoot);
            Object.DestroyImmediate(templateRoot);
        }
    }

    private static void ValidateMassCap(
        MeleeElementStatusAuraVisibilityScheduler scheduler,
        Camera camera,
        GameObject[] roots)
    {
        MeleeElementStatusAuraController[] controllers =
            new MeleeElementStatusAuraController[roots.Length];
        for (int i = 0; i < roots.Length; i++)
        {
            controllers[i] = CreateController("AuraVisibilityMass_" + i, out roots[i]);
            roots[i].transform.position = new Vector3((i % 20) - 10f, 0f, 40f);
            controllers[i].StartAura(MeleeElementStatusAuraType.Burning);
            controllers[i].StartAura(MeleeElementStatusAuraType.Burning);
        }

        Require(MeleeElementStatusAuraVisibilityScheduler.RegisteredControllerCount == roots.Length,
            "1,000 controller 등록에 중복 또는 누락이 있습니다.");
        int checkedBefore = scheduler.TotalCheckedCountForValidation;
        for (int tick = 0; tick < 4; tick++)
        {
            scheduler.AdvanceForValidation(camera);
            Require(scheduler.LastCheckedCountForValidation <= 256,
                "Aura visibility tick이 256 controller 예산을 넘었습니다.");
            Require(scheduler.LeasedPresentationCountForValidation
                <= MeleeElementStatusAuraVisibilityScheduler.DefaultMaxVisiblePresentations,
                "Aura presentation 동시 표시 상한을 넘었습니다.");
        }

        int checkedTotal = scheduler.TotalCheckedCountForValidation - checkedBefore;
        Require(checkedTotal >= roots.Length && checkedTotal <= 1024,
            "1,000 controller의 4 tick 검사 총량이 잘못됐습니다.");
        Require(scheduler.LeasedPresentationCountForValidation == 64
            && scheduler.CreatedPresentationCountForValidation == 64,
            "가시 대상 과밀에서 lazy pool이 64개 상한으로 수렴하지 않았습니다.");

        for (int i = 0; i < controllers.Length; i++)
        {
            Require(controllers[i].VisibilityCheckCountForValidation > 0,
                "1,000 controller 중 4 tick 안에 검사되지 않은 대상이 있습니다.");
            controllers[i].DisableForValidation();
            roots[i].SetActive(false);
        }
        Require(MeleeElementStatusAuraVisibilityScheduler.RegisteredControllerCount == 0
            && scheduler.LeasedPresentationCountForValidation == 0,
            "대량 Disable 뒤 scheduler/presentation 대여가 남았습니다.");
    }

    private static MeleeElementStatusAuraController CreateController(
        string name,
        out GameObject root)
    {
        root = new GameObject(name);
        return root.AddComponent<MeleeElementStatusAuraController>();
    }

    private static GameObject CreatePresentationTemplate()
    {
        GameObject root = new GameObject("AuraPresentationValidationTemplate");
        GameObject burning = CreateModule(root.transform, "Burning_Aura");
        GameObject wet = CreateModule(root.transform, "Wet_Aura");
        MeleeElementStatusAuraPresentation presentation =
            root.AddComponent<MeleeElementStatusAuraPresentation>();
        SerializedObject serialized = new SerializedObject(presentation);
        serialized.FindProperty("burningAura").objectReferenceValue = burning;
        serialized.FindProperty("wetAura").objectReferenceValue = wet;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        presentation.RebuildModulesForValidation();
        presentation.ClearAllAuras();
        root.SetActive(false);
        return root;
    }

    private static GameObject CreateModule(Transform parent, string name)
    {
        GameObject module = new GameObject(name);
        module.transform.SetParent(parent, false);
        ParticleSystem particle = module.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particle.main;
        main.playOnAwake = false;
        module.SetActive(false);
        return module;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
