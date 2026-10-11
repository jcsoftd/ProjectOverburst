using UnityEngine;
using UnityEngine.UI;

public sealed class QuestUiWalletLayout : MonoBehaviour
{
    public Text value, caption;
    public RectTransform coin;
    public float rightEdge;
    private string previous;
    private void LateUpdate() { if(value && value.text!=previous) Refresh(); }
    public void Refresh()
    {
        previous=value.text; value.horizontalOverflow=HorizontalWrapMode.Overflow;
        float width=Mathf.Ceil(value.preferredWidth)+2;
        value.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
        value.rectTransform.anchoredPosition=new Vector2(rightEdge-width,value.rectTransform.anchoredPosition.y);
        coin.anchoredPosition=new Vector2(rightEdge-width-29,coin.anchoredPosition.y);
        if(caption)caption.rectTransform.anchoredPosition=new Vector2(coin.anchoredPosition.x-12-caption.rectTransform.rect.width,caption.rectTransform.anchoredPosition.y);
    }
}
