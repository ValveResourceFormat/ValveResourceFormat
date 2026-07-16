using System.Diagnostics;
using System.Text;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Collects per frame rendering statistics (draw calls, triangles, lights, etc).
/// </summary>
public class Counters
{
    /// <summary>Counters for the frame currently being rendered. Always non-null; collects nothing while <see cref="Capture"/> is off.</summary>
    internal static Counters Active { get; private set; } = new();

    /// <summary>Gets or sets whether statistics are actively collected this frame.</summary>
    public bool Capture { get; set; }

    // Counting is temporarily suspended for passes that should not contribute to stats (shadow and depth prepass).
    private bool suspended;
    private bool Counting => Capture && !suspended;

    private enum LightGroup
    {
        Omni,
        Spot,
        Barn,
        Rect,
        Environment,
    }

    private static readonly string[] LightGroupNames = ["omni", "spot", "barn", "rect", "directional"];

    // Per-frame counters
    private int drawCalls;
    private int meshletDispatches;
    private int materialChanges;
    private int shadowMapsRendered;
    private int particleSystemsRendered;
    private int particleDrawCalls;
    private readonly int[] lightsInView = new int[LightGroupNames.Length];
    private readonly int[] staticLightsInView = new int[LightGroupNames.Length];
    private readonly HashSet<SceneNode> drawnNodes = [];

    // GPU primitive queries measure the triangles actually rendered (including GPU-culled indirect
    // draws and particle effects). Results are read back with one frame of latency.
    private readonly List<int> primitiveQueries = [];
    private int primitiveQueriesUsed;
    private long trianglesRendered;

    // Cached scene totals, recomputed once per second
    private long totalTriangles;
    private int totalDrawCalls;
    private int totalSceneObjects;
    private int totalMaterials;
    private int totalParticleSystems;
    private readonly int[] totalLights = new int[LightGroupNames.Length];
    private readonly int[] totalStaticLights = new int[LightGroupNames.Length];
    private long lastTotalsUpdate;

    /// <summary>Suspends stat collection for the following draws until <see cref="ResumeCounting"/> is called. Used to exclude shadow and depth prepass draws.</summary>
    internal void SuspendCounting() => suspended = true;

    /// <summary>Resumes stat collection suspended by <see cref="SuspendCounting"/>.</summary>
    internal void ResumeCounting() => suspended = false;

    /// <summary>Counts a direct GL draw call for the given node.</summary>
    internal void CountDrawCall(SceneNode node)
    {
        if (!Counting)
        {
            return;
        }

        drawCalls++;
        drawnNodes.Add(node);
    }

    /// <summary>Counts an indirect multi-draw submission of an aggregate's meshlets. The meshlet count is as submitted, before GPU culling; triangles are measured by the surrounding primitive query instead.</summary>
    internal void CountIndirectDraw(SceneAggregate aggregate)
    {
        if (!Counting)
        {
            return;
        }

        meshletDispatches += aggregate.IndirectDrawCount;
        drawnNodes.Add(aggregate);
    }

    /// <summary>
    /// Begins a GL primitives-generated query so triangles rasterized by the following draws are
    /// measured on the GPU. Must be paired with <see cref="EndPrimitiveQuery"/>, and queries must not nest.
    /// </summary>
    internal void BeginPrimitiveQuery()
    {
        if (!Counting)
        {
            return;
        }

        if (primitiveQueriesUsed == primitiveQueries.Count)
        {
            primitiveQueries.Add(GL.GenQuery());
        }

        GL.BeginQuery(QueryTarget.PrimitivesGenerated, primitiveQueries[primitiveQueriesUsed]);
        primitiveQueriesUsed++;
    }

    /// <summary>Ends the primitives-generated query started by <see cref="BeginPrimitiveQuery"/>.</summary>
    internal void EndPrimitiveQuery()
    {
        if (!Counting)
        {
            return;
        }

        GL.EndQuery(QueryTarget.PrimitivesGenerated);
    }

