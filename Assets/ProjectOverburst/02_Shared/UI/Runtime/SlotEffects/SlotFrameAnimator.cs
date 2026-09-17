using UnityEngine;
using UnityEngine.UI;

public class SlotFrameAnimator : MonoBehaviour // 슬롯 프레임 애니메이션
{
    [SerializeField] private float framesPerSecond = 12f; // 초당 프레임

    public Image targetImage; // 대상 이미지

    private Sprite[] frames; // 프레임 목록
    private int frameIndex; // 현재 프레임
    private float timer; // 누적 시간

    private void Update()
    {
        if (targetImage == null || frames == null || frames.Length == 0)
            return;

        timer += Time.unscaledDeltaTime;
        float frameTime = 1f / Mathf.Max(1f, framesPerSecond); // 프레임 간격

        if (timer < frameTime)
            return;

        timer = 0f;
        frameIndex = (frameIndex + 1) % frames.Length;
        targetImage.sprite = frames[frameIndex];
    }

    public void SetFrames(Sprite[] newFrames)
    {
        frames = newFrames;
        frameIndex = 0;
        timer = 0f;

        if (targetImage != null && frames != null && frames.Length > 0)
            targetImage.sprite = frames[0];
    }

    public void Stop()
    {
        frames = null;
        frameIndex = 0;
        timer = 0f;
    }
}
