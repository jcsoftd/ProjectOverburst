using System;
using UnityEditor;
using UnityEngine;

public static class VaporizeReactionVfxSetupUtility
{
    private const string SourcePrefabPath =
        "Assets/ThirdParty/06_VFX/Piloto Studio 1/Elemental VFX Mega Bundle/Fire/Impact_Fire.prefab";
    private const string WrapperPrefabPath =
        "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Prefabs/Vaporize/PF_VFX_Reaction_Vaporize_Start.prefab";
    private const string ContentRootName = "VFX_CONTENT";
    private const string TwirlyName = "Smoke_Twirly_Alpha";
    private const string HarshName = "Smoke_Harsh_Alpha";
    private const float AuthoredRadius = 3.2f;
    private const float Lifetime = 0.8f;
    private const int PoolCapacity = 100;

    private static readonly Color TwirlyColor = new Color(0.78f, 0.83f, 0.86f, 0.72f);
    private static readonly Color HarshColor = new Color(0.91f, 0.93f, 0.94f, 0.58f);

    [MenuItem("OVERBURST/Codex/Setup/Combat/Setup Vaporize Reaction VFX")]
    public static void RunSetupFromMenu()
    {
        RunSetupFromCommandLine();
    }

    public static void RunSetupFromCommandLine()
    {
        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        if (sourcePrefab == null)
            throw new InvalidOperationException("증발 VFX 원본 프리팹을 찾을 수 없습니다: " + SourcePrefabPath);

        Transform sourceTwirly = FindDescendant(sourcePrefab.transform, TwirlyName);
        Transform sourceHarsh = FindDescendant(sourcePrefab.transform, HarshName);
        if (sourceTwirly == null || sourceHarsh == null)
            throw new InvalidOperationException("Impact_Fire에서 증발용 연기 파티클을 찾을 수 없습니다.");

        GameObject wrapperRoot = PrefabUtility.LoadPrefabContents(WrapperPrefabPath);
        try
        {
            Transform contentRoot = wrapperRoot.transform.Find(ContentRootName);
            if (contentRoot == null)
                throw new InvalidOperationException("증발 wrapper에 VFX_CONTENT가 없습니다.");

            ClearChildren(contentRoot);
            CloneSmoke(sourceTwirly, contentRoot, TwirlyColor);
            CloneSmoke(sourceHarsh, contentRoot, HarshColor);

            ElementalReactionVfxAuthoring authoring =
                wrapperRoot.GetComponent<ElementalReactionVfxAuthoring>();
            if (authoring == null)
                throw new InvalidOperationException("증발 wrapper에 authoring 컴포넌트가 없습니다.");

            authoring.ConfigureInitialDefaults(
                ElementalReactionType.Vaporize,
                ElementalReactionVfxSlotType.Start,
                ElementalReactionVfxSpawnBasis.TargetCenter,
                false,
                ElementalReactionVfxScaleMode.ReactionRadius,
                AuthoredRadius,
                Vector3.zero,
                Lifetime,
                PoolCapacity,
                true);

            PrefabUtility.SaveAsPrefabAsset(wrapperRoot, WrapperPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(wrapperRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RunValidationFromCommandLine();
        Debug.Log("[VaporizeReactionVfxSetup] Impact_Fire 연기 2종을 증발 Start wrapper에 연결했습니다.");
    }

    [MenuItem("OVERBURST/Codex/Validate/Vaporize Reaction VFX")]
    public static void RunValidationFromMenu()
    {
        RunValidationFromCommandLine();
    }

    public static void RunValidationFromCommandLine()
    {
        GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPrefabPath);
        if (wrapper == null)
            throw new InvalidOperationException("증발 VFX wrapper가 없습니다.");

        ElementalReactionVfxAuthoring authoring = wrapper.GetComponent<ElementalReactionVfxAuthoring>();
        if (authoring == null
            || authoring.ReactionType != ElementalReactionType.Vaporize
            || authoring.SlotType != ElementalReactionVfxSlotType.Start
            || authoring.SpawnBasis != ElementalReactionVfxSpawnBasis.TargetCenter
            || authoring.FollowTarget
            || authoring.ScaleMode != ElementalReactionVfxScaleMode.ReactionRadius
            || Mathf.Abs(authoring.AuthoredRadius - AuthoredRadius) > 0.001f
            || Mathf.Abs(authoring.Lifetime - Lifetime) > 0.001f
            || authoring.PoolCapacity != PoolCapacity
            || !authoring.NaturalCompletion)
        {
            throw new InvalidOperationException("증발 VFX authoring 계약이 다릅니다.");
        }

        Transform contentRoot = wrapper.transform.Find(ContentRootName);
        if (contentRoot == null || contentRoot.childCount != 2)
            throw new InvalidOperationException("증발 VFX_CONTENT에는 연기 2종만 있어야 합니다.");

        ValidateSmoke(contentRoot.Find(TwirlyName), TwirlyColor);
        ValidateSmoke(contentRoot.Find(HarshName), HarshColor);

        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(wrapper) > 0)
            throw new InvalidOperationException("증발 VFX wrapper에 Missing Script가 있습니다.");

        Debug.Log("[VaporizeReactionVfxSetup] wrapper/연기 2종/색상/범위/수명 검증 PASS");
    }

    private static void CloneSmoke(Transform source, Transform parent, Color color)
    {
        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
        clone.name = source.name;
        clone.transform.SetParent(parent, false);
        clone.transform.localPosition = source.localPosition;
        clone.transform.localRotation = source.localRotation;
        clone.transform.localScale = source.localScale;

        ParticleSystem[] systems = clone.GetComponentsInChildren<ParticleSystem>(true);
        if (systems.Length == 0)
            throw new InvalidOperationException("연기 원본에 ParticleSystem이 없습니다: " + source.name);

        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            main.loop = false;
            main.playOnAwake = true;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }
    }

    private static void ValidateSmoke(Transform smokeRoot, Color expectedColor)
    {
        if (smokeRoot == null)
            throw new InvalidOperationException("증발 연기 오브젝트가 없습니다.");

        ParticleSystem[] systems = smokeRoot.GetComponentsInChildren<ParticleSystem>(true);
        if (systems.Length == 0)
            throw new InvalidOperationException("증발 연기에 ParticleSystem이 없습니다: " + smokeRoot.name);

        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            if (main.loop || !main.playOnAwake || main.duration > 0.61f)
                throw new InvalidOperationException("증발 연기는 0.6초 1회성이어야 합니다: " + smokeRoot.name);

            Color actualColor = main.startColor.color;
            if (ColorDistance(actualColor, expectedColor) > 0.001f)
                throw new InvalidOperationException("증발 연기 색상이 다릅니다: " + smokeRoot.name);
        }
    }

    private static float ColorDistance(Color left, Color right)
    {
        Vector4 delta = (Vector4)left - (Vector4)right;
        return delta.sqrMagnitude;
    }

    private static Transform FindDescendant(Transform root, string targetName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == targetName)
                return transforms[i];
        }

        return null;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }
}