    /// <summary>Counts a node that renders itself outside of the mesh batcher (physics shapes, sprites, etc).</summary>
    internal void CountCustomNode(SceneNode node)
    {
        if (!Counting || node is ParticleSceneNode)
        {
            return; // particles are counted separately as particle systems
        }

        drawnNodes.Add(node);
    }

    /// <summary>Counts a material state change in the mesh batcher.</summary>
    internal void CountMaterialChange()
    {
        if (!Counting)
        {
            return;
        }

        materialChanges++;
    }

    /// <summary>Counts one rendered shadow map (sun pass or one barn light face).</summary>
    internal void CountShadowMap()
    {
        if (!Counting)
        {
            return;
        }

        shadowMapsRendered++;
    }

    /// <summary>Counts a light that passed frustum culling this frame.</summary>
    internal void CountLightInView(SceneLight light)
    {
        if (!Counting)
        {
            return;
        }

        if (SceneLight.IsRealTimeLight(light))
        {
            lightsInView[(int)GetLightGroup(light)]++;
        }
        else
        {
            staticLightsInView[(int)GetLightGroup(light)]++;
        }
    }

    /// <summary>Counts one particle system that rendered this frame.</summary>
    internal void CountParticleSystem()
    {
        if (!Counting)
        {
            return;
        }

        particleSystemsRendered++;
    }

    /// <summary>Counts a GL draw call issued by a particle renderer.</summary>
    internal void CountParticleDraw()
    {
        if (!Counting)
        {
            return;
        }

        particleDrawCalls++;
    }

    private static LightGroup GetLightGroup(SceneLight light) => light.Entity switch
    {
        SceneLight.EntityType.Omni or SceneLight.EntityType.Omni2 => LightGroup.Omni,
        SceneLight.EntityType.Spot => LightGroup.Spot,
        SceneLight.EntityType.Barn => LightGroup.Barn,
        SceneLight.EntityType.Rect => LightGroup.Rect,
        _ => LightGroup.Environment,
    };

    private void UpdateTotals(Scene scene, Scene? skyboxScene)
    {
        if (lastTotalsUpdate != 0 && Stopwatch.GetElapsedTime(lastTotalsUpdate).TotalSeconds < 1.0)
        {
            return;
        }

        lastTotalsUpdate = Stopwatch.GetTimestamp();

        totalTriangles = 0;
        totalDrawCalls = 0;
        totalSceneObjects = 0;
        totalParticleSystems = 0;
        Array.Clear(totalLights);
        Array.Clear(totalStaticLights);

        AccumulateTotals(scene);

        if (skyboxScene != null)
        {
            AccumulateTotals(skyboxScene);
        }

        totalMaterials = scene.RendererContext.MaterialLoader.MaterialCount;
    }

    private void AccumulateTotals(Scene scene)
    {
        foreach (var node in scene.AllNodes)
        {
            totalSceneObjects++;

            switch (node)
            {
                case SceneAggregate.Fragment:
                    break; // draw calls are shared with and counted by the parent aggregate

                case SceneAggregate aggregate:
                    {
                        var instanceCount = Math.Max(1, aggregate.InstanceTransforms.Count);

                        foreach (var drawCall in aggregate.RenderMesh.DrawCalls)
                        {
                            totalDrawCalls++;
                            totalTriangles += (long)(drawCall.IndexCount / 3) * instanceCount;
                        }

                        break;
                    }

                case MeshCollectionNode meshCollection:
                    {
                        foreach (var mesh in meshCollection.RenderableMeshes)
                        {
                            foreach (var drawCall in mesh.DrawCalls)
                            {
                                totalDrawCalls++;
                                totalTriangles += drawCall.IndexCount / 3;
                            }
                        }

                        break;
                    }

                case SceneLight light:
                    if (SceneLight.IsRealTimeLight(light))
                    {
                        totalLights[(int)GetLightGroup(light)]++;
                    }
                    else
                    {
                        totalStaticLights[(int)GetLightGroup(light)]++;
                    }
                    break;

                case ParticleSceneNode:
                    totalParticleSystems++;
                    break;

                default:
                    break;
            }
        }
    }

