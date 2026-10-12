using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class QuestInventoryStyle : MonoBehaviour
{
    public RectTransform modeHost, directionHost, equipmentHost;
    public Text modeLabel, directionLabel, equipmentLabel, gold;
    public Font bodyFont;
    private Button equipment;
    private Text equipmentLegacy;
    private TMP_Text equipmentTmp;
    public void Bind(TMP_Dropdown sort, Button direction, Button equip, Text capacity)
    {
        Stretch((RectTransform)transform);
        Move(sort.transform,modeHost); Move(direction.transform,directionHost);Move(equip.transform,equipmentHost);
        if(sort.captionText)sort.captionText.gameObject.SetActive(false);
        var arrow=sort.transform.Find("Arrow");if(arrow)arrow.gameObject.SetActive(false);
        var caret=modeHost.parent.Find("Quest Caret") as RectTransform;
        if(!caret){caret=new GameObject("Quest Caret",typeof(RectTransform),typeof(CanvasRenderer),typeof(QuestUiLineIcon)).GetComponent<RectTransform>();caret.SetParent(modeHost.parent,false);}
        caret.anchorMin=caret.anchorMax=caret.pivot=new Vector2(1,.5f);caret.anchoredPosition=new Vector2(-14,0);caret.sizeDelta=new Vector2(8,5);
        var icon=caret.GetComponent<QuestUiLineIcon>();icon.kind=3;icon.color=new Color32(188,167,123,255);icon.raycastTarget=false;
        var template=sort.template;template.anchorMin=template.anchorMax=template.pivot=new Vector2(0,1);template.anchoredPosition=new Vector2(0,-42);template.localScale=Vector3.one;template.sizeDelta=new Vector2(154,sort.options.Count*36+10);
        var scroll=template.GetComponent<ScrollRect>();if(scroll){scroll.content.anchorMin=new Vector2(0,1);scroll.content.anchorMax=Vector2.one;scroll.content.pivot=new Vector2(.5f,1);scroll.content.sizeDelta=new Vector2(0,sort.options.Count*36);}
        var item=sort.itemText.GetComponentInParent<Toggle>(true);if(item){var itemRect=(RectTransform)item.transform;itemRect.sizeDelta=new Vector2(itemRect.sizeDelta.x,36);itemRect.localScale=Vector3.one;}
        sort.itemText.fontSize=14;sort.itemText.enableAutoSizing=false;
        var background=template.GetComponent<Image>();if(background){background.sprite=null;background.color=new Color32(32,25,19,255);background.material=null;}
        if(item){var fill=item.GetComponent<Image>();if(fill){fill.sprite=null;fill.color=new Color32(48,38,28,255);}var colors=item.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color32(220,187,121,255);colors.selectedColor=colors.highlightedColor;item.colors=colors;}
        var optionRect=sort.itemText.rectTransform;optionRect.anchorMin=Vector2.zero;optionRect.anchorMax=Vector2.one;optionRect.offsetMin=new Vector2(14,0);optionRect.offsetMax=new Vector2(-10,0);
        sort.itemText.color=new Color32(220,201,170,255);
        if(sort.targetGraphic is Image modeImage)modeImage.color=Color.clear;
        if(direction.targetGraphic is Image directionImage)directionImage.color=Color.clear;
        foreach(var text in direction.GetComponentsInChildren<TMP_Text>(true))text.gameObject.SetActive(false);
        foreach(var text in direction.GetComponentsInChildren<Text>(true))text.gameObject.SetActive(false);
        if(equip.targetGraphic is Image equipmentImage)equipmentImage.color=Color.clear;
        BindVisual(sort,modeHost);BindVisual(direction,directionHost);BindVisual(equip,equipmentHost);
        equipment=equip;equipmentLegacy=equip.GetComponentInChildren<Text>(true);equipmentTmp=equip.GetComponentInChildren<TMP_Text>(true);
        if(equipmentLegacy)equipmentLegacy.enabled=false;if(equipmentTmp)equipmentTmp.enabled=false;
        // Existing runtime controls keep their listeners, sort lock and dropdown dismissal.
        capacity.transform.SetParent(modeHost.parent.parent,false);var rect=capacity.rectTransform;
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(466,-96);rect.sizeDelta=new Vector2(110,22);rect.localScale=Vector3.one;
        capacity.font=bodyFont;capacity.fontSize=14;capacity.color=new Color32(192,184,166,255);capacity.alignment=TextAnchor.MiddleRight;
    }
    public void PresentSort(string mode,bool ascending,bool canSort)
    {
        modeLabel.text=mode;directionLabel.text=ascending ? "정렬 ▲" : "정렬 ▼";
        modeLabel.color=directionLabel.color=canSort ? new Color32(220,201,170,255) : new Color32(117,110,95,255);
    }
    private void LateUpdate()
    {
        string label=equipmentLegacy ? equipmentLegacy.text : equipmentTmp ? equipmentTmp.text : "장비창 열기";
        if(equipmentLabel.text!=label)equipmentLabel.text=label;
    }
    private static void BindVisual(Selectable control,RectTransform host)
    {
        var artwork=host.parent.Find("Artwork").GetComponent<Selectable>();
        control.colors=artwork.colors;
        control.transition=artwork.transition;
        control.targetGraphic=artwork.targetGraphic;
    }
    private static void Move(Transform source,RectTransform host)
    {source.SetParent(host,false);Stretch((RectTransform)source);}
    private static void Stretch(RectTransform rect)
    {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;}
}
