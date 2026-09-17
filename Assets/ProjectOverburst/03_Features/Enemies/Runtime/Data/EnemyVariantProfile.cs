using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Variant Profile", fileName = "EVP_Enemy_Normal")]
public sealed class EnemyVariantProfile : ScriptableObject
{
    [SerializeField] private string variantId;
    [SerializeField] private string displayName;
    [SerializeField] private Vector3 visualScale = Vector3.one;
    [SerializeField] private Color tint = Color.white;
    [SerializeField] private Vector3 collisionScale = Vector3.one;
    [SerializeField] private Vector3 anchorScale = Vector3.one;
    [SerializeField, Min(0.01f)] private float healthMultiplier = 1f;
    [SerializeField, Min(0f)] private float damageMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float moveSpeedMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float attackSpeedMultiplier = 1f;

    public string VariantId => variantId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? variantId : displayName;
    public Vector3 VisualScale => SanitizeScale(visualScale);
    public Color Tint => tint;
    public Vector3 CollisionScale => SanitizeScale(collisionScale);
    public Vector3 AnchorScale => SanitizeScale(anchorScale);
    public float HealthMultiplier => Mathf.Max(0.01f, healthMultiplier);
    public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);
    public float MoveSpeedMultiplier => Mathf.Max(0.01f, moveSpeedMultiplier);
    public float AttackSpeedMultiplier => Mathf.Max(0.01f, attackSpeedMultiplier);
    public bool IsValid => !string.IsNullOrWhiteSpace(variantId);

    public void Configure(string id, string label, Vector3 scale, Color colorTint)
    {
        variantId = id != null ? id.Trim() : string.Empty;
        displayName = label != null ? label.Trim() : string.Empty;
        visualScale = SanitizeScale(scale);
        tint = colorTint;
        collisionScale = visualScale;
        anchorScale = visualScale;
    }

    public void ConfigureRuntimeModifiers(
        Vector3 colliderScale,
        Vector3 runtimeAnchorScale,
        float health,
        float damage,
        float moveSpeed,
        float attackSpeed)
    {
        collisionScale = SanitizeScale(colliderScale);
        anchorScale = SanitizeScale(runtimeAnchorScale);
        healthMultiplier = Mathf.Max(0.01f, health);
        damageMultiplier = Mathf.Max(0f, damage);
        moveSpeedMultiplier = Mathf.Max(0.01f, moveSpeed);
        attackSpeedMultiplier = Mathf.Max(0.01f, attackSpeed);
    }

    private static Vector3 SanitizeScale(Vector3 value)
    {
        return new Vector3(
            Mathf.Max(0.01f, value.x),
            Mathf.Max(0.01f, value.y),
            Mathf.Max(0.01f, value.z));
    }
}
