using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;

public static partial class PerfectParryContactVerifier
{
    public static IEnumerator Compare(string output)
    {
        var actor = PlayerContext.GetOrCreate().CurrentActor;
        var melee = actor.GetComponent<MeleeRuntime>(); var parry = actor.GetComponent<PlayerParryController>();
        var energy = actor.GetComponent<OverburstElementEnergy>(); bool ownEnergy = energy == null;
        if (ownEnergy) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
        var profile = PerfectParryContactProfile.Current;
        bool enabledBefore = profile.enhancedPresentation, afterimageBefore = profile.upswingAfterimage;
        float captureBefore = Time.captureDeltaTime, maxHpBefore = actor.Health.MaxHp;
        ItemData weaponBefore = actor.Equipment.CurrentWeaponItem, gemBefore = actor.Equipment.EquippedElementGem;
        var privateFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        var equipGem = typeof(PlayerEquipment).GetMethod("SetElementGem", privateFlags);
        EnemySpawnService spawn = null; EnemyActor enemy = null; EnemyAbilitySet set = null;
        PerfectParryComparisonRecorder movie = null; var cases = new List<object>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        try
        {
            // Record native renders and native PCM independently, then conform recorded timestamps offline.
            Time.captureDeltaTime = 0;
            melee.CancelCurrentAttackState(); actor.Health.SetMaxHp(1000000, true);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Actual greatsword equipped");
            var fire = AssetDatabase.LoadAssetAtPath<ElementGemItemData>("Assets/ProjectOverburst/Resources/Items/ElementGems/EG_Fire_Common.asset");
            equipGem.Invoke(actor.Equipment, new object[] { new ItemData(fire, 1, ItemGrade.Common) });
            float timeout = Time.unscaledTime + 30f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            { Check(Time.unscaledTime < timeout, "Hideout boot completes"); yield return null; }
            var trial = EnemyThemeTrialHarness.Current;
            if (!trial.InArena) trial.ToggleArena(); yield return WaitContact(1f);
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "Product trial spawn service");
            foreach (var table in trial.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "Trial catalog registration");
            var definition = trial.tables.SelectMany(t => t.Entries).First(e => e.definition != null && e.definition.EnemyId == "CavernMutants_Ursacetus").definition;
            var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility).First(a => a != null && a.name == "CavernMutants_Ursacetus_2HandsSmashAttack");
            Check(ability.IsParryable, "Deployed two-hand smash is parryable");
            set = ScriptableObject.CreateInstance<EnemyAbilitySet>(); var serialized = new SerializedObject(set);
            serialized.FindProperty("abilitySetId").stringValue = "perfect-parry-contact-comparison";
            var entries = serialized.FindProperty("abilities"); entries.arraySize = 1; entries.GetArrayElementAtIndex(0).objectReferenceValue = ability;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
            var movement = actor.GetComponent<PlayerMovement>(); Vector3 forward = movement.ResolveMoveDirection(new Vector2(-1, 1).normalized); forward.y = 0; forward.Normalize();
            Vector3 origin = actor.transform.position;
            movie = actor.gameObject.AddComponent<PerfectParryComparisonRecorder>();
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            foreach (string version in new[] { "A_Legacy", "B_Contact", "C_ContactAfterimage" })
            {
                profile.enhancedPresentation = version != "A_Legacy"; profile.upswingAfterimage = version == "C_ContactAfterimage";
                melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
                ActorTeleportUtility.TeleportSafely(actor.transform, origin, Quaternion.LookRotation(forward));
                movement.ResetMotionAfterTeleport(); actor.GetComponent<PlayerCombatFacingController>()?.ResetAfterTeleport();
                Vector3 spawnPoint = origin + forward * 1.76f;
                Check(Physics.Raycast(spawnPoint + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "Trial ground");
                Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-forward), actor.transform), out enemy), "Actual enemy spawned");
                enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(100000, true);
                enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; enemy.AbilityController.Configure(set, 1.75f, 1f);
                energy.BindWeapon(actor.Equipment.CurrentWeaponItem.runtimeInstanceId, actor.Equipment.ActiveElement, actor.Equipment.EquippedElementGem.runtimeInstanceId, actor.Equipment.GemRevision);
                typeof(OverburstElementEnergy).GetField("<Amount>k__BackingField", privateFlags).SetValue(energy, 100f);
                yield return WaitContact(1.5f);
                Check(parry != null, "Product parry controller is present after arena entry");
                Check(movie != null, "Owned recording component survives arena reset");
                Check(actor.GetComponent<PerfectParryContactPresenter>() != null, "Product contact presenter is present before recording");
                int success = parry.SuccessCount, feedback = parry.FeedbackCount;
                float hp = actor.Health.CurrentHp; var presenter = actor.GetComponent<PerfectParryContactPresenter>(); int main = presenter.MainCount; var afterimage = actor.GetComponent<PerfectParryWeaponAfterimage>(); int captures = afterimage.CapturedCount;
                movie.Begin(Path.Combine(output, version)); yield return WaitContact(1f);
                Check(enemy.AbilityController.TryStart(actor.transform), "Actual source attack commits");
                timeout = Time.unscaledTime + 8f;
                while (!enemy.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()))
                { Check(Time.unscaledTime < timeout, "Source reaches the parry threat phase"); yield return null; }
                Check(melee.TryStartHeavyAttack(forward) == WeaponActionResult.Accepted, "Heavy action accepts perfect gauge");
                while (melee.IsAttackInProgress) { Check(Time.unscaledTime < timeout, "Counter action completes"); yield return null; }
                yield return WaitContact(1.5f);
                object take = movie.End(); var data = JObject.FromObject(take);
                Check((string)data["status"] == "PASS", "Native real-time video and audio clocks align");
                Check(parry.SuccessCount - success == 1 && parry.FeedbackCount - feedback == 1, "One parry and feedback event");
                Check(parry.ActionGrade == ParryGrade.Perfect && Mathf.Abs(actor.Health.CurrentHp - hp) < .01f, "Perfect grade prevents damage");
                Check(ParryFeedbackService.LastAdditionalTingCount == (int)ParryGrade.Perfect, "All original perfect-parry ting layers remain");
                Check(presenter.MainCount - main == (version == "A_Legacy" ? 0 : 1), "Selected legacy/enhanced route");
                Check(afterimage.CapturedCount - captures == 0 ? version != "C_ContactAfterimage" : version == "C_ContactAfterimage", "Upswing weapon poses only follow the selected perfect enhancement");
                Check(afterimage.ActiveCount == 0 && !afterimage.IsEmitting, "Parry blade poses finish before the next action");
                var recorded = JArray.Parse(File.ReadAllText(Path.Combine(output, version, "frames.json")));
                int pullbackCaptures = 0;
                for (int i = 1; i < recorded.Count; i++)
                {
                    int added = (int)recorded[i]["weaponCaptures"] - (int)recorded[i - 1]["weaponCaptures"];
                    Check(added <= 0 || (bool)recorded[i]["afterimagePhase"], "Weapon poses never emit in the counter/bridge");
                    if (added > 0 && !(bool)recorded[i]["upswing"]) pullbackCaptures += added;
                }
                Check(version == "C_ContactAfterimage" ? pullbackCaptures > 0 : pullbackCaptures == 0, "Only enhanced perfect parry records the post-upswing pullback");
                cases.Add(new { version, pullbackCaptures, capturedWeaponPoses = afterimage.CapturedCount - captures, take, grade = parry.ActionGrade.ToString(), gaugeAfter = energy.Amount,
                    sourceCancelled = !enemy.AbilityController.IsExecuting, mainDelta = presenter.MainCount - main });
                File.WriteAllText(Path.Combine(output, "comparison.json"), JsonConvert.SerializeObject(new { status = "RUNNING", cases }, Formatting.Indented));
                spawn.Release(enemy); enemy = null;
            }
            File.WriteAllText(Path.Combine(output, "comparison.json"), JsonConvert.SerializeObject(new { status = "PASS", cases }, Formatting.Indented));
        }
        finally
        {
            if (movie != null) { movie.End(); UnityEngine.Object.DestroyImmediate(movie); }
            if (enemy != null && enemy.IsLeased) spawn?.Release(enemy);
            if (set != null) UnityEngine.Object.DestroyImmediate(set);
            melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
            profile.enhancedPresentation = enabledBefore; profile.upswingAfterimage = afterimageBefore; Time.captureDeltaTime = captureBefore;
            actor.Health.SetMaxHp(maxHpBefore, true);
            if (weaponBefore != null) actor.Equipment.EquipWeaponItem(weaponBefore); else actor.Equipment.ClearCurrentWeapon();
            equipGem.Invoke(actor.Equipment, new object[] { gemBefore });
            if (ownEnergy && energy != null) UnityEngine.Object.DestroyImmediate(energy);
        }
    }
    private static IEnumerator WaitContact(float seconds)
    { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) yield return null; }
}

