using UnityEngine;

// 한 번의 중립 판단에 쓰는 현재 값. 플레이어의 누적 횟수·성향·성공률을 저장하지 않는다.
public readonly struct CrustaspikanCombatContext
{
    public readonly Vector3 Delta, Velocity, RelativeVelocity;
    public readonly float Distance, ForwardDot, SideDot, RadialSpeed, TangentialSpeed, ElementEnergy01, Poise01;
    public readonly int AliveAdds, Phase;

    public CrustaspikanCombatContext(Vector3 bossPosition, Vector3 bossForward, Vector3 playerPosition,
        Vector3 playerVelocity, float energy01, float poise01, int aliveAdds, int phase, Vector3 bossVelocity = default)
    {
        Delta = playerPosition - bossPosition; Delta.y = 0f;
        Velocity = Vector3.ClampMagnitude(new Vector3(playerVelocity.x, 0f, playerVelocity.z), 12f);
        Distance = Delta.magnitude;
        Vector3 forward = new Vector3(bossForward.x, 0f, bossForward.z).normalized;
        Vector3 direction = Distance > .001f ? Delta / Distance : forward;
        ForwardDot = Vector3.Dot(forward, direction);
        SideDot = Vector3.Dot(Vector3.Cross(Vector3.up, forward), direction);
        RelativeVelocity = Velocity - new Vector3(bossVelocity.x, 0f, bossVelocity.z);
        RadialSpeed = Distance > .001f ? Vector3.Dot(RelativeVelocity, direction) : 0f;
        TangentialSpeed = Distance > .001f ? (RelativeVelocity - direction * RadialSpeed).magnitude : 0f;
        ElementEnergy01 = Mathf.Clamp01(energy01); Poise01 = Mathf.Clamp01(poise01);
        AliveAdds = Mathf.Max(0, aliveAdds); Phase = Mathf.Max(1, phase);
    }
}

// 상황 비중을 단계 실행기와 분리한다. 후보의 거리·판정·쿨다운은 Brain이 먼저 검사한다.
public static class CrustaspikanCombatDecision
{
    public static float Weight(CrustaspikanEncounterSettings.Pattern pattern, in CrustaspikanCombatContext context,
        string lastPattern, string lastFamily, int consecutiveFamily, bool circlingPressure = false)
    {
        float situation = 1f;
        switch (pattern.family)
        {
            case "ranged":
                if (context.Distance > 12f) situation *= 1.3f;
                if (context.RadialSpeed > 1f) situation *= 1.15f;
                break;
            case "pressure":
                if (context.Distance > 10f || context.RadialSpeed > 1f) situation *= 1.3f;
                break;
            case "counter":
                if (context.RadialSpeed < -1f) situation *= 1.3f;
                break;
            case "rear":
                if (context.ForwardDot < -.35f) situation *= 1.4f;
                break;
            case "turn":
                if (Mathf.Abs(context.SideDot) > .5f || context.ForwardDot < 0f) situation *= 1.3f;
                break;
            case "stomp":
                if (context.Distance < 5f) situation *= 1.2f;
                if (context.ElementEnergy01 >= .8f) situation *= 1.1f;
                break;
            case "heavy":
            case "area":
                if (context.Phase == 2) situation *= 1.15f;
                break;
        }
        if (context.Poise01 >= .8f && (pattern.family == "light" || pattern.family == "turn")) situation *= 1.1f;
        float repeat = pattern.id == lastPattern ? .4f : 1f;
        if (pattern.family == lastFamily && consecutiveFamily >= 2) repeat *= .2f;
        // 같은 계열을 완전히 막아 유일하게 닿는 공격까지 사라지는 정지를 피한다.
        float circling = !circlingPressure ? 1f : pattern.family == "turn" || pattern.family == "rear"
            || pattern.family == "stomp" || pattern.family == "area" ? 1.75f
            : pattern.family == "light" || pattern.family == "combo" ? .75f : 1f;
        return pattern.weight * Mathf.Clamp(situation, .5f, 1.5f) * repeat * circling;
    }

    // A short movement filter, reset on invalid samples; no player history survives it.
    public static void UpdateCirclingPressure(in CrustaspikanCombatContext context, bool sampleValid, float deltaTime,
        ref bool pressure, ref float stableSeconds)
    {
        if (!sampleValid || context.Distance < 3f || context.Distance > 10f || deltaTime <= 0f || deltaTime > .25f)
        { pressure = false; stableSeconds = 0f; return; }
        bool transition = pressure ? context.TangentialSpeed < 1f : context.TangentialSpeed >= 2f;
        stableSeconds = transition ? stableSeconds + deltaTime : 0f;
        if (stableSeconds >= .25f) { pressure = !pressure; stableSeconds = 0f; }
    }

    public static bool NeedsSummonSlot(CrustaspikanEncounterSettings.Pattern pattern)
    {
        foreach (var step in pattern.steps)
            if (step.kind == CrustaspikanStepKind.LiftElite || step.kind == CrustaspikanStepKind.ThrowElite
                || step.kind == CrustaspikanStepKind.Attack && (step.materialOrMotion == "SpitterShot1" || step.materialOrMotion == "SpitterShot2")) return true;
        return false;
    }
}
