using System;
using System.Collections.Generic;
using System.IO;
using Overburst.Persistence;
using UnityEngine;

namespace Overburst.Appearance
{
    [Serializable] public sealed class AppearanceMeshOption
    {
        public string id, displayName, rendererName;
        public Sprite thumbnail;
    }

    [Serializable] public sealed class AppearanceColorOption
    {
        public string id, displayName;
        public Material material;
        public Material hair10Material;
        public Color swatch = Color.white;
        public Sprite icon;
    }

    [Serializable] public sealed class AppearanceBodyStyle
    {
        public string id, displayName;
        public Vector3 scale = Vector3.one;
    }

    [Serializable] public sealed class AppearanceEquipmentExample
    {
        public string id, displayName;
        public string[] rendererNames = Array.Empty<string>();
        public string[] baseBodyNames = Array.Empty<string>();
        public Sprite thumbnail;
    }

    [CreateAssetMenu(menuName = "OVERBURST/Appearance/Catalog")]
    public sealed class CharacterAppearanceCatalog : ScriptableObject
    {
        public const string ResourcePath = "UI/Appearance/AppearanceCatalog";
        public GameObject visualPrefab;
        public AnimationClip idleClip;
        public CharacterAppearanceSnapshot defaultAppearance = new CharacterAppearanceSnapshot();
        public AppearanceMeshOption[] faces = Array.Empty<AppearanceMeshOption>();
        public AppearanceMeshOption[] hairStyles = Array.Empty<AppearanceMeshOption>();
        public AppearanceColorOption[] hairColors = Array.Empty<AppearanceColorOption>();
        public AppearanceColorOption[] skinColors = Array.Empty<AppearanceColorOption>();
        public AppearanceColorOption[] eyeColors = Array.Empty<AppearanceColorOption>();
        public AppearanceBodyStyle[] bodyStyles = Array.Empty<AppearanceBodyStyle>();
        public AppearanceEquipmentExample[] equipmentExamples = Array.Empty<AppearanceEquipmentExample>();
        public string[] underwearBodyNames = {"Female_Body_Arm", "Female_Body_Chest", "Female_Body_Leg"};
        public string[] nudeBodyNames = {"Female_Body_Arm", "Female_Body_Nakid_Chest", "Female_Body_Nakid_Leg"};

        public void Validate(CharacterAppearanceSnapshot value)
        {
            AccountAppearanceCommands.Validate(value);
            Required(faces, value.faceId, x => x.id);
            Required(hairStyles, value.hairStyleId, x => x.id);
            Required(hairColors, value.hairColorId, x => x.id);
            Required(skinColors, value.skinColorId, x => x.id);
            Required(eyeColors, value.eyeColorId, x => x.id);
            Required(bodyStyles, value.bodyShapeId, x => x.id);
        }

        public CharacterAppearanceSnapshot ResolveForDisplay(CharacterAppearanceSnapshot saved, out bool usedFallback)
        {
            Validate(defaultAppearance);
            var result = saved?.Copy() ?? defaultAppearance.Copy();
            usedFallback = false;
            if (saved != null && saved.dataVersion != CharacterAppearanceSnapshot.CurrentVersion)
                throw new InvalidDataException("현재 버전에서 변경할 수 없는 외모 데이터입니다.");
            if (result.bodyTypeId != "human.female") throw new InvalidDataException("현재는 여성 외모만 사용할 수 있습니다.");
            if (Find(faces, result.faceId, x=>x.id) == null) {result.faceId=defaultAppearance.faceId;usedFallback=true;}
            if (Find(hairStyles,result.hairStyleId,x=>x.id)==null) {result.hairStyleId=defaultAppearance.hairStyleId;usedFallback=true;}
            if (Find(hairColors,result.hairColorId,x=>x.id)==null) {result.hairColorId=defaultAppearance.hairColorId;usedFallback=true;}
            if (Find(skinColors,result.skinColorId,x=>x.id)==null) {result.skinColorId=defaultAppearance.skinColorId;usedFallback=true;}
            if (Find(eyeColors,result.eyeColorId,x=>x.id)==null) {result.eyeColorId=defaultAppearance.eyeColorId;usedFallback=true;}
            if (Find(bodyStyles,result.bodyShapeId,x=>x.id)==null) {result.bodyShapeId=defaultAppearance.bodyShapeId;usedFallback=true;}
            Validate(result);
            return result;
        }

        public static T Find<T>(T[] values, string id, Func<T,string> key) where T:class
        {
            if (values==null) return null;
            for (int i=0;i<values.Length;i++)
                if (values[i]!=null && string.Equals(key(values[i]),id,StringComparison.Ordinal)) return values[i];
            return null;
        }
        public static T Required<T>(T[] values, string id, Func<T,string> key) where T:class =>
            Find(values,id,key) ?? throw new InvalidDataException("사용할 수 없는 외모 항목: "+id);
    }
}

