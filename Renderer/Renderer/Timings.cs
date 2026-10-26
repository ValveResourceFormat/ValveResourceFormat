using System.Diagnostics;
using System.Globalization;
using OpenTK.Graphics.OpenGL;
using QueryId = System.Int32;

namespace ValveResourceFormat.Renderer;
/// <summary>
/// Utility class for measuring CPU and GPU timings of code regions.
/// </summary>
public class Timings
{
    /// <summary>
    /// A timed region, found again each frame by its parent and name rather than its position in the frame,
    /// so regions that come and go do not disturb the GPU queries and peaks of the others.
    /// </summary>
    private sealed class Region(string name)
    {
        public readonly string Name = name;
        public int Depth;
        public bool CpuOnly;
        public bool Collapsed;
        public long LastFrame = -1;
        public long StartTimestamp;

        public int StartQuery;
        public int EndQuery;
        public bool SubmitGpuQueries;
        public double GpuMs;

        // Peak of the last whole second, or null when the region did not run in it
        public double? PreviousMax;
        public double? CurrentMax;

        public bool Averaged;
        public double AveragedCpuMs;
        public int AveragedCpuSamples;
        public double AveragedGpuMs;
        public int AveragedGpuSamples;
    }

    private readonly record struct TimingResult(Region Region, double TimeMs, double TimeMsGpu, bool GpuFresh = false)
    {
        public string Name => Region.Name;
        public int Depth => Region.Depth;
        public bool CpuOnly => Region.CpuOnly;
        public bool Collapsed => Region.Collapsed;
    }

    /// <summary>Gets the average of a timed region over the frames since <see cref="StartAveraging"/>.</summary>
    /// <param name="Name">Name of the region.</param>
    /// <param name="Depth">How deep the region is nested in others.</param>
    /// <param name="CpuMs">Average CPU milliseconds.</param>
    /// <param name="CpuSamples">Frames the region ran in.</param>
    /// <param name="GpuMs">Average GPU milliseconds, or null for a region that submits no GPU work or got no GPU reading.</param>
    /// <param name="GpuSamples">GPU readings the average is made of. GPU times arrive a few frames late and not every frame.</param>
    internal readonly record struct TimingAverage(string Name, int Depth, double CpuMs, int CpuSamples, double? GpuMs, int GpuSamples);

    // Regions in the order they were first seen while averaging, or null when not averaging
    private List<Region>? averaging;

    /// <summary>Gets the number of frames summed since <see cref="StartAveraging"/>.</summary>
    internal int AveragedFrames { get; private set; }

    private readonly Dictionary<(Region? Parent, string Name, int Occurrence), Region> regions = [];
    private readonly List<Region> openRegions = [];
    private readonly List<Region> frameRegions = [];
    private long frame;
    private long lastRollingUpdate;
    private double swapMs;
    private double frameCpuMs;
    private double swapMaxCurrent;
    private double swapMaxPrevious;
    private double unaccountedMs;
    private double unaccountedMaxCurrent;
    private double unaccountedMaxPrevious;
    // Keyed by the order the regions began in this frame
    private readonly SortedDictionary<QueryId, TimingResult> results = [];

    private const int NameColumnWidth = 40;

    // A collapsed region still lists what is nested in it once it peaks above this
    private const double ExpandCollapsedMs = 1.0;

    private static readonly string ColumnHeader = $"  {"",-NameColumnWidth} {"GPU",6} {"CPU",6} {"P100",6}";

    // Rows are formatted into the arena each frame, which stays untouched until the text renders
    private readonly TextRenderer.TextArena displayText = new(16 * 1024);
    private readonly char[] nameColumn = new char[NameColumnWidth];

    private record struct DisplayRow(TimingResult Result, double PeakMs, bool Collapsed);
    private readonly List<DisplayRow> displayRows = [];

