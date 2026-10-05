using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public abstract class SerializedAudioCatalogEditorProvider<T> : IAudioCatalogEditorProvider, IDisposable
    where T : ScriptableObject
{
    protected T catalog;
    protected SerializedObject serializedCatalog;
    public abstract string DisplayName { get; }
    public abstract string AssetPath { get; }
    public UnityEngine.Object CatalogAsset => catalog;
    public bool SupportsConfirmedDefaults => false;
    public int MissingElementCount => 0;
    public virtual bool HasMissingReference
    {
        get
        {
            if (catalog == null) return true;
            using (var source = new SerializedObject(catalog))
                foreach (ClipSlot slot in GetClipSlots())
                    if (IsMissing(source.FindProperty(slot.Path), slot.Optional)) return true;
            return false;
        }
    }

    protected readonly struct ClipSlot
    {
        public readonly string Path, Label, EmptyMessage;
        public readonly bool Optional;
        public ClipSlot(string path, string label, bool optional = false, string emptyMessage = "선정 보류 · 비어 있으면 소리 없음.")
        { Path = path; Label = label; Optional = optional; EmptyMessage = emptyMessage; }
    }

    protected abstract IEnumerable<ClipSlot> GetClipSlots();
    protected abstract void DrawContents();
    protected virtual void ValidateSettings(List<AudioCatalogValidationIssue> issues) { }
    public abstract int ReadElementCount();

    public void Refresh()
    {
        serializedCatalog?.Dispose();
        catalog = AssetDatabase.LoadAssetAtPath<T>(AssetPath);
        serializedCatalog = catalog != null ? new SerializedObject(catalog) : null;
    }
    public void Dispose() { serializedCatalog?.Dispose(); serializedCatalog = null; }

    public void DrawInspector()
    {
        if (catalog == null || serializedCatalog == null)
        {
            EditorGUILayout.HelpBox("카탈로그 에셋이 없습니다: " + AssetPath, MessageType.Error);
            return;
        }
        serializedCatalog.UpdateIfRequiredOrScript();
        EditorGUILayout.HelpBox("미리듣기는 클립 원음을 재생합니다. 볼륨·피치·공간음 설정은 게임 재생에 사용합니다.", MessageType.Info);
        DrawContents();
        if (serializedCatalog.hasModifiedProperties)
            serializedCatalog.ApplyModifiedProperties();
        foreach (AudioCatalogValidationIssue issue in CollectIssues())
            EditorGUILayout.HelpBox(issue.Message, issue.Type);
    }

    public IReadOnlyList<AudioCatalogValidationIssue> CollectIssues()
    {
        var issues = new List<AudioCatalogValidationIssue>();
        if (catalog == null)
        {
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, "카탈로그 에셋이 누락되었습니다."));
            return issues;
        }
        using (var source = new SerializedObject(catalog))
            foreach (ClipSlot slot in GetClipSlots())
            {
                SerializedProperty property = source.FindProperty(slot.Path);
                if (property == null)
                {
                    issues.Add(new AudioCatalogValidationIssue(MessageType.Error, slot.Label + ": 저장 필드가 없습니다."));
                    continue;
                }
                if (property.isArray)
                {
                    if (property.arraySize == 0 && (!slot.Optional || !string.IsNullOrEmpty(slot.EmptyMessage)))
                        issues.Add(new AudioCatalogValidationIssue(slot.Optional ? MessageType.Info : MessageType.Error,
                            slot.Label + (slot.Optional ? ": " + slot.EmptyMessage : ": 클립 후보가 비어 있습니다.")));
                    var seen = new HashSet<UnityEngine.Object>();
                    for (int i = 0; i < property.arraySize; i++)
                    {
                        UnityEngine.Object clip = property.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (clip == null)
                            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, slot.Label + ": 후보 " + (i + 1) + " 참조가 비어 있습니다."));
                        else if (!seen.Add(clip))
                            issues.Add(new AudioCatalogValidationIssue(MessageType.Warning, slot.Label + ": 같은 후보 클립이 중복됩니다: " + clip.name));
                    }
                }
                else if (property.objectReferenceValue == null)
                    issues.Add(new AudioCatalogValidationIssue(slot.Optional ? MessageType.Info : MessageType.Error,
                        slot.Label + (slot.Optional ? ": 기존 Resources 클립 사용." : ": 클립 참조가 비어 있습니다.")));
            }
        ValidateSettings(issues);
        return issues;
    }

    public int ReadClipCount()
    {
        if (catalog == null) return 0;
        int count = 0;
        using (var source = new SerializedObject(catalog))
            foreach (ClipSlot slot in GetClipSlots())
            {
                SerializedProperty property = source.FindProperty(slot.Path);
                if (property == null) continue;
                if (property.isArray)
                {
                    for (int i = 0; i < property.arraySize; i++)
                        if (property.GetArrayElementAtIndex(i).objectReferenceValue != null) count++;
                }
                else if (property.objectReferenceValue != null) count++;
            }
        return count;
    }

    public void ApplyConfirmedDefaults()
        => throw new InvalidOperationException("이 카탈로그는 확정 기본 매핑 교체를 지원하지 않습니다.");
    public int AddMissingElements() => 0;

    protected void DrawAudioArray(string path, string label)
    {
        SerializedProperty clips = serializedCatalog.FindProperty(path);
        EditorGUILayout.LabelField(label + " · 후보 " + clips.arraySize, EditorStyles.boldLabel);
        for (int i = 0; i < clips.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal();
            DrawClip(clips.GetArrayElementAtIndex(i), new GUIContent("후보 " + (i + 1)));
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                clips.GetArrayElementAtIndex(i).objectReferenceValue = null;
                clips.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("빈 후보 추가", GUILayout.Width(100f)))
        {
            int index = clips.arraySize++;
            clips.GetArrayElementAtIndex(index).objectReferenceValue = null;
        }
    }

    protected static void DrawClip(SerializedProperty property, GUIContent label)
    {
        EditorGUILayout.PropertyField(property, label);
        var clip = property.objectReferenceValue as AudioClip;
        EditorGUI.BeginDisabledGroup(clip == null);
        if (GUILayout.Button(new GUIContent("▶", "미리듣기"), GUILayout.Width(28f)))
            AudioCatalogEditorPreview.Play(clip);
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button(new GUIContent("■", "미리듣기 정지"), GUILayout.Width(28f)))
            AudioCatalogEditorPreview.StopAll();
    }

    protected static bool IsMissing(SerializedProperty property, bool optional)
    {
        if (property == null) return true;
        if (!property.isArray) return !optional && property.objectReferenceValue == null;
        if (property.arraySize == 0) return !optional;
        for (int i = 0; i < property.arraySize; i++)
            if (property.GetArrayElementAtIndex(i).objectReferenceValue == null) return true;
        return false;
    }

    protected static void CheckRange(List<AudioCatalogValidationIssue> issues, string label, float value, float low, float high)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < low || value > high)
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, label + ": 허용 범위는 " + low + "~" + high + "입니다."));
    }

    protected static void CheckDistance(List<AudioCatalogValidationIssue> issues, string label, float low, float high)
    {
        if (float.IsNaN(low) || float.IsNaN(high) || float.IsInfinity(low) || float.IsInfinity(high) || low <= 0f || low >= high)
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, label + ": 최소 거리는 0보다 크고 최대 거리보다 작아야 합니다."));
    }
}

