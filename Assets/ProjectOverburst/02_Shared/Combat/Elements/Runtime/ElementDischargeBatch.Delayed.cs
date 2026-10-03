using System;
using UnityEngine;

public sealed partial class ElementDischargeBatch
{
    public const float FirePropagationDelay = .2f;
    public const float LightningHopDelay = .08f;
    public const float OriginGroupDelay = .06f;
    private float[] scheduledAt = new float[128];
    private float chainClock, lastChainTime;

    public void BeginDelayed(Vector3 center, float now)
    {
        using var costScope = ElementCombatCostMarkers.Chain_Schedule.Auto();
        if(scheduledAt.Length<nodes.Length)Array.Resize(ref scheduledAt,nodes.Length);
        chainClock=0;lastChainTime=now;
        // Stable insertion sort of origins only; node indices and hit accounting remain unchanged.
        for(int i=1;i<rootCount;i++)
        {
            int value=roots[i],j=i-1;float distance=(nodes[value].Point-center).sqrMagnitude;
            while(j>=0)
            {
                float other=(nodes[roots[j]].Point-center).sqrMagnitude;
                if(other<distance || (other==distance && nodes[roots[j]].Target.TargetId<=nodes[value].Target.TargetId))break;
                roots[j+1]=roots[j];j--;
            }
            roots[j+1]=value;
        }
        int groups=Mathf.Min(8,rootCount);
        for(int r=0;r<rootCount;r++)
        {
            int group=r*groups/Mathf.Max(1,rootCount);
            scheduledAt[r]=(element==WeaponElement.Fire?FirePropagationDelay:LightningHopDelay)+group*OriginGroupDelay;
            paths[r*8]=roots[r];pathLengths[r]=1;
        }
    }

    // The visual clock slows through a hitch instead of dispatching every overdue wave at once.
    // Damage and its visual callback execute together. No discarded origins or reduced particle counts.
    public bool AdvanceDelayed(float now,float blastDamage,float energy,GameObject source,
        Action<Vector3,float> fireVfx,Action<Vector3,Vector3> linkVfx)
    {
        using var costScope = ElementCombatCostMarkers.Chain_Advance.Auto();
        if (!gemAttack.IsCurrent) return false;
        chainClock+=Mathf.Clamp(now-lastChainTime,0,OriginGroupDelay);lastChainTime=now;
        for(int i=0;i<count;i++)if(Valid(i))
        {
            var volume=nodes[i].Target.CurrentHurtVolume;
            nodes[i].Point=volume.Center;nodes[i].Radius=volume.Radius;nodes[i].HalfHeight=volume.HalfHeight;
        }
        int readyCount=rootCount;
        for(int r=0;r<readyCount;r++)
        {
            if(scheduledAt[r]>chainClock+.00001f)continue;
            scheduledAt[r]=float.PositiveInfinity;
            int origin=roots[r];if(!SameLife(origin))continue;
            if(element==WeaponElement.Fire)
            {
                int stack=Mathf.Clamp(nodes[origin].Stacks,1,5);
                var point=nodes[origin].Point;float radius=CombatBalanceFormulas.FireChainRadius(stack);
                fireVfx?.Invoke(point,radius); // Callback receives the actual damage radius, in metres.
                for(int i=NextCandidate(0);i<count;i=NextCandidate(i+1))
                {
                    CandidateChecks++;
                    if(!Valid(i)){RetireCandidate(i);continue;}
                    if(i==origin||nodes[i].Hits>=2||!InRange(i,point,radius))continue;
                    int burning=!nodes[i].Queued && nodes[i].Status!=null?nodes[i].Status.GetStackCount(WeaponElement.Fire):0;
                    if(!Damage(i,CombatBalanceFormulas.FireChainDamage(blastDamage,stack) * (1f + gemAttack.Modifiers.ExplosionDamage / 100f),source)||burning<=0||nodes[i].Queued)continue;
                    // Capture and consume at ignition; later weak hits belong to a new burn.
                    nodes[i].Stacks=burning;int index=rootCount;Queue(i);
                    if(rootCount>index)
                    {
                        if(Valid(i))nodes[i].Status?.ConsumeForDischarge(WeaponElement.Fire,out _);
                        scheduledAt[index]=chainClock+FirePropagationDelay;
                    }
                }
            }
            else
            {
                int length=pathLengths[r],hop=length-1;
                int maxHops=Mathf.Min(7, CombatBalanceFormulas.LightningMaxHops(nodes[origin].Stacks,energy) + gemAttack.Modifiers.ChainHops);
                if(length<=0||hop>=maxHops)continue;
                int previous=paths[r*8+length-1];
                if(!SameLife(previous))continue;
                var point=nodes[previous].Point;float radius=CombatBalanceFormulas.LightningLinkRadius(energy),best=float.PositiveInfinity;int nearest=-1;
                for(int i=NextCandidate(0);i<count;i=NextCandidate(i+1))
                {
                    CandidateChecks++;
                    if(!Valid(i)){RetireCandidate(i);continue;}
                    if(nodes[i].Hits>=2||!InRange(i,point,radius))continue;
                    bool visited=false;for(int p=0;p<length;p++)if(paths[r*8+p]==i){visited=true;break;}
                    if(visited)continue;
                    float distance=(nodes[i].Point-point).sqrMagnitude;
                    if(distance<best){nearest=i;best=distance;}
                }
                if(nearest<0||!Damage(nearest,CombatBalanceFormulas.LightningHopDamage(OverburstElementTuning.Current,blastDamage,nodes[origin].Stacks,hop) * (1f + gemAttack.Modifiers.ChainDamage / 100f),source))continue;
                paths[r*8+length]=nearest;pathLengths[r]++;
                linkVfx?.Invoke(point,nodes[nearest].Point);
                PlayLightningHopFeedback(nearest,point);
                if(hop+1<maxHops)scheduledAt[r]=chainClock+LightningHopDelay;
            }
        }
        for(int r=0;r<rootCount;r++)if(!float.IsPositiveInfinity(scheduledAt[r]))return true;
        return false;
    }
}
