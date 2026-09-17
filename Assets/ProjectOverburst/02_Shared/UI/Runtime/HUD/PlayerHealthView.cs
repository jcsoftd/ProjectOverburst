using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

public sealed class PlayerHealthView : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private Image fill;
    [SerializeField] private TextMeshProUGUI label;
    [FormerlySerializedAs("leaderColor"), SerializeField] private Color playerColor = new Color(0.28f, 0.74f, 1f, 0.95f);

    private string displayName;

    public void Initialize(string displayName)
    {
        EnsureView();
        this.displayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
    }

    public void Refresh(CombatHealth health)
    {
        EnsureView();

        float normalized = health != null ? health.NormalizedHp : 0f;
        if (fill != null)
        {
            fill.fillAmount = normalized;
            fill.color = playerColor;
        }

        if (background != null)
            background.color = new Color(0.055f, 0.075f, 0.095f, 0.9f);

        if (label != null)
        {
            if (health == null)
                label.text = GetNameText() + " EMPTY";
            else
                label.text = GetNameText() + " " + Mathf.RoundToInt(Mathf.Max(0f, health.CurrentHp)) + " / " + Mathf.RoundToInt(Mathf.Max(1f, health.MaxHp));
        }
    }

    private string GetNameText()
    {
        return string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
    }

    private void EnsureView()
    {
        if (background == null)
            background = transform.Find("Background")?.GetComponent<Image>();

        if (fill == null)
            fill = transform.Find("Fill")?.GetComponent<Image>();

        if (label == null)
            label = transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
    }
}
