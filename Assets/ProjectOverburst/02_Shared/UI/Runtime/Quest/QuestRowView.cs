using UnityEngine;
using UnityEngine.UI;

public sealed class QuestRowView : MonoBehaviour
{
    public Button button;
    public Text title, summary, reward, status;
    public GameObject selection, arrow;
    public bool arrowOnlyWhenSelected;
    public void Present(string heading, string subtitle, string rewards, string state, bool available, bool selected)
    {
        title.text = heading;
        if (summary) summary.text = subtitle;
        if (reward) reward.text = rewards;
        if (status) status.text = state;
        button.interactable = available;
        title.color = available ? selected ? new Color32(225,194,141,255) : new Color32(207,179,129,255) : new Color32(127,119,98,255);
        if (summary) summary.color = available ? new Color32(166,155,133,255) : new Color32(127,119,98,255);
        if (reward) reward.color = available ? new Color32(180,162,124,255) : new Color32(127,119,98,255);
        if (status) status.color = available ? new Color32(184,162,118,255) : new Color32(127,119,98,255);
        if (selection) selection.SetActive(selected);
        if (arrow) arrow.SetActive(available && (!arrowOnlyWhenSelected || selected));
        if (!arrowOnlyWhenSelected && arrow && arrow.GetComponent<Text>())
        {arrow.SetActive(true);arrow.GetComponent<Text>().text=available ? "›" : "—";}
    }
}