    private static Color32 ColorForPeak(double peakMs) => peakMs switch
    {
        > 16.0 => new Color32(255, 0, 0),   // Red for >16ms (60fps threshold)
        > 8.0 => new Color32(255, 150, 0),  // Orange for >8ms
        > 2.0 => new Color32(255, 255, 0),  // Yellow for >2ms
        _ => new Color32(150, 255, 150)     // Light green for <2ms
    };

    /// <summary>Gets or sets whether timing data is actively collected this frame.</summary>
    public bool Capture { get; set; }

    /// <summary>
    /// Records the swap wait and the frame period it belongs to, and folds them into the unaccounted
    /// remainder. Uses the total <see cref="MarkFrameEnd"/> snapshotted rather than the live results,
    /// which are cleared by then, and which the display only ever sees part of anyway.
    /// </summary>
    /// <param name="milliseconds">Wall time the swap call blocked for.</param>
    /// <param name="framePeriodMilliseconds">Wall time between this swap and the last, or 0 for a frame
    /// that should not be sampled at all, such as a paused one.</param>
    public void SetBufferSwapTime(double milliseconds, double framePeriodMilliseconds)
    {
        if (!Capture || framePeriodMilliseconds <= 0.0)
        {
            return;
        }

        swapMs = milliseconds;
        swapMaxCurrent = Math.Max(swapMaxCurrent, milliseconds);

        unaccountedMs = framePeriodMilliseconds - (frameCpuMs + milliseconds);
        unaccountedMaxCurrent = Math.Max(unaccountedMaxCurrent, unaccountedMs);
    }

    private readonly record struct AsyncRow(double? CpuMs, string Detail);
    private readonly SortedDictionary<string, AsyncRow> asyncRows = [];

    /// <summary>
    /// Records work measured off the frame - another thread, or a window that is not a frame - listed
    /// under an "Async" header below the frame's own rows. Replaced by name on each call, so the caller
    /// just sets it every frame from whatever the producing thread last published.
    /// </summary>
    /// <param name="name">Row label, unique per source.</param>
    /// <param name="cpuMs">CPU milliseconds to show, or null to leave the column blank for a row that is not a duration.</param>
    /// <param name="detail">Free-form right-hand column, e.g. a duty cycle or a byte count.</param>
    public void SetAsyncRow(string name, double? cpuMs, string detail = "")
    {
        asyncRows[name] = new AsyncRow(cpuMs, detail);
    }

    /// <summary>
    /// Begins a new timing measurement for the specified name.
    /// </summary>
    /// <param name="name">Name of the region to time.</param>
    /// <param name="cpuOnly">Whether the region submits no GPU work, so no GPU queries are issued for it.</param>
    /// <param name="collapsed">Whether the overlay folds the regions nested in this one into it, unless it is expensive.</param>
    /// <returns>Query ID to use when ending the query, or 0 if timing is disabled.</returns>
    internal QueryId BeginQuery(string name, bool cpuOnly = false, bool collapsed = false)
    {
        if (!Capture)
        {
            return 0;
        }

        var parent = openRegions.Count > 0 ? openRegions[^1] : null;

        // A name repeated under the same parent within a frame is a separate region, told apart by its occurrence
        Region? region;
        var occurrence = 0;

        while (regions.TryGetValue((parent, name, occurrence), out region) && region.LastFrame == frame)
        {
            occurrence++;
        }

        if (region == null)
        {
            region = new Region(name);
            regions[(parent, name, occurrence)] = region;
        }

        region.LastFrame = frame;
        region.Depth = openRegions.Count;
        region.CpuOnly = cpuOnly;
        region.Collapsed = collapsed;

        openRegions.Add(region);
        frameRegions.Add(region);

        // No GPU queries for a CPU-only region: it issues no draw work, so a GPU time would only measure the gap around it
        if (!cpuOnly)
        {
            if (region.StartQuery == 0)
            {
                region.StartQuery = GraphicsDevice.CreateQuery(QueryTarget.Timestamp, "GpuTimingStart");
                region.EndQuery = GraphicsDevice.CreateQuery(QueryTarget.Timestamp, "GpuTimingEnd");
                region.SubmitGpuQueries = true;

                Debug.Assert(region.StartQuery != 0 && region.EndQuery != 0, "Failed to generate GPU query objects.");
            }

            if (region.SubmitGpuQueries)
            {
                GL.QueryCounter(region.StartQuery, QueryCounterTarget.Timestamp);
            }
        }

        region.StartTimestamp = Stopwatch.GetTimestamp();

        return frameRegions.Count;
    }

