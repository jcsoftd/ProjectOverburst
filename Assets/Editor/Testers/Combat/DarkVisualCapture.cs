using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only 60D visual capture: Game view screenshots of the corrosion aura and the dark heavy in real Play Mode.
// Isolated account (OVERBURST_SAVE_DIRECTORY); exits Play itself. The vendor DarkAura shown for comparison is a
// runtime-only instance (nothing is saved).
[InitializeOnLoad]
public static class DarkVisualCapture
{
    const string Key = "DarkVisualCapture";
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<string> shots = new List<string>();
    static readonly List<string> notes = new List<string>();
    static readonly List<string> errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static DarkVisualCapture() { EditorApplication.playModeStateChanged += State; }

    public static void RunAuraCandidates(string output) { SessionState.SetString(Key + ".mode", "aura"); Start(output); }
    public static void RunHeavyRanges(string output) { SessionState.SetString(Key + ".mode", "ranges"); Start(output); }
    public static void RunIceElectricRanges(string output) { SessionState.SetString(Key + ".mode", "iceElectric"); Start(output); }
    public static void Run(string output) { SessionState.SetString(Key + ".mode", "full"); Start(output); }
    static void Start(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 240;
            stack.Clear(); string mode = SessionState.GetString(Key + ".mode", "full"); work = mode == "aura" ? AuraCandidates() : mode == "ranges" ? HeavyRanges(false) : mode == "iceElectric" ? HeavyRanges(true) : Capture(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish("COMPLETE"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }

    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }

    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "capture-results.json"), JsonConvert.SerializeObject(new { status, shots, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    static void Shot(string name)
    {
        string path = Path.Combine(Output, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        shots.Add(path);
    }

    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }

    // ---------- aura candidates: runtime-only tinted copies of the vendor DarkAura (nothing is saved) ----------
    static Color Crimson(Color c, float k)
    {
        float l = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * k;
        return new Color(l, l * .02f, l * .03f, c.a);
    }
    static Gradient MapGradient(Gradient g, Func<Color, Color> f, float alphaScale)
    {
        var colors = g.colorKeys; var alphas = g.alphaKeys;
        for (int i = 0; i < colors.Length; i++) colors[i].color = f(colors[i].color);
        for (int i = 0; i < alphas.Length; i++) alphas[i].alpha = Mathf.Clamp01(alphas[i].alpha * alphaScale);
        var result = new Gradient { mode = g.mode }; result.SetKeys(colors, alphas); return result;
    }
    static ParticleSystem.MinMaxGradient Map(ParticleSystem.MinMaxGradient m, Func<Color, Color> f, float alphaScale)
    {
        Color A(Color c) { var o = f(c); o.a = Mathf.Clamp01(c.a * alphaScale); return o; }
        switch (m.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(A(m.color));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(A(m.colorMin), A(m.colorMax));
            case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(MapGradient(m.gradient, f, alphaScale));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(MapGradient(m.gradientMin, f, alphaScale), MapGradient(m.gradientMax, f, alphaScale));
            default: var r = new ParticleSystem.MinMaxGradient(MapGradient(m.gradient, f, alphaScale)); r.mode = ParticleSystemGradientMode.RandomColor; return r;
        }
    }
    static bool IsAdditive(Material m)
    {
        if (m == null) return false;
        if (m.HasProperty("_BUILTIN_DstBlend")) return Mathf.RoundToInt(m.GetFloat("_BUILTIN_DstBlend")) == 1;
        string n = m.name.ToLowerInvariant(); return n.Contains("add") || n.Contains("glow") || n.Contains("flare");
    }
    // addK: glow brightness (1 = source brightness in weapon crimson); smoke*: darkness/opacity of alpha layers.
    static void Tint(GameObject root, float addK, float smokeStartK, float smokeColK, float smokeAlpha)
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var mat = ps.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
            var main = ps.main; var col = ps.colorOverLifetime;
            main.startSizeMultiplier *= 1.1f; // five stacks, as the presentation applies
            if (IsAdditive(mat))
            {
                main.startColor = Map(main.startColor, c => Crimson(c, addK), 1f);
                if (col.enabled) col.color = Map(col.color, c => Crimson(c, 1f), 1f);
            }
            else if (col.enabled)
            {
                main.startColor = Map(main.startColor, c => Crimson(c, smokeStartK), 1f);
                col.color = Map(col.color, c => Crimson(c, smokeColK), smokeAlpha);
            }
            else main.startColor = Map(main.startColor, c => Crimson(c, smokeStartK), smokeAlpha);
        }
    }

