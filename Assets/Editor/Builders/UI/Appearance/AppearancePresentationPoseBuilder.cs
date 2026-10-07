using System;
using System.Linq;
using Overburst.Appearance;
using UnityEditor;
using UnityEngine;

public static partial class AppearanceCustomizationBuilder
{
    static AnimationClip PresentationIdle(AnimationClip original)
    {
        string path=Root+"/AppearancePresentationIdle.anim";
        var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(existing)return existing;
        var pose=UnityEngine.Object.Instantiate(original);pose.name="Appearance Presentation Idle";
        AnimationUtility.SetAnimationEvents(pose,Array.Empty<AnimationEvent>());
        var bindings=AnimationUtility.GetCurveBindings(pose);
        // Use the relaxed source arm on both sides, retaining breathing, feet and head motion.
        foreach(var binding in bindings.Where(b=>b.type==typeof(Animator)&&b.propertyName.StartsWith("Left ",StringComparison.Ordinal)&&(b.propertyName.Contains("Arm")||b.propertyName.Contains("Forearm")||b.propertyName.Contains("Shoulder")||b.propertyName.Contains("Hand"))))
        {
            float value=AnimationUtility.GetEditorCurve(pose,binding).Evaluate(.7f);
            var mirrored=binding;mirrored.propertyName="Right "+binding.propertyName.Substring(5);
            var curve=AnimationCurve.Constant(0,Mathf.Max(.1f,pose.length),value);
            AnimationUtility.SetEditorCurve(pose,binding,curve);AnimationUtility.SetEditorCurve(pose,mirrored,curve);
        }
        foreach(var binding in bindings.Where(b=>b.propertyName.StartsWith("LeftHandT",StringComparison.Ordinal)||b.propertyName.StartsWith("RightHandT",StringComparison.Ordinal)||b.propertyName.StartsWith("LeftHandQ",StringComparison.Ordinal)||b.propertyName.StartsWith("RightHandQ",StringComparison.Ordinal)))AnimationUtility.SetEditorCurve(pose,binding,null);
        AssetDatabase.CreateAsset(pose,path);return pose;
    }
}
