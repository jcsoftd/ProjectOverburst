using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

// Own copies only: supplier textures, curves and original graphs remain intact.
public static class BloodSweepVariationBuilder
{
    public const string Folder = BloodHitGraphBuilder.Folder + "/SweepVariations";
    public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../개인파일/코덱스산출/CombatVfx/20261002_BloodSweepApply"));
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly string[] ProfilePaths = {
        "Assets/ProjectOverburst/Resources/Combat/Blood/SpiderBrood.asset",
        "Assets/ProjectOverburst/Resources/Combat/Blood/VenomBrood.asset",
        "Assets/ProjectOverburst/Resources/Combat/Blood/PrimalHunt.asset",
        "Assets/ProjectOverburst/Resources/Combat/Blood/CavernMutants.asset",
        "Assets/ProjectOverburst/Resources/Enemies/Themes/Blood/Rake.asset" };
    static readonly string[,] Palettes = {
        { "40584A", "202F27", "718578" }, { "4D5F2C", "28341B", "84935C" },
        { "665033", "33251B", "968267" }, { "564153", "2C202D", "8B7489" },
        { "532325", "2B1114", "906163" } };
    [Serializable] public sealed class ProfileState
    { public string path; public Color main, secondary, highlight; public float specular, size; }
    [Serializable] public sealed class VariantState
    { public string label, guid; public Vector3 localEuler; public float sizeMultiplier; }
    [Serializable] public sealed class Baseline
    { public ProfileState[] profiles; public bool enabled; public VariantState[] variations; }
    static string BaselinePath => Path.Combine(Output, "appearance-before.json");

    [MenuItem("OVERBURST/Combat/Blood Review/개선안 적용")]
    public static void ApplyMenu() => Debug.Log(Apply());
    [MenuItem("OVERBURST/Combat/Blood Review/적용 전 모습으로 원복")]
    public static void RestoreMenu() => Debug.Log(Restore());
    [MenuItem("OVERBURST/Combat/Blood Review/개선안 적용", true)]
    [MenuItem("OVERBURST/Combat/Blood Review/적용 전 모습으로 원복", true)]
    static bool CanEdit() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
    static void RequireIdle()
    { if (!CanEdit()) throw new InvalidOperationException("Play 종료 후 혈흔 모습을 변경하세요."); }
    static BloodHitCatalog Catalog => Resources.Load<BloodHitCatalog>(BloodHitCatalog.ResourcePath)
        ?? throw new InvalidOperationException("Blood catalog missing");
    static BloodHitProfile Profile(string path) => AssetDatabase.LoadAssetAtPath<BloodHitProfile>(path)
        ?? throw new InvalidOperationException("Blood profile missing: " + path);

