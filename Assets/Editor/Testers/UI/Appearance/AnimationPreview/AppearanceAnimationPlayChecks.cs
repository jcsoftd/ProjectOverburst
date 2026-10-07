using System;
using System.Collections;
using System.IO;
using System.Linq;
using Overburst.Appearance;
using Overburst.Appearance.AnimationPreview;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.UI;

public sealed class AppearanceAnimationPlayChecks:AppearancePlayExtension
{
    static float FootContactHeight(AppearanceCustomizationPanel panel)
    {
        var animator=panel.preview.Model.GetComponent<Animator>();
        var feet=new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes}.Select(animator.GetBoneTransform).Where(t=>t).ToArray();
        var skin=panel.preview.Model.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="Female_Body_Nakid_Leg");
        var ids=new System.Collections.Generic.HashSet<int>(skin.bones.Select((t,i)=>new{t,i}).Where(x=>feet.Contains(x.t)).Select(x=>x.i));
        var weights=skin.sharedMesh.boneWeights;var baked=new Mesh();
        try
        {
            skin.BakeMesh(baked);var vertices=baked.vertices;float lowest=float.PositiveInfinity;
            for(int i=0;i<weights.Length;i++)
            {
                var w=weights[i];float footWeight=(ids.Contains(w.boneIndex0)?w.weight0:0)+(ids.Contains(w.boneIndex1)?w.weight1:0)+(ids.Contains(w.boneIndex2)?w.weight2:0)+(ids.Contains(w.boneIndex3)?w.weight3:0);
                if(footWeight>=.5f)lowest=Mathf.Min(lowest,skin.transform.TransformPoint(vertices[i]).y-panel.preview.Model.transform.root.position.y);
            }
            return lowest;
        }
        finally{UnityEngine.Object.Destroy(baked);}
    }
    public override IEnumerator Verify(AppearanceCustomizationPanel panel,string output)
    {
        var tab=panel.GetComponentInChildren<AppearanceAnimationPreviewTab>(true);if(!tab)yield break;
        string path=Path.Combine(output,"Developer","appearance-animation-exclusions.json");
        tab.SetExclusionStore(new AppearanceAnimationExclusions(path));
        var before=AccountGameplaySession.Current.ReadAppearance();var position=PlayerContext.Instance.CurrentActor.transform.position;
        var developer=panel.GetComponentInChildren<AppearanceDeveloperPreview>(true);
        var forbidden=tab.library.animations.First(o=>o.category=="Kawaii");
        AppearanceCustomizationPlayVerifier.Check(!tab.IsAvailable&&!tab.tabButton.gameObject.activeInHierarchy&&!tab.page.activeSelf,"default equipment preview hides the animation tab and page");
        panel.ShowExtension(tab);tab.Select(forbidden);panel.preview.Play(forbidden.clip);
        AppearanceCustomizationPlayVerifier.Check(!panel.ActiveExtension&&tab.Selected==null&&panel.preview.ActiveClip==panel.catalog.idleClip,"non-nude programmatic tab selection and direct playback are blocked");
        yield return AppearanceCustomizationPlayVerifier.Click(developer.iconButton);
        AppearanceCustomizationPlayVerifier.Check(tab.IsAvailable&&tab.tabButton.gameObject.activeInHierarchy,"developer nude preview enables the animation tab");
        yield return AppearanceCustomizationPlayVerifier.Click(tab.tabButton);
        yield return AppearanceCustomizationPlayVerifier.Click(tab.kawaiiCategory);
        AppearanceCustomizationPlayVerifier.Check(tab.kawaiiCategory.GetComponentInChildren<TMPro.TMP_Text>().text=="모션"&&tab.list.GetComponentsInChildren<Button>().Length==416,"motion category label and imported clip count");
        var row=tab.list.GetComponentsInChildren<Button>().First();yield return AppearanceCustomizationPlayVerifier.Click(row);
        AppearanceCustomizationPlayVerifier.Check(tab.Selected!=null&&tab.Selected.thumbnail&&panel.preview.ActiveClip==tab.Selected.clip,"pointer selects actual motion and authored pose thumbnail");
        var selected=tab.Selected;yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-motion-selected");
        yield return AppearanceCustomizationPlayVerifier.Click(tab.playPause);AppearanceCustomizationPlayVerifier.Check(panel.preview.IsPaused,"pointer pauses selected animation");
        yield return AppearanceCustomizationPlayVerifier.Click(tab.playPause);AppearanceCustomizationPlayVerifier.Check(!panel.preview.IsPaused,"pointer resumes selected animation");
        yield return AppearanceCustomizationPlayVerifier.Click(developer.iconButton);
        AppearanceCustomizationPlayVerifier.Check(!tab.tabButton.gameObject.activeInHierarchy&&!tab.page.activeSelf&&!panel.ActiveExtension&&panel.appearanceOptions.activeSelf&&tab.Selected==null&&panel.preview.ActiveClip==panel.catalog.idleClip&&panel.Session.PreviewBody==AppearancePreviewBody.Equipment,"turning nude off during playback hides tab, stops motion and restores equipment appearance");
        tab.Select(selected);panel.preview.Play(selected.clip);tab.playPause.onClick.Invoke();
        AppearanceCustomizationPlayVerifier.Check(panel.preview.ActiveClip==panel.catalog.idleClip&&!panel.preview.IsPaused,"hidden animation callbacks cannot resume a clip");
        yield return AppearanceCustomizationPlayVerifier.Click(developer.iconButton);yield return AppearanceCustomizationPlayVerifier.Click(tab.tabButton);tab.Select(selected);
        panel.preview.Seek(Mathf.Min(.4f,selected.clip.length));panel.preview.SetSpeed(.75f);
        yield return AppearanceCustomizationPlayVerifier.Click(tab.excludeSelected);
        AppearanceCustomizationPlayVerifier.Check(tab.list.GetComponentsInChildren<Button>().Length==415&&new AppearanceAnimationExclusions(path).Contains(selected.id),"pointer excludes selected motion and persists removal record");
        var record=new AppearanceAnimationExclusions(path).Document.excluded.Single();
        AppearanceCustomizationPlayVerifier.Check(record.assetGuid==selected.assetGuid&&record.assetPath==selected.assetPath&&record.localFileId==selected.localFileId&&selected.clip,"exclusion retains source cleanup identity and source clip");
        yield return AppearanceCustomizationPlayVerifier.Click(tab.excludedList);yield return AppearanceCustomizationPlayVerifier.Click(tab.list.GetComponentsInChildren<Button>().First());
        yield return AppearanceCustomizationPlayVerifier.Click(tab.excludeSelected);
        AppearanceCustomizationPlayVerifier.Check(!new AppearanceAnimationExclusions(path).Contains(selected.id),"pointer restores excluded motion");
        yield return AppearanceCustomizationPlayVerifier.Click(tab.kawaiiCategory);
        var backflip=tab.library.animations.Single(o=>o.displayName=="KA_Idle55_Backflip");tab.Select(backflip);panel.preview.Seek(backflip.clip.length*.5f);panel.preview.TogglePause();
        AppearanceCustomizationPlayVerifier.Check(Mathf.Abs(FootContactHeight(panel))<.0003f&&Mathf.Abs(panel.preview.Model.transform.localPosition.x)<.0001f&&Mathf.Abs(panel.preview.Model.transform.localPosition.z)<.0001f,"backflip preview keeps the foot contact on the fixed floor without horizontal root travel");
        yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-backflip");
        var wave=tab.library.animations.Single(o=>o.displayName=="KA_Idle16_WaveHands");tab.Select(wave);panel.preview.Seek(wave.clip.length*.4f);panel.preview.TogglePause();
        yield return AppearanceCustomizationPlayVerifier.Frames(2);
        var camera=(Camera)typeof(AppearanceCharacterPreview).GetField("viewCamera",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(panel.preview);
        var raisedHand=panel.preview.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);var viewport=camera.WorldToViewportPoint(raisedHand.position);
        AppearanceCustomizationPlayVerifier.Check(viewport.y>.05f&&viewport.y<.95f&&viewport.x>.25f&&viewport.x<.75f,"raised-hand motion fits the character viewport");
        var waveShot=panel.preview.CaptureStill(960,540,wave.clip.length*.4f);
        try
        {
            var pixels=waveShot.GetPixels32();int bottom=waveShot.height,top=-1;
            for(int y=0;y<waveShot.height;y++)for(int x=0;x<waveShot.width;x++)
            {
                var pixel=pixels[y*waveShot.width+x];
                if(pixel.a>230&&Mathf.Max(pixel.r,pixel.g,pixel.b)>10){bottom=Mathf.Min(bottom,y);top=Mathf.Max(top,y);}
            }
            File.WriteAllBytes(Path.Combine(output,"wave-character.png"),waveShot.EncodeToPNG());
            AppearanceCustomizationPlayVerifier.Check(top<waveShot.height-8&&bottom>8,"actual wave mesh including fingertips retains visible frame margins");
        }
        finally{UnityEngine.Object.Destroy(waveShot);}
        yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-wave");
        var groundRows=new System.Collections.Generic.List<object>();
        foreach(var option in tab.library.animations)
        {
            tab.Select(option);panel.preview.Seek(Mathf.Min(option.clip.length*.4f,.8f));yield return AppearanceCustomizationPlayVerifier.Frames(1);
            float footHeight=FootContactHeight(panel);
            if(float.IsNaN(footHeight)||Mathf.Abs(footHeight)>.0003f)throw new InvalidOperationException("Preview foot contact left the floor: "+option.id+" / "+footHeight);
            groundRows.Add(new{option.id,option.displayName,footHeight,root=panel.preview.Model.transform.localPosition.ToString("F6")});
            if(option.category=="Expression")
            {
                var head=panel.preview.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                if(head.position.y-panel.preview.Model.transform.root.position.y<1)throw new InvalidOperationException("Expression reset humanoid body pose: "+option.id);
                if(option.displayName=="P09_Facial_Smile")yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-expression-smile");
            }
            if(!panel.preview.Model||panel.preview.ActiveClip!=option.clip||panel.preview.Model.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c.GetType().Namespace!="MagicaCloth2"&&!(c is AppearancePreviewMotionRoot))||panel.preview.Model.GetComponentsInChildren<Collider>(true).Length!=0)
                throw new InvalidOperationException("Isolated motion playback failed: "+option.id);
        }
        File.WriteAllText(Path.Combine(output,"all-motion-grounding.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status="PASS",count=groundRows.Count,rows=groundRows},Newtonsoft.Json.Formatting.Indented));
        AppearanceCustomizationPlayVerifier.Check(tab.library.animations.Length==513,"all 513 game motion and expression clips preview without gameplay receivers");
        AppearanceCustomizationPlayVerifier.Check(groundRows.Count==513,"all 513 motion and expression previews keep measured skin foot contact within 0.3 mm of the floor");
        AppearanceCustomizationPlayVerifier.Check(AccountGameplaySession.Current.ReadAppearance().Equals(before)&&Vector3.Distance(position,PlayerContext.Instance.CurrentActor.transform.position)<.15f,"motion preview preserves committed appearance and player position");
        yield return AppearanceCustomizationPlayVerifier.Click(panel.appearanceTab);
        AppearanceCustomizationPlayVerifier.Check(panel.ActiveExtension==null&&panel.appearanceOptions.activeSelf&&panel.preview.ActiveClip==panel.catalog.idleClip,"appearance tab restores base idle preview");
        yield return AppearanceCustomizationPlayVerifier.Click(developer.iconButton);
        AppearanceCustomizationPlayVerifier.Check(!tab.tabButton.gameObject.activeInHierarchy&&panel.Session.PreviewBody==AppearancePreviewBody.Equipment,"leaving developer nude returns to gear preview and hides animation tab");
        var owner=panel.Owner;panel.Close();yield return AppearanceCustomizationPlayVerifier.Frames(3);
        var root=tab.gameObject;root.SetActive(false);
        try
        {
            AppearanceCustomizationPlayVerifier.Check(panel.Open(owner)&&panel.Session.PreviewBody==AppearancePreviewBody.Equipment&&panel.Session.EquipmentExampleId==panel.catalog.equipmentExamples[0].id,"core customization opens with entire animation module disabled");
            panel.Close();yield return AppearanceCustomizationPlayVerifier.Frames(3);
        }
        finally{root.SetActive(true);}
        AppearanceCustomizationPlayVerifier.Check(panel.Open(owner)&&!tab.IsAvailable&&!tab.tabButton.gameObject.activeInHierarchy,"core customization reopens with hidden animation tab after optional module return");
    }
}
