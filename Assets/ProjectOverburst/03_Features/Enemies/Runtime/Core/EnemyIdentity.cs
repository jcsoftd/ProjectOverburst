using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyIdentity : MonoBehaviour
{
    [SerializeField] private EnemyDefinition definition;

    public EnemyDefinition Definition => definition;
    public EnemySpeciesDefinition Species => definition != null ? definition.Species : null;
    public EnemyGradeProfile Grade => definition != null ? definition.Grade : null;
    public EnemyVariantProfile Variant => definition != null ? definition.Variant : null;
    public EnemyGradeType GradeType => Grade != null ? Grade.GradeType : EnemyGradeType.Normal;
    public string DisplayName => definition != null ? definition.DisplayName : gameObject.name;

    public void SetDefinition(EnemyDefinition value)
    {
        definition = value;
    }

    public void ClearDefinition()
    {
        definition = null;
    }
}
