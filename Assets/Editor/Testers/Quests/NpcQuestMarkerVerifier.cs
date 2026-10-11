using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using Overburst.Quests;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class NpcQuestMarkerVerifier
{
    const string MerchantId = "merchant.main-town.weapon";
    static readonly List<string> checks = new List<string>();
    static TextMeshPro Mark(NpcQuestMarker marker) => (TextMeshPro)new SerializedObject(marker).FindProperty("marker").objectReferenceValue;
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }

    public static void CheckLive(string symbol, bool gold)
    {
        var marker = UnityEngine.Object.FindObjectsByType<NpcQuestMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(m => { var npc = m.GetComponentInParent<GeneralGoodsMerchantInteractable>(); return npc && MerchantReputationService.GetStableMerchantId(npc.Definition) == MerchantId; });
        var text = Mark(marker);
        QuestMerchantPlayVerifier.Check(text.gameObject.activeInHierarchy == (symbol != null)
            && (symbol == null || text.text == symbol && (ColorUtility.ToHtmlStringRGB(text.color) == "D3A666") == gold),
            "Quest head marker: " + (symbol ?? "hidden") + (symbol == null ? "" : gold ? " gold" : " gray"));
    }

    public static object Run()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        checks.Clear();
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NpcQuestMarkerBuilder.FontPath);
        Check(font && font.HasCharacters("!?") && font.atlasPopulationMode == AtlasPopulationMode.Static
            && font.atlasWidth == 256 && font.atlasHeight == 256 && font.characterTable.Count == 2, "Two static 256px Cambria glyphs");
        var root = new GameObject("Owned quest marker native check", typeof(GeneralGoodsMerchantInteractable));
        try
        {
            var npc = root.GetComponent<GeneralGoodsMerchantInteractable>();
            var serialized = new SerializedObject(npc);
            serialized.FindProperty("merchantDefinition").objectReferenceValue = registry.Resolve<MerchantDefinition>(MerchantId);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var label = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(NpcPresentationBuilder.PrefabPath), root.transform);
            var marker = label.GetComponent<NpcQuestMarker>();
            var text = Mark(marker);
            Check(marker && text && text.font == font && !text.raycastTarget && !text.enableAutoSizing && text.transform.localPosition.y > .4f,
                "Serialized head glyph above existing name with no raycasts/autosizing");
            var state = NewAccountFactory.Create(registry);
            var hunt = QuestCatalog.Find("quest.main-town.weapon.first-hunt");
            var map = QuestCatalog.Find("quest.main-town.weapon.first-map");
            Action<string, bool> expect = (symbol, gold) => Check(text.gameObject.activeSelf == (symbol != null)
                && (symbol == null || text.text == symbol && (ColorUtility.ToHtmlStringRGB(text.color) == "D3A666") == gold),
                "Native head: " + (symbol ?? "hidden") + (symbol == null ? "" : gold ? " gold" : " gray"));
            marker.Present(state); expect("!", true);
            text.ForceMeshUpdate(); Check(text.textInfo.characterCount == 1 && text.textInfo.characterInfo[0].isVisible && text.mesh.vertexCount >= 4, "Exclamation generates a visible glyph mesh (characters=" + text.textInfo.characterCount + ", vertices=" + text.mesh.vertexCount + ")");
            AccountQuestCommands.Accept(state, hunt, MerchantId, registry); marker.Present(state); expect("?", false);
            text.ForceMeshUpdate(); Check(text.textInfo.characterCount == 1 && text.textInfo.characterInfo[0].isVisible && text.mesh.vertexCount >= 4, "Question generates a visible glyph mesh (characters=" + text.textInfo.characterCount + ", vertices=" + text.mesh.vertexCount + ")");
            state.quests[0].objectives.ForEach(o => o.current = o.required); marker.Present(state); expect("?", true);
            state.quests[0].state = SavedQuestState.Completed; marker.Present(state); expect("!", true);
            AccountQuestCommands.Accept(state, map, MerchantId, registry); marker.Present(state); expect("?", false);
            state.quests[1].objectives.ForEach(o => o.current = o.required); marker.Present(state); expect("?", true);
            state.quests[1].state = SavedQuestState.Completed; marker.Present(state); expect(null, true);
            marker.Present(NewAccountFactory.Create(registry)); expect("!", true);
            marker.Present(null); expect(null, true);
            // A saved active map record plus an available hunt exercises priority without mutating definitions.
            state.quests.RemoveAt(0); state.quests[0].state = SavedQuestState.Active;
            state.quests[0].objectives.ForEach(o => o.current = 0); marker.Present(state); expect("!", true);
            serialized.FindProperty("merchantDefinition").objectReferenceValue = registry.Resolve<MerchantDefinition>("merchant.main-town.potion");
            serialized.ApplyModifiedPropertiesWithoutUndo(); marker.Present(state); expect(null, true);
            var result = new { status = "PASS_NATIVE", checks = checks.ToArray(), count = checks.Count };
            var directory = Path.GetFullPath("../개인파일/코덱스산출/Implementation/20261011_NpcQuestMarkers/Evidence");
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "native.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
