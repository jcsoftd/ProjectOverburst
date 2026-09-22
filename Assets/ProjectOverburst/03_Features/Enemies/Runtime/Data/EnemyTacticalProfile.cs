using UnityEngine;

public enum EnemyTacticalRole { MeleePressure, RangedHold, Skirmisher }

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Tactical Profile")]
public sealed class EnemyTacticalProfile : ScriptableObject
{
    [SerializeField] private EnemyTacticalRole role;
    [SerializeField, Min(0)] private float preferredMin = 3f;
    [SerializeField, Min(0)] private float preferredMax = 5.5f;
    [SerializeField, Min(.1f)] private float evaluationInterval = .25f;
    [SerializeField, Min(0)] private float retreatDistance = 1f;
    public EnemyTacticalRole Role => role;
    public bool UsesRangedPositioning => role != EnemyTacticalRole.MeleePressure;
    public float PreferredMin => Mathf.Max(0, preferredMin);
    public float PreferredMax => Mathf.Max(PreferredMin, preferredMax);
    public float EvaluationInterval => Mathf.Max(.1f, evaluationInterval);
    public float RetreatDistance => Mathf.Clamp(retreatDistance, 0, 1.2f);
    public void Configure(EnemyTacticalRole value, float min, float max)
    { role = value; preferredMin = min; preferredMax = max; }
}
