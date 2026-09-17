using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ElementalReactionVfxRuntimeValidationUtility
{
    private const string CatalogPath =
        "Assets/ProjectOverburst/Resources/Combat/VFX/ElementalReactionVfxCatalog.asset";
    private const int ProductionPoolCapacity = 100;
    private const int ProductionCategoryBudget = 300;

    [MenuItem("OVERBURST/Codex/Validate/Elemental Reaction VFX Runtime")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        ValidateProductionCatalog();
        ValidateStrictPoolAndVisibility();
        ValidateCategoryBudgets();
        ValidateChainMappingAndPulseExclusion();
        ValidateLoopLifecycle();
        ValidateLargeLoopIndex();
        Debug.Log("[ElementalReactionVfxRuntime] 운영 카탈로그/strict pool/거리·화면/예산/Chain/Loop 검증 PASS");
    }

    private static void ValidateProductionCatalog()
    {
        ElementalReactionVfxCatalog catalog =
            AssetDatabase.LoadAssetAtPath<ElementalReactionVfxCatalog>(CatalogPath);
        Require(catalog != null, "운영 카탈로그가 없습니다.");
        Require(catalog.RuntimeConsumerConnected, "운영 카탈로그의 런타임 연결 플래그가 false입니다.");
        Require(ElementalReactionVfxRuntimeService.StartBudget == ProductionCategoryBudget
            && ElementalReactionVfxRuntimeService.ProcBudget == ProductionCategoryBudget
            && ElementalReactionVfxRuntimeService.LinkBudget == ProductionCategoryBudget
            && ElementalReactionVfxRuntimeService.EndBudget == ProductionCategoryBudget
            && ElementalReactionVfxRuntimeService.LoopBudget == ProductionCategoryBudget,
            "운영 VFX 공용 범주 상한이 모두 300이 아닙니다.");
        RequireProductionPoolCapacity(catalog, "Vaporize.Start", ProductionPoolCapacity);
        RequireProductionPoolCapacity(catalog, "Plasma.Loop", ProductionPoolCapacity);
        RequireProductionPoolCapacity(catalog, "Plasma.Proc", ProductionPoolCapacity);
        RequireProductionPoolCapacity(catalog, "FreezeShatter.Freeze_Loop", ProductionPoolCapacity);
        RequireProductionPoolCapacity(catalog, "FreezeShatter.Shatter_Proc", ProductionPoolCapacity);
        RequireProductionPoolCapacity(catalog, "ChainElectricity.Link", ProductionPoolCapacity);
        RequireProductionPoolCapacity(catalog, "ColdCharge.Loop", ProductionPoolCapacity);
        RequireProductionPoolCapacity(catalog, "ColdCharge.Proc", ProductionPoolCapacity);
        int expectedPrewarmCount = ResolveProductionPrewarmCount(catalog);

        using (Fixture fixture = new Fixture(catalog))
        {
            Require(
                fixture.Service.PrewarmInstantiateCountForValidation == expectedPrewarmCount,
                $"운영 카탈로그 prewarm 수가 다릅니다: "
                + $"{fixture.Service.PrewarmInstantiateCountForValidation}/{expectedPrewarmCount}");
            Require(fixture.Service.ActiveTotalForValidation == 0,
                "prewarm 직후 활성 VFX가 있으면 안 됩니다.");
            Require(fixture.Service.RuntimeInstantiateCountForValidation == 0,
                "운영 카탈로그 초기화 중 런타임 Instantiate가 발생했습니다.");
        }
    }

    private static void RequireProductionPoolCapacity(
        ElementalReactionVfxCatalog catalog,
        string id,
        int expectedCapacity)
    {
        Require(catalog.TryFind(id, out ElementalReactionVfxCatalogEntry entry)
            && entry?.Prefab != null,
            "운영 카탈로그 슬롯이 없습니다: " + id);
        ElementalReactionVfxAuthoring authoring =
            entry.Prefab.GetComponent<ElementalReactionVfxAuthoring>();
        Require(authoring != null && authoring.PoolCapacity == expectedCapacity,
            $"{id} 풀 용량이 다릅니다: {authoring?.PoolCapacity ?? 0}/{expectedCapacity}");
    }

    private static int ResolveProductionPrewarmCount(ElementalReactionVfxCatalog catalog)
    {
        int total = 0;
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            ElementalReactionVfxCatalogEntry entry = catalog.Entries[i];
            if (entry?.Prefab == null
                || !ElementalReactionVfxRuntimeService.HasPlayableContentForValidation(entry.Prefab))
            {
                continue;
            }

            ElementalReactionVfxAuthoring authoring =
                entry.Prefab.GetComponent<ElementalReactionVfxAuthoring>();
            if (authoring == null)
                continue;

            total += Mathf.Min(authoring.PoolCapacity, ResolveCategoryBudget(entry.SlotType));
        }

        return total;
    }

    private static int ResolveCategoryBudget(ElementalReactionVfxSlotType slotType)
    {
        switch (slotType)
        {
            case ElementalReactionVfxSlotType.Start:
                return ElementalReactionVfxRuntimeService.StartBudget;
            case ElementalReactionVfxSlotType.Loop:
                return ElementalReactionVfxRuntimeService.LoopBudget;
            case ElementalReactionVfxSlotType.Proc:
                return ElementalReactionVfxRuntimeService.ProcBudget;
            case ElementalReactionVfxSlotType.Link:
                return ElementalReactionVfxRuntimeService.LinkBudget;
            case ElementalReactionVfxSlotType.End:
                return ElementalReactionVfxRuntimeService.EndBudget;
            default:
                throw new InvalidOperationException("알 수 없는 반응 VFX 슬롯입니다: " + slotType);
        }
    }

    private static void ValidateStrictPoolAndVisibility()
    {
        ElementalReactionVfxCatalog catalog = CreateCatalog();
        GameObject wrapper = AddWrapper(
            catalog, "Fixture.Start", ElementalReactionType.Vaporize,
            ElementalReactionVfxSlotType.Start, 1);

        using (Fixture fixture = new Fixture(catalog, wrapper))
        {
            ElementalReactionVfxRuntimeService service = fixture.Service;
            Require(service.PrewarmInstantiateCountForValidation == 1,
                "내용물 fixture가 정확히 한 번 prewarm되지 않았습니다.");
            Require(service.TryPlayForValidation(
                ElementalReactionType.Vaporize, ElementalReactionVfxSlotType.Start,
                null, Vector3.zero, Vector3.zero, Vector3.zero, 2.5f),
                "가시 범위 Start가 재생되지 않았습니다.");
            Require(!service.TryPlayForValidation(
                ElementalReactionType.Vaporize, ElementalReactionVfxSlotType.Start,
                null, Vector3.zero, Vector3.zero, Vector3.zero, 2.5f),
                "strict pool 고갈 뒤 재생이 허용됐습니다.");
            Require(service.RuntimeInstantiateCountForValidation == 0,
                "pool 고갈 시 Instantiate가 발생했습니다.");
        }

        ValidateRejectedPosition(new Vector3(0f, 1f, -20f), "카메라 뒤");
        ValidateRejectedPosition(new Vector3(100f, 1f, 0f), "화면 밖");
        ValidateRejectedPosition(new Vector3(0f, 1f, 60f), "최대 거리 밖");
    }

    private static void ValidateRejectedPosition(Vector3 position, string label)
    {
        ElementalReactionVfxCatalog catalog = CreateCatalog();
        GameObject wrapper = AddWrapper(
            catalog, "Fixture.Start", ElementalReactionType.Vaporize,
            ElementalReactionVfxSlotType.Start, 2);
        using (Fixture fixture = new Fixture(catalog, wrapper))
        {
            bool played = fixture.Service.TryPlayForValidation(
                ElementalReactionType.Vaporize, ElementalReactionVfxSlotType.Start,
                null, position, position, position, 2.5f);
            Require(!played, label + " VFX가 재생됐습니다.");
            Require(fixture.Service.ActiveTotalForValidation == 0,
                label + " VFX가 category 예산을 점유했습니다.");
        }
    }

    private static void ValidateCategoryBudgets()
    {
        ValidateCategoryBudget(ElementalReactionVfxSlotType.Start,
            ElementalReactionType.Vaporize, ElementalReactionVfxRuntimeService.StartBudget);
        ValidateCategoryBudget(ElementalReactionVfxSlotType.Proc,
            ElementalReactionType.Plasma, ElementalReactionVfxRuntimeService.ProcBudget);
        ValidateCategoryBudget(ElementalReactionVfxSlotType.Link,
            ElementalReactionType.ChainElectricity, ElementalReactionVfxRuntimeService.LinkBudget);
        ValidateCategoryBudget(ElementalReactionVfxSlotType.End,
            ElementalReactionType.Freeze, ElementalReactionVfxRuntimeService.EndBudget);
        ValidateCategoryBudget(ElementalReactionVfxSlotType.Loop,
            ElementalReactionType.ThermalFracture, ElementalReactionVfxRuntimeService.LoopBudget);
    }

    private static void ValidateCategoryBudget(
        ElementalReactionVfxSlotType slotType,
        ElementalReactionType reactionType,
        int budget)
    {
        ElementalReactionVfxCatalog catalog = CreateCatalog();
        GameObject wrapper = AddWrapper(catalog, "Fixture." + slotType, reactionType, slotType, budget + 4);
        using (Fixture fixture = new Fixture(catalog, wrapper))
        {
            for (int i = 0; i < budget + 1; i++)
            {
                fixture.Service.TryPlayForValidation(
                    reactionType, slotType, null,
                    new Vector3(0f, 1f, i * 0.001f),
                    new Vector3(0f, 1f, -0.5f),
                    new Vector3(0f, 1f, i * 0.001f), 2.5f);
            }

            Require(fixture.Service.GetActiveCategoryCountForValidation(slotType) == budget,
                slotType + " category cap이 지켜지지 않았습니다.");
            Require(fixture.Service.RuntimeInstantiateCountForValidation == 0,
                slotType + " 예산 초과 때 Instantiate가 발생했습니다.");
        }
    }

    private static void ValidateChainMappingAndPulseExclusion()
    {
        ElementalReactionVfxCatalog catalog = CreateCatalog();
        List<GameObject> wrappers = new List<GameObject>(3)
        {
            AddWrapper(catalog, "Chain.Start", ElementalReactionType.ChainElectricity,
                ElementalReactionVfxSlotType.Start, 2),
            AddWrapper(catalog, "Chain.Proc", ElementalReactionType.ChainElectricity,
                ElementalReactionVfxSlotType.Proc, 6),
            AddWrapper(catalog, "Chain.Link", ElementalReactionType.ChainElectricity,
                ElementalReactionVfxSlotType.Link, 5)
        };

        using (Fixture fixture = new Fixture(catalog, wrappers.ToArray()))
        {
            const long sequenceId = 101;
            fixture.Service.HandleStartForValidation(
                sequenceId, ElementalReactionType.ChainElectricity, null, Vector3.zero);
            for (int i = 0; i < 5; i++)
            {
                fixture.Service.HandleChainHopForValidation(
                    sequenceId, i, null, new Vector3(0f, 1f, i));
            }
            fixture.Service.HandleChainCompletedForValidation(sequenceId, 5);

            Require(fixture.Service.GetSpawnCountForValidation(
                ElementalReactionType.ChainElectricity, ElementalReactionVfxSlotType.Start) == 1,
                "Chain Start 횟수가 1이 아닙니다.");
            Require(fixture.Service.GetSpawnCountForValidation(
                ElementalReactionType.ChainElectricity, ElementalReactionVfxSlotType.Proc) == 5,
                "Chain Proc 횟수가 5가 아닙니다.");
            Require(fixture.Service.GetSpawnCountForValidation(
                ElementalReactionType.ChainElectricity, ElementalReactionVfxSlotType.Link) == 4,
                "Chain Link 횟수가 4가 아닙니다.");
            Require(fixture.Service.TrackedSequenceCountForValidation == 0,
                "완료된 Chain sequence 추적이 남았습니다.");

        }
    }

    private static void ValidateLoopLifecycle()
    {
        ElementalReactionVfxCatalog catalog = CreateCatalog();
        List<GameObject> wrappers = new List<GameObject>(2)
        {
            AddWrapper(catalog, "Thermal.Loop", ElementalReactionType.ThermalFracture,
                ElementalReactionVfxSlotType.Loop, 2, true),
            AddWrapper(catalog, "Thermal.End", ElementalReactionType.ThermalFracture,
                ElementalReactionVfxSlotType.End, 2)
        };

        using (Fixture fixture = new Fixture(catalog, wrappers.ToArray()))
        {
            GameObject targetRoot = new GameObject("ReactionVfxTarget");
            fixture.Track(targetRoot);
            CombatTarget target = targetRoot.AddComponent<CombatTarget>();
            ElementalStatusController owner = targetRoot.AddComponent<ElementalStatusController>();

            fixture.Service.HandleStateChangedForValidation(
                owner, ElementalReactionType.ThermalFracture,
                ElementalReactionStateChangeReason.Applied);
            int firstId = fixture.Service.GetLoopInstanceIdForValidation(
                owner, ElementalReactionType.ThermalFracture);
            Require(firstId != 0, "상태 시작 때 Loop가 생성되지 않았습니다.");

            fixture.Service.HandleStateChangedForValidation(
                owner, ElementalReactionType.ThermalFracture,
                ElementalReactionStateChangeReason.Refreshed);
            int refreshedId = fixture.Service.GetLoopInstanceIdForValidation(
                owner, ElementalReactionType.ThermalFracture);
            Require(refreshedId == firstId, "같은 상태 갱신이 Loop를 재생성했습니다.");

            fixture.Service.HandleStateRemovedForValidation(
                owner, ElementalReactionType.ThermalFracture,
                ElementalReactionStateRemoveReason.Expired);
            Require(fixture.Service.GetActiveCategoryCountForValidation(
                ElementalReactionVfxSlotType.Loop) == 0,
                "상태 제거 뒤 Loop가 남았습니다.");
            Require(fixture.Service.GetSpawnCountForValidation(
                ElementalReactionType.ThermalFracture, ElementalReactionVfxSlotType.End) == 1,
                "실제 상태 종료 때 End가 1회 재생되지 않았습니다.");

            fixture.Service.HandleStateChangedForValidation(
                owner, ElementalReactionType.ThermalFracture,
                ElementalReactionStateChangeReason.Applied);
            fixture.Service.HandleStatesClearedForValidation(owner, ElementalStatusClearReason.Death);
            Require(fixture.Service.GetActiveCategoryCountForValidation(
                ElementalReactionVfxSlotType.Loop) == 0,
                "death/clear 뒤 Loop가 남았습니다.");

            fixture.Service.HandleStateChangedForValidation(
                owner, ElementalReactionType.ThermalFracture,
                ElementalReactionStateChangeReason.Applied);
            targetRoot.SetActive(false);
            fixture.Service.TickForValidation();
            Require(fixture.Service.GetActiveCategoryCountForValidation(
                ElementalReactionVfxSlotType.Loop) == 0,
                "Disable/pool return 뒤 Loop가 남았습니다.");
        }
    }

    private static void ValidateLargeLoopIndex()
    {
        const int ownerCount = 2000;
        ElementalReactionVfxCatalog catalog = CreateCatalog();
        GameObject wrapper = AddWrapper(
            catalog, "Thermal.Loop.Large", ElementalReactionType.ThermalFracture,
            ElementalReactionVfxSlotType.Loop, 2, true);

        using (Fixture fixture = new Fixture(catalog, wrapper))
        {
            ElementalStatusController[] owners = new ElementalStatusController[ownerCount];
            for (int i = 0; i < ownerCount; i++)
            {
                GameObject targetRoot = new GameObject("ReactionVfxMassOwner_" + i);
                fixture.Track(targetRoot);
                targetRoot.transform.position = i == 0 || i == ownerCount - 1
                    ? Vector3.zero
                    : new Vector3(100f, 0f, 0f);
                targetRoot.AddComponent<CombatTarget>();
                owners[i] = targetRoot.AddComponent<ElementalStatusController>();
                fixture.Service.HandleStateChangedForValidation(
                    owners[i], ElementalReactionType.ThermalFracture,
                    ElementalReactionStateChangeReason.Applied);
            }

            Require(fixture.Service.TrackedLoopKeyCountForValidation == ownerCount,
                "대량 owner key 수가 일치하지 않습니다.");
            Require(fixture.Service.ActiveLoopIndexCountForValidation == ownerCount,
                "dense active index 수가 일치하지 않습니다.");
            Require(fixture.Service.FreeLoopIndexCountForValidation == 48,
                "대량 등록 뒤 free index 수가 일치하지 않습니다.");

            ElementalStatusController lastOwner = owners[ownerCount - 1];
            int lastRecordIndex = fixture.Service.GetLoopRecordIndexForValidation(
                lastOwner, ElementalReactionType.ThermalFracture);
            int lastInstanceId = fixture.Service.GetLoopInstanceIdForValidation(
                lastOwner, ElementalReactionType.ThermalFracture);
            Require(lastRecordIndex >= 0 && lastInstanceId != 0,
                "마지막 owner의 record/Loop instance가 준비되지 않았습니다.");

            fixture.Service.HandleStateChangedForValidation(
                lastOwner, ElementalReactionType.ThermalFracture,
                ElementalReactionStateChangeReason.Refreshed);
            fixture.Service.HandleStateChangedForValidation(
                lastOwner, ElementalReactionType.ThermalFracture,
                ElementalReactionStateChangeReason.Applied);
            Require(fixture.Service.GetLoopRecordIndexForValidation(
                    lastOwner, ElementalReactionType.ThermalFracture) == lastRecordIndex,
                "마지막 owner refresh가 record index를 교체했습니다.");
            Require(fixture.Service.GetLoopInstanceIdForValidation(
                    lastOwner, ElementalReactionType.ThermalFracture) == lastInstanceId,
                "마지막 owner refresh가 Loop instance를 교체했습니다.");
            Require(fixture.Service.TrackedLoopKeyCountForValidation == ownerCount,
                "같은 key가 중복 등록됐습니다.");
            Require(fixture.Service.LoopLinearProbeCountForValidation == 0,
                "Loop lookup에서 선형 probe가 발생했습니다.");

            for (int i = 0; i < ownerCount; i++)
            {
                if ((i & 1) == 0)
                {
                    fixture.Service.HandleStateRemovedForValidation(
                        owners[i], ElementalReactionType.ThermalFracture,
                        ElementalReactionStateRemoveReason.Cleared);
                }
                else
                {
                    fixture.Service.HandleStatesClearedForValidation(
                        owners[i], ElementalStatusClearReason.Reset);
                }
            }

            Require(fixture.Service.TrackedLoopKeyCountForValidation == 0,
                "대량 정리 뒤 owner key가 남았습니다.");
            Require(fixture.Service.ActiveLoopIndexCountForValidation == 0,
                "대량 정리 뒤 active index가 남았습니다.");
            Require(fixture.Service.FreeLoopIndexCountForValidation == 2048,
                "대량 정리 뒤 free index가 완전히 복구되지 않았습니다.");
        }
    }

    private static ElementalReactionVfxCatalog CreateCatalog()
    {
        ElementalReactionVfxCatalog catalog =
            ScriptableObject.CreateInstance<ElementalReactionVfxCatalog>();
        catalog.SetRuntimeConsumerConnected(true);
        return catalog;
    }

    private static GameObject AddWrapper(
        ElementalReactionVfxCatalog catalog,
        string id,
        ElementalReactionType reactionType,
        ElementalReactionVfxSlotType slotType,
        int poolCapacity,
        bool followTarget = false)
    {
        GameObject wrapper = new GameObject(id);
        ElementalReactionVfxAuthoring authoring = wrapper.AddComponent<ElementalReactionVfxAuthoring>();
        authoring.ConfigureInitialDefaults(
            reactionType, slotType,
            slotType == ElementalReactionVfxSlotType.Link
                ? ElementalReactionVfxSpawnBasis.SourceToTarget
                : ElementalReactionVfxSpawnBasis.WorldPosition,
            followTarget,
            ElementalReactionVfxScaleMode.None,
            0f, Vector3.zero, 30f, poolCapacity, false);
        GameObject content = new GameObject("VFX_CONTENT");
        content.transform.SetParent(wrapper.transform, false);
        GameObject particle = new GameObject("FixtureParticle");
        particle.transform.SetParent(content.transform, false);
        particle.AddComponent<ParticleSystem>();
        catalog.EnsureAuthoringEntry(id, reactionType, slotType, wrapper);
        return wrapper;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<Object> ownedObjects = new List<Object>();
        public ElementalReactionVfxRuntimeService Service { get; }

        public Fixture(ElementalReactionVfxCatalog catalog, params GameObject[] wrappers)
        {
            if (!AssetDatabase.Contains(catalog))
                Track(catalog);
            for (int i = 0; i < wrappers.Length; i++)
                Track(wrappers[i]);

            GameObject cameraRoot = new GameObject("ReactionVfxValidationCamera");
            Track(cameraRoot);
            cameraRoot.transform.SetPositionAndRotation(
                new Vector3(0f, 1f, -10f), Quaternion.identity);
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.fieldOfView = 60f;

            GameObject serviceRoot = new GameObject("ReactionVfxValidationService");
            Track(serviceRoot);
            Service = serviceRoot.AddComponent<ElementalReactionVfxRuntimeService>();
            Service.InitializeForValidation(catalog, camera);
        }

        public void Track(Object value)
        {
            if (value != null)
                ownedObjects.Add(value);
        }

        public void Dispose()
        {
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                if (ownedObjects[i] != null)
                    Object.DestroyImmediate(ownedObjects[i]);
            }
            ownedObjects.Clear();
        }
    }
}
