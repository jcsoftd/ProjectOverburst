#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Overburst.Appearance
{
    // Keep authored body motion inside the pose on the destination humanoid avatar.
    internal sealed class AppearancePreviewRetargeting : IDisposable
    {
        public AnimationClip Clip { get; private set; }
        private AppearancePreviewRetargeting(AnimationClip source)
        {
            Clip=UnityEngine.Object.Instantiate(source);
            Clip.name=source.name+"_AppearancePose";Clip.hideFlags=HideFlags.DontSave;
            try
            {
                var settings=AnimationUtility.GetAnimationClipSettings(Clip);
                settings.loopBlendOrientation=true;settings.loopBlendPositionY=true;settings.loopBlendPositionXZ=true;
                AnimationUtility.SetAnimationClipSettings(Clip,settings);
                var bindings=AnimationUtility.GetCurveBindings(Clip);
                foreach(string property in new[]{"RootT.x","RootT.z"})
                {
                    var binding=bindings.Single(b=>b.propertyName==property);
                    var curve=AnimationUtility.GetEditorCurve(Clip,binding);
                    float origin=curve.Evaluate(0),velocity=Clip.length>0?(curve.Evaluate(Clip.length)-origin)/Clip.length:0;
                    var keys=curve.keys;
                    for(int i=0;i<keys.Length;i++)
                    {
                        keys[i].value-=origin+velocity*keys[i].time;
                        if(!float.IsInfinity(keys[i].inTangent))keys[i].inTangent-=velocity;
                        if(!float.IsInfinity(keys[i].outTangent))keys[i].outTangent-=velocity;
                    }
                    AnimationUtility.SetEditorCurve(Clip,binding,new AnimationCurve(keys));
                }
            }
            catch{Dispose();throw;}
        }
        public static AppearancePreviewRetargeting Create(AnimationClip source)
        {
            if(!source||!source.isHumanMotion||!AssetDatabase.GetAssetPath(source).Contains("/Kawaii_Animations_100/"))return null;
            return new AppearancePreviewRetargeting(source);
        }
        public void Dispose()
        {
            if(!Clip)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(Clip);else UnityEngine.Object.DestroyImmediate(Clip);
            Clip=null;
        }
    }
}
#endif
