using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Grade Profile", fileName = "EGP_Normal")]
public sealed class EnemyGradeProfile : ScriptableObject
{
    [SerializeField] private string gradeId;
    [SerializeField] private string displayName;
    [SerializeField] private EnemyGradeType gradeType = EnemyGradeType.Normal;
    [SerializeField, Min(0.01f)] private float healthMultiplier = 1f;
    [SerializeField, Min(0f)] private float damageMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float moveSpeedMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float attackSpeedMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float scaleMultiplier = 1f;

    public string GradeId => gradeId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gradeId : displayName;
    public EnemyGradeType GradeType => gradeType;
    public float HealthMultiplier => Mathf.Max(0.01f, healthMultiplier);
    public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);
    public float MoveSpeedMultiplier => Mathf.Max(0.01f, moveSpeedMultiplier);
    public float AttackSpeedMultiplier => Mathf.Max(0.01f, attackSpeedMultiplier);
    public float ScaleMultiplier => Mathf.Max(0.01f, scaleMultiplier);
    public bool IsValid => !string.IsNullOrWhiteSpace(gradeId);

    public void Configure(
        string id,
        string label,
        EnemyGradeType type,
        float health,
        float damage,
        float moveSpeed,
        float scale)
    {
        Configure(id, label, type, health, damage, moveSpeed, 1f, scale);
    }

    public void Configure(
        string id,
        string label,
        EnemyGradeType type,
        float health,
        float damage,
        float moveSpeed,
        float attackSpeed,
        float scale)
    {
        gradeId = id != null ? id.Trim() : string.Empty;
        displayName = label != null ? label.Trim() : string.Empty;
        gradeType = type;
        healthMultiplier = Mathf.Max(0.01f, health);
        damageMultiplier = Mathf.Max(0f, damage);
        moveSpeedMultiplier = Mathf.Max(0.01f, moveSpeed);
        attackSpeedMultiplier = Mathf.Max(0.01f, attackSpeed);
        scaleMultiplier = Mathf.Max(0.01f, scale);
    }
}
