using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

// Analytic ground geometry with a reversible supplier Telegraph fallback; body cue stays separate.
[DefaultExecutionOrder(11100)]
public sealed class EnemyStrongAttackWarning : MonoBehaviour
{
    [SerializeField] private Transform cueSocket;
    [SerializeField] private Vector3 cueOffset;
    [SerializeField, Min(.01f)] private float cueScale = 1f;
    private Transform attackCueSocket;
    private Vector3 attackCueOffset;
    public Transform AttackCueSocket => attackCueSocket;
    // Runtime strike placement takes precedence over the common head cue.
    public void SetAttackCue(Transform socket, Vector3 offset)
    {
        attackCueSocket = socket != null && (socket == transform || socket.IsChildOf(transform)) ? socket : null;
        attackCueOffset = offset;
    }
    private GameObject visual;
    private MMF_Player signalFeel;
    private ParticleSystem signalParticles;
    private CombatTarget body;
    private bool parryable;
    private float radius, sectorInnerRadius;
    private float corridorHalfWidth = .4f;
    private int signalSequence;
    private int signalSocketIndex;
    private readonly GameObject[] telegraphInstances = new GameObject[3];
    private readonly ParticleSystem[][] telegraphSystems = new ParticleSystem[3][];
    private int activeTelegraph = -1;
    private ProceduralGroundIndicator procedural;
    private float warningLeadSeconds = 1f;
    private static EnemyTelegraphVisualLibrary telegraphLibrary;
    private static Camera signalCamera;
    private static readonly HashSet<EnemyStrongAttackWarning> ThreatSignals = new HashSet<EnemyStrongAttackWarning>();
    private bool signalPlayed;
    private int motionSignalStrike = -1;
    public int ParrySignalCount { get; private set; }
    private float[] radialSurfaceProfile, radialBorderProfile;
    public void SetRadialProfiles(float[] surfaceProfile, float[] borderProfile)
    { radialSurfaceProfile = surfaceProfile; radialBorderProfile = borderProfile; }
    // Registered by PlayerParryController; the warning never searches the scene for it.
    public static CombatTarget PlayerTarget { get; set; }
    // Parry-ready signals that are about to hit the player. Read by the player-side cue.
    public static int ActiveThreatSignalCount => ThreatSignals.Count;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { ThreatSignals.Clear(); PlayerTarget = null; }
    public bool IsVisible => visual != null && visual.activeSelf;
    public bool UsesStandardIndicator => procedural != null && procedural.gameObject.activeSelf;
    public bool FinalSignal { get; private set; }

