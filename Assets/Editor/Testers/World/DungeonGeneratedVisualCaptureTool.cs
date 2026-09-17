using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DungeonGeneratedVisualCaptureTool
{
    private const string OutputDirectory =
        "Logs/DungeonPhase6QualityGallery";
    private const string ReportPath =
        OutputDirectory + "/capture_report.txt";
    private const string TenSampleOutputDirectory =
        "Logs/DungeonLayoutTenSamples";
    private const string TenSampleReportPath =
        TenSampleOutputDirectory + "/capture_report.txt";
    private const string EventAreaOutputDirectory =
        "Logs/DungeonEventArea4To8Gallery";
    private const string EventAreaReportPath =
        EventAreaOutputDirectory + "/capture_report.txt";
    private static readonly int[] RequestedSeeds =
    {
        27040,
        27016,
        27008
    };
    private static readonly int[] TenSampleSeeds =
    {
        27101,
        27102,
        27103,
        27104,
        27105,
        27106,
        27107,
        27108,
        27109,
        27110
    };
    private static readonly int[] EventAreaDifficultyLevels =
    {
        1,
        5,
        10
    };
    private const int CaptureWidth = 1600;
    private const int CaptureHeight = 1000;

    private static int exitDelayTicks;
    private static int pendingExitCode;

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Capture Generated Dungeon")]
    public static void RunFromMenu()
    {
        Execute(
            false,
            RequestedSeeds,
            OutputDirectory,
            ReportPath,
            false);
    }

    public static void RunFromCommandLine()
    {
        Execute(
            true,
            RequestedSeeds,
            OutputDirectory,
            ReportPath,
            false);
    }

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Capture 10 Layout Samples")]
    public static void RunTenSamplesFromMenu()
    {
        Execute(
            false,
            TenSampleSeeds,
            TenSampleOutputDirectory,
            TenSampleReportPath,
            true);
    }

    public static void RunTenSamplesFromCommandLine()
    {
        Execute(
            true,
            TenSampleSeeds,
            TenSampleOutputDirectory,
            TenSampleReportPath,
            true);
    }

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Capture 10 EventArea 4x4~8x8 Difficulty Samples")]
    public static void RunEventAreaSamplesFromMenu()
    {
        ExecuteEventAreaDifficultyComparison(false);
    }

    public static void RunEventAreaSamplesFromCommandLine()
    {
        ExecuteEventAreaDifficultyComparison(true);
    }

    private static void ExecuteEventAreaDifficultyComparison(bool batchMode)
    {
        Directory.CreateDirectory(EventAreaOutputDirectory);
        int exitCode;
        try
        {
            StringBuilder reportBuilder = new();
            for (int difficultyIndex = 0;
                 difficultyIndex < EventAreaDifficultyLevels.Length;
                 difficultyIndex++)
            {
                int difficulty =
                    EventAreaDifficultyLevels[difficultyIndex];
                for (int seedIndex = 0;
                     seedIndex < TenSampleSeeds.Length;
                     seedIndex++)
                {
                    int requestedSeed = TenSampleSeeds[seedIndex];
                    EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Single);
                    string seedDirectory =
                        EventAreaOutputDirectory
                        + $"/Difficulty_{difficulty:00}"
                        + $"/Seed_{requestedSeed}";
                    Directory.CreateDirectory(seedDirectory);
                    CaptureSession session = new(
                        requestedSeed,
                        seedDirectory,
                        true,
                        true,
                        difficulty);
                    if (reportBuilder.Length > 0)
                        reportBuilder.AppendLine().AppendLine();
                    reportBuilder.Append(session.Capture());
                }
            }

            string report = reportBuilder.ToString();
            File.WriteAllText(
                EventAreaReportPath,
                report,
                new UTF8Encoding(false));
            Debug.Log(report);
            exitCode = 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                EventAreaReportPath,
                exception.ToString(),
                new UTF8Encoding(false));
            Debug.LogException(exception);
            exitCode = 1;
        }

        if (batchMode)
            ScheduleBatchExit(exitCode);
    }

    private static void Execute(
        bool batchMode,
        IReadOnlyList<int> requestedSeeds,
        string outputDirectory,
        string reportPath,
        bool layoutOnly,
        bool eventAreaOverlay = false)
    {
        Directory.CreateDirectory(outputDirectory);
        int exitCode;
        try
        {
            StringBuilder reportBuilder = new();
            for (int i = 0; i < requestedSeeds.Count; i++)
            {
                int requestedSeed = requestedSeeds[i];
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
                string seedDirectory =
                    outputDirectory + "/Seed_" + requestedSeed;
                Directory.CreateDirectory(seedDirectory);
                CaptureSession session = new(
                    requestedSeed,
                    seedDirectory,
                    layoutOnly,
                    eventAreaOverlay);
                if (reportBuilder.Length > 0)
                    reportBuilder.AppendLine().AppendLine();
                reportBuilder.Append(session.Capture());
            }
            string report = reportBuilder.ToString();
            File.WriteAllText(
                reportPath,
                report,
                new UTF8Encoding(false));
            Debug.Log(report);
            exitCode = 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                reportPath,
                exception.ToString(),
                new UTF8Encoding(false));
            Debug.LogException(exception);
            exitCode = 1;
        }

        if (batchMode)
            ScheduleBatchExit(exitCode);
    }

    private static void ScheduleBatchExit(int exitCode)
    {
        pendingExitCode = exitCode;
        exitDelayTicks = 10;
        EditorApplication.update -= HandleDelayedExit;
        EditorApplication.update += HandleDelayedExit;
    }

    private static void HandleDelayedExit()
    {
        exitDelayTicks--;
        if (exitDelayTicks > 0)
            return;

        EditorApplication.update -= HandleDelayedExit;
        EditorApplication.Exit(pendingExitCode);
    }

    private sealed class CaptureSession
    {
        private readonly int requestedSeed;
        private readonly string outputDirectory;
        private readonly bool layoutOnly;
        private readonly bool eventAreaOverlay;
        private readonly int eventAreaDifficultyLevel;
        private DungeonRunGenerator generator;
        private DungeonArenaTileInjection arenaTileInjection;
        private DungeonRunGenerationFlow generationFlow;
        private DungeonRunParameters parameters;
        private DungeonEventAreaLayout eventAreaLayout;
        private GameObject generatedRoot;
        private GameObject eventAreaOverlayRoot;
        private GameObject cameraObject;
        private GameObject keyLightObject;
        private GameObject fillLightObject;
        private Camera captureCamera;
        private RenderTexture renderTexture;
        private Texture2D captureTexture;
        private readonly List<Material> overlayMaterials = new();

        public CaptureSession(
            int requestedSeed,
            string outputDirectory,
            bool layoutOnly,
            bool eventAreaOverlay,
            int eventAreaDifficultyLevel = 0)
        {
            this.requestedSeed = requestedSeed;
            this.outputDirectory = outputDirectory;
            this.layoutOnly = layoutOnly;
            this.eventAreaOverlay = eventAreaOverlay;
            this.eventAreaDifficultyLevel = eventAreaDifficultyLevel;
        }

        public string Capture()
        {
            StringBuilder report = new();
            try
            {
                DungeonFlow flow =
                    AssetDatabase.LoadAssetAtPath<DungeonFlow>(
                        DungeonContentAuthoringBuilder.FlowOutputPath);
                Require(flow != null, "Project DungeonFlow 누락");
                DungeonRunDefinition definition =
                    AssetDatabase.LoadAssetAtPath<DungeonRunDefinition>(
                        DungeonRunSceneAuthoringBuilder.DefinitionPath);
                Require(definition != null, "DungeonRunDefinition 누락");
                parameters =
                    definition.ResolveParameters(
                        eventAreaDifficultyLevel,
                        0);

                generatedRoot = new GameObject(
                    "DungeonGeneratedCaptureRoot");
                generationFlow = new DungeonRunGenerationFlow(
                    flow,
                    parameters);
                generator = new DungeonRunGenerator(
                    generatedRoot,
                    definition.EndCapTileSet,
                    definition.TopologyRepairCatalog)
                {
                    DungeonFlow = generationFlow.Flow,
                    LengthMultiplier = 1f,
                    ShouldRandomizeSeed = false,
                    MaxAttemptCount = 100,
                    GenerateAsynchronously = false,
                    PlaceTileTriggers = true,
                    TileTriggerLayer = 2
                };
                arenaTileInjection = new DungeonArenaTileInjection(
                    generator,
                    definition.ArenaTileSet,
                    parameters.ArenaCount);
                DungeonLayoutQualityEvaluation quality =
                    GenerateAcceptedLayout(
                        definition,
                        out int layoutAttempt);

                Dungeon dungeon = generator.CurrentDungeon;
                Require(dungeon != null, "생성 Dungeon 누락");
                int placedArenaCount =
                    DungeonArenaTileInjection.CountPlacedArenas(
                        dungeon,
                        definition.ArenaTileSet);
                int placedEndCapCount =
                    DungeonEndCapContract.CountPlacedEndCaps(
                        dungeon,
                        definition.EndCapTileSet);
                Require(
                    dungeon.MainPathTiles != null
                    && dungeon.MainPathTiles.Count >= 3,
                    "캡처 가능한 Main Path 부족");

                if (eventAreaOverlay)
                    CreateEventAreaOverlay(dungeon);
                Bounds dungeonBounds = CalculateVisualBounds(
                    generatedRoot);
                CreateCaptureEnvironment(dungeonBounds);
                if (layoutOnly)
                    DisableParticleRenderers(generatedRoot);
                else
                    SimulateAmbientEffects(generatedRoot, 1.25f);

                List<CaptureSpec> captures =
                    BuildCaptureSpecs(dungeon, dungeonBounds);
                for (int index = 0; index < captures.Count; index++)
                {
                    CaptureSpec spec = captures[index];
                    ApplyCaptureSpec(spec);
                    WarmRender(2);
                    string outputPath =
                        outputDirectory + "/" + spec.FileName;
                    RenderCapture(outputPath);
                    report.AppendLine(
                        $"{spec.FileName}: "
                        + $"Position={FormatVector(captureCamera.transform.position)}, "
                        + $"Target={FormatVector(spec.Target)}, "
                        + $"Orthographic={spec.Orthographic}");
                }

                report.Insert(
                    0,
                    "[DungeonGeneratedVisualCaptureTool] PASS\n"
                    + $"RequestedSeed={requestedSeed}\n"
                    + $"ChosenSeed={generator.ChosenSeed}\n"
                    + $"LayoutAttempt={layoutAttempt}/"
                    + $"{definition.LayoutCandidateCount}\n"
                    + $"Tiles={dungeon.AllTiles.Count}\n"
                    + $"MainPath={dungeon.MainPathTiles.Count}\n"
                    + $"Arena={placedArenaCount}\n"
                    + $"EndCap={placedEndCapCount}\n"
                    + $"OpenDoorways="
                    + $"{generator.OpenDoorwayCountBeforeEndCaps}->"
                    + $"{generator.OpenDoorwayCountAfterEndCaps}\n"
                    + $"Branches={quality.BranchTileCount}\n"
                    + $"PlanarAspect="
                    + $"{quality.PlanarAspectRatio:F2}\n"
                    + (eventAreaLayout != null
                        ? $"Difficulty="
                            + $"{eventAreaLayout.Parameters.DifficultyLevel}\n"
                            + $"RoomCount="
                            + $"{eventAreaLayout.GameplayRoomCount}\n"
                            + $"AreaCandidateCount="
                            + $"{eventAreaLayout.Candidates.Count}\n"
                            + $"SelectedAreaCount="
                            + $"{eventAreaLayout.SelectedNodes.Count}\n"
                            + $"SelectedSizes="
                            + $"{BuildSelectedSizeSummary(eventAreaLayout)}\n"
                            + $"AverageSelectedSurface="
                            + $"{CalculateAverageSelectedSurface(eventAreaLayout):F2}\n"
                        : string.Empty)
                    + $"BoundsCenter={FormatVector(dungeonBounds.center)}\n"
                    + $"BoundsSize={FormatVector(dungeonBounds.size)}\n"
                    + $"Resolution={CaptureWidth}x{CaptureHeight}\n"
                    + $"CaptureCount={captures.Count}\n\n");
                return report.ToString().TrimEnd();
            }
            finally
            {
                Cleanup();
            }
        }

        private DungeonLayoutQualityEvaluation GenerateAcceptedLayout(
            DungeonRunDefinition definition,
            out int layoutAttempt)
        {
            DungeonLayoutQualityEvaluation lastQuality = default;
            for (int candidateIndex = 0;
                 candidateIndex < definition.LayoutCandidateCount;
                 candidateIndex++)
            {
                generator.Seed =
                    DungeonLayoutQualityEvaluator.GetCandidateSeed(
                        requestedSeed,
                        candidateIndex);
                generator.Generate();
                Require(
                    generator.Status == GenerationStatus.Complete,
                    "던전 생성 실패: " + generator.Status);
                Dungeon generatedDungeon = generator.CurrentDungeon;
                int placedArenaCount =
                    DungeonArenaTileInjection.CountPlacedArenas(
                        generatedDungeon,
                        definition.ArenaTileSet);
                Require(
                    generatedDungeon?.MainPathTiles != null
                    && generatedDungeon.MainPathTiles.Count
                        == parameters.TotalMainPathTileCount
                    && placedArenaCount == parameters.ArenaCount
                    && DungeonArenaTileInjection
                        .AreAllPlacedArenasOnMainPath(
                            generatedDungeon,
                            definition.ArenaTileSet)
                    && DungeonEndCapContract.TryValidateFinalOpenDoorwayPass(
                        generatedDungeon,
                        generator,
                        definition.EndCapTileSet,
                        out _),
                    "직접 총길이, Arena 개수 또는 EndCap 배치 계약 불일치");

                lastQuality = DungeonLayoutQualityEvaluator.Evaluate(
                    generator.CurrentDungeon,
                    definition.MinimumBranchTileCount,
                    definition.MaximumPlanarAspectRatio,
                    definition.EndCapTileSet);
                if (lastQuality.IsAccepted)
                {
                    if (eventAreaOverlay)
                    {
                        DungeonRunParameters parameters =
                            definition.ResolveParameters(
                                eventAreaDifficultyLevel,
                                0);
                        if (!DungeonEventAreaLayoutResolver.TryResolve(
                                generator.CurrentDungeon,
                                definition,
                                parameters,
                                requestedSeed,
                                out eventAreaLayout,
                                out _))
                        {
                            generator.Clear(false);
                            continue;
                        }
                    }

                    layoutAttempt = candidateIndex + 1;
                    return lastQuality;
                }

                generator.Clear(false);
            }

            layoutAttempt = definition.LayoutCandidateCount;
            throw new InvalidOperationException(
                $"Seed {requestedSeed}: 품질 기준 통과 후보 없음. "
                + lastQuality.RejectionReason);
        }

        private void CreateCaptureEnvironment(Bounds bounds)
        {
            RenderSettings.ambientMode =
                UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor =
                new Color(0.50f, 0.56f, 0.66f);
            RenderSettings.ambientEquatorColor =
                new Color(0.30f, 0.33f, 0.38f);
            RenderSettings.ambientGroundColor =
                new Color(0.12f, 0.13f, 0.16f);
            RenderSettings.ambientIntensity = 1f;

            keyLightObject = new GameObject("Capture_KeyLight");
            Light keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.92f, 0.80f);
            keyLight.intensity = 1.1f;
            keyLight.shadows = LightShadows.Soft;
            keyLightObject.transform.rotation =
                Quaternion.Euler(48f, -38f, 0f);
            RenderSettings.sun = keyLight;

            fillLightObject = new GameObject("Capture_FillLight");
            Light fillLight = fillLightObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.48f, 0.62f, 1f);
            fillLight.intensity = 0.32f;
            fillLight.shadows = LightShadows.None;
            fillLightObject.transform.rotation =
                Quaternion.Euler(38f, 142f, 0f);

            cameraObject = new GameObject("DungeonCapture_Camera");
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor =
                new Color(0.055f, 0.068f, 0.090f);
            captureCamera.fieldOfView = 43f;
            captureCamera.nearClipPlane = 0.05f;
            captureCamera.farClipPlane =
                Mathf.Max(500f, bounds.extents.magnitude * 12f);
            captureCamera.allowHDR = true;
            captureCamera.allowMSAA = true;

            renderTexture = new RenderTexture(
                CaptureWidth,
                CaptureHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4,
                name = "DungeonGeneratedCaptureRT"
            };
            renderTexture.Create();
            captureTexture = new Texture2D(
                CaptureWidth,
                CaptureHeight,
                TextureFormat.RGB24,
                false,
                false);
        }

        private static void SimulateAmbientEffects(
            GameObject root,
            float time)
        {
            ParticleSystem[] particleSystems =
                root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem system = particleSystems[i];
                if (system != null && system.gameObject.activeInHierarchy)
                    system.Simulate(time, true, true, true);
            }
        }

        private static void DisableParticleRenderers(GameObject root)
        {
            ParticleSystemRenderer[] renderers =
                root.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = false;
            }
        }

        private void CreateEventAreaOverlay(Dungeon dungeon)
        {
            Require(eventAreaLayout != null, "EventArea Layout 누락");
            eventAreaOverlayRoot = new GameObject(
                "DungeonEventAreaCaptureOverlay");
            eventAreaOverlayRoot.transform.SetParent(
                generatedRoot.transform,
                true);

            Material entranceMaterial = CreateOverlayMaterial(
                "EventArea_Entrance",
                new Color(0.10f, 0.55f, 1f, 0.72f));
            Material gameplayMaterial = CreateOverlayMaterial(
                "EventArea_Gameplay",
                new Color(0.12f, 1f, 0.42f, 0.72f));
            Material exitMaterial = CreateOverlayMaterial(
                "EventArea_Exit",
                new Color(1f, 0.45f, 0.08f, 0.78f));
            Material candidateMaterial = CreateOverlayMaterial(
                "EventArea_Unselected",
                new Color(0.55f, 0.62f, 0.72f, 0.28f));
            Material linkMaterial = CreateOverlayMaterial(
                "EventArea_Link",
                new Color(0.05f, 0.95f, 1f, 0.90f));

            Dictionary<Tile, DungeonEventAreaAuthoring> selectedByTile =
                eventAreaLayout.SelectedNodes.ToDictionary(
                    node => node.Tile,
                    node => node.Area);
            Dictionary<Tile, DungeonEventAreaAuthoring> baselineByTile =
                eventAreaLayout.Candidates
                    .Where(area => area != null)
                    .Select(area => new
                    {
                        Area = area,
                        Tile = area.GetComponentInParent<Tile>()
                    })
                    .Where(pair => pair.Tile != null)
                    .GroupBy(pair => pair.Tile)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderBy(pair => pair.Area.SurfaceArea)
                            .ThenBy(
                                pair => pair.Area.AreaId,
                                StringComparer.Ordinal)
                            .First()
                            .Area);
            foreach (KeyValuePair<Tile, DungeonEventAreaAuthoring> pair
                     in baselineByTile)
            {
                Tile tile = pair.Key;
                if (tile == eventAreaLayout.EntranceTile
                    || tile == eventAreaLayout.ExitTile)
                {
                    continue;
                }

                if (selectedByTile.TryGetValue(
                        tile,
                        out DungeonEventAreaAuthoring selectedArea))
                {
                    CreateAreaPlate(
                        selectedArea,
                        gameplayMaterial,
                        true);
                }
                else
                {
                    CreateAreaPlate(
                        pair.Value,
                        candidateMaterial,
                        false);
                }
            }

            CreateTileMarker(
                eventAreaLayout.EntranceTile,
                entranceMaterial,
                "Entrance");
            CreateTileMarker(
                eventAreaLayout.ExitTile,
                exitMaterial,
                "Exit");
            for (int i = 0;
                 i < eventAreaLayout.DoorwayConnections.Count;
                 i++)
            {
                DoorwayConnection connection =
                    eventAreaLayout.DoorwayConnections[i];
                Doorway doorwayA = connection?.A;
                Doorway doorwayB = connection?.B;
                if (doorwayA?.Tile == null
                    || doorwayB?.Tile == null)
                {
                    continue;
                }

                Vector3 tilePointA = selectedByTile.TryGetValue(
                    doorwayA.Tile,
                    out DungeonEventAreaAuthoring selectedAreaA)
                    ? selectedAreaA.transform.position
                    : baselineByTile.TryGetValue(
                        doorwayA.Tile,
                        out DungeonEventAreaAuthoring baselineAreaA)
                        ? baselineAreaA.transform.position
                        : doorwayA.transform.position;
                Vector3 tilePointB = selectedByTile.TryGetValue(
                    doorwayB.Tile,
                    out DungeonEventAreaAuthoring selectedAreaB)
                    ? selectedAreaB.transform.position
                    : baselineByTile.TryGetValue(
                        doorwayB.Tile,
                        out DungeonEventAreaAuthoring baselineAreaB)
                        ? baselineAreaB.transform.position
                        : doorwayB.transform.position;
                CreateConnectionLine(
                    i,
                    tilePointA,
                    doorwayA.transform.position,
                    doorwayB.transform.position,
                    tilePointB,
                    linkMaterial);
            }
        }

        private void CreateAreaPlate(
            DungeonEventAreaAuthoring area,
            Material material,
            bool selected)
        {
            GameObject plate = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            plate.name = "AreaPlate_" + area.AreaId;
            plate.transform.SetParent(eventAreaOverlayRoot.transform, true);
            plate.transform.SetPositionAndRotation(
                area.transform.position + Vector3.up * (selected ? 0.10f : 0.06f),
                area.transform.rotation);
            plate.transform.localScale = new Vector3(
                area.DimensionsMeters.x,
                selected ? 0.10f : 0.055f,
                area.DimensionsMeters.y);
            Collider collider = plate.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
            plate.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void CreateTileMarker(
            Tile tile,
            Material material,
            string markerName)
        {
            if (tile == null)
                return;

            DungeonRoomAnchor anchor =
                tile.GetComponentInChildren<DungeonRoomAnchor>(true);
            Vector3 position = anchor != null
                ? anchor.transform.position
                : tile.transform.position;
            GameObject marker = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            marker.name = "TileMarker_" + markerName;
            marker.transform.SetParent(
                eventAreaOverlayRoot.transform,
                true);
            marker.transform.position = position + Vector3.up * 0.13f;
            marker.transform.localScale =
                new Vector3(1.25f, 0.14f, 1.25f);
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
            marker.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void CreateConnectionLine(
            int index,
            Vector3 areaA,
            Vector3 doorwayA,
            Vector3 doorwayB,
            Vector3 areaB,
            Material material)
        {
            GameObject lineObject = new(
                "AreaLink_" + index.ToString("D2"));
            lineObject.transform.SetParent(
                eventAreaOverlayRoot.transform,
                true);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 4;
            line.startWidth = 0.16f;
            line.endWidth = 0.16f;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            Vector3 lift = Vector3.up * 0.22f;
            line.SetPosition(0, areaA + lift);
            line.SetPosition(1, doorwayA + lift);
            line.SetPosition(2, doorwayB + lift);
            line.SetPosition(3, areaB + lift);
        }

        private Material CreateOverlayMaterial(
            string materialName,
            Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            Require(shader != null, "Overlay Shader 누락");
            Material material = new(shader)
            {
                name = materialName,
                color = color
            };
            overlayMaterials.Add(material);
            return material;
        }

        private static string BuildSelectedSizeSummary(
            DungeonEventAreaLayout layout)
        {
            return string.Join(
                ",",
                layout.SelectedNodes
                    .Select(node =>
                        $"{node.Area.DimensionsMeters.x}x"
                        + $"{node.Area.DimensionsMeters.y}")
                    .OrderBy(value => value, StringComparer.Ordinal));
        }

        private static float CalculateAverageSelectedSurface(
            DungeonEventAreaLayout layout)
        {
            return layout.SelectedNodes.Count > 0
                ? layout.SelectedNodes.Average(node => node.Area.SurfaceArea)
                : 0f;
        }

        private List<CaptureSpec> BuildCaptureSpecs(
            Dungeon dungeon,
            Bounds dungeonBounds)
        {
            float overviewDistance =
                CalculatePerspectiveDistance(dungeonBounds, 1.18f);
            const float overviewElevation = 38f;
            if (layoutOnly)
            {
                return new List<CaptureSpec>
                {
                    CaptureSpec.Perspective(
                        "01_overview_northeast.png",
                        dungeonBounds.center,
                        DirectionAtElevation(
                            new Vector3(1f, 0f, -1f),
                            overviewElevation),
                        overviewDistance),
                    CaptureSpec.TopDown(
                        "02_topdown_layout.png",
                        dungeonBounds.center,
                        CalculateOrthographicSize(
                            dungeonBounds,
                            1.12f),
                        dungeonBounds.max.y
                            + Mathf.Max(
                                20f,
                                dungeonBounds.size.y * 2f))
                };
            }

            List<CaptureSpec> specs = new()
            {
                CaptureSpec.Perspective(
                    "01_overview_northeast.png",
                    dungeonBounds.center,
                    DirectionAtElevation(
                        new Vector3(1f, 0f, -1f),
                        overviewElevation),
                    overviewDistance),
                CaptureSpec.Perspective(
                    "02_overview_southwest.png",
                    dungeonBounds.center,
                    DirectionAtElevation(
                        new Vector3(-1f, 0f, 1f),
                        overviewElevation),
                    overviewDistance),
                CaptureSpec.TopDown(
                    "03_topdown_layout.png",
                    dungeonBounds.center,
                    CalculateOrthographicSize(dungeonBounds, 1.12f),
                    dungeonBounds.max.y
                        + Mathf.Max(
                            20f,
                            dungeonBounds.size.y * 2f))
            };

            Tile entrance = dungeon.MainPathTiles[0];
            Tile entranceNext = dungeon.MainPathTiles[1];
            Bounds entranceBounds = CalculateCombinedPlayableBounds(
                entrance.gameObject,
                entranceNext.gameObject);
            Vector3 entrancePath = HorizontalDirection(
                entranceNext.transform.position
                - entrance.transform.position,
                new Vector3(1f, 0f, 1f));
            specs.Add(
                CaptureSpec.Perspective(
                    "04_entrance_room.png",
                    entranceBounds.center,
                    DirectionAtElevation(-entrancePath, 36f),
                    CalculatePerspectiveDistance(
                        entranceBounds,
                        1.22f)));

            int middleIndex = dungeon.MainPathTiles.Count / 2;
            Tile middlePrevious =
                dungeon.MainPathTiles[middleIndex - 1];
            Tile middle = dungeon.MainPathTiles[middleIndex];
            Tile middleNext =
                dungeon.MainPathTiles[
                    Mathf.Min(
                        dungeon.MainPathTiles.Count - 1,
                        middleIndex + 1)];
            Bounds middleBounds = CalculateCombinedPlayableBounds(
                middlePrevious.gameObject,
                middle.gameObject,
                middleNext.gameObject);
            Vector3 middlePath = HorizontalDirection(
                middleNext.transform.position
                - middlePrevious.transform.position,
                new Vector3(1f, 0f, 0f));
            Vector3 middleSide =
                new(middlePath.z, 0f, -middlePath.x);
            specs.Add(
                CaptureSpec.Perspective(
                    "05_midpath_multilevel.png",
                    middleBounds.center,
                    DirectionAtElevation(middleSide, 28f),
                    CalculatePerspectiveDistance(
                        middleBounds,
                        1.18f)));

            int lastIndex = dungeon.MainPathTiles.Count - 1;
            Tile exitPrevious =
                dungeon.MainPathTiles[lastIndex - 1];
            Tile exit = dungeon.MainPathTiles[lastIndex];
            Bounds exitBounds = CalculateCombinedPlayableBounds(
                exitPrevious.gameObject,
                exit.gameObject);
            Vector3 exitPath = HorizontalDirection(
                exit.transform.position
                - exitPrevious.transform.position,
                new Vector3(-1f, 0f, 1f));
            specs.Add(
                CaptureSpec.Perspective(
                    "06_exit_room.png",
                    exitBounds.center,
                    DirectionAtElevation(exitPath, 36f),
                    CalculatePerspectiveDistance(exitBounds, 1.22f)));

            return specs;
        }

        private void ApplyCaptureSpec(CaptureSpec spec)
        {
            captureCamera.orthographic = spec.Orthographic;
            captureCamera.orthographicSize = spec.OrthographicSize;
            captureCamera.transform.position =
                spec.Target + spec.ViewDirection * spec.Distance;
            captureCamera.transform.LookAt(
                spec.Target,
                spec.Orthographic ? Vector3.forward : Vector3.up);
        }

        private void WarmRender(int frameCount)
        {
            captureCamera.targetTexture = renderTexture;
            for (int i = 0; i < frameCount; i++)
                captureCamera.Render();
            captureCamera.targetTexture = null;
        }

        private void RenderCapture(string path)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                captureCamera.targetTexture = renderTexture;
                captureCamera.Render();
                RenderTexture.active = renderTexture;
                captureTexture.ReadPixels(
                    new Rect(
                        0,
                        0,
                        renderTexture.width,
                        renderTexture.height),
                    0,
                    0);
                captureTexture.Apply(false);
                File.WriteAllBytes(
                    path,
                    captureTexture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                captureCamera.targetTexture = null;
            }
        }

        private static Bounds CalculateVisualBounds(GameObject root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null
                    || renderer is ParticleSystemRenderer
                    || renderer is TrailRenderer
                    || renderer is LineRenderer
                    || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            Require(found, "캡처할 구조물 Renderer가 없습니다.");
            return bounds;
        }

        private static Bounds CalculateCombinedVisualBounds(
            params GameObject[] roots)
        {
            bool found = false;
            Bounds combined = default;
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                Bounds bounds = CalculateVisualBounds(root);
                if (!found)
                {
                    combined = bounds;
                    found = true;
                }
                else
                {
                    combined.Encapsulate(bounds);
                }
            }

            Require(found, "부분 캡처 Bounds 계산 실패");
            return combined;
        }

        private static Bounds CalculateCombinedPlayableBounds(
            params GameObject[] roots)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            Require(groundLayer >= 0, "Ground 레이어 누락");

            bool found = false;
            Bounds combined = default;
            for (int rootIndex = 0;
                 rootIndex < roots.Length;
                 rootIndex++)
            {
                GameObject root = roots[rootIndex];
                if (root == null)
                    continue;

                Collider[] colliders =
                    root.GetComponentsInChildren<Collider>(true);
                for (int colliderIndex = 0;
                     colliderIndex < colliders.Length;
                     colliderIndex++)
                {
                    Collider collider = colliders[colliderIndex];
                    if (collider == null
                        || !collider.enabled
                        || !collider.gameObject.activeInHierarchy
                        || collider.gameObject.layer != groundLayer)
                    {
                        continue;
                    }

                    if (!found)
                    {
                        combined = collider.bounds;
                        found = true;
                    }
                    else
                    {
                        combined.Encapsulate(collider.bounds);
                    }
                }
            }

            Require(found, "부분 캡처용 상부 Ground Bounds 계산 실패");
            return combined;
        }

        private float CalculatePerspectiveDistance(
            Bounds bounds,
            float padding)
        {
            float radius = Mathf.Max(1f, bounds.extents.magnitude);
            float halfFovRadians =
                captureCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            return radius
                / Mathf.Max(0.1f, Mathf.Tan(halfFovRadians))
                * padding;
        }

        private static float CalculateOrthographicSize(
            Bounds bounds,
            float padding)
        {
            float aspect = (float)CaptureWidth / CaptureHeight;
            float verticalHalf = bounds.extents.z;
            float horizontalHalfAsVertical =
                bounds.extents.x / aspect;
            return Mathf.Max(verticalHalf, horizontalHalfAsVertical)
                * padding;
        }

        private static Vector3 DirectionAtElevation(
            Vector3 horizontalDirection,
            float elevationDegrees)
        {
            Vector3 horizontal = HorizontalDirection(
                horizontalDirection,
                new Vector3(1f, 0f, -1f));
            float radians = elevationDegrees * Mathf.Deg2Rad;
            return (
                horizontal * Mathf.Cos(radians)
                + Vector3.up * Mathf.Sin(radians)).normalized;
        }

        private static Vector3 HorizontalDirection(
            Vector3 direction,
            Vector3 fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = fallback;
            direction.y = 0f;
            return direction.normalized;
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:F2},{1:F2},{2:F2})",
                value.x,
                value.y,
                value.z);
        }

        private void Cleanup()
        {
            arenaTileInjection?.Dispose();
            arenaTileInjection = null;
            if (generator != null)
                generator.Clear(false);
            generationFlow?.Dispose();
            generationFlow = null;
            for (int i = 0; i < overlayMaterials.Count; i++)
            {
                if (overlayMaterials[i] != null)
                    UnityEngine.Object.DestroyImmediate(
                        overlayMaterials[i]);
            }
            overlayMaterials.Clear();
            if (captureTexture != null)
                UnityEngine.Object.DestroyImmediate(captureTexture);
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
            if (cameraObject != null)
                UnityEngine.Object.DestroyImmediate(cameraObject);
            if (keyLightObject != null)
                UnityEngine.Object.DestroyImmediate(keyLightObject);
            if (fillLightObject != null)
                UnityEngine.Object.DestroyImmediate(fillLightObject);
            if (generatedRoot != null)
                UnityEngine.Object.DestroyImmediate(generatedRoot);
        }
    }

    private readonly struct CaptureSpec
    {
        private CaptureSpec(
            string fileName,
            Vector3 target,
            Vector3 viewDirection,
            float distance,
            bool orthographic,
            float orthographicSize)
        {
            FileName = fileName;
            Target = target;
            ViewDirection = viewDirection;
            Distance = distance;
            Orthographic = orthographic;
            OrthographicSize = orthographicSize;
        }

        public string FileName { get; }
        public Vector3 Target { get; }
        public Vector3 ViewDirection { get; }
        public float Distance { get; }
        public bool Orthographic { get; }
        public float OrthographicSize { get; }

        public static CaptureSpec Perspective(
            string fileName,
            Vector3 target,
            Vector3 viewDirection,
            float distance)
        {
            return new CaptureSpec(
                fileName,
                target,
                viewDirection,
                distance,
                false,
                0f);
        }

        public static CaptureSpec TopDown(
            string fileName,
            Vector3 target,
            float orthographicSize,
            float height)
        {
            return new CaptureSpec(
                fileName,
                target,
                Vector3.up,
                height,
                true,
                orthographicSize);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
