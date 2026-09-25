using UnityEngine;
using UnityEngine.UI;

/// <summary>One visual slot contract; gameplay binding is intentionally separate.</summary>
public sealed class OverburstUIItemSlotView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Image placeholder;
    [SerializeField] private Text keyLabel;
    [SerializeField] private OverburstUISlotGradePreview grade;
    public void Configure(Image item,Image empty,Text key,OverburstUISlotGradePreview effect){icon=item;placeholder=empty;keyLabel=key;grade=effect;}
    public void Present(Sprite sprite,ItemGrade tier,string key=""){
        icon.sprite=sprite;icon.GetComponent<OverburstUIIconFraming>()?.Refresh();icon.gameObject.SetActive(sprite);placeholder.gameObject.SetActive(!sprite&&placeholder.sprite);keyLabel.text=key;keyLabel.gameObject.SetActive(!string.IsNullOrEmpty(key));grade.Present(tier);
    }
}
