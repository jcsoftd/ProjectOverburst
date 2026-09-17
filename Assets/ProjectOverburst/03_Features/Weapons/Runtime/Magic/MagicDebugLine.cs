using UnityEngine;
using UnityEngine.Serialization;

public class MagicDebugLine : MonoBehaviour // 마법 조준선 fallback
{
    [Header("References")]
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerMovement playerController;
    [FormerlySerializedAs("magicWeaponCaster")]
    [SerializeField] private MagicRuntime magicRuntimeController;
    [SerializeField] private Camera aimCamera;

    [Header("Line")]
    [SerializeField] private float lineWidth = 0.04f;
    [SerializeField] private Color lineColor = new Color(1f, 0.28f, 0.02f, 0.92f);

    private LineRenderer directionLine; // 방향선
    private Material lineMaterial; // 실행 중 재질

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureSceneLine()
    {
        PlayerEquipment equipment = FindFirstObjectByType<PlayerEquipment>();
        if (equipment != null && equipment.GetComponent<MagicDebugLine>() == null)
            equipment.gameObject.AddComponent<MagicDebugLine>();
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureLine();
    }

    private void Update()
    {
        ResolveReferences();
        RefreshLine();
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
        EnsureLine();

        if (directionLine == null)
            return;

        if (UnifiedDebugAimLine.IsActive || !CanShowLine())
        {
            directionLine.enabled = false;
            return;
        }

        if (magicRuntimeController == null || !magicRuntimeController.TryGetCurrentAimLine(out MagicAimLine aimLine))
        {
            directionLine.enabled = false;
            return;
        }

        directionLine.enabled = true;
        directionLine.SetPosition(0, aimLine.origin);
        directionLine.SetPosition(1, aimLine.End);
    }

    private bool CanShowLine()
    {
        if (GameplayInputBlocker.IsGameplayInputBlocked || playerEquipment == null || playerController == null)
            return false;

        return playerEquipment.HasCurrentWeapon && playerController.IsAiming && playerEquipment.CanCurrentWeaponUseMagicAim;
    }

    private void EnsureLine()
    {
        if (directionLine != null)
            return;

        GameObject lineObject = new GameObject("MagicDebugLine_FireDirection");
        lineObject.transform.SetParent(transform, false);
        directionLine = lineObject.AddComponent<LineRenderer>();
        directionLine.useWorldSpace = true;
        directionLine.positionCount = 2;
        directionLine.startWidth = lineWidth;
        directionLine.endWidth = lineWidth;
        directionLine.startColor = lineColor;
        directionLine.endColor = new Color(lineColor.r, lineColor.g * 0.45f, 0f, lineColor.a * 0.35f);
        directionLine.alignment = LineAlignment.View;
        directionLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        directionLine.receiveShadows = false;
        directionLine.enabled = false;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader != null)
        {
            lineMaterial = new Material(shader);
            lineMaterial.name = "Runtime_MagicDebugLine";
            directionLine.material = lineMaterial;
        }
    }

    private void OnDestroy()
    {
        if (lineMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(lineMaterial);
        else
            DestroyImmediate(lineMaterial);
    }
}
