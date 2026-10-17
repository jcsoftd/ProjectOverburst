using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Uses the real input buffer, attacks, hitboxes, damage and loot; never injects damage or energy.
public static class CombatHitchInputProbe
{
    public static IEnumerator Fight(MapMonsterField field, string output, Action<bool, string> check, double seconds = 75, IReadOnlyList<MapMonsterField> tourFields = null, Func<bool> stopRequested = null, double minimumHitchSeconds = 10)
    {
        var player = PlayerContext.Instance.CurrentActor;
        var input = PlayerInputFacade.Current;
        var movement = player.GetComponent<PlayerMovement>();
        var evade = player.GetComponent<PlayerEvadeController>();
        var melee = player.GetComponent<MeleeRuntime>();
        var previousDevices = input.RuntimeAsset.devices;
        var previousSettings = InputSystem.settings;
        string previousJson = EditorJsonUtility.ToJson(previousSettings);
        InputSettings settings = null; Keyboard keyboard = null; Mouse mouse = null;
        var actors = new List<EnemyActor>(64);
        var observed = new HashSet<CombatHealth>();
        var samples = new List<object>(Math.Max(256, (int)(seconds * 3)));
        var tourActions = new List<object>();
        int tourIndex = 0;
        string stopReason = "duration";
        int triggerFrame = -1, nativeBeforeDrain = -1, nativeAfterDrain = -1, drainFrames = 0;
        double triggerSeconds = -1;
        double nextTour = 60, lastDamage = 0, lastTour = 0;
        var keys = new KeyboardState[32];
        for (int mask = 0; mask < keys.Length; mask++)
        {
            var list = new List<Key>(5);
            if ((mask & 1) != 0) list.Add(Key.W);
            if ((mask & 2) != 0) list.Add(Key.S);
            if ((mask & 4) != 0) list.Add(Key.A);
            if ((mask & 8) != 0) list.Add(Key.D);
            if ((mask & 16) != 0) list.Add(Key.LeftShift);
            keys[mask] = new KeyboardState(list.ToArray());
        }
        EnemyActor target = null;
        int weak = 0, heavy = 0, elemental = 0, kills = 0, evades = 0, inputFrames = 0;
        int leftPresses = 0, rightPresses = 0;
        bool left = false, right = false, priorLeft = false, priorRight = false;
        int keyMask = 0; Vector2 aim = Vector2.zero;
        float distance = 0; var lastPosition = player.transform.position;
        double start = Time.realtimeSinceStartupAsDouble, nextTarget = 0;
        void Hit(CombatHealth health, DamageInfo info, float actual, bool fatal)
        {
            if (actual <= 0 || (info.playerAttackKind & (PlayerAttackKind.Weak | PlayerAttackKind.Heavy | PlayerAttackKind.Elemental)) == 0) return;
            lastDamage = Time.realtimeSinceStartupAsDouble - start;
            if ((info.playerAttackKind & PlayerAttackKind.Heavy) != 0) heavy++;
            else if ((info.playerAttackKind & PlayerAttackKind.Weak) != 0) weak++;
            else if ((info.playerAttackKind & PlayerAttackKind.Elemental) != 0) elemental++;
            if (fatal) kills++;
        }
        void Evaded(PlayerEvadeType type) => evades++;
        void Send()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || keyboard == null || mouse == null) return;
            if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
            if (!mouse.enabled) InputSystem.EnableDevice(mouse);
            InputSystem.QueueStateEvent(keyboard, keys[keyMask]);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = aim, buttons = (ushort)((left ? 1 : 0) | (right ? 2 : 0)) });
            inputFrames++;
        }
        try
        {
            settings = UnityEngine.Object.Instantiate(previousSettings);
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = settings;
            keyboard = InputSystem.AddDevice<Keyboard>("OwnedCombatKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("OwnedCombatMouse");
            input.RuntimeAsset.devices = new InputDevice[] { keyboard, mouse };
            check(player.Equipment.CurrentWeaponItem != null && player.Equipment.ActiveElement == WeaponElement.Fire, "Input combat retains the fire weapon prepared before entry");
            melee.SetManualInputEnabled(true);
            InputSystem.onBeforeUpdate += Send; evade.OnEvadeStarted += Evaded;
            start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - start < seconds)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - start;
                if (elapsed > minimumHitchSeconds && elapsed - lastTour > 2 && stopRequested?.Invoke() == true) { stopReason = "hitch_limit"; triggerFrame = Time.frameCount; triggerSeconds = elapsed; break; }
                distance += Vector3.Distance(lastPosition, player.transform.position); lastPosition = player.transform.position;
                if (tourFields != null && tourFields.Count > 1 && (elapsed >= nextTour || (elapsed - lastTour >= 25 && elapsed - lastDamage >= 20)))
                {
                    string reason = elapsed >= nextTour ? "scheduled" : "no damage for 20 seconds";
                    for (int attempt = 0; attempt < tourFields.Count; attempt++)
                    {
                        var candidate = tourFields[tourIndex++ % tourFields.Count];
                        if (candidate == field || candidate.FiniteArea.Cleared) continue;
                        var ground = candidate.FiniteArea.Ground;
                        var stand = ground.Points.OrderBy(p => (p - ground.Center).sqrMagnitude).First();
                        left = right = priorLeft = priorRight = false; keyMask = 0;
                        input.CombatInputs.Invalidate();
                        ActorTeleportUtility.TeleportSafely(player.transform, stand + Vector3.up * .12f, Quaternion.identity);
                        field = candidate; target = null; nextTarget = 0; lastPosition = player.transform.position;
                        MapSpawnPerformanceProbe.RecordFixtureTransition();
                        tourActions.Add(new { frame = Time.frameCount, seconds = elapsed, area = ground.Index, reason });
                        break;
                    }
                    lastTour = elapsed; nextTour = elapsed + 60;
                }
                if (elapsed >= nextTarget)
                {
                    actors.Clear(); field.CollectLivingActors(actors);
                    float nearest = float.MaxValue; target = null;
                    foreach (var actor in actors)
                    {
                        if (actor.Health && observed.Add(actor.Health)) actor.Health.OnDamageResolved += Hit;
                        float squared = (actor.transform.position - player.transform.position).sqrMagnitude;
                        if (squared < nearest) { nearest = squared; target = actor; }
                    }
                    samples.Add(new { frame = Time.frameCount, seconds = elapsed, x = player.transform.position.x, z = player.transform.position.z,
                        weak, heavy, elemental, kills, evades, area = field.FiniteArea.Ground.Index, alive = actors.Count, field.ReinforcementReleased, blocked = GameplayInputBlocker.IsGameplayInputBlocked });
                    nextTarget = elapsed + .4;
                }
                Vector3 offset = target ? target.transform.position - player.transform.position : field.FiniteArea.Ground.Center - player.transform.position;
                offset.y = 0;
                aim = Camera.main ? (Vector2)Camera.main.WorldToScreenPoint(player.transform.position + offset) : Vector2.zero;
                bool dodge = elapsed % 5.5 < .12 && elapsed > 1;
                Vector3 desired = dodge ? Vector3.Cross(Vector3.up, offset).normalized : offset.sqrMagnitude > 6.25f ? offset.normalized : Vector3.zero;
                float forward = Vector3.Dot(desired, movement.ResolveMoveDirection(Vector2.up));
                float side = Vector3.Dot(desired, movement.ResolveMoveDirection(Vector2.right));
                keyMask = (forward > .3f ? 1 : forward < -.3f ? 2 : 0) | (side < -.3f ? 4 : side > .3f ? 8 : 0) | (dodge ? 16 : 0);
                right = target && elapsed % 4.5 < .65 && elapsed > 1;
                left = target && !right && elapsed % .4 < .12;
                if (left && !priorLeft) leftPresses++; if (right && !priorRight) rightPresses++;
                priorLeft = left; priorRight = right;
                yield return null;
            }
            if (stopReason == "hitch_limit")
            {
                left = right = false; keyMask = 0; input.CombatInputs.Invalidate();
                nativeBeforeDrain = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
                double drainUntil = Time.realtimeSinceStartupAsDouble + 3;
                while ((drainFrames < 12 || UnityEditorInternal.ProfilerDriver.lastFrameIndex < nativeBeforeDrain + 8) && Time.realtimeSinceStartupAsDouble < drainUntil)
                { yield return null; drainFrames++; }
                nativeAfterDrain = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
                check(drainFrames >= 12 && nativeAfterDrain >= nativeBeforeDrain + 8, "Post-hitch native profiler frames arrived before snapshot; input neutral during drain");
            }
        }
        finally
        {
            InputSystem.onBeforeUpdate -= Send;
            if (evade) evade.OnEvadeStarted -= Evaded;
            foreach (var health in observed) if (health) health.OnDamageResolved -= Hit;
            if (input) { input.CombatInputs.Invalidate(); input.RuntimeAsset.devices = previousDevices; }
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            InputSystem.settings = previousSettings;
            if (settings) UnityEngine.Object.DestroyImmediate(settings);
            bool restored = InputSystem.settings == previousSettings && EditorJsonUtility.ToJson(previousSettings) == previousJson;
            File.WriteAllText(Path.Combine(output, "input-combat.json"), JsonConvert.SerializeObject(new { weak, heavy, elemental, kills, evades,
                distance, inputFrames, leftPresses, rightPresses, restored, stopReason, triggerFrame, triggerSeconds, nativeBeforeDrain, nativeAfterDrain, drainFrames, stopFrame = Time.frameCount, stopArea = field.FiniteArea.Ground.Index, seconds = Time.realtimeSinceStartupAsDouble - start,
                scope = "Virtual keyboard/mouse -> production combat. Natural damage/energy/loot. Existing isolated fixture prevents player death; no enemy damage injection. Initial platform teleport precedes measurement. Optional tour relocations are fixture actions recorded separately; exclude their vicinity from ordinary combat frame statistics.", tourActions, samples }, Formatting.Indented));
        }
        bool diagnosticStop = stopReason == "hitch_limit";
        if (!diagnosticStop && tourFields != null && tourFields.Count > 1) check(tourActions.Count > 0, "Long combat exercised recorded platform fixture transitions");
        check(weak > 0 && heavy > 0 && (diagnosticStop || evades > 0) && kills > 0 && distance > 5,
            diagnosticStop ? "Early hitch capture contains movement and natural weak/heavy combat; full evade/tour coverage belongs to duration runs"
                : "Input combat exercised movement, evade, weak/heavy hits and natural kills");
    }
}
