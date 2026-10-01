using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored world-space keycap displayed by the shared interaction prompt route.</summary>
[DisallowMultipleComponent]
public sealed class WorldInteractionKeyPrompt : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private Image keycap;
    [SerializeField] private TMP_Text keyLabel;
    [SerializeField] private RectTransform keyRoot;

    public bool IsVisible => gameObject.activeInHierarchy;
    public string KeyLabel => keyLabel != null ? keyLabel.text : string.Empty;
    public bool HasUsableView => canvas != null && keycap != null && keyLabel != null && keyRoot != null;
    public Vector2 ScreenPosition => keyRoot != null ? (Vector2)keyRoot.position : Vector2.zero;

    private void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        if (canvas != null) canvas.worldCamera = camera;
        if (keyRoot == null) return;
        Vector3 screen = camera.WorldToScreenPoint(transform.position);
        keyRoot.gameObject.SetActive(screen.z > 0f);
        keyRoot.position = new Vector3(screen.x, screen.y, 0f);
    }
}
