using System;
using System.IO;

namespace Overburst.Persistence
{
    [Serializable]
    public sealed class CharacterAppearanceSnapshot : IEquatable<CharacterAppearanceSnapshot>
    {
        public const int CurrentVersion = 1;
        public int dataVersion = CurrentVersion;
        public string bodyTypeId = "human.female";
        public string faceId = "p09.face.female.03";
        public string hairStyleId = "p09.hair.09";
        public string hairColorId = "p09.haircolor.01";
        public string skinColorId = "p09.skin.female.01";
        public string eyeColorId = "p09.eye.01";
        public string bodyShapeId = "p09.bodyshape.m";

        public CharacterAppearanceSnapshot Copy() => (CharacterAppearanceSnapshot)MemberwiseClone();
        public bool Equals(CharacterAppearanceSnapshot other) => other != null
            && dataVersion == other.dataVersion && bodyTypeId == other.bodyTypeId
            && faceId == other.faceId && hairStyleId == other.hairStyleId
            && hairColorId == other.hairColorId && skinColorId == other.skinColorId
            && eyeColorId == other.eyeColorId && bodyShapeId == other.bodyShapeId;
        public override bool Equals(object obj) => Equals(obj as CharacterAppearanceSnapshot);
        public override int GetHashCode() => HashCode.Combine(dataVersion, bodyTypeId, faceId,
            hairStyleId, hairColorId, skinColorId, eyeColorId, bodyShapeId);
    }

    public static class AccountAppearanceCommands
    {
        public static void Apply(AccountSnapshot candidate, CharacterAppearanceSnapshot expected,
            CharacterAppearanceSnapshot next)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (!(candidate.appearance?.Equals(expected) ?? expected == null))
                throw new InvalidOperationException("외모가 다른 곳에서 변경됐습니다. 다시 불러와 주세요.");
            if (candidate.appearance != null && candidate.appearance.dataVersion != CharacterAppearanceSnapshot.CurrentVersion)
                throw new InvalidDataException("현재 버전에서 변경할 수 없는 외모 데이터입니다.");
            Validate(next);
            candidate.appearance = next.Copy();
        }

        public static void Validate(CharacterAppearanceSnapshot value)
        {
            if (value == null || value.dataVersion != CharacterAppearanceSnapshot.CurrentVersion
                || value.bodyTypeId != "human.female" || string.IsNullOrWhiteSpace(value.faceId)
                || string.IsNullOrWhiteSpace(value.hairStyleId) || string.IsNullOrWhiteSpace(value.hairColorId)
                || string.IsNullOrWhiteSpace(value.skinColorId) || string.IsNullOrWhiteSpace(value.eyeColorId)
                || string.IsNullOrWhiteSpace(value.bodyShapeId))
                throw new InvalidDataException("외모 데이터가 올바르지 않습니다.");
        }
    }
}

