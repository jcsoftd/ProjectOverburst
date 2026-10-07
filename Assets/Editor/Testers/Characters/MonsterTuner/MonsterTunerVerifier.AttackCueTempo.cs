using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    public static string StartAttackCueTempoNative(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
        string directory = Path.GetFullPath(Path.Combine(workspace, outputRelative));
        string allowed = Path.GetFullPath(Path.Combine(workspace, "개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Private monster output required.");
        string resultPath = Path.Combine(directory, "native-verification.json");
        if (File.Exists(resultPath)) throw new InvalidOperationException("Inspect previous verification before rerunning.");
        var rows = JObject.Parse(File.ReadAllText(Path.Combine(directory, "plan.json")))["records"].ToArray();
        var records = new JArray(); int index = 0, liveStages = MonsterTunerPreviewStage.LiveStages;
        var scenes = TempoScenes(); double deadline = EditorApplication.timeSinceStartup + 600;
        bool finished = false;
        Directory.CreateDirectory(Path.Combine(directory, "Poses"));
        AssemblyReloadEvents.beforeAssemblyReload += Interrupted;
        EditorApplication.update += Run;
        Progress("QUEUED");
        return "NATIVE_ATTACK_CUE_TEMPO_QUEUED";
        void Progress(string status, string error = null)
        {
            File.WriteAllText(resultPath, new JObject { ["status"] = status, ["completed"] = index,
                ["expected"] = rows.Length, ["records"] = records, ["error"] = error }.ToString());
        }
        void Finish(string status, string error = null)
        {
            if (finished) return; finished = true;
            EditorApplication.update -= Run; AssemblyReloadEvents.beforeAssemblyReload -= Interrupted;
            Progress(status, error);
        }
        void Interrupted() => Finish("FAIL_RELOAD", "Native preview interrupted by compilation; owned stages are disposed per row.");
        void Run()
        {
            if (finished) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.timeSinceStartup > deadline)
            { Finish("FAIL_INTERRUPTED", "Editor ownership changed or native preview deadline expired."); return; }
            try
            {
                if (index == rows.Length)
                {
                    RequireTempo(JToken.DeepEquals(scenes, TempoScenes()), "User scenes changed");
                    RequireTempo(MonsterTunerPreviewStage.LiveStages == liveStages, "Preview resources not returned");
                    var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");
                    var all = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToArray();
                    RequireTempo(all.Length == 56 && all.All(d => Mathf.Approximately(d.ResolveRuntimeStats().AttackInterval, 1f)), "Default regular attack interval roster differs");
                    File.WriteAllText(Path.Combine(directory, "default-stats.json"), new JObject { ["regularActors"] = all.Length,
                        ["defaultAttackIntervalSeconds"] = 1f, ["records"] = new JArray(all.Select(d => new JObject {
                            ["id"] = d.EnemyId, ["attackSpeed"] = d.ResolveRuntimeStats().AttackSpeedMultiplier,
                            ["attackIntervalSeconds"] = d.ResolveRuntimeStats().AttackInterval })) }.ToString());
                    Finish("PASS_NATIVE_35_ATTACKS"); return;
                }
                var row = rows[index];
                var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definitionPath"]);
                var ability = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>((string)row["abilityPath"]);
                RequireTempo(ability != null && ability.HasAttackCue && ability.FirstStrikeOnlyParry, "Missing authored cue / first-only policy");
                RequireTempo(ability.TryGetParryMotionWindow(0, out var window) && ability.PreparationEnd < window.x && window.y <= ability.HitNormalizedTime + .00001f, "Preparation/stroke/ping/hit order");
                float baseline = ability.ResolvePacedTime(window.y, 1f) - ability.ResolvePacedTime(window.x, 1f);
                RequireTempo(baseline >= .2999f, "Insufficient baseline response window");
                var speeds = new JArray();
                foreach (float speed in new[] { .5f, .75f, 1f, 1.5f, 2f })
                {
                    float start = ability.ResolvePacedTime(window.x, speed), hit = ability.ResolvePacedTime(window.y, speed);
                    RequireTempo(Mathf.Abs((hit - start) * speed - baseline) < .0001f, "Window does not follow attack speed");
                    RequireTempo(Mathf.Abs(ability.ResolveExecutionDuration(speed) * speed - ability.ResolveExecutionDuration(1f)) < .0001f, "Attack length does not follow speed");
                    for (int strike = 1; strike < ability.HitCount; strike++) RequireTempo(!ability.TryGetParryMotionWindow(strike, out _), "Later strike parry enabled");
                    speeds.Add(new JObject { ["speed"] = speed, ["cueSeconds"] = start, ["hitSeconds"] = hit, ["parrySeconds"] = hit - start });
                }
                var session = MonsterTunerSession.Create(definition, false);
                var stage = new MonsterTunerPreviewStage();
                var record = new JObject { ["id"] = row["id"], ["name"] = definition.DisplayName, ["speeds"] = speeds };
                try
                {
                    stage.Load(session);
                    int slot = Enumerable.Range(0, definition.AbilitySet.Count).First(i => definition.AbilitySet.GetAbility(i) == ability);
                    stage.SetClip(MonsterTunerAnimationBindings.AttackClip(session, ability, slot), ability);
                    RequireTempo(stage.Clip != null, "Actual attack binding absent");
                    RequireTempo(ability.TryResolveAttackCue(stage.Enemy, out var socket, out var local), "Missing attacking bone at runtime scale");
                    record["bone"] = ability.ParryCueBonePath; record["localPosition"] = new JArray(local.x, local.y, local.z);
                    float time = ability.ResolvePacedTime(window.x, stage.AttackSpeed) + .08f / stage.AttackSpeed;
                    stage.Sample(time);
                    RequireTempo(stage.NormalizedTime > ability.PreparationEnd && stage.NormalizedTime < ability.HitNormalizedTime, "Preview uses wrong phase");
                    record["previewNormalized"] = stage.NormalizedTime; record["previewAttackSpeed"] = stage.AttackSpeed;
                    for (int view = 0; view < 2; view++)
                    {
                        stage.SetView(view == 0 ? 2 : 0); stage.Render(720, 540);
                        string name = (string)row["id"] + (view == 0 ? "-game.png" : "-front.png");
                        Capture(stage.Surface, Path.Combine(directory, "Poses", name));
                        record[view == 0 ? "gamePose" : "frontPose"] = "Poses/" + name;
                    }
                }
                finally { stage.Dispose(); Object.DestroyImmediate(session); }
                RequireTempo(MonsterTunerPreviewStage.LiveStages == liveStages, "Owned stage leak");
                records.Add(record); index++; Progress("RUNNING");
            }
            catch (Exception error) { Finish("FAIL_NATIVE", error.ToString()); }
        }
    }
    static void RequireTempo(bool valid, string reason) { if (!valid) throw new InvalidOperationException(reason); }
    static JArray TempoScenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i))
        .Select(s => new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["handle"] = s.handle.GetHashCode() }));

    public static string StartAttackTempoStatsUI(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, outputRelative));
        var scenes = TempoScenes(); int live = MonsterTunerPreviewStage.LiveStages;
        var window = ScriptableObject.CreateInstance<MonsterTunerWindow>(); window.VerificationOnly = true;
        window.titleContent = new GUIContent("공격속도 · 공격간격 확인");
        window.position = new Rect(60, 60, 1440, 900); window.ShowUtility();
        int updates = 0; double deadline = EditorApplication.timeSinceStartup + 30;
        EditorApplication.update += Run;
        AssemblyReloadEvents.beforeAssemblyReload += Close;
        return "STAT_UI_VERIFICATION_QUEUED";
        void Close() { EditorApplication.update -= Run; AssemblyReloadEvents.beforeAssemblyReload -= Close; if (window != null) window.CloseVerification(); }
        void Run()
        {
            if (++updates < 4 && EditorApplication.timeSinceStartup < deadline) return;
            try
            {
                RequireTempo(!EditorApplication.isPlayingOrWillChangePlaymode && window != null && window.rootVisualElement.panel != null, "UI unavailable");
                var session = Get<MonsterTunerSession>(window, "session"); var stage = Get<MonsterTunerPreviewStage>(window, "stage");
                RequireTempo(session != null && stage.Actor != null, "No loaded actor in window");
                Set(window, "tab", 5); Call(window, "BuildFields");
                var root = window.rootVisualElement;
                var speedField = root.Q("variant|attackSpeedMultiplier")?.Q<FloatField>();
                var intervalField = root.Q("variant|attackInterval")?.Q<FloatField>();
                RequireTempo(speedField != null && intervalField != null, "Both native stat controls required");
                float beforeSpeed = stage.AttackSpeed;
                Change(speedField, speedField.value * .8f); Change(intervalField, .65f);
                RequireTempo(Mathf.Abs(stage.AttackSpeed - beforeSpeed * .8f) < .001f, "Draft speed not consumed by actual preview");
                RequireTempo(Mathf.Abs(session.Value("variant", "attackInterval").number - .65f) < .001f, "Interval draft not connected");
                CapturePanel(window, Path.Combine(directory, "monster-stats-ui.png"));
                Change(speedField, 1.5f);
                float gradeSpeed = session.Definition.Grade != null ? session.Definition.Grade.AttackSpeedMultiplier : 1f;
                RequireTempo(Mathf.Abs(stage.AttackSpeed - 1.5f * gradeSpeed) < .001f, "Authored speed was clipped by the level growth cap");
                RequireTempo(Mathf.Abs(EnemyRuntimeStats.ResolveAuthoredAttackSpeed(1.5f, 1f, 1.2f) - 1.5f) < .001f
                    && Mathf.Abs(EnemyRuntimeStats.ResolveAuthoredAttackSpeed(1.5f, 2f, 1.2f) - 1.8f) < .001f, "Runtime authored speed/growth separation");
                session.Discard(); Close();
                RequireTempo(MonsterTunerPreviewStage.LiveStages == live, "UI preview did not return resources");
                RequireTempo(JToken.DeepEquals(scenes, TempoScenes()), "UI changed product scenes");
                File.WriteAllText(Path.Combine(directory, "stats-ui-result.json"), new JObject { ["status"] = "PASS_STATS_UI_PREVIEW",
                    ["nativeControls"] = new JArray("공격속도 (배율)", "공격간격 (초)"), ["draftSpeedPreviewApplied"] = true,
                    ["authored1_5SpeedNotCapped"] = true, ["ownedWindowClosed"] = true }.ToString());
                StatsSaveFixture(directory);
            }
            catch (Exception error) { Close(); File.WriteAllText(Path.Combine(directory, "stats-ui-result.json"), new JObject { ["status"] = "FAIL_STATS_UI", ["error"] = error.ToString() }.ToString()); }
        }
    }
    static void StatsSaveFixture(string directory)
    {
        const string fixture = "Assets/Editor/Testers/Fixtures/MonsterAttackCueTempo";
        RequireTempo(!AssetDatabase.IsValidFolder(fixture), "Previous stats fixture requires inspection");
        var row = JObject.Parse(File.ReadAllText(Path.Combine(directory, "plan.json")))["records"].First;
        var original = AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definitionPath"]);
        string originalHash = MonsterTunerStamp.FileHash(AssetDatabase.GetAssetPath(original.Variant));
        MonsterTunerSession session = null;
        try
        {
            void Folder(string path) { string parent = Path.GetDirectoryName(path).Replace('\\', '/'); if (!AssetDatabase.IsValidFolder(parent)) Folder(parent); if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
            Folder(fixture);
            var variant = Object.Instantiate(original.Variant); AssetDatabase.CreateAsset(variant, fixture + "/Variant.asset");
            var definition = Object.Instantiate(original); definition.ConfigureIdentity("AttackCueTempoFixture", "공격 템포 검증");
            definition.ConfigureComposition(original.Species, original.Grade, variant, original.ActorPrefab);
            AssetDatabase.CreateAsset(definition, fixture + "/Definition.asset");
            session = MonsterTunerSession.Create(definition, false); var catalog = new MonsterTunerCatalog(); catalog.Refresh();
            MonsterTunerWriter.FixtureRoot = fixture;
            var speed = session.Value("variant", "attackSpeedMultiplier"); speed.number = .8f; session.Set("variant", "attackSpeedMultiplier", speed, "공격속도");
            var interval = session.Value("variant", "attackInterval"); interval.number = .65f; session.Set("variant", "attackInterval", interval, "공격간격");
            var saved = MonsterTunerWriter.Save(session, catalog); RequireTempo(saved.Success, saved.Message);
            var reopened = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(fixture + "/Definition.asset");
            RequireTempo(Mathf.Approximately(reopened.ResolveRuntimeStats().AttackInterval, .65f), "Saved interval not consumed by runtime stats");
            RequireTempo(Mathf.Approximately(reopened.Variant.AttackSpeedMultiplier, .8f), "Saved attack speed not restored");
            session = ReplaceSession(session, reopened);
            interval = session.Value("variant", "attackInterval"); interval.number = -.1f; session.Set("variant", "attackInterval", interval, "공격간격");
            RequireTempo(MonsterTunerWriter.Validate(session, catalog).Any(e => e.Contains("공격간격")), "Invalid interval accepted");
            RequireTempo(originalHash == MonsterTunerStamp.FileHash(AssetDatabase.GetAssetPath(original.Variant)), "Source variant touched");
            File.WriteAllText(Path.Combine(directory, "stats-save-result.json"), new JObject { ["status"] = "PASS_STATS_SAVE_RUNTIME", ["speed"] = .8f,
                ["intervalSeconds"] = .65f, ["negativeIntervalRejected"] = true, ["sourceVariantPreserved"] = true, ["fixturePath"] = fixture }.ToString());
        }
        finally
        {
            MonsterTunerWriter.FixtureRoot = null; if (session != null) Object.DestroyImmediate(session);
            if (AssetDatabase.IsValidFolder(fixture))
            {
                bool trashed = AssetDatabase.MoveAssetToTrash(fixture);
                File.WriteAllText(Path.Combine(directory, "fixture-recycle-result.json"), new JObject { ["path"] = Path.GetFullPath(fixture),
                    ["moveAssetToTrash"] = trashed, ["sourceAbsent"] = !Directory.Exists(fixture) }.ToString());
                RequireTempo(trashed && !Directory.Exists(fixture), "Owned fixture could not be recycled");
            }
        }
    }
    static MonsterTunerSession ReplaceSession(MonsterTunerSession old, EnemyDefinition definition)
    { Object.DestroyImmediate(old); return MonsterTunerSession.Create(definition, false); }

    public static string FinalizeAttackCueTempoNative(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,outputRelative));
        var preview=JObject.Parse(File.ReadAllText(Path.Combine(directory,"native-verification.json")));
        var receipt=JObject.Parse(File.ReadAllText(Path.Combine(directory,"apply-result.json")));
        RequireTempo((int)preview["completed"]==35&&preview["records"].Count()==35,"Native poses incomplete");
        foreach(var row in receipt["saved"])
        {
            var ability=AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>((string)row["path"]);
            RequireTempo(ability!=null&&!EditorUtility.IsDirty(ability)&&ability.HasAttackCue
                &&JToken.DeepEquals(JObject.Parse(EditorJsonUtility.ToJson(ability)),row["fields"])
                &&AssetDatabase.AssetPathToGUID((string)row["path"])==(string)row["guid"],"Saved ability changed after capture");
        }
        var catalog=AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");
        var definitions=Enumerable.Range(0,catalog.Count).Select(catalog.GetDefinition).ToArray();
        RequireTempo(definitions.Length==56&&definitions.All(d=>Mathf.Approximately(d.ResolveRuntimeStats().AttackInterval,1f)),"Saved active catalog interval mismatch");
        File.WriteAllText(Path.Combine(directory,"native-finalization.json"),new JObject{["status"]="PASS_NATIVE_ACTIVE_ROSTER_CUE_TEMPO",
            ["actors"]=35,["speedConditions"]=175,["actualPoseViews"]=70,["activeRegularActors"]=56,
            ["originalPreviewResult"]=preview["status"],["savedAssetFieldsRechecked"]=true,
            ["records"]=new JArray(definitions.Select(d=>new JObject{["id"]=d.EnemyId,["attackSpeed"]=d.ResolveRuntimeStats().AttackSpeedMultiplier,
                ["attackIntervalSeconds"]=d.ResolveRuntimeStats().AttackInterval}))}.ToString());
        return "PASS_NATIVE_ACTIVE_ROSTER_CUE_TEMPO";
    }

    public static string QueueAttackCueTempoReturn(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
        string directory = Path.GetFullPath(Path.Combine(workspace, outputRelative));
        string allowed = Path.GetFullPath(Path.Combine(workspace, "개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        RequireTempo(directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), "Private monster output required");
        string receipt = Path.Combine(directory, "final-editor-return.json");
        RequireTempo(!File.Exists(receipt), "Inspect previous return receipt before rerunning");
        double deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.update += Run;
        AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        return "ATTACK_CUE_TEMPO_RETURN_QUEUED";

        void Detach() { EditorApplication.update -= Run; AssemblyReloadEvents.beforeAssemblyReload -= Cancel; }
        void Cancel()
        {
            Detach();
            File.WriteAllText(receipt, new JObject { ["status"] = "CANCELLED_RELOAD", ["utc"] = DateTime.UtcNow }.ToString());
        }
        void Run()
        {
            Detach();
            var result = new JObject { ["utc"] = DateTime.UtcNow, ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id, ["project"] = Application.dataPath };
            try
            {
                RequireTempo(EditorApplication.timeSinceStartup < deadline, "Return deadline expired");
                MonsterBlenderParryR6Builder.RequireIdle();
                RequireTempo(string.IsNullOrEmpty(SessionState.GetString("Overburst.WeakAttackPlayerLoop.plan", "")), "Another capture is pending");
                RequireTempo(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
                    && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
                    && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")), "Another isolated account is prepared");
                result["nativeFinalization"] = FinalizeAttackCueTempoNative(Path.Combine(outputRelative, "PlacementReview"));
                var scenes = TempoScenes(); result["scenesBefore"] = scenes;
                int desired = AssetDatabase.DesiredWorkerCount, standby = EditorUserSettings.standbyImportWorkerCount;
                result["managedBytesBefore"] = GC.GetTotalMemory(false);
                result["desiredWorkersBefore"] = desired; result["standbyWorkersBefore"] = standby;
                try
                {
                    EditorUtility.UnloadUnusedAssetsImmediate(true);
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    AssetDatabase.DesiredWorkerCount = 0; EditorUserSettings.standbyImportWorkerCount = 0;
                    AssetDatabase.ForceToDesiredWorkerCount();
                }
                finally { AssetDatabase.DesiredWorkerCount = desired; EditorUserSettings.standbyImportWorkerCount = standby; }
                IsolatedSavePlayGuard.UseRealAccount();
                result["managedBytesAfter"] = GC.GetTotalMemory(false);
                result["desiredWorkersAfter"] = AssetDatabase.DesiredWorkerCount; result["standbyWorkersAfter"] = EditorUserSettings.standbyImportWorkerCount;
                result["guardChoice"] = IsolatedSavePlayGuard.RequiresAccountChoice; result["guardActive"] = IsolatedSavePlayGuard.ActiveDirectory;
                result["guardPrepared"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
                result["guardExpires"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "");
                result["environment"] = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
                result["testPlan"] = SessionState.GetString("Overburst.WeakAttackPlayerLoop.plan", "");
                result["captureDelta"] = Time.captureDeltaTime;
                result["startScene"] = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
                result["liveStages"] = MonsterTunerPreviewStage.LiveStages;
                result["ownedCaptureCameras"] = Resources.FindObjectsOfTypeAll<Camera>().Count(c => c.name == "Owned monster parry video camera");
                result["scenesAfter"] = TempoScenes();
                bool pass = JToken.DeepEquals(scenes, result["scenesAfter"])
                    && !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
                    && string.IsNullOrEmpty((string)result["guardPrepared"]) && string.IsNullOrEmpty((string)result["guardExpires"])
                    && string.IsNullOrEmpty((string)result["environment"]) && string.IsNullOrEmpty((string)result["testPlan"])
                    && Time.captureDeltaTime == 0 && (int)result["ownedCaptureCameras"] == 0
                    && AssetDatabase.DesiredWorkerCount == desired && EditorUserSettings.standbyImportWorkerCount == standby;
                result["status"] = pass ? "PASS_IDLE_REAL_ACCOUNT_RETURN" : "FAIL_RETURN";
            }
            catch (Exception error) { result["status"] = "NOT_RUN_OR_FAILED"; result["reason"] = error.ToString(); }
            File.WriteAllText(receipt, result.ToString());
        }
    }
}
