using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>격리 계정 Play에서 다섯 아이템 계열의 광택 수명과 UI 재사용을 검사한다.</summary>
[InitializeOnLoad]
public static class ItemQualityIconPlayVerifier
{
    public const int FixtureVersion = 3;
    private const string Key = "Overburst.ItemQualityIconPlayVerifier.";
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../개인파일/코덱스산출/UI/20261003_StarQualityGradeRelative/Verification/Play"));
    private static string Account => Path.Combine(Output, "IsolatedAccount");
    private static IEnumerator work;
    private static GameObject root;
    private static int checks, frame;
    private static double deadline;
    private static readonly List<string> failures = new List<string>();
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");

    static ItemQualityIconPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetBool(Key + "pending", false) && SessionState.GetBool(Key + "entered", false)) EditorApplication.delayCall += Recover;
        if (SessionState.GetBool(Key + "return", false)) EditorApplication.update += ReturnAccount;
    }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return "DEFERRED: Editor is busy";
        if (SessionState.GetBool(Key + "pending", false) || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            return "DEFERRED: another account operation is active";
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Key + "pending", true);
        SessionState.SetBool(Key + "entered", false);
        SessionState.SetString(Key + "status", "RUNNING");
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManagerStartScene()));
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch { SessionState.SetString(Key + "status", "FAIL: Play start rejected"); ScheduleReturn(); throw; }
        return "STARTED: isolated icon Play";
    }

    private static UnityEngine.Object EditorSceneManagerStartScene() => UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene;

    private static void Recover()
    {
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Key + "entered", false) && OwnAccount())
        {
            SessionState.SetString(Key + "status", "CANCELLED: domain reload during verification");
            EditorApplication.ExitPlaymode();
        }
        else if (!EditorApplication.isPlayingOrWillChangePlaymode) ScheduleReturn();
    }

    private static bool OwnAccount() => string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);

    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode && OwnAccount())
        {
            SessionState.SetBool(Key + "entered", true);
            SessionState.SetBool(Key + "background", Application.runInBackground);
            Application.runInBackground = true;
            failures.Clear(); checks = 0; frame = -1; deadline = EditorApplication.timeSinceStartup + 45;
            work = Verify(); EditorApplication.update += Tick; Application.logMessageReceived += Log;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            if (root) Object.Destroy(root);
            root = null;
            if (SessionState.GetBool(Key + "entered", false)) Application.runInBackground = SessionState.GetBool(Key + "background", false);
            if (Status == "RUNNING") SessionState.SetString(Key + "status", "CANCELLED: Play interrupted");
        }
        else if (state == PlayModeStateChange.EnteredEditMode) ScheduleReturn();
    }

    private static void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            && (stack.Contains("ItemQualityIconEffect") || stack.Contains("ItemQualityIconPlayVerifier"))) failures.Add(message);
    }

    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!OwnAccount()) throw new InvalidOperationException("Isolated account ownership changed");
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Icon Play timed out");
            if (work.MoveNext()) return;
            Finish(failures.Count == 0 ? "PASS" : "FAIL");
        }
        catch (Exception error) { failures.Add(error.ToString()); Finish("FAIL"); }
    }

    private static void Check(bool pass, string message) { checks++; if (!pass) throw new InvalidOperationException(message); }
    private static void Finish(string status)
    {
        SessionState.SetString(Key + "status", status);
        File.WriteAllText(Path.Combine(Output, "play-result.txt"), status + ": " + checks + " checks; five star systems; one-pass shine, mask, reuse, disable and cleanup\n" + string.Join("\n", failures));
        EditorApplication.update -= Tick;
        if (OwnAccount()) EditorApplication.ExitPlaymode();
    }

    private static void ScheduleReturn()
    {
        SessionState.SetBool(Key + "pending", false);
        SessionState.SetBool(Key + "return", true);
        SessionState.SetString(Key + "returnDeadline", (EditorApplication.timeSinceStartup + 45).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.update -= ReturnAccount; EditorApplication.update += ReturnAccount;
    }

    private static void ReturnAccount()
    {
        double.TryParse(SessionState.GetString(Key + "returnDeadline", "0"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double limit);
        if (EditorApplication.timeSinceStartup > limit)
        {
            EditorApplication.update -= ReturnAccount;
            File.WriteAllText(Path.Combine(Output, "return-result.txt"), "DEFERRED: Editor/account busy; return key retained, no callback left");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if ((!string.IsNullOrEmpty(current) && !string.Equals(current, Account, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(prepared) && !string.Equals(prepared, Account, StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) return;
        IsolatedSavePlayGuard.UseRealAccount();
        bool returned = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""))
            && AssetDatabase.GetAssetPath(EditorSceneManagerStartScene()) == SessionState.GetString(Key + "startScene", "");
        EditorApplication.update -= ReturnAccount;
        if (!returned) { File.WriteAllText(Path.Combine(Output, "return-result.txt"), "FAIL: return conditions did not match; return key retained"); return; }
        foreach (string suffix in new[] { "return", "entered", "background", "pending" }) SessionState.EraseBool(Key + suffix);
        foreach (string suffix in new[] { "returnDeadline", "startScene" }) SessionState.EraseString(Key + suffix);
        File.WriteAllText(Path.Combine(Output, "return-result.txt"), "PASS: ready EditMode; actual account selected; environment, Guard and own pending/return keys clear; start scene preserved");
    }

    private static IEnumerator Verify()
    {
        // 부팅/첫 씬 전환의 긴 첫 프레임을 광택 중간 진행률 측정에 포함하지 않는다.
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching) yield return null;
        int stableFrames = 0;
        while (stableFrames < 6) { yield return null; stableFrames = Time.unscaledDeltaTime < .15f ? stableFrames + 1 : 0; }
        root = new GameObject("Item Quality Play Fixture", typeof(RectTransform), typeof(Canvas));
        root.hideFlags = HideFlags.HideAndDontSave; Object.DontDestroyOnLoad(root);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<RectTransform>().anchoredPosition = new Vector2(-10000, -10000);
        var images = new List<Image>(); var items = new List<ItemData>(); var originals = new List<Material>(); var shaders = new List<Material>();
        var rng = UnityEngine.Random.state;
        try
        {
            foreach (var type in new[] { typeof(WeaponItemData), typeof(GearItemData), typeof(BagItemData), typeof(FlaskItemData), typeof(ElementGemItemData) })
            {
                var source = AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets/ProjectOverburst" }).Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(p => p).Select(p => AssetDatabase.LoadAssetAtPath<BaseItemData>(p)).First(a => a && a.icon && (!(a is ElementGemItemData gem) || gem.fixedGrade == ItemGrade.Mythic && ElementGemItemData.IsAllowed(gem.element, gem.fixedGrade)));
                ItemData item = null;
                for(int seed=701;seed<10701;seed++)
                {
                    UnityEngine.Random.InitState(seed);
                    var candidate=ItemInscriptionQualityVerifier.Fixture(source,ItemGrade.Mythic,seed);
                    if(ItemInscriptionQuality.TryEvaluate(candidate,out var quality)&&quality.Tier==ItemInscriptionQualityTier.Masterpiece){item=candidate;break;}
                }
                Check(item!=null,"Natural within-grade masterpiece fixture: "+type.Name);
                var mask = new GameObject("Mask " + type.Name, typeof(RectTransform), typeof(Image), typeof(Mask)); mask.transform.SetParent(root.transform, false);
                mask.GetComponent<RectTransform>().anchoredPosition = new Vector2(-10000, -10000);
                mask.GetComponent<Mask>().showMaskGraphic = false;
                var go = new GameObject(type.Name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(mask.transform, false);
                var image = go.GetComponent<Image>(); image.sprite = source.icon;
                images.Add(image); items.Add(item); originals.Add(image.material);
            }
        }
        finally { UnityEngine.Random.state = rng; }
        for (int n = 0; n < 4; n++) yield return null;
        for (int i = 0; i < images.Count; i++)
        {
            Check(ItemInscriptionQuality.TryEvaluate(items[i],out var quality)&&quality.Grade==ItemGrade.Mythic&&quality.Score>=25,"Grade-relative native masterpiece");
            ItemQualityIconEffect.Present(images[i], items[i]); shaders.Add(images[i].material);
            Check(images[i].material.shader.name == "OVERBURST/UI/Item Quality Shine", "Shine starts: " + images[i].name);
            Check(Mathf.Approximately(images[i].material.GetFloat("_ShineStrength"), .28f), "Masterpiece strength");
        }
        float until = Time.realtimeSinceStartup + .4f; while (Time.realtimeSinceStartup < until) yield return null;
        for (int i = 0; i < images.Count; i++)
        {
            float progress = images[i].material.GetFloat("_ShineProgress");
            Check(progress > .1f && progress < .9f, "Actual frame progression: " + progress + "; frame seconds=" + Time.unscaledDeltaTime);
            ItemQualityIconEffect.Present(images[i], items[i]);
            Check(images[i].material == shaders[i] && images[i].material.GetFloat("_ShineProgress") >= progress, "Same-item refresh does not restart");
            Check(Mathf.Approximately(images[i].materialForRendering.GetFloat("_ShineProgress"), progress), "Stencil material advances");
        }
        until = Time.realtimeSinceStartup + 1.5f; while (Time.realtimeSinceStartup < until) yield return null;
        for (int i = 0; i < images.Count; i++)
        {
            Check(images[i].material == originals[i], "Single sweep restores original material");
            ItemQualityIconEffect.Present(images[i], items[i]); Check(images[i].material == originals[i], "No idle replay");
            images[i].gameObject.SetActive(false);
        }
        yield return null;
        for (int i = 0; i < images.Count; i++)
        {
            Check(!shaders[i], "Disabled icon releases its material");
            images[i].gameObject.SetActive(true); Check(images[i].material != originals[i], "Reopened icon plays once");
            ItemQualityIconEffect.Present(images[i], items[i], Color.white, false); Check(images[i].material == originals[i], "Cooldown/disabled sheen preserves material");
            ItemQualityIconEffect.Present(images[i], null, Color.clear); Check(images[i].material == originals[i] && images[i].color == Color.clear, "Empty icon resets");
            ItemInscriptionQualityVerifier.Assign(items[i],ItemInscriptionQualityVerifier.StarsForScore(20,0));
            ItemQualityIconEffect.Present(images[i],items[i]);
            Check(images[i].material==originals[i]&&Mathf.Approximately(images[i].color.r,.92f),"Same grade all-white item is lowest without shine");
            items[i].grade=ItemGrade.Common;ItemQualityIconEffect.Present(images[i],items[i]);
            Check(images[i].material==originals[i]&&images[i].color==Color.white,"Common grade removes tint and shine");
        }
        Object.Destroy(root); root = null; yield return null;
    }
}
