#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class GraveHuntSoundVerifier
{
    const string Key = "Overburst.GraveHuntSoundVerifier.";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator routine;
    static readonly List<string> checks = new List<string>();
    static readonly List<GameObject> owned = new List<GameObject>();
    static string directory;
    static int lastFrame;
    static double started;
    static GraveHuntSoundVerifier()
    { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += State; }
    static object Scenes() => Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty }).ToArray();
    static string AccountHash()
    {
        string root = Path.Combine(Application.persistentDataPath, "Account");
        if (!Directory.Exists(root)) return "MISSING";
        using (var sha = SHA256.Create()) return string.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => p + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p)))));
    }
    static void Write(string file, object data) => File.WriteAllText(Path.Combine(directory, file), JsonConvert.SerializeObject(data, Formatting.Indented));
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
    static AudioSource[] Voices()
    {
        var service = typeof(CombatActionSfxService).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return service == null ? Array.Empty<AudioSource>() : ((AudioSource[])typeof(CombatActionSfxService).GetField("voices", Hidden).GetValue(service))
            .Where(v => v != null && v.isPlaying).ToArray();
    }
    static AudioSource[] Extra() => Voices().Where(v => v.priority == 160).ToArray();
    static void StopExtra() { foreach (var voice in Extra()) voice.Stop(); }
    public static string Run(string plan, string output)
    {
        GraveHuntSoundBuilder.RequireIdle();
        if (SessionState.GetString(Key + "return", "") != "" || Directory.Exists(output)) throw new Exception("New unoccupied run directory required");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))) throw new Exception("Settings account occupied");
        directory = Path.GetFullPath(output); Directory.CreateDirectory(directory);
        Write("before.json", new { scenes = Scenes(), input = InputSystem.settings.GetInstanceID(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) });
        SessionState.SetString(Key + "return", directory); SessionState.SetString(Key + "plan", plan);
        SessionState.SetString(Key + "stage", "boot"); SessionState.SetString(Key + "accountHash", AccountHash());
        SessionState.SetString(Key + "scenes", JsonConvert.SerializeObject(Scenes()));
        SessionState.SetInt(Key + "input", InputSystem.settings.GetInstanceID());
        SessionState.SetString(Key + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 180);
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", Path.Combine(directory, "Settings"));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(directory, "Account")); }
        catch (Exception error) { Finish(error); throw; }
        return "STARTED";
    }
    static void Tick()
    {
        directory = SessionState.GetString(Key + "return", ""); if (directory == "") return;
        if (SessionState.GetString(Key + "stage", "") == "return") { Return(); return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) { Finish(new TimeoutException("Owned audio run")); return; }
        if (!EditorApplication.isPlaying || IsolatedSavePlayGuard.ActiveDirectory != Path.Combine(directory, "Account")) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
        try
        {
            if (routine == null)
            {
                if (SessionState.GetString(Key + "stage", "") != "boot") throw new Exception("Verifier interrupted by reload");
                SessionState.SetString(Key + "stage", "running"); Application.runInBackground = true;
                checks.Clear(); owned.Clear(); started = EditorApplication.timeSinceStartup; routine = Verify();
            }
            if (!routine.MoveNext()) Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Verify()
    {
        yield return null; yield return null;
        var plan = JObject.Parse(File.ReadAllText(SessionState.GetString(Key + "plan", "")));
        var listener = Object.FindFirstObjectByType<AudioListener>(); Vector3 point = listener != null ? listener.transform.position : Vector3.zero;
        var dummy = new GameObject("Owned sound hit target"); owned.Add(dummy); dummy.transform.position = point;
        var victim = dummy.AddComponent<CombatHealth>(); victim.SetMaxHp(100000, true);
        foreach (var row in plan["actors"])
        {
            StopExtra(); var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)row["actor"]);
            var root = Object.Instantiate(prefab, point + Vector3.forward * 3f, Quaternion.identity); owned.Add(root);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && !(behaviour is EnemySoundEmitter) && !(behaviour is CombatHealth)) behaviour.enabled = false;
            var health = root.GetComponent<CombatHealth>(); var emitter = root.GetComponent<EnemySoundEmitter>();
            health.SetMaxHp(10000, true); string name = (string)row["name"];
            bool hasAttack = row["slots"]["attack"].Any();
            Check(emitter.PlayAttack() == hasAttack, name + " attack follows selection");
            if (hasAttack) Check(Extra().Length == 1, name + " one additional attack voice");
            StopExtra();
            if ((int)row["slots"]["attack"].Count() > 1)
            {
                AudioClip previous = null;
                for (int i = 0; i < 8; i++) { Check(emitter.PlayAttack(), name + " random voice"); var clip = Extra().Single().clip; Check(clip != previous, name + " avoids immediate repeat"); previous = clip; StopExtra(); }
            }
            health.TakeDamage(new DamageInfo(5, root.transform.position, dummy));
            Check(Extra().Length == (row["slots"]["hurt"].Any() ? 1 : 0), name + " confirmed received damage material");
            StopExtra();
            var ability = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>((string)row["ability"]);
            Check(ability != null, name + " real authored attack definition");
            victim.TakeDamage(new DamageInfo(5, point, root, enemyAbility: ability));
            Check(Extra().Length == (row["slots"]["melee"].Any() ? 1 : 0), name + " confirmed outgoing hit only");
            StopExtra();
            victim.TakeDamage(new DamageInfo(0, point, root, enemyAbility: ability));
            Check(Extra().Length == 0, name + " zero damage has no impact layer");
            victim.TakeDamage(new DamageInfo(1, point, root, isDamageOverTime: true, enemyAbility: ability));
            Check(Extra().Length == 0, name + " DOT has no physical impact layer");
            health.TakeDamage(new DamageInfo(100000, root.transform.position, dummy));
            bool hasDeath = row["slots"]["death"].Any(); Check(Extra().Length == (hasDeath ? 1 : 0), name + " death follows selection");
            root.SetActive(false); Check(Extra().Length == (hasDeath ? 1 : 0), name + " death survives actor pooling");
            StopExtra(); yield return null;
        }
        var clipForMix = AssetDatabase.LoadAssetAtPath<AudioClip>((string)plan["actors"][4]["slots"]["attack"][0]["asset"]);
        var common = CombatActionSfxService.ResolveNamedClip("MonsterHitCommon01");
        Check(CombatActionSfxService.PlayEnvironmentBreak(common, point), "Existing base audio starts");
        var baseline = Voices().Where(v => v.priority != 160 && v.clip == common).ToArray();
        Check(baseline.Length > 0, "Existing audio voice remains separate");
        for (int i = 0; i < 48; i++) CombatActionSfxService.PlayMonsterLayer(clipForMix, point, .45f, 1f);
        var extra = Extra(); Check(extra.Length == 12, "Crowd additional voices capped at twelve");
        Check(extra.Min(v => v.volume) < extra.Max(v => v.volume) * .65f, "Crowd gain falls with simultaneous voices");
        Check(baseline.All(v => v.isPlaying && v.clip == common), "Additional layers do not stop or replace base audio");
        Check(extra.All(v => v.volume <= .8f && v.spatialBlend == .65f && v.dopplerLevel == 0f), "Bounded 3D layer volume");
        for (int i = 0; i < 12; i++) yield return null;
        Write("mix-observation.json", new { additionalVoices = extra.Length, volumes = extra.Select(v => v.volume), existingBaseStillPlaying = baseline.Any(v => v.isPlaying), audioListenerVolume = AudioListener.volume, audioListenerPause = AudioListener.pause });
    }
    static void Finish(Exception error)
    {
        try { (routine as IDisposable)?.Dispose(); foreach (var go in owned) if (go != null) Object.Destroy(go); StopExtra(); }
        catch (Exception cleanup) { error = error ?? cleanup; }
        finally
        {
            routine = null; owned.Clear();
            Write("play.json", new { status = error == null ? "PASS" : "FAIL", count = checks.Count, checks, error = error?.ToString(), seconds = EditorApplication.timeSinceStartup - started });
            SessionState.SetString(Key + "stage", "return");
            SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 90);
            if (EditorApplication.isPlayingOrWillChangePlaymode && (IsolatedSavePlayGuard.ActiveDirectory == Path.Combine(directory, "Account")
                || Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) == Path.Combine(directory, "Account"))) EditorApplication.ExitPlaymode();
        }
    }
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(Key + "return", "") != ""
            && SessionState.GetString(Key + "stage", "") != "return") { directory = SessionState.GetString(Key + "return", ""); Finish(new Exception("Owned Play interrupted")); }
    }
    static void Return()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.ActiveDirectory != "") return;
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        if (env != "" && env != Path.Combine(directory, "Account")) return;
        string start = SessionState.GetString(Key + "start", "");
        EditorSceneManager.playModeStartScene = start == "" ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        if (Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY") == Path.Combine(directory, "Settings")) Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", null);
        Application.runInBackground = SessionState.GetBool(Key + "background", false); IsolatedSavePlayGuard.UseRealAccount();
        bool preserved = AccountHash() == SessionState.GetString(Key + "accountHash", "")
            && JsonConvert.SerializeObject(Scenes()) == SessionState.GetString(Key + "scenes", "") && InputSystem.settings.GetInstanceID() == SessionState.GetInt(Key + "input", 0);
        Write("return.json", new { status = preserved ? "PASS" : "FAIL", realAccountScenesInputPreserved = preserved, stopped = !EditorApplication.isPlayingOrWillChangePlaymode,
            guardReady = !IsolatedSavePlayGuard.RequiresAccountChoice, save = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), active = IsolatedSavePlayGuard.ActiveDirectory, start, scenes = Scenes() });
        foreach (string key in new[] { "return", "plan", "stage", "accountHash", "scenes", "start" }) SessionState.EraseString(Key + key);
        SessionState.EraseFloat(Key + "deadline"); SessionState.EraseInt(Key + "input"); SessionState.EraseBool(Key + "background");
    }
}
#endif
