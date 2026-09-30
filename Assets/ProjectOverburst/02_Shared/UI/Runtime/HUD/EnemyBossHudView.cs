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
    [Header("대표 보스(선택)")]
    [SerializeField] private Image groggyFill;
    [SerializeField] private TMP_Text stateText;

    private EnemyBossPhaseController boundBoss;
    private CombatHealth boundHealth;
    private EnemyBossCombatDirector boundDirector;

    public EnemyBossPhaseController BoundBoss => boundBoss;
    public float DisplayedGroggy01 => groggyFill != null ? groggyFill.fillAmount : 0f;
    public string DisplayedState => stateText != null ? stateText.text : string.Empty;
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
        boundDirector = boss.GetComponent<EnemyBossCombatDirector>();
        boundHealth = boss.Health;
        if (boundHealth != null)
        {
            boundHealth.OnHealthChanged += HandleHealthChanged;
            boundHealth.OnDead += HandleHealthDead;
        }

        Refresh();
        SetVisible(!boss.IsDefeated && (boundDirector == null || boundDirector.IsEngaged));
    }

    private void Clear()
    {
        UnbindHealth();
        boundBoss = null;
        boundDirector = null;
        RefreshDirector();
        SetVisible(false);
    }

    // 그로기 게이지와 전환 상태는 매 프레임 바뀌므로 표시 중인 보스 1개만 읽는다.
    private void LateUpdate()
    {
        if (boundBoss == null) return;
        RefreshDirector();
        if (boundDirector != null) SetVisible(!boundBoss.IsDefeated && boundDirector.IsEngaged);
    }

    private void RefreshDirector()
    {
        bool has = boundDirector != null && boundBoss != null && !boundBoss.IsDefeated;
        if (groggyFill != null)
        {
            groggyFill.transform.parent.gameObject.SetActive(has);
            if (has) groggyFill.fillAmount = boundDirector.Groggy01;
        }
        if (stateText != null)
        {
            string state = !has ? string.Empty : boundDirector.IsGroggy ? "그로기"
                : boundDirector.IsTransitioning ? "포효" : string.Empty;
            if (stateText.text != state) stateText.text = state;
        }
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
        EnsureFillSprite(healthFill);
        EnsureFillSprite(groggyFill);
    }

    // 2026-10-01: Filled 이미지는 스프라이트가 없으면 채움 비율을 무시하고 항상 가득 그린다(보스 캡처에서 647/1796인데 막대가 가득).
    // 다른 HUD 스크립트처럼 런타임 1×1 흰 스프라이트를 공유하고 색은 기존 Image.color를 쓴다. 프리팹에는 저장하지 않는다.
    private static Sprite runtimeFillSprite;

    private static void EnsureFillSprite(Image image)
    {
        if (!Application.isPlaying || image == null || image.sprite != null || image.type != Image.Type.Filled)
            return;
        if (runtimeFillSprite == null)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            { name = "BossHudFill", hideFlags = HideFlags.DontSave };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            runtimeFillSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(.5f, .5f), 100f);
            runtimeFillSprite.name = "BossHudFill";
            runtimeFillSprite.hideFlags = HideFlags.DontSave;
        }
        image.sprite = runtimeFillSprite;
    }
}
