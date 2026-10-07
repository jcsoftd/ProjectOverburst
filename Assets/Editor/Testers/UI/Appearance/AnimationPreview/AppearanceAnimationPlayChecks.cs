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
    // Evaluate real successive Play frames, including loop boundaries. No frame-by-frame Seek or foot lock.
    static IEnumerator VerifyContinuousPlayback(AppearanceCustomizationPanel panel,AppearanceAnimationPreviewTab tab,Camera camera,string output)
    {
        var names=new[]{"KA_Idle01_breathing","KA_Idle16_WaveHands","KA_Idle13_Dance01","KA_Idle72_LeanForward",
            "KA_Idle47_Scaring","KA_Idle73_IdolPose","KA_Walk01","KA_Idle07_SpinningJump","KA_Idle55_Backflip",
            "KA_Sit_CrossLegged_Loop","KA_Idle46_SitFloor","KA_Jump01_Start"};
        var rows=new System.Collections.Generic.List<object>();
        panel.preview.SetFraming(AppearanceFraming.FullBody);panel.preview.ResetView();
        foreach(string name in names)
        {
            var option=tab.library.animations.Single(o=>o.displayName==name);tab.Select(option);panel.preview.SetSpeed(2);panel.preview.SetLooping(true);
            var animator=panel.preview.Model.GetComponent<Animator>();var stage=panel.preview.Model.transform.root;
            var origin=panel.preview.Model.transform.localPosition;float initialBody=stage.InverseTransformPoint(animator.bodyPosition).y;
            var binding=UnityEditor.AnimationUtility.GetCurveBindings(option.clip).Single(b=>b.propertyName=="RootT.y");
            var curve=UnityEditor.AnimationUtility.GetEditorCurve(option.clip,binding);float floor=camera.WorldToViewportPoint(stage.position).y;
            float previous=0,heightError=0,floorError=0,rootError=0,minBody=float.PositiveInfinity,maxBody=float.NegativeInfinity;
            int loops=0,frames=0,targetLoops=name=="KA_Idle16_WaveHands"||name=="KA_Idle55_Backflip"||name=="KA_Walk01"?2:1;
            double deadline=Time.realtimeSinceStartupAsDouble+option.clip.length*2+10;
            while(loops<targetLoops&&Time.realtimeSinceStartupAsDouble<deadline)
            {
                yield return AppearanceCustomizationPlayVerifier.Frames(1);
                float time=panel.preview.PlaybackTime;if(time<previous-.01f)loops++;previous=time;frames++;
                float body=stage.InverseTransformPoint(animator.bodyPosition).y;
                heightError=Mathf.Max(heightError,Mathf.Abs(body-initialBody-(curve.Evaluate(time)-curve.Evaluate(0))*animator.humanScale));
                floorError=Mathf.Max(floorError,Mathf.Abs(camera.WorldToViewportPoint(stage.position).y-floor));
                rootError=Mathf.Max(rootError,Vector3.Distance(origin,panel.preview.Model.transform.localPosition));
                minBody=Mathf.Min(minBody,body);maxBody=Mathf.Max(maxBody,body);
            }
            rows.Add(new{name,frames,loops,targetLoops,heightError,floorError,rootError,bodyHeightSpan=maxBody-minBody,lastTime=previous,paused=panel.preview.IsPaused,speed=panel.preview.PlaybackSpeed});
            File.WriteAllText(Path.Combine(output,"continuous-playback-progress.json"),Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));
            AppearanceCustomizationPlayVerifier.Check(loops==targetLoops&&frames>=4&&heightError<.001f&&floorError<.0001f&&rootError<.0001f,
                "continuous P09 playback preserves body height and fixed stage through loops: "+name+" loops="+loops+" frames="+frames+" height="+heightError+" floor="+floorError+" root="+rootError+" time="+previous);
            panel.preview.TogglePause();yield return AppearanceCustomizationPlayVerifier.Frames(1);
            float pausedTime=panel.preview.PlaybackTime,pausedBody=animator.bodyPosition.y;
            yield return AppearanceCustomizationPlayVerifier.Frames(3);
            AppearanceCustomizationPlayVerifier.Check(Mathf.Abs(panel.preview.PlaybackTime-pausedTime)<.0001f&&Mathf.Abs(animator.bodyPosition.y-pausedBody)<.0002f,
                "pause freezes authored pose without moving the character root: "+name);
            var drag=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){delta=new Vector2(90,0)};
            panel.preview.OnBeginDrag(drag);panel.preview.OnDrag(drag);panel.preview.OnEndDrag(drag);
            yield return AppearanceCustomizationPlayVerifier.Frames(2);
            AppearanceCustomizationPlayVerifier.Check(Mathf.Abs(camera.WorldToViewportPoint(stage.position).y-floor)<.0001f&&Vector3.Distance(origin,panel.preview.Model.transform.localPosition)<.0001f,
                "rotation retains the fixed preview stage: "+name);
            panel.preview.ResetView();panel.preview.SetSpeed(.5f);panel.preview.TogglePause();
            yield return AppearanceCustomizationPlayVerifier.Frames(3);
            AppearanceCustomizationPlayVerifier.Check(!panel.preview.IsPaused&&panel.preview.PlaybackTime>pausedTime,"speed change resumes native motion: "+name);
        }
        panel.preview.SetSpeed(1);
        File.WriteAllText(Path.Combine(output,"continuous-playback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status="PASS",count=rows.Count,rows},Newtonsoft.Json.Formatting.Indented));
    }
    static IEnumerator VerifyManualView(AppearanceCustomizationPanel panel,AppearanceAnimationPreviewTab tab)
    {
        var camera=(Camera)typeof(AppearanceCharacterPreview).GetField("viewCamera",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(panel.preview);
        foreach(var button in new[]{panel.faceFrame,panel.upperFrame,panel.fullFrame})
        {
            yield return AppearanceCustomizationPlayVerifier.Click(button);
            panel.preview.OnScroll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){scrollDelta=new Vector2(0,-3)});
            var framing=panel.Session.Framing;var position=camera.transform.localPosition;var rotation=camera.transform.localRotation;float fov=camera.fieldOfView;
            foreach(string category in new[]{"Game","Kawaii","Expression"})
            {
                var option=tab.library.animations.First(o=>o.category==category);tab.Select(option);panel.preview.TogglePause();panel.preview.Seek(option.clip.length*.3f);
                yield return AppearanceCustomizationPlayVerifier.Frames(2);
                AppearanceCustomizationPlayVerifier.Check(panel.Session.Framing==framing&&Vector3.Distance(camera.transform.localPosition,position)<.0001f&&Quaternion.Angle(camera.transform.localRotation,rotation)<.001f&&Mathf.Abs(camera.fieldOfView-fov)<.001f,
                    "motion selection and seeking preserve manual framing and zoom: "+framing+" / "+category);
            }
        }
        yield return AppearanceCustomizationPlayVerifier.Click(panel.fullFrame);panel.preview.ResetView();
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
        yield return VerifyManualView(panel,tab);
        var backflip=tab.library.animations.Single(o=>o.displayName=="KA_Idle55_Backflip");tab.Select(backflip);panel.preview.Seek(backflip.clip.length*.5f);panel.preview.TogglePause();
        AppearanceCustomizationPlayVerifier.Check(FootContactHeight(panel)>.5f&&Mathf.Abs(panel.preview.Model.transform.localPosition.x)<.0001f&&Mathf.Abs(panel.preview.Model.transform.localPosition.z)<.0001f,"backflip preserves its authored air height while the preview origin stays in place");
        yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-backflip");
        panel.preview.ResetView();
        panel.preview.OnScroll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){scrollDelta=new Vector2(0,-5)});
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
        panel.preview.ResetView();
        var fixedCamera=camera.transform.localPosition;var fixedRotation=camera.transform.localRotation;float fixedFov=camera.fieldOfView;
        var groundRows=new System.Collections.Generic.List<object>();
        foreach(var option in tab.library.animations)
        {
            tab.Select(option);panel.preview.TogglePause();
            var animator=panel.preview.Model.GetComponent<Animator>();var stage=panel.preview.Model.transform.root;
            float initialBody=stage.InverseTransformPoint(animator.bodyPosition).y;
            var origin=panel.preview.Model.transform.localPosition;
            panel.preview.Seek(Mathf.Min(option.clip.length*.4f,.8f));yield return AppearanceCustomizationPlayVerifier.Frames(1);
            if(Vector3.Distance(camera.transform.localPosition,fixedCamera)>.0001f||Quaternion.Angle(camera.transform.localRotation,fixedRotation)>.001f||Mathf.Abs(camera.fieldOfView-fixedFov)>.001f)
                throw new InvalidOperationException("Animation changed manual camera framing: "+option.id);
            float footHeight=FootContactHeight(panel),heightError=0;
            if(float.IsNaN(footHeight)||float.IsInfinity(footHeight))throw new InvalidOperationException("Invalid skin pose: "+option.id);
            if(option.category=="Kawaii")
            {
                var binding=UnityEditor.AnimationUtility.GetCurveBindings(option.clip).Single(b=>b.propertyName=="RootT.y");
                var curve=UnityEditor.AnimationUtility.GetEditorCurve(option.clip,binding);
                heightError=Mathf.Abs(stage.InverseTransformPoint(animator.bodyPosition).y-initialBody-(curve.Evaluate(panel.preview.PlaybackTime)-curve.Evaluate(0))*animator.humanScale);
                if(heightError>.001f||Vector3.Distance(origin,panel.preview.Model.transform.localPosition)>.0001f||animator.applyRootMotion)
                    throw new InvalidOperationException("Authored humanoid body motion was lost: "+option.id+" / "+heightError);
            }
            else if(Mathf.Abs(footHeight)>.0003f)throw new InvalidOperationException("Existing game/expression contact changed: "+option.id+" / "+footHeight);
            groundRows.Add(new{option.id,option.displayName,option.category,footHeight,heightError,root=panel.preview.Model.transform.localPosition.ToString("F6")});
            if(option.category=="Expression")
            {
                var head=panel.preview.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                if(head.position.y-panel.preview.Model.transform.root.position.y<1)throw new InvalidOperationException("Expression reset humanoid body pose: "+option.id);
                if(option.displayName=="P09_Facial_Smile")yield return AppearanceCustomizationPlayVerifier.Capture("appearance-runtime-expression-smile");
            }
            if(!panel.preview.Model||panel.preview.ActiveClip!=option.clip||panel.preview.Model.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c.GetType().Namespace!="MagicaCloth2"&&!(c is AppearancePreviewMotionRoot))||panel.preview.Model.GetComponentsInChildren<Collider>(true).Length!=0)
                throw new InvalidOperationException("Isolated motion playback failed: "+option.id);
        }
        File.WriteAllText(Path.Combine(output,"all-motion-playback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status="PASS",count=groundRows.Count,rows=groundRows},Newtonsoft.Json.Formatting.Indented));
        AppearanceCustomizationPlayVerifier.Check(tab.library.animations.Length==513,"all 513 game motion and expression clips preview with a fixed camera and without gameplay receivers");
        AppearanceCustomizationPlayVerifier.Check(groundRows.Count==513,"416 motions preserve authored body height and 97 existing game/expression clips retain their contact behavior");
        yield return VerifyContinuousPlayback(panel,tab,camera,output);
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
