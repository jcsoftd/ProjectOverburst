using UnityEngine;
#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine.UI;
#endif

// Static gallery samples consume the product style route; generated materials are never saved.
[ExecuteAlways]
public sealed class OverburstUIFloatingGalleryPreview : MonoBehaviour
{
    public GameObject productionSource;
    const string GeneratedName = "Floating styles • generated, not saved";
    void OnEnable() { if (!Application.isPlaying) RefreshSample(); }
    void Start() { if (Application.isPlaying) gameObject.SetActive(false); }
    void OnDisable() => Release();
    void OnDestroy() => Release();
    void Release()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (child.name != GeneratedName || (child.hideFlags & HideFlags.DontSaveInEditor) == 0) continue;
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
    }
    public void RefreshSample()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || !productionSource) return;
        Release();
        var generated = new GameObject(GeneratedName, typeof(RectTransform));
        generated.transform.SetParent(transform, false);
        var cameraObject = new GameObject("Owned style projection camera", typeof(Camera));
        cameraObject.transform.SetParent(generated.transform, false);
        var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
        var requests = new[] {
            new DamageNumberStyleRequest(DamageNumberKind.Normal, WeaponElement.None, false),
            new DamageNumberStyleRequest(DamageNumberKind.Critical, WeaponElement.None, true),
            new DamageNumberStyleRequest(DamageNumberKind.DamageOverTime, WeaponElement.Fire, false),
            new DamageNumberStyleRequest(DamageNumberKind.PlayerHit, WeaponElement.None, false),
            new DamageNumberStyleRequest(DamageNumberKind.Heal, WeaponElement.None, false),
            new DamageNumberStyleRequest(DamageNumberKind.Stun, WeaponElement.None, false),
            new DamageNumberStyleRequest(DamageNumberKind.IncompleteParry, WeaponElement.None, false),
            new DamageNumberStyleRequest(DamageNumberKind.Parry, WeaponElement.None, false),
            new DamageNumberStyleRequest(DamageNumberKind.PerfectParry, WeaponElement.None, false),
            new DamageNumberStyleRequest(DamageNumberKind.Discharge, WeaponElement.Fire, false),
            new DamageNumberStyleRequest(DamageNumberKind.Discharge, WeaponElement.Ice, false),
            new DamageNumberStyleRequest(DamageNumberKind.Discharge, WeaponElement.Electric, false, 3),
            new DamageNumberStyleRequest(DamageNumberKind.Discharge, WeaponElement.Dark, false),
            new DamageNumberStyleRequest(DamageNumberKind.Discharge, WeaponElement.Light, false),
            new DamageNumberStyleRequest(DamageNumberKind.Discharge, WeaponElement.Electric, true, 1) };
        var random = Random.state;
        try
        {
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(productionSource, generated.transform);
                var popup = go.GetComponent<DamageNumberPopup>();
                popup.enabled = false;
                popup.SetProjectionRoot((RectTransform)transform); popup.SetTargetCamera(camera);
                string label = DamageNumberStyles.IsParry(request.Kind) ? DamageNumberStyles.ParryLabel(request.Kind)
                    : request.Kind == DamageNumberKind.Stun ? "기절"
                    : request.Kind == DamageNumberKind.PlayerHit ? "-123"
                    : request.Kind == DamageNumberKind.Heal ? "+123" : null;
                popup.InitializeStyled(123, request, Vector3.zero, null, label);
                if (!go.activeSelf || string.IsNullOrEmpty(go.GetComponent<TMP_Text>().text))
                    throw new System.InvalidOperationException("Floating style sample failed: " + request.Kind);
                popup.SetOpacity(1); // Inspect the visible phase instead of the first frame of the parry fade-in.
                foreach (var fx in go.GetComponentsInChildren<DamageNumberFx>(true)) fx.enabled = false;
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2((i % 3 - 1) * 500, 320 - i / 3 * 155);
                rect.localScale = Vector3.one;
                go.GetComponent<TMP_Text>().ForceMeshUpdate();
                var heading = new GameObject("Sample kind", typeof(RectTransform), typeof(Text));
                heading.transform.SetParent(generated.transform, false);
                var labelRect = (RectTransform)heading.transform; labelRect.sizeDelta = new Vector2(420, 30);
                labelRect.anchoredPosition = rect.anchoredPosition + Vector2.up * 55;
                var caption = heading.GetComponent<Text>(); caption.font = Resources.Load<Font>("UI/Fonts/DamageFloating/Pretendard_Medium");
                caption.fontSize = 18; caption.alignment = TextAnchor.MiddleCenter; caption.raycastTarget = false;
                caption.color = new Color32(190, 173, 139, 255);
                caption.text = request.Kind == DamageNumberKind.Discharge ? DamageNumberStyles.DischargeName(request.Element) + (request.IsCritical ? " · 치명타" : "") : request.Kind.ToString();
            }
            foreach (var t in generated.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = 31; t.gameObject.tag = "EditorOnly";
                t.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            }
        }
        finally { Random.state = random; }
#endif
    }
}
