using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class BagInventoryCapacityBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstInventory_Rpg11.prefab";
    public static string UpdateBagDescriptions()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build requires Edit mode.");
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:BagItemData", new[] { "Assets/ProjectOverburst/03_Features/Items/Data/Items/Bags" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<BagItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (!ItemGradeAvailabilityPolicy.IsEnabled(data.defaultGrade)) continue;
            data.description = "전리품을 더 많이 수납하고 탐색과 파밍을 돕는 가방입니다.";
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data); count++;
        }
        return "PASS " + count + " bag descriptions updated; IDs, grades and source assets preserved";
    }
    [MenuItem("OVERBURST/Items/Build Bag Inventory Capacity")]
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build requires Edit mode.");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var grid = root.GetComponentInChildren<GridLayoutGroup>(true);
            if (grid == null || grid.transform.childCount == 0) throw new InvalidOperationException("Inventory grid missing.");
            int before = grid.transform.childCount;
            while (grid.transform.childCount < BagQuality.InventoryCapacity)
            {
                var slot = UnityEngine.Object.Instantiate(grid.transform.GetChild(0).gameObject, grid.transform);
                slot.name = "Bag Inventory Slot " + grid.transform.childCount;
                slot.transform.localScale = Vector3.one;
            }
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;
            var rect = (RectTransform)grid.transform;
            var size = rect.sizeDelta;
            size.y = grid.padding.vertical + 8 * grid.cellSize.y + 7 * grid.spacing.y;
            rect.sizeDelta = size;
            var scroll = grid.GetComponentInParent<ScrollRect>(true);
            if (scroll == null) throw new InvalidOperationException("Inventory scroll view missing.");
            scroll.content = rect;
            scroll.vertical = true;
            scroll.horizontal = false;
            if (grid.GetComponentsInChildren<SlotUI>(true).Length != BagQuality.InventoryCapacity)
                throw new InvalidOperationException("Inventory slot binding mismatch.");
            if (root.GetComponentsInChildren<Transform>(true).Sum(x => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(x.gameObject)) != 0)
                throw new InvalidOperationException("Missing script in inventory prefab.");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            return "PASS inventory prefab " + before + " -> 48 slots, six columns, eight rows, vertical scrolling; no scene saved";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
