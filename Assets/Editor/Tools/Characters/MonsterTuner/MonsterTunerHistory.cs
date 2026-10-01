using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Overburst.EditorTools.MonsterTuner
{
    [Serializable]
    internal sealed class MonsterTunerHistory
    {
        public string definitionGuid, backup;
        public List<MonsterTunerPatch> changes;
        public List<MonsterTunerStamp> savedStamps;
        private static string PathFor(string guid) => Path.Combine(MonsterTunerSession.OutputRoot, "History", guid + ".json");
        public static void Record(MonsterTunerSession session, List<MonsterTunerPatch> changes, string backup)
        {
            var current = MonsterTunerSession.Create(session.Definition, false);
            try
            {
                var record = new MonsterTunerHistory { definitionGuid = session.definitionGuid, changes = changes, backup = backup, savedStamps = current.stamps };
                string path = PathFor(session.definitionGuid); Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonUtility.ToJson(record, true));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(current); }
        }
        public static MonsterTunerHistory Read(string guid)
        {
            string path = PathFor(guid); return File.Exists(path) ? JsonUtility.FromJson<MonsterTunerHistory>(File.ReadAllText(path)) : null;
        }
        public string RestoreDraft(MonsterTunerSession session)
        {
            if (session.Dirty) return "현재 편집을 저장하거나 폐기한 뒤 직전 저장을 복원하세요.";
            if (savedStamps == null || savedStamps.Any(s => !s.Matches())) return "직전 저장 뒤 원본이 변경됐습니다. 복원을 거부했습니다.";
            foreach (var change in changes)
            {
                var previous = JsonUtility.FromJson<MonsterTunerValue>(JsonUtility.ToJson(change.before));
                if (previous.muzzles != null) foreach (var muzzle in previous.muzzles)
                {
                    var oldAbility = muzzle.ability.Resolve() as EnemyAbilityDefinition;
                    if (oldAbility == null) continue;
                    var current = Enumerable.Range(0, session.Definition.AbilitySet.Count).Select(session.Definition.AbilitySet.GetAbility).FirstOrDefault(a => a != null && a.AbilityId == oldAbility.AbilityId);
                    if (current != null) muzzle.ability.text = GlobalObjectId.GetGlobalObjectIdSlow(current).ToString();
                }
                session.Set(change.target, change.property, previous, "직전 저장 복원 · " + change.label);
            }
            return string.Empty;
        }
    }
}
