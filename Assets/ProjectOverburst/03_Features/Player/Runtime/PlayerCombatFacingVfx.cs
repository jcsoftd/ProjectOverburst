using UnityEngine;

/// <summary>전투 중 조작 캐릭터의 실제 전방을 접지면에 은백색으로 표시한다.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerMovement)), DefaultExecutionOrder(450)]
public sealed class PlayerCombatFacingVfx : MonoBehaviour
{
    [SerializeField] Transform visualRoot;
    [SerializeField] Mesh[] quietMeshes;
    [SerializeField] Mesh[] extendedMeshes;
    [SerializeField, Min(.1f)] float worldScale = .45f;
    [SerializeField, Min(0f)] float groundOffset = .015f;
    [SerializeField, Min(.1f)] float groundProbeDistance = 2f;
    [SerializeField, Min(.01f)] float fadeDuration = .16f;
    [SerializeField] LayerMask groundLayers = Physics.DefaultRaycastLayers;

    static readonly string[] VariantNodes = { "RingToTip_-1", "RingToTip_1", "InnerFeather_-1", "InnerFeather_1", "FilledForwardCap", "QuietNoseCore" };
    static readonly int BrightnessId = Shader.PropertyToID("_SilverBrightness");
    MeshFilter[] variantFilters;
    int appliedStyle = -1;

    static readonly int ClockId = Shader.PropertyToID("_SilverRuntimeClock");
    static readonly int TimeId = Shader.PropertyToID("_SilverRuntimeTime");
    static readonly int VisibilityId = Shader.PropertyToID("_SilverVisibility");
    readonly RaycastHit[] groundHits = new RaycastHit[12];
    PlayerMovement movement;
    CombatHealth health;
    Renderer[] renderers;
    MaterialPropertyBlock properties;
    float visibility, phase;
    Vector3 surfaceNormal = Vector3.up;
    bool hadGround;

    public Transform VisualRoot => visualRoot;
    public CombatFacingIndicatorStyle AppliedStyle => (CombatFacingIndicatorStyle)appliedStyle;
    public float Visibility => visibility;
    public float FlowTime => phase;
    public bool IsVisible => visualRoot != null && visualRoot.gameObject.activeSelf && visibility > .001f;

    void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<CombatHealth>();
        if (properties == null) properties = new MaterialPropertyBlock();
        renderers = visualRoot != null ? visualRoot.GetComponentsInChildren<Renderer>(true) : null;
        visibility = 0f; hadGround = false; surfaceNormal = Vector3.up;
        variantFilters = new MeshFilter[VariantNodes.Length];
        for (int i = 0; visualRoot != null && i < VariantNodes.Length; i++)
            variantFilters[i] = visualRoot.Find(VariantNodes[i])?.GetComponent<MeshFilter>();
        appliedStyle = -1;
        ApplyAppearance();
        ApplyRendererProperties();
        if (visualRoot != null) visualRoot.gameObject.SetActive(false);
        OverburstGameSettings.Changed += OnSettingsChanged;
    }

    void LateUpdate()
    {
        if (visualRoot == null || renderers == null) return;
        if (!OverburstGameSettings.CombatFacingIndicator)
        {
            if (visibility > 0f || visualRoot.gameObject.activeSelf) HideImmediately();
            return;
        }
        float dt = OverburstGameClock.UnscaledDeltaTime;
        phase = Mathf.Repeat(phase + dt, 8f);
        bool requested = PlayerCombatModeController.IsSharedCombatModeActive()
            && movement != null && movement.isActiveAndEnabled
            && movement.ControlAuthority == ActorControlAuthority.Player
            && (health == null || !health.IsDead);
        bool grounded = false;
        RaycastHit ground = default;
        if (requested || visibility > 0f) grounded = TryGround(out ground);
        bool show = requested && grounded;
        visibility = Mathf.MoveTowards(visibility, show ? 1f : 0f, dt / Mathf.Max(.01f, fadeDuration));
        if (visibility <= 0f)
        {
            visualRoot.gameObject.SetActive(false);
            hadGround = false;
            return;
        }
        if (grounded)
        {
            surfaceNormal = hadGround ? Vector3.Slerp(surfaceNormal, ground.normal, 1f - Mathf.Exp(-22f * dt)) : ground.normal;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, surfaceNormal);
            if (forward.sqrMagnitude < .001f) forward = Vector3.ProjectOnPlane(Vector3.forward, surfaceNormal);
            visualRoot.SetPositionAndRotation(ground.point + surfaceNormal * groundOffset, Quaternion.LookRotation(forward.normalized, surfaceNormal));
            Vector3 parentScale = transform.lossyScale;
            visualRoot.localScale = new Vector3(worldScale / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                worldScale / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), worldScale / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
            hadGround = true;
        }
        visualRoot.gameObject.SetActive(true);
        ApplyAppearance();
        ApplyRendererProperties();
    }

    void ApplyAppearance()
    {
        int style = (int)OverburstGameSettings.CombatFacingStyle;
        if (appliedStyle == style || variantFilters == null) return;
        Mesh[] meshes = style == (int)CombatFacingIndicatorStyle.Quiet ? quietMeshes : extendedMeshes;
        if (meshes == null || meshes.Length != VariantNodes.Length) return;
        for (int i = 0; i < meshes.Length; i++) if (meshes[i] == null || variantFilters[i] == null) return;
        for (int i = 0; i < meshes.Length; i++) variantFilters[i].sharedMesh = meshes[i];
        appliedStyle = style;
    }

    void ApplyRendererProperties()
    {
        if (renderers == null || properties == null) return;
        float brightness = OverburstGameSettings.CombatFacingBrightness;
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(properties);
            properties.SetFloat(ClockId, 1f);
            properties.SetFloat(TimeId, phase);
            properties.SetFloat(VisibilityId, visibility);
            properties.SetFloat(BrightnessId, brightness);
            renderer.SetPropertyBlock(properties);
        }
    }

    bool TryGround(out RaycastHit closest)
    {
        closest = default;
        int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * .6f, Vector3.down,
            groundHits, groundProbeDistance, groundLayers, QueryTriggerInteraction.Ignore);
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.collider == null || hit.normal.y < .5f || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.GetComponentInParent<CombatTarget>() != null) continue;
            if (!found || hit.distance < closest.distance) { closest = hit; found = true; }
        }
        return found;
    }

    void HideImmediately()
    {
        visibility = 0f; hadGround = false;
        if (visualRoot != null) visualRoot.gameObject.SetActive(false);
    }

    void OnSettingsChanged()
    {
        if (!OverburstGameSettings.CombatFacingIndicator) HideImmediately();
        ApplyAppearance();
        ApplyRendererProperties();
    }

    void OnDisable()
    {
        OverburstGameSettings.Changed -= OnSettingsChanged;
        HideImmediately();
    }
}
