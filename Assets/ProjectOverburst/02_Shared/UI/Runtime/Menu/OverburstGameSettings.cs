using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 2026-10-01 ESC 메뉴 설정(소리·화면·전투 표시·조작). 계정 저장과 분리해 이 PC의 설정 파일 하나에 둔다.
/// 경로: OVERBURST_SETTINGS_DIRECTORY → OVERBURST_SAVE_DIRECTORY(격리 검증 계정) → persistentDataPath.
/// 화면 설정은 사용자가 한 번이라도 바꾼 뒤에만 적용해, 설정 파일이 없을 때는 프로젝트 기본값을 건드리지 않는다.
/// </summary>
public enum CombatFacingIndicatorStyle { Quiet = 0, Extended = 1 }

public static class OverburstGameSettings
{
    public const string FileName = "overburst_settings.json";
    public static readonly int[] FrameLimits = { 30, 60, 120, 144, 165, 240, -1 };

    [Serializable]
    private sealed class Data
    {
        public int version = 1;
        public float masterVolume = 1f;
        public float uiVolume = .8f;
        public bool muteInBackground;
        public bool displayCustomized;
        public int screenMode = (int)FullScreenMode.FullScreenWindow;
        public int width;
        public int height;
        public bool vSync = true;
        public int frameLimit = -1;
        public float cameraShake = 1f;
        public float hitEffect = 1f;
        public bool combatFacingIndicator = true;
        public int combatFacingStyle = (int)CombatFacingIndicatorStyle.Extended;
        public float combatFacingBrightness = 1f;
        public string bindingOverrides = string.Empty;
    }

    private static Data data = new Data();
    private static bool loaded;
    private static bool focused = true;

    public static event Action Changed;

    public static float MasterVolume { get { Ensure(); return data.masterVolume; } set { Ensure(); data.masterVolume = Mathf.Clamp01(value); ApplyAudio(); Notify(); } }
    public static float UiVolume { get { Ensure(); return data.uiVolume; } set { Ensure(); data.uiVolume = Mathf.Clamp01(value); Notify(); } }
    public static bool MuteInBackground { get { Ensure(); return data.muteInBackground; } set { Ensure(); data.muteInBackground = value; ApplyAudio(); Notify(); } }
    public static float CameraShakeScale { get { Ensure(); return data.cameraShake; } set { Ensure(); data.cameraShake = Mathf.Clamp01(value); Notify(); } }
    public static float HitEffectScale { get { Ensure(); return data.hitEffect; } set { Ensure(); data.hitEffect = Mathf.Clamp01(value); Notify(); } }
    public static bool CombatFacingIndicator { get { Ensure(); return data.combatFacingIndicator; } set { Ensure(); if (data.combatFacingIndicator == value) return; data.combatFacingIndicator = value; Notify(); } }

