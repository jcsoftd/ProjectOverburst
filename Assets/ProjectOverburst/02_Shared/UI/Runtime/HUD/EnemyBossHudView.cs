using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class EnemyBossHudView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text bossNameText;
    [SerializeField] private TMP_Text phaseText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image healthFill;

    private EnemyBossPhaseController boundBoss;
    private CombatHealth boundHealth;

    public EnemyBossPhaseController BoundBoss => boundBoss;
    public bool IsVisible =>
        canvasGroup != null
            ? canvasGroup.alpha > 0.001f
            : gameObject.activeInHierarchy;
    public float DisplayedHealth01 =>
        healthFill != null ? healthFill.fillAmount : 0f;

    private void Awake()
    {
        ResolveReferences();
        SetVisible(false);
    }

    private void OnEnable()
    {
        EnemyBossEncounterRegistry.EncounterStarted += HandleEncounterStarted;
        EnemyBossEncounterRegistry.PhaseChanged += HandlePhaseChanged;
        EnemyBossEncounterRegistry.BossDefeated += HandleBossDefeated;
        EnemyBossEncounterRegistry.EncounterEnded += HandleEncounterEnded;

        EnemyBossPhaseController current =
            EnemyBossEncounterRegistry.Current;
        if (current != null)
            Bind(current);
    }

    private void OnDisable()
    {
        EnemyBossEncounterRegistry.EncounterStarted -= HandleEncounterStarted;
        EnemyBossEncounterRegistry.PhaseChanged -= HandlePhaseChanged;
        EnemyBossEncounterRegistry.BossDefeated -= HandleBossDefeated;
        EnemyBossEncounterRegistry.EncounterEnded -= HandleEncounterEnded;
        UnbindHealth();
    }

    public void ConfigureAuthoring(
        CanvasGroup configuredCanvasGroup,
        TMP_Text configuredBossNameText,
        TMP_Text configuredPhaseText,
        TMP_Text configuredHealthText,
        Image configuredHealthFill)
    {
        canvasGroup = configuredCanvasGroup;
        bossNameText = configuredBossNameText;
        phaseText = configuredPhaseText;
        healthText = configuredHealthText;
        healthFill = configuredHealthFill;
        ResolveReferences();
        Refresh();
    }

    private void HandleEncounterStarted(EnemyBossPhaseController boss)
    {
        Bind(boss);
    }

    private void HandlePhaseChanged(
        EnemyBossPhaseController boss,
        int previousPhase,
        int currentPhase)
    {
        if (boss == boundBoss)
            Refresh();
    }

    private void HandleBossDefeated(EnemyBossPhaseController boss)
    {
        if (boss != boundBoss)
            return;

        Refresh();
        SetVisible(false);
    }

    private void HandleEncounterEnded(EnemyBossPhaseController boss)
    {
        if (boss == boundBoss)
            Clear();
    }

    private void Bind(EnemyBossPhaseController boss)
    {
        if (boss == null)
        {
            Clear();
            return;
        }

        UnbindHealth();
        boundBoss = boss;
        boundHealth = boss.Health;
        if (boundHealth != null)
        {
            boundHealth.OnHealthChanged += HandleHealthChanged;
            boundHealth.OnDead += HandleHealthDead;
        }

        Refresh();
        SetVisible(!boss.IsDefeated);
    }

    private void Clear()
    {
        UnbindHealth();
        boundBoss = null;
        SetVisible(false);
    }

    private void UnbindHealth()
    {
        if (boundHealth != null)
        {
            boundHealth.OnHealthChanged -= HandleHealthChanged;
            boundHealth.OnDead -= HandleHealthDead;
        }

        boundHealth = null;
    }

    private void HandleHealthChanged(
        CombatHealth source,
        float currentHealth,
        float maximumHealth)
    {
        if (source == boundHealth)
            Refresh();
    }

    private void HandleHealthDead(CombatHealth source, DamageInfo info)
    {
        if (source == boundHealth)
            Refresh();
    }

    private void Refresh()
    {
        if (boundBoss == null)
            return;

        EnemyBossDefinition definition = boundBoss.BossDefinition;
        EnemyBossPhaseDefinition phase = boundBoss.CurrentPhase;
        CombatHealth health = boundBoss.Health;
        float maximum = health != null ? Mathf.Max(0f, health.MaxHp) : 0f;
        float current = health != null
            ? Mathf.Clamp(health.CurrentHp, 0f, maximum)
            : 0f;
        float normalized = maximum > 0f ? current / maximum : 0f;

        if (bossNameText != null)
        {
            bossNameText.text = definition != null
                ? definition.DisplayName
                : boundBoss.name.Replace("(Clone)", string.Empty).Trim();
        }

        if (phaseText != null)
        {
            int phaseNumber = Mathf.Max(1, boundBoss.CurrentPhaseIndex + 1);
            int phaseCount = definition != null
                ? Mathf.Max(1, definition.PhaseCount)
                : phaseNumber;
            string phaseLabel = phase != null
                ? phase.DisplayName
                : string.Empty;
            phaseText.text = string.IsNullOrWhiteSpace(phaseLabel)
                ? $"PHASE {phaseNumber} / {phaseCount}"
                : $"PHASE {phaseNumber} / {phaseCount}  {phaseLabel}";
        }

        if (healthText != null)
            healthText.text = $"{current:0} / {maximum:0}";
        if (healthFill != null)
            healthFill.fillAmount = Mathf.Clamp01(normalized);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void ResolveReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
    }
}
