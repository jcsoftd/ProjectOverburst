using System.Collections.Generic;
using UnityEngine;

public enum EnemyRankType
{
    Normal,
    Elite
}

public sealed class EnemyRank : MonoBehaviour
{
    private static readonly HashSet<EnemyRank> activeEnemies = new HashSet<EnemyRank>();

    [SerializeField] private EnemyRankType rank = EnemyRankType.Normal;
    [SerializeField] private string displayName;

    public EnemyRankType Rank { get { return rank; } }
    public EnemyGradeType GradeType { get; private set; } = EnemyGradeType.Normal;

    private EnemyRankType authoredRank;
    private string authoredDisplayName;
    private bool authoredStateCaptured;

    private void Awake()
    {
        CaptureAuthoredState();
    }

    private void OnEnable()
    {
        activeEnemies.Add(this);
    }

    private void OnDisable()
    {
        activeEnemies.Remove(this);
    }

    public static void CollectActive(List<EnemyRank> buffer)
    {
        if (buffer == null)
            return;

        buffer.Clear();
        foreach (EnemyRank enemy in activeEnemies)
        {
            if (enemy != null && enemy.isActiveAndEnabled)
                buffer.Add(enemy);
        }
    }

    public void ConfigureFromDefinition(EnemyDefinition definition)
    {
        CaptureAuthoredState();
        EnemyGradeProfile grade = definition != null ? definition.Grade : null;
        GradeType = grade != null ? grade.GradeType : EnemyGradeType.Normal;
        rank = GradeType == EnemyGradeType.Normal
            ? EnemyRankType.Normal
            : EnemyRankType.Elite;
        displayName = definition != null ? definition.DisplayName : authoredDisplayName;
    }

    public void ResetForPool()
    {
        CaptureAuthoredState();
        rank = authoredRank;
        displayName = authoredDisplayName;
        GradeType = authoredRank == EnemyRankType.Elite
            ? EnemyGradeType.Elite
            : EnemyGradeType.Normal;
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            return gameObject.name.Replace("(Clone)", string.Empty).Trim();
        }
    }

    private void CaptureAuthoredState()
    {
        if (authoredStateCaptured)
            return;

        authoredRank = rank;
        authoredDisplayName = displayName;
        GradeType = rank == EnemyRankType.Elite
            ? EnemyGradeType.Elite
            : EnemyGradeType.Normal;
        authoredStateCaptured = true;
    }
}
