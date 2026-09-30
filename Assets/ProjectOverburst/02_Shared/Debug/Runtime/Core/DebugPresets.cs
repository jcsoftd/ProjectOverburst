#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 설정 묶음. 토글·선택·숫자 값의 조합을 이름 붙여 저장하고 한 번에 적용한다. 버튼은 저장하지 않는다.
    /// 기본 묶음은 모듈이 <see cref="RegisterBuiltIn"/>으로 등록하고, 사용자 묶음은 PlayerPrefs에 저장한다.
    /// </summary>
    public static class DebugPresets
    {
        [Serializable]
        private sealed class Preset
        {
            public string name;
            public List<string> ids = new List<string>();
            public List<string> values = new List<string>();
        }

        [Serializable]
        private sealed class PresetFile
        {
            public List<Preset> presets = new List<Preset>();
        }

        private static readonly List<Preset> builtIns = new List<Preset>();
        private static readonly List<string> names = new List<string>();
        private static List<Preset> user;
        private static Dictionary<string, string> undoSnapshot;
        private static string selected;
        private static string pendingName = string.Empty;
        private static bool namesDirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            builtIns.Clear();
            names.Clear();
            user = null;
            undoSnapshot = null;
            selected = null;
            pendingName = string.Empty;
            namesDirty = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterSection()
        {
            DebugSection section = DebugRegistry.Section(DebugTabs.Favorites, "설정 묶음", 100, "여러 값을 한 번에");
            section.Choice("묶음", () => Selected, value => selected = value, value => value, () => Names)
                .WithId("presets.selected")
                .NoPreset()
                .Tip("적용할 묶음을 고른다. 기본 묶음은 모듈이 등록하고 지울 수 없다.");
            section.Buttons("실행")
                .Add("적용", () => Apply(Selected))
                .Add("원래대로", Undo)
                .Add("삭제", () => Delete(Selected))
                .WithId("presets.actions")
                .Tip("원래대로는 마지막으로 묶음을 적용하기 직전 값으로 되돌린다.");
            section.Text("새 묶음 이름", () => pendingName, value => pendingName = value)
                .WithId("presets.newName");
            section.Button("현재 조합을 새 묶음으로 저장", () => SaveCurrent(pendingName))
                .WithId("presets.save");
        }

        public static IReadOnlyList<string> Names
        {
            get
            {
                if (!namesDirty)
                    return names;
                names.Clear();
                for (int i = 0; i < builtIns.Count; i++)
                    names.Add(builtIns[i].name);
                List<Preset> saved = User;
                for (int i = 0; i < saved.Count; i++)
                {
                    if (!names.Contains(saved[i].name))
                        names.Add(saved[i].name);
                }
                namesDirty = false;
                return names;
            }
        }

        public static string Selected
        {
            get
            {
                IReadOnlyList<string> list = Names;
                if (selected != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] == selected)
                            return selected;
                    }
                }
                return list.Count > 0 ? list[0] : null;
            }
        }

        /// <summary>기본 묶음 등록. 같은 이름이면 교체한다.</summary>
        public static void RegisterBuiltIn(string name, params (string id, string value)[] values)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            builtIns.RemoveAll(preset => preset.name == name);
            var created = new Preset { name = name };
            foreach ((string id, string value) in values)
            {
                created.ids.Add(id);
                created.values.Add(value);
            }
            builtIns.Add(created);
            namesDirty = true;
        }

        public static DebugResult Apply(string name)
        {
            Preset preset = Find(name);
            if (preset == null)
                return DebugResult.Fail("묶음을 찾지 못했어요");

            Dictionary<string, string> snapshot = CaptureAll();
            int applied = 0;
            int skipped = 0;
            for (int i = 0; i < preset.ids.Count; i++)
            {
                if (ApplyOne(preset.ids[i], preset.values[i]))
                    applied++;
                else
                    skipped++;
            }
            undoSnapshot = snapshot;
            return DebugResult.Ok(skipped == 0 ? $"{applied}개 적용" : $"{applied}개 적용 · {skipped}개 건너뜀");
        }

        public static DebugResult Undo()
        {
            if (undoSnapshot == null)
                return DebugResult.Fail("되돌릴 적용 기록이 없어요");
            int restored = 0;
            foreach (KeyValuePair<string, string> pair in undoSnapshot)
            {
                if (ApplyOne(pair.Key, pair.Value))
                    restored++;
            }
            undoSnapshot = null;
            return DebugResult.Ok($"{restored}개 되돌림");
        }

        public static DebugResult SaveCurrent(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
                name = "묶음 " + (User.Count + 1);
            if (IsBuiltIn(name))
                return DebugResult.Fail("기본 묶음 이름은 쓸 수 없어요");

            var created = new Preset { name = name };
            foreach (KeyValuePair<string, string> pair in CaptureAll())
            {
                created.ids.Add(pair.Key);
                created.values.Add(pair.Value);
            }
            User.RemoveAll(preset => preset.name == name);
            User.Add(created);
            SaveUser();
            selected = name;
            pendingName = string.Empty;
            return DebugResult.Ok($"'{name}' 저장 · 값 {created.ids.Count}개");
        }

        public static DebugResult Delete(string name)
        {
            if (string.IsNullOrEmpty(name))
                return DebugResult.Fail("고른 묶음이 없어요");
            if (IsBuiltIn(name))
                return DebugResult.Fail("기본 묶음은 지울 수 없어요");
            if (User.RemoveAll(preset => preset.name == name) == 0)
                return DebugResult.Fail("묶음을 찾지 못했어요");
            SaveUser();
            selected = null;
            return DebugResult.Ok($"'{name}' 삭제");
        }

        public static bool IsBuiltIn(string name)
        {
            for (int i = 0; i < builtIns.Count; i++)
            {
                if (builtIns[i].name == name)
                    return true;
            }
            return false;
        }

        private static bool ApplyOne(string id, string value)
        {
            DebugItem item = DebugRegistry.Find(id);
            if (item == null || !item.InPresets || !item.IsEnabled)
                return false;
            if (item.CaptureValue() == value)
                return true;
            try
            {
                if (!item.ApplyValue(value))
                    return false;
                item.afterChange?.Invoke();
                if (item.persist)
                    DebugPrefs.SaveValue(item);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private static Dictionary<string, string> CaptureAll()
        {
            var values = new Dictionary<string, string>();
            foreach (DebugItem item in DebugRegistry.AllItems)
            {
                if (!item.InPresets || values.ContainsKey(item.Id))
                    continue;
                string value = item.CaptureValue();
                if (value != null)
                    values.Add(item.Id, value);
            }
            return values;
        }

        private static Preset Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            for (int i = 0; i < builtIns.Count; i++)
            {
                if (builtIns[i].name == name)
                    return builtIns[i];
            }
            List<Preset> saved = User;
            for (int i = 0; i < saved.Count; i++)
            {
                if (saved[i].name == name)
                    return saved[i];
            }
            return null;
        }

        private static List<Preset> User
        {
            get
            {
                if (user != null)
                    return user;
                user = new List<Preset>();
                string json = PlayerPrefs.GetString(DebugPrefs.PresetsKey, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        PresetFile file = JsonUtility.FromJson<PresetFile>(json);
                        if (file?.presets != null)
                        {
                            foreach (Preset preset in file.presets)
                            {
                                if (preset != null && !string.IsNullOrWhiteSpace(preset.name)
                                    && preset.ids != null && preset.values != null
                                    && preset.ids.Count == preset.values.Count)
                                    user.Add(preset);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[DebugHub] 저장된 설정 묶음을 읽지 못했어요: " + exception.Message);
                    }
                }
                return user;
            }
        }

        private static void SaveUser()
        {
            PlayerPrefs.SetString(DebugPrefs.PresetsKey, JsonUtility.ToJson(new PresetFile { presets = User }));
            namesDirty = true;
        }
    }
}
#endif
