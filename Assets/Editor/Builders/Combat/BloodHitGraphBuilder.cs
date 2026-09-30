using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

// Unity 17.3 VFX authoring API via reflection because its model types are internal.
// Original vendor graphs remain untouched; preserve their curves and texture outputs.
public static class BloodHitGraphBuilder
{
    public const string Folder = "Assets/ProjectOverburst/02_Shared/Combat/VFX/BloodGraphs";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type Type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(x => x != null);
    private static object[] Items(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    private static object Get(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value);
    private static object Call(object value, string name, params object[] args)
        => value.GetType().GetMethods(Flags).First(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    public static VisualEffectAsset Create(string shape)
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        string path = Folder + "/VFX_Blood" + shape + "Scaled.vfx";
        if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
        string original = "Assets/ThirdParty/06_VFX/HIVEMIND/RealisticBloodVFX/URP/RealisticBlood/VFXGraphs/VFX_" + shape + "Low.vfx";
        if (!AssetDatabase.CopyAsset(original, path)) throw new Exception("Copy graph " + shape);
        var resourceType = Type("UnityEditor.VFX.VisualEffectResource");
        var resource = resourceType.GetMethod("GetResourceAtPath", Flags).Invoke(null, new object[] { path });
        var extension = Type("UnityEditor.VFX.VisualEffectResourceExtensions");
        var graph = extension.GetMethod("GetOrCreateGraph", Flags).Invoke(null, new[] { resource });
        var contents = (UnityEngine.Object[])Call(resource, "GetContents");
        var parameter = ScriptableObject.CreateInstance(Type("UnityEditor.VFX.VFXParameter"));
        Call(parameter, "Init", typeof(float));
        Call(parameter, "SetSettingValue", "m_ExposedName", "HitSize");
        Call(parameter, "SetSettingValue", "m_Exposed", true);
        Call(graph, "AddChild", parameter, -1, true);
        var sizeOutput = Items(Get(parameter, "outputSlots"))[0];
        sizeOutput.GetType().GetProperty("value", Flags).SetValue(sizeOutput, 1f);
        int count = 0;
        foreach (var block in contents.Where(x => x != null && x.GetType().Name == "SetAttribute"))
        {
            if ((string)block.GetType().GetField("attribute", Flags).GetValue(block) != "size") continue;
            var input = Items(Get(block, "inputSlots"))[0];
            var upstream = Items(Get(input, "LinkedSlots"));
            if (upstream.Length != 1) throw new Exception("Expected authored size curve");
            var multiply = ScriptableObject.CreateInstance(Type("UnityEditor.VFX.Operator.Multiply"));
            Call(graph, "AddChild", multiply, -1, true);
            var inputs = Items(Get(multiply, "inputSlots"));
            Call(inputs[0], "Link", upstream[0], true);
            Call(inputs[1], "Link", sizeOutput, true);
            Call(input, "Link", Items(Get(multiply, "outputSlots"))[0], true);
            count++;
        }
        if (count == 0) throw new Exception("No size outputs");
        extension.GetMethod("WriteAssetWithSubAssets", Flags).Invoke(null, new[] { resource });
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
    }
    public static string Build()
    {
        var catalog = Resources.Load<BloodHitCatalog>(BloodHitCatalog.ResourcePath);
        catalog.slash = Create("Slash"); catalog.stab = Create("Stab"); catalog.burst = Create("Burst");
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        return "3 scaled graphs with exposed HitSize";
    }
}
