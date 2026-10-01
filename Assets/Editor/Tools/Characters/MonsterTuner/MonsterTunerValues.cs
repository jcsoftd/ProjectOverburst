using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.MonsterTuner
{
    [Serializable]
    internal sealed class MonsterTunerValue
    {
        public SerializedPropertyType type;
        public float number;
        public int integer;
        public bool boolean;
        public Vector3 vector;
        public string text;
        public float[] numbers;
        public MonsterTunerMuzzleValue[] muzzles;
        public string arrayKind;

        public static MonsterTunerValue Read(SerializedProperty property, GameObject actor = null)
        {
            if (property == null) throw new InvalidOperationException("편집 필드를 찾을 수 없습니다.");
            var value = new MonsterTunerValue { type = property.propertyType };
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                if (property.propertyPath == "muzzleOverrides")
                {
                    value.arrayKind = "muzzles";
                    value.muzzles = new MonsterTunerMuzzleValue[property.arraySize];
                    for (int i = 0; i < value.muzzles.Length; i++)
                    {
                        var entry = property.GetArrayElementAtIndex(i);
                        value.muzzles[i] = new MonsterTunerMuzzleValue { ability = Read(entry.FindPropertyRelative("ability"), actor),
                            socket = Read(entry.FindPropertyRelative("socket"), actor), offset = entry.FindPropertyRelative("localOffset").vector3Value };
                    }
                    return value;
                }
                value.arrayKind = "numbers"; value.numbers = new float[property.arraySize];
                for (int i = 0; i < value.numbers.Length; i++) value.numbers[i] = property.GetArrayElementAtIndex(i).floatValue;
                return value;
            }
            switch (value.type)
            {
                case SerializedPropertyType.Float: value.number = property.floatValue; break;
                case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: value.integer = property.intValue; break;
                case SerializedPropertyType.Boolean: value.boolean = property.boolValue; break;
                case SerializedPropertyType.Vector3: value.vector = property.vector3Value; break;
                case SerializedPropertyType.String: value.text = property.stringValue; break;
                case SerializedPropertyType.ObjectReference:
                    var obj = property.objectReferenceValue;
                    if (obj is Transform transform && actor != null && transform.IsChildOf(actor.transform))
                        value.text = "actor:" + MonsterTunerAddress.Path(actor.transform, transform);
                    else value.text = obj != null ? GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString() : string.Empty;
                    break;
                default: throw new InvalidOperationException("지원하지 않는 필드입니다: " + property.propertyPath);
            }
            return value;
        }

        public void Write(SerializedProperty property, GameObject actor = null)
        {
            if (property == null) throw new InvalidOperationException("원본 필드가 변경됐습니다.");
            if (arrayKind == "muzzles" || property.propertyPath == "muzzleOverrides")
            {
                property.arraySize = muzzles.Length;
                for (int i = 0; i < muzzles.Length; i++)
                {
                    var entry = property.GetArrayElementAtIndex(i);
                    muzzles[i].ability.Write(entry.FindPropertyRelative("ability"), actor);
                    muzzles[i].socket.Write(entry.FindPropertyRelative("socket"), actor);
                    entry.FindPropertyRelative("localOffset").vector3Value = muzzles[i].offset;
                }
                return;
            }
            if (arrayKind == "numbers" || (property.isArray && property.propertyType != SerializedPropertyType.String))
            {
                property.arraySize = numbers.Length;
                for (int i = 0; i < numbers.Length; i++) property.GetArrayElementAtIndex(i).floatValue = numbers[i];
                return;
            }
            switch (type)
            {
                case SerializedPropertyType.Float: property.floatValue = number; break;
                case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: property.intValue = integer; break;
                case SerializedPropertyType.Boolean: property.boolValue = boolean; break;
                case SerializedPropertyType.Vector3: property.vector3Value = vector; break;
                case SerializedPropertyType.String: property.stringValue = text ?? string.Empty; break;
                case SerializedPropertyType.ObjectReference: property.objectReferenceValue = Resolve(actor); break;
                default: throw new InvalidOperationException("지원하지 않는 저장 형식입니다.");
            }
        }
        public Object Resolve(GameObject actor = null)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (text.StartsWith("actor:", StringComparison.Ordinal))
                return actor != null ? MonsterTunerAddress.Find(actor.transform, text.Substring(6)) : null;
            return GlobalObjectId.TryParse(text, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null;
        }
        public string Display => arrayKind == "muzzles" ? muzzles.Length + "개 발사점" : arrayKind == "numbers" ? string.Join(", ", numbers) : type == SerializedPropertyType.Vector3
            ? vector.ToString("F3") : type == SerializedPropertyType.Boolean ? (boolean ? "켜짐" : "꺼짐")
            : type == SerializedPropertyType.Float ? number.ToString("0.###", CultureInfo.InvariantCulture)
            : type == SerializedPropertyType.ObjectReference ? (Resolve() != null ? Resolve().name : text)
            : type == SerializedPropertyType.String ? text : integer.ToString();
        public bool EqualsValue(MonsterTunerValue other)
        {
            if (other == null || type != other.type || (arrayKind ?? "") != (other.arrayKind ?? "")) return false;
            if (arrayKind == "muzzles")
            {
                if (muzzles.Length != other.muzzles.Length) return false;
                for (int i = 0; i < muzzles.Length; i++) if (!muzzles[i].ability.EqualsValue(other.muzzles[i].ability) || !muzzles[i].socket.EqualsValue(other.muzzles[i].socket) || (muzzles[i].offset - other.muzzles[i].offset).sqrMagnitude > 1e-12f) return false;
                return true;
            }
            if (arrayKind == "numbers")
            {
                if (numbers.Length != other.numbers.Length) return false;
                for (int i = 0; i < numbers.Length; i++) if (Mathf.Abs(numbers[i] - other.numbers[i]) > 1e-6f) return false;
                return true;
            }
            switch (type)
            {
                case SerializedPropertyType.Float: return Mathf.Abs(number - other.number) <= 1e-6f;
                case SerializedPropertyType.Vector3: return (vector - other.vector).sqrMagnitude <= 1e-12f;
                case SerializedPropertyType.Boolean: return boolean == other.boolean;
                case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: return integer == other.integer;
                case SerializedPropertyType.String: case SerializedPropertyType.ObjectReference: return (text ?? "") == (other.text ?? "");
                default: return false;
            }
        }
    }
    [Serializable]
    internal sealed class MonsterTunerMuzzleValue
    {
        public MonsterTunerValue ability, socket;
        public Vector3 offset;
    }

    [Serializable]
    internal sealed class MonsterTunerPatch
    {
        public string target, property, label;
        public MonsterTunerValue before, after;
        public string Key => target + "|" + property;
    }

    internal static class MonsterTunerAddress
    {
        public static string Path(Transform root, Transform child)
        {
            var parts = new List<string>();
            for (var t = child; t != null && t != root; t = t.parent) parts.Insert(0, t.GetSiblingIndex().ToString());
            if (child != root && !child.IsChildOf(root)) throw new InvalidOperationException("몬스터 밖의 뼈입니다.");
            return string.Join("/", parts);
        }
        public static Transform Find(Transform root, string path)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(path)) return root;
            foreach (string part in path.Split('/'))
            {
                if (!int.TryParse(part, out int index) || index < 0 || index >= root.childCount) return null;
                root = root.GetChild(index);
            }
            return root;
        }
        public static string Component(GameObject actor, Component component)
        {
            var all = component.GetComponents(component.GetType());
            return "component:" + Path(actor.transform, component.transform) + ":" + component.GetType().AssemblyQualifiedName
                + ":" + Array.IndexOf(all, component);
        }
        public static Object ResolveComponent(GameObject actor, string address)
        {
            if (actor == null || !address.StartsWith("component:", StringComparison.Ordinal)) return null;
            var parts = address.Split(':');
            var node = Find(actor.transform, parts[1]);
            var type = Type.GetType(parts[2]);
            if (node == null || type == null || !int.TryParse(parts[3], out int index)) return null;
            var all = node.GetComponents(type);
            return index >= 0 && index < all.Length ? all[index] : null;
        }
        public static string Names(Transform root, Transform child)
        {
            var parts = new List<string>();
            for (var t = child; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return parts.Count == 0 ? "Actor" : string.Join("/", parts);
        }
    }
}
