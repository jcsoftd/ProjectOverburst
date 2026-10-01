using System.Collections.Generic;
using UnityEngine;

/// <summary>픽업 선택은 기존 판정을 사용하고 외곽선은 Highlight Plus에 위임한다.</summary>
public sealed class WorldItemPickupHoverHighlight
{
    private readonly OverburstWorldHighlight visual = new OverburstWorldHighlight(OverburstWorldHighlightStyle.ItemHover);
    private WorldItemPickup currentTarget;
    public WorldItemPickup CurrentTarget => currentTarget;
    public int HighlightedRendererCount => visual.RendererCount;

    // 기존 프리팹의 재질 참조를 보존한다. 실제 표현은 Highlight Plus 프로필을 사용한다.
    public void SetOutlineMaterial(Material material) { }

    public void SetTarget(WorldItemPickup target, IReadOnlyList<Renderer> sourceRenderers)
    {
        if (target == currentTarget && target != null) return;
        Clear();
        if (target == null || sourceRenderers == null) return;
        var renderers = new Renderer[sourceRenderers.Count];
        for (int i = 0; i < renderers.Length; i++) renderers[i] = sourceRenderers[i];
        visual.SetTarget(target.transform, renderers);
        currentTarget = target;
    }
    public void RefreshSourceState()
    {
        if (currentTarget == null || !currentTarget.CanPickup) Clear();
    }
    public void Clear()
    {
        visual.Clear();
        currentTarget = null;
    }
    public void Dispose()
    {
        currentTarget = null;
        visual.Dispose();
    }
}
