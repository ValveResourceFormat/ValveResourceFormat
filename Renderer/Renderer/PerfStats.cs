using System.Diagnostics;
using System.Text;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using QueryId = System.Int32;

namespace ValveResourceFormat.Renderer;

internal enum Counter
{
    SceneObjectInView,
    DrawCall,
    MeshletDispatch,
    MaterialChange,
    DirectionalShadowMap,
    BarnShadowMap,
    ShadowFaceSubmitted,
    ParticleSystem,
    ParticleDraw,
}

internal enum Metric
{
    ShadowAtlasUsage,
}

/// <summary>
/// Collects per frame rendering statistics.
/// </summary>
public class PerfStats
{
    private enum LightGroup
    {
        Omni,
        Spot,
        Barn,
        Rect,
        Environment,
    }

    private static readonly string[] LightGroupNames = ["omni", "spot", "barn", "rect", "directional"];

    // Declared after LightGroupNames so the instance created here sees it initialized (static initializers run in textual order).
    /// <summary>Counters for the frame currently being rendered. Collects nothing while <see cref="Capture"/> is off.</summary>
    internal static PerfStats Active { get; private set; } = new();

    /// <summary>Gets or sets whether statistics are actively collected this frame.</summary>
    public bool Capture { get; set; }

    /// <summary>Gets the CPU and GPU timings for the same frame. Captured independently of <see cref="Capture"/>.</summary>
    public Timings Timings { get; } = new();

    /// <summary>Gets the managed allocation and GC statistics for the same frame. Captured independently of <see cref="Capture"/>.</summary>
    public AllocStats Allocations { get; } = new();

    // Debug groups opened outside a marked frame are not timed.
    private bool timingFrame;

    private int suspendDepth;
    private bool Counting => Capture && suspendDepth == 0 && !IsNotOwningThread;

    // Need this since our renderer can move threads.
    private readonly Lock threadLock = new();
    private int owningThreadId;

    /// <summary>Initializes a new <see cref="PerfStats"/> owned by the current thread until a frame is marked.</summary>
    public PerfStats()
    {
        owningThreadId = Environment.CurrentManagedThreadId;
    }

    private bool IsNotOwningThread => Environment.CurrentManagedThreadId != owningThreadId;

    // Stats
    private readonly int[] counts = new int[Enum.GetValues<Counter>().Length];
    private readonly float[] floatMetrics = new float[Enum.GetValues<Metric>().Length];
    private readonly int[] lightsInView = new int[LightGroupNames.Length];
    private readonly int[] staticLightsInView = new int[LightGroupNames.Length];

    /// <summary>GPU primitives-generated queries for one in-flight frame, one segment per unsuspended span of draws.</summary>
    private sealed class TriangleQueryFrame
    {
        /// <summary>Query objects, reused once the frame slot comes back around.</summary>
        public List<int> Segments { get; } = [];

        /// <summary>Segments issued this frame. Entries beyond it are pooled but idle.</summary>
        public int SegmentsUsed { get; set; }

        public bool Pending { get; set; }
    }

    // The CPU runs ahead of the GPU, so results take a few frames to land and the displayed count lags accordingly.
    private const int TriangleFrameCount = 4;
    private readonly TriangleQueryFrame[] triangleFrames = CreateTriangleFrames();
    private int triangleFrameWrite;
    private bool triangleFrameActive;
    private bool triangleSegmentActive;
    private long trianglesRendered;

    private static TriangleQueryFrame[] CreateTriangleFrames()
    {
        var frames = new TriangleQueryFrame[TriangleFrameCount];

        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = new TriangleQueryFrame();
        }

