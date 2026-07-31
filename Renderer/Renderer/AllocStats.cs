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

        // System.Buffers.ArrayPoolEventSource event ids. Subscribed at Informational level only:
        // the Verbose per-rent/return events box their payloads in the dispatch, which at one rent
        // per frame becomes the dominant allocation in the very report this display produces.
        private const int BufferAllocatedEventId = 2;
        private const int BufferTrimmedEventId = 4;
        private const int BufferDroppedEventId = 6;

        private EventSource? runtimeSource;
        private EventSource? arrayPoolSource;
        private bool enableRequested;

        public Lock Sync { get; } = new();
        public Dictionary<string, TypeTotal> Types { get; } = [];
        public long SampledBytes { get; private set; }

        /// <summary>Sampled bytes attributed to large-object heap allocations (each one ages straight into gen2).</summary>
        public long LohSampledBytes { get; private set; }

        // ArrayPool activity, cumulative since Start. Written with Interlocked from event threads.
        public long PoolMisses;
        public long PoolMissElements;
        public long PoolTrimmed;
        public long PoolDropped;

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
            else if (eventSource.Name == "System.Buffers.ArrayPoolEventSource")
            {
                arrayPoolSource = eventSource;

                if (enableRequested)
                {
                    EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All);
                }
            }
        }

        public void Start()
        {
            using (Sync.EnterScope())
            {
                Types.Clear();
                SampledBytes = 0;
                LohSampledBytes = 0;
            }

            Interlocked.Exchange(ref PoolMisses, 0);
            Interlocked.Exchange(ref PoolMissElements, 0);
            Interlocked.Exchange(ref PoolTrimmed, 0);
            Interlocked.Exchange(ref PoolDropped, 0);

            enableRequested = true;

            if (runtimeSource != null)
            {
                EnableEvents(runtimeSource, EventLevel.Verbose, GCKeyword);
            }

            if (arrayPoolSource != null)
            {
                EnableEvents(arrayPoolSource, EventLevel.Informational, EventKeywords.All);
            }
        }

        public void Stop()
        {
            enableRequested = false;

            if (runtimeSource != null)
            {
                DisableEvents(runtimeSource);
            }

            if (arrayPoolSource != null)
            {
                DisableEvents(arrayPoolSource);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource == arrayPoolSource)
            {
                OnArrayPoolEvent(eventData);
                return;
            }

            // Fires on whichever thread crossed the allocation threshold.
            if (eventData.EventId != GCAllocationTickEventId || eventData.Payload == null || eventData.PayloadNames == null)
            {
                return;
            }

            var typeName = "<unknown>";
            var amount = 0L;
            var isLargeObject = false;

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
                    case "AllocationKind":
                        isLargeObject = eventData.Payload[i] is uint kind && kind == 1;
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

                if (isLargeObject)
                {
                    LohSampledBytes += amount;
                }
            }
        }

        private void OnArrayPoolEvent(EventWrittenEventArgs eventData)
        {
            switch (eventData.EventId)
            {
                case BufferAllocatedEventId:
                    // The pool had no pooled buffer to hand out, so a real allocation happened.
                    Interlocked.Increment(ref PoolMisses);
                    Interlocked.Add(ref PoolMissElements, ReadBufferSize(eventData));
                    break;

                case BufferTrimmedEventId:
                    Interlocked.Increment(ref PoolTrimmed);
                    break;

                case BufferDroppedEventId:
                    Interlocked.Increment(ref PoolDropped);
                    break;

                default:
                    break;
            }
        }

        /// <summary>Reads the bufferSize payload field: the array length in elements, not bytes.</summary>
        private static int ReadBufferSize(EventWrittenEventArgs eventData)
        {
            if (eventData.Payload == null || eventData.PayloadNames == null)
            {
                return 0;
            }

            for (var i = 0; i < eventData.PayloadNames.Count; i++)
            {
                if (eventData.PayloadNames[i] == "bufferSize")
                {
                    return eventData.Payload[i] is int size ? size : 0;
                }
            }

            return 0;
        }
    }

    private const int MaxTypesShown = 15;
    private const int MaxSelfTypesShown = 8;

    private AllocationSampler? sampler;
    private bool wasCapturing;
    private long captureStart;

    // Exceptions allocate (the exception object plus its stack trace); a nonzero rate during
    // normal rendering is a hidden allocator and usually a bug. Counted since capture start.
    private long firstChanceExceptions;
    private EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs>? exceptionHandler;

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

    // Stamped per frame off the collection counts, which unlike GCMemoryInfo cost no allocation to read.
    private int lastGcCollectionTotal = -1;
    private long lastGcTimestamp;

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

                Interlocked.Exchange(ref firstChanceExceptions, 0);
                exceptionHandler ??= (_, _) => Interlocked.Increment(ref firstChanceExceptions);
                AppDomain.CurrentDomain.FirstChanceException += exceptionHandler;
            }
            else
            {
                sampler?.Stop();

                if (exceptionHandler != null)
                {
                    AppDomain.CurrentDomain.FirstChanceException -= exceptionHandler;
                }
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

        var totalCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        if (totalCollections != lastGcCollectionTotal)
        {
            if (lastGcCollectionTotal != -1)
            {
                lastGcTimestamp = Stopwatch.GetTimestamp();
            }

            lastGcCollectionTotal = totalCollections;
        }

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

    /// <summary>Human-readable byte count that formats into a span, so interpolating it does not allocate.</summary>
    private readonly struct ByteSize(long bytes) : ISpanFormattable
    {
        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => bytes switch
        {
            < 0 => destination.TryWrite($"0 B", out charsWritten), // a GC can shrink GetTotalMemory between snapshots
            < 1024 => destination.TryWrite($"{bytes} B", out charsWritten),
            < 1024 * 1024 => destination.TryWrite($"{bytes / 1024.0:0.0} KB", out charsWritten),
            < 1024 * 1024 * 1024 => destination.TryWrite($"{bytes / (1024.0 * 1024.0):0.0} MB", out charsWritten),
            _ => destination.TryWrite($"{bytes / (1024.0 * 1024.0 * 1024.0):0.00} GB", out charsWritten),
        };

        public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

        public override string ToString() => bytes switch
        {
            < 0 => "0 B",
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):0.00} GB",
        };
    }

    private static Color32 ColorForFrameBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => new Color32(255, 0, 0),  // allocating megabytes per frame will trigger constant GCs
        >= 128 * 1024 => new Color32(255, 150, 0),
        >= 16 * 1024 => new Color32(255, 255, 0),
        _ => new Color32(150, 255, 150),
    };

    // Display lines are rebuilt once per second into the arena and rendered from this cache, so the
    // overlay itself allocates almost nothing: only GCMemoryInfoData and the type-name ellipses at 1 Hz.
    private readonly TextRenderer.TextArena displayText = new(16 * 1024);
    private readonly List<(TextRenderer.TextMemory Text, Color32 Color)> displayLines = [];
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
        displayText.Clear();

        var valueColor = new Color32(150, 255, 150);

        void AddLine(TextRenderer.TextMemory text, Color32 color) => displayLines.Add((text, color));

        AddLine("GC Allocations", new Color32(255, 200, 0));

        AddLine(displayText.Format($"Frame:       {new ByteSize(lastFrameTotalBytes),10} process, {new ByteSize(lastFrameThreadBytes),10} render thread"), ColorForFrameBytes(lastFrameTotalBytes));
        AddLine(displayText.Format($"Peak frame:  {new ByteSize(publishedMaxFrameBytes),10} over the last second"), ColorForFrameBytes(publishedMaxFrameBytes));
        var rateColor = publishedBytesPerSecond switch
        {
            >= 64L * 1024 * 1024 => new Color32(255, 0, 0),
            >= 8L * 1024 * 1024 => new Color32(255, 150, 0),
            >= 1024 * 1024 => new Color32(255, 255, 0),
            _ => valueColor,
        };
        AddLine(displayText.Format($"Rate:        {new ByteSize(publishedBytesPerSecond),10}/s"), rateColor);

        var info = GC.GetGCMemoryInfo();
        var exceptions = Interlocked.Read(ref firstChanceExceptions);
        var unmanagedOrGl = Environment.WorkingSet - info.HeapSizeBytes;

        AddLine(
            displayText.Format($"Process:  {new ByteSize(Environment.WorkingSet),10} total, {new ByteSize(unmanagedOrGl),9} unmanaged, {exceptions:N0} thrown exceptions"),
            exceptions > 0 ? new Color32(255, 255, 0) : valueColor);

        AddLine(displayText.Format($"Heap:     {new ByteSize(GC.GetTotalMemory(forceFullCollection: false)),10} used, {new ByteSize(info.HeapSizeBytes),10} heap, {new ByteSize(info.FragmentedBytes),10} fragmented, {new ByteSize(info.TotalCommittedBytes),10} committed"), valueColor);


        // Climbing finalization counts are GL handle wrappers (or other disposables) nobody disposed.
        AddLine(
            displayText.Format($"Objects:     {info.PinnedObjectsCount:N0} pinned, {info.FinalizationPendingCount:N0} awaiting finalization"),
            info.FinalizationPendingCount > 100 ? new Color32(255, 255, 0) : valueColor);


        var windowCollections = 0;

        foreach (var count in publishedCollections)
        {
            windowCollections += count;
        }

        AddLine(
            displayText.Format($"Collections: gen0 {GC.CollectionCount(0):N0}, gen1 {GC.CollectionCount(1):N0}, gen2 {GC.CollectionCount(2):N0}  (+{publishedCollections[0]}/{publishedCollections[1]}/{publishedCollections[2]} last second)"),
            windowCollections > 0 ? new Color32(255, 255, 0) : valueColor);

        // Sizes are as of the last GC, so they hold still between collections.
        var generations = info.GenerationInfo;
        if (generations.Length >= 5)
        {
            AddLine(displayText.Format($"Generations: gen0 {new ByteSize(generations[0].SizeAfterBytes),10}, gen1 {new ByteSize(generations[1].SizeAfterBytes),10}, gen2 {new ByteSize(generations[2].SizeAfterBytes),10}, LOH {new ByteSize(generations[3].SizeAfterBytes),10}, POH {new ByteSize(generations[4].SizeAfterBytes),10}"), valueColor);
        }

        AddLine(displayText.Format($"GC pauses:   {info.PauseTimePercentage:0.00}% of time paused since start"), info.PauseTimePercentage > 1.0 ? new Color32(255, 150, 0) : valueColor);

        if (info.Index > 0)
        {
            var pauses = info.PauseDurations;
            var pauseMs = 0.0;

            for (var i = 0; i < pauses.Length; i++)
            {
                pauseMs += pauses[i].TotalMilliseconds;
            }

            var lastGcColor = pauseMs > 8.0 ? new Color32(255, 150, 0) : valueColor;
            var lastGcLine = lastGcTimestamp != 0
                ? displayText.Format($"Last GC:     #{info.Index:N0} gen{info.Generation}, paused {pauseMs:0.00} ms{(info.Compacted ? ", compacting" : "")}{(info.Concurrent ? ", background" : "")}, {Stopwatch.GetElapsedTime(lastGcTimestamp).TotalSeconds:0.0} s ago")
                : displayText.Format($"Last GC:     #{info.Index:N0} gen{info.Generation}, paused {pauseMs:0.00} ms{(info.Compacted ? ", compacting" : "")}{(info.Concurrent ? ", background" : "")}");
            AddLine(lastGcLine, lastGcColor);
        }

        AddLine(displayText.Format($"Mode:        {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")} GC, latency {System.Runtime.GCSettings.LatencyMode}"), valueColor);

        if (sampler == null)
        {
            return;
        }

        var poolMisses = Volatile.Read(ref sampler.PoolMisses);
        var poolMissElements = Volatile.Read(ref sampler.PoolMissElements);
        var poolTrimmed = Volatile.Read(ref sampler.PoolTrimmed);
        var poolDropped = Volatile.Read(ref sampler.PoolDropped);

        AddLine(
            displayText.Format($"ArrayPool:   {poolMisses:N0} misses ({poolMissElements:N0} elements allocated), {poolTrimmed:N0} trimmed, {poolDropped:N0} dropped"),
            poolDropped > 0 ? new Color32(255, 150, 0) : valueColor);

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

        AddLine(default, default);

        var captureSeconds = Stopwatch.GetElapsedTime(captureStart).TotalSeconds;
        AddLine(displayText.Format($"Top allocated types over {captureSeconds:0} s ({new ByteSize(sampledBytes)} sampled at ~100 KB granularity)"), new Color32(255, 200, 0));
        AddLine(displayText.Format($"Without stats overhead and strings: {new ByteSize(sampledBytes - selfBytes - stringBytes)} <- try to make this zero"), Color32.White);
        AddLine(displayText.Format($"{"Bytes",10} {"Share",6} {"Ticks",6}  Type"), Color32.White);

        void AddTypeLine(string name, long bytes, long samples, Color32? colorOverride)
        {
            var share = sampledBytes > 0 ? (double)bytes / sampledBytes : 0;
            var displayName = name.AsSpan(0, Math.Min(name.Length, 199));
            var ellipsis = name.Length > 199 ? "…" : "";
            var color = colorOverride ?? (share >= 0.25 ? new Color32(255, 255, 0) : valueColor);

            AddLine(displayText.Format($"{new ByteSize(bytes),10} {share,6:0.0%} {samples,6:N0}  {displayName}{ellipsis}"), color);
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

        if (exceptionHandler != null)
        {
            AppDomain.CurrentDomain.FirstChanceException -= exceptionHandler;
            exceptionHandler = null;
        }
    }
}
