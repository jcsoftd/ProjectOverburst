using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.EditorTools.ComboMaker;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

public static class ComboMakerUpdateVerifier
{
    private static readonly string Output=Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/20261005_Update");
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

    public static string Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 EditMode에서 실행하세요.");
        var log=new List<string>();
        var scenes=Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount)
            .Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).Select(s=>(s.handle,s.isDirty,s.rootCount)).ToArray();
        int previewScenes=EditorSceneManager.previewSceneCount;
        var weapon=AssetDatabase.FindAssets("t:WeaponItemData",new[]{"Assets/ProjectOverburst/03_Features/Weapons"})
            .Select(g=>AssetDatabase.LoadAssetAtPath<WeaponItemData>(AssetDatabase.GUIDToAssetPath(g)))
            .First(w=>Enum.GetValues(typeof(ComboMakerAttackMode)).Cast<ComboMakerAttackMode>().All(m=>ComboMakerAttackBinding.Asset(w.GetMeleeDefinition(),m)!=null));
        var originals=Enum.GetValues(typeof(ComboMakerAttackMode)).Cast<ComboMakerAttackMode>()
            .Select(m=>ComboMakerAttackBinding.Asset(weapon.GetMeleeDefinition(),m)).Distinct().ToDictionary(a=>a,a=>EditorJsonUtility.ToJson(a));
        var bytes=originals.Keys.ToDictionary(a=>AssetDatabase.GetAssetPath(a),a=>File.ReadAllBytes(AssetDatabase.GetAssetPath(a)));
        string failure=null;
        try
        {
            foreach(ComboMakerAttackMode mode in Enum.GetValues(typeof(ComboMakerAttackMode)))
                CheckAttack(weapon,mode,log);
            CheckHitAndBlood(weapon,log);
            CheckAuthoring(weapon,log);
            CheckNativeUI(weapon,log);
        }
        catch(Exception e){failure=e.ToString();log.Add("FAIL: "+failure);}
        finally
        {
            foreach(var original in originals)
                Check(EditorJsonUtility.ToJson(original.Key)==original.Value,"게임 원본 JSON 보존: "+original.Key.name,log,ref failure);
            foreach(var original in bytes)
                Check(File.ReadAllBytes(original.Key).SequenceEqual(original.Value),"게임 원본 디스크 보존: "+original.Key,log,ref failure);
            Check(EditorSceneManager.previewSceneCount==previewScenes,"본인 프리뷰 씬 모두 반환",log,ref failure);
            foreach(var before in scenes)
            {
                var scene=Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).First(s=>s.handle==before.handle);
                Check(scene.isDirty==before.isDirty&&scene.rootCount==before.rootCount,"열린 씬 dirty/root 보존",log,ref failure);
            }
            Directory.CreateDirectory(Path.Combine(Output,"data"));
            File.WriteAllText(Path.Combine(Output,"data","verification.json"),JsonConvert.SerializeObject(new{status=failure==null?"PASS":"FAIL",checks=log,failure,player="NOT_RUN: Editor 제작 도구 변경",play="NOT_RUN: 독립 EditMode 프리뷰 검증"},Formatting.Indented));
        }
        if(failure!=null)throw new InvalidOperationException(failure);
        return string.Join("\n",log);
    }

    private static void CheckAttack(WeaponItemData weapon,ComboMakerAttackMode mode,List<string> log)
    {
        using(var session=new ComboMakerSession())
        using(var preview=new ComboMakerPreview())
        {
            session.LoadWeapon(weapon,mode);
            Require(!session.Dirty&&!session.SourceChanged,mode+" 새 사본은 변경 없음",log);
            Require(session.Asset==ComboMakerAttackBinding.Asset(weapon.GetMeleeDefinition(),mode),mode+" 저장 대상 연결",log);
            Require(session.Validate().Count==0,mode+" 최신 자산 유효: "+string.Join(" / ",session.Validate()),log);
            MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(session.Source,session.Working);
            preview.HeavyDefinition=session.HeavyWorking;preview.ShowKnockback=false;preview.ShowBlood=false;
            preview.Load(weapon,session.Working);
            for(int step=0;step<session.Working.StepCount;step++)
            {
                preview.Begin(step,false,true);preview.PlaceTargetAtImpact();preview.Seek(.98f);
                Require(preview.Ready&&preview.HitCount>0,mode+" "+(step+1)+"타 재생·판정: "+preview.HitCount,log);
                int hits=preview.HitCount,cues=preview.CueCount;
                preview.Seek(.01f);preview.Seek(.98f);
                Require(hits==preview.HitCount&&cues==preview.CueCount,mode+" 역스크럽 이벤트 재구성",log);
            }
            if(session.IsHeavy)
            {
                float impact=session.Working.steps[0].attackPhases[session.HeavyWorking.SafeDischargePhaseIndex].SafeStart;
                preview.Seek(Mathf.Max(0,impact-.002f));Require(!preview.HeavyDischarged,mode+" 방출 이전",log);
                preview.Seek(impact+.002f);Require(preview.HeavyDischarged,mode+" 지정 판정에서 방출",log);
            }
        }
    }

    private static void CheckHitAndBlood(WeaponItemData weapon,List<string> log)
    {
        var enemy=AssetDatabase.FindAssets("t:EnemyDefinition",new[]{"Assets/ProjectOverburst"})
            .Select(g=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .First(d=>d!=null&&d.ActorPrefab!=null&&d.ActorPrefab.GetComponentInChildren<BloodHitTarget>(true)?.Profile?.suppressBlood==false);
        using(var session=new ComboMakerSession())
        using(var preview=new ComboMakerPreview())
        {
            session.LoadWeapon(weapon,ComboMakerAttackMode.Light);
            MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(session.Source,session.Working);
            preview.TargetDefinition=enemy;preview.TargetPrefab=enemy.ActorPrefab.gameObject;preview.ShowKnockback=false;preview.ShowEffects=false;
            preview.Load(weapon,session.Working);preview.PlaceTargetAtImpact();
            var catalog=Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);
            foreach(var element in Enumerable.Range(0,OverburstElementRules.Count).Select(OverburstElementRules.At))
            {
                preview.Element=element;preview.ShowBlood=false;preview.Seek(.85f);
                var instance=preview.Actor.transform.root.GetComponentInChildren<MeleeElementHitVfxController>();
                Require(instance!=null&&instance.SelectedElement==element,element+" 원소 적중",log);
                var target=(CombatTarget)typeof(ComboMakerPreview).GetField("targetVolume",Private).GetValue(preview);
                CombatTargetVfxPlacement.ResolveContact(target,target.CurrentHurtVolume.Center,Vector3.forward,out float body);
                catalog.TryResolve(element,out var prefab);
                Vector3 expected=prefab.transform.localScale*catalog.ResolveTierScale(body)*catalog.ResolveHitScale(element);
                Require(Vector3.Distance(instance.transform.localScale,expected)<.00001f,element+" 체급·원소 크기 보정",log);
            }
            preview.ShowHits=false;preview.ShowBlood=true;preview.PackBlood=false;preview.Seek(.55f);
            Require(preview.Actor.transform.root.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>().Any(v=>v.name=="피격 혈흔 프리뷰"),"A 혈흔 그래프",log);
            Save(preview.Render(new Rect(0,0,960,640)),"BloodA.png");
            preview.PackBlood=true;preview.Seek(.55f);
            Require(preview.Actor.transform.root.GetComponentsInChildren<Transform>().Any(t=>t.name=="B 혈흔 비산 프리뷰"&&t.GetComponentsInChildren<ParticleSystem>().Length>0),"B 혈흔 파티클",log);
            var decal=preview.Actor.transform.root.GetComponentsInChildren<DecalProjector>().First(d=>d.name=="혈흔 바닥 프리뷰");
            Require(Mathf.Approximately(decal.size.z,.07f),"B 바닥 projection 깊이 보존",log);
            Save(preview.Render(new Rect(0,0,960,640)),"BloodB.png");
            int effects=preview.EffectCount;
            for(int i=0;i<6;i++){preview.Seek(.01f);preview.Seek(.55f);}
            Require(preview.EffectCount==effects,"B 반복 스크럽 효과 수 제한",log);
        }
    }

    private static void CheckAuthoring(WeaponItemData weapon,List<string> log)
    {
        foreach(var mode in new[]{ComboMakerAttackMode.Heavy,ComboMakerAttackMode.ParriedHeavy,ComboMakerAttackMode.DashHeavy})
        {
            var source=ComboMakerAttackBinding.Heavy(weapon.GetMeleeDefinition(),mode);
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/Testers/Weapons/ComboMakerFixture.asset");
            var fixture=Object.Instantiate(source);fixture.hideFlags=HideFlags.None;
            string fixtureGuid=null;
            try
            {
                AssetDatabase.CreateAsset(fixture,path); fixtureGuid=AssetDatabase.AssetPathToGUID(path);
                using(var session=new ComboMakerSession())
                {
                    session.LoadHeavy(weapon.GetMeleeComboDefinition(),fixture);
                    var step=session.Working.steps[0];step.attackName="콤보 메이커 저장 검사";session.Working.steps[0]=step;
                    var vfx=session.HeavyWorking.elementVfx;vfx.fireChainRadius=1.234f;session.HeavyWorking.elementVfx=vfx;
                    session.Apply();
                    Require(!session.Dirty&&fixture.attack.attackName==step.attackName&&Mathf.Approximately(fixture.elementVfx.fireChainRadius,1.234f),mode+" 별도 자산 적용·저장",log);
                    Require(fixture.attack.comboInputWindow.Equals(source.attack.comboInputWindow),mode+" 사용하지 않는 연결 창 보존",log);
                    EditorUtility.SetDirty(fixture);fixture.emptyDamageMultiplier+=.01f;
                    Require(session.SourceChanged,mode+" 외부 수정 충돌 감지",log);
                    bool rejected=false;try{session.Apply();}catch(InvalidOperationException){rejected=true;}
                    Require(rejected,mode+" 충돌 원본 덮어쓰기 거부",log);
                }
                Undo.ClearUndo(fixture);
            }
            finally{if(fixture!=null&&!EditorUtility.IsPersistent(fixture))Object.DestroyImmediate(fixture);RecycleFixture(path,fixtureGuid);}
        }
    }

    private static void CheckNativeUI(WeaponItemData weapon,List<string> log)
    {
        var window=ScriptableObject.CreateInstance<ComboMakerWindow>();
        try
        {
            window.Show();window.CreateGUI();
            typeof(ComboMakerWindow).GetMethod("SelectWeapon",Private).Invoke(window,new object[]{weapon});
            foreach(ComboMakerAttackMode mode in Enum.GetValues(typeof(ComboMakerAttackMode)))
            {
                Require(window.rootVisualElement.Q<Button>("attack-mode-"+mode)?.enabledSelf==true,mode+" 네이티브 선택 버튼",log);
                typeof(ComboMakerWindow).GetMethod("SelectAttackMode",Private).Invoke(window,new object[]{mode});
                var session=(ComboMakerSession)typeof(ComboMakerWindow).GetField("session",Private).GetValue(window);
                Require(session.Asset==ComboMakerAttackBinding.Asset(weapon.GetMeleeDefinition(),mode),mode+" 창 선택 연결",log);
                typeof(ComboMakerWindow).GetField("tab",Private).SetValue(window,1);
                typeof(ComboMakerWindow).GetMethod("BuildEditor",Private).Invoke(window,null);
                if(session.IsHeavy)
                {
                    var reference=window.rootVisualElement.Q<UnityEditor.UIElements.ObjectField>("heavy-field-elementVfx.darkBarrageSlam");
                    Require(reference!=null&&window.rootVisualElement.Q<FloatField>("heavy-field-elementVfx.fireChainRadius")!=null,"최신 강공 VFX 슬롯·반경 입력",log);
                    var field=window.rootVisualElement.Q<FloatField>("heavy-field-elementVfx.fireChainRadius");
                    float value=field.value+.1f;using(var e=ChangeEvent<float>.GetPooled(field.value,value)){e.target=field;field.SendEvent(e);}
                    Require(Mathf.Approximately(session.HeavyWorking.elementVfx.fireChainRadius,value)&&session.Dirty,"실제 필드 이벤트가 작업 사본 갱신",log);
                    window.DiscardChanges();
                }
            }
        }
        finally{window.DiscardChanges();window.Close();}
    }
    private static void Save(Texture texture,string name)
    {
        var rt=texture as RenderTexture;if(rt==null)throw new InvalidOperationException("프리뷰 렌더 누락");
        var before=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();Directory.CreateDirectory(Path.Combine(Output,"captures"));File.WriteAllBytes(Path.Combine(Output,"captures",name),image.EncodeToPNG());}
        finally{RenderTexture.active=before;Object.DestroyImmediate(image);}
    }
    private static void Require(bool passed,string label,List<string> log)
    {if(!passed)throw new InvalidOperationException(label);log.Add("PASS: "+label);}
    private static void Check(bool passed,string label,List<string> log,ref string failure)
    {log.Add((passed?"PASS: ":"FAIL: ")+label);if(!passed)failure=(failure??"")+"\n"+label;}
    private static void RecycleFixture(string path, string guid)
    {
        string current = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
        if (string.IsNullOrEmpty(current)) return;
        if (string.IsNullOrEmpty(guid) || current != guid || !path.StartsWith("Assets/Editor/Testers/Weapons/ComboMakerFixture", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture ownership changed; preserved: " + path);
        if (!AssetDatabase.MoveAssetToTrash(path))
            throw new InvalidOperationException("Could not recycle owned fixture; preserved: " + path);
    }

}