    public static CombatFacingIndicatorStyle CombatFacingStyle
    {
        get { Ensure(); return (CombatFacingIndicatorStyle)data.combatFacingStyle; }
        set { Ensure(); int style = Mathf.Clamp((int)value, 0, 1); if (data.combatFacingStyle == style) return; data.combatFacingStyle = style; Notify(); }
    }
    public static float CombatFacingBrightness
    {
        get { Ensure(); return data.combatFacingBrightness; }
        set { Ensure(); float brightness = NormalizeFacingBrightness(value); if (Mathf.Approximately(data.combatFacingBrightness, brightness)) return; data.combatFacingBrightness = brightness; Notify(); }
    }
    private static float NormalizeFacingBrightness(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp(value, 0f, 2f);

    public static FullScreenMode ScreenMode { get { Ensure(); return (FullScreenMode)data.screenMode; } }
    public static Vector2Int Resolution { get { Ensure(); return new Vector2Int(data.width, data.height); } }
    public static bool VSync { get { Ensure(); return data.vSync; } }
    public static int FrameLimit { get { Ensure(); return data.frameLimit; } }

    public static string FilePath
    {
        get
        {
            string dir = Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY");
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
            if (string.IsNullOrEmpty(dir)) dir = Application.persistentDataPath;
            return Path.Combine(dir, FileName);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        data = new Data();
        loaded = false;
        focused = true;
        dirty = false;
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        Ensure();
        ApplyAudio();
        if (data.displayCustomized) ApplyDisplay(true);
        var host = new GameObject("OverburstSettingsFocus") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<OverburstSettingsFocusWatcher>();
    }

    private static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        data = new Data { vSync = QualitySettings.vSyncCount > 0, frameLimit = Application.targetFrameRate > 0 ? Application.targetFrameRate : -1,
            screenMode = (int)Screen.fullScreenMode, width = Screen.currentResolution.width, height = Screen.currentResolution.height };
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return;
            // 새 항목은 초기값을 유지한다. 이전 파일에 방향 표시가 없으면 켜짐으로 시작한다.
            var read = new Data { vSync = data.vSync, frameLimit = data.frameLimit, screenMode = data.screenMode, width = data.width, height = data.height };
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path), read);
            read.combatFacingStyle = Mathf.Clamp(read.combatFacingStyle, 0, 1);
            read.combatFacingBrightness = NormalizeFacingBrightness(read.combatFacingBrightness);
            data = read;
        }
        catch (Exception error)
        {
            Debug.LogWarning("[OverburstGameSettings] 설정 파일을 읽지 못해 기본값을 씁니다: " + error.Message);
        }
    }

    public static void Save()
    {
        Ensure();
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        catch (Exception error)
        {
            Debug.LogWarning("[OverburstGameSettings] 설정을 저장하지 못했습니다: " + error.Message);
        }
    }

    // 화면 모드·해상도는 되돌릴 수 있게 이전 값을 돌려준다(10초 유지 확인).
    public static void SetDisplay(FullScreenMode mode, Vector2Int resolution)
    {
        Ensure();
        data.displayCustomized = true;
        data.screenMode = (int)mode;
        data.width = resolution.x;
        data.height = resolution.y;
        ApplyDisplay(true);
        Notify();
        SaveIfDirty();
    }

    public static void SetFrameOptions(bool vSync, int frameLimit)
    {
        Ensure();
        data.displayCustomized = true;
        data.vSync = vSync;
        data.frameLimit = frameLimit;
        ApplyDisplay(false);
        Notify();
        SaveIfDirty();
    }

    public static void ResetSection(string section)
    {
        Ensure();
        var defaults = new Data();
        switch (section)
        {
            case "sound": data.masterVolume = defaults.masterVolume; data.uiVolume = defaults.uiVolume; data.muteInBackground = defaults.muteInBackground; ApplyAudio(); break;
            case "combat": data.cameraShake = defaults.cameraShake; data.hitEffect = defaults.hitEffect; data.combatFacingIndicator = defaults.combatFacingIndicator; data.combatFacingStyle = defaults.combatFacingStyle; data.combatFacingBrightness = defaults.combatFacingBrightness; break;
        }
        Notify();
    }

    public static void ApplyBindingOverrides(InputActionAsset asset)
    {
        Ensure();
        if (asset == null || string.IsNullOrEmpty(data.bindingOverrides)) return;
        try { asset.LoadBindingOverridesFromJson(data.bindingOverrides); }
        catch (Exception error) { Debug.LogWarning("[OverburstGameSettings] 저장된 키 설정을 적용하지 못했습니다: " + error.Message); }
    }

    public static void StoreBindingOverrides(InputActionAsset asset)
    {
        Ensure();
        data.bindingOverrides = asset != null ? asset.SaveBindingOverridesAsJson() : string.Empty;
        Save();
        Changed?.Invoke();
    }

    internal static void SetFocused(bool value)
    {
        focused = value;
        ApplyAudio();
    }

    private static void ApplyAudio()
    {
        AudioListener.volume = data.muteInBackground && !focused ? 0f : data.masterVolume;
    }

    private static void ApplyDisplay(bool includeScreen)
    {
        QualitySettings.vSyncCount = data.vSync ? 1 : 0;
        Application.targetFrameRate = data.vSync ? -1 : data.frameLimit;
        // 에디터 Game 창에서는 해상도·전체 화면을 바꿀 수 없어 빌드에서만 적용한다.
        if (includeScreen && !Application.isEditor && data.width > 0 && data.height > 0)
            Screen.SetResolution(data.width, data.height, (FullScreenMode)data.screenMode);
    }

    // 슬라이더는 끌어 옮기는 동안 매 프레임 바뀌므로 파일은 창을 닫거나 게임을 끌 때 한 번 쓴다(SaveIfDirty).
    private static bool dirty;

    public static void SaveIfDirty()
    {
        if (!dirty) return;
        dirty = false;
        Save();
    }

    private static void Notify()
    {
        dirty = true;
        Changed?.Invoke();
    }
}