    /// <summary>
    /// Ends a timing measurement.
    /// </summary>
    /// <param name="id">Query ID returned from <see cref="BeginQuery"/>.</param>
    internal void EndQuery(QueryId id)
    {
        if (!Capture || id <= 0 || id > frameRegions.Count)
        {
            return;
        }

        var endTimestamp = Stopwatch.GetTimestamp();
        var region = frameRegions[id - 1];
        var elapsedMs = Stopwatch.GetElapsedTime(region.StartTimestamp, endTimestamp).TotalMilliseconds;

        openRegions.Remove(region);

        if (region.CpuOnly)
        {
            results[id] = new TimingResult(region, elapsedMs, 0.0);
            return;
        }

        // The queries are only read back once their results land, so until then the last GPU time is carried forward
        var gpuFresh = false;

        if (region.SubmitGpuQueries)
        {
            GL.QueryCounter(region.EndQuery, QueryCounterTarget.Timestamp);
            region.SubmitGpuQueries = false;
        }
        else
        {
            GL.GetQueryObject(region.StartQuery, GetQueryObjectParam.QueryResultNoWait, out long startTimestampGpu);
            GL.GetQueryObject(region.EndQuery, GetQueryObjectParam.QueryResultNoWait, out long endTimestampGpu);

            if (startTimestampGpu != 0 && endTimestampGpu != 0)
            {
                region.GpuMs = (endTimestampGpu - startTimestampGpu) / 1_000_000.0; // convert nanoseconds to milliseconds
                region.SubmitGpuQueries = true;
                gpuFresh = true;
            }
        }

        results[id] = new TimingResult(region, elapsedMs, region.GpuMs, gpuFresh);
    }

    /// <summary>
    /// Renders timing results to screen using the provided text renderer. When the rows do not fit
    /// the screen, the cheapest regions are folded into a single row until they do.
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

        displayText.Clear();

        var lineHeight = scale * 1.2f / camera.WindowSize.Y;
        var yOffset = y;

        void AddLine(TextRenderer.TextMemory text, Color32 color)
        {
            textRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
            {
                X = x,
                Y = yOffset,
                Scale = scale,
                Color = color,
                Text = text,
            }, camera);

