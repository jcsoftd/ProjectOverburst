using System;
using UnityEditor;
using UnityEngine;

public static class ElementalReactionVfxSelectedContentUtility
{
    private const string WrapperRoot = "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Prefabs/";
    private const string FrostRoot = "Assets/ThirdParty/06_VFX/Piloto Studio 1/Elemental VFX Mega Bundle/Frost/";
    private const string ShockRoot = "Assets/ThirdParty/06_VFX/Piloto Studio 1/Elemental VFX Mega Bundle/Shock/";
    private const string LunarRoot = "Assets/ThirdParty/06_VFX/Piloto Studio/Super Realistic FX Bundle 02/Realistic Lunar Spells/";
    private const string ContentRootName = "VFX_CONTENT";
    private const string ObsoleteFractureLoopPath =
        WrapperRoot + "ThermalFracture/PF_VFX_Reaction_ThermalFracture_Loop.prefab";
    private const string PlasmaLoopPath =
        WrapperRoot + "Plasma/PF_VFX_Reaction_Plasma_Loop.prefab";
    private const string ColdChargeLoopPath =
        WrapperRoot + "ColdCharge/PF_VFX_Reaction_ColdCharge_Loop.prefab";
    private const int StatusLoopPoolCapacity = 100;

    private static readonly Selection[] Selections =
    {
        new Selection(
            "Plasma.Loop",
            WrapperRoot + "Plasma/PF_VFX_Reaction_Plasma_Loop.prefab",
            ShockRoot + "Orb_Shock_Blue.prefab",
            true),
        new Selection(
            "Plasma.Proc",
            WrapperRoot + "Plasma/PF_VFX_Reaction_Plasma_Proc.prefab",
            ShockRoot + "ExplosionAttacks_ShockBlue.prefab",
            false),
        new Selection(
            "Freeze.Loop",
            WrapperRoot + "FreezeShatter/PF_VFX_Reaction_Freeze_Loop.prefab",
            FrostRoot + "FrostAura.prefab",
            true),
        new Selection(
            "Shatter.Proc",
            WrapperRoot + "FreezeShatter/PF_VFX_Reaction_Shatter_Proc.prefab",
            FrostRoot + "FrostFire Impact.prefab",
            false),
        new Selection(
            "ColdCharge.Loop",
            WrapperRoot + "ColdCharge/PF_VFX_Reaction_ColdCharge_Loop.prefab",
            FrostRoot + "Frostmist Orb.prefab",
            true,
            30f,
            5f,
            3),
        new Selection(
            "ColdCharge.Proc",
            WrapperRoot + "ColdCharge/PF_VFX_Reaction_ColdCharge_Proc.prefab",
            LunarRoot + "Lunar_Light_Hit.prefab",
            false)
    };