    public void Show(float size, bool canParry, float angle = 360f,
        bool charge = false, bool useTelegraph = true, float leadSeconds = 1f,
        float halfWidth = .4f, float innerRadius = 0f, GroundIndicatorShape? indicatorShape = null, bool deferParrySignal = false)
    {
        if (visual == null)
        {
            visual = new GameObject("Strong attack warning");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.up * .045f;
        }
        radius = size;
        sectorInnerRadius = Mathf.Clamp(innerRadius, 0f, Mathf.Max(0f, size - .01f));
        corridorHalfWidth = Mathf.Max(.01f, halfWidth);
        visual.transform.localRotation = Quaternion.identity;
        parryable = canParry; FinalSignal = false; signalPlayed = false;
        motionSignalStrike = -1; ParrySignalCount = 0;
        if (canParry)
        {
            signalSocketIndex = signalSequence++ % 3;
            EnsureSignal();
            if (body != null)
            {
                var main = signalParticles.main;
                main.startSize = ResolveCueSize();
            }
        }
        EnemyStrongAttackImpactVfx.Prewarm();
        visual.SetActive(true);
        ConfigureTelegraph(size, angle, charge, useTelegraph, leadSeconds, indicatorShape);
        if (deferParrySignal) SetParryWindow(leadSeconds, false, 0);
        else SetRemaining(leadSeconds);
    }
    private void ConfigureTelegraph(float size, float angle, bool charge, bool enabled,
        float leadSeconds, GroundIndicatorShape? indicatorShape)
    {
        StopTelegraph();
        if (!enabled) return;
        if (telegraphLibrary == null)
            telegraphLibrary = Resources.Load<EnemyTelegraphVisualLibrary>(
                "Enemies/Balance/EnemyTelegraphVisualLibrary");
        if (telegraphLibrary == null) return;

        if (telegraphLibrary.UseProceduralIndicator)
        {
            if (procedural == null)
            {
                var instance = Instantiate(telegraphLibrary.ProceduralIndicator, visual.transform, false);
                instance.name = "Procedural attack indicator";
                procedural = instance.GetComponent<ProceduralGroundIndicator>();
                if (procedural == null) Destroy(instance);
                else if (radialSurfaceProfile != null && radialBorderProfile != null)
                    procedural.SetRadialProfiles(radialSurfaceProfile, radialBorderProfile);
            }
            if (procedural != null)
            {
                visual.transform.localScale = Vector3.one;
                procedural.gameObject.SetActive(true);
                procedural.Configure(indicatorShape ?? (charge ? GroundIndicatorShape.Rectangle
                    : angle >= 359.9f ? GroundIndicatorShape.Circle : GroundIndicatorShape.Sector),
                    size, sectorInnerRadius, angle, corridorHalfWidth * 2f, size, charge ? corridorHalfWidth : 0f);
                warningLeadSeconds = Mathf.Max(.01f, leadSeconds);
                procedural.SetProgress(0f);
                procedural.SetVisible(true);
                return;
            }
        }

        int kind = charge ? 2 : angle >= 359.9f ? 1 : 0;
        if (telegraphInstances[kind] == null)
        {
            GameObject prefab = kind == 0 ? telegraphLibrary.Cone
                : kind == 1 ? telegraphLibrary.Nova : telegraphLibrary.Rectangle;
            if (prefab == null) return;
            var instance = Instantiate(prefab, visual.transform, false);
            instance.name = "Telegraph " + prefab.name;
            telegraphInstances[kind] = instance;
            telegraphSystems[kind] = instance.GetComponentsInChildren<ParticleSystem>(true);
            // Keep the authored materials, fill, border, and warm flecks. Compress
            // only the broad ambient layer so it reads as energy, not hit range.
            foreach (var system in telegraphSystems[kind])
            {
                if (system.name.StartsWith("Fuzz"))
                    system.transform.localScale *= .55f;
                else if (system.name.StartsWith("Flecks"))
                    system.transform.localScale *= .75f;
            }
            instance.SetActive(false);
        }

        var telegraph = telegraphInstances[kind];
        // Original art is about 4.56m long (cone), 5.5m across (nova), or
        // 4.6m square (rectangle). Parent scale is the real strike radius.
        if (kind == 0)
        {
            telegraph.transform.localPosition = Vector3.forward * .5f;
            telegraph.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // The source texture has a narrower curved arc than our strike sector.
            // Keep the animated art inside the authored damage footprint.
            telegraph.transform.localScale = new Vector3(
                .70f * 2f * Mathf.Sin(Mathf.Clamp(angle, 1f, 359f) * Mathf.Deg2Rad * .5f) / 3.15f,
                1f, .94f / 4.56f);
        }
        else if (kind == 1)
        {
            telegraph.transform.localPosition = Vector3.zero;
            telegraph.transform.localRotation = Quaternion.identity;
            // Native border reaches beyond its nominal quad; seat it inside the
            // authoritative circle so decorative fire never suggests extra damage.
            telegraph.transform.localScale = new Vector3(1.8f / 5.5f, 1f, 1.8f / 5.5f);
        }
        else
        {
            telegraph.transform.localPosition = Vector3.forward * .5f;
            telegraph.transform.localRotation = Quaternion.identity;
            telegraph.transform.localScale = new Vector3(2f * corridorHalfWidth / (4.6f * Mathf.Max(.01f, size)),
                1f, 1f / 4.6f);
        }
        telegraph.SetActive(true);
        activeTelegraph = kind;
        foreach (var system in telegraphSystems[kind])
        {
            if (!system.gameObject.activeInHierarchy) continue;
            // The supplier's five-second bloom is nearly invisible during our
            // short windup. Advance the authored fill, border and ambient
            // layers together so the composite reaches its bright phase just
            // before impact, without a multi-second spawn-time simulation.
            var main = system.main;
            main.simulationSpeed = Mathf.Clamp(4.2f / Mathf.Max(.1f, leadSeconds), .5f, 9f);
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(false);
        }
    }
    private void StopTelegraph()
    {
        if (procedural != null) { procedural.SetVisible(false); procedural.gameObject.SetActive(false); }
        if (activeTelegraph < 0) return;
        int kind = activeTelegraph;
        activeTelegraph = -1;
        foreach (var system in telegraphSystems[kind])
            if (system != null) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (telegraphInstances[kind] != null) telegraphInstances[kind].SetActive(false);
    }
    public void SetFacing(Vector3 direction)
    {
        if (visual == null) return;
        direction.y = 0f;
        if (direction.sqrMagnitude > .0001f)
            visual.transform.rotation = Quaternion.LookRotation(direction);
    }
    public void SetCenter(Vector3 worldPosition)
    {
        if (visual == null) return;
        visual.transform.position = new Vector3(worldPosition.x,
            transform.position.y + .045f, worldPosition.z);
    }
    public void SetRemaining(float seconds) => SetRemaining(seconds, true);

