using UnityEngine;
using UnityEngine.UI;

/// <summary>Sample tab contents for the UI workshop; no PlayerStash or save-data writes.</summary>
public sealed class OverburstUIStashPreview : MonoBehaviour
{
    [SerializeField] private Image[] slotIcons;
    [SerializeField] private Sprite[] sampleIcons;
    [SerializeField] private Image[] tabBackgrounds;
    [SerializeField] private Text caption;
    public int SelectedTab { get; private set; }
    public void Configure(Image[] icons, Sprite[] samples, Image[] tabs, Text label)
    { slotIcons=icons; sampleIcons=samples; tabBackgrounds=tabs; caption=label; Select(0); }
    public void First() => Select(0);
    public void Second() => Select(1);
    public void Third() => Select(2);
    private void Select(int index)
    {
        SelectedTab=index;
        for(int i=0;i<slotIcons.Length;i++)
        {
            var icon=slotIcons[i];if(icon==null)continue;
            icon.gameObject.SetActive(i<12-index*4);
            if(sampleIcons.Length>0)icon.sprite=sampleIcons[(i+index)%sampleIcons.Length];
            icon.GetComponent<OverburstUIIconFraming>()?.Refresh();
            var grade=icon.transform.parent.GetComponent<OverburstUISlotGradePreview>();if(grade)grade.Present((ItemGrade)((i+index)%8));
        }
        for(int i=0;i<tabBackgrounds.Length;i++){tabBackgrounds[i].color=new Color(.3f,.2f,.12f,.9f);tabBackgrounds[i].gameObject.SetActive(i==index);}
        if(caption!=null)caption.text=(12-index*4)+" / 63";
    }
}
