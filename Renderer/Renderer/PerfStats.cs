using System.Diagnostics;
using System.Text;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using QueryId = System.Int32;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Collects per frame CPU/GPU performance statistics 
/// </summary>
public class PerfStats
{
    /// <summary>Gets or sets whether performance statistics are actively collected this frame.</summary>
    public bool Capture { get; set; }

    #region Timings

    private record struct TimingQuery(long StartTimestamp, bool SubmitGpuQueries, string Name, int Depth, QueryId Id);
    private readonly record struct TimingResult(string Name, double TimeMs, double TimeMsGpu, int Depth, QueryId Id);

    private readonly Dictionary<QueryId, TimingQuery> activeQueries = [];
    private readonly Dictionary<QueryId, int> gpuStartQueries = [];
    private readonly Dictionary<QueryId, int> gpuEndQueries = [];
    private readonly Dictionary<QueryId, double> gpuTimingsCache = [];
    private readonly Dictionary<QueryId, double> previousMax = [];
    private readonly Dictionary<QueryId, double> currentMax = [];
    private long lastRollingUpdate;
    private readonly SortedDictionary<QueryId, TimingResult> results = [];
    private int currentIndex;
    private int currentDepth;

    private const int NameColumnWidth = 40;

    private readonly Lock threadLock = new();
    private int owningThreadId;

    /// <summary>Initializes a new <see cref="PerfStats"/> instance owned by the current thread.</summary>
    public PerfStats()
    {
        owningThreadId = Environment.CurrentManagedThreadId;
    }

    private bool IsNotOwningThread => Environment.CurrentManagedThreadId != owningThreadId;

    /// <summary>
    /// Begins a new timing measurement for the specified name.
    /// </summary>
    /// <param name="name">Name of the region to time.</param>
    /// <returns>Query ID to use when ending the query, or 0 if timing is disabled.</returns>
    public QueryId BeginQuery(string name)
    {
        if (!Capture || IsNotOwningThread)
        {
            return 0;
        }

        currentIndex++;

        var endQueryId = 0;
        if (!gpuStartQueries.TryGetValue(currentIndex, out var startQueryId))
        {
            startQueryId = GL.GenQuery();
            gpuStartQueries[currentIndex] = startQueryId;

            endQueryId = GL.GenQuery();
            gpuEndQueries[currentIndex] = endQueryId;

            Debug.Assert(startQueryId != 0 && endQueryId != 0, "Failed to generate GPU query objects.");
        }

        if (activeQueries.TryGetValue(currentIndex, out var activeQuery))
        {
            if (activeQuery.Name != name)
            {
                activeQuery = activeQuery with { Name = name };
                for (var i = currentIndex; i < activeQueries.Count; i++)
                {
                    gpuTimingsCache.Remove(i);
                    previousMax.Remove(i);
                    currentMax.Remove(i);
                }
            }

            if (activeQuery.SubmitGpuQueries)
            {
                GL.QueryCounter(gpuStartQueries[activeQuery.Id], QueryCounterTarget.Timestamp);
            }

            activeQueries[currentIndex] = activeQuery with { StartTimestamp = Stopwatch.GetTimestamp() };
            return currentIndex;
        }

        GL.QueryCounter(startQueryId, QueryCounterTarget.Timestamp);
        GL.QueryCounter(endQueryId, QueryCounterTarget.Timestamp);
        activeQueries[currentIndex] = new TimingQuery(Stopwatch.GetTimestamp(), true, name, currentDepth, currentIndex);
        currentDepth++;

        return currentIndex;
    }

    /// <summary>
    /// Ends a timing measurement.
    /// </summary>
    /// <param name="id">Query ID returned from BeginQuery.</param>
    public void EndQuery(QueryId id)
    {
        if (!Capture || id == 0 || IsNotOwningThread)
        {
            return;
        }

        var endTimestamp = Stopwatch.GetTimestamp();

        if (activeQueries.TryGetValue(id, out var query))
        {
            var elapsed = Stopwatch.GetElapsedTime(query.StartTimestamp, endTimestamp);

            // carry forward previous GPU time if new GPU time is not available
            var elapsedGpuMs = gpuTimingsCache.GetValueOrDefault(query.Id);

            var startQueryId = gpuStartQueries[query.Id];
            var endQueryId = gpuEndQueries[query.Id];

            var resubmitQueries = false;
            if (query.SubmitGpuQueries)
            {
                GL.QueryCounter(endQueryId, QueryCounterTarget.Timestamp);
            }
            else
            {
                GL.GetQueryObject(startQueryId, GetQueryObjectParam.QueryResultNoWait, out long startTimestampGpu);
                GL.GetQueryObject(endQueryId, GetQueryObjectParam.QueryResultNoWait, out long endTimestampGpu);
                if (startTimestampGpu != 0)
                {
                    if (endTimestampGpu != 0)
                    {
                        elapsedGpuMs = (endTimestampGpu - startTimestampGpu) / 1_000_000.0; // convert nanoseconds to milliseconds
                        gpuTimingsCache[query.Id] = elapsedGpuMs;
                        resubmitQueries = true;
                    }
                }
            }

            activeQueries[id] = query with { SubmitGpuQueries = resubmitQueries };

            results[id] = new TimingResult(query.Name, elapsed.TotalMilliseconds, elapsedGpuMs, query.Depth, query.Id);
        }

        currentDepth--;
        currentDepth = Math.Max(currentDepth, 0);
    }

