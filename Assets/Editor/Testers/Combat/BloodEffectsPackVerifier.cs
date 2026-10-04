using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BloodEffectsPackVerifier
{
    const string Key = "Overburst.BloodEffectsPackVerifier.";
    static readonly List<string> checks = new List<string>();
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static readonly List<Object> owned = new List<Object>();
    static string output;
    static double deadline;
    static int frame, sequence;
    static BloodHitVfxService blood;
    static BloodGroundDecalService ground;
    static CombatHealth player;
    static Camera camera;
    static RenderTexture texture;
    static Texture2D pixels;
    static bool background;
    static BloodEffectsPackVerifier()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "pending", ""))) EditorApplication.update += AutoStart;
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "return", ""))) EditorApplication.update += Return;
    }
    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks.Add(message);
    }
    public static string AssetsCheck()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("EditMode required");
        checks.Clear();
        var catalog = AssetDatabase.LoadAssetAtPath<BloodEffectsPackCatalog>(BloodEffectsPackBuilder.CatalogPath);
        Check(catalog != null && catalog.sprays.Length == 7,"7 distinct splash and flowing forms loaded");
        foreach (CombatImpactShape shape in Enum.GetValues(typeof(CombatImpactShape)))
            Check(catalog.sprays.Count(s=>s.Accepts(shape,0))>=2,"multiple regular forms for "+shape);
        foreach (var spray in catalog.sprays)
        {
            Check(spray.prefab != null && spray.prefab.GetComponentsInChildren<ParticleSystem>(true).Length > 0,spray.label + " particles loaded");
            Check(spray.prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,spray.label + " owned lifetime and no supplier callbacks");
            Check(spray.prefab.GetComponentsInChildren<ParticleSystem>(true).All(p=>!p.main.loop && !p.main.playOnAwake),spray.label+" bounded emission");
            foreach (var transform in spray.prefab.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0,"no missing scripts " + transform.name);
            foreach (var renderer in spray.prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    Check(material != null && material.shader != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),"valid URP splash shader " + renderer.name);
        }
        Check(catalog.lethalDecals.Length==10 && catalog.trailDecals.Length==2,"10 ground forms including 2 moving trails");
        foreach (var prefab in catalog.lethalDecals)
        {
            var decal = prefab.GetComponent<DecalProjector>();
            Check(decal != null && decal.material != null && !ShaderUtil.ShaderHasError(decal.material.shader),"valid pack ground material " + prefab.name);
            Check(prefab.GetComponent<BloodPackGroundPattern>()!=null && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab)==0,"ground atlas metadata "+prefab.name);
        }
        Check(catalog.screenSprite!=null && catalog.screenMaterials.Length==4,"pack screen sprite and 4 treatments loaded");
        foreach(var material in catalog.screenMaterials)
            Check(material!=null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),"valid screen shader "+material.name);
        var ui = AssetDatabase.LoadAssetAtPath<GameObject>(BloodEffectsPackBuilder.TogglePath).GetComponent<TemporaryBloodComparisonToggle>();
        Check(ui.Button != null && ui.Caption != null && ui.ColorButton != null && ui.ColorCaption != null,"serialized comparison buttons and captions");
        string path = Path.GetFullPath("../개인파일/코덱스산출/CombatVfx/20261004_BloodEffectsPackAB/data");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path,"assets-result.json"),JsonConvert.SerializeObject(new {success=true,checks},Formatting.Indented));
        return "PASS " + checks.Count;
    }
    public static void Start(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required");
        output = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(output);
        SessionState.SetString(Key + "pending",output);
        SessionState.SetString(Key + "return",output);
        SessionState.SetFloat(Key + "returnDeadline",(float)(EditorApplication.timeSinceStartup + 600));
        SessionState.SetString(Key + "startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        File.WriteAllText(Path.Combine(output,"edit-before.json"),JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=> {var s=SceneManager.GetSceneAt(i);return new {s.path,s.isDirty};}),Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update -= AutoStart; EditorApplication.update += AutoStart;
        SessionState.SetFloat(Key + "deadline",(float)(EditorApplication.timeSinceStartup + 240));
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"isolated-save"));
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && work.Count > 0) Finish(new Exception("User or external Play exit"),false);
        if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(Key + "return","")))
        { EditorApplication.update -= Return; EditorApplication.update += Return; }
    }
    static void AutoStart()
    {
        string pending = SessionState.GetString(Key + "pending","");
        if (string.IsNullOrEmpty(pending)) {EditorApplication.update -= AutoStart;return;}
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline",0))
        {
            output=pending; SessionState.EraseString(Key+"pending"); EditorApplication.update -= AutoStart;
            Finish(new Exception("Startup timeout"),EditorApplication.isPlaying); return;
        }
        if (!EditorApplication.isPlaying || PlayerInputFacade.Current == null || Camera.main == null) return;
        output=pending; SessionState.EraseString(Key+"pending"); EditorApplication.update -= AutoStart;
        checks.Clear(); frame=-1; sequence=100; deadline=EditorApplication.timeSinceStartup+180;
        background=Application.runInBackground; Application.runInBackground=true;
        work.Push(Verify()); EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (Time.frameCount == frame && EditorApplication.timeSinceStartup < deadline) return;
        frame=Time.frameCount;
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new Exception("Play timeout or exit");
            while (work.Count>0)
            {
                var item=work.Peek();
                if (!item.MoveNext()) { (work.Pop() as IDisposable)?.Dispose();continue; }
                if (item.Current is IEnumerator nested) {work.Push(nested);continue;}
                return;
            }
            Finish(null,true);
        }
        catch(Exception error) { Finish(error,true); }
    }
    static IEnumerator Frames(int count) {for(int i=0;i<count;i++)yield return null;}
    static IEnumerator Wait(float seconds) {float until=Time.unscaledTime+seconds;while(Time.unscaledTime<until)yield return null;}
    static IEnumerator Verify()
    {
        yield return Wait(2);
        blood=Object.FindFirstObjectByType<BloodHitVfxService>(); ground=Object.FindFirstObjectByType<BloodGroundDecalService>();
        player=PlayerInputFacade.Current.GetComponent<CombatHealth>();
        var toggle=Object.FindFirstObjectByType<TemporaryBloodComparisonToggle>();
        Check(blood != null && ground != null && player != null && toggle != null,"product services, player and comparison HUD bootstrapped");
        float worldDeadline=Time.unscaledTime+60f;
        bool worldReady=false;
        while(Time.unscaledTime<worldDeadline)
        {
            Vector3 origin=player.transform.position+Vector3.forward*2f+Vector3.up*1.6f;
            worldReady=Physics.RaycastAll(origin,Vector3.down,7f,LayerMask.GetMask("Ground","Default"),QueryTriggerInteraction.Ignore)
                .Any(h=>h.normal.y>=.7f && h.collider.GetComponentInParent<CombatHealth>()==null);
            if(worldReady)break;
            yield return null;
        }
        Check(worldReady,"product world ground ready before blood requests");
        yield return Wait(.5f);
        Check(!BloodHitVfxService.PackEnabled && !BloodHitVfxService.UniformRed,"new Play starts at preserved A palette");
        var cameraRoot=new GameObject("Owned Blood Capture Camera");owned.Add(cameraRoot);camera=cameraRoot.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);camera.enabled=false;camera.orthographic=true;camera.orthographicSize=2.1f;
        texture=new RenderTexture(960,640,24,RenderTextureFormat.ARGB32);texture.Create();
        pixels=new Texture2D(960,640,TextureFormat.RGB24,false);camera.targetTexture=texture;
        Vector3 point=player.transform.position+Vector3.forward*2f+Vector3.up;
        var targetRoot=new GameObject("Owned Blood Target");owned.Add(targetRoot);targetRoot.transform.position=point-Vector3.up;
        var health=targetRoot.AddComponent<CombatHealth>();var target=targetRoot.AddComponent<BloodHitTarget>();
        var so=new SerializedObject(target);var profile=Resources.Load<BloodHitProfile>("Combat/Blood/SpiderBrood");
        so.FindProperty("profile").objectReferenceValue=profile;so.ApplyModifiedPropertiesWithoutUndo();
        foreach(bool pack in new[]{false,true}) foreach(bool red in new[]{false,true})
        {
            if (BloodHitVfxService.PackEnabled != pack) Click(toggle.Button);
            if (BloodHitVfxService.UniformRed != red) Click(toggle.ColorButton);
            Check(BloodHitVfxService.PackEnabled == pack && BloodHitVfxService.UniformRed == red,"HUD switches effect and color independently");
            Check(blood.ActiveCount == 0 && ground.ActiveCount == 0,"switch clears mixed old marks and sprays");
            foreach(CombatImpactShape shape in Enum.GetValues(typeof(CombatImpactShape)))
            {
                BloodHitVfxService.SetPackEnabled(pack);
                int before=blood.PlayedCount;
                var hit=new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,target:health,impactShape:shape,impactDirection:Vector3.right);
                BloodHitVfxService.Request(hit,point,1f); BloodHitVfxService.Request(hit,point,1f);
                int peak=0;
                for(int sample=0;sample<6;sample++)
                {
                    yield return null;
                    int particles=pack ? blood.GetComponentsInChildren<ParticleSystem>().Sum(p=>p.particleCount) : blood.GetComponentsInChildren<VisualEffect>().Sum(v=>v.aliveParticleCount);
                    if(particles>0)
                    {
                        peak=Mathf.Max(peak,particles);
                        Capture(point,(pack?"B":"A")+"_"+(red?"Red":"Adjusted")+"_"+shape);
                    }
                }
                Check(blood.PlayedCount == before+1,"one accepted hit and duplicate rejected "+pack+" "+red+" "+shape);
                Check(peak>0,"actual particles emitted "+pack+" "+shape+" peak="+peak);
                if(pack)
                {
                    var renderer=blood.GetComponentsInChildren<ParticleSystemRenderer>(true).First(r=>r.gameObject.activeInHierarchy);
                    var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                    Check(block.GetColor("_BaseColor") == BloodHitVfxService.ResolveColorProfile(profile).mainColor,"new pack profile tint reaches renderer");
                }
                yield return Wait(.22f);
                Check(ground.ActiveCount>0,"ground splatter landed "+pack+" "+shape+" noGround="+ground.SkippedNoGroundCount+" late="+ground.SkippedLateCount);
            }
        }
        yield return Variety(profile,point);
        BloodHitVfxService.SetPackEnabled(true);BloodHitVfxService.SetUniformRed(false);
        int played=blood.PlayedCount;
        so.FindProperty("profile").objectReferenceValue=Resources.Load<BloodHitProfile>("Enemies/Themes/Blood/Bone");so.ApplyModifiedPropertiesWithoutUndo();
        var rejected=new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,target:health,impactDirection:Vector3.right);
        BloodHitVfxService.Request(rejected,point,1f);yield return Frames(4);
        Check(blood.PlayedCount == played && ground.ActiveCount == 0,"bloodless target suppresses new spray and ground");
        so.FindProperty("profile").objectReferenceValue=profile;so.ApplyModifiedPropertiesWithoutUndo();
        var abilities=AssetDatabase.FindAssets("t:EnemyAbilityDefinition").Select(g=>AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(a=>a!=null).ToArray();
        var strong=abilities.FirstOrDefault(a=>a.IsTelegraphedStrongAttack);
        var projectile=abilities.FirstOrDefault(a=>a.ExecutionMode==EnemyAbilityExecutionMode.Projectile);
        Check(strong!=null && projectile!=null,"product strong and projectile attack data loaded");
        var screen=Object.FindFirstObjectByType<PlayerDamageFeedback>();
        Check(screen!=null,"product player damage vignette present");
        uint screenVariations=0;
        foreach(bool pack in new[]{false,true})foreach(bool red in new[]{false,true})foreach(var ability in new[]{null,strong,projectile})
        {
            BloodHitVfxService.SetPackEnabled(pack);BloodHitVfxService.SetUniformRed(red);
            int beforePlayer=blood.PlayedCount;
            yield return Wait(.6f);
            player.TakeDamage(new DamageInfo(1,player.transform.position+Vector3.up,targetRoot,Vector3.right,enemyAbility:ability));
            Check(screen.UsingPackVignette==pack,"actual damage selects matching screen vignette "+pack);
            if(pack)
            {
                screenVariations |= 1u << screen.PackVignetteVariation;
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"B_Screen_"+screen.PackVignetteVariation+".png"));
            }
            for(int sample=0;sample<6;sample++)
            {
                yield return null;
                bool visible=pack ? blood.GetComponentsInChildren<ParticleSystem>().Any(p=>p.particleCount>0) : blood.GetComponentsInChildren<VisualEffect>().Any(v=>v.aliveParticleCount>0);
                if(visible)Capture(player.transform.position+Vector3.up,(pack?"B":"A")+"_Player_"+(red?"Red":"Adjusted")+"_"+(ability==null?"Normal":ability==strong?"Strong":"Projectile"));
            }
            Check(blood.PlayedCount>beforePlayer,"actual player damage event emits blood "+pack+" "+red+" "+(ability==null?"normal":ability.name));
        }
        Check(screenVariations==15,"all 4 pack screen treatments used by real player damage");
        yield return Wait(.6f);
        Check(screen.GetComponent<CanvasGroup>().alpha==0f,"new screen vignette fully fades after damage");
        played=blood.PlayedCount;
        BloodHitVfxService.RequestPlayerHit(player,new DamageInfo(0,point));
        BloodHitVfxService.RequestPlayerHit(player,new DamageInfo(1,point,isDamageOverTime:true));yield return Frames(4);
        Check(blood.PlayedCount==played,"player zero and periodic damage excluded");
        yield return Wounds(health,targetRoot,profile);
        BloodHitVfxService.SetPackEnabled(true);
        int deathBefore=blood.PackPlayedCount;
        BloodHitVfxService.RequestDeath(health,new DamageInfo(1,point,targetRoot,Vector3.right));yield return Frames(4);
        Check(blood.PackPlayedCount>deathBefore,"new pack death burst and fanned jets emitted");
        Capture(point,"B_Death");BloodHitVfxService.SetPackEnabled(true);
        for(int i=0;i<80;i++) BloodHitVfxService.Request(new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,target:health,impactShape:(CombatImpactShape)(i%3),impactDirection:Vector3.right),point,1f);
        yield return Frames(4);
        Check(blood.PeakQueuedCount==64 && blood.DroppedQueueCount>=16 && blood.ActiveCount<=BloodEffectsPackPool.Capacity,"64 queue and fixed new pool capacity under burst");
        int count=blood.GetComponentsInChildren<ParticleSystem>(true).Length;
        yield return Wait(4.3f);Check(blood.ActiveCount==0,"all new spray leases expire");
        Check(blood.GetComponentsInChildren<ParticleSystem>(true).Length==count,"pool particle hierarchy does not grow");
        Click(toggle.Button);Check(!BloodHitVfxService.PackEnabled && ground.ActiveCount==0,"return to A removes all B marks");
        foreach(string name in new[]{"SpiderBrood","VenomBrood","PrimalHunt","CavernMutants","Rake"})
        {
            var theme=Resources.Load<BloodHitProfile>(name=="Rake"?"Enemies/Themes/Blood/Rake":"Combat/Blood/"+name);
            if(theme==null) theme=Resources.Load<BloodHitProfile>("Enemies/Themes/Blood/"+name);
            Check(theme!=null,"theme profile "+name);Color original=theme.mainColor;
            BloodHitVfxService.SetUniformRed(true);var red=BloodHitVfxService.ResolveColorProfile(theme);
            Check(red!=theme && red.mainColor.r>red.mainColor.g*3 && theme.mainColor==original,"red override preserves source "+name);
            BloodHitVfxService.SetUniformRed(false);Check(BloodHitVfxService.ResolveColorProfile(theme)==theme,"adjusted palette restored "+name);
        }
        BloodHitVfxService.SetPackEnabled(true);
        BloodHitVfxService.RequestAt(profile,point,Vector3.right,CombatImpactShape.Sweep,1f,0,targetId:health.GetInstanceID());
        yield return Frames(3);yield return Wait(.2f);
        Check(blood.ActiveCount>0,"active new lease before scene transition");
        var priorScene=SceneManager.GetActiveScene();var temporaryScene=SceneManager.CreateScene("Owned Blood Comparison Transition");
        try
        {
            SceneManager.SetActiveScene(temporaryScene);
            Check(blood.ActiveCount==0 && ground.ActiveCount==0,"scene change clears new pool and ground leases");
        }
        finally {SceneManager.SetActiveScene(priorScene);SceneManager.UnloadSceneAsync(temporaryScene);}
        yield return Frames(2);
    }
    static IEnumerator Variety(BloodHitProfile profile,Vector3 point)
    {
        var catalog=Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.ResourcePath);
        for(int i=0;i<catalog.sprays.Length;i++)
        {
            BloodHitVfxService.SetPackEnabled(true);BloodHitVfxService.SetUniformRed(false);
            var shape=Enum.GetValues(typeof(CombatImpactShape)).Cast<CombatImpactShape>().First(s=>catalog.Accepts(i,s,1));
            int target=1;
            while(catalog.ResolveSpray(shape,1,BloodHitVfxService.CosmeticSeed(0,0,0,target),-1)!=i && target<10000)target++;
            Check(target<10000,"reachable cosmetic selection "+i);
            BloodHitVfxService.RequestAt(profile,point,Vector3.right,shape,1f,1,targetId:target);
            int peak=0;
            for(int f=0;f<6;f++)
            {
                yield return null;
                int particles=blood.GetComponentsInChildren<ParticleSystem>().Sum(p=>p.particleCount);
                if(particles>peak){peak=particles;Capture(point,"B_Form_"+i);}
            }
            Check(blood.LastPackVariation==i && peak>0,"actual particles from distinct pack form "+i+" peak="+peak);
        }
        Check(blood.PackPlayedVariants==127,"all 7 pack sprays used by actual requests");
        foreach(CombatImpactShape shape in Enum.GetValues(typeof(CombatImpactShape)))
        {
            BloodHitVfxService.SetPackEnabled(true);
            int previous=-1;var seen=new HashSet<int>();
            for(int i=0;i<8;i++)
            {
                BloodHitVfxService.RequestAt(profile,point,Vector3.right,shape,1f,0,targetId:3917);
                yield return Frames(2);
                Check(previous!=blood.LastPackVariation,"consecutive same-target form changes "+shape);
                previous=blood.LastPackVariation;seen.Add(previous);
            }
            Check(seen.Count>=2,"same "+shape+" uses multiple visible forms");
        }
        for(int i=0;i<10;i++)
        {
            BloodHitVfxService.SetPackEnabled(true);
            ground.Request(profile,point,Vector3.right,CombatImpactShape.Downward,1f,2,false,0f);
            yield return Frames(2);
            var projector=ground.GetComponentsInChildren<DecalProjector>().FirstOrDefault(p=>p.gameObject.activeInHierarchy);
            Check(projector!=null,"pack ground form lands "+i);
            bool atlas=projector.uvScale.x<1f;
            yield return Wait(.6f);
            if(atlas)Check(Mathf.Abs(projector.uvBias.x-5f/6f)<.001f && Mathf.Abs(projector.uvBias.y)<.001f,"atlas spread holds final frame "+i);
            Capture(point-Vector3.up*.6f,"B_GroundForm_"+i);
        }
        Check(ground.PackShownVariants==1023,"all 10 static, spreading and trail ground forms rendered");
    }
    static IEnumerator Wounds(CombatHealth enemy,GameObject targetRoot,BloodHitProfile profile)
    {
        var trail=Object.FindFirstObjectByType<LowHealthBloodTrailService>();
        Check(trail!=null,"low health movement trail service bootstrapped");
        var affiliation=targetRoot.AddComponent<CombatAffiliation>();affiliation.Configure(CombatTeam.Enemy);
        targetRoot.AddComponent<CombatTarget>();
        var playerHealth=new SerializedObject(player);float originalHp=player.CurrentHp;Vector3 playerPosition=player.transform.position;
        try
        {
            foreach(bool pack in new[]{false,true})
            {
                BloodHitVfxService.SetPackEnabled(pack);
                enemy.ResetHealth();enemy.TakeDamage(new DamageInfo(enemy.MaxHp*.75f,targetRoot.transform.position));
                playerHealth.Update();playerHealth.FindProperty("currentHp").floatValue=player.MaxHp*.25f;playerHealth.ApplyModifiedPropertiesWithoutUndo();
                Check(LowHealthBloodTrailService.ShouldBleed(player) && LowHealthBloodTrailService.ShouldBleed(enemy),"player and monster below 30 percent bleed "+pack);
                yield return Wait(.8f);
                int playerBefore=trail.PlayerDropCount,enemyBefore=trail.MonsterDropCount;
                targetRoot.transform.position+=Vector3.right*.7f;
                player.transform.position+=Vector3.right*.7f;
                Physics.SyncTransforms();yield return Wait(.5f);
                Check(trail.PlayerDropCount>playerBefore && trail.MonsterDropCount>enemyBefore,"moving low-health player and monster leave trails "+pack);
                Check(ground.ActiveCount>0,"low health drops reach actual ground "+pack);
                Capture(player.transform.position+Vector3.up*.3f,(pack?"B":"A")+"_LowHealthTrail");
                int stationaryEnemy=trail.MonsterDropCount;
                yield return Wait(.8f);
                Check(trail.MonsterDropCount==stationaryEnemy,"stationary wound does not flood floor "+pack);
                enemy.Heal(enemy.MaxHp);player.Heal(player.MaxHp);
                Check(!LowHealthBloodTrailService.ShouldBleed(enemy) && !LowHealthBloodTrailService.ShouldBleed(player),"healing above threshold stops bleeding "+pack);
                yield return Wait(.2f);
                int healed=trail.MonsterDropCount;targetRoot.transform.position+=Vector3.right*.7f;yield return Wait(.8f);
                Check(trail.MonsterDropCount==healed,"healed moving monster leaves no new trail "+pack);
            }
            var target=targetRoot.GetComponent<BloodHitTarget>();var properties=new SerializedObject(target);
            properties.FindProperty("profile").objectReferenceValue=Resources.Load<BloodHitProfile>("Enemies/Themes/Blood/Bone");properties.ApplyModifiedPropertiesWithoutUndo();
            enemy.TakeDamage(new DamageInfo(enemy.MaxHp*.75f,targetRoot.transform.position));
            Check(!LowHealthBloodTrailService.ShouldBleed(enemy),"bloodless low-health monster has no trail");
            properties.FindProperty("profile").objectReferenceValue=profile;properties.ApplyModifiedPropertiesWithoutUndo();
            targetRoot.SetActive(false);Check(!LowHealthBloodTrailService.ShouldBleed(enemy),"inactive pooled target stops bleeding");
            targetRoot.SetActive(true);enemy.TakeDamage(new DamageInfo(enemy.MaxHp*2f,targetRoot.transform.position));
            Check(!LowHealthBloodTrailService.ShouldBleed(enemy),"dead target stops ongoing bleeding");
            enemy.ResetHealth();
        }
        finally
        {
            player.transform.position=playerPosition;
            playerHealth.Update();playerHealth.FindProperty("currentHp").floatValue=originalHp;playerHealth.ApplyModifiedPropertiesWithoutUndo();
        }
        BloodHitVfxService.SetPackEnabled(true);
    }
    static void Capture(Vector3 point,string name)
    {
        camera.transform.position=point+new Vector3(3f,2f,-4f);camera.transform.LookAt(point);
        var old=RenderTexture.active;
        try {camera.Render();RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
        finally {RenderTexture.active=old;}
    }
    static void Click(Button button)
    {
        var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
        var pointer=new PointerEventData(EventSystem.current){position=(corners[0]+corners[2])*.5f,button=PointerEventData.InputButton.Left};
        var results=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,results);
        Check(results.Count>0 && results[0].gameObject.GetComponentInParent<Button>()==button,"actual UI raycast reaches "+button.name);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    static void Finish(Exception error,bool exit)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=AutoStart;
        while(work.Count>0)(work.Pop() as IDisposable)?.Dispose();
        if(camera!=null)camera.targetTexture=null;
        if(texture!=null){texture.Release();Object.DestroyImmediate(texture);texture=null;}
        if(pixels!=null){Object.DestroyImmediate(pixels);pixels=null;}
        foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();
        Application.runInBackground=background;
        SessionState.EraseString(Key+"pending");SessionState.EraseFloat(Key+"deadline");
        File.WriteAllText(Path.Combine(output,"play-result.json"),JsonConvert.SerializeObject(new {success=error==null,checks,error=error?.ToString()},Formatting.Indented));
        if(exit && EditorApplication.isPlaying)EditorApplication.ExitPlaymode();
        if(!EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.update-=Return;EditorApplication.update+=Return;}
    }
    static void Return()
    {
        string path=SessionState.GetString(Key+"return","");
        if(string.IsNullOrEmpty(path)){EditorApplication.update-=Return;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"returnDeadline",0))
        {
            EditorApplication.update-=Return;
            File.WriteAllText(Path.Combine(path,"return-result.json"),"{\"success\":false,\"reason\":\"idle return timeout\"}");
            return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        string current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        if(!string.IsNullOrEmpty(current) && !Path.GetFullPath(current).StartsWith(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))return;
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))return;
        string startScene=SessionState.GetString(Key+"startScene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(startScene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
        IsolatedSavePlayGuard.UseRealAccount();
        SessionState.EraseString(Key+"return");SessionState.EraseString(Key+"startScene");SessionState.EraseFloat(Key+"returnDeadline");EditorApplication.update-=Return;
        File.WriteAllText(Path.Combine(path,"return-result.json"),JsonConvert.SerializeObject(new {success=!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),guardActive=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty};})},Formatting.Indented));
    }
}
