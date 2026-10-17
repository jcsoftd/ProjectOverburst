using System;
using Overburst.Persistence;
using Unity.Profiling;
using UnityEngine;
using Random = UnityEngine.Random;

// Map-owned conditional loot. Failure rolls never consume these entries.
public sealed class MapLootReserve : MonoBehaviour
{
    static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("Overburst.LootReserve.Prepare");
    static readonly ProfilerMarker RollMarker = new ProfilerMarker("Overburst.LootReserve.Roll");
    static readonly ProfilerMarker CreateMarker = new ProfilerMarker("Overburst.LootReserve.Create");
    static readonly ProfilerMarker VfxMarker = new ProfilerMarker("Overburst.LootReserve.Vfx");
    static readonly ProfilerMarker WarmMarker = new ProfilerMarker("Overburst.LootReserve.Warm");
    static readonly int[] Channels = { 0,1,2,3,4,5,0,1,1,5,0,1,2,3,4,0,1,2,3,1 };
    sealed class Entry
    {
        public ItemData item, extraItem;
        public WorldItemPickup pickup, extra;
        public bool ready, bonus, playerAtCap;
        public float rare;
        public long order;
    }
    readonly Entry[] entries = new Entry[60];
    readonly int[] channelHits = new int[18], channelMisses = new int[18];
    readonly double[] requestedAt = new double[60], longestWait = new double[18];
    readonly int[] phaseSamples = new int[9], phaseOverruns = new int[9];
    readonly double[] phaseMaximum = new double[9];
    readonly UnityEngine.Object[] phaseAssets = new UnityEngine.Object[9];
    RunWorldGate gate;
    MapItemData mapDefinition;
    AccountContentRegistry registry;
    Transform inactiveRoot;
    Random.State random;
    int cursor, work = -1, phase, lastFrame = -1;
    long sequence;
    float nextPoll;
    bool failed;
    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Prepared { get; private set; }
    public int BudgetOverruns { get; private set; }
    public double MaximumStepMilliseconds { get; private set; }
    public string Error { get; private set; }
    public int ReadyCount(EnemyThemeTier tier)
    {
        float rare = BagQuality.EquippedBonus(BagStat.RareGradeWeight);
        bool bonus = BagQuality.EquippedBonus(BagStat.ExtraItemDrop) > 0;
        bool atCap = PlayerProgression.CurrentLevel >= OverburstGrowthRules.MaximumLevel;
        int count = 0;
        for (int i = (int)tier * 20; i < (int)tier * 20 + 20; i++)
            if (Matches(entries[i], rare, bonus, atCap)) count += entries[i].extra ? 2 : 1;
        return count;
    }

    static bool Matches(Entry entry, float rare, bool bonus, bool atCap) => entry != null && entry.ready
        && entry.pickup && entry.rare == rare && entry.bonus == bonus && entry.playerAtCap == atCap;

