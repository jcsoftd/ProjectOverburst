using UnityEngine;
using UnityEngine.UI;

/// <summary>Workshop-only sample binding to the unchanged production grade effects.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class OverburstUISlotGradePreview : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private ItemGrade grade;
    [SerializeField] private GameObject editorGradeFrame;
    private SlotGradeEffect liveEffect;
    private RectMask2D viewport;
    private readonly Vector3[] slotCorners=new Vector3[4];
    private readonly Vector3[] maskCorners=new Vector3[4];
    public ItemGrade Grade => grade;
    public SlotGradeEffect LiveEffect => liveEffect;
    public bool Occupied => icon && icon.gameObject.activeSelf && icon.sprite;
    public void Configure(Image image, ItemGrade value, GameObject previewFrame){icon=image;grade=value;editorGradeFrame=previewFrame;Refresh();}
    public void Present(ItemGrade value){grade=value;Refresh();}
    private void OnEnable(){viewport=GetComponentInParent<RectMask2D>();Refresh();}
    private void LateUpdate(){
        if(!Application.isPlaying||!liveEffect||!Occupied)return;
        bool visible=true;
        if(viewport){((RectTransform)transform).GetWorldCorners(slotCorners);viewport.rectTransform.GetWorldCorners(maskCorners);
            visible=slotCorners[0].x>=maskCorners[0].x-.01f&&slotCorners[2].x<=maskCorners[2].x+.01f&&slotCorners[0].y>=maskCorners[0].y-.01f&&slotCorners[2].y<=maskCorners[2].y+.01f;}
        if(liveEffect.gameObject.activeSelf!=visible)liveEffect.gameObject.SetActive(visible);
    }
    private void OnDisable(){if(liveEffect){liveEffect.Clear();liveEffect.gameObject.SetActive(false);}}
    public void Refresh()
    {
        bool live=Application.isPlaying && gameObject.layer!=31;
        if(editorGradeFrame){editorGradeFrame.SetActive(!live && Occupied && grade!=ItemGrade.Common);foreach(var image in editorGradeFrame.GetComponentsInChildren<Image>(true)){var c=GradeConfig.GetGradeColor(grade);c.a=.7f;image.color=c;}}
        if(!live)return;
        if(!Occupied){if(liveEffect){liveEffect.Clear();liveEffect.gameObject.SetActive(false);}return;}
        if(!liveEffect){
            var root=new GameObject("Production Grade Effects",typeof(RectTransform));root.layer=gameObject.layer;root.transform.SetParent(transform,false);root.transform.SetSiblingIndex(1);
            var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=Vector2.one*6;rect.offsetMax=Vector2.one*-6;
            liveEffect=root.AddComponent<SlotGradeEffect>();liveEffect.Init(icon,null);
        }
        liveEffect.gameObject.SetActive(true);liveEffect.SetGrade(grade,GradeConfig.GetGradeColor(grade));
    }
}
