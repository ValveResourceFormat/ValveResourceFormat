namespace ValveResourceFormat.Renderer;

/// <summary>
/// RAII wrapper for OpenGL debug group markers.
/// </summary>
/// <remarks>
/// Used to annotate sections of OpenGL commands in debugging tools like RenderDoc.
/// </remarks>
public ref struct GLDebugGroup
{
    internal int TimeQueryId { get; }

    /// <summary>
    /// Initializes a new debug group and pushes it onto the OpenGL debug stack.
    /// </summary>
    /// <param name="name">Name of the debug group to display in profiling tools.</param>
    public GLDebugGroup(string name)
    {
        TimeQueryId = Counters.Active.BeginTimingQuery(name);
#if DEBUG
        OpenTK.Graphics.OpenGL.GL.PushDebugGroup(OpenTK.Graphics.OpenGL.DebugSourceExternal.DebugSourceApplication, 0, name.Length, name);
#endif
    }

#pragma warning disable CA1822 // Mark members as static
    /// <summary>
    /// Pops the debug group from the OpenGL debug stack.
    /// </summary>
    public readonly void Dispose()
#pragma warning restore CA1822
    {
#if DEBUG
        OpenTK.Graphics.OpenGL.GL.PopDebugGroup();
#endif
        Counters.Active.EndTimingQuery(TimeQueryId);
    }
}
