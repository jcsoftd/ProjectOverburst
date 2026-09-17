using UnityEngine;

public class UnifiedDebugAimLine : MonoBehaviour
{
    private static UnifiedDebugAimLine instance;

    [Header("References")]
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private MagicRuntime magicRuntimeController;
    [SerializeField] private Camera aimCamera;

    [Header("Line")]
    [SerializeField] private bool debugLineEnabled = true;
    [SerializeField] private float lineWidth = 0.035f;
    [SerializeField] private float meleeOriginHeightOffset = 0.08f;
    [SerializeField] private Color meleeColor = new Color(1f, 0f, 0f, 0.9f);
    [SerializeField] private Color magicColor = new Color(0.45f, 0.85f, 1f, 0.92f);

    private LineRenderer primaryLine;
    private LineRenderer secondaryLine;
    private LineRenderer tertiaryLine;
    private Material lineMaterial;

    public static bool IsActive => instance != null;
    public static bool DebugLineEnabled => instance == null || instance.debugLineEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureSceneLine()
    {
        if (FindFirstObjectByType<UnifiedDebugAimLine>() != null)
            return;

        PlayerEquipment equipment = FindFirstObjectByType<PlayerEquipment>();
        if (equipment != null)
            equipment.gameObject.AddComponent<UnifiedDebugAimLine>();
    }

    private void Awake()
    {
        instance = this;
        ResolveReferences();
        EnsureLines();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        if (lineMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(lineMaterial);
        else
            DestroyImmediate(lineMaterial);
    }

    private void LateUpdate()
    {
        ResolveReferences();
        RefreshLine();
    }

    public void ToggleDebugLine()
    {
        SetDebugLineEnabled(!debugLineEnabled);
    }

    public void SetDebugLineEnabled(bool value)
    {
        debugLineEnabled = value;
        if (!debugLineEnabled)
            HideDebugLine();
    }

    public string GetToggleLabel()
    {
        return debugLineEnabled ? "Debug Line ON" : "Debug Line OFF";
    }

    private void ResolveReferences()
    {
        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        if (playerController == null)
            playerController = GetComponent<PlayerMovement>();

        if (magicRuntimeController == null)
            magicRuntimeController = GetComponent<MagicRuntime>();

        if (aimCamera == null)
            aimCamera = Camera.main;
    }

    private void RefreshLine()
    {
        EnsureLines();

        if (!ShouldShowAnyDebugLine())
        {
            HideDebugLine();
            return;
        }

        if (TryDrawMelee())
            return;

        if (TryDrawMagic())
            return;

        HideDebugLine();
    }

    private bool ShouldShowAnyDebugLine()
    {
        if (!debugLineEnabled || GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        if (playerEquipment == null || !playerEquipment.HasCurrentWeapon)
            return false;

        return playerEquipment.CurrentWeaponRuntimeStatus.IsUsable;
    }

    private bool TryDrawMelee()
    {
        if (!ShouldShowMeleeLine())
            return false;

        if (!MeleeAimCalculator.TryGetMouseDirectionFromPlayer(transform, aimCamera, out Vector3 direction))
            return false;

        float range = Mathf.Max(0.1f, playerEquipment.CurrentWeaponStats.range);
        Vector3 origin = transform.position + Vector3.up * meleeOriginHeightOffset;
        SetLine(primaryLine, origin, origin + direction * range, meleeColor);
        SetVisible(true, false, false);
        return true;
    }

    private bool ShouldShowMeleeLine()
    {
        WeaponRuntimeStatus runtimeStatus = playerEquipment.CurrentWeaponRuntimeStatus;
        if (runtimeStatus.Kind != WeaponRuntimeKind.Melee || !runtimeStatus.IsUsable)
            return false;

        return playerEquipment.CanCurrentWeaponUseMeleeSlash && aimCamera != null;
    }

    private bool TryDrawMagic()
    {
        if (!ShouldShowMagicLine())
            return false;

        if (!magicRuntimeController.TryGetCurrentAimLine(out MagicAimLine aimLine))
            return false;

        SetLine(primaryLine, aimLine.origin, aimLine.End, magicColor);
        SetVisible(true, false, false);
        return true;
    }

    private bool ShouldShowMagicLine()
    {
        WeaponRuntimeStatus runtimeStatus = playerEquipment.CurrentWeaponRuntimeStatus;
        if (runtimeStatus.Kind != WeaponRuntimeKind.Magic || !runtimeStatus.IsUsable)
            return false;

        if (!playerEquipment.CanCurrentWeaponUseMagicAim)
            return false;

        if (playerController != null && !playerController.IsAiming)
            return false;

        return magicRuntimeController != null;
    }

    private void EnsureLines()
    {
        if (primaryLine != null)
            return;

        lineMaterial = CreateMaterial();
        primaryLine = CreateLine("UnifiedDebugAimLine_Primary");
        secondaryLine = CreateLine("UnifiedDebugAimLine_Secondary");
        tertiaryLine = CreateLine("UnifiedDebugAimLine_Tertiary");
        SetVisible(false, false, false);
    }

    private LineRenderer CreateLine(string lineName)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.material = lineMaterial;
        line.numCapVertices = 4;
        line.alignment = LineAlignment.View;
        line.sortingOrder = 5000;
        return line;
    }

    private Material CreateMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader);
        material.name = "UnifiedDebugAimLine_Material";
        return material;
    }

    private void SetLine(LineRenderer line, Vector3 start, Vector3 end, Color color)
    {
        if (line == null)
            return;

        line.startColor = color;
        line.endColor = color;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    private void SetVisible(bool primary, bool secondary, bool tertiary)
    {
        if (primaryLine != null)
            primaryLine.enabled = primary;
        if (secondaryLine != null)
            secondaryLine.enabled = secondary;
        if (tertiaryLine != null)
            tertiaryLine.enabled = tertiary;
    }

    private void HideDebugLine()
    {
        SetVisible(false, false, false);
    }
}
