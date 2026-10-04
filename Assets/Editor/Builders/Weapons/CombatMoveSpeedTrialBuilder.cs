using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>사용자 체감 비교용 전투 속도와 보폭 배속을 적용하고, 소유 수치만 복원한다.</summary>
[InitializeOnLoad]
public static class CombatMoveSpeedTrialBuilder
{
    public const string Output = "../개인파일/코덱스산출/Combat/20261004_CombatMoveSpeedTrial";
    const string ProfilePath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/GreatswordCombatAnimationProfile.asset";
    const string QueueKey = "Overburst.CombatMoveSpeedTrial.Pending";
    static double idleSince;
    static CombatMoveSpeedTrialBuilder()
    {
        if (SessionState.GetBool(QueueKey,false)) EditorApplication.update += ApplyWhenIdle;
    }
    public static string Queue()
    {
        Directory.CreateDirectory(Path.GetFullPath(Output));
        SessionState.SetBool(QueueKey,true);
        SessionState.SetFloat(QueueKey+".Deadline",(float)EditorApplication.timeSinceStartup+900);
        idleSince=0;
        EditorApplication.update-=ApplyWhenIdle; EditorApplication.update+=ApplyWhenIdle;
        File.WriteAllText(FilePath("QueueResult.json"),"{\"status\":\"WAITING_FOR_IDLE\"}");
        return "QUEUED";
    }
    static void ApplyWhenIdle()
    {
        bool busy=EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"));
        bool expired=EditorApplication.timeSinceStartup>SessionState.GetFloat(QueueKey+".Deadline",0);
        if(busy && !expired) {idleSince=0;return;}
        if(!expired && idleSince==0) {idleSince=EditorApplication.timeSinceStartup;return;}
        if(!expired && EditorApplication.timeSinceStartup-idleSince<.5) return;
        EditorApplication.update-=ApplyWhenIdle; SessionState.EraseBool(QueueKey); SessionState.EraseFloat(QueueKey+".Deadline");
        try {
            if(expired) throw new TimeoutException("Idle 대기 만료. 자산 변경 없음.");
            string result=Apply();
            File.WriteAllText(FilePath("QueueResult.json"),JsonConvert.SerializeObject(new{status="PASS",result}));
        } catch(Exception e) {
            File.WriteAllText(FilePath("QueueResult.json"),JsonConvert.SerializeObject(new{status="FAIL",error=e.ToString()}));
        }
    }
    static readonly float[] Targets = { 6f, 5.75f, 5.5f, 5.25f, 5f, 5.25f, 5.5f, 5.75f };
    sealed class Rate { public long id; public int index, sector; public string clip; public float value; }
    sealed class StateRate { public string name; public float value; public bool parameterActive; }
    sealed class Snapshot
    {
        public string profileGuid, controllerPath, controllerGuid, setPath, setGuid;
        public DirectionalLocomotionSpeedSet8 reference;
        public bool matchMovement, scaleStartStop;
        public List<Rate> loops;
        public List<StateRate> states;
    }
    [MenuItem("OVERBURST/플레이어/전투 이동속도/시험값 적용 (6~5)")]
    public static void ApplyMenu() => Debug.Log(Apply());
    [MenuItem("OVERBURST/플레이어/전투 이동속도/시험 적용 전 값 복원")]
    public static void RestoreMenu() => Debug.Log(Restore());

    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 EditMode가 필요합니다.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY")))
            throw new InvalidOperationException("격리 Play가 준비된 동안에는 수치를 변경하지 않습니다.");
    }
    static (WeaponCombatAnimationProfile profile, CombatLocomotionSet set, AnimatorController controller) Load()
    {
        RequireIdle();
        var p = AssetDatabase.LoadAssetAtPath<WeaponCombatAnimationProfile>(ProfilePath);
        var s = p != null ? p.combatLocomotionSet : null;
        var c = p != null && p.animatorOverrideController != null
            ? p.animatorOverrideController.runtimeAnimatorController as AnimatorController : null;
        if (s == null || s.directions.Length != 8 || c == null) throw new Exception("Sword 8방향 제품 연결이 필요합니다.");
        if (new UnityEngine.Object[] { p, s, c }.Any(EditorUtility.IsDirty))
            throw new Exception("프로필/이동 세트/Animator의 미저장 편집을 보존합니다.");
        if (Mathf.Abs(p.locomotionAnimationSpeedMultiplier - PlayerMovement.GreatswordLocomotionSpeedMultiplier) > .00001f)
            throw new Exception("이동 배율과 애니메이션 배율이 다릅니다.");
        return (p, s, c);
    }
    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        foreach (var s in machine.states) yield return s.state;
        foreach (var child in machine.stateMachines) foreach (var s in States(child.stateMachine)) yield return s;
    }
    static AnimatorState[] MoveStates(AnimatorController c, CombatLocomotionSet set)
    {
        var names = new HashSet<string>(set.directions.SelectMany(m => new[] { m.startState, m.stopState }));
        var states = c.layers.SelectMany(l => States(l.stateMachine)).Where(s => names.Contains(s.name)).ToArray();
        if (states.Length != 16) throw new Exception("Start/Stop 상태 16개가 필요합니다.");
        return states;
    }
    static long Id(UnityEngine.Object o)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long id);
        return id;
    }
    static Vector3 Direction(int i) => Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
    static Snapshot Capture(WeaponCombatAnimationProfile p, CombatLocomotionSet set, AnimatorController c)
    {
        string path = AssetDatabase.GetAssetPath(c), setPath = AssetDatabase.GetAssetPath(set);
        var sectors = set.directions.Select((m,i)=>new {m.loop,i}).ToDictionary(m=>m.loop,m=>m.i);
        var overrides = new List<KeyValuePair<AnimationClip,AnimationClip>>();
        p.animatorOverrideController.GetOverrides(overrides);
        foreach(var pair in overrides)
            if(pair.Value != null && sectors.TryGetValue(pair.Value,out int sector)) sectors[pair.Key]=sector;
        var loops = new List<Rate>();
        foreach (var tree in AssetDatabase.LoadAllAssetsAtPath(path).OfType<BlendTree>())
        {
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
                if (children[i].motion is AnimationClip clip && sectors.TryGetValue(clip,out int sector))
                    loops.Add(new Rate { id = Id(tree), index = i, sector=sector, clip = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)), value = children[i].timeScale });
        }
        if (loops.Count < 8) throw new Exception("방향별 Loop 배속이 누락됐습니다.");
        return new Snapshot {
            profileGuid = AssetDatabase.AssetPathToGUID(ProfilePath), controllerPath = path, controllerGuid = AssetDatabase.AssetPathToGUID(path),
            setPath = setPath, setGuid = AssetDatabase.AssetPathToGUID(setPath), reference = p.locomotionReferenceSpeeds,
            matchMovement = p.matchMovementToLocomotionSpeed, scaleStartStop = set.scaleStartStopWithLocomotionSpeed,
            loops = loops, states = MoveStates(c,set).Select(s => new StateRate { name=s.name, value=s.speed, parameterActive=s.speedParameterActive }).ToList()
        };
    }
    static string SceneState() => JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i =>
    { var s=SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }));
    static string FilePath(string name) => Path.Combine(Path.GetFullPath(Output),name);
    static void Save(WeaponCombatAnimationProfile p, CombatLocomotionSet set, AnimatorController c)
    {
        foreach (var o in new UnityEngine.Object[] {p,set,c}) { EditorUtility.SetDirty(o); AssetDatabase.SaveAssetIfDirty(o); }
    }
    static void RestoreValues(Snapshot before, WeaponCombatAnimationProfile p, CombatLocomotionSet set, AnimatorController c)
    {
        if (AssetDatabase.AssetPathToGUID(ProfilePath) != before.profileGuid
            || AssetDatabase.AssetPathToGUID(before.controllerPath) != before.controllerGuid
            || AssetDatabase.AssetPathToGUID(before.setPath) != before.setGuid) throw new Exception("복원 GUID 불일치.");
        p.locomotionReferenceSpeeds=before.reference; p.matchMovementToLocomotionSpeed=before.matchMovement;
        set.scaleStartStopWithLocomotionSpeed=before.scaleStartStop;
        var trees=AssetDatabase.LoadAllAssetsAtPath(before.controllerPath).OfType<BlendTree>().ToDictionary(Id);
        foreach (var group in before.loops.GroupBy(r=>r.id))
        {
            var tree=trees[group.Key]; var children=tree.children;
            foreach (var r in group)
            {
                if (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(children[r.index].motion)) != r.clip)
                    throw new Exception("복원할 Loop 연결이 바뀌었습니다.");
                children[r.index].timeScale=r.value;
            }
            tree.children=children; EditorUtility.SetDirty(tree);
        }
        var states=MoveStates(c,set).ToDictionary(s=>s.name);
        foreach(var r in before.states) { var s=states[r.name]; s.speed=r.value; s.speedParameterActive=r.parameterActive; EditorUtility.SetDirty(s); }
        Save(p,set,c);
    }
    public static string Apply()
    {
        var (p,set,c)=Load(); Directory.CreateDirectory(Path.GetFullPath(Output));
        var before=Capture(p,set,c); string scenes=SceneState();
        if (File.Exists(FilePath("BeforeValues.json")))
        {
            var saved=JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(FilePath("BeforeValues.json")));
            if (JsonConvert.SerializeObject(saved) != JsonConvert.SerializeObject(before))
                throw new Exception("기존 복원 기준과 다릅니다. 먼저 시험 적용 전 값을 복원하세요.");
        }
        else
        {
            File.WriteAllText(FilePath("BeforeValues.json"),JsonConvert.SerializeObject(before,Formatting.Indented));
            foreach(var path in new[]{ProfilePath,before.setPath,before.controllerPath})
            {
                string dest=FilePath("Before/"+path); Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(path,dest,false); File.Copy(path+".meta",dest+".meta",false);
            }
        }
        try
        {
            float multiplier=PlayerMovement.GreatswordLocomotionSpeedMultiplier;
            p.locomotionReferenceSpeeds=new DirectionalLocomotionSpeedSet8 {
                forward=Targets[0]/multiplier, forwardRight=Targets[1]/multiplier, right=Targets[2]/multiplier,
                backwardRight=Targets[3]/multiplier, backward=Targets[4]/multiplier, backwardLeft=Targets[5]/multiplier,
                left=Targets[6]/multiplier, forwardLeft=Targets[7]/multiplier };
            p.matchMovementToLocomotionSpeed=true; set.scaleStartStopWithLocomotionSpeed=true;
            var trees=AssetDatabase.LoadAllAssetsAtPath(before.controllerPath).OfType<BlendTree>().ToDictionary(Id);
            foreach(var group in before.loops.GroupBy(r=>r.id))
            {
                var tree=trees[group.Key]; var children=tree.children;
                foreach(var r in group)
                {
                    int sector=r.sector;
                    children[r.index].timeScale=p.locomotionReferenceSpeeds.GetSpeed(Direction(sector))/set.directions[sector].authoredSpeed;
                }
                tree.children=children; EditorUtility.SetDirty(tree);
            }
            var states=MoveStates(c,set).ToDictionary(s=>s.name);
            for(int i=0;i<8;i++) foreach(var name in new[]{set.directions[i].startState,set.directions[i].stopState})
            { var s=states[name]; s.speed=Targets[i]/set.directions[i].authoredSpeed; s.speedParameterActive=false; EditorUtility.SetDirty(s); }
            Save(p,set,c);
            string result=Check();
            if(SceneState()!=scenes) throw new Exception("공유 씬 상태가 바뀌었습니다.");
            File.WriteAllText(FilePath("AppliedValues.json"),JsonConvert.SerializeObject(Capture(p,set,c),Formatting.Indented));
            File.WriteAllText(FilePath("ApplyResult.json"),JsonConvert.SerializeObject(new {status="PASS",targets=Targets,result,scenes},Formatting.Indented));
            return result;
        }
        catch { RestoreValues(before,p,set,c); throw; }
    }
    public static string Restore()
    {
        var (p,set,c)=Load(); string scenes=SceneState();
        var applied=JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(FilePath("AppliedValues.json")));
        if(JsonConvert.SerializeObject(Capture(p,set,c))!=JsonConvert.SerializeObject(applied))
            throw new Exception("시험 수치에 후속 변경이 있습니다. 해당 변경을 확인한 뒤 복원하세요.");
        var before=JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(FilePath("BeforeValues.json")));
        RestoreValues(before,p,set,c);
        if(JsonConvert.SerializeObject(Capture(p,set,c))!=JsonConvert.SerializeObject(before) || SceneState()!=scenes)
            throw new Exception("복원 readback 불일치.");
        File.WriteAllText(FilePath("RestoreResult.json"),"{\"status\":\"PASS\"}");
        return "PASS: 전투 속도와 Start/Loop/Stop 배속을 시험 적용 전 값으로 복원했습니다.";
    }
    public static string Check()
    {
        var (p,set,c)=Load(); var snapshot=Capture(p,set,c);
        var trees=AssetDatabase.LoadAllAssetsAtPath(snapshot.controllerPath).OfType<BlendTree>().ToDictionary(Id);
        var states=MoveStates(c,set).ToDictionary(s=>s.name);
        for(int i=0;i<8;i++)
        {
            if(Mathf.Abs(p.locomotionReferenceSpeeds.GetSpeed(Direction(i))*PlayerMovement.GreatswordLocomotionSpeedMultiplier-Targets[i])>.0001f)
                throw new Exception("이동 목표속도 불일치: "+i);
            foreach(var name in new[]{set.directions[i].startState,set.directions[i].stopState})
            {
                var s=states[name];
                if(Mathf.Abs(s.speed*set.directions[i].authoredSpeed-Targets[i])>.0001f || s.speedParameterActive || s.iKOnFeet)
                    throw new Exception("Start/Stop 배속/IK 불일치: "+name);
            }
        }
        foreach(var r in snapshot.loops)
        {
            var child=trees[r.id].children[r.index]; int i=r.sector;
            if(Mathf.Abs(child.timeScale*p.locomotionAnimationSpeedMultiplier*set.directions[i].authoredSpeed-Targets[i])>.0001f)
                throw new Exception("Loop 보폭 배속 불일치: "+i);
        }
        if(!p.matchMovementToLocomotionSpeed || !set.scaleStartStopWithLocomotionSpeed) throw new Exception("속도 연결 옵션 누락.");
        return "PASS: 8방향 최종 속도 6/5.75/5.5/5.25/5와 Start/Loop/Stop 보폭 배속, 발 IK 0.";
    }
}
