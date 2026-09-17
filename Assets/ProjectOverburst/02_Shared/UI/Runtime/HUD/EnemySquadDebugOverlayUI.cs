using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class EnemySquadDebugOverlayUI : MonoBehaviour // 왼쪽 아래 몬스터·부대 실시간 집계
{
    [SerializeField] private Image background;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;
    [SerializeField] private bool showInEditorOrDevelopmentBuild = true;

    private readonly StringBuilder builder = new StringBuilder(256);
    private float nextRefreshTime;

    private void Awake()
    {
        if (!IsDebugUiAllowed())
        {
            gameObject.SetActive(false);
            return;
        }

        ResolveReferences();
        RefreshNow();
    }

    private void OnEnable()
    {
        nextRefreshTime = 0f;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
        RefreshNow();
    }

    private void RefreshNow()
    {
        if (label == null)
            return;

        EnemySquadPursuitRuntimeStats stats = EnemySquadPursuitRuntimeService.GetRuntimeStats();
        builder.Clear();
        builder.Append("총 몬스터: ").Append(EnemyAIController.AliveEnemyCount).AppendLine();
        builder.Append("어그로 몬스터: ").Append(EnemyAIController.AggroEnemyCount).AppendLine();
        builder.Append("세션 사망: ").Append(EnemyAIController.SessionMonsterDeathCount)
            .Append("  ·  이탈 기준: ")
            .Append(EnemyAIController.CurrentCombatLoseTargetRange.ToString("0"))
            .Append("m")
            .AppendLine();

        if (!stats.IsActive)
        {
            builder.Append("부대 시스템: 대기 ")
                .Append(stats.CombatEligibleAgentCount)
                .Append('/')
                .Append(stats.ActivationCount)
                .Append("  ·  등록 ")
                .Append(stats.RegisteredAgentCount);
        }
        else
        {
            builder.Append("부대 시스템: 켜짐  ·  구역 ")
                .Append(stats.ActiveEncounterCount)
                .Append("  ·  부대 ")
                .Append(stats.SquadCount)
                .AppendLine();
            builder.Append("추격 ").Append(stats.PursuitCount)
                .Append("  러쉬 ").Append(stats.RushCount)
                .Append("  근접 ").Append(stats.NearCombatCount)
                .Append("  예비 ").Append(stats.ReserveCount)
                .Append("  잔존 ").Append(stats.RemnantCount);
        }

        label.text = builder.ToString();
    }

    private void ResolveReferences()
    {
        if (background == null)
            background = GetComponent<Image>();
        if (label == null)
            label = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private bool IsDebugUiAllowed()
    {
        if (!showInEditorOrDevelopmentBuild)
            return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }
}
