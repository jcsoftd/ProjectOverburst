using System;
using System.Collections;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Project-owned two-sided card. No reward mutation or random rolls.</summary>
public sealed class OverburstRunCardView : MonoBehaviour
{
    [SerializeField] private RectTransform flipRoot;
    [SerializeField] private GameObject front;
    [SerializeField] private GameObject back;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text description;
    [SerializeField] private TMP_Text value;
    [SerializeField] private TMP_Text gradeLabel;
    [SerializeField] private TMP_Text durationLabel;
    [SerializeField] private Image gem;
    [SerializeField] private Image icon;
    [SerializeField] private Image glow;
    [SerializeField] private Button choose;
    [SerializeField] private AudioClip revealSound;
    private MMF_Player flip;
    private AudioSource audioSource;
    private RunCardPresentation data;
    private Action selected;
    private Color gradeColor;
    public const float RevealDuration = .24f;

    public void Configure(RectTransform pivot, GameObject face, GameObject reverse,
        TMP_Text heading, TMP_Text body, TMP_Text number, TMP_Text grade, TMP_Text duration,
        Image jewel, Image symbol, Image halo, Button button, AudioClip sound)
    {
        flipRoot = pivot; front = face; back = reverse; title = heading; description = body;
        value = number; gradeLabel = grade; durationLabel = duration; gem = jewel; icon = symbol;
        glow = halo; choose = button; revealSound = sound;
    }

    private void EnsureFeedback()
    {
        if (flip != null) return;
        flip = gameObject.AddComponent<MMF_Player>();
        var scale = new MMF_Scale
        {
            Label = "Horizontal Flip", Mode = MMF_Scale.Modes.Absolute,
            AnimateScaleTarget = flipRoot, AnimateScaleDuration = RevealDuration,
            AnimateX = true, AnimateY = false, AnimateZ = false,
            RemapCurveZero = -1f, RemapCurveOne = 1f,
            AnimateScaleTweenX = new MMTweenType(AnimationCurve.EaseInOut(0, 0, 1, 1))
        };
        scale.Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled };
        flip.FeedbacksList = new List<MMF_Feedback> { scale };
        flip.Initialization();
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0;
        audioSource.ignoreListenerPause = true;
    }

    public void Bind(RunCardPresentation presentation, Action onSelected)
    {
        EnsureFeedback();
        StopAllCoroutines();
        flip.StopFeedbacks();
        data = presentation; selected = onSelected;
        gradeColor = data.IsReward ? new Color(.88f, .74f, .48f) : GradeConfig.GetGradeColor(data.Grade);
        title.text = data.Title ?? string.Empty;
        description.text = data.Description == "이번 던전 동안 유지됩니다." ? RunCardIconSet.Describe(data.Title,data.Description) : data.Description ?? string.Empty;
        value.text = data.Value ?? string.Empty;
        gradeLabel.text = data.IsReward ? "보상" : ItemTooltipFormatter.GetGradeName(data.Grade);
        gradeLabel.color = Color.Lerp(gradeColor, RunUiLayout.Ivory, .26f);
        durationLabel.text = data.IsReward ? "선택 즉시 보상" : "이번 던전 동안";
        gem.gameObject.SetActive(!data.IsReward);
        gem.color = gradeColor;
        icon.sprite = data.Icon != null ? data.Icon : RunCardIconSet.Resolve(data.Title);
        icon.enabled = icon.sprite != null;
        icon.preserveAspect = true;
        glow.color = new Color(gradeColor.r, gradeColor.g, gradeColor.b, 0);
        flipRoot.localScale = new Vector3(-1, 1, 1);
        front.SetActive(false); back.SetActive(true);
        var sides = flipRoot.GetComponent<MMTwoSidedUI>();
        sides.BackVisible = true;
        choose.onClick.RemoveAllListeners();
        choose.onClick.AddListener(() => selected?.Invoke());
        SetSelectable(false);
    }

    public void Reveal()
    {
        flip.PlayFeedbacks();
        StartCoroutine(RevealAccent());
    }

    private IEnumerator RevealAccent()
    {
        yield return new WaitForSecondsRealtime(RevealDuration * .5f);
        float strength = data.IsReward ? .5f : Mathf.Clamp01((int)data.Grade / 6f);
        if (revealSound != null)
        {
            audioSource.pitch = Mathf.Lerp(1.14f, .82f, strength);
            audioSource.PlayOneShot(revealSound, Mathf.Lerp(.28f, .64f, strength));
        }
        float elapsed = 0;
        while (elapsed < .48f)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(.20f + .35f * strength, .055f + .08f * strength, elapsed / .48f);
            glow.color = new Color(gradeColor.r, gradeColor.g, gradeColor.b, alpha);
            yield return null;
        }
    }

    public void SetSelectable(bool enabled) { choose.interactable = enabled; }
    public void Focus() { choose.Select(); }
    private void OnDisable()
    {
        StopAllCoroutines();
        if (flip != null) flip.StopFeedbacks();
        if (audioSource != null) audioSource.Stop();
    }
}






