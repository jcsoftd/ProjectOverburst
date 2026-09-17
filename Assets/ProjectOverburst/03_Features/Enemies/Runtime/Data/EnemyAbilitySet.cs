using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Ability Set", fileName = "EAS_Enemy")]
public sealed class EnemyAbilitySet : ScriptableObject
{
    [SerializeField] private string abilitySetId;
    [SerializeField] private EnemyAbilityDefinition[] abilities;

    public string AbilitySetId => abilitySetId;
    public int Count => abilities != null ? abilities.Length : 0;
    public bool IsValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(abilitySetId) || Count == 0)
                return false;

            for (int i = 0; i < Count; i++)
            {
                if (abilities[i] == null || !abilities[i].IsValid)
                    return false;
            }

            return true;
        }
    }

    public EnemyAbilityDefinition GetAbility(int index)
    {
        return index >= 0 && index < Count ? abilities[index] : null;
    }

    public float CalculateTotalWeight()
    {
        float total = 0f;
        for (int i = 0; i < Count; i++)
        {
            EnemyAbilityDefinition ability = abilities[i];
            if (ability != null && ability.IsValid)
                total += ability.Weight;
        }

        return total;
    }

    public bool TrySelectByWeight(float zeroToOne, out EnemyAbilityDefinition selected)
    {
        selected = null;
        float totalWeight = CalculateTotalWeight();
        if (totalWeight <= 0f)
            return false;

        float cursor = Mathf.Clamp01(zeroToOne) * totalWeight;
        for (int i = 0; i < Count; i++)
        {
            EnemyAbilityDefinition ability = abilities[i];
            if (ability == null || !ability.IsValid)
                continue;

            cursor -= ability.Weight;
            if (cursor <= 0f)
            {
                selected = ability;
                return true;
            }
        }

        for (int i = Count - 1; i >= 0; i--)
        {
            if (abilities[i] != null && abilities[i].IsValid)
            {
                selected = abilities[i];
                return true;
            }
        }

        return false;
    }

    public void Configure(string id, EnemyAbilityDefinition[] definitions)
    {
        abilitySetId = id != null ? id.Trim() : string.Empty;
        abilities = definitions != null
            ? (EnemyAbilityDefinition[])definitions.Clone()
            : new EnemyAbilityDefinition[0];
    }
}
