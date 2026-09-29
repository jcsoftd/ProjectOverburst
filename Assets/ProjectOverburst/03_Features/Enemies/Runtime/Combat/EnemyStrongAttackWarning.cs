using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

// Reuses supplier Telegraph art and the separate body cue; no extra ground outline.
public sealed class EnemyStrongAttackWarning : MonoBehaviour
{
    private GameObject visual;
    private MMF_Player signalFeel;
    private ParticleSystem signalParticles;
    private CombatTarget body;
    private bool parryable;
    private float radius;
    private float corridorHalfWidth = .4f;
    private int signalSequence;
    private int signalSocketIndex;
    private readonly GameObject[] telegraphInstances = new GameObject[3];
    private readonly ParticleSystem[][] telegraphSystems = new ParticleSystem[3][];
    private int activeTelegraph = -1;
    private static EnemyTelegraphVisualLibrary telegraphLibrary;
    public bool IsVisible => visual != null && visual.activeSelf;
    public bool FinalSignal { get; private set; }

    public void Show(float size, bool canParry, float angle = 360f,
        bool charge = false, bool useTelegraph = true, float leadSeconds = 1f,
        float halfWidth = .4f)
    {
        if (visual == null)
        {
            visual = new GameObject("Strong attack warning");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.up * .045f;
        }
        radius = size;
        corridorHalfWidth = Mathf.Max(.01f, halfWidth);
        visual.transform.localRotation = Quaternion.identity;
        parryable = canParry; FinalSignal = false;
        if (canParry)
        {
            signalSocketIndex = signalSequence++ % 3;
            EnsureSignal();
        }
        visual.SetActive(true);
        ConfigureTelegraph(size, angle, charge, useTelegraph, leadSeconds);
        SetRemaining(leadSeconds);
    }
    private void ConfigureTelegraph(float size, float angle, bool charge, bool enabled,
        float leadSeconds)
    {
        StopTelegraph();
        if (!enabled) return;
        if (telegraphLibrary == null)
            telegraphLibrary = Resources.Load<EnemyTelegraphVisualLibrary>(
                "Enemies/Balance/EnemyTelegraphVisualLibrary");
        if (telegraphLibrary == null) return;

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
    public void SetRemaining(float seconds)
    {
        if (visual == null) return;
        if (seconds < -.08f) { Hide(); return; }
        if (!FinalSignal && seconds <= .50f && parryable)
        {
            PositionSignal();
            signalFeel?.PlayFeedbacks(signalParticles.transform.position);
            CombatActionSfxService.PlayStrongWarning(transform.position);
        }
        FinalSignal = seconds <= .50f;
        if (FinalSignal && parryable) PositionSignal();
        visual.transform.localScale = new Vector3(radius, 1f, radius);
    }
    private void EnsureSignal()
    {
        if (signalFeel != null) return;
        body = GetComponent<CombatTarget>();
        var point = new GameObject("Parry cue glint");
        point.transform.SetParent(transform, false);
        signalParticles = point.AddComponent<ParticleSystem>();
        signalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = signalParticles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = .22f;
        main.startLifetime = .22f;
        main.startSpeed = .02f;
        main.startSize = .24f;
        main.startColor = new Color(1f, .94f, .54f, 1f);
        main.maxParticles = 12;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = signalParticles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 9) });
        var shape = signalParticles.shape;
        shape.enabled = false;
        var fade = signalParticles.colorOverLifetime;
        fade.enabled = true;
        var fadeGradient = new Gradient();
        fadeGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white * .5f, .45f),
                new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(.55f, .45f), new GradientAlphaKey(0f, 1f) });
        fade.color = fadeGradient;
        var renderer = point.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Resources.Load<Material>("Feel/MAT_OverburstFeelParticles");
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        signalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        signalFeel = point.AddComponent<MMF_Player>();
        signalFeel.FeedbacksList = new List<MMF_Feedback>
        {
            new MMF_Particles { BoundParticleSystem = signalParticles, DeclaredDuration = .22f }
        };
        // In an Editor preview Awake may not run; FEEL normally creates Events there.
        if (signalFeel.Events == null)
        {
            signalFeel.Events = new MMFeedbacksEvents();
            signalFeel.Events.Initialization();
        }
        signalFeel.Initialization(true);
    }
    private void PositionSignal()
    {
        if (signalParticles == null) return;
        if (body == null) body = GetComponent<CombatTarget>();
        Vector3 center = body != null ? body.CurrentVolume.Center
            : transform.position + Vector3.up * 1.2f;
        float width = body != null ? body.CurrentVolume.Radius : .5f;
        float lateral = (signalSocketIndex - 1) * width * .42f;
        float vertical = signalSocketIndex == 1 ? .34f : .14f;
        signalParticles.transform.position = center + transform.right * lateral
            + Vector3.up * vertical;
    }
    public void Hide()
    {
        StopTelegraph();
        if (visual != null) visual.SetActive(false);
        signalFeel?.StopFeedbacks();
        if (signalParticles != null)
            signalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        FinalSignal = false;
    }
    private void OnDisable() => Hide();
}
