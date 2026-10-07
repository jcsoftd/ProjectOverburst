using System;
using UnityEngine;

namespace Overburst.Appearance
{
    [Serializable] public sealed class AppearanceTextureOverride
    {
        public Texture original;
        public Texture highQuality;
    }
    public sealed class AppearancePreviewQuality:ScriptableObject
    {
        public const string ResourcePath="UI/Appearance/AppearancePreviewQuality";
        public Material contactShadowMaterial;
        public AppearanceTextureOverride[] textures=Array.Empty<AppearanceTextureOverride>();
        public Texture Resolve(Texture texture)
        {
            foreach(var pair in textures)if(pair.original==texture)return pair.highQuality;
            return texture;
        }
    }
}
