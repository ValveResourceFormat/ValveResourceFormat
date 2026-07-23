using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Collects per frame managed allocation and garbage collector statistics.
/// </summary>
public class AllocStats
{
    private sealed class TypeTotal
    {
        public long Bytes;
        public long Samples;
    }

    /// <summary>
    /// Listens in-process for the runtime's GCAllocationTick event, which fires once per ~100 KB
    /// allocated and names the type that crossed the threshold. Aggregated per type this gives a
    /// statistically proportional breakdown of what is being allocated, at negligible overhead.
    /// </summary>
    private sealed class AllocationSampler : EventListener
    {
        private const int GCAllocationTickEventId = 10;
        private const EventKeywords GCKeyword = (EventKeywords)0x1;

        private EventSource? runtimeSource;
        private bool enableRequested;

        public Lock Sync { get; } = new();
        public Dictionary<string, TypeTotal> Types { get; } = [];
        public long SampledBytes { get; private set; }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
            {
                runtimeSource = eventSource;

                if (enableRequested)
                {
                    EnableEvents(eventSource, EventLevel.Verbose, GCKeyword);
                }
            }
        }

        public void Start()
        {
            using (Sync.EnterScope())
            {
                Types.Clear();
                SampledBytes = 0;
            }

            enableRequested = true;

            if (runtimeSource != null)
            {
                EnableEvents(runtimeSource, EventLevel.Verbose, GCKeyword);
            }
        }

        public void Stop()
        {
            enableRequested = false;

            if (runtimeSource != null)
            {
                DisableEvents(runtimeSource);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Fires on whichever thread crossed the allocation threshold.
            if (eventData.EventId != GCAllocationTickEventId || eventData.Payload == null || eventData.PayloadNames == null)
            {
                return;
            }

            var typeName = "<unknown>";
            var amount = 0L;

            for (var i = 0; i < eventData.PayloadNames.Count; i++)
            {
                switch (eventData.PayloadNames[i])
                {
                    case "TypeName":
                        typeName = eventData.Payload[i] as string ?? typeName;
                        break;
                    case "AllocationAmount64":
                        amount = (long)(ulong)eventData.Payload[i]!;
                        break;
                    default:
                        break;
                }
            }

            if (amount <= 0)
            {
                return;
            }

            using (Sync.EnterScope())
            {
                if (!Types.TryGetValue(typeName, out var total))
                {
                    total = new TypeTotal();
                    Types[typeName] = total;
                }

                total.Bytes += amount;
                total.Samples++;
                SampledBytes += amount;
            }
        }
    }

    private const int MaxTypesShown = 15;
    private const int MaxSelfTypesShown = 8;

    private AllocationSampler? sampler;
    private bool wasCapturing;
    private long captureStart;

    /// <summary>Gets or sets whether allocation data is actively collected this frame.</summary>
    public bool Capture { get; set; }

    // Snapshots taken at frame begin, deltas computed at frame end.
    private long frameStartThreadBytes;
    private long frameStartTotalBytes;
    private bool frameActive;

    // Last completed frame, displayed mid-frame before the current one ends.
    private long lastFrameThreadBytes;
    private long lastFrameTotalBytes;

    // Rolling one second window, published on rollover so the numbers are readable.
    private long windowStart;
    private long windowTotalBytes;
    private int windowFrames;
    private long windowMaxFrameBytes;
    private readonly int[] windowStartCollections = new int[GC.MaxGeneration + 1];

    private long publishedBytesPerSecond;
    private long publishedMaxFrameBytes;
    private readonly int[] publishedCollections = new int[GC.MaxGeneration + 1];

    /// <summary>Snapshots allocation counters for the new frame.</summary>
    internal void MarkFrameBegin()
    {
        if (Capture != wasCapturing)
        {
            wasCapturing = Capture;

            if (Capture)
            {
                sampler ??= new AllocationSampler();
                sampler.Start();
                captureStart = Stopwatch.GetTimestamp();
                displayLines.Clear();
            }
            else
            {
                sampler?.Stop();
            }
        }

        if (!Capture)
        {
            frameActive = false;
            return;
        }

        if (windowStart == 0)
        {
            StartWindow();
        }

        frameStartThreadBytes = GC.GetAllocatedBytesForCurrentThread();
        frameStartTotalBytes = GC.GetTotalAllocatedBytes(precise: false);
        frameActive = true;
    }