    private static string FormatLightCounts(int[] counts, int[] visibleGroups)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < counts.Length; i++)
        {
            if (visibleGroups[i] == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(counts[i]);
            sb.Append(' ');
            sb.Append(LightGroupNames[i]);
        }

        return sb.Length > 0 ? sb.ToString() : "none";
    }

    /// <summary>
    /// Renders the collected statistics to screen using the provided text renderer.
    /// </summary>
    /// <param name="textRenderer">Text renderer to use for display.</param>
    /// <param name="camera">Camera for positioning text.</param>
    /// <param name="scene">Main scene, used to compute map totals.</param>
    /// <param name="skyboxScene">Optional 3D skybox scene, included in map totals.</param>
    /// <param name="x">X position (0-1 as fraction of screen width).</param>
    /// <param name="y">Y position (0-1 as fraction of screen height).</param>
    /// <param name="scale">Text scale.</param>
    public void DisplayStats(TextRenderer textRenderer, Camera camera, Scene scene, Scene? skyboxScene, float x = 0.02f, float y = 0.05f, float scale = 11f)
    {
        if (!Capture)
        {
            return;
        }

        UpdateTotals(scene, skyboxScene);

        var lineHeight = scale * 1.5f / camera.WindowSize.Y;
        var valueColor = new Color32(150, 255, 150);
        var offset = y;

        void AddLine(string text, Color32 color)
        {
            textRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
            {
                X = x,
                Y = offset,
                Scale = scale,
                Color = color,
                Text = text,
            }, camera);

            offset += lineHeight;
        }

        AddLine("Render Stats", new Color32(255, 200, 0));

        AddLine($"Triangles:        rendered {trianglesRendered:N0} of {totalTriangles:N0}", valueColor);
        AddLine($"Scene objects:    drawn {drawnNodes.Count:N0} of {totalSceneObjects:N0} scene objects in {drawCalls:N0} draw calls and {meshletDispatches:N0} meshlet dispatches ({totalDrawCalls:N0} total draw calls)", valueColor);
        AddLine($"Materials:        {materialChanges:N0} changes between drawcalls, {totalMaterials:N0} total materials in scene", valueColor);
        AddLine($"Dynamic Lights:   in view {FormatLightCounts(lightsInView, totalLights)} out of total {FormatLightCounts(totalLights, totalLights)}", valueColor);
        AddLine($"Static Lights:    in view {FormatLightCounts(staticLightsInView, totalStaticLights)} out of total {FormatLightCounts(totalStaticLights, totalStaticLights)}", valueColor);
        AddLine($"Shadow maps:      {shadowMapsRendered:N0}", valueColor);
        AddLine($"Particle Systems: {particleSystemsRendered:N0} particle systems rendered in {particleDrawCalls:N0} draw calls out of {totalParticleSystems:N0} total particle systems", valueColor);
    }

    /// <summary>Resets per-frame counters and makes these counters the active collection target.</summary>
    public void MarkFrameBegin()
    {
        Active = this;

        if (!Capture)
        {
            return;
        }

        suspended = false;

        // Collect the primitive query results submitted last frame
        trianglesRendered = 0;
        for (var i = 0; i < primitiveQueriesUsed; i++)
        {
            GL.GetQueryObject(primitiveQueries[i], GetQueryObjectParam.QueryResult, out long result);
            trianglesRendered += result;
        }

        primitiveQueriesUsed = 0;

        drawCalls = 0;
        meshletDispatches = 0;
        materialChanges = 0;
        shadowMapsRendered = 0;
        particleSystemsRendered = 0;
        particleDrawCalls = 0;
        Array.Clear(lightsInView);
        Array.Clear(staticLightsInView);
        drawnNodes.Clear();
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var query in primitiveQueries)
        {
            GL.DeleteQuery(query);
        }

        primitiveQueries.Clear();
        primitiveQueriesUsed = 0;
    }
}
