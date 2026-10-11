using UnityEngine;
#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine.UI;
#endif

// Generated children are transient. Reopening the scene regenerates the production layout.
[ExecuteAlways]
public sealed class OverburstUIRunGalleryPreview : MonoBehaviour
{
    public enum Sample { CardsFront, CardsBack, Transfer }
    public GameObject productionSource;
    public Sample sample;
    GameObject generated;
    public GameObject Generated => generated;
    void OnEnable() { if (!Application.isPlaying) RefreshSample(); }
    void Start() { if (Application.isPlaying) gameObject.SetActive(false); }
    void OnDisable() => Release();
    void OnDestroy() => Release();
    void Release()
    {
        var owned = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in transform)
            if (child.name == "Production layout • generated, not saved" && (child.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) owned.Add(child.gameObject);
        foreach (var child in owned) { if (Application.isPlaying) Destroy(child); else DestroyImmediate(child); }
        generated = null;
    }
    public void RefreshSample()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || !productionSource) return;
        Release();
        // Build the Overlay layout before parenting it, so Unity cannot resize the exhibit canvas.
        generated = (GameObject)PrefabUtility.InstantiatePrefab(productionSource, gameObject.scene);
        generated.name = "Production layout • generated, not saved";
        generated.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        var ui = generated.GetComponent<OverburstRunUi>();
        try { ui.SetEventHint("", ""); } // Calls the same lazy layout builder as the dungeon.
        catch { DestroyImmediate(generated); generated = null; throw; }
        ui.enabled = false;
        var canvas = generated.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = false;
        generated.transform.SetParent(transform, false);
        var root = (RectTransform)generated.transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.pivot = new Vector2(.5f, .5f); root.offsetMin = root.offsetMax = Vector2.zero; root.localScale = Vector3.one;
        generated.transform.Find("Modal").gameObject.SetActive(true);
        generated.transform.Find("Modal/CardChoices").gameObject.SetActive(sample != Sample.Transfer);
        generated.transform.Find("Modal/SmallTransfer").gameObject.SetActive(sample == Sample.Transfer);
        if (sample != Sample.Transfer)
        {
            var cards = generated.GetComponentsInChildren<OverburstRunCardView>(true);
            var data = OverburstUIRunPreview.CardSamples();
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].Bind(data[i], null);
                var flip = cards[i].transform.Find("Flip");
                bool back = sample == Sample.CardsBack;
                flip.GetComponent<MoreMountains.Tools.MMTwoSidedUI>().enabled = false;
                flip.localScale = new Vector3(back ? -1 : 1, 1, 1);
                flip.Find("Front").gameObject.SetActive(!back); flip.Find("Back").gameObject.SetActive(back);
                cards[i].SetSelectable(false);
            }
            Text("Modal/CardChoices/Status", "Play의 R 키로 공개·선택·취소를 확인합니다");
        }
        else
        {
            var gear = Resources.Load<GearItemData>("Items/Gear/Gear_Helmet");
            Text("Modal/SmallTransfer/SelectedItem", (gear ? gear.itemName : "전시용 투구") + " ×2");
            Text("Modal/SmallTransfer/StackNote", "장착 중인 아이템입니다. 전송 시 해제됩니다.\n스택 전체를 창고에 자동 보관합니다.");
            Text("Modal/SmallTransfer/Status", "Play의 T 키로 선택·전송 실패·취소를 확인합니다");
            Text("Modal/SmallTransfer/DropSlot/Placeholder", "");
            var icon = generated.transform.Find("Modal/SmallTransfer/DropSlot/SelectedIcon").GetComponent<Image>();
            icon.sprite = gear ? gear.icon : null; icon.enabled = icon.sprite;
            generated.transform.Find("Modal/SmallTransfer/ConfirmTransfer").GetComponent<Button>().interactable = false;
        }
        foreach (var raycaster in generated.GetComponentsInChildren<GraphicRaycaster>(true)) raycaster.enabled = false;
        foreach (var t in generated.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = 31; t.gameObject.tag = "EditorOnly";
            t.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }
#endif
    }
#if UNITY_EDITOR
    void Text(string path, string value) => generated.transform.Find(path).GetComponent<TMP_Text>().text = value;
#endif
}
