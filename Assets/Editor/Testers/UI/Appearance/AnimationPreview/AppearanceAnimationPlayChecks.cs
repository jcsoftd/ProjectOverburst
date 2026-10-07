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
    public override IEnumerator Verify(AppearanceCustomizationPanel panel,string output)
    {
        var tab=panel.GetComponentInChildren<AppearanceAnimationPreviewTab>(true);if(!tab)yield break;
        string path=Path.Combine(output,"Developer","appearance-animation-exclusions.json");
        tab.SetExclusionStore(new AppearanceAnimationExclusions(path));
        var before=AccountGameplaySession.Current.ReadAppearance();var position=PlayerContext.Instance.CurrentActor.transform.position;
        yield return AppearanceCustomizationPlayVerifier.Click(tab.tabButton);
        yield return AppearanceCustomizationPlayVerifier.Click(tab.kawaiiCategory);
        AppearanceCustomizationPlayVerifier.Check(tab.kawaiiCategory.GetComponentInChildren<TMPro.TMP_Text>().text=="모션"&&tab.list.GetComponentsInChildren<Button>().Length==416,"motion category label and imported clip count");
        var row=tab.list.GetComponentsInChildren<Button>().First();yield return AppearanceCustomizationPlayVerifier.Click(row);
        AppearanceCustomizationPlayVerifier.Check(tab.Selected!=null&&tab.Selected.thumbnail&&panel.preview.ActiveClip==tab.Selected.clip,"pointer selects actual motion and authored pose thumbnail");
        var selected=tab.Selected;yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-motion-selected");
        yield return AppearanceCustomizationPlayVerifier.Click(tab.playPause);AppearanceCustomizationPlayVerifier.Check(panel.preview.IsPaused,"pointer pauses selected animation");
        yield return AppearanceCustomizationPlayVerifier.Click(tab.playPause);AppearanceCustomizationPlayVerifier.Check(!panel.preview.IsPaused,"pointer resumes selected animation");
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
        AppearanceCustomizationPlayVerifier.Check(panel.preview.AnimationHeight>.5f&&Mathf.Abs(panel.preview.Model.transform.localPosition.x)<.0001f&&Mathf.Abs(panel.preview.Model.transform.localPosition.z)<.0001f,"backflip preserves source jump height while preview remains horizontally anchored");
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
        foreach(var option in tab.library.animations)
        {
            tab.Select(option);panel.preview.Seek(Mathf.Min(option.clip.length*.4f,.8f));yield return AppearanceCustomizationPlayVerifier.Frames(1);
            if(option.category=="Expression")
            {
                var head=panel.preview.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                if(head.position.y-panel.preview.Model.transform.root.position.y<1)throw new InvalidOperationException("Expression reset humanoid body pose: "+option.id);
                if(option.displayName=="P09_Facial_Smile")yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-expression-smile");
            }
            if(!panel.preview.Model||panel.preview.ActiveClip!=option.clip||panel.preview.Model.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c.GetType().Namespace!="MagicaCloth2"&&!(c is AppearancePreviewMotionRoot))||panel.preview.Model.GetComponentsInChildren<Collider>(true).Length!=0)
                throw new InvalidOperationException("Isolated motion playback failed: "+option.id);
        }
        AppearanceCustomizationPlayVerifier.Check(tab.library.animations.Length==513,"all 513 game motion and expression clips preview without gameplay receivers");
        AppearanceCustomizationPlayVerifier.Check(AccountGameplaySession.Current.ReadAppearance().Equals(before)&&Vector3.Distance(position,PlayerContext.Instance.CurrentActor.transform.position)<.15f,"motion preview preserves committed appearance and player position");
        yield return AppearanceCustomizationPlayVerifier.Click(panel.appearanceTab);
        AppearanceCustomizationPlayVerifier.Check(panel.ActiveExtension==null&&panel.appearanceOptions.activeSelf&&panel.preview.ActiveClip==panel.catalog.idleClip,"appearance tab restores base idle preview");
        var owner=panel.Owner;panel.Close();yield return AppearanceCustomizationPlayVerifier.Frames(3);
        var root=tab.gameObject;root.SetActive(false);
        try
        {
            AppearanceCustomizationPlayVerifier.Check(panel.Open(owner)&&panel.Session.PreviewBody==AppearancePreviewBody.Equipment&&panel.Session.EquipmentExampleId==panel.catalog.equipmentExamples[0].id,"core customization opens with entire animation module disabled");
            panel.Close();yield return AppearanceCustomizationPlayVerifier.Frames(3);
        }
        finally{root.SetActive(true);}
        AppearanceCustomizationPlayVerifier.Check(panel.Open(owner),"core customization reopens after optional module return");
    }
}
