using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum HideoutPortalKind { Dungeon, Boss }

public static class HideoutPortalLayout
{
    // 캠프 울타리 왼쪽에서 3.5m를 비우고 두 상호작용 범위가 겹치지 않게 놓는다.
    public static bool TryGetPosition(Scene scene, HideoutPortalKind kind, out Vector3 position)
    {
        position = Vector3.zero;
        if (!scene.IsValid() || !scene.isLoaded) return false;
        Transform layout = null, spawn = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Camp Layout") layout = child;
                var point = child.GetComponent<HubReturnPoint>();
                if (point != null && point.ReturnPointId == "Default") spawn = child;
            }
        if (layout == null || spawn == null) return false;
        float firstZ = spawn.position.z + 2f;
        Bounds bounds = new Bounds(); bool found = false;
        // 캠프 밖 장식 나무와 뒤쪽 소품 대신 출입할 줄의 울타리 경계를 사용한다.
        foreach (var collider in layout.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || !collider.name.StartsWith("Fence_", System.StringComparison.Ordinal)) continue;
            var current = collider.bounds;
            if (current.max.z < firstZ - 2f || current.min.z > firstZ + 8.5f) continue;
            if (!found) { bounds = current; found = true; } else bounds.Encapsulate(current);
        }
        if (!found) return false;
        position = new Vector3(bounds.min.x - 3.5f, spawn.position.y, firstZ + (kind == HideoutPortalKind.Dungeon ? 0f : 6.5f));
        int groundMask = LayerMask.GetMask("Ground");
        if (Physics.Raycast(position + Vector3.up * 12f, Vector3.down, out var hit, 30f, groundMask, QueryTriggerInteraction.Ignore)) position.y = hit.point.y + .04f;
        return true;
    }
}

// 입장 계약은 기존 포탈이 소유하고, 표시는 보유한 Piloto Studio 포탈의 프로젝트 래퍼를 사용한다.
[DisallowMultipleComponent]
public sealed class HideoutPortalVisual : MonoBehaviour
{
    public HideoutPortalKind Kind { get; private set; }
    public bool HasAssetPresentation { get; private set; }
    public string AssetResourcePath { get; private set; }
    private Transform label;
    private bool configured;

    public void Configure(HideoutPortalKind kind)
    {
        if (configured) return;
        configured = true; Kind = kind;
        AssetResourcePath = kind == HideoutPortalKind.Dungeon
            ? "VFX/Portals/PF_HideoutDungeonEntrance"
            : "VFX/Portals/PF_HideoutBossEntrance";
        var prefab = Resources.Load<GameObject>(AssetResourcePath);
        if (prefab == null)
        {
            Debug.LogError("하이드아웃 포탈 표시 프리팹이 없습니다: " + AssetResourcePath, this);
            return;
        }
        Instantiate(prefab, transform, false); HasAssetPresentation = true;
        var title = new GameObject("Portal Name");
        title.transform.SetParent(transform, false); title.transform.localPosition = Vector3.up * 4.7f;
        var text = title.AddComponent<TextMeshPro>();
        text.text = kind == HideoutPortalKind.Dungeon ? "던전 입장" : "크러스피칸 · 보스방";
        var font = Resources.Load<TMP_FontAsset>("UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body");
        if (font != null) text.font = font;
        text.fontSize = 3.3f; text.alignment = TextAlignmentOptions.Center;
        text.color = kind == HideoutPortalKind.Dungeon ? new Color(.65f,.87f,1f) : new Color(1f,.71f,.65f);
        text.rectTransform.sizeDelta = new Vector2(7f,1f); text.textWrappingMode = TextWrappingModes.NoWrap;
        label = title.transform;
    }

    private void Update()
    {
        if (label != null && Camera.main != null) label.rotation = Camera.main.transform.rotation;
    }
}
