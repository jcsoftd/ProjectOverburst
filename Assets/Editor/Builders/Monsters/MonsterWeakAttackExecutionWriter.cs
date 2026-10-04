using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Called by the V3 actor/attack builder after contact, motion and controller authoring.
// This writer never reads the sound board or modifies SFX resources.
public static class MonsterWeakAttackExecutionWriter
{
    public const string Root = "Assets/ProjectOverburst/Resources/Enemies/Themes/ExecutionProfiles/V3";

    public static bool Apply(string approvedV3Path, string approvedV3Sha256, string cardKey, string selectionKey, JObject authored,
        EnemyAbilityDefinition ability, AnimationClip original, AnimationClip motion)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("약공 프로필 적용은 유휴 EditMode에서만 가능합니다.");
        Validate(approvedV3Path, approvedV3Sha256, cardKey, selectionKey, authored, ability, original, motion);
        var candidate = ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>();
        EnemyWeakAttackExecutionProfile existing = null;
        string path = Root + "/" + ability.AbilityId + ".asset";
        bool created = false;
        bool abilityTouched = false, profileTouched = false;
        var createdFolders = new System.Collections.Generic.List<string>();
        string previousAbility = EditorJsonUtility.ToJson(ability);
        string previousProfile = null;
        try
        {
            existing = AssetDatabase.LoadAssetAtPath<EnemyWeakAttackExecutionProfile>(path);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null && existing == null)
                throw new InvalidOperationException("프로필 경로에 다른 자산 형식이 있습니다: " + path);
            candidate.name = existing != null ? existing.name : ability.AbilityId;
            ConfigureCandidate(candidate,selectionKey,authored,original,motion);
            string wanted = EditorJsonUtility.ToJson(candidate);
            if (existing != null && ability.WeakAttackExecution == existing && EditorJsonUtility.ToJson(existing) == wanted)
                return false;
            if (existing != null)
            {
                if (existing.SelectionKey != selectionKey)
                    throw new InvalidOperationException("기존 프로필의 선택 키와 적용 대상이 다릅니다.");
                previousProfile = EditorJsonUtility.ToJson(existing);
                profileTouched = true;
                EditorJsonUtility.FromJsonOverwrite(wanted, existing);
            }
            else
            {
                EnsureFolder(Root,createdFolders);
                existing = UnityEngine.Object.Instantiate(candidate);
                existing.name = candidate.name;
                AssetDatabase.CreateAsset(existing,path); created = true;
            }
            abilityTouched = true;
            ability.ConfigureWeakAttackExecution(existing);
            EditorUtility.SetDirty(existing); EditorUtility.SetDirty(ability);
            AssetDatabase.SaveAssetIfDirty(existing); AssetDatabase.SaveAssetIfDirty(ability);
            return true;
        }
        catch
        {
            if (abilityTouched)
            {
                EditorJsonUtility.FromJsonOverwrite(previousAbility,ability);
                EditorUtility.SetDirty(ability); AssetDatabase.SaveAssetIfDirty(ability);
            }
            if (created) AssetDatabase.DeleteAsset(path);
            else if (existing != null && previousProfile != null && profileTouched)
            {
                EditorJsonUtility.FromJsonOverwrite(previousProfile,existing);
                EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
            }
            for(int i=createdFolders.Count-1;i>=0;i--)
                if(Directory.Exists(createdFolders[i]) && Directory.GetFileSystemEntries(createdFolders[i]).Length==0)
                    AssetDatabase.DeleteAsset(createdFolders[i]);
            throw;
        }
        finally { UnityEngine.Object.DestroyImmediate(candidate); }
    }

    public static void Validate(string approvedV3Path, string approvedV3Sha256, string cardKey, string selectionKey, JObject authored,
        EnemyAbilityDefinition ability, AnimationClip original, AnimationClip motion)
    {
        if(string.IsNullOrEmpty(approvedV3Sha256) || Hash(approvedV3Path)!=approvedV3Sha256)
            throw new InvalidOperationException("승인 V3 스냅샷 해시와 다릅니다.");
        var approved = JObject.Parse(File.ReadAllText(approvedV3Path));
        if ((int?)approved["version"] != 3) throw new ArgumentException("승인 V3 입력이 아닙니다.");
        foreach (JObject input in approved["inputs"])
            if (Hash((string)input["path"]) != (string)input["sha256"])
                throw new InvalidOperationException("승인 이후 선택 입력이 변경되었습니다: " + input["path"]);
        var card = approved["cards"]?[cardKey] as JObject;
        if (card == null || (bool?)card["inRoster"] != true || (bool?)card["isBoss"] == true
            || (string)card["status"] != "confirmed") throw new ArgumentException("확정 일반 편성 대상이 아닙니다.");
        var selected = card["weak"]?.OfType<JObject>().SingleOrDefault(m => (string)m["key"] == selectionKey);
        if (selected == null || original == null || motion == null || ability == null || !ability.IsValid
            || ability.IsTelegraphedStrongAttack) throw new ArgumentException("선택 약공·클립·능력이 유효하지 않습니다.");
        string abilityPath = AssetDatabase.GetAssetPath(ability);
        if (!abilityPath.StartsWith("Assets/ProjectOverburst/",StringComparison.Ordinal)
            || ability.AbilityId.Any(c => !(char.IsLetterOrDigit(c) || c=='_' || c=='-')))
            throw new ArgumentException("프로젝트 소유 능력 경로/ID가 아닙니다.");
        if (authored == null || (bool?)authored["nativeAuthoringComplete"] != true
            || (string)authored["cardKey"] != cardKey || (string)authored["selectionKey"] != selectionKey)
            throw new ArgumentException("실제 타격·거리·이동 저작이 완료되지 않았습니다.");
        if (original.name != (string)selected["clip"] || AssetDatabase.GetAssetPath(original) != (string)selected["sourcePath"]
            || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original,out string guid,out long localId)
            || guid != (string)authored["sourceGuid"] || localId != (long?)authored["sourceLocalId"])
            throw new ArgumentException("선택 원본의 native GUID/local ID와 다릅니다.");
        if (Hash(ProjectFile(AssetDatabase.GetAssetPath(original))) != RequiredString(authored,"sourceSha256"))
            throw new InvalidOperationException("저작 이후 원본 클립 파일이 변경되었습니다.");
        if (AssetDatabase.GetAssetPath(motion) != AssetDatabase.GetAssetPath(original)
            && !AssetDatabase.GetAssetPath(motion).StartsWith("Assets/ProjectOverburst/",StringComparison.Ordinal))
            throw new ArgumentException("선택 원본 또는 프로젝트 소유 파생 클립만 사용할 수 있습니다.");
        if (motion != original && AssetDatabase.GetAssetPath(motion) == AssetDatabase.GetAssetPath(original))
            throw new ArgumentException("같은 원본 파일의 다른 클립을 실행 모션으로 바꿀 수 없습니다.");
        if ((string)authored["runtimeClipPath"] != AssetDatabase.GetAssetPath(motion))
            throw new ArgumentException("실행 클립의 저작 경로가 다릅니다.");
        if (Mathf.Abs(original.frameRate-RequiredFloat(authored,"nativeFps")) > .001f
            || Mathf.Abs(ability.AttackAnimationDuration-motion.length) > 1f/original.frameRate+.001f)
            throw new ArgumentException("원본 FPS 또는 실행 길이가 맞지 않습니다.");
        var times = authored["hitNormalizedTimes"] as JArray;
        if (times == null || times.Count != ability.HitCount) throw new ArgumentException("실제 타격 사건 수가 맞지 않습니다.");
        float previous = 0f;
        for (int i=0;i<times.Count;i++)
        {
            float time = (float)times[i];
            if (float.IsNaN(time) || float.IsInfinity(time) || time <= previous || time > .95f
                || Mathf.Abs(time-ability.GetHitNormalizedTime(i)) > .0001f)
                throw new ArgumentException("실제 타격 시점과 능력 사건이 맞지 않습니다.");
            previous=time;
        }
        bool melee = EnemyAbilityDefinition.IsWeakMeleeExecution(ability.ExecutionMode);
        if (melee)
        {
            var windows = ReadContactWindows(authored);
            if (windows == null || windows.Length != times.Count) throw new ArgumentException("타격별 원본 접촉 구간이 없습니다.");
            var trim = RequiredPair(authored, "sourceTrimSeconds");
            for (int i = 0; i < windows.Length; i++)
            {
                float startFrame = (trim.x + windows[i].x * motion.length) * original.frameRate;
                float endFrame = (trim.x + windows[i].y * motion.length) * original.frameRate;
                if ((float)times[i] < windows[i].x || (float)times[i] > windows[i].y
                    || Mathf.Abs(startFrame - Mathf.Round(startFrame)) > .001f
                    || Mathf.Abs(endFrame - Mathf.Round(endFrame)) > .001f)
                    throw new ArgumentException("타격 시점 또는 원본 프레임과 접촉 구간이 맞지 않습니다.");
            }
        }
        if (melee && (ability.HitCount > 3 || ability.HitCount != (int?)selected["selectedCount"]))
            throw new ArgumentException("선택 근접 약공 타수/최대3타가 맞지 않습니다.");
        string policy = RequiredString(authored,"motionPolicy");
        if (policy != (string)selected["motionPolicy"]) throw new ArgumentException("승인한 시각적 실행 분류와 다릅니다.");
        var candidate=ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>();
        try { ConfigureCandidate(candidate,selectionKey,authored,original,motion); }
        finally { UnityEngine.Object.DestroyImmediate(candidate); }

    }

    private static void ConfigureCandidate(EnemyWeakAttackExecutionProfile candidate,string selectionKey,JObject authored,
        AnimationClip original,AnimationClip motion)
    {
            var policy = (EnemyWeakAttackMotionPolicy)Enum.Parse(typeof(EnemyWeakAttackMotionPolicy), RequiredString(authored,"motionPolicy"));
            float reach = RequiredFloat(authored,"stationaryStartRange");
            float advance = RequiredFloat(authored,"maxAdvanceDistance");
            var window = RequiredPair(authored,"advanceWindow");
            var trim = RequiredPair(authored,"sourceTrimSeconds");
            AnimationCurve curve = null;
            if (policy == EnemyWeakAttackMotionPolicy.ShortAdvance)
            {
                var keys = (authored["advanceCurve"] as JArray)?.OfType<JObject>().Select(k =>
                    new Keyframe(RequiredFloat(k,"time"),RequiredFloat(k,"value"),RequiredFloat(k,"inTangent"),RequiredFloat(k,"outTangent"))).ToArray();
                if (keys == null) throw new ArgumentException("실제 전진 곡선이 없습니다.");
                curve = new AnimationCurve(keys);
            }
            candidate.Configure(selectionKey, original, motion, trim, policy, reach, advance, window, curve,
                (string)authored["poseRootBonePath"], ReadContactWindows(authored));
    }

    private static Vector2[] ReadContactWindows(JObject authored)
    {
        var data = authored["contactWindowsNormalized"];
        if (data == null || data.Type == JTokenType.Null) return null;
        if (!(data is JArray windows)) throw new ArgumentException("접촉 구간은 타격별 배열이어야 합니다.");
        return windows.Select(w =>
        {
            if (!(w is JArray pair) || pair.Count != 2 || pair.Any(t => t.Type == JTokenType.Null))
                throw new ArgumentException("접촉 구간 시작·종료가 없습니다.");
            return new Vector2((float)pair[0], (float)pair[1]);
        }).ToArray();
    }

    private static string ProjectFile(string assetPath)
        =>Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName,assetPath));

    private static string RequiredString(JObject data,string key)
    {
        string value=(string)data[key];
        if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("저작 값 누락: "+key);
        return value;
    }
    private static float RequiredFloat(JObject data,string key)
    {
        if(data[key]==null || data[key].Type==JTokenType.Null)throw new ArgumentException("저작 값 누락: "+key);
        float value=(float)data[key];
        if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("유한한 저작 값 필요: "+key);
        return value;
    }
    private static Vector2 RequiredPair(JObject data,string key)
    {
        if (!(data[key] is JArray pair) || pair.Count != 2 || pair.Any(v=>v.Type==JTokenType.Null))
            throw new ArgumentException("저작 구간 누락: "+key);
        return new Vector2((float)pair[0],(float)pair[1]);
    }
    private static string Hash(string path)
    { using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
    private static void EnsureFolder(string path,System.Collections.Generic.List<string> created)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');
        EnsureFolder(parent,created);
        string guid=AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        if(string.IsNullOrEmpty(guid))throw new IOException("프로필 폴더 생성 실패: "+path);
        created.Add(path);
    }
}
