using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.Vfx
{
    internal enum VfxSlotBinding
    {
        Field,    // 에셋/프리팹 필드가 외부 프리팹을 직접 참조
        Module,   // 래퍼 프리팹 안에 중첩된 모듈 인스턴스를 참조
        Resource  // 코드가 Resources 경로로 직접 불러옴
    }

    internal enum VfxSlotState { Assigned, Empty, Missing }

    /// <summary>게임에서 VFX 하나가 연결되는 자리. 수집 시점의 읽기 전용 스냅샷이다.</summary>
    internal sealed class VfxSlot
    {
        public string Key;
        public string[] Category = Array.Empty<string>();
        public string Label;
        public string Note;
        public VfxSlotBinding Binding;
        public string OwnerPath;
        public string ComponentType;   // 프리팹 소유자일 때 컴포넌트 타입 이름, SO면 null
        public string ObjectPath;      // 프리팹 안 컴포넌트 위치("" = 루트)
        public string PropertyPath;
        public string ResourcePath;
        public bool ParticleField;
        public Object Current;
        public Object PreviewSource;
        public GameObject ModuleObject;
        public string ModulePath;
        public Quaternion PreviewRotation = Quaternion.identity;
        public Vector3? PreviewScale;
        public VfxSlotState State;
        public string ReadOnlyReason;
        public bool Unregistered;

        private string searchText;

        public bool CanSwap => ReadOnlyReason == null;
        public bool IsPrefabOwner => ComponentType != null;
        public string Breadcrumb => string.Join(" › ", Category);

        public string CurrentName => Current != null
            ? Current.name
            : State == VfxSlotState.Missing ? "참조 깨짐" : "비어 있음";

        public string SearchText => searchText ??= string.Join(" ",
            string.Join(" ", Category), Label, CurrentName,
            System.IO.Path.GetFileNameWithoutExtension(OwnerPath ?? string.Empty),
            PropertyPath, ResourcePath).ToLowerInvariant();
    }

    /// <summary>미분류 검사에서 찾은 슬롯의 위치. 창이 다시 열려도 같은 슬롯을 다시 읽는다.</summary>
    [Serializable]
    internal sealed class VfxExtraSlotSpec
    {
        public string ownerPath;
        public string componentType;
        public string objectPath;
        public string propertyPath;
    }

    internal static class VfxSlotCatalog
    {
        internal const string ProjectRoot = "Assets/ProjectOverburst";
        private const string ElementCombat = "원소 전투";
        private const string Reactions = "원소 반응";
        private const string WeaponAttacks = "무기 공격";
        private const string CommonCombat = "공용 전투";
        private const string Monsters = "몬스터";
        private const string Character = "캐릭터";
        private const string Items = "아이템·상호작용";
        internal const string Unregistered = "미분류";

        private static readonly string[] ElementKeys = { "fire", "ice", "electric", "dark", "light" };
        private static readonly string[] ElementNames = { "불", "얼음", "번개", "어둠", "빛" };

        private static readonly string[] GroupOrder =
        {
            ElementCombat, Reactions, WeaponAttacks, CommonCombat, Monsters, Character, Items, Unregistered,
            "불", "얼음", "번개", "어둠", "빛", "무속성",
            "타격", "강공격", "상태이상", "버프",
            "빙결", "쇄빙",
            "공용", "대검",
            "치명타",
            "공격 예고", "투사체", "발먼지", "패링",
            "레벨업", "픽업 등급", "회복 픽업", "피해 바닥"
        };

        // 목록에서 뺀 소유자. 미분류 검사도 건너뛴다.
        // MeleeWeaponElementFx: 대검은 GreatswordElementFxProfile이 우선해 무기별 복사본은 쓰이지 않는다(2026-09-30 대검 값 비움).
        // CombatVfx: 치명타 섬광·혈흔만 남은 연결 컴포넌트(임시 피격·사망 필드는 2026-09-30 제거).
        // GreatswordElementFxProfile·MeleeElementAttackVfxCatalog: 검기·궤적과 공격 슬래시는
        //   사용자 결정(2026-09-30)으로 관리하지 않는다. 공격 슬래시 에셋·코드는 같은 날 제거했다.
        // Blood*: 사용자 결정으로 혈흔은 관리 대상이 아니다.
        private static readonly string[] SkippedComponents =
        {
            "MeleeWeaponElementFx", "CombatVfx",
            "GreatswordElementFxProfile", "MeleeElementAttackVfxCatalog"
        };

        private static readonly Regex VfxFieldName = new Regex(
            "vfx|fx|effect|particle|aura|slash|trail|impact|burst|flash|glow|spark|explo",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static int GroupRank(string name)
        {
            int index = Array.IndexOf(GroupOrder, name);
            return index < 0 ? 1000 : index;
        }

        internal static List<VfxSlot> Collect()
        {
            var slots = new List<VfxSlot>();
            var keys = new HashSet<string>();
            void Add(VfxSlot slot)
            {
                if (slot != null && keys.Add(slot.Key))
                    slots.Add(slot);
            }

            CollectElementCombat(Add);
            CollectReactions(Add);
            CollectWeaponAttacks(Add);
            CollectCommon(Add);
            CollectMonsters(Add);
            CollectCharacter(Add);
            CollectItems(Add);
            return slots;
        }

        private static void CollectElementCombat(Action<VfxSlot> add)
        {
            Object hitCatalog = FindFirst("MeleeElementHitVfxCatalog");
            if (hitCatalog != null)
            {
                SerializedProperty pools = new SerializedObject(hitCatalog).FindProperty("runtimePools");
                for (int i = 0; pools != null && i < pools.arraySize; i++)
                {
                    int element = pools.GetArrayElementAtIndex(i).FindPropertyRelative("element")?.intValue ?? 0;
                    add(AssetField(hitCatalog, "runtimePools.Array.data[" + i + "].prefab", "타격",
                        "원소 무기가 적중할 때 대상 몸에서 재생하는 원소별 런타임 풀 프리팹입니다.",
                        ElementCombat, ElementName(element), "타격"));
                }


            }

            // 대검 검기·궤적(GreatswordElementFxProfile)은 사용자 결정(2026-09-30)으로 관리 대상에서 뺀다.
            // 공격 슬래시 에셋·코드는 같은 날 제거했다.

            (string field, int element, string label, string note)[] heavyFields =
            {
                ("fireImpact", 0, "강공격 착지 폭발", ""),
                ("fireChainExplosion", 0, "연쇄폭발", "연소 대상에서 번지는 폭발입니다. 비어 있으면 착지 폭발을 씁니다."),
                ("iceImpact", 1, "강공격 착지", ""),
                ("iceShatter", 1, "쇄빙", ""),
                ("electricImpact", 2, "강공격 착지", ""),
                ("electricDirectHit", 2, "직격 번개", "착지 원에 직접 맞은 적마다 발밑에서 재생합니다."),
                ("electricChainLink", 2, "연쇄번개 연결", ""),
                ("darkBarrageSlam", 3, "탄막 내려찍기", "잠식 탄막 강공 착지 1회입니다."),
                ("darkBarrageProjectile", 3, "탄막 투사체", "탄 1발입니다. 투사체와 트레일이 모두 비어 있으면 임시 구체와 트레일을 사용합니다."),
                ("darkBarrageTrail", 3, "탄막 트레일", "탄 뒤를 따라가는 꼬리입니다."),
                ("darkBarrageHit", 3, "탄막 명중 폭발", "탄이 맞을 때마다 재생합니다."),
                ("lightTripleImpact", 4, "3연타 충격", "에너지 100 초과일 때입니다."),
                ("lightDoubleImpact", 4, "2연타 충격", "에너지 100 이하일 때입니다.")
            };
            List<Object> heavyDefinitions = FindAll("MeleeHeavyAttackDefinition");
            foreach (Object heavy in heavyDefinitions)
            {
                string suffix = heavyDefinitions.Count > 1 ? " · " + heavy.name : string.Empty;
                foreach (var (field, element, label, note) in heavyFields)
                    add(AssetField(heavy, "elementVfx." + field, label + suffix, note,
                        ElementCombat, ElementNames[element], "강공격"));
            }

            GameObject aura = Resources.Load<GameObject>("Combat/VFX/PF_VFX_MeleeElementStatusAura");
            if (aura != null)
            {
                (string field, int element, string label)[] auraFields =
                {
                    ("burningAura", 0, "화상"),
                    ("shockedAura", 2, "감전"), ("corrodedAura", 3, "잠식")
                };
                foreach (var (field, element, label) in auraFields)
                    add(PrefabField(aura, "MeleeElementStatusAuraPresentation", field, label + " 상태 오라",
                        "상태이상이 걸린 적의 몸에 붙습니다.", ElementCombat, ElementNames[element], "상태이상"));
            }

            add(ResourceSlot("Combat/VFX/VFX_Light_RadianceAura", "광휘 오라",
                "광휘 중첩(에너지 101~200) 동안 플레이어 몸에 켜집니다.", ElementCombat, "빛", "버프"));
            add(ResourceSlot("Combat/VFX/VFX_Light_RadianceCrown", "광휘 왕관",
                "광휘 중첩 동안 머리 위에 켜집니다.", ElementCombat, "빛", "버프"));
        }

        private static void CollectReactions(Action<VfxSlot> add)
        {
            Object catalog = FindFirst("ElementalReactionVfxCatalog");
            if (catalog == null)
                return;

            SerializedProperty entries = new SerializedObject(catalog).FindProperty("entries");
            var rows = new List<(int type, int slot, int index, string id)>();
            for (int i = 0; entries != null && i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                int type = entry.FindPropertyRelative("reactionType")?.intValue ?? 0;
                int slot = entry.FindPropertyRelative("slotType")?.intValue ?? 0;
                if (type != (int)ElementalReactionType.Freeze || slot != (int)ElementalReactionVfxSlotType.Loop) continue;
                rows.Add((type,
                    entry.FindPropertyRelative("slotType")?.intValue ?? 0, i,
                    entry.FindPropertyRelative("id")?.stringValue ?? string.Empty));
            }

            foreach (var row in rows.OrderBy(r => r.type).ThenBy(r => r.slot).ThenBy(r => r.index))
            {
                bool duplicate = rows.Count(r => r.type == row.type && r.slot == row.slot) > 1;
                string label = ReactionSlotName(row.slot) + (duplicate ? " · " + row.id : string.Empty);
                add(AssetField(catalog, "entries.Array.data[" + row.index + "].prefab", label,
                    "냉기 누적으로 빙결된 적에게 재생합니다.", ElementCombat, "얼음", "빙결"));
            }
        }

        private static void CollectWeaponAttacks(Action<VfxSlot> add)
        {
            foreach (Object definition in FindAll("MeleeAttackVfxDefinition"))
            {
                string path = AssetDatabase.GetAssetPath(definition);
                string weapon = path.Contains("/WP02_Greatsword/") ? "대검" : "공용";
                add(AssetField(definition, "neutralPrefab", AttackLabel(definition.name),
                    "공격 동작 VFX 정의: " + definition.name, WeaponAttacks, weapon));
            }
        }

        private static void CollectCommon(Action<VfxSlot> add)
        {
            add(ResourceSlot("Combat/VFX/VFX_CritHit", "치명타 섬광",
                "현재 자동 재생은 꺼져 있습니다. 추후 연결을 위해 프리팹과 이 칸을 유지합니다.", CommonCombat, "치명타"));
        }

        private static void CollectMonsters(Action<VfxSlot> add)
        {
            Object telegraph = FindFirst("EnemyTelegraphVisualLibrary");
            if (telegraph != null)
            {
                add(AssetField(telegraph, "proceduralIndicator", "공통 판정창 · 부채꼴/원형/도넛/사각형", "", Monsters, "공격 예고"));
                add(AssetField(telegraph, "cone", "부채꼴 원본 불꽃", "", Monsters, "공격 예고"));
                add(AssetField(telegraph, "nova", "원형 공격 예고", "", Monsters, "공격 예고"));
                add(AssetField(telegraph, "rectangle", "직사각형 공격 예고", "", Monsters, "공격 예고"));
                add(AssetField(telegraph, "groundImpact", "강공격 착지 먼지", "", Monsters, "강공격"));
                add(AssetField(telegraph, "parrySuccess", "패링 성공 플레어",
                    "성공 중심에 무지개 렌즈 플레어를 한 번 재생합니다.", Monsters, "패링"));
            }

            Object projectiles = FindFirst("EnemyProjectileVfxCatalog");
            if (projectiles != null)
                add(AssetField(projectiles, "projectile", "투사체",
                    "침·산성 투사체입니다. 쏘는 몬스터의 혈흔 색으로 물듭니다.", Monsters, "투사체"));

            add(ResourceSlot("Enemies/FootDust/PF_EnemyFootDust_Light", "발먼지 (가벼움)", "", Monsters, "발먼지"));
            add(ResourceSlot("Enemies/FootDust/PF_EnemyFootDust_Standard", "발먼지 (보통)", "", Monsters, "발먼지"));
            add(ResourceSlot("Enemies/FootDust/PF_EnemyFootDust_Heavy", "발먼지 (무거움)", "", Monsters, "발먼지"));
        }

        private static void CollectCharacter(Action<VfxSlot> add)
        {
            add(ResourceSlot("VFX/PF_PlayerLevelUpVfx", "레벨업", "레벨업할 때 플레이어에게 재생합니다.", Character, "레벨업"));
        }

        private static void CollectItems(Action<VfxSlot> add)
        {
            Object gradeSet = FindFirst("PickupGradeVfxSet");
            if (gradeSet != null)
            {
                SerializedProperty entries = new SerializedObject(gradeSet).FindProperty("entries");
                for (int i = 0; entries != null && i < entries.arraySize; i++)
                {
                    int grade = entries.GetArrayElementAtIndex(i).FindPropertyRelative("grade")?.intValue ?? 0;
                    add(AssetField(gradeSet, "entries.Array.data[" + i + "].prefab", GradeName(grade) + " 등급",
                        "바닥에 떨어진 아이템의 등급 표시입니다.", Items, "픽업 등급"));
                }

                add(AssetField(gradeSet, "fallbackPrefab", "기본 (등급 미지정)",
                    "등급 항목이 비어 있을 때 씁니다.", Items, "픽업 등급"));
            }


        }

        // ---------- 슬롯 생성 ----------

        private static VfxSlot AssetField(Object owner, string propertyPath, string label, string note, params string[] category)
        {
            if (owner == null)
                return null;

            SerializedProperty property = new SerializedObject(owner).FindProperty(propertyPath);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                return null;

            string ownerPath = AssetDatabase.GetAssetPath(owner);
            var slot = new VfxSlot
            {
                Key = FieldKey(ownerPath, null, null, propertyPath),
                Category = category,
                Label = label,
                Note = note,
                Binding = VfxSlotBinding.Field,
                OwnerPath = ownerPath,
                PropertyPath = propertyPath
            };
            ReadReference(slot, property, null);
            return slot;
        }

        private static VfxSlot PrefabField(GameObject prefabRoot, string componentType, string propertyPath,
            string label, string note, params string[] category)
        {
            Component component = FindComponent(prefabRoot, null, componentType);
            return component == null ? null : ComponentField(prefabRoot, component, propertyPath, label, note, category);
        }

        private static VfxSlot ComponentField(GameObject prefabRoot, Component component, string propertyPath,
            string label, string note, string[] category)
        {
            SerializedProperty property = new SerializedObject(component).FindProperty(propertyPath);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                return null;

            string ownerPath = AssetDatabase.GetAssetPath(prefabRoot);
            string objectPath = AnimationUtility.CalculateTransformPath(component.transform, prefabRoot.transform);
            string type = component.GetType().Name;
            var slot = new VfxSlot
            {
                Key = FieldKey(ownerPath, type, objectPath, propertyPath),
                Category = category,
                Label = label,
                Note = note,
                Binding = VfxSlotBinding.Field,
                OwnerPath = ownerPath,
                ComponentType = type,
                ObjectPath = objectPath,
                PropertyPath = propertyPath
            };
            ReadReference(slot, property, prefabRoot);
            return slot;
        }

        private static VfxSlot ResourceSlot(string resourcePath, string label, string note, params string[] category)
        {
            Object asset = Resources.Load<GameObject>(resourcePath);
            if (asset == null)
                asset = Resources.Load(resourcePath);

            return new VfxSlot
            {
                Key = "res|" + resourcePath,
                Category = category,
                Label = label,
                Note = note,
                Binding = VfxSlotBinding.Resource,
                ResourcePath = resourcePath,
                OwnerPath = asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty,
                PropertyPath = string.Empty,
                Current = asset,
                PreviewSource = asset,
                State = asset != null ? VfxSlotState.Assigned : VfxSlotState.Missing,
                ReadOnlyReason = "게임 코드가 Resources 경로 \"" + resourcePath
                    + "\"로 직접 불러오는 효과입니다. 바꾸려면 그 프리팹 자체를 수정하세요."
            };
        }

        private static void ReadReference(VfxSlot slot, SerializedProperty property, GameObject prefabRoot)
        {
            slot.ParticleField = property.type.IndexOf("ParticleSystem", StringComparison.Ordinal) >= 0;
            Object value = property.objectReferenceValue;
            if (value == null)
            {
                slot.State = property.objectReferenceInstanceIDValue != 0 ? VfxSlotState.Missing : VfxSlotState.Empty;
                return;
            }

            slot.State = VfxSlotState.Assigned;
            GameObject gameObject = value as GameObject;
            if (gameObject == null && value is Component component)
                gameObject = component.gameObject;

            bool insideOwner = gameObject != null && prefabRoot != null && gameObject != prefabRoot
                && gameObject.transform.IsChildOf(prefabRoot.transform);
            if (!insideOwner)
            {
                slot.Current = gameObject != null ? gameObject : value;
                slot.PreviewSource = slot.Current;
                return;
            }

            slot.Binding = VfxSlotBinding.Module;
            slot.ModuleObject = gameObject;
            slot.ModulePath = AnimationUtility.CalculateTransformPath(gameObject.transform, prefabRoot.transform);
            slot.PreviewSource = gameObject; // 래퍼 안 오버라이드까지 포함한 실제 모양
            slot.PreviewRotation = Quaternion.Inverse(prefabRoot.transform.rotation) * gameObject.transform.rotation;
            slot.PreviewScale = gameObject.transform.localScale;

            bool nested = PrefabUtility.IsAnyPrefabInstanceRoot(gameObject);
            Object source = nested ? PrefabUtility.GetCorrespondingObjectFromSource(gameObject) : null;
            slot.Current = source != null ? source : gameObject;
            if (!nested)
                slot.ReadOnlyReason = "래퍼 프리팹 안에 풀어서 넣은 오브젝트라 통째 교체를 지원하지 않습니다. 래퍼 프리팹을 직접 수정하세요.";
        }

        // ---------- 미분류 검사 ----------

        internal static List<VfxExtraSlotSpec> DeepScan(ISet<string> knownKeys, out bool cancelled)
        {
            cancelled = false;
            var specs = new List<VfxExtraSlotSpec>();
            var vfxCache = new Dictionary<Object, bool>();
            var seen = new HashSet<string>(knownKeys);

            string[] assetGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { ProjectRoot });
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { ProjectRoot });
            int total = Mathf.Max(1, assetGuids.Length + prefabGuids.Length);
            int done = 0;

            try
            {
                foreach (string guid in assetGuids)
                {
                    if (Progress(++done, total, out cancelled)) return specs;
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset is ScriptableObject && !IsSkippedType(asset.GetType().Name))
                        ScanObject(asset, null, path, null, null, vfxCache, seen, specs);
                }

                foreach (string guid in prefabGuids)
                {
                    if (Progress(++done, total, out cancelled)) return specs;
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null) continue;

                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        Type type = component.GetType();
                        if ((type.Namespace ?? string.Empty).StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                        if (IsSkippedType(type.Name)) continue;
                        string objectPath = AnimationUtility.CalculateTransformPath(component.transform, root.transform);
                        ScanObject(component, root, path, type.Name, objectPath, vfxCache, seen, specs);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return specs;
        }

        private static bool Progress(int done, int total, out bool cancelled)
        {
            cancelled = done % 12 == 0 && EditorUtility.DisplayCancelableProgressBar(
                "VFX 관리 보드", "미분류 VFX 참조 검사 중… (" + done + "/" + total + ")", done / (float)total);
            return cancelled;
        }

        private static bool IsSkippedType(string typeName) =>
            SkippedComponents.Contains(typeName) || typeName.IndexOf("Blood", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void ScanObject(Object target, GameObject prefabRoot, string ownerPath, string componentType,
            string objectPath, Dictionary<Object, bool> vfxCache, HashSet<string> seen, List<VfxExtraSlotSpec> specs)
        {
            SerializedProperty iterator = new SerializedObject(target).GetIterator();
            bool enter = true;
            while (iterator.Next(enter))
            {
                enter = iterator.propertyType == SerializedPropertyType.Generic;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                string type = iterator.type;
                if (type.IndexOf("GameObject", StringComparison.Ordinal) < 0
                    && type.IndexOf("ParticleSystem", StringComparison.Ordinal) < 0
                    && type.IndexOf("VisualEffectAsset", StringComparison.Ordinal) < 0) continue;
                if (iterator.propertyPath.StartsWith("m_", StringComparison.Ordinal)) continue;

                if (IsEmptyOptionalField(target, iterator.propertyPath)) continue;
                Object value = iterator.objectReferenceValue;
                if (value == null)
                {
                    if (!VfxFieldName.IsMatch(iterator.propertyPath)) continue;
                }
                else
                {
                    if (!IsVfx(value, vfxCache)) continue;
                    if (value.name.IndexOf("Blood", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    GameObject referenced = value as GameObject;
                    if (referenced == null && value is Component component) referenced = component.gameObject;
                    bool internalWiring = referenced != null && prefabRoot != null
                        && referenced.transform.IsChildOf(prefabRoot.transform)
                        && (referenced == prefabRoot || !PrefabUtility.IsAnyPrefabInstanceRoot(referenced));
                    if (internalWiring) continue; // 자기 프리팹 안의 파티클 연결은 슬롯이 아니다
                }

                string key = FieldKey(ownerPath, componentType, objectPath, iterator.propertyPath);
                if (!seen.Add(key)) continue;
                specs.Add(new VfxExtraSlotSpec
                {
                    ownerPath = ownerPath,
                    componentType = componentType,
                    objectPath = objectPath,
                    propertyPath = iterator.propertyPath
                });
            }
        }

        private static bool IsEmptyOptionalField(Object target, string field)
        {
            var data = new SerializedObject(target);
            var value = data.FindProperty(field);
            if (value == null) return true; // A removed field in a saved scan.
            if (value.objectReferenceValue != null) return false;
            if (target is CombatImpactFeel && field.EndsWith(".criticalFlash", StringComparison.Ordinal))
            {
                string surfaceField = field.Substring(0, field.Length - "criticalFlash".Length) + "surface";
                return data.FindProperty(surfaceField)?.intValue == (int)CombatImpactSurface.Ground;
            }
            return target is OverburstFeelEmitter && field == "particleSystem"
                && data.FindProperty("uiImage")?.objectReferenceValue != null;
        }

        internal static VfxSlot CreateExtraSlot(VfxExtraSlotSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.ownerPath))
                return null;
            if (spec.ownerPath.EndsWith("/ElementalReactionVfxCatalog.asset", StringComparison.Ordinal))
                return null;

            string field = ObjectNames.NicifyVariableName(spec.propertyPath.Replace(".Array.data", string.Empty));
            string ownerName = System.IO.Path.GetFileNameWithoutExtension(spec.ownerPath);
            const string note = "게임 코드 연결을 확인하지 않은 참조입니다. 실제로 쓰이는지 직접 확인하세요.";
            VfxSlot slot;
            if (string.IsNullOrEmpty(spec.componentType))
            {
                Object owner = AssetDatabase.LoadMainAssetAtPath(spec.ownerPath);
                slot = AssetField(owner, spec.propertyPath, ownerName + " · " + field, note,
                    Unregistered, owner != null ? owner.GetType().Name : "에셋");
            }
            else
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ownerPath);
                Component component = FindComponent(root, spec.objectPath, spec.componentType);
                if (component != null && IsEmptyOptionalField(component, spec.propertyPath)) return null;
                slot = component == null ? null : ComponentField(root, component, spec.propertyPath,
                    ownerName + " · " + field, note, new[] { Unregistered, spec.componentType });
            }

            if (slot != null)
                slot.Unregistered = true;
            return slot;
        }

        private static bool IsVfx(Object value, Dictionary<Object, bool> cache)
        {
            if (cache.TryGetValue(value, out bool cached))
                return cached;

            bool result = value is VisualEffectAsset;
            if (!result)
            {
                GameObject gameObject = value as GameObject;
                if (gameObject == null && value is Component component)
                    gameObject = component.gameObject;
                result = gameObject != null && HasPlayableVisual(gameObject);
            }

            cache[value] = result;
            return result;
        }

        // ---------- 공용 도우미 ----------

        internal static bool HasPlayableVisual(GameObject gameObject) =>
            gameObject.GetComponentInChildren<ParticleSystem>(true) != null
            || gameObject.GetComponentInChildren<VisualEffect>(true) != null
            || gameObject.GetComponentInChildren<TrailRenderer>(true) != null;

        internal static string FieldKey(string ownerPath, string componentType, string objectPath, string propertyPath) =>
            AssetDatabase.AssetPathToGUID(ownerPath) + "|" + (componentType ?? string.Empty) + "|"
            + (objectPath ?? string.Empty) + "|" + propertyPath;

        internal static Component FindComponent(GameObject root, string objectPath, string typeName)
        {
            if (root == null)
                return null;

            if (objectPath == null)
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    if (component != null && component.GetType().Name == typeName)
                        return component;
                return null;
            }

            Transform target = objectPath.Length == 0 ? root.transform : root.transform.Find(objectPath);
            if (target == null)
                return null;

            foreach (Component component in target.GetComponents<Component>())
                if (component != null && component.GetType().Name == typeName)
                    return component;
            return null;
        }

        private static Object ReadObject(Object owner, string propertyPath) =>
            new SerializedObject(owner).FindProperty(propertyPath)?.objectReferenceValue;

        private static Object FindFirst(string typeName) => FindAll(typeName).FirstOrDefault();

        private static List<Object> FindAll(string typeName)
        {
            var result = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeName, new[] { ProjectRoot }))
            {
                Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.GetType().Name == typeName)
                    result.Add(asset);
            }

            result.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            return result;
        }

        private static string ElementName(int weaponElement)
        {
            switch (weaponElement)
            {
                case 0: return "무속성";
                case 1: return "불";
                case 3: return "얼음";
                case 4: return "번개";
                case 8: return "어둠";
                case 9: return "빛";
                default: return "기타 원소 " + weaponElement;
            }
        }

        private static string ReactionName(int reactionType)
        {
            switch (reactionType)
            {
                case 1: return "증기";
                case 2: return "균열";
                case 3: return "플라즈마";
                case 4: return "빙결";
                case 5: return "쇄빙";
                case 6: return "연쇄감전";
                case 7: return "냉전하";
                default: return "반응 " + reactionType;
            }
        }

        private static string ReactionSlotName(int slotType)
        {
            switch (slotType)
            {
                case 0: return "시작";
                case 1: return "유지";
                case 2: return "발동";
                case 3: return "연결";
                case 4: return "종료";
                default: return "슬롯 " + slotType;
            }
        }

        private static string GradeName(int grade)
        {
            string[] names = { "일반", "고급", "희귀", "영웅", "전설", "유물", "신화", "저주" };
            return grade >= 0 && grade < names.Length ? names[grade] : "등급 " + grade;
        }

        private static string AttackLabel(string assetName)
        {
            (string token, string label)[] map =
            {
                ("VerticalRising", "올려 베기"), ("VerticalFalling", "내려 베기"), ("Horizontal", "가로 베기"),
                ("CircleShockwave", "원형 충격파"), ("HeavyShockwave", "강공격 충격파"),
                ("SectorShockwave", "부채꼴 충격파")
            };
            foreach (var (token, label) in map)
                if (assetName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return label;
            return assetName;
        }
    }
}