// Native video and PCM are independent so uneven render cadence cannot block audio encoding.
// The suggested audio count follows scaled time during hitstop, so use the capture clock.
public sealed class PerfectParryComparisonRecorder : MonoBehaviour
{
    private MediaEncoder encoder;
    private BinaryWriter wave;
    private RenderTexture target;
    private Texture2D texture;
    private bool audioOwned;
    private string directory;
    private int frames;
    private long samples;
    private float peak;
    private string error;
    private double began;
    private readonly List<object> observations = new List<object>();
    public void Begin(string path)
    {
        if (Screen.width != 1920 || Screen.height != 1080 || AudioSettings.speakerMode != AudioSpeakerMode.Stereo)
            throw new InvalidOperationException("FHD Game View and stereo audio required.");
        directory = path; Directory.CreateDirectory(path); observations.Clear(); frames = 0; samples = 0; peak = 0; error = null;
        try
        {
            audioOwned = AudioRenderer.Start(); if (!audioOwned) throw new InvalidOperationException("Audio capture already owned.");
            target = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32); target.Create();
            texture = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
            encoder = new MediaEncoder(Path.Combine(path, "source-frames.mp4"), new VideoTrackEncoderAttributes
                { frameRate = new MediaRational(60), width = 1920, height = 1080, targetBitRate = 32000000, includeAlpha = false });
            wave = new BinaryWriter(File.Create(Path.Combine(path, "native-audio.wav")));
            WriteWaveHeader(0);
            began = Time.realtimeSinceStartupAsDouble; StartCoroutine(Record());
        }
        catch { End(); throw; }
    }
    private IEnumerator Record()
    {
        var end = new WaitForEndOfFrame();
        while (encoder != null)
        {
            yield return end;
            if (encoder == null) yield break;
            var prior = RenderTexture.active;
            try
            {
                double presentationSeconds = Time.realtimeSinceStartupAsDouble - began;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(target); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); texture.Apply(false, false);
                int suggestedCount = AudioRenderer.GetSampleCountForCaptureFrame();
                AudioSettings.GetDSPBufferSize(out int block, out _);
                double captureSeconds = Time.realtimeSinceStartupAsDouble - began;
                long wantedSamples = (long)Math.Floor(captureSeconds * AudioSettings.outputSampleRate / block) * block;
                int count = (int)Math.Max(0L, wantedSamples - samples);
                CaptureAudio(count);
                int targetFrame = frames + 1; // Encode each actual render once; conform its timestamp offline.
                int before = frames;
                while (frames < targetFrame)
                { if (!encoder.AddFrame(texture)) throw new InvalidOperationException("Video frame rejected."); frames++; }
                var actor = PlayerContext.Instance.CurrentActor;
                var parry = actor.GetComponent<PlayerParryController>(); var presenter = actor.GetComponent<PerfectParryContactPresenter>();
                observations.Add(new { startFrame = before, endFrame = frames, presentationSeconds, realSeconds = Time.realtimeSinceStartupAsDouble - began,
                    audioSeconds = samples / (double)AudioSettings.outputSampleRate, suggestedCount, requestedCount = count, Time.timeScale, Time.unscaledDeltaTime, parry.SuccessCount, parry.FeedbackCount,
                    presenter.MainCount, presenter.AdditionalCount,
                    weaponPoses = actor.GetComponent<PerfectParryWeaponAfterimage>()?.ActiveCount ?? 0, weaponCaptures = actor.GetComponent<PerfectParryWeaponAfterimage>()?.CapturedCount ?? 0, afterimagePhase = actor.GetComponent<MeleeRuntime>().IsHeavyParryBladeMotion, upswing = actor.GetComponent<MeleeRuntime>().IsHeavyParryUpswing });
            }
            catch (Exception e) { error = e.ToString(); End(); }
            finally { RenderTexture.active = prior; }
        }
    }
    public object End()
    {
        if (encoder == null && !audioOwned && target == null) return null;
        StopAllCoroutines(); double elapsed = Time.realtimeSinceStartupAsDouble - began;
        try
        {
            if (audioOwned && wave != null)
            {
                AudioSettings.GetDSPBufferSize(out int block, out _);
                long wanted = (long)Math.Floor(elapsed * AudioSettings.outputSampleRate / block) * block;
                CaptureAudio((int)Math.Max(0L, wanted - samples));
            }
        }
        catch (Exception e) { error = e.ToString(); }
        try { encoder?.Dispose(); }
        finally
        {
            encoder = null;
            try { if (audioOwned) AudioRenderer.Stop(); }
            finally
            {
                audioOwned = false;
                if (wave != null) { WriteWaveHeader((int)(samples * 4)); wave.Dispose(); wave = null; }
                if (target != null) { target.Release(); DestroyImmediate(target); target = null; }
                if (texture != null) { DestroyImmediate(texture); texture = null; }
            }
        }
        double audioSeconds = samples / (double)AudioSettings.outputSampleRate;
        var result = new { status = error == null && frames > 0 && peak > 0 && Math.Abs(audioSeconds - elapsed) < .3 ? "PASS" : "FAIL", error,
            frames, fps = 60, sourceVideoDuration = frames / 60d, duration = audioSeconds, needsRealtimeConform = true, realDuration = elapsed, audioDuration = audioSeconds, audioPeak = peak, actualGameView = true, actualAudio = true, timeCaptureDelta = Time.captureDeltaTime };
        File.WriteAllText(Path.Combine(directory, "take.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        File.WriteAllText(Path.Combine(directory, "frames.json"), JsonConvert.SerializeObject(observations));
        return result;
    }
    private void CaptureAudio(int count)
    {
        if (count <= 0) return;
        using (var audio = new NativeArray<float>(count * 2, Allocator.Temp))
        {
            if (!AudioRenderer.Render(audio)) throw new InvalidOperationException("Native audio capture failed.");
            for (int i = 0; i < audio.Length; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(audio[i]));
                wave.Write((short)Mathf.RoundToInt(Mathf.Clamp(audio[i], -1f, 1f) * 32767f));
            }
            samples += count;
        }
    }
    private void WriteWaveHeader(int bytes)
    {
        wave.BaseStream.Position = 0;
        wave.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); wave.Write(36 + bytes);
        wave.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); wave.Write(16);
        wave.Write((short)1); wave.Write((short)2); wave.Write(AudioSettings.outputSampleRate);
        wave.Write(AudioSettings.outputSampleRate * 4); wave.Write((short)4); wave.Write((short)16);
        wave.Write(System.Text.Encoding.ASCII.GetBytes("data")); wave.Write(bytes);
        wave.BaseStream.Position = wave.BaseStream.Length;
    }
    private void OnDestroy() { End(); }
}
