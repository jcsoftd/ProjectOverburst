using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicaCloth2;
using Newtonsoft.Json;
using Overburst.Appearance;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class AppearancePhysicsPlayChecks : AppearancePlayExtension
{
    static MagicaCloth[] Active(AppearanceCustomizationPanel p) => p.preview.Physics.Cloths.Where(c=>c&&c.isActiveAndEnabled).ToArray();
    static IEnumerator Ready(AppearanceCustomizationPanel p)
    {
        double until=Time.realtimeSinceStartupAsDouble+15;
        while(Active(p).Any(c=>!c.IsValid())&&Time.realtimeSinceStartupAsDouble<until)yield return null;
        AppearanceCustomizationPlayVerifier.Check(Active(p).Length>0&&Active(p).All(c=>c.IsValid()),"selected preview physics teams build and become valid");
        AppearanceCustomizationPlayVerifier.Check(Active(p).All(c=>c.SerializeData.updateMode==ClothUpdateMode.Unscaled),"selected physics uses unscaled time in the customization UI");
    }
    static Transform[] Bones(IEnumerable<MagicaCloth> cloths) => cloths.SelectMany(c=>c.SerializeData.rootBones).Where(t=>t).SelectMany(t=>t.GetComponentsInChildren<Transform>()).Distinct().ToArray();
    static float BoneDelta(Transform[] bones,Vector3[] positions,Quaternion[] rotations)
    {
        float value=0;
        for(int i=0;i<bones.Length;i++)value=Mathf.Max(value,Vector3.Distance(bones[i].localPosition,positions[i]),Quaternion.Angle(bones[i].localRotation,rotations[i])*.01f);
        return value;
    }
    public override IEnumerator Verify(AppearanceCustomizationPanel p,string output)
    {
        p.ShowAppearance();var original=p.Session.Draft.Copy();string equipment=p.Session.EquipmentExampleId;
        bool hat=p.Session.HeadgearVisible;var rows=new List<object>();
        foreach(var hair in p.catalog.hairStyles)
        {
            p.Session.Edit(v=>v.hairStyleId=hair.id);yield return Ready(p);
            var active=Active(p);
            AppearanceCustomizationPlayVerifier.Check(hair.id.EndsWith("none",StringComparison.Ordinal)||active.Any(c=>c.SerializeData.rootBones.Any(t=>t&&t.name.StartsWith("Hair_",StringComparison.Ordinal))),"selected hair enables its configured physics: "+hair.id);
            rows.Add(new{hair=hair.id,active=active.Select(c=>c.name).ToArray()});
        }
        foreach(var gear in p.catalog.equipmentExamples)
        {
            p.Session.ShowEquipment(gear.id);yield return Ready(p);
            AppearanceCustomizationPlayVerifier.Check(gear.rendererNames.All(n=>p.preview.Model.GetComponentsInChildren<Renderer>(true).Any(r=>r.name==n&&r.gameObject.activeInHierarchy)),"all garment parts visible in preview: "+gear.id);
            rows.Add(new{equipment=gear.id,active=Active(p).Select(c=>c.name).ToArray()});
        }
        p.Session.ShowEquipment(p.catalog.equipmentExamples[1].id);yield return Ready(p);
        var nonHat=p.preview.Model.GetComponentsInChildren<Renderer>().Where(r=>!r.name.EndsWith("_Head",StringComparison.Ordinal)).Select(r=>r.name).OrderBy(n=>n).ToArray();
        if(!p.Session.HeadgearVisible)p.Session.ToggleHeadgear();
        yield return AppearanceCustomizationPlayVerifier.Click(p.headgearToggle);
        AppearanceCustomizationPlayVerifier.Check(!p.Session.HeadgearVisible&&!p.preview.Model.GetComponentsInChildren<Renderer>().Any(r=>r.name.EndsWith("_Head",StringComparison.Ordinal)),"pointer hides preview headgear");
        AppearanceCustomizationPlayVerifier.Check(nonHat.SequenceEqual(p.preview.Model.GetComponentsInChildren<Renderer>().Where(r=>!r.name.EndsWith("_Head",StringComparison.Ordinal)).Select(r=>r.name).OrderBy(n=>n)),"headgear toggle preserves hair and all other garments");
        yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-headgear-off");
        yield return AppearanceCustomizationPlayVerifier.Click(p.headgearToggle);
        AppearanceCustomizationPlayVerifier.Check(p.Session.HeadgearVisible&&p.preview.Model.GetComponentsInChildren<Renderer>().Any(r=>r.name.EndsWith("_Head",StringComparison.Ordinal)),"pointer restores preview headgear");
        p.Session.Edit(v=>v.hairStyleId=p.catalog.hairStyles.First(h=>h.rendererName=="Hair_09").id);
        p.Session.ShowEquipment(p.catalog.equipmentExamples[0].id);yield return Ready(p);
        p.preview.StopAnimation();p.preview.Seek(.7f);p.preview.TogglePause();yield return AppearanceCustomizationPlayVerifier.Frames(25);
        var live=Active(p);
        var hairBones=Bones(live.Where(c=>c.SerializeData.rootBones.Any(t=>t&&t.name.StartsWith("Hair_",StringComparison.Ordinal))));
        var bustBones=Bones(live.Where(c=>c.name=="Physics_Breast"));
        var garments=live.SelectMany(c=>c.SerializeData.sourceRenderers).OfType<SkinnedMeshRenderer>().Distinct().ToArray();
        AppearanceCustomizationPlayVerifier.Check(hairBones.Length>0&&bustBones.Length>0&&garments.Length>0,"rotation preview contains live hair upper-body and garment simulations");
        var hp=hairBones.Select(t=>t.localPosition).ToArray();var hr=hairBones.Select(t=>t.localRotation).ToArray();
        var bp=bustBones.Select(t=>t.localPosition).ToArray();var br=bustBones.Select(t=>t.localRotation).ToArray();
        var vertices=garments.Select(r=>r.sharedMesh.vertices).ToArray();
        float hairMovement=0,bustMovement=0,garmentMovement=0;var data=new PointerEventData(EventSystem.current);
        p.preview.OnBeginDrag(data);
        try
        {
            for(int frame=0;frame<48;frame++)
            {
                data.delta=new Vector2(frame<18?10:0,0);p.preview.OnDrag(data);yield return null;
                hairMovement=Mathf.Max(hairMovement,BoneDelta(hairBones,hp,hr));bustMovement=Mathf.Max(bustMovement,BoneDelta(bustBones,bp,br));
                for(int r=0;r<garments.Length;r++)
                {
                    var after=garments[r].sharedMesh.vertices;
                    for(int i=0;i<Mathf.Min(after.Length,vertices[r].Length);i++)garmentMovement=Mathf.Max(garmentMovement,Vector3.Distance(after[i],vertices[r][i]));
                }
                if(frame==15)yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-rotation-physics");
            }
        }
        finally{p.preview.OnEndDrag(data);}
        File.WriteAllText(Path.Combine(output,"physics-result.json"),JsonConvert.SerializeObject(new{hairMovement,bustMovement,garmentMovement,hairBones=hairBones.Length,bustBones=bustBones.Length,garments=garments.Length,rows},Formatting.Indented));
        AppearanceCustomizationPlayVerifier.Check(hairMovement>.0001f&&bustMovement>.0001f&&garmentMovement>.00001f,"rotation changes simulated hair upper-body and garment pose while animation is paused");
        p.preview.ResetView();p.preview.StopAnimation();p.Session.Edit(v=>{v.faceId=original.faceId;v.hairStyleId=original.hairStyleId;v.hairColorId=original.hairColorId;v.skinColorId=original.skinColorId;v.eyeColorId=original.eyeColorId;v.bodyShapeId=original.bodyShapeId;});
        p.Session.ShowEquipment(equipment);if(p.Session.HeadgearVisible!=hat)p.Session.ToggleHeadgear();yield return Ready(p);
    }
}
