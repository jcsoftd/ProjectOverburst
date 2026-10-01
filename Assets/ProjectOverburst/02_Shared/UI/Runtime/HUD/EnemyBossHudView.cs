using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 2026-10-01: 상단 보스 HUD. 대상 HUD와 같은 네임플레이트 구성(EnemyTargetHudRpg11Builder)에
// 단계 보석·단계 경계 눈금·마름모 셰이더 연출(HudBossEmblemFx, 보스별 EnemyBossDefinition.EmblemFx)을 더했다.
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
    [Header("2026-10-01 네임플레이트 구성")]
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text percentText;
    [SerializeField] private Image trailFill;
    [SerializeField] private RectTransform phaseGemRoot;
    [SerializeField] private Image phaseGemTemplate;
    [SerializeField] private RectTransform phaseTickRoot;
    [SerializeField] private RectTransform phaseTickTemplate;
    [SerializeField] private EnemyTargetStatusRow statusRow;
    [SerializeField] private HudBossEmblemFx emblemFx;
    [SerializeField] private float phaseGemSpacing = 40f;
    [SerializeField] private string bossTagColor = "#F05840";

    private EnemyBossPhaseController boundBoss;
    private CombatHealth boundHealth;
    private EnemyBossCombatDirector boundDirector;
    private readonly HudDamageTrail trail = new HudDamageTrail();
    private readonly List<Image> phaseGems = new List<Image>();
    private readonly List<RectTransform> phaseTicks = new List<RectTransform>();
    private int shownPhaseCount = -1;
    private int shownPhaseIndex = -1;

    public EnemyBossPhaseController BoundBoss => boundBoss;
    public float DisplayedGroggy01 => groggyFill != null ? groggyFill.fillAmount : 0f;
    public string DisplayedState => stateText != null ? stateText.text : string.Empty;
    public bool IsVisible =>
        canvasGroup != null
            ? canvasGroup.alpha > 0.001f
            : gameObject.activeInHierarchy;
    public float DisplayedHealth01 =>
        healthFill != null ? healthFill.fillAmount : 0f;
    public EnemyBossEmblemFxMode DisplayedEmblemFx =>
        emblemFx != null ? emblemFx.Mode : EnemyBossEmblemFxMode.Fire;
    public int DisplayedLitPhaseGems
    {
        get
        {
            int lit = 0;
            for (int i = 0; i < phaseGems.Count; i++)
            {
                Transform gemLit = phaseGems[i] != null ? phaseGems[i].transform.Find("Lit") : null;
                if (phaseGems[i] != null && phaseGems[i].gameObject.activeSelf && gemLit != null && gemLit.gameObject.activeSelf)
                    lit++;
            }

            return lit;
        }
    }

    private void Awake()
    {
        ResolveReferences();
        trail.Bind(trailFill);
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
        statusRow?.Unbind();
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

        EnemyBossDefinition definition = boss.BossDefinition;
        emblemFx?.SetMode(definition != null ? definition.EmblemFx : EnemyBossEmblemFxMode.Fire);
        statusRow?.Bind(boundHealth);
        BuildPhaseMarks(definition);
        trail.Bind(trailFill);
        trail.Snap(CurrentHealth01());
        shownPhaseIndex = -1;

        Refresh();
        SetVisible(!boss.IsDefeated && (boundDirector == null || boundDirector.IsEngaged));
    }

    private void Clear()
    {
        UnbindHealth();
        statusRow?.Unbind();
        boundBoss = null;
        boundDirector = null;
        RefreshDirector();
        SetVisible(false);
    }

    // 그로기 게이지와 전환 상태는 매 프레임 바뀌므로 표시 중인 보스 1개만 읽는다.
    private void LateUpdate()
    {
        trail.Tick();
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

    private float CurrentHealth01()
    {
        CombatHealth health = boundBoss != null ? boundBoss.Health : null;
        float maximum = health != null ? Mathf.Max(0f, health.MaxHp) : 0f;
        return maximum > 0f ? Mathf.Clamp01(health.CurrentHp / maximum) : 0f;
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

        string displayName = definition != null
            ? definition.DisplayName
            : boundBoss.name.Replace("(Clone)", string.Empty).Trim();
        EnemyRank rank = boundBoss.GetComponent<EnemyRank>();
        int phaseNumber = Mathf.Max(1, boundBoss.CurrentPhaseIndex + 1);
        int phaseCount = definition != null
            ? Mathf.Max(1, definition.PhaseCount)
            : phaseNumber;
        ApplyTexts(displayName, rank != null ? rank.Level : 1, current, maximum);

        if (phaseText != null)
        {
            string phaseLabel = phase != null
                ? phase.DisplayName
                : string.Empty;
            phaseText.text = string.IsNullOrWhiteSpace(phaseLabel)
                ? $"PHASE {phaseNumber} / {phaseCount}"
                : $"PHASE {phaseNumber} / {phaseCount}  {phaseLabel}";
        }

        if (healthFill != null)
            healthFill.fillAmount = Mathf.Clamp01(normalized);
        trail.Set(Mathf.Clamp01(normalized));
        ApplyPhaseGems(boundBoss.CurrentPhaseIndex, phaseCount);
    }

    private void ApplyTexts(string displayName, int level, float current, float maximum)
    {
        if (bossNameText != null)
            bossNameText.text = displayName + "<space=0.4em><size=63%><color=" + bossTagColor + ">보스</color></size>";
        if (levelText != null)
            levelText.text = Mathf.Max(1, level).ToString(CultureInfo.InvariantCulture);
        if (healthText != null)
            healthText.text = EnemyTargetHpSlotUI.FormatHealth(current, maximum);
        if (percentText != null)
            percentText.text = EnemyTargetHpSlotUI.FormatPercent(maximum > 0f ? current / maximum : 0f);
    }

    // 단계 보석(오른쪽부터 남은 단계만 켠다)과 막대 위 단계 경계 눈금을 보스 정의에 맞게 만든다.
    private void BuildPhaseMarks(EnemyBossDefinition definition)
    {
        int count = definition != null ? Mathf.Max(1, definition.PhaseCount) : 1;
        float[] thresholds = new float[Mathf.Max(0, count - 1)];
        for (int i = 1; i < count; i++)
        {
            EnemyBossPhaseDefinition phase = definition != null ? definition.GetPhase(i) : null;
            thresholds[i - 1] = phase != null ? phase.EnterAtOrBelowNormalizedHealth : 1f - (float)i / count;
        }

        BuildPhaseMarks(count, thresholds);
    }

    private void BuildPhaseMarks(int count, float[] thresholds)
    {
        if (phaseGemRoot != null && phaseGemTemplate != null)
        {
            phaseGemTemplate.gameObject.SetActive(false);
            while (phaseGems.Count < count)
            {
                Image gem = Instantiate(phaseGemTemplate, phaseGemRoot, false);
                gem.name = "Gem " + phaseGems.Count;
                phaseGems.Add(gem);
            }

            for (int i = 0; i < phaseGems.Count; i++)
            {
                bool used = i < count;
                phaseGems[i].gameObject.SetActive(used);
                if (used)
                    phaseGems[i].rectTransform.anchoredPosition = new Vector2(-i * phaseGemSpacing, 0f);
            }
        }

        if (phaseTickRoot != null && phaseTickTemplate != null)
        {
            phaseTickTemplate.gameObject.SetActive(false);
            int tickCount = thresholds != null ? thresholds.Length : 0;
            while (phaseTicks.Count < tickCount)
            {
                RectTransform tick = Instantiate(phaseTickTemplate, phaseTickRoot, false);
                tick.name = "Tick " + phaseTicks.Count;
                phaseTicks.Add(tick);
            }

            for (int i = 0; i < phaseTicks.Count; i++)
            {
                bool used = i < tickCount;
                phaseTicks[i].gameObject.SetActive(used);
                if (!used)
                    continue;
                float x = Mathf.Clamp01(thresholds[i]);
                phaseTicks[i].anchorMin = new Vector2(x, 0f);
                phaseTicks[i].anchorMax = new Vector2(x, 1f);
                phaseTicks[i].anchoredPosition = Vector2.zero;
            }
        }

        shownPhaseCount = count;
        shownPhaseIndex = -1;
    }

    private void ApplyPhaseGems(int phaseIndex, int phaseCount)
    {
        if (phaseIndex == shownPhaseIndex && phaseCount == shownPhaseCount)
            return;

        shownPhaseIndex = phaseIndex;
        int remaining = Mathf.Max(0, phaseCount - Mathf.Max(0, phaseIndex));
        for (int i = 0; i < phaseGems.Count; i++)
        {
            Transform lit = phaseGems[i] != null ? phaseGems[i].transform.Find("Lit") : null;
            if (lit != null)
                lit.gameObject.SetActive(i < remaining);
        }
    }

    // 편집 모드 캡처·워크숍 미리보기용. 전투 보스 없이 같은 모양을 띄운다.
    public void ShowPreview(string displayName, int level, float current, float maximum, float trailHealth,
        int phaseIndex, float[] phaseThresholds, EnemyBossEmblemFxMode fx)
    {
        ResolveReferences();
        emblemFx?.SetMode(fx);
        int count = (phaseThresholds != null ? phaseThresholds.Length : 0) + 1;
        BuildPhaseMarks(count, phaseThresholds);
        ApplyTexts(displayName, level, current, maximum);
        if (healthFill != null)
            healthFill.fillAmount = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
        if (trailFill != null)
            trailFill.fillAmount = maximum > 0f ? Mathf.Clamp01(trailHealth / maximum) : 0f;
        ApplyPhaseGems(phaseIndex, count);
        if (groggyFill != null)
            groggyFill.transform.parent.gameObject.SetActive(false);
        SetVisible(true);
    }

    public EnemyTargetStatusRow StatusRow => statusRow;

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
        EnsureFillSprite(trailFill);
    }

    // 2026-10-01: Filled 이미지는 스프라이트가 없으면 채움 비율을 무시하고 항상 가득 그린다(보스 캡처에서 647/1796인데 막대가 가득).
    // 다른 HUD 스크립트처럼 런타임 1×1 흰 스프라이트를 공유하고 색은 기존 Image.color를 쓴다. 프리팹에는 저장하지 않는다.
    // (네임플레이트 구성은 채움 스프라이트가 있어 쓰이지 않지만, 스프라이트가 빠진 프리팹을 위한 안전장치로 둔다.)
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