    private void DisplayTimings(TextRenderer textRenderer, Camera camera, float x, ref float yOffset, float scale)
    {
        if (results.Count == 0)
        {
            return;
        }

        var lineHeight = scale * 1.5f / camera.WindowSize.Y;

        // Header
        textRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
        {
            X = x,
            Y = yOffset,
            Scale = scale,
            Color = new Color32(255, 200, 0),
            Text = $"Render Timings  {"",-NameColumnWidth + 14} {"GPU",6} {"CPU",6} {"P100",6}"
        }, camera);

        yOffset += lineHeight;

        var totalCpu = 0.0;
        var totalGpu = 0.0;
        var total = 0.0;

        if (Stopwatch.GetElapsedTime(lastRollingUpdate).TotalSeconds > 1.0)
        {
            // Shift current max to previous and reset current max for new rolling window
            foreach (var result in results.Values)
            {
                var max = Math.Max(result.TimeMs, result.TimeMsGpu);
                previousMax[result.Id] = currentMax.GetValueOrDefault(result.Id, max);
                currentMax[result.Id] = 0;
            }

            lastRollingUpdate = Stopwatch.GetTimestamp();
        }

        foreach (var result in results.Values)
        {
            var maxTimeCurrent = Math.Max(result.TimeMs, result.TimeMsGpu);
            currentMax[result.Id] = Math.Max(currentMax.GetValueOrDefault(result.Id, 0), maxTimeCurrent);
            var maxTime = previousMax.GetValueOrDefault(result.Id, maxTimeCurrent);

            if (result.Depth == 0)
            {
                totalCpu += result.TimeMs;
                totalGpu += result.TimeMsGpu;
                total += maxTime;
            }

            var color = maxTime switch
            {
                > 16.0 => new Color32(255, 0, 0),   // Red for >16ms (60fps threshold)
                > 8.0 => new Color32(255, 150, 0),  // Orange for >8ms
                > 2.0 => new Color32(255, 255, 0),  // Yellow for >2ms
                _ => new Color32(150, 255, 150)     // Light green for <2ms
            };

            var indent = new string(' ', result.Depth * 2);
            var displayName = $"{indent}{result.Name}";

            textRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
            {
                X = x,
                Y = yOffset,
                Scale = scale,
                Color = color,
                Text = $"  {displayName,-NameColumnWidth} {result.TimeMsGpu,6:0.00} {result.TimeMs,6:0.00} {maxTime,6:0.00}"
            }, camera);

