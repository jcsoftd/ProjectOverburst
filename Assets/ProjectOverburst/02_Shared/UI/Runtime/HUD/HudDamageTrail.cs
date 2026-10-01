using UnityEngine;
using UnityEngine.UI;

// 2026-10-01: 상단 대상·보스 HUD의 주황 잔상. 머리 위 정예 바(EnemyHpBarView)와 같은 시간값과 감속 곡선을 쓴다.
public sealed class HudDamageTrail
{
    public const float HoldSeconds = 0.09f;
    public const float CatchupSeconds = 0.42f;

    private Image image;
    private float startFill;
    private float targetFill;
    private float startsAt;
    private float last = -1f;
    private bool active;

    public void Bind(Image trailImage)
    {
        image = trailImage;
        last = -1f;
        active = false;
    }

    // 새 대상을 띄울 때: 잔상 없이 현재 값으로 맞춘다.
    public void Snap(float fill)
    {
        last = fill;
        active = false;
        if (image != null)
            image.fillAmount = fill;
    }

    public void Set(float next)
    {
        if (image == null)
            return;

        if (last < 0f || next > last + 0.001f)
        {
            image.fillAmount = next;
            active = false;
        }
        else if (next < last - 0.001f)
        {
            startFill = Mathf.Max(last, image.fillAmount);
            targetFill = next;
            image.fillAmount = startFill;
            startsAt = Time.unscaledTime + HoldSeconds;
            active = true;
        }

        last = next;
    }

    public void Tick()
    {
        if (!active || image == null || Time.unscaledTime < startsAt)
            return;

        float t = Mathf.Clamp01((Time.unscaledTime - startsAt) / CatchupSeconds);
        image.fillAmount = Mathf.Lerp(startFill, targetFill, 1f - Mathf.Pow(1f - t, 3f));
        if (t >= 1f)
            active = false;
    }
}
