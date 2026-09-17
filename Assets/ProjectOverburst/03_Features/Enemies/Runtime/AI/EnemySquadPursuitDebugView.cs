using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class EnemySquadPursuitDebugView : MonoBehaviour // 실제 게임 월드의 슬롯점과 반경 표시
{
    private const int CircleSegments = 72;
    private const int PointSegments = 20;
    private const float GroundOffset = 0.08f;
    private const float RangeLineWidth = 0.045f;
    private const float SlotPointRadius = 0.24f;

    private readonly List<EnemySquadPursuitDebugEncounter> encounters =
        new List<EnemySquadPursuitDebugEncounter>(4);
    private readonly List<EnemySquadPursuitDebugSlot> slots =
        new List<EnemySquadPursuitDebugSlot>(32);
    private readonly List<LineRenderer> renderers = new List<LineRenderer>(48);

    private Material lineMaterial;

    private void LateUpdate()
    {
        if (!IsDebugAllowed() || !CombatDebugSettings.ShowEnemySquadGeometryDebug)
        {
            HideAll();
            return;
        }

        EnsureMaterial();
        EnemySquadPursuitRuntimeService.CollectDebugDrawData(encounters, slots);
        int rendererIndex = 0;
        for (int encounterIndex = 0; encounterIndex < encounters.Count; encounterIndex++)
        {
            EnemySquadPursuitDebugEncounter encounter = encounters[encounterIndex];
            Vector3 center = encounter.Anchor;
            center.y = ResolveGroundY(encounter.Anchor) + GroundOffset;
            DrawCircle(ref rendererIndex, center, encounter.NearRadius,
                EnemySquadPursuitDebugPalette.NearRangeColor, RangeLineWidth);
            DrawCircle(ref rendererIndex, center, encounter.SlotRadius,
                EnemySquadPursuitDebugPalette.SlotRangeColor, RangeLineWidth * 0.8f);
            DrawCircle(ref rendererIndex, center, encounter.CommitRadius,
                EnemySquadPursuitDebugPalette.CommitRangeColor, RangeLineWidth);
            DrawCircle(ref rendererIndex, center, encounter.FarRadius,
                EnemySquadPursuitDebugPalette.FarRangeColor, RangeLineWidth);

            int slotEnd = Mathf.Min(slots.Count, encounter.SlotStartIndex + encounter.SlotCount);
            for (int slotIndex = encounter.SlotStartIndex; slotIndex < slotEnd; slotIndex++)
            {
                EnemySquadPursuitDebugSlot slot = slots[slotIndex];
                Color color = EnemySquadPursuitDebugPalette.ResolveSlotRoleColor(slot.Kind);
                if (!slot.Occupied)
                    color.a = 0.58f;
                Vector3 slotPosition = slot.Position;
                slotPosition.y = center.y;
                DrawCircle(
                    ref rendererIndex,
                    slotPosition,
                    slot.Occupied ? SlotPointRadius * 1.18f : SlotPointRadius,
                    color,
                    slot.Occupied ? 0.11f : 0.07f,
                    PointSegments);
            }
        }

        HideFrom(rendererIndex);
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

    private void DrawCircle(
        ref int rendererIndex,
        Vector3 center,
        float radius,
        Color color,
        float width,
        int segments = CircleSegments)
    {
        LineRenderer line = GetRenderer(rendererIndex++);
        int count = Mathf.Max(8, segments);
        line.positionCount = count;
        line.loop = true;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.enabled = true;
        float resolvedRadius = Mathf.Max(0.01f, radius);
        for (int index = 0; index < count; index++)
        {
            float angle = index * Mathf.PI * 2f / count;
            line.SetPosition(index, center + new Vector3(
                Mathf.Cos(angle) * resolvedRadius,
                0f,
                Mathf.Sin(angle) * resolvedRadius));
        }
    }

    private LineRenderer GetRenderer(int index)
    {
        while (renderers.Count <= index)
        {
            GameObject lineObject = new GameObject("EnemySquadDebugLine_" + renderers.Count.ToString("00"));
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 4900;
            line.sharedMaterial = lineMaterial;
            renderers.Add(line);
        }

        LineRenderer renderer = renderers[index];
        if (renderer.sharedMaterial != lineMaterial)
            renderer.sharedMaterial = lineMaterial;
        return renderer;
    }

    private void EnsureMaterial()
    {
        if (lineMaterial != null)
            return;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        lineMaterial = new Material(shader)
        {
            name = "EnemySquadPursuitDebug_Material",
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private static float ResolveGroundY(Vector3 anchor)
    {
        int mask = ~0;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (enemyLayer >= 0)
            mask &= ~(1 << enemyLayer);
        if (playerLayer >= 0)
            mask &= ~(1 << playerLayer);

        Vector3 origin = anchor + Vector3.up * 6f;
        return Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit hit,
            18f,
            mask,
            QueryTriggerInteraction.Ignore)
            ? hit.point.y
            : anchor.y;
    }

    private void HideAll()
    {
        HideFrom(0);
    }

    private void HideFrom(int startIndex)
    {
        for (int index = Mathf.Max(0, startIndex); index < renderers.Count; index++)
            renderers[index].enabled = false;
    }

    private static bool IsDebugAllowed()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }
}
