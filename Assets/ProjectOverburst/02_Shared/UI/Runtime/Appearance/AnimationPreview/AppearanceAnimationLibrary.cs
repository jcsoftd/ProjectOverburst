#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Overburst.Appearance.AnimationPreview
{
    [Serializable] public sealed class AppearanceAnimationOption
    {
        public string id,displayName,category,assetGuid,assetPath;
        public long localFileId;
        public AnimationClip clip;
        public Sprite thumbnail;
    }
    [CreateAssetMenu(menuName="OVERBURST/Appearance/Optional Animation Library")]
    public sealed class AppearanceAnimationLibrary:ScriptableObject
    {
        public int thumbnailCaptureVersion;
        public AppearanceAnimationOption[] animations=Array.Empty<AppearanceAnimationOption>();
    }
}
#endif