    static IEnumerator AuraCandidates()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; var spawned = new List<GameObject>();
        var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.5f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var small = defs.First(d => d.EnemyId.Contains("Ceratoferox"));
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Dark));
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            yield return Wait(1f);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            var darkAura = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/06_VFX/Piloto Studio 1/Elemental VFX Mega Bundle/Dark/DarkAura.prefab");
            // label, viewport x, real status aura?, scale factor, glow, smoke start, smoke col, smoke alpha
            var rows = new (string label, float x, bool real, float scale, float add, float s0, float s1, float sa)[]
            {
                ("A_before", .30f, false, 1f, -1f, 0f, 0f, 0f),
                ("B_current", .43f, true, .6f, 0f, 0f, 0f, 0f),
                ("C_scale0.8_glow0.75", .57f, false, .8f, .75f, .3f, .35f, 1.3f),
                ("D_scale0.8_glow0.55_blacker", .70f, false, .8f, .55f, .15f, .25f, 1.6f),
            };
            foreach (var row in rows)
            {
                Vector3 at = Ground(row.x, .42f); Vector3 f = cam.transform.position - at; f.y = 0f;
                if (!spawn.TrySpawn(new EnemySpawnRequest(small, at, Quaternion.LookRotation(f), player.transform), out var e)) throw new Exception("Spawn");
                leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                yield return null;
                if (row.real)
                {
                    var s = e.GetComponent<ElementalStatusController>();
                    for (int k = 0; k < 5; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark, 10, player.gameObject, energy.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
                    continue;
                }
                var volume = CombatTargetVfxPlacement.ResolveVolume(e.GetComponent<CombatTarget>());
                var aura = UnityEngine.Object.Instantiate(darkAura, volume.Center, Quaternion.identity);
                aura.transform.localScale = Vector3.one * Mathf.Clamp(volume.Radius / .6f, .45f, 3f) * row.scale;
                if (row.add >= 0f) Tint(aura, row.add, row.s0, row.s1, row.sa);
                foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(true); ps.Play(false); }
                spawned.Add(aura);
                notes.Add(row.label + " at viewport x " + row.x + " scale " + aura.transform.localScale.x.ToString("F2"));
            }
            yield return Wait(1.8f); Shot("aura_candidates_1");
            yield return Wait(1.3f); Shot("aura_candidates_2");
            yield return Wait(0.3f);
        }
        finally
        {
            foreach (var g in spawned) if (g != null) UnityEngine.Object.Destroy(g);
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    // ---------- heavy VFX extent vs the shared damage circle (R = 4 m at full energy) ----------
    static GameObject Ring(Vector3 centre, float radius, Color color, string name)
    {
        var go = new GameObject(name);
        var line = go.AddComponent<LineRenderer>();
        line.loop = true; line.useWorldSpace = true; line.positionCount = 96; line.widthMultiplier = .07f;
        line.material = new Material(Shader.Find("Sprites/Default")); line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 96; i++)
        {
            float a = i * Mathf.PI * 2f / 96f;
            line.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, .06f, Mathf.Sin(a) * radius));
        }
        return go;
    }

    static IEnumerator HeavyRanges(bool iceElectric)
    {
        EnemyThemeTrialHarness ui = null; MeleeRuntime melee = null; var temp = new List<GameObject>();
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            melee = player.GetComponent<MeleeRuntime>();
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Vector3 up = Ground(.5f, .62f) - Ground(.5f, .5f); up.y = 0f; up.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 spot = origin + right * 7f; // clear floor away from the arena's display props
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var executorField = typeof(MeleeRuntime).GetField("heavyDischargeExecutor", bf);
            var centreField = typeof(MeleeHeavyDischargeExecutor).GetField("impactCenter", bf);
            float maxR = OverburstElementTuning.Current.maximumRadius;
            EnemyDefinition small = null;
            if (iceElectric)
            {
                if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
                foreach (var table in ui.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
                small = ui.tables.SelectMany(x => x.Entries).Select(x => x.definition).Where(x => x != null).First(x => x.EnemyId.Contains("Ceratoferox"));
            }
            var plans = iceElectric ? new (WeaponElement element, string label, float[] times)[]
            {
                (WeaponElement.Fire, "fire", new[] { .15f }),
                (WeaponElement.Ice, "ice", new[] { .1f, .35f, .8f, 1.6f }),
                (WeaponElement.Electric, "electric", new[] { .03f, .1f, .2f, .5f }),
            } : new (WeaponElement element, string label, float[] times)[]
            {
                (WeaponElement.Fire, "fire", new[] { .15f, .45f, .9f }),
                (WeaponElement.Electric, "electric", new[] { .15f, .45f }),
                (WeaponElement.Dark, "dark", new[] { .2f, 1.0f, 1.97f, 2.3f }),
                (WeaponElement.Light, "light", new[] { .1f, .85f, 1.65f }),
            };
            foreach (var plan in plans)
            {
                melee.CancelCurrentAttackState();
                if (!actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: plan.element))) throw new Exception("Equip " + plan.element);
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                Warp(player, spot, up);
                var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
                if (iceElectric && plan.element == WeaponElement.Electric)
                {
                    Vector3 slam = spot + up * 1.7f;
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 at = slam + Quaternion.AngleAxis(i * 60f, Vector3.up) * up * (i % 2 == 0 ? 1.4f : 2.8f);
                        Vector3 look = cam.transform.position - at; look.y = 0f;
                        if (!spawn.TrySpawn(new EnemySpawnRequest(small, at, Quaternion.LookRotation(look), player.transform), out var e)) throw new Exception("Spawn");
                        leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                    }
                }
                yield return Wait(1.5f); // weapon swap, VFX pool preparation
                energy.Clear(); int seq = 93000 + (int)plan.element * 100;
                for (int i = 0; i < 10; i++) energy.RecordConfirmedHit(energy.WeaponInstanceId, energy.Element, ++seq, 1);
                if (plan.element == WeaponElement.Light)
                {
                    typeof(OverburstElementEnergy).GetProperty("Amount").GetSetMethod(true).Invoke(energy, new object[] { 200f });
                    typeof(OverburstElementEnergy).GetProperty("RadianceStacks").GetSetMethod(true).Invoke(energy, new object[] { 100 });
                }
                WeaponActionResult result = WeaponActionResult.RejectedNotReady; float until = Time.time + 2f;
                while (Time.time < until && result != WeaponActionResult.Accepted)
                {
                    PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                    result = melee.TryStartHeavyAttack(up); if (result != WeaponActionResult.Accepted) yield return null;
                }
                if (result != WeaponActionResult.Accepted) throw new Exception("Heavy " + plan.element + " " + result);
                float commit = -1f; until = Time.time + 3f;
                while (Time.time < until) { if (energy.Amount <= 0f) { commit = Time.time; break; } yield return null; }
                if (commit < 0f) throw new Exception("No commit " + plan.element);
                Vector3 centre = (Vector3)centreField.GetValue(executorField.GetValue(melee));
                temp.Add(Ring(centre, maxR, new Color(1f, .95f, .2f, 1f), "R"));
                if (plan.element == WeaponElement.Dark) temp.Add(Ring(centre, maxR * OverburstElementTuning.Current.SafeDarkGatherRadiusMultiplier, new Color(.3f, .8f, 1f, 1f), "Gather"));
                if (plan.element == WeaponElement.Light)
                {
                    temp.Add(Ring(centre, maxR * .43f, new Color(1f, .95f, .2f, .8f), "R1"));
                    temp.Add(Ring(centre, maxR * .57f, new Color(1f, .95f, .2f, .8f), "R2"));
                }
                notes.Add(plan.label + " centre " + centre + " R " + maxR);
                {
                    var hd = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset");
                    foreach (var pf in new[] { hd.elementVfx.iceImpact, hd.elementVfx.electricImpact, hd.elementVfx.electricDirectHit })
                        if (pf != null) { var st = TransientVfxPool.GetStatistics(pf); notes.Add("  pool " + pf.name + " requests=" + st.Requests + " created=" + st.Created + " active=" + st.Active + " misses=" + st.Misses); }
                }
                foreach (float t in plan.times)
                {
                    while (Time.time < commit + t) yield return null;
                    Shot("range_" + plan.label + "_" + t.ToString("F2"));
                }
                yield return null; yield return null;
                foreach (var g in temp) if (g != null) UnityEngine.Object.Destroy(g); temp.Clear();
                if (spawn != null) { foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); leased.Clear(); }
                yield return Wait(3.5f); // let the VFX tails and follow-ups finish
            }
        }
        finally
        {
            foreach (var g in temp) if (g != null) UnityEngine.Object.Destroy(g);
            melee?.CancelCurrentAttackState();
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; MeleeRuntime melee = null; GameObject vendorAura = null;
        var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.5f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var small = defs.First(d => d.EnemyId.Contains("Ceratoferox"));
            var medium = defs.First(d => d.EnemyId.Contains("Scolokarck"));
            melee = player.GetComponent<MeleeRuntime>();
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            if (!actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Dark))) throw new Exception("Equip dark");
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            energy.Clear();
            var origin = player.transform.position;
            yield return Wait(1.2f);
            var cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy)
            {
                var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f));
                return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin;
            }
            Quaternion FaceCamera(Vector3 at) { Vector3 f = cam.transform.position - at; f.y = 0f; return Quaternion.LookRotation(f.sqrMagnitude > .01f ? f : Vector3.back); }
            EnemyActor Spawn(EnemyDefinition d, Vector3 at)
            {
                if (!spawn.TrySpawn(new EnemySpawnRequest(d, at, FaceCamera(at), player.transform), out var e)) throw new Exception("Spawn " + d.EnemyId);
                leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                return e;
            }
            void Corrode(EnemyActor e)
            {
                var s = e.GetComponent<ElementalStatusController>();
                for (int k = 0; k < 5; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark, 10, player.gameObject, energy.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
            }
            notes.Add("camera " + cam.name + " pos=" + cam.transform.position + " fov=" + cam.fieldOfView + " screen=" + Screen.width + "x" + Screen.height);

            // ---- 1) Aura: left = vendor DarkAura at the old size (runtime-only), right = the new corrosion aura ----
            var left = Spawn(small, Ground(.40f, .40f));
            var right = Spawn(small, Ground(.60f, .40f));
            yield return null;
            Corrode(right);
            var volume = CombatTargetVfxPlacement.ResolveVolume(left.GetComponent<CombatTarget>());
            var darkAura = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/06_VFX/Piloto Studio 1/Elemental VFX Mega Bundle/Dark/DarkAura.prefab");
            vendorAura = UnityEngine.Object.Instantiate(darkAura, volume.Center, Quaternion.identity);
            vendorAura.transform.localScale = Vector3.one * Mathf.Clamp(volume.Radius / .6f, .45f, 3f);
            foreach (var ps in vendorAura.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(false);
            notes.Add("aura: left=vendor DarkAura scale " + vendorAura.transform.localScale.x.ToString("F2") + " (before), right=new corrosion aura (after), body radius " + volume.Radius.ToString("F2"));
            yield return Wait(1.6f); Shot("01_aura_left-before_right-after");
            yield return Wait(1.1f); Shot("02_aura_left-before_right-after");
            yield return null; yield return null; // CaptureScreenshot writes at the end of the frame
            UnityEngine.Object.Destroy(vendorAura); vendorAura = null;
            foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); leased.Clear();
            yield return Wait(0.3f);

            // ---- 2) Dark heavy: corroded cluster at the slam point plus three enemies to be gathered ----
            Vector3 screenUp = Ground(.5f, .62f) - origin; screenUp.y = 0f; screenUp.Normalize();
            Vector3 screenRight = Vector3.Cross(Vector3.up, screenUp);
            Warp(player, origin, screenUp); yield return Wait(0.3f);
            Vector3 impact = origin + screenUp * 1.7f;
            var cluster = new List<EnemyActor> { Spawn(medium, impact + screenUp * .6f) };
            for (int i = 0; i < 4; i++) cluster.Add(Spawn(small, impact + Quaternion.AngleAxis(i * 90f + 45f, Vector3.up) * screenUp * 2.2f));
            cluster.Add(Spawn(small, impact + screenRight * 6.5f));
            cluster.Add(Spawn(small, impact - screenRight * 6.5f));
            cluster.Add(Spawn(small, impact + screenUp * 6f));
            yield return null;
            foreach (var e in cluster) Corrode(e);
            yield return Wait(1.0f); Shot("03_heavy_before-cast_corroded");
            energy.Clear(); int seq = 91000; for (int i = 0; i < 10; i++) energy.RecordConfirmedHit(energy.WeaponInstanceId, energy.Element, ++seq, 1);
            WeaponActionResult result = WeaponActionResult.RejectedNotReady; float until = Time.time + 2f;
            while (Time.time < until && result != WeaponActionResult.Accepted)
            {
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                result = melee.TryStartHeavyAttack(screenUp); if (result != WeaponActionResult.Accepted) yield return null;
            }
            if (result != WeaponActionResult.Accepted) throw new Exception("Heavy " + result);
            float commit = -1f; until = Time.time + 3f;
            while (Time.time < until) { if (energy.Amount <= 0f) { commit = Time.time; break; } yield return null; }
            if (commit < 0f) throw new Exception("No commit");
            foreach (var mark in new[] { (.2f, "04_heavy_0.2s_slam"), (.9f, "05_heavy_0.9s_gather"), (1.5f, "06_heavy_1.5s_hold"), (1.97f, "07_heavy_1.97s_burst"), (2.6f, "08_heavy_2.6s_smoke") })
            {
                while (Time.time < commit + mark.Item1) yield return null;
                Shot(mark.Item2);
            }
            yield return Wait(0.5f);
            notes.Add("heavy impact " + impact + " up=" + screenUp + " barrages=" + DarkBarrageScheduler.CastCount + " shots=" + DarkBarrageScheduler.LastShotCount);
        }
        finally
        {
            if (vendorAura != null) UnityEngine.Object.Destroy(vendorAura);
            melee?.CancelCurrentAttackState();
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }
}
