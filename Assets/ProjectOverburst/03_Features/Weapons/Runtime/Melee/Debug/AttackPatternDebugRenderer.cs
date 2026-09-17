using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[AddComponentMenu("Combat/Melee/Attack Pattern Debug Renderer")]
public sealed class AttackPatternDebugRenderer : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private int radialSegments = 32;
    [SerializeField] private float lineWidth = 0.035f;
    [SerializeField] private float groundOffset = 0.08f;
    [SerializeField] private Color outlineColor = new Color(1f, 0.45f, 0.08f, 0.9f);
    [SerializeField] private Color fillColor = new Color(1f, 0.3f, 0.05f, 0.24f);
    [SerializeField] private Color traceColor = new Color(1f, 0.9f, 0.1f, 0.95f);
    [SerializeField, Min(2)] private int traceMaxPoints = 24;
    [SerializeField, Min(0.001f)] private float tracePointMinDistance = 0.015f;

    private readonly List<Vector3> meshVertices = new List<Vector3>(40);
    private readonly List<int> meshTriangles = new List<int>(120);
    private GameObject outlineObject;
    private LineRenderer outlineRenderer;
    private Material outlineMaterial;
    private GameObject fillObject;
    private MeshFilter fillFilter;
    private MeshRenderer fillRenderer;
    private Material fillMaterial;
    private Mesh fillMesh;
    private GameObject traceObject;
    private LineRenderer traceRenderer;
    private Material traceMaterial;
    private readonly List<Vector3> tracePoints = new List<Vector3>(24);
    private bool traceActive;
    private float previousTraceProgress;

    public bool IsVisible => (outlineRenderer != null && outlineRenderer.enabled)
        || (fillRenderer != null && fillRenderer.enabled)
        || (traceRenderer != null && traceRenderer.enabled);

    private void OnEnable()
    {
        CombatDebugSettings.AttackPatternDebugChanged += HandleDebugVisibilityChanged;
    }

    private void OnDisable()
    {
        CombatDebugSettings.AttackPatternDebugChanged -= HandleDebugVisibilityChanged;
        Hide();
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(outlineMaterial);
        DestroyRuntimeObject(fillMaterial);
        DestroyRuntimeObject(fillMesh);
        DestroyRuntimeObject(traceMaterial);
    }

    public void Render(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        float progress,
        AttackProgressSample progressSample = default)
    {
        if (!CombatDebugSettings.ShowAttackPatternDebug)
        {
            Hide();
            return;
        }

        EnsureResources();
        if (outlineRenderer == null || fillRenderer == null || fillMesh == null)
            return;

        float safeProgress = Mathf.Clamp01(progress);
        BuildOutline(pattern, basis);
        BuildFill(pattern, basis, safeProgress);
        BuildTrace(progressSample);
        outlineRenderer.enabled = true;
        fillRenderer.enabled = safeProgress > 0.0001f;
    }

    public void Hide()
    {
        if (outlineRenderer != null)
            outlineRenderer.enabled = false;

        if (fillRenderer != null)
            fillRenderer.enabled = false;

        if (traceRenderer != null)
            traceRenderer.enabled = false;

        ResetTraceHistory();
    }

    private void EnsureResources()
    {
        if (outlineRenderer == null)
            CreateOutlineRenderer();

        if (fillRenderer == null)
            CreateFillRenderer();

        if (traceRenderer == null)
            CreateTraceRenderer();
    }

    private void CreateOutlineRenderer()
    {
        outlineObject = new GameObject("AttackPatternDebug_Outline");
        outlineObject.transform.SetParent(transform, false);
        outlineRenderer = outlineObject.AddComponent<LineRenderer>();
        outlineRenderer.useWorldSpace = true;
        outlineRenderer.textureMode = LineTextureMode.Stretch;
        outlineRenderer.alignment = LineAlignment.View;
        outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        outlineRenderer.receiveShadows = false;
        outlineRenderer.startWidth = lineWidth;
        outlineRenderer.endWidth = lineWidth;
        outlineRenderer.startColor = outlineColor;
        outlineRenderer.endColor = outlineColor;
        outlineRenderer.sortingOrder = 21;
        outlineRenderer.enabled = false;

        outlineMaterial = CreateRuntimeMaterial("Runtime_AttackPatternDebug_Outline", outlineColor);
        if (outlineMaterial != null)
            outlineRenderer.sharedMaterial = outlineMaterial;
    }

    private void CreateFillRenderer()
    {
        fillObject = new GameObject("AttackPatternDebug_Fill");
        fillObject.transform.SetParent(transform, false);
        fillFilter = fillObject.AddComponent<MeshFilter>();
        fillRenderer = fillObject.AddComponent<MeshRenderer>();
        fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;
        fillRenderer.sortingOrder = 20;
        fillRenderer.enabled = false;

        fillMesh = new Mesh
        {
            name = "Runtime_AttackPatternDebug_FillMesh",
            hideFlags = HideFlags.HideAndDontSave
        };
        fillMesh.MarkDynamic();
        fillFilter.sharedMesh = fillMesh;

        fillMaterial = CreateRuntimeMaterial("Runtime_AttackPatternDebug_Fill", fillColor);
        if (fillMaterial != null)
            fillRenderer.sharedMaterial = fillMaterial;
    }

    private void CreateTraceRenderer()
    {
        traceObject = new GameObject("AttackPatternDebug_WeaponTip");
        traceObject.transform.SetParent(transform, false);
        traceRenderer = traceObject.AddComponent<LineRenderer>();
        traceRenderer.useWorldSpace = true;
        traceRenderer.textureMode = LineTextureMode.Stretch;
        traceRenderer.alignment = LineAlignment.View;
        traceRenderer.shadowCastingMode = ShadowCastingMode.Off;
        traceRenderer.receiveShadows = false;
        traceRenderer.positionCount = 0;
        traceRenderer.startWidth = lineWidth * 0.35f;
        traceRenderer.endWidth = lineWidth * 2f;
        traceRenderer.numCapVertices = 4;
        traceRenderer.startColor = traceColor;
        traceRenderer.endColor = traceColor;
        traceRenderer.sortingOrder = 22;
        traceRenderer.enabled = false;

        traceMaterial = CreateRuntimeMaterial("Runtime_AttackPatternDebug_WeaponTip", traceColor);
        if (traceMaterial != null)
            traceRenderer.sharedMaterial = traceMaterial;
    }

    private void BuildTrace(AttackProgressSample progressSample)
    {
        if (traceRenderer == null || !progressSample.HasTrace)
        {
            ResetTraceHistory();
            return;
        }

        float progress = Mathf.Clamp01(progressSample.ResolvedProgress);
        if (!traceActive || progress + 0.0001f < previousTraceProgress)
        {
            tracePoints.Clear();
            traceActive = true;
        }

        previousTraceProgress = progress;
        AppendTracePoint(progressSample.TracePoint);
        traceRenderer.positionCount = tracePoints.Count;
        for (int i = 0; i < tracePoints.Count; i++)
            traceRenderer.SetPosition(i, tracePoints[i]);

        traceRenderer.startColor = traceColor;
        traceRenderer.endColor = traceColor;
        traceRenderer.enabled = tracePoints.Count >= 2;
    }

    private void AppendTracePoint(Vector3 point)
    {
        int count = tracePoints.Count;
        float minimumDistance = Mathf.Max(0.001f, tracePointMinDistance);
        if (count == 0)
        {
            tracePoints.Add(point);
            return;
        }

        if ((tracePoints[count - 1] - point).sqrMagnitude < minimumDistance * minimumDistance)
        {
            tracePoints[count - 1] = point;
            return;
        }

        tracePoints.Add(point);
        int maxPoints = Mathf.Max(2, traceMaxPoints);
        while (tracePoints.Count > maxPoints)
            tracePoints.RemoveAt(0);
    }

    private void ResetTraceHistory()
    {
        tracePoints.Clear();
        traceActive = false;
        previousTraceProgress = 0f;
        if (traceRenderer == null)
            return;

        traceRenderer.positionCount = 0;
        traceRenderer.enabled = false;
    }

    private void BuildOutline(AttackPatternRuntimeData pattern, AttackPatternBasis basis)
    {
        Vector3 origin = basis.GetPatternOrigin(pattern);
        origin.y = transform.position.y + groundOffset;
        outlineRenderer.startWidth = lineWidth;
        outlineRenderer.endWidth = lineWidth;
        outlineRenderer.startColor = outlineColor;
        outlineRenderer.endColor = outlineColor;

        if (pattern.Shape == AttackAreaShape.Rectangle)
        {
            BuildRectangleOutline(pattern, basis, origin);
            return;
        }

        if (pattern.Shape == AttackAreaShape.Circle)
        {
            BuildCircleOutline(pattern, basis, origin);
            return;
        }

        BuildSectorOutline(pattern, basis, origin);
    }

    private void BuildRectangleOutline(AttackPatternRuntimeData pattern, AttackPatternBasis basis, Vector3 origin)
    {
        float halfWidth = pattern.Width * 0.5f;
        Vector3 startLeft = origin - basis.Right * halfWidth;
        Vector3 startRight = origin + basis.Right * halfWidth;
        Vector3 endLeft = startLeft + basis.Forward * pattern.Range;
        Vector3 endRight = startRight + basis.Forward * pattern.Range;

        outlineRenderer.loop = false;
        outlineRenderer.positionCount = 5;
        outlineRenderer.SetPosition(0, startLeft);
        outlineRenderer.SetPosition(1, endLeft);
        outlineRenderer.SetPosition(2, endRight);
        outlineRenderer.SetPosition(3, startRight);
        outlineRenderer.SetPosition(4, startLeft);
    }

    private void BuildCircleOutline(AttackPatternRuntimeData pattern, AttackPatternBasis basis, Vector3 origin)
    {
        int segments = Mathf.Max(12, radialSegments);
        outlineRenderer.loop = true;
        outlineRenderer.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = pattern.AngleOffset + 360f * i / segments;
            outlineRenderer.SetPosition(i, origin + DirectionAtAngle(basis, angle) * pattern.Range);
        }
    }

    private void BuildSectorOutline(AttackPatternRuntimeData pattern, AttackPatternBasis basis, Vector3 origin)
    {
        int segments = Mathf.Max(6, Mathf.CeilToInt(radialSegments * pattern.Angle / 360f));
        float startAngle = pattern.AngleOffset - pattern.Angle * 0.5f;
        outlineRenderer.loop = false;
        outlineRenderer.positionCount = segments + 3;
        outlineRenderer.SetPosition(0, origin);
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(startAngle, startAngle + pattern.Angle, (float)i / segments);
            outlineRenderer.SetPosition(i + 1, origin + DirectionAtAngle(basis, angle) * pattern.Range);
        }

        outlineRenderer.SetPosition(segments + 2, origin);
    }

    private void BuildFill(AttackPatternRuntimeData pattern, AttackPatternBasis basis, float progress)
    {
        Vector3 patternOrigin = basis.GetPatternOrigin(pattern);
        fillObject.transform.SetPositionAndRotation(
            new Vector3(patternOrigin.x, transform.position.y + groundOffset, patternOrigin.z),
            Quaternion.LookRotation(basis.Forward, Vector3.up));

        meshVertices.Clear();
        meshTriangles.Clear();

        if (pattern.Shape == AttackAreaShape.Rectangle)
            BuildRectangleFill(pattern, progress);
        else if (pattern.FillMode == AttackFillMode.RadialExpand)
            BuildRadialFill(pattern, progress);
        else
            BuildAngularFill(pattern, progress);

        fillMesh.Clear();
        fillMesh.SetVertices(meshVertices);
        fillMesh.SetTriangles(meshTriangles, 0);
        fillMesh.RecalculateBounds();
    }

    private void BuildRectangleFill(AttackPatternRuntimeData pattern, float progress)
    {
        float halfWidth = pattern.Width * 0.5f;
        float length = pattern.Range * progress;
        meshVertices.Add(new Vector3(-halfWidth, 0f, 0f));
        meshVertices.Add(new Vector3(-halfWidth, 0f, length));
        meshVertices.Add(new Vector3(halfWidth, 0f, length));
        meshVertices.Add(new Vector3(halfWidth, 0f, 0f));
        AddQuadTriangles();
    }

    private void BuildRadialFill(AttackPatternRuntimeData pattern, float progress)
    {
        float radius = pattern.Range * progress;
        float totalAngle = pattern.Shape == AttackAreaShape.Circle ? 360f : pattern.Angle;
        float startAngle = pattern.Shape == AttackAreaShape.Circle
            ? pattern.AngleOffset
            : pattern.AngleOffset - pattern.Angle * 0.5f;
        BuildFan(startAngle, totalAngle, radius);
    }

    private void BuildAngularFill(AttackPatternRuntimeData pattern, float progress)
    {
        float totalAngle = pattern.Shape == AttackAreaShape.Circle ? 360f : pattern.Angle;
        float startAngle;
        float sweepAngle;

        if (pattern.Shape == AttackAreaShape.Circle)
        {
            startAngle = pattern.AngleOffset;
            sweepAngle = totalAngle * progress;
        }
        else if (pattern.Direction == AttackFillDirection.RightToLeft)
        {
            startAngle = pattern.AngleOffset + pattern.Angle * 0.5f;
            sweepAngle = -totalAngle * progress;
        }
        else
        {
            startAngle = pattern.AngleOffset - pattern.Angle * 0.5f;
            sweepAngle = totalAngle * progress;
        }

        if (pattern.Shape == AttackAreaShape.Circle && pattern.Direction == AttackFillDirection.RightToLeft)
            sweepAngle = -sweepAngle;

        BuildFan(startAngle, sweepAngle, pattern.Range);
    }

    private void BuildFan(float startAngle, float sweepAngle, float radius)
    {
        int segments = Mathf.Max(2, Mathf.CeilToInt(radialSegments * Mathf.Abs(sweepAngle) / 360f));
        meshVertices.Add(Vector3.zero);
        for (int i = 0; i <= segments; i++)
        {
            float angle = startAngle + sweepAngle * i / segments;
            float radians = angle * Mathf.Deg2Rad;
            meshVertices.Add(new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius));
        }

        for (int i = 0; i < segments; i++)
        {
            meshTriangles.Add(0);
            meshTriangles.Add(i + 1);
            meshTriangles.Add(i + 2);
        }
    }

    private void AddQuadTriangles()
    {
        meshTriangles.Add(0);
        meshTriangles.Add(1);
        meshTriangles.Add(2);
        meshTriangles.Add(0);
        meshTriangles.Add(2);
        meshTriangles.Add(3);
    }

    private static Vector3 DirectionAtAngle(AttackPatternBasis basis, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return basis.Right * Mathf.Sin(radians) + basis.Forward * Mathf.Cos(radians);
    }

    private static Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            return null;

        Material material = new Material(shader)
        {
            name = materialName,
            color = color,
            hideFlags = HideFlags.HideAndDontSave
        };
        return material;
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(target);
        else
            Object.DestroyImmediate(target);
    }

    private void HandleDebugVisibilityChanged(bool visible)
    {
        if (!visible)
            Hide();
    }
}
