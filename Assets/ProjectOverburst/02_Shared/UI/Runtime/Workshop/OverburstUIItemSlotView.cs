using UnityEngine;
using UnityEngine.UI;

/// <summary>One visual slot contract; gameplay binding is intentionally separate.</summary>
public sealed class OverburstUIItemSlotView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Image placeholder;
    [SerializeField] private Text keyLabel;
    [SerializeField] private OverburstUISlotGradePreview grade;
    [SerializeField] private WeaponElementIconView weaponElementIcon;
    public void ConfigureElementIcon(WeaponElementIconView view){weaponElementIcon=view;}
    public void Present(ItemData item){Present(item?.icon,item!=null?item.grade:ItemGrade.Common);PresentElement(item);}
    public void PresentElement(ItemData item){weaponElementIcon?.Present(item);}
    public void Configure(Image item,Image empty,Text key,OverburstUISlotGradePreview effect){icon=item;placeholder=empty;keyLabel=key;grade=effect;}
    public void Present(Sprite sprite,ItemGrade tier,string key=""){
        icon.sprite=sprite;icon.GetComponent<OverburstUIIconFraming>()?.Refresh();icon.gameObject.SetActive(sprite);placeholder.gameObject.SetActive(!sprite&&placeholder.sprite);SetKeyLabel(key);grade.Present(tier);weaponElementIcon?.Present(WeaponElement.None);
    }
    // 2026-10-01 퀵슬롯 키 글자. "Shift"처럼 세 글자 이상이면 칸 안에 들어가게 크기 맞춤을 켠다(원래 크기보다 커지지 않음).
    private int keyFontSize;
    public void SetKeyLabel(string key){
        if(!keyLabel)return;key??="";if(keyFontSize<=0)keyFontSize=keyLabel.fontSize;
        keyLabel.text=key;keyLabel.resizeTextForBestFit=key.Length>2;keyLabel.resizeTextMaxSize=keyFontSize;keyLabel.resizeTextMinSize=Mathf.Min(9,keyFontSize);
        keyLabel.gameObject.SetActive(!string.IsNullOrEmpty(key));
    }
}
