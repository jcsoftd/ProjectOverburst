using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Enemy Catalog", fileName = "EnemyCatalog")]
public sealed class EnemyCatalog : ScriptableObject
{
    [SerializeField] private EnemyDefinition[] definitions;

    public int Count => definitions != null ? definitions.Length : 0;

    public EnemyDefinition GetDefinition(int index)
    {
        return index >= 0 && index < Count ? definitions[index] : null;
    }

    public bool TryGet(string enemyId, out EnemyDefinition definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(enemyId))
            return false;

        for (int i = 0; i < Count; i++)
        {
            EnemyDefinition candidate = definitions[i];
            if (candidate != null
                && string.Equals(candidate.EnemyId, enemyId, System.StringComparison.Ordinal))
            {
                definition = candidate;
                return true;
            }
        }

        return false;
    }

    public bool Validate(out string message)
    {
        if (Count == 0)
        {
            message = "EnemyCatalog에 Definition이 없습니다.";
            return false;
        }

        HashSet<string> ids = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < Count; i++)
        {
            EnemyDefinition definition = definitions[i];
            if (definition == null || !definition.IsValid)
            {
                message = $"EnemyCatalog[{i}] Definition 계약이 유효하지 않습니다.";
                return false;
            }

            if (!ids.Add(definition.EnemyId))
            {
                message = $"EnemyCatalog에 중복 ID가 있습니다: {definition.EnemyId}";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    public void Configure(EnemyDefinition[] enemyDefinitions)
    {
        definitions = enemyDefinitions != null
            ? (EnemyDefinition[])enemyDefinitions.Clone()
            : new EnemyDefinition[0];
    }
}
