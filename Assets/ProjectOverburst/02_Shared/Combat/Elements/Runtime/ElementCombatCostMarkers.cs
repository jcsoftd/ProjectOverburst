// Inclusive CPU scopes: nested markers must not be summed as independent cost.
public static class ElementCombatCostMarkers
{
    public static readonly Unity.Profiling.ProfilerMarker Heavy_TargetSnapshot = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Heavy.TargetSnapshot");
    public static readonly Unity.Profiling.ProfilerMarker Chain_Damage = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Chain.Damage");
    public static readonly Unity.Profiling.ProfilerMarker Chain_Advance = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Chain.Advance");
    public static readonly Unity.Profiling.ProfilerMarker Chain_Schedule = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Chain.Schedule");
    public static readonly Unity.Profiling.ProfilerMarker Heavy_Begin = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Heavy.Begin");
    public static readonly Unity.Profiling.ProfilerMarker Heavy_StatusReaction = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Heavy.StatusReaction");
    public static readonly Unity.Profiling.ProfilerMarker Heavy_ImpactVfx = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Heavy.ImpactVfx");
    public static readonly Unity.Profiling.ProfilerMarker Heavy_Commit = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Heavy.Commit");
    public static readonly Unity.Profiling.ProfilerMarker Status_Advance = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Status.Advance");
    public static readonly Unity.Profiling.ProfilerMarker Status_Control = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Status.Control");
    public static readonly Unity.Profiling.ProfilerMarker Status_Apply = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Status.Apply");
    public static readonly Unity.Profiling.ProfilerMarker Status_Consume = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Status.Consume");
    public static readonly Unity.Profiling.ProfilerMarker Pool_Spawn = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Pool.Spawn");
    public static readonly Unity.Profiling.ProfilerMarker Pool_Acquire = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Pool.Acquire");
    public static readonly Unity.Profiling.ProfilerMarker Pool_Release = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Pool.Release");
    public static readonly Unity.Profiling.ProfilerMarker Pool_ResolveLifetime = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Pool.ResolveLifetime");
    public static readonly Unity.Profiling.ProfilerMarker Pool_RestartPlayback = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Pool.RestartPlayback");
    public static readonly Unity.Profiling.ProfilerMarker Pool_RestartParticles = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Pool.RestartParticles");
    public static readonly Unity.Profiling.ProfilerMarker Pool_StopAndClearPlayback = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Pool.StopAndClearPlayback");
    public static readonly Unity.Profiling.ProfilerMarker Pool_StopAndClearParticles = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Pool.StopAndClearParticles");
    public static readonly Unity.Profiling.ProfilerMarker Aura_Restart = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Aura.Restart");
    public static readonly Unity.Profiling.ProfilerMarker Aura_StopClear = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Aura.StopClear");
    public static readonly Unity.Profiling.ProfilerMarker Dark_Barrage_Collect = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Dark.BarrageCollect");
    public static readonly Unity.Profiling.ProfilerMarker Dark_Barrage_Tick = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Dark.BarrageTick");
    public static readonly Unity.Profiling.ProfilerMarker Dark_Barrage_Hit = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Dark.BarrageHit");
    public static readonly Unity.Profiling.ProfilerMarker Light_Overcharge_Tick = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Light.OverchargeTick");
    public static readonly Unity.Profiling.ProfilerMarker Light_TripleImpact_Dispatch = new Unity.Profiling.ProfilerMarker("Overburst.Cost.Light.TripleImpactDispatch");
}
