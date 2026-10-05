using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Overburst.EditorBalance
{
    public static partial class BalanceTableRuntimeAlignmentVerifier
    {
        static string Output = "../개인파일/코덱스산출/Tools/BalanceTable/20261005_RuntimeAlignment";
        const string Temp = "Assets/Editor/Testers/Balance/__BalanceTableRuntimeAlignment.asset";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static OverburstBalanceTableWindow window;
        static int checks, stage;
        static double started, nextTick;
        static string scenes, guard;
        static int previews, desired, standby;
        static Object selection;
        static UnityEngine.Random.State random;
        static readonly List<(Object source, string json, bool dirty, string path, byte[] bytes, byte[] meta)> sources = new List<(Object,string,bool,string,byte[],byte[])>();
        static readonly List<object> views = new List<object>();
        static EnemyDefinition[] definitions;
        public static bool Running { get; private set; }

        [MenuItem("OVERBURST/테스트/밸런스/현행 데이터와 편집 저장")]
        public static void Run() => Run("../개인파일/코덱스산출/Tools/BalanceTable/20261005_RuntimeAlignment", false);

        public static void Run(string output, bool redesign = false)
        {
            if (Running) throw new InvalidOperationException("밸런스 표 검증이 이미 실행 중입니다.");
            RequireIdle(); Output = output; toolkitUX = redesign;
            if (AssetDatabase.LoadMainAssetAtPath(Temp) != null || File.Exists(Temp))
                throw new InvalidOperationException("검증 임시 경로가 이미 존재합니다. 덮어쓰지 않습니다.");
            Directory.CreateDirectory(Output + "/data"); Directory.CreateDirectory(Output + "/captures");
            sources.Clear(); views.Clear(); checks = stage = 0;
            scenes = SceneState(); guard = GuardState(); selection = Selection.activeObject; random = UnityEngine.Random.state;
            previews = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
            desired = AssetDatabase.DesiredWorkerCount; standby = EditorUserSettings.standbyImportWorkerCount;
            started = EditorApplication.timeSinceStartup; nextTick = started + .2;
            Running = true;
            try
            {
                definitions = Assets<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies");
                string[] types = { "WeaponItemData", "MeleeWeaponDefinition", "GearItemData", "OverburstBalanceTable", "EnemyDefinition", "EnemyCatalog", "EnemyThemeTable", "EnemyAbilitySet", "EnemyAbilityDefinition", "EnemyMovementProfile", "EnemyWeakAttackExecutionProfile" };
                foreach (string path in types.SelectMany(t => AssetDatabase.FindAssets("t:" + t, new[] { "Assets/ProjectOverburst" }))
                    .Select(AssetDatabase.GUIDToAssetPath).Distinct())
                {
                    Object source = AssetDatabase.LoadMainAssetAtPath(path);
                    if (source != null) sources.Add((source, EditorJsonUtility.ToJson(source), EditorUtility.IsDirty(source), path, File.ReadAllBytes(path), File.ReadAllBytes(path + ".meta")));
                }
                VerifyCalculations(); VerifyDocuments();
                window = ScriptableObject.CreateInstance<OverburstBalanceTableWindow>();
                window.position = new Rect(60, 60, 1450, 840); window.ShowUtility();
                EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                Save("RUNNING", null);
            }
            catch (Exception error) { Finish("FAIL", error.ToString()); }
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextTick) return;
            nextTick = EditorApplication.timeSinceStartup + .15;
            try
            {
                try { RequireIdle(); }
                catch (InvalidOperationException) { Finish("DEFERRED_SHARED_USE", "다른 공유 Editor 사용으로 검증을 보류했습니다."); return; }
                if (EditorApplication.timeSinceStartup - started > 120) throw new TimeoutException("밸런스 창 검증 시간 제한");
                if (stage < 12)
                {
                    int index = stage / 2;
                    if (stage % 2 == 0) ChooseView(index);
                    else VerifyView(index);
                    stage++; return;
                }
                if (stage == 12) { ChooseView(5); SelectContactRow(); stage++; return; }
                if (stage == 13) { VerifyContactUi(); Capture(window, Output + "/captures/weak-contact.png"); stage++; return; }
                if (toolkitUX && stage >= 14 && stage < 20) { VerifyToolkitStep(stage - 14); stage++; return; }
                if (stage == (toolkitUX ? 20 : 14)) { VerifySaveConflict(); stage++; return; }
                Finish("PASS", null);
            }
            catch (Exception error) { Finish("FAIL", error.ToString()); }
        }

        static void ChooseView(int index)
        {
            int[] categories = {0,0,1,1,2,2};
            Call(window, "ChooseCategory", categories[index]);
            var sub = Get<DropdownField>(window, "subView");
            if (index % 2 == 1) sub.value = sub.choices[1];
        }
        static IList Rows() => Get<IList>(window, "rows");
        static Object RowSource(object row) => (Object)row.GetType().GetField("Source").GetValue(row);
        static List<BalanceField> Fields(object row) => (List<BalanceField>)row.GetType().GetField("Fields").GetValue(row);
        static void VerifyView(int index)
        {
            IList rows = Rows();
            var rowSources = rows.Cast<object>().Select(RowSource).ToArray();
            int expected = index == 0 ? Assets<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons").Length
                : index == 1 ? 8 : index == 2 ? 10 : index == 3 ? Assets<GearItemData>("Assets/ProjectOverburst/Resources/Items/Gear").Length
                : index == 4 ? definitions.Length : Abilities().Length;
            Check(rows.Count == expected, "View row count " + index);
            if (index == 0)
            {
                var catalog = AssetDatabase.LoadAssetAtPath<WeaponLevelCatalog>("Assets/ProjectOverburst/Resources/" + WeaponLevelCatalog.ResourcePath + ".asset");
                var formal = catalog.entries.Where(e => e.weapon != null && WeaponContentPolicy.IsActiveWeapon(e.weapon)).Select(e => (Object)e.weapon).Distinct();
                Check(new HashSet<Object>(rowSources).SetEquals(formal), "Current weapon catalog agrees with table");
            }
            if (index == 4)
            {
                foreach (var d in definitions) Check(rowSources.Contains(d), "Definition present " + d.EnemyId);
                foreach (var catalog in Assets<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies"))
                    for (int i = 0; i < catalog.Count; i++) Check(rowSources.Contains(catalog.GetDefinition(i)), "Registered definition included");
            }
            if (index == 5)
                foreach (object row in rows)
                {
                    var a = (EnemyAbilityDefinition)RowSource(row); var fields = Fields(row);
                    Check(fields.Count == 9, "Ability columns " + a.AbilityId);
                    if (BalanceTableRuntimeView.UsesBossMaterial(a, definitions))
                        Check(fields.Skip(6).All(f => f == null), "Boss material fields are display only " + a.AbilityId);
                    else
                    {
                        Check((fields[6] == null) == a.HasWeakAttackExecution, "V3 approach ownership " + a.AbilityId);
                        Check((fields[7] == null && fields[8] == null) == (BalanceTableRuntimeView.UsesContactGeometry(a)
                            || BalanceTableRuntimeView.UsesChannelGeometry(a, definitions)), "Contact or channel shape ownership " + a.AbilityId);
                    }
                }
            foreach (object row in rows) foreach (var f in Fields(row).Where(f => f != null)) Check(f.Document.Source != f.Document.Draft, "Working copy isolation");
            Capture(window, Output + "/captures/view-" + index + ".png");
            var list = Get<ListView>(window, "list"); list.SetSelection(0);
            Check(!Get<Label>(window, "preview").text.Contains("계산 오류"), "Native preview renders " + index);
            window.rootVisualElement.Q<ToolbarSearchField>().value = "__no_matching_balance_row__";
            Check(window.VisibleRowCount == 0, "Native search change " + index);
            window.rootVisualElement.Q<ToolbarSearchField>().value = "";
            Check(window.VisibleRowCount == expected, "Search restored " + index);
            views.Add(new { index, rows = expected, rendered = true });
        }
        static void SelectContactRow()
        {
            var contact = Abilities().First(a => BalanceTableRuntimeView.UsesContactGeometry(a) && a.HitCount > 1);
            window.rootVisualElement.Q<ToolbarSearchField>().value = contact.AbilityId;
            Get<ListView>(window, "list").SetSelection(0); window.Repaint();
        }
        static void VerifyContactUi()
        {
            object row = Rows().Cast<object>().First(r => RowSource(r) == RowSource(Get<object>(window,"selected")));
            var a = (EnemyAbilityDefinition)RowSource(row);
            var rendered = window.rootVisualElement.Query<Label>("row-name").ToList().First(l => l.text.Contains(a.AbilityId)).parent;
            Check(rendered.Q("cell-6").Q<FloatField>("number").style.display == DisplayStyle.None, "Actual range input hidden for V3 profile");
            Check(rendered.Q("cell-7").Q<Label>("read-only").text == a.WeakAttackExecution.MaximumContactPlanarReach.ToString("0.###"), "Actual contact reach shown");
            Check(rendered.Q("cell-8").Q<Label>("read-only").text == "접촉 캡슐", "Actual capsule ownership label");
            Check(Get<Label>(window,"preview").text.Contains("V3 약공 원예산") && Get<Label>(window,"preview").text.Contains("실제 Animator"), "Actual V3 budget and animation explanation");
            // ChangeEvent through the displayed FloatField must edit only the copy.
            var field = rendered.Q("cell-5").Q<FloatField>("number");
            float original = a.Cooldown; field.value = original + .1f;
            Check(window.HasDraftChanges && a.Cooldown == original, "Native numeric change leaves source unchanged");
            var docs = Get<Dictionary<Object,BalanceTableDocument>>(window,"documents");
            Check(Mathf.Approximately(((EnemyAbilityDefinition)docs[a].Draft).Cooldown, original + .1f), "Native callback updates draft");
            window.DiscardChanges(); Check(!window.HasDraftChanges, "Discard restores source copy");
        }

        static void VerifyCalculations()
        {
            foreach (var a in Abilities()) foreach (int level in new[]{1,51,100})
            {
                float[] allocation = BalanceTableRuntimeView.DamageAllocations(a,level);
                if (a.HasWeakAttackExecution && (EnemyAbilityDefinition.IsWeakMeleeExecution(a.ExecutionMode)
                    || a.ExecutionMode == EnemyAbilityExecutionMode.Zone))
                {
                    bool valid = a.TryResolveWeakDamageBudget(level,1,out var budget);
                    Check(valid ? allocation.SequenceEqual(Enumerable.Range(0,budget.Count).Select(budget.ForPhase)) : allocation.Length == 0, "Runtime melee budget " + a.AbilityId);
                }
                else if (a.HasWeakAttackExecution && a.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
                {
                    float total = a.UsesLevelDamageBudget ? Mathf.Max(1f,OverburstCombatBalance.RoundStat(OverburstCombatBalance.ReferenceEffectiveHealth(level)*a.ReferencePatternDamagePercent/100f)) : a.Damage;
                    Check(Mathf.Abs(allocation.Sum()-total)<.001f,"Projectile total retained " + a.AbilityId);
                    if(EnemyWeakProjectileDamageBudget.TryCreate(total,a.HitCount,out var budget))
                        Check(allocation.SequenceEqual(Enumerable.Range(0,budget.Count).Select(budget.ForPhase)),"Runtime projectile allocation " + a.AbilityId);
                }
                else Check(allocation.All(v=>v==a.ResolveDamage(level)),"Legacy per-hit damage " + a.AbilityId);
            }
        }
        static void VerifyDocuments()
        {
            var original = Assets<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons")[0];
            Object clone = Object.Instantiate(original); clone.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                using(var d=new BalanceTableDocument(clone))
                {
                    var f=d.Field("baseStats.damage",1,10000); float baseline=f.Value;
                    f.Value=float.NaN; Check(d.Validate().Any(),"NaN rejected");
                    f.Value=10001; Check(d.Validate().Any(),"Out-of-range rejected");
                    f.Value=baseline+1;
                    using(var so=new SerializedObject(clone)){so.FindProperty("baseStats.damage").floatValue=baseline+2;so.ApplyModifiedPropertiesWithoutUndo();}
                    Check(d.HasConflict&&d.Validate().Any(),"External source conflict rejected");
                }
            }
            finally{Object.DestroyImmediate(clone);}
            var weak=Abilities().First(a=>a.HasWeakAttackExecution&&EnemyAbilityDefinition.IsWeakMeleeExecution(a.ExecutionMode));
            clone=Object.Instantiate(weak);clone.hideFlags=HideFlags.HideAndDontSave;
            try{using(var d=new BalanceTableDocument(clone)){d.Field("telegraphedStrongAttack",0,1).Value=1; Check(d.Validate().Any(e=>e.Contains("V3")),"V3 incompatible strong flag rejected");}}
            finally{Object.DestroyImmediate(clone);}
        }
        static void VerifySaveConflict()
        {
            if (window.HasDraftChanges) throw new InvalidOperationException("Unexpected test draft remained");
            var source=ScriptableObject.CreateInstance<OverburstBalanceTable>(); AssetDatabase.CreateAsset(source,Temp);
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
            try
            {
                var d=new BalanceTableDocument(source); Get<Dictionary<Object,BalanceTableDocument>>(window,"documents").Add(source,d);
                d.Field("playerHealthPerLevel",0,1000).Value=19;
                window.ApplyDraft(); Check(source.PlayerHealthPerLevel==19,"Temporary asset real apply");
                window.SaveApplied(); Check(Get<HashSet<Object>>(window,"pendingSave").Count==0,"Only applied assets saved");
                var bytes=File.ReadAllBytes(Temp);
                d.Field("playerHealthPerLevel",0,1000).Value=20;window.ApplyDraft();
                Undo.PerformUndo();Check(source.PlayerHealthPerLevel==19,"Temporary apply undo");
                Undo.PerformRedo();Check(source.PlayerHealthPerLevel==20,"Temporary apply redo");
                Call(window,"Reload");
                using(var so=new SerializedObject(source)){so.FindProperty("playerHealthPerLevel").floatValue=21;so.ApplyModifiedPropertiesWithoutUndo();}
                window.SaveApplied();
                Check(Get<HashSet<Object>>(window,"pendingSave").Contains(source),"Reload preserves pending save conflict");
                Check(File.ReadAllBytes(Temp).SequenceEqual(bytes),"External conflict not saved to disk");
                Check(Get<Label>(window,"status").text.Contains("외부"),"Conflict message shown");
            }
            finally
            {
                window.DiscardChanges();Undo.RevertAllDownToGroup(group);
                if(!AssetDatabase.DeleteAsset(Temp))throw new InvalidOperationException("Test asset cleanup failed");
            }
            Check(!File.Exists(Temp)&&!File.Exists(Temp+".meta"),"Temporary asset and meta returned");
        }

        static void Finish(string status,string error)
        {
            EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=BeforeReload;Running=false;
            try
            {
                if(window!=null){window.DiscardChanges();window.Close();window=null;}
                foreach(var s in sources)
                {
                    Check(s.source!=null&&EditorJsonUtility.ToJson(s.source)==s.json,"Source memory preserved " + s.path);
                    Check(EditorUtility.IsDirty(s.source)==s.dirty,"Source dirty preserved " + s.path);
                    Check(File.ReadAllBytes(s.path).SequenceEqual(s.bytes)&&File.ReadAllBytes(s.path+".meta").SequenceEqual(s.meta),"Disk and GUID preserved " + s.path);
                }
                if(!status.StartsWith("DEFERRED",StringComparison.Ordinal))
                {
                    Check(SceneState()==scenes,"User scenes and dirty preserved");Check(GuardState()==guard,"Guard/start scene preserved");
                    Check(UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount==previews,"Existing preview scenes preserved");
                    Check(Selection.activeObject==selection,"Selection preserved");
                    Check(AssetDatabase.DesiredWorkerCount==desired&&EditorUserSettings.standbyImportWorkerCount==standby,"Worker settings preserved");
                    Check(JsonUtility.ToJson(UnityEngine.Random.state)==JsonUtility.ToJson(random),"Game random state preserved");
                }
            }
            catch(Exception e){status="FAIL";error=(error??"")+"\nRETURN: "+e;}
            Save(status,error);sources.Clear();definitions=null;
            if(status=="PASS")Debug.Log("[OVERBURST] 밸런스 테이블 현행 데이터 "+checks+"검사 PASS.");
            else Debug.LogWarning("[OVERBURST] 밸런스 테이블 검증 "+status+": "+error);
        }
        static void Save(string status,string error)=>File.WriteAllText(Output+"/data/verification.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{utc=DateTime.UtcNow,status,checks,views,sourceCount=sources.Count,error,scene=SceneState(),guard=GuardState(),previews=UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount,integratedPlay="NOT_RUN",player="NOT_RUN",humanFeel="NOT_RUN",pointer="NOT_RUN_NATIVE_CHANGE_CALLBACKS_CHECKED"},Newtonsoft.Json.Formatting.Indented));
        static void BeforeReload()=>Finish("DEFERRED_DOMAIN_RELOAD","검증 중 재로딩으로 소유 콜백을 해제했습니다.");
        static void RequireIdle(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer||EditorUtility.scriptCompilationFailed||IsolatedSavePlayGuard.RequiresAccountChoice||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires","")))throw new InvalidOperationException("Safe shared EditMode return required");}
        static T[] Assets<T>(string folder)where T:Object=>AssetDatabase.FindAssets("t:"+typeof(T).Name,new[]{folder}).Select(g=>AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(x=>x!=null).ToArray();
        static EnemyAbilityDefinition[] Abilities()=>definitions.Where(d=>d.AbilitySet!=null).SelectMany(d=>Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility)).Where(a=>a!=null).Distinct().ToArray();
        static string SceneState()=>Newtonsoft.Json.JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Select(s=>new{s.path,s.isDirty,s.rootCount}));
        static string GuardState()=>Newtonsoft.Json.JsonConvert.SerializeObject(new{environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),active=IsolatedSavePlayGuard.ActiveDirectory,choice=IsolatedSavePlayGuard.RequiresAccountChoice,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),startScene=AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene)});
        static void Check(bool pass,string name){checks++;if(!pass)throw new InvalidOperationException(name);}
        static T Get<T>(object instance,string field)=>(T)instance.GetType().GetField(field,Private).GetValue(instance);
        static object Call(object instance,string method,params object[] args)=>instance.GetType().GetMethod(method,Private).Invoke(instance,args);

        internal static void Capture(EditorWindow target,string path)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
            var parent=typeof(EditorWindow).GetField("m_Parent",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
            MethodInfo Find(string name){for(Type type=parent.GetType();type!=null;type=type.BaseType){var method=type.GetMethods(flags).FirstOrDefault(m=>m.Name==name);if(method!=null)return method;}return null;}
            int width=Mathf.RoundToInt(target.position.width),height=Mathf.RoundToInt(target.position.height);
            var surface=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32);Texture2D pixels=null;var previous=RenderTexture.active;
            try
            {
                surface.Create();Find("RepaintImmediately").Invoke(parent,null);Find("GrabPixels").Invoke(parent,new object[]{surface,new Rect(0,0,width,height)});
                RenderTexture.active=surface;pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);
                var colors=pixels.GetPixels32();for(int y=0;y<height/2;y++)for(int x=0;x<width;x++){int a=y*width+x,b=(height-1-y)*width+x;var color=colors[a];colors[a]=colors[b];colors[b]=color;}
                pixels.SetPixels32(colors);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;if(pixels!=null)Object.DestroyImmediate(pixels);surface.Release();Object.DestroyImmediate(surface);}
        }
    }
}
