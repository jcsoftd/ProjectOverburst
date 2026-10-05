using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>A local, cancellable hideout gag. Counts confirmed melee contacts, never damage ticks.</summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth), typeof(CombatTarget))]
public sealed class HideoutSpinningCatEasterEgg : MonoBehaviour
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private GameObject visual;
    [SerializeField] private AnimationClip spinClip;
    [SerializeField] private GameObject stillVisual;
    [SerializeField] private OiiaCatReferenceTimeline referenceTimeline;
    [SerializeField] private GameObject chaosPrefab;
    [SerializeField] private Material chaosMaterial;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioClip music;
    [SerializeField, Min(1)] private int hitsToActivate = 50;
    [SerializeField, Range(1, 12)] private int maximumCats = 12;

    private readonly HashSet<(int, int, int)> contacts = new HashSet<(int, int, int)>();
    private readonly List<GameObject> cats = new List<GameObject>(12);
    private GameObject presentation;
    private Transform stage;
    private Camera stageCamera;
    private RenderTexture stageTexture;
    private Text title;
    private Image clubWash;
    private RectTransform[] beams;
    private float startedAt;
    private PlayerInputFacade blockedInput;

    private Vector3 idlePosition;
    private Quaternion idleRotation;
    private Vector3 idleScale;

    public int HitCount { get; private set; }
    public int ActivationCount { get; private set; }
    public bool IsActive => presentation != null;
    public int VisibleCatCount => cats.Count;
    public int ReferenceFrame { get; private set; }
    public bool IsReferenceSpinning { get; private set; }
    public float ReferenceTime => musicSource != null && music != null ? (float)musicSource.timeSamples / music.frequency : 0;
    public bool HasReferenceMedia => music != null && referenceTimeline != null && stillVisual != null;


    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (health == null) health = GetComponent<CombatHealth>();
        HitCount = 0;
        contacts.Clear();
        if (visual != null)
        {
            idlePosition = visual.transform.localPosition;
            idleRotation = visual.transform.localRotation;
            idleScale = visual.transform.localScale;
            if (spinClip != null) spinClip.SampleAnimation(visual, 0f);
        }
        ShowWorldStill();
        health.SetDamageDeathPrevention(this, true);
        health.OnDamaged += OnContact;
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.OnDamaged -= OnContact;
            health.SetDamageDeathPrevention(this, false);
        }
        StopChaos();
        contacts.Clear();
        HitCount = 0;
    }

    private void OnContact(CombatHealth target, DamageInfo info)
    {
        if (IsActive || info.source == null || info.isDamageOverTime
            || !info.triggersOnHitEffects || info.elementalReactionType != ElementalReactionType.None
            || info.sourceAttackSequenceId <= 0
            || (info.playerAttackKind & (PlayerAttackKind.Weak | PlayerAttackKind.Heavy)) == 0) return;
        var player = info.source.GetComponentInParent<PlayerActorRuntime>();
        if (player == null) return;
        if (!contacts.Add((player.GetInstanceID(), info.sourceAttackSequenceId, info.sourceAttackPhaseIndex))) return;
        HitCount++;
        if (HitCount >= hitsToActivate) StartChaos();
    }

    private void StartChaos()
    {
        if (visual == null || spinClip == null || chaosPrefab == null || chaosMaterial == null) return;
        if (!HasReferenceMedia || musicSource == null)
        {
            Debug.LogWarning("OIIA reference audio/photo/timeline is missing. Restore the local reference media with the OIIA reference builder.", this);
            HitCount = 0;
            return;
        }
        try
        {
            presentation = Instantiate(chaosPrefab);
            presentation.name = "OIIA Chaos (temporary)";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(presentation, gameObject.scene);
            stage = presentation.transform.Find("Stage");
            stageCamera = stage.GetComponentInChildren<Camera>(true);
            title = presentation.transform.Find("Canvas/Title").GetComponent<Text>();
            clubWash = presentation.transform.Find("Canvas/Club Wash").GetComponent<Image>();
            beams = presentation.transform.Find("Canvas/Beams").GetComponentsInChildren<RectTransform>();
            var surface = presentation.transform.Find("Canvas/Cats").GetComponent<RawImage>();
            int height = Mathf.Clamp(Screen.height, 360, 900);
            int width = Mathf.RoundToInt(height * Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), .6f, 3.6f));
            stageTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {name = "OIIA stage (temporary)", antiAliasing = 1};
            stageTexture.Create();
            stageCamera.targetTexture = stageTexture;
            stageCamera.aspect = (float)width / height;
            surface.texture = stageTexture;
            blockedInput = PlayerInputFacade.Current;

            GameplayInputBlocker.Block(this);
            if (blockedInput != null) blockedInput.SetGameplayMapDisabled(this, true);
            startedAt = Time.unscaledTime;
            ActivationCount++;
            contacts.Clear();
            AddCat();
            if (musicSource != null && music != null)
            {
                musicSource.clip = music;
                musicSource.loop = true;
                musicSource.pitch = 1;
                musicSource.timeSamples = 0;
                musicSource.PlayScheduled(AudioSettings.dspTime + .05);
                ApplyReferencePose();
            }
        }
        catch (Exception exception)
        {
            StopChaos();
            Debug.LogException(exception, this);
        }
    }

    private void AddCat()
    {
        var pivot = new GameObject("OIIA flying cat " + cats.Count);
        pivot.transform.SetParent(stage, false);
        var model = Instantiate(visual, pivot.transform, false);
        model.name = "Original meme visual";
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var animation in model.GetComponentsInChildren<Animation>(true)) animation.enabled = false;
        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = chaosMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        // The clip owns the mesh hierarchy; this pivot owns the screen trajectory.
        spinClip.SampleAnimation(model, 0f);
        model.transform.localPosition = Vector3.zero;
        var photo = Instantiate(stillVisual, pivot.transform, false);
        photo.name = "Reference still photo";
        photo.transform.localPosition = new Vector3(0, .3f, 0);
        photo.transform.localRotation = Quaternion.identity;
        cats.Add(pivot);
    }

    private void Update()
    {
        if (!IsActive) { FaceWorldPhoto(); return; }
        var currentInput = PlayerInputFacade.Current;
        if (currentInput != null) blockedInput = currentInput;
        if (blockedInput != null) blockedInput.SetGameplayMapDisabled(this, true);
        // MenuGate samples our blocker earlier in the frame, so this Escape never opens the pause menu.
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true
            || PlayerInputFacade.Current?.UiCancelPressedThisFrame == true)
        {
            StopChaos();
            return;
        }
        if (cats.Count < Mathf.Min(maximumCats, 12) && Time.unscaledTime - startedAt > cats.Count * .24f) AddCat();
        ApplyReferencePose();
        float t = ReferenceTime;
        float pulse = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI * 4.266667f)), 5);
        float flash = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI * 4.266667f)), 2);
        var neon = Color.HSVToRGB(Mathf.Repeat(t * .55f, 1), .95f, .28f + flash * .72f);
        neon.a = .78f;
        clubWash.color = neon;
        for (int i = 1; i < beams.Length; i++)
        {
            beams[i].localRotation = Quaternion.Euler(0, 0, t * (i % 2 == 0 ? 95 : -125) + i * 36);
            var beam = beams[i].GetComponent<Image>();
            var color = Color.HSVToRGB(Mathf.Repeat(t * .35f + i * .13f, 1), .75f, 1);
            color.a = .1f + flash * .25f;
            beam.color = color;
        }
        title.color = Color.HSVToRGB(Mathf.Repeat(t * .22f, 1), .85f, .3f + pulse * .4f);
        title.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 4.8f) * 12);
        title.rectTransform.localScale = Vector3.one * (1 + pulse * .18f);
    }

    // One audio sample clock drives every cat. Short photo flashes are preserved frame for frame.
    private void ApplyReferencePose()
    {
        float t = ReferenceTime;
        ReferenceFrame = referenceTimeline.FrameFromSamples(musicSource.timeSamples, music.frequency);
        IsReferenceSpinning = referenceTimeline.IsSpinning(ReferenceFrame);
        float segmentStart = (float)referenceTimeline.SegmentStart(ReferenceFrame) / referenceTimeline.FrameRate;
        float poseTime = IsReferenceSpinning ? t : segmentStart;
        float pulse = Mathf.Pow(Mathf.Max(0, Mathf.Sin(poseTime * Mathf.PI * 4.266667f)), 5);
        // The imported clip has a standing intro/outro. Sample only its fully morphed spinning portion.
        float spinStart = (float)referenceTimeline.SpinStart(ReferenceFrame) / referenceTimeline.FrameRate;
        float spinTime = 1.1f + Mathf.Repeat(Mathf.Max(0, t - spinStart), Mathf.Min(4.8f, spinClip.length - 1.1f));
        float horizontal = 5.3f * stageCamera.aspect;
        for (int i = 0; i < cats.Count; i++)
        {
            var pivot = cats[i].transform;
            var model = pivot.GetChild(0).gameObject;
            var photo = pivot.GetChild(1).gameObject;
            model.SetActive(IsReferenceSpinning);
            photo.SetActive(!IsReferenceSpinning);
            float phase = poseTime * (1.2f + i * .19f) + i * 2.39996f;
            if (IsReferenceSpinning) spinClip.SampleAnimation(model, spinTime);
            model.transform.localPosition = Vector3.zero;
            if (i == 0)
            {
                pivot.localPosition = new Vector3(Mathf.Sin(poseTime * 1.9f) * horizontal * .18f, -.8f + Mathf.Cos(poseTime * 3.7f) * .7f, 0);
                pivot.localScale = Vector3.one * (6.8f + pulse * 1.1f);
                pivot.localRotation = IsReferenceSpinning ? Quaternion.Euler(0, 0, Mathf.Sin(poseTime * 2.7f) * 25) : Quaternion.identity;
            }
            else
            {
                float lane = .25f + .7f * ((i * 7 % 11) / 10f);
                pivot.localPosition = new Vector3(Mathf.Sin(phase) * horizontal * lane,
                    Mathf.Cos(phase * (1.15f + i * .025f)) * 4.4f, 1.5f + (i % 3));
                pivot.localScale = Vector3.one * (1.8f + (i % 4) * .8f + pulse * .45f);
                pivot.localRotation = IsReferenceSpinning ? Quaternion.Euler(Mathf.Sin(phase) * 35, poseTime * (i % 2 == 0 ? 220 : -270), poseTime * (i % 3 == 0 ? -160 : 190)) : Quaternion.identity;
            }
        }
        visual.SetActive(IsReferenceSpinning);
        stillVisual.SetActive(!IsReferenceSpinning);
        if (IsReferenceSpinning)
        {
            spinClip.SampleAnimation(visual, spinTime);
            visual.transform.localPosition = idlePosition + new Vector3(Mathf.Sin(t * 4.3f) * 2.2f, Mathf.Abs(Mathf.Sin(t * 3.1f)) * .7f, Mathf.Cos(t * 3.5f) * 2.2f);
        }
        else FaceWorldPhoto();
    }

    private void FaceWorldPhoto()
    {
        var camera = Camera.main;
        if (stillVisual != null && camera != null && stillVisual.activeSelf)
            stillVisual.transform.rotation = Quaternion.LookRotation(camera.transform.forward, Vector3.up);
    }

    private void ShowWorldStill()
    {
        if (stillVisual == null) return;
        stillVisual.SetActive(true);
        if (visual != null) visual.SetActive(false);
        ReferenceFrame = 0;
        IsReferenceSpinning = false;
        FaceWorldPhoto();
    }

    public void StopChaos()
    {
        if (musicSource != null) musicSource.Stop();
        if (stageCamera != null) {stageCamera.enabled = false; stageCamera.targetTexture = null;}
        if (presentation != null) {presentation.SetActive(false); Destroy(presentation);}
        presentation = null;
        cats.Clear();
        stage = null;
        stageCamera = null;
        title = null;
        clubWash = null;
        beams = null;
        if (stageTexture != null) {stageTexture.Release(); Destroy(stageTexture); stageTexture = null;}
        GameplayInputBlocker.Unblock(this);
        PlayerInputFacade.ReleaseGameplayMapDisabled(this);

        blockedInput = null;
        if (visual != null && Application.isPlaying)
        {
            if (spinClip != null) spinClip.SampleAnimation(visual, 0f);
            visual.transform.localPosition = idlePosition;
            visual.transform.localRotation = idleRotation;
            visual.transform.localScale = idleScale;
        }
        ShowWorldStill();
        HitCount = 0;
        contacts.Clear();
    }
}
