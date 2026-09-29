using UnityEngine;

// Repeats the project's chain-electricity link along a charged blade.
[DisallowMultipleComponent]
public sealed class WeaponElectricBladeArc : MonoBehaviour
{
    [SerializeField] private ChainElectricityLiteVfxController chain;
    [SerializeField, Min(.05f)] private float refreshInterval = .22f;
    private float elapsed;
    private bool visible = true;
    private bool started;

    private void OnEnable()
    {
        if (Application.isPlaying && visible)
            Restart();
    }

    private void OnDisable()
    {
        chain?.StopAndClearVfx();
        started = false;
        elapsed = 0f;
    }

    private void Update()
    {
        if (Application.isPlaying && visible)
            Advance(Time.deltaTime);
    }

    public void Configure(ChainElectricityLiteVfxController source, float interval)
    {
        chain = source;
        refreshInterval = Mathf.Max(.05f, interval);
    }

    public void SetVisible(bool value)
    {
        if (visible == value) return;
        visible = value;
        if (visible && isActiveAndEnabled)
            Restart();
        else
        {
            chain?.StopAndClearVfx();
            started = false;
            elapsed = 0f;
        }
    }

    public void SetEnergy(float normalizedEnergy)
    {
        float value = Mathf.Clamp01(normalizedEnergy);
        chain?.SetEnergyMultiplier(value);
        SetVisible(value > 0f);
    }

    public void AdvancePreview(float deltaTime)
    {
        if (Application.isPlaying || !visible || !isActiveAndEnabled) return;
        if (!started) Restart();
        Advance(deltaTime);
        chain?.AdvanceForPreview(deltaTime);
    }

    private void Advance(float deltaTime)
    {
        if (!started) Restart();
        elapsed += deltaTime;
        if (elapsed < refreshInterval) return;
        elapsed %= refreshInterval;
        chain?.RestartVfx();
    }

    private void Restart()
    {
        if (chain == null) return;
        chain.RestartVfx();
        elapsed = 0f;
        started = true;
    }
}