        return frames;
    }

    // Cached scene totals, recomputed once per second
    private long totalTriangles;
    private int totalDrawCalls;
    private int totalSceneObjects;
    private int totalMaterials;
    private int totalParticleSystems;
    private readonly int[] totalLights = new int[LightGroupNames.Length];
    private readonly int[] totalStaticLights = new int[LightGroupNames.Length];
    private long lastTotalsUpdate;

    /// <summary>Suspends stat collection for the following draws until <see cref="ResumeTriangleCounter"/> is called. Nestable.</summary>
    internal void SuspendTriangleCounter()
    {
        if (IsNotOwningThread)
        {
            return;
        }

        if (suspendDepth++ == 0)
        {
            EndTriangleSegment();
        }
    }

    /// <summary>Resumes stat collection suspended by <see cref="SuspendTriangleCounter"/>.</summary>
    internal void ResumeTriangleCounter()
    {
        if (IsNotOwningThread)
        {
            return;
        }

        if (--suspendDepth == 0)
        {
            BeginTriangleSegment();
        }
    }

    /// <summary>Starts a primitive query covering the draws that follow.</summary>
    private void BeginTriangleSegment()
    {
        if (!triangleFrameActive || triangleSegmentActive)
        {
            return;
        }

        var frame = triangleFrames[triangleFrameWrite];

        if (frame.SegmentsUsed == frame.Segments.Count)
        {
            frame.Segments.Add(GL.GenQuery());
        }

        GL.BeginQuery(QueryTarget.PrimitivesGenerated, frame.Segments[frame.SegmentsUsed]);
        triangleSegmentActive = true;
    }

    /// <summary>Ends the primitive query opened by <see cref="BeginTriangleSegment"/>, banking it for the frame.</summary>
    private void EndTriangleSegment()
    {
        if (!triangleSegmentActive)
        {
            return;
        }

        GL.EndQuery(QueryTarget.PrimitivesGenerated);
        triangleSegmentActive = false;
        triangleFrames[triangleFrameWrite].SegmentsUsed++;
    }

    /// <summary>Increments a scalar counter.</summary>
    internal void Count(Counter counter, int amount = 1)
    {
        if (!Counting)
        {
            return;
        }

        counts[(int)counter] += amount;
    }

    /// <summary>Assigns a float counter, replacing any value set earlier this frame.</summary>
    internal void Set(Metric counter, float value)
    {
        if (!Counting)
        {
            return;
        }

        floatMetrics[(int)counter] = value;
    }

    /// <summary>Counts a direct GL draw call for the given node.</summary>
    internal void CountDrawCall(SceneNode node)
    {
        if (!Counting)
        {
            return;
        }

        counts[(int)Counter.DrawCall]++;
    }

    internal void CountIndirectDraw(int indirectDrawCount)
    {
        if (!Counting)
        {
            return;
        }

        counts[(int)Counter.MeshletDispatch] += indirectDrawCount;
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
        AddLine($"Scene objects:    drawn {counts[(int)Counter.SceneObjectInView]:N0} of {totalSceneObjects:N0} scene objects in {counts[(int)Counter.DrawCall]:N0} draw calls and {counts[(int)Counter.MeshletDispatch]:N0} meshlet dispatches ({totalDrawCalls:N0} total draw calls)", valueColor);
        AddLine($"Materials:        {counts[(int)Counter.MaterialChange]:N0} changes between drawcalls, {totalMaterials:N0} total materials in scene", valueColor);
        AddLine($"Dynamic Lights:   in view {FormatLightCounts(lightsInView, totalLights)} out of total {FormatLightCounts(totalLights, totalLights)}", valueColor);
        AddLine($"Static Lights:    in view {FormatLightCounts(staticLightsInView, totalStaticLights)} out of total {FormatLightCounts(totalStaticLights, totalStaticLights)}", valueColor);
        AddLine($"Shadow maps:      {counts[(int)Counter.DirectionalShadowMap]:N0} directional, {counts[(int)Counter.BarnShadowMap]:N0} barn, {counts[(int)Counter.ShadowFaceSubmitted]:N0} faces binned, {floatMetrics[(int)Metric.ShadowAtlasUsage]:0%} atlas utilization", valueColor);
        AddLine($"Particle Systems: {counts[(int)Counter.ParticleSystem]:N0} particle systems rendered in {counts[(int)Counter.ParticleDraw]:N0} draw calls out of {totalParticleSystems:N0} total particle systems", valueColor);
    }

    /// <summary>Resets per-frame counters, reads back the previous frame's results.</summary>
    public void MarkFrameBegin()
    {
        // Held for the rest of the method, and spanning Timings, so that one edge publishes both collectors.
        using var _ = threadLock.EnterScope();
        owningThreadId = Environment.CurrentManagedThreadId;

        Active = this;

        Timings.MarkFrameBegin();
        timingFrame = Timings.Capture;

        Allocations.MarkFrameBegin();

        if (!Capture)
        {
            return;
        }

        suspendDepth = 0;
        Array.Clear(counts);
        Array.Clear(lightsInView);
        Array.Clear(staticLightsInView);
        Array.Clear(floatMetrics);

        // Oldest first, keeping the newest result that has landed. The slot about to be written is the oldest.
        for (var i = 0; i < TriangleFrameCount; i++)
        {
            var frame = triangleFrames[(triangleFrameWrite + i) % TriangleFrameCount];

            if (!frame.Pending)
            {
                continue;
            }

            // Segments complete in submission order, so the last one stands in for the whole frame.
            var lastSegment = frame.Segments[frame.SegmentsUsed - 1];
            GL.GetQueryObject(lastSegment, GetQueryObjectParam.QueryResultAvailable, out long available);

            if (available == 0)
            {
                break; // nothing newer is ready either
            }

            trianglesRendered = SumTriangleSegments(frame);
        }

        var writeFrame = triangleFrames[triangleFrameWrite];

        if (writeFrame.Pending)
        {
            // Every slot is in flight, so block rather than reuse these query objects and drop their result.
            trianglesRendered = SumTriangleSegments(writeFrame);
        }

        writeFrame.SegmentsUsed = 0;
        triangleFrameActive = true;
        BeginTriangleSegment();
    }

    /// <summary>Reads and totals a frame's segment results, blocking until the GPU has finished them.</summary>
    private static long SumTriangleSegments(TriangleQueryFrame frame)
    {
        var total = 0L;

        for (var i = 0; i < frame.SegmentsUsed; i++)
        {
            GL.GetQueryObject(frame.Segments[i], GetQueryObjectParam.QueryResult, out long result);
            total += result;
        }

        frame.Pending = false;

        return total;
    }

    /// <summary>Ends this frame's counters and timings.</summary>
    public void MarkFrameEnd()
    {
        using var _ = threadLock.EnterScope();

        // Keyed off the query having begun rather than Capture, which may have been toggled mid-frame.
        if (triangleFrameActive)
        {
            EndTriangleSegment();

            var frame = triangleFrames[triangleFrameWrite];
            frame.Pending = frame.SegmentsUsed > 0;
            triangleFrameActive = false;
            triangleFrameWrite = (triangleFrameWrite + 1) % TriangleFrameCount;
        }

        // Timed up to here, so that text rendering is still measured.
        timingFrame = false;
        Timings.MarkFrameEnd();
        Allocations.MarkFrameEnd();
    }

    /// <summary>Begins a timing query for a debug group, or returns 0 if this frame is not being timed.</summary>
    internal QueryId BeginTimingQuery(string name)
    {
        if (!timingFrame || IsNotOwningThread)
        {
            return 0;
        }

        return Timings.BeginQuery(name);
    }

    /// <summary>Ends a timing query opened by <see cref="BeginTimingQuery"/>.</summary>
    internal void EndTimingQuery(QueryId id)
    {
        if (!timingFrame || IsNotOwningThread)
        {
            return;
        }

        Timings.EndQuery(id);
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    public void Dispose()
    {
        Timings.Dispose();
        Allocations.Dispose();

        foreach (var frame in triangleFrames)
        {
            foreach (var segment in frame.Segments)
            {
                GL.DeleteQuery(segment);
            }

            frame.Segments.Clear();
            frame.SegmentsUsed = 0;
            frame.Pending = false;
        }
    }
}
