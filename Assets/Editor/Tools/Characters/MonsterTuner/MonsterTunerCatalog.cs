using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Overburst.EditorTools.MonsterTuner
{
    internal sealed class MonsterTunerCatalog
    {
        internal sealed class Entry
        {
            public EnemyDefinition Definition;
            public string Guid, Theme, Grade;
            public bool Registered;
            public string Label => Definition.DisplayName;
        }

        public readonly List<Entry> Entries = new List<Entry>();
        public void Refresh()
        {
            Entries.Clear();
            var registered = new HashSet<EnemyDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:EnemyCatalog"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                for (int i = 0; catalog != null && i < catalog.Count; i++)
                    if (catalog.GetDefinition(i) != null) registered.Add(catalog.GetDefinition(i));
            }
            var themes = new Dictionary<EnemyDefinition, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:EnemyThemeTable"))
            {
                var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(AssetDatabase.GUIDToAssetPath(guid));
                if (table == null) continue;
                foreach (var entry in table.Entries)
                    if (entry.definition != null) themes[entry.definition] = table.DisplayName;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/Editor/", StringComparison.Ordinal)) continue;
                var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
                if (definition == null) continue;
                Entries.Add(new Entry { Definition = definition, Guid = guid,
                    Theme = themes.TryGetValue(definition, out string theme) ? theme : "독립 / 미등록",
                    Grade = definition.Grade != null ? definition.Grade.DisplayName : "체급 누락",
                    Registered = registered.Contains(definition) || themes.ContainsKey(definition) });
            }
            Entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Theme + a.Label, b.Theme + b.Label));
        }

        public int Uses(UnityEngine.Object asset)
        {
            if (asset == null) return 0;
            return Entries.Count(e => e.Definition.ActorPrefab == asset || e.Definition.Variant == asset
                || e.Definition.AnimationProfile == asset || e.Definition.AbilitySet == asset
                || (asset is EnemyAbilityDefinition ability && UsesAbility(e.Definition, ability)));
        }
        private static bool UsesAbility(EnemyDefinition definition, EnemyAbilityDefinition ability)
        {
            var set = definition.AbilitySet;
            for (int i = 0; set != null && i < set.Count; i++) if (set.GetAbility(i) == ability) return true;
            return false;
        }
    }
}
