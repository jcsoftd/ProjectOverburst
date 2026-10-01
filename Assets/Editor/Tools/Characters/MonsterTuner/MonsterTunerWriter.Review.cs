using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.MonsterTuner
{
    internal static partial class MonsterTunerWriter
    {
        internal sealed class SaveTarget
        {
            public string Label, SourcePath, DestinationPath, Reason;
            public int SharedCount;
            public bool Created;
        }
        private static string DestinationFolder(EnemyDefinition definition)
            => FixtureRoot != null ? FixtureRoot + "/Saved" : "Assets/ProjectOverburst/Resources/Enemies/Tuning/" + SafeName(definition.EnemyId);
        public static List<SaveTarget> ReviewTargets(MonsterTunerSession session, MonsterTunerCatalog catalog)
        {
            var result = new List<SaveTarget>(); var definition = session.Definition;
            if (!session.Dirty || definition == null) return result;
            string folder = DestinationFolder(definition);
            Add("몬스터 정의", definition, null, "선택한 몬스터의 연결 갱신");
            if (session.edits.Any(e => e.target == "variant"))
                Add("크기 프로필", definition.Variant, catalog.Uses(definition.Variant) > 1 ? "EVP_Tuning.asset" : null);
            bool remappedAbility = false;
            if (session.edits.Any(e => e.target.StartsWith("ability:", StringComparison.Ordinal)))
            {
                var set = definition.AbilitySet;
                bool inherited = new SerializedObject(definition).FindProperty("abilitySet").objectReferenceValue == null;
                Add("공격 세트", set, catalog.Uses(set) > 1 || inherited ? "EAS_Tuning.asset" : null, inherited ? "상속된 공격 세트를 이 몬스터용으로 분리" : null);
                for (int i = 0; set != null && i < set.Count; i++)
                {
                    if (!session.edits.Any(e => e.target == "ability:" + i)) continue;
                    var ability = set.GetAbility(i); bool shared = catalog.Uses(ability) > 1;
                    remappedAbility |= shared; Add("공격 " + ability?.AbilityId, ability, shared ? "EAD_Tuning_" + i + ".asset" : null);
                }
            }
            if (session.edits.Any(e => e.target == "animation"))
            {
                Add("애니메이션 프로필", definition.AnimationProfile, "EAP_Tuning.asset", "이 몬스터의 실제 Controller 연결로 분리");
                bool overrides = MonsterTunerAnimationBindings.CanUseOverrides(MonsterTunerAnimationBindings.Read(definition.AnimationProfile), MonsterTunerAnimationBindings.Replacements(session));
                Add("Controller", definition.AnimationProfile?.RuntimeController, "AC_Tuning" + (overrides ? ".overrideController" : ".controller"), overrides ? "기존 상태를 유지한 클립 교체" : "같은 원본 클립의 역할별 교체를 분리");
            }
            if (remappedAbility || session.edits.Any(e => e.target.StartsWith("component:", StringComparison.Ordinal)))
                Add("Actor 프리팹", definition.ActorPrefab, catalog.Uses(definition.ActorPrefab) > 1 ? "PF_Tuning.prefab" : null,
                    remappedAbility ? "부착점 변경과 분리된 공격 참조 연결" : "기준점·판정·오라·예고·머즐 변경");
            return result;
            void Add(string label, Object source, string candidate, string reason = null)
            {
                int count = catalog.Uses(source);
                result.Add(new SaveTarget { Label = label, SourcePath = AssetDatabase.GetAssetPath(source), SharedCount = count, Created = candidate != null,
                    DestinationPath = candidate != null ? ProposedPath(folder, candidate) : AssetDatabase.GetAssetPath(source),
                    Reason = reason ?? (candidate != null ? "공유 자산을 이 몬스터용으로 분리" : "현재 자산의 변경 필드 저장") });
            }
        }
        private static string ProposedPath(string folder, string candidate)
            => AssetDatabase.IsValidFolder(folder) ? AssetDatabase.GenerateUniqueAssetPath(folder + "/" + candidate) : folder + "/" + candidate;
    }
}
