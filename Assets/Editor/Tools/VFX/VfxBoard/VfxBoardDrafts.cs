using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.Vfx
{
    /// <summary>적용 전 교체안 하나. prefabGuid가 비어 있고 clear면 슬롯을 비운다.</summary>
    [Serializable]
    internal sealed class VfxDraft
    {
        public string slotKey;
        public string prefabGuid;
        public bool clear;
    }

    [Serializable]
    internal sealed class VfxDraftList
    {
        public List<VfxDraft> items = new List<VfxDraft>();
    }

    internal sealed class VfxDraftCheck
    {
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public bool Blocked => Errors.Count > 0;
    }

    internal sealed class VfxApplyReport
    {
        public readonly List<string> Applied = new List<string>();
        public readonly List<string> Failed = new List<string>();
        public readonly List<VfxDraft> Reverse = new List<VfxDraft>();
        public readonly HashSet<string> AppliedKeys = new HashSet<string>();
    }

    internal static class VfxDraftOperations
    {
        internal static GameObject LoadPrefab(VfxDraft draft)
        {
            if (draft == null || draft.clear || string.IsNullOrEmpty(draft.prefabGuid))
                return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(draft.prefabGuid));
        }

        internal static bool IsPrefabAssetRoot(GameObject gameObject) =>
            gameObject != null && EditorUtility.IsPersistent(gameObject)
            && PrefabUtility.IsPartOfPrefabAsset(gameObject) && gameObject.transform.parent == null;

        /// <summary>슬롯이 지금 가리키는 프리팹 에셋 GUID. 모듈이면 원본 모듈 프리팹 기준이다.</summary>
        internal static string CurrentGuid(VfxSlot slot)
        {
            GameObject current = slot?.Current as GameObject;
            return current != null && IsPrefabAssetRoot(current)
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(current))
                : null;
        }

        internal static string Describe(VfxSlot slot) => slot == null ? "(없는 슬롯)" : slot.Breadcrumb + " › " + slot.Label;

        internal static string DraftName(VfxDraft draft)
        {
            if (draft == null) return string.Empty;
            if (draft.clear) return "비우기";
            GameObject prefab = LoadPrefab(draft);
            return prefab != null ? prefab.name : "찾을 수 없는 프리팹";
        }

        internal static VfxDraftCheck Check(VfxSlot slot, VfxDraft draft)
        {
            var check = new VfxDraftCheck();
            if (slot == null)
            {
                check.Errors.Add("이 슬롯을 더 이상 찾을 수 없습니다. 새로고침 뒤 다시 확인하세요.");
                return check;
            }

            if (!slot.CanSwap)
            {
                check.Errors.Add(slot.ReadOnlyReason);
                return check;
            }

            if (draft.clear)
            {
                if (slot.Binding == VfxSlotBinding.Module)
                    check.Errors.Add("래퍼 안 모듈 슬롯은 비울 수 없습니다.");
                else
                    check.Warnings.Add("적용하면 이 슬롯이 비워져 게임에서 이 효과가 나오지 않습니다.");
                return check;
            }

            GameObject prefab = LoadPrefab(draft);
            if (prefab == null)
            {
                check.Errors.Add("교체안 프리팹을 찾을 수 없습니다. 삭제되었거나 옮겨졌을 수 있습니다.");
                return check;
            }

            if (!IsPrefabAssetRoot(prefab))
            {
                check.Errors.Add("프로젝트의 프리팹 에셋(루트)만 넣을 수 있습니다.");
                return check;
            }

            if (slot.IsPrefabOwner && AssetDatabase.GetAssetPath(prefab) == slot.OwnerPath)
                check.Errors.Add("슬롯을 가진 프리팹 자신은 넣을 수 없습니다.");

            if (slot.ParticleField && prefab.GetComponent<ParticleSystem>() == null)
                check.Errors.Add("이 슬롯은 ParticleSystem 참조라서 루트에 ParticleSystem이 있는 프리팹만 넣을 수 있습니다.");

            if (!VfxSlotCatalog.HasPlayableVisual(prefab))
                check.Warnings.Add("재생할 ParticleSystem·VisualEffect·Trail이 없는 프리팹입니다.");

            GameObject reference = slot.Binding == VfxSlotBinding.Module ? slot.ModuleObject : slot.Current as GameObject;
            if (reference != null)
            {
                var present = new HashSet<string>(RootScripts(prefab));
                List<string> missing = RootScripts(reference).Where(name => !present.Contains(name)).ToList();
                if (missing.Count > 0)
                    check.Warnings.Add("현재 프리팹 루트의 스크립트(" + string.Join(", ", missing)
                        + ")가 새 프리팹에 없습니다. 게임 코드가 이 스크립트를 찾으면 연출이 빠지거나 달라질 수 있습니다.");
            }

            if (slot.Binding == VfxSlotBinding.Module)
            {
                check.Warnings.Add("래퍼 프리팹 안의 모듈을 새 프리팹 인스턴스로 바꿉니다. 이름·위치·회전·크기·활성 상태만 옮기고, 기존 모듈에 준 다른 오버라이드는 사라집니다.");
                try
                {
                    List<string> added = PrefabUtility.GetAddedComponents(slot.ModuleObject)
                        .Select(c => c.instanceComponent != null ? c.instanceComponent.GetType().Name : "?").ToList();
                    if (added.Count > 0)
                        check.Errors.Add("기존 모듈에 따로 붙인 컴포넌트(" + string.Join(", ", added)
                            + ")가 있어 교체하면 사라집니다. 래퍼 프리팹에서 직접 정리하세요.");
                }
                catch (Exception)
                {
                    // 에셋 상태에서 조회할 수 없으면 적용 단계에서 다시 검사한다.
                }
            }

            return check;
        }

        private static IEnumerable<string> RootScripts(GameObject gameObject) =>
            gameObject.GetComponents<MonoBehaviour>().Where(m => m != null).Select(m => m.GetType().Name).Distinct();

        // ---------- 적용 ----------

        internal static VfxApplyReport Apply(IReadOnlyList<(VfxSlot slot, VfxDraft draft)> changes)
        {
            var report = new VfxApplyReport();
            foreach (IGrouping<string, (VfxSlot slot, VfxDraft draft)> group in changes.GroupBy(c => c.slot.OwnerPath))
            {
                List<(VfxSlot slot, VfxDraft draft)> list = group.ToList();
                if (list[0].slot.IsPrefabOwner)
                    ApplyToPrefab(group.Key, list, report);
                else
                    ApplyToAsset(group.Key, list, report);
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        private static void ApplyToAsset(string path, List<(VfxSlot slot, VfxDraft draft)> list, VfxApplyReport report)
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
            {
                foreach (var change in list)
                    report.Failed.Add(Describe(change.slot) + ": 소유 에셋을 찾지 못했습니다.");
                return;
            }

            var serialized = new SerializedObject(asset);
            var done = new List<(VfxSlot slot, VfxDraft draft, Object previous)>();
            foreach (var (slot, draft) in list)
            {
                SerializedProperty property = serialized.FindProperty(slot.PropertyPath);
                Object value = ResolveValue(slot, draft);
                if (property == null)
                {
                    report.Failed.Add(Describe(slot) + ": 필드 " + slot.PropertyPath + "를 찾지 못했습니다.");
                    continue;
                }

                if (value == null && !draft.clear)
                {
                    report.Failed.Add(Describe(slot) + ": 교체안 프리팹을 불러오지 못했습니다.");
                    continue;
                }

                Object previous = property.objectReferenceValue;
                property.objectReferenceValue = value;
                done.Add((slot, draft, previous));
            }

            if (done.Count == 0)
                return;

            serialized.ApplyModifiedProperties(); // SO 변경은 Undo 기록이 남는다
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            foreach (var (slot, draft, previous) in done)
                Record(report, slot, draft, previous);
        }

        private static void ApplyToPrefab(string path, List<(VfxSlot slot, VfxDraft draft)> list, VfxApplyReport report)
        {
            GameObject root;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
            }
            catch (Exception error)
            {
                foreach (var change in list)
                    report.Failed.Add(Describe(change.slot) + ": 프리팹을 열지 못했습니다(" + error.Message + ").");
                return;
            }

            var done = new List<(VfxSlot slot, VfxDraft draft, Object previous)>();
            try
            {
                foreach (var (slot, draft) in list)
                {
                    try
                    {
                        Component component = VfxSlotCatalog.FindComponent(root, slot.ObjectPath, slot.ComponentType)
                            ?? throw new InvalidOperationException("스크립트 " + slot.ComponentType + "를 찾지 못했습니다.");
                        var serialized = new SerializedObject(component);
                        SerializedProperty property = serialized.FindProperty(slot.PropertyPath)
                            ?? throw new InvalidOperationException("필드 " + slot.PropertyPath + "를 찾지 못했습니다.");

                        Object previous;
                        if (slot.Binding == VfxSlotBinding.Module)
                        {
                            previous = SwapModule(root, component, serialized, property, slot, LoadPrefab(draft));
                        }
                        else
                        {
                            Object value = ResolveValue(slot, draft);
                            if (value == null && !draft.clear)
                                throw new InvalidOperationException("교체안 프리팹을 불러오지 못했습니다.");
                            previous = property.objectReferenceValue;
                            property.objectReferenceValue = value;
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                        }

                        done.Add((slot, draft, previous));
                    }
                    catch (Exception error)
                    {
                        report.Failed.Add(Describe(slot) + ": " + error.Message);
                    }
                }

                if (done.Count > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                    if (!saved)
                    {
                        foreach (var change in done)
                            report.Failed.Add(Describe(change.slot) + ": 프리팹 저장에 실패했습니다.");
                        done.Clear();
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            foreach (var (slot, draft, previous) in done)
                Record(report, slot, draft, previous);
        }

        /// <summary>래퍼 안의 중첩 모듈을 새 프리팹 인스턴스로 바꾸고, 바뀌기 전 원본 모듈 프리팹을 돌려준다.</summary>
        private static Object SwapModule(GameObject root, Component owner, SerializedObject serialized,
            SerializedProperty property, VfxSlot slot, GameObject prefab)
        {
            if (prefab == null)
                throw new InvalidOperationException("교체안 프리팹이 없습니다.");

            Object current = property.objectReferenceValue;
            GameObject module = current as GameObject;
            if (module == null && current is Component component)
                module = component.gameObject;
            if (module == null)
                throw new InvalidOperationException("현재 모듈을 찾지 못했습니다.");
            if (AnimationUtility.CalculateTransformPath(module.transform, root.transform) != slot.ModulePath)
                throw new InvalidOperationException("래퍼 구조가 바뀌었습니다. 새로고침 뒤 다시 시도하세요.");
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(module))
                throw new InvalidOperationException("중첩 프리팹 모듈이 아니라서 교체할 수 없습니다.");

            var addedComponents = PrefabUtility.GetAddedComponents(module);
            if (addedComponents.Count > 0)
                throw new InvalidOperationException("기존 모듈에 따로 붙인 컴포넌트가 있어 교체하면 사라집니다.");
            if (PrefabUtility.GetAddedGameObjects(module).Count > 0)
                throw new InvalidOperationException("기존 모듈에 따로 추가한 자식 오브젝트가 있어 교체하면 사라집니다.");

            string conflict = FindOutsideReference(root, module, owner, property.propertyPath);
            if (conflict != null)
                throw new InvalidOperationException("다른 컴포넌트가 기존 모듈 내부를 참조합니다(" + conflict + ").");

            Object previousSource = PrefabUtility.GetCorrespondingObjectFromSource(module);
            Transform parent = module.transform.parent;
            int sibling = module.transform.GetSiblingIndex();
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("새 모듈 인스턴스를 만들지 못했습니다.");

            instance.name = module.name;
            instance.transform.localPosition = module.transform.localPosition;
            instance.transform.localRotation = module.transform.localRotation;
            instance.transform.localScale = module.transform.localScale;
            instance.transform.SetSiblingIndex(sibling);
            instance.SetActive(module.activeSelf);

            property.objectReferenceValue = slot.ParticleField ? (Object)instance.GetComponent<ParticleSystem>() : instance;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Object.DestroyImmediate(module);
            return previousSource;
        }

        private static string FindOutsideReference(GameObject root, GameObject module, Component owner, string ownerProperty)
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform || component.transform.IsChildOf(module.transform))
                    continue;

                SerializedProperty iterator = new SerializedObject(component).GetIterator();
                bool enter = true;
                while (iterator.Next(enter))
                {
                    enter = iterator.propertyType == SerializedPropertyType.Generic;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (iterator.propertyPath.StartsWith("m_", StringComparison.Ordinal)) continue;
                    if (component == owner && iterator.propertyPath == ownerProperty) continue;

                    Object value = iterator.objectReferenceValue;
                    Transform target = value is GameObject go ? go.transform : value is Component c ? c.transform : null;
                    if (target != null && target.IsChildOf(module.transform))
                        return component.GetType().Name + "." + iterator.propertyPath;
                }
            }

            return null;
        }

        private static Object ResolveValue(VfxSlot slot, VfxDraft draft)
        {
            if (draft.clear)
                return null;
            GameObject prefab = LoadPrefab(draft);
            if (prefab == null)
                return null;
            return slot.ParticleField ? (Object)prefab.GetComponent<ParticleSystem>() : prefab;
        }

        private static void Record(VfxApplyReport report, VfxSlot slot, VfxDraft draft, Object previous)
        {
            report.AppliedKeys.Add(slot.Key);
            report.Applied.Add(Describe(slot) + ": " + (previous != null ? previous.name : "비어 있음") + " → " + DraftName(draft));

            GameObject previousObject = previous as GameObject;
            if (previousObject == null && previous is Component component)
                previousObject = component.gameObject;
            if (previous == null)
            {
                report.Reverse.Add(new VfxDraft { slotKey = slot.Key, clear = true });
            }
            else if (previousObject != null && IsPrefabAssetRoot(previousObject))
            {
                report.Reverse.Add(new VfxDraft
                {
                    slotKey = slot.Key,
                    prefabGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(previousObject))
                });
            }
        }
    }
}
