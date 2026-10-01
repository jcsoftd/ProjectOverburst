using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 상단 대상·보스 HUD의 원소 상태와 독립 기절 칸.
// 지나간 시간만큼 시계방향으로 어두워지는 덮개(Filled Radial360)와 칸 오른쪽 아래 중첩 수를 함께 보인다.
// 아이콘 그림은 같은 HUD의 ElementalStatusIconStrip에 지정된 원소 스프라이트를 그대로 쓴다(머리 위 바와 같은 그림).
[DisallowMultipleComponent]
public sealed class EnemyTargetStatusRow : MonoBehaviour
{
    private static readonly WeaponElement[] DisplayOrder =
    {
        WeaponElement.Fire,
        WeaponElement.Ice,
        WeaponElement.Electric,
        WeaponElement.Dark,
        WeaponElement.Light
    };

    [Serializable]
    public sealed class Cell
    {
        public RectTransform root;
        public Image icon;
        public Image sweep;
        public TMP_Text stack;
    }

    [SerializeField] private ElementalStatusIconStrip iconSource;
    [SerializeField] private Cell[] cells = Array.Empty<Cell>();
    [SerializeField] private float cellSpacing = 54f;

    private ElementalStatusController controller;
    private readonly float[] fullDuration = new float[6];
    private readonly float[] lastRemaining = new float[6];
    private int[] shownStacks = Array.Empty<int>();
    private int shownCount = -1;
    private bool wasFrozen;

    public int VisibleCount => Mathf.Max(0, shownCount);

    public void Configure(ElementalStatusIconStrip source, Cell[] configuredCells, float spacing)
    {
        iconSource = source;
        cells = configuredCells ?? Array.Empty<Cell>();
        cellSpacing = spacing;
    }

    public void Bind(CombatHealth health)
    {
        ElementalStatusController next = null;
        if (health != null)
        {
            next = health.GetComponent<ElementalStatusController>();
            if (next == null)
                next = health.GetComponentInParent<ElementalStatusController>();
        }

        controller = next;
        Array.Clear(fullDuration, 0, fullDuration.Length);
        Array.Clear(lastRemaining, 0, lastRemaining.Length);
        wasFrozen = false;
        Refresh();
    }

    public void Unbind()
    {
        controller = null;
        HideFrom(0);
    }

    // 표시 중인 대상 1명만 읽는다(원소 5종 조회).
    private void LateUpdate()
    {
        if (controller != null)
            Refresh();
    }

    private void Refresh()
    {
        if (shownStacks.Length != cells.Length)
            shownStacks = new int[cells.Length];

        int shown = 0;
        for (int i = 0; i < DisplayOrder.Length; i++)
        {
            WeaponElement element = DisplayOrder[i];
            ElementalStatusSnapshot snapshot = default;
            ElementalReactionStateSnapshot frozen = default;
            bool isFrozen = element == WeaponElement.Ice && controller != null
                && controller.TryGetReactionState(ElementalReactionType.Freeze, out frozen);
            bool active = controller != null && controller.TryGetStatus(element, out snapshot) && snapshot.IsActive;
            if (element == WeaponElement.Ice && wasFrozen != isFrozen)
            {
                fullDuration[i] = lastRemaining[i] = 0f;
                wasFrozen = isFrozen;
            }
            if (!active && !isFrozen)
            {
                fullDuration[i] = 0f;
                lastRemaining[i] = 0f;
                continue;
            }

            float remaining = isFrozen ? frozen.RemainingDuration : snapshot.RemainingDuration;
            if (fullDuration[i] <= 0f || remaining > lastRemaining[i] + 0.01f)
                fullDuration[i] = Mathf.Max(remaining, 0.01f); // 새로 걸리거나 갱신되면 그때 남은 시간이 전체 길이
            lastRemaining[i] = remaining;

            if (shown >= cells.Length || iconSource == null || !iconSource.TryGetIcon(element, out Sprite sprite))
                continue;

            Cell cell = cells[shown];
            if (cell == null || cell.root == null)
                continue;

            if (!cell.root.gameObject.activeSelf)
                cell.root.gameObject.SetActive(true);
            cell.root.anchoredPosition = new Vector2(shown * cellSpacing, 0f);
            if (cell.icon != null && cell.icon.sprite != sprite)
                cell.icon.sprite = sprite;
            if (cell.sweep != null)
                cell.sweep.fillAmount = 1f - Mathf.Clamp01(remaining / fullDuration[i]);
            int stackCount = isFrozen ? 1 : snapshot.StackCount;
            if (cell.stack != null && shownStacks[shown] != stackCount)
            {
                shownStacks[shown] = stackCount;
                cell.stack.text = stackCount > 1 ? stackCount.ToString() : string.Empty;
            }

            shown++;
        }

        if (shown < cells.Length && iconSource != null && iconSource.TryGetStun(out float stunRemaining, out Sprite stunSprite))
        {
            const int index = 5;
            if (fullDuration[index] <= 0f || stunRemaining > lastRemaining[index] + .01f)
                fullDuration[index] = stunRemaining;
            lastRemaining[index] = stunRemaining;
            Cell cell = cells[shown];
            if (cell != null && cell.root != null)
            {
                cell.root.gameObject.SetActive(true);
                cell.root.anchoredPosition = new Vector2(shown * cellSpacing, 0f);
                if (cell.icon != null) cell.icon.sprite = stunSprite;
                if (cell.sweep != null) cell.sweep.fillAmount = 1f - Mathf.Clamp01(stunRemaining / Mathf.Max(.01f, fullDuration[index]));
                if (cell.stack != null) cell.stack.text = string.Empty;
                shownStacks[shown] = -1;
                shown++;
            }
        }
        else { fullDuration[5] = lastRemaining[5] = 0f; }

        HideFrom(shown);
        shownCount = shown;
    }

    private void HideFrom(int start)
    {
        for (int i = start; i < cells.Length; i++)
        {
            Cell cell = cells[i];
            if (cell != null && cell.root != null && cell.root.gameObject.activeSelf)
                cell.root.gameObject.SetActive(false);
            if (i < shownStacks.Length)
                shownStacks[i] = -1;
        }

        if (start == 0)
            shownCount = 0;
    }

    // 편집 모드 캡처·워크숍 미리보기용: 컨트롤러 없이 원소·남은 비율·중첩 수를 직접 넣는다.
    public void ShowPreview(WeaponElement[] elements, float[] remaining01, int[] stacks)
    {
        controller = null;
        int count = elements != null ? Mathf.Min(elements.Length, cells.Length) : 0;
        int shown = 0;
        for (int i = 0; i < count; i++)
        {
            if (iconSource == null || !iconSource.TryGetIcon(elements[i], out Sprite sprite))
                continue;

            Cell cell = cells[shown];
            cell.root.gameObject.SetActive(true);
            cell.root.anchoredPosition = new Vector2(shown * cellSpacing, 0f);
            cell.icon.sprite = sprite;
            cell.sweep.fillAmount = 1f - Mathf.Clamp01(remaining01 != null && i < remaining01.Length ? remaining01[i] : 1f);
            int stack = stacks != null && i < stacks.Length ? stacks[i] : 1;
            cell.stack.text = stack > 1 ? stack.ToString() : string.Empty;
            shown++;
        }

        HideFrom(shown);
        shownCount = shown;
    }
}
