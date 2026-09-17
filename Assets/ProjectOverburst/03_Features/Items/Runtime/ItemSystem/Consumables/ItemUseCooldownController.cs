using System.Collections.Generic;
using UnityEngine;

public class ItemUseCooldownController : MonoBehaviour
{
    private readonly Dictionary<string, CooldownState> cooldowns = new Dictionary<string, CooldownState>();

    private struct CooldownState
    {
        public float EndTime;
        public float Duration;
    }

    public bool IsCoolingDown(string cooldownKey)
    {
        return GetRemaining(cooldownKey) > 0f;
    }

    public float GetRemaining(string cooldownKey)
    {
        if (string.IsNullOrWhiteSpace(cooldownKey) || !cooldowns.TryGetValue(cooldownKey, out CooldownState state))
            return 0f;

        float remaining = state.EndTime - Time.unscaledTime;
        if (remaining > 0f)
            return remaining;

        cooldowns.Remove(cooldownKey);
        return 0f;
    }

    public float GetDuration(string cooldownKey)
    {
        if (string.IsNullOrWhiteSpace(cooldownKey) || !cooldowns.TryGetValue(cooldownKey, out CooldownState state))
            return 0f;

        return Mathf.Max(0f, state.Duration);
    }

    public float GetRemainingRatio(string cooldownKey)
    {
        float duration = GetDuration(cooldownKey);
        if (duration <= 0f)
            return 0f;

        return Mathf.Clamp01(GetRemaining(cooldownKey) / duration);
    }

    public void StartCooldown(string cooldownKey, float duration)
    {
        if (string.IsNullOrWhiteSpace(cooldownKey) || duration <= 0f)
            return;

        cooldowns[cooldownKey] = new CooldownState
        {
            EndTime = Time.unscaledTime + duration,
            Duration = duration
        };
    }
}
