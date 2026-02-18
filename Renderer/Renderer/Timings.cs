using System.Diagnostics;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using QueryId = System.Int32;

namespace ValveResourceFormat.Renderer;
/// <summary>
/// Utility class for measuring CPU and GPU timings of code regions.
/// </summary>
public class Timings
{
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

    public Timings()
    {
        owningThreadId = Environment.CurrentManagedThreadId;
    }

    public bool Capture { get; set; }
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

    /// <summary>
    /// Renders timing results to screen using the provided text renderer.
    /// </summary>
    /// <param name="textRenderer">Text renderer to use for display.</param>
    /// <param name="camera">Camera for positioning text.</param>
    /// <param name="x">X position (0-1 as fraction of screen width).</param>
    /// <param name="y">Y position (0-1 as fraction of screen height).</param>
    /// <param name="scale">Text scale.</param>
    public void DisplayTimings(TextRenderer textRenderer, Camera camera, float x = 0.02f, float y = 0.05f, float scale = 11f)
    {
        if (!Capture || results.Count == 0)
        {
            return;
        }

        var yOffset = y;
        var lineHeight = scale * 1.5f / camera.WindowSize.Y;

        // Header
        textRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
        {
            X = x,
            Y = yOffset,
            Scale = scale,
            Color = new Color32(255, 200, 0),
            Text = $"  {"",-NameColumnWidth} {"GPU",6} {"CPU",6} {"",6}"
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
    }

    public void MarkFrameBegin()
    {
        if (Capture)
        {
            using var _ = threadLock.EnterScope();
            owningThreadId = Environment.CurrentManagedThreadId;
            GLDebugGroup.Timings = this;
            currentIndex = 0;
        }
    }

    /// <summary>
    /// Clears all collected timing results and transfers ownership to the calling thread.
    /// </summary>
    public void MarkFrameEnd()
    {
        if (Capture)
        {
            using var _ = threadLock.EnterScope();
            results.Clear();
            GLDebugGroup.Timings = null;
        }
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    public void Dispose()
    {
        activeQueries.Clear();
        results.Clear();
    }
}