    /// <summary>Computes the frame's allocation deltas and advances the rolling window.</summary>
    internal void MarkFrameEnd()
    {
        if (!frameActive)
        {
            return;
        }

        frameActive = false;

        lastFrameThreadBytes = GC.GetAllocatedBytesForCurrentThread() - frameStartThreadBytes;
        lastFrameTotalBytes = GC.GetTotalAllocatedBytes(precise: false) - frameStartTotalBytes;

        windowTotalBytes += lastFrameTotalBytes;
        windowFrames++;
        windowMaxFrameBytes = Math.Max(windowMaxFrameBytes, lastFrameTotalBytes);

        var elapsed = Stopwatch.GetElapsedTime(windowStart).TotalSeconds;
        if (elapsed >= 1.0)
        {
            publishedBytesPerSecond = (long)(windowTotalBytes / elapsed);
            publishedMaxFrameBytes = windowMaxFrameBytes;

            for (var gen = 0; gen <= GC.MaxGeneration; gen++)
            {
                publishedCollections[gen] = GC.CollectionCount(gen) - windowStartCollections[gen];
            }

            StartWindow();
        }
    }

    private void StartWindow()
    {
        windowStart = Stopwatch.GetTimestamp();
        windowTotalBytes = 0;
        windowFrames = 0;
        windowMaxFrameBytes = 0;

        for (var gen = 0; gen <= GC.MaxGeneration; gen++)
        {
            windowStartCollections[gen] = GC.CollectionCount(gen);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 0 => "0 B", // a GC can shrink GetTotalMemory between snapshots
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):0.00} GB",
    };

    private static Color32 ColorForFrameBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => new Color32(255, 0, 0),  // allocating megabytes per frame will trigger constant GCs
        >= 128 * 1024 => new Color32(255, 150, 0),
        >= 16 * 1024 => new Color32(255, 255, 0),
        _ => new Color32(150, 255, 150),
    };

    // Display lines are rebuilt once per second and rendered from this cache, so the overlay
    // itself allocates (strings, GCMemoryInfoData) at 1 Hz instead of every frame.
    private readonly List<(string Text, Color32 Color)> displayLines = [];
    private readonly List<(string Name, long Bytes, long Samples)> typeSnapshot = [];
    private long lastDisplayRebuild;

    /// <summary>
    /// Renders allocation and GC statistics to screen using the provided text renderer.
    /// </summary>
    /// <param name="textRenderer">Text renderer to use for display.</param>
    /// <param name="camera">Camera for positioning text.</param>
    /// <param name="x">X position (0-1 as fraction of screen width).</param>
    /// <param name="y">Y position (0-1 as fraction of screen height).</param>
    /// <param name="scale">Text scale.</param>
    public void DisplayAllocations(TextRenderer textRenderer, Camera camera, float x = 0.02f, float y = 0.05f, float scale = 11f)
    {
        if (!Capture)
        {
            return;
        }

        if (displayLines.Count == 0 || Stopwatch.GetElapsedTime(lastDisplayRebuild).TotalSeconds >= 1.0)
        {
            lastDisplayRebuild = Stopwatch.GetTimestamp();
            RebuildDisplayLines();
        }

        var lineHeight = scale * 1.5f / camera.WindowSize.Y;
        var offset = y;

        foreach (var (text, color) in displayLines)
        {
            if (text.Length > 0)
            {
                textRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
                {
                    X = x,
                    Y = offset,
                    Scale = scale,
                    Color = color,
                    Text = text,
                }, camera);
            }

            offset += lineHeight;
        }
    }

    private void RebuildDisplayLines()
    {
        displayLines.Clear();

        var valueColor = new Color32(150, 255, 150);

        void AddLine(string text, Color32 color) => displayLines.Add((text, color));

        AddLine("GC Allocations", new Color32(255, 200, 0));

        AddLine($"Frame:       {FormatBytes(lastFrameTotalBytes),10} process, {FormatBytes(lastFrameThreadBytes),10} render thread", ColorForFrameBytes(lastFrameTotalBytes));
        AddLine($"Peak frame:  {FormatBytes(publishedMaxFrameBytes),10} over the last second", ColorForFrameBytes(publishedMaxFrameBytes));
        var rateColor = publishedBytesPerSecond switch
        {
            >= 64L * 1024 * 1024 => new Color32(255, 0, 0),
            >= 8L * 1024 * 1024 => new Color32(255, 150, 0),
            >= 1024 * 1024 => new Color32(255, 255, 0),
            _ => valueColor,
        };
        AddLine($"Rate:        {FormatBytes(publishedBytesPerSecond),10}/s", rateColor);

        var collectionsText = $"Collections: gen0 {GC.CollectionCount(0):N0}, gen1 {GC.CollectionCount(1):N0}, gen2 {GC.CollectionCount(2):N0}";
        var windowCollections = 0;

        foreach (var count in publishedCollections)
        {
            windowCollections += count;
        }

        if (windowCollections > 0)
        {
            collectionsText += $"  (+{publishedCollections[0]}/{publishedCollections[1]}/{publishedCollections[2]} last second)";
        }

        AddLine(collectionsText, windowCollections > 0 ? new Color32(255, 255, 0) : valueColor);

        var info = GC.GetGCMemoryInfo();

        AddLine($"Heap:        {FormatBytes(GC.GetTotalMemory(forceFullCollection: false)),10} used, {FormatBytes(info.HeapSizeBytes),10} heap, {FormatBytes(info.FragmentedBytes),10} fragmented, {FormatBytes(info.TotalCommittedBytes),10} committed", valueColor);
        AddLine($"GC pauses:   {info.PauseTimePercentage:0.00}% of time paused since start", info.PauseTimePercentage > 1.0 ? new Color32(255, 150, 0) : valueColor);

        if (info.Index > 0)
        {
            var pauses = info.PauseDurations;
            var pauseMs = 0.0;

            for (var i = 0; i < pauses.Length; i++)
            {
                pauseMs += pauses[i].TotalMilliseconds;
            }

            AddLine($"Last GC:     #{info.Index:N0} gen{info.Generation}, paused {pauseMs:0.00} ms{(info.Compacted ? ", compacting" : "")}{(info.Concurrent ? ", background" : "")}", pauseMs > 8.0 ? new Color32(255, 150, 0) : valueColor);
        }

        AddLine($"Mode:        {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")} GC, latency {System.Runtime.GCSettings.LatencyMode}", valueColor);

        if (sampler == null)
        {
            return;
        }

        long sampledBytes;
        typeSnapshot.Clear();

        using (sampler.Sync.EnterScope())
        {
            sampledBytes = sampler.SampledBytes;

            foreach (var (name, total) in sampler.Types)
            {
                typeSnapshot.Add((name, total.Bytes, total.Samples));
            }
        }

        var selfBytes = 0L;
        var stringBytes = 0L;

        foreach (var (name, bytes, _) in typeSnapshot)
        {
            if (IsSelfType(name))
            {
                selfBytes += bytes;
            }
            else if (name == "System.String")
            {
                stringBytes += bytes;
            }
        }

        typeSnapshot.Sort(static (a, b) => b.Bytes.CompareTo(a.Bytes));

        AddLine(string.Empty, default);

        var captureSeconds = Stopwatch.GetElapsedTime(captureStart).TotalSeconds;
        AddLine($"Top allocated types over {captureSeconds:0} s ({FormatBytes(sampledBytes)} sampled at ~100 KB granularity)", new Color32(255, 200, 0));
        AddLine($"Without stats overhead and strings: {FormatBytes(sampledBytes - selfBytes - stringBytes)} <- try to make this zero", Color32.White);
        AddLine($"{"Bytes",10} {"Share",6} {"Ticks",6}  Type", Color32.White);

        void AddTypeLine(string name, long bytes, long samples, Color32? colorOverride)
        {
            var share = sampledBytes > 0 ? (double)bytes / sampledBytes : 0;
            var displayName = name.Length > 200 ? string.Concat(name.AsSpan(0, 199), "…") : name;
            var color = colorOverride ?? (share >= 0.25 ? new Color32(255, 255, 0) : valueColor);

            AddLine($"{FormatBytes(bytes),10} {share,6:0.0%} {samples,6:N0}  {displayName}", color);
        }

        var shown = 0;

        foreach (var (name, bytes, samples) in typeSnapshot)
        {
            if (IsSelfType(name))
            {
                continue;
            }

            AddTypeLine(name, bytes, samples, null);

            if (++shown == MaxTypesShown)
            {
                break;
            }
        }

        // The display's own allocations, greyed out at the bottom.
        var greyColor = new Color32(140, 140, 140);
        shown = 0;

        foreach (var (name, bytes, samples) in typeSnapshot)
        {
            if (!IsSelfType(name))
            {
                continue;
            }

            AddTypeLine(name, bytes, samples, greyColor);

            if (++shown == MaxSelfTypesShown)
            {
                break;
            }
        }
    }

    /// <summary>Types allocated by this overlay itself: the sampler's event dispatch, the aggregation, and the per-frame GC info queries.</summary>
    private static bool IsSelfType(string name) =>
        name == "System.GCMemoryInfoData"
        || name == "MoreEventInfo"
        || name.Contains(nameof(AllocStats), StringComparison.Ordinal)
        || name.Contains("System.Diagnostics.Tracing", StringComparison.Ordinal);

    /// <summary>
    /// Releases the allocation event listener.
    /// </summary>
    public void Dispose()
    {
        sampler?.Dispose();
        sampler = null;
    }
}