    // threatensPlayer: 간이 판정으로 플레이어를 때릴 공격만 빛·핑을 낸다(수십 마리 전투의 신호 난립 방지).
    public void SetRemaining(float seconds, bool threatensPlayer)
    {
        if (visual == null) return;
        if (seconds < -.08f) { Hide(); return; }
        FinalSignal = seconds <= EnemyAbilityController.ParryLeadSeconds;
        bool threat = FinalSignal && parryable && threatensPlayer;
        if (threat) ThreatSignals.Add(this); else ThreatSignals.Remove(this);
        if (threat && !signalPlayed)
        {
            signalPlayed = true;
            PositionSignal();
            signalFeel?.PlayFeedbacks(signalParticles.transform.position);
            CombatActionSfxService.PlayStrongWarning(transform.position);
        }
        if (signalPlayed) PositionSignal();
        bool proceduralActive = procedural != null && procedural.gameObject.activeSelf;
        visual.transform.localScale = proceduralActive ? Vector3.one : new Vector3(radius, 1f, radius);
        if (proceduralActive) procedural.SetProgress(1f - Mathf.Max(0f, seconds) / warningLeadSeconds);
    }
    // The same pooled glint and common warning sound are replayed once per pending strike.
    public void SetParryWindow(float seconds, bool threatensPlayer, int strike)
    {
        if (visual == null) return;
        FinalSignal = parryable && threatensPlayer;
        if (FinalSignal) ThreatSignals.Add(this); else ThreatSignals.Remove(this);
        if (FinalSignal && motionSignalStrike != strike)
        {
            motionSignalStrike = strike; ParrySignalCount++; signalPlayed = true;
            signalFeel?.StopFeedbacks();
            signalParticles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            PositionSignal();
            if (signalParticles != null) signalFeel?.PlayFeedbacks(signalParticles.transform.position);
            CombatActionSfxService.PlayStrongWarning(transform.position);
        }
        if (!FinalSignal && signalPlayed)
        {
            signalPlayed = false;
            signalFeel?.StopFeedbacks();
            signalParticles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (FinalSignal) PositionSignal();
        bool proceduralActive = procedural != null && procedural.gameObject.activeSelf;
        visual.transform.localScale = proceduralActive ? Vector3.one : new Vector3(radius, 1f, radius);
        if (proceduralActive) procedural.SetProgress(1f - Mathf.Max(0f, seconds) / warningLeadSeconds);
    }
    private void EnsureSignal()
    {
        if (signalFeel != null) return;
        body = GetComponent<CombatTarget>();
        var point = new GameObject("Parry cue glint");
        point.transform.SetParent(transform, false);
        signalParticles = point.AddComponent<ParticleSystem>();
        signalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (telegraphLibrary == null)
            telegraphLibrary = Resources.Load<EnemyTelegraphVisualLibrary>("Enemies/Balance/EnemyTelegraphVisualLibrary");
        EnemyParryCueVisual.Configure(signalParticles, telegraphLibrary != null ? telegraphLibrary.ParryGlint : null);
        signalFeel = point.AddComponent<MMF_Player>();
        signalFeel.FeedbacksList = new List<MMF_Feedback>
        {
            new MMF_Particles { BoundParticleSystem = signalParticles, DeclaredDuration = .70f }
        };
        // In an Editor preview Awake may not run; FEEL normally creates Events there.
        if (signalFeel.Events == null)
        {
            signalFeel.Events = new MMFeedbacksEvents();
            signalFeel.Events.Initialization();
        }
        signalFeel.Initialization(true);
    }
    // 2026-09-30: 패링 빛은 몸·강공 장판과 겹치지 않도록 머리 바로 위에 띄운다(몸 꼭대기 +0.35m).
    // 여러 강공이 겹칠 때 구분되게 소켓마다 좌우로 조금 벌리고, 머리에 가리지 않게 카메라 쪽으로 살짝 당긴다.
    private const float SignalAboveHead = .35f;
    private EnemyParryCueAnchor authoredCueAnchor;
    private void PositionSignal()
    {
        if (signalParticles == null) return;
        if (body == null) body = GetComponent<CombatTarget>();
        if (signalCamera == null) signalCamera = Camera.main;
        signalParticles.transform.position = ResolveCuePosition(signalCamera, signalSocketIndex);
    }
    public float ResolveCueSize()
    {
        if (authoredCueAnchor == null) authoredCueAnchor = GetComponent<EnemyParryCueAnchor>();
        if (authoredCueAnchor != null && authoredCueAnchor.isActiveAndEnabled && authoredCueAnchor.Head != null)
            return authoredCueAnchor.CueSize * Mathf.Max(.01f, cueScale);
        return Mathf.Clamp((GetComponent<CombatTarget>()?.CurrentVolume.Radius ?? .5f) * 2.15f, 3.2f, 4.7f) * Mathf.Max(.01f, cueScale);
    }
    public Vector3 ResolveCuePosition(Camera camera, int socketIndex = 1)
    {
        if (authoredCueAnchor == null) authoredCueAnchor = GetComponent<EnemyParryCueAnchor>();
        if (attackCueSocket != null)
        {
            if (authoredCueAnchor != null && authoredCueAnchor.isActiveAndEnabled
                && authoredCueAnchor.TryResolveAttackSocket(attackCueSocket, attackCueOffset, camera, out var attackPosition)) return attackPosition;
            return attackCueSocket.TransformPoint(attackCueOffset);
        }
        if (authoredCueAnchor != null && authoredCueAnchor.isActiveAndEnabled && authoredCueAnchor.TryResolve(camera, out var authoredPosition))
            return authoredPosition;
        CombatTarget target = body != null ? body : GetComponent<CombatTarget>();
        if (cueSocket != null && (cueSocket == transform || cueSocket.IsChildOf(transform)))
            return cueSocket.TransformPoint(cueOffset);
        Vector3 center = target != null ? target.CurrentVolume.Center
            : transform.position + Vector3.up * 1.2f;
        float width = target != null ? target.CurrentVolume.Radius : .5f;
        float halfHeight = target != null ? target.CurrentVolume.HalfHeight : 1f;
        Vector3 facing = camera != null ? camera.transform.position - center
            : -transform.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < .0001f) facing = -transform.forward;
        facing.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, facing).normalized;
        float lateral = (socketIndex - 1) * width * .27f;
        return center + Vector3.up * (halfHeight + SignalAboveHead)
            + facing * width * .15f + right * lateral + transform.rotation * cueOffset;
    }
    public void Hide()
    {
        ThreatSignals.Remove(this);
        signalPlayed = false;
        StopTelegraph();
        if (visual != null) visual.SetActive(false);
        signalFeel?.StopFeedbacks();
        if (signalParticles != null)
            signalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        FinalSignal = false; attackCueSocket = null; attackCueOffset = Vector3.zero;
    }
    private void LateUpdate() { if (signalPlayed && attackCueSocket != null) PositionSignal(); }
    private void OnDisable() => Hide();
}
