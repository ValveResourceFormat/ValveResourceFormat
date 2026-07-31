namespace ValveResourceFormat.Renderer;

/// <summary>
/// RAII wrapper timing a region that submits no GPU work.
/// </summary>
/// <remarks>
/// Unlike <see cref="GLDebugGroup"/> this issues no GPU timestamp queries and no debug group marker, so
/// the region shows a CPU time and a blank GPU column. Must still be opened on the thread owning the
/// frame, between <see cref="PerfStats.MarkFrameBegin"/> and <see cref="PerfStats.MarkFrameEnd"/>.
/// </remarks>
public ref struct ProfilerScope
{
    private readonly int timeQueryId;

    /// <summary>
    /// Begins timing a CPU-only region.
    /// </summary>
    /// <param name="name">Name of the region, shown in the timings display.</param>
    public ProfilerScope(string name)
    {
        timeQueryId = PerfStats.Active.BeginTimingQuery(name, cpuOnly: true);
    }

    /// <summary>
    /// Ends the region.
    /// </summary>
    public readonly void Dispose()
    {
        PerfStats.Active.EndTimingQuery(timeQueryId);
    }
}
