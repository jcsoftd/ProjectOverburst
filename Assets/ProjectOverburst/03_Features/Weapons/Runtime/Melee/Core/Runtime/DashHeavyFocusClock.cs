using UnityEngine;

// Source-pose time, in seconds at 1x. Gameplay, movement and Animator share this clock.
public static class DashHeavyFocusClock
{
    public const float ClipLength = 112f / 60f;
    public const float SlowClip = .4f, SlowDuration = 4f / 60f, HoldDuration = 4f / 60f;
    public const float RampDuration = 2f / 60f, SwingSpeed = 1.25f;
    public const float HoldClip = SlowClip + SlowDuration * .5f, CutEndClip = 35f / 60f;
    public static float HoldStart => SlowClip + SlowDuration;
    public static float HoldEnd => HoldStart + HoldDuration;
    public static float RampEnd => HoldEnd + RampDuration;
    public static float RampEndClip => HoldClip + SwingSpeed * RampDuration * .5f;
    public static float CutEnd => RampEnd + (CutEndClip - RampEndClip) / SwingSpeed;
    public static float RecoveryStart => CutEnd + RampDuration;
    public static float RecoveryClip => CutEndClip + RampDuration * (SwingSpeed + 1f) * .5f;
    public static float Sample(float elapsed)
    {
        if (elapsed <= SlowClip) return Mathf.Max(0f, elapsed);
        if (elapsed < HoldStart)
        {
            float q = (elapsed - SlowClip) / SlowDuration;
            return SlowClip + SlowDuration * (q - q*q*q + .5f*q*q*q*q);
        }
        if (elapsed < HoldEnd) return HoldClip;
        if (elapsed < RampEnd)
        {
            float q = (elapsed - HoldEnd) / RampDuration;
            return HoldClip + SwingSpeed * RampDuration * (q*q*q - .5f*q*q*q*q);
        }
        if (elapsed < CutEnd) return RampEndClip + (elapsed - RampEnd) * SwingSpeed;
        if (elapsed < RecoveryStart)
        {
            float q = (elapsed - CutEnd) / RampDuration;
            return CutEndClip + RampDuration * (SwingSpeed*q + (1f-SwingSpeed)*(q*q*q-.5f*q*q*q*q));
        }
        return RecoveryClip + elapsed - RecoveryStart;
    }
    public static float RealAt(float sourceSeconds)
    {
        if (sourceSeconds <= SlowClip) return Mathf.Max(0f, sourceSeconds);
        float low = 0f, high = sourceSeconds + 1f;
        for (int i = 0; i < 28; i++)
        {
            float middle = (low + high) * .5f;
            if (Sample(middle) < sourceSeconds) low = middle; else high = middle;
        }
        return (low + high) * .5f;
    }
}

// Cumulative horizontal distance. The landing slide belongs to the existing dash budget.
public sealed class DashHeavyTravelPlan
{
    public readonly float Start, Landing, Stop, CarryVelocity, EntryDuration;
    private readonly float distance, dashDuration, ease, startDistance, startVelocity, startAcceleration;
    private readonly float[] times, coefficients;
    public DashHeavyTravelPlan(float start, float totalDistance, float dashSeconds, float moveEase, float playbackSpeed)
    {
        distance = Mathf.Max(0f, totalDistance); dashDuration = Mathf.Max(.01f, dashSeconds);
        ease = Mathf.Clamp01(moveEase); Start = Mathf.Clamp(start, 0f, dashDuration);
        float p = Start / dashDuration;
        startDistance = BasePosition(Start);
        startVelocity = distance / dashDuration * (1f-ease + ease*6f*p*(1f-p));
        startAcceleration = distance*ease/(dashDuration*dashDuration)*(6f-12f*p);
        float speed = Mathf.Max(.01f, playbackSpeed);
        float remaining = Mathf.Max(0f, distance-startDistance);
        // Late requests have little distance left. Shorten braking instead of teleporting,
        // extending 5m, reversing movement, or skipping any preparation pose.
        EntryDuration = Mathf.Min(.16f/speed, .8f*remaining/Mathf.Max(.001f,startVelocity));
        EntryDuration = Mathf.Max(.000001f, EntryDuration);
        Landing = Start + DashHeavyFocusClock.RealAt(.55f)/speed;
        Stop = Landing + .24f/speed;
        times = new[] { Start, Start+EntryDuration, Start+.4f/speed,
            Start+DashHeavyFocusClock.HoldStart/speed, Start+DashHeavyFocusClock.HoldEnd/speed,
            Start+DashHeavyFocusClock.RampEnd/speed, Landing, Stop };
        coefficients = new[] { 0f,1f,1f,.35f,.35f,1.4f,1.4f,0f };
        float fixedArea = .5f*startVelocity*EntryDuration + startAcceleration*EntryDuration*EntryDuration/12f;
        float weightedArea = .5f*EntryDuration;
        for (int i=1;i<times.Length-1;i++)
            weightedArea += (times[i+1]-times[i])*(coefficients[i]+coefficients[i+1])*.5f;
        CarryVelocity = Mathf.Max(0f,(remaining-fixedArea)/Mathf.Max(.000001f,weightedArea));
    }
    private float BasePosition(float t)
    {
        float p=Mathf.Clamp01(t/dashDuration);
        return distance*((1f-ease)*p+ease*(3f*p*p-2f*p*p*p));
    }
    private float Left(int i) => i==0 ? startVelocity : coefficients[i]*CarryVelocity;
    private float Right(int i) => coefficients[i+1]*CarryVelocity;
    private float Accel(int i) => i==0 ? startAcceleration : 0f;
    private float Area(int i,float q)
    {
        float dt=times[i+1]-times[i],q2=q*q,q3=q2*q,q4=q3*q;
        return dt*(Left(i)*(q-q3+.5f*q4)+Accel(i)*dt*(.5f*q2-2f*q3/3f+.25f*q4)+Right(i)*(q3-.5f*q4));
    }
    public float Position(float t)
    {
        if(t<=0f)return 0f; if(t<Start)return BasePosition(t); if(t>=Stop)return distance;
        float value=startDistance;
        for(int i=0;i<times.Length-1;i++)
        {
            if(t<times[i+1])return Mathf.Clamp(value+Area(i,(t-times[i])/(times[i+1]-times[i])),startDistance,distance);
            value+=Area(i,1f);
        }
        return distance;
    }
    public float Velocity(float t)
    {
        if(t<0f||t>=Stop)return 0f;
        if(t<Start){float p=t/dashDuration;return distance/dashDuration*(1f-ease+ease*6f*p*(1f-p));}
        for(int i=0;i<times.Length-1;i++) if(t<times[i+1])
        {
            float dt=times[i+1]-times[i],q=(t-times[i])/dt;
            return Left(i)*(2f*q*q*q-3f*q*q+1f)+Accel(i)*dt*(q*q*q-2f*q*q+q)+Right(i)*(-2f*q*q*q+3f*q*q);
        }
        return 0f;
    }
}
