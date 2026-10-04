using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.ToolHub
{
    public sealed class OverburstToolHubWindow : EditorWindow // 제작 도구의 공통 진입점
    {
        public const string MenuPath = "OVERBURST/도구 허브";
        private const string StylePath = "Assets/Editor/Tools/ToolHub/OverburstToolHubWindow.uss";
        private const string IconRoot = "Assets/Editor/Tools/ToolHub/Icons/";

        private static readonly ToolEntry ComboMaker = new ToolEntry(
            "combo-maker", "무기 공격 제작", "콤보 메이커",
            "약공·강공·패링 반격·닷지 공격의 모션과 판정을 조립하고, 타임라인과 실시간 미리보기로 확인합니다.",
            "OVERBURST/무기/콤보 메이커", "ToolIcon_ComboMaker.png");

        private static readonly ToolEntry[] CoreTools =
        {
            new ToolEntry("monster-tuner", "몬스터 제작", "몬스터 튜너",
                "몬스터의 능력치·모션·공격 범위·피격 반응을 작업 사본에서 조정하고 미리 봅니다.",
                "OVERBURST/Monsters/몬스터 튜너", "ToolIcon_MonsterTuner.png"),
            new ToolEntry("boss-maker", "보스 공격 제작", "보스 메이커",
                "보스 공격 재료의 판정·타격 시점·패링·피해·이동·발사를 조립하고 미리 봅니다.",
                "OVERBURST/Bosses/보스 메이커", "ToolIcon_BossMaker.png"),
            new ToolEntry("balance-table", "아이템·전투 밸런스", "밸런스 테이블",
                "무기·장비·몬스터·공격·성장 값을 한 표에서 비교하고 레벨·품질별 결과를 확인합니다.",
                "OVERBURST/Balance/무기·장비·몬스터 밸런스 테이블", "ToolIcon_BalanceTable.png"),
            new ToolEntry("weapon-pose", "무기 자세", "무기 포즈 튜닝 V2",
                "캐릭터와 무기의 조합을 보며 장착 위치·회전과 무기 포즈를 조정합니다.",
                "JC Tool/Pose/무기 포즈 튜닝 V2", "ToolIcon_WeaponPose.png"),
            new ToolEntry("element-fx", "대검 원소 연출", "대검 원소 효과",
                "대검의 원소별 검신·트레일 효과와 위치·회전·크기·재생 상태를 조정합니다.",
                "OVERBURST/무기/대검 원소 효과 조절", "ToolIcon_ElementFx.png"),
            new ToolEntry("ground-indicator", "공격 범위 연출", "인디케이터 조절",
                "원형·부채꼴·도넛·사각형의 실제 미터 범위와 예고 진행률을 조정하고 미리 봅니다.",
                "JC Tool/VFX/인디케이터/숫자 조절 미리보기", "ToolIcon_GroundIndicator.png"),
            new ToolEntry("vfx-board", "이펙트 관리", "VFX 관리 보드",
                "VFX 프리팹과 분류·후보·메모를 정리하고 작업할 이펙트를 찾아 미리 봅니다.",
                "JC Tool/VFX/VFX 관리 보드", "ToolIcon_VfxBoard.png"),
            new ToolEntry("audio-catalog", "사운드 관리", "오디오 카탈로그",
                "프로젝트 오디오 카탈로그의 음원·분류·메모를 검색하고 효과음 후보를 관리합니다.",
                "JC Tool/오디오/오디오 카탈로그 관리자", "ToolIcon_AudioCatalog.png")
        };

        private static readonly ToolEntry[] GeneralTools =
        {
            new ToolEntry("animation-preview", "범용 애니메이션", "캐릭터·무기 미리보기",
                "캐릭터 모델·애니메이션·무기를 함께 재생하며 모션과 장착 상태를 확인합니다.",
                "JC Tool/Animation/캐릭터·무기 애니메이션 프리뷰", "ToolIcon_AnimationPreview.png"),
            new ToolEntry("vfx-preview", "범용 이펙트", "VFX 미리보기",
                "VFX 프리팹을 격리 공간에서 재생하며 속도·반복·환경과 카메라 구도를 확인합니다.",
                "JC Tool/VFX/VFX 프리팹 미리보기", "ToolIcon_VfxPreview.png"),
            new ToolEntry("sfx-preview", "범용 오디오", "SFX 미리보기",
                "음원을 빠르게 검색·청취하고 후보 바구니와 메모를 정리합니다.",
                "JC Tool/오디오/SFX 미리보기", "ToolIcon_SfxPreview.png")
        };

        private readonly Dictionary<Button, ToolEntry> launchButtons = new Dictionary<Button, ToolEntry>();
        private HashSet<string> availableMenus;
        private Label statusLabel;

        [MenuItem(MenuPath, false, 0)]
        [MenuItem("JC Tool/Tool Hub", false, 0)]
        public static void OpenWindow()
        {
            var window = GetWindow<OverburstToolHubWindow>();
            window.titleContent = new GUIContent("OVERBURST Tools");
            window.minSize = new Vector2(1000f, 660f);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("OVERBURST Tools");
            minSize = new Vector2(1000f, 660f);
        }

        private void OnFocus()
        {
            if (launchButtons.Count > 0) RefreshAvailability();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            launchButtons.Clear();
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null && !rootVisualElement.styleSheets.Contains(style)) rootVisualElement.styleSheets.Add(style);
            rootVisualElement.name = "overburst-tool-hub";
            rootVisualElement.AddToClassList("hub-root");
            availableMenus = DiscoverMenuPaths();

            rootVisualElement.Add(BuildHeader());
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "tool-hub-scroll" };
            scroll.AddToClassList("hub-scroll");
            scroll.contentContainer.AddToClassList("hub-scroll-content");
            scroll.Add(BuildMainCard());
            scroll.Add(BuildSection("핵심 도구", "core-tool-grid", CoreTools));
            var general = BuildSection("범용 도구", "general-tool-grid", GeneralTools);
            general.AddToClassList("hub-general-section");
            scroll.Add(general);
            rootVisualElement.Add(scroll);

            var statusBar = new VisualElement();
            statusBar.AddToClassList("hub-status-bar");
            statusLabel = new Label("도구를 선택하면 해당 Editor 창이 열립니다.") { name = "tool-hub-status" };
            statusLabel.AddToClassList("hub-status");
            statusBar.Add(statusLabel);
            rootVisualElement.Add(statusBar);
        }

        private static VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("hub-header");
            var copy = new VisualElement();
            copy.Add(Label("OVERBURST  /  EDITOR", "hub-eyebrow"));
            copy.Add(Label("OVERBURST Tools", "hub-title"));
            copy.Add(Label("제작과 밸런스 작업에 필요한 핵심 도구를 한곳에 모았습니다.", "hub-subtitle"));
            header.Add(copy);
            header.Add(Label($"핵심  {CoreTools.Length + 1}   ·   범용  {GeneralTools.Length}", "hub-count"));
            return header;
        }

        private VisualElement BuildMainCard()
        {
            var card = new VisualElement { name = "combo-maker-card" };
            card.AddToClassList("hub-main-card");
            var iconArea = new VisualElement();
            iconArea.AddToClassList("hub-main-icon-area");
            iconArea.Add(BuildIcon(ComboMaker, true));
            card.Add(iconArea);

            var copy = new VisualElement();
            copy.AddToClassList("hub-main-content");
            copy.Add(Label(ComboMaker.Category, "hub-card-category"));
            copy.Add(Label(ComboMaker.Title, "hub-main-title"));
            copy.Add(Label(ComboMaker.Description, "hub-main-description"));
            var tags = new VisualElement();
            tags.AddToClassList("hub-tags");
            foreach (var text in new[] { "모션·타격 판정", "공통 콤보", "원소 연출", "실시간 미리보기" })
                tags.Add(Label(text, "hub-tag"));
            copy.Add(tags);
            card.Add(copy);

            var action = new VisualElement();
            action.AddToClassList("hub-main-action");
            var open = MakeLaunchButton(ComboMaker);
            open.text = "콤보 메이커 열기  ›";
            open.AddToClassList("hub-main-button");
            action.Add(open);
            card.Add(action);
            return card;
        }

        private VisualElement BuildSection(string title, string gridName, IEnumerable<ToolEntry> tools)
        {
            var section = new VisualElement();
            section.AddToClassList("hub-section");
            var heading = new VisualElement();
            heading.AddToClassList("hub-section-heading");
            heading.Add(Label(title, "hub-section-title"));
            var line = new VisualElement();
            line.AddToClassList("hub-section-line");
            heading.Add(line);
            section.Add(heading);
            var grid = new VisualElement { name = gridName };
            grid.AddToClassList("hub-grid");
            foreach (var tool in tools) grid.Add(BuildToolCard(tool));
            section.Add(grid);
            return section;
        }

        private Button BuildToolCard(ToolEntry tool)
        {
            var card = MakeLaunchButton(tool);
            card.text = string.Empty;
            card.AddToClassList("hub-tool-card");
            var iconArea = new VisualElement();
            iconArea.AddToClassList("hub-card-icon-area");
            iconArea.Add(BuildIcon(tool, false));
            card.Add(iconArea);
            var copy = new VisualElement();
            copy.AddToClassList("hub-tool-copy");
            copy.Add(Label(tool.Category, "hub-card-category"));
            copy.Add(Label(tool.Title, "hub-tool-title"));
            copy.Add(Label(tool.Description, "hub-tool-description"));
            var launch = Label(availableMenus.Contains(tool.MenuPath) ? "열기  ›" : "메뉴 없음", "hub-tool-launch");
            launch.name = "launch-label";
            copy.Add(launch);
            card.Add(copy);
            return card;
        }

        private Button MakeLaunchButton(ToolEntry tool)
        {
            var button = new Button(() => OpenTool(tool)) { name = "open-" + tool.Id, userData = tool.MenuPath, tooltip = tool.MenuPath };
            var available = availableMenus.Contains(tool.MenuPath);
            button.SetEnabled(available);
            button.EnableInClassList("hub-tool-card--missing", !available);
            launchButtons.Add(button, tool);
            return button;
        }

        private static VisualElement BuildIcon(ToolEntry tool, bool main)
        {
            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("hub-tool-icon");
            frame.AddToClassList(main ? "hub-main-tool-icon" : "hub-card-tool-icon");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconRoot + tool.IconFile);
            var icon = new Image { name = "icon-" + tool.Id, image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("hub-tool-icon-image");
            frame.Add(icon);
            return frame;
        }

        private void RefreshAvailability()
        {
            availableMenus = DiscoverMenuPaths();
            foreach (var pair in launchButtons)
            {
                var available = availableMenus.Contains(pair.Value.MenuPath);
                pair.Key.SetEnabled(available);
                pair.Key.EnableInClassList("hub-tool-card--missing", !available);
                var launch = pair.Key.Q<Label>("launch-label");
                if (launch != null) launch.text = available ? "열기  ›" : "메뉴 없음";
            }
        }

        private void OpenTool(ToolEntry tool)
        {
            var opened = EditorApplication.ExecuteMenuItem(tool.MenuPath);
            statusLabel.text = opened ? $"{tool.Title} 창을 열었습니다." : $"{tool.Title} 메뉴를 찾지 못했습니다. 스크립트 컴파일 상태를 확인하세요.";
            statusLabel.EnableInClassList("hub-status--error", !opened);
            if (!opened) RefreshAvailability();
        }

        private static Label Label(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        private static HashSet<string> DiscoverMenuPaths()
        {
            return new HashSet<string>(TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Where(item => !item.validate && !string.IsNullOrWhiteSpace(item.menuItem))
                .Select(item => item.menuItem.Trim()), StringComparer.Ordinal);
        }

        private sealed class ToolEntry
        {
            public ToolEntry(string id, string category, string title, string description, string menuPath, string iconFile)
            { Id = id; Category = category; Title = title; Description = description; MenuPath = menuPath; IconFile = iconFile; }
            public string Id { get; }
            public string Category { get; }
            public string Title { get; }
            public string Description { get; }
            public string MenuPath { get; }
            public string IconFile { get; }
        }
    }
}
