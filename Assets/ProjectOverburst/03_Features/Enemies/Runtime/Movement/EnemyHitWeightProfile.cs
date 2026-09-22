using UnityEngine;

public enum EnemyHitWeight { Light, Standard, Heavy }

// Shared presentation/knockback presets. Crowd weight and damage calculation are independent.
[CreateAssetMenu(menuName = "OVERBURST/Enemies/Hit Weight Profile")]
public sealed class EnemyHitWeightProfile : ScriptableObject
{
    [SerializeField] private EnemyHitWeight weight;
    [SerializeField, Min(0f)] private float distancePerStrength = .1f;
    [SerializeField, Min(0f)] private float maximumDistance = .5f;
    [SerializeField, Min(.02f)] private float travelDuration = .16f;
    [SerializeField, Min(0f)] private float staggerDuration = .2f;
    [SerializeField, Min(0f)] private float visualLiftHeight = .12f;
    [SerializeField, Min(.02f)] private float visualLiftDuration = .22f;
    [SerializeField, Min(0f)] private float reactionCooldown = .35f;

    public EnemyHitWeight Weight => weight;
    public float TravelDuration => Mathf.Max(.02f, travelDuration);
    public float StaggerDuration => Mathf.Max(0f, staggerDuration);
    public float VisualLiftHeight => Mathf.Max(0f, visualLiftHeight);
    public float VisualLiftDuration => Mathf.Max(.02f, visualLiftDuration);
    public float ReactionCooldown => Mathf.Max(TravelDuration, Mathf.Max(StaggerDuration, Mathf.Max(VisualLiftDuration, reactionCooldown)));
    public float ResolveDistance(float strength) => Mathf.Min(Mathf.Max(0f, maximumDistance), Mathf.Max(0f, strength) * Mathf.Max(0f, distancePerStrength));

    public void Configure(EnemyHitWeight type, float perStrength, float maxDistance, float travel, float stagger, float lift, float liftDuration, float cooldown)
    {
        weight=type; distancePerStrength=Mathf.Max(0,perStrength); maximumDistance=Mathf.Max(0,maxDistance);
        travelDuration=Mathf.Max(.02f,travel); staggerDuration=Mathf.Max(0,stagger);
        visualLiftHeight=Mathf.Max(0,lift); visualLiftDuration=Mathf.Max(.02f,liftDuration); reactionCooldown=Mathf.Max(0,cooldown);
    }
}