    [InitializeOnLoadMethod]
    private static void ScheduleApplyWhenSelectionChanged()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorApplication.delayCall += ApplyWhenSelectionChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += ApplyWhenSelectionChanged;
    }

    private static void ApplyWhenSelectionChanged()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (ElementalReactionVfxAuthoringUtility.NeedsCatalogSynchronization())
        {
            RunFromCommandLine();
            return;
        }

        if (NeedsClear(ObsoleteFractureLoopPath))
        {
            RunFromCommandLine();
            return;
        }
        if (NeedsPoolCapacityUpdate(PlasmaLoopPath)
            || NeedsPoolCapacityUpdate(ColdChargeLoopPath))
        {
            RunFromCommandLine();
            return;
        }

        for (int i = 0; i < Selections.Length; i++)
        {
            if (NeedsApply(Selections[i]))
            {
                RunFromCommandLine();
                return;
            }
        }
    }

    [MenuItem("OVERBURST/Codex/Setup/Combat/Apply Selected Elemental Reaction VFX")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    public static void RunFromCommandLine()
    {
        ElementalReactionVfxAuthoringUtility.SynchronizeCatalogFromCommandLine();

        int changedCount = 0;
        changedCount += ApplyPoolCapacityIfNeeded(PlasmaLoopPath);
        changedCount += ApplyPoolCapacityIfNeeded(ColdChargeLoopPath);
        if (NeedsClear(ObsoleteFractureLoopPath))
        {
            ClearContent(ObsoleteFractureLoopPath);
            changedCount++;
        }

        for (int i = 0; i < Selections.Length; i++)
        {
            Selection selection = Selections[i];
            if (!NeedsApply(selection))
                continue; // 동일 소스의 위치·크기 튜닝은 보존

            Apply(selection);
            changedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        for (int i = 0; i < Selections.Length; i++)
            Validate(Selections[i]);

        ValidateEmpty(ObsoleteFractureLoopPath);
        ValidatePoolCapacity(PlasmaLoopPath);
        ValidatePoolCapacity(ColdChargeLoopPath);
        ElementalReactionVfxAuthoringUtility.RunValidationFromCommandLine();
        Debug.Log("[ElementalReactionVfxSelectedContent] 확정 슬롯 "
            + Selections.Length + "개 연결 검증 PASS, 변경=" + changedCount);
    }

    private static bool NeedsClear(string wrapperPath)
    {
        GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
        if (wrapper == null)
            return false;

        Transform contentRoot = wrapper.transform.Find(ContentRootName);
        return contentRoot != null && contentRoot.childCount > 0;
    }

    private static bool NeedsPoolCapacityUpdate(string wrapperPath)
    {
        GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
        ElementalReactionVfxAuthoring authoring =
            wrapper != null ? wrapper.GetComponent<ElementalReactionVfxAuthoring>() : null;
        return authoring != null && authoring.PoolCapacity != StatusLoopPoolCapacity;
    }

    private static int ApplyPoolCapacityIfNeeded(string wrapperPath)
    {
        if (!NeedsPoolCapacityUpdate(wrapperPath))
            return 0;

        GameObject wrapperRoot = PrefabUtility.LoadPrefabContents(wrapperPath);
        try
        {
            ElementalReactionVfxAuthoring authoring =
                wrapperRoot.GetComponent<ElementalReactionVfxAuthoring>();
            if (authoring == null || !authoring.SetPoolCapacity(StatusLoopPoolCapacity))
                return 0;

            PrefabUtility.SaveAsPrefabAsset(wrapperRoot, wrapperPath);
            return 1;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(wrapperRoot);
        }
    }

    private static void ValidatePoolCapacity(string wrapperPath)
    {
        GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
        ElementalReactionVfxAuthoring authoring =
            wrapper != null ? wrapper.GetComponent<ElementalReactionVfxAuthoring>() : null;
        if (authoring == null || authoring.PoolCapacity != StatusLoopPoolCapacity)
        {
            throw new InvalidOperationException(
                $"상태 Loop 풀 용량 불일치: {wrapperPath}, "
                + $"{authoring?.PoolCapacity ?? 0}/{StatusLoopPoolCapacity}");
        }
    }

    private static void ClearContent(string wrapperPath)
    {
        GameObject wrapperRoot = PrefabUtility.LoadPrefabContents(wrapperPath);
        try
        {
            Transform contentRoot = wrapperRoot.transform.Find(ContentRootName);
            if (contentRoot == null)
                throw new InvalidOperationException("VFX_CONTENT를 찾을 수 없습니다: " + wrapperPath);

            for (int i = contentRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(contentRoot.GetChild(i).gameObject);

            PrefabUtility.SaveAsPrefabAsset(wrapperRoot, wrapperPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(wrapperRoot);
        }
    }

    private static void ValidateEmpty(string wrapperPath)
    {
        GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
        if (wrapper == null)
            throw new InvalidOperationException("균열 구형 Loop wrapper가 없습니다: " + wrapperPath);

        Transform contentRoot = wrapper.transform.Find(ContentRootName);
        if (contentRoot == null || contentRoot.childCount != 0)
            throw new InvalidOperationException("균열 구형 Loop wrapper가 비어 있지 않습니다.");
        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(wrapper) > 0)
            throw new InvalidOperationException("균열 구형 Loop wrapper에 Missing Script가 있습니다.");
    }

    private static void Apply(Selection selection)
    {
        GameObject selectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(selection.SourcePath);
        if (selectedPrefab == null)
            throw new InvalidOperationException("선택 VFX 프리팹을 찾을 수 없습니다: " + selection.SourcePath);

        GameObject wrapperRoot = PrefabUtility.LoadPrefabContents(selection.WrapperPath);
        try
        {
            Transform contentRoot = wrapperRoot.transform.Find(ContentRootName);
            if (contentRoot == null)
                throw new InvalidOperationException("VFX_CONTENT를 찾을 수 없습니다: " + selection.WrapperPath);

            for (int i = contentRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(contentRoot.GetChild(i).gameObject);

            GameObject content = PrefabUtility.InstantiatePrefab(selectedPrefab, contentRoot) as GameObject;
            if (content == null)
                throw new InvalidOperationException("선택 VFX 인스턴스 생성에 실패했습니다: " + selection.SourcePath);

            content.name = selectedPrefab.name;
            content.transform.localPosition = Vector3.zero;
            content.transform.localRotation = Quaternion.identity;
            content.transform.localScale = Vector3.one;
            if (!selection.RequiresLoop)
                ForceOneShot(content); // 원본은 유지하고 중첩 인스턴스만 일회성 보정
            int normalizedDurationCount = NormalizeExtremeParticleDurations(content, selection);
            if (normalizedDurationCount != selection.ExpectedNormalizedDurationCount)
            {
                throw new InvalidOperationException(selection.Id
                    + " 비정상 파티클 재생 길이 보정 수가 예상과 다릅니다. expected="
                    + selection.ExpectedNormalizedDurationCount + ", actual=" + normalizedDurationCount);
            }

            PrefabUtility.SaveAsPrefabAsset(wrapperRoot, selection.WrapperPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(wrapperRoot);
        }
    }

    private static bool NeedsApply(Selection selection)
    {
        GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(selection.WrapperPath);
        GameObject selectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(selection.SourcePath);
        if (wrapper == null || selectedPrefab == null)
            return true;

        Transform contentRoot = wrapper.transform.Find(ContentRootName);
        if (contentRoot == null || contentRoot.childCount != 1)
            return true;

        GameObject content = contentRoot.GetChild(0).gameObject;
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(content);
        return source != selectedPrefab
            || !MatchesParticleContract(content, selection.RequiresLoop)
            || !MatchesDurationContract(content, selection);
    }

    private static void Validate(Selection selection)
    {
        GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(selection.WrapperPath);
        GameObject selectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(selection.SourcePath);
        if (wrapper == null || selectedPrefab == null)
            throw new InvalidOperationException(selection.Id + " 프리팹 참조가 누락됐습니다.");

        Transform contentRoot = wrapper.transform.Find(ContentRootName);
        if (contentRoot == null || contentRoot.childCount != 1)
            throw new InvalidOperationException(selection.Id + " VFX_CONTENT 구성 오류입니다.");

        GameObject content = contentRoot.GetChild(0).gameObject;
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(content);
        if (source != selectedPrefab)
            throw new InvalidOperationException(selection.Id + " 선택 VFX 참조가 일치하지 않습니다.");

        if (!MatchesParticleContract(content, selection.RequiresLoop))
            throw new InvalidOperationException(selection.Id + " 지속/일회성 계약이 선택 VFX와 맞지 않습니다.");
        if (!MatchesDurationContract(content, selection))
            throw new InvalidOperationException(selection.Id + " wrapper에 비정상적으로 긴 파티클 재생 시간이 남아 있습니다.");
        if (selection.ExpectedNormalizedDurationCount > 0
            && CountExtremeParticleDurations(selectedPrefab, selection.ExtremeDurationThreshold)
                != selection.ExpectedNormalizedDurationCount)
        {
            throw new InvalidOperationException(selection.Id
                + " 원본 VFX의 비정상 재생 길이 개수가 변경됐습니다. 보정 규칙을 다시 검토해야 합니다.");
        }
        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(wrapper) > 0)
            throw new InvalidOperationException(selection.Id + " wrapper에 Missing Script가 있습니다.");
    }

    private static int NormalizeExtremeParticleDurations(GameObject content, Selection selection)
    {
        if (selection.ExpectedNormalizedDurationCount <= 0)
            return 0;

        int normalizedCount = 0;
        ParticleSystem[] particleSystems = content.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            ParticleSystem.MainModule main = particleSystem.main;
            if (main.duration <= selection.ExtremeDurationThreshold)
                continue;

            main.duration = selection.NormalizedDuration;
            PrefabUtility.RecordPrefabInstancePropertyModifications(particleSystem);
            EditorUtility.SetDirty(particleSystem);
            normalizedCount++;
        }

        return normalizedCount;
    }

    private static bool MatchesDurationContract(GameObject content, Selection selection)
    {
        return selection.ExpectedNormalizedDurationCount <= 0
            || CountExtremeParticleDurations(content, selection.ExtremeDurationThreshold) == 0;
    }

    private static int CountExtremeParticleDurations(GameObject content, float threshold)
    {
        int count = 0;
        ParticleSystem[] particleSystems = content.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i].main.duration > threshold)
                count++;
        }

        return count;
    }

    private static void ForceOneShot(GameObject content)
    {
        ParticleSystem[] particleSystems = content.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem.MainModule main = particleSystems[i].main;
            main.loop = false;
        }
    }

    private static bool MatchesParticleContract(GameObject content, bool requiresLoop)
    {
        ParticleSystem[] particleSystems = content.GetComponentsInChildren<ParticleSystem>(true);
        if (particleSystems.Length == 0)
            return false;

        bool hasLoop = false;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i].main.loop)
                hasLoop = true;
        }

        return requiresLoop ? hasLoop : !hasLoop;
    }

    private readonly struct Selection
    {
        public Selection(
            string id,
            string wrapperPath,
            string sourcePath,
            bool requiresLoop,
            float extremeDurationThreshold = 0f,
            float normalizedDuration = 0f,
            int expectedNormalizedDurationCount = 0)
        {
            Id = id;
            WrapperPath = wrapperPath;
            SourcePath = sourcePath;
            RequiresLoop = requiresLoop;
            ExtremeDurationThreshold = extremeDurationThreshold;
            NormalizedDuration = normalizedDuration;
            ExpectedNormalizedDurationCount = expectedNormalizedDurationCount;
        }

        public string Id { get; }
        public string WrapperPath { get; }
        public string SourcePath { get; }
        public bool RequiresLoop { get; }
        public float ExtremeDurationThreshold { get; }
        public float NormalizedDuration { get; }
        public int ExpectedNormalizedDurationCount { get; }
    }
}
