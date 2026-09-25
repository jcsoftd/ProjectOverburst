using UnityEngine;
using UnityEngine.UI;

/// <summary>Passive RPG UI 11 nameplate view, ready for a separate pooled-world-UI adapter.</summary>
[DisallowMultipleComponent]
public sealed class OverburstEnemyHealthBarView : MonoBehaviour
{
    [SerializeField] private Image healthFill;
    [SerializeField] private Text percentageText;
    [SerializeField] private Text levelText;
    [SerializeField] private Text nameText;
    public Image HealthFill => healthFill;
    public void PresentTarget(string displayName,string rank,float health){PresentTarget(displayName,rank,1,health);}
    public void PresentTarget(string displayName,string rank,int level,float health)
    {
        Present(displayName,level,health);
        if(levelText)levelText.text=Mathf.Clamp(level,1,OverburstGrowthRules.MaximumLevel).ToString();
    }
    public void Configure(Image fill, Text percentage, Text level, Text name)
    { healthFill = fill; percentageText = percentage; levelText = level; nameText = name; }
    public void Present(string displayName, int level, float normalizedHealth)
    {
        float health = Mathf.Clamp01(normalizedHealth);
        if (healthFill != null) healthFill.fillAmount = health;
        if (percentageText != null) percentageText.text = Mathf.CeilToInt(health * 100f) + "%";
        if (levelText != null) levelText.text = level.ToString();
        if (nameText != null) { nameText.text = displayName; nameText.gameObject.SetActive(!string.IsNullOrEmpty(displayName)); }
    }
}