public sealed class CombatActionSfxCatalogEditorProvider : SerializedAudioCatalogEditorProvider<CombatActionSfxCatalog>
{
    public override string DisplayName => "전투 동작 효과음";
    public override string AssetPath => "Assets/ProjectOverburst/Resources/" + CombatActionSfxCatalog.ResourcePath + ".asset";
    public override int ReadElementCount() => catalog?.entries?.Length ?? 0;

    protected override IEnumerable<ClipSlot> GetClipSlots()
    {
        for (int i = 0; i < ReadElementCount(); i++)
            yield return new ClipSlot("entries.Array.data[" + i + "].clip", "전투 동작 " + (catalog.entries[i]?.name ?? (i + 1).ToString()),
                catalog.entries[i] != null && catalog.entries[i].clip == null && ResolveFallback(catalog.entries[i].name) != null);
    }
    protected override void DrawContents()
    {
        EditorGUILayout.HelpBox("동작 키는 게임의 호출 이름과 일치해야 합니다. 카탈로그에 지정하지 않은 이름은 기존 Resources/Combat/SFX/CombatAction 클립을 사용합니다. 볼륨·피치·거리는 동작 실행기가 결정합니다.", MessageType.Info);
        SerializedProperty entries = serializedCatalog.FindProperty("entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("name"), new GUIContent("동작 키"));
            EditorGUILayout.BeginHorizontal();
            DrawClip(entry.FindPropertyRelative("clip"), new GUIContent("클립"));
            if (GUILayout.Button("행×", GUILayout.Width(40f)))
            {
                entries.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();
            if (entry.FindPropertyRelative("clip").objectReferenceValue == null)
            {
                AudioClip fallback = ResolveFallback(entry.FindPropertyRelative("name").stringValue);
                if (fallback != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField("Resources 사용", fallback, typeof(AudioClip), false);
                    if (GUILayout.Button("▶", GUILayout.Width(28f))) AudioCatalogEditorPreview.Play(fallback);
                    if (GUILayout.Button("■", GUILayout.Width(28f))) AudioCatalogEditorPreview.StopAll();
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }
        if (GUILayout.Button("빈 동작 행 추가", GUILayout.Width(120f)))
        {
            int index = entries.arraySize++;
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("name").stringValue = string.Empty;
            entry.FindPropertyRelative("clip").objectReferenceValue = null;
        }
    }
    private static AudioClip ResolveFallback(string name) => string.IsNullOrWhiteSpace(name)
        ? null : Resources.Load<AudioClip>("Combat/SFX/CombatAction/" + name);
    protected override void ValidateSettings(List<AudioCatalogValidationIssue> issues)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ReadElementCount(); i++)
        {
            string name = catalog.entries[i]?.name;
            if (string.IsNullOrWhiteSpace(name))
                issues.Add(new AudioCatalogValidationIssue(MessageType.Error, "전투 동작 " + (i + 1) + ": 동작 키가 비어 있습니다."));
            else if (!names.Add(name))
                issues.Add(new AudioCatalogValidationIssue(MessageType.Error, "전투 동작 키가 중복됩니다: " + name));
        }
    }
}

public sealed class ElementalReactionSfxCatalogEditorProvider : SerializedAudioCatalogEditorProvider<ElementalReactionSfxCatalog>
{
    private static readonly string[] Fields = { "vaporize", "thermalFracture", "freeze", "plasmaExplosion", "shatter", "coldChargeExplosion", "chainTransition" };
    private static readonly string[] Labels = { "증기", "균열", "빙결", "플라즈마 폭발", "쇄빙", "냉전하 폭발", "연쇄감전 전이" };
    public override string DisplayName => "원소 반응 효과음";
    public override string AssetPath => "Assets/ProjectOverburst/Resources/" + ElementalReactionSfxCatalog.ResourcePath + ".asset";
    public override int ReadElementCount() => catalog == null ? 0 : Fields.Length;
    protected override IEnumerable<ClipSlot> GetClipSlots()
    {
        for (int i = 0; i < Fields.Length; i++)
            yield return new ClipSlot(Fields[i] + ".clips", Labels[i], Fields[i] == "thermalFracture");
    }
    protected override void DrawContents()
    {
        for (int i = 0; i < Fields.Length; i++)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool deferred = Fields[i] == "thermalFracture" && serializedCatalog.FindProperty(Fields[i] + ".clips").arraySize == 0;
            DrawAudioArray(Fields[i] + ".clips", Labels[i] + (deferred ? " (선정 보류)" : ""));
            SerializedProperty settings = serializedCatalog.FindProperty(Fields[i]);
            foreach (string field in new[] { "volume", "minPitch", "maxPitch", "spatial", "minDistance", "maxDistance", "cooldown" })
                EditorGUILayout.PropertyField(settings.FindPropertyRelative(field));
            EditorGUILayout.EndVertical();
        }
    }
    protected override void ValidateSettings(List<AudioCatalogValidationIssue> issues)
    {
        using (var source = new SerializedObject(catalog))
            for (int i = 0; i < Fields.Length; i++)
            {
                SerializedProperty settings = source.FindProperty(Fields[i]);
                float Read(string key) => settings.FindPropertyRelative(key).floatValue;
                CheckRange(issues, Labels[i] + " 볼륨", Read("volume"), 0f, 2f);
                CheckRange(issues, Labels[i] + " 최소 피치", Read("minPitch"), .1f, 3f);
                CheckRange(issues, Labels[i] + " 최대 피치", Read("maxPitch"), .1f, 3f);
                if (Read("minPitch") > Read("maxPitch"))
                    issues.Add(new AudioCatalogValidationIssue(MessageType.Error, Labels[i] + ": 최소 피치가 최대 피치보다 큽니다."));
                CheckRange(issues, Labels[i] + " 쿨다운", Read("cooldown"), 0f, float.MaxValue);
                CheckDistance(issues, Labels[i], Read("minDistance"), Read("maxDistance"));
            }
    }
}

