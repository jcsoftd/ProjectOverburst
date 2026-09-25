using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>Compares unrolled definition stats with actual grade-applied stats, including production caps.</summary>
public static class OverburstUIQualityBreakdown
{
    public static bool TryGet(ItemData item,string label,out string text,out string finalValue,out string baselineValue,out string changeValue,out bool improved){
        text=null;finalValue=null;baselineValue=null;changeValue=null;improved=true;bool effectPrefix=false;bool capped=false;float before,after;string unit,deltaUnit;bool positive=true;
        if(item.baseData is WeaponItemData weapon){
            var baseline=WeaponStatCalculator.Calculate(weapon);var final=WeaponStatCalculator.Calculate(item);
            switch(label){
                case "데미지":before=baseline.damage;after=final.damage;unit=deltaUnit="";break;
                case "공격속도":before=baseline.meleeAttackSpeedMultiplier*100;after=final.meleeAttackSpeedMultiplier*100;unit="%";deltaUnit="%p";break;
                case "공격 범위":before=baseline.range;after=final.range;unit=deltaUnit="m";break;
                case "치명타확률":before=baseline.critChance;after=final.critChance;unit="%";deltaUnit="%p";break;
                case "치명타피해":before=baseline.critDamageMultiplier*100;after=final.critDamageMultiplier*100;unit="%";deltaUnit="%p";break;
                default:return false;
            }
            positive=after>=before;
            capped=label=="공격속도"&&Mathf.Approximately(after,WeaponGradeStatRoller.MaximumMeleeAttackSpeedMultiplier*100)||label=="공격 범위"&&Mathf.Approximately(after,baseline.range*WeaponGradeStatRoller.MaximumMeleeAttackRangeMultiplier)||label=="치명타확률"&&Mathf.Approximately(after,WeaponGradeStatRoller.MaximumMeleeCriticalChance)||label=="치명타피해"&&Mathf.Approximately(after,WeaponGradeStatRoller.MaximumMeleeCriticalDamageMultiplier*100);
        }else if(item.baseData is FlaskItemData flask){
            var rows=FlaskTooltip.Details(item).Split('\n').Where(s=>s.Contains("<b>")).Take(4).ToArray();int index=Array.FindIndex(rows,s=>s.StartsWith(label+" ",StringComparison.Ordinal));if(index<0)return false;
            var baseline=FlaskStats.Calculate(flask,null);var final=FlaskRuntime.Stats(item);
            if(index>=2){before=index==2?baseline.duration:baseline.cooldown;after=index==2?final.duration:final.cooldown;unit=deltaUnit="초";}
            else{
                effectPrefix=true;var effect=index==0?flask.primaryEffect:flask.secondaryEffect;float sign=effect==FlaskEffect.DirectDamageReduction||effect==FlaskEffect.DotDamageReduction||effect==FlaskEffect.IncomingImpactReduction||effect==FlaskEffect.SlowResistance?-1:1;
                before=(index==0?baseline.primary:baseline.secondary)*100*sign;after=(index==0?final.primary:final.secondary)*100*sign;
                bool points=effect==FlaskEffect.AttackSpeed||effect==FlaskEffect.AttackRadius||effect==FlaskEffect.CritChance||effect==FlaskEffect.CritDamage;
                unit=effect==FlaskEffect.HealPerSecond?"%/초":points?"%p":"%";deltaUnit=effect==FlaskEffect.HealPerSecond?"%p/초":"%p";
            }
        }else return false;
        float delta=after-before;
        string number(float value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
        finalValue=(effectPrefix&&after>0?"+":"")+number(after)+unit;
        baselineValue=number(before)+unit;changeValue=Mathf.Abs(delta)<.0001f?"—":(delta>0?"+":"")+number(delta)+deltaUnit;improved=positive;
        string change=Mathf.Abs(delta)<.0001f?"변화 없음":(delta>0?"+":"")+number(delta)+deltaUnit;
        string color=Mathf.Abs(delta)<.0001f?"#BBB6AB":positive?"#8BD0A6":"#E29A8E";
        text="<size=85%><color=#BBB6AB>기본 "+number(before)+unit+" → "+number(after)+unit+"   </color><color="+color+">각인 "+change+"</color>"+(capped?" <color=#D5B778>상한</color>":"")+"</size>";
        return true;
    }
}