    static void SaveBaseline(BloodHitCatalog catalog)
    {
        Directory.CreateDirectory(Output);
        if (File.Exists(BaselinePath)) return; // Never overwrite the actual pre-application appearance.
        var baseline = new Baseline { enabled = catalog.useSweepVariations,
            variations = catalog.sweepVariations?.Select(v => v == null ? null : new VariantState {
                label = v.label, guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(v.graph)),
                localEuler = v.localEuler, sizeMultiplier = v.sizeMultiplier }).ToArray(),
            profiles = ProfilePaths.Select(path => { var p = Profile(path); return new ProfileState {
                path = path, main = p.mainColor, secondary = p.secondaryColor, highlight = p.specularColor,
                specular = p.specular, size = p.size }; }).ToArray() };
        File.WriteAllText(BaselinePath, JsonUtility.ToJson(baseline, true));
    }
    static Color ColorHex(string value)
    { if (!ColorUtility.TryParseHtmlString("#" + value, out var color)) throw new Exception(value); return color; }
    static void Save(UnityEngine.Object asset)
    { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }

    public static string Apply()
    {
        RequireIdle(); var catalog = Catalog; SaveBaseline(catalog);
        try
        {
            var thin = Create("Thin", AssetDatabase.GetAssetPath(catalog.slash), new Vector3(1.35f, .65f, .48f), 1f);
            var fan = Create("Fan", "Assets/ThirdParty/06_VFX/HIVEMIND/RealisticBloodVFX/URP/RealisticBlood/VFXGraphs/VFX_SplashLow.vfx",
                new Vector3(.75f, .85f, 1.6f), 1f);
            var heavy = Create("Heavy", AssetDatabase.GetAssetPath(catalog.burst), new Vector3(.55f, .7f, .6f), 1.6f);
            catalog.sweepVariations = new[] {
                new BloodHitCatalog.SweepVariation { label = "길고 얇은 분출", graph = thin, sizeMultiplier = .82f },
                new BloodHitCatalog.SweepVariation { label = "부채꼴 분출", graph = fan, sizeMultiplier = .9f },
                new BloodHitCatalog.SweepVariation { label = "굵고 짧은 분출", graph = heavy, sizeMultiplier = 1.22f } };
            catalog.useSweepVariations = true; Save(catalog);
            for (int i = 0; i < ProfilePaths.Length; i++)
            {
                var profile = Profile(ProfilePaths[i]);
                profile.mainColor = ColorHex(Palettes[i, 0]); profile.secondaryColor = ColorHex(Palettes[i, 1]);
                profile.specularColor = ColorHex(Palettes[i, 2]); profile.specular = i == 4 ? .18f : .3f;
                Save(profile);
            }
            return "가로베기 혈흔 3형태와 몬스터 5색 적용. Blood Review 메뉴에서 원복 가능합니다.";
        }
        catch { Restore(); throw; }
    }
    public static string Restore()
    {
        RequireIdle();
        if (!File.Exists(BaselinePath)) throw new InvalidOperationException("적용 전 혈흔 백업이 없습니다.");
        var baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(BaselinePath));
        foreach (var saved in baseline.profiles)
        {
            var p = Profile(saved.path); p.mainColor = saved.main; p.secondaryColor = saved.secondary;
            p.specularColor = saved.highlight; p.specular = saved.specular; Save(p);
        }
        var catalog = Catalog; catalog.useSweepVariations = baseline.enabled;
        catalog.sweepVariations = baseline.variations?.Select(v => v == null ? null : new BloodHitCatalog.SweepVariation {
            label = v.label, graph = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(AssetDatabase.GUIDToAssetPath(v.guid)),
            localEuler = v.localEuler, sizeMultiplier = v.sizeMultiplier }).ToArray(); Save(catalog);
        return "혈흔 형태·색상을 적용 전 모습으로 원복했습니다. 기존 크기 설정은 유지됩니다.";
    }

    static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object[] Items(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    static object Get(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value);
    static object Call(object value, string name, params object[] args)
        => value.GetType().GetMethods(Flags).First(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static void Value(object slot, object value) => slot.GetType().GetProperty("value", Flags).SetValue(slot, value);
    static void MultiplyInput(object graph, object input, object factor, Type type, object parameter = null)
    {
        var upstream = Items(Get(input, "LinkedSlots")); var original = Get(input, "value");
        var op = ScriptableObject.CreateInstance(FindType("UnityEditor.VFX.Operator.Multiply"));
        Call(op, "SetOperandType", 0, type); Call(op, "SetOperandType", 1, type);
        Call(graph, "AddChild", op, -1, true);
        var slots = Items(Get(op, "inputSlots"));
        if (upstream.Length == 1) Call(slots[0], "Link", upstream[0], true);
        else if (upstream.Length == 0) Value(slots[0], original);
        else throw new InvalidOperationException("Unexpected multiple slot links");
        if (parameter != null) Call(slots[1], "Link", parameter, true); else Value(slots[1], factor);
        Call(input, "Link", Items(Get(op, "outputSlots"))[0], true);
    }
    static VisualEffectAsset Create(string name, string original, Vector3 velocity, float gravity)
    {
        string path = Folder + "/VFX_BloodSweep" + name + ".vfx";
        if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path)
            ?? throw new InvalidOperationException("Existing graph failed to load: " + path);
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        if (!AssetDatabase.CopyAsset(original, path)) throw new InvalidOperationException("Graph copy failed: " + original);
        try
        {
            var resourceType = FindType("UnityEditor.VFX.VisualEffectResource");
            var resource = resourceType.GetMethod("GetResourceAtPath", Flags).Invoke(null, new object[] { path });
            var extension = FindType("UnityEditor.VFX.VisualEffectResourceExtensions");
            var graph = extension.GetMethod("GetOrCreateGraph", Flags).Invoke(null, new[] { resource });
            var contents = (UnityEngine.Object[])Call(resource, "GetContents");
            var parameters = contents.Where(x => x != null && x.GetType().Name == "VFXParameter").ToArray();
            var hitSize = parameters.FirstOrDefault(p => (string)Call(p, "GetSettingValue", "m_ExposedName") == "HitSize");
            bool addSize = hitSize == null;
            if (addSize)
            {
                hitSize = ScriptableObject.CreateInstance(FindType("UnityEditor.VFX.VFXParameter"));
                Call(hitSize, "Init", typeof(float)); Call(hitSize, "SetSettingValue", "m_ExposedName", "HitSize");
                Call(hitSize, "SetSettingValue", "m_Exposed", true); Call(graph, "AddChild", hitSize, -1, true);
                Value(Items(Get(hitSize, "outputSlots"))[0], 1f);
            }
            var color = parameters.First(p => (string)Call(p, "GetSettingValue", "m_ExposedName") == "BloodColorMain");
            int changed = 0;
            foreach (var block in contents.Where(x => x != null && x.GetType().Name == "SetAttribute"))
            {
                string attribute = (string)block.GetType().GetField("attribute", Flags).GetValue(block);
                var input = Items(Get(block, "inputSlots"))[0];
                var parent = Call(block, "GetParent");
                if (attribute == "velocity" && parent != null && parent.GetType().Name.Contains("Initialize"))
                { MultiplyInput(graph, input, velocity, typeof(Vector3)); changed++; }
                if (attribute == "size" && addSize)
                    MultiplyInput(graph, input, 1f, typeof(float), Items(Get(hitSize, "outputSlots"))[0]);
                // Native flipbook cores include a fixed red color. Route those through the species palette.
                if (attribute == "color" && Items(Get(input, "LinkedSlots")).Length == 0)
                    Call(input, "Link", Items(Get(color, "outputSlots"))[0], true);
            }
            if (changed == 0) throw new InvalidOperationException("No initial velocity blocks: " + name);
            foreach (var block in contents.Where(x => x != null && x.GetType().Name == "Gravity"))
            {
                var input = Items(Get(block, "inputSlots"))[0];
                if (Items(Get(input, "LinkedSlots")).Length == 0 && Get(input, "value") is Vector3 force)
                    Value(input, force * gravity);
            }
            extension.GetMethod("WriteAssetWithSubAssets", Flags).Invoke(null, new[] { resource });
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path)
                ?? throw new InvalidOperationException("Graph import failed: " + name);
        }
        catch { AssetDatabase.DeleteAsset(path); throw; }
    }
}
