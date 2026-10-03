using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Repeatable presentation capture: deployed elite attack -> actual Health resolution -> product knockdown.
// Uses the verifier's isolated boot, bounded runner, input restoration and real-account return.
public static class PlayerKnockdownPresentationRecorder
{
    const int Fps = 60, Width = 1920, Height = 1080;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(string output) => PlayerKnockdownVerifier.Run(output, true);
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
    static void Warp(PlayerActorRuntime actor, Vector3 position, Quaternion rotation)
    {
        actor.CharacterController.enabled = false; actor.transform.SetPositionAndRotation(position, rotation);
        actor.CharacterController.enabled = true; actor.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms();
    }
    static Vector2 CameraInput(PlayerMovement movement, Vector2 local)
    {
        Vector3 world = movement.transform.TransformDirection(new Vector3(local.x, 0, local.y));
        return new Vector2(Vector3.Dot(world, movement.ResolveMoveDirection(Vector2.right)), Vector3.Dot(world, movement.ResolveMoveDirection(Vector2.up)));
    }
    sealed class Scenario
    {
        public string id, title, detail;
        public float enemyYaw;
        public bool shift, wall;
        public Vector2 rise;
    }
    sealed class Frames : IDisposable
    {
        readonly MediaEncoder encoder;
        readonly RenderTexture target;
        readonly Texture2D texture;
        readonly Camera camera;
        bool audioStarted;
        bool disposed;
        public int Count { get; private set; }
        public long AudioSamples { get; private set; }
        public double AudioPeak { get; private set; }
        public Frames(string path, Camera source)
        {
            camera = source;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try
            {
                Require(AudioSettings.speakerMode == AudioSpeakerMode.Stereo, "Stereo capture expected");
                Require(AudioRenderer.Start(), "Audio output is already owned by another capture"); audioStarted = true;
                encoder = new MediaEncoder(path,
                    new VideoTrackEncoderAttributes { frameRate = new MediaRational(Fps), width = Width, height = Height,
                        includeAlpha = false, targetBitRate = 16000000, bitRateMode = UnityEditor.VideoBitrateMode.High },
                    new AudioTrackAttributes { sampleRate = new MediaRational(AudioSettings.outputSampleRate), channelCount = 2, language = "ko" });
            }
            catch { Dispose(); throw; }
        }
        public void Add()
        {
            var prior = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); texture.Apply(false, false);
                Require(encoder.AddFrame(texture), "Native FHD video frame rejected");
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                using (var samples = new NativeArray<float>(count * 2, Allocator.Temp))
                {
                    Require(AudioRenderer.Render(samples), "Native audio frame rejected");
                    if (count > 0) Require(encoder.AddSamples(samples), "Audio encoder rejected samples");
                    for (int i = 0; i < samples.Length; i++) AudioPeak = Math.Max(AudioPeak, Math.Abs(samples[i]));
                    AudioSamples += count;
                }
                Count++;
            }
            finally { RenderTexture.active = prior; }
        }
        public void Screenshot(string path)
            => File.WriteAllBytes(path, texture.EncodeToPNG());
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { encoder?.Dispose(); }
            finally
            {
                if (audioStarted) { AudioRenderer.Stop(); audioStarted = false; }
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }

    public static IEnumerator Record(string output)
    {
        PlayerActorRuntime actor = null; EnemyActor enemy = null; EnemySpawnService spawn = null;
        EnemyAbilitySet single = null; Gamepad pad = null; Keyboard keyboard = null; GameObject wall = null;
        Material wallMaterial = null; Frames frames = null;
        QuarterViewCamera quarter = null; OverburstCinemachineCameraRig rig = null; Camera camera = null;
        bool quarterEnabled = false, brainEnabled = false; int originalCaptureRate = Time.captureFramerate;
        Vector3 cameraPosition = default; Quaternion cameraRotation = default;
        var records = new List<object>(); string raw = Path.Combine(output, "Raw"); Directory.CreateDirectory(raw);
        try
        {
            float bootEnd = Time.time + 40;
            while (Time.time < bootEnd && (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene" || !Overburst.Persistence.AccountBootstrap.Ready)) yield return null;
            Require(Overburst.Persistence.AccountBootstrap.Ready, "Product boot timed out");
            Require(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "Presentation must use its isolated account");
            actor = PlayerContext.GetOrCreate().CurrentActor; Require(actor != null, "Product player missing");
            var arena = EnemyThemeTrialHarness.Current; Require(arena != null, "Product training arena missing");
            if (!arena.InArena) arena.ToggleArena(); yield return Wait(.5f);
            var weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Require(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Product weapon equip failed");
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); yield return Wait(1.2f);
            actor.Health.SetMaxHp(1000000, true);
            Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "Product spawn service missing");
            foreach (var table in arena.tables) Require(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), error);
            var definition = arena.tables.SelectMany(t => t.Entries).Select(e => e.definition)
                .First(d => d != null && d.EnemyId == "CavernMutants_Ursacetus" && d.Grade.GradeType == EnemyGradeType.Elite);
            var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(i => definition.AbilitySet.GetAbility(i))
                .First(a => a.IsMeleeStrongAttack && a.HitCount == 1);
            single = ScriptableObject.CreateInstance<EnemyAbilitySet>(); Set(single, "abilitySetId", "KnockdownPresentation"); Set(single, "abilities", new[] { ability });
            pad = InputSystem.AddDevice<Gamepad>(); keyboard = InputSystem.AddDevice<Keyboard>();
            var reaction = actor.GetComponent<PlayerKnockdownController>();
            var origin = actor.transform.position; camera = Camera.main; Require(camera != null, "Product camera missing");
            quarter = camera.GetComponent<QuarterViewCamera>();
            if (quarter == null) quarter = UnityEngine.Object.FindFirstObjectByType<QuarterViewCamera>();
            rig = quarter != null ? quarter.CinemachineRig : null;
            if (quarter != null) { quarterEnabled = quarter.enabled; quarter.enabled = false; }
            if (rig != null && rig.Brain != null) { brainEnabled = rig.Brain.enabled; rig.Brain.enabled = false; }
            cameraPosition = camera.transform.position; cameraRotation = camera.transform.rotation;
            var viewRotation = Quaternion.Euler(48, 65, 0);
            Vector3 focus = origin + new Vector3(0, 1.7f, -.45f);
            camera.transform.SetPositionAndRotation(focus + viewRotation * Vector3.back * 15f, viewRotation);
            Time.captureFramerate = Fps;
            var scenarios = new[] {
                new Scenario{id="01_front",title="전방 엘리트 강공격",detail="넉다운 후 일반 기상",enemyYaw=0},
                new Scenario{id="02_left_impact",title="좌측 엘리트 강공격",detail="충격 방향을 따라 넉백",enemyYaw=-90},
                new Scenario{id="03_right_impact",title="우측 엘리트 강공격",detail="충격 방향을 따라 넉백",enemyYaw=90},
                new Scenario{id="04_rear",title="후방 엘리트 강공격",detail="충격 방향을 따라 넉백",enemyYaw=180},
                new Scenario{id="05_rise_front",title="전방 빠른 기상",detail="누운 상태에서 Shift + 앞 방향",shift=true,rise=Vector2.up},
                new Scenario{id="06_rise_back",title="뒤로 구르기 기상",detail="누운 상태에서 Shift + 뒤 방향",shift=true,rise=Vector2.down},
                new Scenario{id="07_rise_left",title="왼쪽 구르기 기상",detail="누운 상태에서 Shift + 왼쪽",shift=true,rise=Vector2.left},
                new Scenario{id="08_rise_right",title="오른쪽 구르기 기상",detail="누운 상태에서 Shift + 오른쪽",shift=true,rise=Vector2.right},
                new Scenario{id="09_wall",title="뒤쪽 벽에 막힌 넉백",detail="벽 앞에서 멈춘 뒤 정상 기상",wall=true},
                new Scenario{id="10_diagonal",title="대각선 방향 기상",detail="Shift + 대각선 · 입력 방향으로 이동",shift=true,rise=new Vector2(-1,1).normalized}
            };
            foreach (var scenario in scenarios)
            {
                viewRotation = Quaternion.Euler(48, scenario.wall ? 110 : 65, 0);
                camera.transform.SetPositionAndRotation(focus + viewRotation * Vector3.back * 15f, viewRotation);
                reaction.ResetReaction(); actor.Health.ResetHealth();
                InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Warp(actor, origin, Quaternion.identity); yield return Wait(.25f);
                Vector3 towardEnemy = Quaternion.Euler(0, scenario.enemyYaw, 0) * Vector3.forward;
                float distance = Mathf.Max(1.2f, ability.MinimumRange + .2f);
                Require(spawn.TrySpawn(new EnemySpawnRequest(definition, origin + towardEnemy * distance,
                    Quaternion.LookRotation(-towardEnemy), actor.transform), out enemy), "Deployed elite spawn failed");
                enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(1000000, true);
                enemy.AbilityController.Configure(single, enemy.RuntimeStats.DamageMultiplier, 1); enemy.AbilityController.BeginStrongOnlyPass();
                if (scenario.wall)
                {
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Owned presentation wall";
                    wall.transform.position = origin + Vector3.back * 1.05f + Vector3.up * 1.1f;
                    wall.transform.localScale = new Vector3(5.5f, 2.2f, .25f);
                    wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); wallMaterial.color = new Color(.31f,.38f,.43f);
                    wall.GetComponent<Renderer>().sharedMaterial = wallMaterial; Physics.SyncTransforms();
                }
                yield return Wait(.5f);
                string path = Path.Combine(raw, scenario.id + ".mp4"); frames = new Frames(path, camera);
                IEnumerator Film(float seconds)
                {
                    int count = Mathf.RoundToInt(seconds * Fps);
                    for (int i = 0; i < count; i++) { frames.Add(); yield return null; }
                }
                yield return Film(.8f);
                Require(frames.AudioSamples > 0, "Capture must include native game audio samples");
                DamageInfo confirmed = default; float actualDamage = 0; int hits = 0;
                void Resolved(CombatHealth h, DamageInfo info, float actual, bool fatal)
                { if (info.source != null && info.source.GetComponentInParent<EnemyActor>() == enemy) { confirmed = info; actualDamage += actual; hits++; } }
                actor.Health.OnDamageResolved += Resolved;
                Vector3 beforeHit = actor.transform.position; int firstHitFrame = -1, riseFrame = -1, readyFrame = -1;
                string riseId = ""; float maxFallDistance = 0, riseDistance = 0; bool capturedDown = false;
                Vector3 riseStart = default;
                var travelFrames = new List<object>();
                var motionField = typeof(PlayerKnockdownController).GetField("motion", Private);
                var elapsedField = typeof(PlayerKnockdownController).GetField("elapsed", Private);
                var directionField = typeof(PlayerKnockdownController).GetField("travelDirection", Private);
                try
                {
                    Require(enemy.AbilityController.TryStart(actor.transform), "Actual elite ability did not start");
                    int maximum = 9 * Fps, postRecovery = 0;
                    for (int i = 0; i < maximum; i++)
                    {
                        if (reaction.IsActive && firstHitFrame < 0) firstHitFrame = frames.Count;
                        if (reaction.Phase == PlayerKnockdownPhase.Falling)
                        {
                            Vector3 fall = actor.transform.position - beforeHit; fall.y = 0; maxFallDistance = Mathf.Max(maxFallDistance, fall.magnitude);
                        }
                        if (scenario.shift && reaction.Phase == PlayerKnockdownPhase.Grounded)
                        {
                            InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.LeftShift));
                            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = CameraInput(actor.Movement, scenario.rise) });
                        }
                        if (reaction.Phase == PlayerKnockdownPhase.Rising && riseFrame < 0)
                        {
                            riseFrame = frames.Count; riseId = reaction.ActiveMotionId;
                            riseStart = actor.transform.position;
                            var expected = reaction.AnimationSet.SelectRise(reaction.AnimationSet.defaultRise.poseId, scenario.rise, scenario.shift);
                            Require(riseId == expected.id && reaction.IsEvadeRise == scenario.shift, "Recorded get-up differs from requested direction");
                            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.QueueStateEvent(pad, new GamepadState());
                        }
                        if (firstHitFrame >= 0 && !reaction.IsActive)
                        {
                            if (readyFrame < 0) readyFrame = frames.Count;
                            postRecovery++;
                        }
                        var motion = (PlayerKnockdownAnimationSet.Motion)motionField.GetValue(reaction);
                        float clipTime = (float)elapsedField.GetValue(reaction);
                        float progress = motion != null ? Mathf.Clamp01(clipTime / motion.clip.length) : 1;
                        if (riseFrame >= 0)
                        {
                            Vector3 riseDelta = actor.transform.position - riseStart; riseDelta.y = 0;
                            riseDistance = Mathf.Max(riseDistance, riseDelta.magnitude);
                        }
                        float physicalDistance = reaction.Phase == PlayerKnockdownPhase.Falling || reaction.Phase == PlayerKnockdownPhase.Grounded
                            ? maxFallDistance : riseDistance;
                        float plannedDistance = motion == null ? 0
                            : reaction.Phase == PlayerKnockdownPhase.Rising
                                ? ((Vector3)directionField.GetValue(reaction)).sqrMagnitude > .001f
                                    ? reaction.AnimationSet.ResolveRiseDistance(motion, reaction.IsEvadeRise) : 0
                                : reaction.AnimationSet.fallDistance;
                        travelFrames.Add(new {frame=frames.Count,phase=reaction.Phase.ToString(),motionId=motion?.id??riseId,
                            clipTime,progress,physicalDistance,plannedDistance,
                            curveValue=motion!=null?Mathf.Clamp01(motion.travel.Evaluate(progress)):1});
                        frames.Add();
                        if (reaction.Phase == PlayerKnockdownPhase.Grounded && firstHitFrame >= 0 && !capturedDown)
                        { frames.Screenshot(Path.Combine(raw, scenario.id + "_down.png")); capturedDown = true; }
                        if (postRecovery >= Fps) break;
                        yield return null;
                    }
                    Require(firstHitFrame >= 0 && riseFrame >= 0 && readyFrame >= 0 && actualDamage > 0 && hits == 1
                        && confirmed.enemyAbility == ability && confirmed.sourceAttackSequenceId > 0, "No actual collider-resolved elite knockdown/recovery");
                    Require(scenario.wall ? maxFallDistance < 1.2f : maxFallDistance > 2.0f, "Recorded physical knockback distance is invalid");
                    if (scenario.shift && scenario.rise.y <= 0)
                        Require(riseDistance > 1.14f && riseDistance <= 1.24f, "Recorded roll get-up must actually move 1.2m");
                    records.Add(new {scenario.id,scenario.title,scenario.detail,scenario.enemyYaw,scenario.shift,scenario.wall,
                        riseInput=scenario.rise.ToString(),enemy=definition.EnemyId,ability=ability.name,hits,actualDamage,
                        configuredHits=ability.HitCount,confirmed.sourceAttackSequenceId,impact=confirmed.direction.ToString("F4"),maxFallDistance,riseId,
                        riseDistance,travelFrames,
                        frames=frames.Count,firstHitFrame,riseFrame,readyFrame,sampleRate=AudioSettings.outputSampleRate,
                        audioSamples=frames.AudioSamples,audioPeak=frames.AudioPeak,file=Path.GetFileName(path)});
                }
                finally { actor.Health.OnDamageResolved -= Resolved; frames.Dispose(); frames = null; }
                File.WriteAllText(Path.Combine(output, "capture-scenes.json"), JsonConvert.SerializeObject(new {status="RECORDING",width=Width,height=Height,fps=Fps,records},Formatting.Indented));
                enemy.AbilityController.Cancel(); spawn.Release(enemy); enemy = null;
                if (wall != null) { UnityEngine.Object.Destroy(wall); wall = null; }
                if (wallMaterial != null) { UnityEngine.Object.Destroy(wallMaterial); wallMaterial = null; }
            }
            var profiles = reaction.AnimationSet.falls.Concat(reaction.AnimationSet.directionalRises)
                .Concat(new[] { reaction.AnimationSet.defaultRise }).Select(m => new {
                    m.id,length=m.clip.length,m.playbackSpeed,m.riseDistance,
                    samples=Enumerable.Range(0,121).Select(i=>new {progress=i/120f,value=Mathf.Clamp01(m.travel.Evaluate(i/120f))}).ToArray()
                }).ToArray();
            File.WriteAllText(Path.Combine(output, "capture-scenes.json"), JsonConvert.SerializeObject(new {status="PASS",width=Width,height=Height,fps=Fps,records,profiles},Formatting.Indented));
        }
        finally
        {
            frames?.Dispose(); Time.captureFramerate = originalCaptureRate;
            if (actor != null) actor.GetComponent<PlayerKnockdownController>()?.ResetReaction();
            if (enemy != null && spawn != null) { enemy.AbilityController.Cancel(); spawn.Release(enemy); }
            if (single != null) UnityEngine.Object.Destroy(single);
            if (wall != null) UnityEngine.Object.Destroy(wall); if (wallMaterial != null) UnityEngine.Object.Destroy(wallMaterial);
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad); if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (camera != null) camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            if (quarter != null) quarter.enabled = quarterEnabled;
            if (rig != null && rig.Brain != null) rig.Brain.enabled = brainEnabled;
        }
    }
}
