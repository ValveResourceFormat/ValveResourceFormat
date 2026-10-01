using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>Which light probe volumes show their sample grid.</summary>
public enum LightProbeDebugGridMode
{
    /// <summary>No grids.</summary>
    Off,

    /// <summary>Only the volume chosen for the camera position.</summary>
    Closest,

    /// <summary>The volume chosen for the camera position, and every volume shown before it.</summary>
    ClosestAndKeep,

    /// <summary>Every volume.</summary>
    All,
}

/// <summary>What the environment map debug colours show.</summary>
public enum EnvMapDebugColorsMode
{
    /// <summary>Nothing.</summary>
    Off,

    /// <summary>Markers, and every reflection in the colour of the environment map it comes from.</summary>
    Reflections,

    /// <summary>Only the markers.</summary>
    Markers,
}

/// <summary>
/// Debug views of a scene's baked lighting: the sample grid of light probe volumes, and a marker in a
/// distinct colour for each environment map with the one chosen for the camera outlined and labelled.
/// The views are scene nodes, created and destroyed as they are switched on and off.
/// </summary>
public sealed class SceneLightingDebug
{
    private const string LayerName = "Internal - Lighting Debug";

    // Larger grids are not drawn at all rather than truncated
    private const int MaxGridAxisSamples = 128;
    private const int MaxGridSamples = 0x200000;

    private const float EnvMapMarkerRadius = 6f;
    private const float EnvMapLabelDrop = 10f;
    private const float LabelLineHeight = 18f;

    private readonly Scene scene;
    private readonly Dictionary<SceneLightProbe, ProbeGrid> probeGrids = [];
    private readonly Dictionary<SceneEnvMap, MarkerSphere> envMapMarkers = [];

    private bool reflectionColorsShown;
    private SceneEnvMap? chosenEnvMap;
    private LineSceneNode? chosenEnvMapBounds;

    private sealed record ProbeGrid(LightProbeSampleGridSceneNode Samples, LineSceneNode? Bounds);

    private sealed class MarkerSphere(Scene scene, Vector3 center, float radius, Color32 color)
        : ShapeSceneNode(scene, center, radius, color)
    {
        public override bool IsTranslucent => false;
    }

    internal SceneLightingDebug(Scene scene)
    {
        this.scene = scene;
    }

    /// <summary>Gets or sets which light probe volumes show their sample grid.</summary>
    public LightProbeDebugGridMode LightProbeGrid { get; set; }

    /// <summary>Gets or sets whether a volume showing its grid is also outlined, in its debug colour.</summary>
    public bool LightProbeGridBounds { get; set; } = true;

    /// <summary>Gets or sets the radius of a grid sample, in world units.</summary>
    public float LightProbeGridSampleSize { get; set; } = 4f;

    /// <summary>Gets or sets whether grid samples are drawn as cubes rather than spheres.</summary>
    public bool LightProbeGridCubes { get; set; }

    /// <summary>Gets or sets the surface colour of grid samples, in sRGB.</summary>
    public Color32 LightProbeGridAlbedo { get; set; } = new(128, 128, 128, 255);

    /// <summary>Gets or sets the roughness of grid samples.</summary>
    public float LightProbeGridRoughness { get; set; } = 0.5f;

    /// <summary>Gets or sets the metalness of grid samples.</summary>
    public float LightProbeGridMetalness { get; set; }

    /// <summary>Gets or sets what the environment map debug colours show.</summary>
    public EnvMapDebugColorsMode EnvMapColors { get; set; }

    internal void Update(Scene.UpdateContext updateContext)
    {
        var viewPosition = updateContext.Camera.Location;

        UpdateProbeGrids(viewPosition);
        UpdateEnvMapMarkers(viewPosition, updateContext);

        var showReflectionColors = EnvMapColors == EnvMapDebugColorsMode.Reflections;

        if (showReflectionColors != reflectionColorsShown)
        {
            reflectionColorsShown = showReflectionColors;
            scene.SetEnvMapDebugColors(showReflectionColors);
        }
    }

    internal void Clear()
    {
        foreach (var grid in probeGrids.Values)
        {
            Destroy(grid.Samples);
            Destroy(grid.Bounds);
        }

        probeGrids.Clear();

        foreach (var marker in envMapMarkers.Values)
        {
            Destroy(marker);
        }

        envMapMarkers.Clear();

        Destroy(chosenEnvMapBounds);
        chosenEnvMapBounds = null;
        chosenEnvMap = null;
    }

    private void UpdateProbeGrids(Vector3 viewPosition)
    {
        if (LightProbeGrid == LightProbeDebugGridMode.Off)
        {
            if (probeGrids.Count > 0)
            {
                foreach (var grid in probeGrids.Values)
                {
                    Destroy(grid.Samples);
                    Destroy(grid.Bounds);
                }

                probeGrids.Clear();
            }

            return;
        }

        var closest = LightProbeGrid == LightProbeDebugGridMode.All ? null : scene.ChooseLightProbeVolume(viewPosition);

        foreach (var probe in scene.LightingInfo.LightProbes)
        {
            probeGrids.TryGetValue(probe, out var grid);

            var show = LightProbeGrid switch
            {
                LightProbeDebugGridMode.All => true,
                LightProbeDebugGridMode.Closest => probe == closest,
                _ => probe == closest || grid != null,
            };

            show &= probe.LayerEnabled;

            if (grid != null && (!show || (grid.Bounds != null) != LightProbeGridBounds))
            {
                Destroy(grid.Samples);
                Destroy(grid.Bounds);
                probeGrids.Remove(probe);
                grid = null;
            }

            if (show && grid == null && CreateProbeGrid(probe) is { } created)
            {
                probeGrids[probe] = grid = created;
            }

            if (grid != null)
            {
                var samples = grid.Samples;
                samples.SampleSize = LightProbeGridSampleSize;
                samples.Cubes = LightProbeGridCubes;
                samples.Albedo = LightProbeGridAlbedo;
                samples.Roughness = LightProbeGridRoughness;
                samples.Metalness = LightProbeGridMetalness;
            }
        }
    }

