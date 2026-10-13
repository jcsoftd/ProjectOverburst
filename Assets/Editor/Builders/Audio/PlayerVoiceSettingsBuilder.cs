using System;
using System.IO;
using System.Linq;
using DuloGames.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class PlayerVoiceSettingsBuilder
{
    public static void Apply(string output)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        Directory.CreateDirectory(output);
        var audition = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVoiceAudition.PrefabPath).GetComponent<PlayerVoiceAudition>();
        if (audition.Candidates.Sum(s => s.attack.Length + s.hurt.Length + s.death.Length + s.jump.Length) != 69)
            throw new InvalidOperationException("Expected the approved 69 clips");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVoiceService.PrefabPath))
            throw new InvalidOperationException("Production voice prefab exists; do not overwrite");
        string directory = Path.GetDirectoryName(PlayerVoiceService.PrefabPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder("Assets/ProjectOverburst/Resources", "Audio");
        var scene = EditorSceneManager.NewPreviewScene();
        var host = new GameObject("PF_PlayerVoice", typeof(AudioSource));
        SceneManager.MoveGameObjectToScene(host, scene);
        try
        {
            var audio = host.GetComponent<AudioSource>(); audio.playOnAwake = false; audio.loop = false;
            audio.spatialBlend = 0f; audio.volume = .8f; audio.pitch = 1f; audio.priority = 40;
            host.AddComponent<PlayerVoiceService>().Configure(audition.Candidates.Select((s,i) => new PlayerVoiceService.VoiceSet
            { name=i==0 ? "Karen1" : s.name, attack=s.attack, hurt=s.hurt, death=s.death, jump=s.jump }).ToArray());
            PrefabUtility.SaveAsPrefabAsset(host, PlayerVoiceService.PrefabPath);
        }
        finally { Object.DestroyImmediate(host); EditorSceneManager.ClosePreviewScene(scene); }

        ApplySettings(output);
    }
    public static void ApplySettings(string output)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath);
        var originalPanel = source.GetComponentInChildren<OverburstSettingsPanel>(true);
        string panelPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(originalPanel.gameObject);
        if (string.IsNullOrEmpty(panelPath)) panelPath = OverburstGameMenuBuilder.PrefabPath;
        if (!File.Exists(Path.Combine(output, "settings-before.prefab"))) File.Copy(panelPath, Path.Combine(output, "settings-before.prefab"), false);
        var root = PrefabUtility.LoadPrefabContents(panelPath);
        try
        {
            var panel = root.GetComponentInChildren<OverburstSettingsPanel>(true);
            if (!panel || panel.playerVoice) throw new InvalidOperationException("Unexpected settings source");
            var view = panel.GetComponent<OverburstSettingsGothicView>();
            var template = panel.screenMode.GetComponentInParent<OverburstSettingsGothicRow>(true);
            var row = Object.Instantiate(template, view.scrolls[0].content);
            row.name = "Row • 캐릭터 음성"; row.transform.SetSiblingIndex(panel.uiVolume.GetComponentInParent<OverburstSettingsGothicRow>(true).transform.GetSiblingIndex()+1);
            row.owner = view; row.tabIndex = 0; row.title = "캐릭터 음성";
            row.description = "공격·피격·사망·점프에 사용할 목소리";
            row.tip = "변경하면 공격음을 미리 듣습니다. 기본 음성은 Karen1입니다.";
            row.icon = panel.masterVolume.GetComponentInParent<OverburstSettingsGothicRow>(true).icon;
            row.transform.Find("Label").GetComponent<Text>().text = row.title;
            row.transform.Find("Description").GetComponent<Text>().text = row.description;
            row.SetHighlighted(false);
            panel.playerVoice = row.GetComponentInChildren<UISwitchSelect>(true);
            panel.playerVoice.onChange = new UISwitchSelect.ChangeEvent();
            panel.playerVoice.options.Clear(); panel.playerVoice.options.AddRange(new[]{"Karen1","Karen2","Kimlinh","Rina","끄기"});
            panel.playerVoice.SelectOptionByIndex(0);
            view.rows = view.rows.Concat(new[]{row}).ToArray();
            PrefabUtility.SaveAsPrefabAsset(root, panelPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        File.WriteAllText(Path.Combine(output,"applied.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { voice=PlayerVoiceService.PrefabPath, settings=panelPath, clips=69 }));
    }

    public static void RefreshManagement(string output)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(OverburstUIWorkshopBuilder.ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (!opened && scene.isDirty) throw new InvalidOperationException("Management scene has unsaved changes");
        if (opened) scene = EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            var panels = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<OverburstSettingsPanel>(true)).ToArray();
            if (panels.Length==0 || panels.Any(p=>!p.playerVoice)) throw new InvalidOperationException("Management settings did not inherit the production row");
            if (panels.Any(p=>p.playerVoice.value!="Karen1")) throw new InvalidOperationException("Management voice default differs");
            // Connected prefab instances inherit the row. Saving this gallery would also serialize
            // unrelated ExecuteAlways-generated exhibit objects, so preserve its existing scene bytes.
            File.WriteAllText(Path.Combine(output,"management.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {status="PASS", instances=panels.Length, scene=OverburstUIWorkshopBuilder.ScenePath, inheritedFromProductionPrefab=true}));
        }
        finally { if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); if(opened) EditorSceneManager.CloseScene(scene,true); }
    }
}
