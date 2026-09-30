using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 2026-10-01 장착 무기 비교: 승인 툴팁 프리팹에 비교 표시 오브젝트를 저작한다(런타임 임시 생성 대신, 99A).
// 다시 실행해도 이미 있는 오브젝트는 건너뛴다. 되돌리기는 git의 프리팹 원본 또는 코덱스산출 백업.
public static class TooltipCompareObjectizer
{
    public const string PrefabPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstTooltip_Rpg11.prefab";
    private const int RowCount = 16;

    [MenuItem("OVERBURST/Codex/Objectizers/UI/Add Tooltip Weapon Compare Objects")]
    private static void RunMenu() => Debug.Log("[TooltipCompareObjectizer] " + Run());

    public static string Run()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        int added = 0;
        try
        {
            Transform content = root.transform.Find("Approved Content");
            Transform rows = content != null ? content.Find("Stat Rows") : null;
            if (rows == null) return "FAILED: Approved Content/Stat Rows missing";

            for (int i = 0; i < RowCount; i++)
            {
                Transform row = rows.Find("Row " + i.ToString("00"));
                if (row == null || row.Find("Compare") != null) continue;
                Transform value = row.Find("Value");
                GameObject clone = UnityEngine.Object.Instantiate(value.gameObject, row, false);
                clone.name = "Compare";
                clone.transform.SetSiblingIndex(value.GetSiblingIndex() + 1);
                TextMeshProUGUI text = clone.GetComponent<TextMeshProUGUI>();
                text.text = string.Empty;
                text.fontSize = 14f;
                text.alignment = TextAlignmentOptions.MidlineRight;
                text.raycastTarget = false;
                var rect = (RectTransform)clone.transform;
                rect.anchoredPosition = new Vector2(236f, 0f);
                rect.sizeDelta = new Vector2(60f, 40f);
                clone.SetActive(false);
                added++;
            }

            // 1차 시안의 요약 띠·맨 아래 안내는 사용자 결정으로 뺐다(2026-10-01). 남아 있으면 지운다.
            foreach (string retired in new[] { "Compare Summary Fill", "Compare Summary", "Compare Footer" })
            {
                Transform old = content.Find(retired);
                if (old != null) { UnityEngine.Object.DestroyImmediate(old.gameObject); added++; }
            }
            added += CloneText(content, "Quality Heading", "Compare Heading", text => text.text = "장착 대비");

            // 장착 중인 무기: 아이콘 아래쪽 띠. 아이콘 틀 바로 뒤에 두어 아이콘 위에 그려진다.
            Transform iconFrame = root.transform.Find("Approved Icon Frame");
            if (iconFrame != null && root.transform.Find("Equipped Tag") == null)
            {
                var tag = new GameObject("Equipped Tag", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                tag.transform.SetParent(root.transform, false);
                tag.transform.SetSiblingIndex(iconFrame.GetSiblingIndex() + 1);
                var rect = (RectTransform)tag.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(27f, -77f);
                rect.sizeDelta = new Vector2(70f, 18f);
                Image image = tag.GetComponent<Image>();
                image.color = new Color(.17f, .13f, .07f, .94f);
                image.raycastTarget = false;
                Transform grade = root.transform.Find("Item Grade");
                if (grade != null)
                {
                    GameObject label = UnityEngine.Object.Instantiate(grade.gameObject, tag.transform, false);
                    label.name = "Equipped Tag Text";
                    TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
                    text.text = "장착 중";
                    text.fontSize = 11.5f;
                    text.fontStyle = FontStyles.Bold;
                    text.color = new Color(.94f, .82f, .55f, 1f);
                    text.alignment = TextAlignmentOptions.Center;
                    text.raycastTarget = false;
                    var labelRect = (RectTransform)label.transform;
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.pivot = new Vector2(.5f, .5f);
                    labelRect.anchoredPosition = Vector2.zero;
                    labelRect.sizeDelta = Vector2.zero;
                }
                tag.SetActive(false);
                added++;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            return (saved ? "saved" : "SAVE FAILED") + " added=" + added;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int CloneText(Transform parent, string source, string name, Action<TextMeshProUGUI> setup)
    {
        if (parent.Find(name) != null) return 0;
        Transform original = parent.Find(source);
        if (original == null) return 0;
        GameObject clone = UnityEngine.Object.Instantiate(original.gameObject, parent, false);
        clone.name = name;
        TextMeshProUGUI text = clone.GetComponent<TextMeshProUGUI>();
        text.text = string.Empty;
        text.richText = true;
        text.raycastTarget = false;
        setup(text);
        clone.SetActive(false);
        return 1;
    }
}
