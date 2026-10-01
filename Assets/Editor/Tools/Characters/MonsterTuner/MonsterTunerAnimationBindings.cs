using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Overburst.EditorTools.MonsterTuner
{
    internal static class MonsterTunerAnimationBindings
    {
        internal sealed class Binding
        {
            public string Key, StatePath, Label;
            public int[] Children;
            public AnimatorState State;
            public AnimationClip Original, Actual;
            public string[] ProfileProperties;
        }
        public static AnimatorController BaseController(RuntimeAnimatorController controller)
        {
            var seen = new HashSet<RuntimeAnimatorController>();
            while (controller is AnimatorOverrideController overrides && seen.Add(controller)) controller = overrides.runtimeAnimatorController;
            return controller as AnimatorController;
        }
        public static AnimationClip Resolve(RuntimeAnimatorController controller, AnimationClip original)
        {
            if (controller is AnimatorOverrideController overrides)
            {
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); overrides.GetOverrides(pairs);
                var pair = pairs.Find(p => p.Key == original);
                return pair.Value != null ? pair.Value : Resolve(overrides.runtimeAnimatorController, original);
            }
            return original;
        }
        public static List<Binding> Read(EnemyAnimationProfile profile)
        {
            var result = ReadController(profile != null ? profile.RuntimeController : null);
            if (profile == null) return result;
            var serialized = new SerializedObject(profile);
            var properties = new List<string> { "idle", "walk", "run", "hit", "death", "parryCollapse", "stunnedLoop", "stunRecover" };
            for (int i = 0; i < profile.AttackClipCount; i++) properties.Add("attackClips.Array.data[" + i + "]");
            for (int i = 0; i < profile.OptionalClipCount; i++) properties.Add("optional.Array.data[" + i + "]");
            foreach (var binding in result)
            {
                binding.ProfileProperties = properties.Where(p => serialized.FindProperty(p) != null && serialized.FindProperty(p).objectReferenceValue == binding.Actual).ToArray();
                string lower = binding.StatePath.ToLowerInvariant();
                string state = lower.Split('.').Last();
                string primary = state == "idle" ? "idle" : state == "walk" ? "walk" : state == "run" ? "run"
                    : state == "death" ? "death" : IsHit(binding) ? "hit" : null;
                if (state == "locomotion" && binding.Children.Length == 1 && binding.State.motion is BlendTree locomotion)
                {
                    float threshold = locomotion.children[binding.Children[0]].threshold;
                    primary = Mathf.Approximately(threshold, 0f) ? "idle" : Mathf.Approximately(threshold, 1f) ? "walk" : Mathf.Approximately(threshold, 2f) ? "run" : null;
                }
                if (primary != null && binding.ProfileProperties.Contains(primary)) binding.ProfileProperties = new[] { primary };
                else if (state.StartsWith("attack_") && int.TryParse(state.Substring(7), out int attackIndex) && attackIndex > 0)
                {
                    string property = "attackClips.Array.data[" + (attackIndex - 1) + "]";
                    if (serialized.FindProperty(property) != null) binding.ProfileProperties = new[] { property };
                }
                else binding.ProfileProperties = binding.ProfileProperties.Where(p => p.StartsWith("optional.", StringComparison.Ordinal) || p.StartsWith("parry", StringComparison.Ordinal) || p == "stunnedLoop" || p == "stunRecover").ToArray();
                if (lower.EndsWith("parry_collapse")) binding.Label = "패링 · 무너짐";
                else if (lower.EndsWith("stunned_loop")) binding.Label = "패링 · 기절 반복";
                else if (lower.EndsWith("stun_recover")) binding.Label = "패링 · 회복";
                else if (IsHit(binding)) binding.Label = HitLabel(binding);
                else if (binding.ProfileProperties.Contains("idle")) binding.Label = "대기";
                else if (binding.ProfileProperties.Contains("walk")) binding.Label = "걷기";
                else if (binding.ProfileProperties.Contains("run")) binding.Label = "달리기";
                else if (binding.ProfileProperties.Contains("hit")) binding.Label = "피격";
                else if (binding.ProfileProperties.Contains("death")) binding.Label = "사망";
                else binding.Label = binding.StatePath.Split('.').Last();
            }
            return result;
        }
        public static bool IsHit(Binding binding)
        {
            string state = binding.StatePath.Split('.').Last();
            return state.Equals("Get_hit", StringComparison.OrdinalIgnoreCase) || state.StartsWith("Hit", StringComparison.OrdinalIgnoreCase);
        }
        private static string HitLabel(Binding binding)
        {
            if (binding.Children.Length == 0) return "피격";
            if (binding.Children.Length == 1 && binding.State.motion is BlendTree tree && tree.blendParameter == "HitX" && tree.blendParameterY == "HitZ")
            {
                Vector2 direction = tree.children[binding.Children[0]].position;
                if (direction.sqrMagnitude > .001f)
                    return "피격 · " + (Mathf.Abs(direction.y) >= Mathf.Abs(direction.x) ? direction.y > 0f ? "정면" : "후면" : direction.x < 0f ? "좌측" : "우측");
            }
            return "피격 · 변형 " + string.Join("/", binding.Children);
        }
        public static List<Binding> ReadController(RuntimeAnimatorController runtime)
        {
            var controller = BaseController(runtime); var result = new List<Binding>();
            if (controller == null) return result;
            foreach (var layer in controller.layers) Walk(layer.stateMachine, layer.name);
            return result;
            void Walk(AnimatorStateMachine machine, string path)
            {
                foreach (var child in machine.states) WalkMotion(child.state, child.state.motion, path + "." + child.state.name, new int[0]);
                foreach (var child in machine.stateMachines) Walk(child.stateMachine, path + "." + child.stateMachine.name);
            }
            void WalkMotion(AnimatorState state, Motion motion, string path, int[] children)
            {
                if (motion is AnimationClip clip)
                    result.Add(new Binding { Key = path + "|" + string.Join("/", children), StatePath = path, Children = children,
                        State = state, Original = clip, Actual = Resolve(runtime, clip), Label = path, ProfileProperties = new string[0] });
                else if (motion is BlendTree tree)
                    for (int i = 0; i < tree.children.Length; i++) WalkMotion(state, tree.children[i].motion, path, children.Concat(new[] { i }).ToArray());
            }
        }
        public static AnimationClip WorkingClip(MonsterTunerSession session, Binding binding)
        {
            var edit = session.edits.Find(e => e.target == "animation" && e.property == "motion:" + binding.Key);
            return edit != null ? edit.after.Resolve() as AnimationClip : binding.Actual;
        }
        public static AnimationClip AttackClip(MonsterTunerSession session, EnemyAbilityDefinition ability, int index)
        {
            string trigger = ability != null ? ability.AnimatorTrigger : string.Empty;
            string state = trigger.StartsWith("Attack", StringComparison.Ordinal) && trigger.Length > 6 ? "Attack_" + trigger.Substring(6) : trigger;
            var binding = Read(session.Definition.AnimationProfile).FirstOrDefault(b => b.StatePath.EndsWith("." + state, StringComparison.Ordinal));
            return binding != null ? WorkingClip(session, binding) : session.Definition.AnimationProfile?.GetAttackClip(index);
        }
        public static Dictionary<string, AnimationClip> Replacements(MonsterTunerSession session)
        {
            var result = new Dictionary<string, AnimationClip>(); var bindings = Read(session.Definition.AnimationProfile);
            foreach (var edit in session.edits.Where(e => e.target == "animation"))
            {
                var replacement = edit.after.Resolve() as AnimationClip;
                if (edit.property.StartsWith("motion:", StringComparison.Ordinal)) result[edit.property.Substring(7)] = replacement;
                else foreach (var binding in bindings.Where(b => b.ProfileProperties.Contains(edit.property))) result[binding.Key] = replacement;
            }
            return result;
        }
        public static string Compatibility(EnemyDefinition definition, AnimationClip clip)
        {
            if (clip == null) return "모션 슬롯을 비울 수 없습니다.";
            if (clip.legacy) return "Legacy 클립은 현재 Animator에 연결할 수 없습니다.";
            var animator = definition.ActorPrefab != null ? definition.ActorPrefab.Animator : null;
            if (animator == null) return "모델 Animator가 없습니다.";
            if (clip.isHumanMotion && (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)) return "Humanoid 클립과 모델 Avatar가 호환되지 않습니다.";
            if (!clip.isHumanMotion)
            {
                var missing = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform) && !string.IsNullOrEmpty(b.path) && animator.transform.Find(b.path) == null).Select(b => b.path).Distinct().Take(3).ToArray();
                if (missing.Length > 0) return "모델에 없는 뼈 경로: " + string.Join(", ", missing);
            }
            return string.Empty;
        }
        public static bool CanUseOverrides(List<Binding> bindings, Dictionary<string, AnimationClip> replacements)
        {
            foreach (var binding in bindings.Where(b => replacements.ContainsKey(b.Key)))
                if (bindings.Any(b => b.Key != binding.Key && b.Original == binding.Original
                    && (!replacements.TryGetValue(b.Key, out var clip) || clip != replacements[binding.Key]))) return false;
            return true;
        }
        public static RuntimeAnimatorController SaveController(MonsterTunerSession session, string path)
        {
            var profile = session.Definition.AnimationProfile; var bindings = Read(profile); var changes = Replacements(session);
            if (CanUseOverrides(bindings, changes))
            {
                var overrides = new AnimatorOverrideController(BaseController(profile.RuntimeController));
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); overrides.GetOverrides(pairs);
                for (int i = 0; i < pairs.Count; i++)
                {
                    var binding = bindings.FirstOrDefault(b => b.Original == pairs[i].Key);
                    var clip = binding != null && changes.TryGetValue(binding.Key, out var replacement) ? replacement : Resolve(profile.RuntimeController, pairs[i].Key);
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip);
                }
                overrides.ApplyOverrides(pairs); AssetDatabase.CreateAsset(overrides, path); return overrides;
            }
            string original = AssetDatabase.GetAssetPath(BaseController(profile.RuntimeController));
            if (!AssetDatabase.CopyAsset(original, path)) throw new InvalidOperationException("Controller 사본을 만들지 못했습니다.");
            var copied = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            foreach (var binding in ReadController(copied))
            {
                var source = bindings.Find(b => b.Key == binding.Key);
                var clip = changes.TryGetValue(binding.Key, out var replacement) ? replacement : source?.Actual;
                if (clip == null) continue;
                if (binding.Children.Length == 0) { binding.State.motion = clip; EditorUtility.SetDirty(binding.State); }
                else
                {
                    BlendTree tree = OwnTree(binding.State.motion as BlendTree); binding.State.motion = tree;
                    for (int depth = 0; depth < binding.Children.Length; depth++)
                    {
                        var children = tree.children; int index = binding.Children[depth];
                        if (depth == binding.Children.Length - 1) children[index].motion = clip;
                        else { var child = OwnTree(children[index].motion as BlendTree); children[index].motion = child; tree.children = children; EditorUtility.SetDirty(tree); tree = child; continue; }
                        tree.children = children; EditorUtility.SetDirty(tree);
                    }
                    EditorUtility.SetDirty(binding.State);
                }
            }
            EditorUtility.SetDirty(copied); AssetDatabase.SaveAssetIfDirty(copied); return copied;
            BlendTree OwnTree(BlendTree tree)
            {
                if (tree == null) throw new InvalidOperationException("BlendTree 연결이 변경됐습니다.");
                if (AssetDatabase.GetAssetPath(tree) == path) return tree;
                var clone = UnityEngine.Object.Instantiate(tree); clone.name = tree.name; AssetDatabase.AddObjectToAsset(clone, copied); return clone;
            }
        }
        public static void SynchronizeProfile(MonsterTunerSession session, EnemyAnimationProfile destination, RuntimeAnimatorController controller)
        {
            var serialized = new SerializedObject(destination); var originalBindings = Read(session.Definition.AnimationProfile);
            var finalBindings = ReadController(controller);
            foreach (var binding in originalBindings)
            {
                var actual = finalBindings.Find(b => b.Key == binding.Key)?.Actual;
                if (actual == null) continue;
                foreach (string property in binding.ProfileProperties)
                {
                    // A shared clip used by separate roles cannot silently rewrite another profile slot.
                    if (originalBindings.Any(b => b.Key != binding.Key && b.ProfileProperties.Contains(property) && finalBindings.Find(f => f.Key == b.Key)?.Actual != actual)) continue;
                    var field = serialized.FindProperty(property); if (field != null) field.objectReferenceValue = actual;
                }
                string role = binding.StatePath.EndsWith(".Parry_Collapse", StringComparison.Ordinal) ? "parryCollapse"
                    : binding.StatePath.EndsWith(".Stunned_Loop", StringComparison.Ordinal) ? "stunnedLoop"
                    : binding.StatePath.EndsWith(".Stun_Recover", StringComparison.Ordinal) ? "stunRecover" : null;
                if (role != null) serialized.FindProperty(role).objectReferenceValue = actual;
            }
            serialized.FindProperty("runtimeController").objectReferenceValue = controller;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