            yOffset += lineHeight;
        }

        AddLine("Render Timings", new Color32(255, 200, 0));
        AddLine(ColumnHeader, Color32.White);

        var totalCpu = 0.0;
        var totalGpu = 0.0;
        var total = 0.0;

        if (Stopwatch.GetElapsedTime(lastRollingUpdate).TotalSeconds > 1.0)
        {
            // Shift current max to previous and reset current max for new rolling window
            foreach (var region in regions.Values)
            {
                region.PreviousMax = region.CurrentMax;
                region.CurrentMax = null;
            }

            swapMaxPrevious = swapMaxCurrent;
            swapMaxCurrent = 0;
            unaccountedMaxPrevious = unaccountedMaxCurrent;
            unaccountedMaxCurrent = 0;

            lastRollingUpdate = Stopwatch.GetTimestamp();
        }

        displayRows.Clear();

        foreach (var result in results.Values)
        {
            var maxTimeCurrent = Math.Max(result.TimeMs, result.TimeMsGpu);
            var region = result.Region;
            region.CurrentMax = Math.Max(region.CurrentMax ?? 0, maxTimeCurrent);
            var maxTime = region.PreviousMax ?? maxTimeCurrent;

            if (result.Depth == 0)
            {
                totalCpu += result.TimeMs;
                totalGpu += result.TimeMsGpu;
                total += maxTime;
            }

            displayRows.Add(new DisplayRow(result, maxTime, result.Collapsed && maxTime < ExpandCollapsedMs));
        }

        // Title, column names, swapchain, total, unaccounted, and the async header with its rows
        var fixedLines = 5 + (asyncRows.Count > 0 ? 1 + asyncRows.Count : 0);
        CollapseToFit((int)((1f - 2f * y) / lineHeight) - fixedLines);

        var hideBelowDepth = int.MaxValue;

        for (var i = 0; i < displayRows.Count; i++)
        {
            var (result, maxTime, collapsed) = displayRows[i];

            if (result.Depth > hideBelowDepth)
            {
                continue;
            }

            hideBelowDepth = collapsed ? result.Depth : int.MaxValue;

            var name = FormatNameColumn(result.Name, result.Depth);

            // A CPU-only region submits no GPU work, so a number in that column would be meaningless
            var text = result.CpuOnly
                ? displayText.Format($"  {name} {"-",6} {result.TimeMs,6:0.00} {maxTime,6:0.00}")
                : displayText.Format($"  {name} {result.TimeMsGpu,6:0.00} {result.TimeMs,6:0.00} {maxTime,6:0.00}");

            AddLine(text, ColorForPeak(maxTime));
        }

        AddLine(displayText.Format($"  {"Swapchain",-NameColumnWidth} {"-",6} {swapMs,6:0.00} {swapMaxPrevious,6:0.00}"), ColorForPeak(swapMaxPrevious));

        totalCpu += swapMs;
        total += swapMaxPrevious;

        AddLine(displayText.Format($"  {"Total",-NameColumnWidth} {totalGpu,6:0.00} {totalCpu,6:0.00} {total,6:0.00}"), Color32.White);
        AddLine(displayText.Format($"  {"Unaccounted",-NameColumnWidth} {"-",6} {unaccountedMs,6:0.00} {unaccountedMaxPrevious,6:0.00}"), Color32.White);

        if (asyncRows.Count == 0)
        {
            return;
        }

        // Off-frame work runs on other threads, so counting it in the totals would overstate the frame cost.
        AddLine("  Async", Color32.White);

        foreach (var (rowName, row) in asyncRows)
        {
            var name = FormatNameColumn(rowName, 1);
            var text = row.CpuMs is { } cpuMs
                ? displayText.Format($"  {name} {"-",6} {cpuMs,6:0.00} {row.Detail,6}")
                : displayText.Format($"  {name} {"-",6} {"",6} {row.Detail,6}");

            AddLine(text, new Color32(150, 255, 150));
        }
    }

    /// <summary>Folds the cheapest shown regions that have nested ones until at most <paramref name="maxRows"/> rows show.</summary>
    private void CollapseToFit(int maxRows)
    {
        while (CountVisibleRows() > maxRows)
        {
            var cheapest = -1;
            var hideBelowDepth = int.MaxValue;

            for (var i = 0; i < displayRows.Count; i++)
            {
                var row = displayRows[i];

                if (row.Result.Depth > hideBelowDepth)
                {
                    continue;
                }

                hideBelowDepth = row.Collapsed ? row.Result.Depth : int.MaxValue;

                if (!row.Collapsed && CountNested(i) > 0 && (cheapest < 0 || row.PeakMs < displayRows[cheapest].PeakMs))
                {
                    cheapest = i;
                }
            }

            if (cheapest < 0)
            {
                return;
            }

            displayRows[cheapest] = displayRows[cheapest] with { Collapsed = true };
        }
    }

    private int CountVisibleRows()
    {
        var count = 0;
        var hideBelowDepth = int.MaxValue;

        foreach (var row in displayRows)
        {
            if (row.Result.Depth > hideBelowDepth)
            {
                continue;
            }

            hideBelowDepth = row.Collapsed ? row.Result.Depth : int.MaxValue;
            count++;
        }

        return count;
    }

    /// <summary>Counts the rows nested at any depth under the row at <paramref name="index"/>.</summary>
    private int CountNested(int index)
    {
        var depth = displayRows[index].Result.Depth;
        var end = index + 1;

        while (end < displayRows.Count && displayRows[end].Result.Depth > depth)
        {
            end++;
        }

        return end - index - 1;
    }

    /// <summary>Indents a region name by its depth and pads it to the name column, cutting what does not fit.</summary>
    private ReadOnlySpan<char> FormatNameColumn(string name, int depth)
    {
        var column = nameColumn.AsSpan();
        column.Fill(' ');

        var indent = Math.Min(depth * 2, column.Length);
        name.AsSpan(0, Math.Min(name.Length, column.Length - indent)).CopyTo(column[indent..]);

        return column;
    }

    /// <summary>Starts a new frame of regions. Marked by <see cref="PerfStats"/>, which owns the thread policy.</summary>
    internal void MarkFrameBegin()
    {
        // Unconditional, as capture turned off mid-frame leaves regions open that would parent the next ones
        frame++;
        openRegions.Clear();
        frameRegions.Clear();
    }

    /// <summary>
    /// Clears all collected timing results for the frame.
    /// </summary>
    internal void MarkFrameEnd()
    {
        if (Capture)
        {
            frameCpuMs = 0.0;

            foreach (var result in results.Values)
            {
                if (result.Depth == 0)
                {
                    frameCpuMs += result.TimeMs;
                }
            }

            if (averaging != null)
            {
                AddToAverages();
            }

            results.Clear();
        }
    }

    private void AddToAverages()
    {
        Debug.Assert(averaging != null);

        AveragedFrames++;

        foreach (var result in results.Values)
        {
            var region = result.Region;

            if (!region.Averaged)
            {
                region.Averaged = true;
                region.AveragedCpuMs = 0.0;
                region.AveragedCpuSamples = 0;
                region.AveragedGpuMs = 0.0;
                region.AveragedGpuSamples = 0;
                averaging.Add(region);
            }

            region.AveragedCpuMs += result.TimeMs;
            region.AveragedCpuSamples++;

            // Between readings the last GPU time is carried forward, which would count it more than once
            if (result.GpuFresh)
            {
                region.AveragedGpuMs += result.TimeMsGpu;
                region.AveragedGpuSamples++;
            }
        }
    }

    /// <summary>
    /// Starts summing every timed region of each frame, for <see cref="StopAveraging"/> to average.
    /// Only collected while <see cref="Capture"/> is on. Call between frames.
    /// </summary>
    internal void StartAveraging()
    {
        StopAveraging();

        averaging = [];
        AveragedFrames = 0;
    }

    /// <summary>Stops summing, and returns each region's averages in the order they were first timed. Call between frames.</summary>
    /// <returns>The averages, empty when nothing was timed.</returns>
    internal List<TimingAverage> StopAveraging()
    {
        if (averaging == null)
        {
            return [];
        }

        var averages = new List<TimingAverage>(averaging.Count);

        foreach (var region in averaging)
        {
            var cpuMs = region.AveragedCpuMs / region.AveragedCpuSamples;
            double? gpuMs = region.CpuOnly || region.AveragedGpuSamples == 0 ? null : region.AveragedGpuMs / region.AveragedGpuSamples;

            averages.Add(new TimingAverage(region.Name, region.Depth, cpuMs, region.AveragedCpuSamples, gpuMs, region.AveragedGpuSamples));
            region.Averaged = false;
        }

        averaging = null;

        return averages;
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    public void Dispose()
    {
        regions.Clear();
        openRegions.Clear();
        frameRegions.Clear();
        results.Clear();
    }
}