public sealed class ItemDropSfxCatalogEditorProvider : SerializedAudioCatalogEditorProvider<ItemDropSfxCatalog>
{
    private static readonly string[] Fields = { "weapon", "armor", "accessory", "potion", "gold", "map", "bag", "legendary", "artifact", "mythic" };
    private static readonly string[] Labels = { "무기", "방어구", "장신구", "물약", "골드", "지도", "가방·기타", "전설 등장", "유물 등장", "신화 등장" };
    public override string DisplayName => "아이템 드롭 효과음";
    public override string AssetPath => "Assets/ProjectOverburst/Resources/" + ItemDropSfxCatalog.ResourcePath + ".asset";
    public override int ReadElementCount() => catalog == null ? 0 : Fields.Length;
    protected override IEnumerable<ClipSlot> GetClipSlots()
    {
        for (int i = 0; i < Fields.Length; i++)
            yield return new ClipSlot(Fields[i], Labels[i], Fields[i] == "gold");
    }
    protected override void DrawContents()
    {
        for (int i = 0; i < Fields.Length; i++)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (i < 7)
            {
                bool deferred = Fields[i] == "gold" && serializedCatalog.FindProperty(Fields[i]).arraySize == 0;
                DrawAudioArray(Fields[i], Labels[i] + (deferred ? " (선정 보류)" : ""));
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                DrawClip(serializedCatalog.FindProperty(Fields[i]), new GUIContent(Labels[i]));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.LabelField("재생 설정", EditorStyles.boldLabel);
        string[] fields = { "dropVolume", "revealVolume", "spatialBlend", "minDistance", "maxDistance", "sameCueCooldown" };
        string[] labels = { "드롭 볼륨", "등장 볼륨", "공간음 비율", "최소 거리", "최대 거리", "동일 종류 쿨다운" };
        for (int i = 0; i < fields.Length; i++)
            EditorGUILayout.PropertyField(serializedCatalog.FindProperty(fields[i]), new GUIContent(labels[i]));
    }
    protected override void ValidateSettings(List<AudioCatalogValidationIssue> issues)
    {
        CheckRange(issues, "드롭 볼륨", catalog.dropVolume, 0f, 1f);
        CheckRange(issues, "등장 볼륨", catalog.revealVolume, 0f, 1f);
        CheckRange(issues, "공간음 비율", catalog.spatialBlend, 0f, 1f);
        CheckRange(issues, "동일 종류 쿨다운", catalog.sameCueCooldown, 0f, float.MaxValue);
        CheckDistance(issues, "아이템 드롭", catalog.minDistance, catalog.maxDistance);
    }
}

public sealed class MeleeElementSfxCatalogEditorProvider : SerializedAudioCatalogEditorProvider<MeleeElementSfxCatalog>
{
    private static readonly WeaponElement[] Required = { WeaponElement.None, WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
    private static readonly string[] CueFields = { "slash", "hit", "heavyImpact", "criticalHit", "followUp" };
    private static readonly string[] CueLabels = { "베기", "적중", "강공", "치명타 (비어 있으면 적중음)", "후속" };
    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
    public override string DisplayName => "근접 원소 효과음";
    public override string AssetPath => "Assets/ProjectOverburst/Resources/" + MeleeElementSfxCatalog.ResourcePath + ".asset";
    public override int ReadElementCount() => catalog?.entries?.Length ?? 0;
    public override bool HasMissingReference
    {
        get
        {
            if (base.HasMissingReference) return true;
            foreach (WeaponElement required in Required)
            {
                bool found = false;
                foreach (MeleeElementSfxEntry entry in catalog.entries)
                    if (entry != null && entry.element == required) { found = true; break; }
                if (!found) return true;
            }
            return false;
        }
    }

    protected override IEnumerable<ClipSlot> GetClipSlots()
    {
        for (int i = 0; i < ReadElementCount(); i++)
            for (int cue = 0; cue < CueFields.Length; cue++)
                yield return new ClipSlot("entries.Array.data[" + i + "]." + CueFields[cue] + ".clips",
                    ElementName(catalog.entries[i]?.element ?? WeaponElement.None) + " " + CueLabels[cue], cue >= 2, "");
        foreach (FieldInfo field in typeof(UpperHeavySfxSettings).GetFields())
            if (field.FieldType == typeof(MeleeElementSfxCueSettings))
                yield return new ClipSlot("upperHeavy." + field.Name + ".clips", "상위 강공 " + UpperName(field.Name), true, "");
    }

    protected override void DrawContents()
    {
        EditorGUILayout.HelpBox("현재 중립·불·얼음·번개·어둠·빛의 전체 큐를 표시합니다. 빈 치명타는 적중음을 사용하고, 빈 강공·후속·단계음은 별도 소리를 재생하지 않습니다. 기존 지정과 보존된 이전 슬롯은 자동 교체하지 않습니다.", MessageType.Info);
        SerializedProperty entries = serializedCatalog.FindProperty("entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(ElementName((WeaponElement)entry.FindPropertyRelative("element").intValue), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("element"), GUIContent.none, GUILayout.Width(120f));
            if (GUILayout.Button("행×", GUILayout.Width(40f)))
            {
                entries.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break;
            }
            EditorGUILayout.EndHorizontal();
            for (int cue = 0; cue < CueFields.Length; cue++)
                DrawCue(entry.propertyPath + "." + CueFields[cue], CueLabels[cue]);
            EditorGUILayout.EndVertical();
        }
        if (GUILayout.Button("빈 원소 행 추가", GUILayout.Width(120f)))
        {
            int index = entries.arraySize++;
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("element").intValue = (int)WeaponElement.None;
            foreach (string cue in CueFields)
            {
                SerializedProperty settings = entry.FindPropertyRelative(cue);
                settings.FindPropertyRelative("clips").ClearArray();
                settings.FindPropertyRelative("volume").floatValue = 1f;
                settings.FindPropertyRelative("minPitch").floatValue = 1f;
                settings.FindPropertyRelative("maxPitch").floatValue = 1f;
                settings.FindPropertyRelative("spatial").boolValue = true;
                settings.FindPropertyRelative("minDistance").floatValue = 2f;
                settings.FindPropertyRelative("maxDistance").floatValue = 40f;
                settings.FindPropertyRelative("cooldown").floatValue = .03f;
                settings.FindPropertyRelative("maxVoices").intValue = 0;
            }
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("빛·어둠 강공 단계 / 보존된 이전 슬롯", EditorStyles.boldLabel);
        foreach (FieldInfo field in typeof(UpperHeavySfxSettings).GetFields())
        {
            string path = "upperHeavy." + field.Name;
            if (field.FieldType == typeof(MeleeElementSfxCueSettings)) DrawCue(path, UpperName(field.Name));
            else if (field.FieldType == typeof(float))
                EditorGUILayout.PropertyField(serializedCatalog.FindProperty(path), new GUIContent(UpperName(field.Name)));
        }
    }

    private void DrawCue(string path, string label)
    {
        SerializedProperty settings = serializedCatalog.FindProperty(path);
        SerializedProperty clips = settings.FindPropertyRelative("clips");
        foldouts.TryGetValue(path, out bool expanded);
        EditorGUILayout.BeginHorizontal();
        expanded = EditorGUILayout.Foldout(expanded, label + " · 후보 " + clips.arraySize, true);
        AudioClip sample = null;
        for (int i = 0; i < clips.arraySize && sample == null; i++) sample = clips.GetArrayElementAtIndex(i).objectReferenceValue as AudioClip;
        if (!expanded && sample != null)
        {
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(sample, typeof(AudioClip), false, GUILayout.MinWidth(160f));
        }
        using (new EditorGUI.DisabledScope(sample == null))
            if (GUILayout.Button("▶", GUILayout.Width(28f))) AudioCatalogEditorPreview.Play(sample);
        if (GUILayout.Button("■", GUILayout.Width(28f))) AudioCatalogEditorPreview.StopAll();
        EditorGUILayout.EndHorizontal();
        foldouts[path] = expanded;
        if (!expanded) return;
        EditorGUI.indentLevel++;
        try
        {
            DrawAudioArray(path + ".clips", label);
            string[] fields = { "volume", "minPitch", "maxPitch", "spatial", "minDistance", "maxDistance", "cooldown", "maxVoices" };
            string[] labels = { "볼륨", "최소 피치", "최대 피치", "공간음", "최소 거리", "최대 거리", "쿨다운", "동시 재생 수 (0: 풀 한도)" };
            for (int i = 0; i < fields.Length; i++) EditorGUILayout.PropertyField(settings.FindPropertyRelative(fields[i]), new GUIContent(labels[i]));
        }
        finally { EditorGUI.indentLevel--; }
    }

    protected override void ValidateSettings(List<AudioCatalogValidationIssue> issues)
    {
        var elements = new HashSet<WeaponElement>();
        for (int i = 0; i < ReadElementCount(); i++)
        {
            MeleeElementSfxEntry entry = catalog.entries[i];
            if (entry == null || !elements.Add(entry.element))
                issues.Add(new AudioCatalogValidationIssue(MessageType.Error, "비어 있거나 중복된 원소 행: " + (i + 1)));
        }
        foreach (WeaponElement element in Required)
            if (!elements.Contains(element)) issues.Add(new AudioCatalogValidationIssue(MessageType.Error, "현행 원소 행이 누락되었습니다: " + ElementName(element)));
        using (var source = new SerializedObject(catalog))
        {
            foreach (ClipSlot slot in GetClipSlots())
            {
                SerializedProperty settings = source.FindProperty(slot.Path.Substring(0, slot.Path.Length - 6));
                if (settings == null) continue;
                float Read(string key) => settings.FindPropertyRelative(key).floatValue;
                CheckRange(issues, slot.Label + " 볼륨", Read("volume"), 0f, 2f);
                CheckRange(issues, slot.Label + " 최소 피치", Read("minPitch"), .1f, 3f);
                CheckRange(issues, slot.Label + " 최대 피치", Read("maxPitch"), .1f, 3f);
                if (Read("minPitch") > Read("maxPitch")) issues.Add(new AudioCatalogValidationIssue(MessageType.Error, slot.Label + ": 피치 범위가 역순입니다."));
                CheckRange(issues, slot.Label + " 쿨다운", Read("cooldown"), 0f, float.MaxValue);
                CheckDistance(issues, slot.Label, Read("minDistance"), Read("maxDistance"));
                if (settings.FindPropertyRelative("maxVoices").intValue < 0) issues.Add(new AudioCatalogValidationIssue(MessageType.Error, slot.Label + ": 동시 재생 수는 음수일 수 없습니다."));
            }
            foreach (FieldInfo field in typeof(UpperHeavySfxSettings).GetFields())
                if (field.FieldType == typeof(float)) CheckRange(issues, UpperName(field.Name), source.FindProperty("upperHeavy." + field.Name).floatValue, 0f, float.MaxValue);
        }
    }

    private static string ElementName(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.None: return "중립";
            case WeaponElement.Fire: return "불";
            case WeaponElement.Ice: return "얼음";
            case WeaponElement.Electric: return "번개";
            case WeaponElement.Dark: return "어둠";
            case WeaponElement.Light: return "빛";
            default: return element.ToString();
        }
    }
    private static string UpperName(string field)
    {
        switch (field)
        {
            case "darkPull": return "어둠 흡인 (이전 슬롯)";
            case "darkForm": return "어둠 생성 (이전 슬롯)";
            case "darkBurst": return "어둠 폭발 (이전 슬롯)";
            case "darkFormDelay": return "어둠 생성 지연 (이전 슬롯)";
            case "darkBurstLayer": return "어둠 폭발 겹침 (이전 슬롯)";
            case "darkBarrageLaunch": return "어둠 탄막 발사";
            case "darkBarrageHit": return "어둠 탄막 명중";
            case "darkBarrageFinisherLaunch": return "어둠 완충 발사 (보존 슬롯)";
            case "darkBarrageFinisherHit": return "어둠 완충 명중 (보존 슬롯)";
            case "lightHit1": return "빛 1타";
            case "lightHit2": return "빛 2타";
            case "lightHit3": return "빛 마지막 타";
            case "lightBuildUp": return "빛 차오름";
            case "lightSparkle": return "빛 반짝임";
            case "lightSparkleDelay": return "빛 반짝임 지연";
            case "lightHit2Layer": return "빛 2타 겹침";
            case "lightHit3Layer": return "빛 마지막 타 겹침";
            case "heavyLowBoom": return "강공 공통 저음";
            default: return ObjectNames.NicifyVariableName(field);
        }
    }
}
