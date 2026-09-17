using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class DungeonSeedPreviewWindow : EditorWindow
{
    private const string WindowTitle = "DunGen Seed Map";
    private const string SeedPreferenceKey =
        "OVERBURST.DungeonSeedPreview.RequestedSeed";
    private const string DefaultSeedText = "20260726";
    private const string SmokeLogPath =
        "Logs/DungeonSeedPreviewSmoke.log";

    private static readonly GUIContent TotalMainPathCountLabel = new(
        "총길이",
        "Start, Exit, Bridge, Arena를 모두 포함한 최종 Main Path 타일 수입니다.");
    private static readonly GUIContent ArenaCountLabel = new(
        "Arena 개수",
        "최종 Main Path 안에 반드시 들어갈 Arena 타일 수입니다. "
        + "성공한 맵은 입력한 수와 정확히 일치합니다. "
        + "중간 경로 타일이므로 출입구가 2개 이상인 Arena 후보만 배치됩니다.");
    private static readonly GUIContent CandidateCountLabel = new(
        "품질 후보 수",
        "요청 Seed에서 결정적으로 파생한 맵 후보를 최대 몇 개까지 "
        + "검사할지 정합니다. 늘리면 합격 가능성이 높아지지만 느려집니다.");
    private static readonly GUIContent MinimumBranchLabel = new(
        "최소 Branch 타일",
        "Main Path 밖의 분기 타일이 최소 몇 개 있어야 합격할지 정합니다. "
        + "높일수록 곁길을 요구하며 너무 높으면 생성에 실패할 수 있습니다.");
    private static readonly GUIContent MaximumAspectLabel = new(
        "최대 평면 비율",
        "생성 맵의 XZ 평면에서 긴 변을 짧은 변으로 나눈 최대값입니다. "
        + "낮을수록 정방형에 가깝고 높을수록 길쭉한 맵을 허용합니다.");
    private static readonly GUIContent MaxAttemptLabel = new(
        "DunGen 실패 재시도",
        "후보 하나를 배치하다 막혔을 때 DunGen 내부에서 다시 시도하는 "
        + "상한입니다. 품질 후보 수와는 별개이며 맵 크기를 직접 늘리지 않습니다.");
    private static readonly GUIContent PaddingLabel = new(
        "타일 Padding",
        "서로 연결되지 않은 타일 경계 사이에 요구할 여유 거리입니다. "
        + "높이면 근접 배치를 줄이지만 생성 가능한 배치도 줄어듭니다.");
    private static readonly GUIContent OverlapLabel = new(
        "연결 Overlap 허용",
        "서로 연결된 타일 경계가 겹쳐도 되는 허용 오차입니다. "
        + "너무 높이면 지오메트리 겹침이 보일 수 있습니다.");
    private static readonly GUIContent OverhangLabel = new(
        "상하 겹침 금지",
        "연결되지 않은 타일이 위아래로 포개지는 배치를 거부합니다. "
        + "다층 던전에서는 켜면 생성 선택지가 크게 줄거나 실패할 수 있습니다.");
    private static readonly GUIContent FamilyMainWeightLabel = new(
        "Main",
        "Main Path에서 이 타일 계열 전체가 선택되는 가중치입니다.");
    private static readonly GUIContent FamilyBranchWeightLabel = new(
        "Branch",
        "Branch Path에서 이 타일 계열 전체가 선택되는 가중치입니다.");

    [SerializeField] private string requestedSeedText = DefaultSeedText;
    [SerializeField] private bool detailSettingsExpanded = true;
    [SerializeField] private bool detailHelpExpanded = true;
    [SerializeField] private int totalMainPathTileCount = 24;
    [SerializeField] private int arenaCount = 4;
    [SerializeField] private int layoutCandidateCount = 6;
    [SerializeField] private int minimumBranchTileCount = 1;
    [SerializeField] private float maximumPlanarAspectRatio = 2.35f;
    [SerializeField] private int maxAttemptCount = 100;
    [SerializeField] private float padding;
    [SerializeField] private float overlapThreshold = 0.01f;
    [SerializeField] private bool disallowOverhangs;
    [SerializeField] private bool settingsInitialized;
    [SerializeField] private bool tileFamilyWeightsExpanded;
    [SerializeField] private bool topologyRepairCandidatesExpanded;
    [SerializeField] private Vector2 scrollPosition;

    private List<DungeonTileFamilyWeightState> tileFamilyWeights;
    private List<DungeonTopologyRepairCandidateState>
        topologyRepairCandidates;
    private DungeonSeedPreviewResult lastResult;
    private string statusMessage =
        "Seed를 입력하고 생성 버튼을 누르세요.";
    private MessageType statusType = MessageType.Info;
    private bool hasResult;

    [MenuItem(
        "OVERBURST/Tools/World/Dungeon/Open Seed Map Generator")]
    public static void OpenWindow()
    {
        DungeonSeedPreviewWindow window =
            GetWindow<DungeonSeedPreviewWindow>(WindowTitle);
        window.minSize = new Vector2(460f, 560f);
        window.Show();
    }

    public static void RunSmokeFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        int exitCode = 0;
        string report;
        try
        {
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            bool rejectedOutsideDungeonRunScene = false;
            try
            {
                DungeonSeedPreviewGenerator.Generate(27100);
            }
            catch (InvalidOperationException exception)
            {
                rejectedOutsideDungeonRunScene =
                    exception.Message.Contains("DungeonRunScene");
            }
            Require(
                rejectedOutsideDungeonRunScene,
                "DungeonRunScene 외부 생성 차단 실패");
            EditorSceneManager.OpenScene(
                DungeonRunSceneAuthoringBuilder.ScenePath,
                OpenSceneMode.Single);
            DungeonSeedPreviewResult result =
                DungeonSeedPreviewGenerator.Generate(27101);
            Require(result.TileCount >= 2, "생성 타일 수 부족");
            Require(
                result.MainPathCount == result.TotalMainPathTileCount,
                "Definition 총길이가 정확히 적용되지 않음");
            Require(
                result.PlacedArenaCount == result.ArenaCount,
                "Definition Arena 개수가 정확히 적용되지 않음");
            Require(
                result.OpenDoorwayCountBeforeEndCaps
                    - result.PlacedEndCapCount
                    == result.OpenDoorwayCountAfterEndCaps,
                "최종 오픈 Doorway EndCap 패스 수치 불일치");
            Require(
                result.OpenDoorwayCountAfterEndCaps
                    - result.RemovedOpenDoorwayCount
                    == result.OpenDoorwayCountAfterTopologyRepair,
                "EndCap 이후 형태 보정 패스 수치 불일치");
            DungeonSeedPreviewResult replay =
                DungeonSeedPreviewGenerator.Generate(
                    result.ChosenSeed);
            Require(
                replay.ChosenSeed == result.ChosenSeed,
                "복사한 채택 Seed가 동일한 Seed로 재현되지 않음");
            Require(
                replay.LayoutAttempt == 1
                && replay.TileCount == result.TileCount
                && replay.MainPathCount == result.MainPathCount
                && replay.BranchTileCount == result.BranchTileCount
                && replay.PlacedEndCapCount
                    == result.PlacedEndCapCount
                && replay.FinalEndCapCount
                    == result.FinalEndCapCount
                && replay.OpenDoorwayCountBeforeEndCaps
                    == result.OpenDoorwayCountBeforeEndCaps
                && replay.OpenDoorwayCountAfterEndCaps
                    == result.OpenDoorwayCountAfterEndCaps
                && replay.ReplacedTopologyTileCount
                    == result.ReplacedTopologyTileCount
                && replay.ReplacedWithEndCapCount
                    == result.ReplacedWithEndCapCount
                && replay.ReplacedShapeTileCount
                    == result.ReplacedShapeTileCount
                && replay.OpenDoorwayCountAfterTopologyRepair
                    == result.OpenDoorwayCountAfterTopologyRepair,
                "복사한 채택 Seed의 레이아웃 구성이 달라짐");
            DungeonSeedPreviewSettings customSettings = new(
                18,
                4,
                8,
                0,
                10f,
                120,
                0.02f,
                0.02f,
                false);
            DungeonSeedPreviewResult customResult =
                DungeonSeedPreviewGenerator.Generate(
                    27102,
                    customSettings);
            Require(
                customResult.MainPathCount == 18
                && customResult.TotalMainPathTileCount == 18
                && customResult.ArenaCount == 4
                && customResult.PlacedArenaCount == 4
                && customResult.LayoutCandidateCount == 8
                && customResult.MinimumBranchTileCount == 0
                && Mathf.Approximately(
                    customResult.MaximumPlanarAspectRatio,
                    10f)
                && customResult.MaxAttemptCount == 120
                && Mathf.Approximately(customResult.Padding, 0.02f)
                && Mathf.Approximately(
                    customResult.OverlapThreshold,
                    0.02f),
                "커스텀 생성 세부값이 적용되지 않음");
            report =
                "[DungeonSeedPreviewWindow] PASS\n"
                + $"RequestedSeed={result.RequestedSeed}\n"
                + $"ChosenSeed={result.ChosenSeed}\n"
                + $"LayoutAttempt={result.LayoutAttempt}\n"
                + $"Tiles={result.TileCount}\n"
                + $"MainPath={result.MainPathCount}\n"
                + $"Arena={result.PlacedArenaCount}\n"
                + $"EndCap={result.PlacedEndCapCount}\n"
                + "EndCapReplacement="
                + $"{result.ReplacedWithEndCapCount}\n"
                + "OpenDoorways="
                + $"{result.OpenDoorwayCountBeforeEndCaps}"
                + $"->{result.OpenDoorwayCountAfterEndCaps}"
                + $"->{result.OpenDoorwayCountAfterTopologyRepair}\n"
                + "TopologyRepair="
                + $"{result.ReplacedTopologyTileCount}, "
                + $"Removed={result.RemovedOpenDoorwayCount}\n"
                + $"Branches={result.BranchTileCount}\n"
                + $"ReplayAttempt={replay.LayoutAttempt}\n"
                + $"ReplayChosenSeed={replay.ChosenSeed}\n"
                + "ReplayLayoutMatch=1\n"
                + $"CustomChosenSeed={customResult.ChosenSeed}\n"
                + $"CustomMainPath={customResult.MainPathCount}\n"
                + $"CustomArena={customResult.PlacedArenaCount}\n"
                + "DungeonRunSceneScopeGuard=1\n"
                + "CustomSettingsApplied=1";
            Debug.Log(report);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            report = exception.ToString();
            Debug.LogException(exception);
        }
        finally
        {
            DungeonSeedPreviewGenerator.Clear();
        }

        File.WriteAllText(
            SmokeLogPath,
            report,
            new UTF8Encoding(false));
        EditorApplication.Exit(exitCode);
    }

    private void OnEnable()
    {
        requestedSeedText = EditorPrefs.GetString(
            SeedPreferenceKey,
            DefaultSeedText);
        if (!settingsInitialized)
            LoadDefinitionDefaults();
        ReloadTileFamilyWeights(false);
        ReloadTopologyRepairCandidates(false);
        EditorSceneManager.activeSceneChangedInEditMode +=
            HandleActiveSceneChangedInEditMode;
        if (!DungeonSeedPreviewGenerator.IsDungeonRunScene(
                SceneManager.GetActiveScene()))
        {
            DungeonSeedPreviewGenerator.Clear();
            statusMessage =
                "DungeonRunScene에서만 미리보기 맵을 생성할 수 있습니다.";
            statusType = MessageType.Warning;
        }
    }

    private void OnDisable()
    {
        EditorPrefs.SetString(
            SeedPreferenceKey,
            requestedSeedText);
        EditorSceneManager.activeSceneChangedInEditMode -=
            HandleActiveSceneChangedInEditMode;
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(
            "DungeonRun Seed 맵 생성",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "현재 DungeonRunDefinition을 기준으로 저장되지 않는 "
            + "에디터 미리보기 맵을 생성합니다. 세부값 변경은 "
            + "미리보기에만 적용되며 원본 SO·Flow·씬은 수정하지 않습니다.",
            MessageType.Info);
        bool isDungeonRunScene =
            DungeonSeedPreviewGenerator.IsDungeonRunScene(
                SceneManager.GetActiveScene());
        if (!isDungeonRunScene)
        {
            EditorGUILayout.HelpBox(
                "현재 활성 씬에서는 생성하지 않습니다. "
                + "Assets/ProjectOverburst/00_Scenes/DungeonRunScene.unity를 "
                + "연 뒤 사용하세요.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(4f);
        DrawSeedInput();
        DrawDetailSettings();
        DrawTileFamilyWeights();
        DrawTopologyRepairCandidates();

        EditorGUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(
                   EditorApplication.isPlayingOrWillChangePlaymode
                   || !isDungeonRunScene))
        {
            if (GUILayout.Button(
                    "입력 Seed로 맵 생성",
                    GUILayout.Height(34f)))
            {
                GeneratePreview();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("새 랜덤 Seed 생성"))
            {
                requestedSeedText =
                    (Guid.NewGuid().GetHashCode() & int.MaxValue)
                    .ToString();
                GeneratePreview();
            }
            EditorGUILayout.EndHorizontal();
        }
        using (new EditorGUI.DisabledScope(
                   EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("미리보기 제거"))
                ClearPreview();
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox(
                "PlayMode 중에는 에디터 미리보기를 생성하지 않습니다.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(statusMessage, statusType);
        if (hasResult)
            DrawResult();
        EditorGUILayout.EndScrollView();
    }

    private void DrawDetailSettings()
    {
        EditorGUILayout.Space(6f);
        detailSettingsExpanded = EditorGUILayout.Foldout(
            detailSettingsExpanded,
            "생성 세부값",
            true);
        if (!detailSettingsExpanded)
            return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        totalMainPathTileCount = EditorGUILayout.IntField(
            TotalMainPathCountLabel,
            totalMainPathTileCount);
        arenaCount = Mathf.Clamp(
            EditorGUILayout.IntField(
                ArenaCountLabel,
                arenaCount),
            DungeonRunParameters.MinimumArenaCount,
            DungeonRunParameters.MaximumArenaCount);
        totalMainPathTileCount = Mathf.Clamp(
            totalMainPathTileCount,
            Mathf.Max(
                DungeonRunParameters.MinimumTotalMainPathTileCount,
                arenaCount + 2),
            DungeonRunParameters.MaximumTotalMainPathTileCount);
        EditorGUILayout.LabelField(
            "Main Path 구성",
            $"Start 1 + Exit 1 + Arena {arenaCount} + "
            + $"Bridge {totalMainPathTileCount - arenaCount - 2}");

        layoutCandidateCount = Mathf.Clamp(
            EditorGUILayout.IntField(
                CandidateCountLabel,
                layoutCandidateCount),
            1,
            30);
        minimumBranchTileCount = Mathf.Clamp(
            EditorGUILayout.IntField(
                MinimumBranchLabel,
                minimumBranchTileCount),
            0,
            50);
        maximumPlanarAspectRatio = Mathf.Clamp(
            EditorGUILayout.FloatField(
                MaximumAspectLabel,
                maximumPlanarAspectRatio),
            1f,
            10f);
        maxAttemptCount = Mathf.Clamp(
            EditorGUILayout.IntField(
                MaxAttemptLabel,
                maxAttemptCount),
            1,
            500);
        padding = Mathf.Clamp(
            EditorGUILayout.FloatField(
                PaddingLabel,
                padding),
            0f,
            5f);
        overlapThreshold = Mathf.Clamp(
            EditorGUILayout.FloatField(
                OverlapLabel,
                overlapThreshold),
            0.0001f,
            1f);
        disallowOverhangs = EditorGUILayout.Toggle(
            OverhangLabel,
            disallowOverhangs);

        DrawDetailHelp();
        EditorGUILayout.HelpBox(
            "같은 맵을 재현하려면 채택 Seed뿐 아니라 이 세부값도 "
            + "같아야 합니다.",
            MessageType.None);
        if (GUILayout.Button("Definition 기본값 불러오기"))
            LoadDefinitionDefaults();
        EditorGUILayout.EndVertical();
    }

    private void DrawDetailHelp()
    {
        EditorGUILayout.Space(4f);
        detailHelpExpanded = EditorGUILayout.Foldout(
            detailHelpExpanded,
            "세부값 도움말",
            true);
        if (!detailHelpExpanded)
            return;

        EditorGUILayout.HelpBox(
            "총길이 / Arena 개수\n"
            + "• 총길이는 Start, Exit, Bridge, Arena를 모두 포함한 "
            + "최종 Main Path 타일 수입니다.\n"
            + "• Arena 개수는 그 총길이 안에 반드시 포함할 정확한 수입니다. "
            + "4를 입력하면 성공한 맵에는 Arena가 정확히 4개 들어갑니다.\n"
            + "• Arena는 Main Path 중간에 들어가므로 출입구가 2개 이상인 "
            + "후보만 실제 선택됩니다. 출입구 1개 후보는 목록에 경고로 "
            + "표시됩니다.\n"
            + "• Start와 Exit가 각각 1개 필요하므로 총길이는 최소 "
            + "Arena 개수 + 2입니다.\n"
            + "• Branch 타일은 곁길이므로 총길이에 포함되지 않습니다.\n"
            + "• 난이도별 길이 배율은 현재 임시 비활성화 상태입니다.",
            MessageType.None);
        EditorGUILayout.HelpBox(
            "품질 게이트\n"
            + "• 품질 후보 수: 요청 Seed에서 파생한 후보를 순서대로 "
            + "검사하는 최대 개수입니다. 첫 합격 후보가 실제 채택 Seed가 됩니다.\n"
            + "• 최소 Branch 타일: Main Path 밖의 곁길 타일 최소 개수입니다.\n"
            + "• 최대 평면 비율: XZ 경계의 긴 변 ÷ 짧은 변입니다. "
            + "1에 가까울수록 정방형이며 현재 기본값 2.35는 "
            + "2.35배보다 길쭉한 결과를 탈락시킵니다.",
            MessageType.None);
        EditorGUILayout.HelpBox(
            "DunGen 배치\n"
            + "• 실패 재시도: 후보 하나의 내부 배치 실패 재시도 상한입니다. "
            + "품질 후보 수와 다른 값입니다.\n"
            + "• Padding: 연결되지 않은 타일 사이의 여유 거리입니다.\n"
            + "• Overlap 허용: 연결 타일 경계의 겹침 허용 오차입니다.\n"
            + "• 상하 겹침 금지: 위아래 포개짐을 거부합니다. "
            + "이 프로젝트는 다층 던전이라 기본 OFF를 권장합니다.",
            MessageType.None);
        EditorGUILayout.HelpBox(
            "EndCap 이후 형태 보정\n"
            + "• 일반 생성의 _A, _B 후보 선택은 그대로 유지합니다.\n"
            + "• EndCap 단일 패스가 끝난 뒤에도 열린 Doorway가 남은 "
            + "Bridge·Arena만 별도 형태 후보를 검사합니다.\n"
            + "• 이미 연결된 Doorway의 위치·높이·회전·Socket을 모두 "
            + "보존하고, 불필요한 Doorway가 없는 후보만 교체됩니다.\n"
            + "• 현재 Bridge_21_L1/L2는 편집 전 원본 복사 상태이므로 "
            + "Doorway가 3개인 동안에는 교체 후보로 채택되지 않습니다.",
            MessageType.None);
    }

    private void DrawTileFamilyWeights()
    {
        EditorGUILayout.Space(6f);
        tileFamilyWeightsExpanded = EditorGUILayout.Foldout(
            tileFamilyWeightsExpanded,
            "타일 계열 가중치",
            true);
        if (!tileFamilyWeightsExpanded)
            return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox(
            "_A 같은 변형은 선택 사항이며 없어도 됩니다. 기본본만 있으면 "
            + "그 기본본이 계열 가중치 전부를 사용합니다. 파일명 끝의 "
            + "_A, _B, _C, _AA 같은 대문자 접미사 Prefab이 실제로 있을 때만 "
            + "개수 제한 없이 기본본과 같은 계열 후보로 자동 인식합니다.\n"
            + "여기 입력하는 값은 계열 전체 가중치입니다. 기본본을 포함한 "
            + "현재 실제 후보 수가 N이면 각 후보에 1/N 비율로 균등 저장되어, "
            + "변형을 추가해도 계열 선택 확률이 N배로 커지지 않습니다.\n"
            + "EndCap은 Main Path 총길이·Arena 개수·일반 회전 규칙에서 "
            + "완전히 제외되는 예외 타일입니다. 던전 본체와 기존 Doorway "
            + "연결을 모두 만든 뒤 남은 모든 오픈 Doorway를 한 번만 "
            + "전수 검사해, 실제로 들어가는 곳에 EndCap을 추가합니다. "
            + "다른 타일과 실제로 겹치는 위치만 생략되고 기존 "
            + "Doorway 차단물이 남습니다. "
            + "보상 상자 같은 내용은 추후 별도 이벤트가 확률적으로 배치합니다.\n"
            + "이 항목은 미리보기 전용이 아니라 Project TileSet에 저장되어 "
            + "DungeonRun 런타임에도 적용됩니다.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("목록 새로고침"))
            ReloadTileFamilyWeights(true);
        using (new EditorGUI.DisabledScope(
                   EditorApplication.isPlayingOrWillChangePlaymode
                   || tileFamilyWeights == null))
        {
            if (GUILayout.Button("가중치 적용 및 TileSet 동기화"))
                ApplyTileFamilyWeights();
        }
        EditorGUILayout.EndHorizontal();

        if (tileFamilyWeights == null)
        {
            EditorGUILayout.HelpBox(
                "타일 계열 목록을 불러오지 못했습니다.",
                MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        DrawTileFamilyWeightGroup(
            "Bridge",
            DungeonTileRole.Bridge);
        DrawTileFamilyWeightGroup(
            "Arena",
            DungeonTileRole.Arena);
        DrawTileFamilyWeightGroup(
            "EndCap",
            DungeonTileRole.EndCap);
        DrawTileFamilyWeightGroup(
            "Start",
            DungeonTileRole.Start);
        DrawTileFamilyWeightGroup(
            "Exit",
            DungeonTileRole.Exit);
        EditorGUILayout.EndVertical();
    }

    private void DrawTileFamilyWeightGroup(
        string title,
        DungeonTileRole role)
    {
        List<DungeonTileFamilyWeightState> rows =
            tileFamilyWeights
                .Where(row => row.Role == role)
                .ToList();
        if (rows.Count == 0)
            return;

        EditorGUILayout.Space(4f);
        int variantCount = rows.Sum(row => row.VariantCount);
        EditorGUILayout.LabelField(
            $"{title} (계열 {rows.Count} / 후보 {variantCount})",
            EditorStyles.boldLabel);
        if (role == DungeonTileRole.EndCap)
        {
            EditorGUILayout.HelpBox(
                "EndCap은 최종 오픈 Doorway 마감 전용입니다. Main 가중치는 "
                + "사용하지 않고 Branch 가중치만 최종 후보 선택에 적용됩니다. "
                + "총길이·Arena 개수에 포함되지 않으며 이 타일만 회전 "
                + "예외가 적용됩니다. 물리 충돌 검사는 유지됩니다.",
                MessageType.None);
        }
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("계열", GUILayout.MinWidth(190f));
        EditorGUILayout.LabelField(
            "후보",
            GUILayout.Width(38f));
        EditorGUILayout.LabelField(
            FamilyMainWeightLabel,
            GUILayout.Width(70f));
        EditorGUILayout.LabelField(
            FamilyBranchWeightLabel,
            GUILayout.Width(70f));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < rows.Count; i++)
        {
            DungeonTileFamilyWeightState row = rows[i];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                row.FamilyName,
                GUILayout.MinWidth(190f));
            EditorGUILayout.LabelField(
                row.VariantCount.ToString(),
                GUILayout.Width(38f));
            using (new EditorGUI.DisabledScope(
                       role == DungeonTileRole.EndCap))
            {
                row.MainPathWeight = Mathf.Max(
                    0f,
                    EditorGUILayout.FloatField(
                        row.MainPathWeight,
                        GUILayout.Width(70f)));
            }
            row.BranchPathWeight = Mathf.Max(
                0f,
                EditorGUILayout.FloatField(
                    row.BranchPathWeight,
                    GUILayout.Width(70f)));
            EditorGUILayout.EndHorizontal();

            if (!row.SupportsRequiredMainPathArena)
            {
                EditorGUILayout.HelpBox(
                    $"{row.FamilyName}: 출입구가 "
                    + $"{row.MinimumDoorwayCount}개라 필수 Main Path "
                    + "Arena 후보로는 배치되지 않습니다. 이 Arena를 "
                    + "주 경로에 쓰려면 연결 가능한 출입구가 최소 2개 "
                    + "필요합니다.",
                    MessageType.Warning);
            }

            if (row.VariantCount > 1)
            {
                EditorGUILayout.LabelField(
                    "  "
                    + string.Join(
                        " / ",
                        row.Variants.Select(variant =>
                            variant.name.Replace(
                                "PF_DungeonTile_",
                                string.Empty))),
                    EditorStyles.miniLabel);
            }
        }
    }

    private void ReloadTileFamilyWeights(bool showStatus)
    {
        try
        {
            tileFamilyWeights =
                DungeonTileFamilyWeightUtility
                    .LoadFamilyWeights()
                    .ToList();
            if (showStatus)
            {
                statusMessage =
                    $"타일 계열 {tileFamilyWeights.Count}개를 "
                    + "새로 불러왔습니다.";
                statusType = MessageType.Info;
            }
        }
        catch (Exception exception)
        {
            tileFamilyWeights = null;
            if (showStatus)
            {
                statusMessage = exception.Message;
                statusType = MessageType.Error;
            }
        }
    }

    private void DrawTopologyRepairCandidates()
    {
        EditorGUILayout.Space(6f);
        topologyRepairCandidatesExpanded = EditorGUILayout.Foldout(
            topologyRepairCandidatesExpanded,
            "EndCap 이후 형태 보정 후보",
            true);
        if (!topologyRepairCandidatesExpanded)
            return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox(
            "이 목록은 일반 생성의 ABCD 외형 후보와 별개입니다. "
            + "연결 1개만 남으면 현재 공용 EndCap으로 교체를 시도하고, "
            + "연결 2개는 방향에 따라 같은 번호 I/L 후보, 연결 3개는 "
            + "같은 번호 T 후보를 숫자 순서대로 검사합니다. "
            + "E 후보는 만들지 않습니다.",
            MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("목록 새로고침"))
            ReloadTopologyRepairCandidates(true);
        using (new EditorGUI.DisabledScope(
                   EditorApplication.isPlayingOrWillChangePlaymode
                   || topologyRepairCandidates == null))
        {
            if (GUILayout.Button("후보 설정 적용"))
                ApplyTopologyRepairCandidateSettings();
        }
        EditorGUILayout.EndHorizontal();

        if (topologyRepairCandidates == null)
        {
            EditorGUILayout.HelpBox(
                "형태 보정 후보 목록을 불러오지 못했습니다.",
                MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("원본 계열", GUILayout.MinWidth(180f));
        EditorGUILayout.LabelField("형태", GUILayout.Width(50f));
        EditorGUILayout.LabelField("순서", GUILayout.Width(38f));
        EditorGUILayout.LabelField("후보", GUILayout.Width(38f));
        EditorGUILayout.LabelField("90° 회전", GUILayout.Width(70f));
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < topologyRepairCandidates.Count; i++)
        {
            DungeonTopologyRepairCandidateState row =
                topologyRepairCandidates[i];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                row.SourceFamilyName.Replace(
                    "PF_DungeonTile_",
                    string.Empty),
                GUILayout.MinWidth(180f));
            EditorGUILayout.LabelField(
                row.Shape.ToString(),
                GUILayout.Width(50f));
            EditorGUILayout.LabelField(
                row.Priority.ToString(),
                GUILayout.Width(38f));
            EditorGUILayout.LabelField(
                row.VariantCount.ToString(),
                GUILayout.Width(38f));
            row.AllowQuarterTurns = EditorGUILayout.Toggle(
                row.AllowQuarterTurns,
                GUILayout.Width(70f));
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void ReloadTopologyRepairCandidates(bool showStatus)
    {
        try
        {
            topologyRepairCandidates =
                DungeonTopologyRepairAuthoringUtility
                    .LoadCandidateStates()
                    .ToList();
            if (showStatus)
            {
                statusMessage =
                    $"형태 보정 후보 그룹 "
                    + $"{topologyRepairCandidates.Count}개를 "
                    + "새로 불러왔습니다.";
                statusType = MessageType.Info;
            }
        }
        catch (Exception exception)
        {
            topologyRepairCandidates = null;
            if (showStatus)
            {
                statusMessage = exception.Message;
                statusType = MessageType.Error;
            }
        }
    }

    private void ApplyTopologyRepairCandidateSettings()
    {
        try
        {
            statusMessage =
                DungeonTopologyRepairAuthoringUtility
                    .ApplyCandidateStates(
                        topologyRepairCandidates);
            statusType = MessageType.Info;
            ReloadTopologyRepairCandidates(false);
        }
        catch (Exception exception)
        {
            statusMessage = exception.Message;
            statusType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    private void ApplyTileFamilyWeights()
    {
        try
        {
            statusMessage =
                DungeonTileFamilyWeightUtility.ApplyFamilyWeights(
                    tileFamilyWeights,
                    true);
            statusType = MessageType.Info;
            ReloadTileFamilyWeights(false);
        }
        catch (Exception exception)
        {
            statusMessage = exception.Message;
            statusType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    private void DrawSeedInput()
    {
        EditorGUILayout.BeginHorizontal();
        requestedSeedText = EditorGUILayout.TextField(
            "요청 Seed",
            requestedSeedText);
        if (GUILayout.Button("붙여넣기", GUILayout.Width(72f)))
            PasteSeed();
        if (GUILayout.Button("복사", GUILayout.Width(52f)))
            CopyText(requestedSeedText, "요청 Seed를 복사했습니다.");
        EditorGUILayout.EndHorizontal();
    }

    private void DrawResult()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            "생성 결과",
            EditorStyles.boldLabel);
        DrawSeedResultRow(
            "요청 Seed",
            lastResult.RequestedSeed,
            false);
        DrawSeedResultRow(
            "실제 채택 Seed",
            lastResult.ChosenSeed,
            true);
        EditorGUILayout.LabelField(
            "후보",
            $"{lastResult.LayoutAttempt}/"
            + $"{lastResult.LayoutCandidateCount}");
        EditorGUILayout.LabelField(
            "타일",
            $"전체 {lastResult.TileCount}, "
            + $"Main Path {lastResult.MainPathCount}, "
            + $"Branch {lastResult.BranchTileCount}");
        EditorGUILayout.LabelField(
            "직접 지정",
            $"총길이 {lastResult.MainPathCount}/"
            + $"{lastResult.TotalMainPathTileCount}, "
            + $"Arena {lastResult.PlacedArenaCount}/"
            + $"{lastResult.ArenaCount}");
        EditorGUILayout.LabelField(
            "추가 EndCap",
            $"{lastResult.PlacedEndCapCount} "
            + "(Main Path 총길이·Arena 개수에서 제외)");
        EditorGUILayout.LabelField(
            "오픈 Doorway",
            $"{lastResult.OpenDoorwayCountBeforeEndCaps} → "
            + $"{lastResult.OpenDoorwayCountAfterEndCaps} → "
            + $"{lastResult.OpenDoorwayCountAfterTopologyRepair}");
        EditorGUILayout.LabelField(
            "형태 보정",
            $"교체 {lastResult.ReplacedTopologyTileCount}, "
            + $"EndCap {lastResult.ReplacedWithEndCapCount}, "
            + $"I/L/T {lastResult.ReplacedShapeTileCount}, "
            + $"제거 Doorway {lastResult.RemovedOpenDoorwayCount}");
        EditorGUILayout.LabelField(
            "Branch / 평면 비율",
            $"{lastResult.BranchTileCount}"
            + $"/{lastResult.MinimumBranchTileCount} 이상, "
            + $"{lastResult.PlanarAspectRatio:F2}"
            + $"/{lastResult.MaximumPlanarAspectRatio:F2} 이하");
        EditorGUILayout.LabelField(
            "DunGen 세부값",
            $"재시도 {lastResult.MaxAttemptCount}, "
            + $"Padding {lastResult.Padding:F3}, "
            + $"Overlap {lastResult.OverlapThreshold:F3}, "
            + $"상하 겹침 금지 "
            + $"{(lastResult.DisallowOverhangs ? "ON" : "OFF")}");

        if (GUILayout.Button("채택 Seed를 입력칸에 사용"))
        {
            requestedSeedText = lastResult.ChosenSeed.ToString();
            EditorPrefs.SetString(
                SeedPreferenceKey,
                requestedSeedText);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawSeedResultRow(
        string label,
        int seed,
        bool emphasize)
    {
        EditorGUILayout.BeginHorizontal();
        GUIStyle style = emphasize
            ? EditorStyles.boldLabel
            : EditorStyles.label;
        EditorGUILayout.LabelField(
            label,
            seed.ToString(),
            style);
        if (GUILayout.Button("복사", GUILayout.Width(52f)))
            CopyText(seed.ToString(), $"{label}를 복사했습니다.");
        EditorGUILayout.EndHorizontal();
    }

    private void GeneratePreview()
    {
        if (!int.TryParse(
                requestedSeedText.Trim(),
                out int requestedSeed))
        {
            statusMessage =
                "Seed는 32비트 정수 형식으로 입력해야 합니다.";
            statusType = MessageType.Error;
            return;
        }

        try
        {
            EditorPrefs.SetString(
                SeedPreferenceKey,
                requestedSeedText);
            EditorUtility.DisplayProgressBar(
                WindowTitle,
                "DungeonRun 맵 생성 중...",
                0.5f);
            lastResult = DungeonSeedPreviewGenerator.Generate(
                requestedSeed,
                CreateCurrentSettings());
            hasResult = true;
            statusMessage =
                $"생성 완료: 채택 Seed {lastResult.ChosenSeed}";
            statusType = MessageType.Info;
            FocusPreview();
        }
        catch (Exception exception)
        {
            hasResult = false;
            statusMessage = exception.Message;
            statusType = MessageType.Error;
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    private DungeonSeedPreviewSettings CreateCurrentSettings()
    {
        return new DungeonSeedPreviewSettings(
            totalMainPathTileCount,
            arenaCount,
            layoutCandidateCount,
            minimumBranchTileCount,
            maximumPlanarAspectRatio,
            maxAttemptCount,
            padding,
            overlapThreshold,
            disallowOverhangs);
    }

    private void LoadDefinitionDefaults()
    {
        try
        {
            DungeonSeedPreviewSettings settings =
                DungeonSeedPreviewGenerator.CreateDefinitionSettings();
            totalMainPathTileCount =
                settings.TotalMainPathTileCount;
            arenaCount = settings.ArenaCount;
            layoutCandidateCount = settings.LayoutCandidateCount;
            minimumBranchTileCount =
                settings.MinimumBranchTileCount;
            maximumPlanarAspectRatio =
                settings.MaximumPlanarAspectRatio;
            maxAttemptCount = settings.MaxAttemptCount;
            padding = settings.Padding;
            overlapThreshold = settings.OverlapThreshold;
            disallowOverhangs = settings.DisallowOverhangs;
            settingsInitialized = true;
        }
        catch (Exception exception)
        {
            settingsInitialized = false;
            statusMessage = exception.Message;
            statusType = MessageType.Error;
        }
    }

    private void ClearPreview()
    {
        DungeonSeedPreviewGenerator.Clear();
        hasResult = false;
        statusMessage = "에디터 미리보기를 제거했습니다.";
        statusType = MessageType.Info;
        SceneView.RepaintAll();
    }

    private void PasteSeed()
    {
        string clipboard =
            EditorGUIUtility.systemCopyBuffer?.Trim() ?? string.Empty;
        if (!int.TryParse(clipboard, out _))
        {
            ShowNotification(
                new GUIContent("클립보드에 정수 Seed가 없습니다."));
            return;
        }

        requestedSeedText = clipboard;
        EditorPrefs.SetString(
            SeedPreferenceKey,
            requestedSeedText);
    }

    private void CopyText(string value, string notification)
    {
        EditorGUIUtility.systemCopyBuffer = value;
        ShowNotification(new GUIContent(notification));
    }

    private void FocusPreview()
    {
        GameObject root = DungeonSeedPreviewGenerator.PreviewRoot;
        if (root == null)
            return;

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        EditorApplication.delayCall += () =>
        {
            if (root != null && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        };
    }

    private void HandleActiveSceneChangedInEditMode(
        Scene previousScene,
        Scene newScene)
    {
        hasResult = false;
        if (DungeonSeedPreviewGenerator.IsDungeonRunScene(newScene))
        {
            statusMessage =
                "Seed를 입력하고 생성 버튼을 누르세요.";
            statusType = MessageType.Info;
        }
        else
        {
            statusMessage =
                "DungeonRunScene에서만 미리보기 맵을 생성할 수 있습니다.";
            statusType = MessageType.Warning;
        }
        Repaint();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

[InitializeOnLoad]
public static class DungeonSeedPreviewGenerator
{
    private const string PreviewRootName =
        "__ProjectVTP_DungeonSeedPreview__";

    private static DungeonGenerator generator;
    private static DungeonArenaTileInjection arenaTileInjection;
    private static DungeonRunGenerationFlow generationFlow;
    private static GameObject previewRoot;

    static DungeonSeedPreviewGenerator()
    {
        EditorSceneManager.activeSceneChangedInEditMode +=
            HandleActiveSceneChangedInEditMode;
        EditorApplication.playModeStateChanged +=
            HandlePlayModeStateChanged;
    }

    public static GameObject PreviewRoot => previewRoot;

    public static bool IsDungeonRunScene(Scene scene)
    {
        return scene.IsValid()
            && scene.isLoaded
            && string.Equals(
                scene.path,
                DungeonRunSceneAuthoringBuilder.ScenePath,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void HandleActiveSceneChangedInEditMode(
        Scene previousScene,
        Scene newScene)
    {
        Clear();
    }

    private static void HandlePlayModeStateChanged(
        PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            Clear();
    }

    public static DungeonSeedPreviewSettings CreateDefinitionSettings()
    {
        DungeonRunDefinition definition = LoadDefinition();
        DungeonRunParameters parameters =
            definition.ResolveParameters(0, 0);
        DungeonGenerator defaults = new();
        return new DungeonSeedPreviewSettings(
            parameters.TotalMainPathTileCount,
            parameters.ArenaCount,
            definition.LayoutCandidateCount,
            definition.MinimumBranchTileCount,
            definition.MaximumPlanarAspectRatio,
            definition.MaxAttemptCount,
            defaults.Padding,
            defaults.OverlapThreshold,
            defaults.DisallowOverhangs);
    }

    public static DungeonSeedPreviewResult Generate(int requestedSeed)
    {
        return Generate(
            requestedSeed,
            CreateDefinitionSettings());
    }

    public static DungeonSeedPreviewResult Generate(
        int requestedSeed,
        DungeonSeedPreviewSettings settings)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "PlayMode 중에는 Seed 미리보기를 생성할 수 없습니다.");
        }

        Clear();
        DungeonRunDefinition definition = LoadDefinition();

        Scene activeScene = SceneManager.GetActiveScene();
        if (!IsDungeonRunScene(activeScene))
        {
            throw new InvalidOperationException(
                "DungeonRunScene에서만 Seed 미리보기를 생성할 수 있습니다.");
        }

        try
        {
            previewRoot = new GameObject(PreviewRootName);
            previewRoot.hideFlags = HideFlags.DontSaveInEditor;
            SceneManager.MoveGameObjectToScene(
                previewRoot,
                activeScene);

            DungeonRunParameters parameters =
                definition.ResolveParameters(
                    0,
                    0,
                    settings.ArenaCount,
                    settings.TotalMainPathTileCount);
            generationFlow = new DungeonRunGenerationFlow(
                definition.DungeonFlow,
                parameters);
            generator = new DungeonRunGenerator(
                previewRoot,
                definition.EndCapTileSet,
                definition.TopologyRepairCatalog)
            {
                DungeonFlow = generationFlow.Flow,
                LengthMultiplier = 1f,
                ShouldRandomizeSeed = false,
                MaxAttemptCount = settings.MaxAttemptCount,
                GenerateAsynchronously = false,
                PlaceTileTriggers = true,
                TileTriggerLayer = 2,
                Padding = settings.Padding,
                OverlapThreshold = settings.OverlapThreshold,
                DisallowOverhangs = settings.DisallowOverhangs
            };
            arenaTileInjection = new DungeonArenaTileInjection(
                generator,
                definition.ArenaTileSet,
                parameters.ArenaCount);

            DungeonLayoutQualityEvaluation acceptedQuality = default;
            int acceptedAttempt = 0;
            string lastFailure = "생성 결과 없음";
            for (int candidateIndex = 0;
                 candidateIndex < settings.LayoutCandidateCount;
                 candidateIndex++)
            {
                generator.Seed =
                    DungeonLayoutQualityEvaluator.GetCandidateSeed(
                        requestedSeed,
                        candidateIndex);
                generator.Generate();
                if (generator.Status != GenerationStatus.Complete)
                {
                    lastFailure =
                        "DunGen 상태=" + generator.Status;
                    generator.Clear(false);
                    continue;
                }

                Dungeon dungeon = generator.CurrentDungeon;
                if (dungeon == null
                    || dungeon.AllTiles == null
                    || dungeon.MainPathTiles == null
                    || dungeon.AllTiles.Count < 2
                    || dungeon.MainPathTiles.Count < 2)
                {
                    lastFailure = "생성 타일 또는 Main Path 부족";
                    generator.Clear(false);
                    continue;
                }

                int placedArenaCount =
                    DungeonArenaTileInjection.CountPlacedArenas(
                        dungeon,
                        definition.ArenaTileSet);
                if (dungeon.MainPathTiles.Count
                        != settings.TotalMainPathTileCount
                    || placedArenaCount != settings.ArenaCount
                    || !DungeonArenaTileInjection
                        .AreAllPlacedArenasOnMainPath(
                            dungeon,
                            definition.ArenaTileSet))
                {
                    lastFailure =
                        $"직접 생성값 불일치: MainPath "
                        + $"{dungeon.MainPathTiles.Count}/"
                        + $"{settings.TotalMainPathTileCount}, Arena "
                        + $"{placedArenaCount}/{settings.ArenaCount}";
                    generator.Clear(false);
                    continue;
                }
                if (!DungeonEndCapContract.TryValidateFinalOpenDoorwayPass(
                        dungeon,
                        generator as DungeonRunGenerator,
                        definition.EndCapTileSet,
                        out string endCapError))
                {
                    lastFailure = endCapError;
                    generator.Clear(false);
                    continue;
                }

                DungeonLayoutQualityEvaluation quality =
                    DungeonLayoutQualityEvaluator.Evaluate(
                        dungeon,
                        settings.MinimumBranchTileCount,
                        settings.MaximumPlanarAspectRatio,
                        definition.EndCapTileSet);
                if (quality.IsAccepted)
                {
                    acceptedQuality = quality;
                    acceptedAttempt = candidateIndex + 1;
                    break;
                }

                lastFailure = quality.RejectionReason;
                generator.Clear(false);
            }

            if (acceptedAttempt == 0)
            {
                throw new InvalidOperationException(
                    "품질 게이트를 통과한 맵이 없습니다: "
                    + lastFailure);
            }

            ApplyDontSaveFlag(previewRoot);
            Dungeon acceptedDungeon = generator.CurrentDungeon;
            DungeonRunGenerator projectGenerator =
                generator as DungeonRunGenerator
                ?? throw new InvalidOperationException(
                    "Project DungeonRunGenerator가 적용되지 않았습니다.");
            if (!projectGenerator.EndCapPassCompleted)
            {
                throw new InvalidOperationException(
                    "최종 오픈 Doorway EndCap 패스가 완료되지 않았습니다.");
            }
            return new DungeonSeedPreviewResult(
                requestedSeed,
                generator.ChosenSeed,
                acceptedAttempt,
                settings.LayoutCandidateCount,
                acceptedDungeon.AllTiles.Count,
                acceptedDungeon.MainPathTiles.Count,
                acceptedQuality.BranchTileCount,
                DungeonArenaTileInjection.CountPlacedArenas(
                    acceptedDungeon,
                    definition.ArenaTileSet),
                projectGenerator.PlacedEndCapCount,
                DungeonEndCapContract.CountPlacedEndCaps(
                    acceptedDungeon,
                    definition.EndCapTileSet),
                projectGenerator.OpenDoorwayCountBeforeEndCaps,
                projectGenerator.OpenDoorwayCountAfterEndCaps,
                projectGenerator.ReplacedTopologyTileCount,
                projectGenerator.ReplacedWithEndCapCount,
                projectGenerator.ReplacedShapeTileCount,
                projectGenerator.RemovedOpenDoorwayCount,
                projectGenerator.OpenDoorwayCountAfterTopologyRepair,
                acceptedQuality.PlanarAspectRatio,
                settings);
        }
        catch
        {
            Clear();
            throw;
        }
    }

    private static DungeonRunDefinition LoadDefinition()
    {
        DungeonRunDefinition definition =
            AssetDatabase.LoadAssetAtPath<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        if (definition == null
            || definition.DungeonFlow == null
            || definition.ArenaTileSet == null
            || definition.EndCapTileSet == null
            || definition.TopologyRepairCatalog == null)
        {
            throw new InvalidOperationException(
                "DungeonRunDefinition의 Flow/Arena/EndCap/"
                + "TopologyRepair 연결이 없습니다.");
        }

        return definition;
    }

    public static void Clear()
    {
        arenaTileInjection?.Dispose();
        arenaTileInjection = null;
        if (generator != null)
        {
            if (generator.IsGenerating)
                generator.Cancel();
            generator.Clear(false);
            generator = null;
        }
        generationFlow?.Dispose();
        generationFlow = null;

        GameObject[] objects =
            Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject candidate = objects[i];
            if (candidate == null
                || candidate.name != PreviewRootName
                || !candidate.scene.IsValid()
                || EditorUtility.IsPersistent(candidate))
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(candidate);
        }
        previewRoot = null;
    }

    private static void ApplyDontSaveFlag(GameObject root)
    {
        Transform[] transforms =
            root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null)
            {
                transforms[i].gameObject.hideFlags |=
                    HideFlags.DontSaveInEditor;
            }
        }
    }
}

public readonly struct DungeonSeedPreviewSettings
{
    public DungeonSeedPreviewSettings(
        int totalMainPathTileCount,
        int arenaCount,
        int layoutCandidateCount,
        int minimumBranchTileCount,
        float maximumPlanarAspectRatio,
        int maxAttemptCount,
        float padding,
        float overlapThreshold,
        bool disallowOverhangs)
    {
        ArenaCount = Mathf.Clamp(
            arenaCount,
            DungeonRunParameters.MinimumArenaCount,
            DungeonRunParameters.MaximumArenaCount);
        TotalMainPathTileCount = Mathf.Clamp(
            totalMainPathTileCount,
            Mathf.Max(
                DungeonRunParameters.MinimumTotalMainPathTileCount,
                ArenaCount + 2),
            DungeonRunParameters.MaximumTotalMainPathTileCount);
        LayoutCandidateCount = Mathf.Clamp(
            layoutCandidateCount,
            1,
            30);
        MinimumBranchTileCount = Mathf.Clamp(
            minimumBranchTileCount,
            0,
            50);
        MaximumPlanarAspectRatio = Mathf.Clamp(
            maximumPlanarAspectRatio,
            1f,
            10f);
        MaxAttemptCount = Mathf.Clamp(maxAttemptCount, 1, 500);
        Padding = Mathf.Clamp(padding, 0f, 5f);
        OverlapThreshold = Mathf.Clamp(
            overlapThreshold,
            0.0001f,
            1f);
        DisallowOverhangs = disallowOverhangs;
    }

    public int TotalMainPathTileCount { get; }
    public int ArenaCount { get; }
    public int LayoutCandidateCount { get; }
    public int MinimumBranchTileCount { get; }
    public float MaximumPlanarAspectRatio { get; }
    public int MaxAttemptCount { get; }
    public float Padding { get; }
    public float OverlapThreshold { get; }
    public bool DisallowOverhangs { get; }
}

public readonly struct DungeonSeedPreviewResult
{
    public DungeonSeedPreviewResult(
        int requestedSeed,
        int chosenSeed,
        int layoutAttempt,
        int layoutCandidateCount,
        int tileCount,
        int mainPathCount,
        int branchTileCount,
        int placedArenaCount,
        int placedEndCapCount,
        int finalEndCapCount,
        int openDoorwayCountBeforeEndCaps,
        int openDoorwayCountAfterEndCaps,
        int replacedTopologyTileCount,
        int replacedWithEndCapCount,
        int replacedShapeTileCount,
        int removedOpenDoorwayCount,
        int openDoorwayCountAfterTopologyRepair,
        float planarAspectRatio,
        DungeonSeedPreviewSettings settings)
    {
        RequestedSeed = requestedSeed;
        ChosenSeed = chosenSeed;
        LayoutAttempt = layoutAttempt;
        LayoutCandidateCount = layoutCandidateCount;
        TileCount = tileCount;
        MainPathCount = mainPathCount;
        BranchTileCount = branchTileCount;
        PlacedArenaCount = placedArenaCount;
        PlacedEndCapCount = placedEndCapCount;
        FinalEndCapCount = finalEndCapCount;
        OpenDoorwayCountBeforeEndCaps =
            openDoorwayCountBeforeEndCaps;
        OpenDoorwayCountAfterEndCaps =
            openDoorwayCountAfterEndCaps;
        ReplacedTopologyTileCount = replacedTopologyTileCount;
        ReplacedWithEndCapCount = replacedWithEndCapCount;
        ReplacedShapeTileCount = replacedShapeTileCount;
        RemovedOpenDoorwayCount = removedOpenDoorwayCount;
        OpenDoorwayCountAfterTopologyRepair =
            openDoorwayCountAfterTopologyRepair;
        PlanarAspectRatio = planarAspectRatio;
        TotalMainPathTileCount =
            settings.TotalMainPathTileCount;
        ArenaCount = settings.ArenaCount;
        MinimumBranchTileCount = settings.MinimumBranchTileCount;
        MaximumPlanarAspectRatio =
            settings.MaximumPlanarAspectRatio;
        MaxAttemptCount = settings.MaxAttemptCount;
        Padding = settings.Padding;
        OverlapThreshold = settings.OverlapThreshold;
        DisallowOverhangs = settings.DisallowOverhangs;
    }

    public int RequestedSeed { get; }
    public int ChosenSeed { get; }
    public int LayoutAttempt { get; }
    public int LayoutCandidateCount { get; }
    public int TileCount { get; }
    public int MainPathCount { get; }
    public int BranchTileCount { get; }
    public int TotalMainPathTileCount { get; }
    public int ArenaCount { get; }
    public int PlacedArenaCount { get; }
    public int PlacedEndCapCount { get; }
    public int FinalEndCapCount { get; }
    public int OpenDoorwayCountBeforeEndCaps { get; }
    public int OpenDoorwayCountAfterEndCaps { get; }
    public int ReplacedTopologyTileCount { get; }
    public int ReplacedWithEndCapCount { get; }
    public int ReplacedShapeTileCount { get; }
    public int RemovedOpenDoorwayCount { get; }
    public int OpenDoorwayCountAfterTopologyRepair { get; }
    public float PlanarAspectRatio { get; }
    public int MinimumBranchTileCount { get; }
    public float MaximumPlanarAspectRatio { get; }
    public int MaxAttemptCount { get; }
    public float Padding { get; }
    public float OverlapThreshold { get; }
    public bool DisallowOverhangs { get; }
}
