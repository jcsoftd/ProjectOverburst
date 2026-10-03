using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// 승인된 2026-10-04 빛 계수만 변경한다. 씬과 다른 자산은 저장하지 않는다.
public static class PlayerElementBalanceBuilder
{
    public const string AssetPath = "Assets/ProjectOverburst/Resources/Combat/OverburstElementTuning.asset";
    public static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../개인파일/코덱스산출/Balance/20261004_PlayerElementBalanceApply"));
    static readonly string[] Fields = { "lightTripleHit1PerStack", "lightTripleHit2Base", "lightTripleHit2PerOvercharge", "lightTripleHit3Scale" };
    static readonly float[] Before = { 2.6f, .5f, 1f, 1f }, After = { 1.6f, .65f, .05f, .8f };

    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 EditMode에서만 실행할 수 있습니다.");
        if (SessionState.GetFloat("OverburstBalanceMeasure.pending", 0) > 0 || SessionState.GetBool("OverburstBalanceMeasure", false)
            || SessionState.GetBool("UpperElementPlayVerifier", false))
            throw new InvalidOperationException("다른 전투 검증이 예약되거나 실행 중입니다.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("다른 격리 Play 또는 계정 반환이 남아 있습니다.");
    }

    [MenuItem("OVERBURST/Balance/승인된 약공·빛 배분 자산 적용")]
    public static void ApplyFromMenu() => Debug.Log(Apply());

    public static string Apply()
    {
        RequireIdle();
        var asset = AssetDatabase.LoadAssetAtPath<OverburstElementTuning>(AssetPath);
        if (asset == null || asset != OverburstElementTuning.Current) throw new InvalidOperationException("현재 연결된 원소 튜닝 자산이 다릅니다.");
        string guid = AssetDatabase.AssetPathToGUID(AssetPath);
        var serialized = new SerializedObject(asset);
        var before = JObject.Parse(EditorJsonUtility.ToJson(asset));
        for (int i = 0; i < Fields.Length; i++)
        {
            var p = serialized.FindProperty(Fields[i]);
            if (p == null || (!Mathf.Approximately(p.floatValue, Before[i]) && !Mathf.Approximately(p.floatValue, After[i])))
                throw new InvalidOperationException("알 수 없는 기존 계수: " + Fields[i]);
        }
        Undo.RecordObject(asset, "빛 첫 타격 배분 조정");
        for (int i = 0; i < Fields.Length; i++) serialized.FindProperty(Fields[i]).floatValue = After[i];
        serialized.ApplyModifiedProperties();
        var after = JObject.Parse(EditorJsonUtility.ToJson(asset));
        var beforeOther = (JObject)(before["MonoBehaviour"] ?? before).DeepClone();
        var afterOther = (JObject)(after["MonoBehaviour"] ?? after).DeepClone();
        foreach (string field in Fields) { beforeOther.Remove(field); afterOther.Remove(field); }
        if (!JToken.DeepEquals(beforeOther, afterOther))
        {
            EditorJsonUtility.FromJsonOverwrite(before.ToString(), asset);
            throw new InvalidOperationException("승인 범위 밖 필드가 변경되어 자산을 복원했습니다.");
        }
        AssetDatabase.SaveAssetIfDirty(asset);
        if (AssetDatabase.AssetPathToGUID(AssetPath) != guid) throw new InvalidOperationException("GUID가 변경되었습니다.");
        Directory.CreateDirectory(OutputRoot);
        string file = Path.Combine(OutputRoot, "native-asset-apply.json");
        File.WriteAllText(file, JsonConvert.SerializeObject(new { status = "PASS", guid, asset = AssetPath, before, after, otherFieldsUnchanged = true }, Formatting.Indented));
        return file;
    }
}
