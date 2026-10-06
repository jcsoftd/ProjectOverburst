using System;
using System.Linq;
using UnityEngine;

// Legal element/grade pairs share each grade's mass; Light/Dark never promote a rejected low-grade roll.
public static class ElementGemLootPolicy
{
    static ElementGemItemData[] catalog;
    public static ElementGemItemData[] Catalog => catalog ?? (catalog = Resources.LoadAll<ElementGemItemData>("Items/ElementGems").OrderBy(x=>(int)x.fixedGrade).ThenBy(x=>(int)x.element).ToArray());
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => catalog = null;
    public static ElementGemItemData Select(float roll, int level, EnemyGradeType rank, ItemGrade mapGrade = ItemGrade.Common, float rarePercent = 0)
    {
        bool boss=rank==EnemyGradeType.Boss, elite=rank==EnemyGradeType.Elite;
        var weights=boss ? new[]{0f,0f,45f,35f,17f,2.5f,.5f} : elite ? new[]{10f,30f,40f,16f,3.7f,.28f,.02f} : new[]{45f,32f,18f,4.5f,.49f,.009f,.001f};
        int maximum=level>=25?6:level>=18?5:level>=10?4:level>=5?3:2;
        float total=0;for(int i=0;i<=maximum;i++)total+=weights[i];
        float lower=0,bias=Mathf.Clamp(MapOptionPolicy.HighGradeRollBias(mapGrade),0,.999999f);
        var gradeMass=new float[8];
        for(int i=0;i<=maximum;i++)
        {
            float upper=lower+weights[i]/total;
            gradeMass[i]=Mathf.Max(0,upper-Mathf.Max(lower,bias))/(1-bias)*(i>=2?1+Mathf.Max(0,rarePercent)*.01f:1);
            lower=upper;
        }
        // Dedicated gem source: late-dungeon bosses, independent of other items' global cursed switch.
        if(boss && level>=25) gradeMass[(int)ItemGrade.Cursed]=.001f;
        var candidates=Catalog.Where(x=>ElementGemItemData.IsAllowed(x.element,x.fixedGrade)&&gradeMass[(int)x.fixedGrade]>0).ToArray();
        if(candidates.Length==0) return null;
        var counts=new int[8];foreach(var data in candidates)counts[(int)data.fixedGrade]++;
        float sum=0;foreach(var data in candidates)sum+=gradeMass[(int)data.fixedGrade]/counts[(int)data.fixedGrade];
        float pick=Mathf.Clamp01(roll)*sum;
        foreach(var data in candidates) if((pick-=gradeMass[(int)data.fixedGrade]/counts[(int)data.fixedGrade])<0) return data;
        return candidates[candidates.Length-1];
    }
    public static ItemData CreateRoll(int level, EnemyGradeType rank, ItemGrade mapGrade=ItemGrade.Common,float rarePercent=0)
    {
        var data=Select(UnityEngine.Random.value,OverburstGrowthRules.ClampLevel(level),rank,mapGrade,rarePercent);
        return data!=null ? new ItemData(data,OverburstGrowthRules.ClampLevel(level),data.fixedGrade,1) : null;
    }
    public static ItemData Roll(EnemyRank rank,int level,ItemGrade mapGrade=ItemGrade.Common,float rarePercent=0)
    {
        var kind=rank!=null?rank.GradeType:EnemyGradeType.Normal;
        float chance=CombatDebugSettings.ApplyRunLootChance(kind==EnemyGradeType.Boss?1f:kind==EnemyGradeType.Elite?.12f:.03f);
        float effectiveChance=Mathf.Min(1,chance*(1+MapRunBuffs.Bonus(MapBuffKind.ItemDrop)));
        float chanceRoll=UnityEngine.Random.value;
        if(effectiveChance<=0 || (effectiveChance<1 && chanceRoll>=effectiveChance))return null;
        return CreateRoll(level,kind,mapGrade,rarePercent);
    }
    public static int Value(ItemData item)
    {
        if(!(item?.baseData is ElementGemItemData)) return item?.baseData!=null?item.baseData.sellPrice:0;
        float[] multipliers={1,2,4,8,16,24,32,24};
        return Mathf.Max(1,Mathf.RoundToInt(item.baseData.sellPrice*multipliers[(int)item.grade]*(1+.02f*(item.level-1))));
    }
}
