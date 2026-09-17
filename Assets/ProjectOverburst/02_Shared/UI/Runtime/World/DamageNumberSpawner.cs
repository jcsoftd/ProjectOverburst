using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DamageNumberSpawner : MonoBehaviour
{
    public const int DefaultPopupBudget = 128;

    private static readonly Color DefaultCurrencyPickupColor = new Color(1f, 0.82f, 0.2f, 1f);
    private static readonly Color PhysicalDamageColor = Color.white;
    private static readonly Color FireDamageColor = new Color(1f, 0.40f, 0.08f, 1f);
    private static readonly Color WaterDamageColor = new Color(0.18f, 0.55f, 1f, 1f);
    private static readonly Color IceDamageColor = new Color(0.32f, 0.92f, 1f, 1f);
    private static readonly Color ElectricDamageColor = new Color(0.72f, 0.36f, 1f, 1f);
    private static readonly Color WindDamageColor = new Color(0.28f, 0.92f, 0.38f, 1f);
    private static readonly Color EarthDamageColor = new Color(0.72f, 0.48f, 0.20f, 1f);
    private static readonly Color VaporizeReactionColor = new Color(184f / 255f, 190f / 255f, 196f / 255f, 1f);
    private static readonly Color FractureReactionColor = new Color(1f, 192f / 255f, 122f / 255f, 1f);
    private static readonly Color PlasmaReactionColor = new Color(230f / 255f, 160f / 255f, 1f, 1f);
    private static readonly Color FreezeReactionColor = new Color(189f / 255f, 239f / 255f, 1f, 1f);
    private static readonly Color ShatterReactionColor = new Color(221f / 255f, 248f / 255f, 1f, 1f);
    private static readonly Color ChainShockReactionColor = new Color(201f / 255f, 164f / 255f, 1f, 1f);
    private static readonly Color ColdChargeReactionColor = new Color(191f / 255f, 215f / 255f, 1f, 1f);
    private static readonly Color ColdChargeProcColor = new Color(221f / 255f, 244f / 255f, 1f, 1f);
    private const float ReactionWorldOffset = 1.15f;

    [SerializeField] private GameObject damageNumberPrefab;
    [SerializeField] private RectTransform popupRoot;
    [SerializeField, Min(1)] private int popupBudget = DefaultPopupBudget;

    private static DamageNumberSpawner instance;
    private static bool missingSpawnerWarningLogged;

    private readonly Queue<DamageNumberPopup> pool = new Queue<DamageNumberPopup>();
    private Camera presentationCamera;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError("[DamageNumberSpawner] 중복 스포너가 발견됐습니다.", this);
            enabled = false;
            return;
        }

        instance = this;
        if (popupRoot == null)
            popupRoot = transform as RectTransform;

        PrewarmPool();
    }

    private void OnValidate()
    {
        popupBudget = Mathf.Max(1, popupBudget);
    }

    private void OnEnable()
    {
        if (instance != this)
            return;

        ElementalReactionEvents.ReactionStarted -= HandleReactionStarted;
        ElementalReactionEvents.ReactionStarted += HandleReactionStarted;
        ElementalReactionEvents.ReactionProcExecuted -= HandleReactionProcExecuted;
        ElementalReactionEvents.ReactionProcExecuted += HandleReactionProcExecuted;
    }

    private void OnDisable()
    {
        ElementalReactionEvents.ReactionStarted -= HandleReactionStarted;
        ElementalReactionEvents.ReactionProcExecuted -= HandleReactionProcExecuted;
    }

    private void OnDestroy()
    {
        ElementalReactionEvents.ReactionStarted -= HandleReactionStarted;
        ElementalReactionEvents.ReactionProcExecuted -= HandleReactionProcExecuted;
        if (instance == this)
            instance = null;
    }

    public static void Spawn(DamageInfo info, float displayDamage)
    {
        if (!TryResolveInstance(out DamageNumberSpawner spawner))
            return;

        Vector3 position = info.hitPoint;
        if (position == Vector3.zero && info.source != null)
            position = info.source.transform.position;

        Vector3 presentationPosition = position + Vector3.up * 0.85f;
        if (!spawner.CanPresent(presentationPosition))
            return;

        DamageNumberPopup popup = spawner.GetPopup();
        if (popup != null)
        {
            popup.Initialize(
                displayDamage,
                info.isCritical,
                ResolveDamageNumberColor(info),
                presentationPosition,
                spawner.ReleasePopup,
                ResolveDamageNumberHorizontalOffsetRange(info));
        }
    }

    public static Color ResolveDamageNumberColor(DamageInfo info)
    {
        if (info.elementalReactionType == ElementalReactionType.Vaporize)
            return VaporizeReactionColor;
        if (info.elementalReactionType == ElementalReactionType.ThermalFracture)
            return FractureReactionColor;
        return ResolveElementDamageColor(info.element);
    }

    public static float ResolveDamageNumberHorizontalOffsetRange(DamageInfo info)
    {
        return info.elementalReactionType == ElementalReactionType.Vaporize
            ? DamageNumberPopup.VaporizeDamageHorizontalOffsetRange
            : 0f;
    }

    public static Color ResolveElementDamageColor(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire:
                return FireDamageColor;
            case WeaponElement.Water:
                return WaterDamageColor;
            case WeaponElement.Ice:
                return IceDamageColor;
            case WeaponElement.Electric:
                return ElectricDamageColor;
            case WeaponElement.Wind:
                return WindDamageColor;
            case WeaponElement.Earth:
                return EarthDamageColor;
            default:
                return PhysicalDamageColor;
        }
    }

    public static bool TryResolveReactionPresentation(
        ElementalReactionType reactionType,
        out string displayName,
        out Color color)
    {
        switch (reactionType)
        {
            case ElementalReactionType.Vaporize:
                displayName = "증기";
                color = VaporizeReactionColor;
                return true;
            case ElementalReactionType.ThermalFracture:
                displayName = "균열";
                color = FractureReactionColor;
                return true;
            case ElementalReactionType.Plasma:
                displayName = "플라즈마";
                color = PlasmaReactionColor;
                return true;
            case ElementalReactionType.Freeze:
                displayName = "빙결";
                color = FreezeReactionColor;
                return true;
            case ElementalReactionType.ChainElectricity:
                displayName = "연쇄감전";
                color = ChainShockReactionColor;
                return true;
            case ElementalReactionType.ColdCharge:
                displayName = "냉전하";
                color = ColdChargeReactionColor;
                return true;
            default:
                displayName = string.Empty;
                color = Color.clear;
                return false;
        }
    }

    public static bool TryResolveReactionProcPresentation(
        ElementalReactionProcType procType,
        out string displayName,
        out Color color)
    {
        switch (procType)
        {
            case ElementalReactionProcType.Plasma:
                displayName = "플라즈마 폭발";
                color = PlasmaReactionColor;
                return true;
            case ElementalReactionProcType.Shatter:
                displayName = "쇄빙";
                color = ShatterReactionColor;
                return true;
            case ElementalReactionProcType.ColdCharge:
                displayName = "냉기 폭발";
                color = ColdChargeProcColor;
                return true;
            default:
                displayName = string.Empty;
                color = Color.clear;
                return false;
        }
    }

    public static void SpawnHeal(Vector3 worldPosition, float displayHeal)
    {
        if (displayHeal <= 0f || !TryResolveInstance(out DamageNumberSpawner spawner))
            return;

        Vector3 presentationPosition = worldPosition + Vector3.up * 1.65f;
        if (!spawner.CanPresent(presentationPosition))
            return;

        DamageNumberPopup popup = spawner.GetPopup();
        if (popup != null)
            popup.InitializeCustom("+" + Mathf.RoundToInt(displayHeal), new Color(0.18f, 1f, 0.28f, 1f), presentationPosition, 24f, spawner.ReleasePopup);
    }

    public static void SpawnPlayerDamage(Vector3 worldPosition, float displayDamage)
    {
        if (displayDamage <= 0f || !TryResolveInstance(out DamageNumberSpawner spawner))
            return;

        Vector3 presentationPosition = worldPosition + Vector3.up * 1.65f;
        if (!spawner.CanPresent(presentationPosition))
            return;

        DamageNumberPopup popup = spawner.GetPopup();
        if (popup != null)
            popup.InitializeCustom("-" + Mathf.RoundToInt(displayDamage), new Color(1f, 0.16f, 0.12f, 1f), presentationPosition, 24f, spawner.ReleasePopup);
    }

    public static void SpawnStatusText(Vector3 worldPosition, string displayText, Color color)
    {
        if (string.IsNullOrWhiteSpace(displayText) || !TryResolveInstance(out DamageNumberSpawner spawner))
            return;

        Vector3 presentationPosition = worldPosition + Vector3.up * 1.7f;
        if (!spawner.CanPresent(presentationPosition))
            return;

        DamageNumberPopup popup = spawner.GetPopup();
        if (popup != null)
            popup.InitializeCustom(displayText, color, presentationPosition, 22f, spawner.ReleasePopup);
    }

    public static void SpawnCurrencyPickup(Vector3 worldPosition, string currencyName, int amount, Color color)
    {
        if (amount <= 0)
            return;

        string displayName = string.IsNullOrWhiteSpace(currencyName) ? "재화" : currencyName;
        Color displayColor = color.a > 0f ? color : DefaultCurrencyPickupColor;
        SpawnStatusText(worldPosition, $"{displayName} +{amount}", displayColor);
    }

    private static bool TryResolveInstance(out DamageNumberSpawner spawner)
    {
        if (instance == null)
            instance = FindFirstObjectByType<DamageNumberSpawner>(FindObjectsInactive.Include);

        spawner = instance;
        if (spawner != null && spawner.isActiveAndEnabled)
            return true;

        if (!missingSpawnerWarningLogged)
        {
            missingSpawnerWarningLogged = true;
            Debug.LogWarning("[DamageNumberSpawner] 화면 공간 데미지 플로팅 스포너를 찾지 못했습니다.");
        }

        return false;
    }

    private void HandleReactionStarted(ElementalReactionEvent reactionEvent)
    {
        if (!isActiveAndEnabled
            || !TryResolveReactionPresentation(reactionEvent.ReactionType, out string displayName, out Color color))
        {
            return;
        }

        Vector3 presentationPosition = reactionEvent.Center + Vector3.up * ReactionWorldOffset;
        if (!CanPresent(presentationPosition))
            return;

        DamageNumberPopup popup = GetPopup();
        if (popup == null)
            return;

        popup.InitializeReaction(
            displayName,
            color,
            presentationPosition,
            ReleasePopup);
    }

    private void HandleReactionProcExecuted(ElementalReactionProcEvent procEvent)
    {
        if (!isActiveAndEnabled
            || !TryResolveReactionProcPresentation(procEvent.ProcType, out string displayName, out Color color))
        {
            return;
        }

        Vector3 presentationPosition = procEvent.Center + Vector3.up * ReactionWorldOffset;
        if (!CanPresent(presentationPosition))
            return;

        DamageNumberPopup popup = GetPopup();
        if (popup == null)
            return;

        popup.InitializeReaction(
            displayName,
            color,
            presentationPosition,
            ReleasePopup);
    }

    private void PrewarmPool()
    {
        for (int i = 0; i < popupBudget; i++)
        {
            DamageNumberPopup popup = CreatePrewarmedPopup();
            if (popup == null)
                break;

            ReleasePopup(popup);
        }
    }

    private DamageNumberPopup GetPopup()
    {
        while (pool.Count > 0)
        {
            DamageNumberPopup popup = pool.Dequeue();
            if (popup != null)
            {
                popup.SetTargetCamera(ResolvePresentationCamera());
                return popup;
            }
        }

        return null;
    }

    private DamageNumberPopup CreatePrewarmedPopup()
    {
        if (damageNumberPrefab == null || popupRoot == null)
        {
            Debug.LogError("[DamageNumberSpawner] 화면 공간 데미지 프리팹 또는 루트가 연결되지 않았습니다.", this);
            return null;
        }

        GameObject instanceObject = Instantiate(damageNumberPrefab, popupRoot, false);
        DamageNumberPopup popup = instanceObject.GetComponent<DamageNumberPopup>();
        if (popup == null)
        {
            Debug.LogError("[DamageNumberSpawner] 데미지 숫자 프리팹에 DamageNumberPopup이 없습니다.", instanceObject);
            Destroy(instanceObject);
            return null;
        }

        popup.SetProjectionRoot(popupRoot);
        return popup;
    }

    private bool CanPresent(Vector3 worldPosition)
    {
        return WorldUiScreenProjection.TryProject(
            popupRoot,
            ResolvePresentationCamera(),
            worldPosition,
            out _,
            DamageNumberPopup.SpawnScreenMargin);
    }

    private Camera ResolvePresentationCamera()
    {
        if (presentationCamera == null || !presentationCamera.isActiveAndEnabled)
            presentationCamera = Camera.main;
        return presentationCamera;
    }

    private void ReleasePopup(DamageNumberPopup popup)
    {
        if (popup == null)
            return;

        popup.ResetForPool();
        popup.gameObject.SetActive(false);
        popup.transform.SetParent(popupRoot, false);
        pool.Enqueue(popup);
    }
}