    private ProbeGrid? CreateProbeGrid(SceneLightProbe probe)
    {
        if (!TryGetSampleCounts(probe, out var counts))
        {
            return null;
        }

        var bounds = probe.LocalBoundingBox;
        var spacing = bounds.Size / counts;
        var first = bounds.Min + spacing * 0.5f;

        var localSamples = new Vector3[(int)(counts.X * counts.Y * counts.Z)];
        var i = 0;

        for (var z = 0; z < counts.Z; z++)
        {
            for (var y = 0; y < counts.Y; y++)
            {
                for (var x = 0; x < counts.X; x++)
                {
                    localSamples[i++] = first + new Vector3(x, y, z) * spacing;
                }
            }
        }

        var samples = new LightProbeSampleGridSceneNode(scene, probe, localSamples)
        {
            LayerName = LayerName,
        };

        scene.Add(samples, true);

        LineSceneNode? outline = null;

        if (LightProbeGridBounds)
        {
            outline = CreateOutline(bounds, probe.Transform, LightingDebugColor.FromOrigin(probe.Transform.Translation));
        }

        return new ProbeGrid(samples, outline);
    }

    /// <summary>
    /// The samples per axis: as the volume's atlas region declares them, otherwise one per texel of its
    /// irradiance texture, which stacks six directions along depth.
    /// </summary>
    private static bool TryGetSampleCounts(SceneLightProbe probe, out Vector3 counts)
    {
        if (probe.AtlasSize is { X: > 0, Y: > 0, Z: > 0 } atlasSize)
        {
            counts = Vector3.Min(atlasSize, new Vector3(MaxGridAxisSamples));
        }
        else if (probe.Irradiance is { } irradiance && irradiance.Depth >= 6)
        {
            counts = Vector3.Min(
                new Vector3(irradiance.Width, irradiance.Height, irradiance.Depth / 6),
                new Vector3(MaxGridAxisSamples));
        }
        else
        {
            counts = default;
            return false;
        }

        return counts.X * counts.Y * counts.Z <= MaxGridSamples;
    }

    private void UpdateEnvMapMarkers(Vector3 viewPosition, Scene.UpdateContext updateContext)
    {
        if (EnvMapColors == EnvMapDebugColorsMode.Off)
        {
            if (envMapMarkers.Count > 0 || chosenEnvMapBounds != null)
            {
                foreach (var marker in envMapMarkers.Values)
                {
                    Destroy(marker);
                }

                envMapMarkers.Clear();

                Destroy(chosenEnvMapBounds);
                chosenEnvMapBounds = null;
                chosenEnvMap = null;
            }

            return;
        }

        foreach (var envMap in scene.LightingInfo.EnvMaps)
        {
            // The default lighting's map covers everything and has no placement to mark
            if (envMap.EntityData == null || envMapMarkers.ContainsKey(envMap))
            {
                continue;
            }

            var marker = new MarkerSphere(scene, envMap.Transform.Translation, EnvMapMarkerRadius, LightingDebugColor.FromOrigin(envMap.Transform.Translation))
            {
                LayerName = LayerName,
            };

            scene.Add(marker, true);
            envMapMarkers[envMap] = marker;
        }

        var chosen = scene.ChooseEnvironmentMap(viewPosition);

        if (chosen?.EntityData == null)
        {
            chosen = null;
        }

        if (chosen != chosenEnvMap)
        {
            Destroy(chosenEnvMapBounds);
            chosenEnvMapBounds = chosen == null
                ? null
                : CreateOutline(chosen.LocalBoundingBox, chosen.Transform, LightingDebugColor.FromOrigin(chosen.Transform.Translation));
            chosenEnvMap = chosen;
        }

        if (chosen != null)
        {
            var color = LightingDebugColor.FromOrigin(chosen.Transform.Translation);
            var labelPosition = chosen.Transform.Translation - new Vector3(0f, 0f, EnvMapLabelDrop);

            AddLabel(updateContext, labelPosition, chosen.EntityData!.GetStringProperty("cubemaptexture") ?? string.Empty, color, 0f);
            AddLabel(updateContext, labelPosition, $"Index: {chosen.ShaderIndex}", color, LabelLineHeight);
        }
    }

    private static void AddLabel(Scene.UpdateContext updateContext, Vector3 position, string text, Color32 color, float lineOffset)
    {
        updateContext.TextRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
        {
            Scale = 15f,
            Color = color,
            Text = text,
            CenterHorizontal = true,
            TextOffset = new Vector2(0f, lineOffset),
        }, updateContext.Camera);
    }

    private LineSceneNode CreateOutline(in AABB bounds, in Matrix4x4 transform, Color32 color)
    {
        var vertices = new List<SimpleVertex>(2 * 12);
        ShapeSceneNode.AddBox(vertices, bounds, color);

        var outline = new LineSceneNode(scene, [.. vertices])
        {
            LayerName = LayerName,
            Transform = transform,
        };

        scene.Add(outline, true);

        return outline;
    }

    private void Destroy(SceneNode? node)
    {
        if (node == null)
        {
            return;
        }

        scene.Remove(node, true);
        node.Delete();
    }
}