    // Snapshot allocation is restricted to diagnostics/export, never the preparation loop.
    public object DiagnosticSnapshot()
    {
        float rare = BagQuality.EquippedBonus(BagStat.RareGradeWeight);
        bool bonus = BagQuality.EquippedBonus(BagStat.ExtraItemDrop) > 0;
        bool atCap = PlayerProgression.CurrentLevel >= OverburstGrowthRules.MaximumLevel;
        var ready = new int[18]; var oldestWait = new double[18];
        double now = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < entries.Length; i++)
        {
            if (bonus && i % 20 >= 10) continue;
            int channel = (i / 20) * 6 + Channels[i % 20];
            if (Matches(entries[i], rare, bonus, atCap)) ready[channel] += entries[i].extra ? 2 : 1;
            else if (requestedAt[i] >= 0) oldestWait[channel] = Math.Max(oldestWait[channel], now - requestedAt[i]);
        }
        var assets = new string[phaseAssets.Length];
        for (int i = 0; i < assets.Length; i++) assets[i] = phaseAssets[i] ? phaseAssets[i].name : null;
        return new { ready, hits = (int[])channelHits.Clone(), misses = (int[])channelMisses.Clone(), oldestWait,
            longestWait = (double[])longestWait.Clone(), phaseSamples = (int[])phaseSamples.Clone(),
            phaseMaximum = (double[])phaseMaximum.Clone(), phaseOverruns = (int[])phaseOverruns.Clone(), assets };
    }

    public static MapLootReserve Attach(RunWorldGate owner)
    {
        if (!owner || !MapFarmingLoot.Applies(owner.Map)) return null;
        if (owner.LootReserve) return owner.LootReserve;
        var root = new GameObject("Prepared map loot"); root.transform.SetParent(owner.transform, false);
        var reserve = root.AddComponent<MapLootReserve>(); owner.LootReserve = reserve;
        for (int i = 0; i < reserve.requestedAt.Length; i++) reserve.requestedAt[i] = Time.realtimeSinceStartupAsDouble;
        reserve.gate = owner; reserve.registry = AccountGameplaySession.Current.ContentRegistry;
        reserve.mapDefinition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var inactive = new GameObject("Inactive prepared items"); inactive.transform.SetParent(root.transform, false);
        inactive.transform.position = new Vector3(0,-2000,0); inactive.SetActive(false); reserve.inactiveRoot = inactive.transform;
        var saved = Random.state;
        try { Random.InitState(unchecked(owner.Map.seed ^ 0x6d2b79f5)); reserve.random = Random.state; }
        finally { Random.state = saved; }
        return reserve;
    }

    public bool PrepareOne()
    {
        if (failed || !gate || lastFrame == Time.frameCount || Time.unscaledTime < nextPoll) return false;
        lastFrame = Time.frameCount;
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        int measuredPhase = -1; UnityEngine.Object measuredAsset = null;
        var saved = Random.state;
        using (PrepareMarker.Auto())
        try
        {
            Random.state = random;
            float rare = BagQuality.EquippedBonus(BagStat.RareGradeWeight);
            bool bonus = BagQuality.EquippedBonus(BagStat.ExtraItemDrop) > 0;
            bool atCap = PlayerProgression.CurrentLevel >= OverburstGrowthRules.MaximumLevel;
            if (work < 0)
            {
                for (int n=0;n<entries.Length;n++)
                {
                    int i=(cursor%3)*20+cursor/3; cursor=(cursor+1)%entries.Length;
                    var old=entries[i]; bool wanted=!bonus || i%20<10;
                    if(old!=null && (!wanted || old.rare!=rare || old.bonus!=bonus || old.playerAtCap!=atCap)) { Discard(old);entries[i]=null;requestedAt[i]=Time.realtimeSinceStartupAsDouble;return true; }
                    if(!wanted || old!=null)continue;
                    work=i;phase=0;entries[i]=new Entry { rare=rare,bonus=bonus,playerAtCap=atCap };break;
                }
                if(work<0){nextPoll=Time.unscaledTime+.1f;return false;}
            }
            var entry=entries[work];
            if(entry.rare!=rare || entry.bonus!=bonus || entry.playerAtCap!=atCap){Discard(entry);entries[work]=null;requestedAt[work]=Time.realtimeSinceStartupAsDouble;work=-1;return true;}
            int channel=Channels[work%20];var tier=(EnemyThemeTier)(work/20);
            var rank=tier==EnemyThemeTier.Elite?EnemyGradeType.Elite:EnemyGradeType.Normal;
            // Skip empty extra-item phases, while retaining one actual preparation unit per frame.
            while ((phase == 1 && (!bonus || channel == 5)) || (phase == 3 && entry.extraItem == null)
                || ((phase == 5 || phase == 7) && !entry.extra)) phase++;
            measuredPhase = phase; measuredAsset = entry.item?.baseData;
            switch(phase)
            {
                case 0:
                    using (RollMarker.Auto())
                    {
                    entry.item=channel==5?MapDropPolicy.CreateDrop(mapDefinition,registry,gate.Map,tier,gate.Context.RunId)
                        :MapFarmingLoot.CreateDrop((MapFarmingKind)channel,rank,gate.Map,rare);
                    entry.item.originRunId=gate.Context.RunId;
                    } break;
                case 1:
                    using (RollMarker.Auto())
                    if(bonus && channel!=5){entry.extraItem=MapFarmingLoot.Extra(entry.item,rank,gate.Map,rare);entry.extraItem.originRunId=gate.Context.RunId;}break;
                case 2: using(CreateMarker.Auto()) entry.pickup=WorldItemDropFactory.PrepareReserved(entry.item,inactiveRoot);break;
                case 3: using(CreateMarker.Auto()) if(entry.extraItem!=null)entry.extra=WorldItemDropFactory.PrepareReserved(entry.extraItem,inactiveRoot);break;
                case 4: using(VfxMarker.Auto()) entry.pickup.PrepareReservedVfx();break;
                case 5: using(VfxMarker.Auto()) if(entry.extra)entry.extra.PrepareReservedVfx();break;
                case 6: using(WarmMarker.Auto()) entry.pickup.WarmReserved();break;
                case 7: using(WarmMarker.Auto()) if(entry.extra)entry.extra.WarmReserved();break;
                case 8:
                    // Native pointers are unused; querying them forces render-thread synchronization.
                    int completedChannel = (work / 20) * 6 + channel;
                    longestWait[completedChannel] = Math.Max(longestWait[completedChannel], Time.realtimeSinceStartupAsDouble - requestedAt[work]);
                    requestedAt[work] = -1;
                    entry.ready=true;entry.order=++sequence;Prepared++;work=-1;break;
            }
            phase++;return true;
        }
        catch(Exception error)
        {
            failed=true;Error=error.Message;
            if(work>=0){Discard(entries[work]);entries[work]=null;work=-1;}
            Debug.LogWarning("아이템 사전 준비 중단; 기존 드롭 경로 사용: "+error.Message);return false;
        }
        finally
        {
            random=Random.state;Random.state=saved;
            double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000d/System.Diagnostics.Stopwatch.Frequency;
            MaximumStepMilliseconds=Math.Max(MaximumStepMilliseconds,ms);
            if (measuredPhase >= 0 && measuredPhase < phaseSamples.Length)
            {
                phaseSamples[measuredPhase]++;
                if (ms > phaseMaximum[measuredPhase]) { phaseMaximum[measuredPhase] = ms; phaseAssets[measuredPhase] = measuredAsset; }
                if (ms > MapSpawnBudget.FrameBudgetMilliseconds) phaseOverruns[measuredPhase]++;
            }
            if(ms>MapSpawnBudget.FrameBudgetMilliseconds){BudgetOverruns++;Observe("prepare_over_budget",measuredPhase,measuredAsset);}
        }
    }

    public bool TryTake(EnemyThemeTier tier,int channel,EnemyGradeType rank,float rare,PickupGradeVfxSet vfx,
        out WorldItemPickup pickup,out WorldItemPickup extra)
    {
        pickup=extra=null;
        bool elite=rank==EnemyGradeType.Elite || rank==EnemyGradeType.GreaterElite;
        bool bonus=BagQuality.EquippedBonus(BagStat.ExtraItemDrop)>0;
        bool atCap=PlayerProgression.CurrentLevel>=OverburstGrowthRules.MaximumLevel;
        int chosen=-1;long order=long.MaxValue;
        if(rank!=EnemyGradeType.Boss && elite==(tier==EnemyThemeTier.Elite) && (!vfx || vfx==PickupGradeVfxSet.Default))
            for(int i=(int)tier*20;i<(int)tier*20+20;i++)
            {
                var e=entries[i];
                if(Channels[i%20]!=channel || !Matches(e,rare,bonus,atCap) || e.order>=order)continue;
                chosen=i;order=e.order;
            }
        int diagnosticChannel = (int)tier * 6 + channel;
        if(chosen<0){Misses++;if(diagnosticChannel>=0 && diagnosticChannel<channelMisses.Length)channelMisses[diagnosticChannel]++;Observe("empty_or_stale",diagnosticChannel);return false;}
        var entry=entries[chosen];entries[chosen]=null;pickup=entry.pickup;extra=entry.extra;Hits++;nextPoll=0;
        requestedAt[chosen] = Time.realtimeSinceStartupAsDouble;
        channelHits[diagnosticChannel]++;
        Observe("reuse",(int)tier*6+channel);return true;
    }

    static void Discard(Entry entry)
    {if(entry==null)return;if(entry.pickup)Destroy(entry.pickup.gameObject);if(entry.extra)Destroy(entry.extra.gameObject);}
    void Observe(string reason,int channel,UnityEngine.Object asset=null)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Overburst.DebugTools.CombatStutterCapture.ObservePool("PreparedLoot",reason,Hits,60,channel,asset);
#endif
    }
}
