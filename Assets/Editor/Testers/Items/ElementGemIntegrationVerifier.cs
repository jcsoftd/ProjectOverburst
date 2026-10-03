using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Overburst.Persistence;

public static class ElementGemIntegrationVerifier
{
    public static string Run(string output)
    {
        var checks=new List<string>();var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        void Check(bool value,string label)=>ElementGemVerifier.Require(value,label,checks);
        void Reject(Action action,string label){bool failed=false;try{action();}catch(IOException){failed=true;}catch(InvalidDataException){failed=true;}Check(failed,label);}
        var state=NewAccountFactory.Create(registry);
        string directory=Path.Combine(output,"GOAL07_BoundaryAccount_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var stale=new EasySaveAccountStore(directory);stale.Save(state,"a");
        var writer=new EasySaveAccountStore(directory);writer.Load();state.experience=1;writer.Save(state,"b");
        Reject(()=>stale.Save(state,"stale"),"stale writer rejected without overwriting newer generation");
        string a=Path.Combine(directory,"account-A.es3"),b=Path.Combine(directory,"account-B.es3");
        byte[] ab=File.ReadAllBytes(a),bb=File.ReadAllBytes(b);
        File.WriteAllText(a,"corrupt fixture");File.WriteAllText(b,"corrupt fixture");
        Reject(()=>new EasySaveAccountStore(directory).Load(),"both corrupt generations reject instead of making new account");
        File.WriteAllBytes(a,ab);File.WriteAllBytes(b,bb);
        var invalid=ItemSnapshotCodec.CopyValues(state);invalid.items[0].contentId="missing.current.content";
        writer.Save(invalid,"missing-content");
        Reject(()=>AccountInvariants.Validate(new EasySaveAccountStore(directory).Load(),registry),"latest missing content rejects without older valid-content fallback");
        var common=registry.Resolve<ElementGemItemData>("item.elementgem.fire.common");var gem=new ItemData(common,1,common.fixedGrade);
        gem.gemState=new ElementGemState {archetype=ElementGemArchetype.Weak,rolls=new List<ElementGemRoll>()};
        foreach(var id in new[]{"gem.common.normal_enemy_damage","gem.common.elite_boss_damage","gem.common.max_health","gem.common.armor"})gem.gemState.rolls.Add(new ElementGemRoll{optionId=id});
        Check(ElementGemQuality.IsValid(common,1,common.fixedGrade,1,gem.gemState)&&ElementGemQuality.Calculate(gem).Common.WeakDamage>0,"valid ordinary zero-star junk rows retain fixed weak effect");
        foreach(var pair in new[]{new[]{"ice","freeze_threshold_reduction"},new[]{"electric","chain_hops"},new[]{"dark","corrosion_max_stacks"},new[]{"light","radiance_max_stacks"}})
        {
            var definition=registry.Resolve<ElementGemItemData>("item.elementgem."+pair[0]+".legendary");var item=new ItemData(definition,20,definition.fixedGrade);
            var row=new ElementGemRoll{optionId="gem."+pair[0]+"."+pair[1]};
            Check(ElementGemQuality.Value(item,row)==0,"integer zero-star zero effect "+pair[0]);
            row.stars.Add(WeaponGradeStarType.White);float one=ElementGemQuality.Value(item,row);
            row.stars.AddRange(new[]{WeaponGradeStarType.Yellow,WeaponGradeStarType.Yellow});float two=ElementGemQuality.Value(item,row);
            Check(one==(pair[0]=="light"?10:1)&&two==(pair[0]=="ice"?1:pair[0]=="light"?20:2),"integer stages and independent cap "+pair[0]);
            row.stars.Clear();row.stars.Add(WeaponGradeStarType.Red);Check(ElementGemQuality.Value(item,row)==0,"negative integer weight floors zero "+pair[0]);
        }
        Check(MeleeElementSfxService.MaximumPoolSize==32&&OverburstElementTuning.Current.SafeDarkBarrageMaxLaunchPerFrame>0,"existing SFX pool and projectile frame budgets retained");
        string result=JsonConvert.SerializeObject(new{status="PASS",checks},Formatting.Indented);
        File.WriteAllText(Path.Combine(output,"GOAL07_Core.json"),result);return result;
    }
}
