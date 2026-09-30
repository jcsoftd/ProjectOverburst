#if !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public sealed class SavePlayerProbe : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private string output;
    private string mode;
    private static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    private static object Property(string type, string name) => TypeOf(type).GetProperty(name, Flags).GetValue(null);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethods(Flags).Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(target, args);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartProbe()
    {
        string mode = Environment.GetEnvironmentVariable("OVERBURST_SAVE_PROBE");
        string output = Environment.GetEnvironmentVariable("OVERBURST_SAVE_PROBE_OUTPUT");
        if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(output)) return;
        var go = new GameObject("SavePlayerProbe");
        DontDestroyOnLoad(go);
        var probe = go.AddComponent<SavePlayerProbe>(); probe.mode = mode; probe.output = output;
        Application.runInBackground = true;
        probe.StartCoroutine(probe.Run());
    }

    private IEnumerator Run()
    {
        float timeout = Time.realtimeSinceStartup + 120f;
        while (Time.realtimeSinceStartup < timeout)
        {
            bool ready = false;
            try { ready = (bool)Property("Overburst.Persistence.AccountBootstrap", "Ready") && Property("WorldSessionState", "Phase").ToString() == "Hideout"; }
            catch (Exception) { }
            if (ready) break;
            yield return null;
        }
        try
        {
            if (!(bool)Property("Overburst.Persistence.AccountBootstrap", "Ready")) throw new Exception("Account boot did not complete.");
            var session = Property("Overburst.Persistence.AccountGameplaySession", "Current");
            var progression = Property("PlayerProgression", "Current");
            if (mode == "mutate")
            {
                Call(progression, "AddExperience", 123);
                if (!(bool)Call(progression, "FlushPendingExperience")) throw new Exception("Experience commit failed.");
            }
            else if (mode != "verify") throw new Exception("Unknown probe mode.");
            object snapshot = Call(session, "Read");
            string json = JsonUtility.ToJson(snapshot);
            Directory.CreateDirectory(output);
            if (mode == "verify")
            {
                string expected = File.ReadAllText(Path.Combine(output, "expected.json"));
                if (expected != json) throw new Exception("Restarted account differs from expected snapshot.");
            }
            var inventory = Property("PlayerAccountInventoryService", "SharedInventory");
            int actual = 0;
            foreach (var item in (IEnumerable)inventory.GetType().GetProperty("Items").GetValue(inventory)) if (item != null) actual++;
            var slots = (IEnumerable)snapshot.GetType().GetField("inventory").GetValue(snapshot);
            int saved = 0; foreach (string id in slots) if (!string.IsNullOrEmpty(id)) saved++;
            if (actual != saved) throw new Exception("Live inventory item count differs from saved ownership.");
            File.WriteAllText(Path.Combine(output, mode + "-snapshot.json"), json);
            File.WriteAllText(Path.Combine(output, mode + "-result.txt"), "PASS account boot, " + mode + ", live inventory=" + actual + ", level=" + progression.GetType().GetProperty("Level").GetValue(progression) + ", XP=" + progression.GetType().GetProperty("Experience").GetValue(progression));
            Application.Quit(0);
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, mode + "-error.txt"), error.ToString());
            Application.Quit(2);
        }
    }
}
#endif
