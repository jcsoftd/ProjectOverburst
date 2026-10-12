using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PlayerLevelUpNotification : MonoBehaviour
{
    public const string ResourcePath = "UI/Notifications/PF_PlayerLevelUpNotification";
    [SerializeField] private RectTransform panel;
    [SerializeField] private CanvasGroup visibility;
    [SerializeField] private Text levelText;
    private enum Phase { Hidden, Enter, Hold, Exit, Return }
    private Phase phase;
    private float elapsed, fromAlpha, fromOffset, fromScale;
    private float offset, scale = 1f;
    public bool IsVisible => phase != Phase.Hidden;

    public void Configure(RectTransform rect, CanvasGroup group, Text label)
    { panel = rect; visibility = group; levelText = label; }

    private void Awake() => Hide();
    private void OnDisable() => Hide();

    public void Show(int level)
    {
        levelText.text = "레벨 " + level;
        transform.SetAsLastSibling();
        if (phase == Phase.Hidden) { phase = Phase.Enter; Apply(0f, -6f, .985f); }
        else if (phase == Phase.Exit || phase == Phase.Return)
        {
            phase = Phase.Return; fromAlpha = visibility.alpha; fromOffset = offset; fromScale = scale;
        }
        // An unfinished entrance continues without a flash or a second panel.
        if (phase != Phase.Enter || elapsed == 0f) elapsed = 0f;
        Fit();
    }

    private void Update()
    {
        if (phase == Phase.Hidden) return;
        Fit(); elapsed += Time.unscaledDeltaTime;
        switch (phase)
        {
            case Phase.Enter:
                float enter = Mathf.Clamp01(elapsed / .30f);
                float eased = 1f - Mathf.Pow(1f - enter, 3f);
                Apply(eased, Mathf.Lerp(-6f, 0f, eased), Mathf.Lerp(.985f, 1f, eased));
                if (enter >= 1f) { phase = Phase.Hold; elapsed = 0f; }
                break;
            case Phase.Return:
                float resume = Mathf.Clamp01(elapsed / .12f);
                float back = 1f - Mathf.Pow(1f - resume, 3f);
                Apply(Mathf.Lerp(fromAlpha, 1f, back), Mathf.Lerp(fromOffset, 0f, back), Mathf.Lerp(fromScale, 1f, back));
                if (resume >= 1f) { phase = Phase.Hold; elapsed = 0f; }
                break;
            case Phase.Hold:
                if (elapsed >= 1.60f) { phase = Phase.Exit; elapsed = 0f; }
                break;
            case Phase.Exit:
                float exit = Mathf.Clamp01(elapsed / .70f);
                Apply(1f - exit * exit, 6f * exit * exit, 1f);
                if (exit >= 1f) Hide();
                break;
        }
    }

    private void Fit()
    {
        var parent = transform.parent as RectTransform;
        float fit = parent != null ? Mathf.Min(parent.rect.width / 1920f, parent.rect.height / 1080f) : 1f;
        transform.localScale = Vector3.one * fit;
    }
    private void Apply(float alpha, float y, float size)
    {
        offset = y; scale = size;
        visibility.alpha = alpha; visibility.interactable = false; visibility.blocksRaycasts = false;
        panel.anchoredPosition = new Vector2(0f, 260f + y);
        panel.localScale = Vector3.one * (.55f * size);
    }
    public void Hide()
    {
        phase = Phase.Hidden; elapsed = 0f;
        if (visibility != null && panel != null) Apply(0f, 0f, 1f);
    }
}
