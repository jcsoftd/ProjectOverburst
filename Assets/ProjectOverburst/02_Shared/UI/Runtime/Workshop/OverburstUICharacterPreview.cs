using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Renders the current actor's visible meshes on an isolated, animation-only rig.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class OverburstUICharacterPreview : MonoBehaviour, IBeginDragHandler, IDragHandler,
    IEndDragHandler, ICanvasRaycastFilter
{
    private const int PreviewLayer = 30;
    private const double RefreshSeconds = 0.75d;
    private const double PreviewFrameSeconds = 1d / 30d;
    private const float IdleSampleSeconds = 0.4f;
    private const float IdlePlaybackSpeed = 0.85f;
    private const float InitialYaw = 150f;
    private const float Aspect = 512f / 768f;

    [SerializeField] private GameObject visualPrefab;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private RawImage target;
    [SerializeField, Range(0.1f, 2f)] private float dragDegreesPerPixel = 0.75f;

    private static GameObject rig;
    private static GameObject model;
    private static Camera previewCamera;
    private static RenderTexture texture;
    private static GameObject currentSource;
    private static Renderer[] templateRenderers;
    private static Dictionary<string, Transform> templateByPath;
    private static Dictionary<string, List<Transform>> templateByName;
    private static GameObject weaponVisual;
    private static GameObject shieldVisual;
    private static int users;

    private PlayerContext subscribedContext;
    private PlayerEquipment subscribedEquipment;
    private PlayerActorRuntime displayedActor;
    private Transform displayedWeapon;
    private GameObject displayedShield;
    private int displayedAppearance;
    private double nextRefresh;
    private double nextPreviewFrame;
    private double idleStartedAt;
    private float yawOffset;
    private bool dragging;
    private bool inputConfigured;
    private bool originalRaycastTarget;
    private bool registered;
    private bool refreshing;

    public RenderTexture Texture => texture;
    public static int ActiveUsers => users;

    public void Configure(GameObject source, AnimationClip idle, RawImage image)
    {
        visualPrefab = source;
        idleClip = idle;
        target = image;
        Register();
        RefreshFromCurrentActor();
    }

    private void OnEnable() => Register();

    private void Register()
    {
        if (registered || !visualPrefab || !target || !gameObject.scene.IsValid())
            return;

        registered = true;
        users++;
        nextRefresh = 0d;
        EnsureRig();
        target.texture = texture;
        EnableDragHitTarget();
        RefreshFromCurrentActor();
    }

    private void OnDisable() => Release();
    private void OnDestroy() => Release();

    private void Release()
    {
        if (!registered)
            return;

        Unsubscribe();
        RestoreDragHitTarget();
        dragging = false;
        registered = false;
        users = Mathf.Max(0, users - 1);
        if (target)
            target.texture = null;
        if (users == 0)
            DisposeRig();
    }

    private void Update()
    {
        if (!registered)
            Register();
        if (!registered)
            return;

        EnableDragHitTarget();
        double now = Time.realtimeSinceStartupAsDouble;
        if (Application.isPlaying && now >= nextPreviewFrame)
        {
            nextPreviewFrame = now + PreviewFrameSeconds;
            if (model && model.activeInHierarchy)
                SampleIdle(GetIdleTime(now));
            RenderNow();
        }

        if (now < nextRefresh)
            return;

        nextRefresh = now + RefreshSeconds;
        if (!Application.isPlaying)
        {
            RenderNow();
            return;
        }

        BindCurrentActor();
        PlayerActorRuntime actor = subscribedContext ? subscribedContext.CurrentActor : null;
        Transform weapon = subscribedEquipment ? subscribedEquipment.CurrentWeaponRoot : null;
        OneHandSwordShieldSet shieldSet = weapon ? weapon.GetComponentInChildren<OneHandSwordShieldSet>(true) : null;
        GameObject shield = shieldSet ? shieldSet.ShieldInstance : null;
        P09CharacterVisualAdapter adapter = actor ? actor.GetComponentInChildren<P09CharacterVisualAdapter>(true) : null;
        int appearance = adapter ? ComputeAppearanceFingerprint(adapter.ModelRoot, weapon, shield ? shield.transform : null) : 0;
        if (actor != displayedActor || weapon != displayedWeapon || shield != displayedShield
            || appearance != displayedAppearance)
            RefreshFromCurrentActor();
    }

    private void BindCurrentActor()
    {
        PlayerContext context = Application.isPlaying ? PlayerContext.GetOrCreate() : null;
        if (subscribedContext != context)
        {
            if (subscribedContext)
                subscribedContext.CurrentActorChanged -= HandleActorChanged;
            subscribedContext = context;
            if (subscribedContext)
                subscribedContext.CurrentActorChanged += HandleActorChanged;
        }

        PlayerEquipment equipment = subscribedContext ? subscribedContext.CurrentActorEquipment : null;
        if (subscribedEquipment == equipment)
            return;
        if (subscribedEquipment)
            subscribedEquipment.WeaponSlotsChanged -= HandleWeaponChanged;
        subscribedEquipment = equipment;
        if (subscribedEquipment)
            subscribedEquipment.WeaponSlotsChanged += HandleWeaponChanged;
    }

    private void Unsubscribe()
    {
        if (subscribedContext)
            subscribedContext.CurrentActorChanged -= HandleActorChanged;
        if (subscribedEquipment)
            subscribedEquipment.WeaponSlotsChanged -= HandleWeaponChanged;
        subscribedContext = null;
        subscribedEquipment = null;
    }

    private void HandleActorChanged(PlayerActorRuntime actor) => RefreshFromCurrentActor();
    private void HandleWeaponChanged() => RefreshFromCurrentActor();

    public void RefreshFromCurrentActor()
    {
        if (!registered || refreshing)
            return;

        refreshing = true;
        try
        {
            EnsureRig();
            if (!model)
                return;

            BindCurrentActor();
            if (!Application.isPlaying)
            {
                model.SetActive(true);
                SampleIdle(IdleSampleSeconds);
                FrameModel();
                return;
            }

            PlayerActorRuntime actor = subscribedContext ? subscribedContext.CurrentActor : null;
            P09CharacterVisualAdapter adapter = actor ? actor.GetComponentInChildren<P09CharacterVisualAdapter>(true) : null;
            Transform sourceRoot = adapter ? adapter.ModelRoot : null;
            if (!sourceRoot)
            {
                model.SetActive(false);
                ClearEquipmentVisuals();
                displayedActor = actor;
                displayedWeapon = null;
                displayedShield = null;
                displayedAppearance = 0;
                return;
            }

            model.SetActive(true);
            SynchronizeAppearance(sourceRoot, actor.Equipment);
            displayedActor = actor;
            displayedWeapon = actor.Equipment ? actor.Equipment.CurrentWeaponRoot : null;
            OneHandSwordShieldSet shieldSet = displayedWeapon
                ? displayedWeapon.GetComponentInChildren<OneHandSwordShieldSet>(true) : null;
            displayedShield = shieldSet ? shieldSet.ShieldInstance : null;
            displayedAppearance = ComputeAppearanceFingerprint(sourceRoot, displayedWeapon,
                displayedShield ? displayedShield.transform : null);
            SampleIdle(GetIdleTime(Time.realtimeSinceStartupAsDouble));
            FrameModel();
        }
        finally
        {
            refreshing = false;
        }
    }

    private static int ComputeAppearanceFingerprint(Transform sourceRoot, Transform weapon, Transform shield)
    {
        unchecked
        {
            int hash = 17;
            foreach (Renderer renderer in sourceRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;
                if (weapon && renderer.transform.IsChildOf(weapon))
                    continue;
                if (shield && renderer.transform.IsChildOf(shield))
                    continue;
                hash = hash * 31 + (renderer.gameObject.activeInHierarchy && renderer.enabled ? 1 : 0);
                hash = hash * 31 + (GetMesh(renderer) ? GetMesh(renderer).GetInstanceID() : 0);
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    hash = hash * 31 + (materials[i] ? materials[i].GetInstanceID() : 0);
            }
            return hash;
        }
    }

    public void RenderNow()
    {
        if (!registered || !previewCamera || !target)
            return;

        // Entering Play can enable several preview panels before URP has created its
        // pipeline. Camera.Render at that point recursively initializes URP's Blitter.
#if UNITY_EDITOR
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying)
            return;
#endif
        if (Application.isPlaying && UnityEngine.Rendering.RenderPipelineManager.currentPipeline == null)
            return;

        previewCamera.Render();
        target.texture = texture;
    }

    private float GetIdleTime(double now)
    {
        if (!idleClip || idleClip.length <= 0f)
            return 0f;
        return Mathf.Repeat(IdleSampleSeconds + (float)((now - idleStartedAt) * IdlePlaybackSpeed), idleClip.length);
    }

    private void SampleIdle(float time)
    {
        if (idleClip && model)
            idleClip.SampleAnimation(model, Mathf.Min(time, idleClip.length));
        ApplyPreviewYaw();
    }

    private void ApplyPreviewYaw()
    {
        if (!model)
            return;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, InitialYaw + yawOffset, 0f);
    }

    private void EnableDragHitTarget()
    {
        if (!Application.isPlaying || inputConfigured || !target)
            return;
        originalRaycastTarget = target.raycastTarget;
        target.raycastTarget = true;
        inputConfigured = true;
        idleStartedAt = Time.realtimeSinceStartupAsDouble;
        nextPreviewFrame = 0d;
    }

    private void RestoreDragHitTarget()
    {
        if (!inputConfigured)
            return;
        if (target)
            target.raycastTarget = originalRaycastTarget;
        inputConfigured = false;
    }

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (!Application.isPlaying || !target ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(target.rectTransform,
                screenPoint, eventCamera, out Vector2 localPoint))
            return false;

        Rect bounds = target.rectTransform.rect;
        return bounds.Contains(localPoint) &&
            Mathf.Abs(localPoint.x - bounds.center.x) <= bounds.width * 0.27f;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = eventData.button == PointerEventData.InputButton.Left;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || !model || eventData.button != PointerEventData.InputButton.Left)
            return;
        yawOffset = Mathf.Repeat(yawOffset + eventData.delta.x * dragDegreesPerPixel, 360f);
        ApplyPreviewYaw();
        FrameModel();
        RenderNow();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        dragging = false;
    }

    private static void SynchronizeAppearance(Transform sourceRoot, PlayerEquipment equipment)
    {
        ClearEquipmentVisuals();

        var sourceByPath = new Dictionary<Transform, string>();
        CollectPaths(sourceRoot, string.Empty, sourceByPath);
        var used = new HashSet<Renderer>();

        foreach (Renderer renderer in templateRenderers)
            if (renderer)
                renderer.enabled = false;

        Transform weaponRoot = equipment ? equipment.CurrentWeaponRoot : null;
        OneHandSwordShieldSet shieldSet = weaponRoot ? weaponRoot.GetComponentInChildren<OneHandSwordShieldSet>(true) : null;
        Transform shieldRoot = shieldSet && shieldSet.ShieldInstance ? shieldSet.ShieldInstance.transform : null;

        foreach (Renderer source in sourceRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!(source is MeshRenderer) && !(source is SkinnedMeshRenderer))
                continue;
            if (!source.enabled || !source.gameObject.activeInHierarchy)
                continue;
            if (weaponRoot && source.transform.IsChildOf(weaponRoot))
                continue;
            if (shieldRoot && source.transform.IsChildOf(shieldRoot))
                continue;

            Renderer destination = MatchRenderer(source, sourceByPath, used);
            if (!destination)
                continue;

            used.Add(destination);
            ActivateToRoot(destination.transform);
            CopyRenderer(source, destination, sourceByPath, null, true);
        }

        if (weaponRoot)
            BuildWeaponVisual(weaponRoot, equipment.CurrentWeaponPose, sourceByPath);
        if (shieldRoot)
            BuildShieldVisual(shieldRoot, shieldSet, sourceByPath);
    }

    private static Renderer MatchRenderer(Renderer source, Dictionary<Transform, string> sourceByPath, HashSet<Renderer> used)
    {
        if (sourceByPath.TryGetValue(source.transform, out string path) && templateByPath.TryGetValue(path, out Transform atPath))
        {
            Renderer exact = source is SkinnedMeshRenderer
                ? (Renderer)atPath.GetComponent<SkinnedMeshRenderer>()
                : atPath.GetComponent<MeshRenderer>();
            if (exact && !used.Contains(exact))
                return exact;
        }

        Mesh mesh = GetMesh(source);
        foreach (Renderer candidate in templateRenderers)
            if (candidate && !used.Contains(candidate) && candidate.GetType() == source.GetType()
                && candidate.name == source.name && GetMesh(candidate) == mesh)
                return candidate;

        foreach (Renderer candidate in templateRenderers)
            if (candidate && !used.Contains(candidate) && candidate.GetType() == source.GetType()
                && GetMesh(candidate) == mesh)
                return candidate;
        return null;
    }

    private static Mesh GetMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
            return skinned.sharedMesh;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter ? filter.sharedMesh : null;
    }

    private static void CopyRenderer(Renderer source, Renderer destination,
        Dictionary<Transform, string> sourceByPath, Dictionary<Transform, Transform> dynamicMap, bool copyPropertyBlock)
    {
        destination.sharedMaterials = source.sharedMaterials;
        destination.enabled = source.enabled;
        destination.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (source is MeshRenderer && destination is MeshRenderer)
        {
            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            MeshFilter targetFilter = destination.GetComponent<MeshFilter>();
            if (sourceFilter && targetFilter)
                targetFilter.sharedMesh = sourceFilter.sharedMesh;
        }
        else if (source is SkinnedMeshRenderer sourceSkin && destination is SkinnedMeshRenderer targetSkin)
        {
            targetSkin.sharedMesh = sourceSkin.sharedMesh;
            Transform rootBone = ResolveBone(sourceSkin.rootBone, sourceByPath, dynamicMap);
            if (rootBone)
                targetSkin.rootBone = rootBone;
            Transform[] sourceBones = sourceSkin.bones;
            Transform[] originalBones = targetSkin.bones;
            Transform[] targetBones = new Transform[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                Transform mapped = ResolveBone(sourceBones[i], sourceByPath, dynamicMap);
                targetBones[i] = mapped ? mapped : i < originalBones.Length ? originalBones[i] : null;
            }
            targetSkin.bones = targetBones;
            targetSkin.updateWhenOffscreen = true;
            int blendCount = sourceSkin.sharedMesh ? sourceSkin.sharedMesh.blendShapeCount : 0;
            for (int i = 0; i < blendCount; i++)
                targetSkin.SetBlendShapeWeight(i, sourceSkin.GetBlendShapeWeight(i));
        }

        if (!copyPropertyBlock)
            return;

        var block = new MaterialPropertyBlock();
        source.GetPropertyBlock(block);
        destination.SetPropertyBlock(block);
        for (int i = 0; i < source.sharedMaterials.Length; i++)
        {
            block.Clear();
            source.GetPropertyBlock(block, i);
            destination.SetPropertyBlock(block, i);
        }
    }

    private static Transform ResolveBone(Transform source, Dictionary<Transform, string> sourceByPath,
        Dictionary<Transform, Transform> dynamicMap)
    {
        if (!source)
            return null;
        if (dynamicMap != null && dynamicMap.TryGetValue(source, out Transform dynamicBone))
            return dynamicBone;
        if (sourceByPath.TryGetValue(source, out string path) && templateByPath.TryGetValue(path, out Transform bone))
            return bone;
        if (templateByName.TryGetValue(source.name, out List<Transform> matches) && matches.Count == 1)
            return matches[0];
        return null;
    }

    private static void BuildWeaponVisual(Transform sourceRoot, WeaponPose pose,
        Dictionary<Transform, string> sourceByPath)
    {
        Transform hand = FindNamed(P09CharacterVisualAdapter.RightHandWeaponSocketName);
        if (!hand)
            return;

        weaponVisual = CloneVisualHierarchy(sourceRoot, hand, sourceByPath);
        if (!weaponVisual)
            return;

        WeaponGripMount grip = sourceRoot.GetComponentInChildren<WeaponGripMount>(true);
        if (pose && grip)
        {
            Vector3 holdPosition = pose.GetPoseLocalPositionForTuning(WeaponPoseSlot.Hold);
            Quaternion holdRotation = Quaternion.Euler(pose.GetPoseLocalRotationForTuning(WeaponPoseSlot.Hold));
            grip.TryResolveRootLocalPose(sourceRoot, WeaponGripAnchor.RightHand, holdPosition, holdRotation,
                out Vector3 rootPosition, out Quaternion rootRotation);
            weaponVisual.transform.localPosition = rootPosition;
            weaponVisual.transform.localRotation = rootRotation;
        }
        else if (sourceRoot.parent != hand)
        {
            weaponVisual.transform.localPosition = Vector3.zero;
            weaponVisual.transform.localRotation = Quaternion.identity;
        }
    }

    private static void BuildShieldVisual(Transform sourceRoot, OneHandSwordShieldSet shieldSet,
        Dictionary<Transform, string> sourceByPath)
    {
        Transform hand = FindNamed(P09CharacterVisualAdapter.LeftHandShieldSocketName);
        if (!hand)
            return;

        shieldVisual = CloneVisualHierarchy(sourceRoot, hand, sourceByPath);
        if (!shieldVisual)
            return;

        ShieldGripMount grip = sourceRoot.GetComponentInChildren<ShieldGripMount>(true);
        Transform point = grip ? grip.GripPoint : null;
        if (!point)
            return;

        Vector3 pointPosition = sourceRoot.InverseTransformPoint(point.position);
        Quaternion pointRotation = Quaternion.Inverse(sourceRoot.rotation) * point.rotation;
        Quaternion offset = Quaternion.Euler(shieldSet.HandSocketLocalRotationOffset);
        Quaternion rootRotation = offset * Quaternion.Inverse(pointRotation);
        shieldVisual.transform.localRotation = rootRotation;
        shieldVisual.transform.localPosition = -(rootRotation * Vector3.Scale(pointPosition, sourceRoot.localScale));
    }

    private static GameObject CloneVisualHierarchy(Transform sourceRoot, Transform parent,
        Dictionary<Transform, string> sourceByPath)
    {
        var copied = new Dictionary<Transform, Transform>();
        Transform cloneRoot = CloneTransforms(sourceRoot, parent, copied);
        foreach (KeyValuePair<Transform, Transform> pair in copied)
        {
            MeshRenderer mesh = pair.Key.GetComponent<MeshRenderer>();
            if (mesh && mesh.enabled)
            {
                MeshFilter sourceFilter = pair.Key.GetComponent<MeshFilter>();
                if (sourceFilter)
                {
                    pair.Value.gameObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                    CopyRenderer(mesh, pair.Value.gameObject.AddComponent<MeshRenderer>(), sourceByPath, copied, false);
                }
            }
            SkinnedMeshRenderer skinned = pair.Key.GetComponent<SkinnedMeshRenderer>();
            if (skinned && skinned.enabled)
                CopyRenderer(skinned, pair.Value.gameObject.AddComponent<SkinnedMeshRenderer>(), sourceByPath, copied, false);
        }
        return cloneRoot ? cloneRoot.gameObject : null;
    }

    private static Transform CloneTransforms(Transform source, Transform parent,
        Dictionary<Transform, Transform> copied)
    {
        var clone = new GameObject(source.name + " • UI Visual");
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.layer = PreviewLayer;
        Transform destination = clone.transform;
        destination.SetParent(parent, false);
        destination.localPosition = source.localPosition;
        destination.localRotation = source.localRotation;
        destination.localScale = source.localScale;
        copied.Add(source, destination);
        for (int i = 0; i < source.childCount; i++)
            CloneTransforms(source.GetChild(i), destination, copied);
        clone.SetActive(source.gameObject.activeSelf);
        return destination;
    }

    private static void ClearEquipmentVisuals()
    {
        if (weaponVisual)
        {
            weaponVisual.SetActive(false);
            DestroyOwned(weaponVisual);
        }
        if (shieldVisual)
        {
            shieldVisual.SetActive(false);
            DestroyOwned(shieldVisual);
        }
        weaponVisual = null;
        shieldVisual = null;
    }

    private static void CollectPaths(Transform root, string path, Dictionary<Transform, string> result)
    {
        result[root] = path;
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            ordinals.TryGetValue(child.name, out int ordinal);
            ordinals[child.name] = ordinal + 1;
            CollectPaths(child, path + "/" + child.name + "[" + ordinal + "]", result);
        }
    }

    private static void CollectTemplate(Transform root, string path)
    {
        templateByPath[path] = root;
        if (!templateByName.TryGetValue(root.name, out List<Transform> names))
        {
            names = new List<Transform>();
            templateByName.Add(root.name, names);
        }
        names.Add(root);
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            ordinals.TryGetValue(child.name, out int ordinal);
            ordinals[child.name] = ordinal + 1;
            CollectTemplate(child, path + "/" + child.name + "[" + ordinal + "]");
        }
    }

    private static Transform FindNamed(string name)
    {
        return templateByName != null && templateByName.TryGetValue(name, out List<Transform> matches)
            && matches.Count > 0 ? matches[0] : null;
    }

    private static void ActivateToRoot(Transform targetTransform)
    {
        for (Transform current = targetTransform; current; current = current.parent)
        {
            current.gameObject.SetActive(true);
            if (current == model.transform)
                break;
        }
    }

    private static void FrameModel()
    {
        if (!previewCamera || !model || !model.activeInHierarchy)
            return;

        bool found = false;
        Bounds bounds = default;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        if (!found)
            return;

        previewCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / Aspect) * 1.08f;
        previewCamera.transform.position = bounds.center + new Vector3(0f, 0.03f, -5f);
        previewCamera.transform.LookAt(bounds.center + new Vector3(0f, 0.03f, 0f));
        previewCamera.farClipPlane = Mathf.Max(12f, bounds.extents.z * 2f + 8f);
    }

    private void EnsureRig()
    {
        if (rig && currentSource == visualPrefab)
        {
            if (target)
                target.texture = texture;
            return;
        }

        DisposeRig();
        currentSource = visualPrefab;
        rig = new GameObject("UI Character Preview • isolated");
        rig.hideFlags = HideFlags.HideAndDontSave;
        rig.transform.position = new Vector3(0f, -10000f, 0f);
        model = Instantiate(visualPrefab, rig.transform, false);
        model.name = "Player Visual Only";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, InitialYaw + yawOffset, 0f);
        model.transform.localScale = Vector3.one;
        foreach (Transform transformPart in model.GetComponentsInChildren<Transform>(true))
        {
            transformPart.gameObject.layer = PreviewLayer;
            transformPart.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        Animator animator = model.GetComponent<Animator>();
        if (animator)
        {
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        templateRenderers = model.GetComponentsInChildren<Renderer>(true);
        templateByPath = new Dictionary<string, Transform>(StringComparer.Ordinal);
        templateByName = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
        CollectTemplate(model.transform, string.Empty);

        texture = new RenderTexture(512, 768, 24, RenderTextureFormat.ARGB32)
        {
            name = "UI Character RT 512x768",
            antiAliasing = 4,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.Create();
        previewCamera = new GameObject("Portrait Camera").AddComponent<Camera>();
        previewCamera.transform.SetParent(rig.transform, false);
        previewCamera.enabled = false;
        previewCamera.orthographic = true;
        previewCamera.aspect = Aspect;
        previewCamera.nearClipPlane = 0.1f;
        previewCamera.farClipPlane = 12f;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.targetTexture = texture;
        previewCamera.allowHDR = false;

        Vector3 center = model.transform.position + Vector3.up;
        Light("Key", new Vector3(-2f, 3f, -3f), new Color(1f, 0.87f, 0.70f), 3.2f, center);
        Light("Fill", new Vector3(2f, 1f, -2f), new Color(0.60f, 0.75f, 1f), 2.2f, center);
        Light("Rim", new Vector3(1f, 2f, 2f), new Color(1f, 0.64f, 0.36f), 3f, center);
    }

    private static void Light(string name, Vector3 offset, Color color, float intensity, Vector3 center)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(rig.transform, false);
        light.transform.position = center + offset;
        light.type = LightType.Point;
        light.range = 10f;
        light.intensity = intensity;
        light.color = color;
        light.cullingMask = 1 << PreviewLayer;
        light.shadows = LightShadows.None;
    }

    private static void DisposeRig()
    {
        if (previewCamera)
            previewCamera.targetTexture = null;
        if (texture)
        {
            texture.Release();
            DestroyOwned(texture);
        }
        if (rig)
            DestroyOwned(rig);
        texture = null;
        rig = null;
        model = null;
        previewCamera = null;
        currentSource = null;
        templateRenderers = null;
        templateByPath = null;
        templateByName = null;
        weaponVisual = null;
        shieldVisual = null;
    }

    private static void DestroyOwned(UnityEngine.Object value)
    {
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }
}
