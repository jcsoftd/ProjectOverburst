using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Standalone visual prototype. Live enemy HP bars continue to use EnemyHpBarView.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyHpBarConceptPreview : MonoBehaviour
{
    [SerializeField] private CanvasGroup visibility;
    [SerializeField] private Image currentFill;
    [SerializeField] private Image damageTrail;
    [SerializeField] private bool persistent;
    [SerializeField, Min(0f)] private float visibleSeconds = 1.35f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.22f;
    [SerializeField, Min(0f)] private float trailHoldSeconds = 0.09f;
    [SerializeField, Min(0.01f)] private float trailCatchupSeconds = 0.42f;

    private float visibleUntil;
    private float trailStartsAt;
    private float trailStartFill = 1f;
    private float trailTargetFill = 1f;

    public bool Persistent => persistent;
    public Image CurrentFill => currentFill;
    public Image DamageTrail => damageTrail;
    public CanvasGroup Visibility => visibility;

    public void Configure(CanvasGroup group, Image fill, Image trail, bool alwaysVisible)
    {
        visibility = group;
        currentFill = fill;
        damageTrail = trail;
        persistent = alwaysVisible;
    }

    private void OnEnable()
    {
        if (currentFill != null)
            currentFill.fillAmount = 1f;
        if (damageTrail != null)
            damageTrail.fillAmount = 1f;
        trailStartFill = 1f;
        trailTargetFill = 1f;
        if (visibility != null)
            visibility.alpha = persistent ? 1f : 0f;
    }

    public void PreviewDamage(float normalizedHealth)
    {
        if (currentFill == null || damageTrail == null)
            return;

        float previous = currentFill.fillAmount;
        float next = Mathf.Clamp01(normalizedHealth);
        currentFill.fillAmount = next;
        trailStartFill = Mathf.Max(previous, damageTrail.fillAmount);
        trailTargetFill = next;
        damageTrail.fillAmount = trailStartFill;
        trailStartsAt = Time.unscaledTime + trailHoldSeconds;
        visibleUntil = Time.unscaledTime + visibleSeconds;
        if (visibility != null)
            visibility.alpha = 1f;
    }

    public void ResetHealth(float normalizedHealth)
    {
        float value = Mathf.Clamp01(normalizedHealth);
        if (currentFill != null)
            currentFill.fillAmount = value;
        if (damageTrail != null)
            damageTrail.fillAmount = value;
        trailStartFill = value;
        trailTargetFill = value;
        if (visibility != null)
            visibility.alpha = persistent ? 1f : 0f;
    }

    // Lets the editor preview board show the same prefab at three points in a hit.
    public void SetEditorPreview(float health, float trail, float alpha)
    {
        if (currentFill != null)
            currentFill.fillAmount = Mathf.Clamp01(health);
        if (damageTrail != null)
            damageTrail.fillAmount = Mathf.Clamp01(trail);
        if (visibility != null)
            visibility.alpha = Mathf.Clamp01(alpha);
    }

    private void Update()
    {
        if (currentFill != null && damageTrail != null && Time.unscaledTime >= trailStartsAt)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - trailStartsAt) / trailCatchupSeconds);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            damageTrail.fillAmount = Mathf.Lerp(trailStartFill, trailTargetFill, eased);
        }

        if (persistent || visibility == null)
            return;

        float timeLeft = visibleUntil - Time.unscaledTime;
        visibility.alpha = timeLeft >= fadeSeconds ? 1f : Mathf.Clamp01(timeLeft / fadeSeconds);
    }
}