            yOffset += lineHeight;
        }

        // Add total line
        textRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
        {
            X = x,
            Y = yOffset,
            Scale = scale,
            Color = Color32.White,
            Text = $"  {"Total",-NameColumnWidth} {totalGpu,6:0.00} {totalCpu,6:0.00} {total,6:0.00}"
        }, camera);

        yOffset += lineHeight;
    }

    #endregion

    #region Stats

    private enum LightGroup
    {
        Omni,
        Spot,
        Barn,
        Rect,
        Environment,
    }

    private static readonly string[] LightGroupNames = ["omni", "spot", "barn", "rect", "directional"];

    /// <summary>Stats collector for the frame currently being rendered, or <see langword="null"/> when capture is off.</summary>
    internal static PerfStats? Active { get; private set; }

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

    /// <summary>Counts a direct GL draw call for the given node.</summary>
    internal void CountDrawCall(SceneNode node)
    {
        drawCalls++;
        drawnNodes.Add(node);
    }

    /// <summary>Counts an indirect multi-draw submission of an aggregate's meshlets. The meshlet count is as submitted, before GPU culling; triangles are measured by the surrounding primitive query instead.</summary>
    internal void CountIndirectDraw(SceneAggregate aggregate)
    {
        meshletDispatches += aggregate.IndirectDrawCount;
        drawnNodes.Add(aggregate);
    }

    /// <summary>
    /// Begins a GL primitives-generated query so triangles rasterized by the following draws are
    /// measured on the GPU. Must be paired with <see cref="EndPrimitiveQuery"/>, and queries must not nest.
    /// </summary>
    internal void BeginPrimitiveQuery()
    {
        if (primitiveQueriesUsed == primitiveQueries.Count)
        {
            primitiveQueries.Add(GL.GenQuery());
        }

        GL.BeginQuery(QueryTarget.PrimitivesGenerated, primitiveQueries[primitiveQueriesUsed]);
        primitiveQueriesUsed++;
    }

    /// <summary>Ends the primitives-generated query started by <see cref="BeginPrimitiveQuery"/>.</summary>
    internal static void EndPrimitiveQuery()
    {
        GL.EndQuery(QueryTarget.PrimitivesGenerated);
    }

    /// <summary>Counts a node that renders itself outside of the mesh batcher (physics shapes, sprites, etc).</summary>
    internal void CountCustomNode(SceneNode node)
    {
        if (node is ParticleSceneNode)
        {
            return; // counted separately as particle systems
        }

        drawnNodes.Add(node);
    }

    /// <summary>Counts a material state change in the mesh batcher.</summary>
    internal void CountMaterialChange()
    {
        materialChanges++;
    }

    /// <summary>Counts one rendered shadow map (sun pass or one barn light face).</summary>
    internal void CountShadowMap()
    {
        shadowMapsRendered++;
    }

    /// <summary>Counts a light that passed frustum culling this frame.</summary>
    internal void CountLightInView(SceneLight light)
    {
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
        particleSystemsRendered++;
    }

    /// <summary>Counts a GL draw call issued by a particle renderer.</summary>
    internal void CountParticleDraw()
    {
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

    private void DisplayStats(TextRenderer textRenderer, Camera camera, Scene scene, Scene? skyboxScene, float x, ref float yOffset, float scale)
    {
        UpdateTotals(scene, skyboxScene);

        var lineHeight = scale * 1.5f / camera.WindowSize.Y;
        var valueColor = new Color32(150, 255, 150);
        var offset = yOffset; // local functions cannot capture ref parameters

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

        yOffset = offset;
    }

    #endregion

    /// <summary>
    /// Renders the collected timings and statistics to screen using the provided text renderer.
    /// </summary>
    /// <param name="textRenderer">Text renderer to use for display.</param>
    /// <param name="camera">Camera for positioning text.</param>
    /// <param name="scene">Main scene, used to compute map totals.</param>
    /// <param name="skyboxScene">Optional 3D skybox scene, included in map totals.</param>
    /// <param name="x">X position (0-1 as fraction of screen width).</param>
    /// <param name="y">Y position (0-1 as fraction of screen height).</param>
    /// <param name="scale">Text scale.</param>
    public void Display(TextRenderer textRenderer, Camera camera, Scene scene, Scene? skyboxScene, float x = 0.02f, float y = 0.05f, float scale = 11f)
    {
        if (!Capture)
        {
            return;
        }

        var yOffset = y;

        DisplayStats(textRenderer, camera, scene, skyboxScene, x, ref yOffset, scale);

        yOffset += scale * 1.5f / camera.WindowSize.Y; // gap between sections

        DisplayTimings(textRenderer, camera, x, ref yOffset, scale);
    }

    /// <summary>Resets per-frame state and assigns ownership of this frame's stats to the calling thread.</summary>
    public void MarkFrameBegin()
    {
        if (!Capture)
        {
            return;
        }

        using (threadLock.EnterScope())
        {
            owningThreadId = Environment.CurrentManagedThreadId;
            GLDebugGroup.PerfStats = this;
            currentIndex = 0;
        }

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

        Active = this;
    }

    /// <summary>
    /// Ends stat collection for this frame and transfers timing ownership to the calling thread.
    /// </summary>
    public void MarkFrameEnd()
    {
        if (Capture)
        {
            using var _ = threadLock.EnterScope();
            results.Clear();
            GLDebugGroup.PerfStats = null;
        }

        if (Active == this)
        {
            Active = null;
        }
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    public void Dispose()
    {
        activeQueries.Clear();
        results.Clear();

        foreach (var query in primitiveQueries)
        {
            GL.DeleteQuery(query);
        }

        primitiveQueries.Clear();
        primitiveQueriesUsed = 0;
    }
}
