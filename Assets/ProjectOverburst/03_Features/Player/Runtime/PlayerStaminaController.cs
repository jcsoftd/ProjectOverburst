using System;
using UnityEngine;

[DefaultExecutionOrder(250)]
public class PlayerStaminaController : MonoBehaviour // 플레이어 스태미너
{
    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float currentStamina = 100f;
    [SerializeField] private float staminaRegenPerSecond = 18f;
    [SerializeField] private float regenDelayAfterConsume = 0.65f;
    [SerializeField] private float dashStaminaCost = 10f;

    private float lastConsumeTime = -999f; // 마지막 소모 시각

    public event Action<float, float> OnStaminaChanged;

    public float MaxStamina
    {
        get { return maxStamina; }
    }

    public float CurrentStamina
    {
        get { return currentStamina; }
    }

    public float NormalizedStamina
    {
        get { return maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f; }
    }

    public float StaminaRegenPerSecond
    {
        get { return staminaRegenPerSecond; }
    }

    public float RegenDelayAfterConsume
    {
        get { return regenDelayAfterConsume; }
    }

    public float DashStaminaCost
    {
        get { return dashStaminaCost; }
    }

    public bool IsStaminaEmpty
    {
        get { return currentStamina <= 0.001f; }
    }

    private void Awake()
    {
        ClampValues();
    }

    private void OnEnable()
    {
        NotifyChanged();
    }

    private void Update()
    {
        RegenerateStamina(Time.deltaTime);
    }

    private void OnValidate()
    {
        ClampValues();
    }

    public bool CanConsume(float amount)
    {
        amount = Mathf.Max(0f, amount);
        return amount <= 0f || currentStamina + 0.001f >= amount;
    }

    public bool TryConsume(float amount)
    {
        amount = Mathf.Max(0f, amount);
        if (amount <= 0f)
            return true;

        if (!CanConsume(amount))
            return false;

        lastConsumeTime = Time.time;
        SetCurrentStamina(currentStamina - amount);
        return true;
    }

    public void Restore(float amount)
    {
        amount = Mathf.Max(0f, amount);
        if (amount <= 0f)
            return;

        SetCurrentStamina(currentStamina + amount);
    }

    public void RestoreFull()
    {
        SetCurrentStamina(maxStamina);
    }

    public void SetMaxStamina(float value, bool refill)
    {
        float previousMax = maxStamina;
        maxStamina = Mathf.Max(1f, value);

        if (refill)
            currentStamina = maxStamina;
        else
            currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);

        if (!Mathf.Approximately(previousMax, maxStamina) || refill)
            NotifyChanged();
    }

    private void RegenerateStamina(float deltaTime)
    {
        if (staminaRegenPerSecond <= 0f || currentStamina >= maxStamina)
            return;

        if (Time.time < lastConsumeTime + regenDelayAfterConsume)
            return;

        SetCurrentStamina(currentStamina + staminaRegenPerSecond * Mathf.Max(0f, deltaTime));
    }

    private void SetCurrentStamina(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, maxStamina);
        if (Mathf.Approximately(currentStamina, clamped))
            return;

        currentStamina = clamped;
        NotifyChanged();
    }

    private void ClampValues()
    {
        maxStamina = Mathf.Max(1f, maxStamina);
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
        staminaRegenPerSecond = Mathf.Max(0f, staminaRegenPerSecond);
        regenDelayAfterConsume = Mathf.Max(0f, regenDelayAfterConsume);
        dashStaminaCost = Mathf.Max(0f, dashStaminaCost);
    }

    private void NotifyChanged()
    {
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }
}
